namespace Antiphon.Checkpoints;

public enum LaunchKind { NotStarted, Started, Unknown }

public sealed record LaunchOutcome(LaunchKind Kind, int Pid = 0, ProcessIdentity? Identity = null);
