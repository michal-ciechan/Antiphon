using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Pure I1–I5 / Z1–Z7 port of <c>scripts/reap-zombie-agents.ps1</c>. Fixture-shaped records in,
/// verdict rows out; no WMI, EF, or runner calls.
/// </summary>
public sealed class ZombieCensusClassifier
{
    private static readonly HashSet<string> AgentShapedNames =
        new(StringComparer.OrdinalIgnoreCase) { "claude.exe", "claude", "grok.exe", "grok", "codex.exe", "codex" };

    private static readonly HashSet<string> AntiphonParentNames =
        new(StringComparer.OrdinalIgnoreCase) { "Antiphon.PtyHost.exe", "herdr.exe", "herdr" };

    private static readonly HashSet<string> OperatorParentNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "WindowsTerminal.exe", "explorer.exe", "Code.exe", "rider64.exe",
            "ssh.exe", "sshd.exe", "ssh", "sshd"
        };

    private static readonly HashSet<AgentTaskStatus> OpenTaskStatuses =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    private static readonly HashSet<SessionStatus> LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running];

    private static readonly HashSet<SessionStatus> TerminalSessionStatuses =
        [SessionStatus.Stopped, SessionStatus.Failed];

    private static readonly Regex SessionIdArg =
        new(@"--session-id(?:\s+|=)([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NameArg =
        new(@"--name(?:\s+|=)([^\s""]+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CardTaskLeaf =
        new(@"card-task-([0-9a-fA-F]{8})(?:\\|""|$|/)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ZombieCensusResult Classify(
        IReadOnlyList<ZombieOsProcess> processes,
        IReadOnlyList<SessionRunnerSessionDto> runnerSessions,
        ZombieCensusDbSnapshot db,
        IReadOnlyDictionary<int, Guid> manifestsByHostPid,
        ZombieCensusThresholds thresholds,
        DateTimeOffset utcNow)
    {
        var started = utcNow;
        var procByPid = new Dictionary<int, ZombieOsProcess>();
        foreach (var proc in processes)
            procByPid[proc.ProcessId] = proc;

        var runnerByPid = BuildRunnerByPid(runnerSessions);
        var sessionsById = db.Sessions.ToDictionary(s => s.Id);
        var agentBySession = new Dictionary<Guid, ZombieCensusAgentRow>();
        var agentBySlug = new Dictionary<string, ZombieCensusAgentRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in db.Agents)
        {
            if (agent.PersistentSessionId is { } sid)
                agentBySession[sid] = agent;
            if (!string.IsNullOrWhiteSpace(agent.Slug))
                agentBySlug[agent.Slug] = agent;
            if (!string.IsNullOrWhiteSpace(agent.Name))
                agentBySlug[agent.Name] = agent;
        }

        var taskBySession = new Dictionary<Guid, ZombieCensusTaskRow>();
        var tasksByAgent = new Dictionary<Guid, List<ZombieCensusTaskRow>>();
        foreach (var task in db.Tasks)
        {
            if (task.AgentSessionId is { } sid)
                taskBySession[sid] = task;
            if (task.AgentId is { } aid)
            {
                if (!tasksByAgent.TryGetValue(aid, out var list))
                {
                    list = [];
                    tasksByAgent[aid] = list;
                }

                list.Add(task);
            }
        }

        var rows = new List<ZombieCensusRow>();
        var unidentified = 0;

        foreach (var proc in processes)
        {
            if (!IsAgentShaped(proc.Name))
                continue;

            var chain = AncestorChain(proc.ProcessId, procByPid);
            var pre = new List<string>();
            if (proc.ExecutablePath.Contains(@"\windowsapps\", StringComparison.OrdinalIgnoreCase))
                pre.Add("WindowsApps");
            if (IsOperatorLaunched(chain, procByPid))
                pre.Add("operator-launched");
            if (pre.Count > 0)
            {
                rows.Add(MakeRow(proc, ZombieIdentityMethod.None, null, ZombieCensusClass.Ignored, pre,
                    ZombieFutureAction.None, runnerClaimed: false, agentName: "", dbStatus: "", treeKillPid: 0,
                    isCandidate: false));
                continue;
            }

            var identity = ResolveIdentity(proc, chain, procByPid, runnerByPid, manifestsByHostPid,
                agentBySlug, db.Tasks);
            if (identity is null)
            {
                unidentified++;
                rows.Add(MakeRow(proc, ZombieIdentityMethod.None, null, ZombieCensusClass.Unclaimed,
                    ["Z2 identity unresolved"], ZombieFutureAction.None, runnerClaimed: false,
                    agentName: "", dbStatus: "", treeKillPid: 0, isCandidate: false));
                continue;
            }

            var agent = GetOwnerAgent(identity.SessionId, agentBySession, taskBySession, db.Agents);
            var agentName = agent?.Name ?? "";
            if (!sessionsById.TryGetValue(identity.SessionId, out var dbSession))
            {
                rows.Add(MakeRow(proc, identity.Method, identity.SessionId, ZombieCensusClass.Unclaimed,
                    ["Z2 no AgentSessions row"], ZombieFutureAction.None, identity.RunnerClaimed,
                    agentName, dbStatus: "", treeKillPid: 0, isCandidate: false));
                continue;
            }

            var failed = new List<string>();
            if (proc.CreationUtc is { } created && created < dbSession.StartedAt.AddSeconds(-thresholds.PidReuseToleranceSeconds))
                failed.Add("Z3 pid-reuse (process start before session StartedAt)");

            var ageOk = true;
            if (proc.CreationUtc is { } createdAge)
            {
                var ageMin = (utcNow - createdAge).TotalMinutes;
                if (ageMin < thresholds.MinDoneMinutes)
                {
                    ageOk = false;
                    failed.Add($"Z6 process only {ageMin:N0} min old (< {thresholds.MinDoneMinutes})");
                }
            }

            var isPool = agent?.IsPoolDelegate == true;
            var hasOpen = agent is not null
                && tasksByAgent.TryGetValue(agent.Id, out var agentTasks)
                && agentTasks.Any(t => OpenTaskStatuses.Contains(t.Status));
            DateTimeOffset? newestDone = null;
            if (agent is not null && tasksByAgent.TryGetValue(agent.Id, out var doneTasks))
            {
                foreach (var task in doneTasks)
                {
                    if (task.CompletedAt is { } completed && (newestDone is null || completed > newestDone))
                        newestDone = completed;
                }
            }
            var doneOld = newestDone is { } done && (utcNow - done).TotalMinutes >= thresholds.MinDoneMinutes;

            var className = ZombieCensusClass.None;
            var future = ZombieFutureAction.None;
            if (LiveSessionStatuses.Contains(dbSession.Status) && isPool && !hasOpen && doneOld)
            {
                className = ZombieCensusClass.PoolExpired;
                future = identity.RunnerClaimed ? ZombieFutureAction.ServerSessionKill : ZombieFutureAction.ProcessTreeKill;
            }
            else if (TerminalSessionStatuses.Contains(dbSession.Status) && identity.RunnerClaimed)
            {
                className = ZombieCensusClass.ReconcilerOwned;
                future = ZombieFutureAction.None;
                failed.Add(dbSession.Status == SessionStatus.Failed
                    ? "Z4 reconciler re-adopts Failed (CARD-0056)"
                    : "Z4 reconciler RetryFailedKillAsync owns Stopped");
            }
            else if (TerminalSessionStatuses.Contains(dbSession.Status) && !identity.RunnerClaimed)
            {
                var endedOld = dbSession.EndedAt is { } ended
                    && (utcNow - ended).TotalMinutes >= thresholds.MinDoneMinutes;
                if (!endedOld)
                {
                    failed.Add("Z4 EndedAt younger than -MinDoneMinutes");
                }
                else
                {
                    var quiet = true;
                    if (dbSession.ActivityUtc is { } activity)
                    {
                        var quietHoursAge = (utcNow - activity).TotalHours;
                        if (quietHoursAge < thresholds.QuietHours)
                        {
                            quiet = false;
                            failed.Add($"Z5 activity {quietHoursAge:N1} h ago (< {thresholds.QuietHours} QuietHours)");
                        }
                    }

                    if (quiet)
                    {
                        className = ZombieCensusClass.EndedButAlive;
                        future = ZombieFutureAction.ProcessTreeKill;
                    }
                }
            }
            else
            {
                failed.Add("Z4 not a zombie class (warm/standing/live)");
            }

            var isCandidate = (className is ZombieCensusClass.PoolExpired or ZombieCensusClass.EndedButAlive)
                && failed.Count == 0
                && ageOk;
            rows.Add(MakeRow(proc, identity.Method, identity.SessionId, className, failed, future,
                identity.RunnerClaimed, agentName, dbSession.Status.ToString(),
                TopAntiphonAncestor(chain, procByPid), isCandidate));
        }

        var counts = new ZombieCensusCounts(
            PoolExpired: rows.Count(r => r.Class == ZombieCensusClass.PoolExpired),
            ReconcilerOwned: rows.Count(r => r.Class == ZombieCensusClass.ReconcilerOwned),
            EndedButAlive: rows.Count(r => r.Class == ZombieCensusClass.EndedButAlive),
            Unclaimed: rows.Count(r => r.Class == ZombieCensusClass.Unclaimed),
            Ignored: rows.Count(r => r.Class == ZombieCensusClass.Ignored),
            Unidentified: unidentified,
            Candidates: rows.Count(r => r.IsCandidate));

        return new ZombieCensusResult(
            GeneratedAtUtc: utcNow,
            Duration: TimeSpan.Zero,
            Counts: counts,
            Rows: rows,
            Candidates: rows.Where(r => r.IsCandidate).ToList(),
            PrerequisiteFailures: []);
    }

    private static Dictionary<int, RunnerClaim> BuildRunnerByPid(
        IReadOnlyList<SessionRunnerSessionDto> runnerSessions)
    {
        var map = new Dictionary<int, RunnerClaim>();
        foreach (var session in runnerSessions)
        {
            var claim = new RunnerClaim(session.SessionId, true);
            if (session.Pid is > 0)
                map[session.Pid.Value] = claim;
            if (session.HostPid is > 0)
                map[session.HostPid.Value] = claim;
        }

        return map;
    }

    private static Identity? ResolveIdentity(
        ZombieOsProcess proc,
        IReadOnlyList<int> chain,
        IReadOnlyDictionary<int, ZombieOsProcess> procByPid,
        IReadOnlyDictionary<int, RunnerClaim> runnerByPid,
        IReadOnlyDictionary<int, Guid> manifestsByHostPid,
        IReadOnlyDictionary<string, ZombieCensusAgentRow> agentBySlug,
        IReadOnlyList<ZombieCensusTaskRow> tasks)
    {
        foreach (var anc in chain)
        {
            if (runnerByPid.TryGetValue(anc, out var hit))
                return new Identity(ZombieIdentityMethod.I1, hit.SessionId, RunnerClaimed: true);
        }

        foreach (var anc in chain)
        {
            if (!procByPid.TryGetValue(anc, out var ancestor))
                continue;
            if (!ancestor.Name.Equals("Antiphon.PtyHost.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (manifestsByHostPid.TryGetValue(anc, out var sid))
                return new Identity(ZombieIdentityMethod.I2, sid, RunnerClaimed: false);
        }

        var fromCmd = SessionIdFromCommandLine(proc.CommandLine);
        if (fromCmd is { } i3)
            return new Identity(ZombieIdentityMethod.I3, i3, RunnerClaimed: false);

        var nameArg = NameFromCommandLine(proc.CommandLine);
        if (nameArg is not null && agentBySlug.TryGetValue(nameArg, out var named)
            && named.PersistentSessionId is { } persistent)
            return new Identity(ZombieIdentityMethod.I4, persistent, RunnerClaimed: false);

        var leaf = GetCardTaskLeaf(proc.Cwd) ?? GetCardTaskLeaf(proc.CommandLine);
        if (leaf is not null)
        {
            foreach (var task in tasks)
            {
                var taskLeaf = GetCardTaskLeaf(task.WorktreePath) ?? GetCardTaskLeaf(task.WorkingDirectory);
                if (taskLeaf == leaf && task.AgentSessionId is { } taskSession)
                    return new Identity(ZombieIdentityMethod.I5, taskSession, RunnerClaimed: false);
            }
        }

        return null;
    }

    private static ZombieCensusAgentRow? GetOwnerAgent(
        Guid sessionId,
        IReadOnlyDictionary<Guid, ZombieCensusAgentRow> agentBySession,
        IReadOnlyDictionary<Guid, ZombieCensusTaskRow> taskBySession,
        IReadOnlyList<ZombieCensusAgentRow> agents)
    {
        if (agentBySession.TryGetValue(sessionId, out var bySession))
            return bySession;
        if (taskBySession.TryGetValue(sessionId, out var task) && task.AgentId is { } agentId)
            return agents.FirstOrDefault(a => a.Id == agentId);
        return null;
    }

    private static List<int> AncestorChain(int leafPid, IReadOnlyDictionary<int, ZombieOsProcess> procByPid)
    {
        var chain = new List<int>();
        var seen = new HashSet<int>();
        var current = leafPid;
        while (current > 0 && seen.Add(current))
        {
            chain.Add(current);
            if (!procByPid.TryGetValue(current, out var proc))
                break;
            current = proc.ParentProcessId;
        }

        return chain;
    }

    private static bool IsOperatorLaunched(IReadOnlyList<int> chain, IReadOnlyDictionary<int, ZombieOsProcess> procByPid)
    {
        foreach (var anc in chain)
        {
            var name = procByPid.TryGetValue(anc, out var proc) ? proc.Name : "";
            if (AntiphonParentNames.Contains(name))
                return false;
            if (OperatorParentNames.Contains(name))
                return true;
        }

        return false;
    }

    private static int TopAntiphonAncestor(IReadOnlyList<int> chain, IReadOnlyDictionary<int, ZombieOsProcess> procByPid)
    {
        if (chain.Count == 0)
            return 0;
        var last = chain[0];
        foreach (var anc in chain)
        {
            var name = procByPid.TryGetValue(anc, out var proc) ? proc.Name : "";
            if (AntiphonParentNames.Contains(name))
                return anc;
            if (name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("node.exe", StringComparison.OrdinalIgnoreCase))
                last = anc;
        }

        return last;
    }

    private static bool IsAgentShaped(string name) => AgentShapedNames.Contains(name);

    private static Guid? SessionIdFromCommandLine(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;
        var match = SessionIdArg.Match(commandLine);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static string? NameFromCommandLine(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;
        var match = NameArg.Match(commandLine);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static string? GetCardTaskLeaf(string? pathValue)
    {
        if (string.IsNullOrEmpty(pathValue))
            return null;
        var match = CardTaskLeaf.Match(pathValue);
        if (match.Success)
            return "card-task-" + match.Groups[1].Value.ToLowerInvariant();
        var leaf = Path.GetFileName(pathValue.TrimEnd('\\', '/'));
        if (leaf is not null && Regex.IsMatch(leaf, @"^card-task-[0-9a-fA-F]{8}$", RegexOptions.CultureInvariant))
            return leaf.ToLowerInvariant();
        return null;
    }

    private static ZombieCensusRow MakeRow(
        ZombieOsProcess proc,
        ZombieIdentityMethod method,
        Guid? sessionId,
        ZombieCensusClass className,
        IReadOnlyList<string> failed,
        ZombieFutureAction future,
        bool runnerClaimed,
        string agentName,
        string dbStatus,
        int treeKillPid,
        bool isCandidate)
    {
        var wsGb = Math.Round(proc.WorkingSetBytes / (1024d * 1024d * 1024d), 3);
        return new ZombieCensusRow(
            Pid: proc.ProcessId,
            Exe: proc.Name,
            StartUtc: proc.CreationUtc,
            WorkingSetGb: wsGb,
            CpuDeltaPercent: proc.CpuDeltaPercent,
            IdentityMethod: method,
            SessionId: sessionId,
            DbStatus: dbStatus,
            AgentName: agentName,
            Class: className,
            FailedRules: failed,
            FutureAction: future,
            RunnerClaimed: runnerClaimed,
            TreeKillPid: treeKillPid,
            IsCandidate: isCandidate);
    }

    private sealed record RunnerClaim(Guid SessionId, bool RunnerClaimed);

    private sealed record Identity(ZombieIdentityMethod Method, Guid SessionId, bool RunnerClaimed);
}
