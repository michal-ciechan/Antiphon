using System.Collections.Concurrent;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0247 S3: once-a-minute detection sweep for orchestrator investigation runs.
/// Detection only — one Warning <see cref="AgentIncidentKind.OrchestratorInvestigation"/> per
/// run, never a kill, retype, block, or card move (CARD-0153's rule).
///
/// Singleton: the per-session watermark has to survive the hosted service's per-tick scope.
/// Durable idempotence is the incident row keyed by <c>(session, runStartSeq)</c>.
/// </summary>
public sealed class OrchestratorInvestigationSweepService
{
    public static readonly TimeSpan BehaviourLookback = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<Guid, long> _watermarks = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupervisionSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<OrchestratorInvestigationSweepService> _logger;

    public OrchestratorInvestigationSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<SupervisionSettings> settings,
        TimeProvider time,
        ILogger<OrchestratorInvestigationSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate every orchestrator-by-behaviour or by-declaration session. Returns how many
    /// NEW investigation incidents this pass raised (not a global count).
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.OrchestratorInvestigation.Enabled)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionIds = await LoadCandidateSessionIdsAsync(db, ct);
        var raised = 0;
        foreach (var sessionId in sessionIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                raised += await SweepSessionAsync(db, sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Orchestrator investigation sweep failed for session {SessionId}; continues",
                    sessionId);
            }
        }

        return raised;
    }

    internal async Task<int> SweepSessionAsync(
        AppDbContext db,
        Guid sessionId,
        CancellationToken ct)
    {
        var watermark = _watermarks.GetOrAdd(sessionId, 0);
        var entries = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId && e.Sequence > watermark)
            .OrderBy(e => e.Sequence)
            .Select(e => new EntryRow(
                e.Sequence, e.Kind, e.Timestamp, e.ToolName, e.ToolInput, e.ToolUseId, e.Text))
            .ToListAsync(ct);

        // A run that started just below the watermark still needs the preceding source reads.
        if (watermark > 0 && entries.Count > 0)
        {
            var lookback = await db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId
                    && e.Sequence <= watermark
                    && e.Sequence > watermark - 256)
                .OrderBy(e => e.Sequence)
                .Select(e => new EntryRow(
                    e.Sequence, e.Kind, e.Timestamp, e.ToolName, e.ToolInput, e.ToolUseId, e.Text))
                .ToListAsync(ct);
            entries = lookback.Concat(entries).ToList();
        }
        else if (watermark == 0 && entries.Count == 0)
        {
            return 0;
        }

        var dispatches = await db.AgentTasks.AsNoTracking()
            .Where(t => t.ParentSessionId == sessionId)
            .Select(t => new { t.CreatedAt, t.DispatchedAt })
            .ToListAsync(ct);

        var reports = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && (m.Origin == QueuedMessageOrigin.Delegation || m.Origin == QueuedMessageOrigin.Check)
                && m.Status == QueuedMessageStatus.Sent
                && m.SentAt != null)
            .Select(m => new { m.Body, m.SentAt })
            .ToListAsync(ct);

        var events = ClassifyEntries(entries, dispatches.Select(d => d.DispatchedAt ?? d.CreatedAt).ToList(),
            reports.Select(r => (r.Body, r.SentAt!.Value)).ToList());
        var runs = OrchestratorInvestigationDetector.FindRuns(events);
        if (runs.Count == 0)
        {
            if (entries.Count > 0)
                _watermarks[sessionId] = entries[^1].Sequence;
            return 0;
        }

        var existing = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.OrchestratorInvestigation)
            .Select(i => i.FailureReason)
            .ToListAsync(ct);
        var known = new HashSet<long>();
        foreach (var reason in existing)
        {
            if (OrchestratorInvestigationDetector.TryParseRunStart(reason, out var seq))
                known.Add(seq);
        }

        var owner = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, sessionId, ct);
        var raised = 0;
        foreach (var run in runs)
        {
            if (known.Contains(run.StartSequence))
            {
                await MaybeUpdateMessageAsync(db, sessionId, run, ct);
                continue;
            }

            var message = OrchestratorInvestigationDetector.FormatMessage(run);
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = owner,
                SessionId = sessionId,
                Kind = AgentIncidentKind.OrchestratorInvestigation,
                Severity = AlertSeverity.Warning,
                Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
                FailureReason = OrchestratorInvestigationDetector.RunStartKey(run.StartSequence),
                CreatedAt = _time.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(ct);
            known.Add(run.StartSequence);
            raised++;
            _logger.LogInformation(
                "Orchestrator investigation on session {SessionId}: {Message} (runStartSeq={Seq})",
                sessionId, message, run.StartSequence);
        }

        if (entries.Count > 0)
            _watermarks[sessionId] = entries[^1].Sequence;
        return raised;
    }

    internal static List<OrchestratorInvestigationDetector.ClassifiedEvent> ClassifyEntries(
        IReadOnlyList<EntryRow> entries,
        IReadOnlyList<DateTime> dispatchTimes,
        IReadOnlyList<(string Body, DateTime SentAt)> reports)
    {
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>(entries.Count + dispatchTimes.Count);
        foreach (var e in entries)
        {
            if (e.Kind == TranscriptKinds.ToolCall)
            {
                var call = OrchestratorInvestigationDetector.ClassifyCall(e.ToolName, e.ToolInput);
                var identifiers = OrchestratorInvestigationDetector.IdentifiersFromCall(e.ToolName, e.ToolInput);
                events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                    call.Kind, e.Sequence, e.Timestamp, e.ToolUseId, e.ToolName, identifiers, e.Text));
                continue;
            }

            if (e.Kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt)
            {
                if (OrchestratorInvestigationDetector.IsReportText(e.Text)
                    || MatchesQueuedReport(e.Text, e.Timestamp, reports))
                {
                    events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                        OrchestratorInvestigationDetector.EventKind.Report,
                        e.Sequence, e.Timestamp, null, null,
                        OrchestratorInvestigationDetector.IdentifiersFromText(e.Text), e.Text));
                }
                else if (OrchestratorInvestigationDetector.IsHumanPrompt(e.Text)
                    || OrchestratorInvestigationDetector.ContainsNudge(e.Text))
                {
                    events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                        OrchestratorInvestigationDetector.EventKind.Human,
                        e.Sequence, e.Timestamp, null, null,
                        OrchestratorInvestigationDetector.IdentifiersFromText(e.Text), e.Text));
                }
            }
            else if (OrchestratorInvestigationDetector.ContainsNudge(e.Text))
            {
                events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                    OrchestratorInvestigationDetector.EventKind.OtherTool,
                    e.Sequence, e.Timestamp, e.ToolUseId, e.ToolName, [], e.Text));
            }
        }

        foreach (var at in dispatchTimes)
        {
            var seq = SequenceAtOrBefore(events, at);
            events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                OrchestratorInvestigationDetector.EventKind.Dispatch,
                seq, at, null, null, []));
        }

        events.Sort((a, b) =>
        {
            var bySeq = a.Sequence.CompareTo(b.Sequence);
            if (bySeq != 0)
                return bySeq;
            var at = a.Timestamp ?? DateTime.MinValue;
            var bt = b.Timestamp ?? DateTime.MinValue;
            var byTime = at.CompareTo(bt);
            if (byTime != 0)
                return byTime;
            var aDispatch = a.Kind == OrchestratorInvestigationDetector.EventKind.Dispatch ? 0 : 1;
            var bDispatch = b.Kind == OrchestratorInvestigationDetector.EventKind.Dispatch ? 0 : 1;
            return aDispatch.CompareTo(bDispatch);
        });
        return events;
    }

    private static bool MatchesQueuedReport(
        string? text, DateTime? timestamp, IReadOnlyList<(string Body, DateTime SentAt)> reports)
    {
        if (string.IsNullOrEmpty(text) || reports.Count == 0)
            return false;
        foreach (var (body, sentAt) in reports)
        {
            if (timestamp is DateTime ts && ts < sentAt.AddSeconds(-5))
                continue;
            if (text.Contains(body, StringComparison.Ordinal)
                || body.Contains(text, StringComparison.Ordinal)
                || OrchestratorInvestigationDetector.IsReportText(body))
            {
                if (OrchestratorInvestigationDetector.IsReportText(body)
                    && PromptHeadsMatch(text, body))
                    return true;
            }
        }

        return false;
    }

    private static bool PromptHeadsMatch(string text, string body)
    {
        var a = text.Trim();
        var b = body.Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;
        var window = Math.Min(80, Math.Min(a.Length, b.Length));
        return a.AsSpan(0, window).Equals(b.AsSpan(0, window), StringComparison.Ordinal);
    }

    private static long SequenceAtOrBefore(
        List<OrchestratorInvestigationDetector.ClassifiedEvent> events, DateTime at)
    {
        long seq = 0;
        foreach (var ev in events)
        {
            if (ev.Timestamp is DateTime ts && ts <= at)
                seq = ev.Sequence;
        }

        return seq;
    }

    private static async Task MaybeUpdateMessageAsync(
        AppDbContext db, Guid sessionId, OrchestratorInvestigationDetector.InvestigationRun run, CancellationToken ct)
    {
        var key = OrchestratorInvestigationDetector.RunStartKey(run.StartSequence);
        var row = await db.AgentIncidents
            .Where(i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.OrchestratorInvestigation
                && i.FailureReason == key)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return;
        var next = OrchestratorInvestigationDetector.FormatMessage(run);
        if (string.Equals(row.Message, next, StringComparison.Ordinal))
            return;
        row.Message = ColumnText.Clip(next, AgentIncident.MessageMaxLength);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> LoadCandidateSessionIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - BehaviourLookback;

        var byBehaviour = await db.AgentTasks.AsNoTracking()
            .Where(t => t.ParentSessionId != null && t.CreatedAt >= cutoff)
            .Select(t => t.ParentSessionId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var bundleAgentIds = await db.AgentBundleAttachments.AsNoTracking()
            .Where(a => a.BundleKey == InstructionBundles.Orchestrator)
            .Select(a => a.AgentId)
            .ToListAsync(ct);

        var byBundle = new List<Guid>();
        if (bundleAgentIds.Count > 0)
        {
            var sessionTexts = await db.Agents.AsNoTracking()
                .Where(a => bundleAgentIds.Contains(a.Id) && a.PersistentSessionId != null)
                .Select(a => a.PersistentSessionId!)
                .ToListAsync(ct);
            foreach (var text in sessionTexts)
            {
                if (Guid.TryParse(text, out var id))
                    byBundle.Add(id);
            }
        }

        var byKind = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Kind == AgentTaskKind.Orchestrator
                && t.AgentSessionId != null
                && t.CreatedAt >= cutoff)
            .Select(t => t.AgentSessionId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return byBehaviour.Concat(byBundle).Concat(byKind).Distinct().ToList();
    }

    internal readonly record struct EntryRow(
        long Sequence,
        string Kind,
        DateTime? Timestamp,
        string? ToolName,
        string? ToolInput,
        string? ToolUseId,
        string? Text);
}
