using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private async Task<List<AttentionItemDto>> BuildChannelOutboundDeliveryItemsAsync(CancellationToken ct)
    {
        var deliveries = await _db.ChannelOutboundDeliveries.AsNoTracking()
            .Include(d => d.Channel)
            .Where(d => d.State == ChannelOutboundDeliveryState.Held
                || d.State == ChannelOutboundDeliveryState.Failed
                || d.State == ChannelOutboundDeliveryState.PublishUncertain)
            .OrderBy(d => d.CreatedAt).Take(100).ToListAsync(ct);
        return deliveries.Select(d => new AttentionItemDto(
            AttentionKind.ChannelOutboundDelivery,
            d.State == ChannelOutboundDeliveryState.PublishUncertain
                ? AlertSeverity.Critical : AlertSeverity.Error,
            d.ConversionTaskId, null, d.ConverterAgentId == Guid.Empty ? null : d.ConverterAgentId,
            null,
            $"Outbound reply {d.State}: {d.Channel.Provider}:{d.Channel.ExternalId}",
            d.FailureReason ?? "An outbound reply needs operator review.",
            $"delivery={d.Id:D}; channel={d.ChannelId:D}; project={d.ProjectId:D}; "
                + $"conversionTask={d.ConversionTaskId?.ToString("D") ?? "none"}; "
                + $"sourceSession={d.SourceSessionId:D}; publicationAttempts={d.PublicationAttempts}",
            d.CreatedAt, null, [AttentionAction.OpenDrawer],
            ConditionKey: $"channel-outbound:{d.Id:N}"))
            .ToList();
    }
}
