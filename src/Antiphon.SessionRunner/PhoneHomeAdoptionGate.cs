namespace Antiphon.SessionRunner;

public interface IPhoneHomeAdoptionGate
{
    Task WaitAsync(CancellationToken ct);
    void SignalReady();
}

public sealed class PhoneHomeAdoptionGate : IPhoneHomeAdoptionGate
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SignalReady() => _ready.TrySetResult();

    public Task WaitAsync(CancellationToken ct) => _ready.Task.WaitAsync(ct);
}
