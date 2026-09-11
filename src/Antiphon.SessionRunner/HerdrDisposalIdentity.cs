using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

internal sealed record HerdrDisposalObservation(
    HerdrPaneInfo Pane, HerdrServerInfo Backend,
    HerdrPaneDisposalProcess? Shell, IReadOnlyList<HerdrPaneDisposalProcess>? Foreground,
    IReadOnlyList<HerdrPaneDisposalProcess> Affected, bool Complete,
    bool PendingInput, bool PendingLaunch, IReadOnlyList<HerdrPaneDisposalClaim> Claims,
    IReadOnlyList<HerdrDisposalFile> Files, bool ClaimsComplete = true,
    string? WorkspaceLabel = null, string? TabLabel = null, bool? EmptyTab = null);

/// <summary>Pure current-observation classifier. Tokens locate; they never own a process.</summary>
internal sealed class HerdrDisposalIdentity
{
    internal string? Refusal(HerdrPaneDisposalPreviewRequest request, HerdrDisposalObservation observation)
    {
        var o = observation;
        if (o.Claims.Any(c => c.Live)) return HerdrProblemTypes.PaneBound;
        if (!o.ClaimsComplete) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (request.ExpectedSessionId is { } session && o.Claims.Any(c => c.SessionId != session))
            return HerdrProblemTypes.PaneBound;
        if (o.Claims.Select(c => c.SessionId).Distinct().Count() > 1)
            return HerdrProblemTypes.PaneBound;
        if (!o.Complete || o.Foreground is null) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (o.Shell is not { } shell || !IsShell(shell.ExecutableName)) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (o.Affected.Any(p => p.Pid <= 0) || o.Foreground.Any(p => p.Pid <= 0) || shell.Pid <= 0)
            return HerdrPaneDisposalCodes.IdentityUnproven;
        if (shell.StartedAtUtc is null || o.Affected.Any(p => p.StartedAtUtc is null || p.ExecutableName is null))
            return HerdrPaneDisposalCodes.IdentityUnproven;
        if (o.Affected.Select(p => p.Pid).Distinct().Count() != o.Affected.Count
            || o.Affected.All(p => p.Pid != shell.Pid)) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (o.PendingInput || o.PendingLaunch) return HerdrProblemTypes.PaneBound;

        var foreground = o.Foreground.Where(p => p.Pid != shell.Pid).ToArray();
        // The shell's direct foreground root may have descendants listed by Herdr too.
        var roots = foreground.Where(p => !foreground.Any(parent => parent.Pid != p.Pid
            && IsDescendant(p, parent.Pid, o.Affected))).ToArray();
        if (roots.Length > 1) return HerdrProblemTypes.PaneForeign;
        var agent = roots.SingleOrDefault();
        if (agent is null)
        {
            if (o.Affected.Any(p => p.Pid != shell.Pid)) return HerdrProblemTypes.PaneForeign;
            // Native-only identity cannot select an empty shell with no native process evidence.
            if (request.ExpectedNativeSessionId is not null) return HerdrPaneDisposalCodes.IdentityUnproven;
            return request.ExpectedSessionId is { } id && o.Claims.Any(c => c.SessionId == id)
                ? null : HerdrPaneDisposalCodes.IdentityUnproven;
        }

        var kind = o.Pane.Agent;
        if (string.IsNullOrEmpty(kind) || !HerdrAgentKinds.Supported.Contains(kind)) return HerdrProblemTypes.PaneForeign;
        if (!HerdrAgentKinds.IsFamilyMember(kind, agent.ExecutableName)) return HerdrProblemTypes.PaneForeign;
        if (agent.StartedAtUtc is null) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (o.Affected.Any(p => p.Pid != shell.Pid && p.Pid != agent.Pid && !IsDescendant(p, agent.Pid, o.Affected)))
            return HerdrProblemTypes.PaneForeign;
        if (!IsDescendant(agent, shell.Pid, o.Affected)) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (foreground.Any(p => !o.Affected.Contains(p))) return HerdrPaneDisposalCodes.IdentityUnproven;

        var natives = agent.NativeSessionIds ?? [];
        if (natives.Distinct().Count() > 1) return HerdrProblemTypes.PaneForeign;
        // Protocol-20 agent_session has no process-incarnation binding. It can contradict
        // independent argv, but can never supply sole destructive authority.
        var metadata = o.Pane.AgentSession;
        if (metadata is not null && metadata.Source != HerdrSources.Antiphon
            && Guid.TryParse(metadata.Value, out var metadataId)
            && natives.Count > 0 && natives.Any(id => id != metadataId)) return HerdrProblemTypes.PaneForeign;
        if (request.ExpectedNativeSessionId is { } expectedNative && !natives.Contains(expectedNative))
            return HerdrPaneDisposalCodes.IdentityUnproven;

        var exact = o.Claims.Any(c => c.SessionId == request.ExpectedSessionId && c.AgentKind == kind
            && c.ChildPid == agent.Pid && c.ChildStartedAtUtc is { } started && started == agent.StartedAtUtc);
        // Claude/Grok --session-id names the Antiphon launch UUID. Codex does not have that
        // launch contract: it requires explicit expectedNativeSessionId or exact binding evidence.
        var nativeMatch = request.ExpectedNativeSessionId is { } explicitNative ? natives.Contains(explicitNative)
            : kind != HerdrAgentKinds.Codex && request.ExpectedSessionId is { } expected && natives.Contains(expected);
        if (!exact && !nativeMatch) return HerdrPaneDisposalCodes.IdentityUnproven;
        if (request.ExpectedSessionId is { } expectedSession && !o.Claims.Any(c => c.SessionId == expectedSession))
            return HerdrPaneDisposalCodes.IdentityUnproven;
        return null;
    }

    internal static bool IsShell(string? name) => name?.ToLowerInvariant() is
        "pwsh" or "pwsh.exe" or "powershell" or "powershell.exe" or "cmd" or "cmd.exe"
        or "bash" or "bash.exe" or "sh" or "sh.exe" or "zsh";

    private static bool IsDescendant(HerdrPaneDisposalProcess process, int root, IReadOnlyList<HerdrPaneDisposalProcess> tree)
    {
        var seen = new HashSet<int>();
        while (process.ParentPid is { } parent && seen.Add(process.Pid))
        {
            var ancestor = tree.FirstOrDefault(p => p.Pid == parent);
            if (ancestor is null || ancestor.StartedAtUtc > process.StartedAtUtc) return false;
            if (parent == root) return true;
            process = ancestor;
        }
        return false;
    }

    internal static IReadOnlyList<Guid> NativeIds(IReadOnlyList<string>? argv)
    {
        var ids = new List<Guid>();
        for (var i = 0; argv is not null && i < argv.Count; i++)
        {
            string? value = null;
            if (argv[i] is "--session-id" or "--resume" or "-r" or "resume")
                value = i + 1 < argv.Count ? argv[++i] : null;
            else if (argv[i].StartsWith("--session-id=", StringComparison.Ordinal) || argv[i].StartsWith("--resume=", StringComparison.Ordinal))
                value = argv[i][(argv[i].IndexOf('=') + 1)..];
            if (Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty) ids.Add(id);
        }
        return ids.Distinct().Order().ToArray();
    }
}
