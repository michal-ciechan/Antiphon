using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            f.Files.Clock.Advance(CheckpointFileReader.StallDeadline);
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

    [Test]
    public async Task Legacy_unbound_record_is_removed_by_the_base_path_delete()
    {
        var f = Sealed();
        StripIdentity(f);
        var loaded = ToolCopyAuthority.Load(f.Run, f.Owner, out var loadRefusal, f.Files.FreshContext());
        loaded.ShouldNotBeNull(loadRefusal);
        loadRefusal.ShouldBeNull();
        var exit = await f.Wait(f.Files.FreshContext());
        exit.ShouldBe(ExitCodes.Green, f.LastWaitOutput.ToString());
        f.LastWaitOutput.ToString().ShouldContain("legacy-unbound-record");
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        f.Io.FileExists(Marker(f)).ShouldBeTrue();
        var again = await f.Wait(f.Files.FreshContext());
        again.ShouldBe(ExitCodes.Green, f.LastWaitOutput.ToString());
        f.LastWaitOutput.ToString().ShouldNotContain("previous-removal-attempt");
    }

    [Test]
    public void Bound_record_uses_the_anchored_delete()
    {
        var f = Sealed();
        var authority = ToolCopyAuthority.Load(f.Run, f.Owner, out var refusal, f.Files.FreshContext());
        authority.ShouldNotBeNull(refusal);
        var payload = f.Io.Nodes[f.Payload];
        payload.Stat = payload.Stat with { Created = payload.Stat.Created + 1 };
        Should.Throw<IOException>(() => authority.Remove(f.Tool)).Message.ShouldBe("anchored-identity-mismatch");
        f.Io.Text(f.Payload).ShouldBe("keep");
        f.Io.Text(Path.Combine(f.Tool, "second.dll")).ShouldBe("second-sentinel");
    }

    [Test]
    public void Bound_parent_swap_is_refused_before_any_marker()
    {
        var f = Sealed();
        var outside = Path.Combine(f.Files.Parent, "outside");
        f.Io.CreateDirectory(outside);
        f.Io.Put(Path.Combine(outside, "payload.txt"), "foreign-sentinel");
        f.Io.Nodes[f.Tool].Target = outside;
        var receipt = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
        receipt.Removed.ShouldBeFalse();
        receipt.Reason.ShouldNotContain("may be partially removed");
        receipt.Reason.ShouldNotContain("tool-removal-incomplete");
        receipt.Reason.ShouldNotContain("previous-removal-attempt");
        f.Io.FileExists(Marker(f)).ShouldBeFalse();
        f.Io.Text(f.Payload).ShouldBe("keep");
        f.Io.Text(Path.Combine(outside, "payload.txt")).ShouldBe("foreign-sentinel");
        var again = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
        again.Reason.ShouldNotContain("previous-removal-attempt");
        f.Io.FileExists(Marker(f)).ShouldBeFalse();
    }

    [Test]
    public void Bound_identity_mismatch_is_refused_before_any_marker()
    {
        var f = Sealed();
        var payload = f.Io.Nodes[f.Payload];
        payload.Stat = payload.Stat with { Created = payload.Stat.Created + 1 };
        var receipt = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
        receipt.Removed.ShouldBeFalse();
        receipt.Reason.ShouldNotContain("may be partially removed");
        receipt.Reason.ShouldNotContain("tool-removal-incomplete");
        receipt.Reason.ShouldNotContain("previous-removal-attempt");
        f.Io.FileExists(Marker(f)).ShouldBeFalse();
        f.Io.Text(f.Payload).ShouldBe("keep");
        var again = f.Cleanup(f.Files.FreshContext()).Remove(f.Run);
        again.Reason.ShouldNotContain("previous-removal-attempt");
        f.Io.FileExists(Marker(f)).ShouldBeFalse();
    }

    private static void StripIdentity(HostCleanupToolFixture f)
    {
        var path = Path.Combine(f.Run, "tool-copy-authority.json");
        var node = JsonNode.Parse(f.Io.Text(path))!.AsObject();
        node.Remove("rootIdentity");
        foreach (var entry in node["entries"]!.AsArray())
            entry!.AsObject().Remove("identity");
        var json = node.ToJsonString();
        f.Io.Put(path, json);
        f.Owner.ToolCopy = new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.ClearEvents();
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
