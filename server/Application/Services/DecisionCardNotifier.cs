using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Sends one loud ping when a card is parked for a human decision, then stamps the card.</summary>
public sealed class DecisionCardNotifier
{
    private readonly AppDbContext _db;
    private readonly AttentionService _attention;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<DecisionCardNotifier> _logger;

    public DecisionCardNotifier(AppDbContext db, AttentionService attention, ChatChannelService channels,
        IOptions<DigestSettings> settings, TimeProvider time, ILogger<DecisionCardNotifier> logger)
    { _db = db; _attention = attention; _channels = channels; _settings = settings.Value; _time = time; _logger = logger; }

    public async Task SweepAsync(CancellationToken ct)
    {
        if (!_settings.WakeOnDecision) return;
        var channelIds = await _db.ChatChannels.AsNoTracking().Where(c => c.DigestEnabled).Select(c => c.Id).ToListAsync(ct);
        if (channelIds.Count == 0) return;

        var attention = await _attention.GetAsync(ct);
        var decisions = attention.Items
            .Where(item => item.Kind == AttentionKind.CardNeedsDecision && item.CardId is not null && item.SinceUtc is not null)
            .ToDictionary(item => item.CardId!.Value);
        if (decisions.Count == 0) return;

        var cards = await _db.Cards.Where(card => decisions.Keys.Contains(card.Id)
            && card.Status == CardStatus.NeedsDecision).ToListAsync(ct);
        foreach (var card in cards)
        {
            var item = decisions[card.Id];
            if (card.DecisionNotifiedAt is not null && card.DecisionNotifiedAt >= item.SinceUtc) continue;
            try
            {
                // Send all configured channels before the shared stamp. A failed send leaves the
                // stamp unset, so the next tick retries rather than losing the decision question.
                foreach (var channelId in channelIds)
                    await _channels.SendAsync(channelId, AwayDigestFormatter.FormatDecisionPing(item, _settings), ct);
                card.DecisionNotifiedAt = _time.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Decision card ping for {CardId} failed", card.Id);
            }
        }
    }
}
