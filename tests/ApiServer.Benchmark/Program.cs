using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ApiServer.Benchmark;

record BenchResult(string Scenario, List<double> Samples)
{
    public int N        => Samples.Count;
    public double Min   => Samples.Count > 0 ? Samples.Min() : 0;
    public double P50   => Percentile(50);
    public double P95   => Percentile(95);
    public double P99   => Percentile(99);
    public double Max   => Samples.Count > 0 ? Samples.Max() : 0;
    public double OpsPerSec => P50 > 0 ? 1000.0 / P50 : 0;

    double Percentile(int p)
    {
        if (Samples.Count == 0) return 0;
        var sorted = Samples.OrderBy(x => x).ToList();
        int idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}

class Program
{
    const int WarmupRounds  = 100;
    const int MeasureRounds = 1000; // Reduced for rendering tests

    static readonly string EchoBody = JsonSerializer.Serialize(new
    {
        id = 42, name = "Test Item", category = "Electronics", price = 19.99,
        description = "A sample product for benchmarking purposes"
    });

    static async Task Main(string[] args)
    {
        string target = args.Length > 0 ? args[0] : "CosmoApiServer";
        string url = target switch
        {
            "CosmoApiServer" => "http://127.0.0.1:9001",
            "AspNetCore"     => "http://127.0.0.1:9002",
            "CosmoRazor"     => "http://127.0.0.1:9003",
            "BlazorSSR"      => "http://127.0.0.1:9004",
            _                => args[0].StartsWith("http") ? args[0] : "http://127.0.0.1:9001"
        };

        Console.WriteLine($"╔══════════════════════════════════════════════╗");
        Console.WriteLine($"║  HTTP Benchmark — {target,-27}║");
        Console.WriteLine($"║  Endpoint : {url,-34}║");
        Console.WriteLine($"║  Rounds   : {WarmupRounds} warmup + {MeasureRounds} measured{"",-12}║");
        Console.WriteLine($"╚══════════════════════════════════════════════╝");

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 100,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(url) };

        // Verify server
        try {
            var resp = await http.GetAsync("/ping");
            resp.EnsureSuccessStatusCode();
            Console.WriteLine($"Server responded: {(int)resp.StatusCode} — ready.\n");
        } catch (Exception ex) {
            Console.Error.WriteLine($"ERROR: Server at {url} is not responding: {ex.Message}");
            return;
        }

        // ─── Warmup ───────────────────────────────────────────────────────
        Console.Write("[Warmup] Running...");
        for (int i = 0; i < WarmupRounds; i++)
        {
            await http.GetAsync("/ping");
            if (target.Contains("Razor") || target.Contains("Blazor"))
                await http.GetAsync("/bench");
            else
                await http.GetAsync("/json");
        }
        Console.WriteLine(" done.\n");

        // ─── Measured rounds ──────────────────────────────────────────────
        var results = new List<BenchResult>();

        var scenarios = new List<(string name, Func<Task<HttpResponseMessage>> fn)>();
        
        if (target == "CosmoRazor" || target == "BlazorSSR" || target.StartsWith("http"))
        {
            scenarios.Add(("GET /ping", () => http.GetAsync("/ping")));
            scenarios.Add(("GET /bench (100 rows)", () => http.GetAsync("/bench")));
        }
        else
        {
            scenarios.Add(("GET /ping",      () => http.GetAsync("/ping")));
            scenarios.Add(("GET /json",      () => http.GetAsync("/json")));
            scenarios.Add(("GET /route/{id}", () => http.GetAsync("/route/7")));
            scenarios.Add(("POST /echo",     () => http.PostAsync("/echo", new StringContent(EchoBody, Encoding.UTF8, "application/json"))));
        }

        foreach (var s in scenarios)
        {
            results.Add(await RunScenario(s.name, MeasureRounds, s.fn));
        }

        // ─── Report ───────────────────────────────────────────────────────
        PrintReport(target, results);
    }

    static async Task<BenchResult> RunScenario(string name, int rounds, Func<Task<HttpResponseMessage>> fn)
    {
        Console.Write($"  {name,-25} [");
        var samples = new List<double>(rounds);
        int errors = 0;
        int progressStep = Math.Max(1, rounds / 20);

        for (int i = 0; i < rounds; i++)
        {
            if (i % progressStep == 0) Console.Write("=");
            
            var sw = Stopwatch.StartNew();
            try {
                using var resp = await fn();
                await resp.Content.ReadAsByteArrayAsync();
                sw.Stop();
                if (resp.IsSuccessStatusCode) samples.Add(sw.Elapsed.TotalMilliseconds);
                else errors++;
            } catch {
                sw.Stop();
                errors++;
            }
        }

        Console.WriteLine($"] done{(errors > 0 ? $" ({errors} errors)" : "")}.");
        return new BenchResult(name, samples);
    }

    static void PrintReport(string server, List<BenchResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════╦════════╦═══════╦═══════╦═══════╦═══════╦═════════════╣");
        Console.WriteLine($"║  Scenario                ║   N    ║  Min  ║  P50  ║  P95  ║  P99  ║   ops/sec   ║");
        Console.WriteLine($"╠══════════════════════════╬════════╬═══════╬═══════╬═══════╬═══════╬═════════════╣");
        foreach (var r in results) {
            Console.WriteLine($"║  {r.Scenario,-24}║  {r.N,4}  ║ {r.Min,5:F2} ║ {r.P50,5:F2} ║ {r.P95,5:F2} ║ {r.P99,5:F2} ║ {r.OpsPerSec,9:F1}   ║");
        }
        Console.WriteLine($"╚══════════════════════════╩════════╩═══════╩═══════╩═══════╩═══════╩═════════════╝");
    }
}
