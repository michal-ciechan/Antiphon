using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1803. One digest page when a Blocked seat has held past the hour bound and its caller
/// note is still undelivered. Notification only: the seat stays occupied.
/// </summary>
public sealed class BlockedSeatEscalationNotifier
{
    private readonly AppDbContext _db;
    private readonly AttentionService _attention;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _digest;
    private readonly SeatOccupancyState _seats;
    private readonly AttentionSettings _attentionSettings;
    private readonly TimeProvider _time;
    private readonly ILogger<BlockedSeatEscalationNotifier> _logger;

    public BlockedSeatEscalationNotifier(
        AppDbContext db,
        AttentionService attention,
        ChatChannelService channels,
        IOptions<DigestSettings> digest,
        SeatOccupancyState seats,
        IOptions<AttentionSettings> attentionSettings,
        TimeProvider time,
        ILogger<BlockedSeatEscalationNotifier> logger)
    {
        _db = db;
        _attention = attention;
        _channels = channels;
        _digest = digest.Value;
        _seats = seats;
        _attentionSettings = attentionSettings.Value;
        _time = time;
        _logger = logger;
    }

    public Task SweepAsync(CancellationToken ct) =>
        throw new NotImplementedException("CARD-1803");
}
