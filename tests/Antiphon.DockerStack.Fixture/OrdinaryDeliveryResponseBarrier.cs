namespace Antiphon.DockerStack.Fixture;

public sealed class HoldingResponse
{
    public bool HasStarted { get; private set; }
    public int FlushCount { get; private set; }
    public List<byte> Body { get; } = new();
    public int Status { get; private set; }
    public string? Header { get; private set; }

    public void MarkStarted() => HasStarted = true;

    public void Append(byte[] body) => Body.AddRange(body);

    public void Flush() => FlushCount++;

    public void Set(int status, string header, byte[] body)
    {
        Status = status;
        Header = header;
        Body.Clear();
        Body.AddRange(body);
    }
}

public sealed class OrdinaryDeliveryResponseBarrier
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Armed { get; set; }

    public void Release() => _release.TrySetResult();

    public async Task StartAsync(HoldingResponse response, CancellationToken cancellationToken)
    {
        if (Armed)
            await _release.Task.WaitAsync(cancellationToken);
        response.MarkStarted();
    }

    public async Task FlushAsync(HoldingResponse response, CancellationToken cancellationToken)
    {
        if (Armed)
            await _release.Task.WaitAsync(cancellationToken);
        response.Flush();
    }

    public async Task WriteAsync(HoldingResponse response, int status, string header, byte[] body, CancellationToken cancellationToken)
    {
        if (Armed)
            await _release.Task.WaitAsync(cancellationToken);
        response.Set(status, header, body);
    }
}
