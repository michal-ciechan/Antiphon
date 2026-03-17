namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Common interface for all agent tools (AR7). Each tool has a name, description,
/// and an Execute method. All file-based tools must scope operations to the worktree
/// and block path traversal (NFR8).
/// </summary>
public interface IAgentTool
{
    /// <summary>Unique tool name used by the LLM to invoke this tool.</summary>
    string Name { get; }

    /// <summary>Human-readable description sent to the LLM as tool documentation.</summary>
    string Description { get; }

    /// <summary>JSON schema describing the tool's input parameters.</summary>
    string ParametersSchema { get; }

    /// <summary>
    /// Executes the tool with the given JSON input and returns the result as a string.
    /// </summary>
    Task<string> ExecuteAsync(string jsonInput, CancellationToken ct);
}
