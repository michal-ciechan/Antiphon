using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.Tools;

namespace Antiphon.Server.Infrastructure.Agents;

internal sealed class EventEmittingToolWrapper : IAgentTool
{
    private readonly IAgentTool _inner;
    private readonly IEventBus _eventBus;
    private readonly AuditService _auditService;
    private readonly string _groupName;
    private readonly Guid _workflowId;
    private readonly Guid _stageId;
    private readonly Guid _stageExecutionId;
    private readonly Action _onToolInvoked;

    public EventEmittingToolWrapper(
        IAgentTool inner,
        IEventBus eventBus,
        AuditService auditService,
        string groupName,
        Guid workflowId,
        Guid stageId,
        Guid stageExecutionId,
        Action onToolInvoked)
    {
        _inner = inner;
        _eventBus = eventBus;
        _auditService = auditService;
        _groupName = groupName;
        _workflowId = workflowId;
        _stageId = stageId;
        _stageExecutionId = stageExecutionId;
        _onToolInvoked = onToolInvoked;
    }

    public string Name => _inner.Name;
    public string Description => _inner.Description;
    public string ParametersSchema => _inner.ParametersSchema;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        _onToolInvoked();

        await _eventBus.PublishToGroupAsync(_groupName, "AgentToolCall", new
        {
            workflowId = _workflowId,
            stageId = _stageId,
            toolName = _inner.Name,
            toolInput = jsonInput,
            timestamp = DateTime.UtcNow
        }, ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string toolOutput;
        try
        {
            toolOutput = await _inner.ExecuteAsync(jsonInput, ct);
        }
        catch (Exception ex)
        {
            toolOutput = $"Error: {ex.Message}";
        }
        sw.Stop();

        await _auditService.RecordToolInvocationAsync(
            _workflowId,
            _stageId,
            _stageExecutionId,
            _inner.Name,
            sw.ElapsedMilliseconds,
            clientIp: null,
            userId: null,
            fullContentJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                tool = _inner.Name,
                input = jsonInput.Length > 2000 ? jsonInput[..2000] + "..." : jsonInput,
                output = toolOutput.Length > 2000 ? toolOutput[..2000] + "..." : toolOutput
            }),
            ct
        );

        await _eventBus.PublishToGroupAsync(_groupName, "AgentToolResult", new
        {
            workflowId = _workflowId,
            stageId = _stageId,
            toolName = _inner.Name,
            toolOutput = toolOutput.Length > 2000 ? toolOutput[..2000] + "..." : toolOutput,
            timestamp = DateTime.UtcNow
        }, ct);

        return toolOutput;
    }
}
