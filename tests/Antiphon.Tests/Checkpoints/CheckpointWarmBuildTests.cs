using System.Runtime.InteropServices;
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

    [Test]
    public void linked_run_lock_stays_one_inode_and_both_runs_go_cold()
    {
        var root = TempDir();
        var source = Arrange(root);
        var outside = Path.Combine(root, "outside-sentinel.txt");
        var home = Path.Combine(root, ".antiphon", "checkpoint-obj", "obj");
        var lockPath = Path.Combine(home, "run.lock");
        Directory.CreateDirectory(home);
        File.WriteAllBytes(outside, "outside-lock-sentinel"u8.ToArray());
        try
        {
            NativeHardLink.Create(lockPath, outside);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            Skip.Test("hard links are unavailable on this volume (" + ex.GetType().Name + ")");
        }

        var before = File.ReadAllBytes(outside);
        var mtime = File.GetLastWriteTimeUtc(outside);
        var identity = NativeHardLink.Observe(lockPath);
        using (var first = CheckpointIntermediateCache.TryOpen(root, source, source.Commit))
            first.ShouldBeNull("a multiply linked lock is not a cache gate");
        File.ReadAllBytes(outside).ShouldBe(before);
        File.GetLastWriteTimeUtc(outside).ShouldBe(mtime);
        var after = NativeHardLink.Observe(lockPath);
        after.FileId.ShouldBe(identity.FileId, "the lock name stays on the planted inode");
        after.Links.ShouldBe(identity.Links);

        using (var second = CheckpointIntermediateCache.TryOpen(root, source, source.Commit))
            second.ShouldBeNull("a second executor stays off that same lock");
        NativeHardLink.Observe(lockPath).FileId.ShouldBe(identity.FileId);
        File.ReadAllBytes(outside).ShouldBe(before);

        var heldRoot = TempDir();
        var heldSource = Arrange(heldRoot);
        var holder = CheckpointIntermediateCache.TryOpen(heldRoot, heldSource, heldSource.Commit);
        try
        {
            holder.ShouldNotBeNull();
            var heldLock = Path.Combine(heldRoot, ".antiphon", "checkpoint-obj", "obj", "run.lock");
            var alias = Path.Combine(heldRoot, "lock-alias");
            NativeHardLink.Create(alias, heldLock);
            var aliasBytes = File.ReadAllBytes(alias);
            var aliasId = NativeHardLink.Observe(alias);
            using var contender = CheckpointIntermediateCache.TryOpen(heldRoot, heldSource, heldSource.Commit);
            contender.ShouldBeNull("a contender does not take a second lock inode");
            File.ReadAllBytes(alias).ShouldBe(aliasBytes);
            NativeHardLink.Observe(alias).FileId.ShouldBe(aliasId.FileId);
        }
        finally
        {
            holder?.Dispose();
        }

        var heldLockAfter = Path.Combine(heldRoot, ".antiphon", "checkpoint-obj", "obj", "run.lock");
        var aliasAfter = Path.Combine(heldRoot, "lock-alias");
        NativeHardLink.Observe(heldLockAfter).FileId.ShouldBe(NativeHardLink.Observe(aliasAfter).FileId);
    }

    [Test]
    [Timeout(30_000)]
    public void fifo_seal_falls_back_cold_without_hanging()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFO seal requires Linux mkfifo");
        var root = TempDir();
        var source = Arrange(root);
        const string project = "App/App.csproj";
        Directory.CreateDirectory(Path.Combine(root, "App"));
        var forwarded = BuildStep.PropertyArguments(new Dictionary<string, string> { ["UseAppHost"] = "false" },
            OperatingSystem.IsWindows());
        using var cache = CheckpointIntermediateCache.TryOpen(root, source, source.Commit);
        cache.ShouldNotBeNull();
        var seal = CheckpointIntermediateCache.PathFor(root, project, forwarded, source) + ".seal";
        Directory.CreateDirectory(Path.GetDirectoryName(seal)!);
        try
        {
            MkFifo(seal, 0x180).ShouldBe(0, "mkfifo errno " + Marshal.GetLastPInvokeError());
            Exception? crash = null;
            CheckpointIntermediateCache.Entry? entry = null;
            try
            {
                entry = cache.Prepare(project, forwarded);
            }
            catch (Exception ex)
            {
                crash = ex;
            }

            crash.ShouldBeNull(crash?.ToString() ?? "");
            entry.ShouldBeNull("a non-regular seal cannot seed a verified build");
        }
        finally
        {
            try { File.Delete(seal); }
            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException) { }
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task read_only_lock_directory_falls_back_cold_without_failing_the_row()
    {
        var root = TempDir();
        var source = Arrange(root);
        var lockPath = Path.Combine(root, ".antiphon", "checkpoint-obj", "obj", "run.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        Directory.CreateDirectory(lockPath);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Exception? openCrash = null;
        CheckpointIntermediateCache? cache = null;
        try
        {
            try
            {
                cache = CheckpointIntermediateCache.TryOpen(root, source, source.Commit);
            }
            catch (Exception ex)
            {
                openCrash = ex;
            }

            openCrash.ShouldBeNull(openCrash?.ToString() ?? "");
            cache.ShouldBeNull("an unusable lock directory is not a cache gate");

            const string project = "App/App.csproj";
            Directory.CreateDirectory(Path.Combine(root, "App"));
            var manifest = new CheckpointManifest();
            manifest.Builds.Add(new BuildSpec { Id = "bin-cold", Project = project, OutputPath = "bin-cold/" });
            var row = CheckpointFixtures.Row("CP-1", "bin-cold");
            row.Expect = [];
            manifest.Checkpoints.Add(row);
            var driver = new FakeDriver();
            driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
            driver.When(CheckpointFixtures.IsRun, (request, _) =>
            {
                CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
                return Task.FromResult(new DriverResult(0, "", ""));
            });
            Exception? rowCrash = null;
            SchedulerResult? result = null;
            try
            {
                result = await new RunScheduler(driver, new FakePlatform { IsWindows = OperatingSystem.IsWindows() }).RunAsync(new SchedulerRequest
                {
                    Manifest = manifest,
                    Rows = manifest.Checkpoints,
                    RunDirectory = root,
                    WorkingDirectory = root,
                    State = new RunState { RunId = "cold-lock", StartedAt = DateTimeOffset.UtcNow },
                    Slots = new FixedSlotClient("granted"),
                    Commit = source.Commit,
                    ExpectedSourceSha = source.Commit,
                    Width = 1,
                    TotalTimeout = TimeSpan.FromMinutes(5),
                    IntermediateCache = cache,
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                rowCrash = ex;
            }

            rowCrash.ShouldBeNull(rowCrash?.ToString() ?? "");
            result.ShouldNotBeNull();
            result!.Rows.Single().State.ShouldBe("green",
                string.Join(Environment.NewLine, result.Rows.Select(item => item.Line + Environment.NewLine + item.Output)));
            driver.Calls.Any(CheckpointFixtures.IsBuild).ShouldBeTrue();
            driver.Calls.Where(CheckpointFixtures.IsBuild).ShouldAllBe(call =>
                call.Arguments.All(arg => !arg.Contains("AntiphonCheckpointIntermediateRoot", StringComparison.Ordinal)));
        }
        finally
        {
            cache?.Dispose();
            if (!OperatingSystem.IsWindows() && Directory.Exists(lockPath))
                File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static SourceObservation Arrange(string root)
    {
        LibGit2Sharp.Repository.Init(root);
        return new SourceObservation(new string('a', 40), 0, "source fingerprint", DateTimeOffset.UtcNow, "known");
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
