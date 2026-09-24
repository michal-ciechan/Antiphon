using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class DurableRunnerSpillReceiptTests
{
    [Test]
    public async Task Queued_runner_spill_keeps_its_bytes_and_receives_a_complete_UserPrompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        const string cwd = "/runner/worktrees/task-one";
        const string oldPath = ".antiphon/old-brief.md";
        var fullBody = "durable-receipt-0647\n" + new string('r', 4096);
        h.Queue.StageRemoteSpill(h.SessionId, cwd, new PhoneHomeInputSpill(oldPath, fullBody));
        await QueuedReceiptAssertions.HoldRecipientBusyAsync(schema.ConnectionString, h.SessionId);
        await h.Queue.EnqueueAsync(h.SessionId, "Read " + cwd + "/" + oldPath,
            MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Delegation);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.RemoteSpillBody.ShouldBe(fullBody);
        row.Body.ShouldContain(TypedBodySpill.InboxRelativePath(row.Id.ToString("D")));
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            schema.ConnectionString, h, row, h.SessionId, busy: true, busyBeforeDelivery: true);

        await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var confirmed = await after.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == row.Id);
        confirmed.Status.ShouldBe(QueuedMessageStatus.Sent);
        confirmed.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        confirmed.RemoteSpillBody.ShouldBeNull();
    }

    [Test]
    public async Task SendNow_persists_the_spill_body_before_delivery_so_a_restart_can_read_it()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        const string runnerCwd = "/runner/worktrees/task-sendnow";
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.RunnerCwd = runnerCwd;
            await db.SaveChangesAsync();
        }

        var fullBody = "send-now-handoff-0647\n" + new string('q', 2048);
        var id = await h.SeedPendingMessageAsync(fullBody);
        h.Adapter.ThrowOnSend = new InvalidOperationException("runner died after the row was saved");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        row.RemoteSpillBody.ShouldBe(fullBody);
        row.Body.ShouldContain(TypedBodySpill.InboxRelativePath(id.ToString("D")));
        row.DeliveryVerdict.ShouldBeNull();

        var restarted = new RemoteSpillCourier(h.Provider);
        var found = await restarted.FindDurableAsync(h.SessionId, row.Body, CancellationToken.None);
        found.ShouldNotBeNull();
        found.RunnerCwd.ShouldBe(runnerCwd);
        found.Spill.Body.ShouldBe(fullBody);
        found.Spill.MessageId.ShouldBe(id);
    }

    [Test]
    public async Task A_null_body_spill_pointer_is_canceled_instead_of_a_silent_lookup_miss()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        var id = await h.SeedPendingMessageAsync("placeholder");
        var relative = TypedBodySpill.InboxRelativePath(id.ToString("D"));
        var pointer = TypedBodySpill.PointerHeadline + "\n\n    " + relative;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Body = pointer;
            row.RemoteSpillBody = null;
            row.RemoteSpillRelativePath = relative;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.RunnerCwd = "/runner/worktrees/task-missing";
            await db.SaveChangesAsync();
        }

        var missing = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));
        missing.Message.ShouldBe(RemoteSpillUndeliverableException.MissingBodyReason);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var canceled = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
        canceled.DeliveryVerdict.ShouldBe(DeliveryVerdict.SpillBodyMissing);

        var restarted = new RemoteSpillCourier(h.Provider);
        var again = await Should.ThrowAsync<RemoteSpillUndeliverableException>(() =>
            restarted.FindDurableAsync(h.SessionId, pointer, CancellationToken.None));
        again.Message.ShouldBe(RemoteSpillUndeliverableException.MissingBodyReason);
    }

    [Test]
    public async Task A_null_body_pointer_is_respilled_when_the_source_message_is_still_on_the_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        var source = "source-message-0647\n" + new string('s', 300);
        var id = await h.SeedPendingMessageAsync(source);
        var relative = TypedBodySpill.InboxRelativePath(id.ToString("D"));
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.RunnerCwd = "/runner/worktrees/task-source";
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.RemoteSpillBody = null;
            row.RemoteSpillRelativePath = relative;
            await db.SaveChangesAsync();
        }

        var courier = new RemoteSpillCourier(h.Provider);
        var found = await courier.FindDurableAsync(
            h.SessionId, "Read " + relative + " before doing anything else", CancellationToken.None);
        found.ShouldNotBeNull();
        found.Spill.Body.ShouldBe(source);
        found.Spill.MessageId.ShouldBe(id);

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.RemoteSpillBody.ShouldBe(source);
        row.RemoteSpillRelativePath.ShouldBe(relative);
    }

    [Test]
    public async Task Queued_brief_is_written_by_the_runner_when_a_busy_recipient_becomes_eligible()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = Path.Combine(Path.GetTempPath(), "c647-join-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        var mirror = root + "/worktrees/task-joined1";
        Directory.CreateDirectory(mirror);
        var writer = new RunnerWorkspaceService(root + "/repo", root);
        try
        {
            await using var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
            });
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                session.RunnerCwd = mirror;
                await db.SaveChangesAsync();
            }

            var fullBody = "joined-brief-0647\n" + new string('j', 2048);
            await QueuedReceiptAssertions.HoldRecipientBusyAsync(schema.ConnectionString, h.SessionId);
            await h.Queue.EnqueueAsync(h.SessionId, fullBody, MessageSendMode.WhenIdle, CancellationToken.None);

            await using (var before = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var waiting = await before.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.AgentSessionId == h.SessionId);
                waiting.Status.ShouldBe(QueuedMessageStatus.Pending);
                waiting.Body.ShouldBe(fullBody);
                waiting.RemoteSpillBody.ShouldBeNull();
            }
            h.Adapter.SubmittedBodies.ShouldBeEmpty();
            Directory.Exists(Path.Combine(mirror, ".antiphon")).ShouldBeFalse();

            var record = h.Adapter.OnSubmitted;
            h.Adapter.OnSubmitted = async submitted =>
            {
                await using var live = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var row = await live.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.AgentSessionId == h.SessionId);
                row.RemoteSpillBody.ShouldBe(fullBody);
                await writer.WriteSpillAsync(mirror, new PhoneHomeInputSpill(
                    row.RemoteSpillRelativePath!, row.RemoteSpillBody!, row.Id), CancellationToken.None);
                if (record is not null)
                    await record(submitted);
            };

            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn, timestamp: DateTime.UtcNow);
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);

            h.Adapter.SubmittedBodies.ShouldNotBeEmpty();
            var submitted = h.Adapter.SubmittedBodies[^1];
            submitted.ShouldContain(TypedBodySpill.PointerHeadline);
            await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var confirmed = await after.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.AgentSessionId == h.SessionId);
            confirmed.Status.ShouldBe(QueuedMessageStatus.Sent);
            confirmed.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            confirmed.RemoteSpillBody.ShouldBeNull();
            var prompt = await after.TranscriptEntries.AsNoTracking().SingleAsync(e =>
                e.AgentSessionId == h.SessionId && e.Kind == TranscriptKinds.UserPrompt);
            PromptSubmissionMatch.IsCompleteIn(submitted, prompt.Text!).ShouldBeTrue();
            var file = Path.Combine(mirror, ".antiphon", "inbox", confirmed.Id.ToString("D") + ".md");
            (await File.ReadAllTextAsync(file)).ShouldBe(fullBody);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
