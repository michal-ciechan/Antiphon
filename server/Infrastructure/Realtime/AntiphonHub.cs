using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace Antiphon.Server.Infrastructure.Realtime;

/// <summary>
/// Single SignalR hub for all real-time communication.
/// Clients join/leave groups to receive targeted events.
/// Services NEVER call this hub directly — they use IEventBus.
/// </summary>
public class AntiphonHub : Hub
{
    private static readonly Serilog.ILogger _logger = Log.ForContext<AntiphonHub>();
    private readonly IAuditService _auditService;
    private readonly AgentChannelService _agentChannelService;
    private readonly SessionTerminalAttachments _attachments;
    private readonly AppDbContext _db;

    public AntiphonHub(IAuditService auditService, AgentChannelService agentChannelService,
        SessionTerminalAttachments attachments, AppDbContext db)
    {
        _auditService = auditService;
        _agentChannelService = agentChannelService;
        _attachments = attachments;
        _db = db;
    }

    public async Task JoinGroup(string groupName)
    {
        var session = SessionGroupId(groupName);
        if (session is Guid id)
        {
            if (!_attachments.TryAttach(id, Context.ConnectionId, out var added))
                throw new HubException(SessionTerminalAttachments.StopInFlightMessage);
            try
            {
                if (await SessionStopAudit.IsPendingAsync(_db, id, Context.ConnectionAborted))
                    throw new HubException(SessionTerminalAttachments.StopInFlightMessage);
            }
            catch
            {
                if (added) _attachments.Detach(id, Context.ConnectionId);
                throw;
            }
        }
        // If adding the group fails, retain the attachment until LeaveGroup or disconnect.
        // A failed repeat join cannot prove an earlier membership is gone.
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} joined group {Group}", Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        if (SessionGroupId(groupName) is Guid id) _attachments.Detach(id, Context.ConnectionId);
        _logger.Information("Connection {ConnectionId} left group {Group}", Context.ConnectionId, groupName);
    }

    public async Task JoinCard(Guid cardId)
    {
        var groupName = AgentChannelGroups.Card(cardId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} joined agent card group {Group}", Context.ConnectionId, groupName);
    }

    public async Task LeaveCard(Guid cardId)
    {
        var groupName = AgentChannelGroups.Card(cardId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} left agent card group {Group}", Context.ConnectionId, groupName);
    }

    public async Task SendAsync(Guid targetSessionId, ChannelMessageRequest request)
    {
        await _agentChannelService.SendToSessionAsync(
            sourceSessionId: null,
            targetSessionId,
            request.Message,
            Context.ConnectionAborted);
    }

    public async Task<SpawnCardResult> DelegateCard(ChannelDelegateCardRequest request)
    {
        return await _agentChannelService.DelegateCardAsync(request, Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.Information("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _attachments.Disconnect(Context.ConnectionId);
        _logger.Information("Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);

        await _auditService.RecordEventAsync(
            AuditEventType.SessionDisconnected,
            workflowId: null,
            stageId: null,
            stageExecutionId: null,
            summary: $"Client disconnected: {Context.ConnectionId}",
            clientIp: null,
            userId: null,
            gitTagName: null,
            fullContentJson: null,
            CancellationToken.None
        );

        await base.OnDisconnectedAsync(exception);
    }

    private static Guid? SessionGroupId(string groupName) =>
        groupName.StartsWith("session-", StringComparison.Ordinal)
        && Guid.TryParseExact(groupName.AsSpan("session-".Length), "D", out var id) ? id : null;
}
