using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// A real session-runner process owned by one E2E fixture. Its manifest and log roots are kept
/// under TestOutput so an E2E run can never register a pty-host with the shared production runner.
/// </summary>
internal sealed class IsolatedSessionRunner : IAsyncDisposable, IIsolatedSessionRunnerClient
{
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessStartTimeTolerance = TimeSpan.FromSeconds(2);
    private static readonly object CrashedRunSweepGate = new();
    private static Task? _crashedRunSweep;
    private readonly Func<int> _getRandomAvailablePort;
    private Process? _process;
    private Task? _stdoutCopy;
    private Task? _stderrCopy;

    public IsolatedSessionRunner(Func<int> getRandomAvailablePort, string repositoryRoot)
    {
        _getRandomAvailablePort = getRandomAvailablePort;
        RunDirectory = Path.Combine(
            repositoryRoot,
            "tests",
            "Antiphon.E2E",
            "TestOutput",
            "runner",
            $"run-{Guid.NewGuid():N}");
    }

    public string RunDirectory { get; }

    public string BaseUrl { get; private set; } = null!;

    public async Task StartAsync()
    {
        await SweepCrashedRunsOnceAsync(Path.GetDirectoryName(RunDirectory)!);
        Directory.CreateDirectory(RunDirectory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var port = _getRandomAvailablePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            var process = StartProcess(port);
            _process = process;

            await File.WriteAllTextAsync(
                Path.Combine(RunDirectory, "runner.json"),
                JsonSerializer.Serialize(new
                {
                    Pid = process.Id,
                    ProcessStartTimeUtc = process.StartTime.ToUniversalTime()
                }));

            if (await WaitUntilHealthyAsync(process))
                return;

            if (process.HasExited)
                await DrainOutputAsync();
            await StopProcessAsync(process);
            var outputTail = GetOutputTail();
            var bindFailure = IsBindFailure(outputTail);

            if (attempt == 0 && bindFailure)
                continue;

            throw new InvalidOperationException(
                $"Isolated session-runner at {BaseUrl} did not become healthy within "
                + $"{ReadinessBudget.TotalSeconds:0} seconds. Captured output tail:{Environment.NewLine}{outputTail}");
        }

        throw new InvalidOperationException("The isolated session-runner did not start.");
    }

    public string GetOutputTail()
    {
        var stdout = ReadTail(Path.Combine(RunDirectory, "stdout.log"));
        var stderr = ReadTail(Path.Combine(RunDirectory, "stderr.log"));
        return $"[stdout]{Environment.NewLine}{stdout}{Environment.NewLine}[stderr]{Environment.NewLine}{stderr}";
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    /// <summary>
    /// Gets the complete session list from this fixture's dedicated runner. Because the runner is
    /// structurally owned by this fixture, this list is the teardown authority rather than a
    /// database-derived subset of it.
    /// </summary>
    public async Task<IReadOnlyList<RunnerSessionDto>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        return await client.GetFromJsonAsync<IReadOnlyList<RunnerSessionDto>>("sessions", cancellationToken)
            ?? [];
    }

