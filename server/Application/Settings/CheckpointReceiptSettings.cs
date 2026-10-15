namespace Antiphon.Server.Application.Settings;

// These are operator-owned producer pins, never request-supplied paths or digests.
public sealed record CheckpointProducer(string Identity, string InventoryPath, string InventoryDigest,
    string RepositoryPath, string SelectionRepositoryPath);

public sealed class CheckpointReceiptSettings
{
    public string StorageRoot { get; set; } = "";
    public string ValidatorScript { get; set; } = "";
    public IReadOnlyList<CheckpointProducer> Producers { get; set; } = [];
}
