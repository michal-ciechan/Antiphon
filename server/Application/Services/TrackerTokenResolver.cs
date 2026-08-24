using System.Security.Cryptography;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0166 S2: resolve a GitHub (or other tracker) token for a board sync pass.
/// Prefers <c>tracker.token_key</c> from the CARD-0106 ApiKeys store (project then global),
/// falls back to <c>tracker.api_key_env</c> / env var (byte-compatible with the pre-S2 path).
/// </summary>
public sealed class TrackerTokenResolver
{
    private readonly AppDbContext _db;
    private readonly IApiKeyProtector _protector;
    private readonly ILogger<TrackerTokenResolver> _logger;

    public TrackerTokenResolver(
        AppDbContext db,
        IApiKeyProtector protector,
        ILogger<TrackerTokenResolver> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// Returns a config copy with <see cref="IssueTrackerConfig.ResolvedToken"/> populated,
    /// or null when the named key cannot be resolved (caller skips the board with a Warning).
    /// Env-var and unauthenticated paths always succeed (ResolvedToken may be null).
    /// </summary>
    public async Task<IssueTrackerConfig?> ResolveAsync(
        IssueTrackerConfig config,
        Guid? projectId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config.TokenKeyName))
        {
            var token = await TryResolveStoredKeyAsync(config.TokenKeyName, projectId, ct);
            if (token is null)
                return null;

            return config with { ResolvedToken = token };
        }

        if (!string.IsNullOrWhiteSpace(config.ApiKeyEnv))
        {
            var fromEnv = Environment.GetEnvironmentVariable(config.ApiKeyEnv);
            return config with { ResolvedToken = string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv };
        }

        return config with { ResolvedToken = null };
    }

    private async Task<string?> TryResolveStoredKeyAsync(
        string keyName,
        Guid? projectId,
        CancellationToken ct)
    {
        var candidates = await _db.ApiKeys
            .AsNoTracking()
            .Where(k => (k.ProjectId == null || k.ProjectId == projectId)
                        && k.Name == keyName)
            .Select(k => new { k.Id, k.Name, k.ProjectId, k.Ciphertext })
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(
                        k => k.ProjectId == projectId && projectId is not null
                             && string.Equals(k.Name, keyName, StringComparison.Ordinal))
                    ?? candidates.FirstOrDefault(
                        k => k.ProjectId == null
                             && string.Equals(k.Name, keyName, StringComparison.Ordinal));

        if (match is null)
        {
            _logger.LogWarning(
                "Tracker token key '{KeyName}' was not found (searched {Scopes}). Skipping board sync.",
                keyName,
                DescribeSearchedScopes(projectId));
            return null;
        }

        try
        {
            var plaintext = _protector.Unprotect(match.Id, match.Ciphertext);
            if (string.IsNullOrEmpty(plaintext))
            {
                _logger.LogWarning(
                    "Tracker token key '{KeyName}' decrypted empty. Skipping board sync.",
                    keyName);
                return null;
            }

            return plaintext;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(
                ex,
                "Tracker token key '{KeyName}' could not be decrypted. Skipping board sync.",
                keyName);
            return null;
        }
    }

    private static string DescribeSearchedScopes(Guid? projectId) =>
        projectId is null
            ? "the global scope"
            : $"project {projectId:N} then the global scope";
}
