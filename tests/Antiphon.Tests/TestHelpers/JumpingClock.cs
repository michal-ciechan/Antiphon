using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that knows when the code under test is parked on it, so a driver can
/// jump straight to the next due timer instead of creeping fake time forward against real sleeps.
/// <para>
/// Every wait on an injected <see cref="TimeProvider"/> (<c>Task.Delay(t, provider)</c>,
/// <c>new CancellationTokenSource(t, provider)</c>, <c>PeriodicTimer</c>, <c>WaitAsync(t, provider)</c>)
/// is a <see cref="CreateTimer"/>, so each one is recorded with its fake due time and kept current as it
/// fires, changes and is disposed. Real work cannot be seen that way, so it is registered explicitly:
/// database commands, connection opens and transactions through <see cref="DbInterceptor"/>, and anything
/// else a fixture owns (a fake wire, a git runner) through <see cref="BeginRealWork"/>.
/// </para>
/// <para>
/// <see cref="DriveAsync"/> advances fake time only after one real tick in which nothing was in flight
/// and nothing happened (no timer armed or fired, no real work started or ended). It then advances
/// exactly to the earliest due timer, so timers still fire in order and a deadline expires only after
/// everything due before it has run. With no timer pending it falls back to a small step, so a wait
/// nothing here sees still moves.
/// </para>
/// <para>
/// Timeouts the tests expect are of two kinds, and neither needs marking. A product deadline on the
/// injected clock is a timer like any other: when nothing real is in flight the driver jumps through the
/// polls to it and it fires. A real-time timeout (Npgsql's command timeout, a git child's budget, an HTTP
/// client's) never reads this clock. Real work that never ends by itself (a row lock the test holds, a
/// fake runner parked on a hand-completed task) holds fake time only for a bounded real wait; then fake
/// time creeps in small steps, as it did before this driver, so such a test still reaches its deadline.
/// </para>
/// </summary>
internal sealed class JumpingClock : TimeProvider
{
    private static readonly TimeSpan FallbackStep = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan StuckRealWork = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private readonly HashSet<TrackedTimer> _timers = [];
    private static int _ids;
    private readonly int _id = Interlocked.Increment(ref _ids);
    private long _epoch;
    private int _inFlight;

    public JumpingClock(FakeTimeProvider fake)
    {
        Fake = fake;
        DbInterceptor = new RealWorkInterceptor(this);
        if (Log is not null)
            lock (typeof(JumpingClock))
                File.AppendAllText(Log, $"{_id} NEW {TUnit.Core.TestContext.Current?.Metadata.TestDetails.TestName}{Environment.NewLine}");
    }

    public FakeTimeProvider Fake { get; }

    /// <summary>Counts EF commands, connection opens and transaction calls as real work.</summary>
    public IInterceptor DbInterceptor { get; }

    /// <summary>How many fake-time jumps <see cref="DriveAsync"/> made (diagnostics).</summary>
    public int Jumps { get; private set; }

