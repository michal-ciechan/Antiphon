using System.Diagnostics;

namespace Antiphon.Agents.Pty;

public enum ClaudeReadinessOutcome { Ready, NotAnswerable, TrustFailed, EffortFailed, ProbeFailed, Exited }
public sealed record ClaudeReadinessResult(ClaudeReadinessOutcome Outcome, string Detail, int Writes = 0,
    ClaudeTrustDialogLayout TrustLayout = ClaudeTrustDialogLayout.Unknown)
{
    public bool Ready => Outcome is ClaudeReadinessOutcome.Ready or ClaudeReadinessOutcome.NotAnswerable;
}

public sealed record ClaudeReadinessOptions(ComposerProbeOptions Probe, TimeSpan TrustBudget,
    TimeSpan EffortBudget, TimeSpan DisabledProbeBudget);

/// <summary>One post-floor deadline and token-write allowance across every startup modal.</summary>
public static class ClaudeStartupReadiness
{
    public static async Task<ClaudeReadinessResult> RunAsync(
        string probeToken, Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write, ClaudeEffortIntent intent,
        ClaudeReadinessOptions options, Action<string>? log, Task exited, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var enabled = options.Probe.Timeout > TimeSpan.Zero;
        var budget = enabled ? options.Probe.Timeout : options.DisabledProbeBudget;
        var writes = 0;
        var active = ClaudeReadinessOutcome.ProbeFailed;
        var detail = "startup deadline exhausted";
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(budget > TimeSpan.Zero ? budget : TimeSpan.FromMilliseconds(1));
        var token = bounded.Token;
        // Await/own the exit observer; do not leave a continuation retaining the adapter indefinitely.
        async Task WatchExitAsync()
        {
            try { await exited.WaitAsync(token); await bounded.CancelAsync(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        var observer = WatchExitAsync();
        try
        {
            while (clock.Elapsed < budget)
            {
                token.ThrowIfCancellationRequested();
                var screen = await snapshotScreen(token);
                var prompt = ClaudeBlockingPromptDetector.Detect(screen);
                if (prompt?.Kind == ClaudeBlockingPromptKind.EffortChoice)
                {
                    active = ClaudeReadinessOutcome.EffortFailed;
                    var menu = ClaudeEffortPrompt.Parse(screen)!;
                    var selected = menu.Select(intent);
                    var enters = 0;
                    string EffortDetail() => $"requested={(intent.Invalid ? "invalid" : intent.Value ?? "absent (Keep option)")}; current={menu.Current}; suggested={menu.Suggested}; selected={selected}; Enter={enters}; inspect the effort picker and relaunch with a supported explicit effort";
                    detail = EffortDetail();
                    async Task EffortWrite(string key, CancellationToken keyToken)
                    {
                        await write(key, keyToken);
                        if (key == "\r") enters++;
                        detail = EffortDetail();
                    }
                    var remaining = budget - clock.Elapsed;
                    var result = await ClaudeEffortPrompt.ResolveAsync(snapshotScreen, EffortWrite, intent,
                        remaining < options.EffortBudget ? remaining : options.EffortBudget, token);
                    detail = result.Detail;
                    log?.Invoke(detail);
                    if (!result.Cleared) return new(active, detail, writes);
                    continue;
                }
                if (prompt?.Kind == ClaudeBlockingPromptKind.TrustFolder)
                {
                    active = ClaudeReadinessOutcome.TrustFailed;
                    var resolution = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
                        snapshotScreen, write, options.TrustBudget, token);
                    detail = resolution.Detail ?? "trust gate deadline exhausted";
                    log?.Invoke(detail);
                    if (resolution.Outcome != ClaudeStartupBlockOutcome.TrustCleared)
                        return new(active, detail, writes, prompt.Layout);
                    continue;
                }
                if (prompt is not null)
                {
                    log?.Invoke($"Skipping input probe: {prompt.Kind} is not auto-answerable.");
                    return new(ClaudeReadinessOutcome.NotAnswerable, "unrelated modal; no input", writes);
                }
                if (!enabled) return new(ClaudeReadinessOutcome.Ready, "probe disabled; startup gate cleared", writes);
                active = ClaudeReadinessOutcome.ProbeFailed;
                if (writes >= options.Probe.MaxWrites) return new(active, "cumulative token-write limit exhausted", writes);
                var probe = await ComposerInputProbe.RunAsync(probeToken, snapshotScreen, write,
                    options.Probe with { Timeout = budget - clock.Elapsed, MaxWrites = options.Probe.MaxWrites - writes },
                    log, ClaudeBlockingPromptDetector.IsBlocked, token);
                writes += probe.Writes;
                if (probe.Outcome == ComposerProbeOutcome.InterruptedByModal)
                    continue; // Discard all pre-modal proof; the next probe must render and clear anew.
                return new(probe.Responsive ? ClaudeReadinessOutcome.Ready : active, probe.Outcome.ToString(), writes);
            }
            return new(active, detail, writes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(exited.IsCompleted ? ClaudeReadinessOutcome.Exited : active,
                detail + "; startup deadline or process exit", writes);
        }
        finally
        {
            await bounded.CancelAsync();
            await observer;
        }
    }
}
