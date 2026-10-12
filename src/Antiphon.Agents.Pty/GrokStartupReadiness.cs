namespace Antiphon.Agents.Pty;

public enum GrokStartupReason
{
    Ready, Unknown, StartingSession, SignIn, Trust, Working, ComposerUnavailable,
    Deadline, Exited, SnapshotFailure
}

public readonly record struct GrokStartupObservation(
    bool IsReady, GrokStartupReason Reason, string Region, bool McpVisible);

public sealed record GrokStartupSnapshot(
    string RenderedScreen, string? RawOutput, long Sequence, DateTime CapturedAt);

/// <summary>
/// Recognizes the captured Grok Build 1.0.41 dashboards at qualified sizes.
/// Windows ConPTY uses ASCII '&gt;' and Linux uses U+276F for the empty composer
/// marker. Width is inferred from the composer border in the rendered screen;
/// unqualified width/height pairs remain unknown (CARD-0861).
/// </summary>
public static class GrokStartupScreen
{
    public const string EnabledHint = "  Shift+Tab:mode  │  Ctrl+x:shortcuts";

    public static GrokStartupObservation Classify(string? screen, string? rawOutput = null)
    {
        var lines = (screen ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var mcp = (screen ?? "").Contains("MCP (", StringComparison.Ordinal);
        GrokStartupObservation No(GrokStartupReason reason) => new(false, reason, "", mcp);
        if (GrokSignInPromptDetector.IsVisibleOnScreen(screen)) return No(GrokStartupReason.SignIn);
        if (GrokTrustPromptDetector.IsVisibleOnScreen(screen)) return No(GrokStartupReason.Trust);
        // TerminalScreen returns exactly one row per terminal row, including blank rows.
        if (lines.Length is not (24 or 30 or 50 or 60)) return No(GrokStartupReason.Unknown);

        for (var top = lines.Length - 5; top >= 2; top--)
        {
            var upper = lines[top];
            var left = upper.IndexOf('╭');
            if (left < 0) continue;
            var right = upper.LastIndexOf('╮');
            if (right <= left + 2 || top + 4 >= lines.Length) continue;
            // Captured layouts have two cells of outer padding and put the composer
            // six rows from the bottom. A model/quota label may change the lower border.
            var cols = right + 3;
            if (left != 2 || top != lines.Length - 6 || !IsQualifiedSize(cols, lines.Length)
                || lines.Any(line => line.Length > cols) || upper.Length != right + 1
                || upper[..left].Trim().Length != 0
                || upper[(left + 1)..right].Any(c => c != '─')) continue;

            var input = lines[top + 1];
            var bottom = lines[top + 2];
            if (input.Length != right + 1 || input[left] != '│' || input[right] != '│'
                || bottom.Length != right + 1 || bottom[left] != '╰' || bottom[right] != '╯')
                return No(GrokStartupReason.ComposerUnavailable);
            if (input[(left + 1)..right].Trim() is not (">" or "\u276f"))
                return No(GrokStartupReason.ComposerUnavailable);
            if (lines[top - 2].Length > left
                && lines[top - 2].Substring(left, Math.Min(right - left + 1, lines[top - 2].Length - left)).Trim().Length != 0)
            {
                return No(lines[top - 2].Contains("Starting session", StringComparison.OrdinalIgnoreCase)
                    ? GrokStartupReason.StartingSession : GrokStartupReason.Working);
            }
            if (lines[top + 4] != EnabledHint)
                return No(lines[top + 4].Contains("Ctrl+c:cancel", StringComparison.Ordinal)
                    || lines[top + 4].Contains("Ctrl+;:queue", StringComparison.Ordinal)
                    ? GrokStartupReason.Working : GrokStartupReason.Unknown);
            if (lines[top - 1].Length > left
                && lines[top - 1].Substring(left, Math.Min(right - left + 1, lines[top - 1].Length - left)).Trim().Length != 0)
                return No(GrokStartupReason.Unknown);
            // The footer label varies by model and quota; retain only its fixed border.
            var region = $"{left}:{right}:{top}\n{upper}\n{input}\n{bottom[left]}{bottom[right]}\n{EnabledHint}";
            return new(true, GrokStartupReason.Ready, region, mcp);
        }
        return No(GrokStartupReason.Unknown);
    }

    private static bool IsQualifiedSize(int cols, int rows) => (cols, rows) is
        (80, 24) or (100, 30) or (120, 30) or (160, 50) or (200, 60);
}

public sealed class GrokReadyTracker(TimeSpan settle)
{
    private string? _region;
    private TimeSpan _since;
    public int PositiveObservations { get; private set; }
    public bool McpEverSeen { get; private set; }
    public GrokStartupReason LastReason { get; private set; } = GrokStartupReason.Unknown;

