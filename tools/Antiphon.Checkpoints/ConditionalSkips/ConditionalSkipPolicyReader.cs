using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

/// <summary>Strict, bounded declaration parsing. S2 supplies committed Git-blob authority.</summary>
public static class ConditionalSkipPolicyReader
{
    public const string CatalogPath = "tests/checkpoint-conditional-skips.json";
    public const int MaximumBytes = 65_536;

    public static ConditionalSkipCatalog Parse(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw Invalid("catalog exceeds 65536 bytes");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            Shape(root, "schemaVersion", "contracts");
            if (Integer(root, "schemaVersion") != 1) throw Invalid("unsupported catalog version");
            var entries = Array(root, "contracts", 64);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var contracts = new List<ConditionalSkipContract>();
            foreach (var entry in entries.EnumerateArray())
            {
                Shape(entry, "id", "probe", "project", "filter", "expectedTotal", "minExecuted", "skips");
                var id = Text(entry, "id");
                if (!ConditionalSkipContract.IsValidId(id)) throw Invalid("invalid contract ID");
                if (!ids.Add(id)) throw Invalid("duplicate contract ID");
                var probe = Text(entry, "probe");
                if (probe != ConditionalSkipContract.ProbeId) throw Invalid("unsupported probe");
                var project = Text(entry, "project");
                if (project.Contains('\\') || project.Contains(':') || project.Any(char.IsControl)
                    || project.Split('/').Any(part => part is "" or "." or ".."))
                    throw Invalid("project must be repository-relative with forward slashes and no traversal");
                var filter = Text(entry, "filter");
                var total = Positive(entry, "expectedTotal");
                var floor = Positive(entry, "minExecuted");
                var skips = Array(entry, "skips", 64);
                if (skips.GetArrayLength() == 0) throw Invalid("skips must be nonempty");
                var identities = new HashSet<(string, string)>();
                var declared = new List<ConditionalSkipIdentity>();
                long skipCount = 0;
                foreach (var skip in skips.EnumerateArray())
                {
                    Shape(skip, "className", "method", "count", "reason");
                    var className = Text(skip, "className");
                    var method = Text(skip, "method");
                    if (className.IndexOfAny(['*', '?', '[', ']', '|']) >= 0
                        || method.IndexOfAny(['*', '?', '[', ']', '|']) >= 0)
                        throw Invalid("skip identities cannot contain wildcards");
                    if (!identities.Add((className, method))) throw Invalid("duplicate qualified skip identity");
                    var count = Positive(skip, "count");
                    var reason = Text(skip, "reason");
                    if (reason.Length > 2_048) throw Invalid("reason exceeds 2048 characters");
                    skipCount += count;
                    declared.Add(new ConditionalSkipIdentity(className, method, count, reason));
                }
                if (skipCount >= total || floor > total - skipCount) throw Invalid("impossible total or execution floor");
                contracts.Add(new ConditionalSkipContract(id, probe, project, filter, total, floor, declared.AsReadOnly()));
            }
            return new ConditionalSkipCatalog(Convert.ToHexStringLower(SHA256.HashData(bytes)), contracts.AsReadOnly());
        }
        catch (JsonException) { throw Invalid("malformed catalog JSON"); }
    }

    public static ConditionalSkipContract Resolve(ConditionalSkipCatalog catalog, CheckpointManifest manifest, CheckpointSpec row)
    {
        ConditionalSkipContract.ValidateCombinations(manifest);
        var contract = catalog.Contracts.SingleOrDefault(c => string.Equals(c.Id, row.ConditionalSkips, StringComparison.Ordinal))
            ?? throw Invalid("unknown conditional contract ID");
        var project = manifest.Builds.SingleOrDefault(b => b.Id == row.Build)?.Project;
        if (!string.Equals(contract.Project, project, StringComparison.Ordinal)
            || !string.Equals(contract.Filter, row.Filter, StringComparison.Ordinal)
            || contract.MinExecuted != row.MinExecuted)
            throw Invalid("conditional declaration project/filter/floor mismatch");
        return contract;
    }

    private static void Shape(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("catalog object required");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !fields.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid("duplicate or unknown catalog property");
        if (seen.Count != fields.Length) throw Invalid("missing catalog property");
    }

    private static JsonElement Array(JsonElement parent, string field, int maximum)
    {
        var value = parent.GetProperty(field);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw Invalid("invalid or oversized " + field);
        return value;
    }

    private static string Text(JsonElement parent, string field)
    {
        var value = parent.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw Invalid("nonempty string required for " + field);
        return value.GetString()!;
    }

    private static int Integer(JsonElement parent, string field)
    {
        var value = parent.GetProperty(field);
        if (value.ValueKind != JsonValueKind.Number
            || !int.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw Invalid("int32 required for " + field);
        return number;
    }

    private static int Positive(JsonElement parent, string field)
    {
        var value = Integer(parent, field);
        return value > 0 ? value : throw Invalid("positive count required for " + field);
    }

    private static ManifestValidationException Invalid(string message) => new("conditionalSkips", message);
}
