using Antiphon.Server.Infrastructure.Agents.Tools;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Discovers and registers agent tools scoped to a specific worktree (AR7).
/// Creates a fresh set of tools for each agent execution so that the worktree path
/// is correctly bound.
/// </summary>
public class ToolRegistry
{
    /// <summary>
    /// Creates the standard set of agent tools scoped to the given worktree root.
    /// </summary>
    public IReadOnlyList<IAgentTool> CreateTools(string worktreeRoot)
    {
        return
        [
            new FileReadTool(worktreeRoot),
            new FileWriteTool(worktreeRoot),
            new FileEditTool(worktreeRoot),
            new BashTool(worktreeRoot),
            new GlobTool(worktreeRoot),
            new GrepTool(worktreeRoot),
            new GitTool(worktreeRoot)
        ];
    }

    /// <summary>
    /// Finds a tool by name from the given collection. Returns null if not found.
    /// </summary>
    public static IAgentTool? FindTool(IReadOnlyList<IAgentTool> tools, string name)
    {
        for (var i = 0; i < tools.Count; i++)
        {
            if (string.Equals(tools[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return tools[i];
        }
        return null;
    }
}
