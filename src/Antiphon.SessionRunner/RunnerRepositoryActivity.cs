namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1817. Counts the runner's own repository lease, an in-flight worktree add or land,
/// and a task git call. The maintenance pass skips while any of them is held. It does not
/// take these counts itself: its git child is not a task git call.
/// </summary>
public sealed class RunnerRepositoryActivity
{
    private int _lease;
    private int _worktreeOrLand;
    private int _git;

    public bool LeaseHeld => Volatile.Read(ref _lease) > 0;
    public bool WorktreeAddOrLandInFlight => Volatile.Read(ref _worktreeOrLand) > 0;
    public bool TaskGitInFlight => Volatile.Read(ref _git) > 0;

    public IDisposable EnterLease()
    {
        Interlocked.Increment(ref _lease);
        return new Hold(LeaveLease);
    }

    public IDisposable EnterWorktreeOrLand()
    {
        Interlocked.Increment(ref _worktreeOrLand);
        return new Hold(LeaveWorktreeOrLand);
    }

    public IDisposable EnterGit()
    {
        Interlocked.Increment(ref _git);
        return new Hold(LeaveGit);
    }

    private void LeaveLease() => Interlocked.Decrement(ref _lease);
    private void LeaveWorktreeOrLand() => Interlocked.Decrement(ref _worktreeOrLand);
    private void LeaveGit() => Interlocked.Decrement(ref _git);

    private sealed class Hold(Action leave) : IDisposable
    {
        private int _once;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _once, 1) == 0)
                leave();
        }
    }
}
