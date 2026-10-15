using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AdvisoryChangePolicyTests
{
    [Test]
    public void C1281_AdvisoryDoesNotRerun()
    {
        var policy = new AdvisoryChangePolicy();
        foreach (var attribution in Enum.GetValues<AdvisoryAttribution>())
        {
            var assessment = new AdvisoryAssessment(AdvisoryBehavior.Satisfied,
                AdvisoryVerification.Complete, true, attribution);
            var result = policy.Decide(assessment);
            result.Action.ShouldBe(AdvisoryAction.None, "advisory-rerun-count == 0");
            result.Assessment.ShouldBe(assessment, "independent-assessment-fields");
            policy.Decide(assessment with { Behavior = AdvisoryBehavior.Substantive }).Action
                .ShouldBe(AdvisoryAction.Code, "missing-behavior-substantive");
            policy.Decide(assessment with { Verification = AdvisoryVerification.Incomplete }).Action
                .ShouldBe(AdvisoryAction.Verification, "incomplete-is-a-gate");
            policy.Decide(assessment with { Behavior = AdvisoryBehavior.Unknown }).Action
                .ShouldBe(AdvisoryAction.Review, "unclassified-needs-review");
        }
        foreach (var role in new[] { AgentTaskRole.Review, AgentTaskRole.Code })
            Compose(role).ShouldContain("No Review rerun solely for advisory", "shipped-advisory-policy");
        Compose(AgentTaskRole.Review).ShouldContain("incomplete gate", "shipped-incomplete-policy");
    }

    [Test]
    public async Task C1281_SeparateDocsIdentity()
    {
        new AdvisoryChangePolicy().CanUseSeparateDocs([Narrative()]).ShouldBeTrue("narrative-helper-eligible");
        await using var git = new LandingGitFixture();
        await git.InitializeAsync();
        var originalSha = git.SeedSha;
        // Publication is independently observable before the helper branch is created.
        (await git.RequiredAsync(git.Remote, "rev-parse", "refs/heads/master")).Trim().ShouldBe(originalSha);
        await git.RequiredAsync(git.Repository, "worktree", "add", "-b", "feat/docs-followup",
            Path.Combine(git.Root, "docs"), originalSha);
        var docsPath = Path.Combine(git.Root, "docs");
        await File.WriteAllTextAsync(Path.Combine(docsPath, "story.md"), Narrative().After);
        await git.RequiredAsync(docsPath, "add", "story.md");
        await git.RequiredAsync(docsPath, "commit", "-m", "narrative correction");
        var docsSha = (await git.RequiredAsync(docsPath, "rev-parse", "HEAD")).Trim();
        docsSha.ShouldNotBe(originalSha);
        (await git.RequiredAsync(git.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(originalSha, "reviewed-tip-unchanged");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var owner = TaskRow(git.Source, git.Repository, git.SourceRef, AgentTaskRole.Code);
        var helper = TaskRow(docsPath, git.Repository, "refs/heads/feat/docs-followup", AgentTaskRole.Docs);
        db.AgentTasks.AddRange(owner, helper);
        var evidence = Review(owner, originalSha, StageOutcomeKind.Clean);
        db.StageOutcomes.Add(evidence);
        await db.SaveChangesAsync();
        var queue = new AgentTaskLandQueue();
        var land = Land(db, queue);
        var borrowed = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(helper.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: docsSha, ReviewEvidenceId: evidence.Id), CancellationToken.None));
        borrowed.Code.ShouldBe("review_evidence_subject_mismatch", "new-sha-separate-approval");
        var accepted = await land.RequestAsync(helper.Id, new LandAgentTaskRequest(ExpectedSourceSha: docsSha), CancellationToken.None);
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
        request.ExpectedSourceSha.ShouldBe(docsSha);
        request.ReviewEvidenceId.ShouldBeNull("no-borrowed-review");
        request.ApprovalKind.ShouldBe(LandApprovalKind.ExplicitCaller);
        var latched = TaskRow(docsPath, git.Repository, "refs/heads/feat/latched", AgentTaskRole.Docs);
        latched.RequiresFinalVerificationReview = true;
        db.AgentTasks.Add(latched);
        await db.SaveChangesAsync();
        var refusal = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(latched.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: docsSha), CancellationToken.None));
        refusal.Code.ShouldBe("final_verification_review_required", "latched-helper-refused");
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == latched.Id)).ShouldBe(0);
        (await db.StageOutcomes.AsNoTracking().SingleAsync()).ReviewedSourceSha.ShouldBe(originalSha);
        Compose(AgentTaskRole.Review).ShouldContain("publish the valid reviewed tip first", "publication-before-helper");
    }

    [Test]
    public void C1281_NarrativeBoundary()
    {
        var policy = new AdvisoryChangePolicy();
        var narrative = Narrative();
        policy.CanUseSeparateDocs([narrative]).ShouldBeTrue("ordinary-narrative-admits");
        foreach (var kind in Enum.GetValues<AdvisoryContent>().Where(k => k != AdvisoryContent.Narrative))
            policy.CanUseSeparateDocs([narrative with { Content = kind }]).ShouldBeFalse($"{kind}-refused");
        // Identical Markdown paths with different inspected payloads are independent negatives.
        foreach (var (kind, payload) in new[] {
            (AdvisoryContent.Instructions, "Always run the release command."),
            (AdvisoryContent.Policy, "A failed check permits publication."),
            (AdvisoryContent.TestSelection, "Run only C1281_NarrativeBoundary."),
            (AdvisoryContent.Schema, "{\"type\":\"string\"}"),
            (AdvisoryContent.ExecutableExample, "```sh\nrelease --skip-checks\n```"),
            (AdvisoryContent.GeneratedInput, "<!-- generated input -->"),
            (AdvisoryContent.Configuration, "Enabled: true"),
            (AdvisoryContent.Code, "return true;") })
            policy.CanUseSeparateDocs([narrative, narrative with { After = payload, Content = kind }])
                .ShouldBeFalse($"{kind}-mixed-refused");
        foreach (var path in new[] { "AGENTS.md", "CLAUDE.md", "docs/testing-and-build.md",
            "docs/superpowers/plans/story.md", "server/Bundles/story.md", "tests/story.md", "../story.md", "/story.md" })
            policy.CanUseSeparateDocs([narrative with { Path = path }]).ShouldBeFalse("policy-doc-refused: " + path);
        foreach (var text in new[] { "```sh\nrelease\n```", "Use `release`.", "<!-- input -->", "[script](run.ps1)" })
            policy.CanUseSeparateDocs([narrative with { After = text }]).ShouldBeFalse("active-markup-refused");
        policy.CanUseSeparateDocs([]).ShouldBeFalse("empty-delta-refused");
        policy.CanUseSeparateDocs([narrative with { After = narrative.Before }]).ShouldBeFalse("unchanged-refused");
        Compose(AgentTaskRole.Review).ShouldContain("instructions, policy, test selections, schemas", "protected-content-policy");
    }

    [Test]
    public void C1281_CommentEquivalence()
    {
        // S2-policy intentionally has no language-aware proof implementation: even ordinary
        // comments remain on ordinary Review. No regex/extension token-equivalence claim.
        Compose(AgentTaskRole.Review).ShouldContain("Comment exemptions remain refused", "unproved-comments-refused");
        var policy = new AdvisoryChangePolicy();
        foreach (var (path, before, after) in new[] {
            ("code.cs", "// old\nreturn 1;", "// new\nreturn 1;"),
            ("code.cs", "return 1;", "return 2;"),
            ("code.cs", "#if A", "#if B"),
            ("code.cs", "[Old]", "[New]"),
            ("code.cs", "#pragma warning restore", "#pragma warning disable"),
            ("code.cs", "// <auto-generated>", "// generator: new"),
            ("code.cs", "/// <summary>Old</summary>", "/// <summary>New</summary>"),
            ("code.unknown", "# old", "# new") })
        {
            policy.CanUseSeparateDocs([new(path, before, after, AdvisoryContent.Comment)]).ShouldBeFalse("directive-refused: " + after);
            policy.CanUseSeparateDocs([new(path, before, after, AdvisoryContent.Narrative)]).ShouldBeFalse("source-cannot-be-narrative");
        }
    }

    [Test]
    public async Task C1281_HistoryPreserved()
    {
        var policy = new AdvisoryChangePolicy();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var db = Context(schema))
        {
            var row = Review(TaskRow("/tmp/source", "/tmp/repo", "refs/heads/old", AgentTaskRole.Code),
                new string('a', 40), StageOutcomeKind.Found);
            row.CostUsd = 7.48m;
            row.ResolutionCostUsd = 2.51m;
            row.Detail = "Historical advisory finding";
            db.StageOutcomes.Add(row);
            await db.SaveChangesAsync();
            foreach (var attribution in Enum.GetValues<AdvisoryAttribution>())
            {
                var decision = policy.Decide(new(AdvisoryBehavior.Satisfied, AdvisoryVerification.Complete, true, attribution));
                decision.Action.ShouldBe(AdvisoryAction.None, "historical-advisory-no-rerun");
                decision.Assessment.Attribution.ShouldBe(attribution);
                policy.Decide(decision.Assessment with { Behavior = AdvisoryBehavior.Substantive }).Action
                    .ShouldBe(AdvisoryAction.Code, "missing-acceptance-remains-substantive");
            }
        }
        await using var fresh = Context(schema);
        var persisted = await fresh.StageOutcomes.AsNoTracking().SingleAsync();
        persisted.Outcome.ShouldBe(StageOutcomeKind.Found, "historical-row-unchanged");
        persisted.CostUsd.ShouldBe(7.48m);
        persisted.ResolutionCostUsd.ShouldBe(2.51m);
        persisted.Detail.ShouldBe("Historical advisory finding");
        persisted.SupersedesId.ShouldBeNull();
        (await new StageOutcomeService(fresh).ListAsync(null, null, "Review", null, true, CancellationToken.None))
            .Summary.ShouldHaveSingleItem().Found.ShouldBe(1);
        Compose(AgentTaskRole.Review).ShouldContain("Preserve historical outcomes and costs", "history-policy");
    }

    private static AdvisoryDelta Narrative() => new("docs/stories/launch.md", "The launch was in May.",
        "The launch was in June.", AdvisoryContent.Narrative);
    private static string Compose(AgentTaskRole role) => InstructionBundleComposer.Compose(
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, role)).Text;
    private static AppDbContext Context(IsolatedTestSchema schema) => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
    private static AgentTask TaskRow(string path, string repo, string branch, AgentTaskRole role)
    {
        var id = Guid.NewGuid();
        return new() { Id = id, RootTaskId = id, Title = "Advisory follow-up", Goal = "Narrative only",
            Kind = AgentTaskKind.Worker, Role = role, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, RepoPath = repo, WorktreePath = path, WorktreeBranch = branch,
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
    }
    private static StageOutcome Review(AgentTask owner, string sha, StageOutcomeKind outcome) => new() {
        Id = Guid.NewGuid(), SubjectTaskId = owner.Id, StageTaskId = Guid.NewGuid(), Stage = OrchestrationStage.Review,
        Source = StageOutcomeSource.Delegate, Outcome = outcome, ReviewedSourceSha = sha, ReviewedSourceClean = true,
        ReviewedSourceRef = owner.WorktreeBranch, ReviewedRepositoryPath = owner.RepoPath,
        CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow };
    private static AgentTaskLandService Land(AppDbContext db, AgentTaskLandQueue queue)
    {
        var clock = TimeProvider.System;
        var worktrees = new DelegationWorktreeService(new WorktreeManager(
            Options.Create(new GitSettings { WorktreeBasePath = Path.GetTempPath() }), clock, NullLogger<WorktreeManager>.Instance),
            new GitService(NullLogger<GitService>.Instance), NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }), new MockEventBus(),
            new RecordingSessionStopper(), clock, NullLogger<AgentTaskService>.Instance);
        return new(db, worktrees, tasks, queue, null!, new MockEventBus(), clock,
            Options.Create(new DelegationSettings()), NullLogger<AgentTaskLandService>.Instance);
    }
}
