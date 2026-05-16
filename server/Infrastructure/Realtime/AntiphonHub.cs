using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
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
    private readonly AuditService _auditService;
    private readonly AgentChannelService _agentChannelService;

    public AntiphonHub(AuditService auditService, AgentChannelService agentChannelService)
    {
        _auditService = auditService;
        _agentChannelService = agentChannelService;
    }

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} joined group {Group}", Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
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
}
