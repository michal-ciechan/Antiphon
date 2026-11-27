using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Abf;
using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Review.Execution;

/// <summary>One reviewer's parsed answer: its findings, check rows and summary, or the handle of a reviewer still running.</summary>
public sealed record ReviewerOutput(
    IReadOnlyList<AbfFinding> Findings,
    IReadOnlyList<AbfCheckStatus> Checks,
    string? Summary,
    IReadOnlyList<string> Warnings,
    string? Handle = null,
    int Attempts = 1);

/// <summary>A collated finding, before it is verified and published. <see cref="Cid"/> is its candidate id.</summary>
public sealed record FindingCandidate(
    string Cid,
    string Group,
    string Check,
    ReviewSeverity Severity,
    double Confidence,
    string File,
    int Line,
    int? LineEnd,
    bool InDiff,
    string Anchor,
    string Title,
    string Body,
    string? Suggestion,
    IReadOnlyList<string> Flags,
    string? Hunk);

public sealed record DroppedFinding(string Group, string Check, string File, string Anchor, string Reason);

public sealed record CollateOutput(IReadOnlyList<FindingCandidate> Candidates, IReadOnlyList<DroppedFinding> Dropped, IReadOnlyList<string> UnverifiedChecks);

public sealed record VerifyOutput(IReadOnlyList<string> Kept, IReadOnlyList<DroppedFinding> Dropped, bool Verified);

public sealed record PublishOutput(IReadOnlyList<string> Inline, IReadOnlyList<string> SummaryOnly, IReadOnlyList<string> BelowBar, string? ReviewId,
    string? ReviewUrl);

/// <summary>The review section (design 4.7 to 4.9): the reviewer fan-out, collate, verify and publish.</summary>
public static class ReviewReviewSteps
{
    /// <summary>A reviewer answer that does not parse is asked for once more, then the reviewer fails (design 4.7).</summary>
    public const int MaxReviewerAttempts = 2;

    /// <summary>
    /// The fan-out (design 3.1): one child execution of the reviewer node per group (<c>itemKey</c>),
    /// each run through <see cref="IReviewAgentRunner"/> with its pack, its answer parsed as
    /// <c>review-findings/1</c> against its assigned checks. With policy <c>all</c>, one failed child
    /// fails the node; a child still running elsewhere leaves the run queued for the next pass.
    /// </summary>
    public static async Task<NodeResult> FanOutAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var childNodeId = node.StringParam("node") ?? "reviewer";
        var childKind = walk.Workflow.Node(childNodeId)?.Kind ?? ReviewNodeKind.Agent;
        var groups = ReviewWalk.Output<GroupOutput>(walk.LatestOf("GroupChecks"))?.Groups ?? [];
        if (groups.Count == 0)
            return NodeResult.Ok(null, "No reviewer groups: nothing to review");

