using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0135: <see cref="TranscriptPromptSpan"/> counts <c>QueuedUserPrompt</c> alongside
/// <c>UserPrompt</c>. The cases below pin the bound, the sequence order (the S2.4 trap does not
/// transfer), the housekeeping filter, and <see cref="TranscriptPromptSpan.PromptRow.Kind"/>.
/// Settlement and the delivery watchdog share this type; they cannot disagree.
/// </summary>
[Category("Integration")]
public class TranscriptPromptSpanTests
{
    private const string Wrapper =
        "<command-name>/compact</command-name>\n            <command-message>compact</command-message>\n"
        + "            <command-args>Keep only what serves the new task.</command-args>";

    [Test]
    public async Task a_queued_row_after_dispatch_is_a_turn_prompt()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "the drained brief",
            sequence: 1, timestamp: dispatchedAt.AddMinutes(1));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        var queued = span.TurnPrompts.ShouldHaveSingleItem();
        queued.Text.ShouldBe("the drained brief");
        queued.Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(db, sessionId, dispatchedAt, CancellationToken.None))
            .ShouldBeTrue();
    }

    [Test]
    public async Task a_queued_row_enqueued_before_dispatch_is_excluded_even_with_a_later_sequence()
    {
        // D2: the bound is the enqueue clock. A body typed before this task existed cannot be
        // its brief, however late it drains (and however high its sequence).
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "inherited history",
            sequence: 1, timestamp: dispatchedAt.AddMinutes(-20));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "queued before this dispatch",
            sequence: 2, timestamp: dispatchedAt.AddMinutes(-1));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldBeEmpty();
    }

    [Test]
    public async Task a_queued_row_with_a_null_timestamp_is_kept()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "untimestamped queued brief",
            sequence: 1, timestamp: null);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldHaveSingleItem().Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
    }

    [Test]
    public async Task the_s24_timestamp_skew_does_not_reorder_the_span()
    {
        // Verbatim from tests/Antiphon.Tests/Agents/Fixtures/queued-command.jsonl: a queued
        // attachment stamped 17:58:06.291Z sits at a higher sequence than a typed user record
        // stamped 17:58:37.774Z. CARD-0132 S2.4's trap is ranking those two against each other
        // by timestamp; this type orders by Sequence, so the typed row stays first. Goes red
        // the day someone adds a timestamp-based ordering here.
        var dispatchedAt = new DateTime(2026, 8, 21, 17, 50, 0, DateTimeKind.Utc);
        var (sessionId, _) = await SeedSessionAsync(dispatchedAt);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "tool_result that arrived first in file order",
            sequence: 1, timestamp: new DateTime(2026, 8, 21, 17, 58, 37, 774, DateTimeKind.Utc));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "The complete completion note that Claude queued while it was busy.",
            sequence: 2, timestamp: new DateTime(2026, 8, 21, 17, 58, 6, 291, DateTimeKind.Utc));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.Count.ShouldBe(2);
        span.TurnPrompts[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
        span.TurnPrompts[0].Sequence.ShouldBe(1);
        span.TurnPrompts[1].Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
        span.TurnPrompts[1].Sequence.ShouldBe(2);
        span.TurnPrompts[0].Timestamp.ShouldNotBeNull();
        span.TurnPrompts[1].Timestamp.ShouldNotBeNull();
        span.TurnPrompts[0].Timestamp!.Value.ShouldBeGreaterThan(span.TurnPrompts[1].Timestamp!.Value);
    }

    [Test]
    public async Task a_queued_row_with_housekeeping_text_is_filtered_the_same_as_typed()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueuedUserPrompt, Wrapper, 1, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix + " that ran out of context.",
            2, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "<task-notification>\n<summary>done</summary>\n</task-notification>",
            3, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "a real queued brief",
            4, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldHaveSingleItem().Text.ShouldBe("a real queued brief");
        span.Notifications.ShouldHaveSingleItem().Text.ShouldContain("<task-notification>");
    }

    [Test]
    public async Task prompt_row_kind_reports_the_rows_real_kind_on_both()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "typed brief", 1, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueuedUserPrompt, "queued brief", 2, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.Count.ShouldBe(2);
        span.TurnPrompts[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
        span.TurnPrompts[1].Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
    }

    private static async Task<(Guid SessionId, DateTime DispatchedAt)> SeedSessionAsync(
        DateTime? dispatchedAt = null)
    {
        var sessionId = Guid.NewGuid();
        var at = dispatchedAt ?? DateTime.UtcNow.AddMinutes(-11);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = at,
        });
        await db.SaveChangesAsync();
        return (sessionId, at);
    }

    private static async Task SeedEntryAsync(
        Guid sessionId, string kind, string? text, long sequence, DateTime? timestamp)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"span-{Guid.NewGuid():N}",
            Role = kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt
                ? "user"
                : "assistant",
            Text = text,
            Timestamp = timestamp,
            CreatedAt = timestamp ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
