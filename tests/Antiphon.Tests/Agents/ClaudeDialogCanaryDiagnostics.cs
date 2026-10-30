using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

/// <summary>Opt-in local evidence for the live canary; no input or admission authority.</summary>
internal sealed class ClaudeDialogCanaryDiagnostics
{
    private readonly string? _directory;
    private readonly string _sessionRoot;
    private readonly string _cwd;
    private readonly Guid _sessionId;
    private string? _previousState;
    private int _capture;
    internal bool ControlledSubmission => _directory is not null
        && Environment.GetEnvironmentVariable("ANTIPHON_C680_DEBUG_SUBMIT") == "1";

    internal ClaudeDialogCanaryDiagnostics(string? root, string sessionRoot, string cwd,
        Guid sessionId, string version, string model, string tool)
    {
        _sessionRoot = sessionRoot;
        _cwd = cwd;
        _sessionId = sessionId;
        if (string.IsNullOrWhiteSpace(root)) return;
        _directory = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(root),
            $"{model}-{tool}-{sessionId:N}")).FullName;
        Write("launch.json", JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, version, model, tool, sessionId, cwd,
            backend = OperatingSystem.IsWindows() ? "ModernConPty" : "UnixPty"
        }));
    }

    internal void Composer(string screen, bool empty, bool settled)
    {
        if (_directory is null) return;
        Append(new { utc = DateTimeOffset.UtcNow, phase = "composer", empty, settled });
        Write("composer-screen.txt", screen);
    }

    internal async Task SubmitAsync(DirectSessionRunnerClient runner, string probe, CancellationToken ct)
    {
        // Explicit fixture input to this owned child, separate from the watchdog under test.
        // The normal canary remains unchanged unless the diagnostic control is requested.
        var initial = await runner.GetSnapshotAsync(_sessionId, ct);
        if (ClaudeDialogObservation.Observe(initial.RenderedScreen).State != ClaudeDialogState.Clear)
            throw new InvalidOperationException("Diagnostic submission requires a clear initial screen.");
        await runner.SendInputAsync(_sessionId, PtyInputEncoding.PasteStart + probe + PtyInputEncoding.PasteEnd, ct);
        string? previous = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500, ct);
            var screen = (await runner.GetSnapshotAsync(_sessionId, ct)).RenderedScreen;
            // 2.1.312's manual-mode hint is not in the product composer allowlist. This
            // test-only oracle accepts exactly that footer on the known, complete draft.
            var fixtureScreen = screen.Replace("⏸ manual mode on", "? for shortcuts", StringComparison.Ordinal);
            var complete = ClaudeScreen.TryReadComposer(fixtureScreen, out var draft)
                && PromptSubmissionMatch.IsCompleteIn(probe, draft);
            Append(new { utc = DateTimeOffset.UtcNow, phase = "controlled-submit", complete,
                settled = previous is not null && ClaudeScreen.IsSettled(previous, screen) });
            Write("before-enter-screen.txt", screen);
            if (complete && previous is not null && ClaudeScreen.IsSettled(previous, screen))
            {
                await runner.SendInputAsync(_sessionId, "\r", ct);
                return;
            }
            previous = complete ? screen : null;
        }
        throw new TimeoutException("Diagnostic fixture did not observe the complete settled draft.");
    }

    internal async Task CaptureAfterQuestionDismissalAsync(DirectSessionRunnerClient runner,
        string probe, CancellationToken ct)
    {
        var before = await runner.GetSnapshotAsync(_sessionId, ct);
        if (before.Dialog?.State != ProviderDialogState.Blocked
            || !before.RenderedScreen.Contains("Blue", StringComparison.Ordinal)
            || !before.RenderedScreen.Contains("Green", StringComparison.Ordinal)) return;
        foreach (var name in new[] { "last-screen.txt", "normalized-transcript.json", "transcript.jsonl" })
        {
            var path = Path.Combine(_directory!, name);
            if (File.Exists(path)) File.Copy(path, Path.Combine(_directory!, "open-" + name));
        }
        Append(new { utc = DateTimeOffset.UtcNow, phase = "explicit-fixture-dismissal", input = "Escape" });
        // Cancel only this fixture's harmless color question, after preserving its failed wait.
        // This is explicit test teardown, never a watchdog response or a permission grant.
        await runner.SendInputAsync(_sessionId, "\u001b", ct);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500, ct);
            var snapshot = await runner.GetSnapshotAsync(_sessionId, ct);
            var transcript = await runner.GetTranscriptAsync(_sessionId, ct);
            var called = transcript.Entries.Any(e => e.Kind == TranscriptKinds.ToolCall && e.ToolName == "AskUserQuestion");
            Observe(snapshot, transcript, snapshot.Dialog?.State == ProviderDialogState.Blocked,
                transcript.Entries.Any(e => e.Kind == TranscriptKinds.UserPrompt && PromptSubmissionMatch.IsCompleteIn(probe, e.Text)), called);
            if (called) return;
        }
    }

    internal void Observe(SessionRunnerSnapshotDto snapshot, SessionRunnerTranscriptDto transcript,
        bool blocked, bool submitted, bool called)
    {
        if (_directory is null) return;
        var screenObservation = ClaudeDialogObservation.Observe(snapshot.RenderedScreen);
        var tools = transcript.Entries.Where(e => e.Kind == TranscriptKinds.ToolCall).Select(e => e.ToolName).ToArray();
        var state = JsonSerializer.Serialize(new
        {
            blocked, submitted, called, observer = snapshot.Dialog,
            screenState = screenObservation.State.ToString(), screenKind = screenObservation.Kind.ToString(),
            composer = ClaudeScreen.TryReadComposer(snapshot.RenderedScreen, out _),
            entries = transcript.Entries.Count, tools
        });
        Append(new { utc = DateTimeOffset.UtcNow, phase = "dialog", blocked, submitted, called,
            screenState = screenObservation.State.ToString(), screenKind = screenObservation.Kind.ToString(),
            observerState = snapshot.Dialog?.State.ToString(), entries = transcript.Entries.Count, tools });
        Write("last-screen.txt", snapshot.RenderedScreen);
        Write("normalized-transcript.json", JsonSerializer.Serialize(transcript));
        var sidecar = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(Path.Combine(_sessionRoot, "sessions"), _sessionId));
        // Only the exact test-owned conversation, never an operator transcript discovered by recency.
        if (sidecar?.SessionId == _sessionId && sidecar.Cwd == _cwd
            && sidecar.TranscriptPath is { } source
            && Path.GetFileNameWithoutExtension(source).Equals(_sessionId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            Write("transcript.jsonl", reader.ReadToEnd());
        }
        if (state == _previousState) return;
        _previousState = state;
        var capture = (++_capture).ToString("D3");
        Write($"{capture}-state.json", state);
        Write($"{capture}-screen.txt", snapshot.RenderedScreen);
    }

    private void Append(object row) => File.AppendAllText(Path.Combine(_directory!, "observations.jsonl"),
        Redact(JsonSerializer.Serialize(row)) + "\n");

    private void Write(string name, string value) => File.WriteAllText(Path.Combine(_directory!, name), Redact(value));

    private static string Redact(string value)
    {
        value = Regex.Replace(value, @"\b(?:sk-ant-|sk-proj-|ghp_|gho_|github_pat_)[A-Za-z0-9_-]+", "[REDACTED]");
        return Regex.Replace(value, @"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*", "Bearer [REDACTED]");
    }
}
