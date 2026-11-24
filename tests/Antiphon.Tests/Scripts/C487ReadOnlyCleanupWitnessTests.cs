using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1687 D-1: the forced-Windows read-only cleanup witness, so a change to how the owned-results cleanup lists
/// directories cannot silently break it again. Unit 39 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
/// moved it in process: it ran scripts/fixtures/c487-readonly-windows-witness.ps1 (deleted) against
/// Clear-C487OwnedResultReadOnly in scripts/lib/c487-harness.ps1; it now witnesses that function's C# twin,
/// <see cref="NightlyFixture.ClearOwnedReadOnly{TNode}"/>, which the in-process nightly tests' fixture runs before it deletes
/// its root. The rows are the script's: the same fake Windows tree (a root under a home with an apostrophe, three owned Git
/// objects, a directory link, a file link, a linked results root and an outside file), so it runs on Linux and Windows alike.
/// Dropped with the script: its assertion-inventory check (Complete-C487Harness's PowerShell row counters) and its
/// DirectoryInfo.GetFileSystemInfos node method (the PowerShell listing call); the listing is the walk's own seam here.
/// </summary>
[Category("Integration")]
public sealed class C487ReadOnlyCleanupWitnessTests
{
    private const string Root = @"C:\Users\fixture's home\AppData\Local\Temp\c487-owned";
    private const string Outside = @"D:\outside-results\keep.txt";
    private const string DirectoryLinkPath = Root + @"\linked-directory";
    private const string FileLinkPath = Root + @"\linked-file";
    private const string RootLink = @"C:\linked-results";

    private static readonly string[] Owned =
        [@"checkout\.git\objects\ab\cdef", @"remote.git\objects\ab\cdef", @"repository with spaces\.git\objects\ab\cdef"];

    [Test]
    public void C487_WindowsReadOnlyWitness()
    {
        var tree = new FakeWindowsTree();
        var before = tree.Count;

        tree.Clear(Root).ShouldBe(3, "C487 cleanup clearedReadOnlyFiles");

        foreach (var relative in Owned)
            tree.IsReadOnly(Root + @"\" + relative).ShouldBeFalse("Windows Directory.Delete refused read-only Git object: " + relative);
        tree.IsReadOnly(Outside).ShouldBeTrue("outside file attributes changed");
        tree.Count.ShouldBe(before, "completion deleted a file before the owner joined its processes");
        tree.Enumerated.Count.ShouldBe(12, "owned Windows directory inventory changed");
        tree.Enumerated[0].ShouldBe(Root);
    }

    [Test]
    public void C487_linked_results_root_is_not_traversed()
    {
        var tree = new FakeWindowsTree();

        tree.Clear(RootLink).ShouldBe(0);

        tree.Enumerated.ShouldBeEmpty("linked root was traversed");
        tree.IsReadOnly(Outside).ShouldBeTrue("outside file attributes changed");
        foreach (var relative in Owned)
            tree.IsReadOnly(Root + @"\" + relative).ShouldBeTrue();
    }

    // The disk binding Dispose runs: an owned read-only object is cleared, and a directory link to an outside directory is
    // neither followed nor cleared through.
    [Test]
    public void C487_disk_cleanup_clears_owned_objects_and_never_follows_a_directory_link()
    {
        var root = Path.Combine(Path.GetTempPath(), "c487-disk-" + Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "owned");
        var outside = Path.Combine(root, "outside");
        var objectPath = Path.Combine(owned, "checkout", ".git", "objects", "ab", "cdef");
        var keep = Path.Combine(outside, "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
        Directory.CreateDirectory(outside);
        File.WriteAllText(objectPath, "object");
        File.WriteAllText(keep, "keep");
        File.SetAttributes(objectPath, FileAttributes.ReadOnly);
        File.SetAttributes(keep, FileAttributes.ReadOnly);
        var link = DirectoryLink.TryCreate(Path.Combine(owned, "linked-directory"), outside);
        try
        {
            if (link is null)
            {
                Skip.Test("this host cannot make a directory link");
                return;
            }

            NightlyFixture.ClearOwnedReadOnly(owned).ShouldBe(1);

            File.GetAttributes(objectPath).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse();
            File.GetAttributes(keep).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue("the directory link was followed");
        }
        finally
        {
            link?.Dispose();
            ReadOnlyDirectory.Delete(root);
        }
    }

    // The script's fake: path-keyed nodes, a parent's children by path, and a listing that refuses a link, the outside file
    // and a file (as Get-FixtureListing threw), recording each directory it lists.
    private sealed class FakeWindowsTree
    {
        private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);

        public FakeWindowsTree()
        {
            Add(Root, FileAttributes.Directory);
            foreach (var relative in new[]
                     {
                         "checkout", @"checkout\.git", @"checkout\.git\objects", @"checkout\.git\objects\ab", "remote.git", @"remote.git\objects",
                         @"remote.git\objects\ab", "repository with spaces", @"repository with spaces\.git", @"repository with spaces\.git\objects",
                         @"repository with spaces\.git\objects\ab",
                     })
                Add(Root + @"\" + relative, FileAttributes.Directory);
            foreach (var relative in Owned)
                Add(Root + @"\" + relative, FileAttributes.ReadOnly);
            Add(Outside, FileAttributes.ReadOnly);
            Add(DirectoryLinkPath, FileAttributes.ReparsePoint | FileAttributes.Directory);
            Add(FileLinkPath, FileAttributes.ReparsePoint);
            Add(RootLink, FileAttributes.ReparsePoint | FileAttributes.Directory);
        }

        public List<string> Enumerated { get; } = [];

        public int Count => _nodes.Count;

        public bool IsReadOnly(string path) => (_nodes[path].Attributes & FileAttributes.ReadOnly) != 0;

        public int Clear(string root) =>
            NightlyFixture.ClearOwnedReadOnly(_nodes[root], node => node.Attributes, List, node => node.Attributes &= ~FileAttributes.ReadOnly);

        private IEnumerable<Node> List(Node directory)
        {
            if (directory.Path is Outside or DirectoryLinkPath or FileLinkPath or RootLink)
                throw new InvalidOperationException("external link was followed: " + directory.Path);
            if ((directory.Attributes & FileAttributes.Directory) == 0)
                throw new InvalidOperationException("file was listed as a directory: " + directory.Path);
            Enumerated.Add(directory.Path);
            return _children.TryGetValue(directory.Path, out var names) ? names.Select(name => _nodes[name]).ToArray() : [];
        }

        private void Add(string path, FileAttributes attributes)
        {
            _nodes[path] = new Node(path) { Attributes = attributes };
            var parent = path[..path.LastIndexOf('\\')];
            if (!_children.TryGetValue(parent, out var names)) _children[parent] = names = [];
            names.Add(path);
        }
    }

    private sealed class Node(string path)
    {
        public string Path { get; } = path;

        public FileAttributes Attributes { get; set; }
    }
}
