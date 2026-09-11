using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

internal sealed record HerdrDisposalStoredOperation(string Fingerprint, HerdrPaneDisposalReceipt Receipt,
    HerdrPaneDisposalPreview? Reviewed = null, IReadOnlyList<HerdrDisposalFile>? Files = null, bool CensusNotified = false);

internal interface IHerdrDisposalReceiptStore
{
    HerdrDisposalStoredOperation? Read(Guid id);
    void Save(HerdrDisposalStoredOperation operation);
    void Prune(DateTimeOffset cutoff);
}

internal sealed class HerdrDisposalReceiptStore(string root) : IHerdrDisposalReceiptStore
{
    private readonly string _directory = Path.Combine(root, "herdr", "disposals");
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public HerdrDisposalStoredOperation? Read(Guid id)
    {
        try
        {
            var row = JsonSerializer.Deserialize<HerdrDisposalStoredOperation>(File.ReadAllBytes(Path.Combine(_directory, $"{id:N}.json")), _json);
            if (row is null || row.Receipt.OperationId != id) throw new IOException("Invalid disposal receipt identity.");
            return row;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(HerdrDisposalStoredOperation operation)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{operation.Receipt.OperationId:N}.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(JsonSerializer.SerializeToUtf8Bytes(operation, _json));
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }

    public void Prune(DateTimeOffset cutoff)
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var path in Directory.GetFiles(_directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            try
            {
                var row = Read(id);
                if (row is not null && row.Receipt.RecordedAtUtc < cutoff && !row.Receipt.CleanupPending
                    && row.Receipt.Outcome is "Closed" or "AlreadyAbsent" or "Refused") File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* retain uncertain state */ }
        }
    }
}
