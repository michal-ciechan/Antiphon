using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Gathers everything a check-in needs to know about a running delegate, deterministically and
/// in-process (CARD-0047 §1.3), and renders it as a digest a human — or a model — can read.
///
/// <para><b>Why code and not an agent.</b> Every probe that WORKED on 2026-08-15/16 is already
/// deterministic and cheaper to read from here than from any agent: the task row, the session row,
/// the working/idle verdict, the transcript tail, two git reads. Every probe that FAILED was an
/// improvised one — inferring from silence, a process scan that matched its own scanning command,
/// trusting prose over the repo. This class is the card's own lesson ("give the checker specific
/// probes, not 'go and see how it's doing'") with the probes promoted into reviewed, tested code.
/// It costs zero model tokens, and the digest is deliverable even when the interpreter is down.</para>
///
/// <para><b>The read-only guarantee is visible in the constructor.</b> It takes the database (every
/// query <c>AsNoTracking</c>), a git wrapper that only ever runs <c>log</c>/<c>status</c>/
/// <c>rev-parse</c> with <c>--no-optional-locks</c>, a clock, and
/// <c>IOptions&lt;SupervisionSettings&gt;</c> — configuration, not a write surface (the parking
/// cap is read so the digest and the queue cannot disagree about what parked means). It depends
/// on NOTHING with a write surface — no message queue, no session stopper, no runner client —
/// so a reviewer can read six lines and see that a check cannot type into, kill, or commit for
/// the delegate it is inspecting. There is no process probe here to get wrong, either: the card's
/// self-matching process-scan trap is excluded by construction rather than by care.</para>
/// </summary>
public sealed class DelegateCheckProbe
{
    /// <summary>How many transcript entries the tail carries. Enough to see the shape of a turn.</summary>
    private const int TranscriptTailSize = 10;

    /// <summary>Per-entry excerpt ceiling. A tail is for shape, not for reading the work.</summary>
    private const int ExcerptChars = 200;

    /// <summary>Flattened tool-input window. Smaller than the 200-char ToolResult excerpt already carried.</summary>
    private const int ToolInputChars = 120;

    private const int CommitLimit = 20;
    private const int PendingMessageLimit = 5;
    private const int IncidentLimit = 5;

    private readonly AppDbContext _db;
    private readonly GitWorkspaceService _git;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionSettings _supervision;

    public DelegateCheckProbe(
        AppDbContext db,
        GitWorkspaceService git,
        TimeProvider timeProvider,
        IOptions<SupervisionSettings> supervision)
    {
        _db = db;
        _git = git;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
    }

    // ---- the fact bundle -----------------------------------------------------------------------

    /// <param name="Settled">
    /// Read from the TASK ROW, not from anything the delegate said. That is deliberate: it means a
    /// check also catches a completion whose notification was lost on the way to the caller, which
    /// is a measured failure mode (CARD-0047 §0), not a hypothetical one.
    /// </param>
    public sealed record CheckTaskFacts(
        Guid Id,
        string ShortId,
        string Title,
        AgentTaskKind Kind,
        /// <summary>
        /// WHICH AGENT PROGRAM is running the task — a different axis from <see cref="Kind"/>
        /// (worker-vs-orchestrator). Carried so the digest can NAME the tier on the ladder the task
        /// actually runs (CARD-0084 S4); a Grok delegate whose digest said <c>tier=fable</c> would
        /// be handing the interpreter a false fact about the thing it is reasoning over.
        /// </summary>
        AgentKind AgentKind,
        AgentTaskRole Role,
        AgentModelLevel ModelLevel,
        AgentTaskStatus Status,
        bool Settled,
        int Attempt,
        int MaxAttempts,
        DateTime? DispatchedAt,
        TimeSpan? Age,
        int ExpectedDurationMinutes,
        /// <summary>
        /// Which check this is, counting from 1 — the number the caller reads in the note's header.
        ///
        /// <para>It is <see cref="AgentTask.CheckCount"/> ITSELF, not <c>+ 1</c>. The sweep counts a
        /// check when it CLAIMS it: <c>ClaimCheckAsync</c> writes <c>CheckCount = checkNumber</c>
        /// before the id is handed to the worker, because the count is half the conditional UPDATE
        /// that makes the claim atomic. So by the time a probe reads the row, the running check is
        /// already counted, and adding one announced the first check of a run as <c>#2</c> and the
        /// last check of a 10-check budget as <c>#11</c> (live, 2026-08-16: "Check #2 on task
        /// 8ae80695" for the first check that task ever had).</para>
        ///
        /// <para>Floored at 1 for the one caller that has not gone through a claim — an ad-hoc probe
        /// of an unclaimed task — which would otherwise read <c>#0</c>.</para>
        /// </summary>
        int CheckNumber,
        bool HasResult,
        string? FailureReason);

