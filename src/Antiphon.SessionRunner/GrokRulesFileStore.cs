using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Owns only the runner session's stable rules file, independent of cwd and native home.</summary>
public sealed class GrokRulesFileStore(string sessionLogPath, GrokRulesSettings settings)
{
    public string PathFor(Guid sessionId)
    {
        try { return Path.Combine(Path.GetFullPath(sessionLogPath), "instructions", "grok", sessionId.ToString("N"), "rules.md"); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { throw WriteFailure("canonical_path"); }
    }

    public async Task<GrokRulesReceipt> WriteAsync(Guid sessionId, GrokRulesPayload payload, CancellationToken ct)
    {
        settings.Validate();
        var bytes = GrokRulesTransport.Encode(payload, true, settings.MaxFileBytes);
        string path;
        try { path = PathFor(sessionId); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { throw WriteFailure("canonical_path"); }
        var receipt = new GrokRulesReceipt(path, GrokRulesTransport.Hash(bytes), bytes.Length,
            payload.TransportVersion, payload.Generation);
        GrokRulesTransport.ValidateReceipt(receipt, sessionId, payload, settings.MaxFileBytes);
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, $"rules.{payload.Generation:N}.{Guid.NewGuid():N}.tmp");
        var operation = "temp_write";
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            operation = "replace";
            File.Move(temp, path, overwrite: true);
            return receipt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw WriteFailure(operation); }
        finally
        {
            // Only this write's sibling; never sweep another session or a concurrent generation.
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<bool> VerifyAsync(Guid sessionId, GrokRulesReceipt receipt, CancellationToken ct)
    {
        if (!string.Equals(receipt.Path, PathFor(sessionId), StringComparison.Ordinal)
            || receipt.TransportVersion != GrokRulesTransport.Version || receipt.Generation == Guid.Empty)
            return false;
        try
        {
            var bytes = await File.ReadAllBytesAsync(receipt.Path, ct);
            return bytes.Length == receipt.ByteCount
                && string.Equals(GrokRulesTransport.Hash(bytes), receipt.Sha256, StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static GrokRulesTransportException WriteFailure(string operation) =>
        new("grok_rules_file_write_failed", operation, 500);
}
