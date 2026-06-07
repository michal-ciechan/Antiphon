using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// <see cref="AntiphonWebAppFactory"/> variant whose filesystem dependencies are mocked so the
/// working-directory browse endpoint returns deterministic results independent of the host
/// machine's real drives and directories. Tests configure <see cref="Lister"/> and
/// <see cref="Drives"/>, then assert the exact suggestion contract over real HTTP.
///
/// The fakes are singletons shared with the (shared) host, so <see cref="ResetAsync"/> clears
/// them — along with the browse cache — between tests.
/// </summary>
public sealed class MockedFileSystemWebAppFactory : AntiphonWebAppFactory
{
    public FakeDirectoryLister Lister { get; } = new();
    public FakeDriveProvider Drives { get; } = new();

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.RemoveAll<IDirectoryLister>();
        services.AddSingleton<IDirectoryLister>(Lister);

        services.RemoveAll<IDriveProvider>();
        services.AddSingleton<IDriveProvider>(Drives);
    }

    public override async Task ResetAsync()
    {
        Lister.Reset();
        Drives.Reset();
        await base.ResetAsync(); // also clears the DirectoryBrowseService cache
    }
}

/// <summary>In-memory <see cref="IDriveProvider"/>. Set <see cref="Roots"/> per test.</summary>
public sealed class FakeDriveProvider : IDriveProvider
{
    public List<string> Roots { get; } = [];
    public IReadOnlyList<string> GetDriveRoots() => Roots.ToList();
    public void Reset() => Roots.Clear();
}

/// <summary>
/// In-memory <see cref="IDirectoryLister"/>. Register a directory's children via
/// <see cref="AddDirectory"/>; children are returned for <see cref="ListDirectories"/> and the
/// directory itself reports <see cref="DirectoryExists"/> = true. Paths are normalized
/// forward-slash form, matching what the real lister emits.
/// </summary>
public sealed class FakeDirectoryLister : IDirectoryLister
{
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers <paramref name="child"/> as a child of <paramref name="parent"/> and marks both as existing.</summary>
    public void AddDirectory(string parent, string child)
    {
        if (!_children.TryGetValue(parent, out var list))
            _children[parent] = list = [];
        if (!list.Contains(child, StringComparer.OrdinalIgnoreCase))
            list.Add(child);
        _existing.Add(parent);
        _existing.Add(child);
    }

    public bool DirectoryExists(string path) => _existing.Contains(path);

    public IReadOnlyList<string> ListDirectories(string parentDir) =>
        _children.TryGetValue(parentDir, out var list)
            ? list.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public void Reset()
    {
        _children.Clear();
        _existing.Clear();
    }
}
