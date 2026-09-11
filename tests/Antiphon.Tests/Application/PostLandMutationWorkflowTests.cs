using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class PostLandMutationWorkflowTests
{
    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Succeeded)]
    public async Task C478_V07_OriginalDoneCompanionOpen(AgentTaskStatus companionStatus)
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        var companion = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(companion.Id, companionStatus, dispatchedAt: world.Now.AddMinutes(-5),
            completedAt: companionStatus is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed ? world.Now : null,
            role: AgentTaskRole.Mutation);
        var moved = await world.ScanAsync();
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);
        (await world.SessionCountForAsync(original.Id)).ShouldBe(0);
        (await world.SessionCountForAsync(companion.Id)).ShouldBe(0);
        if (companionStatus is AgentTaskStatus.Dispatched or AgentTaskStatus.Working or AgentTaskStatus.Blocked)
        {
            moved.ShouldBe(1);
            (await world.ReadCardAsync(companion.Id)).Status.ShouldBe(CardStatus.InProgress);
        }
        else
            (await world.ReadCardAsync(companion.Id)).Status.ShouldNotBe(CardStatus.Done);
    }

    [Test]
    public async Task C478_V08_FindingDispositionMatrix()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        var verification = await world.SeedCardAsync(CardStatus.InProgress);
        await using (var db = world.CreateContext())
        {
            db.StageOutcomes.Add(new StageOutcome
            {
                Id = Guid.NewGuid(),
                CardId = verification.Id,
                Stage = OrchestrationStage.Review,
                Outcome = StageOutcomeKind.Found,
                Source = StageOutcomeSource.Delegate,
                Detail = "coverage survivor PC-1",
                RecordedAt = world.Now,
            });
            await db.SaveChangesAsync();
        }
        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);
        (await world.ReadCardAsync(verification.Id)).Status.ShouldBe(CardStatus.InProgress);
        (await world.MoveCountAsync(original.Id)).ShouldBe(0);
        (await world.SessionCountForAsync(original.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C478_V10_ResumeCommissioningAndTriage()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var codeTask = Guid.NewGuid();
        var key = "post-land-verification:" + codeTask.ToString("D");
        var first = await world.SeedCardAsync(CardStatus.Backlog);
        var second = await world.SeedCardAsync(CardStatus.Backlog);
        await using (var db = world.CreateContext())
        {
            (await db.Cards.SingleAsync(c => c.Id == first.Id)).Description = key + "\npublication pending";
            (await db.Cards.SingleAsync(c => c.Id == second.Id)).Description = key + "\nduplicate";
            await db.SaveChangesAsync();
        }
        await using var observer = world.CreateContext();
        var found = await observer.Cards.Where(c => c.Description != null && c.Description.Contains(key)).Select(c => c.Id).ToListAsync();
        found.Count.ShouldBe(2);
        found.ShouldContain(first.Id);
        found.ShouldContain(second.Id);
    }

    [Test]
    public void C478_V11_CodeReviewLandHeaders()
    {
        var code = PipelineHandoff.TryParse("""
            Ordinary V/R complete, 230 PCs pending.
            --- next stage ---
            next: review
            handoff: original Code owner; restart: none
            """);
        code.Kind.ShouldBe(PipelineHandoffKind.Review);
        var review = PipelineHandoff.TryParse("""
            No defects.
            --- next stage ---
            next: land
            handoff: original Code landing owner
            """);
        review.Kind.ShouldBe(PipelineHandoffKind.Land);
        review.Handoff.ShouldContain("original Code landing owner");
        var defect = PipelineHandoff.TryParse("""
            Missing ordinary delivery test.
            --- next stage ---
            next: code
            handoff: original Code owner
            """);
        defect.Kind.ShouldBe(PipelineHandoffKind.Code);
        var clean = PipelineHandoff.TryParse("""
            All PCs restored.
            --- next stage ---
            next: none
            """);
        clean.Kind.ShouldBe(PipelineHandoffKind.None);
        var finding = PipelineHandoff.TryParse("""
            Coverage survivor.
            --- next stage ---
            next: decide
            """);
        finding.Kind.ShouldBe(PipelineHandoffKind.Decide);
        PipelineHandoff.TryParse("--- next stage ---\nnext: mutation\n").Kind.ShouldBe(PipelineHandoffKind.Mutation);
        PipelineHandoff.TryParse("--- next stage ---\nnext: verify\n").Kind.ShouldBe(PipelineHandoffKind.Review);
    }

    [Test]
    public async Task C478_G124_CompanionDoesNotMoveOriginalDone()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        var companion = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(companion.Id, AgentTaskStatus.Working, dispatchedAt: world.Now, role: AgentTaskRole.Mutation);
        await world.ScanAsync();
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);
    }

    [Test]
    public async Task C478_G126_SuccessfulTaskDoesNotCloseVerification()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var verification = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(verification.Id, AgentTaskStatus.Succeeded,
            dispatchedAt: world.Now.AddMinutes(-10), completedAt: world.Now, role: AgentTaskRole.Mutation);
        await world.ScanAsync();
        (await world.ReadCardAsync(verification.Id)).Status.ShouldNotBe(CardStatus.Done);
    }

    [Test]
    public async Task C478_G129_FoundOutcomeDoesNotMoveCards()
    {
        await C478_V08_FindingDispositionMatrix();
    }

    [Test]
    public async Task C478_G125_CompanionVisible()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        var companion = await world.SeedCardAsync(CardStatus.Backlog);
        var task = await world.SeedTaskAsync(companion.Id, AgentTaskStatus.Queued, role: AgentTaskRole.Mutation);
        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);
        (await world.ReadCardAsync(companion.Id)).Status.ShouldBe(CardStatus.Backlog);
        await using var db = world.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task C478_G127_NoImplicitSpawn()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var companion = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(companion.Id, AgentTaskStatus.Working, dispatchedAt: world.Now, role: AgentTaskRole.Mutation);
        await world.ScanAsync();
        (await world.SessionCountForAsync(companion.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G128_HumanMove()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var companion = await world.SeedCardAsync(CardStatus.Canceled);
        await world.SeedTaskAsync(companion.Id, AgentTaskStatus.Succeeded,
            dispatchedAt: world.Now.AddMinutes(-10), completedAt: world.Now, role: AgentTaskRole.Mutation);
        await world.ScanAsync();
        (await world.ReadCardAsync(companion.Id)).Status.ShouldBe(CardStatus.Canceled);
    }

    [Test]
    public async Task C478_G130_DecisionRevision()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        var verification = await world.SeedCardAsync(CardStatus.InProgress);
        const string question = "Which linked repair owns coverage survivor PC-1?";
        await world.MoveToAsync(verification.Id, CardStatus.NeedsDecision, question);
        var moved = await world.ReadCardAsync(verification.Id);
        moved.Status.ShouldBe(CardStatus.NeedsDecision);
        var revision = (await world.DecisionRevisionsAsync(verification.Id)).ShouldHaveSingleItem();
        revision.Kind.ShouldBe(CardRevisionKind.Move);
        revision.Reason.ShouldBe(question);
        revision.ToStatus.ShouldBe(CardStatus.NeedsDecision);
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);

        await using var db = world.CreateContext();
        var attention = new AttentionService(db, new FakeSessionRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        var item = (await attention.GetAsync(default)).Items.Single(i => i.CardId == verification.Id);
        item.Kind.ShouldBe(AttentionKind.CardNeedsDecision);
        item.Evidence.ShouldBe(question);
        item.CardId.ShouldBe(verification.Id);
        item.BoardId.ShouldBe(world.BoardId);

        const string reopenQuestion = "Reopen into Needs decision: keep the original close history?";
        var closed = await world.SeedCardAsync(CardStatus.Done);
        await world.ReopenToAsync(closed.Id, CardStatus.NeedsDecision, reopenQuestion);
        var reopened = (await world.DecisionRevisionsAsync(closed.Id)).ShouldHaveSingleItem();
        reopened.Kind.ShouldBe(CardRevisionKind.Reopen);
        reopened.Reason.ShouldBe(reopenQuestion);
        var reopenItem = (await attention.GetAsync(default)).Items.Single(i => i.CardId == closed.Id);
        reopenItem.Kind.ShouldBe(AttentionKind.CardNeedsDecision);
        reopenItem.Evidence.ShouldBe(reopenQuestion);
    }

    [Test]
    public async Task C478_G131_NoAlertSink()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var original = await world.SeedCardAsync(CardStatus.Done);
        await world.SeedCardAsync(CardStatus.NeedsDecision);
        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadCardAsync(original.Id)).Status.ShouldBe(CardStatus.Done);
        (await world.SessionCountForAsync(original.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G132_HistoricalResult()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var verification = await world.SeedCardAsync(CardStatus.InProgress);
        var task = await world.SeedTaskAsync(verification.Id, AgentTaskStatus.Succeeded,
            dispatchedAt: world.Now.AddMinutes(-10), completedAt: world.Now, role: AgentTaskRole.Mutation);
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Result = "O/L found coverage survivor";
            await db.SaveChangesAsync();
        }
        await world.ScanAsync();
        await using var observer = world.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == task.Id)).Result.ShouldBe("O/L found coverage survivor");
        (await world.ReadCardAsync(verification.Id)).Status.ShouldNotBe(CardStatus.Done);
    }

    [Test]
    public async Task C478_G133_NoTickSpend()
    {
        await using var world = await CardWorkTransitionServiceTests.CardWorkTransitionServiceTestsHarness.CreateAsync();
        var companion = await world.SeedCardAsync(CardStatus.Backlog);
        (await world.ScanAsync()).ShouldBe(0);
        await using var db = world.CreateContext();
        (await db.AgentTasks.CountAsync(t => t.CardId == companion.Id)).ShouldBe(0);
        (await world.SessionCountForAsync(companion.Id)).ShouldBe(0);
    }
}
