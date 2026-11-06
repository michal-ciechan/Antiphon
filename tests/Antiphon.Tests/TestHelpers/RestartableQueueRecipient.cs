namespace Antiphon.Tests.TestHelpers;

/// <summary>Rebuild the real recipient queue/runtime graph while retaining its database and generation.</summary>
internal sealed class RestartableQueueRecipient : IAsyncDisposable
{
    private readonly BridgeQueueHarness.HarnessOptions _options;
    private readonly List<BridgeQueueHarness> _generations = [];
    public BridgeQueueHarness Current { get; private set; } = null!;
    public int SubmittedCount => _generations.Sum(g => g.Adapter.SubmittedBodies.Count);

    private RestartableQueueRecipient(BridgeQueueHarness.HarnessOptions options) =>
        _options = options with { PreserveDatabaseOnDispose = true };

    public static async Task<RestartableQueueRecipient> CreateAsync(BridgeQueueHarness.HarnessOptions options)
    {
        var recipient = new RestartableQueueRecipient(options);
        recipient.Current = await ClaudeDialogFixture.CreateBridgeAsync(recipient._options);
        recipient._generations.Add(recipient.Current);
        return recipient;
    }

    public async Task RestartAsync()
    {
        await Current.DisposeAsync();
        Current = await ClaudeDialogFixture.CreateBridgeAsync(_options);
        _generations.Add(Current);
    }

    public async ValueTask DisposeAsync()
    {
        await Current.DisposeAsync();
        foreach (var generation in _generations)
            if (Directory.Exists(generation.TempRoot)) Directory.Delete(generation.TempRoot, recursive: true);
    }
}
