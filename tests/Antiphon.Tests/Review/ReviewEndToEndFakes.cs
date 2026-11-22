using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.Review;

/// <summary>
/// An in-memory GitHub for the review engine's end-to-end tests: PRs whose head and base the test
/// moves, and a record of every review, status, approval, reply, resolution and branch update.
/// </summary>
public sealed class ScriptedCodeHost : ICodeHost
{
    private readonly ConcurrentDictionary<int, ScriptedPr> _prs = new();

    public bool Serves(ReviewHost kind) => kind == ReviewHost.GitHub;

    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: true, ApprovalCountsForMerge: true, SupportsAutoMerge: true,
        CanPostReviews: true, CanReplyToThreads: true, SupportsCommitStatuses: true, CanApprove: true, CanUpdateBranch: true,
        CanReadFiles: false);

    public ScriptedPr Open(int number, string title, string author, string baseSha, string headSha, IReadOnlyList<CodeHostChangedFile> files, string diff) =>
        _prs[number] = new ScriptedPr(number, title, author, baseSha, headSha, files, diff);

    public ScriptedPr Pr(int number) => _prs[number];

    private ScriptedPr Of(CodeHostTarget target) =>
        target.PrNumber is { } n && _prs.TryGetValue(n, out var pr) ? pr : throw new CodeHostException(CodeHostFailureKind.NotFound, $"No PR {target.PrNumber}.");

    public Task<CodeHostPullRequest> GetPullRequestAsync(CodeHostTarget target, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
        {
            return Task.FromResult(new CodeHostPullRequest(pr.Number, pr.Title, "Keeps the lease until the save completes.", pr.Author, null,
                "main", pr.BaseSha, $"fix/pr-{pr.Number}", pr.HeadSha, $"https://github.example/acme/widgets/pull/{pr.Number}", pr.State, false));
        }
    }

    public Task<IReadOnlyList<CodeHostChangedFile>> ListChangedFilesAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            return Task.FromResult(range.SinceSha is { } since && pr.Increments.TryGetValue(since, out var step) ? step.Files : pr.Files);
    }

    public Task<string> GetDiffAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            return Task.FromResult(range.SinceSha is { } since && pr.Increments.TryGetValue(since, out var step) ? step.Diff : pr.Diff);
    }

    public Task<CodeHostComparison> CompareAsync(CodeHostTarget target, string fromRef, string toRef, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
        {
            IReadOnlyList<CodeHostChangedFile> files = toRef == pr.BaseTip
                ? pr.BaseMoveFiles.Select(p => new CodeHostChangedFile(p, CodeHostFileStatus.Modified, 3, 1)).ToList()
                : [];
            return Task.FromResult(new CodeHostComparison(fromRef, toRef, pr.MergeBase, files.Count > 0 ? 1 : 0, 0, files));
        }
    }

    public Task<CodeHostBranchState> GetBranchStateAsync(CodeHostTarget target, string baseRef, string headSha, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            return Task.FromResult(new CodeHostBranchState(pr.BaseTip, pr.MergeBase, pr.BaseTip == pr.MergeBase ? 0 : 1, 1));
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(CodeHostTarget target, string sha, string directory, CancellationToken ct) =>
        throw new CodeHostException(CodeHostFailureKind.Unsupported, "The scripted host reads no files.");

    public Task<string?> ReadFileAsync(CodeHostTarget target, string sha, string path, CancellationToken ct) =>
        throw new CodeHostException(CodeHostFailureKind.Unsupported, "The scripted host reads no files.");

    public Task<CodeHostPostedReview> PostReviewAsync(CodeHostTarget target, CodeHostReview review, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
        {
            pr.Reviews.Add(review);
            var posted = review.Comments.Select(c =>
            {
                var n = Interlocked.Increment(ref pr.CommentCounter);
                return new CodeHostPostedComment(c.Path, c.Line, $"c{n}", $"t{n}", $"https://github.example/acme/widgets/pull/{pr.Number}#discussion_r{n}");
            }).ToList();
            return Task.FromResult(new CodeHostPostedReview($"r{pr.Reviews.Count}", $"https://github.example/acme/widgets/pull/{pr.Number}#review-{pr.Reviews.Count}", posted));
        }
    }

    public Task<CodeHostThreadReply> ReplyToThreadAsync(CodeHostTarget target, string commentId, string body, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
        {
            pr.Replies.Add((commentId, body));
            return Task.FromResult(new CodeHostThreadReply($"reply{pr.Replies.Count}", null));
        }
    }

    public Task ResolveThreadAsync(CodeHostTarget target, string threadId, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            pr.Resolved.Add(threadId);
        return Task.CompletedTask;
    }

    public Task SetCommitStatusAsync(CodeHostTarget target, string sha, CodeHostCommitStatus status, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            pr.Statuses.Add((sha, status));
        return Task.CompletedTask;
    }

    public Task ApproveAsync(CodeHostTarget target, string headSha, string body, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
            pr.Approvals.Add(headSha);
        return Task.CompletedTask;
    }

    /// <summary>Merges the base in at once: a new head whose merge base is the base tip.</summary>
    public Task<CodeHostBranchUpdate> UpdateBranchAsync(CodeHostTarget target, string expectedHeadSha, CancellationToken ct)
    {
        var pr = Of(target);
        lock (pr)
        {
            pr.BranchUpdates.Add(expectedHeadSha);
            pr.HeadSha = pr.UpdatedHead;
            pr.MergeBase = pr.BaseTip;
            return Task.FromResult(new CodeHostBranchUpdate(true, pr.HeadSha, "Updating pull request branch."));
        }
    }
}

