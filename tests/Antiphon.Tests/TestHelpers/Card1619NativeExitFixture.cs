using Antiphon.PtyHost.Protocol;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Antiphon.TestSupport;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A fresh owned real Linux PTY lifetime, using the production detached host resolver.</summary>
internal sealed class Card1619NativeExitFixture : IAsyncDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-1));
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "card1619-s3a-" + Guid.NewGuid().ToString("N"));
    private SessionRunnerRuntime _runtime = null!;
    private Process? _child, _host;
    private string? _startTicks;
    public RunnerSessionDto Snapshot => _runtime.Get(Id);
    public PtyHostManifest LaunchManifest { get; private set; } = null!;

    public static async Task<(Card1619NativeExitFixture Fixture, string ExitJson)> CaptureAsync(int signal, int code = 0)
    {
        var fixture = new Card1619NativeExitFixture();
        try { return (fixture, await fixture.RunAsync(signal, code)); }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task<string> RunAsync(int signal, int code)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Directory.CreateDirectory(Root);
        _runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings {
            SessionLogPath = Root, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false
        }), NullLogger<SessionRunnerRuntime>.Instance);
        var events = _runtime.Subscribe(ct.Token);
        using (TestTiming.BeginPhase(TestPhaseKind.ProcessSpawn, "card1619-native-host-readiness"))
        {
            var dto = await _runtime.StartAsync(new RunnerLaunchRequest(Id, "/bin/sh",
                ["-c", $"printf 'card1619-ready\\n'; read line; exit {code}"],
                new Dictionary<string, string>(), Root, 80, 24, AcceptedStartedAt: Generation), ct.Token);
            _child = Process.GetProcessById(dto.Pid!.Value);
            _host = Process.GetProcessById(dto.HostPid!.Value);
            _startTicks = StartTicks(_child.Id);
            while (!_runtime.GetBuffer(Id).Buffer.Contains("card1619-ready")) await Task.Delay(10, ct.Token);
            dto.Status.ShouldBe("Running");
            LaunchManifest = PtyHostManifest.TryLoad(PtyHostManifest.PathFor(Path.Combine(Root, "pty-hosts", "manifests"), Id))!;
            LaunchManifest.ShouldNotBeNull("capture the real manifest before the runner acknowledges exit");
        }
        using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "card1619-native-signal-and-exit-envelope"))
        {
            _child.HasExited.ShouldBeFalse();
            StartTicks(_child.Id).ShouldBe(_startTicks);
            if (signal > 0) Kill(_child.Id, signal).ShouldBe(0);
            else await _runtime.SendInputAsync(Id, "finish\n", ct.Token);
            while (await events.WaitToReadAsync(ct.Token))
                while (events.TryRead(out var value))
                    if (value.EventName == SessionRunnerEventNames.SessionExited)
                    {
                        var exit = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(value.Json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                        exit.AcceptedStartedAt.ShouldBe(Generation);
                        exit.ExitSignal.ShouldBe(signal);
                        exit.ExitCode.ShouldBe(code);
                        return value.Json;
                    }
        }
        throw new InvalidOperationException("No real runner exit envelope");
    }

    public async ValueTask DisposeAsync()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "card1619-native-owned-join");
        if (_runtime is not null) await _runtime.DisposeAsync();
        foreach (var process in new[] { _child, _host })
        {
            if (process is null) continue;
            try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }

    private static string StartTicks(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19];
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
