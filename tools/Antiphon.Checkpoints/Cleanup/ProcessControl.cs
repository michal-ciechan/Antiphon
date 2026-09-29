namespace Antiphon.Checkpoints;

public interface IProcessControl
{
    bool StopAndWait(ProcessIdentity identity, TimeSpan timeout);
}

public sealed class ProcessControl : IProcessControl
{
    public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout)
    {
        using var process = Process.GetProcessById(identity.Pid);
        var current = new ProcessIdentityProbe().Observe(identity);
        if (current.Verdict != ProcessVerdict.AliveSame) return false;
        process.Kill(entireProcessTree: true);
        return process.WaitForExit((int)timeout.TotalMilliseconds);
    }
}
