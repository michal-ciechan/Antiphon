namespace Antiphon.Agents.Pty.Tests;

public sealed class TempPowerShellScript : IDisposable
{
    public string Path { get; }

    public TempPowerShellScript(string content)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"antiphon-pty-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(Path, content);
    }

    public void Dispose()
    {
        try { File.Delete(Path); } catch { }
    }
}
