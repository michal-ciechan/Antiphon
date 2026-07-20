using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antiphon.AppHost.Supervisor;

/// <summary>
/// Singleton service that manages all daemon processes:
/// - adopts already-running services on AppHost start
/// - starts new supervisor scripts for missing services
/// - watchdog: polls /health and updates dashboard state
/// - log tailer: streams log files to Aspire dashboard
/// </summary>
public sealed class DaemonProcessService(
    ResourceNotificationService notifier,
    ResourceLoggerService loggers,
    ILogger<DaemonProcessService> log)
    : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DaemonEntry> _entries = new();
    private readonly CancellationTokenSource _cts = new();

    // Called by lifecycle hook for each resource before AppHost reports "started"
    public async Task InitialiseAsync(DaemonProcessResource resource, CancellationToken ct)
    {
        var entry = new DaemonEntry(resource);
        _entries[resource.Name] = entry;

        EnsureLogDir(resource);

        if (await IsPortListeningAsync(resource.Config.Port, ct))
        {
            log.LogInformation("{Name}: port {Port} already listening — adopting", resource.Name, resource.Config.Port);
            entry.State = DaemonState.Running;
            entry.ServicePid = ReadPid(resource.ServicePidFile);
            entry.SupervisorPid = ReadPid(resource.SupervisorPidFile);
            await notifier.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Running });
        }
        else
        {
            // Kill any stale supervisor from a previous AppHost run before spawning a new one.
            KillPid(ReadPid(resource.SupervisorPidFile));
            await StartDaemonAsync(entry, resource, ct);
        }

        _ = Task.Run(() => WatchdogLoopAsync(entry, resource, _cts.Token), _cts.Token);
        _ = Task.Run(() => TailLogAsync(entry, resource, _cts.Token), _cts.Token);
    }

    // ── Public control API (called from dashboard commands + ControlApiService) ──

    public async Task StartAsync(string name, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(name, out var entry)) return;
        WriteState(entry.Resource.StateFile, "running");
        if (!await IsPortListeningAsync(entry.Resource.Config.Port, ct))
            await StartDaemonAsync(entry, entry.Resource, ct);
    }

    public Task StopAsync(string name, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(name, out var entry)) return Task.CompletedTask;
        WriteState(entry.Resource.StateFile, "stopped");
        KillPid(ReadPid(entry.Resource.ServicePidFile));
        entry.State = DaemonState.Stopped;
        return notifier.PublishUpdateAsync(entry.Resource, s => s with { State = KnownResourceStates.Exited });
    }

    public async Task RestartAsync(string name, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(name, out var entry)) return;

        WriteState(entry.Resource.StateFile, "running");
        KillPid(ReadPid(entry.Resource.ServicePidFile));   // supervisor detects and restarts
        entry.State = DaemonState.Starting;
        await notifier.PublishUpdateAsync(entry.Resource, s => s with { State = KnownResourceStates.Starting });

        // Give supervisor 5 s to restart; if it doesn''t, fire a fresh one
        await Task.Delay(5_000, ct);
        if (!await IsPortListeningAsync(entry.Resource.Config.Port, ct))
            await StartDaemonAsync(entry, entry.Resource, ct);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task StartDaemonAsync(DaemonEntry entry, DaemonProcessResource resource, CancellationToken ct)
    {
        WriteState(resource.StateFile, "running");
        await notifier.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Starting });
        entry.State = DaemonState.Starting;

        var scriptPath = FindScript("run-daemon.ps1");
        var args = BuildArgs(resource, scriptPath);

        log.LogInformation("{Name}: launching supervisor → {Script}", resource.Name, scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName         = "pwsh.exe",
            Arguments        = args,
            UseShellExecute  = true,   // detach from AppHost job object
            WindowStyle      = ProcessWindowStyle.Hidden,
        };

        var supervisor = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start supervisor for {resource.Name}");

        WritePid(resource.SupervisorPidFile, supervisor.Id);
        entry.SupervisorPid = supervisor.Id;
    }

    private async Task WatchdogLoopAsync(DaemonEntry entry, DaemonProcessResource resource, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            if (ReadState(resource.StateFile) == "stopped") continue;

            bool healthy = resource.Config.HealthPath is { } hp
                ? await CheckHttpHealthAsync(http, resource.Config.Port, hp, ct)
                : await IsPortListeningAsync(resource.Config.Port, ct);

            var newState = healthy ? KnownResourceStates.Running : KnownResourceStates.RuntimeUnhealthy;

            if (entry.State != (healthy ? DaemonState.Running : DaemonState.Unhealthy))
            {
                entry.State = healthy ? DaemonState.Running : DaemonState.Unhealthy;
                await notifier.PublishUpdateAsync(resource, s => s with { State = newState });
                log.LogInformation("{Name}: health → {State}", resource.Name, newState);
            }
        }
    }

    private async Task TailLogAsync(DaemonEntry entry, DaemonProcessResource resource, CancellationToken ct)
    {
        var logFile = resource.LogFile;
        var logger  = loggers.GetLogger(resource);

        // Wait for log file to appear (supervisor hasn''t written yet)
        for (var i = 0; i < 60 && !File.Exists(logFile) && !ct.IsCancellationRequested; i++)
            await Task.Delay(500, ct);

        if (!File.Exists(logFile)) return;

        try
        {
            using var fs     = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is not null)
                {
                    if (line.StartsWith("[ERR]", StringComparison.Ordinal))
                        logger.LogError("{Line}", line);
                    else
                        logger.LogInformation("{Line}", line);
                }
                else
                {
                    await Task.Delay(200, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{Name}: log tailer crashed", resource.Name);
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static async Task<bool> IsPortListeningAsync(int port, CancellationToken ct)
    {
        // Try IPv4 first, then IPv6 — Vite binds ::1, dotnet binds 0.0.0.0
        foreach (var addr in new[] { "127.0.0.1", "::1" })
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(addr, port, ct);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static async Task<bool> CheckHttpHealthAsync(HttpClient http, int port, string path, CancellationToken ct)
    {
        try
        {
            // Use localhost so OS picks whichever family the server is bound to
            var resp = await http.GetAsync($"http://localhost:{port}{path}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static int? ReadPid(string file)
    {
        try { return int.TryParse(File.ReadAllText(file).Trim(), out var p) ? p : null; }
        catch { return null; }
    }

    private static void WritePid(string file, int pid)
    {
        try { File.WriteAllText(file, pid.ToString()); }
        catch { /* best-effort */ }
    }

    private static string ReadState(string file)
    {
        try { return File.ReadAllText(file).Trim().ToLowerInvariant(); }
        catch { return "running"; }
    }

    private static void WriteState(string file, string state)
    {
        try { File.WriteAllText(file, state); }
        catch { /* best-effort */ }
    }

    private static void KillPid(int? pid)
    {
        if (pid is null) return;
        try { Process.GetProcessById(pid.Value).Kill(entireProcessTree: true); }
        catch { /* already dead */ }
    }

    private static void EnsureLogDir(DaemonProcessResource r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(r.LogFile)!);
    }

    private static string FindScript(string scriptName)
    {
        // AppContext.BaseDirectory = bin/Debug/net9.0/ inside Antiphon.AppHost/
        // 4 levels up = repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "scripts", scriptName);
        if (!File.Exists(path)) throw new FileNotFoundException($"Daemon script not found: {path}");
        return path;
    }

    private static string BuildArgs(DaemonProcessResource r, string scriptPath)
    {
        // ExeArgs is passed as a single space-joined string — PowerShell -File mode
        // only binds the first value to [string[]] params, so the PS script splits it.
        // A LEADING HYPHEN would make -File treat the value as a parameter name ("Missing an
        // argument for parameter 'ExeArgs'"), so hyphen-leading args (e.g. "--urls …") get a
        // leading space; run-daemon splits on whitespace, so it is harmless.
        var exeArgs = string.Join(" ", r.Config.Args);
        if (exeArgs.StartsWith('-'))
            exeArgs = " " + exeArgs;
        var args = new List<string>
        {
            "-NonInteractive", "-NoProfile", "-File", $"\"{scriptPath}\"",
            "-Name",           $"\"{r.Name}\"",
            "-WorkDir",        $"\"{r.Config.WorkingDirectory}\"",
            "-Exe",            $"\"{r.Config.Executable}\"",
            "-ExeArgs",        $"\"{exeArgs}\"",
            "-LogFile",        $"\"{r.LogFile}\"",
            "-ServicePidFile", $"\"{r.ServicePidFile}\"",
            "-StateFile",      $"\"{r.StateFile}\"",
        };

        // Build-before-launch + run the exe directly, so no 'dotnet run' kill-on-close job
        // captures the daemon's detached child processes (see DaemonProcessConfig.BuildProjectDir).
        if (!string.IsNullOrWhiteSpace(r.Config.BuildProjectDir))
        {
            args.Add("-BuildProjectDir");
            args.Add($"\"{r.Config.BuildProjectDir}\"");
        }

        return string.Join(" ", args);
    }

    // ── IHostedService (no-op; InitialiseAsync is called by the lifecycle hook) ──

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken)  { _cts.Cancel(); return Task.CompletedTask; }

    private int _disposed;
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}

internal sealed class DaemonEntry(DaemonProcessResource resource)
{
    public DaemonProcessResource Resource { get; } = resource;
    public DaemonState State { get; set; } = DaemonState.Unknown;
    public int? SupervisorPid { get; set; }
    public int? ServicePid { get; set; }
}

internal enum DaemonState { Unknown, Starting, Running, Unhealthy, Stopped }
