using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-1320 S3. The reminder is one WhenIdle system note. The server does not create a task.
/// </summary>
[Category("Integration")]
[NotInParallel("KeepSixReminder")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class KeepSixReminderTests
{
    private const string Header = "[keep-six]";
    private const string NotDispatched = "Antiphon has not dispatched";
    private const string HoldScript = "scripts/keep-six.ps1";

    private readonly AntiphonWebAppFactory _factory;

    public KeepSixReminderTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task toggle_off_enqueues_nothing()
    {
        var home = await BoardAsync("ks-off");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var seated = await AgentAsync("ks-off", home.BoardId);
        await CardAsync(home, "KS16-READY", CardImportance.Critical, CardUrgency.Now);
        await EnableAsync(seated.AgentId);
        await EditRowAsync(seated.AgentId, row =>
        {
            row.Enabled = false;
            row.EnabledAt = DateTime.UtcNow.AddHours(-2);
        });

        await RunAsync();

        (await RemindersAsync(seated.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task shortfall_inside_the_grace_period_enqueues_nothing()
    {
        var seated = await ReadyAsync("ks-grace", "KS17-READY", DateTime.UtcNow.AddMinutes(-2));

        await RunAsync();

        (await RemindersAsync(seated.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task shortfall_past_grace_enqueues_one_whenidle_message_and_creates_no_task()
    {
        var seated = await ReadyAsync("ks-due", "KS18-READY", DateTime.UtcNow.AddMinutes(-3).AddSeconds(-1));
        var before = await CountTasksAsync();

        await RunAsync();

        var reminders = await RemindersAsync(seated.SessionId);
        var message = reminders.ShouldHaveSingleItem();
        AssertReminder(message, "KS18-READY");
        (await CountTasksAsync()).ShouldBe(before);
    }

    [Test]
    public async Task a_second_tick_while_that_message_is_pending_enqueues_nothing()
    {
        var seated = await ReadyAsync("ks-once", "KS19-READY", DateTime.UtcNow.AddMinutes(-3).AddSeconds(-1));

        await RunAsync();
        var first = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();

        await RunAsync();

        var second = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        second.Id.ShouldBe(first.Id);
        second.Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task after_delivery_a_tick_inside_the_backoff_enqueues_nothing_until_a_dispatch_or_fifteen_minutes()
    {
        var quiet = await ReadyAsync("ks-backoff", "KS20-QUIET", DateTime.UtcNow.AddMinutes(-30));
        await RunAsync();
        var first = (await RemindersAsync(quiet.SessionId)).ShouldHaveSingleItem();
        await MarkSentAsync(first.Id);

        await RunAsync();
        (await RemindersAsync(quiet.SessionId)).Count.ShouldBe(1);

        await EditRowAsync(quiet.AgentId, row =>
            row.LastReminderEnqueuedAt = DateTime.UtcNow.AddMinutes(-15).AddSeconds(-1));
        await RunAsync();
        var afterQuarterHour = await RemindersAsync(quiet.SessionId);
        afterQuarterHour.Count.ShouldBe(2);
        AssertReminder(afterQuarterHour.Single(message => message.Status == QueuedMessageStatus.Pending), "KS20-QUIET");

        var recent = await ReadyAsync("ks-recent", "KS20-RECENT", DateTime.UtcNow.AddMinutes(-30));
        await RunAsync();
        var recentFirst = (await RemindersAsync(recent.SessionId)).ShouldHaveSingleItem();
        await MarkSentAsync(recentFirst.Id);
        await EditRowAsync(recent.AgentId, row =>
            row.LastReminderEnqueuedAt = DateTime.UtcNow.AddMinutes(-10));
        await TaskAsync(recent.ProjectId!.Value, AgentTaskRole.Code, AgentTaskStatus.Queued, DateTime.UtcNow.AddMinutes(-1));
        await RunAsync();
        (await RemindersAsync(recent.SessionId)).Count.ShouldBe(1);

        var dispatched = await ReadyAsync("ks-dispatch", "KS20-DISPATCH", DateTime.UtcNow.AddMinutes(-30));
        await RunAsync();
        var dispatchedFirst = (await RemindersAsync(dispatched.SessionId)).ShouldHaveSingleItem();
        await MarkSentAsync(dispatchedFirst.Id);
        await EditRowAsync(dispatched.AgentId, row =>
            row.LastReminderEnqueuedAt = DateTime.UtcNow.AddMinutes(-10));
        await TaskAsync(dispatched.ProjectId!.Value, AgentTaskRole.Code, AgentTaskStatus.Queued,
            DateTime.UtcNow.AddMinutes(-4));
        await RunAsync();
        var waived = await RemindersAsync(dispatched.SessionId);
        waived.Count.ShouldBe(2);
        AssertReminder(waived.Single(message => message.Status == QueuedMessageStatus.Pending), "KS20-DISPATCH");
    }

    [Test]
    public async Task an_unexpired_hold_enqueues_nothing_and_an_expired_hold_does_not()
    {
        var seated = await ReadyAsync("ks-hold", "KS21-READY", DateTime.UtcNow.AddMinutes(-30));
        await HoldAsync(seated.AgentId, DateTime.UtcNow.AddDays(1));

        await RunAsync();
        (await RemindersAsync(seated.SessionId)).ShouldBeEmpty();

        await EditRowAsync(seated.AgentId, row => row.HoldUntil = DateTime.UtcNow.AddMinutes(-1));
        await RunAsync();

        var message = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        AssertReminder(message, "KS21-READY");
    }

    [Test]
    public async Task disabling_cancels_the_pending_reminder()
    {
        var seated = await ReadyAsync("ks-disable", "KS22-READY", DateTime.UtcNow.AddMinutes(-3).AddSeconds(-1));
        await RunAsync();
        var pending = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);

        await DisableAsync(seated.AgentId, revision: 1);

        var canceled = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        canceled.Id.ShouldBe(pending.Id);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public async Task ack_records_the_time_and_does_not_enqueue_a_second_message()
    {
        var seated = await ReadyAsync("ks-ack", "KS23-READY", DateTime.UtcNow.AddMinutes(-3).AddSeconds(-1));
        await RunAsync();
        var pending = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        var before = DateTime.UtcNow.AddSeconds(-2);

        var body = await AckAsync(seated.AgentId);

        var acknowledged = body.GetProperty("lastAcknowledgedAt").GetDateTimeOffset().UtcDateTime;
        acknowledged.ShouldBeGreaterThan(before);
        var stillPending = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        stillPending.Id.ShouldBe(pending.Id);
        stillPending.Status.ShouldBe(QueuedMessageStatus.Pending);

        await RunAsync();

        var after = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        after.Id.ShouldBe(pending.Id);
        after.Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task off_rows_do_not_read_shortfall()
    {
        var seated = await ReadyAsync("ks-noop", "KS-D1-OFF", DateTime.UtcNow.AddMinutes(-30));
        await EditRowAsync(seated.AgentId, row => row.Enabled = false);

        (await CountShortfallReadsAsync()).ShouldBe(0);
        (await RemindersAsync(seated.SessionId)).ShouldBeEmpty();

        await EditRowAsync(seated.AgentId, row => row.Enabled = true);
        (await CountShortfallReadsAsync()).ShouldBe(1);
        var message = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        AssertReminder(message, "KS-D1-OFF");
    }

    [Test]
    public async Task an_active_hold_does_not_read_shortfall()
    {
        var seated = await ReadyAsync("ks-held-read", "KS-D1-HOLD", DateTime.UtcNow.AddMinutes(-30));
        await HoldAsync(seated.AgentId, DateTime.UtcNow.AddDays(1));

        (await CountShortfallReadsAsync()).ShouldBe(0);
        (await RemindersAsync(seated.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task disabling_cancels_a_pending_reminder_after_one_failed_typing_attempt()
    {
        var seated = await ReadyAsync("ks-typed", "KS-D2-READY", DateTime.UtcNow.AddMinutes(-3).AddSeconds(-1));
        await RunAsync();
        var pending = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        await MarkFailedAttemptAsync(pending.Id);

        await DisableAsync(seated.AgentId, revision: 1);

        var canceled = (await RemindersAsync(seated.SessionId)).ShouldHaveSingleItem();
        canceled.Id.ShouldBe(pending.Id);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
        canceled.DeliveryAttempts.ShouldBe(1);
    }

    private async Task<SeedBoard> BoardAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = Guid.NewGuid();
        db.Projects.Add(new Project
        {
            Id = project,
            Name = name + " " + project.ToString("N")[..8],
            GitRepositoryUrl = "https://example.test/" + project.ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        db.Boards.Add(new Board
        {
            Id = boardId,
            ProjectId = project,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return new SeedBoard(project, boardId, columnId);
    }

    private async Task<Seated> AgentAsync(string name, Guid boardId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent
        {
            Id = id,
            Name = name + "-" + id.ToString("N")[..8],
            Slug = "ks3-" + id.ToString("N"),
            WorkingDirectory = "/tmp/keep-six",
            BoardId = boardId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = id,
            BundleKey = InstructionBundles.Orchestrator,
            Position = 0,
            CreatedAt = now,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            StandingAgentId = id,
            DefinitionName = "keep-six",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Running,
            CardId = null,
            Cwd = "/tmp/keep-six",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return new Seated(id, sessionId);
    }

    private async Task CardAsync(
        SeedBoard board, string identifier, CardImportance importance, CardUrgency urgency)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.Cards.Add(new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.BoardId,
            BoardColumnId = board.ColumnId,
            Identifier = identifier,
            Title = identifier,
            Description = "keep-six candidate",
            Status = CardStatus.Backlog,
            Importance = importance,
            Urgency = urgency,
            LabelsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task TaskAsync(Guid projectId, AgentTaskRole role, AgentTaskStatus status, DateTime createdAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ProjectId = projectId,
            Role = role,
            Status = status,
            Title = "keep-six " + status,
            Goal = "keep-six reminder",
            Workspace = WorkspaceMode.ReadOnly,
            WorkingDirectory = "/tmp/keep-six",
            CreatedAt = createdAt,
            DispatchedAt = status == AgentTaskStatus.Queued ? null : createdAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Seated> ReadyAsync(string name, string cardId, DateTime enabledAt)
    {
        var home = await BoardAsync(name);
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var seated = await AgentAsync(name, home.BoardId);
        await CardAsync(home, cardId, CardImportance.Critical, CardUrgency.Now);
        await EnableAsync(seated.AgentId);
        await EditRowAsync(seated.AgentId, row => row.EnabledAt = enabledAt);
        return seated with { ProjectId = home.ProjectId };
    }

    private async Task EnableAsync(Guid agentId)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/orchestrators/{agentId}/keep-six", new
        {
            expectedRevision = 0,
            enabled = true,
            target = 6,
            reason = "operator toggle",
            provenance = "Human",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task DisableAsync(Guid agentId, long revision)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/orchestrators/{agentId}/keep-six", new
        {
            expectedRevision = revision,
            enabled = false,
            target = 6,
            reason = "operator toggle",
            provenance = "Human",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task HoldAsync(Guid agentId, DateTime until)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/orchestrators/{agentId}/keep-six/hold",
            new { reason = "model quota", until });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<JsonElement> AckAsync(Guid agentId)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync($"/api/orchestrators/{agentId}/keep-six/ack", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task EditRowAsync(Guid agentId, Action<KeepSixOrchestrator> edit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.KeepSixOrchestrators.SingleAsync(candidate => candidate.AgentId == agentId);
        edit(row);
        await db.SaveChangesAsync();
    }

    private async Task MarkSentAsync(Guid messageId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.SessionQueuedMessages.SingleAsync(candidate => candidate.Id == messageId);
        message.Status = QueuedMessageStatus.Sent;
        message.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task MarkFailedAttemptAsync(Guid messageId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.SessionQueuedMessages.SingleAsync(candidate => candidate.Id == messageId);
        message.Status = QueuedMessageStatus.Pending;
        message.DeliveryAttempts = 1;
        message.DeliveryVerdict = DeliveryVerdict.NoTranscriptRecord;
        message.DeliveryVerdictAt = DateTime.UtcNow;
        message.LastDeliveryStartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<int> CountShortfallReadsAsync()
    {
        var probe = new KeepSixShortfall.ReadProbeCounter();
        KeepSixShortfall.ReadProbe.Value = probe;
        try
        {
            await RunAsync();
            return probe.Count;
        }
        finally
        {
            KeepSixShortfall.ReadProbe.Value = null;
        }
    }

    private async Task RunAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<KeepSixReminderJob>();
        await job.ExecuteAsync(CancellationToken.None);
    }

    private async Task<List<SessionQueuedMessage>> RemindersAsync(Guid sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(message => message.AgentSessionId == sessionId && message.NoteHeader == Header)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
    }

    private async Task<int> CountTasksAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentTasks.CountAsync();
    }

    private async Task SetCeilingAsync(Guid projectId, string overridesJson)
    {
        ConflictException? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>();
            var current = await settings.GetProjectAsync(projectId, CancellationToken.None);
            using var document = JsonDocument.Parse(overridesJson);
            try
            {
                await settings.PutProjectAsync(
                    projectId,
                    new PutDispatchConcurrencyRequest(
                        current.Revision,
                        current.GlobalRevision,
                        document.RootElement.Clone(),
                        "keep-six reminder fixture",
                        "Human"),
                    null,
                    CancellationToken.None);
                return;
            }
            catch (ConflictException ex)
            {
                last = ex;
            }
        }

        throw last!;
    }

    private static void AssertReminder(SessionQueuedMessage message, string cardId)
    {
        message.Origin.ShouldBe(QueuedMessageOrigin.System);
        message.NoteHeader.ShouldBe(Header);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.Body.IndexOf(NotDispatched, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        message.Body.IndexOf("NO_REPLY", StringComparison.Ordinal).ShouldBe(-1);
        message.Body.IndexOf(cardId, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        message.Body.IndexOf(HoldScript, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        message.Body.IndexOf("Host remaining:", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        message.Body.IndexOf("Stage parallel remaining:", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
    }

    private readonly record struct SeedBoard(Guid ProjectId, Guid BoardId, Guid ColumnId);

    private readonly record struct Seated(Guid AgentId, Guid SessionId, Guid? ProjectId = null);
}
