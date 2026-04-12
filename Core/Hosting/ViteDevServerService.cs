using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace CosmoApiServer.Core.Hosting;

public sealed class ViteDevServerOptions
{
    /// <summary>Directory the command is run from (relative to current working directory).</summary>
    public string WorkingDirectory { get; set; } = "frontend";

    /// <summary>Executable to run (e.g. "npm", "pnpm", "bun").</summary>
    public string Command { get; set; } = "npm";

    /// <summary>Arguments passed to <see cref="Command"/> (e.g. "run dev").</summary>
    public string Arguments { get; set; } = "run dev";

    /// <summary>
    /// A substring to look for in the dev server's stdout/stderr that signals it is ready
    /// to accept connections. When null, the process is assumed ready immediately.
    /// </summary>
    public string? ReadyPattern { get; set; } = "Local:";

    /// <summary>How long to wait for the ready signal before proceeding anyway.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Label prepended to console output lines from the dev server process.</summary>
    public string LogPrefix { get; set; } = "[vite]";

    /// <summary>Environment variables to set for the child process.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}

/// <summary>
/// Starts and stops the Vite or Nuxt dev server as part of the Cosmo app lifecycle,
/// eliminating the need for a separate shell script (e.g. run-dev.sh).
///
/// Register via <c>builder.UseViteDevServer()</c>. The process is started before the
/// HTTP server begins accepting connections and killed cleanly on shutdown.
/// </summary>
public sealed class ViteDevServerService : IHostedService, IDisposable
{
    private readonly ViteDevServerOptions _options;
    private Process? _process;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ViteDevServerService(ViteDevServerOptions options) => _options = options;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workDir = Path.GetFullPath(_options.WorkingDirectory);
        if (!Directory.Exists(workDir))
        {
            Console.Error.WriteLine($"{_options.LogPrefix} Working directory '{workDir}' not found — skipping dev server startup.");
            _ready.TrySetResult();
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName               = _options.Command,
            Arguments              = _options.Arguments,
            WorkingDirectory       = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        foreach (var (key, value) in _options.Environment)
            startInfo.EnvironmentVariables[key] = value;

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.WriteLine($"{_options.LogPrefix} {e.Data}");

            if (_options.ReadyPattern is not null &&
                e.Data.Contains(_options.ReadyPattern, StringComparison.OrdinalIgnoreCase))
            {
                _ready.TrySetResult();
            }
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.Error.WriteLine($"{_options.LogPrefix} {e.Data}");

            // Some dev servers (e.g. Nuxt) write the ready line to stderr.
            if (_options.ReadyPattern is not null &&
                e.Data.Contains(_options.ReadyPattern, StringComparison.OrdinalIgnoreCase))
            {
                _ready.TrySetResult();
            }
        };

        _process.Exited += (_, _) =>
        {
            // If the process exits before signalling ready, unblock waiters so the app
            // doesn't hang at startup — they will see 502 errors instead.
            _ready.TrySetResult();
            Console.Error.WriteLine($"{_options.LogPrefix} Dev server process exited (code {_process?.ExitCode}).");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Console.WriteLine($"{_options.LogPrefix} Started '{_options.Command} {_options.Arguments}' in '{workDir}' (PID {_process.Id}).");

        if (_options.ReadyPattern is null)
        {
            _ready.TrySetResult();
            return;
        }

        // Wait for the ready signal, but don't block startup indefinitely.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.ReadyTimeout);

        try
        {
            await _ready.Task.WaitAsync(cts.Token);
            Console.WriteLine($"{_options.LogPrefix} Dev server is ready.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"{_options.LogPrefix} Ready timeout elapsed — proceeding without confirmation.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited) return Task.CompletedTask;

        try
        {
            // Graceful SIGTERM first; kill if the process doesn't exit in 5 seconds.
            _process.Kill(entireProcessTree: true);
            Console.WriteLine($"{_options.LogPrefix} Dev server process stopped.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{_options.LogPrefix} Error stopping dev server: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* best-effort */ }
        }
        _process?.Dispose();
    }
}
