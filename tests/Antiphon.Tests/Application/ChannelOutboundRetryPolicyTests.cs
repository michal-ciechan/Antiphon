using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundRetryPolicyTests
{
    [Test]
    public async Task C519_Attempt_commit_precedes_producer()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        w.Producer.OnSend = async (_, _) =>
        {
            var row = await w.LoadAsync(id);
            row.State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
            row.PublicationAttempts.ShouldBe(1);
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
        };
        await w.TickAsync();
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        w.Producer.Receipts.ShouldHaveSingleItem().Text.ShouldBe("frozen reply " + id);
        var faultId = await w.ReadyAsync();
        var fault = new AttemptSaveFault();
        await w.TickAsync(interceptor: fault);
        fault.Fired.ShouldBeTrue();
        (await w.LoadAsync(faultId)).PublicationAttempts.ShouldBe(0);
        w.Producer.Entries.ShouldBe(1);
        (await w.MemberAsync(faultId)).ChannelReplySettledAt.ShouldBeNull();
    }

    [Test]
    public async Task C519_Only_one_live_lease_can_claim()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        var entered = Signal(); var release = Signal();
        w.Producer.OnSend = async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var first = w.TickAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await w.TickAsync();
            w.Producer.Entries.ShouldBe(1);
            (await w.LoadAsync(id)).PublishedAt.ShouldBeNull();
        }
        finally { release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(10)); }
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        // Isolate the claim's expiry check from the earlier candidate predicate:
        // install a live lease after selection without changing its version.
        var next = await w.ReadyAsync();
        var foreignOwner = Guid.NewGuid();
        var selected = false;
        await w.TickAsync(async (at, delivery, _) =>
        {
            if (at != "before-claim") return;
            delivery.ShouldBe(next); selected = true;
            await w.SetAsync(next, d => { d.LeaseOwner = foreignOwner; d.LeaseUntil = w.Now.AddSeconds(300); });
        });
        selected.ShouldBeTrue();
        w.Producer.Entries.ShouldBe(1);
        (await w.LoadAsync(next)).LeaseOwner.ShouldBe(foreignOwner);
        (await w.MemberAsync(next)).ChannelReplySettledAt.ShouldBeNull();
        await w.SetAsync(next, d => { d.LeaseOwner = null; d.LeaseUntil = null; });
        w.Producer.OnSend = null;
        await w.TickAsync();
        w.Producer.Receipts.Last().Text.ShouldBe("frozen reply " + next);
    }

    [Test]
    public async Task C519_Final_entry_checks_current_lease()
    {
        foreach (var guard in new[] { "owner", "version", "expiry", "state" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.ReadyAsync();
            await w.TickAsync(async (at, delivery, _) =>
            {
                if (at != "before-producer-call") return;
                delivery.ShouldBe(id);
                await using var db = w.Db();
                var row = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id);
                if (guard == "owner") row.LeaseOwner = Guid.NewGuid();
                if (guard == "version") row.Version++;
                if (guard == "expiry") row.LeaseUntil = w.Now;
                if (guard == "state") row.State = ChannelOutboundDeliveryState.PublishUncertain;
                await db.SaveChangesAsync();
            });
            w.Producer.Entries.ShouldBe(0, guard);
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
            w.Clock.Advance(TimeSpan.FromSeconds(301));
            await w.TickAsync();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            w.Producer.Entries.ShouldBe(0);
        }
    }

    [Test]
    public async Task C519_Late_outcome_cannot_overwrite_new_owner()
    {
        foreach (var guard in new[] { "owner", "version", "expiry", "state" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.ReadyAsync();
            var entered = Signal(); var release = Signal();
            w.Producer.OnSend = async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); };
            var run = w.TickAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var db = w.Db();
                var row = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id);
                if (guard == "owner") row.LeaseOwner = Guid.NewGuid();
                if (guard == "version") row.Version++;
                if (guard == "expiry") row.LeaseUntil = w.Now;
                if (guard == "state") row.State = ChannelOutboundDeliveryState.PublishUncertain;
                await db.SaveChangesAsync();
            }
            finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            w.Producer.Receipts.Count.ShouldBe(1);
            (await w.LoadAsync(id)).PublishedAt.ShouldBeNull(guard);
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull(guard);
            w.Clock.Advance(TimeSpan.FromSeconds(301));
            await w.TickAsync();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            w.Producer.Entries.ShouldBe(1);
        }
    }

    [Test]
    public async Task C519_Producer_completion_is_awaited()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        var entered = Signal(); var release = Signal();
        w.Producer.OnSend = async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var run = w.TickAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            run.IsCompleted.ShouldBeFalse();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
            w.Producer.Receipts.ShouldBeEmpty();
        }
        finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldNotBeNull();
        w.Producer.Receipts.Count.ShouldBe(1);
    }

    [Test]
    public async Task C519_Cancellation_is_never_success()
    {
        foreach (var phase in new[] { "before-publication-attempt", "entry", "producer-accepted" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.ReadyAsync();
            using var host = new CancellationTokenSource();
            w.Producer.OnSend = (_, ct) => { if (phase == "entry") { host.Cancel(); ct.ThrowIfCancellationRequested(); } return Task.CompletedTask; };
            Func<string, Guid, CancellationToken, Task> barrier = (at, _, ct) =>
            { if (at == phase) { host.Cancel(); ct.ThrowIfCancellationRequested(); } return Task.CompletedTask; };
            if (phase == "before-publication-attempt")
                await Should.ThrowAsync<OperationCanceledException>(() => w.TickAsync(barrier, ct: host.Token));
            else await w.TickAsync(barrier, ct: host.Token);
            var row = await w.LoadAsync(id);
            row.PublicationAttempts.ShouldBe(phase == "before-publication-attempt" ? 0 : 1);
            row.State.ShouldBe(phase == "before-publication-attempt" ? ChannelOutboundDeliveryState.Ready : ChannelOutboundDeliveryState.PublishUncertain);
            row.PublishedAt.ShouldBeNull();
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
            w.Producer.Entries.ShouldBe(phase == "before-publication-attempt" ? 0 : 1);
            if (phase != "before-publication-attempt") { await w.TickAsync(); w.Producer.Entries.ShouldBe(1); }
        }
    }

    [Test]
    public async Task C519_Send_deadline_bounds_the_attempt()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        var entered = Signal(); var late = Signal();
        using var host = new CancellationTokenSource();
        CancellationToken producerToken = default;
        w.Producer.OnSend = (_, ct) => { producerToken = ct; entered.SetResult(); return late.Task; };
        var run = w.TickAsync(ct: host.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            w.Clock.Advance(TimeSpan.FromSeconds(29));
            run.IsCompleted.ShouldBeFalse();
            w.Clock.Advance(TimeSpan.FromSeconds(1));
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            host.IsCancellationRequested.ShouldBeFalse();
            producerToken.IsCancellationRequested.ShouldBeTrue();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
            await w.TickAsync(); w.Producer.Entries.ShouldBe(1);
        }
        finally { late.TrySetResult(); await w.Producer.LastCall.WaitAsync(TimeSpan.FromSeconds(10)); }
        w.Producer.Receipts.Count.ShouldBe(1); // A late acceptance still cannot settle the source.
        (await w.LoadAsync(id)).PublishedAt.ShouldBeNull();
    }

    [Test]
    public async Task C519_Uncertain_never_automatically_retries()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        w.Producer.OnSend = (_, _) => throw new IOException("unknown result");
        await w.TickAsync();
        w.Producer.OnSend = null;
        for (var i = 0; i < 2; i++) { w.Clock.Advance(TimeSpan.FromSeconds(301)); await w.TickAsync(); }
        w.Producer.Entries.ShouldBe(1);
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        w.Producer.Receipts.ShouldBeEmpty();
    }

    [Test]
    public async Task C519_Retry_requires_duplicate_acknowledgement()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        await w.SetAsync(id, d => { d.State = ChannelOutboundDeliveryState.PublishUncertain; d.PublicationAttempts = 3; });
        await using var db = w.Db();
        await Should.ThrowAsync<ValidationException>(() => w.Service(db).RetryUncertainAsync(id, false, default));
        var row = await w.LoadAsync(id);
        row.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        row.PublicationAttempts.ShouldBe(3); row.PublicationAttemptBudgetBase.ShouldBe(0);
        await w.TickAsync(); w.Producer.Entries.ShouldBe(0);
        await w.Service(db).RetryUncertainAsync(id, true, default);
        await w.TickAsync(); w.Producer.Receipts.Count.ShouldBe(1);
    }

    [Test]
    public async Task C519_Explicit_retry_keeps_lifetime_attempts()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        await w.SetAsync(id, d => { d.State = ChannelOutboundDeliveryState.PublishUncertain; d.PublicationAttempts = 3; });
        await using (var db = w.Db()) await w.Service(db).RetryUncertainAsync(id, true, default);
        (await w.LoadAsync(id)).PublicationAttemptBudgetBase.ShouldBe(3);
        w.Producer.OnSend = (_, _) => throw QueueFull();
        for (var attempt = 4; attempt <= 6; attempt++)
        {
            await w.TickAsync();
            var row = await w.LoadAsync(id);
            row.PublicationAttempts.ShouldBe(attempt); row.PublicationAttemptBudgetBase.ShouldBe(3);
            row.State.ShouldBe(attempt == 6 ? ChannelOutboundDeliveryState.Failed : ChannelOutboundDeliveryState.Ready);
            w.Clock.Advance(TimeSpan.FromSeconds(30));
        }
        await w.TickAsync(); w.Producer.Entries.ShouldBe(3);
    }

    [Test]
    public async Task C519_Only_definite_queue_refusal_is_automatic()
    {
        foreach (var kind in new[] { "sync", "async", "serialization", "accepted-fault", "queue" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.ReadyAsync();
            w.Producer.OnSend = (reply, _) => kind switch
            {
                "sync" => throw new IOException("unknown sync"),
                "async" => Task.FromException(new TimeoutException("unknown async")),
                "serialization" => throw new System.Text.Json.JsonException("unknown serialization"),
                "accepted-fault" => AcceptThenFault(w, reply),
                _ => throw QueueFull(),
            };
            await w.TickAsync();
            var row = await w.LoadAsync(id);
            row.State.ShouldBe(kind == "queue" ? ChannelOutboundDeliveryState.Ready : ChannelOutboundDeliveryState.PublishUncertain);
            row.NextAttemptAt.ShouldBe(kind == "queue" ? w.Now.AddSeconds(30) : null);
            row.PublishedAt.ShouldBeNull(); (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull();
            await w.TickAsync(); w.Producer.Entries.ShouldBe(1);
            w.Producer.Receipts.Count.ShouldBe(kind == "accepted-fault" ? 1 : 0);
        }
    }

    [Test]
    public async Task C519_Size_refusal_is_terminal()
    {
        foreach (var local in new[] { false, true })
        {
            await using var w = await World.CreateAsync();
            var id = await w.ReadyAsync();
            if (local) w.Messaging.MaxMessageBytes = 1;
            else w.Producer.OnSend = (_, _) => throw new ProduceException<string, string>(new Error(ErrorCode.MsgSizeTooLarge), new());
            await w.TickAsync();
            var row = await w.LoadAsync(id);
            row.State.ShouldBe(ChannelOutboundDeliveryState.Failed); row.PublishedAt.ShouldBeNull();
            row.PublicationAttempts.ShouldBe(local ? 0 : 1);
            await w.TickAsync(); w.Producer.Entries.ShouldBe(local ? 0 : 1);
            w.Producer.Receipts.ShouldBeEmpty();
            // A valid unchanged companion proves this isn't a dead producer/fixture.
            w.Messaging.MaxMessageBytes = new AntiphonMessagingOptions().MaxMessageBytes;
            w.Producer.OnSend = null;
            var good = await w.ReadyAsync(); await w.TickAsync();
            w.Producer.Receipts.ShouldHaveSingleItem().Text.ShouldBe("frozen reply " + good);
        }
    }

    [Test]
    public async Task C519_Retry_waits_until_persisted_due_time()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        w.Producer.OnSend = (_, _) => throw QueueFull();
        await w.TickAsync();
        var due = (await w.LoadAsync(id)).NextAttemptAt;
        due.ShouldBe(w.Now.AddSeconds(30));
        var version = (await w.LoadAsync(id)).Version;
        w.Clock.Advance(TimeSpan.FromSeconds(29)); await w.TickAsync();
        w.Producer.Entries.ShouldBe(1);
        (await w.LoadAsync(id)).Version.ShouldBe(version); // No claim before due, even if entry also checks due.
        (await w.LoadAsync(id)).NextAttemptAt.ShouldBe(due);
        w.Producer.OnSend = null;
        w.Clock.Advance(TimeSpan.FromSeconds(1)); await w.TickAsync();
        (await w.LoadAsync(id)).PublicationAttempts.ShouldBe(2);
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        w.Producer.Receipts.ShouldHaveSingleItem().ReplyHandle.ShouldBe("native-thread");
    }

    [Test]
    public async Task C519_Publication_cap_survives_restart()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        w.Producer.OnSend = (_, _) => throw QueueFull();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await w.TickAsync();
            (await w.LoadAsync(id)).PublicationAttempts.ShouldBe(attempt);
            w.Clock.Advance(TimeSpan.FromSeconds(30));
        }
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Failed);
        await w.TickAsync(); w.Producer.Entries.ShouldBe(3);
        // A refused terminal save must leave conservative Publishing, not grant a fourth entry.
        var other = await w.ReadyAsync();
        await w.SetAsync(other, d => d.PublicationAttempts = 2);
        var fault = new RefusalCommandFault();
        await w.TickAsync(interceptor: fault);
        fault.Fired.ShouldBeTrue();
        (await w.LoadAsync(other)).State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
        w.Clock.Advance(TimeSpan.FromSeconds(301)); await w.TickAsync();
        (await w.LoadAsync(other)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        w.Producer.Entries.ShouldBe(4);
        await w.TickAsync(); w.Producer.Entries.ShouldBe(4);
    }

    [Test] public Task C519_Binding_disabled_holds_at_final_entry() => BindingAsync("enabled");
    [Test] public Task C519_Binding_owner_holds_at_final_entry() => BindingAsync("owner");
    [Test] public Task C519_Binding_project_holds_at_final_entry() => BindingAsync("project");
    private static async Task BindingAsync(string guard)
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync(); var fired = false;
        await w.TickAsync(async (at, _, _) =>
        {
            if (at != "before-producer-call") return;
            fired = true;
            await using var db = w.Db();
            var channel = await db.ChatChannels.SingleAsync();
            if (guard == "enabled") channel.Enabled = false;
            if (guard == "owner")
            {
                var other = new Agent { Id = Guid.NewGuid(), BoardId = w.Board, Name = "other", Slug = "other" };
                db.Agents.Add(other); channel.AgentId = other.Id;
            }
            if (guard == "project")
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "other" }; db.Projects.Add(project);
                (await db.Boards.SingleAsync()).ProjectId = project.Id;
            }
            await db.SaveChangesAsync();
        });
        fired.ShouldBeTrue();
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Held);
        (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldBeNull(); w.Producer.Entries.ShouldBe(0);
        await w.TickAsync(); w.Producer.Entries.ShouldBe(0);
        // Fresh binding repair still requires explicit resume.
        await using (var db = w.Db())
        {
            var channel = await db.ChatChannels.SingleAsync(); channel.Enabled = true; channel.AgentId = w.Inbound;
            (await db.Boards.SingleAsync()).ProjectId = w.Project;
            await db.SaveChangesAsync(); await w.Service(db).ResumeHeldAsync(id, default);
        }
        await w.TickAsync(); w.Producer.Receipts.ShouldHaveSingleItem().ReplyHandle.ShouldBe("native-thread");
    }

    [Test]
    public async Task C519_Revocation_prevents_converted_publication()
    {
        await using var w = await World.CreateAsync();
        var id = await w.ReadyAsync();
        var converted = await w.Files.StageAsync(Guid.NewGuid(), new ChannelReply { Channel = "fake", ConversationId = w.Channel.ToString("N"), Text = "converted PDF" }, default);
        w.Settings.Profiles["convert"] = new() { ProjectId = w.Project, AgentId = w.Inbound, PromptFile = "convert.md" };
        await using (var db = w.Db()) { (await db.ChatChannels.SingleAsync()).OutboundAgentProfile = "convert"; await db.SaveChangesAsync(); }
        await w.SetAsync(id, d => { d.ProfileName = "convert"; d.ConverterAgentId = w.Inbound; d.ConversionOutcome = "Converted"; d.OutputPath = converted.ReplyPath; d.OutputSha256 = converted.ReplySha256; });
        var fired = false;
        await w.TickAsync((at, _, _) => { if (at == "before-producer-call") { fired = true; w.Settings.Profiles.Clear(); } return Task.CompletedTask; });
        fired.ShouldBeTrue(); w.Producer.Entries.ShouldBe(0);
        await w.TickAsync(); w.Producer.Receipts.ShouldHaveSingleItem().Text.ShouldBe("frozen reply " + id);
        (await w.LoadAsync(id)).ConversionOutcome.ShouldBe("Revoked");
    }

    [Test]
    public async Task C519_Captured_held_and_uncertain_heads_preserve_order()
    {
        foreach (var state in new[] { ChannelOutboundDeliveryState.Captured, ChannelOutboundDeliveryState.Held, ChannelOutboundDeliveryState.PublishUncertain })
        {
            await using var w = await World.CreateAsync();
            var head = await w.ReadyAsync(); var tail = await w.ReadyAsync();
            // Equal timestamps force the Id tie-breaker; ensure head is the smaller Id.
            if (head.CompareTo(tail) > 0) (head, tail) = (tail, head);
            await w.SetAsync(head, d => d.State = state);
            await w.TickAsync(); w.Producer.Entries.ShouldBe(0);
            await w.SetAsync(head, d => d.State = ChannelOutboundDeliveryState.Failed);
            await w.TickAsync();
            w.Producer.Receipts.ShouldHaveSingleItem().Text.ShouldBe("frozen reply " + tail);
        }
    }

    [Test]
    public async Task C519_Resume_held_restores_the_original_phase()
    {
        await using var w = await World.CreateAsync();
        await using var db = w.Db();
        var captured = await w.Service(db).CaptureAsync(new ChannelReply { Channel = "fake", ConversationId = w.Channel.ToString("N") },
            new(w.Session, 100, 101, 102, "main", []), ChannelReplyPreparation.Describe("unprepared"), new(), default);
        await w.SetAsync(captured.Id, d => d.State = ChannelOutboundDeliveryState.Held);
        db.ChangeTracker.Clear(); await w.Service(db).ResumeHeldAsync(captured.Id, default);
        (await w.LoadAsync(captured.Id)).State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        var pending = await w.ReadyAsync();
        await w.SetAsync(pending, d => { d.State = ChannelOutboundDeliveryState.Held; d.ConverterAgentId = w.Inbound; d.ConversionOutcome = null; });
        await w.Service(db).ResumeHeldAsync(pending, default);
        (await w.LoadAsync(pending)).State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        var ready = await w.ReadyAsync();
        await w.SetAsync(ready, d => { d.State = ChannelOutboundDeliveryState.Held; d.ConverterAgentId = w.Inbound; d.ConversionOutcome = "Converted"; });
        await w.Service(db).ResumeHeldAsync(ready, default);
        (await w.LoadAsync(ready)).State.ShouldBe(ChannelOutboundDeliveryState.Ready);
    }

    [Test]
    public void C519_Settings_require_finite_positive_bounds()
    {
        var bounds = new (string Name, int Max)[] { ("ScanIntervalSeconds", 60), ("PageSize", 32), ("MaximumPages", 10),
            ("RetryDelaySeconds", 300), ("SendTimeoutSeconds", 300), ("LeaseSeconds", 900), ("PreparationAttemptLimit", 3), ("PublicationAttemptLimit", 3) };
        var validator = new ChannelOutboundSettingsValidator();
        foreach (var (name, max) in bounds)
        foreach (var value in new[] { -1, 0, max + 1, int.MaxValue })
        {
            var settings = new ChannelOutboundSettings();
            typeof(ChannelOutboundSettings).GetProperty(name)!.SetValue(settings, value);
            var result = validator.Validate(null, settings);
            result.Failed.ShouldBeTrue(name + "=" + value);
            string.Join(";", result.Failures!).ShouldContain(name);
        }
        validator.Validate(null, new()).Succeeded.ShouldBeTrue();
        new ChannelOutboundSettings().UnifiedRecoveryEnabled.ShouldBeFalse();
    }

    [Test]
    public void C519_Send_timeout_must_be_less_than_lease()
    {
        var validator = new ChannelOutboundSettingsValidator();
        validator.Validate(null, new() { SendTimeoutSeconds = 30, LeaseSeconds = 30 }).Failed.ShouldBeTrue();
        validator.Validate(null, new() { SendTimeoutSeconds = 29, LeaseSeconds = 30 }).Succeeded.ShouldBeTrue();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ProduceException<string, string> QueueFull() => new(new Error(ErrorCode.Local_QueueFull, "definite refusal"), new());
    private static Task AcceptThenFault(World w, ChannelReply reply) { w.Producer.Receipts.Add(reply); return Task.FromException(new IOException("accepted before fault")); }

    private sealed class Producer : IAntiphonMessagingProducer
    {
        public int Entries { get; private set; }
        public List<ChannelReply> Receipts { get; } = [];
        public Func<ChannelReply, CancellationToken, Task>? OnSend { get; set; }
        public Task LastCall { get; private set; } = Task.CompletedTask;
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        { Entries++; return LastCall = ReceiveAsync(reply, cancellationToken); }
        private async Task ReceiveAsync(ChannelReply reply, CancellationToken ct)
        { if (OnSend != null) await OnSend(reply, ct); Receipts.Add(reply); }
    }
    private sealed class AttemptSaveFault : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ChannelOutboundDelivery>().Any(e => e.Entity.State == ChannelOutboundDeliveryState.Publishing && e.Entity.PublicationAttempts > 0))
            { Fired = true; throw new IOException("attempt commit refusal"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RefusalCommandFault : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.StartsWith("UPDATE \"ChannelOutboundDeliveries\"") && command.CommandText.Contains("\"FailureReason\"") && command.CommandText.Contains("\"NextAttemptAt\""))
            { Fired = true; throw new IOException("refusal commit refusal"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class World(IsolatedTestSchema schema, string root) : IAsyncDisposable
    {
        private string Root => root;
        public Guid Project { get; } = Guid.NewGuid(); public Guid Board { get; } = Guid.NewGuid();
        public Guid Inbound { get; } = Guid.NewGuid(); public Guid Channel { get; } = Guid.NewGuid(); public Guid Session { get; } = Guid.NewGuid();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public Producer Producer { get; } = new();
        public ChannelOutboundSettings Settings { get; } = new() { UnifiedRecoveryEnabled = true };
        public AntiphonMessagingOptions Messaging { get; } = new();
        public ChannelOutboundFileStore Files { get; } = new(Path.Combine(root, "store"));
        private long _sequence;
        public AppDbContext Db(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (interceptor != null) builder.AddInterceptors(interceptor);
            return new(builder.Options);
        }
        public static async Task<World> CreateAsync()
        {
            var w = new World(await TestDbFixture.CreateIsolatedSchemaAsync(), Directory.CreateTempSubdirectory("c519-retry-").FullName);
            await using var db = w.Db();
            db.Projects.Add(new() { Id = w.Project, Name = "retry" });
            db.Boards.Add(new() { Id = w.Board, ProjectId = w.Project, Name = "retry" });
            db.Agents.Add(new() { Id = w.Inbound, BoardId = w.Board, Name = "inbound", Slug = "inbound", WorkingDirectory = w.Root });
            db.AgentSessions.Add(new() { Id = w.Session, Cwd = w.Root });
            db.ChatChannels.Add(new() { Id = w.Channel, Provider = "fake", ExternalId = w.Channel.ToString("N"), AgentId = w.Inbound, Enabled = true });
            await db.SaveChangesAsync(); return w;
        }
        public async Task<Guid> ReadyAsync()
        {
            var id = Guid.NewGuid();
            var snapshot = await Files.StageAsync(id, new ChannelReply { Channel = "fake", ConversationId = Channel.ToString("N"), ReplyHandle = "native-thread", Text = "frozen reply " + id }, default);
            await using var db = Db();
            db.ChannelOutboundDeliveries.Add(new() { Id = id, SourceKey = id.ToString("N"), ChannelId = Channel, ProjectId = Project,
                InboundAgentId = Inbound, SourceSessionId = Session, SendKind = "main", PromptSequence = ++_sequence,
                InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256, State = ChannelOutboundDeliveryState.Ready,
                CreatedAt = Now, DeadlineAt = Now.AddMinutes(2), ConversionOutcome = "Passthrough" });
            db.SessionQueuedMessages.Add(new() { Id = Guid.NewGuid(), AgentSessionId = Session, Sequence = _sequence,
                Body = "original prompt", Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                ChannelOutboundDeliveryId = id, CreatedAt = Now, SentAt = Now });
            await db.SaveChangesAsync(); return id;
        }
        public ChannelOutboundService Service(AppDbContext db) => new(db, Files, Producer, Options.Create(Settings), Clock);
        public async Task TickAsync(Func<string, Guid, CancellationToken, Task>? barrier = null, IInterceptor? interceptor = null, CancellationToken ct = default)
        {
            await using var db = Db(interceptor);
            var pump = new ChannelOutboundDeliveryPump(db, null!, Files, Producer, Options.Create(Messaging), Clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance, Options.Create(Settings)) { ProbeBarrierAsync = barrier };
            await pump.TickAsync(ct);
        }
        public async Task SetAsync(Guid id, Action<ChannelOutboundDelivery> change)
        { await using var db = Db(); var row = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id); change(row); await db.SaveChangesAsync(); }
        public async Task<ChannelOutboundDelivery> LoadAsync(Guid id)
        { await using var db = Db(); return await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id); }
        public async Task<SessionQueuedMessage> MemberAsync(Guid id)
        { await using var db = Db(); return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ChannelOutboundDeliveryId == id); }
        public async ValueTask DisposeAsync() { await schema.DisposeAsync(); Directory.Delete(root, true); }
    }
}
