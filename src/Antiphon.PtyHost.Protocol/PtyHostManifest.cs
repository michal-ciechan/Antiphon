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
    public bool LaunchPending { get; init; }
    public global::Antiphon.SessionRunner.Contracts.VerificationExecutionBinding? VerificationBinding { get; init; }
    public global::Antiphon.SessionRunner.Contracts.VerificationHostIdentity? VerificationHost { get; init; }
    public global::Antiphon.SessionRunner.Contracts.GrokRulesReceipt? GrokRulesReceipt { get; init; }
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
        if (GrokRulesReceipt is null && VerificationBinding is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var legacyTemp = path + ".tmp";
            File.WriteAllText(legacyTemp, JsonSerializer.Serialize(this, Options));
            File.Move(legacyTemp, path, overwrite: true);
            return;
        }
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(this, Options));
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new global::Antiphon.SessionRunner.Contracts.GrokRulesTransportException("grok_rules_file_write_failed", "metadata", 500); }
        finally
        {
            try { File.Delete(tmp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
