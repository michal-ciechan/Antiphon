using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Application;
using Antiphon.TestSupport;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Tests.Agents;

/// <summary>FakeGrok through the isolated runner, native PTY, screen and production ready adapter.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerGrokAdapterReadyTestsPty
{
    [Test]
    [Arguments("inbox")]
    [Arguments("modern")]
    public async Task C1011_windows_backends_reach_ready_and_complete_prompt(string backend)
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("CARD-1011 actual inbox/modern qualification requires Windows");
        var root = Path.Combine(Path.GetTempPath(), "c1011-backend-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"), ptyBackend: backend);
        await using var adapter = new RunnerGrokAdapter(client,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200, GrokReadyMinTotalWaitMs = 0,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await adapter.StartAsync(new AgentLaunchSpec("fakegrok", AgentKind.Grok,
            TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory),
            ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
            new Dictionary<string, string>
            {
                ["GROK_HOME"] = Path.Combine(root, "grok-home"),
                ["ANTIPHON_FAKE_GROK_LINUX_COMPOSER"] = "0",
            }, cwd, 120, 30, SessionId: sessionId), deadline.Token);
        (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
        var hostLog = await ReadHostLogAsync(client, sessionId, deadline.Token);
        if (backend == "inbox")
            hostLog.ShouldContain("pty backend: InboxConhost (requested 'inbox')");
        else
            hostLog.ShouldContain("pty backend: ModernConPty (requested 'modern')");
        var snapshot = await client.GetSnapshotAsync(sessionId, deadline.Token);
        GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
        snapshot.RenderedScreen.Split('\n')[25][4].ShouldBe('>');
        (await client.GetTranscriptAsync(sessionId, deadline.Token)).Entries
            .ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);
        var body = $"C1011 HEAD {Guid.NewGuid():N} TAIL";
        await adapter.SendPromptAsync(body, deadline.Token);
        var transcript = await WaitForPromptAsync(client, sessionId, deadline.Token);
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        prompts.ShouldHaveSingleItem();
        prompts[0].Text.ShouldBe(body);
        Console.WriteLine($"C1011 backend={backend} session={sessionId:D} evidence={root}");
    }

    [Test]
    [Explicit]
    [NotInParallel("Headed")]
    [Timeout(600_000)]
    public async Task C1011_real_fresh_worktree_trust(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Requires Windows and ANTIPHON_HEADED_TESTS=1 under an explicit S0 commission");
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
        File.Exists(exe).ShouldBeTrue("The commissioned real Grok CLI must be installed; missing setup is incomplete qualification");
        var root = Path.Combine(Path.GetTempPath(), "c1011-real-trust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cwd = Path.Combine(root, "worktree");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var source = (await GitAsync(DelegateScriptRunner.RepoRoot, ["rev-parse", "HEAD"], deadline.Token)).Trim();
        var version = (await RunAsync(exe, ["--version"], DelegateScriptRunner.RepoRoot, deadline.Token)).Trim();
        await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "add", "--detach", cwd, source], deadline.Token);
        var sessionId = Guid.NewGuid();
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"), ptyBackend: "modern");
        var observer = new TrustObserver(client);
        await using var adapter = new RunnerGrokAdapter(observer, Options.Create(new AgentRegistrySettings
        {
            GrokStartupCaptureDirectory = Path.Combine(root, "startup"),
        }));
        Process? ownedChild = null;
        var started = false;
        var releaseConfirmed = false;
        try
        {
            await adapter.StartAsync(new AgentLaunchSpec("grok", AgentKind.Grok, exe,
                ["--always-approve", "--no-alt-screen", "--model", "grok-4.7", "--session-id", sessionId.ToString("D")],
                new Dictionary<string, string>(), cwd, 120, 30, SessionId: sessionId), deadline.Token);
            started = true;
            adapter.Pid.ShouldNotBeNull();
            ownedChild = Process.GetProcessById(adapter.Pid.Value);
            (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
            observer.TrustBeforeFirstInput.ShouldBeTrue("a new path without observed trust does not qualify WQ-3");
            observer.StartupInputs.ShouldBe(new[] { "y" });
            var ready = await observer.GetSnapshotAsync(sessionId, deadline.Token);
            GrokStartupScreen.Classify(ready.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
            GrokTrustPromptDetector.IsVisibleOnScreen(ready.RenderedScreen).ShouldBeFalse();
            (await ReadHostLogAsync(client, sessionId, deadline.Token))
                .ShouldContain("pty backend: ModernConPty (requested 'modern')");
            observer.StartupComplete = true;
            var nonce = "C1011 TRUST " + Guid.NewGuid().ToString("N");
            var body = $"Reply exactly {nonce}. Do not use tools or change files.";
            await adapter.SendPromptAsync(body, deadline.Token);
            var transcript = await WaitForPromptAsync(client, sessionId, deadline.Token);
            var prompt = transcript.Entries.Single(x => x.Kind == TranscriptKinds.UserPrompt);
            prompt.Text.ShouldBe(body);
            do
            {
                transcript = await client.GetTranscriptAsync(sessionId, deadline.Token);
                if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.TurnEnd && x.Sequence > prompt.Sequence)) break;
                await Task.Delay(250, deadline.Token);
            } while (true);
            transcript.Entries.ShouldContain(x => x.Kind == TranscriptKinds.AssistantText
                && x.Sequence > prompt.Sequence && x.Text != null && x.Text.Contains(nonce));
            await File.WriteAllTextAsync(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new
            {
                source, version, sessionId, cwd, cols = 120, rows = 30,
                prompt.Text, prompt.Sequence, response = transcript.Entries.Where(x => x.Sequence > prompt.Sequence),
            }), deadline.Token);
        }
        finally
        {
            try
            {
                if (started)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.KillAsync(sessionId, cleanup.Token);
                    if (ownedChild is not null)
                    {
                        await ownedChild.WaitForExitAsync(cleanup.Token);
                        ownedChild.HasExited.ShouldBeTrue();
                        releaseConfirmed = true;
                    }
                }
            }
            finally
            {
                ownedChild?.Dispose();
                await File.WriteAllTextAsync(Path.Combine(root, "observations.json"), JsonSerializer.Serialize(new
                {
                    source, version, sessionId, observer.TrustBeforeFirstInput, observer.StartupInputs,
                    observer.Observations, releaseConfirmed,
                }), CancellationToken.None);
                // Remove only this exact owned worktree, after confirmed child exit. No force.
                if (!started || releaseConfirmed)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "remove", cwd], cleanup.Token);
                }
                Console.WriteLine($"C1011 WQ-3 session={sessionId:D} evidence={root} releaseConfirmed={releaseConfirmed}");
            }
        }
    }

    private static async Task<string> ReadHostLogAsync(DirectSessionRunnerClient client, Guid sessionId, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetDirectoryName(client.PtyHostManifestDir)!, "logs", sessionId.ToString("N") + ".log");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task<SessionRunnerTranscriptDto> WaitForPromptAsync(ISessionRunnerClient client, Guid sessionId, CancellationToken ct)
    {
        while (true)
        {
            var transcript = await client.GetTranscriptAsync(sessionId, ct);
            if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt)) return transcript;
            await Task.Delay(25, ct);
        }
    }

    private static Task<string> GitAsync(string cwd, string[] args, CancellationToken ct) => RunAsync("git", args, cwd, ct);
    private static async Task<string> RunAsync(string exe, string[] args, string cwd, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start owned qualification helper");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            throw;
        }
        var output = await stdout;
        await stderr; // Drain, but never export arbitrary CLI diagnostics or auth screens.
        process.ExitCode.ShouldBe(0, "The qualification helper must complete successfully");
        return output;
    }

    private sealed class TrustObserver(DirectSessionRunnerClient inner) : ISessionRunnerClient
    {
        public bool StartupComplete { get; set; }
        public bool TrustBeforeFirstInput { get; private set; }
        public List<string> StartupInputs { get; } = [];
        public List<object> Observations { get; } = [];
        public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            var snapshot = await inner.GetSnapshotAsync(id, ct);
            var reason = GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason;
            if (!StartupComplete)
            {
                if (reason == GrokStartupReason.Trust && StartupInputs.Count == 0) TrustBeforeFirstInput = true;
                // Metadata only: suppress sign-in contents and all unrelated screen text.
                if (Observations.Count < 4096) Observations.Add(new { snapshot.LastSequence, reason = reason.ToString(), at = DateTime.UtcNow });
            }
            return snapshot;
        }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            if (!StartupComplete) StartupInputs.Add(input);
            return inner.SendInputAsync(id, input, ct);
        }
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(id, spec, ct);
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => inner.GetBufferAsync(id, ct);
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => inner.GetTranscriptAsync(id, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => inner.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => inner.ResizeAsync(id, cols, rows, ct);
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => inner.KillAsync(id, ct);
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => inner.StreamEventsAsync(ct);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fake_dashboard_marker_reaches_ready_and_complete_first_prompt(bool linuxMarker)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1004-pty-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"),
            ptyBackend: OperatingSystem.IsWindows() ? "modern" : null);
        await using var adapter = new RunnerGrokAdapter(client,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200,
                GrokReadyMinTotalWaitMs = 0,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await adapter.StartAsync(new AgentLaunchSpec("fakegrok", AgentKind.Grok,
            TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory),
            ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
            new Dictionary<string, string>
            {
                ["GROK_HOME"] = Path.Combine(root, "grok-home"),
                ["ANTIPHON_FAKE_GROK_LINUX_COMPOSER"] = linuxMarker ? "1" : "0",
                // The .NET fake's Unix console can turn a typed CR into LF and
                // deliver it in the body's read. Use its existing transport opt-in.
                ["ANTIPHON_FAKE_LF_ENTER"] = OperatingSystem.IsWindows() ? "0" : "1",
            }, cwd, 120, 30, SessionId: sessionId), deadline.Token);
        (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
        var snapshot = await client.GetSnapshotAsync(sessionId, deadline.Token);
        snapshot.RenderedScreen.Split('\n')[25][4].ShouldBe(linuxMarker ? '\u276f' : '>');
        GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
        (await client.GetTranscriptAsync(sessionId, deadline.Token)).Entries
            .ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);

        // A single-line nonce avoids the .NET fake's unqualified Unix multi-line
        // console behavior. Real Grok paste/Enter qualification belongs to the canary.
        const string body = "C1004 complete first prompt HEAD and TAIL";
        await adapter.SendPromptAsync(body, deadline.Token);
        SessionRunnerTranscriptDto transcript;
        do
        {
            transcript = await client.GetTranscriptAsync(sessionId, deadline.Token);
            if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt)) break;
            await Task.Delay(25, deadline.Token);
        } while (true);
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        prompts.ShouldHaveSingleItem();
        prompts[0].Text.ShouldBe(body);
        // DirectSessionRunnerClient owns and kills every child on disposal. Retain its
        // unique log root for diagnosis; it never uses the production runner or provider.
    }
}
