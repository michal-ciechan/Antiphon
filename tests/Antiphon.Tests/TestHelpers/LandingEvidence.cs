using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Optional, fixture-only evidence; never includes process environment or stderr.</summary>
internal static class LandingEvidence
{
    private static readonly object Gate = new();
    private static long _sequence;

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTIPHON_C448_EVIDENCE"));

    public static void Write(Guid taskId, string kind, object facts)
    {
        var directory = Environment.GetEnvironmentVariable("ANTIPHON_C448_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            var details = TUnit.Core.TestContext.Current?.Metadata.TestDetails;
            var row = JsonSerializer.Serialize(new { sequence = ++_sequence, taskId, testClass = details?.ClassType.Name,
                testName = details?.TestName, process = Environment.ProcessId,
                at = DateTime.UtcNow, kind, facts });
            File.AppendAllText(Path.Combine(directory, $"{taskId:N}-{Environment.ProcessId}.jsonl"), row + "\n");
        }
    }
}
