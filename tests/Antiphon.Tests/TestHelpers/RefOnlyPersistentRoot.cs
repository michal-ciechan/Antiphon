namespace Antiphon.Tests.TestHelpers;

/// <summary>Fixture-owned report/artifact storage outside both source roots and TEMP.</summary>
internal sealed class RefOnlyPersistentRoot : IDisposable
{
    public string Path { get; }
    private readonly string _owner = Guid.NewGuid().ToString("N");
    public RefOnlyPersistentRoot()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(System.IO.Path.Combine(checkout.FullName, ".git"))
            && !Directory.Exists(System.IO.Path.Combine(checkout.FullName, ".git"))) checkout = checkout.Parent;
        if (checkout?.Parent is null) throw new InvalidOperationException("fixture checkout not found");
        Path = System.IO.Path.Combine(checkout.Parent.FullName, ".antiphon-test-evidence", "c0824-s2", _owner);
        if (Directory.Exists(Path)) throw new InvalidOperationException("fixture root already exists");
        Directory.CreateDirectory(Path);
        File.WriteAllText(System.IO.Path.Combine(Path, ".fixture-owner"), _owner);
    }
    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;
        if (File.ReadAllText(System.IO.Path.Combine(Path, ".fixture-owner")) != _owner
            || new DirectoryInfo(Path).Name != _owner) throw new IOException("fixture ownership changed");
        Directory.Delete(Path, recursive: true);
    }
}
