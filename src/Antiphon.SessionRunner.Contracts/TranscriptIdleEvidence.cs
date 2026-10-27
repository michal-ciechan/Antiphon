namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Transcript-confirmed idle for ordinary automated input. The last row must be
/// TurnEnd end_turn, and every tool call must have a later result. An empty
/// transcript is not idle. Queue housekeeping after the turn end is not idle.
/// </summary>
public static class TranscriptIdleEvidence
{
    public readonly record struct ToolRow(long Sequence, string Kind, string? ToolUseId, string? ToolName);

    public static bool FromSnapshot(RunnerTranscriptDto? snapshot)
    {
        if (snapshot is null || snapshot.Entries.Count == 0) return false;
        RunnerTranscriptEvent? last = null;
        var tools = new List<ToolRow>();
        foreach (var entry in snapshot.Entries)
        {
            if (last is null || entry.Sequence >= last.Sequence) last = entry;
            if (entry.Kind is TranscriptKinds.ToolCall or TranscriptKinds.ToolResult)
                tools.Add(new ToolRow(entry.Sequence, entry.Kind, entry.ToolUseId, entry.ToolName));
        }
        return IsConfirmedIdle(last!.Kind, last.StopReason, tools);
    }

    public static bool IsConfirmedIdle(string? lastKind, string? lastStopReason, IReadOnlyList<ToolRow> tools)
    {
        if (!string.Equals(lastKind, TranscriptKinds.TurnEnd, StringComparison.Ordinal)
            || !string.Equals(lastStopReason, TranscriptKinds.StopReasons.EndTurn, StringComparison.Ordinal))
            return false;
        var open = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tool in tools.OrderBy(t => t.Sequence))
        {
            if (tool.Kind == TranscriptKinds.ToolCall)
            {
                if (string.IsNullOrEmpty(tool.ToolUseId)) return false;
                open[tool.ToolUseId] = tool.ToolName;
            }
            else if (tool.Kind == TranscriptKinds.ToolResult && !string.IsNullOrEmpty(tool.ToolUseId))
                open.Remove(tool.ToolUseId);
        }
        return open.Count == 0;
    }
}
