using System.Diagnostics;
using System.Text;
using Antiphon.Agents.Pty;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Captures the live-output growth profile while a quiet-period assertion is waiting.
/// A false "done" result is only actionable when this distinguishes a genuinely silent child
/// from a detector that declared quiet while output was still arriving (CARD-0128 S1).
/// </summary>
internal sealed class OutputGapTimeline : IAsyncDisposable
{
    private const int SamplePeriodMs = 25;
    private readonly PtyAgentRunner _runner;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Sample> _samples = [];
    private readonly Task _sampling;
    private bool _stopped;

    private OutputGapTimeline(PtyAgentRunner runner)
    {
        _runner = runner;
        Capture();
        _sampling = SampleAsync();
    }

    public static OutputGapTimeline Start(PtyAgentRunner runner) => new(runner);

    public async Task<string> StopAndDescribeAsync(TimeSpan quietPeriod)
    {
        if (!_stopped)
        {
            _stopped = true;
            await _stop.CancelAsync();
            await _sampling;
            Capture();
        }

        var samples = _samples.ToArray();
        var previousLength = samples[0].Length;
        var lastGrowthMs = samples[0].ElapsedMs;
        var maxGapMs = 0L;

        foreach (var sample in samples.Skip(1))
        {
            if (sample.Length == previousLength)
                continue;

            maxGapMs = Math.Max(maxGapMs, sample.ElapsedMs - lastGrowthMs);
            lastGrowthMs = sample.ElapsedMs;
            previousLength = sample.Length;
        }

        // Include the terminal no-growth span: it is the quiet interval that can make a detector
        // return true before the assertion observes the result.
        maxGapMs = Math.Max(maxGapMs, samples[^1].ElapsedMs - lastGrowthMs);

        var timeline = new StringBuilder();
        foreach (var sample in samples)
        {
            if (timeline.Length > 0) timeline.Append(", ");
            timeline.Append(sample.ElapsedMs).Append("ms:").Append(sample.Length);
        }

        return $"output-gap timeline (sample period {SamplePeriodMs}ms; QuietPeriod={quietPeriod.TotalMilliseconds:F0}ms; "
               + $"max inter-growth/terminal gap={maxGapMs}ms; samples elapsedMs:liveBufferLength=[{timeline}])";
    }

    public async ValueTask DisposeAsync()
    {
        await StopAndDescribeAsync(TimeSpan.Zero);
        _stop.Dispose();
    }

    private async Task SampleAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(SamplePeriodMs, _stop.Token);
                Capture();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Expected when the detector wait completes.
        }
    }

    private void Capture() => _samples.Add(new Sample(_clock.ElapsedMilliseconds, _runner.SnapshotText().Length));

    private readonly record struct Sample(long ElapsedMs, int Length);
}
