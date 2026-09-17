using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-9a..9c. The description a companion is BORN with, with no database in the way:
/// it is the only durable statement of the obligation, so its shape is a contract.
/// </summary>
[Category("Unit")]
public sealed class PostLandVerificationCompanionsDescribeTests
{
    [Test]
    public void C552_U01_StableKeyTitleAndLabel()
    {
        PostLandVerificationCompanions.Label.ShouldBe("post-land-verification");
        PostLandVerificationCompanions.KeyPrefix.ShouldBe("post-land-verification:");
        var owner = Guid.NewGuid();
        PostLandVerificationCompanions.StableKey(owner)
            .ShouldBe("post-land-verification:" + owner.ToString("D"));
        PostLandVerificationCompanions.Title("CARD-0552").ShouldBe("Post-land verification: CARD-0552");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void C552_U02_DescribeIsAsciiOneFactPerLineAndCapped(bool sparse)
    {
        var (original, owner, op) = Subject();
        var reviewId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        const string plan = "docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md";
        var companionId = Guid.NewGuid();
        owner.NextHandoff = sparse ? null : "the Code handoff line";
        var context = new PostLandVerificationCompanions.CompanionContext(
            companionId,
            sparse ? null : reviewId,
            sparse ? null : "the Review handoff line",
            owner.NextHandoff,
            sparse ? null : projectId,
            sparse ? null : plan);

        var text = PostLandVerificationCompanions.Describe(original, owner, op, context);

        var lines = text.Split('\n');
        lines[0].ShouldBe(PostLandVerificationCompanions.StableKey(owner.Id));
        if (sparse)
        {
            text.ShouldContain("Review task: none recorded");
            text.ShouldContain("Plan: not recorded");
            text.ShouldContain("Commissioning project: null");
            text.ShouldContain("Code handoff: none recorded");
            text.ShouldContain("Review handoff: none recorded");
        }
        else
        {
            text.ShouldContain("Review task: " + reviewId.ToString("D"));
            text.ShouldContain("Plan: " + plan);
            text.ShouldContain("Commissioning project: " + projectId.ToString("D"));
            text.ShouldContain("Code handoff: the Code handoff line");
            text.ShouldContain("Review handoff: the Review handoff line");
        }

        foreach (var ch in text) ((int)ch).ShouldBeLessThan(128);
        text.Length.ShouldBeLessThanOrEqualTo(CardService.MaxDescriptionLength);

        // The handoff lines are report text; an oversized one must not push the whole description
        // past the card cap, and must not cost the dispatch line at the end.
        owner.NextHandoff = new string('h', 30_000);
        var oversized = PostLandVerificationCompanions.Describe(original, owner, op,
            context with { CodeHandoff = owner.NextHandoff, ReviewHandoff = new string('r', 30_000) });
        oversized.Length.ShouldBeLessThanOrEqualTo(CardService.MaxDescriptionLength);
        oversized.ShouldEndWith("-Worktree -SourceLanding " + op.Id.ToString("D"));
        oversized.ShouldContain("Dispatch (explicit, WIP 1): delegate.ps1 -Role Mutation -Card "
            + companionId.ToString("D"));
    }

    [Test]
    public void C552_U03_NonAsciiInputNeverReachesTheDescription()
    {
        var (original, owner, op) = Subject();
        owner.NextHandoff = "an em\u2014dash and a caf\u00e9 handoff";
        var text = PostLandVerificationCompanions.Describe(original, owner, op,
            new PostLandVerificationCompanions.CompanionContext(
                Guid.NewGuid(), null, null, owner.NextHandoff, null, null));

        foreach (var ch in text) ((int)ch).ShouldBeLessThan(128);
        text.ShouldContain("Code handoff: ");
        text.ShouldContain("an em-dash and a cafe handoff");
    }

    private static (Card Original, AgentTask Owner, AgentTaskLanding Op) Subject()
    {
        var original = new Card
        {
            Id = Guid.NewGuid(), BoardId = Guid.NewGuid(), Identifier = "CARD-0001",
            Title = "CARD-0001 title", Status = CardStatus.Done,
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
        return (original, owner, op);
    }
}
