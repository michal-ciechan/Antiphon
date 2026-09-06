using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable rules receipt, delivery barrier and provider acknowledgement. Never kills a working session.</summary>
public sealed class GrokRulesRefreshService(
    IServiceScopeFactory scopes, TimeProvider clock, IOptions<GrokRulesSettings> settings)
{
    public async Task RecoverActiveAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.AgentSessions.AsNoTracking().Where(s => s.AgentKind == AgentKind.Grok
            && s.GrokRulesGeneration != null && (s.Status == SessionStatus.Running || s.Status == SessionStatus.Starting))
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await RecoverSessionAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                scope.ServiceProvider.GetService<ILogger<GrokRulesRefreshService>>()?
                    .LogWarning("Rules recovery for {SessionId} failed ({ErrorType}); remaining sessions will still be reconciled", id, ex.GetType().Name);
            }
        }
    }

    internal async Task RecoverSessionAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<ILaunchOwnership>()?.Owns(id) == true) return;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == id, ct);
        // Starting has not passed provider readiness; the launch recovery owner handles it.
        if (session.Status == SessionStatus.Starting) return;
        try
        {
            if (session.GrokRulesState == GrokRulesState.Failed) return;
            var receipt = Receipt(session);
            if (receipt?.Generation != session.GrokRulesGeneration)
            {
                var runner = scope.ServiceProvider.GetRequiredService<ISessionRunnerClient>();
                var recovered = (await runner.GetAsync(id, ct)).GrokRulesReceipt;
                ValidateExpectedReceipt(session, recovered);
                session.GrokRulesReceiptJson = JsonSerializer.Serialize(recovered);
                await db.SaveChangesAsync(ct);
            }
            await scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>().CatchUpTranscriptAsync(id, ct);
            await ReconcileAsync(id, ct);
            await db.Entry(session).ReloadAsync(ct);
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
            if (session.GrokRulesState == GrokRulesState.Ready)
                await QueueLaunchBriefAsync(db, session, queue, ct);
            await queue.FlushIfIdleAsync(id, ct);
        }
        catch (Exception ex) when (ex is ConflictException or GrokRulesHttpException)
        {
            await db.Entry(session).ReloadAsync(ct);
            session.GrokRulesState = GrokRulesState.Failed;
            session.GrokRulesFailure = session.GrokRulesReadyAt is null
                ? "grok_rules_initialization_failed: revision_mismatch" : "grok_rules_refresh_failed: revision_mismatch";
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            await db.Entry(session).ReloadAsync(ct);
            if (session.GrokRulesState == GrokRulesState.Failed && session.GrokRulesReadyAt is null)
                await scope.ServiceProvider.GetRequiredService<AgentSessionService>().FailRecoveredRulesStartupAsync(id, ct);
        }
    }
    public static string Header(Guid id) => $"[antiphon-grok-rules:{id:N}]";
    public static GrokRulesReceipt? Receipt(AgentSession session) => ParseReceipt(session.GrokRulesReceiptJson);

    public static GrokRulesReceipt? ParseReceipt(string? json)
    {
        try { return json is null ? null : JsonSerializer.Deserialize<GrokRulesReceipt>(json); }
        catch (JsonException) { return null; }
    }

    public static bool IsClosed(AgentSession session) => session.GrokRulesState is GrokRulesState.Pending or GrokRulesState.Failed;

    public static async Task<bool> IsRefreshPromptAsync(AppDbContext db, Guid sessionId, string? prompt, CancellationToken ct)
    {
        if (prompt is null || !prompt.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal)) return false;
        var close = prompt.IndexOf(']');
        if (close < 21 || !Guid.TryParseExact(prompt[21..close], "N", out var id)) return false;
        return await db.SessionQueuedMessages.AnyAsync(m => m.Id == id && m.AgentSessionId == sessionId
            && m.Origin == QueuedMessageOrigin.System && m.RulesRefreshKey != null, ct);
    }

    public static void PreflightResume(AgentSession session, GrokRulesPayload? desired)
    {
        if (session.AgentKind != AgentKind.Grok) return;
        var receipt = Receipt(session);
        if (desired is not null && receipt?.TransportVersion != 1)
            throw new ConflictException($"Grok retains old inline rules on resume. History for session {session.Id:D} is retained. "
                + "Checkpoint or hand off as desired, then use POST /api/agents/{id}/start with {\"fresh\":true}.",
                "grok_rules_legacy_resume_requires_fresh_start");
        if (desired is null && receipt is not null)
            throw new ConflictException("Grok retains the rules bootstrap on resume. History and rules remain retained; "
                + "use POST /api/agents/{id}/start with {\"fresh\":true} to remove all rules.",
                "grok_rules_removal_requires_fresh_start");
    }

    public async Task PrepareLaunchAsync(AppDbContext db, AgentSession session, AgentLaunchSpec spec, CancellationToken ct)
    {
        GrokRulesLaunchValidation.Validate(spec, settings.Value);
        if (spec.GrokRulesPayload is not { } payload) return;
        using var scope = scopes.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ISessionRunnerClient>();
        if ((await runner.GetCapabilitiesAsync(ct))?.Features?.Contains(GrokRulesTransport.Capability, StringComparer.Ordinal) != true)
            throw new ConflictException("Selected runner does not advertise grokRulesFileV1.", "grok_rules_transport_unsupported");
        var bytes = GrokRulesTransport.Encode(payload, true, settings.Value.MaxFileBytes);
        session.GrokRulesGeneration = payload.Generation;
        session.GrokRulesExpectedSha256 = GrokRulesTransport.Hash(bytes);
        session.GrokRulesExpectedByteCount = bytes.Length;
        session.GrokRulesState = GrokRulesState.Pending;
        session.GrokRulesFailure = null;
        session.GrokRulesReadyAt = null;
        session.GrokRulesLaunchTranscriptFloor = await db.TranscriptEntries.Where(e => e.AgentSessionId == session.Id)
            .MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task CaptureReceiptAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
        if (session.GrokRulesGeneration is null) return;
        var runner = scope.ServiceProvider.GetRequiredService<ISessionRunnerClient>();
        var receipt = (await runner.GetAsync(sessionId, ct)).GrokRulesReceipt;
        ValidateExpectedReceipt(session, receipt);
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt);
        await db.SaveChangesAsync(ct);
        await ReconcileAsync(sessionId, ct);
    }

    public async Task InitializeAsync(Guid sessionId, CancellationToken ct)
    {
        await CaptureReceiptAsync(sessionId, ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
        if (session.GrokRulesGeneration is null) return;
        var launchKey = $"launch:{session.GrokRulesGeneration:N}";
        var deadline = clock.GetUtcNow().UtcDateTime.AddSeconds(settings.Value.InitializationTimeoutSeconds);
        await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId && m.RulesRefreshKey == launchKey
            && m.RulesDeadlineAt == null).ExecuteUpdateAsync(u => u.SetProperty(m => m.RulesDeadlineAt, deadline), ct);
        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
        var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Pull before judging absence; catch-up deliberately has no queue side effects.
            await runtime.CatchUpTranscriptAsync(sessionId, ct);
            await ReconcileAsync(sessionId, ct);
            await db.Entry(session).ReloadAsync(ct);
            if (session.GrokRulesState == GrokRulesState.Ready)
            {
                await QueueLaunchBriefAsync(db, session, queue, ct);
                return;
            }
            if (session.GrokRulesState == GrokRulesState.Failed)
                throw new ConflictException(session.GrokRulesFailure ?? "Rules initialization failed.", "grok_rules_initialization_failed");
            await queue.FlushSessionAsync(sessionId, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(250), clock, ct);
        }
    }

    private async Task QueueLaunchBriefAsync(AppDbContext db, AgentSession session, SessionMessageQueueService queue, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Enqueue uses its own context; an advisory lock serializes the durable existence check
        // across initialization and recovery without locking the queue's foreign-key parent.
        var lockKey = $"grok-rules-brief:{session.Id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.AgentSessionId == session.Id
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);
        if (task is null) return;
        // Durable task goal is the recovery source; no brief is constructed until receipt/ack commit.
        if (await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == session.Id
            && m.SourceTaskId == task.Id && m.Origin == QueuedMessageOrigin.Delegation, ct)) return;
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<Antiphon.Server.Application.Settings.DelegationSettings>>().Value;
        var profile = scope.ServiceProvider.GetService<PtyDeliveryProfile>();
        var brief = AgentTaskDispatcher.FitBriefForTyping(task, options, profile?.Ceilings, null, session.AgentKind);
        await queue.EnqueueAsync(session.Id, brief, MessageSendMode.WhenIdle, ct,
            QueuedMessageOrigin.Delegation, sourceTaskId: task.Id, deliverIfIdle: false);
        await transaction.CommitAsync(ct);
    }

    private static void ValidateExpectedReceipt(AgentSession session, GrokRulesReceipt? receipt)
    {
        if (receipt is null || receipt.TransportVersion != 1 || receipt.Generation != session.GrokRulesGeneration
            || receipt.Sha256 != session.GrokRulesExpectedSha256 || receipt.ByteCount != session.GrokRulesExpectedByteCount)
            throw new ConflictException("Missing or mismatched runner rules receipt; work remains held.", "grok_rules_receipt_invalid");
        try
        {
            GrokRulesTransport.ValidateReceiptPath(receipt.Path, session.Id);
            if (GrokRulesArgvPolicy.ValidatePayload(GrokRulesTransport.Bootstrap(receipt.Path), true, true) is not null)
                throw new GrokRulesTransportException("grok_rules_receipt_invalid", "bootstrap");
        }
        catch (GrokRulesTransportException ex) { throw new GrokRulesHttpException(ex); }
    }

    public async Task ReconcileAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session?.AgentKind != AgentKind.Grok || session.GrokRulesGeneration is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize live/sync/startup writers across server processes, not just this singleton.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", ct);
        await db.Entry(session).ReloadAsync(ct);
        var receipt = Receipt(session);
        if (receipt is null || session.GrokRulesState == GrokRulesState.Failed) return;
        ValidateExpectedReceipt(session, receipt);
        var rows = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId && m.RulesRefreshKey != null)
            .OrderBy(m => m.Sequence).ToListAsync(ct);
        var transcript = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId
            && e.Sequence > (session.GrokRulesLaunchTranscriptFloor ?? 0)).OrderBy(e => e.Sequence).ToListAsync(ct);
        var next = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).MaxAsync(m => (long?)m.Sequence, ct) ?? 0;
        var launchKey = $"launch:{receipt.Generation:N}";
        if (!rows.Any(m => m.RulesRefreshKey == launchKey))
            AddRow(launchKey, null, null, 0);

        foreach (var boundary in transcript.Where(e => e.Kind == TranscriptKinds.CompactBoundary))
        {
            var key = $"compact:{boundary.Id:N}";
            if (rows.Any(m => m.RulesRefreshKey == key)) continue;
            var owningPrompt = transcript.LastOrDefault(e => e.Kind == TranscriptKinds.UserPrompt && e.Sequence < boundary.Sequence);
            var causedBy = owningPrompt?.Text is { } text
                ? rows.LastOrDefault(m => text.StartsWith(Header(m.Id), StringComparison.Ordinal)) : null;
            var followOn = causedBy is null ? 0 : causedBy.RulesFollowOnCount + 1;
            var row = AddRow(key, boundary.Sequence, causedBy?.RulesChainId ?? causedBy?.Id, followOn);
            if (followOn > 1) Fail(session, row, "refresh_loop");
        }

        foreach (var row in rows.Where(m => m.RulesAcknowledgedAt is null && m.RulesFailure is null && m.RulesCoveredByMessageId is null))
            Judge(row, transcript, session);

        // Boundaries accumulated before delivery share one read. Keep every trigger identity.
        var pending = rows.Where(m => m.RulesAcknowledgedAt is null && m.RulesFailure is null
            && m.DeliveryAttempts == 0 && m.RulesCoveredByMessageId is null).ToList();
        if (pending.Count > 1)
            foreach (var sibling in pending.Skip(1)) sibling.RulesCoveredByMessageId = pending[0].Id;
        foreach (var covered in rows.Where(m => m.RulesCoveredByMessageId != null))
        {
            var leader = rows.Single(m => m.Id == covered.RulesCoveredByMessageId);
            if (leader.RulesAcknowledgedAt is null) continue;
            covered.RulesAcknowledgedAt = leader.RulesAcknowledgedAt;
            covered.Status = QueuedMessageStatus.Canceled;
            covered.CanceledAt = clock.GetUtcNow().UtcDateTime;
        }
        var current = rows.Where(m => ParseReceipt(m.RulesReceiptJson)?.Generation == receipt.Generation).ToList();
        if (session.GrokRulesState != GrokRulesState.Failed)
        {
            session.GrokRulesState = current.All(m => m.RulesAcknowledgedAt is not null) ? GrokRulesState.Ready : GrokRulesState.Pending;
            if (session.GrokRulesState == GrokRulesState.Ready) session.GrokRulesReadyAt ??= clock.GetUtcNow().UtcDateTime;
        }
        else
        {
            var sessionText = sessionId.ToString("D");
            var agentId = await db.Agents.Where(a => a.PersistentSessionId == sessionText)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(), AgentId = agentId, SessionId = sessionId,
                Kind = AgentIncidentKind.DeliveryVerificationFailed, Severity = AlertSeverity.Error,
                Message = $"{session.GrokRulesFailure}. Queued work is held; inspect the rules read and explicitly retry or cancel.",
                FailureReason = session.GrokRulesFailure, CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        SessionQueuedMessage AddRow(string key, long? boundary, Guid? chain, int followOn)
        {
            var id = Guid.NewGuid();
            var row = new SessionQueuedMessage
            {
                Id = id, AgentSessionId = sessionId, Sequence = ++next, Origin = QueuedMessageOrigin.System,
                CreatedAt = clock.GetUtcNow().UtcDateTime, RulesRefreshKey = key,
                RulesReceiptJson = session.GrokRulesReceiptJson, RulesChainId = chain ?? id,
                RulesFollowOnCount = followOn, RulesBoundarySequence = boundary,
                RulesDeadlineAt = boundary is null && session.Status == SessionStatus.Running
                    ? clock.GetUtcNow().UtcDateTime.AddSeconds(settings.Value.InitializationTimeoutSeconds) : null,
                Body = Prompt(id, receipt),
            };
            rows.Add(row);
            db.SessionQueuedMessages.Add(row);
            session.GrokRulesState = GrokRulesState.Pending;
            return row;
        }
    }

    public static string Prompt(Guid id, GrokRulesReceipt receipt) =>
        $"{Header(id)}\nRead the ENTIRE standing rules file \"{receipt.Path}\"; use continuation reads beyond the 1,000-line tool default. "
        + $"Do no task work yet. Version={receipt.TransportVersion} generation={receipt.Generation:N} sha256={receipt.Sha256} byteCount={receipt.ByteCount}. "
        + $"On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id={id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}\n"
        + $"Otherwise reply: ANTIPHON_RULES_FAILED id={id:N} generation={receipt.Generation:N} reason=unreadable (or incomplete or revision_mismatch).";

    private void Judge(SessionQueuedMessage row, List<TranscriptEntry> transcript, AgentSession session)
    {
        var receipt = ParseReceipt(row.RulesReceiptJson);
        if (receipt is null || receipt.Generation != session.GrokRulesGeneration) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (row.DeliveryAttempts > 0)
        {
            var prompt = transcript.FirstOrDefault(e => e.Kind == TranscriptKinds.UserPrompt && e.Text is not null
                && e.Sequence > (row.LastDeliveryBaselineSequence ?? session.GrokRulesLaunchTranscriptFloor ?? 0)
                && e.Text.StartsWith(Header(row.Id), StringComparison.Ordinal)
                && PromptSubmissionMatch.IsCompleteIn(row.Body, e.Text));
            if (prompt is not null)
            {
                row.Status = QueuedMessageStatus.Sent;
                row.DeliveryVerdict = DeliveryVerdict.LateConfirmed;
                row.RulesPromptSequence = prompt.Sequence;
                var next = transcript.FirstOrDefault(e => e.Kind == TranscriptKinds.UserPrompt && e.Sequence > prompt.Sequence)?.Sequence ?? long.MaxValue;
                var turn = transcript.Where(e => e.Sequence > prompt.Sequence && e.Sequence < next).ToList();
                var end = turn.FirstOrDefault(e => e.Kind == TranscriptKinds.TurnEnd);
                if (turn.Any(e => e.IsApiError == true)) { Fail(session, row, "provider_error"); return; }
                var answer = string.Join("\n", turn.Where(e => e.Kind == TranscriptKinds.AssistantText).Select(e => e.Text));
                var failure = $"ANTIPHON_RULES_FAILED id={row.Id:N} generation={receipt.Generation:N} reason=";
                var lines = StandaloneLines(answer).ToList();
                var reason = lines.FirstOrDefault(l => l.StartsWith(failure, StringComparison.Ordinal))?[failure.Length..];
                if (reason is "unreadable" or "incomplete" or "revision_mismatch") { Fail(session, row, reason); return; }
                var ack = $"ANTIPHON_RULES_ACK id={row.Id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}";
                if (end is not null && TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason) && lines.Contains(ack, StringComparer.Ordinal))
                {
                    row.RulesAcknowledgedAt = now;
                    row.RulesTurnEndSequence = end.Sequence;
                    return;
                }
                using var graceScope = scopes.CreateScope();
                var grace = graceScope.ServiceProvider.GetService<IOptions<Antiphon.Server.Application.Settings.DelegationSettings>>()
                    ?.Value.FinalMessageGraceSeconds ?? 120;
                if (end is not null && now >= end.CreatedAt.AddSeconds(grace)) { Fail(session, row, "missing_ack"); return; }
            }
            else
            {
                using var attemptScope = scopes.CreateScope();
                var ceiling = attemptScope.ServiceProvider.GetService<IOptions<Antiphon.Server.Application.Settings.SupervisionSettings>>()
                    ?.Value.DeliveryVerification.MaxDeliveryAttempts ?? 3;
                if (row.Status == QueuedMessageStatus.Pending && row.DeliveryVerdict is not null
                    && row.DeliveryAttempts >= Math.Max(1, ceiling)) { Fail(session, row, "delivery_failed"); return; }
            }
        }
        if (row.RulesDeadlineAt is { } deadline && now >= deadline) Fail(session, row, "timeout");
    }

    internal static IEnumerable<string> StandaloneLines(string text)
    {
        var fenced = false;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            { fenced = !fenced; continue; }
            if (!fenced && !line.StartsWith('>') && !line.StartsWith(' ') && !line.StartsWith('\t')) yield return line;
        }
    }

    private static void Fail(AgentSession session, SessionQueuedMessage row, string reason)
    {
        row.RulesFailure = reason;
        session.GrokRulesState = GrokRulesState.Failed;
        var code = row.RulesRefreshKey!.StartsWith("launch:", StringComparison.Ordinal)
            ? "grok_rules_initialization_failed" : "grok_rules_refresh_failed";
        session.GrokRulesFailure = $"{code}: {reason}";
    }
}
