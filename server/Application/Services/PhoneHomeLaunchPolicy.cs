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
    public static bool IsAdmittedKind(AgentKind kind) => kind is AgentKind.Grok or AgentKind.ClaudeCode;

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
            if (!IsAdmittedKind(kind))
                throw new ConflictException("A runner-bound task must be Grok or Claude Code.", "phone_home_kind_refused");
            // SourceLanding on the runner is Cut B (CARD-0604 D-19): until the Linux custody
            // backend exists, a tracked task here would have no receipt to seal.
            if (sourceLanding)
                throw new ConflictException("SourceLanding is not supported on the phone-home runner.", "phone_home_sourcelanding_refused");
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

        var env = new Dictionary<string, string>(spec.Env, StringComparer.Ordinal)
        {
            ["GROK_HOME"] = _settings.ChildGrokHome,
            ["CLAUDE_CONFIG_DIR"] = _settings.ChildClaudeHome,
            ["ANTIPHON_API"] = _settings.CallbackOrigin,
        };

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
        var fileName = Path.GetFileName(specExe);
        if (string.Equals(fileName, "grok.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "grok", StringComparison.OrdinalIgnoreCase))
            return "grok";
        if (!pinned && (string.Equals(fileName, "claude.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "claude", StringComparison.OrdinalIgnoreCase)))
            return "claude";
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
