namespace Antiphon.Agents.Pty;

/// <summary>
/// Actual native exit evidence. A null code/signal is unknown; signal zero is observed no signal,
/// and a positive signal is an observed signal death even when the native code is zero.
/// </summary>
public sealed record PtyExitObservation(int? ExitCode, int? ExitSignal);
