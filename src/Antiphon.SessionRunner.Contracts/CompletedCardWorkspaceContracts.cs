using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-1032. Schema for the guarded ordinary-mirror command. Version 1 is the only admitted schema.</summary>
public static class CompletedCardWorkspaceProtocol
{
    public const int SchemaVersion = 1;
    public const string DispositionCardDone = "CardDone";
    public const string ProofContained = "Contained";
    public const string ProofRebased = "Rebased";
}

/// <summary>Runner-issued creation identity. BindingId is nonempty and runner-issued; this record does not mint one.</summary>
public sealed record CompletedCardWorkspaceBinding(
    Guid TaskId,
    int Attempt,
    Guid BindingId,
    Guid PreparationId,
    string RunnerId,
    string RunnerStoreId,
    string RepositoryIdentity,
    string CommonDirectory,
    string GitDirectory,
    string WorktreePath,
    string SourceFullRef,
    string CreationBaselineSha);

/// <summary>
/// Preservation proof carried in the immutable command. <see cref="RebasedSourceSha"/> is present
/// only for <see cref="CompletedCardWorkspaceProtocol.ProofRebased"/>; direct containment leaves it absent.
/// </summary>
public sealed record CompletedCardWorkspaceProof(
    string ExpectedHeadSha,
    string DestinationFingerprint,
    string DestinationRef,
    string ObservedTargetSha,
    string ProofKind,
    string ProofIdentity,
    string PreservedSourceSha,
    string? RebasedSourceSha);

/// <summary>CARD-1032. Read-only inspect body. It cannot enroll an unbound directory and has no Force.</summary>
public sealed record CompletedCardWorkspaceInspectRequest(
    int SchemaVersion,
    CompletedCardWorkspaceBinding Binding);

public sealed record CompletedCardWorkspaceInspectionEntry(string Path, string Kind);

public sealed record CompletedCardWorkspaceInspectionResponse(string HeadSha, string SourceFullRef,
    IReadOnlyList<CompletedCardWorkspaceInspectionEntry> IgnoredContent, string? Reason)
{
    public IReadOnlyList<string> HeldPaths { get; init; } = [];
}

public sealed record CompletedCardWorkspaceRemovalResponse(bool Complete, string? Reason,
    CompletedCardWorkspaceReceipt? Receipt);

/// <summary>
/// CARD-1032. Immutable guarded remove command. There is no Force member; operation 15 stays the legacy remover.
/// </summary>
public sealed record CompletedCardWorkspaceCommand(
    int SchemaVersion,
    Guid CardId,
    Guid DoneRevisionId,
    Guid TargetId,
    Guid EndpointId,
    Guid OperationId,
    string Disposition,
    CompletedCardWorkspaceBinding Binding,
    CompletedCardWorkspaceProof Proof,
    string ReportDigest,
    string ArtifactProofDigest,
    string DisposalPolicyDigest,
    IReadOnlyList<string> ArtifactPointers,
    IReadOnlyList<string> PolicyPatterns);

/// <summary>Typed runner receipt imported before a mirror endpoint may become complete.</summary>
public sealed record CompletedCardWorkspaceReceipt(
    int SchemaVersion,
    Guid CardId,
    Guid DoneRevisionId,
    Guid TargetId,
    Guid EndpointId,
    Guid OperationId,
    Guid TaskId,
    int Attempt,
    Guid BindingId,
    Guid PreparationId,
    string RunnerId,
    string RunnerStoreId,
    string RepositoryIdentity,
    string CommonDirectory,
    string GitDirectory,
    string WorktreePath,
    string SourceFullRef,
    string CreationBaselineSha,
    string ExpectedHeadSha,
    string PreservationProofDigest,
    string ArtifactProofDigest,
    string DisposalPolicyDigest,
    string Disposition,
    string SetAsideIdentity,
    string JournalIdentity,
    bool DirectoryRemoved,
    bool RegistrationRemoved);

public readonly record struct CompletedCardWorkspaceValidation(bool IsValid, string? Reason)
{
    public static CompletedCardWorkspaceValidation Ok { get; } = new(true, null);

    public static CompletedCardWorkspaceValidation Reject(string reason) => new(false, reason);
}

