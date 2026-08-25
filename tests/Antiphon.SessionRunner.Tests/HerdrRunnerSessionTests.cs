using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0164 B1: herdr <c>LastSequence</c> advances via the runner's content-delta counter, never
/// by relying on herdr's own sticky <c>pane.revision</c>. Cases (i)–(v) from the design plan §9.
/// </summary>
[NotInParallel("HerdrRunnerSession")]
public class HerdrRunnerSessionTests
{
    [Test]
    public async Task Sticky_revision_plus_changed_read_text_advances_LastSequence_via_GetSnapshot_and_GetAsync()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();

        // Establish baseline (first read — no content bump yet).
        var baselineSnap = runtime.GetSnapshot(sessionId).LastSequence;
        var baselineGet = (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence;
        baselineGet.ShouldBe(baselineSnap);

        // Sticky revision stays 0; visible text changes → content counter bumps → LastSequence moves.
        fake.SetPaneRevision(paneId, 0);
        fake.SetPaneScreenText(paneId, "composer shows typed body CARD0164");

        var afterSnap = runtime.GetSnapshot(sessionId).LastSequence;
        afterSnap.ShouldBeGreaterThan(baselineSnap);

        // Reset text to a NEW value so GetAsync's pane.read also sees a delta.
        fake.SetPaneScreenText(paneId, "composer cleared — reply rendering");
        var afterGet = (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence;
        afterGet.ShouldBeGreaterThan(afterSnap);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    [Test]
    public async Task Sticky_revision_plus_identical_text_across_reads_does_not_advance()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();

        fake.SetPaneScreenText(paneId, "stable idle screen");
        var first = runtime.GetSnapshot(sessionId).LastSequence;

        for (var i = 0; i < 6; i++)
        {
            runtime.GetSnapshot(sessionId).LastSequence.ShouldBe(first);
            (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence.ShouldBe(first);
        }

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    [Test]
    public async Task Interleaved_GetSnapshot_and_GetAsync_never_fabricate_a_delta_and_counter_is_monotonic()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();

        fake.SetPaneScreenText(paneId, "same text for both paths");
        var seen = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            seen.Add(i % 2 == 0
                ? runtime.GetSnapshot(sessionId).LastSequence
                : (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence);
        }

        // Identical text → no fabricated advance from path interleaving.
        seen.Distinct().Count().ShouldBe(1);

        fake.SetPaneScreenText(paneId, "now the screen changed once");
        var afterChange = runtime.GetSnapshot(sessionId).LastSequence;
        afterChange.ShouldBeGreaterThan(seen[0]);

        // Further identical reads stay flat; counter never goes backwards.
        for (var i = 0; i < 4; i++)
        {
            var snap = runtime.GetSnapshot(sessionId).LastSequence;
            var get = (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence;
            snap.ShouldBe(afterChange);
            get.ShouldBe(afterChange);
        }

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    [Test]
    public async Task Revision_moving_without_text_change_still_advances_LastSequence()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();

        fake.SetPaneScreenText(paneId, "unchanged visible text");
        var baseline = (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence;

        // Revision fold kept: a future herdr that fixes revision can only add advances.
        fake.SetPaneRevision(paneId, baseline + 10);
        var after = (await runtime.GetAsync(sessionId, CancellationToken.None)).LastSequence;
        after.ShouldBeGreaterThanOrEqualTo(baseline + 10);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    [Test]
    public async Task Pty_sessions_are_untouched_LastSequence_still_the_output_event_counter()
    {
        // No HerdrClient registered — pty-host path must not call herdr at all.
        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance);

        var sessionId = Guid.NewGuid();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                cmd,
                ["/d", "/q", "/k", "@echo off & prompt $G"],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                Cols: 80,
                Rows: 24),
            CancellationToken.None);
        dto.Status.ShouldBe("Running");

        var before = runtime.Get(sessionId).LastSequence;
        await runtime.SendInputAsync(sessionId, "echo pty-card0164-marker\r", CancellationToken.None);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        long after = before;
        while (DateTime.UtcNow < deadline)
        {
            after = runtime.GetSnapshot(sessionId).LastSequence;
            if (after > before && runtime.GetSnapshot(sessionId).RawOutput.Contains("pty-card0164-marker"))
                break;
            await Task.Delay(50);
        }

        after.ShouldBeGreaterThan(before);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [Test]
    public async Task Grok_request_starts_GrokTranscriptTailer_with_deterministic_sidecar()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        var grokHome = Path.Combine(settings.SessionLogPath, "grok-home");
        Directory.CreateDirectory(grokHome);
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                "grok",
                ["--no-alt-screen", "--always-approve", "--session-id", sessionId.ToString("D")],
                new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                cwd,
                Cols: 120,
                Rows: 30,
                TranscriptEnabled: true,
                TranscriptFormat: TranscriptFormats.Grok,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0187-grok",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0187-grok",
                    AgentKind: HerdrAgentKinds.Grok)),
            CancellationToken.None);
        dto.Status.ShouldBe("Running");

        var sidecar = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(settings.SessionLogPath, sessionId));
        sidecar.ShouldNotBeNull();
        sidecar!.Format.ShouldBe(TranscriptFormats.Grok);
        sidecar.How.ShouldBe(TranscriptBindMethods.Deterministic);
        sidecar.TranscriptPath.ShouldBe(
            GrokTranscriptTailer.ResolveUpdatesPath(
                new Dictionary<string, string> { ["GROK_HOME"] = grokHome }, cwd, sessionId));

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        TryDeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Codex_request_starts_CodexTranscriptTailer()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Codex;
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = BuildSettings();
        var sessionsRoot = Path.Combine(settings.SessionLogPath, "codex-home", "sessions");
        Directory.CreateDirectory(sessionsRoot);
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                "codex.cmd",
                ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
                new Dictionary<string, string> { ["CODEX_HOME"] = Path.Combine(settings.SessionLogPath, "codex-home") },
                cwd,
                Cols: 120,
                Rows: 30,
                TranscriptEnabled: true,
                TranscriptFormat: TranscriptFormats.Codex,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0187-codex",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0187-codex",
                    AgentKind: HerdrAgentKinds.Codex)),
            CancellationToken.None);
        dto.Status.ShouldBe("Running");

        var sidecar = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(settings.SessionLogPath, sessionId));
        sidecar.ShouldNotBeNull();
        sidecar!.Format.ShouldBe(TranscriptFormats.Codex);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        TryDeleteLogRoot(settings.SessionLogPath);
    }

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-runner-tests-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static async Task StartHerdrSessionAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string cwd)
    {
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                "claude",
                ["--dangerously-skip-permissions"],
                new Dictionary<string, string>(),
                cwd,
                Cols: 120,
                Rows: 30,
                TranscriptEnabled: false,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0164-herdr-runner-test",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0164-test")),
            CancellationToken.None);
        dto.Status.ShouldBe("Running");
    }

    private static void TryDeleteLogRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }
}
