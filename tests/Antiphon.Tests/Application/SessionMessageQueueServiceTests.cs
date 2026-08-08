using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueServiceTests
{
    [Test]
    public async Task Send_now_delivers_immediately_and_does_not_queue()
    {
        var h = await CreateHarnessAsync();
        try
        {
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "ship it", MessageSendMode.Now, CancellationToken.None);

            dto.Messages.ShouldBeEmpty();
            h.Adapter.SentInput.ShouldContain("ship it");
            h.Adapter.SentInput.ShouldEndWith("\r");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // Regression lock for the queue-submit bug: Claude's TUI treats text and a trailing CR arriving in
    // ONE write as a bracketed paste — the CR becomes a literal newline and the message never submits.
    // DeliverAsync must therefore send the body and the submitting CR as TWO separate writes. The
    // companion FakeClaudeContractTests prove (against a real PTY) that two writes submit and one does
    // not; this test pins that the queue actually produces the two-write shape.
    [Test]
    public async Task Delivery_sends_body_then_a_separate_CR_not_one_combined_write()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await h.Queue.EnqueueAsync(h.SessionId, "queued hello", MessageSendMode.Now, CancellationToken.None);

            h.Adapter.Inputs.Count.ShouldBe(2, "body and CR must be two separate writes, not one combined write");
            h.Adapter.Inputs[0].ShouldBe("queued hello", "a single-line body needs no paste wrap");
            h.Adapter.Inputs[1].ShouldBe("\r");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // Regression lock for the 2026-07-29 fragmentation bug ("Add these to my calendar"): a multi-line
    // body written raw is chunked by ConPTY and fragments at line breaks — the agent's prompt was only
    // the final fragment. Multi-line bodies must be wrapped in bracketed paste (\e[200~..\e[201~) so
    // the TUI treats the whole body as ONE literal paste regardless of how the transport chunks it.
    // The companion FakeClaudeContractTests prove the fragment/intact behaviours against a real PTY;
    // this pins that the queue produces the wrapped shape.
    [Test]
    public async Task Multiline_delivery_is_wrapped_in_bracketed_paste()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await h.Queue.EnqueueAsync(
                h.SessionId, "line one\nline two\nline three", MessageSendMode.Now, CancellationToken.None);

            h.Adapter.Inputs.Count.ShouldBe(2);
            h.Adapter.Inputs[0].ShouldBe("\x1b[200~line one\nline two\nline three\x1b[201~");
            h.Adapter.Inputs[1].ShouldBe("\r", "the submitting CR stays a separate unbracketed write");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task When_idle_message_is_held_while_the_agent_is_working()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);

            var dto = await h.Queue.EnqueueAsync(h.SessionId, "next task", MessageSendMode.WhenIdle, CancellationToken.None);

            dto.Messages.Count.ShouldBe(1);
            dto.Messages[0].Body.ShouldBe("next task");
            dto.Working.ShouldBeTrue();
            h.Adapter.SentInput.ShouldBeEmpty();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Turn_end_flushes_the_oldest_queued_message()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            await h.Queue.EnqueueAsync(h.SessionId, "first", MessageSendMode.WhenIdle, CancellationToken.None);
            await h.Queue.EnqueueAsync(h.SessionId, "second", MessageSendMode.WhenIdle, CancellationToken.None);

            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            h.Adapter.SentInput.ShouldContain("first");
            h.Adapter.SentInput.ShouldNotContain("second");
            var remaining = await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None);
            remaining.Messages.Count.ShouldBe(1);
            remaining.Messages[0].Body.ShouldBe("second");
            h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionQueueChanged");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task When_idle_and_agent_is_idle_the_message_is_delivered_right_away()
    {
        var h = await CreateHarnessAsync();
        try
        {
            // No working transcript entries → the agent is idle, so a "wait until idle" message goes now.
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "go", MessageSendMode.WhenIdle, CancellationToken.None);

            dto.Messages.ShouldBeEmpty();
            h.Adapter.SentInput.ShouldContain("go");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Turn_end_with_empty_queue_broadcasts_finished()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            var finished = h.EventBus.PublishedEvents.Where(e => e.EventName == "SessionFinished").ToList();
            // Exactly one global broadcast — a second group-scoped publish would reach connections
            // in the session group twice and duplicate the toast.
            var single = finished.ShouldHaveSingleItem();
            single.Group.ShouldBeNull();
            h.Adapter.SentInput.ShouldBeEmpty();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Send_now_promotes_a_specific_queued_message()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            await h.Queue.EnqueueAsync(h.SessionId, "first", MessageSendMode.WhenIdle, CancellationToken.None);
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "second", MessageSendMode.WhenIdle, CancellationToken.None);
            var second = dto.Messages.Single(m => m.Body == "second");

            await h.Queue.SendNowAsync(h.SessionId, second.Id, CancellationToken.None);

            h.Adapter.SentInput.ShouldContain("second");
            var remaining = await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None);
            remaining.Messages.Select(m => m.Body).ShouldBe(["first"]);
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Cancel_removes_a_pending_message_without_delivering_it()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "drop me", MessageSendMode.WhenIdle, CancellationToken.None);

            var after = await h.Queue.CancelAsync(h.SessionId, dto.Messages[0].Id, CancellationToken.None);

            after.Messages.ShouldBeEmpty();
            h.Adapter.SentInput.ShouldBeEmpty();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // Live miss 2026-07-29: Mike rejected a tool call mid-turn; Claude wrote the
    // "[Request interrupted by user for tool use]" USER marker and no TurnEnd, the session read
    // as permanently "working", and the photo + multiline test messages stranded for 25 minutes.
    // The interrupt marker is a turn END: the session must read idle and the flush must deliver.
    [Test]
    public async Task An_interrupt_marker_ends_the_turn_and_unblocks_when_idle_delivery()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            await h.Queue.EnqueueAsync(h.SessionId, "stranded hello", MessageSendMode.WhenIdle, CancellationToken.None);
            h.Adapter.SentInput.ShouldBeEmpty("the session is mid-turn — nothing may deliver yet");
            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeTrue("activity with no turn end reads as working");

            // The interrupt lands (Esc / rejected tool call): a USER marker entry, no TurnEnd.
            await InsertEntryAsync(h, TranscriptKinds.UserPrompt, "[Request interrupted by user for tool use]");

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeFalse("the interrupt marker IS the aborted turn's end");

            // AgentSessionRuntime triggers the same flush on the marker as on a TurnEnd.
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            h.Adapter.SentInput.ShouldContain("stranded hello");
            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages.ShouldBeEmpty();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task A_real_user_message_mentioning_interruption_is_not_a_turn_end()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            // A genuine prompt whose text merely CONTAINS the phrase must still count as activity.
            await InsertEntryAsync(h, TranscriptKinds.UserPrompt, "why was my [Request interrupted] earlier?");

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeTrue("only prompts STARTING with the marker end a turn");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // Live miss 2026-07-31: /model was run in the AZ Care session; Claude wrote the
    // <command-name>/<local-command-stdout> USER records (no TurnEnd — local commands make no API
    // call), the session read as permanently "working", and a queued Telegram message stranded.
    // Local command records are housekeeping: they neither start nor end work. Real-Claude record
    // shape pinned by ClaudeLocalCommandCanaryTests; fakeclaude mirrors it.
    [Test]
    public async Task Local_slash_command_records_do_not_read_as_working_and_do_not_block_delivery()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            await InsertEntryAsync(h, TranscriptKinds.TurnEnd, null);

            // The user runs /model (or /clear): invocation + stdout records, NO TurnEnd after.
            await InsertEntryAsync(
                h, TranscriptKinds.UserPrompt,
                "<command-name>/model</command-name>\n<command-message>model</command-message>\n<command-args>opus</command-args>");
            await InsertEntryAsync(
                h, TranscriptKinds.UserPrompt,
                "<local-command-stdout>Set model to Opus 5 and saved as your default for new sessions</local-command-stdout>");

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeFalse("local command records are housekeeping, not agent work");

            // A WhenIdle enqueue against the (correctly) idle session delivers immediately.
            await h.Queue.EnqueueAsync(h.SessionId, "after slash command", MessageSendMode.WhenIdle, CancellationToken.None);
            h.Adapter.SentInput.ShouldContain("after slash command");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Local_slash_command_records_do_not_end_a_running_turn()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            // Mid-turn the user fires /status — the records land, but the turn is still running.
            await InsertEntryAsync(h, TranscriptKinds.UserPrompt, "<command-name>/status</command-name>");

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeTrue("a slash command mid-turn neither ends nor extends the turn");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // The 2026-08-08 stranding: entries lost during a server restart were backfilled by a later
    // catch-up sync, and the sequence rebase landed them ABOVE the already-persisted TurnEnd.
    // Their record timestamps prove they predate the end — the session must read idle, or the
    // agent badges "Working" forever and every WhenIdle delivery strands.
    [Test]
    public async Task Backfilled_stale_activity_above_a_turn_end_does_not_read_as_working()
    {
        var h = await CreateHarnessAsync();
        try
        {
            var t0 = DateTime.UtcNow.AddMinutes(-10);
            await InsertEntryAsync(h, TranscriptKinds.AssistantText, "turn output", timestamp: t0);
            await InsertEntryAsync(h, TranscriptKinds.TurnEnd, null, timestamp: t0.AddMinutes(1));
            // The backfill: records from BEFORE the end arrive later, so they hold higher sequences.
            await InsertEntryAsync(h, TranscriptKinds.ToolCall, "backfilled tool call", timestamp: t0.AddSeconds(10));
            await InsertEntryAsync(h, TranscriptKinds.ToolResult, "backfilled tool result", timestamp: t0.AddSeconds(11));

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeFalse("backfilled records that predate the turn end are history, not live activity");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // Guard for the timestamp override's direction: genuinely NEW activity after the last end
    // (later timestamp AND later sequence) must still read as working.
    [Test]
    public async Task Activity_newer_than_the_last_turn_end_still_reads_as_working()
    {
        var h = await CreateHarnessAsync();
        try
        {
            var t0 = DateTime.UtcNow.AddMinutes(-10);
            await InsertEntryAsync(h, TranscriptKinds.AssistantText, "turn output", timestamp: t0);
            await InsertEntryAsync(h, TranscriptKinds.TurnEnd, null, timestamp: t0.AddMinutes(1));
            await InsertEntryAsync(h, TranscriptKinds.UserPrompt, "next piece of work", timestamp: t0.AddMinutes(2));

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeTrue("a prompt after the last turn end is a new turn");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // A relaunch of a session whose process died mid-turn writes a SessionRestartBoundary (there
    // is no TurnEnd and never will be) — it must end the phantom turn exactly like an interrupt
    // marker does, or the relaunched agent reads "Working" forever (live miss 2026-08-08).
    [Test]
    public async Task Session_restart_boundary_ends_an_interrupted_turn()
    {
        var h = await CreateHarnessAsync();
        try
        {
            await MarkWorkingAsync(h);
            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeTrue("sanity: the seeded transcript reads mid-turn");

            await InsertEntryAsync(
                h, TranscriptKinds.SessionRestartBoundary,
                "Session relaunched; the previous turn had been interrupted mid-flight.",
                timestamp: DateTime.UtcNow);

            (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working
                .ShouldBeFalse("the relaunch proved the interrupted turn's process is gone");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    private static async Task InsertEntryAsync(Harness h, string kind, string? text, DateTime? timestamp = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var baseSeq = (await db.TranscriptEntries
            .Where(t => t.AgentSessionId == h.SessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = h.SessionId,
            Sequence = baseSeq + 1,
            Kind = kind,
            Text = text,
            Timestamp = timestamp,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // Insert an assistant-text transcript entry with no following turn-end so the session reads as "working".
    private static async Task MarkWorkingAsync(Harness h)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = h.SessionId,
            Sequence = 1,
            Kind = TranscriptKinds.AssistantText,
            Text = "working on it",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-queue-tests-{sessionId:N}");
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = null,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        return new Harness(
            provider,
            sessionId,
            adapter,
            eventBus,
            provider.GetRequiredService<SessionMessageQueueService>());
    }

    private sealed record Harness(
        ServiceProvider Provider,
        Guid SessionId,
        FakeAgentProtocolAdapter Adapter,
        MockEventBus EventBus,
        SessionMessageQueueService Queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }
            await Provider.DisposeAsync();
        }
    }
}