    /// <param name="Working">
    /// The SAME verdict every other surface uses (<c>SessionMessageQueueService.IsWorkingAsync</c>),
    /// not a second implementation of it. A check that disagreed with the agent card about whether
    /// a delegate is mid-turn would be worse than no check.
    /// </param>
    public sealed record CheckSessionFacts(
        Guid SessionId,
        SessionStatus Status,
        bool Working,
        int TranscriptEntries,
        DateTime? LastEntryAt,
        TimeSpan? SinceLastEntry);

    public sealed record CheckTranscriptLine(
        long Sequence,
        string Kind,
        string? Excerpt,
        DateTime? At,
        string? ToolInput = null,
        bool? IsError = null);

    /// <summary>
    /// Whether the Git facts can be attributed to the checked task. Ownership is an explicit
    /// discriminator, never inferred from a nullable range (CARD-0227).
    /// </summary>
    public enum CheckGitEvidenceScope
    {
        /// <summary>
        /// Commits and working-tree counts are from <c>mergeTarget..taskBranch</c> on a Worktree
        /// task — the dedicated branch the dispatcher created for this task.
        /// </summary>
        TaskBranch = 0,

        /// <summary>
        /// Shared (or ReadOnly) checkout: any commit or dirty file on HEAD can belong to another
        /// writer, so commit and working-tree evidence is omitted.
        /// </summary>
        SharedWorkspaceUnattributable = 1,
    }

    /// <summary>
    /// Digest line for <see cref="CheckGitEvidenceScope.SharedWorkspaceUnattributable"/>. No
    /// numeric Git counts ride with it: zero would read as "no work" and any positive value
    /// invites false credit.
    /// </summary>
    public const string SharedWorkspaceUnattributableExplanation =
        "shared checkout — commits and working-tree state are deliberately omitted because they cannot be attributed to this task.";

    /// <param name="Scope">
    /// Who the Git facts belong to. <see cref="CheckGitEvidenceScope.TaskBranch"/> is the only
    /// shape that may be read as this task's own commits or files.
    /// </param>
    /// <param name="Range">
    /// <c>mergeTarget..branch</c> for a <see cref="CheckGitEvidenceScope.TaskBranch"/> probe —
    /// commit messages are the durable report in this repo, so "what has landed on the branch" is
    /// the highest-signal fact available. Null when there is no task-owned range.
    /// </param>
    /// <param name="Unavailable">
    /// Non-null when git could not answer, and why. A probe that reported "0 commits" for a git
    /// failure would read as "the delegate has done nothing", which is the opposite of the truth.
    /// </param>
    public sealed record CheckGitFacts(
        string Directory,
        CheckGitEvidenceScope Scope,
        string? Range,
        IReadOnlyList<string> Commits,
        int ChangedFiles,
        int UntrackedFiles,
        string? Unavailable);

    public sealed record CheckQueuedMessage(
        long Sequence,
        QueuedMessageOrigin Origin,
        DateTime CreatedAt,
        string Excerpt,
        int DeliveryAttempts = 0,
        DateTime? LastDeliveryStartedAt = null,
        bool Parked = false,
        int MaxDeliveryAttempts = 3,
        string Label = "");

    public sealed record CheckIncident(AgentIncidentKind Kind, AlertSeverity Severity, string Message, DateTime CreatedAt);

    /// <param name="Session">Null when the task never got a session, or its row is gone.</param>
    /// <param name="Git">Null when the task's directory is not a git repository at all.</param>
    public sealed record CheckFacts(
        DateTime At,
        CheckTaskFacts Task,
        CheckSessionFacts? Session,
        IReadOnlyList<CheckTranscriptLine> TranscriptTail,
        CheckGitFacts? Git,
        IReadOnlyList<CheckQueuedMessage> PendingMessages,
        IReadOnlyList<CheckIncident> Incidents,
        DateTime? PreviousCheckAt = null);

    // ---- gathering -----------------------------------------------------------------------------

