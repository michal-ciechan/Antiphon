using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class CallerWakeTests
{
    [Test]
    public async Task Adopted_caller_wakes_for_an_unread_report_and_a_withheld_shortfall()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        rig.Caller.DialogAdopted = true;
        var (taskId, _) = await rig.SettleReviewAsync();
        await rig.FlushAsync();
        var row = await rig.RowAsync(taskId);
        row.LastWithholdReason.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
        row.DeliveryAttempts.ShouldBe(0);

        var agentId = Guid.NewGuid();
        var (token, hash) = AgentTaskService.NewToken();
        var now = rig.World.Clock.GetUtcNow().UtcDateTime;
        DateTime generation;
        await using (var db = rig.World.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == rig.World.CallerSessionId);
            generation = session.StartedAt;
            session.DelegationTokenHash = hash;
            session.StandingAgentId = agentId;
            var reminder = new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = session.Id, Sequence = row.Sequence + 1,
                Origin = QueuedMessageOrigin.System, NoteHeader = KeepSixReminderText.NoteHeader,
                CreatedAt = now.AddDays(-12), Body = "Old shortfall", WithholdCount = 1,
                FirstWithheldAt = now.AddDays(-12), LastWithheldAt = now.AddDays(-12),
                LastWithholdReason = DialogDeliveryAdmission.AdoptedSession,
            };
            db.AddRange(new Agent
            {
                Id = agentId, Name = "Wake caller", Slug = "wake-caller", BoardId = rig.World.Board.Id,
                PersistentSessionId = session.Id.ToString(), WorkingDirectory = session.Cwd,
                CreatedAt = now, UpdatedAt = now,
            }, new AgentBundleAttachment { AgentId = agentId, BundleKey = InstructionBundles.Orchestrator, CreatedAt = now },
                new KeepSixOrchestrator
                {
                    AgentId = agentId, Enabled = true, Target = 6, EnabledAt = now.AddDays(-12),
                    LastReminderEnqueuedAt = reminder.CreatedAt, OutstandingMessageId = reminder.Id, UpdatedAt = now,
                }, reminder, new Card
                {
                    Id = Guid.NewGuid(), Identifier = "WAKE-1", Title = "Available work",
                    BoardId = rig.World.Board.Id, BoardColumnId = rig.World.Card.BoardColumnId,
                    Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddScoped(_ => rig.World.CreateContext());
        builder.Services.AddSingleton<TimeProvider>(rig.World.Clock);
        builder.Services.AddScoped<DispatchConcurrencySettingsService>();
        builder.Services.AddScoped<KeepSixShortfall>();
        builder.Services.AddScoped<CallerWakeService>();
        builder.Services.AddScoped<KeepSixService>();
        builder.Services.AddMemoryCache();
        await using var app = builder.Build();
        app.MapKeepSixEndpoints();
        await app.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var path = $"/api/orchestrators/{agentId}/caller-wake";
        using (var anonymous = await http.PostAsync(path, null))
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        http.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, token);
        using (var other = await http.PostAsync($"/api/orchestrators/{Guid.NewGuid()}/caller-wake", null))
            other.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        async Task<JsonElement> PollAsync()
        {
            using var response = await http.PostAsync(path, null);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, "the independent caller wake route");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var sessionId = rig.World.CallerSessionId;
        string BoundPath(Guid conversation, DateTime started) =>
            $"/api/orchestrators/{agentId}/caller-wake?conversationId={conversation}&generation={Uri.EscapeDataString(started.ToString("O"))}";
        path = BoundPath(sessionId, generation);
        var early = await PollAsync();
        early.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        early.GetProperty("shortfall").GetInt32().ShouldBe(0);

        rig.World.Clock.Advance(TimeSpan.FromMinutes(11));
        async Task<bool> HasAttentionAsync()
        {
            await using var db = rig.World.CreateContext();
            var items = await AttentionService.BuildUnreadReportItemsAsync(db, rig.World.Clock.GetUtcNow().UtcDateTime, default);
            return items.Any(i => i.SessionId == rig.World.CallerSessionId);
        }
        (await HasAttentionAsync()).ShouldBeTrue();
        await BridgeQueueHarness.InsertEntryAsync(sessionId, "AssistantText", "Busy again",
            connectionString: rig.World.Schema.ConnectionString);
        var busy = await PollAsync();
        busy.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0, "a busy turn cannot consume the report");
        busy.GetProperty("shortfall").GetInt32().ShouldBe(0);
        await using (var db = rig.World.CreateContext())
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
        (await PollAsync()).GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0, "unknown turn state");
        await rig.EndCallerTurnAsync();
        foreach (var stale in new[] { BoundPath(Guid.NewGuid(), generation), BoundPath(sessionId, generation.AddSeconds(-1)) })
        {
            using var rejected = await http.PostAsync(stale, null);
            rejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        var checkout = C1514Support.RepoRoot();
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(checkout, ".claude", "settings.json")));
        var stop = settings.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0];
        stop.GetProperty("asyncRewake").GetBoolean().ShouldBeTrue();
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        var script = stop.GetProperty("args")[0].GetString()!.Replace("${CLAUDE_PROJECT_DIR}", checkout);
        start.ArgumentList.Add(script);
        start.Environment["ANTIPHON_SESSION_ID"] = sessionId.ToString();
        start.Environment["ANTIPHON_SESSION_GENERATION"] = generation.ToString("O");
        start.Environment["ANTIPHON_ORCHESTRATOR"] = "1";
        start.Environment.Remove("ANTIPHON_TASK_ID");
        start.Environment["ANTIPHON_HOOK_STATE_DIR"] = rig.World.CallerDirectory;
        start.Environment["ANTIPHON_AGENT_ID"] = agentId.ToString();
        start.Environment["ANTIPHON_TASK_TOKEN"] = token;
        start.Environment["ANTIPHON_API"] = http.BaseAddress!.ToString();
        using var hook = Process.Start(start)!;
        try
        {
            await hook.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { hook_event_name = "Stop", session_id = sessionId }));
            hook.StandardInput.Close();
            await hook.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            hook.ExitCode.ShouldBe(2, "Claude's native idle wake");
            var nudge = await hook.StandardError.ReadToEndAsync();
            nudge.ShouldContain(taskId.ToString());
            nudge.ShouldContain("shortfall");
            (await hook.StandardOutput.ReadToEndAsync()).ShouldBeEmpty();
        }
        finally
        {
            if (!hook.HasExited) hook.Kill(entireProcessTree: true);
            await hook.WaitForExitAsync();
        }

        var duplicate = await PollAsync();
        duplicate.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        duplicate.GetProperty("shortfall").GetInt32().ShouldBe(0);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        (await PollAsync()).GetProperty("reportTaskIds")[0].GetGuid().ShouldBe(taskId);
        await rig.World.Services.GetRequiredService<AgentTaskService>().GetAsync(taskId, default, rig.World.CallerSessionId);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        var afterRead = await PollAsync();
        afterRead.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        (await HasAttentionAsync()).ShouldBeFalse();
        afterRead.GetProperty("shortfall").GetInt32().ShouldBeGreaterThan(0);
        rig.Caller.SubmittedBodies.ShouldBeEmpty();
        (await rig.RowAsync(taskId)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await rig.NotificationAsync(taskId))!.ConfirmingPromptSequence.ShouldBeNull();
    }

    [Test]
    public async Task Ordinary_report_needs_a_settlement_receipt_after_queue_retention()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, postEvidenceSettleMs: 0);
        var caller = rig.World.CallerSessionId;
        var id = Guid.NewGuid();
        var now = rig.World.Clock.GetUtcNow().UtcDateTime;
        const string first = "The first report is waiting for a review.";
        await using var db = rig.World.CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Debug,
            Title = "Ordinary report", Goal = "Debug", WorkingDirectory = rig.World.CallerDirectory,
            Status = AgentTaskStatus.Blocked, Result = first, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = caller, CreatedAt = now.AddDays(-3), CompletedAt = now.AddDays(-2),
        });
        await db.SaveChangesAsync();
        Task<bool> Owed() => CallerWakeService.OwedReports(db, rig.World.Clock.GetUtcNow().UtcDateTime, caller)
            .AnyAsync(t => t.Id == id);
        (await Owed()).ShouldBeTrue("enqueue recovery has not run");

        // A pre-upgrade queue entry can be Sent/Delivered with only a screen advance.
        var message = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = caller, Sequence = 1, SourceTaskId = id,
            ConversationKey = $"task:{id:N}", Origin = QueuedMessageOrigin.Delegation,
            Body = first, ContentDigest = DelegationNoteDigest.Compute(first),
            CreatedAt = now.AddDays(-2).AddMinutes(1), Status = QueuedMessageStatus.Sent,
            SentAt = now.AddDays(-2).AddMinutes(2), DeliveryVerdict = DeliveryVerdict.Delivered,
            LastDeliveryBaselineSequence = 0, ChannelReplyDiscoveryClosedAt = now.AddDays(-2).AddMinutes(3),
        };
        await BridgeQueueHarness.InsertEntryAsync(caller, "QueuedUserPrompt", first, connectionString: rig.World.Schema.ConnectionString);
        db.Add(message);
        await db.SaveChangesAsync();
        (await Owed()).ShouldBeTrue("a screen verdict cannot close an ordinary completion");
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db,
            Options.Create(new RetentionSettings { QueuedMessageRetentionDays = 1 }),
            audit, rig.World.Clock, NullLogger<DataRetentionService>.Instance,
            new AuditService(db, audit));
        (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(1);
        (await Owed()).ShouldBeTrue("the enqueue stamp survived retention, the caller still owes a read");

        var service = rig.World.Services.GetRequiredService<AgentTaskService>();
        await service.GetAsync(id, default, Guid.NewGuid());
        (await Owed()).ShouldBeTrue();
        await service.GetAsync(id, default, caller);
        (await Owed()).ShouldBeFalse();

        rig.World.Clock.Advance(TimeSpan.FromMinutes(20));
        var settled = rig.World.Clock.GetUtcNow().UtcDateTime;
        const string second = "The second settlement completed the repair.";
        await db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(t => t.Status, AgentTaskStatus.Succeeded)
            .SetProperty(t => t.Result, second)
            .SetProperty(t => t.CompletedAt, settled));
        (await Owed()).ShouldBeTrue("the prior settlement's parent read cannot close the new report");
        await rig.Queue.EnqueueAsync(caller, second, MessageSendMode.WhenIdle, default,
            QueuedMessageOrigin.Delegation, $"task:{id:N}", id, DelegationNoteDigest.Compute(second));
        await rig.FlushAsync();
        (await rig.CallerPromptsAsync()).ShouldContain(p => p.Text != null && p.Text.Contains(second));
        (await Owed()).ShouldBeFalse("the complete submitted prompt closes this settlement");
        await db.SessionQueuedMessages.Where(m => m.SourceTaskId == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(m => m.ChannelReplyDiscoveryClosedAt, settled));
        rig.World.Clock.Advance(TimeSpan.FromDays(2));
        (await retention.PruneQueuedMessagesAsync(default)).ShouldBeGreaterThan(0);
        (await Owed()).ShouldBeFalse("the receipt outlives its queue row");
    }
}
