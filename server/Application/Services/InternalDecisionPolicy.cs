using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure CARD-0407 grant-contract validation. Empty input becomes absent; a nonempty document is
/// canonicalized, hashed, and stamped with server-resolved provenance. Question-time evaluation
/// lives in a later slice.
/// </summary>
public static class InternalDecisionPolicy
{
    public const int Version = 1;
    public const int MaxGrants = 16;
    public const int MaxDistinctPaths = 64;
    public const int MaxPreserveChars = 1_000;
    public const int MaxCanonicalJsonChars = 20_000;
    public const int MaxGrantIdChars = 64;
    public const string GitAttributesFileName = ".gitattributes";
    public const string Field = "InternalDecisionPolicy";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>Exact-path comparison for the executing OS's repository filesystem.</summary>
    public static StringComparer FileSystemComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static bool RoleMayCarryGrants(AgentTaskRole role) =>
        Enum.IsDefined(role)
        && role is AgentTaskRole.Code
            or AgentTaskRole.Debug
            or AgentTaskRole.Custom
            or AgentTaskRole.Deploy
            or AgentTaskRole.Plan
            or AgentTaskRole.TestDesign
            or AgentTaskRole.Coverage
            or AgentTaskRole.Docs
            or AgentTaskRole.Commit
            or AgentTaskRole.Merge;

    public static StoredInternalDecisionPolicy? Normalize(
        InternalDecisionPolicyRequest? request,
        AgentTaskRole role,
        WorkspaceMode workspace,
        StoredInternalDecisionGrantedBy grantedBy,
        DateTime grantedAt)
    {
        if (request is null)
            return null;
        if (request.Grants is null || request.Grants.Count == 0)
            return null;

        if (request.Version != Version)
        {
            throw new ValidationException(
                Field,
                $"Internal decision policy version {request.Version} is not supported. Use version {Version}.",
                "internal_decision_policy_invalid");
        }

        if (!Enum.IsDefined(role) || !RoleMayCarryGrants(role))
        {
            throw new ValidationException(
                Field,
                $"Role '{role}' cannot carry an internal decision policy.",
                "internal_decision_role_ineligible");
        }

        if (workspace == WorkspaceMode.ReadOnly)
        {
            throw new ValidationException(
                Field,
                "A ReadOnly workspace cannot carry an internal decision policy.",
                "internal_decision_readonly");
        }

        if (request.Grants.Count > MaxGrants)
        {
            throw new ValidationException(
                Field,
                $"An internal decision policy may have at most {MaxGrants} grants (got {request.Grants.Count}).",
                "internal_decision_policy_invalid");
        }

        var grants = new List<StoredInternalDecisionGrant>(request.Grants.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinctPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < request.Grants.Count; i++)
        {
            grants.Add(NormalizeGrant(request.Grants[i], i, seenIds, distinctPaths));
        }

        if (distinctPaths.Count > MaxDistinctPaths)
        {
            throw new ValidationException(
                Field,
                $"An internal decision policy may name at most {MaxDistinctPaths} distinct paths (got {distinctPaths.Count}).",
                "internal_decision_policy_invalid");
        }

        var document = new InternalDecisionPolicyDocument(Version, grants);
        var canonical = JsonSerializer.Serialize(document, JsonOptions);
        if (canonical.Length > MaxCanonicalJsonChars)
        {
            throw new ValidationException(
                Field,
                $"Canonical internal decision policy JSON must be at most {MaxCanonicalJsonChars} characters (got {canonical.Length}).",
                "internal_decision_policy_invalid");
        }

        var at = DateTime.SpecifyKind(grantedAt, DateTimeKind.Utc);
        return new StoredInternalDecisionPolicy(Version, grants, grantedBy, at);
    }

    public static StoredInternalDecisionPolicy? Parse(
        string? json,
        AgentTaskRole role,
        WorkspaceMode workspace,
        StoredInternalDecisionGrantedBy grantedBy,
        DateTime grantedAt)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        InternalDecisionPolicyRequest request;
        try
        {
            request = JsonSerializer.Deserialize<InternalDecisionPolicyRequest>(json, JsonOptions)
                ?? throw new ValidationException(
                    Field,
                    "Internal decision policy JSON was empty.",
                    "internal_decision_policy_invalid");
        }
        catch (JsonException ex)
        {
            throw new ValidationException(
                Field,
                "Internal decision policy JSON is invalid: " + ex.Message,
                "internal_decision_policy_invalid");
        }

