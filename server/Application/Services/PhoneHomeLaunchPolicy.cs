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

    public bool IsPinnedAgent(Guid agentId) =>
        _settings.Enabled && agentId == _settings.StandingAgentId && _settings.StandingAgentId != Guid.Empty;

    public string AllowedRunnerId => _settings.AllowedRunnerId;
    public string RunnerWorkspace => _settings.RunnerWorkspace;

    public void RefuseUnsupportedStart(
        Agent agent,
        bool cardStart,
        bool delegatedTask,
        bool worktree,
        bool sourceLanding,
        bool onAgent,
        SessionBackend backend,
        AgentKind kind,
        string? customWrapper)
    {
        if (!IsPinnedAgent(agent.Id))
            return;
        if (!_settings.Enabled)
            throw new ConflictException("Phone-home runner is disabled.", "phone_home_disabled");
        if (cardStart)
            throw new ConflictException("The pinned phone-home agent cannot start card work.", "phone_home_card_refused");
        if (delegatedTask)
            throw new ConflictException("The pinned phone-home agent cannot run delegated tasks.", "phone_home_task_refused");
        if (worktree)
            throw new ConflictException("The pinned phone-home agent cannot use worktrees.", "phone_home_worktree_refused");
        if (sourceLanding)
            throw new ConflictException("The pinned phone-home agent cannot use SourceLanding.", "phone_home_sourcelanding_refused");
        if (onAgent)
            throw new ConflictException("The pinned phone-home agent cannot accept OnAgent work.", "phone_home_onagent_refused");
        if (agent.IsPoolDelegate || agent.AlwaysOn)
            throw new ConflictException("The pinned phone-home agent must be named, non-pool, and not AlwaysOn.", "phone_home_pool_refused");
        if (kind != AgentKind.Grok)
            throw new ConflictException("The pinned phone-home agent must be Grok.", "phone_home_kind_refused");
        if (backend != SessionBackend.PtyHost)
            throw new ConflictException("The pinned phone-home agent must use PtyHost.", "phone_home_backend_refused");
        if (!string.IsNullOrWhiteSpace(customWrapper))
            throw new ConflictException("Custom wrappers are refused for the phone-home Grok projection.", "phone_home_wrapper_refused");
        EnsureExactHostRoot(agent.WorkingDirectory);
    }

    public void EnsureExactHostRoot(string hostCwd)
    {
        var configured = CanonicalHost(_settings.HostWorkspaceRoot);
        var actual = CanonicalHost(hostCwd);
        if (!string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Only the configured host workspace root maps to /work.", "phone_home_cwd_refused");
    }

    public AgentLaunchSpec Project(AgentLaunchSpec spec, Agent agent)
    {
        if (!IsPinnedAgent(agent.Id))
            return spec;
        EnsureExactHostRoot(agent.WorkingDirectory);
        var env = new Dictionary<string, string>(spec.Env, StringComparer.Ordinal);
        env["GROK_HOME"] = _settings.ChildGrokHome;
        env["ANTIPHON_API"] = _settings.CallbackOrigin;
        var exe = Path.GetFileName(spec.Exe);
        if (!string.Equals(exe, "grok.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(exe, "grok", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Only the standard grok.exe definition may project to Linux.", "phone_home_wrapper_refused");
        return spec with
        {
            Exe = "grok",
            Cwd = _settings.RunnerWorkspace,
            Env = env,
            MemoryLimitMb = 0,
            Backend = SessionBackend.PtyHost,
            Herdr = null,
            VerificationBinding = null,
        };
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
