using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0033: project a Blocked task into the question, prior rounds and (bounded) git progress
/// the drawer and attention rows share. Null when the task is not Blocked.
/// </summary>
internal static class BlockedContextBuilder
{
    private static readonly TimeSpan ProgressBudget = TimeSpan.FromSeconds(2);

    public static async Task<BlockedContextDto?> BuildAsync(
        AgentTask task,
        IReadOnlyList<AgentTask> family,
        IReadOnlyList<AgentTaskEventDto> events,
        DelegateCheckProbe? probe,
        CancellationToken ct)
    {
        if (task.Status != AgentTaskStatus.Blocked)
            return null;

        var kind = Classify(task, events);
        var round = CurrentRound(events);
        var blockedAt = LatestBlockAt(events) ?? task.CompletedAt ?? task.DispatchedAt ?? task.CreatedAt;
        var (question, context) = QuestionAndContext(kind, task);
        var prior = PriorRounds(events);
        var progress = await ProgressAsync(task, events, probe, ct);
        var (canAnswer, cannotAnswer) = Answerability(kind, task);
        var mergeTaskId = kind == BlockedKind.MergeConflict
            ? family
                .Where(t => t.ParentTaskId == task.Id && t.Role == AgentTaskRole.Merge)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefault()
            : null;

        return new BlockedContextDto(
            kind, round, blockedAt, question, context, prior, progress,
            canAnswer, cannotAnswer, mergeTaskId);
    }

    public static BlockedKind Classify(AgentTask task, IReadOnlyList<AgentTaskEventDto> events)
    {
        var latest = events.LastOrDefault(e =>
            e.Type is AgentTaskEventType.Blocked or AgentTaskEventType.Conflicted);
        if (latest?.Type == AgentTaskEventType.Conflicted)
            return BlockedKind.MergeConflict;
        if (task.FailureReason is { } reason)
        {
            if (reason.StartsWith(BlockedQuestion.CostCeilingPrefix, StringComparison.Ordinal))
                return BlockedKind.CostCeiling;
            if (reason.StartsWith(ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal))
                return BlockedKind.RoutingExhausted;
        }

        return BlockedKind.Question;
    }

    public static int CurrentRound(IReadOnlyList<AgentTaskEventDto> events)
    {
        var count = events.Count(e => e.Type is AgentTaskEventType.Blocked or AgentTaskEventType.Conflicted);
        return count == 0 ? 1 : count;
    }

    public static string AttentionPrimary(AgentTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.FailureReason))
            return task.FailureReason;
        if (BlockedQuestion.TryExtract(task.Result, out var question, out _))
            return question;
        return task.Result ?? "The delegate is blocked and gave no reason.";
    }

    private static DateTime? LatestBlockAt(IReadOnlyList<AgentTaskEventDto> events) =>
        events
            .Where(e => e.Type is AgentTaskEventType.Blocked or AgentTaskEventType.Conflicted)
            .Select(e => (DateTime?)e.At)
            .LastOrDefault();

    private static (string Question, string? Context) QuestionAndContext(BlockedKind kind, AgentTask task)
    {
        if (kind != BlockedKind.Question)
            return (task.FailureReason ?? task.Result ?? "The delegate is blocked.", null);

        if (BlockedQuestion.TryExtract(task.Result, out var question, out var context))
            return (question, context);

        if (!string.IsNullOrWhiteSpace(task.Result))
        {
            BlockedQuestion.TryExtract(task.Result, out question, out context);
            if (!string.IsNullOrWhiteSpace(question))
                return (question, context);
            return (task.Result, null);
        }

        return ("The delegate is blocked and gave no reason.", null);
    }

    private static IReadOnlyList<BlockedRoundDto> PriorRounds(IReadOnlyList<AgentTaskEventDto> events)
    {
        var prior = new List<BlockedRoundDto>();
        var round = 0;
        string? question = null;
        DateTime askedAt = default;
        foreach (var e in events)
        {
            if (e.Type is AgentTaskEventType.Blocked or AgentTaskEventType.Conflicted)
            {
                if (question is not null)
                    prior.Add(new BlockedRoundDto(round, question, askedAt, null, null, null));
                round++;
                question = BlockedQuestion.QuestionFromEventDetail(e.Detail);
                askedAt = e.At;
            }
            else if (e.Type == AgentTaskEventType.Replied
                && question is not null
                && BlockedQuestion.IsBlockedAnswer(e.Detail))
            {
                var (answer, origin) = BlockedQuestion.AnswerFromEventDetail(e.Detail);
                prior.Add(new BlockedRoundDto(round, question, askedAt, answer, e.At, origin));
                question = null;
            }
        }

        return prior;
    }

    private static (bool CanAnswer, string? Reason) Answerability(BlockedKind kind, AgentTask task)
    {
        if (kind is BlockedKind.CostCeiling or BlockedKind.RoutingExhausted)
            return (false, task.FailureReason);
        if (task.AgentSessionId is null)
            return (false, "The delegate's session is no longer available.");
        return (true, null);
    }

    private static async Task<BlockedProgressDto?> ProgressAsync(
        AgentTask task,
        IReadOnlyList<AgentTaskEventDto> events,
        DelegateCheckProbe? probe,
        CancellationToken ct)
    {
        string? lastDigest = null;
        DateTime? lastAt = null;
        var check = events.LastOrDefault(e => e.Type == AgentTaskEventType.Check);
        if (check is not null)
        {
            lastAt = check.At;
            var line = check.Detail.ReplaceLineEndings("\n").Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
            lastDigest = line is { Length: > 400 } longLine ? longLine[..400] : line;
        }

        if (task.Workspace != WorkspaceMode.Worktree)
        {
            return new BlockedProgressDto(
                task.WorktreeBranch,
                [],
                0,
                0,
                lastDigest,
                lastAt,
                DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);
        }

        if (probe is null)
        {
            return new BlockedProgressDto(
                task.WorktreeBranch, [], 0, 0, lastDigest, lastAt,
                "git progress was not collected.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(ProgressBudget);
            var facts = await probe.GatherAsync(task, linked.Token);
            var git = facts.Git;
            if (git is null)
            {
                return new BlockedProgressDto(
                    task.WorktreeBranch, [], 0, 0, lastDigest, lastAt,
                    "working directory is not a git repository.");
            }

            var unavailable = git.Unavailable;
            if (git.Scope == DelegateCheckProbe.CheckGitEvidenceScope.SharedWorkspaceUnattributable)
                unavailable ??= DelegateCheckProbe.SharedWorkspaceUnattributableExplanation;

            return new BlockedProgressDto(
                task.WorktreeBranch,
                git.Commits,
                git.ChangedFiles,
                git.UntrackedFiles,
                lastDigest,
                lastAt,
                unavailable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new BlockedProgressDto(
                task.WorktreeBranch, [], 0, 0, lastDigest, lastAt, "git probe timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BlockedProgressDto(
                task.WorktreeBranch, [], 0, 0, lastDigest, lastAt, ex.Message);
        }
    }
}
