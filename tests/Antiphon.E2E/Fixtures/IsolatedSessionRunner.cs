using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// A real session-runner process owned by one E2E fixture. Its manifest and log roots are kept
/// under TestOutput so an E2E run can never register a pty-host with the shared production runner.
/// </summary>
internal sealed class IsolatedSessionRunner : IAsyncDisposable
{
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(15);
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
        if (_process is not null)
            await StopProcessAsync(_process);
    }

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

}
