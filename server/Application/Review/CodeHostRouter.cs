using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>An adapter and the target it serves.</summary>
public sealed record CodeHostBinding(ICodeHost Host, CodeHostTarget Target);

/// <summary>
/// Resolves a subject to its code host through <c>Review:CodeHosts</c> (design 8): the repo's row
/// names a host id (absent: <c>github</c>, or <c>local</c> for a row with a path), the host's kind
/// must match the subject's, and the first adapter that serves that kind takes it.
/// </summary>
public sealed class CodeHostRouter(IEnumerable<ICodeHost> hosts, IOptions<ReviewCodeHostSettings> settings)
{
    private readonly IReadOnlyList<ICodeHost> _hosts = hosts.ToList();

    public CodeHostBinding Resolve(ReviewHost kind, string repo, int? prNumber, string? baseRef, string? headRef)
    {
        var options = settings.Value;
        var row = options.FindRepo(repo);
        var hostId = row?.Host is { Length: > 0 } named
            ? named
            : kind == ReviewHost.Local ? ReviewCodeHostSettings.DefaultLocalHostId : ReviewCodeHostSettings.DefaultGitHubHostId;
        if (!options.Hosts.TryGetValue(hostId, out var host))
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"Code host '{hostId}' for {repo} is not configured (Review:CodeHosts:Hosts).");
        if (host.Kind != kind)
            throw new CodeHostException(CodeHostFailureKind.InvalidRequest, $"{repo} is configured on the {host.Kind} host '{hostId}', not on {kind}.");
        if (kind == ReviewHost.Local && string.IsNullOrWhiteSpace(row?.LocalPath))
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"No local path is configured for {repo} (Review:CodeHosts:Repos).");

        var adapter = _hosts.FirstOrDefault(h => h.Serves(kind))
            ?? throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"No code host adapter serves {kind}.");
        return new CodeHostBinding(adapter, new CodeHostTarget(hostId, kind, repo, prNumber, row?.LocalPath, baseRef, headRef));
    }

    /// <summary>
    /// A stored subject. A local subject keeps the refs it was asked for (a branch can move); a
    /// GitHub subject is found by its PR number.
    /// </summary>
    public CodeHostBinding Resolve(ReviewSubject subject)
    {
        var repo = subject.Repo;
        var prNumber = subject.PrNumber;
        if (subject.Host == ReviewHost.GitHub)
            (repo, prNumber) = GitHubReviewIdentity.Parse(repo.Trim(), prNumber);
        return Resolve(subject.Host, repo, prNumber, Blank(subject.BaseRef), Blank(subject.HeadRef));
    }

    /// <summary>A code host failure as the API reports it to a caller.</summary>
    public static HttpException ToHttp(CodeHostException ex) => ex.Kind switch
    {
        CodeHostFailureKind.NotConfigured or CodeHostFailureKind.InvalidRequest => new ValidationException("repo", ex.Message),
        CodeHostFailureKind.NotFound => new NotFoundException("Code host subject", "", ex.Message),
        CodeHostFailureKind.Rejected => new ServiceUnavailableException(ex.Message, "review_code_host_rejected", ex),
        CodeHostFailureKind.RateLimited => new ServiceUnavailableException(ex.Message, "review_code_host_rate_limited", ex),
        CodeHostFailureKind.TooLarge => new ServiceUnavailableException(ex.Message, "review_code_host_too_large", ex),
        _ => new ServiceUnavailableException(ex.Message, "review_code_host_unavailable", ex),
    };

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
