using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class C1307ToolRemovalTests
{
    private static string Marker(HostCleanupToolFixture f) => Path.Combine(f.Run, "tool-removal-started.json");
    private static string[] Unlinks(HostCleanupToolFixture f) => f.Io.Events
        .Where(e => e.Operation == "unlink" && f.Context.IsStrictlyInside(f.Tool, e.Path))
        .Select(e => e.Path).ToArray();

    private static HostCleanupToolFixture Sealed()
    {
        var f = new HostCleanupToolFixture();
        SafeWrite.Require(f.Files.Write.Text(f.Tool + "/second.dll", "second-sentinel", f.Files.Write.Under(f.Tool)));
        f.Owner.ToolCopy = ToolCopyAuthority.Seal(f.Run, f.Owner, f.Context);
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.ClearEvents();
        return f;
    }

    [Test]
    public async Task Mid_removal_cancellation_leaves_marker()
    {
        var f = Sealed();
        // A real clock also lets the baseline's obsolete retry finish.
        var fresh = new CheckpointCleanupContext(f.Io, f.Io);
        using var cancel = new CancellationTokenSource();
        var stream = BlockAfterFirstUnlink(f);
        var removing = Task.Run(() => f.Cleanup(fresh).Remove(f.Run, cancel.Token));
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deleted = Unlinks(f).ShouldHaveSingleItem();
            f.Io.FileExists(deleted).ShouldBeFalse("one actual tool unlink precedes cancellation");
            var remaining = ToolBytes(f);
            cancel.Cancel();
            var receipt = await removing.WaitAsync(TimeSpan.FromSeconds(5));
            f.Io.FileExists(Marker(f)).ShouldBeTrue("removal must publish its durable attempt marker");
            AssertIncomplete(f, receipt);
            var publish = f.Io.Events.FindIndex(e => e.Operation == "move" && e.Path == Marker(f));
            var unlink = f.Io.Events.FindIndex(e => e.Operation == "unlink" && e.Path == deleted);
            publish.ShouldBeGreaterThanOrEqualTo(0);
            publish.ShouldBeLessThan(unlink);
            AssertBytes(f, remaining);
            receipt.Reason.ShouldContain("receipt-read-refused");
            f.Io.StreamFactory = null;
            f.Io.Before = null;
            f.Receipt.ClearEvents();
            AssertIncomplete(f, f.Cleanup(f.Files.FreshContext()).Remove(f.Run));
            Unlinks(f).ShouldBeEmpty();
            AssertBytes(f, remaining);
        }
        finally
        {
            cancel.Cancel();
            stream.Release.TrySetResult();
            await removing;
        }
    }

    [Test]
    public async Task Read_deadline_leaves_marker()
    {
        var f = Sealed();
        var stream = BlockAfterFirstUnlink(f);
        var removing = Task.Run(() => f.Cleanup(f.Files.FreshContext()).Remove(f.Run));
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deleted = Unlinks(f).ShouldHaveSingleItem();
            f.Io.FileExists(deleted).ShouldBeFalse();
            var remaining = ToolBytes(f);
            f.Files.Clock.Advance(TimeSpan.FromSeconds(3));
            var receipt = await removing.WaitAsync(TimeSpan.FromSeconds(5));
            AssertIncomplete(f, receipt);
            receipt.Reason.ShouldContain("receipt-read-refused");
            f.Io.FileExists(Marker(f)).ShouldBeTrue();
            AssertBytes(f, remaining);
            Unlinks(f).Length.ShouldBe(1);
        }
        finally
        {
            stream.Release.TrySetResult();
            await removing;
        }
    }

    [Test]
    public void Marker_failure_prevents_tool_unlink()
    {
        foreach (var variant in new[] { "write", "foreign", "invalid-version", "directory", "linked" })
        {
            var f = Sealed();
            var before = ToolBytes(f);
            if (variant == "foreign") f.Io.Put(Marker(f), "foreign-marker");
            if (variant == "invalid-version") f.Io.Put(Marker(f), "{\"version\":\"1\"}");
            if (variant == "directory") f.Io.CreateDirectory(Marker(f));
            if (variant == "linked")
            {
                f.Io.Put(f.Files.Parent + "/outside", "outside-sentinel");
                var node = f.Io.Put(Marker(f), "linked-marker");
                node.Target = f.Files.Parent + "/outside";
                node.Stat = node.Stat with { Kind = "symlink", Mode = 0xA180 };
            }
            f.Io.Before = (operation, path) =>
            {
                if (variant == "write" && operation == "write" && path.StartsWith(Marker(f) + ".", StringComparison.Ordinal))
                    throw new IOException("marker-write-failed");
            };
            var receipt = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
            receipt.Removed.ShouldBeFalse(variant);
            Unlinks(f).ShouldBeEmpty(variant);
            AssertBytes(f, before);
            if (variant == "foreign") f.Io.Text(Marker(f)).ShouldBe("foreign-marker");
            if (variant == "linked") f.Io.Text(f.Files.Parent + "/outside").ShouldBe("outside-sentinel");
        }
    }

    [Test]
    public void Custody_refusal_preserves_sentinels()
    {
        foreach (var variant in new[] { "changed", "added", "linked", "live", "unknown" })
        {
            var f = Sealed();
            if (variant == "changed") f.Io.Put(f.Payload, "changed-sentinel");
            if (variant == "added") f.Io.Put(f.Tool + "/foreign", "added-sentinel");
            if (variant == "linked")
            {
                f.Io.Put(f.Files.Parent + "/outside", "outside-sentinel");
                f.Io.Nodes[f.Payload].Target = f.Files.Parent + "/outside";
                f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Kind = "symlink", Mode = 0xA180 };
            }
            if (variant == "live") f.Io.Processes[f.Dead.Pid] = new(f.Dead.StartUtcTicks, 'R', false);
            if (variant == "unknown")
            {
                f.Owner.Launched = f.Dead with { Host = "other-host" };
                RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
            }
            var before = ToolBytes(f);
            var receipt = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
            receipt.Removed.ShouldBeFalse(variant);
            Unlinks(f).ShouldBeEmpty(variant);
            f.Io.FileExists(Marker(f)).ShouldBeFalse(variant);
            AssertBytes(f, before);
            if (variant == "linked") f.Io.Text(f.Files.Parent + "/outside").ShouldBe("outside-sentinel");
        }
    }

    [Test]
    public void Complete_removal_and_manual_recovery_are_absent()
    {
        var complete = Sealed();
        complete.Cleanup(complete.Files.FreshContext()).Remove(complete.Run).Outcome.ShouldBe("Removed");
        complete.Io.DirectoryExists(complete.Tool).ShouldBeFalse();
        complete.Io.FileExists(Marker(complete)).ShouldBeTrue();
        using (var json = JsonDocument.Parse(complete.Io.Text(Marker(complete))))
        {
            json.RootElement.GetProperty("version").GetInt32().ShouldBe(1);
            json.RootElement.GetProperty("runId").GetString().ShouldBe(complete.Owner.RunId);
            json.RootElement.GetProperty("toolPath").GetString().ShouldBe(complete.Tool);
            json.RootElement.GetProperty("authorityDigest").GetString().ShouldBe(complete.Owner.ToolCopy!.Value.Sha256);
        }
        complete.Cleanup(complete.Files.FreshContext()).Remove(complete.Run).Outcome.ShouldBe("AlreadyAbsent");

        var partial = Sealed();
        // Change an entry only after full authority admission and marker publication.
        var receipt = partial.Cleanup(partial.Files.FreshContext(), _ => partial.Io.Put(partial.Payload, "changed-residue"))
            .Remove(partial.Run);
        AssertIncomplete(partial, receipt);
        partial.Io.FileExists(Marker(partial)).ShouldBeTrue();
        var marker = partial.Io.Text(Marker(partial));
        var authority = partial.Io.Text(partial.Run + "/tool-copy-authority.json");
        var owner = partial.Io.Text(partial.Run + "/ownership.json");
        partial.Receipt.ClearEvents();
        AssertIncomplete(partial, partial.Cleanup(partial.Files.FreshContext()).Remove(partial.Run));
        Unlinks(partial).ShouldBeEmpty();
        // Simulate the operator's inspected, exact residue removal; keep control records.
        foreach (var path in partial.Io.Enumerate(partial.Tool).ToArray()) partial.Io.Unlink(path);
        partial.Io.RemoveDirectory(partial.Tool, recursive: false);
        partial.Cleanup(partial.Files.FreshContext()).Remove(partial.Run).Outcome.ShouldBe("AlreadyAbsent");
        partial.Io.Text(Marker(partial)).ShouldBe(marker);
        partial.Io.Text(partial.Run + "/tool-copy-authority.json").ShouldBe(authority);
        partial.Io.Text(partial.Run + "/ownership.json").ShouldBe(owner);
        partial.Io.FileExists(partial.Receipt.StatePath).ShouldBeTrue();
        partial.Io.FileExists(partial.Receipt.JsonPath).ShouldBeTrue();
    }

    private static EnteredRead BlockAfterFirstUnlink(HostCleanupToolFixture f)
    {
        var stream = new EnteredRead();
        f.Io.Before = (operation, path) =>
        {
            if (operation == "stream" && f.Context.IsStrictlyInside(f.Tool, path) && Unlinks(f).Length == 1)
                f.Io.StreamFactory = () => stream;
        };
        return stream;
    }

    private static Dictionary<string, byte[]> ToolBytes(HostCleanupToolFixture f) => f.Io.Nodes
        .Where(pair => f.Context.IsStrictlyInside(f.Tool, pair.Key))
        .ToDictionary(pair => pair.Key, pair => pair.Value.Bytes.ToArray());

    private static void AssertBytes(HostCleanupToolFixture f, Dictionary<string, byte[]> expected)
    {
        foreach (var (path, bytes) in expected) f.Io.Nodes[path].Bytes.ShouldBe(bytes, path);
    }

    private static void AssertIncomplete(HostCleanupToolFixture f, CleanupReceipt receipt)
    {
        receipt.Outcome.ShouldBe("Refused");
        receipt.Removed.ShouldBeFalse();
        receipt.Reason.ShouldContain("tool-removal-incomplete");
        receipt.Reason.ShouldContain("may be partially removed");
        receipt.Reason.ShouldContain("manual inspection");
        receipt.Reason.ShouldContain(f.Tool);
        receipt.Reason.ShouldContain(Marker(f));
    }

    private sealed class EnteredRead : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }
}
