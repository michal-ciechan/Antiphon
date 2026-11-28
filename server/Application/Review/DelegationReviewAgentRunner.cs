using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Abf;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// The reviewer agents (design 4.7) as delegate tasks. A first call maps the subject's repo to an
/// Antiphon project (<c>Review:Agents:Repos</c>, else the project's <c>GitRepositoryUrl</c> or
/// <c>LocalRepositoryPath</c>), makes the head reachable in that project's checkout (a GitHub PR head
/// is fetched from <c>refs/pull/&lt;n&gt;/head</c>), creates one read-only Review task in a fresh worktree
/// cut at the head SHA with the group's tier level, and returns <see cref="ReviewAgentStatus.Pending"/>
/// with the task id as the handle. The executor parks the reviewer node and polls on later passes:
/// a running task stays Pending, a succeeded one completes with its final report, and a failed,
/// cancelled, blocked or timed-out one throws <see cref="ReviewAgentRunnerException"/>, which fails the
/// node with the reason. The executor never waits on a task.
/// </summary>
public sealed class DelegationReviewAgentRunner(
    AppDbContext db,
    IReviewTaskGateway tasks,
    IOptions<ReviewAgentSettings> settings,
    TimeProvider time,
    ILogger<DelegationReviewAgentRunner> logger) : IReviewAgentRunner
{
    /// <summary>The task goal limit (<c>AgentTaskService</c>).</summary>
    public const int MaxGoalChars = 20_000;

    public const string NoProjectPrefix = "No Antiphon project maps";

    public async Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct)
    {
        if (request.Handle is { Length: > 0 } handle)
            return await PollAsync(request, handle, ct);

        var options = settings.Value;
        var (project, repoPath) = await ResolveProjectAsync(request, options, ct);
        await tasks.EnsureHeadAsync(repoPath, request.Host, request.PrNumber, request.HeadSha, ct);
        var create = BuildCreateRequest(request, options);
        Guid taskId;
        try
        {
            taskId = await tasks.CreateAsync(create, project.Id, repoPath, ct);
        }
        catch (WorkspaceAdmissionBusyException)
        {
            return ReviewAgentResult.Retry();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ReviewAgentRunnerException))
        {
            throw new ReviewAgentRunnerException($"The reviewer task for group {request.GroupKey} could not be created in project " +
                $"'{project.Name}' ({repoPath}): {ex.Message}");
        }

        logger.LogInformation("Review run {RunId} reviewer {Group}: dispatched task {TaskId} ({Kind} {Level}) at {HeadSha}",
            request.RunId, request.GroupKey, taskId, create.AgentKind, create.ModelLevel, request.HeadSha);
        return ReviewAgentResult.Pending(taskId.ToString("D"));
    }

    /// <summary>
    /// The task to create: Worker, the configured role (Review), a fresh worktree cut at the head SHA,
    /// never committed on settle, the kind and the level of the group's tier, the configured runner.
    /// </summary>
    public static CreateAgentTaskRequest BuildCreateRequest(ReviewAgentRequest request, ReviewAgentSettings options) => new(
        Goal: Brief(request, options),
        Title: Title(request),
        Kind: AgentTaskKind.Worker,
        Role: options.Role,
        ModelLevel: options.LevelFor(request.Tier),
        AgentKind: options.Kind,
        Workspace: WorkspaceMode.Worktree,
        ExpectedMinutes: Math.Clamp(options.ExpectedMinutes, 1, 1440),
        AutoContinue: false,
        CommitOnSettle: "Never",
        RunnerId: string.IsNullOrWhiteSpace(options.Runner) ? null : options.Runner.Trim(),
        WorktreeBaseRequestedRef: request.HeadSha);

    private async Task<ReviewAgentResult> PollAsync(ReviewAgentRequest request, string handle, CancellationToken ct)
    {
        if (!Guid.TryParse(handle, out var taskId))
            throw new ReviewAgentRunnerException($"Reviewer handle '{handle}' is not an agent task id.");
        var task = await tasks.ReadAsync(taskId, ct)
            ?? throw new ReviewAgentRunnerException($"Reviewer task {Short(taskId)} no longer exists.");

        switch (task.Status)
        {
            case AgentTaskStatus.Succeeded:
            {
                var text = task.ReportText();
                if (string.IsNullOrWhiteSpace(text))
                    throw new ReviewAgentRunnerException($"Reviewer task {Short(taskId)} succeeded with no report.");
                return ReviewAgentResult.Completed(text, Usage(task));
            }
            case AgentTaskStatus.Failed:
                throw new ReviewAgentRunnerException($"Reviewer task {Short(taskId)} failed: {task.FailureReason ?? "no reason recorded"}");
            case AgentTaskStatus.Canceled:
                throw new ReviewAgentRunnerException($"Reviewer task {Short(taskId)} was cancelled{(task.FailureReason is { } why ? $": {why}" : "")}.");
            case AgentTaskStatus.Blocked:
                await tasks.CancelAsync(taskId, "The review engine takes no questions from a reviewer.", ct);
                throw new ReviewAgentRunnerException($"Reviewer task {Short(taskId)} asked a question instead of answering: {Excerpt(task.Result)}");
        }

        var limit = TimeSpan.FromMinutes(Math.Max(1, settings.Value.TimeoutMinutes));
        var age = time.GetUtcNow().UtcDateTime - task.CreatedAt;
        if (age > limit)
        {
            await tasks.CancelAsync(taskId, $"The review engine timed this reviewer out after {limit.TotalMinutes:0} minutes.", ct);
            throw new ReviewAgentRunnerException(
                $"Reviewer task {Short(taskId)} did not finish within {limit.TotalMinutes:0} minutes (Review:Agents:TimeoutMinutes); it was cancelled.");
        }

        return ReviewAgentResult.Pending(handle);
    }

    /// <summary>A seat's cost is api-equivalent (D-20).</summary>
    private static ReviewAgentUsage Usage(ReviewTaskState task) => new(
        $"delegate-{task.Kind}".ToLowerInvariant(),
        task.Model ?? $"{task.Kind} {task.Level}",
        ReviewTransport.Seat,
        task.InputTokens,
        task.OutputTokens,
        task.CostUsd,
        "api-equivalent");

    /// <summary>
    /// The project and checkout for <paramref name="request"/>'s repo: an explicit
    /// <c>Review:Agents:Repos</c> row, else the one live project whose <c>GitRepositoryUrl</c> names the
    /// repo (GitHub) or whose <c>LocalRepositoryPath</c> is the subject's path (local git).
    /// </summary>
    public async Task<(Project Project, string RepoPath)> ResolveProjectAsync(ReviewAgentRequest request, ReviewAgentSettings options,
        CancellationToken ct)
    {
        var row = options.FindRepo(request.Repo);
        Project? project = null;
        if (row?.ProjectId is { } id)
        {
            project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new ReviewAgentRunnerException($"Review:Agents:Repos maps {request.Repo} to project {id}, which does not exist.");
        }
        else
        {
            var live = await db.Projects.AsNoTracking().Where(p => p.ArchivedAt == null).ToListAsync(ct);
            if (row?.Project is { Length: > 0 } name)
            {
                project = live.SingleOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ReviewAgentRunnerException($"Review:Agents:Repos maps {request.Repo} to project '{name}', which does not exist.");
            }
            else
            {
                var matches = live.Where(p => RepoUrlNames(p.GitRepositoryUrl, request.Repo)
                        || (request.Host == ReviewHost.Local && SamePath(p.LocalRepositoryPath, request.LocalPath)))
                    .ToList();
                if (matches.Count > 1)
                    throw new ReviewAgentRunnerException($"{matches.Count} Antiphon projects map {request.Repo} " +
                        $"({string.Join(", ", matches.Select(p => p.Name))}); name one in Review:Agents:Repos.");
                project = matches.SingleOrDefault();
            }
        }

        if (project is null)
            throw new ReviewAgentRunnerException($"{NoProjectPrefix} {request.Repo}: set a project's GitRepositoryUrl (or LocalRepositoryPath " +
                "for local git), or add a Review:Agents:Repos row, so its reviewers have a checkout to run in.");
        var repoPath = row?.LocalPath is { Length: > 0 } configured ? configured
            : project.LocalRepositoryPath is { Length: > 0 } local ? local
            : request.Host == ReviewHost.Local ? request.LocalPath : null;
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new ReviewAgentRunnerException($"Project '{project.Name}' has no LocalRepositoryPath for {request.Repo}'s reviewers " +
                "(or set LocalPath on its Review:Agents:Repos row).");
        return (project, repoPath);
    }

    /// <summary>True when <paramref name="url"/> (https, ssh or scp form, with or without <c>.git</c>) ends in <paramref name="repo"/>.</summary>
    public static bool RepoUrlNames(string? url, string repo)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var text = url.Trim().TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            text = text[..^4];
        var parts = text.Replace('\\', '/').Replace(':', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && string.Equals($"{parts[^2]}/{parts[^1]}", repo.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Title(ReviewAgentRequest request)
    {
        var subject = request.PrNumber is { } n ? $"{request.Repo}#{n}" : request.Repo;
        var title = $"Review {subject} {request.GroupKey} at {Short(request.HeadSha)}";
        return title.Length <= 200 ? title : title[..200];
    }

    /// <summary>
    /// The reviewer's brief: what to review and at which head, that it is read-only, the pack (inline
    /// when it fits, else where to read it), the assigned checks and the review-findings/1.0 contract.
    /// <paramref name="inlineLimit"/> overrides the inline bound (<c>InlinePackMaxChars</c> capped by the
    /// task goal limit): the one-shot CLI passes <see cref="int.MaxValue"/> to always inline the pack.
    /// With <paramref name="hasCheckout"/> false the reviewer is told it has the pack alone.
    /// </summary>
    public static string Brief(ReviewAgentRequest request, ReviewAgentSettings options, int? inlineLimit = null, bool hasCheckout = true)
    {
        var subject = request.PrNumber is { } n ? $"{request.Repo} PR #{n}" : request.Repo;
        var text = new StringBuilder()
            .Append("Review ").Append(subject).Append(" at ").Append(request.HeadSha).Append(" for the Antiphon review engine (run ")
            .Append(request.RunId.ToString("D")).Append(", iteration ").Append(request.Iteration).Append(", round ").Append(request.Round)
            .Append(", reviewer group ").Append(request.GroupKey).Append(", attempt ").Append(request.Attempt).AppendLine(").")
            .AppendLine()
            .AppendLine(hasCheckout
                ? "This is a read-only code review. Your worktree is checked out at the reviewed head. Do not edit, commit or push,"
                : "This is a read-only code review. No checkout of the repository is available: your folder is empty and the context pack below is all you have. Do not edit, commit or push,")
            .AppendLine("do not build or run tests, and do not delegate. The pipeline review rules about checkpoints, review evidence and")
            .AppendLine("landing do not apply to this task: its only output is the review-findings document described below.")
            .AppendLine()
            .AppendLine("Read the context pack: the PR, the diff, the outstanding findings, and your group's checks with their prompts.")
            .AppendLine(hasCheckout
                ? "Open any file in the worktree you need, and list each one you read beyond the pack in the context record."
                : "Review from the pack alone and leave the context record out; mark a check unverified when it needs code the pack does not show.")
            .AppendLine("Anchor each finding on one line copied verbatim from the file at the reviewed head. Do not re-report the")
            .AppendLine("outstanding findings the pack lists.")
            .AppendLine()
            .Append("Assigned checks (each gets one row in the checks record): ").AppendLine(string.Join(", ", request.AssignedChecks))
            .AppendLine()
            .AppendLine("Your final message must contain the answer:")
            .AppendLine(AbfTemplates.ReviewFindings)
            .AppendLine("After `=== end` you may add the usual report ending; nothing before the document is read.")
            .AppendLine();

        var limit = inlineLimit ?? Math.Min(Math.Max(0, options.InlinePackMaxChars), MaxGoalChars - text.Length - 200);
        if (request.Pack.Length <= limit)
        {
            text.Append("--- context pack (hash ").Append(request.PackHash[..Math.Min(12, request.PackHash.Length)]).AppendLine(") ---")
                .AppendLine(request.Pack.TrimEnd())
                .AppendLine("--- end of context pack ---");
        }
        else
        {
            var path = $"/api/review-runs/{request.RunId:D}/packs/{request.Iteration}/{Uri.EscapeDataString(request.GroupKey)}";
            var safeGroup = new string(request.GroupKey.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
            var file = $"antiphon-review-pack-{request.RunId:N}-{request.Iteration}-{safeGroup}.md";
            text.Append("The context pack is ").Append(request.Pack.Length).Append(" characters (hash ")
                .Append(request.PackHash[..Math.Min(12, request.PackHash.Length)])
                .AppendLine("). Save it to a temporary file outside the worktree, then read that file in full:")
                .Append("  PowerShell: Invoke-RestMethod \"$env:ANTIPHON_API").Append(path)
                .Append("\" | Set-Content -Encoding utf8 (Join-Path ([IO.Path]::GetTempPath()) '").Append(file).AppendLine("')")
                .Append("  bash: curl -fsS \"$ANTIPHON_API").Append(path).Append("\" -o \"${TMPDIR:-/tmp}/").Append(file).AppendLine("\"");
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    private static string Excerpt(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "(no text)" : text.Trim().Length <= 300 ? text.Trim() : text.Trim()[..300] + "…";

    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string Short(string sha) => sha.Length <= 12 ? sha : sha[..12];
}
