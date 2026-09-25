using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class RunStateStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public void Write(string path, RunState state)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(state, Json);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
        var history = Path.Combine(Path.GetDirectoryName(path) ?? ".", "state.history.jsonl");
        File.AppendAllText(history, JsonSerializer.Serialize(state, new JsonSerializerOptions(Json) { WriteIndented = false }) + "\n");
    }

    public RunState? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<RunState>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