/// <summary>One scripted PR. The test moves its head and base and reads what the engine wrote.</summary>
public sealed class ScriptedPr(int number, string title, string author, string baseSha, string headSha, IReadOnlyList<CodeHostChangedFile> files, string diff)
{
    public int CommentCounter;

    public int Number { get; } = number;
    public string Title { get; } = title;
    public string Author { get; } = author;
    public string BaseSha { get; } = baseSha;
    public string BaseTip { get; set; } = baseSha;
    public string MergeBase { get; set; } = baseSha;
    public string HeadSha { get; set; } = headSha;
    public string UpdatedHead { get; set; } = new('e', 40);
    public CodeHostPullRequestState State { get; set; } = CodeHostPullRequestState.Open;
    public IReadOnlyList<CodeHostChangedFile> Files { get; } = files;
    public string Diff { get; } = diff;
    public Dictionary<string, (IReadOnlyList<CodeHostChangedFile> Files, string Diff)> Increments { get; } = new(StringComparer.Ordinal);
    public List<string> BaseMoveFiles { get; set; } = [];

    public List<CodeHostReview> Reviews { get; } = [];
    public List<(string Sha, CodeHostCommitStatus Status)> Statuses { get; } = [];
    public List<string> Approvals { get; } = [];
    public List<(string CommentId, string Body)> Replies { get; } = [];
    public List<string> Resolved { get; } = [];
    public List<string> BranchUpdates { get; } = [];

    public void Push(string newHead, IReadOnlyList<CodeHostChangedFile> changed, string changedDiff)
    {
        lock (this)
        {
            Increments[HeadSha] = (changed, changedDiff);
            HeadSha = newHead;
        }
    }

    public void MoveBase(string tip, params string[] touched)
    {
        lock (this)
        {
            BaseTip = tip;
            BaseMoveFiles = touched.ToList();
        }
    }

    public void Merge()
    {
        lock (this)
            State = CodeHostPullRequestState.Merged;
    }

    public T Read<T>(Func<ScriptedPr, T> read)
    {
        lock (this)
            return read(this);
    }

    /// <summary>The last status of <paramref name="context"/> on <paramref name="sha"/>.</summary>
    public CodeHostCommitStatus? Status(string context, string sha) =>
        Read(p => p.Statuses.LastOrDefault(s => s.Sha == sha && s.Status.Context == context).Status);
}

/// <summary>A decision model with scripted answers per question name; an unscripted question is refused (no answer).</summary>
public sealed class ScriptedDecisions : IDecisionModel
{
    public const string Model = "jev-scripted-1";

    private readonly ConcurrentDictionary<string, DecisionAnswer> _answers = new(StringComparer.Ordinal);

    public ConcurrentQueue<DecisionRequest> Requests { get; } = new();

    public bool Serves(string dialect) => true;

    public void Reset()
    {
        _answers.Clear();
        Requests.Clear();
    }

    public ScriptedDecisions Yes(string name, double probability)
    {
        _answers[name] = new DecisionAnswer(name, DecisionQuestionKind.Noul, probability >= 0.5 ? "yes" : "no", Math.Max(probability, 1 - probability),
            new Dictionary<string, double> { ["yes"] = probability, ["no"] = 1 - probability }, false);
        return this;
    }

    public ScriptedDecisions Pick(string name, string value, DecisionQuestionKind kind = DecisionQuestionKind.Score)
    {
        _answers[name] = new DecisionAnswer(name, kind, value, 0.9, null, false);
        return this;
    }

