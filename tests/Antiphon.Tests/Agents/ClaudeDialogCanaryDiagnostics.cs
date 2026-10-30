using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;

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

    internal void Draft(string screen, bool complete, bool settled)
    {
        if (_directory is null) return;
        Append(new { utc = DateTimeOffset.UtcNow, phase = "submit-draft", complete, settled });
        Write("before-enter-screen.txt", screen);
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