    public bool Observe(GrokStartupObservation observation, TimeSpan elapsed)
    {
        McpEverSeen |= observation.McpVisible;
        LastReason = observation.Reason;
        if (!observation.IsReady)
        {
            Reset();
            LastReason = observation.Reason;
            return false;
        }
        if (_region != observation.Region)
        {
            _region = observation.Region;
            _since = elapsed;
            PositiveObservations = 1;
            return false;
        }
        PositiveObservations++;
        return PositiveObservations >= 2 && elapsed - _since >= (settle > TimeSpan.Zero ? settle : TimeSpan.Zero);
    }

    public void Reset()
    {
        _region = null;
        PositiveObservations = 0;
    }
}

public sealed class GrokReadyWaitOptions
{
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MinimumAgeRemaining { get; init; }
    public TimeSpan TrustSettle { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public Action<GrokStartupReason, GrokStartupReason, GrokStartupSnapshot?, TimeSpan, int, bool, bool>? OnFailure { get; init; }
    public Action<GrokStartupSnapshot>? OnSignIn { get; init; }
}

/// <summary>
/// One monotonic deadline covers reads, trust, settling and minimum process age.
/// A snapshot that has already completed is still classified when that budget
/// elapsed during the preceding poll, so a sign-in screen records its launch block.
/// A snapshot that is still pending is cancelled and is not awaited past the budget.
/// A snapshot that is not consumed has its fault observed before any completion test.
/// </summary>
public static class GrokReadyWait
{
    /// <summary>
    /// Test-only fault injection. Production leaves this unset. The regression
    /// test faults the snapshot after the observer is attached and before the
    /// completion test, which is the gap that used to skip both observers.
    /// </summary>
    internal static readonly AsyncLocal<Action<Task>?> SnapshotFaultGate = new();

    public static async Task<bool> WaitAsync(
        Func<CancellationToken, Task<GrokStartupSnapshot?>> snapshotAsync,
        GrokReadyWaitOptions options,
        Func<string, CancellationToken, Task>? writeAsync = null,
        Func<bool>? isExited = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotAsync);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        var time = options.TimeProvider;
        var started = time.GetTimestamp();
        var tracker = new GrokReadyTracker(options.Settle);
        GrokStartupSnapshot? last = null;
        var signInSeen = false;
        var trustWritten = false;
        TimeSpan? trustAt = null;
        bool Fail(GrokStartupReason outcome)
        {
            options.OnFailure?.Invoke(outcome, tracker.LastReason, last, time.GetElapsedTime(started),
                tracker.PositiveObservations, tracker.McpEverSeen, signInSeen);
            return false;
        }
        if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);
        if (options.MaxWait <= TimeSpan.Zero) return Fail(GrokStartupReason.Deadline);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);
            var elapsed = time.GetElapsedTime(started);
            var outerRemaining = options.MaxWait - elapsed;
            var trustExpired = false;
            var readBudget = outerRemaining;
            if (trustAt is { } trustStarted && options.TrustSettle > TimeSpan.Zero)
            {
                var trustRemaining = options.TrustSettle - (elapsed - trustStarted);
                if (trustRemaining <= TimeSpan.Zero) trustExpired = true;
                else if (trustRemaining < readBudget) readBudget = trustRemaining;
            }
            GrokStartupSnapshot? frame;
            if (outerRemaining <= TimeSpan.Zero || trustExpired)
            {
                // The poll woke after the budget. Classify a frame that is already complete.
                // A fault is observed before that test. Cancel a read that is still
                // pending; do not await it.
                if (!TryTakeCompleted(snapshotAsync, ct, out frame, out var faulted))
                    return Fail(faulted
                        ? GrokStartupReason.SnapshotFailure
                        : outerRemaining <= TimeSpan.Zero
                            ? GrokStartupReason.Deadline
                            : GrokStartupReason.Trust);
            }
            else
            {
                try
                {
                    frame = await Bounded(snapshotAsync, readBudget, time, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Fail(trustAt is not null && options.TrustSettle > TimeSpan.Zero
                        ? GrokStartupReason.Trust : GrokStartupReason.Deadline);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    return Fail(GrokStartupReason.SnapshotFailure);
                }
            }
            if (frame is not null) last = frame;
            ct.ThrowIfCancellationRequested();
            if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);
            elapsed = time.GetElapsedTime(started);

