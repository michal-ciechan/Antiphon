using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;

namespace Antiphon.Server.Application.Services;

public static class PipelineStagesJson
{
    public static string Serialise(IReadOnlyList<PipelineStageSpec> stages)
    {
        var canonical = stages.Select(stage => new
        {
            role = stage.Role.ToString(),
            bundleKey = stage.BundleKey.Trim(),
            allowedNext = stage.AllowedNext
                .Select(CanonicalToken)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        });
        return JsonSerializer.Serialize(canonical);
    }

    public static string ContentHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..8].ToLowerInvariant();

    public static IReadOnlyList<PipelineStageSpec> Parse(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw Invalid();
            var stages = new List<PipelineStageSpec>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var roleText = row.GetProperty("role").GetString();
                if (!Enum.TryParse<AgentTaskRole>(roleText, false, out var role)
                    || !Enum.IsDefined(role)
                    || !string.Equals(roleText, role.ToString(), StringComparison.Ordinal))
                    throw Invalid();
                var bundleKey = row.GetProperty("bundleKey").GetString() ?? throw Invalid();
                var next = row.GetProperty("allowedNext");
                if (next.ValueKind != JsonValueKind.Array)
                    throw Invalid();
                stages.Add(new PipelineStageSpec(role, bundleKey,
                    next.EnumerateArray().Select(item => item.GetString() ?? throw Invalid()).ToArray()));
            }
            return stages;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    public static string CanonicalToken(string token)
    {
        var parsed = PipelineHandoff.TryParse($"--- next stage ---\nnext: {token.Trim()}\n");
        return parsed.Kind is { } kind ? PipelineHandoff.Token(kind) : token.Trim().ToLowerInvariant();
    }

    private static ValidationException Invalid() =>
        new("stages", "Stages must be an array of known role, bundle key and allowed next rows.",
            "pipeline_stages_invalid");
}
