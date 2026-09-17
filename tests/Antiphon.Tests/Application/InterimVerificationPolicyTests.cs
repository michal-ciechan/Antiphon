using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-1/V-3, R-1/R-2/R-5. Interim admission through the production
/// <see cref="AgentTaskService.CreateAsync"/> with the concrete <see cref="InterimVerificationPolicy"/>,
/// an owned database, a real Git repository and a real settled Final Review baseline. Each negative
/// row varies exactly one field of an otherwise admissible request or stored fact, and every
/// assertion names its row.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class InterimVerificationPolicyTests
{
    [Test]
    public async Task C544_DefaultIsFinal()
    {
        await using var world = await C544World.CreateAsync();
        // Readiness unready and Goal text begging for a quick round: neither may select Interim.
        world.Readiness.Verdict = InterimReadiness.Unready("monitor_stale");
        var rows = new (string Row, CreateAgentTaskRequest Request, VerificationRound? Expected)[]
        {
            ("code-omitted", new("Quick interim repair, smoke only please.", Title: "quick repair", Role: AgentTaskRole.Code,
                Workspace: WorkspaceMode.Worktree, WorkingDirectory: world.Repo.Path, Card: world.Card.Id.ToString()), VerificationRound.Final),
            ("review-omitted", world.FinalReview("Interim review only, skip the full sweep."), VerificationRound.Final),
            ("code-explicit-final", new("Repair.", Title: "final repair", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                WorkingDirectory: world.Repo.Path, Card: world.Card.Id.ToString(), VerificationRound: VerificationRound.Final), VerificationRound.Final),
            ("docs-no-profile", new("Write docs.", Title: "docs", Role: AgentTaskRole.Docs, WorkingDirectory: world.Repo.Path,
                Card: world.Card.Id.ToString()), null),
        };
        foreach (var (row, request, expected) in rows)
        {
            var created = await world.CreateTaskAsync(request);
            var stored = await world.TaskAsync(created.Id);
            stored.VerificationRound.ShouldBe(expected, row);
            stored.VerificationProfileVersion.ShouldBe(expected is null ? null : 1, row);
            stored.VerificationSubjectTaskId.ShouldBeNull(row);
            stored.VerificationBaselineOutcomeId.ShouldBeNull(row);
            stored.VerificationAdmissionJson.ShouldBeNull(row);
        }
        world.Readiness.Reads.ShouldBe(0, "omitted and explicit Final never consult nightly readiness");
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeFalse("Final never latches");
    }

    [Test]
    public async Task C544_CodePermission() => await PermissionMatrixAsync(AgentTaskRole.Code);

    [Test]
    public async Task C544_ReviewPermission() => await PermissionMatrixAsync(AgentTaskRole.Review);

    /// <summary>V-1: this role x 4 card policy pairs x {omitted, Final, Interim} = 12 admission rows.</summary>
    private static async Task PermissionMatrixAsync(AgentTaskRole role)
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var pairs = new[]
        {
            (Code: CardVerificationPolicy.FullOnly, Review: CardVerificationPolicy.FullOnly),
            (Code: CardVerificationPolicy.AllowInterim, Review: CardVerificationPolicy.FullOnly),
            (Code: CardVerificationPolicy.FullOnly, Review: CardVerificationPolicy.AllowInterim),
            (Code: CardVerificationPolicy.AllowInterim, Review: CardVerificationPolicy.AllowInterim),
        };
        foreach (var pair in pairs)
        {
            await world.SetCardPolicyAsync(pair.Code, pair.Review);
            var permitted = (role == AgentTaskRole.Code ? pair.Code : pair.Review) == CardVerificationPolicy.AllowInterim;
            foreach (var mode in new[] { "omitted", "final", "interim" })
            {
                var row = $"{role} code={pair.Code} review={pair.Review} mode={mode}";
                var interim = role == AgentTaskRole.Code ? world.InterimCode(baseline.Id) : world.InterimReview(baseline.Id);
                var request = mode switch
                {
                    "interim" => interim,
                    "final" => interim with { VerificationRound = VerificationRound.Final, VerificationSubjectTaskId = null,
                        VerificationBaselineOutcomeId = null, VerificationSelection = null },
                    _ => interim with { VerificationRound = null, VerificationSubjectTaskId = null,
                        VerificationBaselineOutcomeId = null, VerificationSelection = null },
                };
                var before = await world.TaskCountAsync();
                if (mode == "interim" && !permitted)
                {
                    var refused = await Should.ThrowAsync<ConflictException>(() => world.CreateTaskAsync(request), row);
                    refused.Code.ShouldBe(InterimVerificationPolicy.InterimDisallowedCode, row);
                    refused.StatusCode.ShouldBe(409, row);
                    (await world.TaskCountAsync()).ShouldBe(before, row + ": no task on refusal");
                    continue;
                }
                var created = await world.CreateTaskAsync(request);
                (await world.TaskAsync(created.Id)).VerificationRound.ShouldBe(
                    mode == "interim" ? VerificationRound.Interim : VerificationRound.Final, row);
            }
        }
    }

    [Test]
    public async Task C544_RoleWorkspaceMatrix()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var code = world.InterimCode(baseline.Id);
        var review = world.InterimReview(baseline.Id);
        var invalid = new (string Row, CreateAgentTaskRequest Request)[]
        {
            ("code-readonly", code with { Workspace = WorkspaceMode.ReadOnly }),
            ("code-shared", code with { Workspace = WorkspaceMode.Shared }),
            ("code-default-workspace", code with { Workspace = null }),
            ("review-worktree", review with { Workspace = WorkspaceMode.Worktree }),
            ("review-shared", review with { Workspace = WorkspaceMode.Shared }),
            ("orchestrator-code", code with { Kind = AgentTaskKind.Orchestrator }),
            ("docs-interim", review with { Role = AgentTaskRole.Docs }),
            ("mutation-final", new("Mutate.", Role: AgentTaskRole.Mutation, Workspace: WorkspaceMode.Worktree,
                WorkingDirectory: world.Repo.Path, VerificationRound: VerificationRound.Final)),
            ("plan-final", new("Plan.", Role: AgentTaskRole.Plan, WorkingDirectory: world.Repo.Path,
                VerificationRound: VerificationRound.Final)),
            ("testdesign-interim", review with { Role = AgentTaskRole.TestDesign }),
            ("final-with-baseline", review with { VerificationRound = VerificationRound.Final }),
        };
        foreach (var (row, request) in invalid)
        {
            var error = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(request), row);
            error.Code.ShouldBe(InterimVerificationPolicy.RoundRoleCode, row);
            error.StatusCode.ShouldBe(422, row);
            (await world.TaskCountAsync(q => q.Where(t => t.Role != AgentTaskRole.Review))).ShouldBe(0, row + ": no task");
            await using var db = world.CreateContext();
            (await db.AgentSessions.CountAsync(s => s.DefinitionName == "c544-delegate")).ShouldBe(1, row + ": no launch");
        }
        foreach (var (row, request) in new[] { ("code-worktree", code), ("review-readonly", review) })
            (await world.TaskAsync((await world.CreateTaskAsync(request)).Id)).VerificationRound.ShouldBe(VerificationRound.Interim, row);
    }

    [Test]
    public async Task C544_BaselineScope()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        baseline.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "fixture baseline settled Full");
        foreach (var (row, scope, round) in new (string, VerificationScope?, VerificationRound?)[]
                 {
                     ("interim", VerificationScope.Interim, VerificationRound.Final),
                     ("unknown", VerificationScope.Unknown, VerificationRound.Final),
                     ("none", VerificationScope.None, VerificationRound.Final),
                     ("legacy-null", null, null),
                     ("interim-round-full-scope", VerificationScope.Full, VerificationRound.Interim),
                 })
        {
            await UpdateOutcomeAsync(world, baseline.Id, o => { o.OrdinaryScopeCompleted = scope; o.CommissionedRound = round; });
            await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, row);
        }
        await UpdateOutcomeAsync(world, baseline.Id, o => { o.OrdinaryScopeCompleted = VerificationScope.Full; o.CommissionedRound = VerificationRound.Final; });
        await world.CreateTaskAsync(world.InterimReview(baseline.Id));
    }

    [Test]
    public async Task C544_BaselineCompletion()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var reviewTaskId = baseline.StageTaskId!.Value;
        foreach (var (row, status, completed) in new (string, AgentTaskStatus, bool)[]
                 {
                     ("failed", AgentTaskStatus.Failed, true),
                     ("canceled", AgentTaskStatus.Canceled, true),
                     ("queued", AgentTaskStatus.Queued, false),
                     ("working", AgentTaskStatus.Working, false),
                     ("succeeded-incomplete", AgentTaskStatus.Succeeded, false),
                 })
        {
            await UpdateTaskAsync(world, reviewTaskId, t => { t.Status = status; t.CompletedAt = completed ? DateTime.UtcNow : null; });
            await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, row);
        }
        await UpdateTaskAsync(world, reviewTaskId, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; });

        // A Found Full review establishes a baseline: it may have found the very regressions repaired.
        var found = await world.SettleReviewAsync(found: true, next: "code");
        found.Outcome.ShouldBe(StageOutcomeKind.Found, "found-full fixture");
        found.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "found-full fixture");
        (await world.TaskAsync((await world.CreateTaskAsync(world.InterimReview(found.Id))).Id))
            .VerificationBaselineOutcomeId.ShouldBe(found.Id, "found-full admits");
    }

    [Test]
    public async Task C544_BaselineProvenance()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        foreach (var (row, source) in new[]
                 {
                     ("manual-orchestrator-override", StageOutcomeSource.Orchestrator),
                     ("backfill", StageOutcomeSource.Backfill),
                     ("server", StageOutcomeSource.Server),
                 })
        {
            await UpdateOutcomeAsync(world, baseline.Id, o => o.Source = source);
            await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, row);
        }

        // A real manual override appended through StageOutcomeService is ineligible as well.
        await UpdateOutcomeAsync(world, baseline.Id, o => o.Source = StageOutcomeSource.Delegate);
        await using (var db = world.CreateContext())
        {
            var manual = await new StageOutcomeService(db).RecordFindingAsync(baseline.StageTaskId!.Value,
                new RecordStageFindingRequest("Review", Found: false, Detail: "manual clean"), CancellationToken.None);
            await ExpectRefusedAsync(world, world.InterimReview(manual.Id), InterimVerificationPolicy.BaselineInvalidCode, "real-manual-override-row");
        }
    }

    [Test]
    public async Task C544_BaselineOwner()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var other = await SeedOwnerAsync(world, world.Card.Id, world.Project.Id);
        await UpdateOutcomeAsync(world, baseline.Id, o => o.SubjectTaskId = other.Id);
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, "same-card-foreign-owner");
    }

    [Test]
    public async Task C544_BaselineCard()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var otherCard = await SeedCardAsync(world, "CARD-0999");
        await UpdateOutcomeAsync(world, baseline.Id, o => o.CardId = otherCard);
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, "foreign-card-same-repo-project");
    }

    [Test]
    public async Task C544_BaselineRepository()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        await UpdateOutcomeAsync(world, baseline.Id, o => o.ReviewedRepositoryPath = Path.Combine(Path.GetTempPath(), "c544-foreign-repo"));
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, "foreign-repository");
    }

    [Test]
    public async Task C544_BaselineProject()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var reviewTaskId = baseline.StageTaskId!.Value;
        var foreignProject = await SeedProjectAsync(world);
        foreach (var (row, project) in new (string, Guid?)[] { ("baseline-null-task-project", null), ("foreign-project", foreignProject) })
        {
            await UpdateTaskAsync(world, reviewTaskId, t => t.ProjectId = project);
            await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, row);
        }

        // Null matches null only: an all-null owner/baseline/request admits.
        await UpdateTaskAsync(world, reviewTaskId, t => t.ProjectId = null);
        await UpdateTaskAsync(world, world.Owner.Id, t => t.ProjectId = null);
        var nullCaller = new AgentTaskService.Caller(null, world.CallerSessionId, world.Repo.Path);
        var admitted = await world.CreateTaskAsync(world.InterimReview(baseline.Id), nullCaller);
        (await world.TaskAsync(admitted.Id)).ProjectId.ShouldBeNull("null-null");
        // ...but a non-null request project no longer matches that null owner/baseline.
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BaselineInvalidCode, "request-project-vs-null");
    }

    [Test]
    public async Task C544_SubjectLinks()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var other = await SeedOwnerAsync(world, world.Card.Id, world.Project.Id);
        await ExpectRefusedAsync(world, world.InterimCode(baseline.Id) with { RepairSourceTaskId = other.Id },
            InterimVerificationPolicy.BaselineInvalidCode, "repair-source-names-another-owner");

        var foreignPrior = await world.CreateTaskAsync(world.FinalReview("Review the other owner."));
        await UpdateTaskAsync(world, foreignPrior.Id, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; t.VerificationSubjectTaskId = other.Id; });
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id) with { FollowUpOnTask = foreignPrior.Id.ToString("D") },
            InterimVerificationPolicy.BaselineInvalidCode, "follow-up-of-another-owner");

        var ownPrior = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await UpdateTaskAsync(world, ownPrior.Id, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; });
        var linked = await world.CreateTaskAsync(world.InterimReview(baseline.Id) with { FollowUpOnTask = ownPrior.Id.ToString("D") });
        (await world.TaskAsync(linked.Id)).VerificationSubjectTaskId.ShouldBe(world.Owner.Id, "follow-up-of-own-owner");
    }

    [Test]
    public async Task C544_SelectionPath()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var outside = Path.Combine(Path.GetTempPath(), "c544-outside.md");
        await File.WriteAllTextAsync(outside, C544World.SelectionMarkdown);
        var blob = await HashObjectAsync(world.Repo.Path, outside);
        await world.Repo.GitAsync("update-index", "--add", "--cacheinfo", $"120000,{blob},docs/plans/c544-link.md");
        await world.Repo.GitAsync("commit", "-m", "link escape");
        var linkSha = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var rows = new (string Row, VerificationSelectionReference Selection)[]
        {
            ("absolute", new(outside, world.SelectionSha, C544World.Section)),
            ("rooted-slash", new("/docs/plans/c544-selection.md", world.SelectionSha, C544World.Section)),
            ("traversal", new("docs/../README.md", world.SelectionSha, C544World.Section)),
            ("sibling-escape", new("../c544-world-sibling/docs/x.md", world.SelectionSha, C544World.Section)),
            ("backslash", new("docs\\plans\\c544-selection.md", world.SelectionSha, C544World.Section)),
            ("not-docs", new("README.md", world.SelectionSha, C544World.Section)),
            ("drive", new("C:docs/plans/c544-selection.md", world.SelectionSha, C544World.Section)),
            ("link-escape", new("docs/plans/c544-link.md", linkSha, C544World.Section)),
        };
        foreach (var (row, selection) in rows)
        {
            var before = await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim));
            var error = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(world.InterimReview(baseline.Id) with { VerificationSelection = selection }), row);
            error.Code.ShouldBe(InterimVerificationPolicy.SelectionInvalidCode, row);
            (await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim))).ShouldBe(before, row);
        }
        File.Delete(outside);
    }

    [Test]
    public async Task C544_SelectionRevision()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "docs", "plans", "c544-dirty.md"), C544World.SelectionMarkdown);
        Directory.CreateDirectory(Path.Combine(world.Repo.Path, "docs", "plans"));
        await world.Repo.CommitFileAsync(Path.Combine("docs", "plans", "c544-empty.md"),
            "# Empty\n\n## Round selection\n\n| V/R ID | project |\n|---|---|\n\n## Next\n");
        var emptySha = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, C544World.SelectionPath), "# working tree edit only\n");
        var rows = new (string Row, VerificationSelectionReference Selection)[]
        {
            ("object-not-in-repo", new(C544World.SelectionPath, new string('9', 40), C544World.Section)),
            ("short-sha", new(C544World.SelectionPath, world.SelectionSha[..12], C544World.Section)),
            ("path-absent-at-commit", new(C544World.SelectionPath, world.BaseSha, C544World.Section)),
            ("dirty-only-file", new("docs/plans/c544-dirty.md", world.SelectionSha, C544World.Section)),
            ("missing-section", new(C544World.SelectionPath, world.SelectionSha, "No such section")),
            ("empty-section-name", new(C544World.SelectionPath, world.SelectionSha, " ")),
            ("empty-rows", new("docs/plans/c544-empty.md", emptySha, C544World.Section)),
        };
        foreach (var (row, selection) in rows)
        {
            var error = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(world.InterimReview(baseline.Id) with { VerificationSelection = selection }), row);
            error.Code.ShouldBe(InterimVerificationPolicy.SelectionInvalidCode, row);
        }
        // The committed object wins over the (now unrelated) working-tree edit; no HEAD fallback.
        var admitted = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        var admission = VerificationAdmission.TryRead((await world.TaskAsync(admitted.Id)).VerificationAdmissionJson).ShouldNotBeNull("committed");
        admission.Selection.ArtifactCommitSha.ShouldBe(world.SelectionSha, "committed");
    }

    [Test]
    public async Task C544_CodeLatchAtomic()
    {
        var fault = new LatchFault { FailTaskInsert = true };
        await using var world = await C544World.CreateAsync(fault);
        fault.Armed = false;
        var baseline = await world.SettleReviewAsync();
        fault.Armed = true;
        var failed = await Should.ThrowAsync<IOException>(() => world.CreateTaskAsync(world.InterimCode(baseline.Id)), "insert-fault");
        failed.Message.ShouldContain("task insert");
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeFalse("insert-fault: no latch escapes");
        (await world.TaskCountAsync(q => q.Where(t => t.Role == AgentTaskRole.Code))).ShouldBe(0, "insert-fault: no task");

        fault.Armed = false;
        var created = await world.CreateTaskAsync(world.InterimCode(baseline.Id));
        await using var fresh = world.CreateContext();
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Owner.Id)).RequiresFinalVerificationReview
            .ShouldBeTrue("admitted: committed latch");
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).VerificationRound.ShouldBe(VerificationRound.Interim);
    }

    [Test]
    public async Task C544_ReviewLatchAtomic()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeFalse("before");
        var created = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await using var fresh = world.CreateContext();
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Owner.Id)).RequiresFinalVerificationReview
            .ShouldBeTrue("review admitted: committed latch");
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).VerificationSubjectTaskId.ShouldBe(world.Owner.Id);
    }

    [Test]
    public async Task C544_OwnerAlreadyLanding()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        await UpdateTaskAsync(world, world.Owner.Id, t => t.LandRequestedAt = DateTime.UtcNow);
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.OwnerLandingCode, "pending-land-review");
        await ExpectRefusedAsync(world, world.InterimCode(baseline.Id), InterimVerificationPolicy.OwnerLandingCode, "pending-land-code");
        await UpdateTaskAsync(world, world.Owner.Id, t => t.LandRequestedAt = null);

        await using (var db = world.CreateContext())
        {
            db.AgentTaskLandings.Add(PublishedLanding(world));
            await db.SaveChangesAsync();
        }
        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.OwnerLandingCode, "confirmed-publication");
    }

    [Test]
    public async Task C544_AdmissionTransaction()
    {
        var fault = new LatchFault { FailLatchSave = true };
        await using var world = await C544World.CreateAsync(fault);
        fault.Armed = false;
        var baseline = await world.SettleReviewAsync();
        fault.Armed = true;
        await Should.ThrowAsync<IOException>(() => world.CreateTaskAsync(world.InterimReview(baseline.Id)), "latch-save-fault");
        fault.Throws.ShouldBe(1, "latch-save-fault");
        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
        (await fresh.AgentTasks.AsNoTracking().CountAsync(t => t.VerificationRound == VerificationRound.Interim))
            .ShouldBe(0, "latch-save-fault: zero admitted Interim tasks");
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Owner.Id)).RequiresFinalVerificationReview
            .ShouldBeFalse("latch-save-fault: no latch");
    }

    [Test]
    public async Task C544_ExplicitBaselineRequired()
    {
        await using (var first = await C544World.CreateAsync())
        {
            // First round: no full baseline exists at all.
            await ExpectRefusedAsync(first, first.InterimReview(Guid.Empty) with { VerificationBaselineOutcomeId = null },
                InterimVerificationPolicy.BaselineInvalidCode, "first-round-without-baseline");
        }

        await using var world = await C544World.CreateAsync();
        var valid = await world.SettleReviewAsync(); // a usable same-card baseline exists and must not be inferred
        var rows = new (string Row, CreateAgentTaskRequest Request)[]
        {
            ("missing-subject", world.InterimReview(valid.Id) with { VerificationSubjectTaskId = null }),
            ("missing-baseline", world.InterimReview(valid.Id) with { VerificationBaselineOutcomeId = null }),
            ("both-missing", world.InterimReview(valid.Id) with { VerificationSubjectTaskId = null, VerificationBaselineOutcomeId = null }),
        };
        foreach (var (row, request) in rows)
            await ExpectRefusedAsync(world, request, InterimVerificationPolicy.BaselineInvalidCode, row);
    }

    // ---- helpers --------------------------------------------------------------------------------

    internal static async Task ExpectRefusedAsync(C544World world, CreateAgentTaskRequest request, string code, string row)
    {
        var before = await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim));
        var latched = (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview;
        var error = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(request), row);
        error.Code.ShouldBe(code, $"{row}: {error.Message}");
        (await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim)))
            .ShouldBe(before, row + ": zero admitted tasks");
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBe(latched, row + ": latch unchanged");
    }

    internal static async Task UpdateOutcomeAsync(C544World world, Guid id, Action<StageOutcome> change)
    {
        await using var db = world.CreateContext();
        var row = await db.StageOutcomes.SingleAsync(o => o.Id == id);
        change(row);
        await db.SaveChangesAsync();
    }

    internal static async Task UpdateTaskAsync(C544World world, Guid id, Action<AgentTask> change)
    {
        await using var db = world.CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
        change(row);
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    internal static async Task<AgentTask> SeedOwnerAsync(C544World world, Guid cardId, Guid? projectId)
    {
        var id = Guid.NewGuid();
        var owner = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "other owner", Goal = "other", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, WorkingDirectory = world.Repo.Path,
            RepoPath = world.Repo.Path, WorktreePath = world.Repo.Path, WorktreeBranch = world.Owner.WorktreeBranch,
            MergeTargetRef = "master", Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CardId = cardId, ProjectId = projectId, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        };
        await using var db = world.CreateContext();
        db.AgentTasks.Add(owner);
        await db.SaveChangesAsync();
        return owner;
    }

    private static async Task<Guid> SeedCardAsync(C544World world, string identifier)
    {
        await using var db = world.CreateContext();
        var column = await db.BoardColumns.FirstAsync(c => c.BoardId == world.Board.Id);
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = world.Board.Id, BoardColumnId = column.Id, Identifier = identifier,
            Title = identifier, CodeVerificationPolicy = CardVerificationPolicy.AllowInterim,
            ReviewVerificationPolicy = CardVerificationPolicy.AllowInterim, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    private static async Task<Guid> SeedProjectAsync(C544World world)
    {
        await using var db = world.CreateContext();
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c544-foreign-{Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/f.git",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    internal static AgentTaskLanding PublishedLanding(C544World world)
    {
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = world.Owner.Id, SchemaVersion = 1, Active = true,
            SourceFullRef = "refs/heads/" + world.Owner.WorktreeBranch, TargetFullRef = "refs/heads/master",
            DestinationFullRef = "refs/heads/master", RepositoryPath = world.Repo.Path, CommonDirectory = world.Repo.Path,
            GitDirectory = world.Repo.Path, WorktreePath = world.Repo.Path, OriginalSourceSha = world.OwnerSha,
            RebasedSourceSha = world.OwnerSha, VerifiedSourceSha = world.OwnerSha, TargetBeforeSha = world.BaseSha,
            ObservedRemoteTargetSha = world.OwnerSha, RemoteFingerprint = new string('c', 64),
            RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow, Publication = LandPublicationOutcome.Landed,
            VerificationSkipReason = "base_unchanged", SourcePinned = true, TargetPinned = true, PreparedPinned = true,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", Phase = LandPhase.PublicationConfirmed,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("fixture publication is confirmed");
        return op;
    }

    private static async Task<string> HashObjectAsync(string repo, string file)
    {
        var result = await ScratchGitRepo.GitInAsync(repo, "hash-object", "-w", file);
        result.Ok.ShouldBeTrue(result.StdErr);
        return result.StdOut.Trim();
    }

    /// <summary>Fault injection on the admission save: the new task insert, or the owner latch.</summary>
    internal sealed class LatchFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;
        public bool FailTaskInsert { get; init; }
        public bool FailLatchSave { get; init; }
        public int Throws { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            var tracker = data.Context!.ChangeTracker;
            if (FailTaskInsert && tracker.Entries<AgentTask>().Any(e => e.State == EntityState.Added
                    && e.Entity.VerificationRound == VerificationRound.Interim))
            {
                Throws++;
                throw new IOException("c544 fault: interim task insert");
            }
            if (FailLatchSave && tracker.Entries<AgentTask>().Any(e => e.State == EntityState.Modified
                    && e.Entity.RequiresFinalVerificationReview
                    && e.Property(t => t.RequiresFinalVerificationReview).IsModified))
            {
                Throws++;
                throw new IOException("c544 fault: owner latch save");
            }
            return ValueTask.FromResult(result);
        }
    }
}
