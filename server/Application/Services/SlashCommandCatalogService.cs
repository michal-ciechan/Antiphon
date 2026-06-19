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
        all.AddRange(EnumeratePlugins(userDir));

        // De-dup by name (case-insensitive): a project definition overrides a user one, and within a
        // scope command > skill; built-ins/plugins sit between project and user. Then sort by name.
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
        ("plugin", "command") => 2,
        ("plugin", "skill") => 3,
        ("builtin", _) => 4,
        ("user", "command") => 5,
        ("user", "skill") => 6,
        _ => 7,
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
                // separators since the filesystem yields OS-native slashes; slugify each segment.
                var segments = noExt
                    .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Slugify)
                    .Where(s => s.Length > 0)
                    .ToArray();
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

            // Claude invokes a skill by its directory name (slugified), even if frontmatter `name:` differs.
            var folderName = _fileSystem.Path.GetFileName(skillDir.TrimEnd('/', '\\'));
            var slug = Slugify(folderName);
            if (slug.Length == 0)
                continue;

            var description = SlashCommandParser.Describe(SafeReadAllText(skillMd), folderName);
            yield return new SlashCommandDto("/" + slug, description, "skill", scope);
        }
    }

    // Installed Claude plugins contribute slash-commands + skills too (e.g. /frontend-design,
    // /memory-recall). They live at the installPath recorded in <userDir>/plugins/installed_plugins.json,
    // each with commands/ and skills/ subdirs. Claude surfaces them by the plain (slugified) leaf name.
    private IEnumerable<SlashCommandDto> EnumeratePlugins(string? userDir)
    {
        if (string.IsNullOrWhiteSpace(userDir))
            yield break;

        var manifest = _fileSystem.Path.Combine(userDir, "plugins", "installed_plugins.json");
        if (!SafeFileExists(manifest))
            yield break;

        foreach (var installPath in ReadPluginInstallPaths(SafeReadAllText(manifest)))
        {
            foreach (var c in EnumerateCommands(installPath, scope: "plugin"))
                yield return c with { Source = "command" };
            foreach (var s in EnumerateSkills(installPath, scope: "plugin"))
                yield return s with { Source = "skill" };
        }
    }

    private IEnumerable<string> ReadPluginInstallPaths(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
            return [];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("plugins", out var plugins)
                || plugins.ValueKind != System.Text.Json.JsonValueKind.Object)
                return [];

            var paths = new List<string>();
            foreach (var plugin in plugins.EnumerateObject())
            {
                if (plugin.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                    continue;
                foreach (var install in plugin.Value.EnumerateArray())
                {
                    if (install.TryGetProperty("installPath", out var p)
                        && p.ValueKind == System.Text.Json.JsonValueKind.String
                        && p.GetString() is { Length: > 0 } path)
                    {
                        paths.Add(path);
                    }
                }
            }
            return paths;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse installed_plugins.json");
            return [];
        }
    }

    // Mirror how Claude derives a slash-command token from a folder/file name: lower-case, runs of
    // non-alphanumerics collapse to a single hyphen, trimmed. "Object Type Router" -> "object-type-router".
    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var sb = new System.Text.StringBuilder(name.Length);
        var pendingHyphen = false;
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && sb.Length > 0)
                    sb.Append('-');
                pendingHyphen = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingHyphen = true;
            }
        }
        return sb.ToString();
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

    // Curated list of Claude Code built-in slash commands + bundled (ship-with-Claude) skills that
    // are not on disk under ~/.claude or a project. This set is Claude-version-specific; the headed
    // reconciliation test (SlashCommandMenuReconciliationTests) flags drift against the live `/` menu.
    private static readonly IReadOnlyList<SlashCommandDto> BuiltInCommands = BuildBuiltIns();

    private static IReadOnlyList<SlashCommandDto> BuildBuiltIns()
    {
        // name -> short description. Names are the reconciliation key; descriptions are UI-only.
        var entries = new (string Name, string Desc)[]
        {
            // System commands
            ("/add-dir", "Add a new working directory"),
            ("/advisor", "Let Claude consult a stronger model at key moments"),
            ("/agents", "Manage agent configurations"),
            ("/autofix-pr", "Monitor and autofix issues with the current PR"),
            ("/background", "Send this session to the background"),
            ("/batch", "Run a batch of prompts"),
            ("/branch", "Create a branch of the conversation at this point"),
            ("/btw", "Ask a quick side question without interrupting"),
            ("/cd", "Change the working directory"),
            ("/chrome", "Open Claude in Chrome (beta) settings"),
            ("/clear", "Clear the conversation history"),
            ("/color", "Set the prompt bar color for this session"),
            ("/compact", "Summarize and compact the conversation"),
            ("/config", "Open the configuration panel"),
            ("/context", "Show the current context usage"),
            ("/copy", "Copy the last response"),
            ("/cost", "Show token usage and cost for this session"),
            ("/debug", "Toggle debug output"),
            ("/desktop", "Open the Claude desktop app"),
            ("/diff", "Show changes in the working tree"),
            ("/doctor", "Diagnose and report on the Claude Code install"),
            ("/effort", "Set the reasoning effort level"),
            ("/exit", "Exit Claude Code"),
            ("/export", "Export the conversation"),
            ("/fast", "Toggle fast mode"),
            ("/feedback", "Send feedback to Anthropic"),
            ("/focus", "Focus the conversation"),
            ("/fork", "Fork the current session"),
            ("/goal", "Set or show the session goal"),
            ("/help", "List available commands and usage"),
            ("/hooks", "Manage hooks"),
            ("/ide", "Manage IDE integration"),
            ("/init", "Generate a CLAUDE.md for the project"),
            ("/insights", "Show session insights"),
            ("/install-github-app", "Install the GitHub app"),
            ("/install-slack-app", "Install the Slack app"),
            ("/login", "Sign in to your Anthropic account"),
            ("/logout", "Sign out of your Anthropic account"),
            ("/mcp", "Manage MCP server connections"),
            ("/memory", "Edit Claude's memory files"),
            ("/mobile", "Connect a mobile device"),
            ("/model", "Choose the model for this session"),
            ("/passes", "Manage passes"),
            ("/permissions", "View or edit tool permissions"),
            ("/plan", "Enter plan mode"),
            ("/plugin", "Manage plugins"),
            ("/powerup", "Apply a power-up"),
            ("/privacy-settings", "Open privacy settings"),
            ("/radio", "Toggle radio"),
            ("/recap", "Recap the conversation"),
            ("/release-notes", "Show release notes"),
            ("/reload-plugins", "Reload installed plugins"),
            ("/reload-skills", "Reload skills"),
            ("/remote-control", "Enable remote control of this session"),
            ("/remote-env", "Configure the remote environment"),
            ("/rename", "Rename the session"),
            ("/resume", "Resume a previous conversation"),
            ("/review", "Review a pull request"),
            ("/rewind", "Rewind the conversation"),
            ("/skills", "Manage skills"),
            ("/status", "Show the current session status"),
            ("/statusline", "Configure the status line"),
            ("/stickers", "Claim Claude Code stickers"),
            ("/tasks", "Manage background tasks"),
            ("/team-onboarding", "Set up Claude Code for your team"),
            ("/teleport", "Jump to a point in the conversation"),
            ("/terminal-setup", "Install terminal key bindings"),
            ("/theme", "Change the theme"),
            ("/tui", "Toggle TUI options"),
            ("/ultraplan", "Run an ultra plan"),
            ("/ultrareview", "Run an ultra review"),
            ("/upgrade", "Upgrade your plan"),
            ("/usage", "Show usage"),
            ("/usage-credits", "Show usage credits"),
            ("/vim", "Toggle vim editing mode"),
            ("/voice", "Toggle voice input"),
            ("/web-setup", "Set up Claude on the web"),
            ("/workflows", "Manage workflows"),
            // Bundled skills (ship with Claude Code, invocable via `/`)
            ("/claude-api", "Reference for the Claude API / Anthropic SDK"),
            ("/code-review", "Review the current diff for issues"),
            ("/deep-research", "Fan-out web research with cited synthesis"),
            ("/design-login", "Design a login experience"),
            ("/design-sync", "Sync design assets"),
            ("/fewer-permission-prompts", "Reduce permission prompts via an allowlist"),
            ("/keybindings", "Customize keyboard shortcuts"),
            ("/loop", "Run a prompt on a recurring interval"),
            ("/run", "Launch and drive the project's app"),
            ("/run-skill-generator", "Generate a new skill"),
            ("/schedule", "Schedule recurring cloud agents"),
            ("/security-review", "Security review of pending changes"),
            ("/simplify", "Simplify the changed code"),
            ("/update-config", "Configure the Claude Code harness"),
            ("/verify", "Verify a change works by running the app"),
        };

        return entries.Select(e => new SlashCommandDto(e.Name, e.Desc, "builtin", "builtin")).ToList();
    }
}
