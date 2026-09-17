using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-20..26. The shared rule, with no database: the glance and the sweep both call
/// this, so a disagreement between them is impossible by construction and the rule is testable at
/// unit speed.
/// </summary>
[Category("Unit")]
public sealed class MutationDebtProjectionTests
{
    private static readonly DateTime At = new(2100, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void C552_P01_EmptyInputsYieldNothing() =>
        MutationDebtProjection.Build([], [], [], new Dictionary<Guid, MutationDebtProjection.CardRow>())
            .ShouldBeEmpty();

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Blocked, true)]
    [Arguments(AgentTaskStatus.Succeeded, true)]
    [Arguments(AgentTaskStatus.Failed, false)]
    [Arguments(AgentTaskStatus.Canceled, false)]
    public void C552_P02_ConsumingStatusesAreExactlyOpenAndSucceeded(AgentTaskStatus status, bool consumed)
    {
        var companion = Card("CARD-0002");
        var op = Landing(companion.Id, At);
        var sourced = new[] { Sourced(op.Id, companion.Id, status) };

        var debts = MutationDebtProjection.Build([op], sourced, [], Cards(companion));

        debts.Any(d => d.Companion.Id == companion.Id).ShouldBe(!consumed);
        if (!consumed) debts.Single().AnyAttempt.ShouldBeTrue();
    }

    [Test]
    public void C552_P03_NewestConfirmedOperationWins()
    {
        var companion = Card("CARD-0002");
        var older = Landing(companion.Id, At.AddHours(-2));
        var newer = Landing(companion.Id, At.AddHours(-1));
        var sourced = new[] { Sourced(older.Id, companion.Id, AgentTaskStatus.Succeeded) };

        var debt = MutationDebtProjection.Build([older, newer], sourced, [], Cards(companion)).ShouldHaveSingleItem();

        debt.Source.Id.ShouldBe(newer.Id);
        debt.AnyAttempt.ShouldBeFalse();
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Blocked, true)]
    [Arguments(AgentTaskStatus.Succeeded, false)]
    [Arguments(AgentTaskStatus.Failed, false)]
    [Arguments(AgentTaskStatus.Canceled, false)]
    public void C552_P04_UnsourcedMutationOnCompanion(AgentTaskStatus status, bool consumed)
    {
        var companion = Card("CARD-0002");
        var op = Landing(companion.Id, At);
        var bound = new[] { TaskRow(companion.Id, AgentTaskRole.Mutation, status) };

        MutationDebtProjection.Build([op], [], bound, Cards(companion))
            .Any(d => d.Companion.Id == companion.Id).ShouldBe(!consumed);
    }

    [Test]
    [Arguments(CardStatus.Done, false, false)]
    [Arguments(CardStatus.Canceled, false, false)]
    [Arguments(CardStatus.NeedsDecision, false, false)]
    [Arguments(CardStatus.Backlog, true, false)]
    [Arguments(CardStatus.Backlog, false, true)]
    [Arguments(CardStatus.InProgress, false, true)]
    [Arguments(CardStatus.Review, false, true)]
    public void C552_P05_TerminalOrArchivedCompanionYieldsNothing(CardStatus status, bool archived, bool expected)
    {
        var companion = Card("CARD-0002", status, archived);
        var op = Landing(companion.Id, At);

        MutationDebtProjection.Build([op], [], [], Cards(companion))
            .Any(d => d.Companion.Id == companion.Id).ShouldBe(expected);
    }

    [Test]
    public void C552_P06_OrderIsRemoteConfirmedAtThenIdentifierIgnoreCase()
    {
        var a = Card("CARD-0008");
        var b = Card("card-0009");
        var c = Card("CARD-0010");
        var opA = Landing(a.Id, At);
        var opB = Landing(b.Id, At.AddHours(-1));
        var opC = Landing(c.Id, At.AddHours(-1));

        var debts = MutationDebtProjection.Build([opA, opB, opC], [], [], Cards(a, b, c));

        debts.Select(d => d.Companion.Identifier).ShouldBe(["card-0009", "CARD-0010", "CARD-0008"]);
    }

    [Test]
    public void C552_P07_UnknownCardYieldsNothing()
    {
        var op = Landing(Guid.NewGuid(), At);
        MutationDebtProjection.Build([op], [], [], new Dictionary<Guid, MutationDebtProjection.CardRow>())
            .ShouldBeEmpty();
        MutationDebtProjection.Build([], [], [], Cards(Card("CARD-0002"))).ShouldBeEmpty();
    }

    private static MutationDebtProjection.CardRow Card(
        string identifier, CardStatus status = CardStatus.Backlog, bool archived = false) =>
        new(Guid.NewGuid(), identifier, identifier + " title", status, archived ? At : null);

    private static MutationDebtProjection.LandingRow Landing(Guid companionId, DateTime confirmedAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), companionId, confirmedAt,
            new string('c', 40), new string('b', 40), new string('b', 40), LandPublicationOutcome.Landed);

    private static MutationDebtProjection.SourcedRow Sourced(Guid opId, Guid cardId, AgentTaskStatus status) =>
        new(Guid.NewGuid(), opId, cardId, AgentTaskRole.Mutation, status, At, At, At);

    private static AgentTaskPipelineStatusService.TaskRow TaskRow(
        Guid cardId, AgentTaskRole role, AgentTaskStatus status) =>
        new(Guid.NewGuid(), "bound", role, status, cardId, null, AgentKind.ClaudeCode,
            AgentModelLevel.Frontier, At, At, At, null, "C:\\repo", "C:\\repo", null,
            WorkspaceMode.Worktree, null, null, null);

    private static Dictionary<Guid, MutationDebtProjection.CardRow> Cards(
        params MutationDebtProjection.CardRow[] cards) => cards.ToDictionary(c => c.Id);
}
