using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677RetentionScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1895. Exact-name output drop on a finished mirror: <c>bin</c>, <c>obj</c>, and <c>bin-*</c> only.
/// Each guard names the retention reason and a directory that must survive. The positive cases require
/// those directories to be gone and the byte count to match the regular files inside them.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(15_000)]
public sealed class C1895OutputDropEngineTests
{
    private const string Execute = WorkspaceOutputDropModes.Execute;

    [Test]
    [Timeout(60_000)]
    public async Task Execute_deletes_allow_listed_output_directories(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        RequireClean(m.Path);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Dropped, response.Detail);
        response.Bytes.ShouldBe(14, "hello + world! + xyz, and the symlink target is not counted");
        nameof(RunnerWorkspaceNoFollowDelete.MeasureOrDelete).ShouldBe("MeasureOrDelete");
        response.Directories.ShouldBe(3);
        response.HeadSha.ShouldBe(m.Head);
        Directory.Exists(planted.Bin).ShouldBeFalse();
        Directory.Exists(planted.Obj).ShouldBeFalse();
        Directory.Exists(planted.BinCp).ShouldBeFalse();
        File.ReadAllText(planted.OutsideFile).ShouldBe("outside-bytes-should-not-count");
        File.ReadAllText(m.Path + "/tests/Foo/binary/keep.txt").ShouldBe("keep");
        File.ReadAllText(m.Path + "/node_modules/pkg/keep.txt").ShouldBe("keep");
        File.ReadAllText(m.Path + "/client/dist/keep.txt").ShouldBe("keep");
        File.ReadAllText(m.Path + "/tests/Foo/obj2/keep.txt").ShouldBe("keep");
        File.ReadAllText(m.Path + "/tests/Foo/bin-/keep.txt").ShouldBe("keep");
        File.ReadAllText(m.Path + "/notes/bin").ShouldBe("not-a-dir");
        File.ReadAllText(m.Path + "/README.md").ShouldBe("c1677");
        File.ReadAllText(m.Path + "/a").ShouldBe("a");
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Dry_run_reports_bytes_and_leaves_the_directories(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        RequireClean(m.Path);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m, WorkspaceOutputDropModes.DryRun), ct);

        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.WouldDrop, response.Detail);
        response.Bytes.ShouldBe(14);
        response.Directories.ShouldBe(3);
        Directory.Exists(planted.Bin).ShouldBeTrue();
        Directory.Exists(planted.Obj).ShouldBeTrue();
        Directory.Exists(planted.BinCp).ShouldBeTrue();
        File.ReadAllText(planted.OutsideFile).ShouldBe("outside-bytes-should-not-count");
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Dirty_tree_is_kept(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        File.WriteAllText(m.Path + "/a", "modified");

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        Kept(s, response, WorkspaceRetentionCodes.Dirty, planted.Bin, m);
    }

    [Test]
    [Arguments("local-commit")]
    [Arguments("origin-behind")]
    [Timeout(60_000)]
    public async Task Unpublished_work_is_kept(string shape, CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        string reason;
        if (shape == "local-commit")
        {
            s.CommitIn(m.Path, "local.txt");
            reason = WorkspaceRetentionCodes.Unpushed;
        }
        else
        {
            s.ResetOriginBehind(m);
            reason = WorkspaceRetentionCodes.OriginDoesNotContainHead;
        }

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        Kept(s, response, reason, planted.Bin, m);
    }

    [Test]
    [Arguments("holder")]
    [Arguments("scan-unknown")]
    [Timeout(60_000)]
    public async Task Holder_keeps_the_outputs(string shape, CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        s.Holders.Next = shape == "holder"
            ? _ => new HolderScan(true, [new WorkspaceSlotHolder(4242, "sleep", "cwd")])
            : _ => new HolderScan(false, []);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        Kept(s, response, shape == "holder" ? WorkspaceRetentionCodes.LiveProcess : WorkspaceRetentionCodes.HolderScanUnknown, planted.Bin, m);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Symlink_named_bin_is_not_followed(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Exclude(m.Path, "bin-*/", "bin-/", "node_modules/", "dist/", "binary/", "obj2/", "notes/bin", "tests/bin");
        var outside = s.Root + "/outside-dir";
        Directory.CreateDirectory(outside);
        File.WriteAllText(outside + "/secret.txt", "keep-me");
        Directory.CreateDirectory(m.Path + "/tests");
        var link = m.Path + "/tests/bin";
        File.CreateSymbolicLink(link, outside);
        var obj = m.Path + "/tests/obj";
        Directory.CreateDirectory(obj);
        File.WriteAllText(obj + "/world.txt", "world!");
        RequireClean(m.Path);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        File.ReadAllText(outside + "/secret.txt").ShouldBe("keep-me");
        new FileInfo(link).LinkTarget.ShouldNotBeNull("the bin link stays");
        Directory.Exists(obj).ShouldBeFalse("the real obj beside the link is output");
        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Dropped, response.Detail);
        response.Bytes.ShouldBe(6);
        response.Directories.ShouldBe(1);
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Tracked_content_under_bin_is_kept(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Directory.CreateDirectory(m.Path + "/src/bin");
        File.WriteAllText(m.Path + "/src/bin/tracked.txt", "tracked");
        Git(m.Path, "add", "-f", "src/bin/tracked.txt");
        Git(m.Path, "commit", "-q", "-m", "track bin");
        Git(m.Path, "push", "-q", s.OriginGit, "HEAD:refs/heads/" + m.Branch);
        Git(m.Path, "fetch", "origin", "refs/heads/" + m.Branch + ":refs/remotes/origin/" + m.Branch);
        var obj = m.Path + "/tests/obj";
        Directory.CreateDirectory(obj);
        File.WriteAllText(obj + "/world.txt", "world!");
        RequireClean(m.Path);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        File.ReadAllText(m.Path + "/src/bin/tracked.txt").ShouldBe("tracked");
        Directory.Exists(obj).ShouldBeFalse();
        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Dropped, response.Detail);
        response.Bytes.ShouldBe(6);
        response.Directories.ShouldBe(1);
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Arguments("outside-root")]
    [Arguments("nested-under-root")]
    [Arguments("symlinked-leaf")]
    [Arguments("leaf-id-mismatch")]
    [Timeout(60_000)]
    public async Task Confinement_refuses_before_any_delete(string shape, CancellationToken ct)
    {
        using var s = Create();
        MirrorTree m;
        PhoneHomeWorkspaceOutputDropRequest request;
        string? link = null;
        switch (shape)
        {
            case "outside-root":
                m = s.Mirror(path: s.Work + "/elsewhere/task-a1677001");
                request = DropRequest(m);
                break;
            case "nested-under-root":
                m = s.Mirror(path: s.Worktrees + "/sub/task-a1677001");
                request = DropRequest(m);
                break;
            case "symlinked-leaf":
                m = s.Mirror(path: s.Work + "/real/task-a1677001");
                link = s.Worktrees + "/task-a1677001";
                File.CreateSymbolicLink(link, m.Path);
                request = DropRequest(m, path: link);
                break;
            default:
                m = s.Mirror("b1677002");
                request = DropRequest(m, taskId: TaskOf("a1677001"));
                break;
        }

        var planted = PlantOutputs(m, s.Root);
        var response = await s.Engine().DropOutputsAsync(request, ct);

        Kept(s, response, WorkspaceRetentionCodes.OutsideScope, planted.Bin, m);
        if (link is not null)
            new FileInfo(link).LinkTarget.ShouldNotBeNull("the leaf link is untouched");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Slot_leaf_is_kept(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror(path: s.Worktrees + "/ws-03", branch: "feat/card-task-a1677001");
        var planted = PlantOutputs(m, s.Root);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        Kept(s, response, WorkspaceRetentionCodes.SlotManaged, planted.Bin, m);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Control_character_path_is_kept(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m, path: m.Path + "\n"), ct);

        Kept(s, response, WorkspaceRetentionCodes.NameControlCharacter, planted.Bin, m);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Unknown_mode_deletes_nothing(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m, mode: "Nope"), ct);

        Kept(s, response, WorkspaceRetentionCodes.OutsideScope, planted.Bin, m);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Capability_is_advertised_only_on_linux(bool isLinux)
    {
        var features = PhoneHomeRuntimeAdapter.RetentionFeatures(isLinux);
        if (isLinux)
            features.ShouldContain(RunnerCapabilityFeatures.WorkspaceOutputDropV1);
        else
            features.ShouldNotContain(RunnerCapabilityFeatures.WorkspaceOutputDropV1);
        await Task.CompletedTask;
    }

    [Test]
    [Timeout(60_000)]
    public async Task Proven_output_is_deleted_and_foreign_directories_stay(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Exclude(m.Path, "bin-*/", "node_modules/", "dist/", "vendor/", "*.csproj", "Directory.Build.*", "tests/Linked/bin");
        var legitimate = m.Path + "/tests/Foo/bin";
        Directory.CreateDirectory(legitimate);
        File.WriteAllText(m.Path + "/tests/Foo/Foo.csproj", "<Project />");
        File.WriteAllText(legitimate + "/hello.txt", "hello");
        var named = m.Path + "/tests/Bar/bin-tool";
        Directory.CreateDirectory(named);
        File.WriteAllText(m.Path + "/tests/Bar/Directory.Build.props", "<Project />");
        File.WriteAllText(named + "/x.txt", "xyz");
        var packageBin = m.Path + "/node_modules/pkg/bin";
        Directory.CreateDirectory(packageBin);
        File.WriteAllText(packageBin + "/cli.js", "package-source");
        var distBin = m.Path + "/dist/tool/bin";
        Directory.CreateDirectory(distBin);
        File.WriteAllText(distBin + "/script.js", "dist-source");
        var orphan = m.Path + "/tests/orphan/bin";
        Directory.CreateDirectory(orphan);
        File.WriteAllText(orphan + "/keep.txt", "no-project");
        var outside = s.Root + "/linked-bin";
        Directory.CreateDirectory(outside);
        File.WriteAllText(outside + "/secret.txt", "symlink-target");
        Directory.CreateDirectory(m.Path + "/tests/Linked");
        File.WriteAllText(m.Path + "/tests/Linked/Linked.csproj", "<Project />");
        File.CreateSymbolicLink(m.Path + "/tests/Linked/bin", outside);
        var nestedBin = m.Path + "/vendor/pkg/bin";
        Directory.CreateDirectory(nestedBin);
        File.WriteAllText(m.Path + "/vendor/pkg/Pkg.csproj", "<Project />");
        File.WriteAllText(nestedBin + "/tracked.txt", "nested-tracked");
        Git(m.Path + "/vendor", "init", "-q", "--initial-branch=main");
        Git(m.Path + "/vendor", "config", "user.email", "c1895@localhost");
        Git(m.Path + "/vendor", "config", "user.name", "c1895");
        Git(m.Path + "/vendor", "add", "-A");
        Git(m.Path + "/vendor", "commit", "-q", "-m", "nested");
        RequireClean(m.Path);

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        Directory.Exists(legitimate).ShouldBeFalse("a bin beside a sibling csproj is output");
        Directory.Exists(named).ShouldBeFalse("a bin-* beside Directory.Build.props is output");
        File.ReadAllText(packageBin + "/cli.js").ShouldBe("package-source");
        File.ReadAllText(distBin + "/script.js").ShouldBe("dist-source");
        File.ReadAllText(orphan + "/keep.txt").ShouldBe("no-project");
        File.ReadAllText(outside + "/secret.txt").ShouldBe("symlink-target");
        new FileInfo(m.Path + "/tests/Linked/bin").LinkTarget.ShouldNotBeNull();
        File.ReadAllText(nestedBin + "/tracked.txt").ShouldBe("nested-tracked");
        Git(m.Path + "/vendor", "ls-files", "--error-unmatch", "--", "pkg/bin/tracked.txt").Trim().ShouldBe("pkg/bin/tracked.txt");
        response.Bytes.ShouldBe(8, "hello + xyz");
        response.Directories.ShouldBe(2);
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Tracked_file_added_after_inspection_is_not_deleted(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Exclude(m.Path, "*.csproj");
        var obj = m.Path + "/tests/Foo/obj";
        Directory.CreateDirectory(obj);
        File.WriteAllText(m.Path + "/tests/Foo/Foo.csproj", "<Project />");
        File.WriteAllText(obj + "/world.txt", "world!");
        RequireClean(m.Path);
        var scans = 0;
        s.Holders.Next = _ =>
        {
            if (++scans >= 2 && !File.Exists(obj + "/now-tracked.txt"))
            {
                File.WriteAllText(obj + "/now-tracked.txt", "late");
                Git(m.Path, "add", "-f", "tests/Foo/obj/now-tracked.txt");
            }
            return new HolderScan(true, []);
        };

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        File.Exists(obj + "/now-tracked.txt").ShouldBeTrue("a file tracked after inspection must survive");
        File.ReadAllText(obj + "/now-tracked.txt").ShouldBe("late");
        Git(m.Path, "ls-files", "--error-unmatch", "--", "tests/Foo/obj/now-tracked.txt").Trim()
            .ShouldBe("tests/Foo/obj/now-tracked.txt");
        Directory.Exists(obj).ShouldBeTrue("the directory gained tracked content before delete");
        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Kept, response.Detail);
        response.Reason.ShouldBe("changed_during_drop", response.Detail);
        (response.Bytes ?? 0).ShouldBe(0);
        s.MutatingVerbs().ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Untracked_file_added_after_inspection_stops_the_drop(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Exclude(m.Path, "*.csproj");
        var bin = m.Path + "/tests/Foo/bin";
        Directory.CreateDirectory(bin);
        File.WriteAllText(m.Path + "/tests/Foo/Foo.csproj", "<Project />");
        File.WriteAllText(bin + "/hello.txt", "hello");
        RequireClean(m.Path);
        var scans = 0;
        s.Holders.Next = _ =>
        {
            if (++scans >= 2 && !File.Exists(m.Path + "/notes.txt"))
                File.WriteAllText(m.Path + "/notes.txt", "late-untracked");
            return new HolderScan(true, []);
        };

        var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

        File.ReadAllText(m.Path + "/notes.txt").ShouldBe("late-untracked");
        Directory.Exists(bin).ShouldBeTrue();
        File.ReadAllText(bin + "/hello.txt").ShouldBe("hello");
        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Kept, response.Detail);
        response.Reason.ShouldBe("changed_during_drop", response.Detail);
        (response.Bytes ?? 0).ShouldBe(0);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Partial_unlink_reports_the_bytes_actually_removed(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        Exclude(m.Path, "*.csproj");
        var obj = m.Path + "/tests/Foo/obj";
        var blocked = obj + "/zdir";
        Directory.CreateDirectory(blocked);
        File.WriteAllText(m.Path + "/tests/Foo/Foo.csproj", "<Project />");
        File.WriteAllText(obj + "/a.dll", "abcdef");
        File.WriteAllText(blocked + "/b.dll", "blocked");
        RequireClean(m.Path);
        var scans = 0;
        s.Holders.Next = _ =>
        {
            if (++scans >= 2)
                File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return new HolderScan(true, []);
        };

        try
        {
            var response = await s.Engine().DropOutputsAsync(DropRequest(m), ct);

            response.Verdict.ShouldBe(WorkspaceOutputDropCodes.DeleteFailed, response.Detail);
            response.Bytes.ShouldBe(6, "a.dll was unlinked before zdir refused");
            File.Exists(obj + "/a.dll").ShouldBeFalse();
            File.ReadAllText(blocked + "/b.dll").ShouldBe("blocked");
            Directory.Exists(obj).ShouldBeTrue();
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Dispatcher_routes_execute_to_the_engine(CancellationToken ct)
    {
        using var s = Create();
        var m = s.Mirror();
        var planted = PlantOutputs(m, s.Root);
        RequireClean(m.Path);
        var settings = new PhoneHomeSettings
        {
            Enabled = true, AllowedCwd = s.Work, Capacity = 2, CapacityStatePath = s.Root + "/capacity",
            StoreIdPath = s.Root + "/runner-store-id", RunnerRepository = s.Repository, RunnerCloneSource = s.OriginGit,
            AllowedCloneSources = [s.OriginGit],
        };
        var dispatcher = new PhoneHomeCommandDispatcher(new DropRuntime(s.Engine()), settings);
        dispatcher.UseWorkspace(s.Service());
        var frame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceDropOutputs,
            JsonSerializer.SerializeToElement(DropRequest(m), PhoneHomeFraming.Json));

        var response = Read(await dispatcher.DispatchAsync(frame, ct));

        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Dropped, response.Detail);
        response.Bytes.ShouldBe(14);
        Directory.Exists(planted.Bin).ShouldBeFalse();
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    private static PhoneHomeWorkspaceOutputDropRequest DropRequest(MirrorTree m, string mode = Execute,
        string? path = null, Guid? taskId = null) =>
        new(taskId ?? m.TaskId, path ?? m.Path, m.Branch, mode, Guid.NewGuid());

    private static void Kept(C1677RetentionScratch s, PhoneHomeWorkspaceOutputDropResponse response, string reason,
        string bin, MirrorTree m)
    {
        response.Verdict.ShouldBe(WorkspaceOutputDropCodes.Kept, response.Detail);
        response.Reason.ShouldBe(reason, response.Detail);
        Directory.Exists(bin).ShouldBeTrue("refused output stays");
        Directory.Exists(m.Path).ShouldBeTrue("the tree stays");
        s.Registered(m.Path).ShouldBeTrue();
        s.MutatingVerbs().ShouldBeEmpty();
    }

    private static Planted PlantOutputs(MirrorTree m, string scratchRoot)
    {
        Exclude(m.Path, "bin-*/", "bin-/", "node_modules/", "dist/", "binary/", "obj2/", "notes/bin");
        var bin = m.Path + "/tests/Foo/bin";
        var obj = m.Path + "/tests/Foo/obj";
        var binCp = m.Path + "/tests/Foo/bin-cp1";
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(obj);
        Directory.CreateDirectory(binCp);
        File.WriteAllText(bin + "/hello.txt", "hello");
        var outside = scratchRoot + "/outside-file.txt";
        File.WriteAllText(outside, "outside-bytes-should-not-count");
        File.CreateSymbolicLink(bin + "/link", outside);
        File.WriteAllText(obj + "/world.txt", "world!");
        File.WriteAllText(binCp + "/x.txt", "xyz");
        Directory.CreateDirectory(m.Path + "/tests/Foo/binary");
        File.WriteAllText(m.Path + "/tests/Foo/binary/keep.txt", "keep");
        Directory.CreateDirectory(m.Path + "/node_modules/pkg");
        File.WriteAllText(m.Path + "/node_modules/pkg/keep.txt", "keep");
        Directory.CreateDirectory(m.Path + "/client/dist");
        File.WriteAllText(m.Path + "/client/dist/keep.txt", "keep");
        Directory.CreateDirectory(m.Path + "/tests/Foo/obj2");
        File.WriteAllText(m.Path + "/tests/Foo/obj2/keep.txt", "keep");
        Directory.CreateDirectory(m.Path + "/tests/Foo/bin-");
        File.WriteAllText(m.Path + "/tests/Foo/bin-/keep.txt", "keep");
        Directory.CreateDirectory(m.Path + "/notes");
        File.WriteAllText(m.Path + "/notes/bin", "not-a-dir");
        return new Planted(bin, obj, binCp, outside);
    }

    private static void Exclude(string worktree, params string[] lines)
    {
        var raw = Git(worktree, "rev-parse", "--path-format=absolute", "--git-path", "info/exclude").Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(raw)!);
        var existing = File.Exists(raw) ? File.ReadAllText(raw) : "";
        if (existing.Length > 0 && !existing.EndsWith('\n'))
            existing += "\n";
        File.WriteAllText(raw, existing + string.Join('\n', lines) + "\n");
    }

    private static void RequireClean(string path)
    {
        Git(path, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none")
            .Trim().ShouldBeEmpty("the fixture must be a clean published tree");
    }

    private static PhoneHomeWorkspaceOutputDropResponse Read(PhoneHomeFrame frame)
    {
        frame.Kind.ShouldBe(PhoneHomeFrameKind.Result, frame.ErrorDetail);
        return frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceOutputDropResponse>(PhoneHomeFraming.Json)!;
    }

    private sealed record Planted(string Bin, string Obj, string BinCp, string OutsideFile);

    /// <summary>Routes retention calls at the real engine. Output drop uses the interface default until the dispatcher grows the operation.</summary>
    private sealed class DropRuntime(RunnerWorkspaceRetention engine) : IPhoneHomeRuntimeSurface
    {
        public Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct) =>
            engine.CheckAndMoveAsync(request, ct);

        public Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct) =>
            engine.CompleteAsync(step, ct);

        public Task<WorkspaceOutputDropStep> CheckWorkspaceOutputsAsync(PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct) =>
            engine.CheckOutputsAsync(request, ct);

        public Task<PhoneHomeWorkspaceOutputDropResponse> ApplyWorkspaceOutputsAsync(WorkspaceOutputDropStep step, CancellationToken ct) =>
            engine.ApplyOutputsAsync(step, ct);

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false,
                Features: [RunnerCapabilityFeatures.WorkspaceRetentionV1, RunnerCapabilityFeatures.WorkspaceOutputDropV1]);
        public int OwnedSessionCount => 0;
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new RunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }
}
