namespace Antiphon.Tests.Application;

internal sealed class DecisionTempWorkspace : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c407-s2a").FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}
