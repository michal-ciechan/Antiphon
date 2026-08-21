using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Substitutes <c>{{key:NAME}}</c> placeholders in a launch environment for stored key values
/// (CARD-0106 S2).
///
/// <para>It runs over the FULLY-MERGED environment, at the two places <c>Env</c> is finalized, so a
/// placeholder works identically whichever layer contributed the value — the agent's own launch
/// env, a profile revision's non-secret env, or an <c>appsettings</c> definition. That is also what
/// makes the <c>AgentTuiSecret</c> convergence (plan section 7) a small card later: a profile env
/// value can already say <c>ANTHROPIC_API_KEY={{key:anthropic-default}}</c>.</para>
///
/// <para><b>Project overrides global.</b> A project key exists precisely to specialize what the
/// installation-wide default would do, and the narrower scope winning is the rule every other
/// override in this codebase follows. A pool delegate has no board and therefore no project, so it
/// resolves GLOBAL keys only — deriving a project from a working directory was rejected as
/// unreliable (worktrees are sibling directories; a prefix match would silently mis-scope a secret).</para>
///
/// <para>Every failure arm names the KEY NAME and the scopes searched. None of them carries a value,
/// and this class logs key names only — never a value, never a substituted environment value.</para>
/// </summary>
public sealed class ApiKeyEnvResolver
{
    private readonly AppDbContext _db;
    private readonly IApiKeyProtector _protector;
    private readonly ILogger<ApiKeyEnvResolver> _logger;

