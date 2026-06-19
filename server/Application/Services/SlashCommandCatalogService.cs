using System.Collections.Concurrent;
using System.IO.Abstractions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Builds the slash-command autocomplete catalog for an agent session: built-in Claude commands plus
/// custom commands (<c>.claude/commands/**/*.md</c>) and skills (<c>.claude/skills/*/SKILL.md</c>) from
/// both the user scope (<c>~/.claude</c>) and the project scope (<c>&lt;session cwd&gt;/.claude</c>).
///
/// Modeled on <see cref="DirectoryBrowseService"/>: singleton, short-TTL cache keyed on the (user,
/// project) directory pair (a session's cwd has ~100+ project skills — re-reading on every keystroke
/// would be far too slow), <see cref="SemaphoreSlim"/>-guarded refresh, injected <see cref="TimeProvider"/>
/// for deterministic expiry, and <see cref="IResettableCache"/> so the shared test host can clear it.
/// </summary>
public sealed class SlashCommandCatalogService : IResettableCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClaudeConfigDirProvider _configDir;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SlashCommandCatalogService> _logger;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly record struct CacheEntry(IReadOnlyList<SlashCommandDto> Commands, DateTimeOffset CapturedAt);

    public SlashCommandCatalogService(
        IServiceScopeFactory scopeFactory,
        IClaudeConfigDirProvider configDir,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<SlashCommandCatalogService> logger)
    {
        _scopeFactory = scopeFactory;
        _configDir = configDir;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Catalog for a session, resolving its cwd (project scope) from the database.</summary>
    public async Task<IReadOnlyList<SlashCommandDto>> GetCommandsAsync(Guid sessionId, CancellationToken ct)
    {
        string cwd;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                ?? throw new NotFoundException(nameof(AgentSession), sessionId);
            cwd = session.Cwd;
        }

        var userDir = _configDir.Resolve();
        var projectDir = string.IsNullOrWhiteSpace(cwd) ? null : _fileSystem.Path.Combine(cwd, ".claude");
        return await GetForDirsAsync(userDir, projectDir, ct);
    }

    /// <summary>
    /// Catalog for an explicit (user, project) <c>.claude</c> directory pair. Public so it's unit-testable
    /// against a <c>MockFileSystem</c> without a database. <paramref name="projectClaudeDir"/> may be null
    /// (no project scope).
    /// </summary>
    public async Task<IReadOnlyList<SlashCommandDto>> GetForDirsAsync(
        string userClaudeDir, string? projectClaudeDir, CancellationToken ct)
    {
        var key = (userClaudeDir ?? string.Empty) + "|" + (projectClaudeDir ?? string.Empty);

        if (TryGetFresh(key, out var cached))
            return cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (TryGetFresh(key, out cached))
                return cached;

            var commands = Enumerate(userClaudeDir, projectClaudeDir);
            _cache[key] = new CacheEntry(commands, _timeProvider.GetUtcNow());
            return commands;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Clear() => _cache.Clear();

    private IReadOnlyList<SlashCommandDto> Enumerate(string? userDir, string? projectDir)
    {
        var all = new List<SlashCommandDto>();
        all.AddRange(BuiltInCommands);
        all.AddRange(EnumerateCommands(userDir, scope: "user"));
        all.AddRange(EnumerateCommands(projectDir, scope: "project"));
        all.AddRange(EnumerateSkills(userDir, scope: "user"));
        all.AddRange(EnumerateSkills(projectDir, scope: "project"));

        // De-dup by name (case-insensitive): a project definition overrides a user one, and within a
        // scope command > skill; built-ins sit between project and user (they beat a user skill but
        // yield to anything the project defines). Then sort by name for a stable list.
        return all
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(Priority).First())
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Lower wins.
    private static int Priority(SlashCommandDto c) => (c.Scope, c.Source) switch
    {
        ("project", "command") => 0,
        ("project", "skill") => 1,
        ("builtin", _) => 2,
        ("user", "command") => 3,
        ("user", "skill") => 4,
        _ => 5,
    };

    private IEnumerable<SlashCommandDto> EnumerateCommands(string? claudeDir, string scope)
    {
        if (string.IsNullOrWhiteSpace(claudeDir))
            yield break;

        var commandsRoot = _fileSystem.Path.Combine(claudeDir, "commands");
        if (!SafeDirectoryExists(commandsRoot))
            yield break;

        foreach (var file in SafeEnumerateFiles(commandsRoot, "*.md"))
        {
            string name;
            try
            {
                var relative = _fileSystem.Path.GetRelativePath(commandsRoot, file);
                var noExt = relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    ? relative[..^3]
                    : relative;
                // Subfolders namespace the command: git/commit.md -> /git:commit. Split on both
                // separators since the filesystem yields OS-native slashes.
                var segments = noExt.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    continue;
                name = "/" + string.Join(":", segments);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to derive command name for {File}", file);
                continue;
            }

            var description = SlashCommandParser.Describe(SafeReadAllText(file), name[1..]);
            yield return new SlashCommandDto(name, description, "command", scope);
        }
    }

    private IEnumerable<SlashCommandDto> EnumerateSkills(string? claudeDir, string scope)
    {
        if (string.IsNullOrWhiteSpace(claudeDir))
            yield break;

        var skillsRoot = _fileSystem.Path.Combine(claudeDir, "skills");
        if (!SafeDirectoryExists(skillsRoot))
            yield break;

        foreach (var skillDir in SafeEnumerateDirectories(skillsRoot))
        {
            var skillMd = _fileSystem.Path.Combine(skillDir, "SKILL.md");
            if (!SafeFileExists(skillMd))
                continue;

            // Claude invokes a skill by its directory name, even if frontmatter `name:` differs.
            var folderName = _fileSystem.Path.GetFileName(skillDir.TrimEnd('/', '\\'));
            if (string.IsNullOrWhiteSpace(folderName))
                continue;

            var description = SlashCommandParser.Describe(SafeReadAllText(skillMd), folderName);
            yield return new SlashCommandDto("/" + folderName, description, "skill", scope);
        }
    }

    private bool TryGetFresh(string key, out IReadOnlyList<SlashCommandDto> commands)
    {
        if (_cache.TryGetValue(key, out var entry)
            && _timeProvider.GetUtcNow() - entry.CapturedAt <= CacheTtl)
        {
            commands = entry.Commands;
            return true;
        }

        commands = [];
        return false;
    }

    private bool SafeDirectoryExists(string path)
    {
        try { return _fileSystem.Directory.Exists(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "Directory.Exists failed for {Path}", path); return false; }
    }

    private bool SafeFileExists(string path)
    {
        try { return _fileSystem.File.Exists(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "File.Exists failed for {Path}", path); return false; }
    }

    private IReadOnlyList<string> SafeEnumerateFiles(string root, string pattern)
    {
        try { return _fileSystem.Directory.GetFiles(root, pattern, SearchOption.AllDirectories); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetFiles failed for {Root}", root); return []; }
    }

    private IReadOnlyList<string> SafeEnumerateDirectories(string root)
    {
        try { return _fileSystem.Directory.GetDirectories(root); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetDirectories failed for {Root}", root); return []; }
    }

    private string SafeReadAllText(string path)
    {
        try { return _fileSystem.File.ReadAllText(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "ReadAllText failed for {Path}", path); return string.Empty; }
    }

    // Curated list of Claude Code built-in slash commands (not on disk). Kept small + extendable.
    private static readonly IReadOnlyList<SlashCommandDto> BuiltInCommands =
    [
        new("/clear", "Clear the conversation history", "builtin", "builtin"),
        new("/compact", "Summarize and compact the conversation", "builtin", "builtin"),
        new("/config", "Open the configuration panel", "builtin", "builtin"),
        new("/cost", "Show token usage and cost for this session", "builtin", "builtin"),
        new("/doctor", "Diagnose and report on the Claude Code install", "builtin", "builtin"),
        new("/help", "List available commands and usage", "builtin", "builtin"),
        new("/init", "Generate a CLAUDE.md for the project", "builtin", "builtin"),
        new("/login", "Sign in to your Anthropic account", "builtin", "builtin"),
        new("/logout", "Sign out of your Anthropic account", "builtin", "builtin"),
        new("/mcp", "Manage MCP server connections", "builtin", "builtin"),
        new("/memory", "Edit Claude's memory files", "builtin", "builtin"),
        new("/model", "Choose the model for this session", "builtin", "builtin"),
        new("/permissions", "View or edit tool permissions", "builtin", "builtin"),
        new("/resume", "Resume a previous conversation", "builtin", "builtin"),
        new("/review", "Review a pull request", "builtin", "builtin"),
        new("/status", "Show the current session status", "builtin", "builtin"),
        new("/terminal-setup", "Install terminal key bindings", "builtin", "builtin"),
        new("/vim", "Toggle vim editing mode", "builtin", "builtin"),
    ];
}
