namespace Antiphon.LandGraphAudit;

/// <summary>Wall-clock phase waits. The copy clock's callback cancels an overrun copy.</summary>
internal sealed class WallClock : IPhaseClock
{
    private readonly Action? _onExpire;

    public WallClock(Action? onExpire = null) => _onExpire = onExpire;

    public bool TryComplete(TimeSpan budget, Action work)
    {
        var task = Task.Run(work);
        if (task.Wait(budget))
        {
            task.GetAwaiter().GetResult();
            return true;
        }

        if (_onExpire is not null)
        {
            _onExpire();
            try { task.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }

        return false;
    }
}
