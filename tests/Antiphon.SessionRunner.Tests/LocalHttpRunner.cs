using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0502: shared random-port <c>Antiphon.SessionRunner.exe</c> host, extracted from
/// <c>RunnerCustodyCrashTests.HttpRunner</c>. Never binds 17204.
/// </summary>
public sealed class LocalHttpRunner : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();
    private readonly TaskCompletionSource<string> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _evidence;
    public HttpClient Http { get; private set; } = null!;
    public Uri Address { get; private set; } = null!;

    private LocalHttpRunner(SessionRunnerSettings settings)
    {
        Directory.CreateDirectory(settings.SessionLogPath);
        _evidence = Path.Combine(AppContext.BaseDirectory, "TestOutput", "Logs", "LocalHttpRunner", Guid.NewGuid().ToString("N") + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(_evidence)!);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe"))
        {
            WorkingDirectory = settings.SessionLogPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "--urls", "http://127.0.0.1:0",
                     "--SessionRunner:SessionLogPath", settings.SessionLogPath,
                     "--SessionRunner:Herdr:Enabled", "false",
                     "--SessionRunner:PtyBackend", "modern",
                     "--SessionRunner:CpuWatchdogEnabled", "false",
                     "--SessionRunner:PtyHostLingerHours", "0.02",
                     "--Serilog:LogPath", Path.Combine(settings.SessionLogPath, "server-logs"),
                 })
            info.ArgumentList.Add(arg);
        _process = new Process { StartInfo = info };
        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        _output.Enqueue(line);
        var match = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
        if (match.Success) _listening.TrySetResult(match.Groups[1].Value);
    }

    public static async Task<LocalHttpRunner> StartAsync(SessionRunnerSettings settings)
    {
        var runner = new LocalHttpRunner(settings);
        try
        {
            runner._process.Start().ShouldBeTrue();
            runner._process.BeginOutputReadLine();
            runner._process.BeginErrorReadLine();
            var address = await runner._listening.Task.WaitAsync(TimeSpan.FromSeconds(30));
            new Uri(address).Port.ShouldNotBe(17204);
            runner.Address = new Uri(address);
            runner.Http = new HttpClient { BaseAddress = runner.Address, Timeout = TimeSpan.FromSeconds(20) };
            return runner;
        }
        catch
        {
            await runner.DisposeAsync();
            throw;
        }
    }

    public async Task CrashAsync()
    {
        if (!_process.HasExited) _process.Kill();
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    public async ValueTask DisposeAsync()
    {
        await CrashAsync();
        Http?.Dispose();
        _process.Dispose();
        await File.WriteAllLinesAsync(_evidence, _output);
    }
}
