namespace Antiphon.Agents.Pty;

/// <summary>Low-level successful send, failed send, or unobservable attempt. No host dependency.</summary>
public delegate bool? PtyStopCallback(int pid, int signal, string path, Func<bool?> send);
