using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-83..87 (D-11). The unattended sweep starts batteries nobody asked for, so a
/// settled sourced battery has no caller to report to; the feed is how it reaches the board.
/// </summary>
public partial class AttentionServiceTests
{
    [Test]
    [Arguments(PipelineHandoffKind.Decide, AlertSeverity.Warning)]
    [Arguments(PipelineHandoffKind.None, AlertSeverity.Info)]
    public async Task C552_A01_SucceededSourcedBatteryOnOpenCompanionIsListed(
        PipelineHandoffKind next, AlertSeverity severity)
    {
        await using var scenario = new Scenario();
        var world = await SeedBatteryAsync(scenario, nextStage: next);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == world.Battery);

        item.Kind.ShouldBe(AttentionKind.MutationDispositionPending);
        ((int)item.Kind).ShouldBe(42);
        item.Severity.ShouldBe(severity);
        item.CardId.ShouldBe(world.Companion);
        item.BoardId.ShouldBe(world.BoardId);
        item.Headline.ShouldContain("CARD-0002");
        item.Headline.ShouldContain("disposition");
        item.Evidence.ShouldContain("CARD-0001");
        item.Evidence.ShouldContain("CARD-0002");
        item.Evidence.ShouldContain("O=" + world.OperationId.ToString("D"));
        item.Evidence.ShouldContain("L=" + new string('b', 40));
        item.Evidence.ShouldContain(DelegationReportFormatter.Short(world.Battery));
        item.Evidence.ShouldContain(next == PipelineHandoffKind.Decide ? "verdict=findings" : "verdict=clean");
        item.Actions.ShouldBe([AttentionAction.OpenCard, AttentionAction.OpenDrawer]);
        item.SinceUtc.ShouldNotBeNull();
        AttentionSummaryDto.From(new AttentionDto(DateTime.UtcNow, true, [item])).Open.ShouldBe(1);
    }

    [Test]
    [Arguments(CardStatus.Done, false, false)]
    [Arguments(CardStatus.Canceled, false, false)]
    [Arguments(CardStatus.Backlog, true, false)]
    [Arguments(CardStatus.Review, false, true)]
    [Arguments(CardStatus.InProgress, false, true)]
    public async Task C552_A02_RowClearsWhenCompanionIsClosedCanceledOrArchived(
        CardStatus status, bool archived, bool listed)
    {
        await using var scenario = new Scenario();
        var world = await SeedBatteryAsync(scenario, companionStatus: status, companionArchived: archived);

        var items = (await ItemsForAsync(scenario))
            .Where(i => i.TaskId == world.Battery && i.Kind == AttentionKind.MutationDispositionPending)
            .ToList();

        if (listed) items.ShouldHaveSingleItem();
        else items.ShouldBeEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C552_A03_NewerBoundMutationTaskClearsTheRow(bool sourced)
    {
        await using var scenario = new Scenario();
        var world = await SeedBatteryAsync(scenario);
        var newer = await scenario.AddTaskAsync(world.SessionId, AgentTaskStatus.Queued,
            dispatchedMinutesAgo: 0, neverDispatched: true, role: AgentTaskRole.Mutation,
            cardId: world.Companion,
            sourceLandingOperationId: sourced ? world.OperationId : null);

        var items = await ItemsForAsync(scenario);

        items.ShouldNotContain(i => i.TaskId == world.Battery
            && i.Kind == AttentionKind.MutationDispositionPending);
        items.ShouldNotContain(i => i.TaskId == newer
            && i.Kind == AttentionKind.MutationDispositionPending);
    }

    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task C552_A04_FailedOrCanceledAttemptIsNotThisKind(AgentTaskStatus status)
    {
        await using var scenario = new Scenario();
        var world = await SeedBatteryAsync(scenario, batteryStatus: status);

        var items = (await ItemsForAsync(scenario)).Where(i => i.TaskId == world.Battery).ToList();

        items.ShouldNotContain(i => i.Kind == AttentionKind.MutationDispositionPending);
        if (status == AgentTaskStatus.Failed)
            items.ShouldContain(i => i.Kind == AttentionKind.RecentFailure);
    }

    [Test]
    public async Task C552_A05_UnsourcedSucceededMutationIsNotThisKind()
    {
        await using var scenario = new Scenario();
        var world = await SeedBatteryAsync(scenario, sourced: false);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == world.Battery
            && i.Kind == AttentionKind.MutationDispositionPending);
    }

    private sealed record BatteryWorld(
        Guid SessionId, Guid BoardId, Guid Original, Guid Companion, Guid Owner, Guid OperationId, Guid Battery);

    private static async Task<BatteryWorld> SeedBatteryAsync(
        Scenario scenario,
        PipelineHandoffKind nextStage = PipelineHandoffKind.None,
        AgentTaskStatus batteryStatus = AgentTaskStatus.Succeeded,
        CardStatus companionStatus = CardStatus.Backlog,
        bool companionArchived = false,
        bool sourced = true)
    {
        var sessionId = await scenario.AddSessionAsync(SessionStatus.Running);
        var (_, boardId, columnId) = await scenario.AddBoardAsync($"c552-{Guid.NewGuid():N}"[..16],
            localRepositoryPath: Scenario.UniqueCwd());
        var original = await scenario.AddCardOnBoardAsync(boardId, columnId, CardStatus.Done, identifier: "CARD-0001");
        var companion = await scenario.AddCardOnBoardAsync(boardId, columnId, companionStatus,
            companionArchived, "CARD-0002");
        var owner = await scenario.AddTaskAsync(sessionId, AgentTaskStatus.Succeeded,
            dispatchedMinutesAgo: 240, completedMinutesAgo: 120, cardId: original);
        var op = await scenario.AddLandingAsync(owner, companion);
        var battery = await scenario.AddTaskAsync(sessionId, batteryStatus,
            dispatchedMinutesAgo: 30, completedMinutesAgo: 5, role: AgentTaskRole.Mutation,
            cardId: companion, sourceLandingOperationId: sourced ? op.Id : null, nextStage: nextStage);
        return new BatteryWorld(sessionId, boardId, original, companion, owner, op.Id, battery);
    }
}
