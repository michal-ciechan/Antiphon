using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Validates a worker's description and evaluates one immutable dispatch grant.</summary>
public static class InternalDecisionQuestionPolicy
{
    public const int MaxRequestChars = 10_000;
    public const int MaxQuestionChars = 500;
    public const int MaxActionChars = 1_000;
    public const int MaxEvidenceChars = 2_000;

    public static InternalDecisionQuestionRequest Normalize(InternalDecisionQuestionRequest request)
    {
        if (request.RequestId == Guid.Empty || request.Attempt < 1
            || string.IsNullOrWhiteSpace(request.GrantId)
            || request.GrantId.Length > InternalDecisionPolicy.MaxGrantIdChars
            || request.Category is null || !Enum.IsDefined(request.Category.Value)
            || request.Impact is null || !Enum.IsDefined(request.Impact.Value)
            || request.Paths is not { Count: > 0 and <= InternalDecisionPolicy.MaxDistinctPaths })
            throw Invalid("A request id, current attempt, grant, category, impact and exact paths are required.");

        var paths = NormalizePaths(request.Paths);
        var targets = request.AttributeTargets is null ? null : NormalizePaths(request.AttributeTargets);
        var attributes = request.Attributes is null ? null : request.Attributes.Select(a => a?.Trim() ?? "").ToArray();
        if (attributes is { Length: 0 } || attributes?.Any(string.IsNullOrWhiteSpace) == true)
            throw Invalid("Attributes must name concrete text/eol operations.");

        var question = Bounded(request.Question, MaxQuestionChars, "Question");
        var action = Bounded(request.ProposedAction, MaxActionChars, "ProposedAction");
        var evidence = Bounded(request.PreservationEvidence, MaxEvidenceChars, "PreservationEvidence");
        var normalized = request with
        {
            GrantId = request.GrantId.Trim(),
            Paths = paths,
            AttributeTargets = targets,
            Attributes = attributes,
            Question = question,
            ProposedAction = action,
            PreservationEvidence = evidence,
        };
        if (JsonSerializer.Serialize(normalized, InternalDecisionPolicy.JsonOptions).Length > MaxRequestChars)
            throw Invalid($"A decision question may be at most {MaxRequestChars} characters.");
        return normalized;
    }

    public static string CanonicalJson(InternalDecisionQuestionRequest normalized) =>
        JsonSerializer.Serialize(normalized, InternalDecisionPolicy.JsonOptions);

    public static InternalDecisionQuestionResponse Evaluate(
        StoredInternalDecisionPolicy? policy,
        InternalDecisionQuestionRequest question,
        string repositoryRoot)
    {
        var request = Normalize(question);
        InternalDecisionQuestionResponse Deny(string reason) => new(
            request.RequestId, InternalDecisionDisposition.NeedsHuman, reason, request.GrantId, null);

        if (request.Impact != InternalDecisionImpact.None)
            return Deny("human_impact");
        var grant = policy?.Grants.FirstOrDefault(g =>
            string.Equals(g.Id, request.GrantId, StringComparison.OrdinalIgnoreCase));
        if (grant is null)
            return Deny("no_grant");
        if (!grant.Categories.Contains(request.Category!.Value))
            return Deny("category_not_granted");

        foreach (var path in request.Paths!)
        {
            if (!InternalDecisionPolicy.PathIsGranted(path, grant.Paths, InternalDecisionPolicy.FileSystemComparer)
                || !StaysInRepository(repositoryRoot, path))
                return Deny("path_not_granted");
        }

        var touchesAttributes = request.Paths.Any(InternalDecisionPolicy.IsGitAttributesPath);
        if (touchesAttributes)
        {
            if (request.Category != InternalDecisionCategory.LineEndings
                || request.AttributeTargets is not { Count: > 0 }
                || request.Attributes is not { Count: > 0 })
                return Deny("attribute_not_granted");
            foreach (var target in request.AttributeTargets)
            {
                if (grant.AttributeTargets is null
                    || !InternalDecisionPolicy.PathIsGranted(target, grant.AttributeTargets, InternalDecisionPolicy.FileSystemComparer)
                    || !InternalDecisionPolicy.PathIsGranted(target, request.Paths, InternalDecisionPolicy.FileSystemComparer)
                    || !StaysInRepository(repositoryRoot, target))
                    return Deny("attribute_not_granted");
            }
            if (request.Attributes.Any(a => a is not ("text" or "eol")))
                return Deny("attribute_not_granted");
        }
        else if (request.AttributeTargets is { Count: > 0 } || request.Attributes is { Count: > 0 })
            return Deny("attribute_not_granted");

        return new InternalDecisionQuestionResponse(request.RequestId,
            InternalDecisionDisposition.Continue, "dispatch_grant", grant.Id,
            $"Proceed with the proposed local repair under {grant.Id}; preserve its stated behavior and report verification.");
    }

    private static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string> raw)
    {
        if (raw.Count == 0 || raw.Count > InternalDecisionPolicy.MaxDistinctPaths)
            throw Invalid("Paths must name one or more exact files.");
        var paths = raw.Select(InternalDecisionPolicy.NormalizeRepositoryPath).ToArray();
        if (paths.Distinct(InternalDecisionPolicy.FileSystemComparer).Count() != paths.Length)
            throw Invalid("Paths cannot contain duplicates.");
        return paths;
    }

    private static string Bounded(string? value, int limit, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > limit)
            throw Invalid($"{field} must contain 1 to {limit} characters.");
        return text;
    }

    private static ValidationException Invalid(string message) =>
        new("DecisionQuestion", message, "decision_question_invalid");

    private static bool StaysInRepository(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return false;
        var basePath = Path.GetFullPath(root);
        var current = basePath;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo item = Directory.Exists(current)
                ? new DirectoryInfo(current) : new FileInfo(current);
            if (item.LinkTarget is not null)
            {
                var resolved = item.ResolveLinkTarget(true);
                if (resolved is null)
                    return false;
                current = Path.GetFullPath(resolved.FullName);
            }
            var fromRoot = Path.GetRelativePath(basePath, current);
            if (fromRoot == ".." || fromRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(fromRoot))
                return false;
        }
        return true;
    }
}
