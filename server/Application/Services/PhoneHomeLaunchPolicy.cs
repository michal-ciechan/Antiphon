using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class PhoneHomeLaunchPolicy
{
    private readonly PhoneHomeRunnerSettings _settings;

    public PhoneHomeLaunchPolicy(IOptions<PhoneHomeRunnerSettings> settings) =>
        _settings = settings.Value;

    /// <summary>
    /// CARD-0490's one pinned cardless agent. Kept as its own predicate because that agent keeps
    /// its exact projection (grok at <see cref="PhoneHomeRunnerSettings.RunnerWorkspace"/>, no
    /// worktree, no task) even on a runner that now also carries a pool.
    /// </summary>
    public bool IsPinnedAgent(Guid agentId) =>
        _settings.Enabled && agentId == _settings.StandingAgentId && _settings.StandingAgentId != Guid.Empty;

    /// <summary>
    /// CARD-0604 D-2/D-14. Any agent whose row names the allowed runner: the pinned agent, an
    /// operator-created named agent with <c>RunnerId</c> set, or a pool delegate the dispatcher
    /// created for a runner-bound task. Everything the runner refuses is refused for all of them.
    /// </summary>
    public bool IsRunnerBound(Agent agent) =>
        IsPinnedAgent(agent.Id) || IsRunnerBound(agent.RunnerId);

    public bool IsRunnerBound(string? runnerId) =>
        _settings.Enabled
        && !string.IsNullOrWhiteSpace(runnerId)
        && string.Equals(runnerId, _settings.AllowedRunnerId, StringComparison.Ordinal);

    public string AllowedRunnerId => _settings.AllowedRunnerId;
    public string RunnerWorkspace => _settings.RunnerWorkspace;
    public string RunnerRepository => _settings.RunnerRepository;
    public bool AllowDelegatedTasks => _settings.Enabled && _settings.AllowDelegatedTasks;
    public IReadOnlyList<string> RawExeAllowList => _settings.RawExeAllowList;
    public bool ClaudeAuthProbeEnabled => _settings.ClaudeAuthProbeEnabled;
    public string ChildClaudeHome => _settings.ChildClaudeHome;
    public string ChildGrokHome => _settings.ChildGrokHome;
    public string ChildCodexHome => _settings.ChildCodexHome;
    public bool CodexAuthProbeEnabled => _settings.CodexAuthProbeEnabled;

    /// <summary>
    /// The kinds a runner takes without anyone having named it: default placement (CARD-0659) and
    /// every automatic or rerouted kind move onto a runner-bound task. CARD-0660 D-10 keeps Codex
    /// out of this set until its default-placement round (S7).
    /// </summary>
    public static bool IsAdmittedKind(AgentKind kind) => kind is AgentKind.Grok or AgentKind.ClaudeCode;

    /// <summary>
    /// CARD-0660 D-7/D-10: the kinds a delegated Worktree task may run on a runner the caller named
    /// explicitly (<c>-Runner</c>). Codex is admitted here, and only here, ahead of S7.
    /// </summary>
    public static bool IsExplicitRunnerTaskKind(AgentKind kind) => IsAdmittedKind(kind) || kind == AgentKind.Codex;

    /// <summary>
    /// CARD-0660 D-8: the credential environment names a runner-bound Codex launch refuses. The
    /// runner's Codex authenticates only through the subscription login in its own home.
    /// </summary>
    public static readonly IReadOnlyList<string> CodexCredentialEnvNames =
        ["OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN"];

    public void RefuseUnsupportedStart(
        Agent agent,
        bool cardStart,
        bool delegatedTask,
        bool worktree,
        bool sourceLanding,
        bool onAgent,
        SessionBackend backend,
        AgentKind kind,
        string? customWrapper,
        bool remoteControl = false)
    {
        if (!IsRunnerBound(agent))
            return;
        if (!_settings.Enabled)
            throw new ConflictException("Phone-home runner is disabled.", "phone_home_disabled");

        var pinned = IsPinnedAgent(agent.Id);

        // Refused for every runner-bound agent, pinned or pooled. A card start binds the session
        // to board work with a desktop working directory; OnAgent hands an existing process work
        // it was not launched for; a custom wrapper is a host path the image does not own.
        if (cardStart)
            throw new ConflictException("A runner-bound agent cannot start card work.", "phone_home_card_refused");
        if (onAgent)
            throw new ConflictException("A runner-bound agent cannot accept OnAgent work.", "phone_home_onagent_refused");
        if (!string.IsNullOrWhiteSpace(customWrapper))
            throw new ConflictException("Custom wrappers are refused for the phone-home projection.", "phone_home_wrapper_refused");
        if (backend != SessionBackend.PtyHost)
            throw new ConflictException("A runner-bound agent must use PtyHost.", "phone_home_backend_refused");
        if (kind == AgentKind.ClaudeCode && remoteControl)
            throw new ConflictException("Remote control is refused for runner-bound Claude Code.", "phone_home_remote_control_refused");

        if (pinned)
        {
            // CARD-0490's pinned agent keeps its narrower shape exactly as it was: one cardless
            // Grok session at /work, no tasks, no worktree, no SourceLanding.
            if (delegatedTask)
                throw new ConflictException("The pinned phone-home agent cannot run delegated tasks.", "phone_home_task_refused");
            if (worktree)
                throw new ConflictException("The pinned phone-home agent cannot use worktrees.", "phone_home_worktree_refused");
            if (sourceLanding)
                throw new ConflictException("The pinned phone-home agent cannot use SourceLanding.", "phone_home_sourcelanding_refused");
            if (agent.IsPoolDelegate || agent.AlwaysOn)
                throw new ConflictException("The pinned phone-home agent must be named, non-pool, and not AlwaysOn.", "phone_home_pool_refused");
            if (kind != AgentKind.Grok)
                throw new ConflictException("The pinned phone-home agent must be Grok.", "phone_home_kind_refused");
            EnsureExactHostRoot(agent.WorkingDirectory);
            return;
        }

        // CARD-0604: a runner-bound named or pool agent.
        if (delegatedTask)
        {
            if (!_settings.AllowDelegatedTasks)
                throw new ConflictException("Delegated tasks are not enabled for the phone-home runner.", "phone_home_task_refused");
            if (!worktree)
                throw new ConflictException("A runner-bound task must use a Worktree workspace.", "phone_home_worktree_refused");
            if (!IsExplicitRunnerTaskKind(kind))
                throw new ConflictException("A runner-bound task must be Grok, Claude Code or Codex.", "phone_home_kind_refused");
            // CARD-0660: Codex runs ordinary Worker tasks on the runner; runner-side SourceLanding
            // custody is not part of its admission.
            if (kind == AgentKind.Codex && sourceLanding)
                throw new ConflictException("A runner-bound Codex task cannot use SourceLanding.", "phone_home_kind_refused");
            // CARD-0604 D-19 (Cut B) / CARD-0659: a runner-bound SourceLanding Mutation is a
            // supported shape. Create admits it only as a Mutation after asking THIS runner for
            // custody (SourceLandingAdmission.RequireSupportAsync), the dispatcher launches it into
            // the runner-side snapshot with its binding, and Project keeps that binding. Refusing
            // it here too left every admitted remote Mutation to fail at launch.
            if (agent.AlwaysOn)
                throw new ConflictException("A runner-bound task agent cannot be AlwaysOn.", "phone_home_pool_refused");
            return;
        }

        // A runner-bound NAMED agent: Grok, or Raw with an image-owned executable (D-2). This is
        // the shape the server2 acceptance cases use.
        if (!IsAdmittedKind(kind) && kind != AgentKind.Raw)
            throw new ConflictException("A runner-bound named agent must be Grok, Claude Code or Raw.", "phone_home_kind_refused");
        if (worktree)
            throw new ConflictException("A runner-bound named agent cannot use worktrees.", "phone_home_worktree_refused");
        if (sourceLanding)
            throw new ConflictException("A runner-bound named agent cannot use SourceLanding.", "phone_home_sourcelanding_refused");
        if (agent.IsPoolDelegate || agent.AlwaysOn)
            throw new ConflictException("A runner-bound named agent must be named, non-pool, and not AlwaysOn.", "phone_home_pool_refused");
    }

    public void EnsureExactHostRoot(string hostCwd)
    {
        var configured = CanonicalHost(_settings.HostWorkspaceRoot);
        var actual = CanonicalHost(hostCwd);
        if (!string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Only the configured host workspace root maps to /work.", "phone_home_cwd_refused");
    }

    public AgentLaunchSpec Project(AgentLaunchSpec spec, Agent agent) => Project(spec, agent, null);

    /// <summary>
    /// Projects a desktop launch spec onto the runner. <paramref name="runnerCwd"/> is the mirror
    /// worktree for a runner-bound task (CARD-0604 D-15); a named agent projects to
    /// <see cref="PhoneHomeRunnerSettings.RunnerWorkspace"/>.
    /// </summary>
    public AgentLaunchSpec Project(AgentLaunchSpec spec, Agent agent, string? runnerCwd)
    {
        if (!IsRunnerBound(agent))
            return spec;

        var pinned = IsPinnedAgent(agent.Id);
        if (pinned)
            EnsureExactHostRoot(agent.WorkingDirectory);

        if (spec.Kind == AgentKind.ClaudeCode)
        {
            foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "CCR_OAUTH_TOKEN_FILE" })
            {
                if (spec.Env.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)))
                    throw new ConflictException($"Credential environment name {name} is refused for runner-bound Claude Code.", "phone_home_env_refused");
            }
        }

        if (spec.Kind == AgentKind.Codex)
        {
            // Refused by name whatever the value, including empty: an empty OPENAI_API_KEY still
            // changes which auth path Codex considers. The value never enters the message.
            foreach (var name in CodexCredentialEnvNames)
            {
                if (spec.Env.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)))
                    throw new ConflictException($"Credential environment name {name} is refused for runner-bound Codex.", "phone_home_env_refused");
            }
        }

        var env = new Dictionary<string, string>(spec.Env, StringComparer.Ordinal)
        {
            ["GROK_HOME"] = _settings.ChildGrokHome,
            ["CLAUDE_CONFIG_DIR"] = _settings.ChildClaudeHome,
            ["ANTIPHON_API"] = _settings.CallbackOrigin,
        };
        if (spec.Kind == AgentKind.Codex)
        {
            // CARD-0660 D-3: the runner's own home, never a desktop one in any spelling.
            foreach (var key in env.Keys.Where(key => string.Equals(key, "CODEX_HOME", StringComparison.OrdinalIgnoreCase)).ToArray())
                env.Remove(key);
            env["CODEX_HOME"] = _settings.ChildCodexHome;
        }

        var exe = ProjectExe(spec.Exe, agent, pinned);
        var cwd = string.IsNullOrWhiteSpace(runnerCwd) ? _settings.RunnerWorkspace : runnerCwd;

        return spec with
        {
            Exe = exe,
            Cwd = cwd,
            Env = env,
            MemoryLimitMb = 0,
            Backend = SessionBackend.PtyHost,
            Herdr = null,
            // CARD-0604 D-19 (Cut B). The runner now holds a binding, so a projected launch keeps
            // the one the reservation made. It is not re-created or adjusted here: the runner
            // admits it only when its backend and store are the runner's own (G-37), and a
            // projection that quietly dropped it would turn a tracked Mutation into an untracked
            // session with a reserved execution nothing can resolve.
        };
    }

    private string ProjectExe(string specExe, Agent agent, bool pinned)
    {
        // The desktop spells its paths with backslashes; take the file name the same way on any host.
        var fileName = Path.GetFileName(specExe.Replace('\\', '/'));
        if (string.Equals(fileName, "grok.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "grok", StringComparison.OrdinalIgnoreCase))
            return "grok";
        if (!pinned && (string.Equals(fileName, "claude.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "claude", StringComparison.OrdinalIgnoreCase)))
            return "claude";
        // CARD-0660 D-7: the desktop's standard Codex (the npm codex.cmd shim, codex.exe or a bare
        // codex) is the image's native /usr/local/bin/codex. A different file name is not Codex.
        if (!pinned && (string.Equals(fileName, "codex.cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "codex.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "codex", StringComparison.OrdinalIgnoreCase)))
            return "codex";
        if (pinned)
            throw new ConflictException("Only the standard grok.exe definition may project to Linux.", "phone_home_wrapper_refused");

        // A runner-bound Raw agent may name an image-owned executable, and only one: the allow
        // list is the image's own, not the desktop's, and a host path that happens to end in the
        // same file name is not the same program.
        var posix = specExe.Replace('\\', '/');
        var match = _settings.RawExeAllowList
            .FirstOrDefault(allowed => string.Equals(allowed, posix, StringComparison.Ordinal));
        if (match is not null)
            return match;
        throw new ConflictException(
            "Only an image-owned executable may project to the phone-home runner.",
            "phone_home_wrapper_refused");
    }

    public static string CanonicalHost(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd('/', '\\');
        }
    }
}
