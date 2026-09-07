using Antiphon.Server.Domain.Entities;
using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyBoundaryAcceptanceTests
{
    private static CardService Cards(CardFilePrivacyWorld world, AppDbContext db, MockEventBus events) =>
        new(db, null!, null!, null!, events, TimeProvider.System, null!, cardFiles: world.Service(db));

    [Test]
    public async Task Private_card_metadata_and_archive_groups_never_enter_any_new_blob()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); await world.AddCardAsync();
        var id = await world.AddCardAsync("CARD-9999", "C408_HIDDEN_TITLE", visibility: CardFileVisibility.Private);
        await using var db = world.Db(); var card = await db.Cards.SingleAsync(c => c.Id == id);
        card.Alias = "C408_HIDDEN_ALIAS"; card.Description = "C408_HIDDEN_BODY"; card.LabelsJson = "[\"C408_HIDDEN_LABEL\"]";
        card.ArchivedAt = DateTime.UtcNow; card.ArchivedReason = "C408_HIDDEN_ARCHIVE"; card.ArchivedBy = "C408_HIDDEN_AUTHOR"; card.TerminalReason = "C408_HIDDEN_OUTCOME";
        db.ExternalIssueRefs.Add(new ExternalIssueRef { Id = Guid.NewGuid(), CardId = id, TrackerKind = TrackerKind.GitHubIssues, ExternalId = "C408_HIDDEN_ID", ExternalKey = "C408_HIDDEN_KEY", Url = "https://example.invalid/C408_HIDDEN_URL", RawPayloadJson = "{}", LastSyncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        try {
            var result = await world.SyncAsync(true); result.EligibleCards.ShouldBe(1); result.ExcludedCards.ShouldBe(1); result.Error.ShouldBeNull();
            foreach (var path in (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                path.ShouldNotContain("C408_HIDDEN"); (await world.Repo.GitReadAsync("show", "HEAD:"+path)).ShouldNotContain("C408_HIDDEN");
            }
            (await File.ReadAllTextAsync(Path.Combine(world.DirectoryPath, "INDEX.md"))).ShouldContain("1 card, 0 archived.");
        }
        finally { await db.ExternalIssueRefs.Where(r => r.CardId == id).ExecuteDeleteAsync(); }
    }

    [Test]
    public async Task Hostile_notes_are_not_selected_or_rendered_and_note_only_edits_leave_HEAD_unchanged()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync();
        var id = await world.AddCardAsync(notes: "C408_OLD_NOTE\n---\nprivateNotes: secret\n```");
        const string publicBody = "{{PrivateNotes}} ${PrivateNotes}\n---\n```text\nquotes | pipes\n```";
        await using (var seed = world.Db()) await seed.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, publicBody));
        var interceptor = new RejectPrivateSelect();
        var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions()).AddInterceptors(interceptor).Options;
        await using (var db = new AppDbContext(options))
        {
            var first = await world.Service(db, true).SyncBoardAsync(world.BoardId);
            first.Error.ShouldBeNull(); first.Written.ShouldBe(2); first.CommitSha.ShouldNotBeNull();
        }
        interceptor.Checked.ShouldBeGreaterThan(0);
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD");
        var path = "docs/cards/board/CARD-0001-public.md";
        (await world.Repo.GitReadAsync("show", "HEAD:"+path)).ShouldContain(publicBody);
        foreach (var name in (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            (await world.Repo.GitReadAsync("show", "HEAD:"+name.Trim())).ShouldNotContain("C408_OLD_NOTE");
        for (var i = 0; i < 2; i++)
        {
            await using var db = world.Db(); var card = await db.Cards.SingleAsync(c => c.Id == id);
            await Cards(world, db, new MockEventBus()).UpdateContentAsync(id, new UpdateCardContentRequest(card.ConcurrencyToken, "notes only", PrivateNotes: "C408_CURRENT_NOTE_"+i), default);
            var result = await world.SyncAsync(true); result.Error.ShouldBeNull(); result.Written.ShouldBe(0);
            (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        }
        await using var history = world.Db();
        (await history.CardRevisions.CountAsync(r => r.CardId == id)).ShouldBe(2);
    }

    [Test]
    public async Task Concurrent_note_edits_wait_for_gate_and_have_one_winner_and_one_event()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); var id = await world.AddCardAsync();
        await using var one = world.Db(); await using var two = world.Db();
        var token = (await one.Cards.SingleAsync(c => c.Id == id)).ConcurrencyToken;
        await two.Cards.SingleAsync(c => c.Id == id); // Deliberately cache the losing context before the gate.
        var events = new MockEventBus();
        using var held = await world.Gate.EnterProjectAsync(world.ProjectId, true, default);
        var first = Cards(world, one, events).UpdateContentAsync(id, new(token, "winner", PrivateNotes: "C408_WINNER", CardFileVisibility: CardFileVisibility.Private), default);
        var second = Cards(world, two, events).UpdateContentAsync(id, new(token, "loser", PrivateNotes: "C408_LOSER"), default);
        first.IsCompleted.ShouldBeFalse(); second.IsCompleted.ShouldBeFalse(); held!.Dispose();
        var failures = new List<Exception>();
        foreach (var task in new[] { first, second }) { try { await task; } catch (Exception ex) { failures.Add(ex); } }
        failures.Count.ShouldBe(1); failures.Single().ShouldBeOfType<ConflictException>().StatusCode.ShouldBe(409);
        await using var fresh = world.Db();
        (await fresh.Cards.SingleAsync(c => c.Id == id)).PrivateNotes.ShouldBe(first.IsCompletedSuccessfully ? "C408_WINNER" : "C408_LOSER");
        (await fresh.CardRevisions.CountAsync(r => r.CardId == id)).ShouldBe(1);
        events.PublishedEvents.Count(e => e.EventName == "CardChanged").ShouldBe(1);
    }

    [Test]
    public async Task Failed_atomic_correction_persists_neither_notes_description_token_nor_revision()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false); var id = await world.AddCardAsync();
        await using var original = world.Db(); var before = await original.Cards.SingleAsync(c => c.Id == id);
        var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions()).AddInterceptors(new RefuseSave()).Options;
        await using var failing = new AppDbContext(options); var events = new MockEventBus();
        await Should.ThrowAsync<IOException>(() => Cards(world, failing, events).UpdateContentAsync(id,
            new(before.ConcurrencyToken, "atomic", Description: "changed public", PrivateNotes: "C408_NEW", CardFileVisibility: CardFileVisibility.Private), default));
        await using var fresh = world.Db(); var after = await fresh.Cards.SingleAsync(c => c.Id == id);
        after.Description.ShouldBe(before.Description); after.PrivateNotes.ShouldBe(before.PrivateNotes);
        after.ConcurrencyToken.ShouldBe(before.ConcurrencyToken); after.CardFileVisibility.ShouldBe(before.CardFileVisibility);
        (await fresh.CardRevisions.CountAsync(r => r.CardId == id)).ShouldBe(0); events.PublishedEvents.ShouldBeEmpty();
    }

    private sealed class RejectPrivateSelect : DbCommandInterceptor
    {
        public int Checked { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Checked++; command.CommandText.ShouldNotContain("PrivateNotes"); command.CommandText.ShouldNotContain("CardRevisions"); return ValueTask.FromResult(result);
        }
    }
    private sealed class RefuseSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) => throw new IOException("C408 synthetic save failure");
    }
}
