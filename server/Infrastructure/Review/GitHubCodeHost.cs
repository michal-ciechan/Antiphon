using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// GitHub over REST with a personal access token or a GitHub App sign-in (review engine design 8.3).
/// The host comes from <c>Review:CodeHosts:Hosts</c> by the target's host id: its base URL (GitHub
/// Enterprise Server or a proxy) and its token, or its app's installation token
/// (<see cref="GitHubAppTokens"/>), sent as a bearer token and never logged. Every GET is conditional through
/// <see cref="CodeHostResponseCache"/>, so an unchanged resource costs a 304, which GitHub does not
/// count against the rate limit.
/// </summary>
public sealed class GitHubCodeHost(
    HttpClient http,
    IOptions<ReviewCodeHostSettings> settings,
    CodeHostResponseCache cache,
    GitHubAppTokens? appTokens = null) : ICodeHost
{
    private readonly GitHubAppTokens _appTokens = appTokens ?? new GitHubAppTokens();

    public const string DefaultApiRoot = "https://api.github.com";
    public const string JsonMediaType = "application/vnd.github+json";
    public const string DiffMediaType = "application/vnd.github.diff";
    public const string ApiVersion = "2022-11-28";

    private const int PageSize = 100;

    /// <summary>GitHub lists at most 3000 files for a PR.</summary>
    private const int MaxPages = 30;

    public bool Serves(ReviewHost kind) => kind == ReviewHost.GitHub;

    public const string RawMediaType = "application/vnd.github.raw";

    /// <summary>
    /// A machine user's review counts for branch protection; check runs need a GitHub App (design 8.2).
    /// Threads are resolved over GraphQL (<c>resolveReviewThread</c>): the thread node id is in no REST
    /// response, so a posted review reads its threads once over GraphQL (<see cref="PostReviewAsync"/>).
    /// </summary>
    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: true, ApprovalCountsForMerge: true, SupportsAutoMerge: true,
        CanPostReviews: true, CanReplyToThreads: true, SupportsCommitStatuses: true, CanApprove: true, CanUpdateBranch: true,
        CanReadFiles: true, CanListPullRequests: true, CanPostConversation: true, CanMerge: true);

    /// <summary>A polling listing reads at most this many pages (1,000 open PRs or comments).</summary>
    public const int MaxListingPages = 10;

    /// <summary>The PR's review threads, a page at a time: each thread's node id and its first comment's database id.</summary>
    public const string ReviewThreadsQuery = """
        query($owner: String!, $name: String!, $number: Int!, $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              reviewThreads(first: 100, after: $after) {
                pageInfo { hasNextPage endCursor }
                nodes { id isResolved comments(first: 1) { nodes { databaseId } } }
              }
            }
          }
        }
        """;

    public const string ResolveThreadMutation = """
        mutation($threadId: ID!) {
          resolveReviewThread(input: { threadId: $threadId }) { thread { id isResolved } }
        }
        """;

    /// <summary>GitHub lists at most this many threads per page; a PR is read up to <see cref="MaxThreadPages"/> pages.</summary>
    private const int MaxThreadPages = 10;

    public async Task<CodeHostPullRequest> GetPullRequestAsync(CodeHostTarget target, CancellationToken ct)
    {
        var body = await GetAsync(target, $"{RepoPath(target)}/pulls/{RequirePr(target)}", JsonMediaType, ct);
        using var doc = Parse(body, "pull request");
        return ParsePullRequest(doc.RootElement);
    }

    /// <summary>
    /// The whole PR reads <c>pulls/{n}/files</c> page by page (it reflects the PR's current head); a
    /// recheck, or a target with no PR, reads <c>compare/{from}...{head}</c>, which lists at most 300 files.
    /// </summary>
    public async Task<IReadOnlyList<CodeHostChangedFile>> ListChangedFilesAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct)
    {
        if (!range.IsIncremental && target.PrNumber is { } number)
        {
            var files = new List<CodeHostChangedFile>();
            for (var page = 1; page <= MaxPages; page++)
            {
                var body = await GetAsync(target, $"{RepoPath(target)}/pulls/{number}/files?per_page={PageSize}&page={page}", JsonMediaType, ct);
                using var doc = Parse(body, "file list");
                var count = 0;
                foreach (var file in doc.RootElement.EnumerateArray())
                {
                    files.Add(ParseFile(file));
                    count++;
                }

                if (count < PageSize)
                    break;
            }

            return files;
        }

        var compare = await GetAsync(target, ComparePath(target, range), JsonMediaType, ct);
        using var compared = Parse(compare, "comparison");
        return compared.RootElement.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ParseFile).ToList()
            : [];
    }

    public Task<string> GetDiffAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct) =>
        !range.IsIncremental && target.PrNumber is { } number
            ? GetAsync(target, $"{RepoPath(target)}/pulls/{number}", DiffMediaType, ct)
            : GetAsync(target, ComparePath(target, range), DiffMediaType, ct);

    /// <summary><c>GET compare/{from}...{to}</c>: <c>merge_base_commit</c>, <c>ahead_by</c>, <c>behind_by</c> and up to 300 files.</summary>
    public async Task<CodeHostComparison> CompareAsync(CodeHostTarget target, string fromRef, string toRef, CancellationToken ct)
    {
        var body = await GetAsync(target, $"{RepoPath(target)}/compare/{EscapeRef(fromRef)}...{EscapeRef(toRef)}", JsonMediaType, ct);
        using var doc = Parse(body, "comparison");
        var root = doc.RootElement;
        var files = root.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ParseFile).ToList()
            : [];
        return new CodeHostComparison(
            CommitSha(root, "base_commit") ?? fromRef,
            root.TryGetProperty("commits", out var commits) && commits.ValueKind == JsonValueKind.Array && commits.GetArrayLength() > 0
                ? Text(commits[commits.GetArrayLength() - 1], "sha") ?? toRef
                : toRef,
            CommitSha(root, "merge_base_commit") ?? "",
            Int(root, "ahead_by"),
            Int(root, "behind_by"),
            files);
    }

    /// <summary>
    /// <c>GET compare/{baseRef}...{head}</c>: <c>base_commit</c> is the base tip, <c>merge_base_commit</c>
    /// the merge base and <c>behind_by</c> the base commits the head lacks (D-37 option B).
    /// </summary>
    public async Task<CodeHostBranchState> GetBranchStateAsync(CodeHostTarget target, string baseRef, string headSha, CancellationToken ct)
    {
        var body = await GetAsync(target, $"{RepoPath(target)}/compare/{EscapeRef(baseRef)}...{EscapeRef(headSha)}", JsonMediaType, ct);
        using var doc = Parse(body, "comparison");
        var root = doc.RootElement;
        var tip = CommitSha(root, "base_commit")
            ?? throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub compared {baseRef}...{headSha} with no base commit.");
        return new CodeHostBranchState(tip, CommitSha(root, "merge_base_commit") ?? "", Int(root, "behind_by"), Int(root, "ahead_by"));
    }

    /// <summary><c>GET git/trees/{sha}?recursive=1</c>, filtered to blobs under the directory.</summary>
    public async Task<IReadOnlyList<string>> ListFilesAsync(CodeHostTarget target, string sha, string directory, CancellationToken ct)
    {
        var body = await GetAsync(target, $"{RepoPath(target)}/git/trees/{Uri.EscapeDataString(sha)}?recursive=1", JsonMediaType, ct);
        using var doc = Parse(body, "tree");
        var prefix = directory.Trim('/') + "/";
        return doc.RootElement.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array
            ? tree.EnumerateArray()
                .Where(e => Text(e, "type") == "blob" && Text(e, "path") is { } p && p.StartsWith(prefix, StringComparison.Ordinal))
                .Select(e => Text(e, "path")!)
                .ToList()
            : [];
    }

    /// <summary><c>GET contents/{path}?ref={sha}</c> as raw text; a 404 is null.</summary>
    public async Task<string?> ReadFileAsync(CodeHostTarget target, string sha, string path, CancellationToken ct)
    {
        try
        {
            return await GetAsync(target, $"{RepoPath(target)}/contents/{EscapeRef(path)}?ref={Uri.EscapeDataString(sha)}", RawMediaType, ct);
        }
        catch (CodeHostException ex) when (ex.Kind == CodeHostFailureKind.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>POST pulls/{n}/reviews</c> with event <c>COMMENT</c>, then <c>GET pulls/{n}/reviews/{id}/comments</c>
    /// for the comment ids (the create response does not carry them), matched by path and line in order.
    /// </summary>
    public async Task<CodeHostPostedReview> PostReviewAsync(CodeHostTarget target, CodeHostReview review, CancellationToken ct)
    {
        var number = RequirePr(target);
        var payload = new
        {
            commit_id = review.CommitSha,
            body = review.Body,
            @event = "COMMENT",
            comments = review.Comments.Select(c => new { path = c.Path, line = c.Line, side = "RIGHT", body = c.Body }).ToArray(),
        };
        var created = await SendAsync(target, HttpMethod.Post, $"{RepoPath(target)}/pulls/{number}/reviews", payload, ct);
        using var doc = Parse(created, "review");
        var reviewId = doc.RootElement.TryGetProperty("id", out var id) ? id.ToString() : "";
        var url = Text(doc.RootElement, "html_url");
        if (review.Comments.Count == 0 || reviewId.Length == 0)
            return new CodeHostPostedReview(reviewId, url, []);

        var listed = await GetAsync(target, $"{RepoPath(target)}/pulls/{number}/reviews/{reviewId}/comments?per_page={PageSize}", JsonMediaType, ct);
        using var comments = Parse(listed, "review comments");
        var remaining = comments.RootElement.ValueKind == JsonValueKind.Array ? comments.RootElement.EnumerateArray().ToList() : [];
        var posted = new List<CodeHostPostedComment>();
        foreach (var wanted in review.Comments)
        {
            var match = remaining.FirstOrDefault(c => Text(c, "path") == wanted.Path
                && (Int(c, "line") == wanted.Line || Int(c, "original_line") == wanted.Line));
            // GitHub's per-review listing sends position but line null, and stores the body with CRLF:
            // match the body that was posted instead.
            if (match.ValueKind != JsonValueKind.Object)
                match = remaining.FirstOrDefault(c => Text(c, "path") == wanted.Path && SameBody(Text(c, "body"), wanted.Body));
            if (match.ValueKind != JsonValueKind.Object)
                continue;
            remaining.Remove(match);
            posted.Add(new CodeHostPostedComment(wanted.Path, wanted.Line, match.GetProperty("id").ToString(), null, Text(match, "html_url")));
        }

        if (posted.Count == 0)
            return new CodeHostPostedReview(reviewId, url, posted);

        // The thread ids, so a later resolution is one mutation. A failure here leaves them null: the
        // review is posted, and resolving looks the thread up by its comment instead.
        try
        {
            var threads = await ThreadIdsByCommentAsync(target, ct);
            posted = posted.Select(c => threads.TryGetValue(c.CommentId, out var thread) ? c with { ThreadId = thread } : c).ToList();
        }
        catch (CodeHostException)
        {
        }

        return new CodeHostPostedReview(reviewId, url, posted);
    }

    private static bool SameBody(string? posted, string wanted) =>
        posted is not null && string.Equals(posted.ReplaceLineEndings("\n").Trim(), wanted.ReplaceLineEndings("\n").Trim(), StringComparison.Ordinal);

    /// <summary>
    /// GraphQL <c>resolveReviewThread</c>. <paramref name="threadId"/> is the thread's node id, or the
    /// database id of the REST comment that started it (all digits), which is mapped to the thread through
    /// <c>repository.pullRequest.reviewThreads</c> first. An already-resolved thread is left alone.
    /// </summary>
    public async Task ResolveThreadAsync(CodeHostTarget target, string threadId, CancellationToken ct)
    {
        var id = threadId.Trim();
        if (id.Length == 0)
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, "No thread id to resolve.");
        if (id.All(char.IsAsciiDigit))
        {
            var threads = await ThreadIdsByCommentAsync(target, ct);
            id = threads.TryGetValue(id, out var found)
                ? found
                : throw new CodeHostException(CodeHostFailureKind.NotFound, $"No review thread on {target.Repo}#{target.PrNumber} starts with comment {threadId}.");
        }

        using var doc = await GraphQlAsync(target, ResolveThreadMutation, new Dictionary<string, object?> { ["threadId"] = id }, ct);
        var resolved = doc.RootElement.GetProperty("data").TryGetProperty("resolveReviewThread", out var payload)
            && payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("thread", out var thread) && thread.ValueKind == JsonValueKind.Object
            && thread.TryGetProperty("isResolved", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (!resolved)
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub did not report thread {id} as resolved.");
    }

    /// <summary>The PR's review threads by the database id of their first comment (paged GraphQL <c>reviewThreads</c>).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ThreadIdsByCommentAsync(CodeHostTarget target, CancellationToken ct)
    {
        var parts = RepoPath(target).Split('/');
        var number = RequirePr(target);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string? after = null;
        for (var page = 0; page < MaxThreadPages; page++)
        {
            using var doc = await GraphQlAsync(target, ReviewThreadsQuery, new Dictionary<string, object?>
            {
                ["owner"] = Uri.UnescapeDataString(parts[1]),
                ["name"] = Uri.UnescapeDataString(parts[2]),
                ["number"] = number,
                ["after"] = after,
            }, ct);
            var data = doc.RootElement.GetProperty("data");
            if (!data.TryGetProperty("repository", out var repo) || repo.ValueKind != JsonValueKind.Object
                || !repo.TryGetProperty("pullRequest", out var pr) || pr.ValueKind != JsonValueKind.Object)
                throw new CodeHostException(CodeHostFailureKind.NotFound, $"GitHub GraphQL found no pull request {target.Repo}#{number}.");
            var threads = pr.GetProperty("reviewThreads");
            foreach (var node in threads.GetProperty("nodes").EnumerateArray())
            {
                if (Text(node, "id") is not { } nodeId || !node.TryGetProperty("comments", out var first)
                    || !first.TryGetProperty("nodes", out var firstNodes) || firstNodes.ValueKind != JsonValueKind.Array || firstNodes.GetArrayLength() == 0)
                    continue;
                if (firstNodes[0].TryGetProperty("databaseId", out var databaseId) && databaseId.ValueKind == JsonValueKind.Number)
                    map[databaseId.GetRawText()] = nodeId;
            }

            var info = threads.GetProperty("pageInfo");
            if (!info.TryGetProperty("hasNextPage", out var more) || more.ValueKind != JsonValueKind.True)
                break;
            after = Text(info, "endCursor");
        }

        return map;
    }

    /// <summary>
    /// <c>{api root}/api/graphql</c> for GitHub Enterprise Server (<c>…/api/v3</c>), <c>{root}/graphql</c>
    /// otherwise (<c>https://api.github.com/graphql</c>), or the host's <c>GraphQlUrl</c>.
    /// </summary>
    public static Uri GraphQlEndpoint(ReviewCodeHost host)
    {
        if (!string.IsNullOrWhiteSpace(host.GraphQlUrl))
            return new Uri(host.GraphQlUrl.Trim(), UriKind.Absolute);
        var root = (string.IsNullOrWhiteSpace(host.BaseUrl) ? DefaultApiRoot : host.BaseUrl.Trim()).TrimEnd('/');
        return new Uri(root.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase) ? root[..^"v3".Length] + "graphql" : root + "/graphql", UriKind.Absolute);
    }

    /// <summary>
    /// One GraphQL POST. A non-2xx is mapped like REST; a 200 with <c>errors</c> is mapped by the first
    /// error's type (NOT_FOUND, FORBIDDEN, RATE_LIMITED; anything else is an invalid request).
    /// </summary>
    private async Task<JsonDocument> GraphQlAsync(CodeHostTarget target, string query, IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        var host = Host(target);
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint(host));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        await AuthorizeAsync(request, target, host, ct);
        request.Content = new StringContent(JsonSerializer.Serialize(new { query, variables }), System.Text.Encoding.UTF8, "application/json");

        var seconds = Math.Max(1, host.TimeoutSeconds);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        string body;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            body = await response.Content.ReadAsStringAsync(budget.Token);
            if (!response.IsSuccessStatusCode)
                throw Failure(response, body, "POST", "graphql");
        }
        catch (HttpRequestException ex)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub POST graphql: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub POST graphql: no response within {seconds}s.");
        }

        var doc = Parse(body, "GraphQL response");
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            var type = Text(first, "type");
            var message = Text(first, "message") ?? "GraphQL error";
            doc.Dispose();
            throw new CodeHostException(type switch
            {
                "NOT_FOUND" => CodeHostFailureKind.NotFound,
                "FORBIDDEN" or "INSUFFICIENT_SCOPES" => CodeHostFailureKind.Rejected,
                "RATE_LIMITED" => CodeHostFailureKind.RateLimited,
                _ => CodeHostFailureKind.InvalidRequest,
            }, $"GitHub GraphQL: {message}");
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new CodeHostException(CodeHostFailureKind.Unavailable, "GitHub GraphQL answered with no data.");
        }

        return doc;
    }

    /// <summary><c>POST pulls/{n}/comments/{id}/replies</c>.</summary>
    public async Task<CodeHostThreadReply> ReplyToThreadAsync(CodeHostTarget target, string commentId, string body, CancellationToken ct)
    {
        var created = await SendAsync(target, HttpMethod.Post,
            $"{RepoPath(target)}/pulls/{RequirePr(target)}/comments/{Uri.EscapeDataString(commentId)}/replies", new { body }, ct);
        using var doc = Parse(created, "reply");
        return new CodeHostThreadReply(doc.RootElement.TryGetProperty("id", out var id) ? id.ToString() : "", Text(doc.RootElement, "html_url"));
    }

    /// <summary><c>POST statuses/{sha}</c>; the description is cut to GitHub's 140 characters.</summary>
    public async Task SetCommitStatusAsync(CodeHostTarget target, string sha, CodeHostCommitStatus status, CancellationToken ct)
    {
        var description = status.Description.Length <= 140 ? status.Description : status.Description[..139] + "…";
        await SendAsync(target, HttpMethod.Post, $"{RepoPath(target)}/statuses/{Uri.EscapeDataString(sha)}", new
        {
            state = status.State.ToString().ToLowerInvariant(),
            target_url = status.TargetUrl,
            description,
            context = status.Context,
        }, ct);
    }

    /// <summary><c>POST pulls/{n}/reviews</c> with event <c>APPROVE</c> at the head.</summary>
    public async Task ApproveAsync(CodeHostTarget target, string headSha, string body, CancellationToken ct) =>
        await SendAsync(target, HttpMethod.Post, $"{RepoPath(target)}/pulls/{RequirePr(target)}/reviews",
            new { commit_id = headSha, body, @event = "APPROVE" }, ct);

    /// <summary>How long a rebase is waited for, and how often the head is re-read meanwhile.</summary>
    private static readonly TimeSpan RebaseSettleBudget = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan RebaseSettlePoll = TimeSpan.FromSeconds(2);

    private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public const string UpdateBranchMutation = """
        mutation($id: ID!, $head: GitObjectID!, $method: PullRequestBranchUpdateMethod!) {
          updatePullRequestBranch(input: { pullRequestId: $id, expectedHeadOid: $head, updateMethod: $method }) {
            pullRequest { headRefOid }
          }
        }
        """;

    /// <summary>
    /// Brings the PR branch up to date with its base. By default a rebase, over GraphQL
    /// (<c>updatePullRequestBranch</c>, <c>REBASE</c>): REST <c>update-branch</c> can only merge, which
    /// puts a merge commit on the author's branch. <see cref="ReviewCodeHost.UpdateMethod"/> <c>merge</c>
    /// keeps the REST merge (asynchronous, 202, so the new head is not known yet).
    /// </summary>
    public async Task<CodeHostBranchUpdate> UpdateBranchAsync(CodeHostTarget target, string expectedHeadSha, CancellationToken ct)
    {
        if (!string.Equals(Host(target).UpdateMethod, "merge", StringComparison.OrdinalIgnoreCase))
        {
            var pr = await GetAsync(target, $"{RepoPath(target)}/pulls/{RequirePr(target)}", JsonMediaType, ct);
            using var prDoc = Parse(pr, "pull request");
            var nodeId = Text(prDoc.RootElement, "node_id")
                ?? throw new CodeHostException(CodeHostFailureKind.Unavailable, "GitHub sent the pull request without a node_id.");
            using var updated = await GraphQlAsync(target, UpdateBranchMutation,
                new Dictionary<string, object?> { ["id"] = nodeId, ["head"] = expectedHeadSha, ["method"] = "REBASE" }, ct);
            var head = updated.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("updatePullRequestBranch", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("pullRequest", out var updatedPr)
                    ? Text(updatedPr, "headRefOid")
                    : null;
            // GitHub answers the mutation before the rebase lands (live, 2026-11-23: the old head came
            // back), so wait briefly for the new head. Reported now, the run accepts it as a base-only
            // update and keeps its approval; seen later as a push, it would start a new round.
            for (var waited = TimeSpan.Zero; Same(head, expectedHeadSha) && waited < RebaseSettleBudget; waited += RebaseSettlePoll)
            {
                await Task.Delay(RebaseSettlePoll, ct);
                using var reread = Parse(await GetAsync(target, $"{RepoPath(target)}/pulls/{RequirePr(target)}", JsonMediaType, ct), "pull request");
                head = reread.RootElement.TryGetProperty("head", out var headRef) ? Text(headRef, "sha") : head;
            }

            return new CodeHostBranchUpdate(true, Same(head, expectedHeadSha) ? null : head, "Rebased onto the base branch.");
        }

        var body = await SendAsync(target, HttpMethod.Put, $"{RepoPath(target)}/pulls/{RequirePr(target)}/update-branch",
            new { expected_head_sha = expectedHeadSha }, ct);
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = Text(doc.RootElement, "message");
        }
        catch (JsonException)
        {
        }

        return new CodeHostBranchUpdate(true, null, message);
    }

    /// <summary>How often a merge GitHub answers 405 (not mergeable yet) is tried, and the pause between tries.</summary>
    private const int MergeAttempts = 4;

    private static readonly TimeSpan MergeRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// <c>PUT pulls/{n}/merge</c> with <c>sha</c> = the approved head, so GitHub refuses (409) a head that
    /// moved since the approval. Just after a rebase GitHub may still be computing mergeability and
    /// answer 405; that is retried a few times. A 405 merged nothing, so the retry is safe for a write.
    /// </summary>
    public async Task<CodeHostMerge> MergeAsync(CodeHostTarget target, string expectedHeadSha, string method, CancellationToken ct)
    {
        var path = $"{RepoPath(target)}/pulls/{RequirePr(target)}/merge";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var doc = Parse(await SendAsync(target, HttpMethod.Put, path, new { sha = expectedHeadSha, merge_method = method }, ct), "merge");
                var merged = doc.RootElement.TryGetProperty("merged", out var flag) && flag.ValueKind == JsonValueKind.True;
                return new CodeHostMerge(merged, Text(doc.RootElement, "sha"), Text(doc.RootElement, "message"));
            }
            catch (CodeHostException ex) when (ex.StatusCode == 405 && attempt < MergeAttempts)
            {
                await Task.Delay(MergeRetryDelay, ct);
            }
        }
    }

    /// <summary>
    /// <c>GET pulls?state=open</c>, page by page. Each page is a conditional GET, so an unchanged
    /// listing costs a 304 that GitHub does not count against the rate limit.
    /// </summary>
    public async Task<IReadOnlyList<CodeHostOpenPullRequest>> ListOpenPullRequestsAsync(CodeHostTarget target, CancellationToken ct)
    {
        var prs = new List<CodeHostOpenPullRequest>();
        for (var page = 1; page <= MaxListingPages; page++)
        {
            var body = await GetAsync(target, $"{RepoPath(target)}/pulls?state=open&per_page={PageSize}&page={page}", JsonMediaType, ct);
            using var doc = ParseArray(body, "pull request list");
            var count = 0;
            foreach (var pr in doc.RootElement.EnumerateArray())
            {
                prs.Add(ParseOpenPullRequest(pr));
                count++;
            }

            if (count < PageSize)
                break;
        }

        return prs;
    }

    /// <summary>
    /// <c>GET issues/comments?since=</c>, page by page, keeping comments on PRs: a PR comment's
    /// <c>html_url</c> has <c>/pull/</c>, or the comment carries a <c>pull_request_url</c>. The URL only
    /// changes when <paramref name="since"/> does, so a caller that keeps its cursor still gets 304s.
    /// </summary>
    public async Task<IReadOnlyList<CodeHostConversationComment>> ListConversationCommentsAsync(CodeHostTarget target, DateTime since, CancellationToken ct)
    {
        var utc = since.Kind == DateTimeKind.Local ? since.ToUniversalTime() : DateTime.SpecifyKind(since, DateTimeKind.Utc);
        var stamp = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var comments = new List<CodeHostConversationComment>();
        for (var page = 1; page <= MaxListingPages; page++)
        {
            var body = await GetAsync(target, $"{RepoPath(target)}/issues/comments?since={stamp}&per_page={PageSize}&page={page}", JsonMediaType, ct);
            using var doc = ParseArray(body, "comment list");
            var count = 0;
            foreach (var comment in doc.RootElement.EnumerateArray())
            {
                count++;
                if (ParseConversationComment(comment) is { } parsed)
                    comments.Add(parsed);
            }

            if (count < PageSize)
                break;
        }

        return comments;
    }

    /// <summary><c>POST issues/{n}/comments</c>: a PR's conversation is its issue; the Pull requests permission is enough.</summary>
    public async Task<CodeHostThreadReply> PostConversationCommentAsync(CodeHostTarget target, string body, CancellationToken ct)
    {
        var created = await SendAsync(target, HttpMethod.Post, $"{RepoPath(target)}/issues/{RequirePr(target)}/comments", new { body }, ct);
        using var doc = Parse(created, "comment");
        return new CodeHostThreadReply(doc.RootElement.TryGetProperty("id", out var id) ? id.ToString() : "", Text(doc.RootElement, "html_url"));
    }

    /// <summary><c>POST issues/comments/{id}/reactions</c>.</summary>
    public async Task AddCommentReactionAsync(CodeHostTarget target, string commentId, string reaction, CancellationToken ct) =>
        await SendAsync(target, HttpMethod.Post, $"{RepoPath(target)}/issues/comments/{Uri.EscapeDataString(commentId)}/reactions",
            new { content = reaction }, ct);

    public static CodeHostOpenPullRequest ParseOpenPullRequest(JsonElement pr) => new(
        pr.GetProperty("number").GetInt32(),
        Text(pr, "title") ?? "",
        pr.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object ? Text(user, "login") ?? "" : "",
        pr.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
        pr.TryGetProperty("base", out var @base) && @base.ValueKind == JsonValueKind.Object ? Text(@base, "ref") ?? "" : "",
        pr.TryGetProperty("head", out var head) && head.ValueKind == JsonValueKind.Object ? Text(head, "sha") ?? "" : "",
        pr.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
            ? labels.EnumerateArray().Select(l => l.ValueKind == JsonValueKind.Object ? Text(l, "name") : null).OfType<string>().ToList()
            : [],
        Time(pr, "updated_at") ?? DateTime.MinValue);

    /// <summary>A comment on a PR, or null for a comment on an issue.</summary>
    public static CodeHostConversationComment? ParseConversationComment(JsonElement comment)
    {
        var number = NumberAfter(Text(comment, "html_url"), "/pull/");
        if (number is null && Text(comment, "pull_request_url") is { } pullUrl)
            number = NumberAfter(pullUrl, "/pulls/") ?? NumberAfter(Text(comment, "issue_url"), "/issues/");
        if (number is null || !comment.TryGetProperty("id", out var id))
            return null;
        return new CodeHostConversationComment(
            id.ToString(),
            number.Value,
            comment.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object ? Text(user, "login") ?? "" : "",
            Text(comment, "author_association") ?? "NONE",
            Text(comment, "body") ?? "",
            Time(comment, "created_at") ?? DateTime.MinValue);
    }

    /// <summary>The number after the last <paramref name="marker"/> in a URL (<c>…/pull/12#issuecomment-1</c> is 12).</summary>
    private static int? NumberAfter(string? url, string marker)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        var at = url.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        var start = at + marker.Length;
        var end = start;
        while (end < url.Length && char.IsAsciiDigit(url[end]))
            end++;
        return end > start && int.TryParse(url.AsSpan(start, end - start), out var n) ? n : null;
    }

    public static CodeHostPullRequest ParsePullRequest(JsonElement root)
    {
        var merged = root.TryGetProperty("merged", out var m) && m.ValueKind == JsonValueKind.True
            || root.TryGetProperty("merged_at", out var at) && at.ValueKind == JsonValueKind.String;
        var state = Text(root, "state") == "closed"
            ? merged ? CodeHostPullRequestState.Merged : CodeHostPullRequestState.Closed
            : CodeHostPullRequestState.Open;
        var head = root.GetProperty("head");
        var @base = root.GetProperty("base");
        return new CodeHostPullRequest(
            root.GetProperty("number").GetInt32(),
            Text(root, "title") ?? "",
            Text(root, "body"),
            root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object ? Text(user, "login") ?? "" : "",
            null,
            Text(@base, "ref") ?? "",
            Text(@base, "sha") ?? "",
            Text(head, "ref") ?? "",
            Text(head, "sha") ?? "",
            Text(root, "html_url"),
            state,
            root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True);
    }

    public static CodeHostChangedFile ParseFile(JsonElement file) => new(
        Text(file, "filename") ?? "",
        Text(file, "status") switch
        {
            "added" => CodeHostFileStatus.Added,
            "modified" => CodeHostFileStatus.Modified,
            "removed" => CodeHostFileStatus.Deleted,
            "renamed" => CodeHostFileStatus.Renamed,
            "copied" => CodeHostFileStatus.Copied,
            _ => CodeHostFileStatus.Other,
        },
        file.TryGetProperty("additions", out var add) && add.TryGetInt32(out var a) ? a : 0,
        file.TryGetProperty("deletions", out var del) && del.TryGetInt32(out var d) ? d : 0,
        Text(file, "previous_filename"));

    /// <summary>One conditional GET. A 304 returns the cached body; any other non-2xx throws <see cref="CodeHostException"/>.</summary>
    private async Task<string> GetAsync(CodeHostTarget target, string path, string accept, CancellationToken ct)
    {
        var host = Host(target);
        var uri = new Uri(new Uri((string.IsNullOrWhiteSpace(host.BaseUrl) ? DefaultApiRoot : host.BaseUrl).TrimEnd('/') + "/"), path);
        var key = $"{target.HostId}|{accept}|{uri}";
        var cached = cache.Get(key);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        await AuthorizeAsync(request, target, host, ct);
        if (cached is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);

        var seconds = Math.Max(1, host.TimeoutSeconds);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
                return cached.Body;
            var body = await response.Content.ReadAsStringAsync(budget.Token);
            if (!response.IsSuccessStatusCode)
                throw Failure(response, body, "GET", path);
            if (response.Headers.ETag is { } etag)
                cache.Set(key, etag.ToString(), body);
            return body;
        }
        catch (HttpRequestException ex)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub GET {path}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub GET {path}: no response within {seconds}s.");
        }
    }

    /// <summary>One write with a JSON body. Never cached and never retried: a write is not idempotent.</summary>
    private async Task<string> SendAsync(CodeHostTarget target, HttpMethod method, string path, object payload, CancellationToken ct)
    {
        var host = Host(target);
        var uri = new Uri(new Uri((string.IsNullOrWhiteSpace(host.BaseUrl) ? DefaultApiRoot : host.BaseUrl).TrimEnd('/') + "/"), path);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        await AuthorizeAsync(request, target, host, ct);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        var seconds = Math.Max(1, host.TimeoutSeconds);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            var body = await response.Content.ReadAsStringAsync(budget.Token);
            if (!response.IsSuccessStatusCode)
                throw Failure(response, body, method.Method, path);
            return body;
        }
        catch (HttpRequestException ex)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub {method.Method} {path}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub {method.Method} {path}: no response within {seconds}s.");
        }
    }

    private static CodeHostException Failure(HttpResponseMessage response, string body, string method, string path)
    {
        var status = (int)response.StatusCode;
        var spent = response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0";
        var kind = status switch
        {
            404 => CodeHostFailureKind.NotFound,
            429 => CodeHostFailureKind.RateLimited,
            403 when spent => CodeHostFailureKind.RateLimited,
            401 or 403 => CodeHostFailureKind.Rejected,
            406 => CodeHostFailureKind.TooLarge,
            400 or 422 => CodeHostFailureKind.InvalidRequest,
            _ => CodeHostFailureKind.Unavailable,
        };
        return new CodeHostException(kind, $"GitHub {method} {path}: HTTP {status}: {ErrorText(body)}", status);
    }

    /// <summary>GitHub's <c>{"message"}</c> or the first 300 characters of the body.</summary>
    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && Text(doc.RootElement, "message") is { } message)
                return message;
        }
        catch (JsonException)
        {
        }

        return body.Length <= 300 ? body : body[..300];
    }

    /// <summary>The bearer: an installation token when the host signs in as a GitHub App, else its PAT.</summary>
    private async Task AuthorizeAsync(HttpRequestMessage request, CodeHostTarget target, ReviewCodeHost host, CancellationToken ct)
    {
        var token = host.App is { IsConfigured: true }
            ? await _appTokens.GetTokenAsync(http, target.HostId, host, ApiRoot(host), target.Repo, ct)
            : host.Token;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static Uri ApiRoot(ReviewCodeHost host) =>
        new((string.IsNullOrWhiteSpace(host.BaseUrl) ? DefaultApiRoot : host.BaseUrl).TrimEnd('/') + "/");

    private ReviewCodeHost Host(CodeHostTarget target) =>
        settings.Value.Hosts.TryGetValue(target.HostId, out var host) && host.Kind == ReviewHost.GitHub
            ? host
            : throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"GitHub host '{target.HostId}' is not configured (Review:CodeHosts:Hosts).");

    private static int RequirePr(CodeHostTarget target) =>
        target.PrNumber ?? throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"A GitHub review of {target.Repo} needs a PR number.");

    /// <summary><c>repos/{owner}/{name}</c>, each segment escaped.</summary>
    private static string RepoPath(CodeHostTarget target)
    {
        var parts = target.Repo.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"'{target.Repo}' is not an owner/name repo.");
        return $"repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}";
    }

    private static string ComparePath(CodeHostTarget target, CodeHostRange range) =>
        $"{RepoPath(target)}/compare/{Uri.EscapeDataString(range.SinceSha ?? range.BaseSha)}...{Uri.EscapeDataString(range.HeadSha)}";

    private static JsonDocument Parse(string body, string what)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub returned a {what} that is not JSON: {ex.Message}");
        }
    }

    /// <summary>A listing must be a JSON array; anything else is an unavailable host.</summary>
    private static JsonDocument ParseArray(string body, string what)
    {
        var doc = Parse(body, what);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            return doc;
        doc.Dispose();
        throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub returned a {what} that is not a JSON array.");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTime? Time(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var at)
            ? at.UtcDateTime
            : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : 0;

    private static string? CommitSha(JsonElement root, string name) =>
        root.TryGetProperty(name, out var commit) && commit.ValueKind == JsonValueKind.Object ? Text(commit, "sha") : null;

    /// <summary>A ref or path in a URL: each segment escaped, the slashes kept (<c>release/1.0</c>).</summary>
    private static string EscapeRef(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
}

/// <summary>
/// ETag-validated response bodies for code host GETs (design 8.4), keyed by host, media type and URL.
/// Bounded: past <see cref="MaxEntries"/> it starts again, and bodies over <see cref="MaxBodyChars"/>
/// are not kept. Content addressed by SHA could be kept forever; that split comes with the mirror.
/// </summary>
public sealed class CodeHostResponseCache : IResettableCache
{
    public const int MaxEntries = 512;
    public const int MaxBodyChars = 4_000_000;

    public sealed record Entry(string ETag, string Body);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Entry? Get(string key) => _entries.TryGetValue(key, out var entry) ? entry : null;

    public void Set(string key, string etag, string body)
    {
        if (body.Length > MaxBodyChars)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        if (_entries.Count >= MaxEntries && !_entries.ContainsKey(key))
            _entries.Clear();
        _entries[key] = new Entry(etag, body);
    }

    public void Clear() => _entries.Clear();
}
