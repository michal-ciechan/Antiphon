using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceRebindingTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Block(Guid subject, string sha = Sha) =>
        $"--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {sha}\nreviewedSourceClean: true\nordinaryScopeCompleted: Full";

    [Test]
    public async Task C1043_StandaloneAndOid()
    {
        await using var w = await World.CreateAsync();
        var block = Block(w.Subject.Id);
        foreach (var text in new[] { "```\n" + block + "\n```", "> " + block.Replace("\n", "\n> "),
                     block + "\n" + block, "--- next stage ---\nnext: review\n" + block })
        {
            var c = await w.PrepareAsync(text);
            c.Bound.ShouldBeFalse("G10");
            if (text == block + "\n" + block) c.Warnings.ShouldContain("review_evidence_duplicate", "G110");
            if (text.StartsWith("--- next stage")) c.Warnings.ShouldContain("review_evidence_after_next_stage", "G111");
        }
        foreach (var sha in new[] { "deadbee", Sha + "+dirty:" + Fingerprint })
            (await w.PrepareAsync(Block(w.Subject.Id, sha))).Bound.ShouldBeFalse("G11");
        foreach (var sha in new[] { Sha.ToUpperInvariant(), Sha64.ToUpperInvariant() })
        {
            w.Git.Observation = new(ProgressRemoteState.Present, sha.ToLowerInvariant(), Fingerprint);
            var c = await w.PrepareAsync(Block(w.Subject.Id, sha));
            c.Bound.ShouldBeTrue("G11");
            c.ReviewedSourceSha.ShouldBe(sha.ToLowerInvariant(), "G11");
            c.SubjectTaskId.ShouldBe(w.Subject.Id);
        }
    }

    [Test]
    public async Task C1043_CleanAndScope()
    {
        await using var w = await World.CreateAsync();
        var block = Block(w.Subject.Id);
        foreach (var text in new[] { block.Replace("reviewedSourceClean: true", "reviewedSourceClean: false"),
                     block.Replace("reviewedSourceClean: true\n", ""),
                     block.Replace("reviewedSourceClean: true", "reviewedSourceClean: true\nreviewedSourceClean: true") })
            (await w.PrepareAsync(text)).Bound.ShouldBeFalse("G12");
        foreach (var text in new[] { block.Replace("ordinaryScopeCompleted: Full", ""),
                     block.Replace("ordinaryScopeCompleted: Full", "ordinaryScopeCompleted: 3"),
                     block + "\nordinaryScopeCompleted: Full" })
            (await w.PrepareAsync(text)).Scope.ShouldBe(VerificationScope.Unknown, "G13");
        w.Review.VerificationRound = VerificationRound.Interim;
        (await w.PrepareAsync(block)).Scope.ShouldBe(VerificationScope.Interim, "G14");
        foreach (var scope in new[] { VerificationScope.Interim, VerificationScope.None, VerificationScope.Unknown })
            (await w.PrepareAsync(block.Replace("ordinaryScopeCompleted: Full", "ordinaryScopeCompleted: " + scope)))
                .Scope.ShouldBe(scope, "declared-scope");
    }

    [Test]
    public async Task C1043_StoredBodyTokenBoundary()
    {
        await using var w = await World.CreateAsync();
        var report = "retained example\r\n[antiphon-report:example done]\r\n" + Block(w.Subject.Id).Replace("\n", "\r\n");
        var c = await w.PrepareAsync(report, stored: true);
        c.Bound.ShouldBeTrue("G15");
        c.ReviewedSourceSha.ShouldBe(Sha, "G15");
        c.ReportSha256.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(report))), "exact-stored-bytes");
        c.ReportSha256.ShouldNotBe(ReviewEvidenceBindingService.ReportDigest(report.ReplaceLineEndings("\n")));
    }

    [Test]
    public async Task C1043_RawTokenBoundary()
    {
        await using var w = await World.CreateAsync();
        var report = "final body\n[antiphon-report:example done]\n" + Block(w.Subject.Id);
        (await w.PrepareAsync(report)).Bound.ShouldBeFalse("G16");
        (await w.PrepareAsync(Block(w.Subject.Id) + "\n[antiphon-report:example done]"))
            .Bound.ShouldBeTrue("valid-raw-report");
    }

    [Test]
    public async Task C1043_SubjectAuthorization()
    {
        await using var w = await World.CreateAsync();
        (await w.PrepareAsync(Block(Guid.NewGuid()))).Bound.ShouldBeFalse("G17");
        foreach (var workspace in new[] { WorkspaceMode.Shared, WorkspaceMode.ReadOnly })
        {
            w.Subject.Workspace = workspace;
            await w.Db.SaveChangesAsync();
            (await w.PrepareAsync()).Bound.ShouldBeFalse("G17");
        }
        w.Subject.Workspace = WorkspaceMode.Worktree;
        await w.Db.SaveChangesAsync();
        w.Review.FollowUpOfTaskId = Guid.NewGuid();
        (await w.PrepareAsync()).Bound.ShouldBeFalse("G18");
        w.Review.FollowUpOfTaskId = w.Subject.Id;
        (await w.PrepareAsync()).Bound.ShouldBeTrue("authorized-followup");
        w.Review.FollowUpOfTaskId = null;
        foreach (var card in new Guid?[] { Guid.NewGuid(), null })
        {
            w.Review.CardId = card;
            (await w.PrepareAsync()).Bound.ShouldBeFalse("G19");
        }
    }

    [Test]
    public async Task C1043_SubjectRepositoryAndRef()
    {
        await using var w = await World.CreateAsync();
        var c = await w.PrepareAsync();
        c.ReviewedSourceRef.ShouldBe("refs/heads/subject", "G21");
        c.ReviewedSourceRef.ShouldNotBe("refs/heads/review", "G21");
        c.ReviewedSourceRef.ShouldNotBe("refs/heads/adoption-owner", "G21");
        c.ReviewedRepositoryPath.ShouldBe(w.Subject.RepoPath);
        w.Review.RepoPath = Path.Combine(Path.GetTempPath(), "foreign-repo");
        (await w.PrepareAsync()).Bound.ShouldBeFalse("G20");
    }

    [Test]
    public async Task C1043_CurrentSyncWitness()
    {
        await using var w = await World.CreateAsync();
        w.Review.RunnerId = "fixture-runner";
        var good = new RemoteSettlementSyncResult(RemoteSettlementSyncState.Synchronized,
            FullRef: "refs/heads/review", RemoteSha: Sha, DesktopAfterSha: Sha, MirrorDirty: false);
        foreach (var bad in new RemoteSettlementSyncResult?[] { null, good with { DesktopAfterSha = null },
                     good with { DesktopAfterSha = new string('b', 40) } })
            (await w.PrepareAsync(sync: bad)).Bound.ShouldBeFalse("G22");
        foreach (var state in new[] { RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncState.Refused,
                     RemoteSettlementSyncState.NotApplicable, (RemoteSettlementSyncState)999 })
            (await w.PrepareAsync(sync: good with { State = state })).Bound.ShouldBeFalse("G23");
        foreach (var dirty in new bool?[] { true, null })
            (await w.PrepareAsync(sync: good with { MirrorDirty = dirty })).Bound.ShouldBeFalse("G24");
        (await w.PrepareAsync(sync: good with { FullRef = "refs/heads/other" })).Bound.ShouldBeFalse("G25");
        foreach (var state in new[] { RemoteSettlementSyncState.Synchronized, RemoteSettlementSyncState.NoPushedProgress })
        {
            var c = await w.PrepareAsync(sync: good with { State = state });
            c.Bound.ShouldBeTrue("confirmed-current-turn");
            c.ConfirmedReviewSha.ShouldBe(Sha);
        }
    }

    [Test]
    public async Task C1043_FreshSubjectWitness()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var leases = new RepositoryMutationLease(fixture.Git);
        var realGit = new TaskProgressGit(leases);
        await using var w = await World.CreateAsync(fixture.Repository, fixture.SourceRef, fixture.SeedSha,
            await realGit.EndpointFingerprintAsync(fixture.Repository, CancellationToken.None));
        var binder = new ReviewEvidenceBindingService(w.Db, realGit);
        var report = Block(w.Subject.Id, fixture.SeedSha);
        var positive = await binder.PrepareRepairAsync(w.Review, StageOutcomeKind.Clean, report, false, null, null, CancellationToken.None);
        positive.Bound.ShouldBeTrue("real-exact-ref");
        positive.ObservedSubjectSha.ShouldBe(fixture.SeedSha);
        w.Subject.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(1,
            CompletionProgressAssessment.ProgressObserved, RemoteSync: new(1, RemoteSettlementSyncState.Synchronized, ConfirmedSha: fixture.SeedSha)));
        await w.Db.SaveChangesAsync();
        foreach (var state in new[] { ProgressRemoteState.Missing, ProgressRemoteState.Unavailable })
        {
            w.Git.Observation = new(state, EndpointFingerprint: w.Fingerprint);
            (await w.PrepareAsync(report)).Bound.ShouldBeFalse("G27");
        }
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "moved.txt"), "new commit\n");
        await fixture.RequiredAsync(fixture.Source, "add", "moved.txt");
        await fixture.RequiredAsync(fixture.Source, "commit", "-m", "move source after report");
        await fixture.RequiredAsync(fixture.Source, "push", "origin", fixture.SourceRef);
        var moved = await binder.PrepareRepairAsync(w.Review, StageOutcomeKind.Clean, report, false, null, null, CancellationToken.None);
        moved.Bound.ShouldBeFalse("G28");
        moved.Warnings.ShouldContain("review_evidence_subject_ref_mismatch", "G28");
    }

    [Test]
    public async Task C1043_EndpointWitness()
    {
        await using var w = await World.CreateAsync();
        w.Git.ActualFingerprint = new string('b', 64);
        var c = await w.PrepareAsync();
        w.Git.ExpectedFingerprints.ShouldBe(new[] { Fingerprint }, "G29");
        c.Bound.ShouldBeFalse("G29");
        w.Git.RequestedRefs.ShouldBe(new[] { "refs/heads/subject" }, "G29");
        w.Subject.ProgressBaselineJson = null;
        await w.Db.SaveChangesAsync();
        w.Git.RequestedRefs.Clear();
        (await w.PrepareAsync()).Bound.ShouldBeFalse("G29");
        w.Git.RequestedRefs.ShouldBeEmpty("G29");
    }

    [Test]
    public async Task C1043_ObservationUsesExistingLease()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var leases = new RepositoryMutationLease(fixture.Git);
        var git = new WitnessGit(leases) { Real = true, LeaseExpected = true };
        var fingerprint = await git.EndpointFingerprintAsync(fixture.Repository, CancellationToken.None);
        await using var w = await World.CreateAsync(fixture.Repository, fixture.SourceRef, fixture.SeedSha, fingerprint);
        await using var lease = (await leases.TryAcquireAsync(fixture.Repository, CancellationToken.None)).ShouldNotBeNull();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var c = await new ReviewEvidenceBindingService(w.Db, git).PrepareRepairAsync(w.Review, StageOutcomeKind.Clean,
            Block(w.Subject.Id, fixture.SeedSha), false, null, lease, deadline.Token);
        git.NestedAcquireCalls.ShouldBe(0, "G30");
        git.UnderLeaseCalls.ShouldBe(1, "G30");
        c.Bound.ShouldBeTrue("G30");
        leases.Owns(lease, lease.CommonDirectory).ShouldBeTrue("lease-still-owned");
    }

    [Test]
    public async Task C1043_ActiveReaders()
    {
        await using var w = await World.CreateAsync();
        foreach (var kind in new[] { StageOutcomeKind.Found, StageOutcomeKind.Clean })
        {
            w.Db.StageOutcomes.RemoveRange(await w.Db.StageOutcomes.ToListAsync());
            var old = Row(w.Review.Id, w.Subject.Id, StageOutcomeKind.Clean, DateTime.UtcNow.AddMinutes(-1));
            old.ReviewedSourceSha = Sha;
            var replacement = Row(w.Review.Id, w.Subject.Id, kind, DateTime.UtcNow, old.Id);
            if (kind == StageOutcomeKind.Found) replacement.ReviewedSourceSha = Sha;
            w.Db.StageOutcomes.AddRange(old, replacement);
            await w.Db.SaveChangesAsync();
            await using var reader = w.FreshContext();
            (await LandCompletionFacts.LoadReviewAsync(reader, w.Review, CancellationToken.None)).ShouldBeNull("G31");
            StageOutcomeService.ActiveReview(new[] { old, replacement }, w.Review.Id)!.Id.ShouldBe(replacement.Id);
            (await reader.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == old.Id)).ReviewedSourceSha.ShouldBe(Sha);
        }
    }

    [Test]
    public async Task C1043_ActiveSummary()
    {
        await using var w = await World.CreateAsync();
        var old = Row(w.Review.Id, null, StageOutcomeKind.Clean, DateTime.UtcNow.AddMinutes(-1));
        var replacement = Row(w.Review.Id, w.Subject.Id, StageOutcomeKind.Clean, DateTime.UtcNow, old.Id);
        replacement.ReviewedSourceSha = Sha;
        w.Db.StageOutcomes.AddRange(old, replacement);
        await w.Db.SaveChangesAsync();
        await using var reader = w.FreshContext();
        var latest = await new StageOutcomeService(reader).ListAsync(null, null, "Review", null, true, CancellationToken.None);
        latest.Summary.ShouldHaveSingleItem().Runs.ShouldBe(1, "G33");
        latest.Rows.ShouldHaveSingleItem().Id.ShouldBe(replacement.Id, "G33");
        StageOutcomeService.LatestPerTaskStage(new[] { old, replacement }).ShouldHaveSingleItem().Id.ShouldBe(replacement.Id, "G33");
        var all = await new StageOutcomeService(reader).ListAsync(null, null, "Review", null, false, CancellationToken.None);
        all.Rows.Count.ShouldBe(2, "history-preserved");
    }

    [Test]
    public async Task C1043_FilteredSummary()
    {
        await using var w = await World.CreateAsync();
        var now = DateTime.UtcNow;
        var old = Row(w.Review.Id, null, StageOutcomeKind.Clean, now.AddMinutes(-2));
        old.CardId = w.Review.CardId;
        var replacement = Row(w.Review.Id, w.Subject.Id, StageOutcomeKind.Found, now, old.Id);
        replacement.CardId = Guid.NewGuid();
        w.Db.StageOutcomes.AddRange(old, replacement);
        await w.Db.SaveChangesAsync();
        await using var reader = w.FreshContext();
        var service = new StageOutcomeService(reader);
        foreach (var args in new[] { (Until: (DateTime?)now.AddMinutes(-1), Card: (Guid?)null),
                     (Until: (DateTime?)null, Card: w.Review.CardId) })
        {
            (await service.ListAsync(null, args.Until, null, args.Card, true, CancellationToken.None))
                .Rows.ShouldBeEmpty("G34");
            (await service.ListAsync(null, args.Until, null, args.Card, false, CancellationToken.None))
                .Rows.ShouldHaveSingleItem().Id.ShouldBe(old.Id, "filtered-history");
        }
    }

    [Test]
    public async Task C1043_EqualTimestampOrdering()
    {
        await using var w = await World.CreateAsync();
        var at = DateTime.UtcNow;
        var low = Row(w.Review.Id, w.Subject.Id, StageOutcomeKind.Clean, at);
        low.Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        low.ReviewedSourceSha = new string('b', 40);
        var high = Row(w.Review.Id, w.Subject.Id, StageOutcomeKind.Clean, at);
        high.Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        high.ReviewedSourceSha = Sha;
        w.Db.StageOutcomes.AddRange(high, low);
        await w.Db.SaveChangesAsync();
        for (var i = 0; i < 3; i++)
        {
            await using var reader = w.FreshContext();
            (await LandCompletionFacts.LoadReviewAsync(reader, w.Review, CancellationToken.None))!.Id.ShouldBe(high.Id, "G35");
            var latest = await new StageOutcomeService(reader).ListAsync(null, null, null, null, true, CancellationToken.None);
            latest.Rows.ShouldHaveSingleItem().Id.ShouldBe(high.Id, "G35");
            StageOutcomeService.ActiveReview(new[] { low, high }, w.Review.Id)!.Id.ShouldBe(high.Id, "G35");
        }
    }

    [Test]
    public void C1043_ProvenanceBounds()
    {
        var record = new ReviewRebindProvenance("recovery", new string('A', 64),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            Sha, Sha, "operator:");
        var padding = 1000 - record.Serialize().Length;
        var exact = record with { Actor = record.Actor + new string('x', padding) };
        var wire = exact.Serialize();
        wire.Length.ShouldBe(1000, "G61");
        var parsed = ReviewRebindProvenance.TryParse(wire).ShouldNotBeNull("G61");
        parsed.Mode.ShouldBe("recovery");
        parsed.SourceEventId.ShouldBe(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        parsed.Actor!.Length.ShouldBe(9 + padding, "G61");
        parsed.ObservedSubjectSha.ShouldBe(Sha);
        Should.Throw<ArgumentException>(() => (exact with { Actor = exact.Actor + "x" }).Serialize(), "G61");
        ReviewRebindProvenance.TryParse(wire + "x").ShouldBeNull("G61");
        ReviewRebindProvenance.TryParse("review-rebind-v1:{}").ShouldBeNull();
    }

    private static StageOutcome Row(Guid review, Guid? subject, StageOutcomeKind kind, DateTime at, Guid? supersedes = null) => new()
    {
        Id = Guid.NewGuid(), StageTaskId = review, SubjectTaskId = subject, Stage = OrchestrationStage.Review,
        Outcome = kind, Source = StageOutcomeSource.Delegate, RecordedAt = at, SupersedesId = supersedes,
    };

    private sealed class World : IAsyncDisposable
    {
        public IsolatedTestSchema Schema { get; private set; } = null!;
        public AppDbContext Db { get; private set; } = null!;
        public AgentTask Subject { get; private set; } = null!;
        public AgentTask Review { get; private set; } = null!;
        public WitnessGit Git { get; } = new();
        public string Fingerprint { get; private set; } = ReviewEvidenceRebindingTests.Fingerprint;
        public string Report { get; private set; } = "";
        public static async Task<World> CreateAsync(string? repo = null, string sourceRef = "refs/heads/subject",
            string sha = Sha, string? fingerprint = ReviewEvidenceRebindingTests.Fingerprint)
        {
            var w = new World { Schema = await TestDbFixture.CreateIsolatedSchemaAsync() };
            w.Db = w.FreshContext();
            repo ??= Path.Combine(Path.GetTempPath(), "c1043-binder-repo");
            var card = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var project = new Project { Id = Guid.NewGuid(), Name = "C1043 binding", CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C1043 binding", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
                CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
            w.Db.AddRange(project, board, column, new Card { Id = card, BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-1043", Title = "C1043 binding", CreatedAt = now, UpdatedAt = now });
            w.Subject = TaskRow(repo, card, AgentTaskRole.Code, sourceRef);
            w.Review = TaskRow(repo, card, AgentTaskRole.Review, "review");
            w.Review.Stage = OrchestrationStage.Review;
            w.Review.VerificationProfileVersion = 1;
            w.Review.VerificationRound = VerificationRound.Final;
            w.Fingerprint = fingerprint!;
            w.Subject.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, DateTime.UtcNow, DateTime.UtcNow,
                new(repo, repo, w.Subject.Id, null, sourceRef, sha, new(ProgressRemoteState.Present, sha, fingerprint)), null));
            w.Git.ActualFingerprint = fingerprint!;
            w.Git.Observation = new(ProgressRemoteState.Present, sha, fingerprint);
            w.Db.AgentTasks.AddRange(w.Subject, w.Review);
            await w.Db.SaveChangesAsync();
            w.Report = Block(w.Subject.Id, sha);
            return w;
        }
        public AppDbContext FreshContext() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public Task<ReviewEvidenceBindingService.Candidate> PrepareAsync(string? report = null, bool stored = false,
            RemoteSettlementSyncResult? sync = null) => new ReviewEvidenceBindingService(Db, Git)
            .PrepareRepairAsync(Review, StageOutcomeKind.Clean, report ?? Report, stored, sync, null, CancellationToken.None);
        private static AgentTask TaskRow(string repo, Guid card, AgentTaskRole role, string branch)
        {
            var id = Guid.NewGuid();
            return new() { Id = id, RootTaskId = id, Title = "C1043 binding fixture", Goal = "binding", Role = role,
                Workspace = WorkspaceMode.Worktree, CardId = card, RepoPath = repo, WorkingDirectory = repo,
                WorktreeBranch = branch, Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Schema.DisposeAsync(); }
    }

    /// <summary>Controlled invalid observations; Real=true uses the production Git/lease implementation.</summary>
    private sealed class WitnessGit(IRepositoryMutationLease? leases = null) : TaskProgressGit(leases), ITaskProgressGit
    {
        public ProgressRemoteObservation Observation { get; set; } = new(ProgressRemoteState.Present, Sha, Fingerprint);
        public string ActualFingerprint { get; set; } = Fingerprint;
        public List<string?> ExpectedFingerprints { get; } = [];
        public List<string> RequestedRefs { get; } = [];
        public bool Real { get; set; }
        public bool LeaseExpected { get; set; }
        public int NestedAcquireCalls { get; private set; }
        public int UnderLeaseCalls { get; private set; }
        Task<ProgressRemoteObservation> ITaskProgressGit.ObserveExactRefAsync(string repository, string fullRef,
            string? expectedFingerprint, Guid taskId, CancellationToken ct)
        {
            if (LeaseExpected)
            {
                NestedAcquireCalls++;
                return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Unavailable, Reason: "nested-acquire"));
            }
            if (Real) return base.ObserveExactRefAsync(repository, fullRef, expectedFingerprint, taskId, ct);
            RequestedRefs.Add(fullRef);
            ExpectedFingerprints.Add(expectedFingerprint);
            return Task.FromResult(expectedFingerprint is not null && expectedFingerprint != ActualFingerprint
                ? new(ProgressRemoteState.Unavailable, EndpointFingerprint: ActualFingerprint, Reason: "endpoint-changed") : Observation);
        }
        Task<ProgressRemoteObservation> ITaskProgressGit.ObserveExactRefUnderLeaseAsync(string repository, string fullRef,
            string? expectedFingerprint, Guid taskId, RepositoryLease lease, CancellationToken ct)
        {
            UnderLeaseCalls++;
            return base.ObserveExactRefUnderLeaseAsync(repository, fullRef, expectedFingerprint, taskId, lease, ct);
        }
    }
}
