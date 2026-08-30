using System.Collections.Concurrent;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Exceptions;
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
    private sealed record ReplyTarget(string Provider, string? ReplyHandle, string ConversationId);

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
    }

    /// <summary>
    /// Prefix of the targeted send to the originating conversation when a correlation is abandoned
    /// (CARD-0233 / CARD-0171). Distinct from an agent reply so tests (and humans) can tell them apart.
    /// </summary>
    internal const string LostReplyNoticePrefix =
        "[Antiphon] A reply this chat was owed was never delivered:";

    // The last turn we replied for, per session. Claude can keep writing AssistantText AFTER the
    // TurnEnd that triggered dispatch (observed live 2026-07-29, AZ Care: TurnEnd, AssistantText,
    // TurnEnd — the first dispatch consumed the correlations with only the turn's interim narration
    // extracted, and the real answer landed one second later with nothing left to match). Remembering
    // the dispatched turn's watermark lets that trailing text go out as a follow-up reply. Record
    // equality (value-compared watermark, reference-compared targets) makes TryUpdate an atomic
    // claim, so racing triggers can't double-send.
    private readonly ConcurrentDictionary<Guid, DispatchedTurn> _dispatched = new();

    private sealed record DispatchedTurn(long PromptSeq, long MaxTextSeq, IReadOnlyList<ReplyTarget> Targets);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly Settings.ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelReplyDispatcher> _logger;

    public ChannelReplyDispatcher(
        IServiceScopeFactory scopeFactory,
        IAntiphonMessagingProducer producer,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelReplyDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
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
                && m.ChannelReplySettledAt == null);

    /// <summary>
    /// Called on every completed turn. Cheap for sessions with no channel correlations (one indexed
    /// count), and the only path that can turn an agent's turn into a chat reply.
    /// </summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await DispatchAsync(sessionId, ct);

            // Trailing text for an already-answered turn (stop marker mid-stream) goes out as a
            // follow-up. No-op unless this session's last dispatched turn is still the live one.
            if (_dispatched.ContainsKey(sessionId))
                await DispatchFollowUpAsync(sessionId, ct);

            // CARD-0250: a later machine-triggered turn (task-done / check-in / system note) that
            // carries [[attach:]] still reaches the last-known conversation after the ack turn
            // settled the Channel correlation. Additive; never matches or settles Channel-origin
            // rows. The main path always wins first claim on the turn.
            await DispatchMachineTurnAttachmentsAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to dispatch channel reply for session {SessionId}", sessionId);
        }
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

    private async Task DispatchAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await AbandonStaleCorrelationsAsync(db, sessionId, ct);

        var open = await OpenCorrelations(db)
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        if (open.Count == 0)
            return;

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
            return;
        }

        var userPrompt = await TranscriptTurnWindow.FindOwningPromptAsync(db, sessionId, endSeq, ct);
        if (userPrompt?.Text is not string promptText)
        {
            _logger.LogDebug(
                "Session {SessionId} owes {Count} channel reply/replies but the turn ending at seq {EndSeq} "
                + "has no preceding UserPrompt or QueuedUserPrompt to match",
                sessionId, open.Count, endSeq);
            return;
        }

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
            _logger.LogWarning(
                "Turn on session {SessionId} (prompt seq {PromptSeq}) was killed by an API error; withholding "
                + "the channel reply for the whole turn. {Count} correlation(s) stay owed for a resumed turn, "
                + "with the TTL sweep as the backstop.",
                sessionId, userPrompt.Sequence, open.Count);
            return;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogDebug(
                "Turn on session {SessionId} has no assistant text yet; correlations stay pending", sessionId);
            return;
        }

        // Only answer turns WE started: match the turn's prompt against the queued bodies still owed
        // a reply. A human typing directly into the terminal produces turns that match nothing and
        // are skipped. A BATCHED turn (several queued channel messages coalesced into one body)
        // matches — and settles — every constituent row by containment, exactly as the in-memory
        // queue did: the row's Body IS the string the bridge enqueued and the queue typed.
        var normalizedTurn = Normalize(promptText);
        var matches = open.Where(m => PromptsMatch(Normalize(m.Body), normalizedTurn)).ToList();
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
            return;
        }

        // Resolve the reply target from the row itself — the whole CARD-0067 fix. ConversationKey is
        // '{provider}:{conversationId}'; the addressing handle comes from the channel catalog, which
        // keeps the newest one the provider gave us (for every Telegram record on the topic it equals
        // the conversation id). ConversationId alone is a complete address for the gateway, so a
        // missing catalog row degrades to a Warning and still sends — a reply nobody reads because
        // the handle was null would be this card's bug wearing a different hat.
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
            if (channel is null)
            {
                _logger.LogWarning(
                    "No channel catalog row for {ConversationKey} (session {SessionId}); addressing the reply "
                    + "by conversation id alone", group.Key, sessionId);
            }

            targets.Add(new ReplyTarget(provider, channel?.ReplyHandle, conversationId));
        }

        if (unroutable.Count > 0)
        {
            // The turn was answered and there is nowhere to send it. That is a lost reply, not a
            // skip, so it settles with a Critical incident rather than being retried forever.
            await SettleAsync(db, unroutable, ct);
            await ReportLostAsync(sessionId, unroutable, LossReason.Unroutable, ct);
            matches = matches.Except(unroutable).ToList();
            if (matches.Count == 0)
                return;
        }

        // CLAIM BEFORE SENDING. Marking settled first is what makes a restart safe in the other
        // direction: the dispatcher is re-triggered for the same turn all the time (an AssistantText
        // arrival, the closing TurnEnd, a reconnect's backfilled boundary), and with a durable
        // correlation an unclaimed row would answer the same turn again — a duplicate into a real
        // family chat. Same shape, and the same reasoning, as the queue stamping Sent before it types.
        // A produce failure below un-claims, so a broker blip retries on the next turn end.
        await SettleAsync(db, matches, ct);

        // Settling the correlations closes the turn — remember its watermark so text that lands
        // AFTER this dispatch (stop marker mid-stream) can still be delivered as a follow-up.
        _dispatched[sessionId] = new DispatchedTurn(userPrompt.Sequence, maxTextSeq, targets);

        // The frozen silent-turn contract: a whole-turn NO_REPLY settles the correlations and
        // sends nothing — system notes and housekeeping turns must never spam the chat.
        if (ChannelContracts.IsNoReply(responseText))
        {
            _logger.LogInformation(
                "Silent turn (NO_REPLY) on session {SessionId}; {Count} correlation(s) settled without a reply",
                sessionId, matches.Count);
            return;
        }

        var (bodyText, attachments) = PrepareReplyBody(responseText, sessionId);
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);

        // One reply per distinct conversation. With same-conversation batching this loop is
        // degenerate (exactly one send) — the fan-out is a deliberate latent safety net in case
        // batching scope ever widens to cross-conversation.
        try
        {
            foreach (var target in targets)
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
                await _producer.SendAsync(reply, ct);
                _logger.LogInformation(
                    "Sent {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) to {Provider} conversation {ConversationId} from session {SessionId}",
                    reply.Kind, text.Length, attachments.Count, target.Provider, target.ConversationId, sessionId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un-claim so the next turn end tries again, and drop the follow-up watermark with it —
            // a turn whose reply never left must not have trailing text sent on its behalf.
            _dispatched.TryRemove(sessionId, out _);
            foreach (var m in matches)
                m.ChannelReplySettledAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex,
                "Producing the channel reply for session {SessionId} failed; {Count} correlation(s) returned to "
                + "owed for the next turn end", sessionId, matches.Count);
        }
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

        await SettleAsync(db, stale, ct);
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
        var sentAt = message.SentAt ?? message.CreatedAt;
        var candidates = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == message.AgentSessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && (t.Timestamp ?? t.CreatedAt) >= sentAt)
            .OrderBy(t => t.Sequence)
            .ToListAsync(ct);

        var normalizedBody = Normalize(message.Body);
        var prompt = candidates.FirstOrDefault(t =>
            t.Text is string text && PromptsMatch(normalizedBody, Normalize(text)));
        if (prompt is null)
            return (LossReason.StaleTtl, null, 0);

        var hasTurnEnd = await db.TranscriptEntries.AnyAsync(
            t => t.AgentSessionId == message.AgentSessionId
                && t.Kind == TranscriptKinds.TurnEnd
                && t.Sequence > prompt.Sequence, ct);
        if (!hasTurnEnd)
            return (LossReason.TurnIncomplete, prompt, 0);

        var (_, entries) = await QueryTurnWindowAsync(
            db, message.AgentSessionId, prompt.Sequence, afterSeq: prompt.Sequence, ct);
        var assistantChars = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .Sum(e => e.Text!.Length);
        if (assistantChars == 0)
            return (LossReason.TurnIncomplete, prompt, 0);

        return (LossReason.TurnUnmatched, prompt, assistantChars);
    }

    /// <summary>
    /// The end of silent loss. Logs at Error and records a Critical
    /// <see cref="AgentIncidentKind.ChannelReplyLost"/> incident on the agent that owns the session —
    /// which routes through the normal alert pipeline, i.e. it reaches a human. Always Critical: a
    /// channel correlation exists only because somebody in a chat asked something.
    ///
    /// <para>Runs in its OWN scope on purpose. The caller's context holds the rows it has just marked
    /// settled; a failed incident insert (an agent row deleted underneath us, a constraint) would
    /// otherwise leave a poisoned entity tracked there and break every later save on it.</para>
    /// </summary>
    private async Task ReportLostAsync(
        Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> lost,
        LossReason reason,
        CancellationToken ct,
        TranscriptEntry? unmatchedPrompt = null,
        int assistantChars = 0)
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
            _ => $"no turn matching the message completed within {_settings.PendingReplyTtlMinutes} minutes",
        };
        var oldest = lost.Min(m => m.SentAt ?? m.CreatedAt);

        _logger.LogError(
            "CHANNEL REPLY LOST: {Count} message(s) from {Conversations} routed into session {SessionId} were "
            + "never answered — {Why}. Oldest owed since {Oldest:u}. Message ids: {MessageIds}. "
            + "A human asked and got silence.",
            lost.Count, conversations, sessionId, why, oldest,
            string.Join(", ", lost.Select(m => m.Id)));

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionIdText = sessionId.ToString("D");
            var agentId = await db.Agents
                .Where(a => a.PersistentSessionId == sessionIdText)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (agentId is null && TrySplitConversationKey(lost[0].ConversationKey!, out var p, out var cid))
            {
                // A session the agent no longer points at (relaunched, re-adopted) still belongs to
                // whichever agent the conversation is bound to — that is the agent a human is waiting on.
                agentId = await db.ChatChannels
                    .Where(c => c.Provider == p && c.ExternalId == cid && c.AgentId != null)
                    .Select(c => c.AgentId)
                    .FirstOrDefaultAsync(ct);
            }
            if (agentId is not Guid owner)
            {
                // Nothing to hang an incident on — the Error line above is the whole record. Never
                // swallow: an unowned session that answers a chat is itself worth seeing.
                _logger.LogError(
                    "No agent owns session {SessionId}, so the lost channel reply could not be recorded as an "
                    + "incident. {Count} message(s) from {Conversations} went unanswered.",
                    sessionId, lost.Count, conversations);
                return;
            }

            // Scoped supervisor sharing this scope's AppDbContext; RecordIncidentAsync does NOT save.
            var supervisor = scope.ServiceProvider.GetService<AgentSupervisorService>();
            if (supervisor is not null)
            {
                var incidentMessage =
                    $"A reply this agent owed {conversations} was never sent: {lost.Count} message(s) went "
                    + $"unanswered because {why}.";
                if (reason == LossReason.TurnUnmatched && unmatchedPrompt?.Text is string promptBody)
                {
                    var excerpt = ColumnText.Clip(Normalize(promptBody), 80);
                    incidentMessage += $" Unmatched prompt: \"{excerpt}\".";
                }
                incidentMessage += " Someone in that chat asked a question and got silence.";

                await supervisor.RecordIncidentAsync(
                    owner,
                    sessionId,
                    AgentIncidentKind.ChannelReplyLost,
                    AlertSeverity.Critical,
                    ColumnText.Clip(incidentMessage, AgentIncident.MessageMaxLength),
                    failureReason: reason.ToString(),
                    ct: ct);
                await db.SaveChangesAsync(ct);
            }

            // CARD-0233 decision 2: tell the originating conversation, not ChannelAlertRouter
            // (every AlertMinSeverity is null today; filling one would dump every Critical into
            // Family). Unroutable has nowhere to send. A send failure is a Warning, never a
            // failed sweep — the incident has already committed.
            if (reason != LossReason.Unroutable)
                await NotifyOriginatingConversationsAsync(scope.ServiceProvider, lost, why, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Recording the lost channel reply incident for session {SessionId} failed", sessionId);
        }
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
                await channels.SendAsync(channel.Id, text, ct);
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
        string responseText, Guid sessionId)
    {
        var (text, paths) = ChannelContracts.ExtractAttachments(responseText);
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

    private static AttachmentKind InferAttachmentKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => AttachmentKind.Image,
        ".mp4" or ".mov" or ".webm" => AttachmentKind.Video,
        ".mp3" or ".m4a" or ".wav" or ".flac" => AttachmentKind.Audio,
        ".ogg" or ".oga" => AttachmentKind.Voice,
        _ => AttachmentKind.File,
    };

    private static string InferMime(string extension) => extension.ToLowerInvariant() switch
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
    // This watermark is deliberately still process-memory only, and it is the one thing CARD-0067 did
    // NOT make durable: it addresses a turn that has ALREADY been answered, so losing it to a restart
    // costs at most a trailing fragment, never the answer — and a durable version would have to decide
    // what "already sent" means for text a dead process may or may not have produced.
    //
    // Trailing text is attributed by the same sequence window ExtractTurnResponseAsync uses
    // (PromptSeq < seq < nextPromptSeq), lower-bounded at MaxTextSeq. A newer prompt is the
    // window's upper bound, not a reason to drop in-window text — CARD-0068, seq 469 on 2026-08-17
    // (the guest list) landed in the same PersistTranscriptAsync batch as seq 471 (the next
    // UserPrompt) and was discarded by the old "is PromptSeq still the latest UserPrompt?" bail.
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
            await _producer.SendAsync(reply, ct);
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
    /// CARD-0250: after the Channel-origin match/settle path has had first claim, a later turn
    /// whose owning prompt is one of Antiphon's own injections (Delegation / Check / System) and
    /// whose AssistantText carries <c>[[attach:]]</c> is published as a follow-up
    /// <see cref="ChannelReply"/> to the session's newest Channel-origin conversation.
    ///
    /// Idempotency reuses the injection row's own <see cref="SessionQueuedMessage.ChannelReplySettledAt"/>
    /// as the claim-before-produce marker. <see cref="OpenCorrelations"/> filters
    /// <c>Origin == Channel</c>, so this is invisible to correlation logic and costs no migration.
    /// A first trigger that sees text without markers does not claim — a marker landing in a later
    /// transcript batch of the same turn still sends.
    /// </summary>
    private async Task DispatchMachineTurnAttachmentsAsync(Guid sessionId, CancellationToken ct)
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

        var userPrompt = await TranscriptTurnWindow.FindOwningPromptAsync(db, sessionId, endSeq, ct);
        if (userPrompt?.Text is not string promptText)
            return;

        var (responseText, _, containsApiErrorStub) =
            await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, ct);
        if (containsApiErrorStub)
            return;
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        var (_, paths) = ChannelContracts.ExtractAttachments(responseText);
        if (paths.Count == 0)
            return;

        var normalizedTurn = Normalize(promptText);

        // Gate 2: the main path already owns this turn (Channel-origin prompt match). Its
        // attachments went with it, or CARD-0071 / NO_REPLY withheld them on purpose.
        var channelBodies = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Channel)
            .Select(m => m.Body)
            .ToListAsync(ct);
        if (channelBodies.Any(b => PromptsMatch(Normalize(b), normalizedTurn)))
            return;

        var candidates = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && (m.Origin == QueuedMessageOrigin.Delegation
                    || m.Origin == QueuedMessageOrigin.Check
                    || m.Origin == QueuedMessageOrigin.System)
                && m.Status == QueuedMessageStatus.Sent
                && m.ChannelReplySettledAt == null)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        var matches = candidates.Where(m => PromptsMatch(Normalize(m.Body), normalizedTurn)).ToList();
        if (matches.Count == 0)
        {
            await ReportAttachmentsDroppedAsync(
                sessionId, userPrompt.Sequence, conversationKey,
                AlertSeverity.Warning, "UnmatchedHuman", ct);
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
                + "machine-turn attachment follow-up by conversation id alone",
                conversationKey, sessionId);
        }

        await SettleAsync(db, matches, ct);

        var (bodyText, attachments) = PrepareReplyBody(responseText, sessionId);
        // A remaining-text of NO_REPLY still sends — the marker is the explicit ask. Empty text
        // (the file IS the follow-up), never a skip.
        if (ChannelContracts.IsNoReply(bodyText))
            bodyText = "";
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);

        try
        {
            var reply = new ChannelReply
            {
                Channel = provider,
                ReplyHandle = channel?.ReplyHandle,
                ConversationId = conversationId,
                Text = text.Length == 0 ? null : text,
                Kind = kind,
                Attachments = attachments,
            };
            await _producer.SendAsync(reply, ct);
            _logger.LogInformation(
                "Sent machine-turn follow-up {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) "
                + "to {Provider} conversation {ConversationId} from session {SessionId}",
                reply.Kind, text.Length, attachments.Count, provider, conversationId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var m in matches)
                m.ChannelReplySettledAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex,
                "Producing the machine-turn attachment follow-up for session {SessionId} failed; "
                + "{Count} injection row(s) returned to unclaimed for the next turn end",
                sessionId, matches.Count);
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
            _ =>
                $"a completed turn produced attach markers but the owning prompt (seq {promptSeq}) was "
                + "not an Antiphon injection and matched no channel correlation — publishing would be a stray reply",
        };

        if (severity == AlertSeverity.Critical)
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

            var incidentMessage = branch == "Unroutable"
                ? $"This agent tried to send a file to {conversationKey} and there is no routable conversation to deliver it to. {why}."
                : $"This agent produced attach markers on a turn that was not started by an Antiphon note and matched no channel message. The file was not sent to {conversationKey}. {why}.";

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
        var (_, entries) = await QueryTurnWindowAsync(db, sessionId, promptSeq, afterSeq: promptSeq, ct);

        var maxSeq = entries.Count > 0 ? entries[^1].Sequence : promptSeq;
        var containsStub = entries.Any(t => TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError));
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

    // The delivered prompt and the transcript's UserPrompt should be byte-identical, but hooks can
    // append suffixes and long prompts may be normalised — match on a generous probe. EVERY match
    // settles: a batched turn's body contains several owed prompts, and each must be closed by the
    // one reply. Non-matches simply stay owed (there is no queue to preserve order in any more).
    private static bool PromptsMatch(string pending, string turn)
    {
        var probe = pending.Length <= 120 ? pending : pending[..120];
        // Containment, not prefix: an unbatched turn IS the pending prompt (plus possible hook
        // suffixes); a batched turn embeds it after the batch markers. A human-typed turn contains
        // neither — the 120-char enveloped probe ([Telegram "…" — …] …) is not something a human
        // types into the terminal by accident.
        return turn.Contains(probe, StringComparison.Ordinal);
    }

    private static string Normalize(string s) =>
        s.ReplaceLineEndings("\n").Trim();

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
