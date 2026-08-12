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

    // Regression lock for the 2026-08-01 recursive-copy explosion: the runner's workspace/ (which
    // holds these very shadow copies) sat inside the copy source, and the old top-level-only
    // exclusion let every new shadow copy swallow all previous generations at full depth. Junk
    // dirs must be pruned at ANY depth, from both the hash and the copy.
    [Test]
    public async Task Workspace_and_alternate_output_dirs_are_excluded_at_any_depth()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            var before = ShadowCopyStore.ComputeContentSha8(source);

            // Top-level workspace holding a fake previous shadow copy, and junk nested deeper.
            var nestedStamp = Path.Combine(source, "workspace", "session-runner-logs", "pty-hosts", "bin", "20260101-000000-deadbeef");
            Directory.CreateDirectory(nestedStamp);
            File.WriteAllText(Path.Combine(nestedStamp, "host.exe"), "previous-generation-bytes");
            Directory.CreateDirectory(Path.Combine(source, "runtimes", "bin-verify"));
            File.WriteAllText(Path.Combine(source, "runtimes", "bin-verify", "host.exe"), "stray-alt-output");

            var after = ShadowCopyStore.ComputeContentSha8(source);
            after.ShouldBe(before, "junk dirs must not affect the content hash at any depth");

            var copy = store.EnsureCurrent(source);
            Directory.Exists(Path.Combine(copy, "workspace")).ShouldBeFalse("workspace must not be copied");
            Directory.Exists(Path.Combine(copy, "runtimes", "bin-verify")).ShouldBeFalse("nested junk must not be copied");
            File.Exists(Path.Combine(copy, "runtimes", "win-x64", "native.dll")).ShouldBeTrue("real runtime assets still copy");
        }
        finally
        {
            Cleanup(root);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// CARD-0037. Once a deps.json is present the copy is filtered down to the host's dependency
    /// closure, and the shipped pseudoconsole (conpty.dll + OpenConsole.exe) is a Content item that
    /// deps.json never mentions. If it is dropped here, every detached host silently falls back to
    /// the inbox conhost — which strips bracketed-paste markers and re-arms the 1 KB clipping the
    /// inline ceilings exist for. Nothing downstream can tell the difference, so it has to be caught
    /// at the copy.
    /// </summary>
    [Test]
    public async Task Shipped_conpty_binaries_survive_the_deps_json_closure_filter()
    {
        var (root, source, store) = CreateFixture();
        try
        {
            File.WriteAllText(
                Path.Combine(source, "Antiphon.PtyHost.deps.json"),
                """{"targets":{".NETCoreApp,Version=v9.0":{"Antiphon.PtyHost/1.0.0":{"runtime":{"Antiphon.PtyHost.dll":{}}}}}}""");
            var conptyDir = Path.Combine(source, "conpty", "win-x64");
            Directory.CreateDirectory(conptyDir);
            File.WriteAllText(Path.Combine(conptyDir, "conpty.dll"), "conpty-bytes");
            File.WriteAllText(Path.Combine(conptyDir, "OpenConsole.exe"), "openconsole-bytes");
            File.WriteAllText(Path.Combine(source, "Unrelated.Package.dll"), "not-in-the-closure");

            var copy = store.EnsureCurrent(source);

            File.Exists(Path.Combine(copy, "conpty", "win-x64", "conpty.dll"))
                .ShouldBeTrue("the shadow copy must carry conpty.dll, at its relative path");
            File.Exists(Path.Combine(copy, "conpty", "win-x64", "OpenConsole.exe"))
                .ShouldBeTrue("and its OpenConsole.exe — conpty.dll falls back to the inbox conhost without it");
            File.Exists(Path.Combine(copy, "Unrelated.Package.dll"))
                .ShouldBeFalse("the closure filter must still be doing its job");
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
