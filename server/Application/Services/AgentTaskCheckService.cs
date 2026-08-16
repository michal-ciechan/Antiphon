using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Runs one scheduled check-in and delivers it to the delegate's CALLER (CARD-0047 §1.3-§1.4).
///
/// <para>It gathers facts through <see cref="DelegateCheckProbe"/> — which cannot write — and
/// delivers a note through the caller's own message queue. It never touches the delegate: not its
/// queue, not its session, not its transcript, not its status. That is the whole read-only
/// guarantee, and the only reason a check is safe to run against work in flight.</para>
///
/// <para><b>A check cannot settle anything, structurally.</b> Settlement of the DELEGATE's task
/// reads the DELEGATE's transcript, and nothing here writes there. Settlement of the PARENT's own
/// task (when the caller is itself a delegate) is gated on the parent's task marker appearing in
/// the prompt that opened the turn — and a check note is an ordinary unmarked prompt, so the
/// parent's reaction turn fails that gate. This is the same shape completion notes already have,
/// and it is load-bearing: check notes must NEVER be classified as housekeeping prompts in the
/// walk-back, or the walk-back would skip past them to the parent's marked brief and let the
/// parent settle its own task on its reaction to a note about someone else's.</para>
/// </summary>
public sealed class AgentTaskCheckService
{
    /// <summary>How much of the digest the task's timeline entry keeps.</summary>
    private const int EventDetailChars = 900;

    private readonly AppDbContext _db;
    private readonly DelegateCheckProbe _probe;
    private readonly SessionMessageQueueService _queue;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskCheckService> _logger;
    private readonly PtyDeliveryProfile? _ptyProfile;

    public AgentTaskCheckService(
        AppDbContext db,
        DelegateCheckProbe probe,
        SessionMessageQueueService queue,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskCheckService> logger,
        PtyDeliveryProfile? ptyProfile = null)
    {
        _db = db;
        _probe = probe;
        _queue = queue;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _ptyProfile = ptyProfile;
    }

    /// <summary>What one check did — for the worker's logging and for the tests.</summary>
    public enum CheckOutcome
    {
        /// <summary>The task row is gone.</summary>
        TaskMissing = 0,

        /// <summary>It settled between the claim and now; the completion note is the caller's answer.</summary>
        AlreadySettled = 1,

        /// <summary>There is no caller session to deliver to.</summary>
        NoRecipient = 2,

        Delivered = 3,

        /// <summary>Facts were gathered but the note could not be queued (transport).</summary>
        DeliveryFailed = 4,
    }