        var packs = await walk.Db.ReviewContextPacks.AsNoTracking()
            .Where(p => p.RunId == walk.Run.Id && p.Iteration == walk.Iteration.Iteration)
            .ToDictionaryAsync(p => p.GroupKey, ct);
        var runner = walk.Services.GetRequiredService<IReviewAgentRunner>();
        var logger = walk.Services.GetRequiredService<ILogger<ReviewWalkLog>>();
        var subject = walk.Run.Subject;
        var localPath = subject.Host == ReviewHost.Local ? walk.Binding.Target.LocalPath : null;
        foreach (var group in groups)
        {
            var child = walk.Latest(childNodeId, group.Key) ?? walk.NewExecution(childNodeId, childKind, group.Key);
            if (child.Status is ReviewNodeStatus.Succeeded or ReviewNodeStatus.Failed)
                continue;
            if (!packs.TryGetValue(group.Key, out var pack))
            {
                Complete(walk, child, ReviewNodeStatus.Failed, $"No context pack for group {group.Key}", null);
                continue;
            }

            child.Status = ReviewNodeStatus.Running;
            child.StartedAt ??= walk.Now();
            await SaveReviewerProgressAsync(walk, ct);
            var previous = ReviewWalk.Output<ReviewerOutput>(child);
            var attempt = previous?.Attempts ?? 1;
            var unchanged = false;
            try
            {
                while (true)
                {
                    var result = await runner.RunAsync(new ReviewAgentRequest(walk.Run.Id, walk.Iteration.Iteration, walk.Run.Round, group.Key,
                        pack.Content, pack.Hash, group.CheckIds, subject.Repo, subject.PrNumber, walk.Iteration.HeadSha ?? walk.HeadSha, attempt,
                        previous?.Handle, group.Tier, subject.Host, localPath, child.Id), ct);
                    Usage(child, result.Usage);
                    if (result.Status == ReviewAgentStatus.Pending)
                    {
                        // A reviewer still running under the same handle changes nothing on the code host.
                        unchanged = previous?.Handle is { } polled && polled == result.Handle;
                        child.Status = ReviewNodeStatus.Waiting;
                        child.Summary = $"reviewer running ({result.Handle})";
                        child.OutputJson = ReviewWalk.ToJson(new ReviewerOutput([], [], null, [], result.Handle, attempt));
                        break;
                    }

                    var parsed = AbfParser.ParseReviewFindings(result.Text, AbfMode.Tolerant, group.CheckIds);
                    if (parsed.HasErrors || parsed.Value is null)
                    {
                        var errors = string.Join(", ", parsed.Document.Diagnostics.Where(d => d.Severity == AbfSeverity.Error)
                            .Select(d => d.Name is null ? d.Code : $"{d.Code} ({d.Name})").Distinct().Take(8));
                        if (attempt < MaxReviewerAttempts)
                        {
                            logger.LogInformation("Review run {RunId} reviewer {Group}: answer did not parse ({Errors}); asking again",
                                walk.Run.Id, group.Key, errors);
                            attempt++;
                            previous = null;
                            continue;
                        }

                        Complete(walk, child, ReviewNodeStatus.Failed,
                            $"The reviewer's answer is not a valid review-findings/1 document after {attempt} attempts: {errors}", null);
                        break;
                    }

                    var doc = parsed.Value;
                    var warnings = parsed.Document.Diagnostics.Where(d => d.Severity == AbfSeverity.Warning)
                        .Select(d => d.Name is null ? d.Code : $"{d.Code} ({d.Name})").Distinct().ToList();
                    var counts = doc.Checks.GroupBy(c => c.Status).Select(g => $"{g.Count()} {g.Key}");
                    Complete(walk, child, ReviewNodeStatus.Succeeded,
                        $"{doc.Findings.Count} findings; checks: {string.Join(", ", counts)}",
                        new ReviewerOutput(doc.Findings, doc.Checks, doc.Summary, warnings, null, attempt));
                    break;
                }
            }
            catch (ReviewAgentRunnerException ex)
            {
                Complete(walk, child, ReviewNodeStatus.Failed, ex.Message, null);
            }

            await SaveReviewerProgressAsync(walk, ct);
            if (unchanged)
                continue;
            var done = groups.Count(g => walk.Latest(childNodeId, g.Key)?.Status == ReviewNodeStatus.Succeeded);
            await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, CodeHostStatusState.Pending, $"{done}/{groups.Count} reviewers done", ct);
        }

        var children = groups.Select(g => (Group: g, Child: walk.Latest(childNodeId, g.Key)!)).ToList();
        if (children.FirstOrDefault(c => c.Child.Status == ReviewNodeStatus.Failed) is { Child: not null } failed)
            return NodeResult.Fail($"Reviewer {failed.Group.Key} failed: {failed.Child.Summary}");
        if (children.Any(c => c.Child.Status == ReviewNodeStatus.Waiting))
            return NodeResult.Poll($"{children.Count(c => c.Child.Status == ReviewNodeStatus.Succeeded)}/{children.Count} reviewers done");
        return NodeResult.Ok(null, $"{children.Count} reviewers done: "
            + string.Join(", ", children.Select(c => $"{c.Group.Key} {ReviewWalk.Output<ReviewerOutput>(c.Child)?.Findings.Count ?? 0}")));
    }

    internal static Task SaveReviewerProgressAsync(ReviewWalk walk, CancellationToken ct)
    {
        // Include the run's status check when admitting the reviewer and saving its result.
        walk.Run.UpdatedAt = walk.Now();
        walk.Db.Entry(walk.Run).Property(run => run.UpdatedAt).IsModified = true;
        return walk.Db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Collate (design 4.8): every reviewer's findings, duplicates collapsed (same file, anchor and
    /// check, keeping the most severe), duplicates of open findings dropped, and each anchor resolved
    /// at the reviewed head (the diff, then the file); a finding whose anchor is not found is dropped.
    /// </summary>
    public static async Task<NodeResult> CollateAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var from = node.StringParam("from") ?? "review";
        var childNodeId = walk.Workflow.Node(from)?.StringParam("node") ?? "reviewer";
        var outputs = walk.Run.Nodes
            .Where(n => n.NodeId == childNodeId && n.Iteration == walk.Iteration.Iteration && n.ItemKey != null && n.Status == ReviewNodeStatus.Succeeded)
            .Select(n => (Group: n.ItemKey!, Output: ReviewWalk.Output<ReviewerOutput>(n)))
            .Where(o => o.Output is not null)
            .ToList();
        var diff = await walk.DiffAsync(walk.WholeRange, ct);
        var open = await ReviewIntakeSteps.OpenFindingsAsync(walk, ct);
        var openKeys = open.ToDictionary(f => Fingerprint(f.FilePath, f.Anchor), f => f.Key);
        var dropped = new List<DroppedFinding>();
        var kept = new Dictionary<string, FindingCandidate>(StringComparer.Ordinal);
        var files = new Dictionary<string, string?>(StringComparer.Ordinal);
        var unverified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (group, output) in outputs)
        {
            foreach (var row in output!.Checks.Where(c => c.Status == "unverified"))
                unverified.Add(row.CheckId);
            foreach (var finding in output.Findings)
            {
                var print = Fingerprint(finding.File, finding.Anchor);
                if (openKeys.TryGetValue(print, out var existing))
                {
                    dropped.Add(new DroppedFinding(group, finding.Check, finding.File, finding.Anchor, $"duplicate of open finding {existing}"));
                    continue;
                }

                string? fileText = null;
                var location = ReviewDiffAnchors.Locate(diff, finding.File, finding.Anchor);
                if (location is null && walk.Host.Capabilities.CanReadFiles)
                {
                    if (!files.TryGetValue(finding.File, out fileText))
                        files[finding.File] = fileText = await walk.Host.ReadFileAsync(walk.Binding.Target, walk.Iteration.HeadSha ?? walk.HeadSha, finding.File, ct);
                    location = ReviewDiffAnchors.Locate(null, finding.File, finding.Anchor, fileText);
                }

                if (location is null)
                {
                    dropped.Add(new DroppedFinding(group, finding.Check, finding.File, finding.Anchor, "anchor not found at the reviewed head"));
                    continue;
                }

                var severity = Enum.TryParse<ReviewSeverity>(finding.Severity, ignoreCase: true, out var s) ? s : ReviewSeverity.Minor;
                var key = $"{print}|{finding.Check.ToLowerInvariant()}";
                var candidate = new FindingCandidate("", group, finding.Check, severity, finding.Confidence, finding.File, location.Line,
                    finding.Lines?.End is { } end && end > location.Line ? end : null, location.InDiff, finding.Anchor.Trim(),
                    finding.Title is { Length: > 0 } title ? title : FirstLine(finding.Body), finding.Body, finding.Suggestion,
                    finding.Flags.Select(f => f.ToLowerInvariant()).ToList(), location.Hunk);
                if (kept.TryGetValue(key, out var twin))
                {
                    var better = (candidate.Severity, candidate.Confidence).CompareTo((twin.Severity, twin.Confidence)) > 0 ? candidate : twin;
                    kept[key] = better with { Flags = twin.Flags.Union(candidate.Flags).ToList() };
                    dropped.Add(new DroppedFinding(group, finding.Check, finding.File, finding.Anchor, "duplicate across reviewer groups"));
                    continue;
                }

                kept[key] = candidate;
            }
        }

        var candidates = kept.Values.Select((c, i) => c with { Cid = $"C{i + 1}" }).ToList();
        var summary = $"{candidates.Count} findings from {outputs.Count} reviewers"
            + (dropped.Count > 0 ? $"; {dropped.Count} dropped ({string.Join(", ", dropped.GroupBy(d => d.Reason.Split(' ')[0]).Select(g => $"{g.Count()} {g.Key}"))})" : "");
        return NodeResult.Ok(null, summary, new CollateOutput(candidates, dropped, unverified.Order(StringComparer.Ordinal).ToList()));
    }

    /// <summary>
    /// Verify (design 4.8): a cheaper model tries to disprove each finding through the node's
    /// completion chain, answering <c>decision-response/1.0</c> with the probability each holds. A
    /// disproved finding is dropped and recorded; a bypass finding is never dropped by the verifier
    /// alone; a refused answer keeps the finding. The instructions carry the answer's ABF shape, since a
    /// chat model or seat knows nothing of ABF. Only an empty chain skips the node.
    /// </summary>
    public static async Task<NodeResult> VerifyAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var candidates = ReviewWalk.Output<CollateOutput>(walk.LatestOf("CollateFindings"))?.Candidates ?? [];
        var all = candidates.Select(c => c.Cid).ToList();
        if (candidates.Count == 0)
            return NodeResult.Ok(null, "Nothing to verify", new VerifyOutput([], [], false));
        var chain = node.StringParam("chain") ?? "completions-cheap";
        var completions = walk.Services.GetRequiredService<CompletionChain>();
        if (!completions.IsConfigured(chain))
        {
            return NodeResult.Skip($"No completion chain '{chain}' is configured (Review:Completions); {candidates.Count} findings passed through unverified",
                new VerifyOutput(all, [], false));
        }

        var requestId = $"verify-{walk.Run.Id:N}-{walk.Iteration.Iteration}";
        var questions = candidates.Select(c => new AbfQuestion(HoldsName(c.Cid), "noul",
            $"Does finding {c.Cid} hold: is it a real defect in the change?", [])).ToList();
        var request = new CompletionRequest(
            $"""
            You verify code review findings. Try to disprove each finding below against the diff: a finding holds only when the
            defect is real at the reviewed head. Answer with one ABF decision-response/1.0 document for request {requestId}, with one
            noul answer per finding named {string.Join(", ", questions.Select(q => q.Name))}: the value is the probability the finding holds.

            {AbfTemplates.DecisionResponse(requestId)}
            """,
            [
                new CompletionInput("findings", string.Join("\n\n", candidates.Select(c =>
                    $"{c.Cid} [{c.Severity}, check {c.Check}] {c.File}:{c.Line}\nanchor: {c.Anchor}\n{c.Title}\n{c.Body}"))),
                new CompletionInput("diff", Truncate(await walk.DiffAsync(walk.WholeRange, ct) ?? "(no diff)", ReviewCategorisation.MaxDiffChars)),
            ],
            "decision-response/1.0");
        // Verification only drops findings: when it cannot run and may continue, every finding goes through unverified.
        NodeResult Unverified(string reason, NodeUsage? spent) => walk.ContinuesOnFailure(node)
            ? NodeResult.Ok(null, $"{candidates.Count} findings passed through unverified", new VerifyOutput(all, [], false), spent).AsDegraded(reason)
            : NodeResult.Fail(reason, spent);

        CompletionChainResult answered;
        try
        {
            answered = await completions.CompleteAsync(chain, request, ct);
        }
        catch (DecisionChainException ex)
        {
            return Unverified($"No verifier answered: {ex.Message}", null);
        }

        var usage = NodeUsage.From(answered.Calls, answered.CostUsd);
        var requestDoc = new DecisionRequestDocument { RequestId = requestId, State = "", Questions = questions };
        var parsed = AbfParser.ParseDecisionResponse(answered.Text, requestDoc);
        if (parsed.Value is null)
        {
            var codes = string.Join(", ", parsed.Document.Diagnostics.Where(d => d.Severity == AbfSeverity.Error).Select(d => d.Code).Distinct());
            return Unverified($"The verifier's answer is not a decision-response/1.0 for {requestId}: {codes}", usage);
        }

        var policy = walk.Policy;
        var keptIds = new List<string>();
        var dropped = new List<DroppedFinding>();
        foreach (var candidate in candidates)
        {
            var answer = parsed.Value.Answer(HoldsName(candidate.Cid));
            var p = answer is { Refused: false } ? answer.Probability : null;
            if (p is { } holds && holds < policy.VerifyDropBelow)
            {
                if (ReviewRoundsPolicy.IsBypass(candidate.Flags, policy))
                {
                    keptIds.Add(candidate.Cid);
                    continue;
                }

                dropped.Add(new DroppedFinding(candidate.Group, candidate.Check, candidate.File, candidate.Anchor,
                    $"disproved by the verifier (p={holds:0.00})"));
                continue;
            }

            keptIds.Add(candidate.Cid);
        }

        return NodeResult.Ok(null, $"{keptIds.Count} kept, {dropped.Count} disproved", new VerifyOutput(keptIds, dropped, true), usage);
    }

    /// <summary>
    /// Publish (design 4.9): stores the verified findings, then posts those that clear the round's
    /// bar as one PR review (inline comments plus a summary; a round-1 nit and a line outside the diff
    /// go in the summary only), records each comment's thread, and sets <c>antiphon/review</c>. A host
    /// with no reviews (local git) keeps the findings in Antiphon.
    /// </summary>
    public static async Task<NodeResult> PublishAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var collated = ReviewWalk.Output<CollateOutput>(walk.LatestOf("CollateFindings"));
        var verify = walk.LatestOf("VerifyFindings");
        var keptIds = ReviewWalk.Output<VerifyOutput>(verify)?.Kept ?? collated?.Candidates.Select(c => c.Cid).ToList() ?? [];
        var candidates = (collated?.Candidates ?? []).Where(c => keptIds.Contains(c.Cid)).ToList();
        var policy = walk.Policy;
        var run = walk.Run;
        var bar = ReviewRoundsPolicy.BarFor(policy, run.Round);
        var checks = (await walk.ChecksAsync(ct)).Checks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var findings = await walk.Db.ReviewFindings.Where(f => f.RunId == run.Id).ToListAsync(ct);
        var next = findings.Select(f => int.TryParse(f.Key.AsSpan(1), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

        var created = new List<(ReviewFinding Finding, FindingCandidate Candidate)>();
        foreach (var candidate in candidates)
        {
            var print = Fingerprint(candidate.File, candidate.Anchor);
            if (findings.FirstOrDefault(f => f.RoundRaised == run.Round && f.Status != ReviewFindingStatus.Outdated
                    && Fingerprint(f.FilePath, f.Anchor) == print && string.Equals(f.CheckId ?? "none", candidate.Check, StringComparison.OrdinalIgnoreCase))
                is { } already)
            {
                created.Add((already, candidate));
                continue;
            }

            var check = checks.GetValueOrDefault(candidate.Check);
            var flags = candidate.Flags.Union(check?.Flags ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var finding = new ReviewFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Key = $"F{next++}",
                CheckId = candidate.Check.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : candidate.Check,
                CheckWhy = check?.HumansWhy,
                Severity = candidate.Severity,
                Flags = flags,
                BypassesRoundBar = ReviewRoundsPolicy.IsBypass(flags, policy),
                Confidence = candidate.Confidence,
                FilePath = candidate.File,
                LineStart = candidate.Line,
                LineEnd = candidate.LineEnd,
                Anchor = candidate.Anchor,
                Title = Truncate(candidate.Title, 1000),
                Body = candidate.Body,
                Suggestion = candidate.Suggestion,
                Hunk = candidate.Hunk,
                Status = ReviewFindingStatus.Open,
                RoundRaised = run.Round,
                SyncState = ReviewSyncState.NotApplicable,
                CreatedAt = walk.Now(),
                UpdatedAt = walk.Now(),
            };
            walk.Db.ReviewFindings.Add(finding);
            findings.Add(finding);
            created.Add((finding, candidate));
        }

        var nits = 0;
        var inline = new List<(ReviewFinding Finding, FindingCandidate Candidate)>();
        var summaryOnly = new List<ReviewFinding>();
        var belowBar = new List<ReviewFinding>();
        foreach (var (finding, candidate) in created.Where(c => !c.Finding.Posted))
        {
            switch (ReviewRoundsPolicy.Place(finding.Severity, finding.Confidence, finding.BypassesRoundBar, candidate.InDiff, bar, ref nits))
            {
                case ReviewPlacement.Inline:
                    inline.Add((finding, candidate));
                    break;
                case ReviewPlacement.SummaryOnly:
                    summaryOnly.Add(finding);
                    break;
                default:
                    belowBar.Add(finding);
                    break;
            }
        }

        string? reviewId = null;
        string? reviewUrl = null;
        var canPost = walk.Host.Capabilities.CanPostReviews && run.Subject.PrNumber is not null;
        if (canPost && (inline.Count > 0 || summaryOnly.Count > 0))
        {
            var body = SummaryBody(walk, inline.Select(i => i.Finding).ToList(), summaryOnly, belowBar.Count,
                ReviewWalk.Output<VerifyOutput>(verify), verify?.Status is ReviewNodeStatus.Skipped or ReviewNodeStatus.FailedContinued);
            var comments = inline.Select(i => new CodeHostReviewComment(i.Finding.FilePath, i.Candidate.Line, CommentBody(i.Finding))).ToList();
            var posted = await walk.Host.PostReviewAsync(walk.Binding.Target, new CodeHostReview(walk.Iteration.HeadSha ?? walk.HeadSha, body, comments), ct);
            reviewId = posted.ReviewId;
            reviewUrl = posted.Url;
            var remaining = posted.Comments.ToList();
            foreach (var (finding, candidate) in inline)
            {
                var comment = remaining.FirstOrDefault(c => c.Path == finding.FilePath && c.Line == candidate.Line);
                if (comment is not null)
                {
                    remaining.Remove(comment);
                    finding.ExternalCommentId = comment.CommentId;
                    finding.ExternalThreadId = comment.ThreadId;
                    finding.ExternalThreadUrl = comment.Url;
                }

                finding.Posted = true;
                finding.UpdatedAt = walk.Now();
            }

            foreach (var finding in summaryOnly)
            {
                finding.Posted = true;
                finding.ExternalThreadUrl ??= posted.Url;
                finding.UpdatedAt = walk.Now();
            }
        }

        var blocking = findings.Where(f => ReviewRoundsPolicy.IsBlocking(f, run.Round, policy)).ToList();
        var description = blocking.Count == 0
            ? "No findings above the bar"
            : $"{blocking.Count} findings ({string.Join(", ", blocking.GroupBy(f => f.Severity).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"))})";
        await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, blocking.Count == 0 ? CodeHostStatusState.Success : CodeHostStatusState.Failure,
            description, ct);

        var where = canPost ? "posted" : "kept in Antiphon (the code host takes no reviews)";
        var summary = $"{created.Count} findings: {inline.Count} inline, {summaryOnly.Count} in the summary, {belowBar.Count} below round {run.Round}'s bar; {where}";
        return NodeResult.Ok(null, summary, new PublishOutput(inline.Select(i => i.Finding.Key).ToList(), summaryOnly.Select(f => f.Key).ToList(),
            belowBar.Select(f => f.Key).ToList(), reviewId, reviewUrl));
    }

    public static string HoldsName(string cid) => $"holds_{cid.ToLowerInvariant()}";

    /// <summary>Two findings are the same when they anchor the same trimmed line of the same file.</summary>
    public static string Fingerprint(string file, string anchor) => $"{file}|{anchor.Trim()}";

    private static void Complete(ReviewWalk walk, ReviewNodeExecution child, ReviewNodeStatus status, string summary, ReviewerOutput? output)
    {
        child.Status = status;
        child.Summary = summary;
        child.CompletedAt = walk.Now();
        if (output is not null)
            child.OutputJson = ReviewWalk.ToJson(output);
    }

    private static void Usage(ReviewNodeExecution child, ReviewAgentUsage? usage)
    {
        if (usage is null)
            return;
        child.Provider = usage.Provider;
        child.Model = usage.Model;
        child.Transport = usage.Transport;
        child.InputTokens += usage.InputTokens;
        child.OutputTokens += usage.OutputTokens;
        child.CostUsd += usage.CostUsd;
        child.CostBasis = usage.CostBasis;
    }

    private static string SummaryBody(ReviewWalk walk, IReadOnlyList<ReviewFinding> inline, IReadOnlyList<ReviewFinding> summaryOnly, int belowBar,
        VerifyOutput? verify, bool verifySkipped)
    {
        var text = new StringBuilder()
            .Append("**Antiphon review**, round ").Append(walk.Run.Round).Append(" (iteration ").Append(walk.Iteration.Iteration).AppendLine(")")
            .AppendLine();
        if (inline.Count > 0)
        {
            text.AppendLine("Findings:");
            foreach (var finding in inline)
                text.Append("- **").Append(finding.Key).Append("** ").Append(finding.Severity.ToString().ToLowerInvariant()).Append(" · `")
                    .Append(finding.FilePath).Append("`: ").AppendLine(finding.Title);
        }

        if (summaryOnly.Count > 0)
        {
            text.AppendLine().AppendLine("Listed here only:");
            foreach (var finding in summaryOnly)
                text.Append("- ").Append(finding.Key).Append(' ').Append(finding.Severity.ToString().ToLowerInvariant()).Append(" · `")
                    .Append(finding.FilePath).Append(':').Append(finding.LineStart).Append("`: ").AppendLine(finding.Title);
        }

        if (belowBar > 0)
            text.AppendLine().Append(belowBar).AppendLine(" more below this round's bar are kept in Antiphon.");
        if (verifySkipped)
            text.AppendLine().AppendLine("_These findings were not verified: no verifier is configured._");
        else if (verify is { Dropped.Count: > 0 })
            text.AppendLine().Append(verify.Dropped.Count).AppendLine(" findings were disproved by the verifier and dropped.");
        return text.ToString();
    }

    private static string CommentBody(ReviewFinding finding)
    {
        var text = new StringBuilder()
            .Append("**").Append(finding.Key).Append(" · ").Append(finding.Severity.ToString().ToLowerInvariant()).Append("** ")
            .AppendLine(finding.Title)
            .AppendLine()
            .AppendLine(finding.Body.Trim());
        if (!string.IsNullOrWhiteSpace(finding.Suggestion))
            text.AppendLine().AppendLine("```suggestion").AppendLine(finding.Suggestion.TrimEnd()).AppendLine("```");
        if (finding.CheckId is { } check)
            text.AppendLine().Append("_Check ").Append(check).AppendLine("_");
        return text.ToString();
    }

    private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>The log category for the executor's node steps.</summary>
public sealed class ReviewWalkLog;
