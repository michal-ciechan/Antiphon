using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1061: the native Windows junction DirectoryLink makes in process (unit 35 of the 2026-11-22 plan; until then mklink /J in
/// an owned pwsh child). The junction command's process-ownership row, C1061_Junction_command_joins_live_root_and_pipe_holders,
/// went with the child: nothing starts, so there is no root, pipe holder or job to join.
/// </summary>
[Category("Integration"), Category("Slow")]
public sealed class DirectoryLinkFixtureWindowsTests
{
    [Test]
    public async Task C1061_Junction_fixture_creates_moves_and_disposes_owned_link()
    {
        using var scratch = new NativeJunctionScratch();
        var target = Directory.CreateDirectory(Path.Combine(scratch.Root, "target caf\u00e9 space")).FullName;
        var sentinel = Path.Combine(target, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "target survives");

        var linkPath = Path.Combine(scratch.Root, "sync caf\u00e9 link");
        var link = await scratch.CreateAsync(linkPath, target);
        link.ShouldNotBeNull();
        AssertJunction(linkPath, target, "target survives");
        var moved = Path.Combine(scratch.Root, "moved caf\u00e9 link");
        scratch.ExpectAbsent(moved);
        try { link.MoveTo(moved); AssertJunction(moved, target, "target survives"); }
        finally { link.Dispose(); }
        DirectoryLink.IsLink(linkPath).ShouldBeFalse();
        DirectoryLink.IsLink(moved).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("target survives");

        var ordinary = Directory.CreateDirectory(Path.Combine(scratch.Root, "ordinary")).FullName;
        await File.WriteAllTextAsync(Path.Combine(ordinary, "own.txt"), "ordinary survives");
        DirectoryLink.TryCreate(ordinary, target).ShouldBeNull();
        File.ReadAllText(Path.Combine(ordinary, "own.txt")).ShouldBe("ordinary survives");
        DirectoryLink.IsLink(ordinary).ShouldBeFalse();

        var bodyPath = Path.Combine(scratch.Root, "body failure link");
        var bodyError = new InvalidOperationException("intentional body sentinel");
        var actual = await CaptureAsync(Task.Run(async () =>
        {
            using var owned = await scratch.CreateAsync(bodyPath, target);
            owned.ShouldNotBeNull();
            AssertJunction(bodyPath, target, "target survives");
            throw bodyError;
        }));
        ReferenceEquals(actual, bodyError).ShouldBeTrue();
        DirectoryLink.IsLink(bodyPath).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("target survives");
    }

    // A junction (a mount point, which needs no privilege), not a symbolic link: .NET reads its target back and the
    // attribute is a reparse point.
    private static void AssertJunction(string path, string target, string contents)
    {
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
        new DirectoryInfo(path).LinkTarget.ShouldBe(target);
        File.ReadAllText(Path.Combine(path, "sentinel.txt")).ShouldBe(contents);
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try { await task; return null; }
        catch (Exception error) { return error; }
    }
}

/// <summary>Unique NTFS scratch; the links it made are removed before the directory.</summary>
internal sealed class NativeJunctionScratch : IDisposable
{
    internal string Root { get; }
    private readonly HashSet<string> _createdEntries = [];
    internal NativeJunctionScratch()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows qualification required.");
        Root = Directory.CreateTempSubdirectory("c1061-junction-").FullName;
        NativeHardLink.RequireNtfs(Root);
    }
    internal void ExpectAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || DirectoryLink.IsLink(path))
            throw new IOException("Fixture link name must be absent.");
        _createdEntries.Add(path);
    }
    internal Task<DirectoryLink?> CreateAsync(string path, string target)
    {
        ExpectAbsent(path);
        return Task.FromResult(DirectoryLink.TryCreate(path, target));
    }
    internal void RemoveCreatedLink(string path)
    {
        if (!_createdEntries.Contains(path)) throw new InvalidOperationException("Not a fixture-owned link name.");
        if (DirectoryLink.IsLink(path)) Directory.Delete(path, recursive: false);
    }
    public void Dispose()
    {
        foreach (var path in _createdEntries) RemoveCreatedLink(path);
        Directory.Delete(Root, true);
    }
}
