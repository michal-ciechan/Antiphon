namespace Antiphon.Agents.Pty;

public enum CodexStartupReason
{
    Ready,
    Unknown,
    Loading,
    McpBoot,
    McpIncomplete,
    Working,
    QueueMode,
    QueuedFollowUp,
    Trust,
    BlockingUpdate,
    Sandbox,
    SignIn,
}

public readonly record struct CodexStartupObservation(
    bool IsReadyCandidate,
    CodexStartupReason Reason,
    string NormalizedRegion,
    bool McpVisible)
{
    public bool IsReady => IsReadyCandidate;

    public static CodexStartupObservation Ready(string region, bool mcpVisible) =>
        new(true, CodexStartupReason.Ready, region, mcpVisible);

    public static CodexStartupObservation NotReady(
        CodexStartupReason reason, string region = "", bool mcpVisible = false) =>
        new(false, reason, region, mcpVisible);
}

public sealed record CodexStartupSnapshot(string RenderedScreen, string? RawOutput = null);

/// <summary>
/// CARD-0574 D-1: classify one current rendered Codex startup screen. Pure; no I/O.
/// </summary>
public static class CodexStartupScreen
{
    public const string HintAskAnything = "Ask Codex to do anything";
    public const string HintImproveDocs = "Improve documentation in @filename";
    public const string HintWriteTests = "Write tests for @filename";

    public static readonly string[] SupportedIdleHints =
    [
        HintAskAnything,
        HintImproveDocs,
        HintWriteTests,
    ];

    public static CodexStartupObservation Classify(string? renderedScreen, string? rawOutput = null)
    {
        if (rawOutput is not null && !VisiblePtyOutput.HasVisibleOutput(rawOutput)
            && string.IsNullOrWhiteSpace(renderedScreen))
        {
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown);
        }

