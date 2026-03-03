using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Diagnostics;

namespace CosmoS3.Benchmark;

class Program
{
    const string Endpoint    = "http://localhost:8100";
    const string AccessKey   = "default";
    const string SecretKey   = "default";
    const string Region      = "us-east-1";
    const string BucketName  = "benchmark-bucket-cosmo";

    // Iterations per scenario
    const int WarmupRounds   = 10;
    const int MeasureRounds  = 100;

    static AmazonS3Client _client;
    static List<BenchmarkResult> _results = new();

    static async Task Main(string[] args)
    {
        string serverName = args.Length > 0 ? args[0] : "CosmoS3";

        Console.WriteLine($"╔══════════════════════════════════════════╗");
        Console.WriteLine($"║  S3 Benchmark — {serverName,-26}║");
        Console.WriteLine($"║  Endpoint: {Endpoint,-31}║");
        Console.WriteLine($"║  Rounds: {WarmupRounds} warmup + {MeasureRounds} measured{"",-9}║");
        Console.WriteLine($"╚══════════════════════════════════════════╝");

        var config = new AmazonS3Config
        {
            ServiceURL            = Endpoint,
            ForcePathStyle        = true,
            UseHttp               = true,
            Timeout               = TimeSpan.FromSeconds(30),
            MaxErrorRetry         = 0,
            DisableHostPrefixInjection = true,
        };
        var creds  = new BasicAWSCredentials(AccessKey, SecretKey);
        _client    = new AmazonS3Client(creds, config);

        // --- Setup ---
        await SetupBucket();

        // --- Warmup (not measured) ---
        Console.WriteLine("\n[Warmup] Running warmup rounds...");
        await RunScenarios(WarmupRounds, measure: false);
        Console.WriteLine("[Warmup] Done.\n");

        // --- Measured rounds ---
        await RunScenarios(MeasureRounds, measure: true);

        // --- Teardown ---
        await TeardownBucket();

        // --- Report ---
        PrintReport(serverName);
    }

    static async Task SetupBucket()
    {
        Console.Write($"Setting up bucket '{BucketName}'... ");
        try
        {
            await _client.PutBucketAsync(BucketName);
            Console.WriteLine("created.");
        }
        catch
        {
            Console.WriteLine("already exists (ok).");
        }
    }

    static async Task TeardownBucket()
    {
        Console.WriteLine($"\nCleaning up bucket '{BucketName}'...");
        try
        {
            // Delete all objects
            var list = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
            if (list.S3Objects.Count > 0)
            {
                await _client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = BucketName,
                    Objects    = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            }
            await _client.DeleteBucketAsync(BucketName);
            Console.WriteLine("Done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Teardown warning: {ex.Message}");
        }
    }

