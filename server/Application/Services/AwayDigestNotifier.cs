using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Computes and sends due away digests, stamping a channel only after a successful produce.</summary>
public sealed class AwayDigestNotifier
{
    private readonly AppDbContext _db;
    private readonly AwayDigestProjection _projection;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<AwayDigestNotifier> _logger;

    public AwayDigestNotifier(AppDbContext db, AwayDigestProjection projection, ChatChannelService channels,
        IOptions<DigestSettings> settings, TimeProvider time, ILogger<AwayDigestNotifier> logger)
    {
        _db = db; _projection = projection; _channels = channels; _settings = settings.Value; _time = time; _logger = logger;
    }

    public async Task<IReadOnlyList<AwayDigestSendResult>> SendDueAsync(
        Guid? channelId, DateTime? sinceOverride, bool force, CancellationToken ct)
    {
        var query = _db.ChatChannels.Where(c => c.DigestEnabled);
        if (channelId is Guid id) query = query.Where(c => c.Id == id);
        var channels = await query.ToListAsync(ct);
        if (channels.Count == 0)
            return [new AwayDigestSendResult(channelId, false, "no_digest_channel")];

        var results = new List<AwayDigestSendResult>();
        foreach (var channel in channels)
            results.Add(await SendForChannelAsync(channel, sinceOverride, force, ct));
        return results;
    }

    private async Task<AwayDigestSendResult> SendForChannelAsync(
        Domain.Entities.ChatChannel channel, DateTime? sinceOverride, bool force, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_settings.TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        if (!force && !IsDue(channel.DigestLastSentAt, localNow, zone))
            return new AwayDigestSendResult(channel.Id, false, "not_due");
        var first = channel.DigestLastSentAt is null && sinceOverride is null;
        var since = sinceOverride?.ToUniversalTime() ?? channel.DigestLastSentAt ?? now.UtcDateTime.AddHours(-24);
        var digest = await _projection.ComputeAsync(since, now.UtcDateTime, ct);
        digest = digest with { FirstWindow = first };
        var text = AwayDigestFormatter.FormatDigest(digest, _settings, localNow);
        try
        {
            if (text is null && digest.Running.Count > 0) text = AwayDigestFormatter.FormatQuiet(digest, localNow);
            if (text is not null)
                await _channels.SendAsync(channel.Id, text, new ChannelSendOptions(Silent: true), ct);
            channel.DigestLastSentAt = now.UtcDateTime;
            channel.UpdatedAt = now.UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return new AwayDigestSendResult(channel.Id, text is not null);
        }
        catch (ConflictException ex) when (ex.Code == "channel_disabled")
        {
            _logger.LogWarning("Away digest to channel {ChannelId} not sent: channel disabled", channel.Id);
            return new AwayDigestSendResult(channel.Id, false, "channel_disabled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Away digest to channel {ChannelId} failed", channel.Id);
            return new AwayDigestSendResult(channel.Id, false, "send_failed");
        }
    }

    private bool IsDue(DateTime? lastSentUtc, DateTimeOffset localNow, TimeZoneInfo zone)
    {
        var last = lastSentUtc is null ? DateTimeOffset.MinValue : TimeZoneInfo.ConvertTime(new DateTimeOffset(lastSentUtc.Value, TimeSpan.Zero), zone);
        foreach (var raw in _settings.SendTimesLocal)
        {
            if (!TimeOnly.TryParse(raw, out var time)) continue;
            var dueLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
            var due = new DateTimeOffset(dueLocal, zone.GetUtcOffset(dueLocal));
            if (localNow >= due && last < due) return true;
        }
        return false;
    }
}
