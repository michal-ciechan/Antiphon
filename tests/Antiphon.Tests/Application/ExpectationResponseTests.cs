using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class ExpectationResponseTests
{
    [Test]
    public async Task C650_Only_complete_UserPrompt_opens_ack_window()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body[..(n.Body.Length / 2)]);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect the queue.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldBeNull();
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect the queue.");
        await ReconcileAsync(f);
        var answered = await f.ReloadAsync(n.Id);
        answered.ReceiptSequence.ShouldNotBeNull();
        answered.AnsweredSequence.ShouldNotBeNull();
        answered.AnsweredSequence!.Value.ShouldBeGreaterThan(answered.ReceiptSequence!.Value);
    }

    [Test]
    public async Task C650_Assistant_ack_needs_matching_id_turn_and_action()
    {
        var id = Guid.NewGuid();
        var marker = ExpectationPromptFormatter.AckMarker(id);
        Entry($"Quote: {marker}\nI will inspect").IsAnswer(id).ShouldBeFalse();
        Entry($"> {marker}\nI will inspect").IsAnswer(id).ShouldBeFalse();
        Entry($"```\n{marker}\n```\nI will inspect").IsAnswer(id).ShouldBeFalse();
        Entry(marker).IsAnswer(id).ShouldBeFalse();
        Entry(ExpectationPromptFormatter.AckMarker(Guid.NewGuid()) + "\nI will inspect").IsAnswer(id).ShouldBeFalse();
        Entry(marker + "\nI will inspect the task.").IsAnswer(id).ShouldBeTrue();
        Entry(marker + "\nI will inspect", kind: TranscriptKinds.UserPrompt).IsAnswer(id).ShouldBeFalse();
        Entry(marker + "\nI will inspect", error: true).IsAnswer(id).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Wrong_session_generation_and_sequence_do_not_answer()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        var other = await f.AddSessionAsync(true, false);
        await f.AppendTranscriptAsync(other, TranscriptKinds.UserPrompt, n.Body);
        await f.AppendTranscriptAsync(other, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldBeNull();
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldBeNull("pre-receipt ACK is not an answer");
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldNotBeNull();
    }

    [Test]
    public async Task C650_Queued_submission_is_not_delivery_or_answer()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.QueuedUserPrompt, n.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        var queued = await f.ReloadAsync(n.Id);
        queued.ReceiptAt.ShouldBeNull();
        queued.AnsweredAt.ShouldBeNull();
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldNotBeNull();
        f.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Deadline_survives_restart_and_catchup_failure()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        var due = DateTime.UtcNow.AddMinutes(-1);
        await using (var db = f.Db())
            await db.ExpectationNudges.Where(x => x.Id == n.Id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.AnswerDueAt, due)
                .SetProperty(x => x.AttemptStartedAt, due.AddMinutes(-5)));
        await using (var db = f.Db())
            await new ExpectationResponseService(db, TimeProvider.System, new FailingCatchUp(),
                new ExpectationTimingSettings()).ReconcileAsync(f.World.Directive, CancellationToken.None);
        var stored = await f.ReloadAsync(n.Id);
        stored.AnswerDueAt.ShouldNotBeNull();
        stored.AnswerDueAt.Value.ShouldBe(due, TimeSpan.FromMicroseconds(1));
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
    }

    [Test]
    public async Task C650_Receipt_without_ack_still_becomes_due()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await using (var db = f.Db())
            await db.ExpectationNudges.Where(x => x.Id == n.Id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.AnswerDueAt, DateTime.UtcNow.AddSeconds(-1)));
        await ReconcileAsync(f);
        var stored = await f.ReloadAsync(n.Id);
        stored.ReceiptAt.ShouldNotBeNull();
        stored.AnsweredAt.ShouldBeNull();
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
    }

    [Test]
    public async Task C650_Late_ack_suppresses_unpublished_debt()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f);
        await using (var db = f.Db())
            await db.ExpectationNudges.Where(x => x.Id == n.Id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.OperatorOutboxState, ExpectationOperatorOutboxState.Due));
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Suppressed);
    }

    [Test]
    public async Task C650_Ack_does_not_resolve_or_retype_uncertain_prompt()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var n = await AttemptedAsync(f, ExpectationAttemptState.Uncertain);
        var episode = new ExpectationEpisode
        {
            Id = Guid.NewGuid(), DirectiveId = f.World.Directive.Id,
            ConfigDigest = f.World.Digest, Kind = ExpectationEpisodeKind.StalledPipeline,
            SubjectKey = "queue:one", Evidence = "still held",
            FirstObservedAt = DateTime.UtcNow.AddMinutes(-20), LastObservedAt = DateTime.UtcNow,
        };
        await using (var db = f.Db()) { db.ExpectationEpisodes.Add(episode); await db.SaveChangesAsync(); }
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, n.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText,
            ExpectationPromptFormatter.AckMarker(n.Id) + "\nI will inspect.");
        await ReconcileAsync(f);
        (await f.ReloadAsync(n.Id)).AnsweredAt.ShouldNotBeNull();
        await using (var db = f.Db())
            (await db.ExpectationEpisodes.AsNoTracking().SingleAsync(e => e.Id == episode.Id)).ResolvedAt.ShouldBeNull();
        f.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Reconcile_bounds_unanswered_history_per_pass_and_rotates_pages()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        for (var i = 0; i < 60; i++)
            await f.NudgeAsync();
        await using (var db = f.Db())
            await db.ExpectationNudges.Where(n => n.DirectiveId == f.World.Directive.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.CreatedAt, DateTime.UtcNow.AddMinutes(-10)));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using (var db = f.Db())
            await new ExpectationResponseService(db, clock, f.CatchUp,
                new ExpectationTimingSettings()).ReconcileAsync(f.World.Directive, CancellationToken.None);
        await using (var db = f.Db())
            (await db.ExpectationNudges.CountAsync(n => n.DirectiveId == f.World.Directive.Id
                && n.OperatorOutboxState == ExpectationOperatorOutboxState.Due)).ShouldBe(25);
        clock.Advance(TimeSpan.FromMinutes(1));
        await using (var db = f.Db())
            await new ExpectationResponseService(db, clock, f.CatchUp,
                new ExpectationTimingSettings()).ReconcileAsync(f.World.Directive, CancellationToken.None);
        await using var verify = f.Db();
        (await verify.ExpectationNudges.CountAsync(n => n.DirectiveId == f.World.Directive.Id
            && n.OperatorOutboxState == ExpectationOperatorOutboxState.Due)).ShouldBe(50);
    }

    private static async Task<ExpectationNudge> AttemptedAsync(ExpectationDeliveryFixture f,
        ExpectationAttemptState state = ExpectationAttemptState.Submitted) =>
        await f.NudgeAsync(state: state, destination: f.SessionId,
            generation: await f.GenerationAsync(), baseline: await f.MaxSequenceAsync());

    private static async Task ReconcileAsync(ExpectationDeliveryFixture f)
    {
        await using var db = f.Db();
        await new ExpectationResponseService(db, TimeProvider.System, f.CatchUp,
            new ExpectationTimingSettings()).ReconcileAsync(f.World.Directive, CancellationToken.None);
    }

    private static TranscriptEntry Entry(string text, string kind = TranscriptKinds.AssistantText,
        bool error = false) => new() { Kind = kind, Text = text, IsApiError = error };

    private sealed class FailingCatchUp : IExpectationCatchUp
    {
        public Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct) =>
            throw new IOException("probe failed");
    }
}

internal static class ExpectationResponseTestExtensions
{
    public static bool IsAnswer(this TranscriptEntry entry, Guid nudgeId) =>
        ExpectationResponseMatcher.IsAnswer(entry, nudgeId);
}