    public async Task<CheckFacts> GatherAsync(AgentTask task, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var taskFacts = new CheckTaskFacts(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            task.Kind,
            task.AgentKind,
            task.Role,
            task.ModelLevel,
            task.Status,
            AgentTaskService.IsSettled(task.Status),
            task.Attempt,
            task.MaxAttempts,
            task.DispatchedAt,
            task.DispatchedAt is { } from ? now - from : null,
            task.ExpectedDurationMinutes,
            Math.Max(1, task.CheckCount),
            !string.IsNullOrWhiteSpace(task.Result),
            task.FailureReason);

        var previousCheckAt = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Check)
            .MaxAsync(e => (DateTime?)e.At, ct);

        var session = await GatherSessionAsync(task, now, ct);
        var tail = task.AgentSessionId is Guid tailSession
            ? await GatherTranscriptTailAsync(tailSession, ct)
            : [];
        var pending = task.AgentSessionId is Guid queueSession
            ? await GatherPendingMessagesAsync(queueSession, ct)
            : [];
        var incidents = task.AgentSessionId is Guid incidentSession
            ? await GatherIncidentsAsync(incidentSession, ct)
            : [];
        var git = await GatherGitAsync(task, ct);

        return new CheckFacts(now, taskFacts, session, tail, git, pending, incidents, previousCheckAt);
    }

    private async Task<CheckSessionFacts?> GatherSessionAsync(AgentTask task, DateTime now, CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid sessionId)
            return null;

        var status = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (SessionStatus?)s.Status)
            .FirstOrDefaultAsync(ct);
        if (status is not SessionStatus sessionStatus)
            return null;

        var count = await _db.TranscriptEntries.AsNoTracking()
            .CountAsync(e => e.AgentSessionId == sessionId, ct);
        var lastAt = await _db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId)
            .MaxAsync(e => (DateTime?)e.CreatedAt, ct);

        // The shared verdict, not a private reimplementation of it.
        var working = await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct);

        return new CheckSessionFacts(
            sessionId, sessionStatus, working, count, lastAt, lastAt is { } at ? now - at : null);
    }

    private async Task<IReadOnlyList<CheckTranscriptLine>> GatherTranscriptTailAsync(
        Guid sessionId, CancellationToken ct)
    {
        var rows = await _db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId)
            .OrderByDescending(e => e.Sequence)
            .Take(TranscriptTailSize)
            .Select(e => new { e.Sequence, e.Kind, e.Text, e.ToolName, e.Timestamp, e.ToolInput, e.ToolIsError })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Sequence)
            .Select(r => new CheckTranscriptLine(
                r.Sequence,
                r.Kind,
                // A tool call's identity is still its NAME. ToolInput is carried separately so a
                // row with none degrades to today's line, never a blank.
                r.Kind == TranscriptKinds.ToolCall ? r.ToolName : Excerpt(r.Text),
                r.Timestamp,
                r.ToolInput,
                r.ToolIsError))
            .ToList();
    }

    private async Task<IReadOnlyList<CheckQueuedMessage>> GatherPendingMessagesAsync(
        Guid sessionId, CancellationToken ct)
    {
        // A stranded WhenIdle delivery is a classic stall signature: the delegate looks alive, and
        // the thing it is waiting for has been sitting in front of it the whole time.
        var maxAttempts = Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts);
        var rows = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .Select(m => new { m.Sequence, m.Origin, m.CreatedAt, m.Body, m.DeliveryAttempts, m.LastDeliveryStartedAt })
            .ToListAsync(ct);

        return rows
            .Select(m => new CheckQueuedMessage(
                m.Sequence,
                m.Origin,
                m.CreatedAt,
                Excerpt(m.Body) ?? string.Empty,
                m.DeliveryAttempts,
                m.LastDeliveryStartedAt,
                Parked: m.DeliveryAttempts >= maxAttempts,
                MaxDeliveryAttempts: maxAttempts,
                Label: QueueLabel(m.Origin, m.Body)))
            .OrderByDescending(m => m.Parked)
            .ThenBy(m => m.Sequence)
            .Take(PendingMessageLimit)
            .ToList();
    }

    private async Task<IReadOnlyList<CheckIncident>> GatherIncidentsAsync(Guid sessionId, CancellationToken ct)
    {
        // Limit is applied AFTER collapsing in RenderDigest, so five distinct incidents survive
        // where five identical ones used to fill the block.
        var rows = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new CheckIncident(i.Kind, i.Severity, i.Message, i.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>
    /// The git reads. Both go through <see cref="GitWorkspaceService"/>'s read-only path, so
    /// neither refreshes the index — the probe must not be able to change the workspace it is
    /// reporting on, even by a write git considers housekeeping.
    ///
    /// <para>Scope is selected from <see cref="AgentTask.Workspace"/>, not from a nullable range.
    /// A Shared (or ReadOnly) checkout is confirmed to be a repository and then returned as
    /// <see cref="CheckGitEvidenceScope.SharedWorkspaceUnattributable"/> with no log or status
    /// call — those facts can belong to another writer (CARD-0227). A Worktree task whose
    /// branch/merge-target coordinates are missing is unavailable, never silently Shared.</para>
    /// </summary>
    private async Task<CheckGitFacts?> GatherGitAsync(AgentTask task, CancellationToken ct)
    {
        var directory = task.WorktreePath ?? task.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        if (!await _git.IsRepositoryAsync(directory, ct))
            return null;

        if (task.Workspace != WorkspaceMode.Worktree)
        {
            return new CheckGitFacts(
                directory,
                CheckGitEvidenceScope.SharedWorkspaceUnattributable,
                Range: null,
                Commits: [],
                ChangedFiles: 0,
                UntrackedFiles: 0,
                Unavailable: null);
        }

        if (string.IsNullOrWhiteSpace(task.WorktreeBranch) || string.IsNullOrWhiteSpace(task.MergeTargetRef))
        {
            return new CheckGitFacts(
                directory,
                CheckGitEvidenceScope.TaskBranch,
                Range: null,
                Commits: [],
                ChangedFiles: 0,
                UntrackedFiles: 0,
                Unavailable: "the task-branch range could not be determined (WorktreeBranch or MergeTargetRef is missing)");
        }

        var range = $"{task.MergeTargetRef}..{task.WorktreeBranch}";
        var commits = await _git.LogOnelineAsync(
            directory, task.MergeTargetRef, task.WorktreeBranch, CommitLimit, null, ct);
        var counts = await _git.GetWorkingTreeCountsAsync(directory, ct);

        var unavailable = (commits, counts) switch
        {
            (null, null) => "git could not be read in this workspace",
            (null, _) => "the commit log could not be read",
            (_, null) => "the working tree status could not be read",
            _ => null,
        };

        return new CheckGitFacts(
            directory,
            CheckGitEvidenceScope.TaskBranch,
            range,
            commits ?? [],
            counts?.Changed ?? 0,
            counts?.Untracked ?? 0,
            unavailable);
    }

    private static string? Excerpt(string? text) => Excerpt(text, ExcerptChars);

    private static string? Excerpt(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= limit ? flat : flat[..limit] + "…";
    }

    // ---- rendering -----------------------------------------------------------------------------

    /// <summary>
    /// The fact bundle as text. This is what an interpreter reads AND what the caller gets verbatim
    /// when no interpreter is available — so it has to stand on its own, and every section that
    /// could not be gathered has to say so rather than be silently missing.
    /// </summary>
    public static string RenderDigest(CheckFacts facts)
    {
        var sb = new StringBuilder();
        var task = facts.Task;

        sb.Append("CAPTURED ").Append(Stamp(facts.At))
          .AppendLine(" — a snapshot of that moment, not of now. Nothing below updates");
        sb.AppendLine("after capture; if the delta since then matters, read the task row before acting on it.");
        sb.AppendLine();
        sb.Append("TASK ").Append(task.ShortId).Append(": ").AppendLine(task.Title);
        sb.Append("  status=").Append(task.Status)
          .Append(task.Settled ? " (SETTLED)" : string.Empty)
          .Append(" kind=").Append(task.Kind)
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(ModelLevelAliases.For(task.AgentKind, task.ModelLevel))
          .Append(" attempt=").Append(task.Attempt).Append('/').Append(task.MaxAttempts)
          .AppendLine();
        sb.Append("  dispatched=")
          .Append(task.DispatchedAt is { } at ? at.ToString("u") : "never")
          .Append(" elapsed=").Append(Duration(task.Age))
          .Append(" expected=").Append(task.ExpectedDurationMinutes).Append('m')
          .Append(" check#").Append(task.CheckNumber)
          .AppendLine();
        if (task.HasResult)
            sb.AppendLine("  a report is already stored on the task row");
        if (!string.IsNullOrWhiteSpace(task.FailureReason))
            sb.Append("  failureReason: ").AppendLine(Excerpt(task.FailureReason));

        sb.AppendLine();
        if (facts.Session is { } session)
        {
            sb.Append("SESSION ").Append(session.SessionId.ToString("N")[..8])
              .Append(": ").Append(session.Status)
              .Append(session.Working ? ", WORKING (mid-turn)" : ", idle (at the prompt)")
              .AppendLine();
            sb.Append("  transcript entries=").Append(session.TranscriptEntries)
              .Append(" last=")
              .Append(session.SinceLastEntry is { } quiet ? $"{Duration(quiet)} ago" : "never")
              .AppendLine();
        }
        else
        {
            sb.AppendLine("SESSION: none — this task has no live session row.");
        }

        sb.AppendLine();
        if (facts.TranscriptTail.Count == 0)
        {
            sb.AppendLine("TRANSCRIPT TAIL: empty — the session has written nothing.");
        }
        else
        {
            sb.Append("TRANSCRIPT TAIL (last ").Append(facts.TranscriptTail.Count).AppendLine("):");
            foreach (var (line, count) in CollapseConsecutiveTranscript(facts.TranscriptTail))
                AppendTranscriptLine(sb, line, count);
        }

        sb.AppendLine();
        if (facts.Git is { } git)
        {
            sb.Append("GIT (").Append(git.Directory).Append(')');
            if (git.Range is { } range)
                sb.Append(' ').Append(range);
            sb.AppendLine(":");
            if (git.Scope == CheckGitEvidenceScope.SharedWorkspaceUnattributable)
            {
                sb.Append("  ").AppendLine(SharedWorkspaceUnattributableExplanation);
            }
            else if (git.Unavailable is { } why)
            {
                sb.Append("  unavailable: ").AppendLine(why);
            }
            else
            {
                sb.Append("  commits=").Append(git.Commits.Count)
                  .Append(" changed=").Append(git.ChangedFiles)
                  .Append(" untracked=").Append(git.UntrackedFiles)
                  .AppendLine();
                foreach (var commit in git.Commits)
                    sb.Append("  · ").AppendLine(commit);
            }
        }
        else
        {
            sb.AppendLine("GIT: not a repository — no commit or working-tree evidence available.");
        }

        sb.AppendLine();
        sb.Append("DELEGATE QUEUE: ");
        if (facts.PendingMessages.Count == 0)
        {
            sb.AppendLine("nothing pending.");
        }
        else
        {
            sb.Append(facts.PendingMessages.Count).AppendLine(" message(s) still Pending:");
            foreach (var message in facts.PendingMessages)
            {
                sb.Append("  · #").Append(message.Sequence).Append(' ').Append(message.Label)
                  .Append(" (").Append(message.Origin).Append(", ")
                  .Append(Duration(facts.At - message.CreatedAt)).Append(" old)");
                if (message.Parked)
                {
                    sb.Append(" PARKED ").Append(message.DeliveryAttempts).Append('/')
                      .Append(message.MaxDeliveryAttempts).Append(" attempts");
                    if (message.LastDeliveryStartedAt is { } lastTried)
                        sb.Append(", last tried ").Append(Duration(facts.At - lastTried)).Append(" ago");
                }
                sb.Append(": ").AppendLine(message.Excerpt);
            }
        }

        AppendIncidents(sb, facts);

        return sb.ToString().ReplaceLineEndings("\n").TrimEnd() + "\n";
    }

    private static void AppendIncidents(StringBuilder sb, CheckFacts facts)
    {
        sb.Append("INCIDENTS: ");
        if (facts.Incidents.Count == 0)
        {
            sb.AppendLine("none on this session.");
            return;
        }

        var collapsed = CollapseIncidents(facts.Incidents);
        sb.Append(facts.Incidents.Count).Append(" on this session — ");
        if (facts.PreviousCheckAt is not DateTime previous)
        {
            sb.AppendLine("(first check — all are new to you):");
        }
        else
        {
            var newCount = facts.Incidents.Count(i => i.CreatedAt > previous);
            var prevNumber = Math.Max(1, facts.Task.CheckNumber - 1);
            var since = $"since check #{prevNumber} ({Clock(previous)}, {Duration(facts.At - previous)} ago)";
            if (newCount == 0)
                sb.Append("none NEW ").Append(since).AppendLine(":");
            else
                sb.Append(newCount).Append(" NEW ").Append(since).AppendLine(":");
        }

        foreach (var (incident, count) in collapsed)
        {
            var isNew = facts.PreviousCheckAt is DateTime prev && incident.CreatedAt > prev;
            sb.Append("  · ");
            if (isNew)
                sb.Append("NEW ");
            sb.Append(Clock(incident.CreatedAt))
              .Append(" (").Append(Duration(facts.At - incident.CreatedAt)).Append(" ago)  ")
              .Append(incident.Severity).Append(' ').Append(incident.Kind);
            if (count > 1)
                sb.Append(" ×").Append(count);
            sb.Append(": ").AppendLine(Excerpt(incident.Message));
        }
    }

    private static List<(CheckIncident Incident, int Count)> CollapseIncidents(
        IReadOnlyList<CheckIncident> incidents) =>
        incidents
            .GroupBy(i => (i.Severity, i.Kind, Excerpt: Excerpt(i.Message)))
            .Select(g => (
                Incident: g.OrderByDescending(i => i.CreatedAt).First(),
                Count: g.Count()))
            .OrderByDescending(g => g.Incident.CreatedAt)
            .Take(IncidentLimit)
            .ToList();

    private static void AppendTranscriptLine(StringBuilder sb, CheckTranscriptLine line, int count)
    {
        sb.Append("  #").Append(line.Sequence).Append(' ').Append(line.Kind);

        var input = Excerpt(line.ToolInput, ToolInputChars);
        if (input is not null)
        {
            if (!string.IsNullOrWhiteSpace(line.Excerpt))
                sb.Append(' ').Append(line.Excerpt);
            if (line.IsError == true)
                sb.Append(" ERROR");
            if (count > 1)
                sb.Append(" ×").Append(count);
            sb.Append(": ").Append(input);
        }
        else
        {
            // No ToolInput — today's shape, plus ERROR / ×N when they apply.
            if (line.IsError == true)
                sb.Append(" ERROR");
            if (count > 1)
                sb.Append(" ×").Append(count);
            if (!string.IsNullOrWhiteSpace(line.Excerpt))
                sb.Append(": ").Append(line.Excerpt);
        }

        sb.AppendLine();
    }

    private static List<(CheckTranscriptLine Line, int Count)> CollapseConsecutiveTranscript(
        IReadOnlyList<CheckTranscriptLine> lines)
    {
        var result = new List<(CheckTranscriptLine Line, int Count)>();
        foreach (var line in lines)
        {
            if (result.Count > 0 && TranscriptKey(result[^1].Line) == TranscriptKey(line))
                result[^1] = (result[^1].Line, result[^1].Count + 1);
            else
                result.Add((line, 1));
        }
        return result;
    }

    private static (string Kind, string? ToolName, string? Detail, bool Error) TranscriptKey(
        CheckTranscriptLine line) =>
        (line.Kind,
            line.Kind == TranscriptKinds.ToolCall ? line.Excerpt : null,
            Excerpt(line.ToolInput, ToolInputChars) ?? (line.Kind == TranscriptKinds.ToolCall ? null : line.Excerpt),
            line.IsError == true);

    private static string QueueLabel(QueuedMessageOrigin origin, string body)
    {
        var trimmed = body.TrimStart();
        // Pointer first, not a length/heuristic: a spilled brief must stay BRIEF even if a later
        // rule would treat a short pointer as plumbing (CARD-0025).
        if (origin == QueuedMessageOrigin.Delegation
            && trimmed.StartsWith(TypedBodySpill.PointerHeadline, StringComparison.Ordinal))
            return "BRIEF";

        return origin switch
        {
            QueuedMessageOrigin.Delegation when StartsWithSlash(trimmed) => "control-plane",
            QueuedMessageOrigin.Delegation => "BRIEF",
            QueuedMessageOrigin.System or QueuedMessageOrigin.Supervision or QueuedMessageOrigin.Check
                => "control-plane",
            QueuedMessageOrigin.Ui or QueuedMessageOrigin.Channel => "human",
            _ => origin.ToString(),
        };
    }

    private static bool StartsWithSlash(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            return ch == '/';
        }
        return false;
    }

    private static string Clock(DateTime at) =>
        at.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

    /// <summary>Full ISO-8601 UTC (day included) — a check note can be read the next morning.</summary>
    internal static string Stamp(DateTime at) =>
        at.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

    private static string Duration(TimeSpan? span)
    {
        if (span is not { } value)
            return "unknown";
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}h{value.Minutes:00}m"
            : $"{(int)value.TotalMinutes}m";
    }
}
