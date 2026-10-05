using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>The result of one pass over durable channel-reply correlations.</summary>
public enum ChannelReplyDispatchOutcome
{
    NotPublished,
    Published,
    Deferred,
    IntentionallyWithheld,
    PublicationFailed,
}

/// <summary>
/// Correlation-level outcome data for a dispatcher pass. The runtime uses this only when a queue
/// boundary late-confirms a Channel row after the normal dispatcher-first pass.
/// </summary>
public sealed record ChannelReplyDispatchResult(
    IReadOnlySet<Guid> PublishedCorrelationIds,
    IReadOnlySet<Guid> IntentionallyWithheldCorrelationIds,
    IReadOnlySet<Guid> FailedCorrelationIds,
    IReadOnlySet<Guid>? DeferredCorrelationIds = null)
{
    public static ChannelReplyDispatchResult Empty { get; } = new(
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    public ChannelReplyDispatchOutcome OutcomeFor(Guid correlationId) =>
        PublishedCorrelationIds.Contains(correlationId)
            ? ChannelReplyDispatchOutcome.Published
            : DeferredCorrelationIds?.Contains(correlationId) == true
                ? ChannelReplyDispatchOutcome.Deferred
            : IntentionallyWithheldCorrelationIds.Contains(correlationId)
                ? ChannelReplyDispatchOutcome.IntentionallyWithheld
                : FailedCorrelationIds.Contains(correlationId)
                    ? ChannelReplyDispatchOutcome.PublicationFailed
                    : ChannelReplyDispatchOutcome.NotPublished;
}

/// <summary>
/// Routes an agent's turn output back down the external channel that asked for it. When a session
/// completes a turn (<c>TurnEnd</c>/<c>end_turn</c>, observed by <see cref="AgentSessionRuntime"/>),
/// this dispatcher matches the turn's prompt (<c>UserPrompt</c> or <c>QueuedUserPrompt</c>) back to
/// the channel message that started it.
/// extracts the assistant's text for that turn, classifies it (final answer vs question — see
/// <see cref="ChannelReplyKind"/>; Progress is reserved for future mid-turn notes), and produces a
/// <see cref="ChannelReply"/> to the outbound topic.
///
/// <para><b>ONE STORE (CARD-0067).</b> There is no correlation map. The reply target is resolved at
/// dispatch time from the <see cref="SessionQueuedMessage"/> row the bridge already persisted for
/// the inbound half — <see cref="SessionQueuedMessage.Body"/> is the prompt to match,
/// <see cref="SessionQueuedMessage.ConversationKey"/> is <c>{provider}:{conversationId}</c>, and
/// <see cref="SessionQueuedMessage.ChannelReplySettledAt"/> is the consume marker. Until 2026-08-17
/// the two halves of one round trip lived in two stores, one durable and one not: the bridge called
/// <c>Track()</c> into a <see cref="ConcurrentDictionary"/> immediately before persisting the queued
/// row, so any restart in between voided the reply. A hard restart at 09:05:01Z that day killed four
/// live correlations and the Family agent's guest list — 42 people, then 64 — was emitted twice and
/// published never, with no log line and no incident anywhere.</para>
///
/// <para><b>NO SILENT LOSS.</b> Every channel correlation now ends in exactly one of two states: a
/// published reply, or a Critical <see cref="AgentIncidentKind.ChannelReplyLost"/> incident when it
/// is abandoned unanswered (TTL expiry, or a conversation key nothing can be routed to). The
/// abandon sweep runs both per-session on turn end and globally via
/// <see cref="SweepStaleCorrelationsAsync"/>, because a session that answers into a void may never
/// end another turn to be swept on.</para>
///
/// Singleton. Prompt-matching (not blind FIFO) means a turn a human triggered directly in the
/// terminal never sends a stray reply to the chat.
/// </summary>
public sealed class ChannelReplyDispatcher
{
    private sealed record ReplyTarget(string Provider, string? ReplyHandle, string ConversationId,
        IReadOnlyList<Guid> CorrelationIds, bool HasCatalog);

    /// <summary>Why a correlation was abandoned without an answer. All are Critical incidents.</summary>
    private enum LossReason
    {
        /// <summary>No matching UserPrompt/QueuedUserPrompt after SentAt inside <c>PendingReplyTtlMinutes</c>.</summary>
        StaleTtl,

        /// <summary>The turn WAS answered, but the stored conversation key names no routable target.</summary>
        Unroutable,

        /// <summary>A matching prompt was recorded but no TurnEnd (and no AssistantText) followed it.</summary>
        TurnIncomplete,

        /// <summary>A matching prompt, TurnEnd and AssistantText exist, but dispatch never settled the row.</summary>
        TurnUnmatched,

        /// <summary>
        /// CARD-0281: a terminal provider-capacity/auth wall killed the turn. Raised now, not at TTL.
        /// </summary>
        ProviderCapacity,

        /// <summary>
        /// CARD-0360: the model endpoint was unreachable (transport-class death). Raised now,
        /// not at TTL. Pages; unlike ProviderCapacity there is no hold incident.
        /// </summary>
        ProviderTransport,

        /// <summary>
        /// CARD-0360: Transient/Unknown recovery resolved terminally (parked or exhausted).
        /// Raised now, not at TTL.
        /// </summary>
        ProviderError,
    }

    /// <summary>
    /// Prefix of the targeted send to the originating conversation when a correlation is abandoned
    /// (CARD-0233 / CARD-0171). Distinct from an agent reply so tests (and humans) can tell them apart.
    /// </summary>
    internal const string LostReplyNoticePrefix =
        "[Antiphon] A reply this chat was owed was never delivered:";

    // Legacy/default-off and catalog-less routing only. Claude can write AssistantText AFTER the
    // TurnEnd that triggered dispatch (observed live 2026-07-29, AZ Care: TurnEnd, AssistantText,
    // TurnEnd — the first dispatch consumed the correlations with only the turn's interim narration
    // extracted, and the real answer landed one second later with nothing left to match). Remembering
    // the dispatched turn's watermark lets that trailing text go out as a follow-up reply. Record
    // equality (value-compared watermark, reference-compared targets) makes TryUpdate an atomic
    // claim, so racing triggers can't double-send.
    private readonly ConcurrentDictionary<Guid, DispatchedTurn> _dispatched = new();

    private sealed record DispatchedTurn(long PromptSeq, long MaxTextSeq, IReadOnlyList<ReplyTarget> Targets);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Settings.ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelReplyDispatcher> _logger;
    private readonly TimeSpan _correlationTolerance;

    public ChannelReplyDispatcher(
        IServiceScopeFactory scopeFactory,
        IAntiphonMessagingProducer producer,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelReplyDispatcher> logger,
        IOptions<Settings.SupervisionSettings>? supervision = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _correlationTolerance = TimeSpan.FromSeconds(Math.Max(0,
            (supervision?.Value ?? new Settings.SupervisionSettings()).DeliveryVerification
                .UnobservableBaselineConfirmClockToleranceSeconds));
    }

    /// <summary>
    /// Channel correlations still owed a reply on this session (test/diagnostic surface). Async
    /// because the correlations live in Postgres now — the whole point of CARD-0067.
    /// </summary>
    public async Task<int> PendingCountAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await OpenCorrelations(db).CountAsync(m => m.AgentSessionId == sessionId, ct);
    }

    /// <summary>
    /// A channel message that has reached the agent and is still owed a reply.
    ///
    /// <para><see cref="QueuedMessageStatus.Sent"/> is deliberate: a correlation becomes owed only
    /// once the agent has actually been handed the message (CARD-0055 makes Sent mean a matching
    /// <c>UserPrompt</c> transcript record exists). A row still Pending, or Canceled, is the INBOUND
    /// path's problem — the delivery verification, parking and incidents of CARD-0055 already own
    /// it, and reporting it here as a lost reply would double-count the same silence.</para>
    /// </summary>
    private static IQueryable<SessionQueuedMessage> OpenCorrelations(AppDbContext db) =>
        db.SessionQueuedMessages
            .Where(m => m.Origin == QueuedMessageOrigin.Channel
                && m.Status == QueuedMessageStatus.Sent
                && m.ConversationKey != null
                && m.ChannelReplySettledAt == null
                && m.ChannelOutboundDeliveryId == null);

    /// <summary>
    /// Called on every completed turn. Cheap for sessions with no channel correlations (one indexed
    /// count), and the only path that can turn an agent's turn into a chat reply.
    /// </summary>
    public async Task<ChannelReplyDispatchResult> OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        var result = ChannelReplyDispatchResult.Empty;
        try
        {
            result = await DispatchAsync(sessionId, null, ct);

            // Trailing text for an already-answered turn (stop marker mid-stream) goes out as a
            // follow-up. No-op unless this session's last dispatched turn is still the live one.
            if (_dispatched.ContainsKey(sessionId))
                await DispatchFollowUpAsync(sessionId, ct);

            await DispatchDurableFollowUpsAsync(sessionId, ct);

            // CARD-0250 / CARD-0338: a later machine-triggered turn (task-done / check-in /
            // scheduled / system note) still reaches the last-known conversation after the ack
            // turn settled the Channel correlation. Attachments always; plain text for origins
            // in MachineTurnTextOrigins. Additive; never matches or settles Channel-origin rows.
            // The main path always wins first claim on the turn.
            await DispatchMachineTurnFollowUpAsync(sessionId, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to dispatch channel reply for session {SessionId}", sessionId);
        }

        return result;
    }

    /// <summary>
    /// Examine one bounded historical prompt page for an open source. The returned sequence
    /// is only a scheduling hint; source linkage in CaptureAsync remains the durable owner.
    /// Unlike OnTurnEndAsync this never selects the newest turn or changes the trailing cache.
    /// </summary>
    internal async Task<long?> DiscoverSourceAsync(SessionQueuedMessage source, long afterPrompt,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().UnifiedRecoveryEnabled)
            return null;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == source.AgentSessionId && t.Sequence > afterPrompt
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence).Take(ChannelOutboundDiscoveryService.PageSize).ToListAsync(ct);
        foreach (var prompt in prompts)
        {
            if (prompt.Text is null)
                continue;
            var matched = source.Origin == QueuedMessageOrigin.Channel
                ? (await MatchChannelRowsAsync(db, prompt, ct)).Any(m => m.Id == source.Id)
                : ChannelPromptCorrelation.MatchesMachineDelivery(source, prompt, _correlationTolerance);
            if (!matched)
                continue;
            var next = await TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync(
                db, source.AgentSessionId, prompt.Sequence, ct);
            var end = await db.TranscriptEntries.Where(t => t.AgentSessionId == source.AgentSessionId
                && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > prompt.Sequence
                && (next == null || t.Sequence < next)).OrderBy(t => t.Sequence)
                .Select(t => (long?)t.Sequence).FirstOrDefaultAsync(ct);
            if (end is null || (await TranscriptTurnWindow.FindOwningPromptAsync(
                db, source.AgentSessionId, end.Value, ct))?.Id != prompt.Id)
                continue;
            if (source.Origin == QueuedMessageOrigin.Channel)
                await DispatchAsync(source.AgentSessionId, prompt, ct);
            else
            {
                await DispatchMachineTurnFollowUpAsync(source.AgentSessionId, prompt, ct);
                if (next is not null)
                {
                    var (text, _, apiError) = await ExtractTurnResponseAsync(db, source.AgentSessionId, prompt.Sequence, ct);
                    var (_, paths) = ChannelContracts.ExtractAttachments(text ?? "");
                    var (_, tasks) = await CollectImpliedAttachmentsAsync(db, [source], ct, describeOnly: true);
                    // Only conclusive policy silence closes a machine source. Failed routing,
                    // missing text and API withholding still need later recovery/loss handling.
                    var ineligible = !apiError && !string.IsNullOrWhiteSpace(text)
                        && paths.Count == 0 && (ChannelContracts.IsNoReply(text)
                            || tasks.Count == 0 && !AdmitsMachineTurnText([source]));
                    if (ineligible)
                        await db.SessionQueuedMessages.Where(m => m.Id == source.Id
                            && m.ChannelOutboundDeliveryId == null && m.ChannelReplySettledAt == null
                            && m.ChannelReplyDiscoveryClosedAt == null)
                            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelReplyDiscoveryClosedAt,
                                _timeProvider.GetUtcNow().UtcDateTime), ct);
                }
            }
            if (await db.SessionQueuedMessages.AsNoTracking().AnyAsync(m => m.Id == source.Id
                && (m.ChannelOutboundDeliveryId != null || m.ChannelReplySettledAt != null
                    || m.ChannelReplyDiscoveryClosedAt != null), ct))
                return null;
            // A later complete receipt can own a resumed answer. Do not let an earlier
            // withheld attempt hide it. On wrap we still revisit late text in the old window.
        }
        return prompts.Count == ChannelOutboundDiscoveryService.PageSize ? prompts[^1].Sequence : null;
    }

    /// <summary>
    /// The global abandon sweep, for the periodic supervision tick. The per-session sweep inside
    /// <see cref="DispatchAsync"/> only ever runs when that session ends ANOTHER turn — and the
    /// 2026-08-17 shape is precisely a session that answered into a void, so a correlation on a
    /// session that goes quiet must not depend on it to be reported.
    /// </summary>
    public async Task<int> SweepStaleCorrelationsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await AbandonStaleCorrelationsAsync(db, sessionId: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Channel reply correlation sweep failed");
            return 0;
        }
    }

    private async Task<ChannelReplyDispatchResult> DispatchAsync(Guid sessionId,
        TranscriptEntry? historicalPrompt, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (historicalPrompt is null)
            await AbandonStaleCorrelationsAsync(db, sessionId, ct);

        var open = await OpenCorrelations(db)
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        if (open.Count == 0)
            return ChannelReplyDispatchResult.Empty;

        // The turn that just finished: latest TurnEnd, its owning prompt, and the assistant text
        // in between. CARD-0154: a channel message typed into a busy composer lands as
        // QueuedUserPrompt with no accompanying UserPrompt, so that kind is still a legal opener.
        // CARD-0233: ranking is no longer "latest of either kind before latest TurnEnd" — a
        // mid-turn QueuedUserPrompt (fresh-session bootstrap typed Mode.Now into a working
        // composer) used to steal the UserPrompt that actually opened the turn. Owning prompt is
        // the latest UserPrompt in (prevTurnEnd, thisTurnEnd), falling back to QueuedUserPrompt
        // only when that window has none. Sequence, never Timestamp (CARD-0068).
        var turnEndSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (turnEndSeq is not long endSeq)
        {
            // Not silence any more: a channel-bound session with owed correlations and NO TurnEnd row
            // is either pre-first-turn or missing its transcript entirely (CARD-0006's bind failure,
            // which has its own incident). Either way the correlations stay owed and the TTL reports.
            _logger.LogDebug(
                "Session {SessionId} owes {Count} channel reply/replies but its transcript has no TurnEnd yet",
                sessionId, open.Count);
            return ChannelReplyDispatchResult.Empty;
        }

        var userPrompt = historicalPrompt
            ?? await TranscriptTurnWindow.FindOwningPromptAsync(db, sessionId, endSeq, ct);
        if (userPrompt?.Text is not string promptText)
        {
            _logger.LogDebug(
                "Session {SessionId} owes {Count} channel reply/replies but the turn ending at seq {EndSeq} "
                + "has no preceding UserPrompt or QueuedUserPrompt to match",
                sessionId, open.Count, endSeq);
            return ChannelReplyDispatchResult.Empty;
        }

        if (await GrokRulesRefreshService.IsRefreshPromptAsync(db, sessionId, promptText, ct))
            return ChannelReplyDispatchResult.Empty;

        // Extract the response BEFORE consuming any correlations: Claude sometimes writes the
        // turn's stop marker before its reply text (observed live 2026-07-24: TurnEnd seq N,
        // AssistantText seq N+1) — consuming on a text-less TurnEnd loses the reply forever.
        // With no text yet the correlations stay pending; the AssistantText that follows (or a
        // later TurnEnd) re-triggers dispatch, and genuinely silent turns' correlations age out
        // via the TTL.
        var (responseText, maxTextSeq, containsApiErrorStub) =
            await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, ct);

        // S2 (CARD-0071): a turn killed by the API must never be published as a chat reply. The
        // stub's error string is ordinary AssistantText, so without this check "API Error: 529
        // Overloaded" would go to a family chat — and, because settling happens before the produce,
        // publishing it would also CONSUME the correlation and cancel the genuine answer. The whole
        // turn is withheld, not just the stub line stripped: a multi-call turn can produce real text
        // before a later API call dies, and publishing the fragment would settle the correlation
        // against half an answer. The correlations stay owed — a resumed turn's real answer routes
        // by the same stored prompt match, and if nothing ever answers, the TTL sweep's Critical
        // ChannelReplyLost incident is the designed backstop (no second timeout here).
        if (containsApiErrorStub)
        {
            var withheld = historicalPrompt is null ? open
                : open.Where(m => ChannelPromptCorrelation.Matches(m, userPrompt, _correlationTolerance, out _)).ToList();
            await HandleApiErrorWithholdAsync(
                db, sessionId, userPrompt.Sequence, withheld, ct);
            return new ChannelReplyDispatchResult(
                new HashSet<Guid>(), withheld.Select(m => m.Id).ToHashSet(), new HashSet<Guid>());
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogDebug(
                "Turn on session {SessionId} has no assistant text yet; correlations stay pending", sessionId);
            return ChannelReplyDispatchResult.Empty;
        }

        // Only answer turns WE started: match the turn's prompt against the queued bodies still owed
        // a reply. A human typing directly into the terminal produces turns that match nothing and
        // are skipped. A BATCHED turn (several queued channel messages coalesced into one body)
        // matches — and settles — every constituent row by containment, exactly as the in-memory
        // queue did: the row's Body IS the string the bridge enqueued and the queue typed.
        var matchedIds = (await MatchChannelRowsAsync(db, userPrompt, ct)).Select(m => m.Id).ToHashSet();
        var matches = open.Where(m => matchedIds.Contains(m.Id)).ToList();
        if (matches.Count == 0)
        {
            // The 2026-08-17 hole. This used to be a bare `return` — the one place that knew a
            // channel-bound agent had just finished a turn and answered nobody. It is a legitimate
            // state (the operator typed into the terminal while a chat message was still in flight),
            // so it is not an incident: the correlations stay owed and the TTL sweep raises the
            // Critical incident if nothing ever answers them. But it is never again invisible.
            _logger.LogWarning(
                "Turn on session {SessionId} (prompt seq {PromptSeq}) matched NONE of the {Count} channel "
                + "correlation(s) still owed a reply for {Conversations}. They stay owed; if no turn ever "
                + "matches them the TTL sweep raises a ChannelReplyLost incident.",
                sessionId, userPrompt.Sequence, open.Count, DescribeConversations(open));
            return ChannelReplyDispatchResult.Empty;
        }

        // Freeze the native handle from the inbound that caused this turn. The catalog handle may
        // already name a newer Slack thread when an earlier answer is finally published.
        var unified = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().UnifiedRecoveryEnabled;
        var targets = new List<ReplyTarget>();
        var unroutable = new List<SessionQueuedMessage>();
        foreach (var group in matches.GroupBy(m => m.ConversationKey!, StringComparer.Ordinal))
        {
            if (!TrySplitConversationKey(group.Key, out var provider, out var conversationId))
            {
                unroutable.AddRange(group);
                continue;
            }

            var channel = await db.ChatChannels.AsNoTracking()
                .Where(c => c.Provider == provider && c.ExternalId == conversationId)
                .Select(c => new { c.ReplyHandle })
                .FirstOrDefaultAsync(ct);
            if (channel is null && unified)
            {
                unroutable.AddRange(group);
                continue;
            }
            if (channel is null)
            {
                _logger.LogWarning(
                    "No channel catalog row for {ConversationKey} (session {SessionId}); addressing the reply "
                    + "by conversation id alone", group.Key, sessionId);
            }

            var replyHandle = await ResolveInboundReplyHandleAsync(db, group, channel?.ReplyHandle, ct);
            targets.Add(new ReplyTarget(provider, replyHandle, conversationId,
                group.Select(m => m.Id).ToArray(), channel is not null));
        }

        var failed = unroutable.Select(m => m.Id).ToHashSet();
        if (unroutable.Count > 0)
        {
            // The turn was answered and there is nowhere to send it. That is a lost reply, not a
            // skip, so it settles with a Critical incident rather than being retried forever.
            await ReportLostAsync(sessionId, unroutable, LossReason.Unroutable, ct);
            matches = matches.Except(unroutable).ToList();
            if (matches.Count == 0)
                return new ChannelReplyDispatchResult(
                    new HashSet<Guid>(), new HashSet<Guid>(), failed);
        }

        var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        // Only legacy and catalog-less routes need the process-local watermark. Captured
        // destinations always derive trailing ownership from their committed root.
        if (historicalPrompt is null)
        {
            var legacyTargets = targets.Where(t => !outbound.UnifiedRecoveryEnabled || !t.HasCatalog).ToArray();
            if (legacyTargets.Length > 0)
                _dispatched[sessionId] = new DispatchedTurn(userPrompt.Sequence, maxTextSeq, legacyTargets);
        }

        // The frozen silent-turn contract: a whole-turn NO_REPLY settles the correlations and
        // sends nothing — system notes and housekeeping turns must never spam the chat.
        if (ChannelContracts.IsNoReply(responseText))
        {
            if (outbound.UnifiedRecoveryEnabled)
            {
                foreach (var target in targets.Where(t => t.HasCatalog))
                    await outbound.CaptureAsync(new ChannelReply { Channel = target.Provider,
                            ConversationId = target.ConversationId, ReplyHandle = target.ReplyHandle },
                        new ChannelOutboundSource(sessionId, userPrompt.Sequence, userPrompt.Sequence + 1,
                            maxTextSeq, "main", target.CorrelationIds),
                        ChannelReplyPreparation.Describe(responseText), _settings, ct, suppress: true);
                await SettleAsync(db, matches.Where(m => targets.Any(t => !t.HasCatalog
                    && t.CorrelationIds.Contains(m.Id))).ToList(), ct);
            }
            else
                await SettleAsync(db, matches, ct);
            _logger.LogInformation(
                "Silent turn (NO_REPLY) on session {SessionId}; {Count} correlation(s) settled without a reply",
                sessionId, matches.Count);
            return new ChannelReplyDispatchResult(
                new HashSet<Guid>(), matches.Select(m => m.Id).ToHashSet(), failed);
        }

        var descriptor = ChannelReplyPreparation.Describe(responseText) with { MaxTextChars = _settings.MaxReplyChars };
        var (bodyText, attachments) = outbound.UnifiedRecoveryEnabled
            ? (descriptor.Text, new List<OutboundAttachment>())
            : PrepareReplyBody(responseText, sessionId);

        // One reply per distinct conversation. With same-conversation batching this loop is
        // degenerate (exactly one send) — the fan-out is a deliberate latent safety net in case
        // batching scope ever widens to cross-conversation.
        var published = new HashSet<Guid>();
        var deferred = new HashSet<Guid>();
        var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        foreach (var target in targets)
        {
            try
            {
                // A conversation-id route without a catalog cannot own a captured delivery.
                // Keep its existing preparation and direct publication contract when enabled.
                var captureReply = outbound.UnifiedRecoveryEnabled && target.HasCatalog;
                var (targetBody, targetAttachments) = outbound.UnifiedRecoveryEnabled && !captureReply
                    ? PrepareReplyBody(responseText, sessionId) : (bodyText, attachments);
                var targetText = Truncate(targetBody);
                var reply = new ChannelReply
                {
                    Channel = target.Provider,
                    ReplyHandle = target.ReplyHandle,
                    ConversationId = target.ConversationId,
                    Text = targetText.Length == 0 ? null : targetText,
                    Kind = ClassifyKind(targetBody),
                    Attachments = targetAttachments,
                };
                var targetRows = matches.Where(m => target.CorrelationIds.Contains(m.Id)).ToList();
                ChannelOutboundSendOutcome outcome;
                var source = new ChannelOutboundSource(sessionId, userPrompt.Sequence,
                    userPrompt.Sequence + 1, maxTextSeq, "main", target.CorrelationIds,
                    targetRows.Select(m => m.SourceTaskId).FirstOrDefault(id => id.HasValue));
                if (captureReply)
                {
                    var delivery = await outbound.CaptureAsync(reply, source, descriptor, _settings, ct);
                    outcome = delivery.State == ChannelOutboundDeliveryState.Published
                        ? ChannelOutboundSendOutcome.Published : ChannelOutboundSendOutcome.Deferred;
                }
                else
                {
                    outcome = await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply, source, ct);
                }
                if (outcome == ChannelOutboundSendOutcome.Published)
                {
                    published.UnionWith(target.CorrelationIds);
                    try { await channels.StampLastReplyAsync(target.Provider, target.ConversationId, targetText, ct); }
                    catch (Exception stampError) when (stampError is not OperationCanceledException)
                    {
                        _logger.LogWarning(stampError,
                            "Published reply to {ConversationId} but could not update its catalog preview",
                            target.ConversationId);
                    }
                }
                else
                    deferred.UnionWith(target.CorrelationIds);
                _logger.LogInformation(
                    "{Outcome} {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) to {Provider} conversation {ConversationId} from session {SessionId}",
                    outcome, reply.Kind, targetText.Length, targetAttachments.Count, target.Provider, target.ConversationId, sessionId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _dispatched.TryRemove(sessionId, out _);
                if (!outbound.UnifiedRecoveryEnabled)
                {
                    foreach (var m in matches.Where(m => target.CorrelationIds.Contains(m.Id)))
                        m.ChannelReplySettledAt = null;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                _logger.LogError(ex,
                    "Producing the channel reply for session {SessionId} failed; target {ConversationId} remains owed",
                    sessionId, target.ConversationId);
                failed.UnionWith(target.CorrelationIds);
            }
        }

        return new ChannelReplyDispatchResult(
            published,
            new HashSet<Guid>(),
            failed,
            deferred);
    }

    /// <summary>
    /// CARD-0281 / CARD-0071 / CARD-0360: a turn killed by the API is never published as a
    /// chat reply. Retryable non-transport classes stay owed for a resumed turn. Terminal
    /// classes — and a transport-class death, which is terminal for the <em>correlation</em>
    /// even while the session ladder stays Transient — send one class-appropriate notice,
    /// settle the rows, and raise ChannelReplyLost now so the TTL sweep cannot send the
    /// contradictory "no turn completed" sentence.
    /// </summary>
    private async Task HandleApiErrorWithholdAsync(
        AppDbContext db,
        Guid sessionId,
        long promptSeq,
        IReadOnlyList<SessionQueuedMessage> open,
        CancellationToken ct)
    {
        var found = await FindApiErrorStubAsync(db, sessionId, promptSeq, ct);
        var stub = found?.Stub;
        var stubText = found?.Text;
        ApiErrorRecovery? recovery = null;
        if (stub is not null)
        {
            await using var recoveryScope = _scopeFactory.CreateAsyncScope();
            var recoveryService = recoveryScope.ServiceProvider.GetService<ApiErrorRecoveryService>();
            if (recoveryService is not null)
            {
                recovery = await recoveryService.EnsureAdoptedAsync(
                    sessionId, stub.Sequence, stub.Uuid, stub.ApiErrorClass, stub.ApiErrorStatus,
                    stubText, ct, raiseIncident: true);
            }
        }

        var isTransport = string.Equals(
            stub?.ApiErrorClass, TranscriptKinds.ApiErrorClasses.Transport, StringComparison.Ordinal);

        if (!isTransport && (recovery is null || recovery.ResolvedAt is null))
        {
            _logger.LogWarning(
                "Turn on session {SessionId} (prompt seq {PromptSeq}) was killed by an API error; withholding "
                + "the channel reply for the whole turn. {Count} correlation(s) stay owed for a resumed turn, "
                + "with the TTL sweep as the backstop.",
                sessionId, promptSeq, open.Count);
            return;
        }

        var session = await db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        var alias = ModelAlias.Normalize(kind, session?.EffectiveModelId)
            ?? ModelLevelAliases.For(kind, AgentModelLevel.High);

        LossReason lossReason;
        string notice;
        if (isTransport)
        {
            notice = ProviderCapacityNotice.FormatTransport(kind, alias, retryCount: 0, stubText);
            lossReason = LossReason.ProviderTransport;
        }
        else if (IsCapacityTerminal(recovery?.ResolvedReason))
        {
            notice = ProviderCapacityNotice.Format(
                kind,
                alias,
                stub?.ApiErrorStatus ?? recovery?.ApiErrorStatus,
                ReasonPhraseOf(stub?.ApiErrorClass ?? recovery?.ApiErrorClass),
                fallbackDeclared: false,
                detail: stubText ?? recovery?.ApiErrorClass);
            lossReason = LossReason.ProviderCapacity;
        }
        else
        {
            notice = ProviderCapacityNotice.FormatProviderError(
                kind,
                alias,
                stub?.ApiErrorStatus ?? recovery?.ApiErrorStatus,
                ReasonPhraseOf(stub?.ApiErrorClass ?? recovery?.ApiErrorClass),
                stubText ?? recovery?.ApiErrorClass);
            lossReason = LossReason.ProviderError;
        }

        var recorded = await ReportLostAsync(
            sessionId, open, lossReason, ct,
            apiErrorClass: stub?.ApiErrorClass ?? recovery?.ApiErrorClass,
            apiErrorStatus: stub?.ApiErrorStatus ?? recovery?.ApiErrorStatus);
        if (recorded) await NotifyCapacityAsync(db, open, notice, ct);
        _logger.LogWarning(
            "Turn on session {SessionId} (prompt seq {PromptSeq}) died on a terminal API error "
            + "({Reason}); {Count} correlation(s) settled with a {Loss} notice.",
            sessionId, promptSeq, recovery?.ResolvedReason ?? "transport", open.Count, lossReason);
    }

    private static bool IsCapacityTerminal(string? resolvedReason) =>
        resolvedReason is ApiErrorRecoveryReasons.WallModelPaused
            or ApiErrorRecoveryReasons.WallParked
            or ApiErrorRecoveryReasons.NeedsHuman
            or ApiErrorRecoveryService.WallUnparsedFailureReason;

    private async Task NotifyCapacityAsync(
        AppDbContext db,
        IReadOnlyList<SessionQueuedMessage> open,
        string notice,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var channels = scope.ServiceProvider.GetService<ChatChannelService>();
        if (channels is null)
            return;

        foreach (var key in open
                     .Select(m => m.ConversationKey)
                     .Where(k => !string.IsNullOrWhiteSpace(k))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!TrySplitConversationKey(key!, out var provider, out var conversationId))
                continue;

            var channel = await db.ChatChannels.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Provider == provider && c.ExternalId == conversationId, ct);
            if (channel is null)
            {
                _logger.LogWarning(
                    "Provider-capacity notice not sent: no catalog row for {ConversationKey}", key);
                continue;
            }

            try
            {
                await channels.SendAsync(
                    channel.Id,
                    notice,
                    new ChannelSendOptions(ReplyHandle: channel.ReplyHandle),
                    ct);
            }
            catch (ConflictException ex) when (ex.Code == "channel_disabled")
            {
                _logger.LogWarning(
                    "Provider-capacity notice not sent: channel {ChannelId} ({ConversationKey}) is disabled",
                    channel.Id, key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Provider-capacity notice to {ConversationKey} failed", key);
            }
        }
    }

    private static string? ReasonPhraseOf(string? apiErrorClass)
    {
        if (string.IsNullOrWhiteSpace(apiErrorClass))
            return null;
        return apiErrorClass.Replace('_', ' ');
    }

    private static async Task<(TranscriptEntry Stub, string? Text)?> FindApiErrorStubAsync(
        AppDbContext db, Guid sessionId, long promptSeq, CancellationToken ct)
    {
        var nextPromptSeq = await TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync(
            db, sessionId, promptSeq, ct);
        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.IsApiError == true
                && t.Kind == TranscriptKinds.TurnEnd
                && t.Sequence > promptSeq);
        if (nextPromptSeq is long cap)
            query = query.Where(t => t.Sequence < cap);
        var stub = await query.OrderByDescending(t => t.Sequence).FirstOrDefaultAsync(ct);
        if (stub is null)
            return null;
        // CARD-0401: same-uuid AssistantText first; TurnEnd.Text only for Codex.
        var text = await ApiErrorStubText.ResolveAsync(db, stub.AgentSessionId, stub.Uuid, stub.Text, ct);
        return (stub, text);
    }

    /// <summary>The durable consume marker — see <see cref="SessionQueuedMessage.ChannelReplySettledAt"/>.</summary>
    private async Task SettleAsync(AppDbContext db, IReadOnlyList<SessionQueuedMessage> rows, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in rows)
            row.ChannelReplySettledAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary><c>{provider}:{conversationId}</c>; split on the FIRST colon (chat ids carry none).</summary>
    private static bool TrySplitConversationKey(
        string conversationKey, out string provider, out string conversationId)
    {
        provider = string.Empty;
        conversationId = string.Empty;
        var separator = conversationKey.IndexOf(':');
        if (separator <= 0 || separator == conversationKey.Length - 1)
            return false;

        provider = conversationKey[..separator];
        conversationId = conversationKey[(separator + 1)..];
        return true;
    }

    private static string DescribeConversations(IEnumerable<SessionQueuedMessage> rows) =>
        string.Join(", ", rows.Select(m => m.ConversationKey).Distinct(StringComparer.Ordinal));

    /// <summary>
    /// The TTL sweep, durable version of the old in-memory <c>EvictStale</c>. A correlation that ages
    /// past <c>PendingReplyTtlMinutes</c> with no matching turn is abandoned — and abandoning it is
    /// reported as a Critical incident, because the only reason a channel correlation exists is that a
    /// person is waiting on it. The clock runs from <see cref="SessionQueuedMessage.SentAt"/> (when the
    /// agent actually got the message), NOT from enqueue: a message that sat Pending behind a long
    /// turn would otherwise be stale the instant it was finally delivered.
    /// </summary>
    private async Task<int> AbandonStaleCorrelationsAsync(
        AppDbContext db, Guid? sessionId, CancellationToken ct)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_settings.PendingReplyTtlMinutes);
        var query = OpenCorrelations(db).Where(m => (m.SentAt ?? m.CreatedAt) < cutoff);
        if (sessionId is Guid scoped)
            query = query.Where(m => m.AgentSessionId == scoped);

        var stale = await query.OrderBy(m => m.Sequence).ToListAsync(ct);
        if (stale.Count == 0)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().UnifiedRecoveryEnabled)
        {
            var examined = new List<SessionQueuedMessage>();
            foreach (var source in stale)
            {
                // Never declare TTL loss before its historical answer has been examined.
                // A remaining page defers classification to discovery's bounded cursor work.
                if (await DiscoverSourceAsync(source, -1, ct) is not null) continue;
                if (await OpenCorrelations(db).AsNoTracking().AnyAsync(m => m.Id == source.Id, ct))
                    examined.Add(source);
            }
            stale = examined;
        }
        foreach (var bySession in stale.GroupBy(m => m.AgentSessionId))
        {
            var classified = new List<(SessionQueuedMessage Msg, LossReason Reason, TranscriptEntry? Prompt, int Chars)>();
            foreach (var m in bySession)
            {
                var (reason, prompt, chars) = await ClassifyTtlLossAsync(db, m, ct);
                classified.Add((m, reason, prompt, chars));
            }

            foreach (var byReason in classified.GroupBy(c => c.Reason))
            {
                var sample = byReason.First();
                await ReportLostAsync(
                    bySession.Key,
                    byReason.Select(c => c.Msg).ToList(),
                    sample.Reason,
                    ct,
                    unmatchedPrompt: sample.Prompt,
                    assistantChars: sample.Chars);
            }
        }

        return stale.Count;
    }

    /// <summary>
    /// CARD-0233: the TTL sweep used to always claim "no turn matching the message completed",
    /// which was a lie when the agent had answered and dispatch attributed the turn to the wrong
    /// prompt. Inspect the transcript before writing the incident.
    /// </summary>
    private async Task<(LossReason Reason, TranscriptEntry? Prompt, int AssistantChars)> ClassifyTtlLossAsync(
        AppDbContext db, SessionQueuedMessage message, CancellationToken ct)
    {
        var candidates = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == message.AgentSessionId
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence).ToListAsync(ct);
        TranscriptEntry? prompt = null;
        foreach (var candidate in candidates)
        {
            if (!ChannelPromptCorrelation.Matches(message, candidate, _correlationTolerance, out _))
                continue;
            if ((await MatchChannelRowsAsync(db, candidate, ct)).Any(m => m.Id == message.Id))
            {
                prompt = candidate;
                break;
            }
        }
        if (prompt is null)
            return (LossReason.StaleTtl, null, 0);

        var next = await TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync(
            db, message.AgentSessionId, prompt.Sequence, ct);
        var ends = await db.TranscriptEntries.Where(t => t.AgentSessionId == message.AgentSessionId
            && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > prompt.Sequence
            && (next == null || t.Sequence < next)).OrderBy(t => t.Sequence).ToListAsync(ct);
        var completed = false;
        foreach (var end in ends)
        {
            if ((await TranscriptTurnWindow.FindOwningPromptAsync(db, message.AgentSessionId, end.Sequence, ct))?.Id == prompt.Id)
            {
                completed = true;
                break;
            }
        }
        if (!completed)
            return (LossReason.TurnIncomplete, prompt, 0);

        var (text, _, apiError) = await ExtractTurnResponseAsync(db, message.AgentSessionId, prompt.Sequence, ct);
        if (apiError || string.IsNullOrWhiteSpace(text))
            return (LossReason.TurnIncomplete, prompt, 0);
        return (LossReason.TurnUnmatched, prompt, text.Length);
    }

    // Include settled rows: they still own their turn after a process restart. Limit candidates
    // to this session and the attempt window before looking at text, including in machine gate 2.
    private async Task<List<SessionQueuedMessage>> MatchChannelRowsAsync(
        AppDbContext db, TranscriptEntry prompt, CancellationToken ct)
    {
        var latestStart = prompt.Timestamp?.Add(_correlationTolerance);
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == prompt.AgentSessionId && m.Origin == QueuedMessageOrigin.Channel
                && m.Status == QueuedMessageStatus.Sent && m.DeliveryAttempts > 0
                && ((m.LastDeliveryBaselineSequence != null && m.LastDeliveryBaselineSequence < prompt.Sequence)
                    || (m.LastDeliveryBaselineSequence == null && latestStart != null
                        && (m.LastDeliveryStartedAt ?? m.SentAt) <= latestStart)))
            .ToListAsync(ct);
        var matches = new List<SessionQueuedMessage>();
        foreach (var row in rows)
        {
            if (ChannelPromptCorrelation.Matches(row, prompt, _correlationTolerance, out var reason))
                matches.Add(row);
            else
                _logger.LogDebug("Channel correlation {MessageId} on session {SessionId}: {Reason}; prompt {Sequence}, floor {Floor}",
                    row.Id, row.AgentSessionId, reason, prompt.Sequence, row.LastDeliveryBaselineSequence);
        }
        // A complete earlier Channel body can occur inside a genuine task/Check report. Its
        // marker and lower floor alone do not give that historical row ownership of this turn.
        // Require the outer machine delivery's own full receipt and attempt evidence. Keep this
        // decision here so main routing, TTL and machine gate 2 cannot disagree about ownership.
        if (matches.Count > 0 && !ChannelPromptCorrelation.HasMarkedTransportFrame(prompt.Text!))
        {
            var machines = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == prompt.AgentSessionId
                    && (m.Origin == QueuedMessageOrigin.Delegation || m.Origin == QueuedMessageOrigin.Check
                        || m.Origin == QueuedMessageOrigin.System || m.Origin == QueuedMessageOrigin.Scheduled)
                    && m.Status == QueuedMessageStatus.Sent && m.DeliveryAttempts > 0
                    && ((m.LastDeliveryBaselineSequence != null && m.LastDeliveryBaselineSequence < prompt.Sequence)
                        || (m.LastDeliveryBaselineSequence == null && latestStart != null
                            && m.LastDeliveryStartedAt <= latestStart)))
                .ToListAsync(ct);
            if (machines.Any(m => ChannelPromptCorrelation.MatchesMachineDelivery(m, prompt, _correlationTolerance)))
                return [];
        }
        var legacy = matches.Where(m => ChannelPromptCorrelation.OpeningMarker(m.Body) is null).ToList();
        if (legacy.Count > 1 && !legacy.All(m => ChannelPromptCorrelation.SameDeliveredBatch(legacy[0], m)))
        {
            _logger.LogWarning("Ambiguous legacy channel correlation on session {SessionId}, prompt {Sequence}: {Count} rows remain unclaimed",
                prompt.AgentSessionId, prompt.Sequence, legacy.Count);
            matches.RemoveAll(m => legacy.Contains(m));
        }
        return matches;
    }

    /// <summary>
    /// The end of silent loss. Logs at Error and records a Critical
    /// <see cref="AgentIncidentKind.ChannelReplyLost"/> incident on the agent that owns the session —
    /// which routes through the normal alert pipeline, i.e. it reaches a human. Always Critical: a
    /// channel correlation exists only because somebody in a chat asked something.
    ///
    /// <para>Settlement, incident and alert share one transaction in a fresh scope.
    /// Failure leaves the source owed; notices run only after commit.</para>
    /// </summary>
    private async Task<bool> ReportLostAsync(
        Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> lost,
        LossReason reason,
        CancellationToken ct,
        TranscriptEntry? unmatchedPrompt = null,
        int assistantChars = 0,
        string? apiErrorClass = null,
        int? apiErrorStatus = null)
    {
        var conversations = DescribeConversations(lost);
        var why = reason switch
        {
            LossReason.Unroutable =>
                $"the stored conversation key names no routable target ({conversations})",
            LossReason.TurnIncomplete =>
                $"a matching prompt was recorded but no turn completed within {_settings.PendingReplyTtlMinutes} minutes",
            LossReason.TurnUnmatched =>
                $"a turn completed (prompt seq {unmatchedPrompt?.Sequence}, {assistantChars} chars) but the dispatcher did not route it",
            LossReason.ProviderCapacity =>
                "the provider refused the request (capacity/auth wall)",
            LossReason.ProviderTransport =>
                "the model endpoint was unreachable (connection error)",
            LossReason.ProviderError =>
                $"the turn died on a provider error ({apiErrorClass ?? "unknown"}"
                + (apiErrorStatus is int s ? $", HTTP {s}" : "")
                + ") and no automatic resume answered",
            _ => $"no turn matching the message completed within {_settings.PendingReplyTtlMinutes} minutes",
        };
        var oldest = lost.Min(m => m.SentAt ?? m.CreatedAt);

        _logger.LogError(
            "CHANNEL REPLY LOST: {Count} message(s) from {Conversations} routed into session {SessionId} were "
            + "never answered — {Why}. Oldest owed since {Oldest:u}. Message ids: {MessageIds}. "
            + "A human asked and got silence.",
            lost.Count, conversations, sessionId, why, oldest,
            string.Join(", ", lost.Select(m => m.Id)));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recorder = new ChannelOutboundFailureRecorder(db, _timeProvider,
            scope.ServiceProvider.GetService<IEventBus>(), scope.ServiceProvider.GetService<IAlertRouter>());
        var message = $"A reply owed {conversations} was never sent because {why}.";
        if (reason == LossReason.TurnUnmatched && unmatchedPrompt?.Text is string promptBody)
            message += $" Unmatched prompt: \"{ColumnText.Clip(Normalize(ChannelPromptCorrelation.RemoveMarkers(promptBody)), 80)}\".";
        var cutoff = reason is LossReason.StaleTtl or LossReason.TurnIncomplete or LossReason.TurnUnmatched
            ? _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_settings.PendingReplyTtlMinutes) : (DateTime?)null;
        var recorded = await recorder.RecordSourcesAsync(sessionId, lost, reason.ToString(), message, cutoff, ct);
        if (recorded && reason is not LossReason.Unroutable
            and not LossReason.ProviderCapacity and not LossReason.ProviderTransport and not LossReason.ProviderError)
        {
            try { await NotifyOriginatingConversationsAsync(scope.ServiceProvider, lost, why, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lost reply notice failed after durable recording"); }
        }
        return recorded;
    }

    /// <summary>
    /// CARD-0171 shape: a targeted <see cref="ChatChannelService.SendAsync"/> to the conversation
    /// named by each lost row's <c>ConversationKey</c>. Not the alert-sink path.
    /// </summary>
    private async Task NotifyOriginatingConversationsAsync(
        IServiceProvider services,
        IReadOnlyList<SessionQueuedMessage> lost,
        string why,
        CancellationToken ct)
    {
        var channels = services.GetService<ChatChannelService>();
        if (channels is null)
            return;

        var db = services.GetRequiredService<AppDbContext>();
        var text = $"{LostReplyNoticePrefix} {why}.";
        foreach (var key in lost
                     .Select(m => m.ConversationKey)
                     .Where(k => !string.IsNullOrWhiteSpace(k))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!TrySplitConversationKey(key!, out var provider, out var conversationId))
                continue;

            var channel = await db.ChatChannels.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Provider == provider && c.ExternalId == conversationId, ct);
            if (channel is null)
            {
                _logger.LogWarning(
                    "ChannelReplyLost notice not sent: no catalog row for {ConversationKey}", key);
                continue;
            }

            try
            {
                await channels.SendAsync(
                    channel.Id,
                    text,
                    new ChannelSendOptions(ReplyHandle: channel.ReplyHandle),
                    ct);
            }
            catch (ConflictException ex) when (ex.Code == "channel_disabled")
            {
                _logger.LogWarning(
                    "ChannelReplyLost notice not sent: channel {ChannelId} ({ConversationKey}) is disabled",
                    channel.Id, key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ChannelReplyLost notice to {ConversationKey} failed", key);
            }
        }
    }

    /// <summary>
    /// Resolves the agent's <c>[[attach: path]]</c> markers into inline attachments: marker lines
    /// come out of the text, files are read from THIS host's disk (agent and server share the
    /// machine; the remote gateway cannot), and files that are missing or blow the raw budget are
    /// skipped with a note appended to the text so the agent's mistake is visible in the chat.
    /// </summary>
    private (string Text, IReadOnlyList<OutboundAttachment> Attachments) PrepareReplyBody(
        string responseText, Guid sessionId, IReadOnlyList<string>? extraPaths = null)
    {
        var descriptor = ChannelReplyPreparation.Describe(responseText, extraPaths);
        responseText = ChannelPromptCorrelation.RemoveMarkers(responseText);
        var text = descriptor.Text;
        var paths = descriptor.AttachmentPaths;

        if (paths.Count == 0)
            return (responseText, []);

        var attachments = new List<OutboundAttachment>();
        var notes = new List<string>();
        var budget = _settings.MaxAttachmentBytes; // cumulative: all attachments share one Kafka message

        foreach (var path in paths)
        {
            FileInfo file;
            try { file = new FileInfo(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                notes.Add($"⚠️ attachment path invalid: {path}");
                continue;
            }

            if (!file.Exists)
            {
                notes.Add($"⚠️ attachment not found: {path}");
                _logger.LogWarning("Session {SessionId} attach marker points at a missing file: {Path}", sessionId, path);
                continue;
            }
            if (file.Length > budget)
            {
                notes.Add($"⚠️ attachment skipped — {file.Name} is {file.Length / (1024 * 1024)} MB, over the {_settings.MaxAttachmentBytes / (1024 * 1024)} MB limit");
                _logger.LogWarning(
                    "Session {SessionId} attachment {Path} ({Bytes} bytes) exceeds the remaining budget {Budget}",
                    sessionId, path, file.Length, budget);
                continue;
            }

            attachments.Add(new OutboundAttachment
            {
                Kind = InferAttachmentKind(file.Extension),
                Source = file.FullName,
                Content = File.ReadAllBytes(file.FullName),
                Name = file.Name,
                Mime = InferMime(file.Extension),
            });
            budget -= file.Length;
        }

        if (notes.Count > 0)
            text = text.Length == 0 ? string.Join("\n", notes) : $"{text}\n\n{string.Join("\n", notes)}";
        return (text, attachments);
    }

    internal static AttachmentKind InferAttachmentKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => AttachmentKind.Image,
        ".mp4" or ".mov" or ".webm" => AttachmentKind.Video,
        ".mp3" or ".m4a" or ".wav" or ".flac" => AttachmentKind.Audio,
        ".ogg" or ".oga" => AttachmentKind.Voice,
        _ => AttachmentKind.File,
    };

    internal static string InferMime(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".html" or ".htm" => "text/html",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".txt" or ".md" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".zip" => "application/zip",
        ".mp4" => "video/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" or ".oga" => "audio/ogg",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };

    // A turn we already replied for can gain more AssistantText: Claude sometimes writes a stop
    // marker mid-stream, dispatch fires with only the text so far (e.g. interim narration between
    // tool calls), and the real answer follows seconds later. The correlations are settled by then, so
    // route the trailing text to the same targets. The watermark claim via TryUpdate keeps racing
    // triggers (AssistantText arrival + the closing TurnEnd) from double-sending.
    //
    // Unified catalog-backed routes reserve from the committed delivery root instead. The
    // process-local watermark below serves only legacy/default-off and catalog-less sends.
    //
    // Trailing text is attributed by the same sequence window ExtractTurnResponseAsync uses
    // (PromptSeq < seq < nextPromptSeq), lower-bounded at MaxTextSeq. A newer prompt is the
    // window's upper bound, not a reason to drop in-window text — CARD-0068, seq 469 on 2026-08-17
    // (the guest list) landed in the same PersistTranscriptAsync batch as seq 471 (the next
    // UserPrompt) and was discarded by the old "is PromptSeq still the latest UserPrompt?" bail.
    private async Task DispatchDurableFollowUpsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().UnifiedRecoveryEnabled)
            return;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roots = await db.ChannelOutboundDeliveries.AsNoTracking().Where(d => d.SourceSessionId == sessionId
            && d.CaptureJson != null && d.RootDeliveryId == null && d.ReservedThroughSequence != null
            && d.TailClosedAt == null && (d.SendKind == "main" || d.SendKind == "machine"))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Take(ChannelOutboundDiscoveryService.PageSize)
            .Select(d => d.Id).ToListAsync(ct);
        foreach (var id in roots) await DiscoverRootAsync(id, ct);
    }

    /// <summary>Reserve a tail using durable routing and cursor; preparation belongs to the pump.</summary>
    internal async Task DiscoverRootAsync(Guid rootId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        if (!outbound.UnifiedRecoveryEnabled) return;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = await db.ChannelOutboundDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == rootId, ct);
        if (root is not { RootDeliveryId: null, CaptureJson: not null, TailClosedAt: null,
                ReservedThroughSequence: long cursor }) return;
        var (next, late) = await QueryTurnWindowAsync(db, root.SourceSessionId, root.PromptSequence, cursor, ct);
        var texts = late.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToArray();
        if (!late.Any(t => TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError)) && texts.Length > 0)
        {
            var joined = string.Join("\n\n", texts.Select(t => t.Text!)).Trim();
            var descriptor = ChannelReplyPreparation.Describe(joined) with { MaxTextChars = _settings.MaxReplyChars };
            var suppress = ChannelContracts.IsNoReply(joined);
            if (root.SendKind == "machine")
            {
                var members = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.ChannelOutboundDeliveryId == root.Id).ToListAsync(ct);
                // Plain trailing machine text retains the root's origin policy. Explicit
                // attachments remain eligible, and silent fragments still reserve their range.
                suppress |= descriptor.AttachmentPaths.Count == 0 && !AdmitsMachineTurnText(members);
                if (ChannelContracts.IsNoReply(descriptor.Text) && descriptor.AttachmentPaths.Count > 0)
                    descriptor = descriptor with { Text = "" };
            }
            var route = ChannelReplyPreparation.Deserialize(root.CaptureJson).Route
                with { Kind = ClassifyKind(descriptor.Text) };
            await outbound.CaptureAsync(route,
                new ChannelOutboundSource(root.SourceSessionId, root.PromptSequence, cursor + 1,
                    late[^1].Sequence, "trailing", []), descriptor, _settings, ct, root.Id, suppress: suppress);
            // A racing reservation may have won a shorter interval; only close after a new
            // examination of the committed cursor, so its remaining suffix is never dropped.
            root = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == rootId, ct);
            var (_, remaining) = await QueryTurnWindowAsync(db, root.SourceSessionId, root.PromptSequence,
                root.ReservedThroughSequence!.Value, ct);
            if (remaining.Count > 0) return;
        }
        if (next is not null) await outbound.CloseTailAsync(root, ct);
    }

    private async Task DispatchFollowUpAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_dispatched.TryGetValue(sessionId, out var turn))
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (nextPromptSeq, late) = await QueryTurnWindowAsync(
            db, sessionId, promptSeq: turn.PromptSeq, afterSeq: turn.MaxTextSeq, ct);

        // S2 (CARD-0071), same rule as the main path: an API-error stub in the trailing window
        // withholds the whole follow-up — the turn died mid-stream and its error string must not
        // reach the chat, not even beside real trailing text. The watermark is NOT advanced, which
        // is deliberate: nothing was sent, and a later prompt already capping the window drops
        // this record below (no future row can land in a sequence gap that already has a later
        // prompt).
        if (late.Any(l => TranscriptKinds.IsApiErrorStub(l.Kind, l.IsApiError)))
        {
            _logger.LogWarning(
                "Trailing text on session {SessionId} (dispatched prompt seq {PromptSeq}) contains an API-error "
                + "stub; withholding the follow-up reply.",
                sessionId, turn.PromptSeq);
            if (nextPromptSeq is not null)
                _dispatched.TryRemove(new KeyValuePair<Guid, DispatchedTurn>(sessionId, turn));
            return;
        }

        var texts = late.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        if (texts.Count == 0)
        {
            // Window drained: a later prompt means no future row can land in this gap, so the
            // record is done. No next prompt: keep the watermark so a later fragment of the
            // same turn still follow-ups.
            if (nextPromptSeq is not null)
                _dispatched.TryRemove(new KeyValuePair<Guid, DispatchedTurn>(sessionId, turn));
            return;
        }

        // Claim the trailing entries before sending; the loser of a race sees the moved watermark.
        var claimed = turn with { MaxTextSeq = late[^1].Sequence };
        if (!_dispatched.TryUpdate(sessionId, claimed, turn))
            return;

        var joined = string.Join("\n\n", texts.Select(l => l.Text!)).Trim();
        if (ChannelContracts.IsNoReply(joined))
        {
            if (nextPromptSeq is not null)
                _dispatched.TryRemove(new KeyValuePair<Guid, DispatchedTurn>(sessionId, claimed));
            return;
        }

        var (bodyText, attachments) = PrepareReplyBody(joined, sessionId);
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);
        var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        foreach (var target in turn.Targets)
        {
            var reply = new ChannelReply
            {
                Channel = target.Provider,
                ReplyHandle = target.ReplyHandle,
                ConversationId = target.ConversationId,
                Text = text.Length == 0 ? null : text,
                Kind = kind,
                Attachments = attachments,
            };
            var outcome = await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply,
                    new ChannelOutboundSource(sessionId, turn.PromptSeq,
                        turn.MaxTextSeq + 1, claimed.MaxTextSeq, "trailing", []), ct);
            if (outcome == ChannelOutboundSendOutcome.Published)
            {
                try { await channels.StampLastReplyAsync(target.Provider, target.ConversationId, text, ct); }
                catch (Exception stampError) when (stampError is not OperationCanceledException)
                {
                    _logger.LogWarning(stampError,
                        "Published follow-up to {ConversationId} but could not update its catalog preview",
                        target.ConversationId);
                }
            }
            _logger.LogInformation(
                "Sent follow-up {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) to {Provider} conversation {ConversationId} from session {SessionId} — text arrived after the turn's dispatch",
                reply.Kind, text.Length, attachments.Count, target.Provider, target.ConversationId, sessionId);
        }

        // After the window is drained: a next prompt already caps it, so drop. Otherwise keep
        // the advanced watermark for a later fragment of the same turn.
        if (nextPromptSeq is not null)
            _dispatched.TryRemove(new KeyValuePair<Guid, DispatchedTurn>(sessionId, claimed));
    }

    /// <summary>
    /// CARD-0250 / CARD-0338: after the Channel-origin match/settle path has had first claim, a
    /// later turn whose owning prompt is one of Antiphon's own injections (Delegation / Check /
    /// System / Scheduled) is published as a follow-up <see cref="ChannelReply"/> to the session's
    /// newest Channel-origin conversation. Attachments always; plain text when the matched origin
    /// is in <see cref="Settings.ChannelBridgeSettings.MachineTurnTextOrigins"/> (opt out with
    /// exact <c>NO_REPLY</c>).
    ///
    /// Idempotency reuses the injection row's own <see cref="SessionQueuedMessage.ChannelReplySettledAt"/>
    /// as the claim-before-produce marker. <see cref="OpenCorrelations"/> filters
    /// <c>Origin == Channel</c>, so this is invisible to correlation logic and costs no migration.
    /// A successful send records the <c>_dispatched</c> watermark so trailing AssistantText of the
    /// same turn follows via <see cref="DispatchFollowUpAsync"/>.
    /// </summary>
    private async Task DispatchMachineTurnFollowUpAsync(Guid sessionId,
        TranscriptEntry? historicalPrompt, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Gate 1: channel-bound. One indexed query — the only cost added to every non-channel
        // session's turn end. Newest ConversationKey is the follow-up target.
        var conversationKey = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.Channel
                && m.ConversationKey != null)
            .OrderByDescending(m => m.Sequence)
            .Select(m => m.ConversationKey)
            .FirstOrDefaultAsync(ct);
        if (conversationKey is null)
            return;

        var turnEndSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (turnEndSeq is not long endSeq)
            return;

        var userPrompt = historicalPrompt
            ?? await TranscriptTurnWindow.FindOwningPromptAsync(db, sessionId, endSeq, ct);
        if (userPrompt?.Text is not string promptText)
            return;
        if (await GrokRulesRefreshService.IsRefreshPromptAsync(db, sessionId, promptText, ct))
            return;

        var (responseText, maxTextSeq, containsApiErrorStub) =
            await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, ct);
        if (containsApiErrorStub)
            return;
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        var (_, explicitPaths) = ChannelContracts.ExtractAttachments(responseText);

        var normalizedTurn = Normalize(promptText);

        // Gate 2: the main path already owns this turn (Channel-origin prompt match). Its
        // attachments went with it, or CARD-0071 / NO_REPLY withheld them on purpose.
        if ((await MatchChannelRowsAsync(db, userPrompt, ct)).Count > 0)
            return;
        // A marked channel prompt that failed completeness/floors must not fall through to a
        // quoted task/check header and publish as a machine note.
        if (ChannelPromptCorrelation.HasMarkedTransportFrame(promptText))
            return;

        var candidates = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && (m.Origin == QueuedMessageOrigin.Delegation
                    || m.Origin == QueuedMessageOrigin.Check
                    || m.Origin == QueuedMessageOrigin.System
                    || m.Origin == QueuedMessageOrigin.Scheduled)
                && m.Status == QueuedMessageStatus.Sent
                && m.ChannelReplySettledAt == null
                && m.ChannelOutboundDeliveryId == null)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        var ids = ChannelContracts.CollectInjectionShortIds(promptText);
        var matches = candidates.Where(m =>
            MatchesByTaskId(m, ids) || MatchesHeaderLine(m, normalizedTurn)).ToList();
        SessionQueuedMessage? originalContext = null;
        if (outbound.UnifiedRecoveryEnabled)
        {
            // Task/header routing is not a delivered-prompt receipt. Historical and event
            // dispatch share the complete matcher and original attempt floors.
            matches = MatchMachineSources(matches, userPrompt);
            if (matches.Count > 0)
            {
                var first = matches.OrderBy(m => m.Sequence).First();
                var injectionAt = first.LastDeliveryStartedAt ?? first.SentAt ?? first.CreatedAt;
                originalContext = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Channel
                        && m.Status == QueuedMessageStatus.Sent && m.ConversationKey != null
                        && m.Sequence < first.Sequence
                        && (m.LastDeliveryStartedAt ?? m.SentAt ?? m.CreatedAt) <= injectionAt)
                    .OrderByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);
                if (originalContext is null)
                    return;
                conversationKey = originalContext.ConversationKey!;
            }
        }
        if (matches.Count == 0)
        {
            var injection = ChannelContracts.IsAntiphonInjectionPrompt(promptText);
            if (explicitPaths.Count == 0)
            {
                if (injection)
                {
                    _logger.LogWarning(
                        "Machine-turn follow-up on session {SessionId} prompt seq {PromptSeq} is "
                        + "injection-shaped, no row",
                        sessionId, userPrompt.Sequence);
                }

                return;
            }

            await ReportAttachmentsDroppedAsync(
                sessionId, userPrompt.Sequence, conversationKey,
                injection ? AlertSeverity.Error : AlertSeverity.Warning,
                injection ? "UnmatchedInjection" : "UnmatchedHuman", ct);
            return;
        }

        var captureReply = outbound.UnifiedRecoveryEnabled;
        if (captureReply)
        {
            captureReply = TrySplitConversationKey(conversationKey, out var captureProvider, out var captureConversation)
                && await db.ChatChannels.AnyAsync(c => c.Provider == captureProvider
                    && c.ExternalId == captureConversation, ct);
        }
        var (impliedPaths, impliedTasks) = await CollectImpliedAttachmentsAsync(db, matches, ct,
            describeOnly: captureReply);

        // CARD-0337 S3: an exact NO_REPLY with no explicit markers holds the bundle. The
        // orchestrator chose silence; S5's Done-time check catches it later.
        if (ChannelContracts.IsNoReply(responseText) && explicitPaths.Count == 0)
            return;

        var hasAttachments = explicitPaths.Count > 0 || impliedPaths.Count > 0
            || captureReply && impliedTasks.Count > 0;
        var deliverText = !hasAttachments && AdmitsMachineTurnText(matches);
        if (!hasAttachments && !deliverText)
        {
            _logger.LogDebug(
                "Machine-turn follow-up on session {SessionId} held: origin not in "
                + "MachineTurnTextOrigins and the turn has no attach markers or implied bundle",
                sessionId);
            return;
        }

        if (!TrySplitConversationKey(conversationKey, out var provider, out var conversationId))
        {
            await ReportAttachmentsDroppedAsync(
                sessionId, userPrompt.Sequence, conversationKey,
                AlertSeverity.Critical, "Unroutable", ct);
            return;
        }

        var channel = await db.ChatChannels.AsNoTracking()
            .Where(c => c.Provider == provider && c.ExternalId == conversationId)
            .Select(c => new { c.ReplyHandle })
            .FirstOrDefaultAsync(ct);
        if (channel is null)
        {
            _logger.LogWarning(
                "No channel catalog row for {ConversationKey} (session {SessionId}); addressing the "
                + "machine-turn follow-up by conversation id alone",
                conversationKey, sessionId);
        }

        var descriptor = ChannelReplyPreparation.Describe(responseText) with
        {
            BundleTaskIds = impliedTasks.Select(t => t.Id).ToArray(),
            RequiresAttachment = explicitPaths.Count == 0 && !AdmitsMachineTurnText(matches),
            MaxTextChars = _settings.MaxReplyChars,
        };
        var (bodyText, attachments) = captureReply
            ? (descriptor.Text, new List<OutboundAttachment>())
            : PrepareReplyBody(responseText, sessionId, impliedPaths);
        // A remaining-text of NO_REPLY still sends — the marker is the explicit ask. Empty text
        // (the file IS the follow-up), never a skip.
        if (ChannelContracts.IsNoReply(bodyText))
            bodyText = "";
        descriptor = descriptor with { Text = bodyText };
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);
        var replyHandle = await ResolveInboundReplyHandleAsync(db,
            originalContext is null ? matches : [originalContext], channel?.ReplyHandle, ct);
        var target = new ReplyTarget(provider, replyHandle, conversationId,
            matches.Select(m => m.Id).ToArray(), channel is not null);

        var publicationAccepted = false;
        try
        {
            var reply = new ChannelReply
            {
                Channel = provider,
                ReplyHandle = replyHandle,
                ConversationId = conversationId,
                Text = text.Length == 0 ? null : text,
                Kind = kind,
                Attachments = attachments,
            };
            ChannelOutboundSendOutcome outcome;
            var source = new ChannelOutboundSource(sessionId, userPrompt.Sequence,
                userPrompt.Sequence + 1, maxTextSeq, "machine", target.CorrelationIds,
                impliedTasks.FirstOrDefault()?.Id);
            if (captureReply)
            {
                var delivery = await outbound.CaptureAsync(reply, source, descriptor, _settings, ct,
                    sourceTaskIds: impliedTasks.Select(t => t.Id).ToArray());
                outcome = delivery.State == ChannelOutboundDeliveryState.Published
                    ? ChannelOutboundSendOutcome.Published : ChannelOutboundSendOutcome.Deferred;
            }
            else
                outcome = await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply, source, ct);
            publicationAccepted = outcome == ChannelOutboundSendOutcome.Published;
            if (outcome == ChannelOutboundSendOutcome.Published)
            {
                StampDeliveredBundles(impliedTasks, attachments, _timeProvider.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
            }
            if (historicalPrompt is null && !captureReply)
                _dispatched[sessionId] = new DispatchedTurn(userPrompt.Sequence, maxTextSeq, [target]);
            _logger.LogInformation(
                "Sent machine-turn follow-up {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) "
                + "to {Provider} conversation {ConversationId} from session {SessionId}",
                reply.Kind, text.Length, attachments.Count, provider, conversationId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!publicationAccepted && !captureReply)
            {
                foreach (var m in matches)
                    m.ChannelReplySettledAt = null;
                foreach (var task in impliedTasks)
                    task.DeliverableDeliveredAt = null;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            _logger.LogError(ex,
                "Machine-turn follow-up for session {SessionId} failed after accepted={Accepted}; "
                + "{Count} injection row(s) affected",
                sessionId, publicationAccepted, matches.Count);
            return;
        }

        if (!captureReply && matches.All(m => m.ChannelOutboundDeliveryId is null))
        {
            var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
            await channels.StampLastReplyAsync(provider, conversationId, text, ct);
        }
    }

    private List<SessionQueuedMessage> MatchMachineSources(List<SessionQueuedMessage> candidates,
        TranscriptEntry prompt)
    {
        candidates = candidates.Where(m => m.ChannelReplyDiscoveryClosedAt == null).ToList();
        var opening = candidates.Where(m => ChannelPromptCorrelation.MatchesMachineDelivery(
            m, prompt, _correlationTolerance)).ToList();
        var outer = opening.FirstOrDefault();
        if (outer is null)
            return [];
        var batch = candidates.Where(m => m.Origin == outer.Origin && m.DeliveryAttempts > 0
            && ChannelPromptCorrelation.SameDeliveredBatch(outer, m)).OrderBy(m => m.Sequence).ToList();
        // A complete first member establishes the outer machine receipt. Subsequent members
        // need the same persisted attempt and the entire composed batch, not a quoted header.
        if (batch.Count > 1 && Normalize(prompt.Text!).StartsWith(
            Normalize(ChannelPromptFormat.BatchContextMarker), StringComparison.Ordinal)
            && PromptSubmissionMatch.IsCompleteIn(ChannelPromptFormat.FormatBatch(
                batch.Take(batch.Count - 1).Select(m => m.Body).ToArray(), batch[^1].Body), prompt.Text!))
            return batch;
        return opening;
    }

    private static bool MatchesByTaskId(
        SessionQueuedMessage row, ChannelContracts.InjectionShortIds ids)
    {
        if (row.Origin == QueuedMessageOrigin.Delegation && row.SourceTaskId is Guid taskId)
            return ids.TaskIds.Contains(DelegationReportFormatter.Short(taskId));

        if (row.Origin == QueuedMessageOrigin.Check)
        {
            Guid? id = row.SourceTaskId;
            if (id is null
                && AgentTaskCheckService.TryParseCheckConversationKey(row.ConversationKey, out var parsed))
            {
                id = parsed;
            }

            return id is Guid checkId && ids.CheckIds.Contains(DelegationReportFormatter.Short(checkId));
        }

        return false;
    }

    private static bool MatchesHeaderLine(SessionQueuedMessage row, string normalizedTurn)
    {
        var header = ChannelContracts.HeaderProbe(row.Body);
        return header.Length > 0 && MachineHeaderProbeMatches(header, normalizedTurn);
    }

    private bool AdmitsMachineTurnText(IReadOnlyList<SessionQueuedMessage> matches)
    {
        var origins = _settings.MachineTurnTextOrigins;
        if (origins is null || origins.Count == 0)
            return false;
        return matches.Any(m => origins.Contains(m.Origin));
    }

    /// <summary>
    /// CARD-0337 S3: undelivered bundle files for matched Delegation rows (PDF first, then sources).
    /// Check/System rows contribute none. Deduped by full path.
    /// </summary>
    private static async Task<(List<string> Paths, List<AgentTask> Tasks)> CollectImpliedAttachmentsAsync(
        AppDbContext db,
        IReadOnlyList<SessionQueuedMessage> matches,
        CancellationToken ct, bool describeOnly = false)
    {
        var paths = new List<string>();
        var tasks = new List<AgentTask>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in matches)
        {
            if (row.Origin != QueuedMessageOrigin.Delegation || row.SourceTaskId is not Guid taskId)
                continue;
            var task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task is null
                || string.IsNullOrWhiteSpace(task.DeliverableBundleDir)
                || task.DeliverableDeliveredAt is not null)
                continue;
            if (describeOnly)
            {
                // Bundle identity is sufficient before capture. The pump reads its manifest/files.
                tasks.Add(task);
                continue;
            }
            var files = DeliverableBundleService.ListAttachableFiles(task);
            if (files.Count == 0)
                continue;
            tasks.Add(task);
            foreach (var file in files)
            {
                if (seen.Add(file))
                    paths.Add(file);
            }
        }

        return (paths, tasks);
    }

    private static void StampDeliveredBundles(
        IReadOnlyList<AgentTask> impliedTasks,
        IReadOnlyList<OutboundAttachment> attachments,
        DateTime now)
    {
        var attached = new HashSet<string>(
            attachments.Select(a => a.Source ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var task in impliedTasks)
        {
            var files = DeliverableBundleService.ListAttachableFiles(task);
            if (files.Any(f => attached.Contains(f)))
                task.DeliverableDeliveredAt = now;
        }
    }

    /// <summary>
    /// CARD-0250: attach markers that neither the main path nor the follow-up path delivered.
    /// Deduped per (session, owning-prompt sequence) so transcript re-triggers of one turn raise
    /// once. Own scope, same plumbing as <see cref="ReportLostAsync"/>.
    /// </summary>
    private async Task ReportAttachmentsDroppedAsync(
        Guid sessionId,
        long promptSeq,
        string conversationKey,
        AlertSeverity severity,
        string branch,
        CancellationToken ct)
    {
        var failureReason = $"{branch}:{promptSeq}";
        var why = branch switch
        {
            "Unroutable" =>
                $"a machine-triggered turn produced attach markers but the follow-up conversation key "
                + $"({conversationKey}) names no routable target",
            "UnmatchedInjection" =>
                $"a completed turn produced attach markers but the owning prompt (seq {promptSeq}) looks "
                + "like an Antiphon injection and matched no queued row — the follow-up was not sent",
            _ =>
                $"a completed turn produced attach markers but the owning prompt (seq {promptSeq}) was "
                + "not an Antiphon injection and matched no channel correlation — publishing would be a stray reply",
        };

        if (severity >= AlertSeverity.Error)
        {
            _logger.LogError(
                "CHANNEL ATTACHMENTS DROPPED: session {SessionId} turn prompt seq {PromptSeq} — {Why}.",
                sessionId, promptSeq, why);
        }
        else
        {
            _logger.LogWarning(
                "CHANNEL ATTACHMENTS DROPPED: session {SessionId} turn prompt seq {PromptSeq} — {Why}.",
                sessionId, promptSeq, why);
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var already = await db.AgentIncidents.AnyAsync(
                i => i.SessionId == sessionId
                    && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped
                    && i.FailureReason == failureReason,
                ct);
            if (already)
                return;

            var sessionIdText = sessionId.ToString("D");
            var agentId = await db.Agents
                .Where(a => a.PersistentSessionId == sessionIdText)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (agentId is null && TrySplitConversationKey(conversationKey, out var p, out var cid))
            {
                agentId = await db.ChatChannels
                    .Where(c => c.Provider == p && c.ExternalId == cid && c.AgentId != null)
                    .Select(c => c.AgentId)
                    .FirstOrDefaultAsync(ct);
            }
            if (agentId is not Guid owner)
            {
                _logger.LogError(
                    "No agent owns session {SessionId}, so the dropped-attachments event could not be "
                    + "recorded as an incident. {Why}.",
                    sessionId, why);
                return;
            }

            var supervisor = scope.ServiceProvider.GetService<AgentSupervisorService>();
            if (supervisor is null)
                return;

            var incidentMessage = branch switch
            {
                "Unroutable" =>
                    $"This agent tried to send a file to {conversationKey} and there is no routable conversation to deliver it to. {why}.",
                "UnmatchedInjection" =>
                    $"This agent produced attach markers on a turn that looks like an Antiphon note but matched no queued injection. The file was not sent to {conversationKey}. {why}.",
                _ =>
                    $"This agent produced attach markers on a turn that was not started by an Antiphon note and matched no channel message. The file was not sent to {conversationKey}. {why}.",
            };

            await supervisor.RecordIncidentAsync(
                owner,
                sessionId,
                AgentIncidentKind.ChannelAttachmentsDropped,
                severity,
                ColumnText.Clip(incidentMessage, AgentIncident.MessageMaxLength),
                failureReason: failureReason,
                ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Recording the dropped-attachments incident for session {SessionId} failed", sessionId);
        }
    }

    private string Truncate(string responseText) =>
        responseText.Length > _settings.MaxReplyChars
            ? responseText[.._settings.MaxReplyChars] + "…"
            : responseText;

    // The turn's response = all assistant text after its prompt up to the NEXT prompt — NOT capped
    // at the TurnEnd sequence, because the stop marker can precede the reply text in Claude's
    // transcript ordering. At dispatch time the next turn hasn't produced entries yet, so an open
    // upper bound is safe. Also returns the highest sequence included, so trailing text that lands
    // after this dispatch can be told apart from what was already sent — and whether the window
    // contains an API-error stub (CARD-0071), which the caller withholds the whole turn on. Stub
    // rows are additionally excluded from the join so no refactor of the withhold can ever let the
    // error string ride out inside a reply body.
    private static async Task<(string? Text, long MaxSeq, bool ContainsApiErrorStub)> ExtractTurnResponseAsync(
        AppDbContext db, Guid sessionId, long promptSeq, CancellationToken ct)
    {
        var (nextPromptSeq, entries) = await QueryTurnWindowAsync(db, sessionId, promptSeq, afterSeq: promptSeq, ct);

        var maxSeq = entries.Count > 0 ? entries[^1].Sequence : promptSeq;
        var containsStub = entries.Any(t => TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError));
        if (!containsStub)
        {
            // Grok/Codex stamp the diagnostic on the TurnEnd itself — no AssistantText stub.
            var turnEndQuery = db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.TurnEnd
                    && t.IsApiError == true
                    && t.Sequence > promptSeq);
            if (nextPromptSeq is long cap)
                turnEndQuery = turnEndQuery.Where(t => t.Sequence < cap);
            containsStub = await turnEndQuery.AnyAsync(ct);
        }

        var joined = string.Join("\n\n", entries
            .Where(t => !TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError))
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return (string.IsNullOrWhiteSpace(joined) ? null : joined.Trim(), maxSeq, containsStub);
    }

    private readonly record struct TurnWindowRow(long Sequence, string? Text, string Kind, bool? IsApiError);

    /// <summary>
    /// Assistant text belonging to <paramref name="promptSeq"/>'s turn: after
    /// <paramref name="afterSeq"/>, before the next turn-opening prompt (uncapped if none).
    /// Main-path extraction uses <c>afterSeq == promptSeq</c>; follow-up uses the already-sent
    /// watermark. A <c>UserPrompt</c> always caps. A <c>QueuedUserPrompt</c> caps only when it
    /// opens the next turn — i.e. a TurnEnd sits between this prompt and it (CARD-0154 /
    /// CARD-0068 / CARD-0233). An in-turn queued body must not exclude this turn's AssistantText.
    /// </summary>
    private static async Task<(long? NextPromptSeq, IReadOnlyList<TurnWindowRow> Entries)> QueryTurnWindowAsync(
        AppDbContext db, Guid sessionId, long promptSeq, long afterSeq, CancellationToken ct)
    {
        var nextPromptSeq = await TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync(
            db, sessionId, promptSeq, ct);

        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > afterSeq);
        if (nextPromptSeq is long cap)
            query = query.Where(t => t.Sequence < cap);

        var entries = await query
            .OrderBy(t => t.Sequence)
            .Select(t => new TurnWindowRow(t.Sequence, t.Text, t.Kind, t.IsApiError))
            .ToListAsync(ct);
        return (nextPromptSeq, entries);
    }

    // The independent machine-note header contract (CARD-0397), never channel attribution.
    private static bool MachineHeaderProbeMatches(string header, string turn) =>
        turn.Contains(header.Length <= 120 ? header : header[..120], StringComparison.Ordinal);

    private static string Normalize(string s) =>
        s.ReplaceLineEndings("\n").Trim();

    private static async Task<string?> ResolveInboundReplyHandleAsync(AppDbContext db,
        IEnumerable<SessionQueuedMessage> rows, string? catalogHandle, CancellationToken ct)
    {
        var inboundIds = rows.OrderByDescending(r => r.Sequence)
            .Where(r => r.SourceChannelInboundId.HasValue)
            .Select(r => r.SourceChannelInboundId!.Value).ToArray();
        if (inboundIds.Length == 0)
            return catalogHandle;
        var envelopes = await db.ChannelInbounds.AsNoTracking()
            .Where(i => inboundIds.Contains(i.Id) && i.EnvelopeJson != null)
            .Select(i => new { i.Id, i.EnvelopeJson }).ToListAsync(ct);
        var byId = envelopes.ToDictionary(i => i.Id, i => i.EnvelopeJson);
        foreach (var id in inboundIds)
        {
            if (!byId.TryGetValue(id, out var json) || json is null)
                continue;
            try
            {
                var handle = JsonSerializer.Deserialize<ChannelMessage>(json,
                    global::Antiphon.Messaging.MessagingJson.Options)?.ReplyHandle;
                if (!string.IsNullOrWhiteSpace(handle))
                    return handle;
            }
            catch (JsonException) { /* Old or damaged envelope: use the catalog fallback. */ }
        }
        return catalogHandle;
    }

    /// <summary>
    /// Final answer vs blocking question. Heuristic: the response's closing line asking something is
    /// the strongest signal the agent is waiting on the human. (Progress is reserved for future
    /// mid-turn notes; a completed turn is never Progress.)
    /// </summary>
    internal static ChannelReplyKind ClassifyKind(string responseText)
    {
        var lines = responseText.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return ChannelReplyKind.Answer;

        // Look at the tail of the response: a question mark ending any of the last two lines.
        return lines.TakeLast(2).Any(l => l.EndsWith('?'))
            ? ChannelReplyKind.Question
            : ChannelReplyKind.Answer;
    }
}
