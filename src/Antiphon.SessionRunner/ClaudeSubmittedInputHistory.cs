using System.Security.Cryptography;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Volatile factual custody, separate from terminal-seat release authority.</summary>
internal sealed class ClaudeSubmittedInputHistory
{
    private string? _emptyScreen;
    private DateTimeOffset _emptyAt;
    private long _emptyRevision;
    private bool _armed;
    private Capture? _capture;
    private sealed record Capture(Guid Id, string Body, string Binding, DateTime Generation,
        long Floor, long BodyRevision, long? SubmitRevision = null);

    public void Clear() { _emptyScreen = null; _armed = false; _capture = null; }

    public void ObserveEmpty(string screen, long revision, DateTimeOffset now, bool known)
    {
        if (!known || ClaudeDialogObservation.Observe(screen).State != ClaudeDialogState.Clear
            || !ClaudeScreen.TryReadComposer(screen, out var composer)
            || !ClaudeScreen.ComposerContentIsEmpty(composer))
        {
            _emptyScreen = null;
            _armed = false;
            return;
        }
        if (_emptyScreen is not null && revision == _emptyRevision
            && now - _emptyAt >= TimeSpan.FromMilliseconds(500)
            && ClaudeScreen.IsSettled(_emptyScreen, screen))
            _armed = true;
        else if (_emptyScreen is null || revision != _emptyRevision || !ClaudeScreen.IsSettled(_emptyScreen, screen))
        {
            _emptyScreen = screen;
            _emptyRevision = revision;
            _emptyAt = now;
            _armed = false;
        }
    }

    public void BeforeWrite(string input, long revision, DateTime? generation, string? binding,
        long floor, bool known)
    {
        if (known && _capture is { SubmitRevision: null } capture && input == "\r"
            && capture.BodyRevision + 1 == revision && capture.Generation == generation
            && capture.Binding == binding)
            _capture = capture with { SubmitRevision = revision };
        else
        {
            var body = PromptSubmissionMatch.Normalize(input);
            // One whole body and a separate Enter. Controls/chunks are never positive history.
            var paste = input.StartsWith("\u001b[200~", StringComparison.Ordinal)
                && input.EndsWith("\u001b[201~", StringComparison.Ordinal)
                ? input[6..^6] : input;
            _capture = known && _armed && _emptyRevision + 1 == revision
                && generation is { } accepted && binding is not null && floor >= 0
                && !paste.Any(c => char.IsControl(c) && c != '\n')
                && body.Length >= PromptSubmissionMatch.MinMatchChars
                ? new(Guid.NewGuid(), body, binding, accepted, floor, revision) : null;
        }
        _armed = false;
        _emptyScreen = null;
    }

    public SubmittedInputProof? Read(long revision, DateTime? generation, string? binding,
        RunnerTranscriptDto? transcript, bool known)
    {
        if (!known || _capture is not { SubmitRevision: { } submitted } capture
            || submitted != revision || capture.Generation != generation || capture.Binding != binding
            || transcript is null) return null;
        var prompts = transcript.Entries.Where(e => e.Sequence > capture.Floor
            && e.Kind == TranscriptKinds.UserPrompt).ToArray();
        if (prompts.Length != 1 || string.IsNullOrEmpty(prompts[0].Uuid)
            || !PromptSubmissionMatch.IsConfirmedBy(capture.Body, prompts[0].Text)
            || !PromptSubmissionMatch.IsCompleteIn(capture.Body, prompts[0].Text)) return null;
        return new(capture.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capture.Binding))),
            capture.Floor, prompts[0].Sequence, prompts[0].Uuid!, submitted);
    }
}
