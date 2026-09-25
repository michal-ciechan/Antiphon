using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRefusedRetryTests
{
    [Test]
    public async Task RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation()
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        await s.AssertUnchangedIdentityAsync();
        var requested = await s.RequestAsync();
        await s.H.RestartServicesAsync();
        await s.H.SweepAsync();
        (await s.TaskAsync()).LandRequestedAt.ShouldBe(requested);
        (await s.TaskAsync()).ActiveLandingId.ShouldBe(s.A.Id);
        var b = await s.ExecuteAsync();
        b.Id.ShouldNotBe(s.A.Id, "explicit retry must allocate B before any cleanup assertions");
        b.TargetBeforeSha.ShouldBe(s.A.TargetBeforeSha);
        b.VerificationSkipReason.ShouldBe("base_unchanged");
        s.H.Verifier.Calls.ShouldBe(0);
        s.Trace.ShouldContain(t => s.IsTargetStatus(t.Repository, t.Args) && t.Result.Succeeded && t.Result.Output.Length == 0);
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b);
    }

    [Test]
    public async Task RR_V2_StillDirtyTargetCreatesFreshRefusal()
    {
        // CARD-0688: the unrepaired cause (the foreign land-worktree directory) refuses B again before any mutation.
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        b.Phase.ShouldBe(LandPhase.Refused, "B must refuse the still-foreign land worktree before mutation");
        b.LastReason.ShouldBe("land_worktree_foreign");
        s.Trace.ShouldNotContain(t => t.Args.Length > 1 && t.Args[0] == "worktree" && t.Args[1] == "add");
        await s.AssertReplacementAsync(b);
        await s.AssertRefusalAsync(b);
    }

    [Test]
    public async Task RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification()
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        // CARD-0688 D-4: the target is the remote; a target-only repair is published there (the main checkout too).
        await File.WriteAllTextAsync(Path.Combine(s.F.Repository, "target-repair.txt"), "target-only repair\n");
        await s.F.RequiredAsync(s.F.Repository, "add", "target-repair.txt");
        await s.F.RequiredAsync(s.F.Repository, "commit", "-m", "target-only repair");
        await s.ReadAsync(s.F.Repository, "push", "origin", s.F.TargetRef); // independent git: not the land's trace
        var target = (await s.F.RequiredAsync(s.F.Repository, "rev-parse", "HEAD")).Trim();
        const string filter = "/*/*/RefusedRetryFixture/SelectedCheck";
        var barrierHit = false;
        s.H.Verifier.Barrier = async () =>
        {
            var prepared = (await s.H.OperationAsync())!;
            prepared.Id.ShouldNotBe(s.A.Id);
            prepared.Phase.ShouldBe(LandPhase.Prepared);
            prepared.PreparedPinned.ShouldBeTrue();
            prepared.VerifiedAt.ShouldBeNull();
            prepared.RemoteConfirmedAt.ShouldBeNull();
            (await s.ReadAsync(prepared.LandWorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(prepared.RebasedSourceSha);
            (await s.ReadAsync(s.F.Source, "rev-parse", "HEAD")).Trim().ShouldBe(s.SourceSha, "the task branch is never rebased");
            (await s.ReadAsync(s.F.Repository, "rev-parse", prepared.RecoveryRefPrefix + "/prepared")).Trim().ShouldBe(prepared.RebasedSourceSha);
            barrierHit = true;
        };
        await s.RequestAsync(filter);
        var b = await s.ExecuteAsync(filter);
        barrierHit.ShouldBeTrue();
        b.TargetBeforeSha.ShouldBe(target);
        b.OriginalSourceSha.ShouldBe(s.SourceSha);
        b.VerificationFilter.ShouldBe(filter);
        b.VerificationPassed.ShouldBeTrue();
        b.VerificationSkipReason.ShouldBeNull();
        s.H.Verifier.Invocations.ShouldHaveSingleItem().ShouldBe((s.LandPath, filter));
        s.Trace.ShouldContain(t => t.Args.Contains("rebase") && t.Args.Contains(target));
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b, target);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RR_V4_TargetStatusFailureRetriesFromFreshEvidence(bool restoreStatus)
    {
        // CARD-0688: the pre-rebase status proof of the clean land worktree replaces the old pre-rebase target status.
        await using var s = await Scenario.CreateAsync(sentinel: false);
        s.Inject = (p, a) => s.IsLandStatus(p, a);
        await s.RefuseAsync("land_worktree_unready");
        s.Hits.ShouldBeGreaterThan(0);
        if (restoreStatus) s.Inject = null;
        s.Hits = 0;
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        await s.AssertReplacementAsync(b);
        if (restoreStatus)
        {
            s.Trace.ShouldContain(t => s.IsTargetStatus(t.Repository, t.Args) && t.Result.Succeeded && t.Result.Output.Length == 0);
            await s.AssertSuccessAsync(b);
        }
        else
        {
            s.Hits.ShouldBeGreaterThan(0);
            await s.AssertRefusalAsync(b);
        }
    }

    [Test]
    public async Task RR_V4_RemoteReadFailureRetriesUnchangedSource()
    {
        await using var s = await Scenario.CreateAsync(sentinel: false);
        // CARD-0688 D-4/D-8: the target is read while the operation is created, so the operation-level remote read
        // is the source recheck at protocol entry (after the resolver's observation and its recheck).
        var sourceReads = 0;
        s.Inject = (p, a) => s.Is(p, s.F.Repository) && a.SequenceEqual(new[] { "ls-remote", "--refs", "--exit-code", s.F.Remote, s.F.SourceRef })
            && ++sourceReads == 3; // 1 resolver observation, 2 resolver recheck, 3 protocol entry
        await s.RefuseAsync("source_remote_unreadable");
        s.Hits.ShouldBeGreaterThan(0);
        s.Inject = null;
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        s.Trace.ShouldContain(t => t.Args[0] == "ls-remote");
        s.Trace.ShouldContain(t => t.Args[0] == "fetch" && t.Args.Any(a => a.Contains(b.RecoveryRefPrefix)));
        s.Trace.ShouldContain(t => t.Args[0] == "merge-base");
        s.Trace.ShouldContain(t => s.IsTargetStatus(t.Repository, t.Args));
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b);
    }

    [Test]
    public async Task RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut()
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        await s.ReadAsync(s.F.Source, "push", s.F.Remote, s.SourceSha + ":" + s.F.TargetRef);
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        b.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        b.VerificationSkipReason.ShouldBe("exact_remote_containment");
        s.H.Verifier.Calls.ShouldBe(0);
        s.Trace.ShouldNotContain(t => t.Args.Contains("rebase") || t.Args[0] == "push" || s.InLand(t.Repository));
        // CARD-0688 D-4: after the already-present confirmation the stale main checkout is fast-forwarded to it.
        (await s.ReadAsync(s.F.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(s.SourceSha);
        (await File.ReadAllTextAsync(s.Sentinel)).ShouldBe(Scenario.SentinelBytes);
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b);
    }

    [Test]
    public async Task RR_V5_OriginalPendingRequestCannotReplaceRefusal() => await RecoveryAsync(equal: false);

    [Test]
    public async Task RR_V5_EqualPendingTimestampCannotReplaceRefusal() => await RecoveryAsync(equal: true);

    private async Task RecoveryAsync(bool equal)
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        var marker = equal ? s.A.UpdatedAt : s.InitialRequest;
        var settled = (await s.EventsAsync()).Last(e => e.IsLandTerminal);
        Guid requestId;
        await using (var db = s.H.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == s.F.TaskId);
            task.LandRequestedAt = marker;
            task.LandStartedAt = marker;
            task.LandAttempt = 1;
            // A request persisted before CARD-0488 (schema 1, no review binding) that a restart
            // finds pending beside A's settled history. CARD-0467's one-terminal-event-per-request
            // rule is why A's own settled row is not revived. CARD-0488 refuses such a row at
            // admission (legacy_review_binding_required) before any source inspection; request
            // identity, not timestamps, now decides what may replace a refusal, so `equal` keeps
            // the two historical fixture shapes without changing the production path.
            var request = new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = task.Id, RequestedAt = marker, StartedAt = marker,
                LastAttemptAt = marker, Attempt = 1, State = LandRequestState.Running,
                ReplyTo = AgentTaskReplyTo.None, LastEvaluatedAt = marker, LastProgressAt = marker,
            };
            db.AgentTaskLandRequests.Add(request);
            await db.SaveChangesAsync();
            task.CurrentLandRequestId = request.Id;
            await db.SaveChangesAsync();
            requestId = request.Id;
        }
        if (equal) (await s.TaskAsync()).LandRequestedAt.ShouldBe(s.A.UpdatedAt);
        else (await s.TaskAsync()).LandRequestedAt!.Value.ShouldBeLessThan(s.A.UpdatedAt);
        await s.H.RestartServicesAsync();
        await s.H.SweepAsync();
        (await s.TaskAsync()).LandRequestedAt.ShouldBe(marker);
        var active = await s.ExecuteAsync();
        (await s.OperationsAsync()).ShouldHaveSingleItem().Id.ShouldBe(s.A.Id, "automatic recovery must retain only A");
        (await s.TaskAsync()).ActiveLandingId.ShouldBe(s.A.Id);
        active.LastReason.ShouldBe(s.A.LastReason);
        var terminal = (await s.EventsAsync()).Last(e => e.IsLandTerminal);
        terminal.Id.ShouldNotBe(settled.Id, "recovery must settle the legacy request with its own terminal event");
        terminal.LandRequestId.ShouldBe(requestId);
        terminal.LandingOperationId.ShouldBe(s.A.Id);
        terminal.Detail.ShouldContain("legacy_review_binding_required");
        await using (var db = s.H.CreateContext())
        {
            var legacy = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            legacy.IsPending.ShouldBeFalse();
            legacy.State.ShouldBe(LandRequestState.Completed);
            legacy.TerminalEventId.ShouldBe(terminal.Id);
        }
        s.Trace.ShouldNotContain(t => s.IsTargetStatus(t.Repository, t.Args) || t.Args[0] == "update-ref");
        await s.AssertPinsAsync();
        await s.AssertRefusalAsync(active);
    }

    [Test]
    public async Task RR_V5_SettledRefusalIsNotAutomaticallyQueued()
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        var operations = JsonSerializer.Serialize(await s.OperationsAsync());
        var events = JsonSerializer.Serialize(await s.EventsAsync());
        await s.H.RestartServicesAsync();
        s.Trace.Clear();
        await s.H.SweepAsync();
        await s.H.SweepAsync();
        s.H.Queue.IsActive(s.F.TaskId).ShouldBeFalse();
        s.H.Queue.TryDequeue(out _).ShouldBeFalse();
        s.Trace.ShouldBeEmpty();
        JsonSerializer.Serialize(await s.OperationsAsync()).ShouldBe(operations);
        JsonSerializer.Serialize(await s.EventsAsync()).ShouldBe(events);
        await s.AssertPinsAsync();
        await s.AssertSettledAsync(s.A);
    }

    [Test]
    [Arguments("destination")]
    [Arguments("target-commit")]
    public async Task RR_V6_PreparationFailurePreservesActiveRefusal(string boundary)
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        await s.RequestAsync();
        s.Inject = (p, a) => s.Is(p, s.F.Repository) && a.SequenceEqual(boundary == "destination"
            ? new[] { "remote", "get-url", "--push", "--all", "origin" }
            : new[] { "rev-parse", "--verify", s.F.TargetRef + "^{commit}" });
        await s.ExecuteAsync();
        s.Hits.ShouldBeGreaterThan(0);
        // CARD-0688 D-2: the source is read as the branch ref (no worktree status); the push endpoint is first read by
        // the resolver's own source observation in the repository, so that is where the destination fault lands.
        s.Trace.ShouldContain(t => s.Is(t.Repository, s.F.Repository) && t.Args.SequenceEqual(new[] { "show-ref", "--verify", "--hash", s.F.SourceRef })
            && t.Result.Succeeded);
        await s.AssertOldOnlyAsync();
        (await s.EventsAsync()).Last(e => e.IsLandTerminal).Detail.ShouldContain(boundary == "destination" ? "source_remote_endpoint_ambiguous" : "commit_lookup_failed");
        s.AssertNoMutation();
        s.Inject = null;
        await s.H.RestartServicesAsync();
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RR_V6_ReplacementSaveFailureRollsBackOldDeactivation(bool afterSave)
    {
        await using var s = await Scenario.CreateAsync();
        await s.RefuseAsync();
        File.Delete(s.Sentinel);
        await s.RequestAsync();
        var firstSave = false;
        s.H.Fault.AfterSaveAcknowledged = async db =>
        {
            if (firstSave || !db.ChangeTracker.Entries<AgentTaskLanding>().Any(e => e.Entity.Id == s.A.Id && !e.Entity.Active)) return;
            db.Database.CurrentTransaction.ShouldNotBeNull();
            var visible = (await s.H.OperationAsync())!;
            visible.Id.ShouldBe(s.A.Id);
            visible.Active.ShouldBeTrue("uncommitted deactivation must not escape to another connection");
            visible.ConcurrencyToken.ShouldBe(s.A.ConcurrencyToken);
            firstSave = true;
        };
        s.H.Fault.Matches = op => op.Id != s.A.Id && op.Phase == LandPhase.Inspected;
        s.H.Fault.AfterSave = afterSave;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => s.ExecuteAsync());
        firstSave.ShouldBeTrue();
        s.H.Fault.Triggered.ShouldBeTrue();
        await s.AssertOldOnlyAsync();
        s.AssertNoMutation();
        (await s.EventsAsync()).ShouldNotContain(e => e.LandingOperationId != null && e.LandingOperationId != s.A.Id);
        s.H.Fault.Matches = null;
        s.H.Fault.AfterSaveAcknowledged = null;
        s.H.Fault.AfterSave = false;
        await s.H.FailAsync(new LandingSafetyHarness.InjectedSaveFailure());
        await s.AssertSettledAsync(s.A);
        await s.RequestAsync();
        var b = await s.ExecuteAsync();
        await s.AssertReplacementAsync(b);
        await s.AssertSuccessAsync(b);
    }

    private sealed class OffsetClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void After(DateTime instant) => _offset = instant.AddSeconds(2) - DateTime.UtcNow;
    }

    private sealed record Command(string Repository, string[] Args, LandingGitResult Result);

    [Test]
    public async Task C688_RealLandProfile()
    {
        // CARD-0688 V-18 (D-11): a rebased, built real-git land after R1. Printed for the card as C688_PROFILE.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        var logger = new RecordingLogger<AgentTaskLandService>();
        h.Logger = logger;
        var cleanupAt = -1;
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.CleanupStarted && cleanupAt < 0) cleanupAt = h.Fixture.Git.Trace.Count;
            return Task.CompletedTask;
        };
        await h.RequestAsync();
        h.Fixture.Git.Trace.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        var entry = logger.Entries.Where(e => e.Message.StartsWith("Land git profile ", StringComparison.Ordinal)).ShouldHaveSingleItem();
        Console.WriteLine("C688_PROFILE: " + entry.Message);
        // R1 V-18 budget approved 2026-09-25: four listings, preserving D-14's cleanup safeguards.
        // No listing precedes cleanup. Cleanup lists three times itself and its first Full inspection fills
        // the scope's registration cache once.
        cleanupAt.ShouldBeGreaterThan(0);
        h.Fixture.Git.Trace.Take(cleanupAt).ShouldNotContain(a => a.Length > 1 && a[0] == "worktree" && a[1] == "list");
        Convert.ToInt32(entry.State["WorktreeList"]).ShouldBeLessThanOrEqualTo(4);
        Convert.ToInt32(entry.State["Inspections"]).ShouldBeLessThanOrEqualTo(2);
        Convert.ToInt32(entry.State["Processes"]).ShouldBeLessThanOrEqualTo(250);
        // Remote round trips before cleanup: resolver observation (ls-remote+fetch) and its recheck, the target
        // observation at creation, three protocol rechecks (entry, pre-reset, pre-push), the pre-push observation,
        // the push and its confirming observation = 13. Guarded cleanup (unchanged, D-14) observes the remote three
        // times (2 each), so the approved R1 V-18 total is 19, preserving all cleanup observations.
        h.Fixture.Git.Trace.Take(cleanupAt).Count(a => a[0] is "ls-remote" or "fetch" or "push").ShouldBeLessThanOrEqualTo(13);
        Convert.ToInt32(entry.State["Remote"]).ShouldBeLessThanOrEqualTo(19);
        foreach (var phase in new[] { "reset=", "rebase=", "verify=", "push=", "canonical=", "cleanup=" })
            entry.Message.ShouldContain(phase);
        var refs = await h.Fixture.RequiredAsync(h.Fixture.Repository, "for-each-ref", "--format=%(refname)", "refs/antiphon/");
        refs.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldNotContain(r => r.Contains("/source-recheck/"),
            "a one-round-trip recheck leaves no loose ref");
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public const string SentinelBytes = "operator-owned target sentinel\n";
        public LandingSafetyHarness H { get; } = new();
        public LandingGitFixture F => H.Fixture;
        public AgentTaskLanding A { get; private set; } = null!;
        public string SourceSha { get; private set; } = "";
        public DateTime InitialRequest { get; private set; }
        /// <summary>CARD-0688: the refusal A is now a foreign, non-empty directory where the land worktree must be
        /// (land_worktree_foreign, before any mutation); deleting the sentinel is the operator's repair, and the
        /// source never changes. A dirty target checkout is only post-publication residue now (D-4).</summary>
        public string Sentinel => Path.Combine(LandPath, "retry-target-sentinel.txt");
        public string LandPath { get; private set; } = "";
        public bool InLand(string path) => Path.GetFullPath(path).StartsWith(LandPath, StringComparison.Ordinal);
        public List<Command> Trace { get; } = [];
        public Func<string, IReadOnlyList<string>, bool>? Inject { get; set; }
        public int Hits { get; set; }
        private readonly OffsetClock _clock = new();
        private string _pins = "";
        private List<AgentTaskEvent> _oldEvents = [];
        private bool _freshBoundary;
        private string? _filter;

        public static async Task<Scenario> CreateAsync(bool sentinel = true)
        {
            var s = new Scenario();
            try
            {
                s.H.Clock = s._clock;
                await s.H.InitializeAsync();
                s.SourceSha = await s.H.AddSourceAsync();
                await s.H.RequestAsync(expectedSourceSha: s.SourceSha);
                s.InitialRequest = (await s.TaskAsync()).LandRequestedAt!.Value;
                s._clock.After(s.InitialRequest);
                s.LandPath = s.H.Services.GetRequiredService<ILandWorkspace>()
                    .PathFor(await s.F.Git.CommonDirectoryAsync(s.F.Repository, CancellationToken.None));
                if (sentinel)
                {
                    Directory.CreateDirectory(s.LandPath);
                    await File.WriteAllTextAsync(s.Sentinel, SentinelBytes);
                }
                (await s.ReadAsync(s.F.Remote, "rev-parse", s.F.TargetRef)).Trim().ShouldBe(s.F.SeedSha);
                var reader = s.Reader();
                (await reader.RunAsync(s.F.Repository, ["merge-base", "--is-ancestor", s.SourceSha, s.F.TargetRef], CancellationToken.None)).ExitCode.ShouldBe(1);
                if (sentinel) File.Exists(s.Sentinel).ShouldBeTrue();
                s.InstallTrace();
                return s;
            }
            catch { await s.DisposeAsync(); throw; }
        }

        private string[] StatusArgs => ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"];
        public bool Is(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        public bool IsTargetStatus(string p, IReadOnlyList<string> a) => Is(p, F.Repository) && a.SequenceEqual(StatusArgs);
        public bool IsSourceStatus(string p, IReadOnlyList<string> a) => Is(p, F.Source) && a.SequenceEqual(StatusArgs);
        public bool IsLandStatus(string p, IReadOnlyList<string> a) => InLand(p) && a.SequenceEqual(StatusArgs);
        private void InstallTrace()
        {
            F.Git.BeforeCommand = async (p, a) =>
            {
                if (Inject?.Invoke(p, a) == true)
                {
                    Hits++;
                    var result = new LandingGitResult(128, "", "fixture target status unavailable");
                    Trace.Add(new(Path.GetFullPath(p), a.ToArray(), result));
                    LandingEvidence.Write(F.TaskId, "retry_injected_command", new { repository = p, arguments = a.ToArray(), result });
                    return result;
                }
                if (A is not null && a[0] == "update-ref" && a[1].EndsWith("/source") && !a[1].StartsWith(A.RecoveryRefPrefix))
                {
                    var fresh = (await H.OperationAsync())!;
                    fresh.Id.ShouldNotBe(A.Id);
                    fresh.Phase.ShouldBe(LandPhase.Inspected);
                    fresh.Mode.ShouldBe(LandOperationMode.Fresh);
                    fresh.OriginalSourceSha.ShouldBe(SourceSha);
                    fresh.SourceFullRef.ShouldBe(F.SourceRef);
                    fresh.TargetFullRef.ShouldBe(F.TargetRef);
                    // CARD-0688 D-4: the base is the observed remote target; the checkout is recorded after publication.
                    fresh.SchemaVersion.ShouldBe(3);
                    fresh.SourceLocalSha.ShouldBe(SourceSha);
                    fresh.LandWorktreePath.ShouldBe(LandPath);
                    fresh.TargetBeforeSha.ShouldBe((await ReadAsync(F.Remote, "rev-parse", F.TargetRef)).Trim());
                    fresh.DestinationFullRef.ShouldBe(A.DestinationFullRef);
                    fresh.RemoteFingerprint.ShouldBe(A.RemoteFingerprint);
                    fresh.CommonDirectory.ShouldBe(A.CommonDirectory);
                    fresh.WorktreePath.ShouldBe(A.WorktreePath);
                    fresh.TargetCheckoutRecorded.ShouldBeFalse();
                    fresh.TargetCheckoutPath.ShouldBeNull();
                    fresh.VerificationFilter.ShouldBe(_filter);
                    fresh.SourcePinned.ShouldBeFalse(); fresh.TargetPinned.ShouldBeFalse(); fresh.PreparedPinned.ShouldBeFalse();
                    fresh.Publication.ShouldBe(LandPublicationOutcome.Unconfirmed);
                    fresh.Cleanup.ShouldBe(LandCleanupStatus.NotStarted);
                    AssertNoReceipts(fresh);
                    fresh.RebaseStartedAt.ShouldBeNull(); fresh.RebasedSourceSha.ShouldBeNull(); fresh.PreparedAt.ShouldBeNull();
                    _freshBoundary = true;
                    LandingEvidence.Write(F.TaskId, "retry_fresh_committed_boundary", fresh);
                }
                if (a.Contains("rebase") || a.Contains("merge") && a.Contains("--ff-only") || a[0] == "push")
                {
                    var committed = (await H.OperationAsync())!;
                    committed.Phase.ShouldBe(a.Contains("rebase") ? LandPhase.RebaseStarted : a[0] == "push" ? LandPhase.PushStarted : LandPhase.PublicationConfirmed);
                }
                return null;
            };
            F.Git.AfterCommand = (p, a, r) => { Trace.Add(new(Path.GetFullPath(p), a.ToArray(), r)); return Task.CompletedTask; };
        }

        public LandingGitFixture.FixtureGit Reader() => new(Path.Combine(F.Root, "home"), F.TaskId);
        public async Task<string> ReadAsync(string path, params string[] args)
        {
            var result = await Reader().RunAsync(path, args, CancellationToken.None);
            result.Succeeded.ShouldBeTrue($"independent read {args[0]}: {result.Diagnostic}");
            return result.Output;
        }
        public async Task<AgentTask> TaskAsync()
        {
            await using var db = H.CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == F.TaskId);
        }
        public async Task<List<AgentTaskLanding>> OperationsAsync()
        {
            await using var db = H.CreateContext();
            return await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == F.TaskId).OrderBy(o => o.CreatedAt).ToListAsync();
        }
        public async Task<List<AgentTaskEvent>> EventsAsync()
        {
            await using var db = H.CreateContext();
            return await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == F.TaskId).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
        }
        public async Task RefuseAsync(string reason = "land_worktree_foreign")
        {
            await H.RunAsync();
            A = (await H.OperationAsync())!;
            A.Phase.ShouldBe(LandPhase.Refused);
            A.LastReason.ShouldBe(reason);
            (await ReadAsync(F.Source, "rev-parse", "HEAD")).Trim().ShouldBe(SourceSha);
            _pins = await PinsAsync();
            _pins.ShouldNotBeEmpty();
            _oldEvents = await EventsAsync();
            await AssertSettledAsync(A);
            await F.AssertRemoteSourceAsync();
        }
        public async Task AssertUnchangedIdentityAsync()
        {
            var inspection = await Reader().InspectAsync(F.Coordinates, CancellationToken.None);
            inspection.Accepted.ShouldBeTrue();
            var source = inspection.Snapshot!;
            source.HeadSha.ShouldBe(A.OriginalSourceSha);
            source.Coordinates.SourceFullRef.ShouldBe(A.SourceFullRef);
            source.Coordinates.TargetFullRef.ShouldBe(A.TargetFullRef);
            source.CommonDirectory.ShouldBe(A.CommonDirectory);
            source.RegisteredPath.ShouldBe(A.WorktreePath);
            (await ReadAsync(F.Repository, "rev-parse", F.TargetRef)).Trim().ShouldBe(A.TargetBeforeSha);
        }
        public async Task<DateTime> RequestAsync(string? filter = null)
        {
            _filter = filter;
            var before = await TaskAsync();
            var old = (await H.OperationAsync())!;
            var oldEvents = await EventsAsync();
            _clock.After(old.UpdatedAt);
            Trace.Clear();
            var result = await H.RequestAsync(filter, expectedSourceSha: old.OriginalSourceSha);
            result.Status.ShouldBe("queued");
            Trace.ShouldBeEmpty("request must queue before Git runs");
            var after = await TaskAsync();
            after.ActiveLandingId.ShouldBe(old.Id);
            before.ActiveLandingId.ShouldBe(old.Id);
            (await OperationsAsync()).Count(o => o.Active).ShouldBe(1);
            after.LandRequestedAt!.Value.ShouldBeGreaterThan(old.UpdatedAt);
            after.LandStartedAt.ShouldBeNull(); after.LandAttempt.ShouldBe(0); after.LandVerifyFilter.ShouldBe(filter);
            H.Queue.IsActive(F.TaskId).ShouldBeTrue();
            var added = (await EventsAsync()).Where(e => !oldEvents.Any(o => o.Id == e.Id)).ToList();
            added.ShouldHaveSingleItem().Type.ShouldBe(AgentTaskEventType.LandRequested);
            added[0].LandRequestId.ShouldBe(result.RequestId);
            if (filter is not null) added[0].Detail.ShouldContain(filter);
            return after.LandRequestedAt.Value;
        }
        public async Task<AgentTaskLanding> ExecuteAsync(string? filter = null)
        {
            Trace.Clear();
            try { await H.RunQueuedAsync(filter); }
            finally
            {
                LandingEvidence.Write(F.TaskId, "retry_attempt_trace", Trace.ToArray());
                LandingEvidence.Write(F.TaskId, "retry_attempt_database", new { operations = await OperationsAsync(), task = await TaskAsync(), events = await EventsAsync() });
            }
            return (await H.OperationAsync())!;
        }
        private async Task<string> PinsAsync() => await ReadAsync(F.Repository, "for-each-ref", "--format=%(refname) %(objectname)", A.RecoveryRefPrefix + "/");
        public async Task AssertPinsAsync()
        {
            (await PinsAsync()).ShouldBe(_pins);
            foreach (var line in _pins.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ');
                (await ReadAsync(F.Repository, "rev-parse", "--verify", parts[0])).Trim().ShouldBe(parts[1]);
            }
        }
        private string Evidence(AgentTaskLanding op) => JsonSerializer.Serialize(typeof(AgentTaskLanding).GetProperties()
            .Where(p => p.Name is not (nameof(AgentTaskLanding.Active) or nameof(AgentTaskLanding.ConcurrencyToken)))
            .ToDictionary(p => p.Name, p => p.GetValue(op)));
        public async Task AssertOldOnlyAsync()
        {
            var old = (await OperationsAsync()).ShouldHaveSingleItem();
            old.Active.ShouldBeTrue(); old.Id.ShouldBe(A.Id); old.ConcurrencyToken.ShouldBe(A.ConcurrencyToken);
            Evidence(old).ShouldBe(Evidence(A));
            (await TaskAsync()).ActiveLandingId.ShouldBe(A.Id);
            await AssertPinsAsync();
            (await ReadAsync(F.Source, "rev-parse", "HEAD")).Trim().ShouldBe(SourceSha);
            (await ReadAsync(F.Repository, "rev-parse", F.TargetRef)).Trim().ShouldBe(A.TargetBeforeSha);
        }
        public async Task AssertReplacementAsync(AgentTaskLanding b)
        {
            var rows = await OperationsAsync();
            rows.Count.ShouldBe(2);
            rows.Single(o => o.Active).Id.ShouldBe(b.Id);
            (await TaskAsync()).ActiveLandingId.ShouldBe(b.Id);
            b.Id.ShouldNotBe(A.Id); b.Mode.ShouldBe(LandOperationMode.Fresh);
            b.RecoveryRefPrefix.ShouldBe($"refs/antiphon/land/{F.TaskId:N}/{b.Id:N}");
            var old = rows.Single(o => o.Id == A.Id);
            old.Active.ShouldBeFalse(); Evidence(old).ShouldBe(Evidence(A));
            await AssertPinsAsync();
            var events = await EventsAsync();
            foreach (var oldEvent in _oldEvents)
                JsonSerializer.Serialize(events.Single(e => e.Id == oldEvent.Id)).ShouldBe(JsonSerializer.Serialize(oldEvent));
            _freshBoundary.ShouldBeTrue("B must have fresh authority at its first committed pin boundary");
        }
        public async Task AssertSettledAsync(AgentTaskLanding op)
        {
            var task = await TaskAsync();
            task.LandRequestedAt.ShouldBeNull(); task.LandStartedAt.ShouldBeNull(); task.LandVerifyFilter.ShouldBeNull();
            H.Queue.IsActive(F.TaskId).ShouldBeFalse();
            var terminal = (await EventsAsync()).Last(e => e.IsLandTerminal);
            terminal.LandingOperationId.ShouldBe(op.Id);
            terminal.LandingMode.ShouldBe(op.Mode);
            terminal.LandingPublication.ShouldBe(op.Publication);
            terminal.LandingCleanup.ShouldBe(op.Cleanup);
        }
        public async Task AssertSuccessAsync(AgentTaskLanding b, string? target = null)
        {
            new AgentTaskLandingState().HasPublication(b).ShouldBeTrue();
            b.Phase.ShouldBe(LandPhase.Complete); b.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            var remote = (await ReadAsync(F.Remote, "rev-parse", F.TargetRef)).Trim();
            remote.ShouldBe(b.ObservedRemoteTargetSha);
            var observed = "refs/antiphon-observer/retry/" + Guid.NewGuid().ToString("N");
            await ReadAsync(F.Observer, "fetch", "--no-tags", F.Remote, F.TargetRef + ":" + observed);
            await ReadAsync(F.Observer, "merge-base", "--is-ancestor", b.VerifiedSourceSha!, observed);
            if (target is not null) await ReadAsync(F.Observer, "merge-base", "--is-ancestor", target, observed);
            Directory.Exists(F.Source).ShouldBeFalse();
            (await ReadAsync(F.Repository, "worktree", "list", "--porcelain")).Replace('\\', '/').ShouldNotContain(F.Source.Replace('\\', '/'));
            (await Reader().RunAsync(F.Repository, ["show-ref", "--verify", F.SourceRef], CancellationToken.None)).Succeeded.ShouldBeFalse();
            await F.AssertRemoteSourceAsync();
            await AssertSettledAsync(b);
            var terminal = (await EventsAsync()).Last(e => e.IsLandTerminal);
            terminal.Type.ShouldBe(b.Publication == LandPublicationOutcome.AlreadyPresent ? AgentTaskEventType.AlreadyPresent : AgentTaskEventType.Landed);
        }
        private static void AssertNoReceipts(AgentTaskLanding op)
        {
            op.VerifiedSourceSha.ShouldBeNull(); op.VerifiedAt.ShouldBeNull(); op.VerificationStartedAt.ShouldBeNull();
            op.VerificationCommand.ShouldBeNull(); op.VerificationSkipReason.ShouldBeNull(); op.VerificationPassed.ShouldBeFalse();
            op.PushStartedAt.ShouldBeNull(); op.PushExitCode.ShouldBeNull(); op.RemoteConfirmedAt.ShouldBeNull();
            op.ConfirmationMethod.ShouldBeNull(); op.ObservedRemoteTargetSha.ShouldBeNull(); op.LocalTargetAfterSha.ShouldBeNull();
            op.CleanupStartedAt.ShouldBeNull(); op.CleanupCompletedAt.ShouldBeNull(); op.ExpectedDeletionSha.ShouldBeNull();
            op.DirectoryRemoved.ShouldBeFalse(); op.RegistrationRemoved.ShouldBeFalse(); op.BranchRemoved.ShouldBeFalse();
            op.ChildOperation.ShouldBeNull(); op.ChildProcessId.ShouldBeNull(); op.ChildProcessStartTicks.ShouldBeNull();
        }
        // The land worktree is the land's own disposable scratch checkout; resetting it mutates no operator state.
        public void AssertNoMutation() => Trace.Where(t => !InLand(t.Repository)).ShouldNotContain(t => t.Args.Contains("rebase") || t.Args.Contains("merge")
            || t.Args[0] == "push" || t.Args.Contains("remove") || t.Args.Contains("--delete") || t.Args.Contains("-d")
            || t.Args.Any(a => a == "clean" || a == "stash" || a == "reset")
            || t.Args[0] == "update-ref" && t.Args.Contains(F.TargetRef));
        public async Task AssertRefusalAsync(AgentTaskLanding b)
        {
            b.Phase.ShouldBe(LandPhase.Refused); b.Publication.ShouldBe(LandPublicationOutcome.Refused);
            new AgentTaskLandingState().HasPublication(b).ShouldBeFalse(); b.Cleanup.ShouldBe(LandCleanupStatus.NotStarted);
            AssertNoReceipts(b); b.RebaseStartedAt.ShouldBeNull();
            H.Verifier.Calls.ShouldBe(0); AssertNoMutation();
            (await ReadAsync(F.Source, "rev-parse", "HEAD")).Trim().ShouldBe(SourceSha);
            (await ReadAsync(F.Repository, "rev-parse", F.SourceRef)).Trim().ShouldBe(SourceSha);
            (await ReadAsync(F.Repository, "worktree", "list", "--porcelain")).Replace('\\', '/').ShouldContain(F.Source.Replace('\\', '/'));
            (await File.ReadAllTextAsync(Path.Combine(F.Source, "feature.txt"))).ShouldBe("valuable feature\n");
            (await ReadAsync(F.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(A.TargetBeforeSha);
            (await ReadAsync(F.Repository, "rev-parse", F.TargetRef)).Trim().ShouldBe(A.TargetBeforeSha);
            if (File.Exists(Sentinel)) (await File.ReadAllTextAsync(Sentinel)).ShouldBe(SentinelBytes);
            (await ReadAsync(F.Remote, "rev-parse", F.TargetRef)).Trim().ShouldBe(F.SeedSha);
            await F.AssertRemoteSourceAsync();
            await AssertSettledAsync(b);
        }
        public ValueTask DisposeAsync() => H.DisposeAsync();
    }
}
