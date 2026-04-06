using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Antiphon.Server.Infrastructure.Agents.Tools;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Adapts an <see cref="IAgentTool"/> to the <see cref="AIFunction"/> contract so that
/// the LLM sees the correct JSON parameter schema (from ParametersSchema) rather than
/// a single opaque "input: string" parameter.
/// </summary>
internal sealed class AgentToolAIFunction : AIFunction
{
    private readonly IAgentTool _tool;
    private readonly JsonElement _schema;

    public AgentToolAIFunction(IAgentTool tool)
    {
        _tool = tool;
        _schema = JsonDocument.Parse(tool.ParametersSchema).RootElement.Clone();
    }

    public override string Name => _tool.Name;
    public override string Description => _tool.Description;
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Re-serialize the arguments dictionary back to a JSON string so that
        // IAgentTool.ExecuteAsync receives the format it expects (e.g. {"command":"git clone ..."}).
        // Values may arrive as JsonElement, primitives, or strings depending on how the
        // FunctionInvokingChatClient deserializes the LLM's tool-call payload.
        var node = new JsonObject();
        foreach (var (key, value) in arguments)
        {
            node[key] = value switch
            {
                JsonElement el => JsonNode.Parse(el.GetRawText()),
                null => null,
                _ => JsonValue.Create(value)
            };
        }

        var json = node.ToJsonString();
        return await _tool.ExecuteAsync(json, cancellationToken);
    }
}
