using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Diagnostics;
using System.Net.Http;

// ─────────────────────────────────────────────────────────────────────────────
// CosmoS3 Multi-Database Benchmark
//
// Starts each per-database host, warms up, measures, then tears it down.
// Prints a side-by-side comparison table at the end.
//
// Ports:
//   8101 – SQLite
//   8102 – PostgreSQL
//   8103 – MySQL
//   8104 – SQL Server  (skipped unless --sqlserver flag is passed)
//
// Usage:
//   dotnet run                     (SQLite + Postgres + MySQL + SQL Server)
//   dotnet run -- --no-sqlserver   (skip SQL Server)
//   dotnet run -- --rounds 200     (override measured rounds, default 100)
// ─────────────────────────────────────────────────────────────────────────────

namespace CosmoS3.DbBenchmark;

class Program
{
    const string AccessKey  = "default";
    const string SecretKey  = "default";
    const string BucketName = "db-bench-bucket";

    static int WarmupRounds  = 10;
    static int MeasureRounds = 100;

    static readonly string RepoRoot = FindRepoRoot();

    record DbTarget(string Label, string DbType, int Port, string HostProject);

    static readonly DbTarget[] AllTargets =
    [
        new("SQLite",     "sqlite",   8101, "CosmoS3Host.SQLite"),
        new("PostgreSQL", "postgres", 8102, "CosmoS3Host.Postgres"),
        new("MySQL",      "mysql",    8103, "CosmoS3Host.MySQL"),
        new("SQL Server", "mssql",    8104, "CosmoS3Host.SqlServer"),
    ];

    // Results keyed by (dbLabel, scenarioName)
    static readonly Dictionary<string, List<(string Scenario, List<double> Latencies, int Errors)>> AllResults = new();

    static async Task Main(string[] args)
    {
        bool includeSqlServer = !args.Contains("--no-sqlserver");
        int  roundsIdx        = Array.IndexOf(args, "--rounds");
        if (roundsIdx >= 0 && roundsIdx + 1 < args.Length && int.TryParse(args[roundsIdx + 1], out int r))
            MeasureRounds = r;

        var targets = AllTargets
            .Where(t => t.DbType != "mssql" || includeSqlServer)
            .ToArray();

        Banner(targets);

        // Build the host projects first (single restore+build pass)
        Console.WriteLine("Building host projects...");
        foreach (var t in targets)
            await BuildHost(t);
        Console.WriteLine("Build complete.\n");

        // Run each target sequentially (one server at a time)
        foreach (var target in targets)
        {
            Console.WriteLine(new string('─', 60));
            Console.WriteLine($"  Testing: {target.Label} (port {target.Port})");
            Console.WriteLine(new string('─', 60));

            var host = await StartHost(target);
            if (host == null)
            {
                Console.WriteLine($"  [SKIP] Could not start {target.Label} host.\n");
                continue;
            }

            try
            {
                var client  = MakeClient(target.Port);
                var results = new List<(string Scenario, List<double> Latencies, int Errors)>();
                AllResults[target.Label] = results;

                await EnsureBucket(client, target.Label);

                Console.Write($"  Warmup ({WarmupRounds} rounds)... ");
                await RunAll(client, WarmupRounds, measure: false, results: null);
                Console.WriteLine("done.");

                Console.Write($"  Measuring ({MeasureRounds} rounds)... ");
                await RunAll(client, MeasureRounds, measure: true, results);
                Console.WriteLine("done.");

                await Cleanup(client, target.Label);
            }
            finally
            {
                try { host.Kill(entireProcessTree: true); } catch { }
                host.Dispose();
            }
        }

        PrintComparison(targets);
    }

    // ── Host lifecycle ────────────────────────────────────────────────────────

    static async Task BuildHost(DbTarget t)
    {
        var proj = Path.Combine(RepoRoot, "samples", t.HostProject, $"{t.HostProject}.csproj");
        Console.Write($"  Building {t.Label}... ");
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"build \"{proj}\" -c Release -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        });
        await p.WaitForExitAsync();
        Console.WriteLine(p.ExitCode == 0 ? "OK" : "FAILED (will try anyway)");
    }

    static async Task<Process> StartHost(DbTarget t)
    {
        var proj = Path.Combine(RepoRoot, "samples", t.HostProject, $"{t.HostProject}.csproj");

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{proj}\" -c Release --no-build")
        {
            RedirectStandardOutput = false,
            RedirectStandardError  = false,
            UseShellExecute        = false,
        };

        Process proc;
        try   { proc = Process.Start(psi); }
        catch { return null; }

