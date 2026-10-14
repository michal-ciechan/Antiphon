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
    public ToolCopyDigest? ToolCopy { get; set; }
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
            var record = ReceiptJson.Read<RunOwnership>(path, Json);
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
        var json = JsonSerializer.Serialize(record, Json);
        var ownership = SafeWrite.Under(full);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var reason = SafeWrite.Text(path, json, ownership);
                if (reason is not null)
                    throw new IOException("refused: " + reason);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       && attempt < 8
                                       && !ex.Message.StartsWith("refused:", StringComparison.Ordinal))
            {
                Thread.Sleep(20 * (attempt + 1));
            }
        }
    }

    public static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
