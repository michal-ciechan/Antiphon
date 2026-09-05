using System.Text.Json;

namespace Antiphon.Server.Application.Services;

/// <summary>JSON array of capability roots. Kept off the domain entity so Domain stays package-free.</summary>
internal static class DelegationCapabilityRoots
{
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<string> roots) =>
        JsonSerializer.Serialize(roots);
}
