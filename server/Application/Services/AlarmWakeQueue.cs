using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0726 D-3: coalesced wakes for eligibility, fences, and the backstop sweep.</summary>
public sealed class AlarmWakeQueue : IRunnerEligibilityObserver, IRepositoryFenceObserver
{
    private readonly object _gate = new();
    private readonly HashSet<string> _runners = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fenced = new(StringComparer.Ordinal);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private bool _sweep;

    public void Changed(string runnerId) => Signal(runnerId);

    public void Signal(string runnerId)
    {
        lock (_gate)
            _runners.Add(runnerId);
        _wake.Writer.TryWrite(true);
    }

    public void Fenced(string commonDirectory)
    {
        lock (_gate)
            _fenced.Add(commonDirectory);
        _wake.Writer.TryWrite(true);
    }

    public void RequestSweep()
    {
        lock (_gate)
            _sweep = true;
        _wake.Writer.TryWrite(true);
    }

    public (bool Sweep, string[] Runners, string[] Fenced) Take()
    {
        lock (_gate)
        {
            _wake.Reader.TryRead(out _);
            var runners = _runners.ToArray();
            var fenced = _fenced.ToArray();
            _runners.Clear();
            _fenced.Clear();
            var sweep = _sweep;
            _sweep = false;
            return (sweep, runners, fenced);
        }
    }

    public async Task WaitAsync(DateTimeOffset due, TimeProvider clock, CancellationToken ct)
    {
        var delay = due - clock.GetUtcNow();
        if (delay <= TimeSpan.Zero)
            return;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signal = _wake.Reader.WaitToReadAsync(wait.Token).AsTask();
        var timer = Task.Delay(delay, clock, wait.Token);
        try
        {
            await await Task.WhenAny(signal, timer);
        }
        finally
        {
            await wait.CancelAsync();
        }
    }
}
