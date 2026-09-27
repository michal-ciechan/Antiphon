using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Creates one ordinary, pinned Worker task with an internal outbound purpose link.</summary>
public sealed class OutboundConversionTaskRunner
{
    private readonly AppDbContext _db;
    private readonly AgentTaskService _tasks;

    public OutboundConversionTaskRunner(AppDbContext db, AgentTaskService tasks)
    {
        _db = db;
        _tasks = tasks;
    }

    public async Task<Guid> CreateAsync(ChannelOutboundDelivery delivery, CancellationToken ct)
    {
        if (delivery.ConversionTaskId is Guid existing)
            return existing;
        var converter = await _db.Agents.Include(a => a.Board)
            .SingleAsync(a => a.Id == delivery.ConverterAgentId, ct);
        if (converter.Board?.ProjectId != delivery.ProjectId || converter.IsPoolDelegate
            || converter.AlwaysOn || !AgentTaskService.DelegatableKinds.Contains(converter.Kind))
            throw new InvalidOperationException("The pinned conversion agent is unavailable for this project.");
        var requestPath = Path.Combine(Path.GetDirectoryName(delivery.InputPath)!, "request.json");
        if (!File.Exists(requestPath))
            throw new InvalidDataException("The frozen conversion request is missing.");
        var goal = $"Outbound preparation for delivery {delivery.Id:D}.\n"
            + "Read the immutable request JSON at: " + requestPath + "\n"
            + "Write output/manifest.json beside that request and put any additional files under output/. "
            + "Use manifest version 1 with deliveryId, disposition (unchanged or converted), optional replacementText, "
            + "and files with relative path, name, mime, length and sha256. Preserve the original sources. "
            + "Do not dispatch child tasks or send to a channel. End with the ordinary report token.\n\n"
            + delivery.PromptText;
        if (goal.Length > 20_000)
            throw new InvalidDataException("The conversion prompt exceeds the task goal limit.");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var created = await _tasks.CreateAsync(new CreateAgentTaskRequest(
            Goal: goal,
            Title: "Outbound conversion " + delivery.Id.ToString("N")[..8],
            Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Custom,
            AgentKind: converter.Kind,
            ModelLevel: converter.ModelLevel,
            Workspace: WorkspaceMode.Shared,
            WorkingDirectory: converter.WorkingDirectory,
            AgentId: converter.Id,
            AutoContinue: false,
            CommitOnSettle: "Never"),
            new AgentTaskService.Caller(null, null, converter.WorkingDirectory,
                ProjectId: delivery.ProjectId, BoardId: converter.BoardId), ct);
        var task = await _db.AgentTasks.SingleAsync(t => t.Id == created.Id, ct);
        task.OutboundDeliveryId = delivery.Id;
        task.ExecutionDeadlineAt = delivery.DeadlineAt;
        task.MaxAttempts = 1;
        delivery.ConversionTaskId = task.Id;
        delivery.State = ChannelOutboundDeliveryState.Converting;
        delivery.Version++;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return task.Id;
    }
}
