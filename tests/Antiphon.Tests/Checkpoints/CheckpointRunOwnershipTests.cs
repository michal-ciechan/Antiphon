using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointRunOwnershipTests : CheckpointTestBase
{
    [Test]
    public void only_exact_direct_child_names_are_candidates()
    {
        var sandbox = TempDir();
        var names = new[] { "c723-short", "c723-" + new string('A', 32), "wrong-" + new string('a', 32) };
        foreach (var name in names) Directory.CreateDirectory(Path.Combine(sandbox, name));
        var nested = Path.Combine(sandbox, "nested", "c723-" + new string('a', 32));
        Directory.CreateDirectory(nested);
        Index(sandbox, names.Select(name => Path.Combine(sandbox, name)).Append(nested));
        var receipt = Sweeper(sandbox).SweepOnce();
        receipt.CompletedRoots.ShouldBe(0);
        foreach (var name in names) Directory.Exists(Path.Combine(sandbox, name)).ShouldBeTrue();
        Directory.Exists(nested).ShouldBeTrue();
    }

    [Test]
    public void unsupported_or_missing_markers_are_retained()
    {
        var sandbox = TempDir();
        var paths = Enumerable.Range(0, 3).Select(_ => Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"))).ToArray();
        foreach (var path in paths) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(paths[1], TestRootGuard.MarkerName), "not-json");
        var unknown = Marker(paths[2]);
        unknown.Version = 99;
        File.WriteAllText(Path.Combine(paths[2], TestRootGuard.MarkerName), JsonSerializer.Serialize(unknown));
        Index(sandbox, paths);
        var receipt = Sweeper(sandbox).SweepOnce();
        receipt.CompletedRoots.ShouldBe(0);
        receipt.Skips["marker-invalid"].ShouldBe(3);
        paths.ShouldAllBe(path => Directory.Exists(path));
    }

    [Test]
    public void marker_binds_root_run_and_normalized_path()
    {
        var run = Run();
        var record = RunOwnershipStore.Read(run)!;
        try
        {
            record.RunId = "other";
            File.WriteAllText(Path.Combine(run, RunOwnershipStore.FileName), JsonSerializer.Serialize(record,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            RunOwnershipStore.Read(run).ShouldBeNull();
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            record.RunId = Path.GetFileName(run);
            record.RunDirectory = Path.Combine(Path.GetDirectoryName(run)!, "sibling");
            File.WriteAllText(Path.Combine(run, RunOwnershipStore.FileName), JsonSerializer.Serialize(record,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            RunOwnershipStore.Read(run).ShouldBeNull();
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        }
        finally { File.Delete(Path.Combine(run, RunOwnershipStore.FileName)); }
    }

    [Test]
    public void lexical_escape_is_rejected()
    {
        var sandbox = TempDir();
        var outside = TempDir();
        var escaped = Path.Combine(sandbox, "..", Path.GetFileName(outside));
        Index(sandbox, [escaped]);
        Sweeper(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
        Directory.Exists(outside).ShouldBeTrue();
    }

    [Test]
    public void linked_root_is_rejected()
    {
        var sandbox = TempDir();
        var target = TempDir();
        var link = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, target);
        try
        {
            Index(sandbox, [link]);
            Sweeper(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
            Directory.Exists(target).ShouldBeTrue();
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public void linked_ancestor_is_rejected()
    {
        var parent = TempDir();
        var actual = Path.Combine(parent, "actual");
        Directory.CreateDirectory(actual);
        var link = Path.Combine(parent, "link");
        Directory.CreateSymbolicLink(link, actual);
        var tool = Path.Combine(actual, "run", "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel"), "keep");
        try
        {
            var receipt = new ToolCopyCleanup().Remove(Path.Combine(link, "run"));
            receipt.Outcome.ShouldBe("Retained");
            File.Exists(Path.Combine(tool, "sentinel")).ShouldBeTrue();
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public void linked_descendant_is_rejected()
    {
        var run = Run();
        var target = TempDir();
        var link = Path.Combine(run, "tool", "linked");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
            Directory.Exists(target).ShouldBeTrue();
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public void incomplete_walk_is_not_empty()
    {
        var run = Run();
        var receipt = new ToolCopyCleanup(maxEntries: 2).Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("inventory-incomplete-or-linked");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void busy_mutation_lock_refuses_cleanup()
    {
        var run = Run();
        using var gate = new FileStream(Path.Combine(run, ".cleanup.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var receipt = new ToolCopyCleanup().Remove(run);
        receipt.Outcome.ShouldBe("Failed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void custody_is_reread_under_the_lock()
    {
        var run = Run();
        var cleanup = new ToolCopyCleanup(beforeLock: () => CheckpointFixtures.MarkRun(run, alive: true));
        var receipt = cleanup.Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("identity-alive");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void deleting_root_rejects_registration()
    {
        var root = TempDir();
        var marker = TestRootGuard.Read(root)!;
        marker.State = "deleting";
        TestRootGuard.Write(root, marker);
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }],
        };
        Should.Throw<IOException>(() => CheckpointApp.CreateRun(manifest, new RunRequest(), root))
            .Message.ShouldContain("sealed");
        Directory.EnumerateFiles(root, "Antiphon.Checkpoints.dll", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public void current_executor_image_is_always_retained()
    {
        var run = Run();
        var tool = Path.Combine(run, "tool");
        var receipt = new ToolCopyCleanup(imageDirectory: tool).Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("current-executor-image");
        Directory.Exists(tool).ShouldBeTrue();
    }

    private string Run()
    {
        var run = TempDir();
        CheckpointFixtures.MarkRun(run, alive: false);
        var tool = Path.Combine(run, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel"), "keep");
        return run;
    }

    private static CheckpointRootMarker Marker(string path) => new()
    {
        RootId = Path.GetFileName(path)[5..], RootPath = Path.GetFullPath(path),
        AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "test",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        Owner = new ProcessIdentityProbe().Current() with { Pid = int.MaxValue, StartUtcTicks = 1 },
    };

    private static CheckpointTempRootSweep Sweeper(string sandbox) => new(sandbox,
        options: new CheckpointSweepOptions { Grace = TimeSpan.Zero, Interval = TimeSpan.Zero });

    private static void Index(string sandbox, IEnumerable<string> paths)
    {
        File.WriteAllLines(Path.Combine(sandbox, ".checkpoint-temp-roots.jsonl"),
            paths.Select(path => JsonSerializer.Serialize(path)));
    }
}