    static async Task RunScenarios(int rounds, bool measure)
    {
        var smallData  = new byte[1_024];         // 1 KB
        var medData    = new byte[102_400];        // 100 KB
        var largeData  = new byte[1_048_576];      // 1 MB
        new Random(42).NextBytes(smallData);
        new Random(43).NextBytes(medData);
        new Random(44).NextBytes(largeData);

        const string keySmall  = "bench-small.bin";
        const string keyMed    = "bench-medium.bin";
        const string keyLarge  = "bench-large.bin";

        // Seed objects for GET tests (delete first if they exist to support servers without versioning)
        await TryDelete(keySmall);
        await TryDelete(keyMed);
        await TryDelete(keyLarge);
        await PutBytes(keySmall, smallData);
        await PutBytes(keyMed,   medData);
        await PutBytes(keyLarge, largeData);

        for (int i = 0; i < rounds; i++)
        {
            await Run("ListBuckets",       measure, () => _client.ListBucketsAsync());
            await Run("ListObjects",       measure, () => _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName }));
            await Run("PutObject_1KB",     measure, () => PutBytes($"put-bench-s-{i}.bin", smallData));
            await Run("PutObject_100KB",   measure, () => PutBytes($"put-bench-m-{i}.bin", medData));
            await Run("PutObject_1MB",     measure, () => PutBytes($"put-bench-l-{i}.bin", largeData));
            await Run("GetObject_1KB",     measure, async () => { var r = await _client.GetObjectAsync(BucketName, keySmall); await r.ResponseStream.CopyToAsync(Stream.Null); });
            await Run("GetObject_100KB",   measure, async () => { var r = await _client.GetObjectAsync(BucketName, keyMed);   await r.ResponseStream.CopyToAsync(Stream.Null); });
            await Run("GetObject_1MB",     measure, async () => { var r = await _client.GetObjectAsync(BucketName, keyLarge); await r.ResponseStream.CopyToAsync(Stream.Null); });
            await Run("HeadObject",        measure, () => _client.GetObjectMetadataAsync(BucketName, keySmall));
            await Run("DeleteObject",      measure, async () => {
                await PutBytes($"delete-bench-{i}.bin", smallData);
                await _client.DeleteObjectAsync(BucketName, $"delete-bench-{i}.bin");
            });
        }
    }

    static async Task TryDelete(string key)
    {
        try { await _client.DeleteObjectAsync(BucketName, key); } catch { }
    }

    static async Task PutBytes(string key, byte[] data)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = BucketName,
            Key         = key,
            InputStream = new MemoryStream(data),
        });
    }

    static async Task Run(string name, bool measure, Func<Task> action)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            if (measure)
                Record(name, sw.Elapsed.TotalMilliseconds, success: true);
        }
        catch (Exception ex)
        {
            if (measure)
                Record(name, -1, success: false, error: ex.Message);
        }
    }

    static void Record(string name, double ms, bool success, string error = null)
    {
        _results.Add(new BenchmarkResult { Name = name, Ms = ms, Success = success, Error = error });
    }

    static void PrintReport(string serverName)
    {
        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  BENCHMARK RESULTS — {serverName,-52}║");
        Console.WriteLine($"╠══════════════════════╦═══════╦═══════╦═══════╦═══════╦═══════╦══════════╣");
        Console.WriteLine($"║  Scenario            ║  N    ║  Min  ║  P50  ║  P95  ║  P99  ║  Max     ║");
        Console.WriteLine($"╠══════════════════════╬═══════╬═══════╬═══════╬═══════╬═══════╬══════════╣");

        var groups = _results.GroupBy(r => r.Name);
        foreach (var g in groups)
        {
            var ok   = g.Where(r => r.Success).Select(r => r.Ms).OrderBy(x => x).ToList();
            int fail = g.Count(r => !r.Success);
            if (ok.Count == 0)
            {
                Console.WriteLine($"║  {g.Key,-20}║ FAIL  ║   --  ║   --  ║   --  ║   --  ║   --     ║");
                continue;
            }
            double min  = ok.First();
            double max  = ok.Last();
            double p50  = Percentile(ok, 50);
            double p95  = Percentile(ok, 95);
            double p99  = Percentile(ok, 99);
            string fstr = fail > 0 ? $" ({fail}err)" : "";
            Console.WriteLine($"║  {g.Key,-20}║ {ok.Count,5}{fstr}║{min,6:F1} ║{p50,6:F1} ║{p95,6:F1} ║{p99,6:F1} ║{max,9:F1} ║");
        }

        Console.WriteLine($"╚══════════════════════╩═══════╩═══════╩═══════╩═══════╩═══════╩══════════╝");
        Console.WriteLine($"  All values in milliseconds. N={MeasureRounds} measured rounds.");

        // Throughput summary
        Console.WriteLine();
        Console.WriteLine("  Throughput (ops/sec, based on P50 latency):");
        foreach (var g in groups)
        {
            var ok = g.Where(r => r.Success).Select(r => r.Ms).OrderBy(x => x).ToList();
            if (ok.Count == 0) continue;
            double p50 = Percentile(ok, 50);
            Console.WriteLine($"    {g.Key,-22}: {1000.0 / p50,8:F1} ops/sec");
        }
    }

    static double Percentile(List<double> sorted, int p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Count - 1))];
    }
}

record BenchmarkResult
{
    public string Name    { get; init; }
    public double Ms      { get; init; }
    public bool   Success { get; init; }
    public string Error   { get; init; }
}
