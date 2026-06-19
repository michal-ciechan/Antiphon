namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Resolves the Claude Code user config directory (where <c>~/.claude/skills</c> and
/// <c>~/.claude/commands</c> live). A seam so it can be faked in tests. Mirrors how
/// <c>TranscriptTailer.ResolveProjectsRoot()</c> finds the root: <c>CLAUDE_CONFIG_DIR</c> if set,
/// otherwise <c>%UserProfile%/.claude</c>.
/// </summary>
public interface IClaudeConfigDirProvider
{
    /// <summary>The user-scope <c>.claude</c> directory (not the <c>/projects</c> subfolder).</summary>
    string Resolve();
}
