using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointSweepRaceTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public void vanished_index_entry_does_not_abort_remaining_candidate(bool pendingTemp, bool missingDirectory)
    {
        var f = new HostCleanupCheckpointFixture();
        var root = Path.Combine(f.Parent, "c723-" + Guid.NewGuid().ToString("N"));
        f.Io.CreateDirectory(root);
        f.Io.Put(Path.Combine(root, "payload"), "owned payload");
        TestRootGuard.Write(root, new()
        {
            RootId = Path.GetFileName(root)["c723-".Length..], RootPath = root,
            AttemptId = "virtual-attempt", AssemblyInvocationId = "virtual-assembly",
            CreatedAt = f.Clock.GetUtcNow().AddHours(-1),
            Owner = new(999, 1, f.Io.MachineName, f.Io.BootId, f.Io.PidNamespace)
        }, f.Context);
        var sweep = new CheckpointTempRootSweep(f.Context, f.Parent,
            options: new() { Interval = TimeSpan.Zero });
        sweep.Register(root);

        var unowned = Path.Combine(f.Parent, "c723-" + Guid.NewGuid().ToString("N"));
        f.Io.CreateDirectory(unowned);
        var sentinel = Path.Combine(unowned, "sentinel");
        f.Io.Put(sentinel, "keep");
        var index = Path.Combine(f.Parent, ".checkpoint-temp-roots");
        var ownedEntry = Path.Combine(index, Path.GetFileName(root) + ".json");
        var vanished = Path.Combine(index, Path.GetFileName(unowned) + ".json"
            + (pendingTemp ? "." + Guid.NewGuid().ToString("N") + ".tmp" : ""));
        f.Io.Put(vanished, JsonSerializer.Serialize(unowned));
        f.Io.Events.Clear();
        var listings = 0;
        var removals = 0;
        f.Io.Before = (operation, path) =>
        {
            if (operation == "enumerate" && path == index) listings++;
            // The first listing is stale-temp cleanup, the second the root index.
            // Remove after Enumerate captured its names, immediately before stat.
            if (operation != "attributes" || path != vanished || listings != (pendingTemp ? 1 : 2)) return;
            f.Io.Nodes.Remove(path).ShouldBeTrue("the disappearing entry was in this listing");
            removals++;
            if (missingDirectory) throw new DirectoryNotFoundException(path);
            // VirtualCheckpointIo.Attributes now throws the native FileNotFound shape.
        };

        var receipt = sweep.SweepOnce();

        removals.ShouldBe(1, "the listing/stat race must actually occur");
        listings.ShouldBe(2);
        receipt.Examined.ShouldBe(1, "the surviving candidate must still be reached");
        receipt.CompletedRoots.ShouldBe(1, "proven custody must still permit cleanup");
        f.Io.DirectoryExists(root).ShouldBeFalse();
        f.Io.Text(sentinel).ShouldBe("keep", "an unowned sibling must remain untouched");
        f.Io.Text(f.Payload).ShouldBe("original");
        f.Io.Events.Where(e => e.Operation is "unlink" or "rmdir").ShouldAllBe(e =>
            e.Path == root || e.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || e.Path == ownedEntry, "deletion must stay inside proven custody and its index entry");
        f.Io.RecursiveIntents.ShouldBe(0);
    }
}
