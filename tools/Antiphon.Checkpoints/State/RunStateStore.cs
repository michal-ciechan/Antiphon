using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class RunStateStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string? Write(string path, RunState state)
    {
        if (string.IsNullOrEmpty(state.ToolOwner))
            state.ToolOwner = SafeWrite.Marker;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var ownership = SafeWrite.Under(directory, state.RunId, WriteKind.ToolOwnedReplaceable);
        var history = Path.Combine(directory, "state.history.jsonl");
        var json = JsonSerializer.Serialize(state, Json);
        var refusal = SafeWrite.Text(path, json, ownership);
        if (refusal is not null)
            return refusal;
        try
        {
            var line = JsonSerializer.Serialize(state, new JsonSerializerOptions(Json) { WriteIndented = false }) + "\n";
            SafeWrite.Append(history, line, ownership);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public RunState? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<RunState>(json, Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
