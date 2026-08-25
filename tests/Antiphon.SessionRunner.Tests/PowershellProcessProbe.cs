namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0187: FakeHerdrServer's default <c>shell_pid</c> (4242) is not a real process, so the
/// pane-shell check needs a name without <see cref="System.Diagnostics.Process.GetProcessById"/>.
/// </summary>
internal sealed class PowershellProcessProbe : IProcessLivenessProbe
{
    public bool IsAlive(int pid, DateTime startedAt) => true;

    public string? TryGetProcessName(int pid) => "powershell";
}

internal sealed class NamedProcessProbe(string? name) : IProcessLivenessProbe
{
    public bool IsAlive(int pid, DateTime startedAt) => true;

    public string? TryGetProcessName(int pid) => name;
}