        var lines = NormalizeLines(renderedScreen);
        if (lines.Length == 0)
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown);

        var text = string.Join('\n', lines);
        var mcpVisible = CodexMcpBoot.IsVisible(text);

        if (CodexTrustPromptDetector.IsVisibleOnCurrentScreen(text))
            return CodexStartupObservation.NotReady(CodexStartupReason.Trust, mcpVisible: mcpVisible);
        if (ContainsSignIn(text))
            return CodexStartupObservation.NotReady(CodexStartupReason.SignIn, mcpVisible: mcpVisible);
        if (ContainsSandboxOrInputDisabled(text))
            return CodexStartupObservation.NotReady(CodexStartupReason.Sandbox, mcpVisible: mcpVisible);
        if (ContainsBlockingUpdate(text))
            return CodexStartupObservation.NotReady(CodexStartupReason.BlockingUpdate, mcpVisible: mcpVisible);
        if (text.Contains("MCP startup incomplete", StringComparison.OrdinalIgnoreCase))
            return CodexStartupObservation.NotReady(CodexStartupReason.McpIncomplete, mcpVisible: mcpVisible);
        if (mcpVisible)
            return CodexStartupObservation.NotReady(CodexStartupReason.McpBoot, mcpVisible: true);
        if (CodexWorkingIndicator.IsVisible(text))
            return CodexStartupObservation.NotReady(CodexStartupReason.Working, mcpVisible: mcpVisible);
        if (text.Contains("tab to queue message", StringComparison.OrdinalIgnoreCase))
            return CodexStartupObservation.NotReady(CodexStartupReason.QueueMode, mcpVisible: mcpVisible);
        if (text.Contains("Queued follow-up inputs", StringComparison.OrdinalIgnoreCase))
            return CodexStartupObservation.NotReady(CodexStartupReason.QueuedFollowUp, mcpVisible: mcpVisible);

        if (!HasCodexBanner(lines))
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown, mcpVisible: mcpVisible);

        if (!TryReadSelectedModel(lines, out var modelValue, out var loading))
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown, mcpVisible: mcpVisible);
        if (loading)
            return CodexStartupObservation.NotReady(CodexStartupReason.Loading, mcpVisible: mcpVisible);

        if (!TryReadBottomComposer(lines, out var composerContent, out var footer, out var composerLine))
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown, mcpVisible: mcpVisible);

        if (!IsIdleComposer(composerContent))
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown, mcpVisible: mcpVisible);

        if (!IsModelEffortCwdFooter(footer))
            return CodexStartupObservation.NotReady(CodexStartupReason.Unknown, mcpVisible: mcpVisible);

        var region = NormalizeRegion(modelValue, composerLine, footer);
        return CodexStartupObservation.Ready(region, mcpVisible);
    }

    internal static string[] NormalizeLines(string? screen)
    {
        if (string.IsNullOrEmpty(screen))
            return [];

        var normalized = screen.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var raw = normalized.Split('\n');
        var lines = new string[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            lines[i] = raw[i].TrimEnd();
        return lines;
    }

    internal static string NormalizeRegion(string model, string composer, string footer) =>
        model.Trim() + "\n" + composer.Trim() + "\n" + footer.Trim();

    private static bool HasCodexBanner(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("OpenAI Codex", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool TryReadSelectedModel(string[] lines, out string modelValue, out bool loading)
    {
        modelValue = "";
        loading = false;
        foreach (var line in lines)
        {
            var stripped = StripBox(line);
            var idx = stripped.IndexOf("model:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var rest = stripped[(idx + "model:".Length)..].Trim();
            var cut = rest.IndexOf("/model", StringComparison.OrdinalIgnoreCase);
            if (cut >= 0)
                rest = rest[..cut].Trim();
            if (rest.Length == 0)
                return false;

            if (IsLoadingModel(rest))
            {
                loading = true;
                modelValue = rest;
                return true;
            }

            modelValue = rest;
            return true;
        }

        return false;
    }

    private static bool IsLoadingModel(string value)
    {
        if (value.Equals("loading", StringComparison.OrdinalIgnoreCase))
            return true;
        return value.StartsWith("loading", StringComparison.OrdinalIgnoreCase)
            && (value.Length == 7 || !char.IsLetterOrDigit(value[7]));
    }

    private static bool TryReadBottomComposer(
        string[] lines, out string composerContent, out string footer, out string composerLine)
    {
        composerContent = "";
        footer = "";
        composerLine = "";

        var end = lines.Length - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        if (end < 0)
            return false;

        footer = lines[end];
        var composerIdx = end - 1;
        if (composerIdx >= 0 && lines[composerIdx].Length == 0)
            composerIdx--;
        if (composerIdx < 0)
            return false;

        composerLine = lines[composerIdx];
        if (!TryParseComposer(composerLine, out _, out composerContent))
            return false;

        return true;
    }

    internal static bool TryParseComposer(string line, out string glyph, out string content)
    {
        glyph = "";
        content = "";
        var t = line.TrimStart();
        if (t.Length == 0)
            return false;
        var g = t[0];
        if (g is not ('>' or '›'))
            return false;
        if (t.Length > 1 && t[1] == '_')
            return false;
        if (t.Contains("OpenAI Codex", StringComparison.Ordinal))
            return false;

        glyph = g.ToString();
        content = t.Length == 1 ? "" : t[1..].Trim();
        return true;
    }

    internal static bool IsIdleComposer(string content)
    {
        if (content.Length == 0)
            return true;
        foreach (var hint in SupportedIdleHints)
        {
            if (content.Equals(hint, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static bool IsModelEffortCwdFooter(string line)
    {
        var t = line.Trim();
        if (t.Length == 0)
            return false;
        if (t.Contains("context left", StringComparison.OrdinalIgnoreCase))
            return false;
        if (t.Contains("tab to queue", StringComparison.OrdinalIgnoreCase))
            return false;

        var dot = t.IndexOf('·');
        if (dot <= 0)
            return false;
        var left = t[..dot].Trim();
        var right = t[(dot + 1)..].Trim();
        if (right.Length == 0)
            return false;
        var parts = left.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2;
    }

    private static bool ContainsSignIn(string text) =>
        text.Contains("Please sign in", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Sign in to continue", StringComparison.OrdinalIgnoreCase)
        || text.Contains("sign in to", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSandboxOrInputDisabled(string text) =>
        text.Contains("sandbox setup", StringComparison.OrdinalIgnoreCase)
        || text.Contains("input disabled", StringComparison.OrdinalIgnoreCase)
        || text.Contains("input is disabled", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsBlockingUpdate(string text) =>
        text.Contains("Press enter to continue", StringComparison.OrdinalIgnoreCase);

    private static string StripBox(string line)
    {
        var t = line.Trim();
        t = t.Trim('│', '|', '┃', '╭', '╮', '╰', '╯', '─', '━');
        return t.Trim();
    }
}

/// <summary>
/// CARD-0574 D-2: elapsed-time settle tracker. No I/O; callers pass elapsed ticks.
/// </summary>
public sealed class CodexReadyTracker
{
    private readonly TimeSpan _settle;
    private TimeSpan? _candidateSince;
    private string? _region;
    private int _positiveObservations;

    public CodexReadyTracker(TimeSpan settle)
    {
        _settle = settle < TimeSpan.Zero ? TimeSpan.Zero : settle;
    }

    public CodexStartupReason LastReason { get; private set; } = CodexStartupReason.Unknown;
    public bool McpEverSeen { get; private set; }

    public bool Observe(CodexStartupObservation observation, TimeSpan elapsed)
    {
        if (observation.McpVisible)
            McpEverSeen = true;
        LastReason = observation.Reason;

        if (!observation.IsReadyCandidate)
        {
            _candidateSince = null;
            _region = null;
            _positiveObservations = 0;
            return false;
        }

        if (_region is not null && !string.Equals(_region, observation.NormalizedRegion, StringComparison.Ordinal))
        {
            _candidateSince = elapsed;
            _region = observation.NormalizedRegion;
            _positiveObservations = 1;
            LastReason = CodexStartupReason.Unknown;
            return false;
        }

        if (_candidateSince is null)
        {
            _candidateSince = elapsed;
            _region = observation.NormalizedRegion;
            _positiveObservations = 1;
            return false;
        }

        _positiveObservations++;
        if (_positiveObservations < 2)
            return false;
        return elapsed - _candidateSince.Value >= _settle;
    }

    public void Reset()
    {
        _candidateSince = null;
        _region = null;
        _positiveObservations = 0;
        LastReason = CodexStartupReason.Unknown;
    }
}

public sealed class CodexReadyWaitOptions
{
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan BootStatusThreshold { get; init; } = TimeSpan.FromSeconds(10);
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public Action<string>? OnDiagnostic { get; init; }
    public Action<int>? OnBootStatusThreshold { get; init; }
}

/// <summary>
/// CARD-0574: one bounded positive-ready wait. Snapshot, trust write and exit delegates only.
/// </summary>
public static class CodexReadyWait
{
    public static async Task<bool> WaitAsync(
        Func<CancellationToken, Task<CodexStartupSnapshot?>> snapshotAsync,
        CodexReadyWaitOptions options,
        Func<string, CancellationToken, Task>? writeAsync = null,
        Func<bool>? isExited = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotAsync);
        ArgumentNullException.ThrowIfNull(options);

        var time = options.TimeProvider ?? TimeProvider.System;
        ct.ThrowIfCancellationRequested();
        if (isExited?.Invoke() == true)
            return false;
        if (options.MaxWait <= TimeSpan.Zero)
        {
            options.OnDiagnostic?.Invoke("codex-startup not-ready reason=deadline elapsedMs=0 mcpSeen=false");
            return false;
        }

        var started = time.GetUtcNow();
        var deadline = started + options.MaxWait;
        var tracker = new CodexReadyTracker(options.Settle);
        var acceptedTrust = false;
        var warnedBoot = false;
        var poll = options.PollInterval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(50)
            : options.PollInterval;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (isExited?.Invoke() == true)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            var now = time.GetUtcNow();
            var remaining = deadline - now;
            if (remaining <= TimeSpan.Zero)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            CodexStartupSnapshot? snapshot;
            try
            {
                snapshot = await ReadBoundedAsync(snapshotAsync, time, remaining, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            ct.ThrowIfCancellationRequested();
            if (isExited?.Invoke() == true)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            now = time.GetUtcNow();
            if (now >= deadline)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            if (snapshot is null)
            {
                tracker.Observe(CodexStartupObservation.NotReady(CodexStartupReason.Unknown), now - started);
            }
            else
            {
                var screen = snapshot.RenderedScreen ?? "";
                if (!acceptedTrust && CodexTrustPromptDetector.IsVisibleOnCurrentScreen(screen))
                {
                    if (writeAsync is null)
                    {
                        tracker.Observe(
                            CodexStartupObservation.NotReady(CodexStartupReason.Trust),
                            now - started);
                    }
                    else
                    {
                        remaining = deadline - time.GetUtcNow();
                        if (remaining <= TimeSpan.Zero)
                        {
                            LogFinal(options, tracker, started, time, success: false);
                            return false;
                        }

                        try
                        {
                            await WriteBoundedAsync(writeAsync, "\r", time, remaining, ct);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            LogFinal(options, tracker, started, time, success: false);
                            return false;
                        }

                        acceptedTrust = true;
                        tracker.Reset();
                        continue;
                    }
                }
                else
                {
                    var observation = CodexStartupScreen.Classify(screen, snapshot.RawOutput);
                    var elapsed = time.GetUtcNow() - started;
                    if (observation.McpVisible || tracker.McpEverSeen)
                    {
                        if (!warnedBoot
                            && options.BootStatusThreshold > TimeSpan.Zero
                            && elapsed >= options.BootStatusThreshold
                            && tracker.McpEverSeen)
                        {
                            warnedBoot = true;
                            options.OnBootStatusThreshold?.Invoke((int)options.BootStatusThreshold.TotalMilliseconds);
                        }
                    }

                    if (tracker.Observe(observation, elapsed))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (isExited?.Invoke() == true)
                        {
                            LogFinal(options, tracker, started, time, success: false);
                            return false;
                        }

                        if (time.GetUtcNow() >= deadline)
                        {
                            LogFinal(options, tracker, started, time, success: false);
                            return false;
                        }

                        options.OnDiagnostic?.Invoke(
                            $"codex-startup ready reason=positive-settle elapsedMs={(int)(time.GetUtcNow() - started).TotalMilliseconds} mcpSeen={tracker.McpEverSeen}");
                        return true;
                    }
                }
            }

            now = time.GetUtcNow();
            remaining = deadline - now;
            if (remaining <= TimeSpan.Zero)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }

            var delay = poll < remaining ? poll : remaining;
            try
            {
                await Task.Delay(delay, time, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LogFinal(options, tracker, started, time, success: false);
                return false;
            }
        }
    }

    private static void LogFinal(
        CodexReadyWaitOptions options,
        CodexReadyTracker tracker,
        DateTimeOffset started,
        TimeProvider time,
        bool success)
    {
        var elapsedMs = (int)(time.GetUtcNow() - started).TotalMilliseconds;
        if (success)
        {
            options.OnDiagnostic?.Invoke(
                $"codex-startup ready reason=positive-settle elapsedMs={elapsedMs} mcpSeen={tracker.McpEverSeen}");
            return;
        }

        options.OnDiagnostic?.Invoke(
            $"codex-startup not-ready reason={tracker.LastReason} elapsedMs={elapsedMs} mcpSeen={tracker.McpEverSeen}");
    }

    private static async Task<CodexStartupSnapshot?> ReadBoundedAsync(
        Func<CancellationToken, Task<CodexStartupSnapshot?>> snapshotAsync,
        TimeProvider time,
        TimeSpan remaining,
        CancellationToken caller)
    {
        using var gate = CancellationTokenSource.CreateLinkedTokenSource(caller);
        var read = snapshotAsync(gate.Token);
        var timeout = Task.Delay(remaining, time, gate.Token);
        var done = await Task.WhenAny(read, timeout);
        if (done == read)
        {
            await gate.CancelAsync();
            return await read;
        }

        await gate.CancelAsync();
        try { await read; }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { }
        caller.ThrowIfCancellationRequested();
        throw new OperationCanceledException(gate.Token);
    }

    private static async Task WriteBoundedAsync(
        Func<string, CancellationToken, Task> writeAsync,
        string input,
        TimeProvider time,
        TimeSpan remaining,
        CancellationToken caller)
    {
        using var gate = CancellationTokenSource.CreateLinkedTokenSource(caller);
        var write = writeAsync(input, gate.Token);
        var timeout = Task.Delay(remaining, time, gate.Token);
        var done = await Task.WhenAny(write, timeout);
        if (done == write)
        {
            await gate.CancelAsync();
            await write;
            return;
        }

        await gate.CancelAsync();
        try { await write; }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { }
        caller.ThrowIfCancellationRequested();
        throw new OperationCanceledException(gate.Token);
    }
}