    /// <summary>The categorise answers (design 4.3).</summary>
    public ScriptedDecisions Categorise(double externalContract, double persistentData = 0.1, string reach = "A few", string reversal = "Easy")
    {
        Pick("change_kind", "bugfix", DecisionQuestionKind.Choice);
        Yes("external_contract", externalContract);
        Yes("persistent_data", persistentData);
        Pick("reach", reach);
        Pick("rollback_at_release", "Easy");
        Pick("reversal_after_months", reversal);
        Pick("exposure", "Low");
        return this;
    }

    public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var answers = request.Questions.Where(q => _answers.ContainsKey(q.Name)).ToDictionary(q => q.Name, q => _answers[q.Name]);
        return Task.FromResult(new DecisionResult(answers, new DecisionUsage(100 * request.Questions.Count, 0), 0.0001m * request.Questions.Count,
            "actual", new DecisionServedBy(options.ProviderId, "Http", Model)));
    }
}

/// <summary>A finding a scripted reviewer reports.</summary>
public sealed record ScriptedFinding(string Check, string Severity, double Confidence, string File, string Anchor, string Title, string Body);

/// <summary>
/// The reviewer agents: a scripted review-findings/1 document per PR, round and group (a group with no
/// script passes every assigned check). Every request is recorded.
/// </summary>
public sealed class ScriptedReviewer : IReviewAgentRunner
{
    private readonly ConcurrentDictionary<(int Pr, int Round, string Group), ScriptedFinding[]> _scripts = new();

    public ConcurrentQueue<ReviewAgentRequest> Requests { get; } = new();

    public void Reset()
    {
        _scripts.Clear();
        Requests.Clear();
    }

    public void Script(int pr, int round, string group, params ScriptedFinding[] findings) => _scripts[(pr, round, group)] = findings;

    public Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var findings = _scripts.GetValueOrDefault((request.PrNumber ?? 0, request.Round, request.GroupKey)) ?? [];
        return Task.FromResult(ReviewAgentResult.Completed(Document(request.AssignedChecks, findings),
            new ReviewAgentUsage("scripted-reviewer", "claude-scripted", ReviewTransport.Seat, 2000, 300, 0.02m, "api-equivalent")));
    }

    public static string Document(IReadOnlyList<string> assigned, IReadOnlyList<ScriptedFinding> findings)
    {
        var text = new StringBuilder("@abf 1 review-findings/1.0\n");
        for (var i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            text.Append("=== finding F").Append(i + 1).Append('\n')
                .Append("check: ").Append(f.Check).Append('\n')
                .Append("severity: ").Append(f.Severity).Append('\n')
                .Append("confidence: ").Append(f.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                .Append("file: ").Append(f.File).Append('\n')
                .Append("anchor: ").Append(f.Anchor).Append('\n')
                .Append("title: ").Append(f.Title).Append('\n')
                .Append("body: ").Append(f.Body).Append('\n');
        }

        text.Append("=== checks\n");
        foreach (var check in assigned)
        {
            var ids = findings.Select((f, i) => (f, i)).Where(x => x.f.Check == check).Select(x => $"F{x.i + 1}").ToList();
            text.Append(check).Append(ids.Count > 0 ? $" finding {string.Join(",", ids)}" : " pass").Append('\n');
        }

        return text.Append("=== end\n").ToString();
    }
}

/// <summary>The verifier: holds every finding (p 0.9), answering the request it was sent.</summary>
public sealed partial class ScriptedCompletions : ICompletionModel
{
    public const string Model = "verify-scripted-1";

    public ConcurrentQueue<CompletionRequest> Requests { get; } = new();

    public bool Serves(string dialect) => dialect == "scripted";

    [GeneratedRegex("request (?<id>verify-[0-9a-f]+-\\d+)")]
    private static partial Regex RequestId();

    [GeneratedRegex("holds_c\\d+")]
    private static partial Regex Holds();

    public Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var text = new StringBuilder("@abf 1 decision-response/1.0\n=== response ").Append(RequestId().Match(request.Instructions).Groups["id"].Value).Append('\n');
        foreach (var name in Holds().Matches(request.Instructions).Select(m => m.Value).Distinct())
            text.Append("=== answer ").Append(name).Append("\nvalue: 0.9\nconfidence: 0.8\n");
        text.Append("=== end\n");
        return Task.FromResult(new CompletionResult(text.ToString(), new DecisionUsage(500, 50), 0.001m, "actual",
            new DecisionServedBy(options.ProviderId, "Http", Model)));
    }
}
