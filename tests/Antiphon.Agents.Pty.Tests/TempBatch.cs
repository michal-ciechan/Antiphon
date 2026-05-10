namespace Antiphon.Agents.Pty.Tests;

public sealed class TempBatch : IDisposable
{
    public string Path { get; }

    public TempBatch(string content)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"antiphon-pty-{Guid.NewGuid():N}.bat");
        File.WriteAllText(Path, content);
    }

    public void Dispose()
    {
        try { File.Delete(Path); } catch { }
    }
}
