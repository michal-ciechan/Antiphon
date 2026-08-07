using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Turns a delegate's finished turn into a task result and a note delivered to its parent.
///
/// Correlation matches the <c>[antiphon-task:id]</c> MARKER carried in the brief, not the prompt
/// text. Prompt-text matching is right for chat (where a human's stray turn should be ignored) but
/// wrong here: a delegate that reformulates, or a human typing in the delegate's terminal, would
/// otherwise end the task with the wrong text.
///
/// Singleton — invoked from the singleton <see cref="AgentSessionRuntime"/> transcript observer,
/// with a DI scope per operation, mirroring the channel dispatcher's pattern.
/// </summary>
public sealed class AgentTaskReplyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskReplyService> _logger;

    public AgentTaskReplyService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskReplyService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// A session finished a turn. If that session is running a delegated task and the turn was the
    /// one we asked for, settle the task and deliver its report.
    /// </summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var task = await db.AgentTasks
                .FirstOrDefaultAsync(t => t.AgentSessionId == sessionId
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);
            if (task is null)
                return;

            var turn = await ExtractMarkedTurnAsync(db, sessionId, task.Id, ct);
            if (turn is null)
            {
                // Either the delegate hasn't produced text yet (Claude can write the stop marker
                // before its reply — the AssistantText arrival re-triggers us), or this turn was a
                // human typing in the delegate's terminal. Both mean: leave the task running.
                _logger.LogDebug(
                    "Session {SessionId} ended a turn with no report for task {ShortId}; still working",
                    sessionId, DelegationReportFormatter.Short(task.Id));
                return;
            }

            await SettleAsync(db, task, turn, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to settle a delegated task for session {SessionId}", sessionId);
        }
    }

    /// <summary>Answer a Blocked delegate's question and let it resume.</summary>
    public async Task<AgentTaskSummaryDto> AnswerAsync(Guid taskId, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException(nameof(message), "A reply message is required.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

        var task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);
        if (task.Status != AgentTaskStatus.Blocked)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(taskId)} is not waiting for an answer.");
        if (task.AgentSessionId is not Guid sessionId)
            throw new ConflictException("The delegate's session is no longer available.");

        var now = UtcNow();
        task.Status = AgentTaskStatus.Working;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(taskId, AgentTaskEventType.Replied, "Caller answered the delegate's question.", now));
        await db.SaveChangesAsync(ct);

        // The marker rides the answer so the delegate's NEXT turn correlates back to this task.
        var body = $"{DelegationReportFormatter.TaskMarker(taskId)}\n\n{message.Trim()}";
        await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);

        await PublishAsync(task, ct);
        var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, family);
    }

    private async Task SettleAsync(AppDbContext db, AgentTask task, string report, CancellationToken ct)
    {
        var now = UtcNow();
        task.Result = report;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        task.Status = LooksLikeAQuestion(report) ? AgentTaskStatus.Blocked : AgentTaskStatus.Succeeded;

        // Roll the delegate's token spend up onto the task so the board and the per-root ceiling
        // see it. TranscriptEntry carries raw counts per API call (cache reads count as input);
        // cost is derived from the tier, since Claude Code doesn't report dollars per turn.
        if (task.AgentSessionId is Guid sessionId)
        {
            var usage = await db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId && t.InputTokens != null)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    In = g.Sum(t => (long?)(t.InputTokens ?? 0) + (t.CacheReadTokens ?? 0) + (t.CacheCreationTokens ?? 0)) ?? 0,
                    Out = g.Sum(t => (long?)(t.OutputTokens ?? 0)) ?? 0,
                })
                .FirstOrDefaultAsync(ct);
            task.TokensIn = usage?.In ?? 0;
            task.TokensOut = usage?.Out ?? 0;
            task.CostUsd = DelegationCost.Estimate(task.ModelLevel, task.TokensIn, task.TokensOut);
        }

        // The delegate was told to spill a long report to a file. Note it if it did — and if it
        // ignored the instruction, write the file ourselves so the excerpt has somewhere to point.
        task.ResultFilePath = await ResolveSpillFileAsync(task, report, ct);

        db.AgentTaskEvents.Add(NewEvent(
            task.Id,
            task.Status == AgentTaskStatus.Blocked ? AgentTaskEventType.Blocked : AgentTaskEventType.Completed,
            task.Status == AgentTaskStatus.Blocked
                ? "Delegate asked a question."
                : $"Delegate reported {report.Length:N0} characters.",
            now));
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Task {ShortId} settled as {Status} ({Chars:N0} chars, ${Cost:0.000})",
            DelegationReportFormatter.Short(task.Id), task.Status, report.Length, task.CostUsd);

        await DeliverToParentAsync(task, report, ct);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// Deliver the completion note into the parent's session — WhenIdle, so it lands between the
    /// parent's turns rather than interrupting one.
    /// </summary>
    private async Task DeliverToParentAsync(AgentTask task, string report, CancellationToken ct)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;

        var note = DelegationReportFormatter.BuildCompletionNote(task, _settings, report);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

            // ConversationKey batches a contiguous run of same-root completions into ONE delivery,
            // so five delegates landing together produce one note, not five turns. The queue's
            // size-aware batching stops before the combined body crosses the inline ceiling.
            await queue.EnqueueAsync(
                parentSession, note.Body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A dead parent session must not lose the result — it is already persisted on the task.
            _logger.LogWarning(
                ex, "Could not deliver task {ShortId} report to parent session {SessionId}",
                DelegationReportFormatter.Short(task.Id), parentSession);
        }
    }

    /// <summary>
    /// The turn's assistant text, but only if the turn was the one we asked for — its prompt must
    /// carry this task's marker. Returns null when the turn isn't ours or has no text yet.
    /// </summary>
    private static async Task<string?> ExtractMarkedTurnAsync(
        AppDbContext db, Guid sessionId, Guid taskId, CancellationToken ct)
    {
        var endSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (endSeq is not long turnEnd)
            return null;

        var prompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence < turnEnd)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (prompt?.Text is not string promptText)
            return null;

        // The marker gate. A human typing in this terminal produces a prompt without it.
        if (!promptText.Contains(DelegationReportFormatter.TaskMarker(taskId), StringComparison.Ordinal))
            return null;

        var nextPrompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > prompt.Sequence)
            .MinAsync(t => (long?)t.Sequence, ct);

        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > prompt.Sequence);
        if (nextPrompt is long cap)
            query = query.Where(t => t.Sequence < cap);

        var texts = await query.OrderBy(t => t.Sequence).Select(t => t.Text).ToListAsync(ct);
        var joined = string.Join("\n\n", texts.Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>
    /// Where the full detail lives when a report is too big to forward. The delegate was told to
    /// write one; if it did and the path exists, use it. Otherwise the server writes it, so the
    /// head+tail excerpt always has something real to point at.
    /// </summary>
    private async Task<string?> ResolveSpillFileAsync(AgentTask task, string report, CancellationToken ct)
    {
        if (report.Length <= _settings.ReplyInlineMaxChars)
            return null;

        var relative = Path.Combine(".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        var absolute = Path.Combine(task.WorkingDirectory, relative);
        try
        {
            if (File.Exists(absolute))
                return absolute;

            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            await File.WriteAllTextAsync(absolute, report, ct);
            _logger.LogInformation(
                "Task {ShortId} reported {Chars:N0} chars without spilling; wrote {Path} as the backstop",
                DelegationReportFormatter.Short(task.Id), report.Length, absolute);
            return absolute;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only or vanished workspace must not lose the result — it is on the task row.
            _logger.LogWarning(ex, "Could not write the spill file for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }
    }

    /// <summary>
    /// A delegate that ends its turn asking something needs an ANSWER, not a retry. Deliberately
    /// conservative: only a question mark in the last couple of lines counts, so a report that
    /// merely mentions a question mid-text still reads as finished.
    /// </summary>
    internal static bool LooksLikeAQuestion(string report)
    {
        var lines = report.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0)
            return false;

        return lines.TakeLast(2).Any(l => l.EndsWith('?'));
    }

    private async Task PublishAsync(AgentTask task, CancellationToken ct) =>
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);

    private static AgentTaskEvent NewEvent(Guid taskId, AgentTaskEventType type, string detail, DateTime at) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail.Length <= 4000 ? detail : detail[..4000],
            At = at,
        };

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