        return Normalize(request, role, workspace, grantedBy, grantedAt);
    }

    public static StoredInternalDecisionGrantedBy GrantorFrom(
        Guid? taskId,
        Guid? sessionId,
        Guid? capabilityId,
        string? capabilityName)
    {
        if (capabilityId is not null)
        {
            return new StoredInternalDecisionGrantedBy(
                "capability", null, null, capabilityId, capabilityName);
        }

        if (taskId is not null)
            return new StoredInternalDecisionGrantedBy("task", taskId, sessionId, null, null);

        if (sessionId is not null)
            return new StoredInternalDecisionGrantedBy("session", null, sessionId, null, null);

        return new StoredInternalDecisionGrantedBy("manual", null, null, null, null);
    }

    public static string Serialize(StoredInternalDecisionPolicy policy) =>
        JsonSerializer.Serialize(policy, JsonOptions);

    public static string Hash(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

    public static StoredInternalDecisionPolicy ReadStored(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredInternalDecisionPolicy>(json, JsonOptions)
                ?? throw new InvalidOperationException("Stored internal decision policy JSON was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Stored internal decision policy JSON is unreadable.", ex);
        }
    }

    public static string NormalizeRepositoryPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ValidationException(
                Field,
                "Internal decision paths must be nonempty repository-relative file paths.",
                "internal_decision_policy_invalid");
        }

        var trimmed = raw.Trim();
        if (trimmed.Contains('\0', StringComparison.Ordinal))
        {
            throw new ValidationException(
                Field,
                "Internal decision paths cannot contain NUL.",
                "internal_decision_policy_invalid");
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' is not a repository-relative file path.",
                "internal_decision_policy_invalid");
        }

        if (IndexOfWildcard(trimmed) >= 0)
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot contain wildcards.",
                "internal_decision_policy_invalid");
        }

        var slashed = trimmed.Replace('\\', '/');
        if (slashed.StartsWith("//", StringComparison.Ordinal) || slashed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot be a UNC path.",
                "internal_decision_policy_invalid");
        }

        if (slashed.StartsWith('/'))
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot be rooted.",
                "internal_decision_policy_invalid");
        }

        if (slashed.StartsWith('~'))
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot be home-relative.",
                "internal_decision_policy_invalid");
        }

        if (slashed.Length >= 2 && char.IsAsciiLetter(slashed[0]) && slashed[1] == ':')
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot be drive-absolute or drive-relative.",
                "internal_decision_policy_invalid");
        }

        if (slashed.EndsWith('/'))
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' cannot grant a directory.",
                "internal_decision_policy_invalid");
        }

        var segments = slashed.Split('/');
        var built = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                throw new ValidationException(
                    Field,
                    $"Internal decision path '{raw}' cannot contain empty segments.",
                    "internal_decision_policy_invalid");
            }

            if (segment == ".")
                continue;

            if (segment == "..")
            {
                throw new ValidationException(
                    Field,
                    $"Internal decision path '{raw}' cannot contain '..'.",
                    "internal_decision_policy_invalid");
            }

            built.Add(segment);
        }

        if (built.Count == 0)
        {
            throw new ValidationException(
                Field,
                $"Internal decision path '{raw}' is not a repository-relative file path.",
                "internal_decision_policy_invalid");
        }

        return string.Join('/', built);
    }

    public static bool PathsEqual(string left, string right, StringComparer comparer) =>
        comparer.Equals(NormalizeRepositoryPath(left), NormalizeRepositoryPath(right));

    public static bool PathIsGranted(string requested, IReadOnlyList<string> grantPaths, StringComparer comparer)
    {
        var normalized = NormalizeRepositoryPath(requested);
        foreach (var granted in grantPaths)
        {
            if (comparer.Equals(normalized, granted))
                return true;
        }

        return false;
    }

    public static bool IsGitAttributesPath(string normalizedPath)
    {
        var slash = normalizedPath.LastIndexOf('/');
        var name = slash < 0 ? normalizedPath : normalizedPath[(slash + 1)..];
        return name.Equals(GitAttributesFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static StoredInternalDecisionGrant NormalizeGrant(
        InternalDecisionGrantRequest grant,
        int index,
        HashSet<string> seenIds,
        HashSet<string> distinctPaths)
    {
        var id = grant.Id?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ValidationException(
                Field,
                $"Grant {index} is missing an id.",
                "internal_decision_policy_invalid");
        }

        if (id.Length > MaxGrantIdChars || id.Contains('/') || id.Contains('\\') || id.Any(char.IsWhiteSpace))
        {
            throw new ValidationException(
                Field,
                $"Grant id '{id}' is not a valid grant identifier.",
                "internal_decision_policy_invalid");
        }

        if (!seenIds.Add(id))
        {
            throw new ValidationException(
                Field,
                $"Grant id '{id}' is duplicated.",
                "internal_decision_policy_invalid");
        }

        if (grant.Categories is null || grant.Categories.Count == 0)
        {
            throw new ValidationException(
                Field,
                $"Grant '{id}' must name at least one category.",
                "internal_decision_policy_invalid");
        }

        foreach (var category in grant.Categories)
        {
            if (!Enum.IsDefined(category))
            {
                throw new ValidationException(
                    Field,
                    $"Grant '{id}' names unsupported category '{category}'.",
                    "internal_decision_policy_invalid");
            }
        }

        if (grant.Paths is null || grant.Paths.Count == 0)
        {
            throw new ValidationException(
                Field,
                $"Grant '{id}' must name at least one path.",
                "internal_decision_policy_invalid");
        }

        var preserve = grant.Preserve?.Trim();
        if (string.IsNullOrWhiteSpace(preserve))
        {
            throw new ValidationException(
                Field,
                $"Grant '{id}' must include a preserve statement.",
                "internal_decision_policy_invalid");
        }

        if (preserve.Length > MaxPreserveChars)
        {
            throw new ValidationException(
                Field,
                $"Grant '{id}' preserve text must be at most {MaxPreserveChars} characters (got {preserve.Length}).",
                "internal_decision_policy_invalid");
        }

        var paths = new List<string>(grant.Paths.Count);
        var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasGitAttributes = false;
        foreach (var raw in grant.Paths)
        {
            var path = NormalizeRepositoryPath(raw);
            if (!pathSet.Add(path))
                continue;
            paths.Add(path);
            distinctPaths.Add(path);
            if (IsGitAttributesPath(path))
                hasGitAttributes = true;
        }

        IReadOnlyList<string>? attributeTargets = null;
        if (hasGitAttributes)
        {
            if (!grant.Categories.Contains(InternalDecisionCategory.LineEndings))
            {
                throw new ValidationException(
                    Field,
                    $"Grant '{id}' may include {GitAttributesFileName} only for LineEndings.",
                    "internal_decision_policy_invalid");
            }

            if (grant.AttributeTargets is null || grant.AttributeTargets.Count == 0)
            {
                throw new ValidationException(
                    Field,
                    $"Grant '{id}' must name nonempty attributeTargets when {GitAttributesFileName} is granted.",
                    "internal_decision_policy_invalid");
            }

            var targets = new List<string>(grant.AttributeTargets.Count);
            var targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in grant.AttributeTargets)
            {
                var target = NormalizeRepositoryPath(raw);
                if (IsGitAttributesPath(target))
                {
                    throw new ValidationException(
                        Field,
                        $"Grant '{id}' attributeTargets cannot include {GitAttributesFileName}.",
                        "internal_decision_policy_invalid");
                }

                if (!pathSet.Contains(target))
                {
                    throw new ValidationException(
                        Field,
                        $"Grant '{id}' attribute target '{target}' is not in the grant paths.",
                        "internal_decision_policy_invalid");
                }

                if (!targetSet.Add(target))
                    continue;
                targets.Add(target);
            }

            if (targets.Count == 0)
            {
                throw new ValidationException(
                    Field,
                    $"Grant '{id}' must name nonempty attributeTargets when {GitAttributesFileName} is granted.",
                    "internal_decision_policy_invalid");
            }

            attributeTargets = targets;
        }
        else if (grant.AttributeTargets is { Count: > 0 })
        {
            throw new ValidationException(
                Field,
                $"Grant '{id}' cannot set attributeTargets without granting {GitAttributesFileName}.",
                "internal_decision_policy_invalid");
        }

        var categories = grant.Categories.Distinct().OrderBy(c => (int)c).ToArray();
        return new StoredInternalDecisionGrant(id, categories, paths, attributeTargets, preserve);
    }

    private static int IndexOfWildcard(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '*' or '?' or '[' or ']')
                return i;
        }

        return -1;
    }

    private sealed record InternalDecisionPolicyDocument(
        int Version,
        IReadOnlyList<StoredInternalDecisionGrant> Grants);
}
