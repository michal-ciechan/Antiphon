using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

public sealed class WorktreeBaseBudgetExceededException : Exception
{
    public WorktreeBaseBudgetExceededException(string reason) : base(reason) => Reason = reason;
    public string Reason { get; }
}

/// <summary>One worktree-base resolution's command/time budget. Equality on the deadline is expired.</summary>
public sealed class WorktreeBaseGitSession
{
    public WorktreeBaseGitSession(GitSettings settings, TimeProvider clock, DateTimeOffset startedAt)
    {
        MaxCommands = Math.Max(1, settings.WorktreeBaseMaxGitCommands);
        MaxCandidates = Math.Max(1, settings.WorktreeBaseMaxCandidates);
        Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.WorktreeBaseInspectionTimeoutSeconds));
        Deadline = startedAt + Timeout;
        Clock = clock;
        StartedAt = startedAt;
    }

    public int MaxCommands { get; }
    public int MaxCandidates { get; }
    public TimeSpan Timeout { get; }
    public DateTimeOffset Deadline { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset StartedAt { get; }
    public int Admissions { get; private set; }
    public int Starts { get; private set; }
    public bool FetchAttempted { get; private set; }
    public List<string[]> Commands { get; } = [];

    public double ElapsedSeconds => Math.Max(0, (Clock.GetUtcNow() - StartedAt).TotalSeconds);

    public bool DeadlineReached => Clock.GetUtcNow() >= Deadline;

    public TimeSpan Remaining
    {
        get
        {
            var left = Deadline - Clock.GetUtcNow();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    /// <summary>
    /// Links caller cancellation with the remaining inspection deadline. Gate waits and owned
    /// git processes must observe this token so expiry cancels in-flight work instead of
    /// checking the clock only before/after admission.
    /// </summary>
    public DeadlineScope Link(CancellationToken caller)
    {
        if (DeadlineReached)
            throw new WorktreeBaseBudgetExceededException("inspection_timeout");
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        var timer = Clock.CreateTimer(
            static state =>
            {
                try { ((CancellationTokenSource)state!).Cancel(); }
                catch (ObjectDisposedException) { }
            },
            cts,
            Remaining,
            System.Threading.Timeout.InfiniteTimeSpan);
        return new DeadlineScope(cts, timer);
    }

    public void Admit(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && string.Equals(args[0], "fetch", StringComparison.OrdinalIgnoreCase))
            FetchAttempted = true;
        if (DeadlineReached)
            throw new WorktreeBaseBudgetExceededException("inspection_timeout");
        if (Admissions >= MaxCommands || Starts >= MaxCommands)
            throw new WorktreeBaseBudgetExceededException("git_command_limit");
        Admissions++;
    }

    public void Started(IReadOnlyList<string> args)
    {
        Starts++;
        var copy = new string[args.Count];
        for (var i = 0; i < args.Count; i++) copy[i] = args[i];
        Commands.Add(copy);
    }

    public sealed class DeadlineScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ITimer _timer;

        public DeadlineScope(CancellationTokenSource cts, ITimer timer)
        {
            _cts = cts;
            _timer = timer;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            _timer.Dispose();
            _cts.Dispose();
        }
    }
}
