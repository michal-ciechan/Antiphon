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
    public async Task Grok_exe_is_only_the_bare_name_or_the_image_install_path()
    {
        // CARD-0640. Same contract as Claude: the Dockerfile installs exactly /usr/local/bin/grok.
        // A path that merely ends in "/grok" is not this image's binary.
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var exe in new[]
                 {
                     "/opt/evil/grok",
                     "/usr/local/bin/grok/../../tmp/grok",
                     "./grok",
                     "Grok",
                     "grok.exe",
                 })
        {
            PhoneHomeCommandDispatcher.IsGrokExe(exe).ShouldBeFalse(exe);
            var refused = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, exe + " is not the image's grok");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        }

        runtime.Mutations.ShouldBeEmpty();

        foreach (var exe in new[] { "grok", "/usr/local/bin/grok" })
        {
            PhoneHomeCommandDispatcher.IsGrokExe(exe).ShouldBeTrue(exe);
            var admitted = await dispatcher.DispatchAsync(Launch(Request(exe, "/work")), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, exe + " is the image's grok and must be admitted");
        }

        runtime.Mutations.Count.ShouldBe(2);
    }

    [Test]
    public async Task Transcript_format_null_grok_claude_and_codex_admitted_others_refused()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var format in new string?[] { null, TranscriptFormats.Grok, TranscriptFormats.Claude, TranscriptFormats.Codex })
        {
            var admitted = await dispatcher.DispatchAsync(
                Launch(Request("claude", "/work") with { TranscriptFormat = format }), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, (format ?? "null") + " must be admitted");
        }

        foreach (var format in new[] { "opencode", "Codex-rollout", "" })
        {
            var refused = await dispatcher.DispatchAsync(
                Launch(Request("claude", "/work") with { TranscriptFormat = format }), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, format + " has no tailer on this runner");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
            refused.ErrorDetail.ShouldBe("Only the Grok, Claude and Codex transcript formats are admitted.");
        }

        runtime.Mutations.Count.ShouldBe(4);
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

        var opencode = await dispatcher.DispatchAsync(ProviderAuth("opencode"), CancellationToken.None);
        opencode.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        opencode.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        opencode.StatusCode.ShouldBe(400);
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

    // --- CARD-0660 D-7/D-9: Codex on the runner (V-3). ---

    [Test]
    public async Task Codex_exe_is_only_the_bare_name_or_the_image_install_path()
    {
        // The Dockerfile links exactly /usr/local/bin/codex. The desktop's codex.cmd, another
        // directory's codex, a relative or traversing path and a case variant are not the image's.
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime);

        foreach (var exe in new[]
                 {
                     "codex.cmd", "codex.exe", "./codex", "Codex", "codex ", "/usr/bin/codex", "/opt/evil/codex",
                     "/usr/local/bin/codex/../../tmp/codex", "/usr/local/bin/../bin/codex", "/usr/local/bin//codex",
                     "C:\\Users\\me\\AppData\\Roaming\\npm\\codex.cmd", "/opt/codex/0.156.1/package/bin/codex.js",
                 })
        {
            PhoneHomeCommandDispatcher.IsCodexExe(exe).ShouldBeFalse(exe);
            var refused = await dispatcher.DispatchAsync(
                Launch(Request(exe, "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, exe + " is not the image's codex");
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget, exe);
        }

        runtime.Mutations.ShouldBeEmpty();

        foreach (var exe in new[] { "codex", "/usr/local/bin/codex" })
        {
            PhoneHomeCommandDispatcher.IsCodexExe(exe).ShouldBeTrue(exe);
            var admitted = await dispatcher.DispatchAsync(
                Launch(Request(exe, "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, exe + " is the image's codex and must be admitted: " + admitted.ErrorDetail);
        }

        runtime.Started.Select(started => started.Exe).ShouldBe(
            ["/usr/local/bin/codex", "/usr/local/bin/codex"], "a bare codex is launched as the image's own path");
    }

    [Test]
    public async Task Bare_codex_launches_the_image_path_never_a_competing_codex_earlier_on_path()
    {
        // Review of ff170389: the bare name used to reach the runtime unchanged, so which codex
        // ran depended on PATH lookup in the child's environment. A real, executable codex placed
        // first on the launch's PATH must never be the one launched.
        var competing = Path.Combine(Path.GetTempPath(), "c660-competing-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(competing);
        try
        {
            var marker = Path.Combine(competing, "ran");
            foreach (var name in new[] { "codex", "codex.cmd", "codex.exe" })
            {
                var fake = Path.Combine(competing, name);
                File.WriteAllText(fake, $"#!/bin/sh\necho ran > '{marker}'\n");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var path = competing + Path.PathSeparator + "/usr/local/bin" + Path.PathSeparator + "/usr/bin";
            var runtime = new RecordingRuntime();
            var request = Request("codex", "/work") with
            {
                Env = new Dictionary<string, string> { ["PATH"] = path, ["CODEX_HOME"] = "/state/codex" },
                TranscriptFormat = TranscriptFormats.Codex,
            };

            var reply = await Dispatcher(runtime, probe: new RecordingProbe(true)).DispatchAsync(Launch(request), CancellationToken.None);

            reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, reply.ErrorDetail);
            var started = runtime.Started.ShouldHaveSingleItem();
            started.Exe.ShouldBe(PhoneHomeCommandDispatcher.ImageCodexPath, "the fixed image path, never a PATH lookup");
            started.Exe.ShouldNotStartWith(competing);
            started.Env.ShouldBe(request.Env, "the child's environment is not rewritten");
            started.Args.ShouldBe(request.Args);
            File.Exists(marker).ShouldBeFalse("the competing codex never ran");
        }
        finally
        {
            try { Directory.Delete(competing, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public async Task Codex_launch_reaches_the_runtime_with_the_codex_transcript_and_unchanged_launch()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(true);
        var dispatcher = Dispatcher(runtime, probe: probe);
        string[] args =
        [
            "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox", "-m", "gpt-5.5",
            "-c", "model_reasoning_effort=\"high\"", "-c", "disable_paste_burst=true",
            "-c", "developer_instructions=\"line one\nline two \\\"quoted\\\" \u00e9\"",
        ];
        var request = Request("/usr/local/bin/codex", "/work/worktrees/task-c660") with
        {
            Args = args,
            Env = new Dictionary<string, string> { ["CODEX_HOME"] = "/state/codex" },
            TranscriptEnabled = true,
            TranscriptFormat = TranscriptFormats.Codex,
            Backend = SessionBackends.PtyHost,
        };

        var reply = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);

        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, reply.ErrorDetail);
        probe.Calls.ShouldBe(["codex"], "a Codex launch asks the Codex probe only");
        var started = runtime.Started.ShouldHaveSingleItem();
        started.SessionId.ShouldBe(request.SessionId);
        started.Exe.ShouldBe("/usr/local/bin/codex");
        started.TranscriptFormat.ShouldBe(TranscriptFormats.Codex);
        started.TranscriptEnabled.ShouldBeTrue();
        started.Cwd.ShouldBe("/work/worktrees/task-c660");
        started.Args.ShouldBe(args, "argv reaches the runtime byte-for-byte");
        started.Env.ShouldBe(request.Env);
        started.MemoryLimitMb.ShouldBe(0);
    }

    [Test]
    public async Task Codex_launch_is_refused_before_the_runtime_when_the_probe_says_logged_out()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(false);
        var refused = await Dispatcher(runtime, probe: probe)
            .DispatchAsync(Launch(Request("codex", "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);

        refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.ProviderSignInRequired);
        refused.StatusCode.ShouldBe(409);
        refused.ErrorDetail.ShouldNotBeNull();
        refused.ErrorDetail.ShouldContain("Codex");
        refused.ErrorDetail.ShouldContain("CODEX_HOME=/state/codex");
        refused.ErrorDetail.ShouldContain("codex login --device-auth");
        refused.ErrorDetail.ShouldContain("uid 1654");
        refused.ErrorDetail.ShouldNotContain("grok login");
        refused.ErrorDetail.ShouldNotContain("claude");
        probe.Calls.ShouldBe(["codex"]);
        runtime.Mutations.ShouldBeEmpty();
    }

    [Test]
    public async Task Codex_launch_is_admitted_when_the_probe_is_signed_in_unknown_absent_or_disabled()
    {
        foreach (var loggedIn in new bool?[] { true, null })
        {
            var runtime = new RecordingRuntime();
            var probe = new RecordingProbe(loggedIn);
            var admitted = await Dispatcher(runtime, probe: probe)
                .DispatchAsync(Launch(Request("codex", "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"loggedIn={loggedIn?.ToString() ?? "null"} must admit: {admitted.ErrorDetail}");
            probe.Calls.ShouldBe(["codex"]);
            runtime.Mutations.ShouldBe(["start"]);
        }

        var noProbeRuntime = new RecordingRuntime();
        var noProbe = await Dispatcher(noProbeRuntime)
            .DispatchAsync(Launch(Request("codex", "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);
        noProbe.Kind.ShouldBe(PhoneHomeFrameKind.Result, noProbe.ErrorDetail);
        noProbeRuntime.Mutations.ShouldBe(["start"]);

        // The setting is the kill switch: off, the same signed-out answer is never asked for.
        var disabledRuntime = new RecordingRuntime();
        var disabledProbe = new RecordingProbe(false);
        var disabled = await Dispatcher(disabledRuntime, probe: disabledProbe, codexAuthProbeEnabled: false)
            .DispatchAsync(Launch(Request("codex", "/work") with { TranscriptFormat = TranscriptFormats.Codex }), CancellationToken.None);
        disabled.Kind.ShouldBe(PhoneHomeFrameKind.Result, disabled.ErrorDetail);
        disabledProbe.Calls.ShouldBeEmpty();
        disabledRuntime.Mutations.ShouldBe(["start"]);

        // Codex's switch is its own: turning Grok's and Claude's off does not skip Codex's probe.
        var otherSwitchesProbe = new RecordingProbe(false);
        var otherSwitches = await Dispatcher(new RecordingRuntime(), probe: otherSwitchesProbe,
                claudeAuthProbeEnabled: false, grokAuthProbeEnabled: false)
            .DispatchAsync(Launch(Request("codex", "/work")), CancellationToken.None);
        otherSwitches.ErrorCode.ShouldBe(PhoneHomeProblemTypes.ProviderSignInRequired);
        otherSwitchesProbe.Calls.ShouldBe(["codex"]);
    }

    [Test]
    public async Task Codex_admission_keeps_cwd_backend_memory_custody_and_capacity_checks()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 1);
        RunnerLaunchRequest Codex(string cwd = "/work") => Request("codex", cwd) with { TranscriptFormat = TranscriptFormats.Codex };

        foreach (var (label, request) in new (string, RunnerLaunchRequest)[]
                 {
                     ("windows cwd", Codex("C:\\src\\Antiphon")),
                     ("traversing cwd", Codex("/work/worktrees/../../etc")),
                     ("herdr backend", Codex() with { Backend = SessionBackends.Herdr }),
                     ("memory limit", Codex() with { MemoryLimitMb = 512 }),
                     ("custody without backend", TrackedRequest(VerificationCustodyBackends.LinuxCgroup, runtime.RunnerStoreId)
                         with { Exe = "codex", TranscriptFormat = TranscriptFormats.Codex }),
                     ("unknown format", Codex() with { TranscriptFormat = "opencode" }),
                 })
        {
            var refused = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, label);
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget, label);
        }

        runtime.Mutations.ShouldBeEmpty();

        // The positive control: the same dispatcher admits a well-formed Codex launch...
        var admitted = await dispatcher.DispatchAsync(Launch(Codex("/work/worktrees/task-c660")), CancellationToken.None);
        admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, admitted.ErrorDetail);

        // ...and then holds it to the one seat it has.
        var full = await dispatcher.DispatchAsync(Launch(Codex()), CancellationToken.None);
        full.ErrorCode.ShouldBe(PhoneHomeProblemTypes.Capacity);
        runtime.Mutations.ShouldBe(["start"]);
    }

    [Test]
    public async Task Provider_auth_operation_canonicalizes_codex_and_refuses_other_unknown_providers()
    {
        var runtime = new RecordingRuntime();
        var probe = new RecordingProbe(false);
        var dispatcher = Dispatcher(runtime, probe: probe);

        foreach (var spelling in new[] { "codex", "Codex", "CODEX" })
        {
            var answered = await dispatcher.DispatchAsync(ProviderAuth(spelling), CancellationToken.None);
            answered.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{spelling}: {answered.ErrorDetail}");
            var dto = answered.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json)!;
            dto.Provider.ShouldBe("codex", spelling);
            dto.LoggedIn.ShouldBe(false, spelling);
        }

        probe.Calls.ShouldBe(["codex", "codex", "codex"], "the probe is always asked by the canonical name");

        foreach (var other in new[] { "opencode", "codex-cli", "openai" })
        {
            var refused = await dispatcher.DispatchAsync(ProviderAuth(other), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error, other);
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget, other);
            refused.StatusCode.ShouldBe(400, other);
            refused.ErrorDetail.ShouldNotBeNull();
            refused.ErrorDetail.ShouldContain("'codex'", customMessage: other);
        }

        probe.Calls.Count.ShouldBe(3);

        // No probe on this runner: Codex, like the others, cannot tell rather than guessing.
        var unknown = await Dispatcher(runtime).DispatchAsync(ProviderAuth("codex"), CancellationToken.None);
        unknown.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        var unknownDto = unknown.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json)!;
        unknownDto.Provider.ShouldBe("codex");
        unknownDto.LoggedIn.ShouldBeNull();
        unknownDto.Error.ShouldBe("probe_unavailable");
        runtime.Mutations.ShouldBeEmpty();
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

    // --- CARD-0679 D-9: a retried Launch for a session the runner already holds. ---

    [Test]
    public async Task Duplicate_launch_with_the_same_generation_returns_the_existing_session()
    {
        var logs = new List<string>();
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 1, logs: logs);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc));
        var request = Request("grok", "/work") with { AcceptedStartedAt = generation };

        var first = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);
        first.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        // Capacity 1 is full with the first launch: the retry must be recognised before capacity refuses it.
        var second = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);

        second.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{second.ErrorCode}: {second.ErrorDetail}");
        var firstDto = first.Payload!.Value.Deserialize<RunnerSessionDto>(PhoneHomeFraming.Json)!;
        var secondDto = second.Payload!.Value.Deserialize<RunnerSessionDto>(PhoneHomeFraming.Json)!;
        secondDto.SessionId.ShouldBe(firstDto.SessionId);
        secondDto.AcceptedStartedAt.ShouldBe(firstDto.AcceptedStartedAt);
        secondDto.AcceptedStartedAt.ShouldBe(generation);
        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(1);
        runtime.List().Count.ShouldBe(1);
        lock (logs)
            logs.Count(line => line.StartsWith("[Information]", StringComparison.Ordinal)
                && line.Contains("duplicate-ack", StringComparison.Ordinal)
                && line.Contains(request.SessionId.ToString(), StringComparison.Ordinal)).ShouldBe(1, string.Join(Environment.NewLine, logs));
    }

    [Test]
    public async Task Duplicate_launch_with_another_generation_is_a_typed_409()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 2);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc));
        var request = Request("grok", "/work") with { AcceptedStartedAt = generation };
        (await dispatcher.DispatchAsync(Launch(request), CancellationToken.None)).Kind.ShouldBe(PhoneHomeFrameKind.Result);

        var later = request with { AcceptedStartedAt = SessionGeneration.Next(generation, generation) };
        var refused = await dispatcher.DispatchAsync(Launch(later), CancellationToken.None);

        refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        refused.StatusCode.ShouldBe(409);
        // The wire value, pinned: the desktop matches on the code, never on detail text.
        refused.ErrorCode.ShouldBe("phone_home_session_already_running");
        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(1);
        runtime.List().Single().AcceptedStartedAt.ShouldBe(generation);
    }

    // --- CARD-0679 R5 repair (review 137c1631): a re-sent Launch for a session that already exited. ---

    [Test]
    public async Task Duplicate_launch_of_an_exited_session_with_the_same_generation_starts_no_second_process()
    {
        var logs = new List<string>();
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 1, logs: logs);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc));
        var request = Request("grok", "/work") with { AcceptedStartedAt = generation };
        (await dispatcher.DispatchAsync(Launch(request), CancellationToken.None)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
        // The first process ran and exited before the desktop's pre-ack re-send arrived.
        runtime.MarkExited(request.SessionId, exitCode: 3, exitReason: "ProcessExited");

        var resend = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);

        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(1, "the same generation never runs twice");
        resend.Kind.ShouldBe(PhoneHomeFrameKind.Error, "the re-send is answered, not started");
        resend.StatusCode.ShouldBe(409);
        // The wire value, pinned: the desktop matches on the code, never on detail text.
        resend.ErrorCode.ShouldBe("phone_home_session_already_exited");
        var exited = resend.Payload!.Value.Deserialize<RunnerSessionDto>(PhoneHomeFraming.Json)!;
        exited.SessionId.ShouldBe(request.SessionId);
        exited.Status.ShouldBe("Exited");
        exited.ExitCode.ShouldBe(3);
        exited.ExitReason.ShouldBe("ProcessExited");
        exited.AcceptedStartedAt.ShouldBe(generation);
        runtime.List().Single().Status.ShouldBe("Exited");
        lock (logs)
            logs.Count(line => line.StartsWith("[Information]", StringComparison.Ordinal)
                && line.Contains("phone_home_session_already_exited", StringComparison.Ordinal)
                && line.Contains(request.SessionId.ToString(), StringComparison.Ordinal)).ShouldBe(1, string.Join(Environment.NewLine, logs));
    }

    [Test]
    public async Task Launch_of_an_exited_session_under_a_new_generation_still_relaunches()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 1);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc));
        var request = Request("grok", "/work") with { AcceptedStartedAt = generation };
        (await dispatcher.DispatchAsync(Launch(request), CancellationToken.None)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
        runtime.MarkExited(request.SessionId, exitCode: 0, exitReason: "ProcessExited");

        // claude --resume reuses the session id under a new generation: today's relaunch stands.
        var next = request with { AcceptedStartedAt = SessionGeneration.Next(generation, generation) };
        var relaunched = await dispatcher.DispatchAsync(Launch(next), CancellationToken.None);

        relaunched.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{relaunched.ErrorCode}: {relaunched.ErrorDetail}");
        var dto = relaunched.Payload!.Value.Deserialize<RunnerSessionDto>(PhoneHomeFraming.Json)!;
        dto.Status.ShouldBe("Running");
        dto.AcceptedStartedAt.ShouldBe(next.AcceptedStartedAt);
        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(2);
    }

    // --- CARD-0679 R5 repair 2 (review 18f52a40): the fence outlives the runner's session record. ---

    [Test]
    public async Task Pre_ack_resend_after_the_slot_was_released_starts_no_second_process()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, capacity: 1);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc));
        var request = Request("grok", "/work") with { AcceptedStartedAt = generation };
        (await dispatcher.DispatchAsync(Launch(request), CancellationToken.None)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
        runtime.MarkExited(request.SessionId, exitCode: 0, exitReason: "ProcessExited");
        // The seat is released before the desktop's pre-ack re-send arrives: the runner no longer lists the session.
        var released = await dispatcher.DispatchAsync(ReleaseSlot(request.SessionId), CancellationToken.None);
        released.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{released.ErrorCode}: {released.ErrorDetail}");
        runtime.List().ShouldBeEmpty();

        var resend = await dispatcher.DispatchAsync(Launch(request), CancellationToken.None);
        var older = await dispatcher.DispatchAsync(
            Launch(request with { AcceptedStartedAt = generation.AddTicks(-SessionGeneration.MicrosecondTicks) }),
            CancellationToken.None);

        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(1, "an accepted generation never starts twice");
        resend.Kind.ShouldBe(PhoneHomeFrameKind.Error, "the re-send is refused, not started");
        resend.StatusCode.ShouldBe(409);
        // The wire value, pinned: the desktop matches on the code, never on detail text.
        resend.ErrorCode.ShouldBe("phone_home_session_generation_already_accepted");
        older.Kind.ShouldBe(PhoneHomeFrameKind.Error, "an older generation than the accepted one is refused too");
        older.ErrorCode.ShouldBe("phone_home_session_generation_already_accepted");

        // A resume under a newer generation is not blocked by the fence.
        var next = await dispatcher.DispatchAsync(
            Launch(request with { AcceptedStartedAt = SessionGeneration.Next(generation, generation) }), CancellationToken.None);
        next.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{next.ErrorCode}: {next.ErrorDetail}");
        runtime.Mutations.Count(mutation => mutation == "start").ShouldBe(2);
    }

    [Test]
    public async Task Pre_ack_resend_after_a_runner_restart_starts_no_second_process()
    {
        var state = Path.Combine(Path.GetTempPath(), $"antiphon-launch-generations-{Guid.NewGuid():N}");
        try
        {
            var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 25, 12, 30, 0, DateTimeKind.Utc));
            var request = Request("grok", "/work") with { AcceptedStartedAt = generation };
            var before = new RecordingRuntime();
            (await Dispatcher(before, capacity: 1, launchGenerationsPath: state)
                .DispatchAsync(Launch(request), CancellationToken.None)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
            before.Mutations.Count(mutation => mutation == "start").ShouldBe(1);

            // The runner restarts: a new process with an empty session table over the same state directory.
            var after = new RecordingRuntime();
            var restarted = Dispatcher(after, capacity: 1, launchGenerationsPath: state);
            var resend = await restarted.DispatchAsync(Launch(request), CancellationToken.None);

            after.Mutations.Count(mutation => mutation == "start").ShouldBe(0, "the generation accepted before the restart never starts again");
            resend.Kind.ShouldBe(PhoneHomeFrameKind.Error, "the re-send is refused, not started");
            resend.StatusCode.ShouldBe(409);
            resend.ErrorCode.ShouldBe("phone_home_session_generation_already_accepted");

            var next = await restarted.DispatchAsync(
                Launch(request with { AcceptedStartedAt = SessionGeneration.Next(generation, generation) }), CancellationToken.None);
            next.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{next.ErrorCode}: {next.ErrorDetail}");
            after.Mutations.Count(mutation => mutation == "start").ShouldBe(1);
        }
        finally
        {
            if (Directory.Exists(state))
                Directory.Delete(state, recursive: true);
        }
    }

    // --- CARD-0679 D-4: the mutation log line carries ids, exe basename and generations only. ---

    [Test]
    public async Task Launch_and_kill_log_lines_carry_no_argv_env_cwd_or_error_detail()
    {
        const string argSentinel = "ARG-SENTINEL-7f3c-do-not-log";
        const string envKeySentinel = "ENV_KEY_SENTINEL_7f3c";
        const string envValueSentinel = "ENV-VALUE-SENTINEL-7f3c";
        const string cwdSentinel = "/work/worktrees/task-7f3c0bad";
        var logs = new List<string>();
        var runtime = new RecordingRuntime();
        var dispatcher = Dispatcher(runtime, logs: logs);
        var generation = SessionGeneration.Normalize(new DateTime(2026, 9, 24, 11, 0, 0, DateTimeKind.Utc));
        var env = new Dictionary<string, string> { [envKeySentinel] = envValueSentinel };
        var admittedRequest = new RunnerLaunchRequest(Guid.NewGuid(), "/usr/local/bin/grok",
            ["--prompt", argSentinel], env, cwdSentinel, 80, 24) with { AcceptedStartedAt = generation };

        var admitted = await dispatcher.DispatchAsync(Launch(admittedRequest), CancellationToken.None);
        admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{admitted.ErrorCode}: {admitted.ErrorDetail}");

        // A refusal whose detail text is non-empty: the line names the code, never the detail.
        var refusedRequest = admittedRequest with { SessionId = Guid.NewGuid(), Cwd = "/outside-" + cwdSentinel.TrimStart('/') };
        var refused = await dispatcher.DispatchAsync(Launch(refusedRequest), CancellationToken.None);
        refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        refused.ErrorDetail.ShouldNotBeNullOrWhiteSpace();

        var killSession = Guid.NewGuid();
        var kill = await dispatcher.DispatchAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.KillGeneration,
            JsonSerializer.SerializeToElement(
                new { sessionId = killSession, expectedAcceptedStartedAt = generation }, PhoneHomeFraming.Json)),
            CancellationToken.None);
        kill.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{kill.ErrorCode}: {kill.ErrorDetail}");

        string[] lines;
        lock (logs)
            lines = [.. logs];
        var all = string.Join(Environment.NewLine, lines);

        var admittedLine = lines.Single(line => line.Contains("Phone-home Launch", StringComparison.Ordinal)
            && line.Contains(admittedRequest.SessionId.ToString(), StringComparison.Ordinal));
        admittedLine.ShouldStartWith("[Information]");
        admittedLine.ShouldContain(" exe=grok ");
        admittedLine.ShouldContain("outcome=started");
        admittedLine.ShouldNotContain("/usr/local/bin");

        var refusedLine = lines.Single(line => line.Contains("Phone-home Launch", StringComparison.Ordinal)
            && line.Contains(refusedRequest.SessionId.ToString(), StringComparison.Ordinal));
        refusedLine.ShouldStartWith("[Information]");
        refusedLine.ShouldContain(" exe=grok ");
        refusedLine.ShouldContain($"outcome=refused:{PhoneHomeProblemTypes.UnsupportedTarget}");
        refusedLine.ShouldNotContain(refused.ErrorDetail!);
        refusedLine.ShouldNotContain("cwd must be");

        foreach (var sentinel in new[] { argSentinel, envKeySentinel, envValueSentinel, cwdSentinel, "task-7f3c0bad", "--prompt" })
            all.ShouldNotContain(sentinel, customMessage: "log leaked " + sentinel + Environment.NewLine + all);

        var killLine = lines.Single(line => line.Contains("Phone-home KillGeneration", StringComparison.Ordinal));
        killLine.ShouldStartWith("[Information]");
        killLine.ShouldContain(kill.RequestId.ToString());
        killLine.ShouldContain($"session={killSession}");
        killLine.ShouldContain("expectedGeneration=2026-09-24T11:00:00");
        killLine.ShouldContain($"outcome={KillGenerationOutcomes.Missing}");
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
        bool grokAuthProbeEnabled = true, bool codexAuthProbeEnabled = true,
        int maxMessageUtf8Bytes = PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes, List<string>? logs = null,
        string? launchGenerationsPath = null)
    {
        var settings = new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            RunnerRepository = "/work/repos/antiphon",
            Capacity = capacity,
            ClaudeAuthProbeEnabled = claudeAuthProbeEnabled,
            GrokAuthProbeEnabled = grokAuthProbeEnabled,
            CodexAuthProbeEnabled = codexAuthProbeEnabled,
            Limits = new PhoneHomeLimits(MaxMessageUtf8Bytes: maxMessageUtf8Bytes),
        };
        // Bound from configuration, the way the runner reads it (PhoneHome:LaunchGenerationsPath).
        if (launchGenerationsPath is not null)
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LaunchGenerationsPath"] = launchGenerationsPath })
                .Build().Bind(settings);
        return new(runtime, settings, probe, logs is null ? null : new ListLogger<PhoneHomeCommandDispatcher>(logs));
    }

    private static PhoneHomeFrame ReleaseSlot(Guid sessionId) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.ReleaseSlot,
            JsonSerializer.SerializeToElement(new { sessionId, reason = "test-release" }, PhoneHomeFraming.Json));

    private static RunnerLaunchRequest Request(string exe, string cwd) =>
        new(Guid.NewGuid(), exe, [], new Dictionary<string, string>(), cwd, 80, 24);

    private static PhoneHomeFrame Launch(RunnerLaunchRequest request) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
            System.Text.Json.JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json));

    private sealed class RecordingRuntime : IPhoneHomeRuntimeSurface
    {
        public int Owned { get; set; }
        public List<string> Mutations { get; } = [];
        public List<RunnerLaunchRequest> Started { get; } = [];

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
        /// <summary>CARD-0679: started sessions, keyed like the real runtime: one live session per id.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, RunnerSessionDto> _sessions = new();
        public IReadOnlyList<RunnerSessionDto> List() => [.. _sessions.Values];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw (Fault?.Invoke() ?? new KeyNotFoundException());
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            // The real runtime refuses a second live session with the same id.
            if (_sessions.TryGetValue(request.SessionId, out var running) && running.Status != "Exited")
                throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
            Mutations.Add("start");
            Started.Add(request);
            Owned++;
            var session = new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0,
                AcceptedStartedAt: request.AcceptedStartedAt);
            _sessions[request.SessionId] = session;
            return Task.FromResult(session);
        }

        /// <summary>The session's process exited; like the real runtime, it stays listed as Exited.</summary>
        public void MarkExited(Guid sessionId, int? exitCode, string exitReason)
        {
            _sessions[sessionId] = _sessions[sessionId] with { Status = "Exited", ExitCode = exitCode, ExitReason = exitReason };
            Owned--;
        }

        /// <summary>Like the real runtime (CARD-0653): an exited record is evicted and forgotten.</summary>
        public Task<RunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct)
        {
            Mutations.Add("release");
            return Task.FromResult(_sessions.TryRemove(sessionId, out var released)
                ? released
                : new RunnerSessionDto(sessionId, null, DateTime.UtcNow, "Exited", null, "Released", 0));
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
