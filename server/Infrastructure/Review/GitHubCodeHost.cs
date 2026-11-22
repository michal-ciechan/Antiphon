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

    /// <summary>A machine user's review counts for branch protection; check runs need a GitHub App (design 8.2).</summary>
    public CodeHostCapabilities Capabilities { get; } = new(
        SupportsCheckRuns: false, CanResolveThreads: true, ApprovalCountsForMerge: true, SupportsAutoMerge: true);

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
                throw Failure(response, body, path);
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

    private static CodeHostException Failure(HttpResponseMessage response, string body, string path)
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
        return new CodeHostException(kind, $"GitHub GET {path}: HTTP {status}: {ErrorText(body)}", status);
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
