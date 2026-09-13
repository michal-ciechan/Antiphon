using System.Text.Json;
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
/// CARD-0418: ordinary Worker/Custom task for opted-in outbound conversion. Not a specialist seat.
/// </summary>
public sealed class OutboundConversionTaskRunner(
    AppDbContext db,
    AgentTaskService tasks,
    ChannelOutboundPolicy policy,
    IChannelOutboundFileStore store,
    IOptions<ChannelOutboundSettings> settings,
    TimeProvider clock,
    ILogger<OutboundConversionTaskRunner> logger)
{
    public async Task<Guid?> TryDispatchAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.ConversionTaskId is Guid existing)
            return existing;
        if (string.IsNullOrWhiteSpace(delivery.ProfileName))
            return null;

        var profile = policy.TryGetProfile(delivery.ProfileName);
        if (profile is null)
            return null;

        var converter = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == profile.AgentId, ct);
        if (converter is null)
            return null;

        var globalConverting = await db.ChannelOutboundDeliveries.CountAsync(
            d => d.State == ChannelOutboundDeliveryState.Converting && d.ConversionTaskId != null, ct);
        if (globalConverting >= settings.Value.GlobalConversionLimit)
            return null;

        var sameAgent = await db.ChannelOutboundDeliveries.CountAsync(
            d => d.ConversionTaskId != null
                && d.State == ChannelOutboundDeliveryState.Converting
                && d.ProfileName == delivery.ProfileName, ct);
        if (sameAgent >= 1)
            return null;

        var now = clock.GetUtcNow().UtcDateTime;
        if (delivery.DeadlineAt <= now)
            return null;

        var workerDir = await store.WorkerDirectoryAsync(delivery.Id, ct);
        var requestPath = Path.Combine(store.InputDirectory(delivery.Id), "request.json");
        var goal =
            "Convert the frozen outbound request at the given path. Read input/request.json, write "
            + "output/manifest.json using the generic v1 contract, then end with the normal report token. "
            + "Do not dispatch child tasks. Do not change Channel, ConversationId, ReplyHandle, or Kind. "
            + $"delivery={delivery.Id:D} request={requestPath} output={Path.Combine(workerDir, "output")}";

        var created = await tasks.CreateOutboundConversionAsync(
            new CreateAgentTaskRequest(
                Goal: goal,
                Title: "Outbound conversion",
                Kind: AgentTaskKind.Worker,
                Role: AgentTaskRole.Custom,
                Workspace: WorkspaceMode.Shared,
                WorkingDirectory: converter.WorkingDirectory,
                AgentId: converter.Id),
            new AgentTaskService.Caller(
                Task: null,
                SessionId: null,
                WorkingDirectory: converter.WorkingDirectory,
                ProjectId: profile.ProjectId),
            new OutboundConversionCreateContext(delivery.Id, delivery.DeadlineAt),
            ct);

        delivery.ConversionTaskId = created.Id;
        delivery.State = ChannelOutboundDeliveryState.Converting;
        delivery.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Dispatched outbound conversion task {TaskId} for delivery {DeliveryId}",
            created.Id, delivery.Id);
        return created.Id;
    }
}

public sealed record OutboundConversionCreateContext(Guid OutboundDeliveryId, DateTime ExecutionDeadlineAt);