/// <summary>
/// CARD-1032. Parses the guarded command with the phone-home JSON options, refuses duplicates and
/// Force, and hashes a key-sorted canonical document. Digest coverage accepts a second well-typed
/// value for every field; <see cref="Validate"/> still admits only schema 1 and CardDone.
/// </summary>
public static class CompletedCardWorkspaceContract
{
    public static CompletedCardWorkspaceValidation ValidateInspect(string json)
    {
        if (!TryDocument(json, out var document, out var failure))
            return CompletedCardWorkspaceValidation.Reject(failure!);
        using (document)
        {
            var root = document!.RootElement;
            if (ContainsForce(root)) return CompletedCardWorkspaceValidation.Reject("force_forbidden");
            if (root.ValueKind != JsonValueKind.Object) return CompletedCardWorkspaceValidation.Reject("malformed");
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("schemaVersion" or "binding"))
                    return CompletedCardWorkspaceValidation.Reject("unknown." + property.Name);
            if (!TryInt(root, "schemaVersion", out var version) || version != CompletedCardWorkspaceProtocol.SchemaVersion)
                return CompletedCardWorkspaceValidation.Reject("schema_version");
            if (RequireObject(root, "binding", "Binding") is { } missing)
                return CompletedCardWorkspaceValidation.Reject(missing);
            var binding = root.GetProperty("binding");
            if (Unknown(binding, BindingNames) is { } unknown)
                return CompletedCardWorkspaceValidation.Reject("unknown." + unknown);
            return ValidateBinding(binding) is { } reason
                ? CompletedCardWorkspaceValidation.Reject(reason) : CompletedCardWorkspaceValidation.Ok;
        }
    }

    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schemaVersion", "cardId", "doneRevisionId", "targetId", "endpointId", "operationId",
        "disposition", "binding", "proof", "reportDigest", "artifactProofDigest", "disposalPolicyDigest",
        "artifactPointers", "policyPatterns",
    };

    private static readonly HashSet<string> BindingNames = new(StringComparer.Ordinal)
    {
        "taskId", "attempt", "bindingId", "preparationId", "runnerId", "runnerStoreId",
        "repositoryIdentity", "commonDirectory", "gitDirectory", "worktreePath", "sourceFullRef",
        "creationBaselineSha",
    };

    private static readonly HashSet<string> ProofNames = new(StringComparer.Ordinal)
    {
        "expectedHeadSha", "destinationFingerprint", "destinationRef", "observedTargetSha",
        "proofKind", "proofIdentity", "preservedSourceSha", "rebasedSourceSha",
    };

    public static CompletedCardWorkspaceValidation Validate(string json)
    {
        if (!TryDocument(json, out var document, out var failure))
            return CompletedCardWorkspaceValidation.Reject(failure!);
        using (document)
        {
            var root = document!.RootElement;
            if (ContainsForce(root))
                return CompletedCardWorkspaceValidation.Reject("force_forbidden");
            if (root.ValueKind != JsonValueKind.Object)
                return CompletedCardWorkspaceValidation.Reject("malformed");
            if (Unknown(root, RootNames) is { } unknown)
                return CompletedCardWorkspaceValidation.Reject("unknown." + unknown);
            if (!TryInt(root, "schemaVersion", out var version) || version != CompletedCardWorkspaceProtocol.SchemaVersion)
                return CompletedCardWorkspaceValidation.Reject("schema_version");
            if (RequireGuid(root, "cardId", "CardId") is { } card)
                return CompletedCardWorkspaceValidation.Reject(card);
            if (RequireGuid(root, "doneRevisionId", "DoneRevisionId") is { } done)
                return CompletedCardWorkspaceValidation.Reject(done);
            if (RequireGuid(root, "targetId", "TargetId") is { } target)
                return CompletedCardWorkspaceValidation.Reject(target);
            if (RequireGuid(root, "endpointId", "EndpointId") is { } endpoint)
                return CompletedCardWorkspaceValidation.Reject(endpoint);
            if (RequireGuid(root, "operationId", "OperationId") is { } operation)
                return CompletedCardWorkspaceValidation.Reject(operation);
            if (RequireText(root, "disposition", "Disposition") is { } dispositionMissing)
                return CompletedCardWorkspaceValidation.Reject(dispositionMissing);
            if (root.GetProperty("disposition").GetString() != CompletedCardWorkspaceProtocol.DispositionCardDone)
                return CompletedCardWorkspaceValidation.Reject("disposition");
            if (RequireObject(root, "binding", "Binding") is { } bindingMissing)
                return CompletedCardWorkspaceValidation.Reject(bindingMissing);
            var binding = root.GetProperty("binding");
            if (Unknown(binding, BindingNames) is { } bindingUnknown)
                return CompletedCardWorkspaceValidation.Reject("unknown." + bindingUnknown);
            if (ValidateBinding(binding) is { } bindingFailure)
                return CompletedCardWorkspaceValidation.Reject(bindingFailure);
            if (RequireObject(root, "proof", "Proof") is { } proofMissing)
                return CompletedCardWorkspaceValidation.Reject(proofMissing);
            var proof = root.GetProperty("proof");
            if (Unknown(proof, ProofNames) is { } proofUnknown)
                return CompletedCardWorkspaceValidation.Reject("unknown." + proofUnknown);
            if (ValidateProof(proof) is { } proofFailure)
                return CompletedCardWorkspaceValidation.Reject(proofFailure);
            if (RequireText(root, "reportDigest", "ReportDigest") is { } report)
                return CompletedCardWorkspaceValidation.Reject(report);
            if (RequireText(root, "artifactProofDigest", "ArtifactProofDigest") is { } artifacts)
                return CompletedCardWorkspaceValidation.Reject(artifacts);
            if (RequireText(root, "disposalPolicyDigest", "DisposalPolicyDigest") is { } policy)
                return CompletedCardWorkspaceValidation.Reject(policy);
            if (RequireStrings(root, "artifactPointers", "ArtifactPointers") is { } pointers)
                return CompletedCardWorkspaceValidation.Reject(pointers);
            if (RequireStrings(root, "policyPatterns", "PolicyPatterns") is { } patterns)
                return CompletedCardWorkspaceValidation.Reject(patterns);
            return CompletedCardWorkspaceValidation.Ok;
        }
    }

    /// <summary>
    /// SHA-256 of the canonical JSON. Object keys are ordered, and artifact pointers and policy
    /// patterns are ordered. A null property is omitted, so an absent rebase source and an explicit
    /// null are the same contained state. Does not apply the admission version or disposition gate.
    /// </summary>
    public static string CanonicalDigest(string json)
    {
        if (!TryDocument(json, out var document, out var failure))
            throw new InvalidOperationException(failure);
        using (document)
        {
            var canonical = Canonical(document!.RootElement, null);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }
    }

    private static string? ValidateBinding(JsonElement binding)
    {
        if (RequireGuid(binding, "taskId", "TaskId") is { } task)
            return task;
        if (!TryInt(binding, "attempt", out var attempt) || attempt < 1)
            return "required.Attempt";
        if (RequireGuid(binding, "bindingId", "BindingId") is { } bindingId)
            return bindingId;
        if (RequireGuid(binding, "preparationId", "PreparationId") is { } preparation)
            return preparation;
        if (RequireText(binding, "runnerId", "RunnerId") is { } runner)
            return runner;
        if (RequireText(binding, "runnerStoreId", "RunnerStoreId") is { } store)
            return store;
        if (RequireText(binding, "repositoryIdentity", "RepositoryIdentity") is { } repository)
            return repository;
        if (RequireText(binding, "commonDirectory", "CommonDirectory") is { } common)
            return common;
        if (RequireText(binding, "gitDirectory", "GitDirectory") is { } git)
            return git;
        if (RequireText(binding, "worktreePath", "WorktreePath") is { } path)
            return path;
        if (RequireText(binding, "sourceFullRef", "SourceFullRef") is { } reference)
            return reference;
        if (RequireText(binding, "creationBaselineSha", "CreationBaselineSha") is { } baseline)
            return baseline;
        return null;
    }

    private static string? ValidateProof(JsonElement proof)
    {
        if (RequireText(proof, "expectedHeadSha", "ExpectedHeadSha") is { } head)
            return head;
        if (RequireText(proof, "destinationFingerprint", "DestinationFingerprint") is { } fingerprint)
            return fingerprint;
        if (RequireText(proof, "destinationRef", "DestinationRef") is { } destination)
            return destination;
        if (RequireText(proof, "observedTargetSha", "ObservedTargetSha") is { } observed)
            return observed;
        if (RequireText(proof, "proofKind", "ProofKind") is { } kindMissing)
            return kindMissing;
        var kind = proof.GetProperty("proofKind").GetString();
        if (kind is not (CompletedCardWorkspaceProtocol.ProofContained or CompletedCardWorkspaceProtocol.ProofRebased))
            return "proof_kind";
        if (RequireText(proof, "proofIdentity", "ProofIdentity") is { } identity)
            return identity;
        if (RequireText(proof, "preservedSourceSha", "PreservedSourceSha") is { } preserved)
            return preserved;
        var hasRebase = proof.TryGetProperty("rebasedSourceSha", out var rebase)
            && rebase.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(rebase.GetString());
        var rebaseNullOrAbsent = !proof.TryGetProperty("rebasedSourceSha", out rebase) || rebase.ValueKind == JsonValueKind.Null;
        if (kind == CompletedCardWorkspaceProtocol.ProofContained && !rebaseNullOrAbsent)
            return "rebased_source";
        if (kind == CompletedCardWorkspaceProtocol.ProofRebased && !hasRebase)
            return "rebased_source";
        if (proof.TryGetProperty("rebasedSourceSha", out rebase)
            && rebase.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return "rebased_source";
        return null;
    }

    private static bool TryDocument(string json, out JsonDocument? document, out string? failure)
    {
        document = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "malformed";
            return false;
        }

        var utf8 = Encoding.UTF8.GetBytes(json);
        try
        {
            if (HasDuplicateProperty(utf8))
            {
                failure = "duplicate";
                return false;
            }

            document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
            return true;
        }
        catch (JsonException)
        {
            failure = "malformed";
            return false;
        }
    }

    private static bool HasDuplicateProperty(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    names.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (names.Count == 0)
                        return false;
                    names.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    if (names.Count == 0 || !names.Peek().Add(reader.GetString() ?? ""))
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool ContainsForce(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals("force", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (ContainsForce(property.Value))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsForce(item))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static string? Unknown(JsonElement obj, HashSet<string> allowed)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return property.Name;
        }

        return null;
    }

    private static string? RequireObject(JsonElement obj, string name, string logical)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            return "required." + logical;
        return null;
    }

    private static string? RequireGuid(JsonElement obj, string name, string logical)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "required." + logical;
        return Guid.TryParse(value.GetString(), out _) ? null : "required." + logical;
    }

    private static string? RequireText(JsonElement obj, string name, string logical)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "required." + logical;
        return string.IsNullOrWhiteSpace(value.GetString()) ? "required." + logical : null;
    }

    private static string? RequireStrings(JsonElement obj, string name, string logical)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return "required." + logical;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                return "required." + logical;
        }

        return null;
    }

    private static bool TryInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
            return false;
        if (!property.TryGetInt32(out value))
            return false;
        var raw = property.GetRawText();
        return raw.IndexOfAny(['.', 'e', 'E']) < 0;
    }

    private static string Canonical(JsonElement element, string? name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new List<(string Name, string Json)>();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    properties.Add((property.Name, Canonical(property.Value, property.Name)));
                }

                properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
                var objectText = new StringBuilder("{");
                for (var i = 0; i < properties.Count; i++)
                {
                    if (i > 0)
                        objectText.Append(',');
                    objectText.Append(JsonSerializer.Serialize(properties[i].Name));
                    objectText.Append(':');
                    objectText.Append(properties[i].Json);
                }

                objectText.Append('}');
                return objectText.ToString();
            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                    items.Add(Canonical(item, null));
                if (name is "artifactPointers" or "policyPatterns")
                    items.Sort(StringComparer.Ordinal);
                return "[" + string.Join(",", items) + "]";
            case JsonValueKind.String:
                return JsonSerializer.Serialize(element.GetString());
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer) && element.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0)
                    return integer.ToString(CultureInfo.InvariantCulture);
                return element.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
                return "null";
            default:
                throw new InvalidOperationException("malformed");
        }
    }
}
