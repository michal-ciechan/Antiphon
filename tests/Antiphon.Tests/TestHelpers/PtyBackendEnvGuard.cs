using Antiphon.Agents.Pty;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Each PTY test assembly clears inherited backend input independently. Tests name any override;
/// the isolated default is modern on Windows with the shipped pair, UnixPty elsewhere (CARD-1022).
/// Process-environment mutators remain excluded by ConPtyEnvironmentIsolationGuardTests.
/// </summary>
public class PtyBackendEnvGuard
{
    /// <summary>The value this process was launched with, kept only so the log can name it.</summary>
    public static string? Inherited { get; private set; }

    public static void ClearInheritedPtyBackend()
    {
        Inherited = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        if (string.IsNullOrEmpty(Inherited))
            return;

        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, null);
        Console.WriteLine(
            $"[CARD-0045] cleared inherited {PtyBackendPolicy.EnvVar}='{Inherited}' — pty tests "
            + "declare their backend; the suite means the same thing whoever launched it.");
    }
}
