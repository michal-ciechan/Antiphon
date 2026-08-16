using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

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
/// <c>rev-parse</c> with <c>--no-optional-locks</c>, and a clock. It depends on NOTHING with a
/// write surface — no message queue, no session stopper, no runner client — so a reviewer can read
/// six lines and see that a check cannot type into, kill, or commit for the delegate it is
/// inspecting. There is no process probe here to get wrong, either: the card's self-matching
/// process-scan trap is excluded by construction rather than by care.</para>
/// </summary>
public sealed class DelegateCheckProbe
{
    /// <summary>How many transcript entries the tail carries. Enough to see the shape of a turn.</summary>
    private const int TranscriptTailSize = 10;

    /// <summary>Per-entry excerpt ceiling. A tail is for shape, not for reading the work.</summary>
    private const int ExcerptChars = 200;

    private const int CommitLimit = 20;
    private const int PendingMessageLimit = 5;
    private const int IncidentLimit = 5;

    private readonly AppDbContext _db;
    private readonly GitWorkspaceService _git;
    private readonly TimeProvider _timeProvider;

    public DelegateCheckProbe(AppDbContext db, GitWorkspaceService git, TimeProvider timeProvider)
    {
        _db = db;
        _git = git;
        _timeProvider = timeProvider;
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

    public sealed record CheckTranscriptLine(long Sequence, string Kind, string? Excerpt, DateTime? At);

    /// <param name="Range">
    /// <c>mergeTarget..branch</c> for a worktree task — commit messages are the durable report in
    /// this repo, so "what has landed on the branch" is the highest-signal fact available. Null for
    /// a Shared task, which commits straight onto its checkout's HEAD; there the bound is the
    /// task's own dispatch time instead.
    /// </param>
    /// <param name="Unavailable">
    /// Non-null when git could not answer, and why. A probe that reported "0 commits" for a git
    /// failure would read as "the delegate has done nothing", which is the opposite of the truth.
    /// </param>
    public sealed record CheckGitFacts(
        string Directory,
        string? Range,
        IReadOnlyList<string> Commits,
        int ChangedFiles,
        int UntrackedFiles,
        string? Unavailable);

    public sealed record CheckQueuedMessage(long Sequence, QueuedMessageOrigin Origin, DateTime CreatedAt, string Excerpt);

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
        IReadOnlyList<CheckIncident> Incidents);

    // ---- gathering -----------------------------------------------------------------------------

    public async Task<CheckFacts> GatherAsync(AgentTask task, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var taskFacts = new CheckTaskFacts(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            task.Kind,
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

        return new CheckFacts(now, taskFacts, session, tail, git, pending, incidents);
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
            .Select(e => new { e.Sequence, e.Kind, e.Text, e.ToolName, e.Timestamp })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Sequence)
            .Select(r => new CheckTranscriptLine(
                r.Sequence,
                r.Kind,
                // A tool call's identity is its NAME; its text is null and its input is JSON nobody
                // needs at triage altitude.
                r.Kind == TranscriptKinds.ToolCall ? r.ToolName : Excerpt(r.Text),
                r.Timestamp))
            .ToList();
    }

    private async Task<IReadOnlyList<CheckQueuedMessage>> GatherPendingMessagesAsync(
        Guid sessionId, CancellationToken ct)
    {
        // A stranded WhenIdle delivery is a classic stall signature: the delegate looks alive, and
        // the thing it is waiting for has been sitting in front of it the whole time.
        var rows = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .Take(PendingMessageLimit)
            .Select(m => new { m.Sequence, m.Origin, m.CreatedAt, m.Body })
            .ToListAsync(ct);

        return rows
            .Select(m => new CheckQueuedMessage(m.Sequence, m.Origin, m.CreatedAt, Excerpt(m.Body) ?? string.Empty))
            .ToList();
    }

    private async Task<IReadOnlyList<CheckIncident>> GatherIncidentsAsync(Guid sessionId, CancellationToken ct)
    {
        var rows = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(IncidentLimit)
            .Select(i => new CheckIncident(i.Kind, i.Severity, i.Message, i.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>
    /// The two git reads. Both go through <see cref="GitWorkspaceService"/>'s read-only path, so
    /// neither refreshes the index — the probe must not be able to change the workspace it is
    /// reporting on, even by a write git considers housekeeping.
    /// </summary>
    private async Task<CheckGitFacts?> GatherGitAsync(AgentTask task, CancellationToken ct)
    {
        var directory = task.WorktreePath ?? task.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        if (!await _git.IsRepositoryAsync(directory, ct))
            return null;

        var range = task.WorktreeBranch is { } branch && task.MergeTargetRef is { } target
            ? $"{target}..{branch}"
            : null;
        var commits = range is null
            ? await _git.LogOnelineAsync(directory, null, "HEAD", CommitLimit, task.DispatchedAt, ct)
            : await _git.LogOnelineAsync(directory, task.MergeTargetRef, task.WorktreeBranch!, CommitLimit, null, ct);
        var counts = await _git.GetWorkingTreeCountsAsync(directory, ct);

        var unavailable = (commits, counts) switch
        {
            (null, null) => "git could not be read in this workspace",
            (null, _) => "the commit log could not be read",
            (_, null) => "the working tree status could not be read",
            _ => null,
        };

        return new CheckGitFacts(
            directory, range, commits ?? [], counts?.Changed ?? 0, counts?.Untracked ?? 0, unavailable);
    }

    private static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= ExcerptChars ? flat : flat[..ExcerptChars] + "…";
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

        sb.Append("TASK ").Append(task.ShortId).Append(": ").AppendLine(task.Title);
        sb.Append("  status=").Append(task.Status)
          .Append(task.Settled ? " (SETTLED)" : string.Empty)
          .Append(" kind=").Append(task.Kind)
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(ModelLevelAliases.ForClaude(task.ModelLevel))
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
            foreach (var line in facts.TranscriptTail)
            {
                sb.Append("  #").Append(line.Sequence).Append(' ').Append(line.Kind);
                if (!string.IsNullOrWhiteSpace(line.Excerpt))
                    sb.Append(": ").Append(line.Excerpt);
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        if (facts.Git is { } git)
        {
            sb.Append("GIT (").Append(git.Directory).Append(')');
            if (git.Range is { } range)
                sb.Append(' ').Append(range);
            sb.AppendLine(":");
            if (git.Unavailable is { } why)
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
                sb.Append("  · #").Append(message.Sequence).Append(' ').Append(message.Origin)
                  .Append(": ").AppendLine(message.Excerpt);
        }

        sb.Append("INCIDENTS: ");
        if (facts.Incidents.Count == 0)
        {
            sb.AppendLine("none on this session.");
        }
        else
        {
            sb.Append(facts.Incidents.Count).AppendLine(":");
            foreach (var incident in facts.Incidents)
                sb.Append("  · ").Append(incident.Severity).Append(' ').Append(incident.Kind)
                  .Append(": ").AppendLine(Excerpt(incident.Message));
        }

        return sb.ToString().ReplaceLineEndings("\n").TrimEnd() + "\n";
    }

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
