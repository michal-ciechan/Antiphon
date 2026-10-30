namespace Antiphon.Agents.Pty;

/// <summary>Observe the actual low-level attempt, without depending on a host or wire library.
/// True is an observable successful send, false a refusal/failure, null an unobservable attempt.
/// The callback must invoke send immediately; diagnostic IO belongs after it and outside its gate.</summary>
public delegate bool? PtyStopCallback(int pid, int signal, string path, Func<bool?> send);
