using System.Text.Json;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public sealed class RunStateStore
{
    private readonly CheckpointCleanupContext _context;

    public RunStateStore() : this(CheckpointCleanupDefaults.Context) { }
    public RunStateStore(CheckpointCleanupContext context) => _context = context;

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
        var write = new CheckpointWrite(_context);
        var ownership = write.Under(directory, state.RunId, WriteKind.ToolOwnedReplaceable);
        var history = Path.Combine(directory, "state.history.jsonl");
        var json = JsonSerializer.Serialize(state, Json);
        var refusal = write.Text(path, json, ownership);
        if (refusal is not null)
            return refusal;
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
