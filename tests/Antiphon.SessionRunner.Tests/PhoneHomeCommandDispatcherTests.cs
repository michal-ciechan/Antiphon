using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class PhoneHomeCommandDispatcherTests
{
    [Test]
    public async Task Unsupported_operation_or_launch_never_enters_runtime()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 1,
        });

        var herdr = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24, Backend: SessionBackends.Herdr)), CancellationToken.None);
        herdr.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        runtime.Mutations.ShouldBeEmpty();
        var runtimeMutations = runtime.Mutations;

        var unknown = await dispatcher.DispatchAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), (PhoneHomeOperation)999), CancellationToken.None);
        unknown.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        var custody = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24,
            VerificationBinding: new VerificationExecutionBinding(
                Guid.NewGuid(),
                new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), "deadbeef"),
                new VerificationSessionGeneration(Guid.NewGuid(), DateTime.UtcNow),
                new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid())))), CancellationToken.None);
        custody.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        var wrongCwd = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "C:\\Windows", 80, 24)), CancellationToken.None);
        wrongCwd.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        var emptyExe = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "", [], new Dictionary<string, string>(), "/work", 80, 24)), CancellationToken.None);
        emptyExe.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        runtimeMutations.ShouldBeEmpty();
        runtime.Capabilities().VerificationCustodyBackend.ShouldBeNull();
    }

    [Test]
    public async Task Capacity_counts_adopted_and_concurrent_launches()
    {
        var runtime = new RecordingRuntime { Owned = 1 };
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 1,
        });
        var result = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24)), CancellationToken.None);
        result.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        result.ErrorCode.ShouldBe(PhoneHomeProblemTypes.Capacity);
        var maxOwnedSessions = runtime.OwnedSessionCount;
        maxOwnedSessions.ShouldBe(1);
    }

    // --- CARD-0604 D-2/D-15: the runner's own admission of the widened shapes. ---

    [Test]
    public async Task Raw_exe_outside_allow_list_is_refused()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var exe in new[] { "/bin/sh", "/bin/bash", "/usr/local/bin/pwsh", "grok" })
        {
            var admitted = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, exe + " is image-owned and must be admitted");
        }

        // A host path that merely ends in an allow-listed name is not the image's own executable,
        // and neither is anything else the server might name.
        foreach (var exe in new[] { "/usr/bin/sh", "/opt/evil/bash", "/bin/sh ", "sh", "/usr/local/bin/node" })
        {
            var refused = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, exe + " is not image-owned");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        }
    }

    [Test]
    public async Task Cwd_outside_workspace_is_refused()
    {
        var dispatcher = Dispatcher(new RecordingRuntime());

        // The workspace root itself, and a single-segment mirror directly under its worktrees/.
        foreach (var cwd in new[] { "/work", "/work/worktrees/task-deadbeef" })
        {
            var admitted = await dispatcher.DispatchAsync(Launch(Request("grok", cwd)), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, cwd + " is inside the runner workspace");
        }

        // Traversal, nesting, a sibling that merely shares the prefix, and the root of nothing.
        foreach (var cwd in new[]
                 {
                     "/work/worktrees", "/work/worktrees/", "/work/worktrees/..",
                     "/work/worktrees/task-deadbeef/src", "/work/worktrees/../../etc",
                     "/workspace", "/work-other", "/", "", "C:\\src\\Antiphon",
                 })
        {
            var refused = await dispatcher.DispatchAsync(Launch(Request("grok", cwd)), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, cwd + " is outside the runner workspace");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        }
    }

    [Test]
    public async Task Capacity_is_the_configured_value()
    {
        // CARD-0604 D-14: the seat count is configuration, not the constant 1 CARD-0490 pinned.
        var runtime = new RecordingRuntime { Owned = 1 };
        var dispatcher = Dispatcher(runtime, capacity: 2);
        var admitted = await dispatcher.DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);
        admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result);

        var full = await dispatcher.DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);
        full.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        full.ErrorCode.ShouldBe(PhoneHomeProblemTypes.Capacity);
    }

    [Test]
    public async Task Workspace_ops_are_admitted_only_under_allowed_cwd()
    {
        var dispatcher = Dispatcher(new RecordingRuntime());
        var removeOutside = await dispatcher.DispatchAsync(
            new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceRemove,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new PhoneHomeWorkspaceRemoveRequest("/etc"), PhoneHomeFraming.Json)),
            CancellationToken.None);
        removeOutside.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        removeOutside.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);

        // A mirror name outside task-<8 hex> never becomes a directory the runner creates.
        var badName = await dispatcher.DispatchAsync(
            new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceMirror,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new PhoneHomeWorkspaceMirrorRequest("main", new string('a', 40), "../escape"), PhoneHomeFraming.Json)),
            CancellationToken.None);
        badName.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        badName.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
    }

    // --- CARD-0628 D-5/D-7: Claude on the runner. ---

    [Test]
    public async Task Claude_exe_is_image_owned()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var exe in new[] { "claude", "/usr/local/bin/claude" })
        {
            var admitted = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, exe + " is the image's claude and must be admitted");
        }

        runtime.Mutations.Count.ShouldBe(2);

        // A trailing space, a Windows binary name, and a Windows path that merely ends in "claude".
        foreach (var exe in new[] { "/opt/evil/claude ", "claude.exe", "C:\\tools\\claude", "Claude", "claude-code" })
        {
            var refused = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, exe + " is not the image's claude");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        }

        runtime.Mutations.Count.ShouldBe(2);
    }

    [Test]
    public async Task Transcript_format_null_grok_and_claude_admitted_codex_refused()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var format in new string?[] { null, TranscriptFormats.Grok, TranscriptFormats.Claude })
        {
            var admitted = await dispatcher.DispatchAsync(
                Launch(Request("claude", "/work") with { TranscriptFormat = format }), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, (format ?? "null") + " must be admitted");
        }

        foreach (var format in new[] { "codex", "opencode", "" })
        {
            var refused = await dispatcher.DispatchAsync(
                Launch(Request("claude", "/work") with { TranscriptFormat = format }), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, format + " has no tailer on this runner");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
            refused.ErrorDetail.ShouldBe("Only the Grok and Claude transcript formats are admitted.");
        }

        runtime.Mutations.Count.ShouldBe(3);
    }

    [Test]
    public async Task Claude_launch_is_refused_when_the_probe_says_logged_out()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(false);
        var dispatcher = Dispatcher(runtime, probe: probe);

        var refused = await dispatcher.DispatchAsync(Launch(Request("claude", "/work")), CancellationToken.None);

        refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.ProviderSignInRequired);
        refused.ErrorCode.ShouldBe("provider_sign_in_required");
        refused.StatusCode.ShouldBe(409);
        refused.ErrorDetail.ShouldNotBeNull();
        refused.ErrorDetail.ShouldContain("CLAUDE_CODE_OAUTH_TOKEN");
        refused.ErrorDetail.ShouldContain("claude auth login");
        refused.ErrorDetail.ShouldContain("/state/claude");
        probe.Calls.ShouldBe(["claude"]);
        runtime.Mutations.ShouldBeEmpty();

        // The setting is the kill switch: off, the same signed-out answer is never asked for.
        var disabledProbe = new RecordingProbe(false);
        var disabled = Dispatcher(runtime, probe: disabledProbe, claudeAuthProbeEnabled: false);
        var admitted = await disabled.DispatchAsync(Launch(Request("claude", "/work")), CancellationToken.None);
        admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        disabledProbe.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Claude_launch_is_admitted_when_the_probe_is_unknown_or_absent()
    {
        foreach (var loggedIn in new bool?[] { null, true })
        {
            var runtime = new RecordingRuntime();
            var probe = new RecordingProbe(loggedIn);
            var admitted = await Dispatcher(runtime, probe: probe)
                .DispatchAsync(Launch(Request("claude", "/work")), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"loggedIn={loggedIn?.ToString() ?? "null"} must admit");
            probe.Calls.ShouldBe(["claude"]);
            runtime.Mutations.ShouldBe(["start"]);
        }

        var noProbe = await Dispatcher(new RecordingRuntime())
            .DispatchAsync(Launch(Request("claude", "/work")), CancellationToken.None);
        noProbe.Kind.ShouldBe(PhoneHomeFrameKind.Result);

        // A grok launch never asks about Claude, even on a signed-out runner.
        var grokProbe = new RecordingProbe(false);
        var grok = await Dispatcher(new RecordingRuntime(), probe: grokProbe)
            .DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);
        grok.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        grokProbe.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Provider_auth_operation_answers_the_probe_and_refuses_unknown_providers()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(true, authMethod: "claude.ai", subscriptionType: "max");
        var dispatcher = Dispatcher(runtime, probe: probe);

        var answered = await dispatcher.DispatchAsync(ProviderAuth("claude"), CancellationToken.None);
        answered.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        answered.Operation.ShouldBe(PhoneHomeOperation.ProviderAuth);
        var dto = answered.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json);
        dto.ShouldNotBeNull();
        dto.Provider.ShouldBe("claude");
        dto.LoggedIn.ShouldBe(true);
        dto.AuthMethod.ShouldBe("claude.ai");
        dto.SubscriptionType.ShouldBe("max");
        probe.Calls.ShouldBe(["claude"]);

        var codex = await dispatcher.DispatchAsync(ProviderAuth("codex"), CancellationToken.None);
        codex.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        codex.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        codex.StatusCode.ShouldBe(400);
        probe.Calls.Count.ShouldBe(1);
        runtime.Mutations.ShouldBeEmpty();

        // The wire number the server already sends (CARD-0628 Round A) is the named operation.
        ((int)PhoneHomeOperation.ProviderAuth).ShouldBe(16);

        // A runner without a probe cannot tell, and says so rather than guessing.
        var unknown = await Dispatcher(runtime).DispatchAsync(ProviderAuth("claude"), CancellationToken.None);
        var unknownDto = unknown.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json)!;
        unknownDto.LoggedIn.ShouldBeNull();
        unknownDto.Error.ShouldBe("probe_unavailable");
    }

    private static PhoneHomeFrame ProviderAuth(string provider) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.ProviderAuth,
            System.Text.Json.JsonSerializer.SerializeToElement(new PhoneHomeProviderAuthRequest(provider), PhoneHomeFraming.Json));

    private sealed class RecordingProbe(bool? loggedIn, string? authMethod = null, string? subscriptionType = null)
        : IProviderAuthProbe
    {
        public List<string> Calls { get; } = [];

        public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
        {
            Calls.Add(provider);
            return Task.FromResult(new RunnerProviderAuthDto(
                provider, loggedIn, authMethod, subscriptionType, DateTimeOffset.UtcNow, loggedIn is null ? "timeout" : null));
        }
    }

    private static PhoneHomeCommandDispatcher Dispatcher(
        IPhoneHomeRuntimeSurface runtime, int capacity = 8, IProviderAuthProbe? probe = null, bool claudeAuthProbeEnabled = true) =>
        new(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            RunnerRepository = "/work/repos/antiphon",
            Capacity = capacity,
            ClaudeAuthProbeEnabled = claudeAuthProbeEnabled,
        }, probe);

    private static RunnerLaunchRequest Request(string exe, string cwd) =>
        new(Guid.NewGuid(), exe, [], new Dictionary<string, string>(), cwd, 80, 24);

    private static PhoneHomeFrame Launch(RunnerLaunchRequest request) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
            System.Text.Json.JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json));

    private sealed class RecordingRuntime : IPhoneHomeRuntimeSurface
    {
        public int Owned { get; set; }
        public List<string> Mutations { get; } = [];
        public int OwnedSessionCount => Owned;
        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            Mutations.Add("start");
            Owned++;
            return Task.FromResult(new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0));
        }
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Mutations.Add("input");
            return Task.CompletedTask;
        }
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }
}
