using System.Text.Json;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public sealed class RunStateStore
{
    private readonly CheckpointCleanupContext _context;
    private readonly object _publicationGate = new();

    public RunStateStore() : this(CheckpointCleanupDefaults.Context) { }
    public RunStateStore(CheckpointCleanupContext context) => _context = context;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string? Write(string path, RunState state)
    {
        // Scheduler and row continuations share this store. Keep serialization,
        // replacement verification, custody recording and history in one publication.
        lock (_publicationGate)
            return WriteCore(path, state);
    }

    private string? WriteCore(string path, RunState state)
    {
        if (string.IsNullOrEmpty(state.ToolOwner))
            state.ToolOwner = SafeWrite.Marker;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var write = new CheckpointWrite(_context);
        var ownership = write.Under(directory, state.RunId, WriteKind.ToolOwnedReplaceable);
        var history = Path.Combine(directory, "state.history.jsonl");
        var json = JsonSerializer.Serialize(state, Json);
        for (var attempt = 0; ; attempt++)
        {
            // A read-only destination is a permanent policy refusal, not contention.
            if (_context.Io.FileExists(path) && (_context.Io.Attributes(path) & FileAttributes.ReadOnly) != 0)
                return "read-only";
            try
            {
                var refusal = write.Text(path, json, ownership);
                if (refusal is not null)
                    return refusal;
                break;
            }
            catch (Exception ex) when (attempt < 5 && IsContention(ex))
            {
                _context.Delay(TimeSpan.FromMilliseconds(50 << attempt));
            }
        }
        try
        {
            var line = JsonSerializer.Serialize(state, new JsonSerializerOptions(Json) { WriteIndented = false }) + "\n";
            write.Append(history, line, ownership);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public RunState? TryRead(string path) => TryRead(path, out _);

    private static bool IsContention(Exception failure)
    {
        if (failure is UnauthorizedAccessException)
            return true;
        if (failure is not IOException)
            return false;
        // 1175/1176/1177 are ReplaceFileW outcomes that can drop or rename the destination.
        if (failure.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021)
            or unchecked((int)0x80070497) or unchecked((int)0x80070498) or unchecked((int)0x80070499))
            return true;
        // ReceiptFile retains the typed native failure when adding path context.
        return failure.InnerException is { } inner && IsContention(inner);
    }

    public RunState? TryRead(string path, out string? refusal) => TryRead(path, out refusal, CancellationToken.None);

    public RunState? TryRead(string path, out string? refusal, CancellationToken cancellationToken)
    {
        refusal = null;
        try
        {
            var json = ReceiptFile.ReadText(_context, path, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return ReceiptJson.Parse<RunState>(json, Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            refusal = "state-receipt-unreadable: " + ex.Message;
            return null;
        }
    }
}
