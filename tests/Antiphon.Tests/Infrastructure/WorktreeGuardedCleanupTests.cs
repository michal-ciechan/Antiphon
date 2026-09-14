using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeGuardedCleanupTests
{
    [Test]
    public async Task C443_CaptureCommittedBeforeRetry()
    {
        await using var h = await RemovalHarness.CreateAsync();
        h.OnRemove = async count => {
            await using var db = h.H.CreateContext(); var row = await db.WorktreeCleanupAttempts.AsNoTracking().SingleAsync();
            row.InitialCommandId.ShouldNotBeNull();
            if (count == 2) { row.CaptureState.ShouldBe(WorktreeCleanupCaptureState.Captured); row.CaptureJson.ShouldNotBeNull(); row.RetryCommandId.ShouldNotBeNull(); }
        };
        (await h.RemoveAsync()).IsClean.ShouldBeTrue(); h.Removes.ShouldBe(2);
    }
    [Test]
    public async Task C443_OneCapturePerInvocation()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Persistent = true;
        await h.RemoveAsync(); h.Diagnostics.Calls.ShouldBe(1); h.Probe.Calls.ShouldBe(1);
    }
    [Test]
    public async Task C443_InitialGitLockRefuses()
    {
        await using var h = await RemovalHarness.CreateAsync();
        await h.H.Fixture.RequiredAsync(h.H.Fixture.Repository, "worktree", "lock", h.H.Fixture.Source);
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(0); h.Diagnostics.Calls.ShouldBe(0);
    }
    [Test]
    public async Task C443_NewGitLockStopsRetry()
    {
        await using var h = await RemovalHarness.CreateAsync();
        h.Diagnostics.Before = () => h.H.Fixture.RequiredAsync(h.H.Fixture.Repository, "worktree", "lock", h.H.Fixture.Source);
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(1);
        (await h.RowAsync()).RetryCommandId.ShouldBeNull();
    }
    [Test]
    public async Task C443_UnknownRegistrationStopsRetry()
    {
        await using var h = await RemovalHarness.CreateAsync();
        h.OtherCommand = (_, args) => Task.FromResult<LandingGitResult?>(h.Removes == 1 && args[0] == "worktree" && args[1] == "list" ? new(128, "", "query_failed") : null);
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(1);
    }
    [Test]
    [Arguments(32, true)] [Arguments(33, true)] [Arguments(5, false)] [Arguments(145, false)] [Arguments(-1, false)]
    public async Task C443_RetryOnlyNativeSharingCodes(int code, bool retry)
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Probe.Code = code < 0 ? null : code;
        await h.RemoveAsync(); h.Removes.ShouldBe(retry ? 2 : 1);
    }
    [Test]
    public async Task C443_TwoGitSlotsMaximum()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Persistent = true;
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(2);
        var row = await h.RowAsync(); row.InitialCommandId.ShouldNotBeNull(); row.RetryCommandId.ShouldNotBeNull();
        await h.RemoveAsync(); h.Removes.ShouldBe(2);
    }
    [Test]
    public async Task C443_SingleBackoff()
    {
        await using var h = await RemovalHarness.CreateAsync(); await h.RemoveAsync();
        h.Clock.Delays.Where(d => d < TimeSpan.FromSeconds(10)).ShouldBe([TimeSpan.FromMilliseconds(250)]);
    }
    [Test]
    public async Task C443_PreserveFirstAndLastGitOutcomes()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Persistent = true; await h.RemoveAsync();
        var row = await h.RowAsync(); row.FirstGitFailureJson.ShouldContain("128"); row.LastGitOutcomeJson.ShouldContain("1");
        row.CaptureJson.ShouldContain("DeleteAccessOpen"); row.CaptureJson.ShouldContain("32");
    }
    [Test]
    public async Task C443_UnavailableHandleStillProbes()
    {
        await using var h = await RemovalHarness.CreateAsync();
        (await h.RemoveAsync()).IsClean.ShouldBeTrue(); h.Probe.Calls.ShouldBe(1); h.Removes.ShouldBe(2);
    }
    [Test]
    public async Task C443_GitAndOwnersCannotNominate()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Diagnostics.Owners = true; h.Probe.Code = null;
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(1);
    }
    [Test]
    public async Task C443_IncompleteSuccessDoesNotRetry()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Incomplete = true;
        (await h.RemoveAsync()).Residue.ShouldBe("worktree_removal_incomplete");
        h.Removes.ShouldBe(1); h.Diagnostics.Calls.ShouldBe(1); h.Clock.Delays.ShouldNotContain(TimeSpan.FromMilliseconds(250));
    }
    [Test]
    public async Task C443_TimeoutDoesNotRetry()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Timeout = true;
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(1); (await h.RowAsync()).FirstGitFailureJson.ShouldContain("git_timeout");
    }
    [Test]
    public async Task C443_ContextFreePublicationIsOnePass()
    {
        await using var h = await RemovalHarness.CreateAsync();
        (await h.RemoveAsync(context: false)).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(1); h.Diagnostics.Calls.ShouldBe(0);
        (await h.RowAsync()).InitialCommandId.ShouldBeNull();
    }
    [Test]
    public async Task C443_InitialCleanHasNoCapture()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.CleanFirst = true;
        (await h.RemoveAsync()).IsClean.ShouldBeTrue(); h.Removes.ShouldBe(1); h.Diagnostics.Calls.ShouldBe(0); h.Probe.Calls.ShouldBe(0);
        var row = await h.RowAsync(); row.CaptureState.ShouldBe(WorktreeCleanupCaptureState.NotNeeded); row.RetryCommandId.ShouldBeNull();
    }
    [Test] public Task C443_InitialFirstInspectionRequired() => ContentBoundaryAsync(false, 1, false);
    [Test] public Task C443_InitialSecondInspectionRequired() => ContentBoundaryAsync(false, 2, false);
    [Test] public Task C443_InitialFirstIgnoredBoundary() => ContentBoundaryAsync(false, 1, true);
    [Test] public Task C443_InitialSecondIgnoredBoundary() => ContentBoundaryAsync(false, 2, true);
    [Test] public Task C443_RetryFirstInspectionRequired() => ContentBoundaryAsync(true, 1, false);
    [Test] public Task C443_RetrySecondInspectionRequired() => ContentBoundaryAsync(true, 2, false);
    [Test] public Task C443_RetryFirstIgnoredBoundary() => ContentBoundaryAsync(true, 1, true);
    [Test] public Task C443_RetrySecondIgnoredBoundary() => ContentBoundaryAsync(true, 2, true);

    private static async Task ContentBoundaryAsync(bool retry, int read, bool ignored)
    {
        await using var h = await RemovalHarness.CreateAsync();
        var reads = 0; var reached = false;
        var path = Path.Combine(h.H.Fixture.Source, ignored ? "bin-private/keep.txt" : "feature.txt");
        h.OtherCommand = async (repo, args) => {
            if (repo == h.H.Fixture.Source && args[0] == "status" && (retry ? h.Removes == 1 : h.Removes == 0) && ++reads == read)
            { reached = true; Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "new owner bytes"); }
            return null;
        };
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); reached.ShouldBeTrue(); reads.ShouldBe(read);
        h.Removes.ShouldBe(retry ? 1 : 0); (await File.ReadAllTextAsync(path)).ShouldBe("new owner bytes");
    }
    [Test]
    public async Task C443_UnregisteredRootPreserved()
    {
        await using var h = await RemovalHarness.CreateAsync();
        h.OtherCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "worktree" && args[1] == "list" ? new(0, "", "") : null);
        (await h.RemoveAsync()).IsClean.ShouldBeFalse(); h.Removes.ShouldBe(0); Directory.Exists(h.H.Fixture.Source).ShouldBeTrue();
    }
    [Test]
    public async Task C443_DirectoryPostcondition()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Incomplete = true;
        var result = await h.RemoveAsync(); result.DirectoryGone.ShouldBeFalse(); result.BranchDeleted.ShouldBeFalse(); result.IsClean.ShouldBeFalse();
    }
    [Test]
    public async Task C443_RegistrationPostcondition()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Incomplete = true;
        var result = await h.RemoveAsync(); result.Unregistered.ShouldBeFalse(); result.IsClean.ShouldBeFalse();
    }
    [Test]
    public async Task C443_BranchDeleteUsesOldSha()
    {
        await using var h = await RemovalHarness.CreateAsync(); await h.RemoveAsync();
        h.H.Fixture.Git.Trace.Single(a => a[0] == "update-ref" && a.Contains("-d"))
            .ShouldBe(["update-ref", "--no-deref", "-d", h.H.Fixture.SourceRef, h.SourceSha]);
    }
    [Test]
    public async Task C443_BranchDeleteNoDeref()
    {
        await using var h = await RemovalHarness.CreateAsync(); await h.RemoveAsync();
        h.H.Fixture.Git.Trace.Single(a => a[0] == "update-ref" && a.Contains("-d")).ShouldContain("--no-deref");
    }
    [Test]
    public async Task C443_BranchNotRetried()
    {
        await using var h = await RemovalHarness.CreateAsync();
        h.OtherCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "update-ref" && args.Contains("-d") ? new(1, "", "delete_failed") : null);
        var result = await h.RemoveAsync();
        result.Residue.ShouldBe("branch_delete_failed", System.Text.Json.JsonSerializer.Serialize(await h.RowAsync()));
        h.H.Fixture.Git.Trace.Count(a => a[0] == "update-ref" && a.Contains("-d")).ShouldBe(1);
    }
    [Test]
    public async Task C443_NoCleanupRescue()
    {
        await using var h = await RemovalHarness.CreateAsync(); h.Persistent = true;
        await h.RemoveAsync();
        (await File.ReadAllTextAsync(Path.Combine(h.H.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
        h.H.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--force") || a.Contains("prune") || a.Contains("reset"));
    }
    [Test]
    public async Task C443_CoordinatorOuterCancellation()
    {
        await using var h = await RemovalHarness.CreateAsync(); using var source = new CancellationTokenSource();
        h.Diagnostics.Before = () => { source.Cancel(); return Task.CompletedTask; };
        await Should.ThrowAsync<OperationCanceledException>(() => h.RemoveAsync(ct: source.Token)); h.Removes.ShouldBe(1);
    }

    internal sealed class RemovalHarness : IAsyncDisposable
    {
        public LandingSafetyHarness H = null!;
        public DiagnosticsDouble Diagnostics { get; } = new();
        public ProbeDouble Probe { get; } = new();
        public RecordingClock Clock { get; } = new();
        public WorktreeCleanupContext Context = null!;
        public AgentTaskLanding Operation = null!;
        public string SourceSha = "";
        public int Removes;
        public bool Persistent, CleanFirst, Incomplete, Timeout;
        public Func<int, Task>? OnRemove;
        public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? OtherCommand;
        public static async Task<RemovalHarness> CreateAsync(Func<LandingSafetyHarness, Task>? beforeRequest = null)
        {
            var h = new RemovalHarness();
            h.H = new LandingSafetyHarness { Clock = h.Clock, ConfigureServices = services => {
                services.AddSingleton<IWorktreeLockDiagnostics>(h.Diagnostics); services.AddSingleton<IWorktreeDeleteAccessProbe>(h.Probe); } };
            await h.H.InitializeAsync(); h.SourceSha = await h.H.AddSourceAsync();
            if (beforeRequest is not null) await beforeRequest(h.H);
            h.H.Fault.Phase = LandPhase.CleanupStarted; h.H.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.H.RunAsync());
            h.Operation = (await h.H.OperationAsync())!;
            await using var db = h.H.CreateContext();
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.H.Fixture.TaskId && r.IsPending);
            var op = h.Operation;
            var row = await h.H.Services.GetRequiredService<IWorktreeCleanupJournal>().GetOrCreateAsync(new(request.Id, op.Id, op.TaskId,
                op.RepositoryPath, op.WorktreePath, op.CommonDirectory, op.GitDirectory, op.SourceFullRef, op.TargetFullRef, op.VerifiedSourceSha!, op.TargetBeforeSha), default);
            h.Context = new(row.Id, request.Id, op.Id, op.TaskId); h.Clock.Delays.Clear(); h.H.Fixture.Git.Trace.Clear();
            h.H.Fixture.Git.BeforeCommand = async (repo, args) => {
                if (args[0] == "worktree" && args[1] == "remove")
                {
                    h.Removes++; if (h.OnRemove is not null) await h.OnRemove(h.Removes);
                    if (h.Timeout) throw new TimeoutException("fixture_command_timeout");
                    if (h.Incomplete) return new(0, "", "");
                    if (h.Persistent || h.Removes == 1 && !h.CleanFirst) return new(h.Removes == 1 ? 128 : 1, "", "controlled_failure");
                }
                return h.OtherCommand is null ? null : await h.OtherCommand(repo, args);
            };
            return h;
        }
        public async Task<WorktreeRemoval> RemoveAsync(bool context = true, CancellationToken ct = default)
        {
            await using var lease = await H.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(H.Fixture.Repository, ct);
            lease.ShouldNotBeNull();
            return await H.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(new(WorktreeRemovalPurpose.Publication,
                H.Fixture.Coordinates, Operation.CommonDirectory, Operation.GitDirectory, SourceSha, Operation.TargetBeforeSha,
                Operation.Id, lease, CleanupContext: context ? Context : null), ct);
        }
        public Task<WorktreeCleanupAttempt> RowAsync() => H.Services.GetRequiredService<IWorktreeCleanupJournal>().ReadAsync(Context, default);
        public ValueTask DisposeAsync() => H.DisposeAsync();
    }
    internal sealed class DiagnosticsDouble : IWorktreeLockDiagnostics
    {
        public int Calls; public bool Owners; public Func<Task>? Before;
        public string OwnerName = "owned-holder", OwnerPath = ".";
        public async Task<WorktreeLockSnapshot> CaptureAsync(string root, CancellationToken ct)
        {
            Calls++; if (Before is not null) await Before(); ct.ThrowIfCancellationRequested();
            return new(Owners ? WorktreeLockStatus.OwnersObserved : WorktreeLockStatus.Unavailable,
                Owners ? "Observed" : "InsufficientPrivileges", DateTime.UtcNow, Owners ? [new(OwnerName, 4321, OwnerPath)] : []);
        }
    }
    internal sealed class ProbeDouble : IWorktreeDeleteAccessProbe
    {
        public int Calls; public int? Code = 32;
        public Task<WorktreeNativeSnapshot> ObserveAsync(WorktreeProbeTarget target, IReadOnlyList<WorktreeLockOwner> owners, CancellationToken ct)
        { Calls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(new WorktreeNativeSnapshot(WorktreeLockStatus.Partial,
            "ControlledObservation", [new("DeleteAccessOpen", ".", DateTime.UtcNow, Code is null, Code, true)])); }
    }
    internal sealed class RecordingClock : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Delays.Add(dueTime); return TimeProvider.System.CreateTimer(callback, state, dueTime, period); }
    }
}
