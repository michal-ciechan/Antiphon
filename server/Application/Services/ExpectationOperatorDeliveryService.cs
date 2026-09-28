using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable, direct operator publication. Broker acceptance is the Published boundary.</summary>
public sealed class ExpectationOperatorDeliveryService(AppDbContext db,
    IAntiphonMessagingProducer producer, TimeProvider time, ExpectationTimingSettings timing)
{
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromSeconds(30);

    public async Task<int> PublishDueAsync(ExpectationDirectiveSettings directive, CancellationToken ct)
    {
        if (!directive.Enabled || directive.ActiveUntilUtc <= time.GetUtcNow())
            return 0;
        var digest = await ExpectationConfigIdentity.ResolveAsync(db, directive, timing, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var ids = await db.ExpectationNudges.AsNoTracking()
            .Where(n => n.DirectiveId == directive.Id && n.ConfigDigest == digest
                && n.AnsweredAt == null
                && (n.OperatorOutboxState == ExpectationOperatorOutboxState.Due
                    || (n.OperatorOutboxState == ExpectationOperatorOutboxState.Published
                        && n.OperatorPublishedAt != null
                        && n.OperatorPublishedAt <= now.AddMinutes(-timing.RepeatMinutes))))
            .OrderBy(n => n.OperatorNextAttemptAt).ThenBy(n => n.CreatedAt)
            .Select(n => n.Id).Take(50).ToListAsync(ct);
        var published = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await PublishOneAsync(directive, id, digest, ct))
                published++;
        }
        return published;
    }

    private async Task<bool> PublishOneAsync(ExpectationDirectiveSettings directive,
        Guid id, string digest, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var channel = await db.ChatChannels.AsNoTracking()
            .Where(c => c.Id == directive.OperatorChannelId && c.Enabled)
            .Select(c => new { c.Id, c.Provider, c.ExternalId })
            .SingleOrDefaultAsync(ct);
        if (channel is null || string.IsNullOrWhiteSpace(channel.Provider)
            || string.IsNullOrWhiteSpace(channel.ExternalId))
            return false; // Keep the existing durable debt and expose the config fault in status.

        var token = Guid.NewGuid();
        string? body = null;
        int ordinal = 0;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var nudge = await db.ExpectationNudges.AsNoTracking().SingleAsync(n => n.Id == id, ct);
            if (nudge.AnsweredAt is not null || nudge.ConfigDigest != digest
                || nudge.OperatorClaimExpiresAt > now
                || (nudge.OperatorOutboxState == ExpectationOperatorOutboxState.Due
                    && nudge.OperatorNextAttemptAt > now))
                return false;
            if (nudge.OperatorPageProvider is not null
                && (nudge.OperatorPageProvider != channel.Provider
                    || nudge.OperatorPageConversationId != channel.ExternalId))
            {
                await db.ExpectationNudges.Where(n => n.Id == id
                        && n.OperatorOutboxState != ExpectationOperatorOutboxState.Published)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Suppressed), ct);
                await tx.CommitAsync(ct);
                return false;
            }
            var repeating = nudge.OperatorOutboxState == ExpectationOperatorOutboxState.Published;
            if (repeating && (nudge.OperatorPublishedAt is null
                || nudge.OperatorPublishedAt > now.AddMinutes(-timing.RepeatMinutes)))
                return false;
            var episodeIds = JsonSerializer.Deserialize<Guid[]>(nudge.EpisodeIdsJson) ?? [];
            if (!await db.ExpectationEpisodes.AsNoTracking().AnyAsync(e => episodeIds.Contains(e.Id)
                && e.ResolvedAt == null && e.ConfigDigest == digest, ct))
            {
                await db.ExpectationNudges.Where(n => n.Id == id
                        && n.OperatorOutboxState != ExpectationOperatorOutboxState.Published)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Suppressed), ct);
                await tx.CommitAsync(ct);
                return false;
            }

            ordinal = repeating ? nudge.OperatorPublicationOrdinal + 1
                : Math.Max(1, nudge.OperatorPublicationOrdinal);
            body = repeating || nudge.OperatorPageBody is null
                ? PageBody(nudge.Id, ordinal, directive, nudge, now)
                : nudge.OperatorPageBody;
            var rows = await db.ExpectationNudges.Where(n => n.Id == id
                    && n.AnsweredAt == null && n.ConfigDigest == digest
                    && (n.OperatorClaimExpiresAt == null || n.OperatorClaimExpiresAt <= now)
                    && n.OperatorOutboxState == nudge.OperatorOutboxState)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Due)
                    .SetProperty(n => n.OperatorChannelId, channel.Id)
                    .SetProperty(n => n.OperatorPageProvider, channel.Provider)
                    .SetProperty(n => n.OperatorPageConversationId, channel.ExternalId)
                    .SetProperty(n => n.OperatorPageBody, body)
                    .SetProperty(n => n.OperatorPageDigest, ExpectationDirectiveDigest.HashUtf8(body))
                    .SetProperty(n => n.OperatorPublicationOrdinal, ordinal)
                    .SetProperty(n => n.OperatorClaimToken, token)
                    .SetProperty(n => n.OperatorClaimExpiresAt, now.Add(ClaimLifetime))
                    .SetProperty(n => n.OperatorLastAttemptAt, now)
                    .SetProperty(n => n.OperatorAttemptCount, repeating ? 1 : nudge.OperatorAttemptCount + 1), ct);
            if (rows != 1)
                return false;
            await ExpectationHoldAudit.AddAsync(db, nudge.AuditCommentId, nudge.CheckEventIdsJson,
                $"[expectation-operator:{id:D}:{ordinal}] Claim committed for {channel.Provider}; attempt {nudge.OperatorAttemptCount + 1}.",
                ExpectationLedger.AuditAuthor, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // No database transaction, transcript pull or session lock spans broker I/O.
        var current = await db.ExpectationNudges.AsNoTracking().SingleAsync(n => n.Id == id, ct);
        var currentChannel = await db.ChatChannels.AsNoTracking().Where(c => c.Id == directive.OperatorChannelId)
            .Select(c => new { c.Enabled, c.Provider, c.ExternalId }).SingleOrDefaultAsync(ct);
        var currentEpisodes = JsonSerializer.Deserialize<Guid[]>(current.EpisodeIdsJson) ?? [];
        var stillOpen = await db.ExpectationEpisodes.AsNoTracking()
            .AnyAsync(e => currentEpisodes.Contains(e.Id) && e.ResolvedAt == null
                && e.ConfigDigest == digest, ct);
        if (current.OperatorClaimToken != token || current.AnsweredAt is not null
            || current.ConfigDigest != digest || !directive.Enabled
            || directive.ActiveUntilUtc <= time.GetUtcNow() || !stillOpen
            || currentChannel is not { Enabled: true }
            || currentChannel.Provider != current.OperatorPageProvider
            || currentChannel.ExternalId != current.OperatorPageConversationId)
        {
            await SuppressClaimAsync(id, token, ct);
            return false;
        }
        var accepted = false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(5));
            await producer.SendAsync(new ChannelReply
            {
                Channel = current.OperatorPageProvider!,
                ConversationId = current.OperatorPageConversationId,
                Text = current.OperatorPageBody,
            }, budget.Token);
            accepted = true;
            var publishedAt = time.GetUtcNow().UtcDateTime;
            await using var completion = await db.Database.BeginTransactionAsync(ct);
            var stamped = await db.ExpectationNudges.Where(n => n.Id == id && n.OperatorClaimToken == token)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Published)
                    .SetProperty(n => n.OperatorPublishedAt, publishedAt)
                    .SetProperty(n => n.OperatorClaimToken, (Guid?)null)
                    .SetProperty(n => n.OperatorClaimExpiresAt, (DateTime?)null)
                    .SetProperty(n => n.OperatorLastError, (string?)null), ct);
            if (stamped == 1)
                await AuditAsync(id, $"[expectation-operator:{id:D}:{ordinal}] Broker accepted page.", ct);
            await completion.CommitAsync(ct);
            return stamped == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (accepted)
                return false; // Acceptance may have happened; claim expiry retries the same frozen page.
            var failedAt = time.GetUtcNow().UtcDateTime;
            var count = current.OperatorAttemptCount;
            var delay = count <= 1 ? 1 : count == 2 ? 5 : 15;
            await db.ExpectationNudges.Where(n => n.Id == id && n.OperatorClaimToken == token)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.OperatorClaimToken, (Guid?)null)
                    .SetProperty(n => n.OperatorClaimExpiresAt, (DateTime?)null)
                    .SetProperty(n => n.OperatorNextAttemptAt, failedAt.AddMinutes(delay))
                    .SetProperty(n => n.OperatorLastError, ex.GetType().Name), CancellationToken.None);
            await AuditAsync(id, $"[expectation-operator:{id:D}:{ordinal}] Publish failed: {ex.GetType().Name}.",
                CancellationToken.None);
            return false;
        }
    }

    private async Task SuppressClaimAsync(Guid id, Guid token, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ExpectationNudges.Where(n => n.Id == id && n.OperatorClaimToken == token)
            .ExecuteUpdateAsync(u => u
                .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Suppressed)
                .SetProperty(n => n.OperatorClaimToken, (Guid?)null)
                .SetProperty(n => n.OperatorClaimExpiresAt, (DateTime?)null), ct);
        await AuditAsync(id, $"[expectation-operator:{id:D}] Unpublished claim suppressed.", ct);
        await tx.CommitAsync(ct);
    }

    private async Task AuditAsync(Guid id, string text, CancellationToken ct)
    {
        var nudge = await db.ExpectationNudges.AsNoTracking().Where(n => n.Id == id)
            .Select(n => new { n.AuditCommentId, n.CheckEventIdsJson }).SingleAsync(ct);
        await ExpectationHoldAudit.AddAsync(db, nudge.AuditCommentId, nudge.CheckEventIdsJson,
            text, ExpectationLedger.AuditAuthor, time.GetUtcNow().UtcDateTime, ct);
        await db.SaveChangesAsync(ct);
    }

    private static string PageBody(Guid id, int ordinal, ExpectationDirectiveSettings directive,
        Antiphon.Server.Domain.Entities.ExpectationNudge nudge, DateTime now) =>
        $"[expectation-operator:{id:D}:{ordinal}] Directive {directive.Id} has an unanswered watchdog prompt. "
        + $"Created {nudge.CreatedAt:O}; age {(int)(now - nudge.CreatedAt).TotalMinutes} minutes. "
        + $"Prompt={nudge.AttemptState}, receipt={nudge.ReceiptAt?.ToString("O") ?? "none"}, "
        + $"answer={nudge.AnsweredAt?.ToString("O") ?? "none"}. "
        + $"Conditions: {nudge.EvidenceSnapshot}. Audit card {directive.AuditCardId:D}. "
        + $"Inspect GET /api/expectation-watchdog?boardId={directive.BoardId:D}; "
        + "check the caller session and dispatch holds. Do not force recovery without operator evidence.";
}
