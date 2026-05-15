namespace Antiphon.Tests.Agents;

/// <summary>
/// Disposable temp .bat helper for PTY-based adapter tests.
/// Mirrors TempBatch in Antiphon.Agents.Pty.Tests (kept local to avoid
/// a cross-test-project reference).
/// </summary>
internal sealed class PtyTempBatch : IDisposable
{
    public string Path { get; }

    public PtyTempBatch(string content)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"antiphon-e02-{Guid.NewGuid():N}.bat");
        File.WriteAllText(Path, content);
    }

    public void Dispose()
    {
        try { File.Delete(Path); } catch { }
    }
}
