using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-73..75. The server-composed Mutation brief carries IDENTITY. Method text here
/// would drift from <c>stage-mutation.md</c>, which is the bundle the delegate actually runs under.
/// </summary>
[Category("Unit")]
public sealed class MutationAutoDispatchGoalTests
{
    [Test]
    public void C552_B01_GoalCarriesIdentityNotMethod()
    {
        var (original, companion, owner, op) = Subject();
        var reviewId = Guid.NewGuid();
        const string plan = "docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md";

        var goal = MutationAutoDispatchSweep.ComposeGoal(original, companion, owner, reviewId, plan, op);

        foreach (var expected in new[]
        {
            "CARD-0001", original.Id.ToString("D"), "CARD-0002", companion.Id.ToString("D"),
            owner.Id.ToString("D"), reviewId.ToString("D"),
            "C=" + op.OriginalSourceSha, "O=" + op.Id.ToString("D"),
            "L=" + op.VerifiedSourceSha, "R=" + op.ObservedRemoteTargetSha, plan,
            "HEAD must equal L=" + op.VerifiedSourceSha,
            "enumerate every PC-n and named variant", "stage-mutation.md", "you only report",
        })
        {
            goal.ShouldContain(expected);
        }

        foreach (var absent in new[] { "--treenode-filter", "dotnet run", "bin-pc", "OutputPath" })
            goal.ShouldNotContain(absent);

        foreach (var ch in goal) ((int)ch).ShouldBeLessThan(128);
        goal.Length.ShouldBeLessThan(20_000);
    }

    [Test]
    public void C552_B02_GoalStaysUnderTheCapWithOversizedInputs()
    {
        var (original, companion, owner, op) = Subject();
        owner.NextHandoff = new string('h', 30_000);

        var goal = MutationAutoDispatchSweep.ComposeGoal(
            original, companion, owner, Guid.NewGuid(), new string('p', 5_000), op);

        goal.Length.ShouldBeLessThan(20_000);
        goal.ShouldContain("HEAD must equal L=");
    }

    [Test]
    public void C552_B03_SparseInputsAreNamedNotOmitted()
    {
        var (original, companion, owner, op) = Subject();

        var goal = MutationAutoDispatchSweep.ComposeGoal(original, companion, owner, null, null, op);

        goal.ShouldContain("Review task: none recorded");
        goal.ShouldContain("Plan: not recorded");
    }

    private static (Card Original, Card Companion, AgentTask Owner, AgentTaskLanding Op) Subject()
    {
        var original = new Card
        {
            Id = Guid.NewGuid(), BoardId = Guid.NewGuid(), Identifier = "CARD-0001",
            Title = "CARD-0001 title", Status = CardStatus.Done,
        };
        var companion = new Card
        {
            Id = Guid.NewGuid(), BoardId = original.BoardId, Identifier = "CARD-0002",
            Title = PostLandVerificationCompanions.Title("CARD-0001"), Status = CardStatus.Backlog,
        };
        var owner = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "owner", Goal = "g",
            Role = AgentTaskRole.Code, CardId = original.Id, WorkingDirectory = "C:\\repo",
        };
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = owner.Id,
            OriginalSourceSha = new string('c', 40), VerifiedSourceSha = new string('b', 40),
            ObservedRemoteTargetSha = new string('b', 40),
            Publication = LandPublicationOutcome.Landed, Cleanup = LandCleanupStatus.Complete,
            RemoteConfirmedAt = new DateTime(2100, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        return (original, companion, owner, op);
    }
}
