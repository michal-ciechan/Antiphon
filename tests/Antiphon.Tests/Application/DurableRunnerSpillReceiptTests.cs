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
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, runnerCwd);

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

        var restarted = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
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
            await db.SaveChangesAsync();
        }
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, "/runner/worktrees/task-missing");

        var missing = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));
        missing.Message.ShouldBe(RemoteSpillUndeliverableException.MissingBodyReason);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var canceled = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
        canceled.DeliveryVerdict.ShouldBe(DeliveryVerdict.SpillBodyMissing);

        var restarted = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
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
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.RemoteSpillBody = null;
            row.RemoteSpillRelativePath = relative;
            await db.SaveChangesAsync();
        }
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, "/runner/worktrees/task-source");

        var courier = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
        var found = await courier.FindDurableAsync(
            h.SessionId, "Read " + relative + " before doing anything else", CancellationToken.None);
        found.ShouldNotBeNull();
        found.Spill.Body.ShouldBe(source);
        found.Spill.MessageId.ShouldBe(id);

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        stored.RemoteSpillBody.ShouldBe(source);
        stored.RemoteSpillRelativePath.ShouldBe(relative);
    }

    [Test]
    public async Task A_screen_only_Delivered_null_body_is_respilled_from_the_source_message()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        var source = "legacy-screen-only-source-0647\n" + new string('s', 300);
        var id = await h.SeedPendingMessageAsync(source);
        var relative = TypedBodySpill.InboxRelativePath(id.ToString("D"));
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.RemoteSpillBody = null;
            row.RemoteSpillRelativePath = relative;
            row.DeliveryVerdict = DeliveryVerdict.Delivered;
            row.DeliveryVerdictAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, "/runner/worktrees/task-legacy-source");

        var courier = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
        var found = await courier.FindDurableAsync(
            h.SessionId, "Read " + relative + " before doing anything else", CancellationToken.None);
        found.ShouldNotBeNull();
        found.Spill.Body.ShouldBe(source);
        found.Spill.MessageId.ShouldBe(id);

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        stored.RemoteSpillBody.ShouldBe(source);
        stored.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task A_screen_only_Delivered_null_body_pointer_is_canceled_instead_of_typed_without_bytes()
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
            row.DeliveryVerdict = DeliveryVerdict.Delivered;
            row.DeliveryVerdictAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, "/runner/worktrees/task-legacy-missing");

        var restarted = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
        var again = await Should.ThrowAsync<RemoteSpillUndeliverableException>(() =>
            restarted.FindDurableAsync(h.SessionId, pointer, CancellationToken.None));
        again.Message.ShouldBe(RemoteSpillUndeliverableException.MissingBodyReason);

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var canceled = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
        canceled.DeliveryVerdict.ShouldBe(DeliveryVerdict.SpillBodyMissing);
    }

    [Test]
    public async Task A_complete_matching_UserPrompt_releases_a_null_body_pointer()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        var id = await h.SeedPendingMessageAsync("placeholder");
        var relative = TypedBodySpill.InboxRelativePath(id.ToString("D"));
        var pointer = TypedBodySpill.PointerHeadline + "\n\n    " + relative + "\n" + new string('u', 80);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Body = pointer;
            row.RemoteSpillBody = null;
            row.RemoteSpillRelativePath = relative;
            row.DeliveryVerdict = DeliveryVerdict.Delivered;
            row.DeliveryVerdictAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, pointer, timestamp: DateTime.UtcNow);
        await BindRunnerAsync(schema.ConnectionString, h.SessionId, "/runner/worktrees/task-legacy-released");

        var courier = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
        var found = await courier.FindDurableAsync(h.SessionId, pointer, CancellationToken.None);
        found.ShouldBeNull();

        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await saved.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        stored.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        stored.RemoteSpillBody.ShouldBeNull();
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
            await BindRunnerAsync(schema.ConnectionString, h.SessionId, mirror);

            // CARD-0649: an opening task marker rides the typed pointer. The file keeps the
            // original bytes, and a complete UserPrompt of that pointer is what clears them.
            const string marker = "[antiphon-task:f67e6efa]";
            var fullBody = marker + " joined-brief-0647\n" + new string('j', 2048);
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
            submitted.ShouldContain(marker + " " + TypedBodySpill.PointerHeadline);
            submitted.ShouldEndWith(marker);
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

    [Test]
    public async Task Screen_only_delivery_keeps_spill_bytes_and_a_retry_writes_the_same_body()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = Path.Combine(Path.GetTempPath(), "c647-screen-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        var mirror = root + "/worktrees/task-screen";
        Directory.CreateDirectory(mirror);
        var writer = new RunnerWorkspaceService(root + "/repo", root);
        try
        {
            await using var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
            });
            await BindRunnerAsync(schema.ConnectionString, h.SessionId, mirror);
            // The harness records a UserPrompt on submit. This delivery is the screen-only
            // fallback: the terminal redrew, and no recipient prompt exists.
            h.Adapter.OnSubmitted = _ => Task.CompletedTask;

            const string marker = "[antiphon-task:f67e6efa]";
            var fullBody = marker + " screen-only-0647\n" + new string('k', 2048);
            await h.Queue.EnqueueAsync(h.SessionId, fullBody, MessageSendMode.WhenIdle, CancellationToken.None);

            h.Adapter.SubmittedBodies.ShouldNotBeEmpty();
            var submitted = h.Adapter.SubmittedBodies[^1];
            submitted.ShouldContain(marker + " " + TypedBodySpill.PointerHeadline);
            submitted.ShouldEndWith(marker);
            submitted.ShouldNotBe(fullBody);

            await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var row = await after.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.AgentSessionId == h.SessionId);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            row.RemoteSpillBody.ShouldBe(fullBody);
            (await after.TranscriptEntries.CountAsync(e =>
                e.AgentSessionId == h.SessionId && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);

            var courier = new RemoteSpillCourier(h.Provider.GetRequiredService<IServiceScopeFactory>());
            var retry = await courier.FindDurableAsync(h.SessionId, row.Body, CancellationToken.None);
            retry.ShouldNotBeNull();
            retry!.Spill.Body.ShouldBe(fullBody);
            await writer.WriteSpillAsync(mirror, retry.Spill, CancellationToken.None);
            var file = Path.Combine(mirror, ".antiphon", "inbox", row.Id.ToString("D") + ".md");
            (await File.ReadAllTextAsync(file)).ShouldBe(fullBody);
            await writer.WriteSpillAsync(mirror, retry.Spill, CancellationToken.None);
            (await File.ReadAllTextAsync(file)).ShouldBe(fullBody);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// CK_AgentSessions_RunnerBinding_AllOrNone: RunnerId, RunnerStoreId and RunnerCwd move together.
    /// </summary>
    private static async Task BindRunnerAsync(string connection, Guid sessionId, string runnerCwd)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RunnerId, "server2")
            .SetProperty(x => x.RunnerStoreId, Guid.NewGuid())
            .SetProperty(x => x.RunnerCwd, runnerCwd));
    }
}
