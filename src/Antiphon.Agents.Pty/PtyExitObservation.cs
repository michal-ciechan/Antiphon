namespace Antiphon.Agents.Pty;

/// <summary>Actual native exit evidence. A null signal is unknown; zero is observed no signal.</summary>
public sealed record PtyExitObservation(int? ExitCode, int? ExitSignal);
