using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Concrete (no interface — not an external I/O seam) lookup + resolution helper for
/// configured agent definitions. Owns the conversion from
/// <c>(definitionName, AgentLaunchOptions)</c> to a fully-formed <see cref="AgentLaunchSpec"/>.
/// Adapters never read configuration directly.
/// </summary>
public sealed class AgentRegistry
{
    // Claude Code sets these on its own process; if a spawned agent inherits them it behaves as a
    // NESTED/child session (ignores --session-id, may skip transcript persistence). Overridden to
    // empty for spawned ClaudeCode agents so each is a clean top-level session the tailer can follow.
    private static readonly string[] ClaudeNestingMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    private readonly IOptionsMonitor<AgentRegistrySettings> _options;

    public AgentRegistry(IOptionsMonitor<AgentRegistrySettings> options)
    {
        _options = options;
    }

    public AgentRegistrySettings Settings => _options.CurrentValue;

    public AgentDefinition LookupByName(string definitionName)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
            throw new ArgumentException("Definition name must be provided.", nameof(definitionName));

        var definitions = _options.CurrentValue.Definitions;
        if (!definitions.TryGetValue(definitionName, out var def))
            throw new NotFoundException(nameof(AgentDefinition), definitionName);

        return def;
    }

    public AgentLaunchSpec Resolve(string definitionName, AgentLaunchOptions options)
    {
        var def = LookupByName(definitionName);

        if (!Enum.TryParse<AgentKind>(def.Kind, ignoreCase: true, out var kind))
        {
            throw new InvalidOperationException(
                $"AgentDefinition '{definitionName}' has unknown Kind '{def.Kind}'. Validator should have rejected this at startup.");
        }

        var args = new List<string>(def.ArgsTemplate);
        if (options.ExtraArgs is not null)
            args.AddRange(options.ExtraArgs);

        var env = new Dictionary<string, string>(def.Env, StringComparer.Ordinal);
        if (options.ExtraEnv is not null)
        {
            foreach (var (k, v) in options.ExtraEnv)
                env[k] = v;
        }

        // Claude Code's background auto-updater fails ("Auto-update failed: claude.exe in use") and
        // wedges the agent's TUI when another claude.exe holds the binary (e.g. the orchestrating
        // session). DISABLE_AUTOUPDATER=1 stops the background check entirely (no network call, no
        // "Auto-updating…" UI), so spawned Claude agents start clean. Config/options can still override.
        if (kind == AgentKind.ClaudeCode && !env.ContainsKey("DISABLE_AUTOUPDATER"))
            env["DISABLE_AUTOUPDATER"] = "1";

        // Force the classic (inline) renderer for spawned Claude agents. Fullscreen mode (tui: fullscreen,
        // which a user may set globally in ~/.claude/settings.json) draws on the terminal's alternate screen
        // buffer (\e[?1049h) with complex cursor positioning — our PTY capture + TerminalScreen reconstruction
        // can't faithfully replay that. Today Claude already falls back to classic under our ConPTY, so this
        // is belt-and-suspenders against a future default change; it also trims alt-screen escape sequences,
        // keeping the captured stream cleaner. Config/options can still override.
        if (kind == AgentKind.ClaudeCode && !env.ContainsKey("CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"))
            env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1";

        // Neutralize Claude "nested/child session" markers so a spawned agent is a fresh top-level
        // session, not a child of whatever Claude launched the server. When these are inherited,
        // Claude IGNORES --session-id (forks to a self-chosen id) and may skip transcript
        // persistence — which silently breaks the transcript tailer (it follows <session-id>.jsonl),
        // and with it turn-end detection and channel reply routing. Empty string overrides the
        // inherited value through the pty env merge (it reads as unset). Verified 2026-07-22: without
        // this, dev-launched agents wrote to a self-chosen <uuid>.jsonl and never routed replies.
        if (kind == AgentKind.ClaudeCode)
        {
            foreach (var marker in ClaudeNestingMarkers)
                if (!env.ContainsKey(marker))
                    env[marker] = string.Empty;
        }

        var cwd = string.IsNullOrWhiteSpace(options.Cwd)
            ? Environment.CurrentDirectory
            : options.Cwd;

        if (options.Cols <= 0)
            throw new ArgumentException("Cols must be positive.", nameof(options));
        if (options.Rows <= 0)
            throw new ArgumentException("Rows must be positive.", nameof(options));

        // Resolve a bare exe name to an absolute path when possible. ConPTY resolves bare names
        // against the session cwd (not PATH), and the installed launcher flavor drifts over time
        // (npm claude.cmd vs native claude.exe) — an absolute path sidesteps both. Unresolvable
        // names pass through untouched so fakes/tests and exotic setups keep working; launch paths
        // that need a hard guarantee call AgentExecutableResolver.EnsureSpawnable explicitly.
        var exe = AgentExecutableResolver.Default.TryResolve(def.Exe) ?? def.Exe;

        return new AgentLaunchSpec(
            DefinitionName: definitionName,
            Kind: kind,
            Exe: exe,
            Args: args.AsReadOnly(),
            Env: env,
            Cwd: cwd,
            Cols: options.Cols,
            Rows: options.Rows);
    }
}