    /// <summary>Uses the runner's sanctioned bulk endpoint to stop every session and pty-host.</summary>
    public async Task KillAllSessionsAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("sessions/kill-all", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Stops only the runner child. Detached pty-hosts must already have been proved gone by the
    /// fixture census; a runner tree-kill intentionally cannot reach them.
    /// </summary>
    public async Task StopAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is not null)
            await StopProcessAsync(process);
    }

    /// <summary>Removes a clean run's diagnostic root without masking teardown evidence on failure.</summary>
    public Task DeleteRunDirectoryBestEffortAsync() => DeleteDirectoryBestEffortAsync(RunDirectory);

    private Process StartProcess(int port)
    {
        var runnerPath = Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe");
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException(
                "Antiphon.SessionRunner.exe was not copied to the E2E output directory. "
                + "The E2E project must reference Antiphon.SessionRunner.",
                runnerPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = runnerPath,
            WorkingDirectory = RunDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["SessionRunner__SessionLogPath"] = Path.Combine(RunDirectory, "logs");
        startInfo.Environment["SessionRunner__PtyHostLingerHours"] = "0.02";
        startInfo.Environment["Serilog__LogPath"] = RunDirectory;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {runnerPath}.");

        _stdoutCopy = CopyOutputAsync(process.StandardOutput, Path.Combine(RunDirectory, "stdout.log"));
        _stderrCopy = CopyOutputAsync(process.StandardError, Path.Combine(RunDirectory, "stderr.log"));
        return process;
    }

    private async Task<bool> WaitUntilHealthyAsync(Process process)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow + ReadinessBudget;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                return false;

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/health");
                if (response.StatusCode == HttpStatusCode.OK)
                    return true;
            }
            catch (HttpRequestException)
            {
                // The process has not bound its listener yet.
            }
            catch (TaskCanceledException)
            {
                // A listener that has not started can consume this short probe timeout.
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task CopyOutputAsync(StreamReader source, string destination)
    {
        await using var writer = new StreamWriter(destination, append: false) { AutoFlush = true };
        while (await source.ReadLineAsync() is { } line)
            await writer.WriteLineAsync(line);
    }

    private async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException)
        {
            // The child exited between inspection and kill.
        }
        catch (TimeoutException)
        {
            // S2 owns the graceful kill-all-first teardown. S1 only guarantees this child is stopped.
        }
        finally
        {
            await DrainOutputAsync();
            process.Dispose();
        }
    }

    private async Task DrainOutputAsync()
    {
        var copies = new[] { _stdoutCopy, _stderrCopy }.Where(task => task is not null).Cast<Task>().ToArray();
        if (copies.Length == 0)
            return;

        try
        {
            await Task.WhenAll(copies).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // The child may still be flushing as it is killed; its files remain the best evidence.
        }
    }

    private static bool IsBindFailure(string output) =>
        output.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);

    private static string ReadTail(string path)
    {
        if (!File.Exists(path))
            return "(no output captured)";

        var content = File.ReadAllText(path);
        const int maxCharacters = 8_000;
        return content.Length <= maxCharacters ? content : content[^maxCharacters..];
    }

    private HttpClient CreateClient() => new()
    {
        BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static Task SweepCrashedRunsOnceAsync(string runnerRoot)
    {
        lock (CrashedRunSweepGate)
            return _crashedRunSweep ??= SweepCrashedRunsAsync(runnerRoot, new SystemProcessInspector());
    }

    /// <summary>
    /// Reaps detached hosts left by a previously crashed E2E process. This intentionally examines
    /// only the fixed E2E TestOutput/runner root; no production manifest directory is ever swept.
    /// </summary>
    internal static async Task SweepCrashedRunsAsync(string runnerRoot, IProcessInspector processes)
    {
        if (!Directory.Exists(runnerRoot))
            return;

        foreach (var runDirectory in Directory.EnumerateDirectories(runnerRoot, "run-*", SearchOption.TopDirectoryOnly))
        {
            if (IsConcurrentRunner(runDirectory, processes))
                continue;

            var manifestDirectory = Path.Combine(runDirectory, "logs", "pty-hosts", "manifests");
            if (Directory.Exists(manifestDirectory))
            {
                foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var manifest = PtyHostManifest.TryLoad(manifestPath);
                    if (manifest is not null
                        && processes.TryGet(manifest.HostPid, out var host)
                        && Matches(host, "Antiphon.PtyHost", manifest.HostStartTimeUtc))
                    {
                        processes.KillTree(manifest.HostPid);
                    }
                }
            }

            await DeleteDirectoryBestEffortAsync(runDirectory);
        }
    }

    private static bool IsConcurrentRunner(string runDirectory, IProcessInspector processes)
    {
        var markerPath = Path.Combine(runDirectory, "runner.json");
        if (!File.Exists(markerPath))
            return false;

        try
        {
            var marker = JsonSerializer.Deserialize<RunnerMarker>(File.ReadAllText(markerPath));
            return marker is not null
                && processes.TryGet(marker.Pid, out var runner)
                && Matches(runner, "Antiphon.SessionRunner", marker.ProcessStartTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Matches(ProcessIdentity process, string expectedName, DateTime expectedStartTimeUtc) =>
        string.Equals(process.Name, expectedName, StringComparison.OrdinalIgnoreCase)
        && (process.StartTimeUtc.ToUniversalTime() - expectedStartTimeUtc.ToUniversalTime()).Duration()
            <= ProcessStartTimeTolerance;

    private static async Task DeleteDirectoryBestEffortAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
            }
        }
    }

    private sealed record RunnerMarker(int Pid, DateTime ProcessStartTimeUtc);

}

/// <summary>
/// The deliberate teardown order. Keeping it outside the fixture makes the safety property unit
/// testable without starting a PostgreSQL container, a runner executable, or real processes.
/// </summary>
internal static class IsolatedSessionRunnerTeardown
{
    public static async Task<IReadOnlyList<RunnerSessionDto>> SnapshotKillAllThenCensusAsync(
        IIsolatedSessionRunnerClient runner,
        Func<IReadOnlyList<RunnerSessionDto>, Task> snapshotObserved,
        Func<IReadOnlyList<SessionHostSnapshot>, Task> census,
        Action<Exception> killAllFailed,
        CancellationToken cancellationToken)
    {
        var listed = await runner.ListSessionsAsync(cancellationToken);
        await snapshotObserved(listed);

        // Snapshot before kill-all: an exited session no longer names the host that must be
        // censused, and no database subset is allowed to discard an unclaimed runner session.
        var hosts = listed
            .Where(session => session.HostPid is int pid && pid > 0)
            .Select(session => new SessionHostSnapshot(session.SessionId, session.HostPid!.Value))
            .ToList();

        try
        {
            await runner.KillAllSessionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The census still runs after an unsuccessful request: this is the only way a failed
            // pipe cleanup becomes a red leak rather than an unobserved teardown error.
            killAllFailed(ex);
        }

        await census(hosts);
        return listed;
    }
}

internal interface IIsolatedSessionRunnerClient
{
    Task<IReadOnlyList<RunnerSessionDto>> ListSessionsAsync(CancellationToken cancellationToken);
    Task KillAllSessionsAsync(CancellationToken cancellationToken);
}

internal readonly record struct SessionHostSnapshot(Guid SessionId, int HostPid);

/// <summary>A deliberately small seam so sweep tests never need to create or kill real processes.</summary>
internal interface IProcessInspector
{
    bool TryGet(int pid, out ProcessIdentity process);
    void KillTree(int pid);
}

internal readonly record struct ProcessIdentity(string Name, DateTime StartTimeUtc);

internal sealed class SystemProcessInspector : IProcessInspector
{
    public bool TryGet(int pid, out ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                identity = default;
                return false;
            }

            identity = new ProcessIdentity(process.ProcessName, process.StartTime.ToUniversalTime());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            identity = default;
            return false;
        }
    }

    public void KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The host exited between the liveness check and its cleanup tree-kill.
        }
    }
}
