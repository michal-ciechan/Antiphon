using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

// Fixture-owned inactivity deadline, like CheckpointStepDeadline. The existing
// 10/60-second case budgets now measure silence between receipts, not total
// seeding/body/teardown time. Only completed operations renew them.
internal sealed class C1514HostLoadScenario : IDisposable
{
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _stop;
    private readonly TimeSpan _quiet;
    private readonly CancellationToken _caller;
    private readonly object _gate = new();
    private bool _disposed;
    private long _completed;
    private string _receipt = "scenario startup";
    private static readonly AsyncLocal<C1514HostLoadScenario?> Ambient = new();
    public static C1514HostLoadScenario? Current => Ambient.Value;
    public long Completed => Interlocked.Read(ref _completed);
    public CancellationToken Token => _stop.Token;
    public C1514HostLoadScenario(TimeSpan quiet, CancellationToken cancellationToken = default, TimeProvider? time = null)
    {
        _quiet = quiet;
        _caller = cancellationToken;
        _deadline = new(quiet, time ?? TimeProvider.System);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
    }
    public void Observe(string receipt = "completed operation")
    {
        lock (_gate)
        {
            if (_disposed || Token.IsCancellationRequested) return;
            _receipt = receipt; Interlocked.Increment(ref _completed);
            _deadline.CancelAfter(_quiet);
        }
    }
    public void Cancel() => _stop.Cancel();
    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        var previous = Ambient.Value; Ambient.Value = this;
        Task? body = null;
        try
        {
            body = action(Token);
            await body.WaitAsync(Token);
            Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested)
        {
            // WaitAsync alone abandons the body. Cancel and join it, including its
            // await-using/finally blocks, before TUnit releases the serial group.
            Cancel();
            Exception? cause = null;
            if (body is not null) try { await body; } catch (Exception error) { cause = error; }
            if (_deadline.IsCancellationRequested && !_caller.IsCancellationRequested)
                throw new TimeoutException($"No C1514 progress for {_quiet.TotalSeconds}s; {Completed} receipts; last: {_receipt}", cause);
            throw;
        }
        finally { Cancel(); Ambient.Value = previous; }
    }
    public void Dispose()
    { lock (_gate) { _disposed = true; _stop.Dispose(); _deadline.Dispose(); } }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class C1514ProgressBudgetAttribute(int milliseconds) : Attribute
{ public TimeSpan Quiet { get; } = TimeSpan.FromMilliseconds(milliseconds); }

public sealed class C1514HostLoadProgressExecutor : ITestExecutor
{
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        var budget = context.Metadata.TestDetails.GetAttributes<C1514ProgressBudgetAttribute>().First().Quiet;
        using var scenario = new C1514HostLoadScenario(budget, context.Execution.CancellationToken);
        await scenario.RunAsync(_ => action().AsTask());
    }
}
