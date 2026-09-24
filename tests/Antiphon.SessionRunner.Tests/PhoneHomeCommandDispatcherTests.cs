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
                new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid()),
                VerificationCustodyBackends.WindowsJob, Guid.NewGuid()))), CancellationToken.None);
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

    [Test]
    public async Task Concurrent_launches_at_capacity_admit_exactly_capacity()
    {
        const int capacity = 2;
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity, new GateProbe(capacity + 1));
        var launches = Enumerable.Range(0, capacity + 1)
            .Select(_ => dispatcher.DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(launches);
        results.Count(frame => frame.Kind == PhoneHomeFrameKind.Result).ShouldBe(capacity);
        results.Count(frame => frame.Kind == PhoneHomeFrameKind.Error
            && frame.ErrorCode == PhoneHomeProblemTypes.Capacity).ShouldBe(1);
        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(capacity);
        runtime.Owned.ShouldBe(capacity);
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

    // CARD-0604 D-19 / G-37 (Cut B). Cut A refused EVERY verification binding here, because the
    // runner had no containment at all. Cut B refuses on capability instead: a binding is admitted
    // only when this runner advertises a backend AND the binding names that exact backend and this
    // runner's own store. The three refusals below are the whole of that gate.
    [Test]
    public async Task Binding_without_custody_backend_is_refused()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        var frame = await dispatcher.DispatchAsync(
            Launch(TrackedRequest(VerificationCustodyBackends.LinuxCgroup, runtime.RunnerStoreId)), CancellationToken.None);

        frame.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        frame.ErrorDetail.ShouldContain("advertises no verification custody backend");
        runtime.Mutations.ShouldBeEmpty();
    }

    [Test]
    public async Task Foreign_backend_binding_is_refused_by_the_dispatcher()
    {
        var runtime = new RecordingRuntime { CustodyBackend = VerificationCustodyBackends.LinuxCgroup };
        var dispatcher = Dispatcher(runtime);

        var frame = await dispatcher.DispatchAsync(
            Launch(TrackedRequest(VerificationCustodyBackends.WindowsJob, runtime.RunnerStoreId)), CancellationToken.None);

        frame.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        frame.ErrorDetail.ShouldBe("verification_custody_invalid_binding");
        runtime.Mutations.ShouldBeEmpty();
    }

    [Test]
    public async Task Binding_with_foreign_store_is_refused()
    {
        var runtime = new RecordingRuntime { CustodyBackend = VerificationCustodyBackends.LinuxCgroup };
        var dispatcher = Dispatcher(runtime);

        var frame = await dispatcher.DispatchAsync(
            Launch(TrackedRequest(VerificationCustodyBackends.LinuxCgroup, Guid.NewGuid())), CancellationToken.None);

        frame.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        frame.ErrorCode.ShouldBe(PhoneHomeProblemTypes.StoreMismatch);
        runtime.Mutations.ShouldBeEmpty();
    }

    // The positive half: with an advertised backend, a matching binding and this runner's own
    // store, the launch reaches the runtime. Without this the three refusals above would pass
    // just as well if the gate refused everything.
    [Test]
    public async Task Matching_backend_and_store_admits_the_tracked_launch()
    {
        var runtime = new RecordingRuntime { CustodyBackend = VerificationCustodyBackends.LinuxCgroup };
        var dispatcher = Dispatcher(runtime);

        var frame = await dispatcher.DispatchAsync(
            Launch(TrackedRequest(VerificationCustodyBackends.LinuxCgroup, runtime.RunnerStoreId)), CancellationToken.None);

        frame.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        runtime.Mutations.ShouldContain("start");
    }

    private static RunnerLaunchRequest TrackedRequest(string backend, Guid storeId) =>
        new(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24,
            VerificationBinding: new VerificationExecutionBinding(
                Guid.NewGuid(),
                new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new VerificationSessionGeneration(Guid.NewGuid(), DateTime.UtcNow),
                new VerificationCreationCoordinates("/work/repos/antiphon", "/work/repos/antiphon/.git",
                    "/work", "/work/repos/antiphon/.git/worktrees/w", "feat/card-task-12345678", Guid.NewGuid()),
                backend, storeId));

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
    public async Task Claude_exe_is_only_the_bare_name_or_the_image_install_path()
    {
        // Review be0f8640: a POSIX path merely ending in "/claude" is not the image's binary.
        // The Dockerfile installs exactly /usr/local/bin/claude; nothing else may launch as Claude.
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var exe in new[] { "/opt/evil/claude", "/usr/local/bin/claude/../../tmp/claude", "./claude", "/tmp/claude" })
        {
            PhoneHomeCommandDispatcher.IsClaudeExe(exe).ShouldBeFalse(exe);
            var refused = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, exe + " is not the image's claude");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        }

        runtime.Mutations.ShouldBeEmpty();

        foreach (var exe in new[] { "claude", "/usr/local/bin/claude" })
        {
            PhoneHomeCommandDispatcher.IsClaudeExe(exe).ShouldBeTrue(exe);
            var admitted = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, exe + " is the image's claude and must be admitted");
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

        // A grok launch asks the Grok probe, never Claude's. Unknown still admits.
        var grokProbe = new RecordingProbe(null);
        var grok = await Dispatcher(new RecordingRuntime(), probe: grokProbe)
            .DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);
        grok.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        grokProbe.Calls.ShouldBe(["grok"]);
    }

    [Test]
    public async Task Grok_launch_is_refused_when_the_probe_says_logged_out()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(false);
        var refused = await Dispatcher(runtime, probe: probe)
            .DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);

        refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.ProviderSignInRequired);
        refused.StatusCode.ShouldBe(409);
        refused.ErrorDetail.ShouldNotBeNull();
        refused.ErrorDetail.ShouldContain("grok login");
        refused.ErrorDetail.ShouldContain("/state/grok");
        refused.ErrorDetail.ShouldNotContain("claude");
        probe.Calls.ShouldBe(["grok"]);
        runtime.Mutations.ShouldBeEmpty();

        var disabledProbe = new RecordingProbe(false);
        var admitted = await Dispatcher(runtime, probe: disabledProbe, grokAuthProbeEnabled: false)
            .DispatchAsync(Launch(Request("grok", "/work")), CancellationToken.None);
        admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        disabledProbe.Calls.ShouldBeEmpty();
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

        var grokAnswered = await dispatcher.DispatchAsync(ProviderAuth("grok"), CancellationToken.None);
        grokAnswered.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        var grokDto = grokAnswered.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json);
        grokDto.ShouldNotBeNull();
        grokDto.Provider.ShouldBe("grok");
        grokDto.LoggedIn.ShouldBe(true);
        probe.Calls.ShouldBe(["claude", "grok"]);

        var codex = await dispatcher.DispatchAsync(ProviderAuth("codex"), CancellationToken.None);
        codex.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        codex.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        codex.StatusCode.ShouldBe(400);
        probe.Calls.Count.ShouldBe(2);
        runtime.Mutations.ShouldBeEmpty();

        // The wire number follows CARD-0604 Cut B's custody operations (16-21); the server sends it by name.
        ((int)PhoneHomeOperation.ProviderAuth).ShouldBe(22);

        // A runner without a probe cannot tell, and says so rather than guessing.
        var unknown = await Dispatcher(runtime).DispatchAsync(ProviderAuth("claude"), CancellationToken.None);
        var unknownDto = unknown.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json)!;
        unknownDto.LoggedIn.ShouldBeNull();
        unknownDto.Error.ShouldBe("probe_unavailable");
    }

    // --- CARD-0631 D-2/D-5: every handler fault still answers the request. ---

    [Test]
    public async Task Dispatch_replies_with_error_frame_when_handler_throws_unexpected_exception()
    {
        var faults = new (PhoneHomeOperation Op, Exception Fault)[]
        {
            (PhoneHomeOperation.Get, new System.ComponentModel.Win32Exception(2, "No such file or directory")),
            (PhoneHomeOperation.Health, new IOException("disk went away")),
            (PhoneHomeOperation.Get, new UnauthorizedAccessException("access to /work/repos denied")),
            (PhoneHomeOperation.Health, new JsonException("bad json from a child")),
        };
        foreach (var (op, fault) in faults)
        {
            var runtime = new RecordingRuntime { Fault = () => fault };
            var request = SessionRequest(op, epoch: 7);
            var reply = await Dispatcher(runtime).DispatchAsync(request, CancellationToken.None);

            var what = fault.GetType().Name;
            reply.Kind.ShouldBe(PhoneHomeFrameKind.Error, what + " must be answered, not escape");
            reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RunnerInternalError, what);
            reply.StatusCode.ShouldBe(500, what);
            reply.Epoch.ShouldBe(request.Epoch, what);
            reply.RequestId.ShouldBe(request.RequestId, what);
            reply.Operation.ShouldBe(request.Operation, what);
            reply.ErrorDetail.ShouldNotBeNull();
            reply.ErrorDetail.ShouldStartWith(what + ": ");
            reply.ErrorDetail.ShouldContain(fault.Message);
            reply.ErrorDetail.ShouldNotContain("   at ");
            runtime.Mutations.ShouldBeEmpty();
        }

        // An oversized diagnostic is bounded under the default budget...
        var huge = new string('x', 200_000);
        var bounded = await Dispatcher(new RecordingRuntime { Fault = () => new IOException(huge) })
            .DispatchAsync(SessionRequest(PhoneHomeOperation.Get, epoch: 7), CancellationToken.None);
        bounded.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RunnerInternalError);
        bounded.ErrorDetail!.Length.ShouldBeLessThan(2048);
        bounded.ErrorDetail.ShouldStartWith("IOException: ");

        // ...and a tight message budget still yields a frame that fits, rather than one the
        // writer must refuse and the server never sees.
        const int budget = 600;
        var tightRequest = SessionRequest(PhoneHomeOperation.Get, epoch: 7);
        var tight = await Dispatcher(new RecordingRuntime { Fault = () => new IOException(huge) }, maxMessageUtf8Bytes: budget)
            .DispatchAsync(tightRequest, CancellationToken.None);
        tight.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        tight.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RunnerInternalError);
        tight.RequestId.ShouldBe(tightRequest.RequestId);
        tight.ErrorDetail!.ShouldStartWith("IOException");
        JsonSerializer.SerializeToUtf8Bytes(tight, PhoneHomeFraming.Json).Length.ShouldBeLessThanOrEqualTo(budget);
    }

    private static PhoneHomeFrame SessionRequest(PhoneHomeOperation operation, long epoch) =>
        new(PhoneHomeFrameKind.Request, epoch, Guid.NewGuid(), operation,
            JsonSerializer.SerializeToElement(new { sessionId = Guid.NewGuid() }, PhoneHomeFraming.Json));

    private static PhoneHomeFrame ProviderAuth(string provider) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.ProviderAuth,
            System.Text.Json.JsonSerializer.SerializeToElement(new PhoneHomeProviderAuthRequest(provider), PhoneHomeFraming.Json));

    /// <summary>
    /// Holds every probe until <paramref name="expected"/> launches are inside it, so they all
    /// pass any capacity check that runs before the mutation lock.
    /// </summary>
    private sealed class GateProbe(int expected) : IProviderAuthProbe
    {
        private int _entered;
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _entered) >= expected)
                _gate.TrySetResult();
            await _gate.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return new RunnerProviderAuthDto(provider, true, "test", null, DateTimeOffset.UtcNow, null);
        }
    }

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
        IPhoneHomeRuntimeSurface runtime, int capacity = 8, IProviderAuthProbe? probe = null, bool claudeAuthProbeEnabled = true,
        bool grokAuthProbeEnabled = true,
        int maxMessageUtf8Bytes = PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes) =>
        new(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            RunnerRepository = "/work/repos/antiphon",
            Capacity = capacity,
            ClaudeAuthProbeEnabled = claudeAuthProbeEnabled,
            GrokAuthProbeEnabled = grokAuthProbeEnabled,
            Limits = new PhoneHomeLimits(MaxMessageUtf8Bytes: maxMessageUtf8Bytes),
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

        /// <summary>CARD-0631: a runtime fault Health and Get throw, standing in for an escaping handler.</summary>
        public Func<Exception>? Fault { get; set; }
        public int OwnedSessionCount => Owned;

        /// <summary>Null (the default) is a runner that advertises no custody at all.</summary>
        public string? CustodyBackend { get; init; }
        public string? VerificationCustodyBackend => CustodyBackend;
        public Guid RunnerStoreId { get; } = Guid.NewGuid();

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: CustodyBackend);
        public string Health() => Fault is { } fault ? throw fault() : "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw (Fault?.Invoke() ?? new KeyNotFoundException());
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
