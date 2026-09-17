using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-1..7. The land hook against real git: a CONFIRMED publication and the
/// obligation it creates commit together, and nothing else creates a companion.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandCompanionLandHookTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C552_L01_ConfirmedLandCreatesCompanion(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.SeedOriginalCardAsync();
        if (!alreadyPresent) await h.AddSourceAsync();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        await using var db = h.CreateContext();
        (await db.Cards.CountAsync(c => c.BoardId == h.BoardId)).ShouldBe(2);
        companion.Identifier.ShouldBe("CARD-0002");
        companion.Title.ShouldBe("Post-land verification: CARD-0001");
        companion.Status.ShouldBe(CardStatus.Backlog);
        companion.BoardColumnId.ShouldBe(h.BacklogColumnId);
        BoardService.ParseLabels(companion.LabelsJson).ShouldBe(["post-land-verification"]);
        companion.Importance.ShouldBe(CardImportance.Normal);
        companion.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.VerificationCardId.ShouldBe(companion.Id);
        op.Publication.ShouldBe(alreadyPresent ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Landed);

        companion.Description.ShouldStartWith("post-land-verification:" + h.Fixture.TaskId.ToString("D") + "\n");
        foreach (var fact in new[]
        {
            $"Original card: CARD-0001 ({h.OriginalCardId:D})",
            $"Landing owner (Code task): {h.Fixture.TaskId:D}",
            "Reviewed C: " + op.OriginalSourceSha,
            $"O: {op.Id:D}  L={op.VerifiedSourceSha}  R={op.ObservedRemoteTargetSha}",
            $"Publication: {op.Publication} confirmed at {op.RemoteConfirmedAt:O}",
            $"Dispatch (explicit, WIP 1): delegate.ps1 -Role Mutation -Card {companion.Id:D} "
                + $"-Worktree -SourceLanding {op.Id:D}",
        })
        {
            companion.Description.ShouldContain(fact);
        }

        foreach (var ch in companion.Description) ((int)ch).ShouldBeLessThan(128);
    }

    [Test]
    public async Task C552_L02_CompanionCarriesConfirmationRevision()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.SeedOriginalCardAsync();
        await h.AddSourceAsync();
        await h.RunAsync();

        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using var db = h.CreateContext();
        var revision = (await db.CardRevisions.Where(r => r.CardId == companion.Id).ToListAsync())
            .ShouldHaveSingleItem();
        revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
        revision.Reason.ShouldBe("Confirmed publication " + op.Id.ToString("N"));
        revision.EditedBy.ShouldBe("land");
        companion.RevisionCount.ShouldBe(1);
    }

    [Test]
    public async Task C552_L03_OriginalGetsReverseLinkAppendOnly()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (_, _, originalId, _) = await h.SeedOriginalCardAsync();
        await h.AddSourceAsync();
        await using (var before = h.CreateContext())
        {
            var seeded = await before.Cards.AsNoTracking().SingleAsync(c => c.Id == originalId);
            seeded.Status.ShouldBe(CardStatus.Done);
        }

        await h.RunAsync();

        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        await using var db = h.CreateContext();
        var original = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == originalId);
        original.Description.ShouldEndWith($"\nPost-land verification: CARD-0002 ({companion.Id:D})");
        var revision = (await db.CardRevisions.Where(r => r.CardId == originalId).ToListAsync())
            .ShouldHaveSingleItem();
        revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
        revision.Reason.ShouldBe("Post-land verification companion");
        revision.EditedBy.ShouldBe("land");
        original.Status.ShouldBe(CardStatus.Done);
        original.CompletedAt.ShouldNotBeNull();
        original.TerminalReason.ShouldBe("closed by fixture");
        original.ConcurrencyToken.ShouldNotBe(h.OriginalToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C552_L04_TerminalDetailAndOutcomeBodyNameTheCompanion(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.SeedOriginalCardAsync();
        if (!alreadyPresent) await h.AddSourceAsync();

        await h.RunAsync();

        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        await using var db = h.CreateContext();
        var marker = $"companion=CARD-0002 ({companion.Id:D})";
        var terminal = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent));
        terminal.Detail.ShouldContain(marker);
        (await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == h.Fixture.TaskId
            && n.Kind == LandNotificationKind.Outcome)).Body.ShouldContain("companion=CARD-0002 (");
    }

    [Test]
    public async Task C552_L05_CardChangedPublishedForBothCardsAfterCommit()
    {
        await using var h = new LandingSafetyHarness();
        var events = new MockEventBus();
        h.Events = events;
        var boundary = new RecordingBoundary(events);
        h.Boundary = boundary;
        await h.InitializeAsync();
        var (_, _, originalId, _) = await h.SeedOriginalCardAsync();
        await h.AddSourceAsync();

        await h.RunAsync();

        // Both publishes happen AFTER the terminal commit, so a rolled-back terminal never
        // announces a card that does not exist.
        boundary.CardChangedAtCommit.ShouldBe(0);
        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        CardChangedCount(events, companion.Id).ShouldBe(1);
        CardChangedCount(events, originalId).ShouldBe(1);
    }

    [Test]
    [Arguments("dirty")]
    [Arguments("push-rejected")]
    public async Task C552_L06_RefusedOrUnconfirmedLandCreatesNoCompanion(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (_, _, originalId, _) = await h.SeedOriginalCardAsync();
        if (variant == "dirty")
        {
            await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"), "changed\n");
        }
        else
        {
            await h.AddSourceAsync();
            var pushed = false;
            h.Fixture.Git.BeforeCommand = (_, args) =>
            {
                if (args[0] == "push")
                {
                    pushed = true;
                    return Task.FromResult<LandingGitResult?>(new(1, "", "fixture rejection"));
                }

                return Task.FromResult<LandingGitResult?>(
                    pushed && args[0] == "ls-remote" ? new(128, "", "fixture read failure") : null);
            };
        }

        await h.RunAsync();

        (await h.CompanionAsync()).ShouldBeNull();
        await using var db = h.CreateContext();
        (await db.Cards.CountAsync(c => c.BoardId == h.BoardId)).ShouldBe(1);
        ((await h.OperationAsync())?.VerificationCardId).ShouldBeNull();
        (await db.CardRevisions.CountAsync(r => r.CardId == originalId)).ShouldBe(0);
        foreach (var terminal in await db.AgentTaskEvents
            .Where(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal).ToListAsync())
        {
            terminal.Detail.ShouldNotContain("companion=");
        }
    }

    [Test]
    public async Task C552_L07_CardlessOwnerLandsWithoutCompanion()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();

        var result = LandRunResult.Held;
        await Should.NotThrowAsync(async () => result = await h.RunAsync());
        result.ShouldBe(LandRunResult.Complete);

        await using var db = h.CreateContext();
        (await db.Cards.CountAsync()).ShouldBe(0);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.VerificationCardId.ShouldBeNull();
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal))
            .Detail.ShouldNotContain("companion=");
    }

    [Test]
    [Arguments("landed")]
    [Arguments("already-present")]
    public async Task C552_L08_InterruptedAfterPublicationStillRecordsCompanion(string kind)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.SeedOriginalCardAsync();
        if (kind == "landed") await h.AddSourceAsync();
        h.Fault.AfterAcknowledged = phase => phase == LandPhase.PublicationConfirmed
            ? throw new IOException("fixture")
            : Task.CompletedTask;

        var error = await Should.ThrowAsync<IOException>(() => h.RunAsync());
        h.Fault.AfterAcknowledged = null;
        await h.FailAsync(error);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("landing_interrupted_after_publication");
        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        op.VerificationCardId.ShouldBe(companion.Id);
        await using var db = h.CreateContext();
        var terminal = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.LandedWithResidue
                || e.Type == AgentTaskEventType.AlreadyPresent));
        terminal.Detail.ShouldContain("companion=CARD-0002");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
    }

    [Test]
    [Arguments("before-save")]
    [Arguments("after-save")]
    public async Task C552_L09_TerminalCutLeavesNeitherEventNorCompanion(string cut)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.SeedOriginalCardAsync();
        await h.AddSourceAsync();
        h.Fault.TerminalCut = cut;

        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());

        await using (var cutDb = h.CreateContext())
        {
            (await cutDb.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal))
                .ShouldBe(0);
            (await cutDb.Cards.CountAsync(c => c.BoardId == h.BoardId)).ShouldBe(1);
            (await cutDb.AgentTaskLandings.Where(o => o.TaskId == h.Fixture.TaskId)
                .Select(o => o.VerificationCardId).ToListAsync()).ShouldAllBe(id => id == null);
            (await cutDb.CardRevisions.CountAsync()).ShouldBe(0);
        }

        h.Fault.TerminalCut = null;
        await h.RestartServicesAsync();
        await h.RunAsync();

        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal)).ShouldBe(1);
        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        (await h.OperationAsync())!.VerificationCardId.ShouldBe(companion.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C552_L10_CleanupRetryCreatesNoSecondCardOrRevision(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (_, _, originalId, _) = await h.SeedOriginalCardAsync();
        if (!alreadyPresent) await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, "bin-private", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        await h.RunAsync();
        var first = (await h.OperationAsync()).ShouldNotBeNull();
        var publicationTime = first.RemoteConfirmedAt;
        var companionId = first.VerificationCardId.ShouldNotBeNull();
        File.Delete(sentinel);
        await h.RepostAsync();

        await h.RunAsync();

        await using var db = h.CreateContext();
        var labelled = (await db.Cards.Where(c => c.BoardId == h.BoardId).ToListAsync())
            .Count(c => BoardService.ParseLabels(c.LabelsJson).Contains(PostLandVerificationCompanions.Label));
        labelled.ShouldBe(1);
        (await db.CardRevisions.CountAsync(r => r.CardId == companionId)).ShouldBe(1);
        (await db.CardRevisions.CountAsync(r => r.CardId == originalId)).ShouldBe(1);
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandingCleanup)).Detail.ShouldContain("companion=CARD-0002");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.VerificationCardId.ShouldBe(companionId);
        op.RemoteConfirmedAt.ShouldBe(publicationTime);
    }

    [Test]
    public async Task C552_L11_NoBacklogColumnFallsBackToTheFirstColumn()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (_, _, _, firstColumnId) = await h.SeedOriginalCardAsync(backlogColumn: false);
        await h.AddSourceAsync();

        await h.RunAsync();

        var companion = (await h.CompanionAsync()).ShouldNotBeNull();
        companion.BoardColumnId.ShouldBe(firstColumnId);
        companion.Status.ShouldBe(CardStatus.InProgress);
    }

    private static int CardChangedCount(MockEventBus events, Guid cardId) => events.PublishedEvents
        .Count(e => e.EventName == "CardChanged"
            && JsonSerializer.Deserialize<CardChangedPayload>(JsonSerializer.Serialize(e.Payload))?.CardId == cardId);

    private sealed record CardChangedPayload(Guid BoardId, Guid CardId);

    /// <summary>
    /// Counts the CardChanged publishes that had already happened when the terminal commit was
    /// reported. Zero is the contract: the cards are announced only after the row exists.
    /// </summary>
    private sealed class RecordingBoundary(MockEventBus events) : LandDeliveryBoundary
    {
        public int CardChangedAtCommit { get; private set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "terminal-committed")
                CardChangedAtCommit = events.PublishedEvents.Count(e => e.EventName == "CardChanged");
            return Task.CompletedTask;
        }
    }
}