        // Wait up to 20 s for the HTTP port to be ready
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(25);
        bool ready = false;
        Console.Write($"  Waiting for {t.Label} to start");
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            Console.Write(".");
            try
            {
                await http.GetAsync($"http://localhost:{t.Port}/");
                ready = true;
                break;
            }
            catch { /* still starting */ }
        }
        Console.WriteLine(ready ? " ready." : " timed out.");

        if (!ready) { proc.Kill(entireProcessTree: true); return null; }
        return proc;
    }

    // ── S3 helpers ────────────────────────────────────────────────────────────

    static AmazonS3Client MakeClient(int port)
    {
        var cfg = new AmazonS3Config
        {
            ServiceURL                 = $"http://localhost:{port}",
            ForcePathStyle             = true,
            UseHttp                    = true,
            Timeout                    = TimeSpan.FromSeconds(30),
            MaxErrorRetry              = 0,
            DisableHostPrefixInjection = true,
        };
        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), cfg);
    }

    static async Task EnsureBucket(AmazonS3Client client, string label)
    {
        try   { await client.PutBucketAsync(BucketName); }
        catch { /* already exists */ }
    }

    static async Task Cleanup(AmazonS3Client client, string label)
    {
        try
        {
            var list = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
            if (list.S3Objects.Count > 0)
                await client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = BucketName,
                    Objects    = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            await client.DeleteBucketAsync(BucketName);
        }
        catch { /* ignore */ }
    }

    // ── Benchmark scenarios ───────────────────────────────────────────────────

    static readonly byte[] Small  = MakeData(1_024,       42);   // 1 KB
    static readonly byte[] Medium = MakeData(102_400,     43);   // 100 KB
    static readonly byte[] Large  = MakeData(1_048_576,   44);   // 1 MB

    static byte[] MakeData(int size, int seed) { var b = new byte[size]; new Random(seed).NextBytes(b); return b; }

    static async Task RunAll(AmazonS3Client c, int rounds, bool measure,
                             List<(string, List<double>, int)> results)
    {
        // Pre-seed objects used by GET/HEAD tests
        await TryPut(c, "bench-get-1kb",   Small);
        await TryPut(c, "bench-get-100kb", Medium);
        await TryPut(c, "bench-get-1mb",   Large);

        for (int i = 0; i < rounds; i++)
        {
            await Measure(c, "ListBuckets",     measure, results,
                () => c.ListBucketsAsync());

            await Measure(c, "ListObjects",     measure, results,
                () => c.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName }));

            await Measure(c, "PutObject_1KB",   measure, results,
                () => TryPut(c, $"put-s-{i}", Small));

            await Measure(c, "PutObject_100KB", measure, results,
                () => TryPut(c, $"put-m-{i}", Medium));

            await Measure(c, "PutObject_1MB",   measure, results,
                () => TryPut(c, $"put-l-{i}", Large));

            await Measure(c, "GetObject_1KB",   measure, results,
                async () => { var r = await c.GetObjectAsync(BucketName, "bench-get-1kb");
                              await r.ResponseStream.CopyToAsync(Stream.Null); });

            await Measure(c, "GetObject_100KB", measure, results,
                async () => { var r = await c.GetObjectAsync(BucketName, "bench-get-100kb");
                              await r.ResponseStream.CopyToAsync(Stream.Null); });

            await Measure(c, "GetObject_1MB",   measure, results,
                async () => { var r = await c.GetObjectAsync(BucketName, "bench-get-1mb");
                              await r.ResponseStream.CopyToAsync(Stream.Null); });

            await Measure(c, "HeadObject",      measure, results,
                () => c.GetObjectMetadataAsync(BucketName, "bench-get-1kb"));

            await Measure(c, "DeleteObject",    measure, results, async () =>
            {
                await TryPut(c, $"del-{i}", Small);
                await c.DeleteObjectAsync(BucketName, $"del-{i}");
            });
        }
    }

    static async Task TryPut(AmazonS3Client c, string key, byte[] data)
    {
        await c.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = BucketName,
            Key         = key,
            InputStream = new MemoryStream(data),
        });
    }

    static async Task Measure(AmazonS3Client c, string name, bool measure,
                              List<(string Scenario, List<double> Latencies, int Errors)> results,
                              Func<Task> action)
    {
        if (!measure) { try { await action(); } catch { } return; }

        var sw = Stopwatch.StartNew();
        bool ok = true;
        try   { await action(); }
        catch { ok = false; }
        sw.Stop();

        var entry = results.Find(x => x.Scenario == name);
        if (entry == default)
        {
            entry = (name, new List<double>(), 0);
            results.Add(entry);
        }
        int idx = results.IndexOf(entry);
        if (ok)
            results[idx] = (entry.Scenario, entry.Latencies.Append(sw.Elapsed.TotalMilliseconds).ToList(), entry.Errors);
        else
            results[idx] = (entry.Scenario, entry.Latencies, entry.Errors + 1);
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    static void Banner(DbTarget[] targets)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║          CosmoS3 Multi-Database Performance Benchmark        ║");
        Console.WriteLine($"║  Databases : {string.Join(", ", targets.Select(t => t.Label)),-48}║");
        Console.WriteLine($"║  Rounds    : {WarmupRounds} warmup + {MeasureRounds} measured{"",-30}║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    static void PrintComparison(DbTarget[] targets)
    {
        // Collect all scenario names in order
        var scenarios = AllResults.Values
            .SelectMany(r => r.Select(x => x.Scenario))
            .Distinct()
            .ToList();

        var activeTargets = targets.Where(t => AllResults.ContainsKey(t.Label)).ToArray();
        if (activeTargets.Length == 0) { Console.WriteLine("No results to compare."); return; }

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  BENCHMARK RESULTS — P50 Latency (ms)                          ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

        // Header row
        string hdr = $"║  {"Scenario",-20}";
        foreach (var t in activeTargets) hdr += $"  {t.Label,-12}";
        hdr = hdr.PadRight(83) + "║";
        Console.WriteLine(hdr);
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

        foreach (var scenario in scenarios)
        {
            string row = $"║  {scenario,-20}";
            double? best = null;
            var p50s = new List<(DbTarget Target, double P50)>();

            foreach (var t in activeTargets)
            {
                if (!AllResults.TryGetValue(t.Label, out var res)) { p50s.Add((t, -1)); continue; }
                var entry = res.Find(x => x.Scenario == scenario);
                if (entry == default || entry.Latencies.Count == 0) { p50s.Add((t, -1)); continue; }
                var sorted = entry.Latencies.OrderBy(x => x).ToList();
                double p50 = Percentile(sorted, 50);
                p50s.Add((t, p50));
                if (!best.HasValue || p50 < best.Value) best = p50;
            }

            foreach (var (t, p50) in p50s)
            {
                string cell = p50 < 0 ? "  N/A" : $"{p50,6:F1}";
                // Mark the winner with a *
                bool winner = best.HasValue && p50 >= 0 && Math.Abs(p50 - best.Value) < 0.01;
                row += $"  {cell + (winner ? "★" : " "),-13}";
            }

            row = row.PadRight(83) + "║";
            Console.WriteLine(row);
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

        // Throughput summary row (ops/sec based on P50)
        string thr = $"║  {"Throughput (ops/s)",-20}";
        foreach (var t in activeTargets)
        {
            if (!AllResults.TryGetValue(t.Label, out var res)) { thr += $"  {"N/A",-13}"; continue; }
            double totalP50 = res
                .Where(x => x.Latencies.Count > 0)
                .Average(x => Percentile(x.Latencies.OrderBy(v => v).ToList(), 50));
            thr += $"  {(1000.0 / totalP50),6:F1}/s  ";
        }
        thr = thr.PadRight(83) + "║";
        Console.WriteLine(thr);
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════════╝");

        // Detailed per-database tables
        Console.WriteLine();
        Console.WriteLine("── Detailed results ──────────────────────────────────────────────────────────────");
        foreach (var t in activeTargets)
        {
            if (!AllResults.TryGetValue(t.Label, out var res)) continue;
            Console.WriteLine();
            Console.WriteLine($"  {t.Label} (port {t.Port}):");
            Console.WriteLine($"  {"Scenario",-20}  {"N",5}  {"Min",7}  {"P50",7}  {"P95",7}  {"P99",7}  {"Max",9}  ops/s");
            Console.WriteLine($"  {new string('─', 78)}");
            foreach (var (scenario, latencies, errors) in res)
            {
                if (latencies.Count == 0) { Console.WriteLine($"  {scenario,-20}  FAIL"); continue; }
                var s = latencies.OrderBy(x => x).ToList();
                double p50 = Percentile(s, 50);
                Console.WriteLine($"  {scenario,-20}  {latencies.Count,5}  {s.First(),7:F1}  {p50,7:F1}  {Percentile(s,95),7:F1}  {Percentile(s,99),7:F1}  {s.Last(),9:F1}  {1000/p50,7:F1}");
            }
        }

        // Winner summary
        Console.WriteLine();
        Console.WriteLine("── Winners ────────────────────────────────────────────────────────────────────────");
        var wins = new Dictionary<string, int>();
        foreach (var t in activeTargets) wins[t.Label] = 0;
        foreach (var scenario in scenarios)
        {
            double best = double.MaxValue;
            string winner = null;
            foreach (var t in activeTargets)
            {
                if (!AllResults.TryGetValue(t.Label, out var res)) continue;
                var entry = res.Find(x => x.Scenario == scenario);
                if (entry == default || entry.Latencies.Count == 0) continue;
                double p50 = Percentile(entry.Latencies.OrderBy(x => x).ToList(), 50);
                if (p50 < best) { best = p50; winner = t.Label; }
            }
            if (winner != null) wins[winner]++;
        }
        foreach (var (label, count) in wins.OrderByDescending(x => x.Value))
            Console.WriteLine($"  {label,-14}: {count,2} scenario(s) won  ({(100.0*count/scenarios.Count),5:F1}%)");

        Console.WriteLine($"\n  ★ = fastest for that scenario  |  N={MeasureRounds} measured rounds  |  all latencies in ms");
    }

    static double Percentile(List<double> sorted, int p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Count - 1))];
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CosmoApiServer.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot find CosmoApiServer.sln — run from within the repo.");
    }
}
