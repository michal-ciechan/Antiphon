namespace Antiphon.Checkpoints;

internal sealed class ConsoleCancellation : IDisposable
{
    private readonly ConsoleCancelEventHandler _handler;

    private ConsoleCancellation(ConsoleCancelEventHandler handler)
    {
        _handler = handler;
        Console.CancelKeyPress += handler;
    }

    public static IDisposable Subscribe(ConsoleCancelEventHandler handler) => new ConsoleCancellation(handler);

    public void Dispose() => Console.CancelKeyPress -= _handler;
}