    public async Task<CheckOutcome> RunCheckAsync(Guid taskId, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return CheckOutcome.TaskMissing;

        // Claimed while running, settled before we got here. The completion note is already on its
        // way and says everything a check would; a second note would only be noise.
        if (AgentTaskService.IsSettled(task.Status))
        {
            _logger.LogDebug(
                "Check on task {ShortId} skipped — it settled between the claim and the run",
                DelegationReportFormatter.Short(task.Id));
            return CheckOutcome.AlreadySettled;
        }

        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return CheckOutcome.NoRecipient;

        var facts = await _probe.GatherAsync(task, ct);
        var digest = DelegateCheckProbe.RenderDigest(facts);

        // Slice 4 replaces this with the interpreter's 3-5 lines and falls back to the digest when
        // the model call fails. Until then the digest IS the note, which is exactly why slices 1-3
        // are useful on their own.
        var body = BuildNote(task, facts, digest);

        try
        {
            await _queue.EnqueueAsync(
                parentSession, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Check, ConversationKey(task.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A check is advisory. A caller session that is momentarily unreachable must never turn
            // an observation into an error path — the schedule has already moved on.
            _logger.LogWarning(
                ex, "Check note for task {ShortId} could not be queued to session {SessionId}",
                DelegationReportFormatter.Short(task.Id), parentSession);
            return CheckOutcome.DeliveryFailed;
        }

        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Check,
            Detail = digest.Length <= EventDetailChars ? digest : digest[..EventDetailChars] + "…",
            At = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Check #{Number} on task {ShortId} delivered to session {SessionId}",
            facts.Task.CheckNumber, DelegationReportFormatter.Short(task.Id), parentSession);
        return CheckOutcome.Delivered;
    }

    /// <summary>One conversation per task, so a check never coalesces with anything else.</summary>
    public static string ConversationKey(Guid taskId) => $"check:{taskId:N}";

    /// <summary>The prefix every check note starts with — nothing else in the system emits it.</summary>
    public const string HeaderPrefix = "[check ";

    /// <summary>
    /// The note as the caller sees it. The first line has to be unmistakable at a glance: a check
    /// is an OBSERVATION about work still in flight, and a caller that read it as a completion
    /// would move on from a task that has not finished.
    ///
    /// <para>So the envelope is fixed: it opens with <see cref="HeaderPrefix"/>, never with
    /// <c>[task </c>, its status vocabulary is the session's (Running/idle/working) and never the
    /// completion vocabulary (done/failed), and it carries NO task marker of any task — see
    /// <see cref="ScrubTaskMarkers"/>, which matters because the transcript tail legitimately
    /// contains the delegate's own brief, marker and all.</para>
    /// </summary>
    internal string BuildNote(AgentTask task, DelegateCheckProbe.CheckFacts facts, string digest)
    {
        var header = new StringBuilder();
        header.Append(HeaderPrefix)
              .Append(DelegationReportFormatter.Short(task.Id))
              .Append(" #").Append(facts.Task.CheckNumber).Append(']');

        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Title))
            bits.Add(task.Title.Trim().ReplaceLineEndings(" "));
        bits.Add($"{FormatAge(facts.Task.Age)} elapsed (expected {facts.Task.ExpectedDurationMinutes}m)");
        bits.Add(facts.Session is { } session
            ? $"session {session.Status} · {(session.Working ? "working" : "idle")}"
            : "no session");
        if (task.NextCheckAt is null)
            bits.Add($"final check — the {_settings.CheckMaxCount}-check budget is spent");
        header.Append(' ').Append(string.Join(" · ", bits));

        var note = $"{header}\n\n{ScrubTaskMarkers(digest)}".ReplaceLineEndings("\n");

        // A digest is a few KB and the pty it is typed into has a measured ceiling (CARD-0037). Fit
        // it the same way a report is fitted rather than letting every check trip the oversize
        // tripwire — the excerpt banner names where to read the rest.
        var ceiling = _ptyProfile?.Ceilings.ReplyInlineMaxChars ?? _settings.ReplyInlineMaxChars;
        if (note.Length <= ceiling)
            return note;

        // Floored: a pathological title (300 chars) against a small configured ceiling could
        // otherwise leave the body a negative budget, and the excerpt arithmetic would throw on the
        // one delivery that most needed to survive.
        var bodyBudget = Math.Max(400, ceiling - header.Length - 2);
        var (fitted, _) = DelegationReportFormatter.FitReport(
            ScrubTaskMarkers(digest), task, _settings, bodyBudget);
        return $"{header}\n\n{fitted}".ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Strip every task marker from the body. The transcript tail quotes the delegate's brief, which
    /// OPENS with <c>[antiphon-task:xxxxxxxx]</c> — so a note that simply pasted the digest would
    /// carry a live-looking marker into someone else's session. Nothing downstream should have to
    /// reason about whose marker it was.
    /// </summary>
    internal static string ScrubTaskMarkers(string body) =>
        TaskMarkerPattern.Replace(body, "[task-marker removed]");

    private static readonly Regex TaskMarkerPattern = new(
        @"\[antiphon-task:[0-9a-fA-F]{8}\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FormatAge(TimeSpan? age)
    {
        if (age is not { } value || value < TimeSpan.Zero)
            return "unknown";
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}h{value.Minutes:00}m"
            : $"{(int)value.TotalMinutes}m";
    }
}
