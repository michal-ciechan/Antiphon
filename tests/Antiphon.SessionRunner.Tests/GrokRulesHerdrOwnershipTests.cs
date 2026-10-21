using System.Text;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), NotInParallel("SessionLiveness"), ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesHerdrOwnershipTests
{
    [Test, Arguments(false), Arguments(true)]
    public async Task A_live_native_target_cannot_receive_a_replacement_file_or_new_receipt(bool named)
    {
        var root = TestSessionLogRoot.Create("card0395-live-target"); Directory.CreateDirectory(root);
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok }; fake.Start(); await fake.WaitUntilListeningAsync();
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        var probe = new Probe();
        SessionRunnerRuntime Runtime() => new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }), probe);
        var id = Guid.NewGuid();
        var request = new RunnerLaunchRequest(id, "grok", ["--session-id", id.ToString("D")], new Dictionary<string,string> { ["GROK_HOME"] = Path.Combine(root, "home") }, root, 120, 30,
            TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: new("original owned rules\n", 1, Guid.NewGuid()), Backend: SessionBackends.Herdr,
            Herdr: new HerdrLaunchOptions("card0395-" + id.ToString("N"), "rules", root, "rules", AgentKind: HerdrAgentKinds.Grok, TabLabel: named ? "rules target" : null));
        var first = Runtime(); await using var second = Runtime();
        try
        {
            var original = await first.StartAsync(request, CancellationToken.None); await first.DisposeAsync();
            var receipt = original.GrokRulesReceipt!; var path = HerdrPaneSidecar.PathFor(root, id); var metadata = File.ReadAllBytes(path);
            var before = fake.Requests.Count;
            var failure = await Should.ThrowAsync<GrokRulesTransportException>(() => second.StartAsync(request with { GrokRulesPayload = new("new forbidden revision\n", 1, Guid.NewGuid()) }, CancellationToken.None));
            failure.Code.ShouldBe("grok_rules_receipt_invalid");
            File.ReadAllBytes(receipt.Path).ShouldBe(Encoding.UTF8.GetBytes(request.GrokRulesPayload!.Content));
            File.ReadAllBytes(path).ShouldBe(metadata);
            fake.Requests.Skip(before).Select(r => r.GetProperty("method").GetString()).ShouldNotContain(m =>
                m is "pane.send_text" or "pane.send_keys" or "pane.close" or "pane.rename" or "workspace.create" or "tab.create");
            second.List().ShouldBeEmpty();
        }
        finally { await first.DisposeAsync(); }
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task Crash_after_typing_before_pid_commit_recovers_only_exact_native_identity(bool foreign)
    {
        var root = TestSessionLogRoot.Create("card0395-pending-target"); Directory.CreateDirectory(root);
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok }; fake.Start(); await fake.WaitUntilListeningAsync();
        var settings = new SessionRunnerSettings { SessionLogPath = root }; var probe = new Probe();
        SessionRunnerRuntime Runtime() => new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }), probe);
        var id = Guid.NewGuid(); var first = Runtime(); await using var second = Runtime();
        var request = new RunnerLaunchRequest(id, "grok", ["--session-id", id.ToString("D")], new Dictionary<string,string> { ["GROK_HOME"] = Path.Combine(root, "home") }, root, 120, 30,
            TranscriptEnabled: true, TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: new("rules to retain\n", 1, Guid.NewGuid()), Backend: SessionBackends.Herdr,
            Herdr: new HerdrLaunchOptions("card0395-" + id.ToString("N"), "rules", root, "rules", AgentKind: HerdrAgentKinds.Grok));
        try
        {
            var started = await first.StartAsync(request, CancellationToken.None); await first.DisposeAsync();
            var path = HerdrPaneSidecar.PathFor(root, id); var sidecar = HerdrPaneSidecar.TryLoad(path)!;
            (sidecar with { ChildPid = null, LaunchPending = true, LaunchedAtUtc = probe.Epoch.AddSeconds(-1) }).SaveAtomic(path);
            fake.SetPaneProcessInfo(sidecar.PaneId, sidecar.ShellPid,
                new[] { (started.Pid!.Value, "grok", new[] { "grok", "--session-id", (foreign ? Guid.NewGuid() : id).ToString("D") }, (string?)root) });
            await second.AdoptOrphanedHostsAsync(probe, CancellationToken.None);
            var recovered = second.Get(id);
            if (foreign)
            {
                recovered.Status.ShouldBe("Starting");
                recovered.Pending.ShouldBe(HerdrPendingReasons.Unreachable);
                recovered.GrokRulesReceipt.ShouldBe(started.GrokRulesReceipt);
                var killed = await second.KillAsync(id, TimeSpan.FromSeconds(1), CancellationToken.None);
                killed.Status.ShouldBe("Starting", "unverified child cleanup must never be reported complete");
                HerdrPaneSidecar.TryLoad(path).ShouldNotBeNull();
            }
            else
            {
                recovered.Status.ShouldBe("Running"); recovered.Pid.ShouldBe(started.Pid); recovered.GrokRulesReceipt.ShouldBe(started.GrokRulesReceipt);
                HerdrPaneSidecar.TryLoad(path)!.LaunchPending.ShouldBeFalse();
                await second.KillAsync(id, TimeSpan.FromSeconds(2), CancellationToken.None);
            }
        }
        finally { await first.DisposeAsync(); }
    }
    private sealed class Probe : IProcessLivenessProbe
    {
        internal DateTime Epoch { get; } = DateTime.UtcNow;
        public bool IsAlive(int pid, DateTime start) => true;
        public string? TryGetProcessName(int pid) => "powershell";
        public DateTime? TryGetStartTimeUtc(int pid) => Epoch;
    }
}
