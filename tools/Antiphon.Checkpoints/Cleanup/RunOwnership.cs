using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class RunOwnership
{
    public int Version { get; set; } = 1;
    public string RunId { get; set; } = "";
    public string RunDirectory { get; set; } = "";
    public string Phase { get; set; } = "preparing";
    public ProcessIdentity? Starter { get; set; }
    public ProcessIdentity? Launched { get; set; }
}

public static class RunOwnershipStore
{
    public const string FileName = "ownership.json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static RunOwnership? Read(string runDirectory)
    {
        try
        {
            var path = Path.Combine(runDirectory, FileName);
            var record = JsonSerializer.Deserialize<RunOwnership>(File.ReadAllText(path), Json);
            return record is { Version: 1 }
                && record.RunId == Path.GetFileName(Path.GetFullPath(runDirectory))
                && SamePath(record.RunDirectory, runDirectory) ? record : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Write(string runDirectory, RunOwnership record)
    {
        var full = Path.GetFullPath(runDirectory);
        if (record.Version != 1 || record.RunId != Path.GetFileName(full) || !SamePath(record.RunDirectory, full))
            throw new InvalidOperationException("run ownership does not bind its directory");
        var path = Path.Combine(full, FileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, record, Json);
            stream.Flush(flushToDisk: true);
        }
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(temp, path, true); return; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
                { Thread.Sleep(20 * (attempt + 1)); }
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
