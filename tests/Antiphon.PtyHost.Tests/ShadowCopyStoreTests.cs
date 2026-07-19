using System.Text.RegularExpressions;
using Antiphon.PtyHost.Client;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public partial class ShadowCopyStoreTests
{
    [GeneratedRegex(@"^\d{8}-\d{6}-[0-9a-f]{8}$")]
    private static partial Regex VersionDirPattern();

    private static (string Root, string Source, ShadowCopyStore Store) CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-shadowcopy-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "host.exe"), "exe-bytes-v1");
        Directory.CreateDirectory(Path.Combine(source, "runtimes", "win-x64"));
        File.WriteAllText(Path.Combine(source, "runtimes", "win-x64", "native.dll"), "native-bytes");
        return (root, source, new ShadowCopyStore(Path.Combine(root, "bin")));
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Test]
    public async Task Same_content_reuses_the_same_version_dir()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            var first = store.EnsureCurrent(source);
            var second = store.EnsureCurrent(source);

            second.ShouldBe(first);
            Directory.GetDirectories(store.BinRoot).Length.ShouldBe(1);
            Path.GetFileName(first).ShouldMatch(VersionDirPattern().ToString());
            File.ReadAllText(Path.Combine(first, "host.exe")).ShouldBe("exe-bytes-v1");
            File.ReadAllText(Path.Combine(first, "runtimes", "win-x64", "native.dll")).ShouldBe("native-bytes");
        }
        finally
        {
            Cleanup(root);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Changed_content_creates_a_new_version_dir_and_keeps_the_old()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            var first = store.EnsureCurrent(source);
            File.WriteAllText(Path.Combine(source, "host.exe"), "exe-bytes-v2-different");
            var second = store.EnsureCurrent(source);

            second.ShouldNotBe(first);
            Directory.Exists(first).ShouldBeTrue();
            Directory.GetDirectories(store.BinRoot).Length.ShouldBe(2);
            Path.GetFileName(second).ShouldMatch(VersionDirPattern().ToString());
        }
        finally
        {
            Cleanup(root);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Cleanup_deletes_only_unreferenced_dirs()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            var first = store.EnsureCurrent(source);
            File.WriteAllText(Path.Combine(source, "host.exe"), "exe-bytes-v2-different");
            var second = store.EnsureCurrent(source);

            var deleted = store.CleanupUnreferenced(new HashSet<string> { second });

            deleted.ShouldBe(1);
            Directory.Exists(first).ShouldBeFalse();
            Directory.Exists(second).ShouldBeTrue();
        }
        finally
        {
            Cleanup(root);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task TestResults_dir_does_not_affect_the_content_hash()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            var before = ShadowCopyStore.ComputeContentSha8(source);
            Directory.CreateDirectory(Path.Combine(source, "TestResults"));
            File.WriteAllText(Path.Combine(source, "TestResults", "report.html"), Guid.NewGuid().ToString());
            var after = ShadowCopyStore.ComputeContentSha8(source);

            after.ShouldBe(before);
        }
        finally
        {
            Cleanup(root);
        }
        await Task.CompletedTask;
    }
}
