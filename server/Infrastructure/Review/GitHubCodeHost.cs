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
/// GitHub over REST with a personal access token (review engine design 8.3). The host comes from
/// <c>Review:CodeHosts:Hosts</c> by the target's host id: its base URL (GitHub Enterprise Server or a
/// proxy) and its token, sent as a bearer token and never logged. Every GET is conditional through
/// <see cref="CodeHostResponseCache"/>, so an unchanged resource costs a 304, which GitHub does not
/// count against the rate limit.
/// </summary>
public sealed class GitHubCodeHost(HttpClient http, IOptions<ReviewCodeHostSettings> settings, CodeHostResponseCache cache) : ICodeHost
{
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
    /// Resolving a thread needs GraphQL (the thread node id is not in any REST response), so it is off
    /// until the GraphQL call lands; replies still post.
    /// </summary>
    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: false, ApprovalCountsForMerge: true, SupportsAutoMerge: true,
        CanPostReviews: true, CanReplyToThreads: true, SupportsCommitStatuses: true, CanApprove: true, CanUpdateBranch: true,
        CanReadFiles: true);

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
            if (match.ValueKind != JsonValueKind.Object)
                continue;
            remaining.Remove(match);
            posted.Add(new CodeHostPostedComment(wanted.Path, wanted.Line, match.GetProperty("id").ToString(), null, Text(match, "html_url")));
        }

        return new CodeHostPostedReview(reviewId, url, posted);
    }

    /// <summary><c>POST pulls/{n}/comments/{id}/replies</c>.</summary>
    public async Task<CodeHostThreadReply> ReplyToThreadAsync(CodeHostTarget target, string commentId, string body, CancellationToken ct)
    {
        var created = await SendAsync(target, HttpMethod.Post,
            $"{RepoPath(target)}/pulls/{RequirePr(target)}/comments/{Uri.EscapeDataString(commentId)}/replies", new { body }, ct);
        using var doc = Parse(created, "reply");
        return new CodeHostThreadReply(doc.RootElement.TryGetProperty("id", out var id) ? id.ToString() : "", Text(doc.RootElement, "html_url"));
    }

    /// <summary>Needs GraphQL <c>resolveReviewThread</c>; see <see cref="Capabilities"/>.</summary>
    public Task ResolveThreadAsync(CodeHostTarget target, string threadId, CancellationToken ct) =>
        throw new CodeHostException(CodeHostFailureKind.Unsupported, "Resolving a GitHub thread needs the GraphQL API, which this adapter does not call yet.");

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

    /// <summary><c>PUT pulls/{n}/update-branch</c>: asynchronous (202), so the new head is not known yet.</summary>
    public async Task<CodeHostBranchUpdate> UpdateBranchAsync(CodeHostTarget target, string expectedHeadSha, CancellationToken ct)
    {
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
        if (!string.IsNullOrEmpty(host.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
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
        if (!string.IsNullOrEmpty(host.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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
