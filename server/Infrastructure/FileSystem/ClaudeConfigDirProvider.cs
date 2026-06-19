using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.FileSystem;

/// <summary>
/// Default <see cref="IClaudeConfigDirProvider"/>: honours <c>CLAUDE_CONFIG_DIR</c> if set, else falls
/// back to <c>%UserProfile%/.claude</c> — the same resolution Claude Code uses and that
/// <c>TranscriptTailer.ResolveProjectsRoot()</c> mirrors.
/// </summary>
public sealed class ClaudeConfigDirProvider : IClaudeConfigDirProvider
{
    public string Resolve()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(configDir)
            ? configDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }
}