    public ApiKeyEnvResolver(
        AppDbContext db,
        IApiKeyProtector protector,
        ILogger<ApiKeyEnvResolver> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// The project whose keys an agent resolves against, or null for global-only.
    /// <c>Agent.BoardId → Board.ProjectId</c> is the only mapping used: it is the one an operator
    /// actually set, unlike anything inferred from a path.
    /// </summary>
    public async Task<Guid?> ResolveProjectIdAsync(Guid? boardId, CancellationToken ct)
    {
        if (boardId is not { } id)
            return null;

        return await _db.Boards
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Resolves a spec's environment for this agent, deriving the project from the agent's board.
    /// The spec's ARGUMENTS are checked too and refused if they carry a placeholder — this is the
    /// enforcement half of "env values only", ahead of the launch-time tripwire so the message names
    /// the agent rather than a session id.
    /// </summary>
    public async Task<AgentLaunchSpec> ResolveSpecAsync(
        AgentLaunchSpec spec,
        Agent? agent,
        string subject,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var projectId = await ResolveProjectIdAsync(agent?.BoardId, ct);
        return await ResolveSpecAsync(spec, projectId, subject, ct);
    }

    /// <summary>Resolves a spec's environment against an explicit scope.</summary>
    public async Task<AgentLaunchSpec> ResolveSpecAsync(
        AgentLaunchSpec spec,
        Guid? projectId,
        string subject,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        for (var i = 0; i < spec.Args.Count; i++)
            ApiKeyPlaceholder.EnsureAbsent(spec.Args[i], $"{subject}, launch argument {i}");

        var env = await ResolveAsync(spec.Env, projectId, subject, ct);
        return ReferenceEquals(env, spec.Env) ? spec : spec with { Env = env };
    }

    /// <summary>
    /// The core: every <c>{{key:NAME}}</c> in every value, replaced with the stored value for that
    /// name — project scope first, then global. Returns the input untouched when nothing references
    /// a key, which is every launch in the installation until somebody writes one.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IReadOnlyDictionary<string, string> env,
        Guid? projectId,
        string subject,
        CancellationToken ct) =>
        (await ResolveWithSecretValuesAsync(env, projectId, subject, ct)).Environment;

    /// <summary>
    /// Resolves an environment and returns the plaintexts substituted while doing so. The latter is
    /// deliberately only for a process probe's redaction list; it must never be persisted, logged,
    /// or returned from an API.
    /// </summary>
    public async Task<ApiKeyEnvironmentResolution> ResolveWithSecretValuesAsync(
        IReadOnlyDictionary<string, string> env,
        Guid? projectId,
        string subject,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(env);

        var referenced = new List<string>();
        foreach (var (name, value) in env)
        {
            if (!ApiKeyPlaceholder.ContainsMarker(value))
                continue;

            // A marker that is not a well-formed token is a typo, and it is caught HERE rather than
            // left for the tripwire: at this point we can still name the variable the operator
            // typed it into.
            if (ApiKeyPlaceholder.HasMalformedToken(value))
            {
                throw new ConflictException(
                    $"Environment variable '{name}' in {subject} contains a malformed API key "
                    + $"placeholder. The form is {{{{key:NAME}}}}, where NAME may contain only "
                    + "letters, digits, '_', '.' and '-'.",
                    "api_key_placeholder_malformed");
            }

            foreach (var keyName in ApiKeyPlaceholder.Names(value))
            {
                if (!referenced.Contains(keyName, StringComparer.Ordinal))
                    referenced.Add(keyName);
            }
        }

        if (referenced.Count == 0)
            return new ApiKeyEnvironmentResolution(env, []);

        var candidates = await _db.ApiKeys
            .AsNoTracking()
            .Where(k => (k.ProjectId == null || k.ProjectId == projectId)
                        && referenced.Contains(k.Name))
            .Select(k => new { k.Id, k.Name, k.ProjectId, k.Ciphertext })
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var keyName in referenced)
        {
            // Project first, then global. Both may exist under the same name — that is the whole
            // point of the two filtered unique indexes.
            var match = candidates.FirstOrDefault(
                            k => k.ProjectId == projectId && projectId is not null
                                 && string.Equals(k.Name, keyName, StringComparison.Ordinal))
                        ?? candidates.FirstOrDefault(
                            k => k.ProjectId == null
                                 && string.Equals(k.Name, keyName, StringComparison.Ordinal));

            if (match is null)
            {
                throw new ConflictException(
                    $"API key '{keyName}' referenced by {subject} was not found "
                    + $"(searched {DescribeSearchedScopes(projectId)}). Add it under "
                    + "Settings -> API Keys, or fix the placeholder.",
                    "api_key_not_found");
            }

            string plaintext;
            try
            {
                plaintext = _protector.Unprotect(match.Id, match.Ciphertext);
            }
            catch (CryptographicException exception)
            {
                // Same split ResolveCoreAsync already makes for a managed profile secret: a
                // protection failure is a 503 the operator can act on, not a 409 about their config.
                throw new ServiceUnavailableException(
                    $"API key protection is unavailable; '{keyName}' could not be read for {subject}.",
                    "api_key_protection_unavailable",
                    exception);
            }

            if (string.IsNullOrEmpty(plaintext) || plaintext.Length > ApiKeyNaming.MaxValueLength)
            {
                // Re-checked AFTER decrypt, not only at write time: a row could predate the ceiling
                // or have been written by a future path that skipped validation.
                throw new ConflictException(
                    $"API key '{keyName}' could not be read safely for {subject}.",
                    "api_key_protection_unavailable");
            }

            values[keyName] = plaintext;
        }

        var resolved = new Dictionary<string, string>(env, StringComparer.Ordinal);
        foreach (var (name, value) in env)
        {
            if (!ApiKeyPlaceholder.ContainsMarker(value))
                continue;

            var substituted = ApiKeyPlaceholder.Token.Replace(
                value,
                match => values[match.Groups[1].Value]);
            if (substituted.Length > ApiKeyNaming.MaxValueLength)
            {
                // A resolved value that cannot be exported. The LENGTH is safe to name; the value is
                // not, and neither is the substring that pushed it over.
                throw new ConflictException(
                    $"Environment variable '{name}' in {subject} is {substituted.Length} characters "
                    + $"once its API keys are resolved; the maximum an environment value may carry "
                    + $"is {ApiKeyNaming.MaxValueLength}.",
                    "api_key_value_too_long");
            }

            resolved[name] = substituted;
        }

        // Key NAMES and the subject. Never a value, never a resolved environment value — this line
        // is the one that would otherwise end up in a log file next to the secret it describes.
        _logger.LogInformation(
            "Resolved API key(s) {KeyNames} for {Subject} ({Scope})",
            string.Join(", ", referenced),
            subject,
            ApiKeyService.DescribeScope(projectId));

        return new ApiKeyEnvironmentResolution(resolved, values.Values.ToArray());
    }

    private static string DescribeSearchedScopes(Guid? projectId) =>
        projectId is { } id ? $"project {id:D}, then the global scope" : "the global scope";
}

/// <summary>Resolved environment plus plaintexts that must be redacted from child-process output.</summary>
public sealed record ApiKeyEnvironmentResolution(
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> SecretValues);
