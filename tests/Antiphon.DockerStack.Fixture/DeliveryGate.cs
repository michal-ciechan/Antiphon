namespace Antiphon.DockerStack.Fixture;

public sealed class DeliveryGate
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Guid? TargetSession { get; init; }
    public string? ArmedCut { get; init; }
    public bool Released { get; private set; }

    public bool IsArmed(string cut) =>
        string.Equals(ArmedCut, cut, StringComparison.Ordinal) && !Released;

    public bool IsTarget(Guid sessionId) => TargetSession is null || TargetSession == sessionId;

    public void Release()
    {
        Released = true;
        _release.TrySetResult();
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _release.Task.WaitAsync(cancellationToken);
}
