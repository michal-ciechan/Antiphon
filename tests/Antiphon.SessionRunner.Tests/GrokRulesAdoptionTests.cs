using System.Text;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesAdoptionTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Restart_recovers_only_a_verified_rules_receipt_and_retains_files_after_worktree_removal(bool herdr, bool corrupt)
    {
        if (OperatingSystem.IsLinux() && !herdr && corrupt)
            throw new SkipTestException("CARD-0871: Linux PtyHost adoption exits when a changed rules receipt is encountered.");
        // The owned child accepts the generated Grok argv and stays alive for adoption.
        var root = TestSessionLogRoot.Create("card0395-adoption");
        var cwd = Path.Combine(root, "disposable worktree");
        Directory.CreateDirectory(cwd);
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyBackend = "modern", PtyHostLingerHours = 0.02 };
        var id = Guid.NewGuid();
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
        fake.Start();
        await fake.WaitUntilListeningAsync();
        SessionRunnerRuntime Runtime() => new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
            herdr ? new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }) : null,
            herdr ? new AliveProbe() : new SystemProcessLivenessProbe());
        var payload = new GrokRulesPayload("early file-only sentinel\r\n" + new string('é', 8000) + "\ntail file-only sentinel", 1, Guid.NewGuid());
        var nativeExe = OperatingSystem.IsWindows() ? HerdrTestProcess.ShellPath : HerdrTestProcess.CreateOwnedUnixArgvChild(root);
        var argvCapture = Path.Combine(root, "native-argv");
        var request = new RunnerLaunchRequest(id, herdr ? "grok" : nativeExe,
            herdr ? [] : OperatingSystem.IsWindows() ? HerdrTestProcess.InteractiveArgs : [],
            new Dictionary<string,string> { ["GROK_HOME"] = Path.Combine(root, "isolated home"), ["ANTIPHON_TEST_ARGV"] = argvCapture },
            cwd, 120, 30, TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: payload,
            Backend: herdr ? SessionBackends.Herdr : null,
            Herdr: herdr ? new HerdrLaunchOptions("card0395-" + id.ToString("N"), "rules", cwd, "rules", AgentKind: HerdrAgentKinds.Grok) : null);
        var a = Runtime();
        SessionRunnerRuntime? b = null;
        RunnerSessionDto? started = null;
        try
        {
            started = await a.StartAsync(request, CancellationToken.None);
            started.Status.ShouldBe("Running");
            if (!herdr && !OperatingSystem.IsWindows())
            {
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (!File.Exists(argvCapture) && DateTime.UtcNow < until) await Task.Delay(20);
                File.Exists(argvCapture).ShouldBeTrue("owned Unix child must receive the native argv");
                File.ReadAllText(argvCapture).ShouldContain("--rules");
            }
            var receipt = started.GrokRulesReceipt.ShouldNotBeNull();
            (await File.ReadAllBytesAsync(receipt.Path)).ShouldBe(Encoding.UTF8.GetBytes(payload.Content));
            var metadata = herdr ? HerdrPaneSidecar.PathFor(settings.SessionLogPath, id) : PtyHostManifest.PathFor(settings.PtyHostManifestDir, id);
            File.ReadAllText(metadata).ShouldNotContain("file-only sentinel");
            await a.DisposeAsync();
            if (corrupt) await File.WriteAllTextAsync(receipt.Path, "wrong complete revision");
            b = Runtime();
            await b.AdoptOrphanedHostsAsync(herdr ? new AliveProbe() : new SystemProcessLivenessProbe(), CancellationToken.None);
            var recovered = b.Get(id);
            await Should.ThrowAsync<InvalidOperationException>(() => b.ExpireRulesArtifactAsync(id, CancellationToken.None));
            if (corrupt) recovered.GrokRulesReceipt.ShouldBeNull("a changed file must never be advertised as the committed receipt");
            else recovered.GrokRulesReceipt.ShouldBe(receipt, "metadata must survive a real runtime restart");
            recovered.Pid.ShouldBe(started.Pid);
            await TestSessionTeardown.KillAndAwaitHostExitAsync(b, id, recovered.HostPid);
            Directory.Delete(cwd, recursive: true);
            File.Exists(receipt.Path).ShouldBeTrue("worktree removal is not session artifact expiry");
            if (!corrupt) (await File.ReadAllBytesAsync(receipt.Path)).ShouldBe(Encoding.UTF8.GetBytes(payload.Content));
            var sibling = await new GrokRulesFileStore(settings.SessionLogPath, new()).WriteAsync(Guid.NewGuid(),
                new("another session's retained rules", 1, Guid.NewGuid()), CancellationToken.None);
            await b.ExpireRulesArtifactAsync(id, CancellationToken.None);
            Directory.Exists(Path.GetDirectoryName(receipt.Path)).ShouldBeFalse();
            File.Exists(sibling.Path).ShouldBeTrue();
        }
        finally
        {
            if (b is not null)
            {
                if (b.List().Any(s => s.SessionId == id && s.Status == "Running"))
                    await TestSessionTeardown.KillAndAwaitHostExitAsync(b, id, started?.HostPid);
                await b.DisposeAsync();
            }
            else if (started is not null) await TestSessionTeardown.KillAndAwaitHostExitAsync(a, id, started.HostPid);
            await a.DisposeAsync();
        }
    }

    private sealed class AliveProbe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => true;
        public string? TryGetProcessName(int pid) => "powershell";
        public DateTime? TryGetStartTimeUtc(int pid) => DateTime.UtcNow.AddMinutes(-1);
    }
}
