namespace Antiphon.LandGraphAudit;

/// <summary>Wall-clock phase waits. A copy timeout cancels the copy and gives it a moment to stop.</summary>
internal sealed class WallClock : IPhaseClock
{
    private readonly bool _cancelCopy;

    public WallClock(bool cancelCopy = false) => _cancelCopy = cancelCopy;

    public bool TryComplete(TimeSpan budget, Action work)
    {
        var task = Task.Run(work);
        if (task.Wait(budget))
        {
            task.GetAwaiter().GetResult();
            return true;
        }

        if (_cancelCopy)
        {
            CanaryRepository.CancelCopy();
            try { task.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }

        return false;
    }
}
