using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointWarmBuildTests : CheckpointTestBase
{
    [Test]
    public void intermediate_cache_is_shared_by_matching_builds_and_survives_bin_cleanup()
    {
        var root = TempDir();
        LibGit2Sharp.Repository.Init(root);
        const string project = "App/App.csproj";
        Directory.CreateDirectory(Path.Combine(root, "App"));

        var source = new SourceObservation(new string('a', 40), 0, "source fingerprint", DateTimeOffset.UtcNow, "known");
        (string Owned, string Cache) Plan(string name, string useAppHost, string entry = project, SourceObservation? observed = null)
        {
            var properties = BuildStep.PropertyArguments(
                new Dictionary<string, string> { ["UseAppHost"] = useAppHost }, OperatingSystem.IsWindows()).ToList();
            var intermediateRoot = CheckpointIntermediateCache.PathFor(root, entry, properties, observed ?? source);
            var layout = BuildStep.AppendSharedLayout(properties, root, entry, name, intermediateRoot);
            var arguments = BuildStep.BuildArguments(entry, name, properties, 1);
            const string cacheProperty = "--property:AntiphonCheckpointIntermediateRoot=";
            const string importProperty = "--property:CustomBeforeDirectoryBuildProps=";
            var cache = arguments.SingleOrDefault(arg => arg.StartsWith(cacheProperty, StringComparison.Ordinal));
            var import = arguments.SingleOrDefault(arg => arg.StartsWith(importProperty, StringComparison.Ordinal));
            cache.ShouldNotBeNull("build arguments must select a persistent intermediate root");
            import.ShouldNotBeNull("build arguments must import the generated intermediate props");
            var importPath = import![importProperty.Length..];
            Path.GetRelativePath(root, importPath).Replace('\\', '/').ShouldBe(
                ".antiphon/checkpoint-obj/CheckpointIntermediates.props");
            var ownedRoot = Path.GetFullPath(layout.Owned).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            Path.GetFullPath(importPath).StartsWith(ownedRoot, StringComparison.Ordinal).ShouldBeFalse();
            return (Path.GetFullPath(layout.Owned).TrimEnd(Path.DirectorySeparatorChar), cache![cacheProperty.Length..]);
        }

        var first = Plan("bin-warm-first/", "false");
        var second = Plan("bin-warm-second/", "false");
        var changed = Plan("bin-warm-second/", "true");
        var otherProject = Plan("bin-warm-second/", "false", "App/Other.csproj");
        var otherSource = Plan("bin-warm-second/", "false", observed: source with { Commit = new string('b', 40) });
        Path.GetRelativePath(root, first.Cache).Replace('\\', '/').ShouldStartWith(".antiphon/checkpoint-obj/obj/");
        first.Cache.ShouldBe(second.Cache);
        first.Cache.ShouldNotBe(changed.Cache);
        first.Cache.ShouldNotBe(otherProject.Cache);
        first.Cache.ShouldNotBe(otherSource.Cache);

        var assets = Path.Combine(first.Cache, "project.assets.json");
        var props = Path.Combine(root, CheckpointIntermediateCache.ProjectProps);
        Directory.CreateDirectory(Path.GetDirectoryName(props)!);
        File.WriteAllText(props, "<Project />");
        using var cacheOwner = CheckpointIntermediateCache.TryOpen(root, source, source.Commit);
        cacheOwner.ShouldNotBeNull();
        File.ReadAllText(props).ShouldBe(CheckpointIntermediateCache.PropsText);
        using var competingCache = CheckpointIntermediateCache.TryOpen(root, source, source.Commit);
        competingCache.ShouldBeNull("a second run must build without sharing these intermediates");

        var forwarded = BuildStep.PropertyArguments(new Dictionary<string, string> { ["UseAppHost"] = "false" },
            OperatingSystem.IsWindows());
        using (var entry = cacheOwner!.Prepare(project, forwarded))
        {
            entry.ShouldNotBeNull();
            entry!.Path.ShouldBe(first.Cache);
            entry.Invalidate();
            File.WriteAllText(assets, "{}");
            entry.Publish();
        }
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(first.Owned, SafeWrite.Under(root)));
        var binary = Path.Combine(first.Owned, "App.dll");
        SafeWrite.Require(SafeWrite.Text(binary, "build output", SafeWrite.Under(first.Owned)));
        SafeWrite.Require(OwnedOutputLedger.Seal(first.Owned));

        var cleanup = OutputCleanup.CleanOwnedOutputs(root, ["bin-warm-first"], 0, false, false,
            createdPaths: [first.Owned]);
        cleanup.Refusals.ShouldBeEmpty();
        cleanup.Deleted.ShouldContain(first.Owned);
        Directory.Exists(first.Owned).ShouldBeFalse();
        File.ReadAllText(assets).ShouldBe("{}");
        File.ReadAllText(props).ShouldBe(CheckpointIntermediateCache.PropsText);

        using (var reused = cacheOwner.Prepare(project, forwarded))
            reused.ShouldNotBeNull("a matching sealed partition is reusable");
        File.WriteAllText(assets, "{\"changed\":true}");
        using var changedCache = cacheOwner.Prepare(project, forwarded);
        changedCache.ShouldBeNull("unsealed intermediate bytes cannot seed a verified build");
        File.Exists(assets).ShouldBeTrue("cold fallback leaves the cache in place");
    }

    [Test]
    public void props_replace_breaks_a_hard_link_without_writing_the_outside_file()
    {
        var root = TempDir();
        var source = Arrange(root);
        var outside = Path.Combine(root, "outside-sentinel.txt");
        var props = Path.Combine(root, CheckpointIntermediateCache.ProjectProps);
        Directory.CreateDirectory(Path.GetDirectoryName(props)!);
        File.WriteAllBytes(outside, "outside-sentinel-not-props"u8.ToArray());
        try
        {
            NativeHardLink.Create(props, outside);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            Skip.Test("hard links are unavailable on this volume (" + ex.GetType().Name + ")");
        }

        var before = File.ReadAllBytes(outside);
        var mtime = File.GetLastWriteTimeUtc(outside);
        using var cache = CheckpointIntermediateCache.TryOpen(root, source, source.Commit);
        cache.ShouldNotBeNull();
        File.ReadAllBytes(outside).ShouldBe(before);
        File.GetLastWriteTimeUtc(outside).ShouldBe(mtime);
        File.ReadAllText(props).ShouldBe(CheckpointIntermediateCache.PropsText);
        var propsId = NativeHardLink.Observe(props);
        var outsideId = NativeHardLink.Observe(outside);
        propsId.Links.ShouldBe(1u);
        outsideId.Links.ShouldBe(1u);
        propsId.FileId.ShouldNotBe(outsideId.FileId);
    }

    [Test]
    public void props_symlink_is_a_typed_refusal()
    {
        var root = TempDir();
        var source = Arrange(root);
        var outside = Path.Combine(root, "outside-sentinel.txt");
        var props = Path.Combine(root, CheckpointIntermediateCache.ProjectProps);
        Directory.CreateDirectory(Path.GetDirectoryName(props)!);
        File.WriteAllBytes(outside, "outside-sentinel-not-props"u8.ToArray());
        try
        {
            File.CreateSymbolicLink(props, outside);
        }
        catch (Exception linkFailure) when (linkFailure is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            Skip.Test("symlinks are unavailable on this volume (" + linkFailure.GetType().Name + ")");
        }

        var before = File.ReadAllBytes(outside);
        var mtime = File.GetLastWriteTimeUtc(outside);
        try
        {
            var refusal = Should.Throw<Exception>(() => CheckpointIntermediateCache.TryOpen(root, source, source.Commit));
            refusal.GetType().Name.ShouldBe("CheckpointCacheRefusal");
            refusal.Message.ShouldBe("link-component");
            File.ReadAllBytes(outside).ShouldBe(before);
            File.GetLastWriteTimeUtc(outside).ShouldBe(mtime);
            File.Exists(outside).ShouldBeTrue();
        }
        finally
        {
            // The fixture inventory rejects a temp root that still contains a reparse.
            try { File.Delete(props); }
            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException) { }
        }
    }

    private static SourceObservation Arrange(string root)
    {
        LibGit2Sharp.Repository.Init(root);
        return new SourceObservation(new string('a', 40), 0, "source fingerprint", DateTimeOffset.UtcNow, "known");
    }
}
