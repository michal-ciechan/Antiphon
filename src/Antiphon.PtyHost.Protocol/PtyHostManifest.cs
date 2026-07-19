using System.Text.Json;

namespace Antiphon.PtyHost.Protocol;

/// <summary>
/// On-disk record of one pty-host, written by the host to <c>logs/pty-hosts/&lt;sessionId&gt;.json</c>.
/// The runner's adoption sweep reads these to find hosts that survived a runner restart.
/// Env is deliberately never persisted (secrets). Written atomically (temp + rename) so the
/// sweep never observes a torn file.
/// </summary>
public sealed record PtyHostManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }
    public required string PipeName { get; init; }
    public int ProtocolVersion { get; init; } = PtyHostProtocol.Version;
    public required int HostPid { get; init; }
    public required DateTime HostStartTimeUtc { get; init; }
    public int? ChildPid { get; init; }
    public DateTime? ChildStartTimeUtc { get; init; }
    public string? Exe { get; init; }
    public string? Cwd { get; init; }
    public int Cols { get; init; }
    public int Rows { get; init; }
    public bool TranscriptEnabled { get; init; }
    public string? AnsiLogPath { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int? ExitCode { get; init; }
    public string? ExitReason { get; init; }
    public DateTime? ExitedAtUtc { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string PathFor(string manifestDir, Guid sessionId) =>
        Path.Combine(manifestDir, $"{sessionId:N}.json");

    public void SaveAtomic(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static PtyHostManifest? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PtyHostManifest>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
