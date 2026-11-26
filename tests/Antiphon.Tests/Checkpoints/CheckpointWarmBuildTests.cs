using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
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
        var entry = cacheOwner!.Prepare(project, forwarded).ShouldNotBeNull();
        entry.Path.ShouldBe(first.Cache);
        entry.Invalidate().ShouldBeTrue();
        File.WriteAllText(assets, "{}");
        entry.Publish();
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

        cacheOwner.Prepare(project, forwarded).ShouldNotBeNull("a matching sealed partition is reusable");
        File.WriteAllText(assets, "{\"changed\":true}");
        var changedCache = cacheOwner.Prepare(project, forwarded);
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
            using var contender = CheckpointIntermediateCache.TryOpen(heldRoot, heldSource, heldSource.Commit);
            contender.ShouldBeNull("a contender does not take a second lock inode");
        }
        finally
        {
            holder?.Dispose();
        }

        var heldLockAfter = Path.Combine(heldRoot, ".antiphon", "checkpoint-obj", "obj", "run.lock");
        var aliasAfter = Path.Combine(heldRoot, "lock-alias");
        var heldIdentity = NativeHardLink.Observe(heldLockAfter);
        var aliasIdentity = NativeHardLink.Observe(aliasAfter);
        heldIdentity.FileId.ShouldBe(aliasIdentity.FileId);
        heldIdentity.Links.ShouldBe(aliasIdentity.Links);
        heldIdentity.Links.ShouldBe(2u);
        File.ReadAllBytes(aliasAfter).ShouldBe(File.ReadAllBytes(heldLockAfter));
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
        var refusals = new List<string>();
        using var cache = CheckpointIntermediateCache.TryOpen(root, source, source.Commit, refusals.Add).ShouldNotBeNull();
        var partition = CheckpointIntermediateCache.PathFor(root, project, forwarded, source);
        var seal = partition + ".seal";
        Directory.CreateDirectory(partition);
        Directory.Exists(partition).ShouldBeTrue();
        try
        {
            MkFifo(seal, 0x180).ShouldBe(0, "mkfifo errno " + Marshal.GetLastPInvokeError());
            var clock = Stopwatch.StartNew();
            var entry = cache.Prepare(project, forwarded);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            entry.ShouldBeNull("a non-regular seal cannot seed a verified build");
            refusals.ShouldBe(["checkpoint cache refused: prepare: untrusted-seal"]);
        }
        finally
        {
            try { File.Delete(seal); }
            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException) { }
        }
    }

    [Test]
    public void existing_partition_without_a_seal_is_a_partial_pair()
    {
        var fixture = NewSealFixture();
        var refusals = new List<string>();
        using var cache = CheckpointIntermediateCache.TryOpen(fixture.Root, fixture.Source, fixture.Source.Commit,
            refusals.Add).ShouldNotBeNull();
        Directory.CreateDirectory(fixture.Partition);
        File.Exists(fixture.Seal).ShouldBeFalse();
        fixture.Prepare(cache).ShouldBeNull();
        refusals.ShouldBe(["checkpoint cache refused: prepare: partial-partition"]);
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

    [Test]
    [Arguments("write-and-close")]
    [Arguments("flush-and-close")]
    [Arguments("close-only")]
    [Arguments("rename")]
    public async Task publication_fault_keeps_verified_row(string fault)
    {
        var fixture = NewSealFixture();
        var driver = new SealDriver(fixture);
        var io = new FaultSealIo(fault)
        {
            BeforePublication = () =>
            {
                fixture.Binding("bin-proof", driver.Builds.Single()).Check().ShouldBe("verified");
                OwnedOutputLedger.Check(fixture.Output("bin-proof")).ShouldBeNull();
                new FileInfo(fixture.Seal).Length.ShouldBe(0);
            },
        };
        Exception? escaped = null;
        SchedulerResult? result = null;
        using (var cache = fixture.Open(io))
        {
            try { result = await fixture.RunAsync(cache, driver); }
            catch (IOException error) { escaped = error; }
        }

        io.PublicationWrites.ShouldBe(1);
        io.PublicationCloses.ShouldBe(1, "publication must close its file even when a write or flush failed");
        io.PublicationHandle.ShouldNotBeNull().IsClosed.ShouldBeTrue();
        escaped.ShouldBeNull("a second exception from seal disposal must not escape a qualified build");
        io.Faults.ShouldBe(fault is "write-and-close" or "flush-and-close" ? 2 : 1);
        if (fault is "close-only" or "rename")
            io.AcceptedDigest.ShouldBe(fixture.ExpectedDigest());
        fixture.AssertGreen(result!, driver, "bin-proof", warm: true);
        File.ReadAllBytes(fixture.Seal).ShouldBeEmpty();
        Directory.GetFiles(Path.GetDirectoryName(fixture.Seal)!, "*.tmp").ShouldBeEmpty();
        using var reopened = fixture.Open();
        fixture.Prepare(reopened).ShouldBeNull("a failed publication cannot grant reuse, even if it staged the complete digest");
    }

    [Test]
    [Arguments("slot-refused", ExitCodes.Invalid)]
    [Arguments("slot-timeout", ExitCodes.SlotTimeout)]
    [Arguments("canceled-wait", ExitCodes.Canceled)]
    [Arguments("ended-owner", ExitCodes.OwnerEnded)]
    [Arguments("source-refused", ExitCodes.Invalid)]
    [Arguments("output-exists", ExitCodes.Invalid)]
    [Arguments("ledger-refused", ExitCodes.Invalid)]
    [Arguments("graph-refused", ExitCodes.Invalid)]
    public async Task unlaunched_build_preserves_partition(string boundary, int expectedExit)
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var seal = File.ReadAllBytes(fixture.Seal);
        seal.Length.ShouldBe(32);
        var partition = fixture.PartitionFiles();
        var clock = new FakeTimeProvider();
        using var cancel = new CancellationTokenSource();
        var driver = new SealDriver(fixture) { Confine = boundary == "ledger-refused" };
        var factory = new RecordingProcessFactory();
        var refusalReached = false;
        var slots = new SealSlots((label, token) =>
        {
            refusalReached = true;
            switch (boundary)
            {
                case "slot-refused":
                    return Task.FromResult(new SlotLease { State = "refused", ExitCode = ExitCodes.Invalid, SlotReason = "fixture-refused" });
                case "slot-timeout":
                    clock.Advance(TimeSpan.FromMinutes(1));
                    return Task.FromCanceled<SlotLease>(token);
                case "canceled-wait":
                    cancel.Cancel();
                    return Task.FromCanceled<SlotLease>(token);
                case "source-refused":
                    File.WriteAllText(fixture.SourceFile, "changed source");
                    break;
                case "output-exists":
                    Directory.CreateDirectory(fixture.Output("bin-proof"));
                    break;
            }
            return Task.FromResult(SealSlots.Grant());
        });
        if (boundary == "graph-refused")
            File.WriteAllText(fixture.GraphToggle, "request a missing reference");

        SchedulerResult refused;
        using (var cache = fixture.Open())
        {
            refused = await fixture.RunAsync(cache, boundary == "graph-refused" ? new ProcessDriver(factory) : driver,
                slots: slots, clock: clock, cancellationToken: cancel.Token, ownerBound: boundary == "ended-owner",
                beforeLaunch: token =>
                {
                    if (boundary == "ended-owner")
                    {
                        cancel.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                    if (boundary == "ledger-refused")
                        File.WriteAllText(fixture.LedgerFile, "changed ignored input");
                    return Task.CompletedTask;
                });
        }

        refusalReached.ShouldBeTrue();
        refused.ExitCode.ShouldBe(expectedExit, Describe(refused));
        driver.Builds.ShouldBeEmpty();
        driver.Rows.ShouldBeEmpty();
        factory.Started.ShouldBeEmpty();
        var build = refused.State.Builds.Single();
        switch (boundary)
        {
            case "slot-refused": build.SlotReason.ShouldBe("fixture-refused"); break;
            case "slot-timeout": build.TerminalReason.ShouldBe("slot-admission-deadline-exceeded"); break;
            case "canceled-wait": refused.State.Reason.ShouldBe("caller-requested"); break;
            case "ended-owner": refused.State.Reason.ShouldBe("owner-ended"); break;
            case "source-refused": build.SlotReason.ShouldBe("source_changed"); break;
            case "output-exists": build.SlotReason.ShouldBe("refused: pre-existing-output"); break;
            case "ledger-refused":
                build.SlotReason.ShouldBe("refused: tampered");
                File.ReadAllText(fixture.Log("bin-proof")).ShouldContain("TAMPERED ledger-input.txt");
                break;
            case "graph-refused":
                build.SlotReason.ShouldBe("refused: graph-unevaluated");
                File.ReadAllText(fixture.Log("bin-proof")).ShouldContain("referenced project missing:");
                break;
        }

        File.ReadAllBytes(fixture.Seal).ShouldBe(seal, boundary + " must preserve the valid seal");
        fixture.PartitionFiles().ShouldBe(partition);
        File.WriteAllText(fixture.SourceFile, "original source");
        File.WriteAllText(fixture.LedgerFile, "original input");
        File.Delete(fixture.GraphToggle);
        var retryDriver = new SealDriver(fixture);
        using (var cache = fixture.Open())
            fixture.AssertGreen(await fixture.RunAsync(cache, retryDriver, "bin-retry"), retryDriver, "bin-retry", warm: true);
        File.ReadAllBytes(fixture.Seal).ShouldBe(fixture.ExpectedDigest());

        if (boundary == "slot-refused")
        {
            var unleased = new SealSlots((_, _) => Task.FromResult(new SlotLease { State = "unleased" }));
            var coldDriver = new SealDriver(fixture);
            using (var warmCache = fixture.Open())
                fixture.AssertGreen(await fixture.RunAsync(warmCache, coldDriver, "bin-unleased", slots: unleased),
                    coldDriver, "bin-unleased", warm: false);
            File.ReadAllBytes(fixture.Seal).ShouldBe(fixture.ExpectedDigest());

            var firstUse = NewSealFixture();
            using var cache = firstUse.Open();
            (await firstUse.RunAsync(cache, new SealDriver(firstUse), slots: slots)).ExitCode.ShouldBe(expectedExit);
            File.Exists(firstUse.Seal).ShouldBeFalse();
            Directory.Exists(firstUse.Partition).ShouldBeFalse();
            var initialDriver = new SealDriver(firstUse);
            firstUse.AssertGreen(await firstUse.RunAsync(cache, initialDriver), initialDriver, "bin-proof", warm: true);
            File.ReadAllBytes(firstUse.Seal).ShouldBe(firstUse.ExpectedDigest());
        }
    }

    [Test]
    public async Task invalidation_refusal_rebinds_cold()
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var bytes = File.ReadAllBytes(fixture.Seal);
        var io = new FaultSealIo("invalidation");
        var driver = new SealDriver(fixture) { Confine = true };
        var slots = new SealSlots();
        using (var cache = fixture.Open(io))
            fixture.AssertGreen(await fixture.RunAsync(cache, driver, slots: slots), driver, "bin-proof", warm: false);
        slots.BuildGrants.ShouldBe(1);
        io.Faults.ShouldBe(1);
        driver.Captures.Count.ShouldBe(1, "the ledger must precede the slot and cannot be replaced during fallback");
        driver.Builds.Single().Ledger.ShouldBeSameAs(driver.Captures.Single());
        driver.Builds.Single().Ledger!.Files.Keys.ShouldContain("ledger-input.txt");
        driver.Builds.Single().GraphEvaluationParallelism.ShouldBe(1);
        File.ReadAllBytes(fixture.Seal).ShouldBe(bytes);

        var processIo = new FaultSealIo("invalidation");
        var factory = new RecordingProcessFactory(runChildren: true);
        var processSlots = new SealSlots();
        using (var cache = fixture.Open(processIo))
        {
            var recovered = await fixture.RunAsync(cache, new ProcessDriver(factory), "bin-driver", slots: processSlots);
            recovered.ExitCode.ShouldBe(ExitCodes.Green, Describe(recovered));
            recovered.Rows.Single().State.ShouldBe("green");
            recovered.Rows.Single().Source.BuildSource.ShouldBe("verified");
        }
        processSlots.BuildGrants.ShouldBe(1);
        processIo.Faults.ShouldBe(1);
        var prepared = factory.Prepared.Where(CheckpointFixtures.IsBuild).ToArray();
        prepared.Length.ShouldBe(2);
        prepared[0].Arguments.ShouldContain("--property:" + CheckpointIntermediateCache.RootProperty + "=" + fixture.Partition);
        prepared[1].Arguments.Any(arg => arg.StartsWith("--property:CustomBeforeDirectoryBuildProps=", StringComparison.Ordinal)).ShouldBeFalse();
        var started = factory.Started.Where(CheckpointFixtures.IsBuild).ShouldHaveSingleItem();
        started.ShouldBeSameAs(prepared[1]);
        var processRow = factory.Started.Where(CheckpointFixtures.IsRun).ShouldHaveSingleItem();
        SealFixture.Properties(processRow).ShouldBe(SealFixture.Properties(started));
        fixture.Binding("bin-driver", processRow).Check().ShouldBe("verified");
        OwnedOutputLedger.Check(fixture.Output("bin-driver")).ShouldBeNull();
        File.ReadAllBytes(fixture.Seal).ShouldBe(bytes);

        var drift = new FaultSealIo("invalidation")
        {
            OnInvalidationRefusal = () => File.WriteAllText(fixture.SourceFile, "changed during fallback"),
        };
        var driftDriver = new SealDriver(fixture) { Confine = true };
        using (var cache = fixture.Open(drift))
        {
            var refused = await fixture.RunAsync(cache, driftDriver, "bin-drift");
            refused.ExitCode.ShouldBe(ExitCodes.Invalid, Describe(refused));
            refused.State.Builds.Single().SlotReason.ShouldBe("source_changed");
        }
        drift.Faults.ShouldBe(1);
        driftDriver.Builds.ShouldBeEmpty();
        driftDriver.Rows.ShouldBeEmpty();
        File.ReadAllText(fixture.Log("bin-drift")).ShouldContain("TAMPERED source.txt");
        File.ReadAllBytes(fixture.Seal).ShouldBe(bytes);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task standalone_row_replays_the_producing_layout(bool warm)
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var producer = new SealDriver(fixture);
        using (var cache = fixture.Open(warm ? null : new FaultSealIo("invalidation")))
        {
            var result = await fixture.RunAsync(cache, producer);
            fixture.AssertGreen(result, producer, "bin-proof", warm);
        }

        var replay = new SealDriver(fixture)
        {
            OnRow = (_, _) =>
            {
                using var contender = CheckpointIntermediateCache.TryOpen(fixture.Root, fixture.Source, fixture.Source.Commit);
                (contender is null).ShouldBe(warm, "only a warm replay must hold the worktree gate");
                return Task.CompletedTask;
            },
        };
        using var output = new StringWriter();
        string[] args =
        [
            "row", "--name", "replay", "--project", SealFixture.Project, "--output-path", "bin-proof/",
            "--filter", "/*/*/Sample/alpha", "--no-build", "--expected-source-sha", fixture.Source.Commit!,
        ];
        var runtime = new CheckpointApp.Runtime
        {
            RepoRoot = fixture.Root, Driver = replay, Slots = new SealSlots(), Output = output,
        };
        var properties = SealFixture.Properties(producer.Builds.Single());
        var explicitLayout = args.Concat(properties.SelectMany(property =>
            new[] { "--msbuild-property", property["--property:".Length..] })).ToArray();
        foreach (var replayArgs in warm ? new[] { args, explicitLayout } : new[] { args })
        {
            if (warm)
            {
                using (var holder = fixture.Open())
                {
                    var contended = await Antiphon.Checkpoints.Program.RunAsync(replayArgs, runtime);
                    contended.ShouldBe(ExitCodes.Invalid, output.ToString());
                    output.ToString().ShouldContain("reason=build_cache_unavailable");
                    replay.Rows.ShouldBeEmpty();
                }
                output.GetStringBuilder().Clear();
            }
            var exit = await Antiphon.Checkpoints.Program.RunAsync(replayArgs, runtime);
            exit.ShouldBe(ExitCodes.Green, output.ToString());
            replay.Builds.ShouldBeEmpty();
            SealFixture.Properties(replay.Rows.ShouldHaveSingleItem()).ShouldBe(properties);
            output.ToString().ShouldContain("buildSource=verified");
            replay.Rows.Clear();
            output.GetStringBuilder().Clear();
        }
        using var reopened = fixture.Open();
    }

    [Test]
    public async Task ordinary_warm_replay_requires_a_verified_binding_and_gate()
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var producer = new SealDriver(fixture);
        using (var cache = fixture.Open())
            fixture.AssertGreen(await fixture.RunAsync(cache, producer), producer, "bin-proof", warm: true);

        var replay = new SealDriver(fixture)
        {
            OnRow = (_, _) =>
            {
                using var contender = CheckpointIntermediateCache.TryOpen(fixture.Root, fixture.Source, fixture.Source.Commit);
                contender.ShouldBeNull("a warm replay host must run under the worktree gate");
                return Task.CompletedTask;
            },
        };
        using var output = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            RepoRoot = fixture.Root, Driver = replay, Slots = new SealSlots(), Output = output,
        };
        string[] args =
        [
            "row", "--name", "replay", "--project", SealFixture.Project, "--output-path", "bin-proof/",
            "--filter", "/*/*/Sample/alpha", "--no-build",
        ];
        var properties = SealFixture.Properties(producer.Builds.Single());
        var explicitLayout = args.Concat(properties.SelectMany(property =>
            new[] { "--msbuild-property", property["--property:".Length..] })).ToArray();
        var mismatched = explicitLayout.Concat(new[] { "--msbuild-property", "Label=stale" }).ToArray();

        using (fixture.Open())
        {
            var refused = await Antiphon.Checkpoints.Program.RunAsync(mismatched, runtime);
            replay.Rows.ShouldBeEmpty("an ordinary stale-binding replay must refuse before the host starts");
            refused.ShouldBe(ExitCodes.Invalid, output.ToString());
            output.ToString().ShouldContain("reason=build_source_mismatch");
            output.GetStringBuilder().Clear();

            var contended = await Antiphon.Checkpoints.Program.RunAsync(explicitLayout, runtime);
            contended.ShouldBe(ExitCodes.Invalid, output.ToString());
            output.ToString().ShouldContain("reason=build_cache_unavailable");
            replay.Rows.ShouldBeEmpty();
        }

        output.GetStringBuilder().Clear();
        var admitted = await Antiphon.Checkpoints.Program.RunAsync(explicitLayout, runtime);
        admitted.ShouldBe(ExitCodes.Green, output.ToString());
        SealFixture.Properties(replay.Rows.ShouldHaveSingleItem()).ShouldBe(properties);
        output.ToString().ShouldContain("buildSource=verified");
    }

    [Test]
    public void cache_file_creation_does_not_reopen_its_exclusive_handle()
    {
        var root = TempDir();
        var path = Path.Combine(root, "staged.seal");
        var io = new ExclusiveCreateIo(path);
        var context = new CheckpointCleanupContext(io, CheckpointCleanupDefaults.Context.Git);
        var write = new CheckpointWrite(context);

        var refusal = write.OpenCreate(path, write.Under(root), out var stream);
        using (stream)
        {
            refusal.ShouldBeNull();
            stream.ShouldNotBeNull();
            io.ExclusivePathProbes.ShouldBe(0, "Windows denies a second open while the creator holds FileShare.None");
            context.Registry.Identity(path).ShouldNotBeNull();
        }

        context.Registry.Identity(path).ShouldBe(FilesystemIdentityReader.Read(io, path));
    }

    [Test]
    public async Task overlapping_build_keeps_first_rows_partition()
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var rowEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBuildEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slots = new SealSlots(async (label, token) =>
        {
            if (label == "build:bin-second")
                await rowEntered.Task.WaitAsync(token);
            return SealSlots.Grant();
        });
        var driver = new SealDriver(fixture)
        {
            OnRow = async (request, token) =>
            {
                if (SealFixture.HasOutput(request, "bin-proof"))
                {
                    rowEntered.TrySetResult();
                    await secondBuildEntered.Task.WaitAsync(token);
                }
            },
            OnBuild = (request, _) =>
            {
                if (request.Arguments.Contains("--property:OutputPath=bin-second/"))
                {
                    using var contender = CheckpointIntermediateCache.TryOpen(fixture.Root, fixture.Source, fixture.Source.Commit);
                    contender.ShouldBeNull("the first test host still owns borrowed intermediates");
                    secondBuildEntered.TrySetResult();
                }
                return Task.FromResult(new DriverResult(0, "", ""));
            },
        };
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using (var cache = fixture.Open())
        {
            var result = await fixture.RunAsync(cache, driver, slots: slots, cancellationToken: watchdog.Token, second: true);
            result.ExitCode.ShouldBe(ExitCodes.Green, Describe(result));
            result.Rows.Count.ShouldBe(2);
            fixture.AssertGreen(result, driver, "bin-proof", warm: true);
            fixture.AssertGreen(result, driver, "bin-second", warm: false);
        }

        rowEntered.Task.IsCompletedSuccessfully.ShouldBeTrue();
        secondBuildEntered.Task.IsCompletedSuccessfully.ShouldBeTrue();
        driver.Builds.Count.ShouldBe(2);
        using var reopened = fixture.Open();
        fixture.Prepare(reopened).ShouldNotBeNull().Path.ShouldBe(fixture.Partition);
        File.ReadAllBytes(fixture.Seal).ShouldBe(fixture.ExpectedDigest());
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task cache_digest_stops_on_cancellation_during_a_member_read(bool duringPreparation)
    {
        var fixture = NewSealFixture();
        if (duringPreparation)
            fixture.Seed(extraMember: true);
        byte[] originalSeal = duringPreparation ? File.ReadAllBytes(fixture.Seal) : [];
        var driver = new SealDriver(fixture)
        {
            OnBuild = (_, _) =>
            {
                File.WriteAllText(Path.Combine(fixture.Partition, "second.txt"), "second intermediate");
                return Task.FromResult(new DriverResult(0, "", ""));
            },
        };
        using var cancel = new CancellationTokenSource();
        var reads = new CancelOnDigestIo(cancel);
        var context = new CheckpointCleanupContext(reads, CheckpointCleanupDefaults.Context.Git);
        var slots = new SealSlots();
        using (var cache = fixture.Open(new DigestSealIo(context)))
        {
            var result = await fixture.RunAsync(cache, driver, slots: slots, cancellationToken: cancel.Token);
            result.ExitCode.ShouldBe(ExitCodes.Canceled, Describe(result));
            result.State.Reason.ShouldBe("caller-requested");
        }

        (await reads.FirstReadSawCancellation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue(
            "the bounded reader must pass the run's cancellation signal to the active read");
        reads.MembersOpened.ShouldBe(1, "cancellation inside the first bounded read must stop the traversal");
        slots.BuildGrants.ShouldBe(duringPreparation ? 0 : 1);
        driver.Builds.Count.ShouldBe(duringPreparation ? 0 : 1);
        driver.Rows.ShouldBeEmpty();
        File.ReadAllBytes(fixture.Seal).ShouldBe(originalSeal);
        using var reopened = fixture.Open();
        (fixture.Prepare(reopened) is not null).ShouldBe(duringPreparation);
    }

    [Test]
    [Arguments("nonzero", ExitCodes.Invalid, "build-failed")]
    [Arguments("timeout", ExitCodes.Timeout, "build-deadline-exceeded")]
    [Arguments("source-drift", ExitCodes.Invalid, "source_changed")]
    [Arguments("post-build-ledger", ExitCodes.Invalid, "slot-refused")]
    [Arguments("receipt-refused", ExitCodes.Invalid, "slot-refused")]
    public async Task unqualified_build_keeps_invalid_seal(string failure, int expectedExit, string expectedRowState)
    {
        var fixture = NewSealFixture();
        fixture.Seed();
        var clock = new FakeTimeProvider();
        var io = new FaultSealIo("observe");
        var driver = new SealDriver(fixture)
        {
            Confine = failure == "post-build-ledger",
            OnBuild = (request, token) =>
            {
                new FileInfo(fixture.Seal).Length.ShouldBe(0, "invalidate before the build's first write to the partition");
                File.WriteAllText(fixture.Asset, "first write by build");
                if (failure == "source-drift") File.WriteAllText(fixture.SourceFile, "changed during build");
                if (failure == "post-build-ledger") File.WriteAllText(fixture.LedgerFile, "changed during build");
                if (failure == "timeout")
                {
                    clock.Advance(TimeSpan.FromMinutes(1));
                    token.ThrowIfCancellationRequested();
                }
                return Task.FromResult(new DriverResult(failure == "nonzero" ? 1 : 0, "", ""));
            },
        };
        using (var cache = fixture.Open(io))
        {
            var result = await fixture.RunAsync(cache, driver, clock: clock, beforeReceipt: (binding, _) =>
            {
                if (failure == "receipt-refused")
                    File.WriteAllText(binding.PathName, "foreign receipt");
                return Task.CompletedTask;
            });
            result.ExitCode.ShouldBe(expectedExit, Describe(result));
            result.Rows.Single().State.ShouldBe(expectedRowState, Describe(result));
            if (failure == "receipt-refused")
                result.State.Builds.Single().SlotReason.ShouldBe("refused: pre-existing-build-source");
            if (failure == "post-build-ledger")
                File.ReadAllText(fixture.Log("bin-proof")).ShouldContain("TAMPERED ledger-input.txt");
        }

        driver.Builds.Count.ShouldBe(1);
        driver.Rows.ShouldBeEmpty();
        io.PublicationWrites.ShouldBe(0);
        File.ReadAllBytes(fixture.Seal).ShouldBeEmpty();
        using var reopened = fixture.Open();
        fixture.Prepare(reopened).ShouldBeNull();
    }

    private static string Describe(SchedulerResult result) =>
        string.Join(Environment.NewLine, result.Rows.Select(row => row.State + " " + row.Line + " " + row.Output));

    private SealFixture NewSealFixture()
    {
        // The scope's ownership marker belongs to its parent, outside the fixture Git root.
        var root = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(Path.Combine(root, "App"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".antiphon/\nbin-*/\nledger-input.txt\ngraph-refusal\n");
        File.WriteAllText(Path.Combine(root, SealFixture.Project), "<Project><Import Project=\"Graph.props\" /></Project>");
        File.WriteAllText(Path.Combine(root, "App", "Graph.props"),
            "<Project><ItemGroup Condition=\"Exists('graph-refusal')\">" +
            "<ProjectReference Include=\"Absent.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(root, "source.txt"), "original source");
        LibGit2Sharp.Repository.Init(root);
        using var repo = new LibGit2Sharp.Repository(root);
        LibGit2Sharp.Commands.Stage(repo, [".gitignore", SealFixture.Project, "App/Graph.props", "source.txt"]);
        var author = new LibGit2Sharp.Signature("Checkpoint Test", "checkpoint@example.invalid", DateTimeOffset.UtcNow);
        repo.Commit("seed", author, author, new LibGit2Sharp.CommitOptions());
        File.WriteAllText(Path.Combine(root, "ledger-input.txt"), "original input");
        var source = new SourceSnapshot().Capture(root);
        source.CaptureStatus.ShouldBe("known", source.ErrorCode);
        source.DirtyFiles.ShouldBe(0);
        return new SealFixture(root, source);
    }

    private sealed class SealFixture(string root, SourceObservation source)
    {
        internal const string Project = "App/App.csproj";
        private const string OutputProperty = "--property:OutputPath=";
        private readonly IReadOnlyList<string> properties = BuildStep.PropertyArguments([], OperatingSystem.IsWindows());
        internal string Root => root;
        internal SourceObservation Source => source;
        internal string Partition => CheckpointIntermediateCache.PathFor(root, Project, properties, source);
        internal string Seal => Partition + ".seal";
        internal string Asset => Path.Combine(Partition, "asset.txt");
        internal string SourceFile => Path.Combine(root, "source.txt");
        internal string LedgerFile => Path.Combine(root, "ledger-input.txt");
        internal string GraphToggle => Path.Combine(root, "App", "graph-refusal");
        internal string Output(string id) => Path.Combine(root, "App", id);
        internal string Log(string id) => Path.Combine(root, ".antiphon", id, "builds", id, "build.log");

        internal CheckpointIntermediateCache Open(CheckpointIntermediateCache.SealIo? io = null) =>
            CheckpointIntermediateCache.TryOpen(root, source, source.Commit, sealIo: io).ShouldNotBeNull();
        internal CheckpointIntermediateCache.Entry? Prepare(CheckpointIntermediateCache cache) =>
            cache.Prepare(Project, properties);
        internal void Seed(bool extraMember = false)
        {
            using var cache = Open();
            var entry = Prepare(cache).ShouldNotBeNull();
            entry.Invalidate().ShouldBeTrue();
            File.WriteAllText(Asset, "seeded intermediates");
            if (extraMember)
                File.WriteAllText(Path.Combine(Partition, "second.txt"), "second intermediate");
            entry.Publish();
        }

        internal string[] PartitionFiles() => Directory.GetFiles(Partition, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => Path.GetRelativePath(Partition, file) + "=" + Convert.ToHexString(File.ReadAllBytes(file))).ToArray();

        internal byte[] ExpectedDigest()
        {
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(Partition, "*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(Partition, file),
                    ReceiptFile.Digest(CheckpointCleanupDefaults.Context, file).Sha256);
            return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files));
        }

        internal CheckpointBuildBinding Binding(string id, DriverRequest request) =>
            CheckpointBuildBinding.Expected(root, Project, id + "/", Properties(request), source);

        internal Task<SchedulerResult> RunAsync(CheckpointIntermediateCache cache, IDriver driver,
            string id = "bin-proof", SealSlots? slots = null, FakeTimeProvider? clock = null,
            CancellationToken cancellationToken = default, bool ownerBound = false,
            Func<CancellationToken, Task>? beforeLaunch = null,
            Func<CheckpointBuildBinding, CancellationToken, Task>? beforeReceipt = null, bool second = false)
        {
            var manifest = new CheckpointManifest();
            manifest.Build.SlotWaitMinutes = 1;
            manifest.Build.AdmissionHash = "strict";
            foreach (var build in second ? new[] { id, "bin-second" } : [id])
            {
                manifest.Builds.Add(new BuildSpec { Id = build, Project = Project, OutputPath = build + "/" });
                var row = CheckpointFixtures.Row("CP-" + manifest.Builds.Count, build);
                row.Expect = [];
                manifest.Checkpoints.Add(row);
            }
            var state = new RunState { RunId = id, StartedAt = DateTimeOffset.UtcNow };
            state.Source.Start = source;
            return new RunScheduler(driver, new RuntimePlatform(), clock ?? TimeProvider.System).RunAsync(new SchedulerRequest
            {
                Manifest = manifest, Rows = manifest.Checkpoints, State = state,
                RunDirectory = Path.Combine(root, ".antiphon", id), WorkingDirectory = root,
                Slots = slots ?? new SealSlots(), Width = 2,
                Commit = source.Commit!, ExpectedSourceSha = source.Commit,
                SourceGuard = new SourceRunGuard(root, state.Source),
                IntermediateCache = cache, BeforeLaunch = beforeLaunch, BeforeBuildReceiptWrite = beforeReceipt,
                OwnerBound = ownerBound, RowTimeoutOverride = TimeSpan.FromMinutes(1),
            }, cancellationToken);
        }

        internal void AssertGreen(SchedulerResult result, SealDriver driver, string id, bool warm)
        {
            var build = driver.Builds.Single(request => HasOutput(request, id));
            var row = driver.Rows.Single(request => HasOutput(request, id));
            result.ExitCode.ShouldBe(ExitCodes.Green, Describe(result));
            var rowIndex = result.State.Builds.FindIndex(item => item.Id == id);
            result.Rows.Single(item => item.Id == "CP-" + (rowIndex + 1)).State.ShouldBe("green", Describe(result));
            result.Rows.Single(item => item.Id == "CP-" + (rowIndex + 1)).Source.BuildSource.ShouldBe("verified");
            var property = "--property:" + CheckpointIntermediateCache.RootProperty + "=" + Partition;
            build.Arguments.Contains(property).ShouldBe(warm);
            build.Arguments.Any(arg => arg.StartsWith("--property:CustomBeforeDirectoryBuildProps=", StringComparison.Ordinal)).ShouldBe(warm);
            Properties(row).ShouldBe(Properties(build));
            Binding(id, row).Check().ShouldBe("verified");
            OwnedOutputLedger.Check(Output(id)).ShouldBeNull();
            File.Exists(CheckpointFixtures.TrxFile(row)).ShouldBeTrue();
        }

        internal static bool HasOutput(DriverRequest request, string id)
        {
            var value = request.Arguments.Single(arg => arg.StartsWith(OutputProperty, StringComparison.Ordinal))[OutputProperty.Length..];
            return Path.GetFileName(Path.TrimEndingDirectorySeparator(value)) == id;
        }

        internal static string Binary(DriverRequest request)
        {
            var value = request.Arguments.Single(arg => arg.StartsWith(OutputProperty, StringComparison.Ordinal))[OutputProperty.Length..];
            return Path.Combine(request.WorkingDirectory, "App", value, "App.dll");
        }

        internal static string[] Properties(DriverRequest request) => request.Arguments
            .Where(arg => arg.StartsWith("--property:", StringComparison.Ordinal) &&
                !arg.StartsWith("--property:OutputPath=", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private sealed class ExclusiveCreateIo(string protectedPath) : ForwardingNativeIo, ICheckpointCleanupIo
    {
        private Stream? _exclusive;
        internal int ExclusivePathProbes { get; private set; }
        CheckpointPlatform ICheckpointCleanupIo.Platform => CheckpointPlatform.Windows;

        public override Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            var opened = base.Open(path, mode, access, share);
            if (path == protectedPath && share == FileShare.None)
                _exclusive = opened;
            return opened;
        }

        CheckpointStat ICheckpointCleanupIo.CustodyStat(string path)
        {
            if (path != protectedPath || _exclusive?.CanWrite != true)
                return base.CustodyStat(path);
            ExclusivePathProbes++;
            return new CheckpointStat("unknown", 0, 0, 0, 0, 0);
        }
    }

    private sealed class DigestSealIo(CheckpointCleanupContext context) : CheckpointIntermediateCache.SealIo
    {
        internal override CheckpointCleanupContext ReadContext => context;
    }

    private sealed class CancelOnDigestIo(CancellationTokenSource cancel) : ForwardingNativeIo
    {
        private readonly TaskCompletionSource<bool> _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _membersOpened;
        private int _readStarted;
        internal int MembersOpened => Volatile.Read(ref _membersOpened);
        internal Task<bool> FirstReadSawCancellation => _firstRead.Task;

        public override ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request)
        {
            var handle = base.OpenRead(path, request);
            Interlocked.Increment(ref _membersOpened);
            return new MemberHandle(handle, CancelFirstRead);
        }

        private void CancelFirstRead(CancellationToken readToken)
        {
            if (Interlocked.Exchange(ref _readStarted, 1) != 0)
                return;
            cancel.Cancel();
            _firstRead.TrySetResult(readToken.IsCancellationRequested);
        }

        private sealed class MemberHandle(ICheckpointReadHandle inner, Action<CancellationToken> onRead) : ICheckpointReadHandle
        {
            public CheckpointHandleStat Inspect() => inner.Inspect();
            public Stream OpenStream() => new MemberStream(inner.OpenStream(), onRead);
            public void Dispose() => inner.Dispose();
        }

        private sealed class MemberStream(Stream inner, Action<CancellationToken> onRead) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                onRead(cancellationToken);
                return inner.ReadAsync(buffer, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private sealed class SealDriver(SealFixture fixture) : IConfinedDriver
    {
        internal bool Confine { get; init; }
        internal Func<DriverRequest, CancellationToken, Task<DriverResult>>? OnBuild { get; init; }
        internal Func<DriverRequest, CancellationToken, Task>? OnRow { get; init; }
        internal List<DriverRequest> Builds { get; } = [];
        internal List<DriverRequest> Rows { get; } = [];
        internal List<BuildConfinement.Ledger> Captures { get; } = [];

        public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request)
        {
            if (!Confine) return BuildConfinement.LedgerCapture.None;
            var capture = BuildConfinement.CaptureLedger(request.WorkingDirectory, request.Arguments);
            capture.Refusal.ShouldBeNull(capture.Detail);
            Captures.Add(capture.Value.ShouldNotBeNull());
            return capture;
        }

        public async Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
        {
            if (CheckpointFixtures.IsBuild(request))
            {
                Builds.Add(request);
                var warm = request.Arguments.Any(arg => arg == "--property:" + CheckpointIntermediateCache.RootProperty + "=" + fixture.Partition);
                if (OnBuild is not null)
                {
                    var result = await OnBuild(request, cancellationToken);
                    if (result.ExitCode != 0) return result;
                }
                if (warm)
                    File.WriteAllText(fixture.Asset, "built intermediates");
                var binary = SealFixture.Binary(request);
                File.WriteAllText(binary, "checkpoint test build");
                return new DriverResult(0, "", "") { Exited = CheckpointFixtures.AfterFileClock(binary) };
            }

            Rows.Add(request);
            if (OnRow is not null) await OnRow(request, cancellationToken);
            File.ReadAllText(SealFixture.Binary(request)).ShouldBe("checkpoint test build");
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return new DriverResult(0, "", "") { Exited = DateTimeOffset.UtcNow };
        }

        public void Kill(bool entireProcessTree) { }
    }

    private sealed class SealSlots(Func<string, CancellationToken, Task<SlotLease>>? onBuild = null) : IBuildSlotClient
    {
        internal int BuildGrants { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("enabled", 4));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            if (!label.StartsWith("build:", StringComparison.Ordinal)) return Grant();
            var lease = onBuild is null ? Grant() : await onBuild(label, cancellationToken);
            if (lease.State == "granted") BuildGrants++;
            return lease;
        }
        internal static SlotLease Grant() => new() { State = "granted", LeaseId = "fixture", MaxCpuCount = 4 };
    }

    private sealed class RecordingProcessFactory(bool runChildren = false) : IProcessHandleFactory
    {
        private bool RunChildren => runChildren;
        internal List<DriverRequest> Prepared { get; } = [];
        internal List<DriverRequest> Started { get; } = [];

        public IProcessHandle Create(ProcessStartInfo startInfo)
        {
            var request = new DriverRequest(startInfo.FileName, startInfo.ArgumentList.ToArray(), startInfo.WorkingDirectory);
            Prepared.Add(request);
            return new RecordedProcess(this, request);
        }

        private sealed class RecordedProcess(RecordingProcessFactory owner, DriverRequest request) : IProcessHandle
        {
            public bool Start()
            {
                owner.Started.Add(request);
                if (!owner.RunChildren)
                    return false;
                if (CheckpointFixtures.IsBuild(request))
                    File.WriteAllText(SealFixture.Binary(request), "checkpoint test build");
                else if (CheckpointFixtures.IsRun(request))
                {
                    File.ReadAllText(SealFixture.Binary(request)).ShouldBe("checkpoint test build");
                    CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
                }
                return true;
            }
            public void BeginRead(Action<string?> stdout, Action<string?> stderr) { }
            public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public bool HasExited => true;
            public int ExitCode => 0;
            public void Dispose() { }
            public void Kill(bool entireProcessTree) { }
        }
    }

    private sealed class FaultSealIo(string fault) : CheckpointIntermediateCache.SealIo
    {
        private FileStream? publication;
        internal int Faults { get; private set; }
        internal int PublicationWrites { get; private set; }
        internal int PublicationCloses { get; private set; }
        internal SafeFileHandle? PublicationHandle { get; private set; }
        internal byte[]? AcceptedDigest { get; private set; }
        internal Action? BeforePublication { get; init; }
        internal Action? OnInvalidationRefusal { get; init; }

        internal override void Write(FileStream stream, ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 32)
            {
                PublicationWrites++;
                publication = stream;
                PublicationHandle = stream.SafeFileHandle;
                BeforePublication?.Invoke();
                if (fault == "write-and-close") Fail("write");
            }
            base.Write(stream, payload);
            if (ReferenceEquals(publication, stream)) AcceptedDigest = payload.ToArray();
        }

        internal override void Flush(FileStream stream)
        {
            if (ReferenceEquals(publication, stream) && fault == "flush-and-close") Fail("flush");
            base.Flush(stream);
        }

        internal override void Close(FileStream stream)
        {
            // Report a close failure after releasing the real handle, including a buffered flush if any.
            base.Close(stream);
            if (!ReferenceEquals(publication, stream)) return;
            PublicationCloses++;
            if (fault is "write-and-close" or "flush-and-close" or "close-only") Fail("close");
        }

        internal override void Replace(string temporary, string destination)
        {
            var bytes = File.ReadAllBytes(temporary);
            if (fault == "rename" && bytes.Length == 32)
            {
                bytes.ShouldBe(AcceptedDigest);
                Fail("rename");
            }
            if (fault == "invalidation" && bytes.Length == 0)
            {
                OnInvalidationRefusal?.Invoke();
                Fail("invalidate");
            }
            base.Replace(temporary, destination);
        }

        private void Fail(string operation)
        {
            Faults++;
            throw new IOException("fixture seal " + operation + " failure");
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
