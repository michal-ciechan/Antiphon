using System.Globalization;

namespace Antiphon.TestSupport;

/// <summary>CARD-0550 D-4: a quiet deadline renewed by evidence of progress, under an absolute cap.</summary>
public static class ProgressAwareWait
{
    public const int PollMilliseconds = 100;

    /// <summary>
    /// Polls <paramref name="predicate"/> every <see cref="PollMilliseconds"/> until it is true. The deadline
    /// starts at <paramref name="seconds"/>; when <paramref name="progress"/> is supplied and its value has
    /// changed since the previous poll, the deadline becomes now + <paramref name="seconds"/>, never past
    /// start + <paramref name="capSeconds"/>. Without a probe the behaviour and the timeout message are
    /// exactly the plain wait's.
    /// </summary>
    public static async Task UntilAsync(Func<Task<bool>> predicate, string evidence, int seconds = 60,
        Func<long>? progress = null, int capSeconds = 600, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var start = clock.GetUtcNow();
        var cap = start.AddSeconds(capSeconds);
        var deadline = start.AddSeconds(seconds);
        var last = progress?.Invoke();
        while (clock.GetUtcNow() < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(PollMilliseconds), clock);
            if (progress is null) continue;
            var current = progress();
            if (current == last) continue;
            last = current;
            var renewed = clock.GetUtcNow().AddSeconds(seconds);
            deadline = renewed < cap ? renewed : cap;
        }
        if (progress is null) throw new TimeoutException(evidence);
        var elapsed = (clock.GetUtcNow() - start).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        throw new TimeoutException($"{evidence} (quiet {seconds}s after {elapsed}s; progress {last})");
    }
}
