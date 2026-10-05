namespace Antiphon.SessionRunner;

internal enum TerminalSeatDeliveryState { Missing, Prepared, Submitted, Invalid }

// Volatile metadata only: neither the composer text nor native paths are retained.
internal sealed record TerminalSeatDeliveryCapture(
    Guid CaptureId, Guid RuntimeEpoch, Guid RunnerStoreId, object Session, Guid SessionId,
    DateTime AcceptedStartedAt, string BindingIdentity, long PromptFloorRevision);

internal sealed record TerminalSeatDeliverySnapshot(
    TerminalSeatDeliveryState State, TerminalSeatDeliveryCapture? Pending,
    TerminalSeatDeliveryCapture? Submitted)
{
    internal TerminalSeatDeliveryCapture? Current => Pending ?? Submitted;
}

/// <summary>One composer submission, serialized by the owning session's monitor.</summary>
internal sealed class TerminalSeatDeliveryEvidence
{
    internal const int MaximumCharacters = 1024 * 1024;
    private TerminalSeatDeliveryState _state;
    private TerminalSeatDeliveryCapture? _pending;
    private TerminalSeatDeliveryCapture? _submitted;
    private bool _open;
    private bool _paste;
    private int _delimiter;
    private int _characters;

    internal TerminalSeatDeliverySnapshot Snapshot => new(_state, _pending, _submitted);

    internal void Clear()
    {
        _state = TerminalSeatDeliveryState.Missing;
        _pending = _submitted = null;
        _open = _paste = false;
        _delimiter = _characters = 0;
    }

    // Returns true only for the first bytes of a new submission. Even an unavailable
    // capture owns this submission; later chunks/Enter cannot invent a newer floor.
    internal bool BeginWrite(string input)
    {
        if (input.Length == 0) return false;
        if (input == "\r")
        {
            if (_open && (_paste || _delimiter != 0 || _characters == 0)) Invalidate();
            return false;
        }

        var first = !_open;
        if (first)
        {
            Clear();
            _open = true;
        }
        foreach (var c in input)
        {
            if (_state == TerminalSeatDeliveryState.Invalid) break;
            if (_delimiter != 0)
            {
                var expected = _paste ? "\u001b[201~" : "\u001b[200~";
                if (c != expected[_delimiter]) { Invalidate(); break; }
                if (++_delimiter == expected.Length) { _paste = !_paste; _delimiter = 0; }
            }
            else if (c == '\u001b') _delimiter = 1;
            else if ((char.IsControl(c) && c != '\n' && !(c == '\t' && _paste))
                     || ++_characters > MaximumCharacters)
                Invalidate();
        }
        return first;
    }

    internal void Prepare(TerminalSeatDeliveryCapture capture)
    {
        if (_state != TerminalSeatDeliveryState.Missing || !_open) return;
        _pending = capture;
        _state = TerminalSeatDeliveryState.Prepared;
    }

    internal void CompleteWrite(string input)
    {
        if (input != "\r") return;
        if (_state == TerminalSeatDeliveryState.Prepared)
        {
            _submitted = _pending;
            _pending = null;
            _state = TerminalSeatDeliveryState.Submitted;
        }
        _open = false;
    }

    internal void Invalidate()
    {
        _state = TerminalSeatDeliveryState.Invalid;
        _pending = _submitted = null;
    }
}
