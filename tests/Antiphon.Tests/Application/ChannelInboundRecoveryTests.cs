using System.Text.Json;
using System.Data.Common;
using System.Collections.Concurrent;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public sealed class ChannelInboundRecoveryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static ChannelBridgeService Bridge(BridgeQueueHarness h, TimeProvider? bridgeClock = null) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), bridgeClock ?? h.Clock,
        NullLogger<ChannelBridgeService>.Instance,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

    private static AppDbContext Db(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static ChannelMessage Message(string chat, string native, string body, string sender = "alice") => new()
    {
        Id = Guid.NewGuid().ToString("N"), Channel = "telegram", ChannelMessageId = native,
        Conversation = new Conversation { Id = chat, Kind = ConversationKind.Group, Title = "Family" },
        Author = new Participant { Id = sender, DisplayName = sender },
        Timestamp = DateTimeOffset.UtcNow, Text = body, ReplyHandle = chat,
        Raw = JsonDocument.Parse("{\"from\":\"native\"}").RootElement.Clone(),
    };

    private static ChannelBridgeSettings Settings(int debounce = 0, int timeout = 1) => new()
    {
        Enabled = true, DebounceWindowMs = debounce, DebounceMaxMs = Math.Max(2000, debounce * 2),
        AgentStartTimeoutSeconds = timeout, AgentReadyDelaySeconds = 0,
    };

    private static void InstallTranscriptReceipt(BridgeQueueHarness h) =>
        InstallTranscriptReceipt(h, h.SessionId, h.Adapter);

    private static void InstallTranscriptReceipt(BridgeQueueHarness h, Guid sessionId,
        FakeAgentProtocolAdapter adapter)
    {
        var syncGate = new SemaphoreSlim(1, 1);
        adapter.OnSubmitted = async submitted =>
        {
            await syncGate.WaitAsync();
            try
            {
                await using var db = Db(h.ConnectionString);
                var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
                    .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
                var record = new SessionRunnerTranscriptEvent(sessionId, sequence, TranscriptKinds.UserPrompt,
                    Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                    null, null, null, null, null);
                h.Runner.SetTranscript(new SessionRunnerTranscriptDto(sessionId, [record], sequence));
                await h.Runtime.SyncTranscriptAsync(sessionId, Ct);
            }
            finally { syncGate.Release(); }
        };
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(25);
        }
        (await predicate()).ShouldBeTrue("durable handoff did not complete before the diagnostic deadline");
    }

    [Test]
    [Arguments(2)]
    [Arguments(64)]
    public async Task C767_HealthyAgent_ReceivesBeforeFailedWakeDeadline(int earlierMessages)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 90),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var failingChat = await h.BindChannelAsync();
        var healthyChat = await h.BindChannelAsync();
        var healthyWorkspace = Path.Combine(h.TempRoot, "healthy");
        Directory.CreateDirectory(healthyWorkspace);
        await using var agentScope = h.Provider.CreateAsyncScope();
        var healthy = await agentScope.ServiceProvider.GetRequiredService<AgentService>()
            .CreateAsync(new CreateAgentRequest("Healthy channel recipient", healthyWorkspace), Ct);
        var healthySession = Guid.NewGuid();
        await using (var setup = Db(schema.ConnectionString))
        {
            await setup.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            setup.AgentSessions.Add(new AgentSession
            {
                Id = healthySession, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
                Status = SessionStatus.Running, Cwd = healthyWorkspace,
                CreatedAt = h.Now, StartedAt = h.Now, LastSeenAt = h.Now,
            });
            await setup.SaveChangesAsync();
            await setup.Agents.Where(a => a.Id == healthy.Id).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.AlwaysOn, false)
                .SetProperty(a => a.PersistentSessionId, healthySession.ToString("D")));
            await setup.ChatChannels.Where(c => c.ExternalId == healthyChat)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentId, healthy.Id));
            var channels = await setup.ChatChannels.AsNoTracking()
                .Where(c => c.ExternalId == failingChat || c.ExternalId == healthyChat)
                .ToDictionaryAsync(c => c.ExternalId, c => c.Id);
            for (var index = 0; index < earlierMessages; index++)
            {
                var native = $"failing-{index:D2}-{Guid.NewGuid():N}";
                setup.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = failingChat,
                    NativeMessageId = native, AgentId = h.AgentId,
                    ChatChannelId = channels[failingChat],
                    EnvelopeJson = JsonSerializer.Serialize(Message(failingChat, native, $"failed body {index}"),
                        Antiphon.Messaging.MessagingJson.Options),
                    AcceptedAt = h.Now,
                });
            }
            await setup.SaveChangesAsync();
            var healthyNative = $"healthy-{Guid.NewGuid():N}";
            setup.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = "telegram", ConversationId = healthyChat,
                NativeMessageId = healthyNative, AgentId = healthy.Id,
                ChatChannelId = channels[healthyChat],
                EnvelopeJson = JsonSerializer.Serialize(Message(healthyChat, healthyNative,
                    "healthy complete body " + new string('b', 220) + " DISTINCT HEALTHY TAIL"),
                    Antiphon.Messaging.MessagingJson.Options),
                AcceptedAt = h.Now,
            });
            await setup.SaveChangesAsync();
            var firstHealthy = await setup.ChannelInbounds.Where(i => i.NativeMessageId == healthyNative)
                .Select(i => i.AcceptanceSequence).SingleAsync();
            (await setup.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId
                && i.AcceptanceSequence < firstHealthy)).ShouldBe(earlierMessages);
        }
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn",
            sessionId: healthySession);
        var receipt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthyAdapter = new FakeAgentProtocolAdapter();
        healthyAdapter.OnSubmitted = async submitted =>
        {
            await using var db = Db(schema.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == healthySession)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            var record = new SessionRunnerTranscriptEvent(healthySession, sequence, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                null, null, null, null, null);
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(healthySession, [record], sequence));
            await h.Runtime.SyncTranscriptAsync(healthySession, Ct);
            if (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == healthySession
                && t.Sequence == sequence && t.Kind == TranscriptKinds.UserPrompt && t.Text == submitted))
                receipt.TrySetResult(submitted);
        };
        h.Runtime.Register(healthySession, healthyAdapter);
        var clock = new BridgeWakeClock();
        var bridge = Bridge(h, clock);
        using var drainCts = new CancellationTokenSource();
        var drain = bridge.DrainPendingAsync(drainCts.Token);
        string submittedBody;
        try
        {
            await clock.PollRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var observed = await Task.WhenAny(receipt.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            observed.ShouldBe(receipt.Task, "healthy recipient lacks a complete UserPrompt before the failing wake deadline");
            submittedBody = await receipt.Task;
            drain.IsCompleted.ShouldBeFalse();
            clock.GetUtcNow().ShouldBe(clock.Start);
            await using var beforeDeadline = Db(schema.ConnectionString);
            (await beforeDeadline.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
                && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(0);
            var owner = await beforeDeadline.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.AgentSessionId == healthySession && q.SourceChannelInboundId != null);
            submittedBody.ShouldBe(owner.Body);
            submittedBody.ShouldContain($"[antiphon-channel:{owner.Id:N}]");
            submittedBody.ShouldContain("DISTINCT HEALTHY TAIL");
            submittedBody.ShouldContain("Telegram direct message");
            submittedBody.ShouldContain("alice");
            (await beforeDeadline.TranscriptEntries.CountAsync(t => t.AgentSessionId == healthySession
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > owner.LastDeliveryBaselineSequence
                && t.Text == owner.Body)).ShouldBe(1);
            h.Adapter.SentInput.ShouldBeEmpty();
        }
        finally
        {
            if (receipt.Task.IsCompletedSuccessfully)
            {
                clock.Advance(TimeSpan.FromSeconds(90));
                await drain.WaitAsync(TimeSpan.FromSeconds(20));
            }
            else
            {
                await drainCts.CancelAsync();
                try { await drain.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (OperationCanceledException) { }
            }
        }
        await using var afterDeadline = Db(schema.ConnectionString);
        (await afterDeadline.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId
            && i.QueueMessageId == null && i.WakeTimeoutIncidentAt != null)).ShouldBe(earlierMessages);
        (await afterDeadline.TranscriptEntries.CountAsync(t => t.AgentSessionId == healthySession
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == submittedBody)).ShouldBe(1);
        var retry = bridge.DrainPendingAsync(Ct);
        await clock.SecondPollRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(90));
        await retry.WaitAsync(TimeSpan.FromSeconds(20));
        (await afterDeadline.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(earlierMessages);
        (await afterDeadline.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId
            && i.QueueMessageId == null)).ShouldBe(earlierMessages);
        (await afterDeadline.TranscriptEntries.CountAsync(t => t.AgentSessionId == healthySession
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == submittedBody)).ShouldBe(1);
    }

    private sealed class BridgeWakeClock : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
        public DateTimeOffset Start => _clock.GetUtcNow() - _advanced;
        private TimeSpan _advanced;
        public TaskCompletionSource PollRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondPollRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _polls;
        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == TimeSpan.FromSeconds(2))
            {
                PollRegistered.TrySetResult();
                if (Interlocked.Increment(ref _polls) >= 2) SecondPollRegistered.TrySetResult();
            }
            return _clock.CreateTimer(callback, state, dueTime, period);
        }
        public void Advance(TimeSpan amount) { _advanced += amount; _clock.Advance(amount); }
    }

    private sealed record HealthyRecipient(Guid AgentId, Guid SessionId, string NativeId,
        FakeAgentProtocolAdapter Adapter, Task Receipt);

    private static async Task<HealthyRecipient> SeedHealthyRecipientAsync(BridgeQueueHarness h, string body)
    {
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var chat = $"healthy-recipient-{Guid.NewGuid():N}";
        var native = $"healthy-message-{Guid.NewGuid():N}";
        var directory = Path.Combine(h.TempRoot, $"healthy-{agentId:N}");
        Directory.CreateDirectory(directory);
        await using (var seed = Db(h.ConnectionString))
        {
            seed.Agents.Add(new Agent
            {
                Id = agentId, Name = "Healthy recipient", Slug = $"healthy-{agentId:N}",
                WorkingDirectory = directory, AlwaysOn = false,
                PersistentSessionId = sessionId.ToString("D"), CreatedAt = h.Now, UpdatedAt = h.Now,
            });
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
                Status = SessionStatus.Running, Cwd = directory,
                CreatedAt = h.Now, StartedAt = h.Now, LastSeenAt = h.Now,
            });
            var channelId = Guid.NewGuid();
            seed.ChatChannels.Add(new ChatChannel
            {
                Id = channelId, Provider = "telegram", ExternalId = chat, ReplyHandle = chat,
                Kind = ChatChannelKind.Direct, AgentId = agentId, Enabled = true,
                CreatedAt = h.Now, UpdatedAt = h.Now,
            });
            seed.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                NativeMessageId = native, AgentId = agentId, ChatChannelId = channelId,
                EnvelopeJson = JsonSerializer.Serialize(Message(chat, native, body),
                    Antiphon.Messaging.MessagingJson.Options), AcceptedAt = h.Now,
            });
            await seed.SaveChangesAsync();
        }
        var adapter = new FakeAgentProtocolAdapter();
        InstallTranscriptReceipt(h, sessionId, adapter);
        var original = adapter.OnSubmitted!;
        var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnSubmitted = async submitted =>
        {
            await original(submitted);
            await using var evidence = Db(h.ConnectionString);
            if (await evidence.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == submitted))
                receipt.TrySetResult();
        };
        h.Runtime.Register(sessionId, adapter);
        return new HealthyRecipient(agentId, sessionId, native, adapter, receipt.Task);
    }

    private static async Task AssertCompleteHealthyReceiptAsync(BridgeQueueHarness h, HealthyRecipient recipient)
    {
        await recipient.Receipt.WaitAsync(TimeSpan.FromSeconds(10));
        await using var db = Db(h.ConnectionString);
        var inbound = await db.ChannelInbounds.AsNoTracking()
            .SingleAsync(i => i.NativeMessageId == recipient.NativeId);
        var owner = await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == recipient.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        recipient.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C767_FailedAgent_WakesOncePerPass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var refusal = new StartLockRefusal();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 5),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(refusal),
        });
        var chat = await h.BindChannelAsync();
        var nativeIds = Enumerable.Range(0, 3).Select(i => $"wake-once-{i}-{Guid.NewGuid():N}").ToArray();
        var originals = new Dictionary<string, ChannelMessage>();
        await using (var seed = Db(schema.ConnectionString))
        {
            await seed.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            await seed.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            var channelId = await seed.ChatChannels.Where(c => c.ExternalId == chat)
                .Select(c => c.Id).SingleAsync();
            for (var index = 0; index < nativeIds.Length; index++)
            {
                var native = nativeIds[index];
                var original = Message(chat, native, native + " DISTINCT TAIL") with
                {
                    Attachments = [new Attachment
                    {
                        Kind = AttachmentKind.File, ChannelRef = $"file-{index}",
                        Name = $"wake-{index}.bin", Content = [(byte)(index + 1), 42],
                    }],
                };
                originals.Add(native, original);
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                    NativeMessageId = native, AgentId = h.AgentId, ChatChannelId = channelId,
                    EnvelopeJson = JsonSerializer.Serialize(original,
                        Antiphon.Messaging.MessagingJson.Options), AcceptedAt = h.Now,
                });
            }
            await seed.SaveChangesAsync();
        }
        var healthy = await SeedHealthyRecipientAsync(h, "healthy beside failed starts DISTINCT TAIL");
        refusal.Arm(h.AgentId);
        var bridge = Bridge(h);
        await bridge.DrainPendingAsync(Ct);
        refusal.Entries.ShouldBe(1, "one failed wake is allowed for an agent in one pass");
        await AssertCompleteHealthyReceiptAsync(h, healthy);
        await using (var verify = Db(schema.ConnectionString))
        {
            var retained = await verify.ChannelInbounds.AsNoTracking()
                .Where(i => nativeIds.Contains(i.NativeMessageId)).ToListAsync();
            retained.Count.ShouldBe(3);
            retained.ShouldAllBe(i => i.EnvelopeJson != null && i.QueueMessageId == null
                && i.TransferredAt == null && i.AgentId == h.AgentId);
            foreach (var inbound in retained)
            {
                var restored = JsonSerializer.Deserialize<ChannelMessage>(inbound.EnvelopeJson!,
                    Antiphon.Messaging.MessagingJson.Options)!;
                restored.Text.ShouldBe(originals[inbound.NativeMessageId].Text);
                restored.Attachments.Single().Content.ShouldBe(originals[inbound.NativeMessageId]
                    .Attachments.Single().Content);
                inbound.ChatChannelId.ShouldNotBeNull();
            }
            (await verify.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == h.SessionId)).ShouldBe(0);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        }
        await bridge.DrainPendingAsync(Ct);
        refusal.Entries.ShouldBe(2, "a later pass may make one new wake attempt");
        await using (var revive = Db(schema.ConnectionString))
        {
            await revive.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
            await revive.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, h.SessionId.ToString("D")));
        }
        await bridge.DrainPendingAsync(Ct);
        refusal.Entries.ShouldBe(2);
        await using var delivered = Db(schema.ConnectionString);
        var owners = await delivered.SessionQueuedMessages.AsNoTracking()
            .Where(q => q.AgentSessionId == h.SessionId && q.SourceChannelInboundId != null)
            .OrderBy(q => q.Sequence).ToListAsync();
        owners.Count.ShouldBe(3);
        foreach (var owner in owners)
            (await delivered.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        h.Adapter.SubmittedBodies.Count.ShouldBe(3);
        await AssertCompleteHealthyReceiptAsync(h, healthy);
    }

    private sealed class StartLockRefusal : DbCommandInterceptor
    {
        private Guid _target;
        private int _armed;
        private int _entries;
        public int Entries => Volatile.Read(ref _entries);
        public void Arm(Guid agentId) { _target = agentId; Volatile.Write(ref _armed, 1); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains("FROM \"Agents\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == _target))
            {
                Interlocked.Increment(ref _entries);
                throw new InvalidOperationException("synthetic channel start lock refusal");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task C767_AgentHeadPaging_WrapsPastHeldAgents()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(h);
        var healthyChat = await h.BindChannelAsync();
        var heldAgents = new List<(Guid AgentId, Guid SessionId, string Native)>();
        await using (var seed = Db(schema.ConnectionString))
        {
            for (var index = 0; index < 64; index++)
            {
                var agentId = Guid.NewGuid();
                var native = $"held-head-{index:D2}-{Guid.NewGuid():N}";
                var chat = $"held-chat-{index:D2}-{Guid.NewGuid():N}";
                var directory = Path.Combine(h.TempRoot, $"held-{index:D2}");
                Directory.CreateDirectory(directory);
                seed.Agents.Add(new Agent
                {
                    Id = agentId, Name = $"Held {index}", Slug = $"held-{Guid.NewGuid():N}",
                    WorkingDirectory = directory, AlwaysOn = false,
                    CreatedAt = h.Now, UpdatedAt = h.Now,
                });
                var channelId = Guid.NewGuid();
                seed.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId, Provider = "telegram", ExternalId = chat, ReplyHandle = chat,
                    Kind = ChatChannelKind.Direct, AgentId = agentId, Enabled = true,
                    CreatedAt = h.Now, UpdatedAt = h.Now,
                });
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                    NativeMessageId = native, AgentId = agentId, ChatChannelId = channelId,
                    EnvelopeJson = JsonSerializer.Serialize(Message(chat, native, $"held {index} full body"),
                        Antiphon.Messaging.MessagingJson.Options), AcceptedAt = h.Now,
                });
                heldAgents.Add((agentId, Guid.NewGuid(), native));
            }
            await seed.SaveChangesAsync();
            var healthyChannelId = await seed.ChatChannels.Where(c => c.ExternalId == healthyChat)
                .Select(c => c.Id).SingleAsync();
            var healthyNative = $"healthy-after-heads-{Guid.NewGuid():N}";
            seed.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = "telegram", ConversationId = healthyChat,
                NativeMessageId = healthyNative, AgentId = h.AgentId, ChatChannelId = healthyChannelId,
                EnvelopeJson = JsonSerializer.Serialize(Message(healthyChat, healthyNative,
                    "healthy after held heads DISTINCT TAIL"), Antiphon.Messaging.MessagingJson.Options),
                AcceptedAt = h.Now,
            });
            await seed.SaveChangesAsync();
            var healthySequence = await seed.ChannelInbounds.Where(i => i.NativeMessageId == healthyNative)
                .Select(i => i.AcceptanceSequence).SingleAsync();
            (await seed.ChannelInbounds.CountAsync(i => i.AcceptanceSequence < healthySequence
                && i.AgentId != h.AgentId)).ShouldBe(64);
        }
        await using var lockDb = Db(schema.ConnectionString);
        await lockDb.Database.OpenConnectionAsync();
        async Task SetLockAsync(Guid id, bool acquire)
        {
            await using var command = lockDb.Database.GetDbConnection().CreateCommand();
            command.CommandText = acquire ? "SELECT pg_advisory_lock(@key)" : "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = BitConverter.ToInt64(id.ToByteArray(), 0);
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync();
        }
        foreach (var held in heldAgents) await SetLockAsync(held.AgentId, true);
        try
        {
            var bridge = Bridge(h);
            await bridge.DrainPendingAsync(Ct);
            await using (var first = Db(schema.ConnectionString))
                (await first.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId
                    && i.QueueMessageId != null)).ShouldBe(0);
            await bridge.DrainPendingAsync(Ct);
            await using (var second = Db(schema.ConnectionString))
            {
                var owner = await second.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(q => q.AgentSessionId == h.SessionId && q.SourceChannelInboundId != null);
                (await second.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
                (await second.ChannelInbounds.CountAsync(i => heldAgents.Select(a => a.AgentId)
                    .Contains(i.AgentId!.Value) && i.QueueMessageId == null)).ShouldBe(64);
            }
            var released = heldAgents[0];
            await SetLockAsync(released.AgentId, false);
            await using (var revive = Db(schema.ConnectionString))
            {
                revive.AgentSessions.Add(new AgentSession
                {
                    Id = released.SessionId, AgentKind = AgentKind.ClaudeCode,
                    DefinitionName = "fake", Status = SessionStatus.Running,
                    Cwd = Path.Combine(h.TempRoot, "held-00"),
                    CreatedAt = h.Now, StartedAt = h.Now, LastSeenAt = h.Now,
                });
                await revive.SaveChangesAsync();
                await revive.Agents.Where(a => a.Id == released.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId,
                        released.SessionId.ToString("D")));
            }
            var adapter = new FakeAgentProtocolAdapter();
            InstallTranscriptReceipt(h, released.SessionId, adapter);
            h.Runtime.Register(released.SessionId, adapter);
            await bridge.DrainPendingAsync(Ct);
            await using var wrapped = Db(schema.ConnectionString);
            var wrappedOwner = await wrapped.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.AgentSessionId == released.SessionId && q.SourceChannelInboundId != null);
            (await wrapped.TranscriptEntries.CountAsync(t => t.AgentSessionId == released.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == wrappedOwner.Body)).ShouldBe(1);
            (await wrapped.ChannelInbounds.CountAsync(i => i.NativeMessageId == released.Native
                && i.QueueMessageId == wrappedOwner.Id)).ShouldBe(1);
        }
        finally
        {
            foreach (var held in heldAgents) await SetLockAsync(held.AgentId, false);
            await lockDb.Database.CloseConnectionAsync();
        }
    }

    [Test]
    public async Task C767_MalformedDelivery_IsAcknowledgedAndNextMessageArrives()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var native = $"after-malformed-{Guid.NewGuid():N}";
        var consumer = new TwoDeliveryConsumer(Message(chat, native, "valid after poison DISTINCT TAIL"));
        var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = Db(schema.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(h.SessionId,
                [new SessionRunnerTranscriptEvent(h.SessionId, sequence, TranscriptKinds.UserPrompt,
                    Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                    null, null, null, null, null)], sequence));
            await h.Runtime.SyncTranscriptAsync(h.SessionId, Ct);
            receipt.TrySetResult();
        };
        var bridge = new ChannelBridgeService(consumer, h.Queue,
            h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock,
            NullLogger<ChannelBridgeService>.Instance,
            h.Provider.GetRequiredService<ChannelInboundWakeSignal>());
        await bridge.StartAsync(Ct);
        try
        {
            await receipt.Task.WaitAsync(TimeSpan.FromSeconds(15));
            consumer.Dispositions.ShouldBe(["malformed:synthetic null broker row", "accepted"]);
            await using var verify = Db(schema.ConnectionString);
            var inbound = await verify.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == native);
            var owner = await verify.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
            owner.Body.ShouldContain("valid after poison DISTINCT TAIL");
            (await verify.ChannelInbounds.CountAsync()).ShouldBe(1);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        }
        finally { await bridge.StopAsync(Ct); }
    }

    private sealed class TwoDeliveryConsumer(ChannelMessage valid) : IAntiphonMessagingConsumer
    {
        private readonly List<string> _dispositions = [];
        public IReadOnlyList<string> Dispositions => _dispositions;
        public async IAsyncEnumerable<ChannelMessage> ConsumeAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public async IAsyncEnumerable<InboundDelivery> ConsumeDeliveriesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new InboundDelivery(null, "synthetic null broker row",
                (disposition, _) => { _dispositions.Add(disposition); return Task.CompletedTask; });
            yield return new InboundDelivery(valid, null,
                (disposition, _) => { _dispositions.Add(disposition); return Task.CompletedTask; });
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    [Test]
    public async Task C767_ConcurrentDrains_ClaimMissRecoversWithoutDuplicatePrompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var gate = new StartLockGate();
        await using var first = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 5),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(gate),
        });
        var chat = await first.BindChannelAsync();
        var native = $"claim-miss-{Guid.NewGuid():N}";
        await using (var seed = Db(schema.ConnectionString))
        {
            await seed.AgentSessions.Where(s => s.Id == first.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            await seed.Agents.Where(a => a.Id == first.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            var channelId = await seed.ChatChannels.Where(c => c.ExternalId == chat)
                .Select(c => c.Id).SingleAsync();
            seed.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                NativeMessageId = native, AgentId = first.AgentId, ChatChannelId = channelId,
                EnvelopeJson = JsonSerializer.Serialize(Message(chat, native,
                    "claim miss complete body DISTINCT TAIL"), Antiphon.Messaging.MessagingJson.Options),
                AcceptedAt = first.Now,
            });
            await seed.SaveChangesAsync();
        }
        var healthy = await SeedHealthyRecipientAsync(first, "healthy during claim contention DISTINCT TAIL");
        await using var second = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachAgentId = first.AgentId,
            AttachSessionId = first.SessionId, Bridge = Settings(timeout: 5),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(second);
        gate.Arm(first.AgentId);
        var firstDrain = Bridge(first).DrainPendingAsync(Ct);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertCompleteHealthyReceiptAsync(first, healthy);
            await Bridge(second).DrainPendingAsync(Ct);
            gate.Entries.ShouldBe(1, "the competing bridge must miss the PostgreSQL agent claim");
            await using (var pending = Db(schema.ConnectionString))
                (await pending.ChannelInbounds.Where(i => i.NativeMessageId == native)
                    .Select(i => i.QueueMessageId).SingleAsync()).ShouldBeNull();
        }
        finally
        {
            gate.Release.TrySetResult();
            await firstDrain.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using (var revive = Db(schema.ConnectionString))
        {
            await revive.AgentSessions.Where(s => s.Id == first.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
            await revive.Agents.Where(a => a.Id == first.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId,
                    first.SessionId.ToString("D")));
        }
        await Bridge(second).DrainPendingAsync(Ct);
        await using var delivered = Db(schema.ConnectionString);
        var inbound = await delivered.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await delivered.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        (await delivered.TranscriptEntries.CountAsync(t => t.AgentSessionId == first.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        await AssertCompleteHealthyReceiptAsync(first, healthy);
        (await delivered.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId == inbound.Id)).ShouldBe(1);
        Guid repeatedOwner = Guid.Empty;
        await second.Queue.EnqueueAsync(first.SessionId, owner.Body, MessageSendMode.WhenIdle, Ct,
            origin: QueuedMessageOrigin.Channel, conversationKey: owner.ConversationKey,
            deliverIfIdle: false, onCreated: id => repeatedOwner = id,
            sourceChannelInboundId: inbound.Id, channelMemberInboundIds: [inbound.Id]);
        repeatedOwner.ShouldBe(owner.Id, "a repeated keyed handoff must report its existing owner");
        (await delivered.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId == inbound.Id)).ShouldBe(1);
        (await delivered.TranscriptEntries.CountAsync(t => t.AgentSessionId == first.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        gate.Entries.ShouldBe(1);
    }

    private sealed class StartLockGate : DbCommandInterceptor
    {
        private Guid _target;
        private int _entries;
        private int _active;
        public int Entries => Volatile.Read(ref _entries);
        public int Active => Volatile.Read(ref _active);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldForCancellation { get; set; }
        public void Arm(Guid agentId) => _target = agentId;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Agents\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == _target))
            {
                Interlocked.Increment(ref _entries);
                Interlocked.Increment(ref _active);
                Entered.TrySetResult();
                try
                {
                    if (HoldForCancellation)
                    {
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                        catch (OperationCanceledException) { CancellationObserved.TrySetResult(); }
                    }
                    await Release.Task;
                }
                finally { Interlocked.Decrement(ref _active); }
                if (HoldForCancellation) throw new OperationCanceledException(cancellationToken);
                throw new InvalidOperationException("synthetic gated start refusal");
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task C767_Stop_AwaitsCancelledGroupsAndRestartRecovers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var gate = new StartLockGate { HoldForCancellation = true };
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 90),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(gate),
        });
        var chat = await h.BindChannelAsync();
        var native = $"stop-group-{Guid.NewGuid():N}";
        await using (var seed = Db(schema.ConnectionString))
        {
            await seed.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            await seed.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            var channelId = await seed.ChatChannels.Where(c => c.ExternalId == chat)
                .Select(c => c.Id).SingleAsync();
            seed.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                NativeMessageId = native, AgentId = h.AgentId, ChatChannelId = channelId,
                EnvelopeJson = JsonSerializer.Serialize(Message(chat, native,
                    "recover after stopped group DISTINCT TAIL"), Antiphon.Messaging.MessagingJson.Options),
                AcceptedAt = h.Now,
            });
            await seed.SaveChangesAsync();
        }
        var healthy = await SeedHealthyRecipientAsync(h, "healthy before canceled stop DISTINCT TAIL");
        gate.Arm(h.AgentId);
        var bridge = Bridge(h);
        await bridge.StartAsync(Ct);
        Task stop = Task.CompletedTask;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            stop = bridge.StopAsync(Ct);
            await gate.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.IsCompleted.ShouldBeFalse("stop must await the canceled in-flight group");
        }
        finally { gate.Release.TrySetResult(); }
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        gate.Active.ShouldBe(0);
        await using (var lockDb = Db(schema.ConnectionString))
        {
            await lockDb.Database.OpenConnectionAsync();
            await using var command = lockDb.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var key = command.CreateParameter();
            key.ParameterName = "key";
            key.Value = BitConverter.ToInt64(h.AgentId.ToByteArray(), 0);
            command.Parameters.Add(key);
            (await command.ExecuteScalarAsync()).ShouldBe(true,
                "stop must release the canceled group's PostgreSQL agent claim");
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            await command.ExecuteScalarAsync();
        }
        await using (var pending = Db(schema.ConnectionString))
        {
            (await pending.ChannelInbounds.Where(i => i.NativeMessageId == native)
                .Select(i => i.QueueMessageId).SingleAsync()).ShouldBeNull();
            (await pending.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        }
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachAgentId = h.AgentId,
            AttachSessionId = h.SessionId, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(recovered);
        await using (var revive = Db(schema.ConnectionString))
        {
            await revive.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
            await revive.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId,
                    h.SessionId.ToString("D")));
        }
        await Bridge(recovered).DrainPendingAsync(Ct);
        await using var delivered = Db(schema.ConnectionString);
        var inbound = await delivered.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await delivered.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        (await delivered.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        await AssertCompleteHealthyReceiptAsync(h, healthy);
        gate.Entries.ShouldBe(1);
    }

    [Test]
    public async Task C767_AgentConcurrency_IsBoundedAndAllGroupsAreAwaited()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var gate = new CountingStartGate();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 5),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(gate),
        });
        var agents = new List<(Guid Id, Guid SessionId, string Native, string Directory)>();
        await using (var seed = Db(schema.ConnectionString))
        {
            for (var index = 0; index < 5; index++)
            {
                var id = Guid.NewGuid();
                var chat = $"bounded-chat-{index}-{Guid.NewGuid():N}";
                var native = $"bounded-native-{index}-{Guid.NewGuid():N}";
                var directory = Path.Combine(h.TempRoot, $"bounded-{index}");
                Directory.CreateDirectory(directory);
                seed.Agents.Add(new Agent
                {
                    Id = id, Name = $"Bounded {index}", Slug = $"bounded-{Guid.NewGuid():N}",
                    WorkingDirectory = directory, AlwaysOn = false,
                    CreatedAt = h.Now, UpdatedAt = h.Now,
                });
                var channelId = Guid.NewGuid();
                seed.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId, Provider = "telegram", ExternalId = chat, ReplyHandle = chat,
                    Kind = ChatChannelKind.Direct, AgentId = id, Enabled = true,
                    CreatedAt = h.Now, UpdatedAt = h.Now,
                });
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
                    NativeMessageId = native, AgentId = id, ChatChannelId = channelId,
                    EnvelopeJson = JsonSerializer.Serialize(Message(chat, native,
                        $"bounded complete body {index} DISTINCT TAIL"), Antiphon.Messaging.MessagingJson.Options),
                    AcceptedAt = h.Now,
                });
                agents.Add((id, Guid.NewGuid(), native, directory));
            }
            await seed.SaveChangesAsync();
        }
        gate.Arm(agents.Select(a => a.Id));
        var drain = Bridge(h).DrainPendingAsync(Ct);
        try
        {
            await gate.FourEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            gate.Peak.ShouldBe(4);
            gate.Entries.ShouldBe(4);
            drain.IsCompleted.ShouldBeFalse();
            gate.ReleaseOne();
            await gate.FiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            gate.Peak.ShouldBe(4, "the fifth wake must wait for a group slot");
        }
        finally { gate.ReleaseAll(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
        gate.Entries.ShouldBe(5);
        gate.Active.ShouldBe(0);
        await using (var pending = Db(schema.ConnectionString))
            (await pending.ChannelInbounds.CountAsync(i => agents.Select(a => a.Id)
                .Contains(i.AgentId!.Value) && i.QueueMessageId == null)).ShouldBe(5);
        await using (var revive = Db(schema.ConnectionString))
        {
            foreach (var agent in agents)
                revive.AgentSessions.Add(new AgentSession
                {
                    Id = agent.SessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, Cwd = agent.Directory,
                    CreatedAt = h.Now, StartedAt = h.Now, LastSeenAt = h.Now,
                });
            await revive.SaveChangesAsync();
            foreach (var agent in agents)
                await revive.Agents.Where(a => a.Id == agent.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId,
                        agent.SessionId.ToString("D")));
        }
        foreach (var agent in agents)
        {
            var adapter = new FakeAgentProtocolAdapter();
            InstallTranscriptReceipt(h, agent.SessionId, adapter);
            h.Runtime.Register(agent.SessionId, adapter);
        }
        await Bridge(h).DrainPendingAsync(Ct);
        await using var delivered = Db(schema.ConnectionString);
        foreach (var agent in agents)
        {
            var inbound = await delivered.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == agent.Native);
            var owner = await delivered.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
            (await delivered.TranscriptEntries.CountAsync(t => t.AgentSessionId == agent.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        }
    }

    private sealed class CountingStartGate : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _releases = new();
        private HashSet<Guid> _targets = [];
        private int _entries;
        private int _active;
        private int _peak;
        private int _open;
        public int Entries => Volatile.Read(ref _entries);
        public int Active => Volatile.Read(ref _active);
        public int Peak => Volatile.Read(ref _peak);
        public TaskCompletionSource FourEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm(IEnumerable<Guid> targets) => _targets = targets.ToHashSet();
        public void ReleaseOne() { if (_releases.TryDequeue(out var release)) release.TrySetResult(); }
        public void ReleaseAll()
        {
            Volatile.Write(ref _open, 1);
            while (_releases.TryDequeue(out var release)) release.TrySetResult();
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Agents\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && _targets.Contains(id)))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _releases.Enqueue(release);
                if (Volatile.Read(ref _open) == 1) release.TrySetResult();
                var active = Interlocked.Increment(ref _active);
                var peak = Volatile.Read(ref _peak);
                while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak)
                    peak = Volatile.Read(ref _peak);
                var entries = Interlocked.Increment(ref _entries);
                if (entries == 4) FourEntered.TrySetResult();
                if (entries == 5) FiveEntered.TrySetResult();
                try { await release.Task; }
                finally { Interlocked.Decrement(ref _active); }
                throw new InvalidOperationException("synthetic bounded start refusal");
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task C593_WakeTimeout_PreservesEnvelopeAndCriticalIncident()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var original = Message(chat, "timeout-" + Guid.NewGuid().ToString("N"), new string('x', 220) + "\nUNIQUE TAIL") with
        {
            Attachments = [new Attachment { Kind = AttachmentKind.File, ChannelRef = "file-1", Name = "proof.bin", Content = [1, 2, 3, 4] }],
            Mentions = [new Mention { Id = "target", IsMe = true }],
            ReplyTo = new ReplyReference { ChannelMessageId = "prior", Excerpt = "context" },
        };
        await Bridge(h).HandleInboundAsync(original, Ct);
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == original.ChannelMessageId);
        inbound.AgentId.ShouldBe(h.AgentId);
        inbound.ChatChannelId.ShouldNotBeNull();
        var restored = JsonSerializer.Deserialize<ChannelMessage>(inbound.EnvelopeJson!, Antiphon.Messaging.MessagingJson.Options)!;
        restored.Text.ShouldBe(original.Text);
        restored.Attachments.Single().Content.ShouldBe(original.Attachments.Single().Content);
        restored.ReplyTo!.ChannelMessageId.ShouldBe("prior");
        restored.Mentions.Single().Id.ShouldBe("target");
        restored.Raw.GetProperty("from").GetString().ShouldBe("native");
        (await verify.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == h.SessionId)).ShouldBe(0);
        h.Adapter.SentInput.ShouldBeEmpty();
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId &&
            i.Kind == AgentIncidentKind.ChannelReplyLost && i.Severity == AlertSeverity.Critical &&
            i.FailureReason == "ChannelWakeTimeout" && i.Message.Contains("pending") &&
            i.Message.Contains("has not reached"))).ShouldBe(1);
        await Bridge(h).DrainPendingAsync(Ct);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(1);

        await using var faultSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var incidentFault = new IncidentCommitFault();
        await using var faultHarness = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = faultSchema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(incidentFault),
        });
        var faultChat = await faultHarness.BindChannelAsync();
        await using (var setup = Db(faultSchema.ConnectionString))
            await setup.AgentSessions.Where(s => s.Id == faultHarness.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var faultMessage = Message(faultChat, $"incident-fault-{Guid.NewGuid():N}", "incident failure must be retryable");
        incidentFault.Arm();
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Bridge(faultHarness).HandleInboundAsync(faultMessage, Ct));
        await using (var cut = Db(faultSchema.ConnectionString))
        {
            var retained = await cut.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == faultMessage.ChannelMessageId);
            retained.EnvelopeJson.ShouldContain(faultMessage.Text!);
            retained.WakeTimeoutIncidentAt.ShouldBeNull();
            (await cut.AgentIncidents.CountAsync(i => i.AgentId == faultHarness.AgentId
                && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(0);
        }
        await Bridge(faultHarness).DrainPendingAsync(Ct);
        await using var repaired = Db(faultSchema.ConnectionString);
        (await repaired.AgentIncidents.CountAsync(i => i.AgentId == faultHarness.AgentId
            && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(1);
        (await repaired.ChannelInbounds.Where(i => i.NativeMessageId == faultMessage.ChannelMessageId)
            .Select(i => i.WakeTimeoutIncidentAt).SingleAsync()).ShouldNotBeNull();
    }

    [Test]
    public async Task C593_Restart_DrainsToEligibleAndBusyRecipients()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid agent; Guid session; string chat; string native;
        await using (var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        }))
        {
            agent = h.AgentId; session = h.SessionId; chat = await h.BindChannelAsync();
            await using (var db = Db(schema.ConnectionString))
                await db.AgentSessions.Where(s => s.Id == session)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            native = "restart-" + Guid.NewGuid().ToString("N");
            await Bridge(h).HandleInboundAsync(Message(chat, native, "durable across restart") with
            {
                Attachments = [new Attachment
                {
                    Kind = AttachmentKind.File, ChannelRef = "restart-file", Name = "restart-proof.bin",
                    Content = [41, 42, 43, 44],
                }],
            }, Ct);
        }
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == session)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        Guid replacementAgentId; Guid replacementSessionId;
        await using (var replacement = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        }))
        {
            replacementAgentId = replacement.AgentId;
            replacementSessionId = replacement.SessionId;
        }
        await using (var rebound = Db(schema.ConnectionString))
            await rebound.ChatChannels.Where(c => c.ExternalId == chat)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentId, replacementAgentId));
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(recovered);
        var recoveredBridge = Bridge(recovered);
        await recoveredBridge.StartAsync(Ct);
        await WaitForAsync(async () =>
        {
            await using var pending = Db(schema.ConnectionString);
            return await pending.ChannelInbounds.AnyAsync(i => i.NativeMessageId == native && i.QueueMessageId != null);
        });
        await recoveredBridge.StopAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        owner.Body.ShouldContain("durable across restart");
        owner.Body.ShouldContain("[antiphon-channel:");
        var inbox = Path.Combine((await verify.Agents.Where(a => a.Id == agent)
            .Select(a => a.WorkingDirectory).SingleAsync()), ".antiphon", "inbox");
        var saved = Directory.GetFiles(inbox).ShouldHaveSingleItem();
        File.ReadAllBytes(saved).ShouldBe(new byte[] { 41, 42, 43, 44 });
        owner.Body.ShouldContain(saved);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == session &&
            t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(owner.Body))).ShouldBe(1);
        (await verify.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == replacementSessionId
            && q.SourceChannelInboundId != null)).ShouldBe(0);
        await recoveredBridge.DrainPendingAsync(Ct);
        Directory.GetFiles(inbox).ShouldBe([saved]);

        await verify.ChatChannels.Where(c => c.ExternalId == chat)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentId, agent));
        await recovered.MarkWorkingAsync();
        var busyNative = $"busy-{Guid.NewGuid():N}";
        await Bridge(recovered).HandleInboundAsync(Message(chat, busyNative, "wait until turn end"), Ct);
        var busyInbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == busyNative);
        var busyOwner = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == busyInbound.Id);
        busyOwner.Status.ShouldBe(QueuedMessageStatus.Pending);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        await WaitForAsync(async () =>
        {
            await using var received = Db(schema.ConnectionString);
            return await received.TranscriptEntries.AnyAsync(t => t.AgentSessionId == session
                && t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(busyOwner.Body));
        });
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(2);

        await RecoveryTriggerAsync(signal: true);
        await RecoveryTriggerAsync(signal: false);
    }

    private static async Task RecoveryTriggerAsync(bool signal)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = signal ? null : 5,
            Bridge = Settings(timeout: 1), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        InstallTranscriptReceipt(h);
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var native = $"trigger-{signal}-{Guid.NewGuid():N}";
        await Bridge(h).HandleInboundAsync(Message(chat, native, "recovered by worker trigger"), Ct);
        var worker = Bridge(h);
        await worker.StartAsync(Ct);
        await WaitForAsync(() => Task.FromResult(worker.CompletedDrainIterations > 0));
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        if (signal)
            h.Provider.GetRequiredService<ChannelInboundWakeSignal>().Signal(h.AgentId);
        await WaitForAsync(async () =>
        {
            await using var db = Db(schema.ConnectionString);
            return await db.ChannelInbounds.AnyAsync(i => i.NativeMessageId == native && i.QueueMessageId != null)
                && await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text != null
                    && t.Text.Contains("recovered by worker trigger"));
        });
        await worker.StopAsync(Ct);
    }

    [Test]
    public async Task C593_CrashAfterQueueOwnership_RecoveryFlushesCompleteRecipientPrompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid agentId; Guid sessionId; Guid ownerId; string body;
        await using (var original = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        }))
        {
            agentId = original.AgentId;
            sessionId = original.SessionId;
            var chat = await original.BindChannelAsync();
            await original.MarkWorkingAsync();
            var native = $"owned-before-crash-{Guid.NewGuid():N}";
            await Bridge(original).HandleInboundAsync(Message(chat, native, "recover complete body DISTINCT TAIL"), Ct);
            await using var db = Db(schema.ConnectionString);
            var inbound = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
            inbound.QueueMessageId.ShouldNotBeNull();
            var owner = await db.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
            ownerId = owner.Id;
            body = owner.Body;
            owner.Status.ShouldBe(QueuedMessageStatus.Pending);
            owner.DeliveryAttempts.ShouldBe(0);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)).ShouldBe(0);
        }

        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachAgentId = agentId,
            AttachSessionId = sessionId, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(recovered);
        await recovered.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await using (var beforeStartup = Db(schema.ConnectionString))
            (await beforeStartup.ChannelInbounds.CountAsync(i => i.AgentId == agentId
                && i.EnvelopeJson != null && i.QueueMessageId == null)).ShouldBe(0,
                "startup must recover the already owned row without an unowned journal hint");
        var bridge = Bridge(recovered);
        await bridge.StartAsync(Ct);
        try
        {
            await WaitForAsync(async () =>
            {
                await using var db = Db(schema.ConnectionString);
                return await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == body);
            });
            await using var verify = Db(schema.ConnectionString);
            var owner = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownerId);
            owner.Body.ShouldBe(body);
            owner.Status.ShouldBe(QueuedMessageStatus.Sent);
            recovered.Adapter.SubmittedBodies.ShouldContain(body);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)).ShouldBe(1);
        }
        finally { await bridge.StopAsync(Ct); }
    }

    [Test]
    public async Task C593_AcceptanceAckAndQueueCommitCuts_AreIdempotent()
    {
        await using (var ackSchema = await TestDbFixture.CreateIsolatedSchemaAsync())
        {
            var gate = new AcceptanceGate();
            await using var hostedHarness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = ackSchema.ConnectionString, Bridge = Settings(timeout: 5),
                ClockSpeed = 20, AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureDbContext = options => options.AddInterceptors(gate),
            });
            var hostedChat = await hostedHarness.BindChannelAsync();
            await using (var setup = Db(ackSchema.ConnectionString))
                await setup.AgentSessions.Where(s => s.Id == hostedHarness.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            var acceptedMessage = Message(hostedChat, "hosted-" + Guid.NewGuid().ToString("N"),
                "full envelope before Kafka acknowledgement");
            gate.PauseNext();
            var hosted = Bridge(hostedHarness);
            await hosted.StartAsync(Ct);
            hostedHarness.Messaging.InjectInbound(acceptedMessage);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            hostedHarness.Messaging.AcknowledgedCount.ShouldBe(0);
            await using (var beforeCommit = Db(ackSchema.ConnectionString))
            {
                (await beforeCommit.ChannelInbounds.AnyAsync(i => i.NativeMessageId == acceptedMessage.ChannelMessageId)).ShouldBeFalse();
                (await beforeCommit.ChatChannels.Where(c => c.ExternalId == hostedChat)
                    .Select(c => c.MessageCount).SingleAsync()).ShouldBe(0);
            }
            gate.Release.TrySetResult(true);
            await WaitForAsync(() => Task.FromResult(hostedHarness.Messaging.AcknowledgedCount == 1));
            await using (var afterCommit = Db(ackSchema.ConnectionString))
                (await afterCommit.ChannelInbounds.Where(i => i.NativeMessageId == acceptedMessage.ChannelMessageId)
                    .Select(i => i.EnvelopeJson).SingleAsync()).ShouldContain(acceptedMessage.Text!);
            hostedHarness.Adapter.SentInput.ShouldBeEmpty(); // slow wake has not completed
            await hosted.StopAsync(Ct);

            gate.FailNext();
            var failedMessage = Message(hostedChat, "failed-" + Guid.NewGuid().ToString("N"),
                "replay after acceptance failure");
            var failedHost = Bridge(hostedHarness);
            await failedHost.StartAsync(Ct);
            hostedHarness.Messaging.InjectInbound(failedMessage);
            await gate.Failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            hostedHarness.Messaging.AcknowledgedCount.ShouldBe(1);
            await using (var failedRead = Db(ackSchema.ConnectionString))
                (await failedRead.ChannelInbounds.AnyAsync(i => i.NativeMessageId == failedMessage.ChannelMessageId)).ShouldBeFalse();
            await failedHost.StopAsync(Ct);
            var replayHost = Bridge(hostedHarness);
            await replayHost.StartAsync(Ct);
            await WaitForAsync(() => Task.FromResult(hostedHarness.Messaging.AcknowledgedCount == 2));
            await replayHost.StopAsync(Ct);
            InstallTranscriptReceipt(hostedHarness);
            await using (var running = Db(ackSchema.ConnectionString))
                await running.AgentSessions.Where(s => s.Id == hostedHarness.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
            await Bridge(hostedHarness).DrainPendingAsync(Ct);
            await hostedHarness.Queue.OnTurnEndAsync(hostedHarness.SessionId, Ct);
            await using (var replayed = Db(ackSchema.ConnectionString))
            {
                foreach (var nativeId in new[] { acceptedMessage.ChannelMessageId, failedMessage.ChannelMessageId })
                {
                    var inbound = await replayed.ChannelInbounds.AsNoTracking()
                        .SingleAsync(i => i.NativeMessageId == nativeId);
                    var owner = await replayed.SessionQueuedMessages.AsNoTracking()
                        .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
                    (await replayed.TranscriptEntries.CountAsync(t => t.AgentSessionId == hostedHarness.SessionId
                        && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
                }
            }
        }

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var mappingFault = new QueueMappingFault();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(mappingFault),
        });
        var chat = await h.BindChannelAsync();
        var faulted = Message(chat, "fault-" + Guid.NewGuid().ToString("N"), "must survive mapping fault") with
        {
            Attachments = [new Attachment
            {
                Kind = AttachmentKind.File, ChannelRef = "stable-file", Name = "retry-proof.bin",
                Content = [31, 32, 33, 34],
            }],
        };
        mappingFault.Arm();
        await Bridge(h).HandleInboundAsync(faulted, Ct);
        await using (var cut = Db(schema.ConnectionString))
        {
            var pending = await cut.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == faulted.ChannelMessageId);
            pending.QueueMessageId.ShouldBeNull();
            (await cut.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId == pending.Id)).ShouldBe(0,
                "queue owner and every journal member must roll back together");
        }
        var inbox = Path.Combine(h.TempRoot, "workspace", ".antiphon", "inbox");
        var firstSavedPath = Directory.GetFiles(inbox).Single();
        File.ReadAllBytes(firstSavedPath).ShouldBe(faulted.Attachments.Single().Content);
        await Bridge(h).DrainPendingAsync(Ct);
        Directory.GetFiles(inbox).ShouldBe([firstSavedPath]);
        await using (var recoveredOwner = Db(schema.ConnectionString))
        {
            var owner = await recoveredOwner.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.SourceChannelInboundId != null
                    && q.Body.Contains("must survive mapping fault"));
            owner.Body.ShouldContain(firstSavedPath);
            (await recoveredOwner.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        }
        await using (var batchSchema = await TestDbFixture.CreateIsolatedSchemaAsync())
        {
            var batchFault = new QueueMappingFault();
            await using var batchHarness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = batchSchema.ConnectionString, Bridge = Settings(debounce: 100000),
                AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureDbContext = options => options.AddInterceptors(batchFault),
            });
            var batchChat = await batchHarness.BindChannelAsync();
            var firstBatch = Message(batchChat, $"batch-a-{Guid.NewGuid():N}", "batch first DISTINCT TAIL");
            var secondBatch = Message(batchChat, $"batch-b-{Guid.NewGuid():N}", "batch second DISTINCT TAIL");
            var batchBridge = Bridge(batchHarness);
            await batchBridge.HandleInboundAsync(firstBatch, Ct);
            await batchBridge.HandleInboundAsync(secondBatch, Ct);
            batchFault.Arm();
            await batchHarness.Provider.GetRequiredService<ChannelInboundDebouncer>().FlushAllAsync();
            await using (var cut = Db(batchSchema.ConnectionString))
            {
                var retained = await cut.ChannelInbounds.AsNoTracking()
                    .Where(i => i.NativeMessageId == firstBatch.ChannelMessageId
                        || i.NativeMessageId == secondBatch.ChannelMessageId).ToListAsync();
                retained.Count.ShouldBe(2);
                retained.ShouldAllBe(i => i.EnvelopeJson != null && i.QueueMessageId == null);
                (await cut.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId != null
                    && q.AgentSessionId == batchHarness.SessionId)).ShouldBe(0);
            }
            InstallTranscriptReceipt(batchHarness);
            await Bridge(batchHarness).DrainPendingAsync(Ct);
            await batchHarness.Provider.GetRequiredService<ChannelInboundDebouncer>().FlushAllAsync();
            await using var recoveredBatch = Db(batchSchema.ConnectionString);
            var members = await recoveredBatch.ChannelInbounds.AsNoTracking()
                .Where(i => i.NativeMessageId == firstBatch.ChannelMessageId
                    || i.NativeMessageId == secondBatch.ChannelMessageId)
                .OrderBy(i => i.AcceptanceSequence).ToListAsync();
            members.Select(i => i.QueueMessageId).Distinct().Count().ShouldBe(1);
            var owner = await recoveredBatch.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.SourceChannelInboundId == members[0].Id);
            members.ShouldAllBe(i => i.QueueMessageId == owner.Id);
            owner.Body.IndexOf("batch first DISTINCT TAIL", StringComparison.Ordinal)
                .ShouldBeLessThan(owner.Body.IndexOf("batch second DISTINCT TAIL", StringComparison.Ordinal));
            (await recoveredBatch.TranscriptEntries.CountAsync(t => t.AgentSessionId == batchHarness.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        }
        var first = Message(chat, "first-" + Guid.NewGuid().ToString("N"), "first complete body");
        var second = Message(chat, "second-" + Guid.NewGuid().ToString("N"), "second complete body");
        h.Messaging.InjectInbound(first);
        await using (var delivery = h.Messaging.ConsumeDeliveriesAsync().GetAsyncEnumerator())
        {
            (await delivery.MoveNextAsync()).ShouldBeTrue();
            h.Messaging.AcknowledgedCount.ShouldBe(0);
            await Bridge(h).HandleInboundAsync(delivery.Current.Message!, Ct);
            await using (var verify = Db(schema.ConnectionString))
                (await verify.ChannelInbounds.AnyAsync(i => i.NativeMessageId == first.ChannelMessageId &&
                    i.EnvelopeJson != null && i.EnvelopeJson.Contains("first complete body"))).ShouldBeTrue();
            await delivery.Current.AcknowledgeAsync("accepted");
        }
        h.Messaging.AcknowledgedCount.ShouldBe(1);
        await Bridge(h).HandleInboundAsync(second, Ct);
        await Bridge(h).HandleInboundAsync(first with { Id = Guid.NewGuid().ToString("N") }, Ct);
        await using var db = Db(schema.ConnectionString);
        (await db.ChatChannels.Where(c => c.ExternalId == chat).Select(c => c.MessageCount).SingleAsync()).ShouldBe(3);
        var inbounds = await db.ChannelInbounds.Where(i => i.ConversationId == chat).ToListAsync();
        inbounds.Count.ShouldBe(3);
        var owners = await db.SessionQueuedMessages.Where(q => q.AgentSessionId == h.SessionId &&
            q.SourceChannelInboundId != null).ToListAsync();
        owners.Count.ShouldBe(3);
        owners.Select(o => o.SourceChannelInboundId!.Value).Distinct().Count().ShouldBe(3);
        foreach (var owner in owners)
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);

        // A catalog row from before the inbound journal can already name this message.
        // That one-slot hint is not an acceptance or delivery receipt.
        var legacyChat = await h.BindChannelAsync($"legacy-{Guid.NewGuid():N}");
        var legacyMessage = Message(legacyChat, $"legacy-native-{Guid.NewGuid():N}",
            "complete legacy replay body");
        await db.ChatChannels.Where(c => c.ExternalId == legacyChat)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastChannelMessageId,
                legacyMessage.ChannelMessageId));
        await Bridge(h).HandleInboundAsync(legacyMessage, Ct);
        var legacyInbound = await db.ChannelInbounds.AsNoTracking()
            .SingleAsync(i => i.NativeMessageId == legacyMessage.ChannelMessageId);
        legacyInbound.EnvelopeJson.ShouldContain(legacyMessage.Text!);
        legacyInbound.QueueMessageId.ShouldNotBeNull();

        // The database, not a process-local lookup, owns both uniqueness rules.
        await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await using var duplicate = Db(schema.ConnectionString);
            duplicate.ChannelInbounds.Add(new ChannelInbound
            {
                Id = Guid.NewGuid(), Provider = first.Channel,
                ConversationId = chat, NativeMessageId = first.ChannelMessageId,
                EnvelopeJson = "{}", AgentId = h.AgentId,
                ChatChannelId = inbounds[0].ChatChannelId, AcceptedAt = DateTime.UtcNow,
            });
            await duplicate.SaveChangesAsync();
        });
        await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await using var duplicate = Db(schema.ConnectionString);
            duplicate.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId,
                SourceChannelInboundId = owners[0].SourceChannelInboundId,
                Body = "second physical owner is forbidden", Sequence = long.MaxValue,
                Origin = QueuedMessageOrigin.Channel, ConversationKey = $"telegram:{chat}",
                CreatedAt = DateTime.UtcNow,
            });
            await duplicate.SaveChangesAsync();
        });
        var otherChat = await h.BindChannelAsync($"other-{Guid.NewGuid():N}");
        await Bridge(h).HandleInboundAsync(first with
        {
            Id = Guid.NewGuid().ToString("N"),
            Conversation = first.Conversation with { Id = otherChat },
            ReplyHandle = otherChat,
        }, Ct);
        await Bridge(h).HandleInboundAsync(first with
        {
            Id = Guid.NewGuid().ToString("N"), Channel = "other-provider",
        }, Ct);
        (await db.ChannelInbounds.CountAsync(i => i.NativeMessageId == first.ChannelMessageId)).ShouldBe(3);

        // These are deliberate routing dispositions, so the hosted consumer may ack
        // each one without creating a deliverable journal owner or typing input.
        await using var dispositionSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var dispositions = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = dispositionSchema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var disabledChat = await dispositions.BindChannelAsync($"disabled-{Guid.NewGuid():N}");
        var emptyChat = await dispositions.BindChannelAsync($"empty-{Guid.NewGuid():N}");
        await using (var setup = Db(dispositionSchema.ConnectionString))
            await setup.ChatChannels.Where(c => c.ExternalId == disabledChat)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Enabled, false));
        var self = Message(emptyChat, $"self-{Guid.NewGuid():N}", "self echo") with
        {
            Author = new Participant { Id = "agent", IsSelf = true },
        };
        var unbound = Message($"unbound-{Guid.NewGuid():N}", $"unbound-{Guid.NewGuid():N}", "discovery only");
        var disabled = Message(disabledChat, $"disabled-{Guid.NewGuid():N}", "disabled input");
        var empty = Message(emptyChat, $"empty-{Guid.NewGuid():N}", "") with { Attachments = [] };
        var dispositionHost = Bridge(dispositions);
        await dispositionHost.StartAsync(Ct);
        foreach (var input in new[] { self, unbound, disabled, empty })
            dispositions.Messaging.InjectInbound(input);
        await WaitForAsync(() => Task.FromResult(dispositions.Messaging.AcknowledgedCount == 4));
        await dispositionHost.StopAsync(Ct);
        await using var dispositionRead = Db(dispositionSchema.ConnectionString);
        (await dispositionRead.ChannelInbounds.CountAsync(i => i.NativeMessageId == self.ChannelMessageId)).ShouldBe(0);
        var ignoredIds = new[] { unbound.ChannelMessageId, disabled.ChannelMessageId, empty.ChannelMessageId };
        var ignored = await dispositionRead.ChannelInbounds.AsNoTracking()
            .Where(i => ignoredIds.Contains(i.NativeMessageId)).ToListAsync();
        ignored.Count.ShouldBe(3);
        ignored.ShouldAllBe(i => i.EnvelopeJson == null && i.QueueMessageId == null);
        (await dispositionRead.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == dispositions.SessionId)).ShouldBe(0);
        dispositions.Adapter.SentInput.ShouldBeEmpty();
    }

    [Test]
    public async Task C593_DebounceCrash_RetainsEveryMemberInOrder()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid agent; Guid session; string chat; string firstId; string secondId;
        await using (var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(debounce: 10000),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        }))
        {
            agent = h.AgentId; session = h.SessionId; chat = await h.BindChannelAsync();
            firstId = "batch-1-" + Guid.NewGuid().ToString("N");
            secondId = "batch-2-" + Guid.NewGuid().ToString("N");
            await Bridge(h).HandleInboundAsync(Message(chat, firstId, "line one tail"), Ct);
            await Bridge(h).HandleInboundAsync(Message(chat, secondId, "line two tail"), Ct);
            await using var verify = Db(schema.ConnectionString);
            (await verify.ChannelInbounds.CountAsync(i => i.ConversationId == chat && i.QueueMessageId == null)).ShouldBe(2);
            // Force the tie that a frozen clock or broker replay can produce. The old
            // (AcceptedAt, random Id) order now places the second native message first.
            var tiedAt = DateTime.UtcNow;
            await verify.ChannelInbounds.Where(i => i.NativeMessageId == firstId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.AcceptedAt, tiedAt)
                    .SetProperty(i => i.Id, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
            await verify.ChannelInbounds.Where(i => i.NativeMessageId == secondId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.AcceptedAt, tiedAt)
                    .SetProperty(i => i.Id, Guid.Parse("00000000-0000-0000-0000-000000000001")));
        }
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
            Bridge = Settings(debounce: 150), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(recovered);
        await Bridge(recovered).DrainPendingAsync(Ct);
        await WaitForAsync(async () =>
        {
            await using var db = Db(schema.ConnectionString);
            return await db.ChannelInbounds.CountAsync(i => i.ConversationId == chat && i.QueueMessageId != null) == 2
                && recovered.Adapter.SubmittedBodies.Count == 1;
        });
        await using var db = Db(schema.ConnectionString);
        var members = await db.ChannelInbounds.Where(i => i.ConversationId == chat).OrderBy(i => i.AcceptedAt).ToListAsync();
        members.Select(i => i.NativeMessageId).Order().ShouldBe(new[] { firstId, secondId }.Order());
        members.Select(i => i.QueueMessageId).Distinct().Count().ShouldBe(1);
        var owner = await db.SessionQueuedMessages.SingleAsync(q => q.Id == members[0].QueueMessageId);
        owner.Body.IndexOf("line one tail", StringComparison.Ordinal).ShouldBeLessThan(owner.Body.IndexOf("line two tail", StringComparison.Ordinal));
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(1);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == session
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);

        // Delimiter-bearing native IDs must still form distinct (conversation, sender) lanes.
        // These two tuples collide under a colon-concatenated string key.
        var leftChat = await recovered.BindChannelAsync($"lane-{Guid.NewGuid():N}:a:b");
        var rightChat = leftChat[..^2];
        await recovered.BindChannelAsync(rightChat);
        var leftNative = $"left-{Guid.NewGuid():N}";
        var rightNative = $"right-{Guid.NewGuid():N}";
        var laneBridge = Bridge(recovered);
        await laneBridge.HandleInboundAsync(Message(leftChat, leftNative, "left lane tail", "c"), Ct);
        await laneBridge.HandleInboundAsync(Message(rightChat, rightNative, "right lane tail", "b:c"), Ct);
        await WaitForAsync(async () =>
        {
            await using var pending = Db(schema.ConnectionString);
            return await pending.ChannelInbounds.CountAsync(i =>
                (i.NativeMessageId == leftNative || i.NativeMessageId == rightNative)
                && i.QueueMessageId != null) == 2;
        });
        var laneMembers = await db.ChannelInbounds.AsNoTracking()
            .Where(i => i.NativeMessageId == leftNative || i.NativeMessageId == rightNative)
            .Select(i => i.QueueMessageId).ToListAsync();
        laneMembers.Distinct().Count().ShouldBe(2);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(3);
        recovered.Adapter.SubmittedBodies.Count(b => b.Contains("left lane tail") && b.Contains("right lane tail")).ShouldBe(0);
        foreach (var laneOwnerId in laneMembers.Distinct())
        {
            var laneBody = await db.SessionQueuedMessages.Where(q => q.Id == laneOwnerId)
                .Select(q => q.Body).SingleAsync();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == session
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == laneBody)).ShouldBe(1);
        }
        await laneBridge.HandleInboundAsync(Message(chat, secondId, "line two tail"), Ct);
        (await db.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId != null
            && (q.AgentSessionId == session))).ShouldBe(3);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(3);

        // A held first page must not starve the next accepted message forever.
        await using var pageSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var held = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = pageSchema.ConnectionString,
            Bridge = Settings(timeout: 1), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var heldChat = await held.BindChannelAsync();
        var nativeIds = Enumerable.Range(0, 65).Select(i => $"page-{i:D2}-{Guid.NewGuid():N}").ToArray();
        await using (var seed = Db(pageSchema.ConnectionString))
        {
            await seed.AgentSessions.Where(s => s.Id == held.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            var channelId = await seed.ChatChannels.Where(c => c.ExternalId == heldChat).Select(c => c.Id).SingleAsync();
            foreach (var nativeId in nativeIds)
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = heldChat,
                    NativeMessageId = nativeId, AgentId = held.AgentId, ChatChannelId = channelId,
                    EnvelopeJson = JsonSerializer.Serialize(Message(heldChat, nativeId, nativeId), Antiphon.Messaging.MessagingJson.Options),
                    AcceptedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }
        var pageClock = new BridgeWakeClock();
        var pageBridge = Bridge(held, pageClock);
        string lastNativeId;
        await using (var pageOrder = Db(pageSchema.ConnectionString))
            lastNativeId = await pageOrder.ChannelInbounds.OrderByDescending(i => i.AcceptanceSequence)
                .Select(i => i.NativeMessageId).FirstAsync();
        var firstPage = pageBridge.DrainPendingAsync(Ct);
        await pageClock.PollRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pageClock.Advance(TimeSpan.FromSeconds(2));
        await firstPage.WaitAsync(TimeSpan.FromSeconds(20));
        var secondPage = pageBridge.DrainPendingAsync(Ct);
        await pageClock.SecondPollRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pageClock.Advance(TimeSpan.FromSeconds(2));
        await secondPage.WaitAsync(TimeSpan.FromSeconds(20));
        await using var pageVerify = Db(pageSchema.ConnectionString);
        (await pageVerify.ChannelInbounds.Where(i => i.NativeMessageId == lastNativeId)
            .Select(i => i.WakeTimeoutIncidentAt).SingleAsync()).ShouldNotBeNull();
    }

    [Test]
    public async Task C593_HoldsAndConflicts_ParkWithoutRerouting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
            var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
            if (state is null) { state = new AgentSupervisionState { AgentId = h.AgentId }; db.AgentSupervisionStates.Add(state); }
            state.HerdrFailureHeldAt = DateTime.UtcNow.AddMinutes(-1);
            state.HerdrConsecutiveFailures = 3;
            await db.SaveChangesAsync();
        }
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "held first full body"), Ct);
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "held second full body"), Ct);
        await using var verify = Db(schema.ConnectionString);
        (await verify.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId && i.EnvelopeJson != null)).ShouldBe(2);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId &&
            i.Kind == AgentIncidentKind.ChannelReplyLost && i.FailureReason == "HerdrSupervisionHeld" &&
            i.Severity == AlertSeverity.Critical)).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();
        h.Messaging.SentReplies.ShouldBeEmpty();

        await CapacityHoldUsesCapturedReplyHandleAsync();
        await SupervisionHoldsPreserveEveryEnvelopeAsync();
    }

    private static async Task SupervisionHoldsPreserveEveryEnvelopeAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = true, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using var db = Db(schema.ConnectionString);
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        var initialSessions = await db.AgentSessions.CountAsync(s => s.Cwd.StartsWith(h.TempRoot));
        var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = h.AgentId };
            db.AgentSupervisionStates.Add(state);
        }
        var names = new[] { "continuity", "suspended", "liveness" };
        foreach (var name in names)
        {
            state.ContinuityHeldAt = name == "continuity" ? DateTime.UtcNow : null;
            state.Suspended = name == "suspended";
            state.LivenessLatchedAt = name == "liveness" ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
            var native = $"{name}-{Guid.NewGuid():N}";
            await Bridge(h).HandleInboundAsync(Message(chat, native, $"{name} full retained body"), Ct);
            var retained = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
            retained.EnvelopeJson.ShouldContain($"{name} full retained body");
            retained.QueueMessageId.ShouldBeNull();
            (await db.AgentSessions.CountAsync(s => s.Cwd.StartsWith(h.TempRoot))).ShouldBe(initialSessions);
        }
        h.Adapter.SentInput.ShouldBeEmpty();
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    private static async Task CapacityHoldUsesCapturedReplyHandleAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(timeout: 1), AlwaysOn = true, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var originalHandle = $"captured-{Guid.NewGuid():N}";
        var message = Message(chat, $"capacity-{Guid.NewGuid():N}", "capacity hold complete body") with
        {
            ReplyHandle = originalHandle,
        };
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        await Bridge(h).HandleInboundAsync(message, Ct);
        var firstEpisodeAt = h.Now.AddMinutes(-2);
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            await db.ChatChannels.Where(c => c.ExternalId == chat)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ReplyHandle, "newer-catalog-handle"));
            foreach (var kind in new[] { AgentKind.Raw, AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex })
                db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
                {
                    Id = Guid.NewGuid(), Kind = kind, ModelAlias = ModelAlias.KindWide,
                    Source = ModelAvailabilitySource.Manual, HitAt = firstEpisodeAt,
                    Reason = "provider capacity test hold",
                });
            await db.SaveChangesAsync();
        }
        var bridge = Bridge(h);
        await bridge.DrainPendingAsync(Ct);
        var notice = h.Messaging.SentReplies.ShouldHaveSingleItem();
        notice.ConversationId.ShouldBe(chat);
        notice.ReplyHandle.ShouldBe(originalHandle);
        notice.Text.ShouldContain("Your message is kept");
        await bridge.DrainPendingAsync(Ct);
        h.Messaging.SentReplies.Count.ShouldBe(1);
        await using var verify = Db(schema.ConnectionString);
        var retained = await verify.ChannelInbounds.AsNoTracking()
            .SingleAsync(i => i.NativeMessageId == message.ChannelMessageId);
        retained.EnvelopeJson.ShouldContain(message.Text!);
        retained.QueueMessageId.ShouldBeNull();
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost && i.Severity == AlertSeverity.Critical
            && i.FailureReason == "ProviderCapacity" && i.Message.Contains("pending"))).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();
        var firstIncidentAt = await verify.AgentIncidents.Where(i => i.AgentId == h.AgentId
            && i.FailureReason == "ProviderCapacity").Select(i => i.CreatedAt).SingleAsync();
        await verify.ModelAvailabilityHolds.ExecuteDeleteAsync();
        ((ScaledTimeProvider)h.Clock).Advance(TimeSpan.FromMinutes(1));
        var secondEpisodeAt = h.Now;
        secondEpisodeAt.ShouldBeGreaterThan(firstIncidentAt);
        foreach (var kind in new[] { AgentKind.Raw, AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex })
            verify.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(), Kind = kind, ModelAlias = ModelAlias.KindWide,
                Source = ModelAvailabilitySource.Manual, HitAt = secondEpisodeAt,
                Reason = "second capacity episode",
            });
        await verify.SaveChangesAsync();
        await bridge.DrainPendingAsync(Ct);
        h.Messaging.SentReplies.Count.ShouldBe(2);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ProviderCapacity" && i.Severity == AlertSeverity.Critical)).ShouldBe(2);
        await bridge.DrainPendingAsync(Ct);
        h.Messaging.SentReplies.Count.ShouldBe(2);
    }

    [Test]
    public async Task C593_ChannelWake_IsAutomaticWithQuotaOverride()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
            if (state is null) { state = new AgentSupervisionState { AgentId = h.AgentId }; db.AgentSupervisionStates.Add(state); }
            state.Suspended = true;
            state.LivenessLatchedAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "preserve operator hold"), Ct);
        await using var verify = Db(schema.ConnectionString);
        var verifiedState = await verify.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == h.AgentId);
        verifiedState.Suspended.ShouldBeTrue();
        verifiedState.LivenessLatchedAt.ShouldNotBeNull();
        (await verify.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId && i.QueueMessageId == null)).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();
    }

    [Test]
    public async Task C593_ChannelWake_StartsNonAlwaysOnAutomaticallyThroughExhaustedQuota()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var launchAdapter = new FakeAgentProtocolAdapter();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(timeout: 5),
            ClockSpeed = 5, AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureServices = services =>
            {
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "fake",
                        Definitions =
                        {
                            ["fake"] = new AgentDefinition
                            {
                                Kind = "Raw",
                                Exe = OperatingSystem.IsWindows()
                                    ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
                            },
                        },
                    }));
                services.AddScoped<SubscriptionUsageReader>();
                services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
                services.AddScoped<SubscriptionQuotaGate>();
                services.AddSingleton<IAgentProtocolAdapterFactory>(new FixedAdapterFactory(launchAdapter));
            },
        });
        launchAdapter.RegisterOnStart = h.Runtime;
        launchAdapter.OnSubmitted = async submitted =>
        {
            var sessionId = launchAdapter.StartedSessionId!.Value;
            await using var db = Db(schema.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            var record = new SessionRunnerTranscriptEvent(sessionId, sequence, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                null, null, null, null, null);
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(sessionId, [record], sequence));
            await h.Runtime.SyncTranscriptAsync(sessionId, Ct);
        };
        var chat = await h.BindChannelAsync();
        DateTime nextRestartAt;
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            nextRestartAt = h.Now.AddMinutes(10);
            nextRestartAt = nextRestartAt.AddTicks(-(nextRestartAt.Ticks % 10));
            db.AgentSupervisionStates.Add(new AgentSupervisionState
            {
                AgentId = h.AgentId, NextRestartAt = nextRestartAt,
            });
            db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
            {
                Id = Guid.NewGuid(), Provider = AgentKind.Raw,
                SubscriptionKey = SubscriptionUsageKey.For(agent, AgentKind.Raw),
                PlanLabel = "Exhausted", RemainingPercent = 0,
                ResetsAt = h.Now.AddDays(2), ObservedAt = h.Now,
                AgentSessionId = h.SessionId, SourceCommand = "/status",
                ParseStatus = SubscriptionUsageParseStatus.Parsed, RawExcerpt = "seeded",
            });
            await db.SaveChangesAsync();
        }
        var native = $"quota-wake-{Guid.NewGuid():N}";
        await Bridge(h).HandleInboundAsync(Message(chat, native, "quota wake complete body DISTINCT TAIL"), Ct);
        await using (var db = Db(schema.ConnectionString))
        {
            (await db.ChannelInbounds.Where(i => i.NativeMessageId == native)
                .Select(i => i.QueueMessageId).SingleAsync()).ShouldBeNull();
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
        }
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var refusal = await Should.ThrowAsync<SubscriptionQuotaLowException>(() =>
                control.StartAsync(h.AgentId, new StartAgentRequest(), Ct, automatic: true));
            refusal.Code.ShouldBe("subscription_quota_low");
        }
        await using (var db = Db(schema.ConnectionString))
        {
            (await db.AgentSessions.CountAsync(s => s.Cwd.StartsWith(h.TempRoot))).ShouldBe(1);
            (await db.AgentSupervisionStates.Where(s => s.AgentId == h.AgentId)
                .Select(s => s.NextRestartAt).SingleAsync()).ShouldBe(nextRestartAt);
            (await db.ChannelInbounds.Where(i => i.NativeMessageId == native)
                .Select(i => i.QueueMessageId).SingleAsync()).ShouldBeNull();
        }
        await Bridge(h).DrainPendingAsync(Ct);
        var launchDeadline = DateTime.UtcNow.AddSeconds(8);
        while (!launchAdapter.Started && DateTime.UtcNow < launchDeadline)
            await Task.Delay(25);
        if (!launchAdapter.Started)
        {
            await using var diagnostic = Db(schema.ConnectionString);
            var sessions = await diagnostic.AgentSessions.AsNoTracking()
                .Where(s => s.Cwd.StartsWith(h.TempRoot))
                .Select(s => new { s.Id, s.Status }).ToListAsync();
            var incidents = await diagnostic.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == h.AgentId).Select(i => i.Message).ToListAsync();
            throw new InvalidOperationException($"No channel launch: sessions={string.Join(';', sessions.Select(s => $"{s.Id}:{s.Status}"))}; incidents={string.Join(';', incidents)}");
        }
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        owner.Body.ShouldContain("quota wake complete body DISTINCT TAIL");
        var recipientSession = launchAdapter.StartedSessionId!.Value;
        owner.AgentSessionId.ShouldBe(recipientSession);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == recipientSession
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
        (await verify.AgentSupervisionStates.Where(s => s.AgentId == h.AgentId)
            .Select(s => s.NextRestartAt).SingleAsync()).ShouldBe(nextRestartAt,
                "an automatic channel wake must not clear the restart latch");
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.SubscriptionQuotaOverridden)).ShouldBe(1);
    }

    private sealed class FixedAdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    [Test]
    public async Task C593_QueuedLaunch_WaitsOnceAndRecognizesTerminalFailure()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(timeout: 5), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var inbound = Message(chat, Guid.NewGuid().ToString("N"), "wait for Running");
        var bridgeClock = new BridgeWakeClock();
        var handling = Bridge(h, bridgeClock).HandleInboundAsync(inbound, Ct);
        await bridgeClock.PollRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Adapter.SentInput.ShouldBeEmpty();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        bridgeClock.Advance(TimeSpan.FromSeconds(2));
        await handling;
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await using var verify = Db(schema.ConnectionString);
        var accepted = await verify.ChannelInbounds.SingleAsync(i => i.NativeMessageId == inbound.ChannelMessageId);
        accepted.QueueMessageId.ShouldNotBeNull();
        var owner = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == accepted.QueueMessageId);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)).ShouldBe(1);
    }

    [Test]
    public async Task C593_TransferAndUncertainWrite_RequireCompleteRecipientReceipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var body = "complete marked body " + new string('z', 240) + " DISTINCT TAIL";
        var id = await h.SeedPendingMessageAsync(body, origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chat}", deliveryAttempts: 3);
        await h.Queue.FlushSessionAsync(h.SessionId, Ct);
        await using var verify = Db(schema.ConnectionString);
        var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == id);
        row.Body.ShouldBe(body);
        row.DeliveryAttempts.ShouldBe(3);
        h.Adapter.SentInput.ShouldBeEmpty();
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId &&
            t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains("DISTINCT TAIL"))).ShouldBe(0);

        // A real Channel queue row owns the marker. A capped attempt must await the
        // recipient's complete submitted prompt; a queued or assistant transcript event
        // describes a different state even when it contains identical bytes.
        Guid ownedId = default;
        var markedInput = $"[antiphon-channel:{Guid.NewGuid():N}]\n{body}";
        await h.Queue.EnqueueAsync(h.SessionId, markedInput, MessageSendMode.WhenIdle, Ct,
            origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chat}",
            deliverIfIdle: false, onCreated: value => ownedId = value);
        ownedId.ShouldNotBe(Guid.Empty);
        await verify.SessionQueuedMessages.Where(q => q.Id == ownedId).ExecuteUpdateAsync(u => u
            .SetProperty(q => q.DeliveryAttempts, 3)
            .SetProperty(q => q.LastDeliveryBaselineSequence, 0L)
            .SetProperty(q => q.LastDeliveryStartedAt, DateTime.UtcNow.AddMinutes(-1)));
        var marked = (await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.Id == ownedId)).Body;
        marked.ShouldContain("[antiphon-channel:");
        var events = new List<SessionRunnerTranscriptEvent>();
        async Task IngestAsync(string kind, string? text, string? stopReason = null)
        {
            var sequence = events.Count + 1L;
            events.Add(new SessionRunnerTranscriptEvent(h.SessionId, sequence, kind,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow,
                kind == TranscriptKinds.TurnEnd ? "assistant" : "user", text,
                null, null, null, null, stopReason));
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(h.SessionId, events.ToArray(), sequence));
            await h.Runtime.SyncTranscriptAsync(h.SessionId, Ct);
            await using (var evidence = Db(schema.ConnectionString))
                (await evidence.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Sequence == sequence && t.Kind == kind && t.Text == text)).ShouldBeTrue(
                    "runner transcript bytes must be ingested before judging queue delivery");
            await h.Queue.FlushSessionAsync(h.SessionId, Ct);
        }
        await IngestAsync(TranscriptKinds.QueuedUserPrompt, marked);
        await IngestAsync(TranscriptKinds.TurnEnd, null, "end_turn");
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownedId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
        await IngestAsync(TranscriptKinds.AssistantText, marked);
        await IngestAsync(TranscriptKinds.TurnEnd, null, "end_turn");
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownedId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
        await IngestAsync(TranscriptKinds.UserPrompt, marked[..^13] + "different tail");
        await IngestAsync(TranscriptKinds.TurnEnd, null, "end_turn");
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownedId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
        var markerEnd = marked.IndexOf(']');
        markerEnd.ShouldBeGreaterThan(0);
        var wrongMarker = $"[antiphon-channel:{Guid.NewGuid():N}]" + marked[(markerEnd + 1)..];
        await IngestAsync(TranscriptKinds.UserPrompt, wrongMarker);
        await IngestAsync(TranscriptKinds.TurnEnd, null, "end_turn");
        var stillPending = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownedId);
        stillPending.Status.ShouldBe(QueuedMessageStatus.Pending);
        stillPending.Body.ShouldBe(marked);
        stillPending.DeliveryAttempts.ShouldBe(3);
        h.Adapter.SentInput.ShouldBeEmpty();
        await IngestAsync(TranscriptKinds.UserPrompt, marked);
        await IngestAsync(TranscriptKinds.TurnEnd, null, "end_turn");
        var received = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownedId);
        received.Status.ShouldBe(QueuedMessageStatus.Sent);
        received.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == marked)).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();

        async Task<(Guid Id, string Body)> NewCappedOwnerAsync(string tail, long? floor, DateTime started)
        {
            Guid ownerId = Guid.Empty;
            await h.Queue.EnqueueAsync(h.SessionId, $"floor case {tail}", MessageSendMode.WhenIdle, Ct,
                origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chat}",
                deliverIfIdle: false, onCreated: value => ownerId = value);
            ownerId.ShouldNotBe(Guid.Empty);
            await using var db = Db(schema.ConnectionString);
            await db.SessionQueuedMessages.Where(q => q.Id == ownerId).ExecuteUpdateAsync(u => u
                .SetProperty(q => q.DeliveryAttempts, 3)
                .SetProperty(q => q.LastDeliveryBaselineSequence, floor)
                .SetProperty(q => q.LastDeliveryStartedAt, started));
            var ownerBody = await db.SessionQueuedMessages.Where(q => q.Id == ownerId)
                .Select(q => q.Body).SingleAsync();
            return (ownerId, ownerBody);
        }
        async Task<long> ObserveAsync(Guid sessionId, string kind, string? text,
            DateTimeOffset timestamp, string? stopReason = null)
        {
            await using var db = Db(schema.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            await h.Runtime.ObserveTranscriptAsync(new SessionRunnerTranscriptEvent(sessionId, sequence, kind,
                Guid.NewGuid().ToString("N"), null, timestamp,
                kind == TranscriptKinds.TurnEnd ? "assistant" : "user", text,
                null, null, null, null, stopReason), Ct);
            (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId
                && t.Sequence == sequence && t.Kind == kind && t.Text == text)).ShouldBeTrue();
            return sequence;
        }
        async Task AssertPendingAsync(Guid ownerId)
        {
            await h.Queue.FlushSessionAsync(h.SessionId, Ct);
            await using var db = Db(schema.ConnectionString);
            var owner = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownerId);
            owner.Status.ShouldBe(QueuedMessageStatus.Pending);
            owner.DeliveryAttempts.ShouldBe(3);
            h.Adapter.SentInput.ShouldBeEmpty();
        }
        async Task AssertLateConfirmedAsync(Guid ownerId, string fullBody, long floor)
        {
            await h.Queue.FlushSessionAsync(h.SessionId, Ct);
            await using var db = Db(schema.ConnectionString);
            var owner = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownerId);
            owner.Status.ShouldBe(QueuedMessageStatus.Sent);
            owner.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor
                && t.Text == fullBody)).ShouldBe(1);
            h.Adapter.SentInput.ShouldBeEmpty();
        }

        var equalityFloor = (await verify.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        var equality = await NewCappedOwnerAsync("EQUALITY DISTINCT TAIL", equalityFloor,
            DateTime.UtcNow.AddMinutes(-1));
        (await ObserveAsync(h.SessionId, TranscriptKinds.UserPrompt, equality.Body,
            DateTimeOffset.UtcNow)).ShouldBe(equalityFloor);
        await ObserveAsync(h.SessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertPendingAsync(equality.Id);
        await ObserveAsync(h.SessionId, TranscriptKinds.UserPrompt, equality.Body, DateTimeOffset.UtcNow);
        await ObserveAsync(h.SessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertLateConfirmedAsync(equality.Id, equality.Body, equalityFloor);

        var staleStart = DateTime.UtcNow;
        var stale = await NewCappedOwnerAsync("STALE NATIVE DISTINCT TAIL", null, staleStart);
        await ObserveAsync(h.SessionId, TranscriptKinds.UserPrompt, stale.Body,
            new DateTimeOffset(staleStart.AddMinutes(-2), TimeSpan.Zero));
        await ObserveAsync(h.SessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertPendingAsync(stale.Id);
        var staleFloor = await ObserveAsync(h.SessionId, TranscriptKinds.UserPrompt, stale.Body,
            DateTimeOffset.UtcNow);
        await ObserveAsync(h.SessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertLateConfirmedAsync(stale.Id, stale.Body, staleFloor - 1);

        var otherSessionId = Guid.NewGuid();
        await using (var db = Db(schema.ConnectionString))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = otherSessionId, AgentKind = AgentKind.ClaudeCode,
                DefinitionName = "fake", Status = SessionStatus.Running,
                Cwd = Path.Combine(h.TempRoot, "other-recipient"),
                CreatedAt = h.Now, StartedAt = h.Now, LastSeenAt = h.Now,
            });
            await db.SaveChangesAsync();
        }
        h.Runtime.Register(otherSessionId, new FakeAgentProtocolAdapter());
        var wrongRecipient = await NewCappedOwnerAsync("WRONG SESSION DISTINCT TAIL", 0,
            DateTime.UtcNow.AddMinutes(-1));
        await ObserveAsync(otherSessionId, TranscriptKinds.UserPrompt, wrongRecipient.Body,
            DateTimeOffset.UtcNow);
        await ObserveAsync(otherSessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertPendingAsync(wrongRecipient.Id);
        var recipientFloor = await ObserveAsync(h.SessionId, TranscriptKinds.UserPrompt,
            wrongRecipient.Body, DateTimeOffset.UtcNow);
        await ObserveAsync(h.SessionId, TranscriptKinds.TurnEnd, null, DateTimeOffset.UtcNow, "end_turn");
        await AssertLateConfirmedAsync(wrongRecipient.Id, wrongRecipient.Body, recipientFloor - 1);

    }

    private sealed class QueueMappingFault : SaveChangesInterceptor
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 && eventData.Context?.ChangeTracker.Entries<ChannelInbound>()
                .Any(e => e.State == EntityState.Modified && e.Entity.QueueMessageId != null) == true
                && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InvalidOperationException("synthetic journal mapping failure after queue preparation");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class AcceptanceGate : SaveChangesInterceptor
    {
        private int _mode;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PauseNext() => Interlocked.Exchange(ref _mode, 1);
        public void FailNext() => Interlocked.Exchange(ref _mode, 2);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ChannelInbound>()
                .Any(e => e.State == EntityState.Added) == true)
            {
                switch (Interlocked.Exchange(ref _mode, 0))
                {
                    case 1:
                        Entered.TrySetResult(true);
                        await Release.Task.WaitAsync(cancellationToken);
                        break;
                    case 2:
                        Failed.TrySetResult(true);
                        throw new InvalidOperationException("synthetic acceptance transaction failure");
                }
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class IncidentCommitFault : SaveChangesInterceptor
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AgentIncident>()
                .Any(e => e.State == EntityState.Added && e.Entity.FailureReason == "ChannelWakeTimeout") == true
                && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InvalidOperationException("synthetic incident commit failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
