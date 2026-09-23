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

    private static PhoneHomeCommandDispatcher Dispatcher(IPhoneHomeRuntimeSurface runtime, int capacity = 8) =>
        new(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            RunnerRepository = "/work/repos/antiphon",
            Capacity = capacity,
        });

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

        /// <summary>Null (the default) is a runner that advertises no custody at all.</summary>
        public string? CustodyBackend { get; init; }
        public string? VerificationCustodyBackend => CustodyBackend;
        public Guid RunnerStoreId { get; } = Guid.NewGuid();

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: CustodyBackend);
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