            var observation = GrokStartupScreen.Classify(frame?.RenderedScreen, frame?.RawOutput);
            if (observation.Reason == GrokStartupReason.SignIn)
            {
                signInSeen = true;
                tracker.Observe(observation, elapsed);
                if (frame is not null) options.OnSignIn?.Invoke(frame);
                return Fail(GrokStartupReason.SignIn);
            }
            if (elapsed >= options.MaxWait) return Fail(GrokStartupReason.Deadline);
            if (trustExpired && observation.Reason == GrokStartupReason.Trust)
                return Fail(GrokStartupReason.Trust);
            if (observation.Reason == GrokStartupReason.Trust)
            {
                tracker.Observe(observation, elapsed);
                if (!trustWritten && writeAsync is not null)
                {
                    try
                    {
                        await Bounded(token => writeAsync(GrokTrustPromptDetector.AffirmativeKey, token),
                            options.MaxWait - elapsed, time, ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        return Fail(GrokStartupReason.Deadline);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        return Fail(GrokStartupReason.Trust);
                    }
                    trustWritten = true;
                    trustAt = time.GetElapsedTime(started);
                }
                else if (trustAt is { } at && options.TrustSettle > TimeSpan.Zero
                    && elapsed - at >= options.TrustSettle)
                    return Fail(GrokStartupReason.Trust);
            }
            else
            {
                trustAt = null;
                if (tracker.Observe(observation, elapsed)
                    && elapsed >= options.MinimumAgeRemaining)
                {
                    ct.ThrowIfCancellationRequested();
                    if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);
                    if (time.GetElapsedTime(started) >= options.MaxWait) return Fail(GrokStartupReason.Deadline);
                    return true;
                }
            }
            var remaining = options.MaxWait - time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return Fail(GrokStartupReason.Deadline);
            var delay = options.PollInterval > TimeSpan.Zero ? options.PollInterval : TimeSpan.FromMilliseconds(50);
            await Task.Delay(delay < remaining ? delay : remaining, time, ct);
        }
    }

    private static bool TryTakeCompleted<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct,
        out T? value, out bool faulted)
    {
        using var gate = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var work = operation(gate.Token);
            // Before any completion test. OnlyOnFaulted still runs when the task
            // has already faulted, so a fault between the old IsFaulted and
            // IsCompleted reads cannot stay unobserved. A consumed success is
            // observed by GetResult. The test gate faults in that old gap.
            ObserveAbandonedSnapshot(work);
            SnapshotFaultGate.Value?.Invoke(work);
            if (work.IsCompletedSuccessfully)
            {
                value = work.GetAwaiter().GetResult();
                faulted = false;
                return true;
            }

            value = default;
            faulted = work.IsFaulted;
            return false;
        }
        finally
        {
            gate.Cancel();
        }
    }

    private static void ObserveAbandonedSnapshot(Task work) =>
        _ = work.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation,
        TimeSpan remaining, TimeProvider time, CancellationToken caller)
    {
        using var gate = CancellationTokenSource.CreateLinkedTokenSource(caller);
        var work = operation(gate.Token);
        var timeout = Task.Delay(remaining, time, gate.Token);
        if (await Task.WhenAny(work, timeout) == work)
        {
            await gate.CancelAsync();
            return await work;
        }
        await gate.CancelAsync();
        // A provider may ignore cancellation; do not wait past the startup deadline.
        _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        caller.ThrowIfCancellationRequested();
        throw new OperationCanceledException(gate.Token);
    }

    private static async Task<bool> Bounded(Func<CancellationToken, Task> operation,
        TimeSpan remaining, TimeProvider time, CancellationToken caller)
    {
        await Bounded(async token => { await operation(token); return true; }, remaining, time, caller);
        return true;
    }
}