    public override DateTimeOffset GetUtcNow() => Fake.GetUtcNow();
    public override long GetTimestamp() => Fake.GetTimestamp();
    public override long TimestampFrequency => Fake.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => Fake.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TrackedTimer(this) { IsDeadline = IsDeadlineState(state) };
        if (Log is not null) timer.Origin = string.Join(" < ", new StackTrace(1, false).GetFrames().Select(f => f.GetMethod()).Where(m => m?.DeclaringType?.FullName?.StartsWith("Antiphon") == true && m.DeclaringType != typeof(JumpingClock)).Take(3).Select(m => m!.DeclaringType!.Name + "." + m.Name));
        // Armed before the inner timer exists: a zero due time may fire inside CreateTimer.
        timer.Arm(dueTime, period);
        timer.Inner = Fake.CreateTimer(s => { timer.Fired(); callback(s); }, state, dueTime, period);
        return timer;
    }

    /// <summary>Marks real (non-clock) work in flight until the result is disposed.</summary>
    public IDisposable BeginRealWork()
    {
        Interlocked.Increment(ref _inFlight);
        Interlocked.Increment(ref _epoch);
        return new RealWork(this);
    }

    private void EndRealWork()
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _epoch);
    }

    /// <summary>
    /// A timer that cancels something (a <see cref="CancellationTokenSource"/> timeout, a
    /// <c>WaitAsync(timeout, provider)</c>) is a deadline: a watchdog over work that may still be running
    /// out of sight (a continuation queued behind other tests' work, CPU between two SQL commands). Anything
    /// else (<c>Task.Delay</c>, <c>PeriodicTimer</c>) is a wake-up the code is parked on.
    /// </summary>
    private static bool IsDeadlineState(object? state) =>
        state is CancellationTokenSource
        || state?.GetType().Name.StartsWith("CancellationPromise", StringComparison.Ordinal) == true;

    /// <summary>The earliest fake due time of any armed timer.</summary>
    public DateTimeOffset? NextDue() => NextTimer(deadline: null)?.Due;

    /// <summary>The earliest armed timer of the given kind (any kind when null).</summary>
    private TrackedTimer? NextTimer(bool? deadline)
    {
        lock (_gate)
        {
            TrackedTimer? next = null;
            foreach (var timer in _timers)
                if ((deadline is null || timer.IsDeadline == deadline) && timer.Due is { } due
                    && (next?.Due is not { } best || due < best)) next = timer;
            return next;
        }
    }

    /// <summary>Diagnostics: when set (env ANTIPHON_JUMPING_CLOCK_LOG), each jump is logged with the creating stack.</summary>
    private static readonly string? Log = Environment.GetEnvironmentVariable("ANTIPHON_JUMPING_CLOCK_LOG");

    /// <summary>
    /// Runs <paramref name="work"/> to completion, moving fake time whenever it is parked on the clock.
    /// </summary>
    public async Task DriveAsync(Task work)
    {
        var quietWithoutTimer = 0;
        var stuckSince = (long?)null;
        while (!work.IsCompleted)
        {
            var before = Interlocked.Read(ref _epoch);
            await Task.WhenAny(work, Task.Delay(1));
            if (work.IsCompleted) break;
            if (Interlocked.Read(ref _epoch) != before)
            {
                quietWithoutTimer = 0;
                stuckSince = null;
                continue;
            }
            if (Volatile.Read(ref _inFlight) > 0)
            {
                // Real work that does not finish on its own (a row lock the test holds, a wire handler parked on
                // a hand-completed task or on this clock) must not freeze fake time. Past a bounded real wait
                // with nothing changing, creep as the old driver did: small steps, never a jump past a deadline.
                stuckSince ??= Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(stuckSince.Value) >= StuckRealWork) Fake.Advance(FallbackStep);
                continue;
            }
            stuckSince = null;
            var now = Fake.GetUtcNow();
            // Jump only to a wake-up, and only one due before every armed deadline: a quiet tick does not
            // prove the work is parked (its continuation may be waiting for a thread), so jumping to a
            // deadline could cancel work that was about to finish. A deadline is reached by creeping, as
            // the old driver did; it still fires, just not early.
            var deadline = NextTimer(deadline: true)?.Due;
            if (NextTimer(deadline: false) is { Due: { } due } next && (deadline is null || due < deadline))
            {
                quietWithoutTimer = 0;
                Jumps++;
                if (Log is not null)
                    lock (typeof(JumpingClock))
                        File.AppendAllText(Log, $"{_id} JUMP {(due - now).TotalMilliseconds} {next.Origin}{Environment.NewLine}");
                Fake.Advance(due > now ? due - now : TimeSpan.Zero);
            }
            else if (deadline is not null || ++quietWithoutTimer >= 2)
            {
                quietWithoutTimer = 0;
                Fake.Advance(deadline is { } d && d - now < FallbackStep ? d - now : FallbackStep);
            }
        }
        await work;
    }

    private void Touch() => Interlocked.Increment(ref _epoch);

    private sealed class RealWork(JumpingClock clock) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) clock.EndRealWork();
        }
    }

    private sealed class TrackedTimer(JumpingClock clock) : ITimer
    {
        public ITimer? Inner { get; set; }
        public DateTimeOffset? Due { get; private set; }
        public string? Origin { get; set; }
        public bool IsDeadline { get; init; }
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public void Arm(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                _period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Fake.GetUtcNow() + dueTime;
                if (Due is null) clock._timers.Remove(this);
                else clock._timers.Add(this);
            }
            clock.Touch();
        }

        public void Fired()
        {
            lock (clock._gate)
            {
                if (_period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero)
                {
                    Due = null;
                    clock._timers.Remove(this);
                }
                else Due = clock.Fake.GetUtcNow() + _period;
            }
            clock.Touch();
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Arm(dueTime, period);
            return Inner?.Change(dueTime, period) ?? true;
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                Due = null;
                clock._timers.Remove(this);
            }
            clock.Touch();
            Inner?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>EF's view of real I/O: each command, connection open and transaction call is in flight from
    /// its "-ing" event to its "-ed", failed or canceled event.</summary>
    private sealed class RealWorkInterceptor(JumpingClock clock)
        : IDbCommandInterceptor, IDbConnectionInterceptor, IDbTransactionInterceptor
    {
        private readonly ConcurrentDictionary<object, IDisposable> _open = new(ReferenceEqualityComparer.Instance);

        private void Begin(object key)
        {
            var work = clock.BeginRealWork();
            if (!_open.TryAdd(key, work)) work.Dispose();
        }

        private void End(object key)
        {
            if (_open.TryRemove(key, out var work)) work.Dispose();
        }

        // Commands.
        public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Begin(command); return result; }
        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Begin(command); return ValueTask.FromResult(result); }
        public DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        { End(command); return result; }
        public ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        { End(command); return ValueTask.FromResult(result); }

        public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Begin(command); return result; }
        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Begin(command); return ValueTask.FromResult(result); }
        public int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        { End(command); return result; }
        public ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        { End(command); return ValueTask.FromResult(result); }

        public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Begin(command); return result; }
        public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Begin(command); return ValueTask.FromResult(result); }
        public object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        { End(command); return result; }
        public ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result,
            CancellationToken cancellationToken = default)
        { End(command); return ValueTask.FromResult(result); }

        public void CommandFailed(DbCommand command, CommandErrorEventData eventData) => End(command);
        public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        { End(command); return Task.CompletedTask; }
        public void CommandCanceled(DbCommand command, CommandEndEventData eventData) => End(command);
        public Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
        { End(command); return Task.CompletedTask; }

        // Connections.
        public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        { Begin(connection); return result; }
        public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Begin(connection); return ValueTask.FromResult(result); }
        public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => End(connection);
        public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        { End(connection); return Task.CompletedTask; }
        public void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => End(connection);
        public Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
        { End(connection); return Task.CompletedTask; }

        // Transactions: begin, commit and rollback are round trips with no command.
        public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result)
        { Begin(connection); return result; }
        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        { Begin(connection); return ValueTask.FromResult(result); }
        public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        { End(connection); return result; }
        public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        { End(connection); return ValueTask.FromResult(result); }

        public InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        { Begin(transaction); return result; }
        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Begin(transaction); return ValueTask.FromResult(result); }
        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => End(transaction);
        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { End(transaction); return Task.CompletedTask; }

        public InterceptionResult TransactionRollingBack(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        { Begin(transaction); return result; }
        public ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Begin(transaction); return ValueTask.FromResult(result); }
        public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => End(transaction);
        public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { End(transaction); return Task.CompletedTask; }

        public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        {
            End(transaction);
            if (transaction.Connection is { } connection) End(connection);
        }
        public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
        { TransactionFailed(transaction, eventData); return Task.CompletedTask; }
    }
}
