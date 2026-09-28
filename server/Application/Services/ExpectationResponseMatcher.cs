using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Matches one complete answer to the frozen delivered watchdog prompt.</summary>
public static class ExpectationResponseMatcher
{
    public static bool IsAnswer(TranscriptEntry entry, Guid nudgeId)
    {
        if (entry.Kind != TranscriptKinds.AssistantText || entry.IsApiError == true
            || string.IsNullOrWhiteSpace(entry.Text))
            return false;
        var marker = ExpectationPromptFormatter.AckMarker(nudgeId);
        var lines = entry.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var valid = new List<string>();
        var fenced = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }
            if (!fenced && !trimmed.StartsWith(">", StringComparison.Ordinal))
                valid.Add(trimmed);
        }
        if (valid.Count(line => string.Equals(line, marker, StringComparison.Ordinal)) != 1)
            return false;
        // A marker alone is not an answer. The other text must state an action or a reason.
        return valid.Any(line => line.Length > 0
            && !string.Equals(line, marker, StringComparison.Ordinal));
    }
}
