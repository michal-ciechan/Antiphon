using Win32Exception = System.ComponentModel.Win32Exception;
using System.Reflection;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointCustodyTests
{
    [Test]
    public void Creation_registries_are_isolated()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Seal();
        f.Ledger.Check(f.Root).ShouldBeNull("C987.Creation_registries_are_isolated.fresh-context:control");
        new CheckpointOutputLedger(f.FreshContext()).Check(f.Root).ShouldStartWith("output-memory-authority-missing", Case.Sensitive, "C987.Creation_registries_are_isolated.fresh-context");
        f.Ledger.Remove(f.Root).ShouldBeNull("C987.Creation_registries_are_isolated.fresh-context:creator-removes");
        var native = CheckpointCleanupDefaults.Context;
        ReferenceEquals(native.Registry, CheckpointCleanupDefaults.Context.Registry).ShouldBeTrue("C987.Creation_registries_are_isolated.native-facade");
        var key = "/virtual-native-resolution/" + Guid.NewGuid().ToString("N");
        native.Registry.RecordCreated(key);
        SafeWrite.WasCreated(key).ShouldBeTrue("C987.Creation_registries_are_isolated.native-facade:create-match-without-io");
        typeof(CheckpointCleanupDefaults).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.SetMethod is not null).ShouldBeEmpty("C987.Creation_registries_are_isolated.native-override:setter");
        typeof(CheckpointCleanupDefaults).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(p => !p.IsInitOnly).ShouldBeEmpty("C987.Creation_registries_are_isolated.native-override:field");
        typeof(CheckpointCleanupDefaults).GetMethods().Where(m => m.Name.Contains("Reset", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty("C987.Creation_registries_are_isolated.native-override:reset");
        typeof(CheckpointCleanupDefaults).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(p => p.FieldType.IsGenericType && p.FieldType.GetGenericTypeDefinition() == typeof(AsyncLocal<>))
            .ShouldBeEmpty("C987.Creation_registries_are_isolated.native-override:async-local");
        var a = new HostCleanupCheckpointFixture(); a.Seal();
        a.Io.Put(a.Payload, "rewritten"); a.Seal();
        a.Write.MatchesCreation(a.Payload).ShouldBeFalse("C987.Creation_registries_are_isolated.append-only");
    }

    [Test]
    public void Existing_directory_cannot_gain_creation_authority()
    {
        var f = new HostCleanupCheckpointFixture();
        ExclusiveDirectory.Create(f.Context, f.Root).ShouldBe("pre-existing-output", "C987.Existing_directory_cannot_gain_creation_authority.mkdir-exists");
        f.Write.CreateFreshDirectory(f.Root, f.Write.Under(f.Parent)).ShouldBe("pre-existing-output",
            "C987.Existing_directory_cannot_gain_creation_authority.mkdir-exists:creation-entry");
        f.Io.Count("write").ShouldBe(0);
        var foreign = Path.Combine(f.Parent, "foreign"); f.Io.CreateDirectory(foreign);
        f.Write.CreateDirectory(foreign, f.Write.Under(f.Parent)).ShouldBeNull();
        f.Write.WasCreated(foreign).ShouldBeFalse("C987.Existing_directory_cannot_gain_creation_authority.ordinary-existing:directory");
        f.Write.Text(f.Payload, "replace", f.Write.Under(f.Root)).ShouldBe("unrecorded-existing", "C987.Existing_directory_cannot_gain_creation_authority.ordinary-existing");
        f.Io.Text(f.Payload).ShouldBe("original");
        var marker = Path.Combine(f.Root, "marker");
        f.Io.Before = (op, path) => { if (op == "open" && path == marker) f.Io.Put(marker, "foreign-marker"); };
        Should.Throw<IOException>(() => f.Write.OpenCreate(marker, f.Write.Under(f.Root), out _), "C987.Existing_directory_cannot_gain_creation_authority.open-create:race");
        f.Io.Opens.Single().Mode.ShouldBe(FileMode.CreateNew, "C987.Existing_directory_cannot_gain_creation_authority.open-create");
        f.Io.Text(marker).ShouldBe("foreign-marker");
        f.Io.Before = null;
        var destination = Path.Combine(f.Root, "destination");
        f.Io.Before = (op, path) =>
        {
            if (op == "write" && path.EndsWith(".tmp", StringComparison.Ordinal))
            {
                f.Io.Put(destination, "tracked");
                f.Io.GitResult = _ => new(0, destination + "\0", "", false);
            }
        };
        f.Write.Text(destination, "new", f.Write.Under(f.Root)).ShouldBe("tracked", "C987.Existing_directory_cannot_gain_creation_authority.move-recheck");
        f.Io.Count("move", destination).ShouldBe(0, "C987.Existing_directory_cannot_gain_creation_authority.move-recheck:entry");
        f.Io.Text(destination).ShouldBe("tracked");
    }

    [Test]
    public void Disk_ledger_cannot_authorize_generic_delete()
    {
        var f = new HostCleanupCheckpointFixture(); f.Seal();
        var fresh = new CheckpointOutputLedger(f.FreshContext());
        fresh.Check(f.Root).ShouldStartWith("output-memory-authority-missing", Case.Sensitive, "C987.Disk_ledger_cannot_authorize_generic_delete.disk-only");
        fresh.Remove(f.Root).ShouldStartWith("output-memory-authority-missing");
        f.Io.Count("unlink").ShouldBe(0, "C987.Disk_ledger_cannot_authorize_generic_delete.disk-only:no-delete");
        f.Io.Text(f.Payload).ShouldBe("original");
    }

    [Test]
    public void Build_exit_is_required_for_adoption()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Ledger.Seal(f.Root).ShouldBeNull();
        f.Write.MatchesCreation(f.Payload).ShouldBeFalse("C987.Build_exit_is_required_for_adoption.no-exit");
        f.Ledger.Remove(f.Root).ShouldStartWith("output-ledger-mismatch");
        f.Io.Text(f.Payload).ShouldBe("original");
        OutputCustody.For(f.Context, f.Root, f.Write.FreshRootCreated(f.Root)!.Value.AddTicks(-1))
            .ShouldBeNull("C987.Build_exit_is_required_for_adoption.inverted-window");
        OutputCustody.For(f.Context, f.Root, f.Exit).ShouldNotBeNull("C987.Build_exit_is_required_for_adoption.inverted-window:control");
    }

    [Test]
    public void Custody_window_checks_owner_mode_and_times()
    {
        foreach (var variant in new[] { "root-owner", "root-mode", "root-kind" })
        {
            var f = new HostCleanupCheckpointFixture();
            var stat = f.Io.Nodes[f.Root].Stat;
            f.Io.Nodes[f.Root].Stat = variant switch
            {
                "root-owner" => stat with { Owner = stat.Owner + 1 },
                "root-mode" => stat with { Mode = 0x41C1 },
                _ => stat with { Mode = 0x8180, Kind = "file" }
            };
            OutputCustody.For(f.Context, f.Root, f.Exit).ShouldBeNull("C987.Custody_window_checks_owner_mode_and_times." + variant);
            f.Write.MatchesCreation(f.Payload).ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times." + variant + ":no-adoption");
        }
        foreach (var (variant, reason) in new[] { ("entry-owner", "foreign-owner"), ("ctime-before", "changed-before-root"),
            ("ctime-after", "changed-after-build"), ("mtime-after", "modified-after-build"), ("unknown-stat", "unknown") })
        {
            var f = new HostCleanupCheckpointFixture(); var window = f.Window;
            var stat = f.Io.Nodes[f.Payload].Stat;
            var start = (window.RootCreated.UtcTicks - DateTime.UnixEpoch.Ticks) * 100;
            var exit = (window.BuildExited.UtcTicks - DateTime.UnixEpoch.Ticks) * 100;
            f.Io.Nodes[f.Payload].Stat = variant switch
            {
                "entry-owner" => stat with { Owner = stat.Owner + 1 },
                "ctime-before" => stat with { Changed = start - 100 },
                "ctime-after" => stat with { Changed = exit + 100 },
                "mtime-after" => stat with { Modified = exit + 100 },
                _ => stat with { Kind = "unknown" }
            };
            var entry = OutputCustody.Stat(f.Context, f.Payload);
            if (variant == "unknown-stat") entry.ShouldBeNull("C987.Custody_window_checks_owner_mode_and_times.unknown-stat");
            else OutputCustody.Refusal(entry!.Value, window).ShouldBe(reason, "C987.Custody_window_checks_owner_mode_and_times." + variant);
            f.Write.AdoptCreated(f.Payload, window);
            f.Write.MatchesCreation(f.Payload).ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times." + variant + ":record");
            f.Io.Count("stream", f.Payload).ShouldBe(0, "C987.Custody_window_checks_owner_mode_and_times." + variant + ":describe-entry");
        }
        var original = new LedgerStat("file", 1, 2, 3, 4, 5);
        original.Matches(original).ShouldBeTrue("C987.Custody_window_checks_owner_mode_and_times.stat:control");
        LedgerStat.Unknown.Matches(LedgerStat.Unknown).ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times.stat-unknown-match");
        foreach (var (variant, other) in new[] { ("stat-kind", original with { Kind = "directory" }), ("stat-device", original with { Device = 2 }),
            ("stat-inode", original with { Inode = 3 }), ("stat-size", original with { Size = 4 }),
            ("stat-modified", original with { Modified = 5 }), ("stat-changed", original with { Changed = 6 }) })
            original.Matches(other).ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times." + variant);
        (original with { Inode = 0 }).HasIdentityAndChangeTime.ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times.stat-inode-known");
        (original with { Changed = 0 }).HasIdentityAndChangeTime.ShouldBeFalse("C987.Custody_window_checks_owner_mode_and_times.stat-ctime-known");
        original.HasIdentityAndChangeTime.ShouldBeTrue("C987.Custody_window_checks_owner_mode_and_times.stat-known:control");
    }

    [Test]
    public void Links_and_special_entries_are_not_adopted()
    {
        foreach (var mode in new ushort[] { 0x1180, 0x2180, 0xA180 })
        {
            var f = new HostCleanupCheckpointFixture();
            f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Mode = mode, Kind = "other" };
            OutputCustody.Refusal(OutputCustody.Stat(f.Context, f.Payload)!.Value, f.Window).ShouldBe("not-regular", "C987.Links_and_special_entries_are_not_adopted.custody-type");
            f.Write.AdoptCreated(f.Payload, f.Window);
            f.Io.Count("open-read").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.custody-type:describe-entry");
            Should.Throw<InvalidDataException>(() => f.Read()).Message.ShouldBe("selected file is not regular", "C987.Links_and_special_entries_are_not_adopted.preopen-type");
            f.Io.Count("open-read").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.preopen-type:entry");
        }
        foreach (uint links in new uint[] { 0, 2 })
        {
            var f = new HostCleanupCheckpointFixture();
            f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Links = links };
            OutputCustody.Refusal(OutputCustody.Stat(f.Context, f.Payload)!.Value, f.Window).ShouldBe("multiple-links", "C987.Links_and_special_entries_are_not_adopted.custody-links");
            f.Write.AdoptCreated(f.Payload, f.Window);
            f.Io.Count("open-read").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.custody-links:describe-entry");
            f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Links = 1 };
            f.Io.HandleMetadata = m => m with { Links = links };
            Should.Throw<InvalidDataException>(() => f.Read()).Message.ShouldBe("selected file has multiple links", "C987.Links_and_special_entries_are_not_adopted.held-links");
            f.Io.Count("stream").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.held-links:entry");
        }
        var held = new HostCleanupCheckpointFixture(); held.Io.HandleMetadata = m => m with { Regular = false };
        Should.Throw<InvalidDataException>(() => held.Read()).Message.ShouldBe("selected file is not regular", "C987.Links_and_special_entries_are_not_adopted.held-type");
        held.Io.Count("stream").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.held-type:entry");
        var flags = new HostCleanupCheckpointFixture();
        flags.Io.Before = (op, path) => { if (op == "open-read") flags.Io.Nodes[path].Stat = flags.Io.Nodes[path].Stat with { Mode = 0x1180 }; };
        Should.Throw<InvalidDataException>(() => flags.Read());
        (flags.Io.ReadRequests.Single().UnixFlags & 0x20000).ShouldBe(0x20000, "C987.Links_and_special_entries_are_not_adopted.nofollow-open");
        (flags.Io.ReadRequests.Single().UnixFlags & 0x800).ShouldBe(0x800, "C987.Links_and_special_entries_are_not_adopted.nonblocking-open");
        flags.Io.Count("follow-target").ShouldBe(0);
        var swapped = new HostCleanupCheckpointFixture();
        var outside = Path.Combine(swapped.Parent, "outside"); swapped.Io.Put(outside, "outside-bytes");
        swapped.Io.Before = (op, path) => { if (op == "open-read") swapped.Io.Nodes[path].Target = outside; };
        Should.Throw<IOException>(() => swapped.Read());
        swapped.Io.Count("follow-target").ShouldBe(0, "C987.Links_and_special_entries_are_not_adopted.nofollow-open:outside-entry");
        swapped.Io.Count("stream").ShouldBe(0);
        swapped.Io.Text(outside).ShouldBe("outside-bytes");
    }

    [Test]
    public async Task Entry_change_during_read_is_not_adopted()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.AfterRead = path => { if (path == f.Payload) f.Io.Nodes[path].Stat = f.Io.Nodes[path].Stat with { Changed = f.Io.Nodes[path].Stat.Changed + 100 }; };
        f.Write.AdoptCreated(f.Payload, f.Window);
        f.Io.Count("stream", f.Payload).ShouldBeGreaterThan(0, "C987.Entry_change_during_read_is_not_adopted.after-read-stat:entry");
        f.Write.MatchesCreation(f.Payload).ShouldBeFalse("C987.Entry_change_during_read_is_not_adopted.after-read-stat");
        var handle = new HostCleanupCheckpointFixture();
        Encoding.UTF8.GetString(handle.Read(after: () => handle.Io.Put(handle.Payload, "replacement")))
            .ShouldBe("original", "C987.Entry_change_during_read_is_not_adopted.held-handle");
        handle.Io.Text(handle.Payload).ShouldBe("replacement");
        foreach (var variant in new[] { "final-path", "held-size", "handle-inspect-failure" })
        {
            var bad = new HostCleanupCheckpointFixture();
            bad.Io.InspectFails = variant == "handle-inspect-failure";
            bad.Io.HandleMetadata = m => variant == "final-path" ? m with { Path = Path.Combine(bad.Parent, "outside") }
                : variant == "held-size" ? m with { Size = 1025 } : m;
            if (variant == "handle-inspect-failure")
                Should.Throw<IOException>(() => ReceiptFile.ReadBytes(bad.Context, bad.Payload)).Message.ShouldContain("receipt-read-refused", Case.Sensitive, "C987.Entry_change_during_read_is_not_adopted." + variant);
            else Should.Throw<InvalidDataException>(() => bad.Read()).Message.ShouldBe(variant == "final-path"
                ? "opened file is outside root" : "selected file exceeds size limit", "C987.Entry_change_during_read_is_not_adopted." + variant);
            bad.Io.Count("stream").ShouldBe(0, "C987.Entry_change_during_read_is_not_adopted." + variant + ":entry");
        }
        var growing = new HostCleanupCheckpointFixture(); growing.Io.StreamFactory = () => new MemoryStream(new byte[1025]);
        Should.Throw<InvalidDataException>(() => growing.Read()).Message.ShouldBe("selected file exceeds size limit", "C987.Entry_change_during_read_is_not_adopted.growing-stream");
        var windows = new HostCleanupCheckpointFixture(); windows.Io.Platform = CheckpointPlatform.Windows;
        Encoding.UTF8.GetString(windows.Read()).ShouldBe("original");
        (windows.Io.ReadRequests.Single().WindowsFlags & 0x00200000).ShouldBe(0x00200000u, "C987.Entry_change_during_read_is_not_adopted.windows-flags");
        var target = Path.Combine(windows.Parent, "outside"); windows.Io.Put(target, "outside-bytes");
        windows.Io.Before = (op, path) => { if (op == "open-read") windows.Io.Nodes[path].Target = target; };
        Should.Throw<IOException>(() => windows.Read());
        windows.Io.Count("follow-target").ShouldBe(0, "C987.Entry_change_during_read_is_not_adopted.windows-flags:outside-entry");
        foreach (var cancel in new[] { false, true }) await BlockedRead(cancel);
    }

    private static async Task BlockedRead(bool callerCancellation)
    {
        var f = new HostCleanupCheckpointFixture();
        using var source = new CancellationTokenSource();
        var stream = new BlockedStream(); f.Io.StreamFactory = () => stream;
        var read = Task.Run(() => ReceiptFile.ReadBytes(f.Context, f.Payload, source.Token));
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (callerCancellation) source.Cancel(); else f.Clock.Advance(TimeSpan.FromSeconds(3));
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2)));
            read.IsCompleted.ShouldBeTrue(callerCancellation ? "C987.Entry_change_during_read_is_not_adopted.cancellation:completed-before-release" : "C987.Entry_change_during_read_is_not_adopted.read-deadline:completed-before-release");
            var exception = await Should.ThrowAsync<IOException>(async () => await read);
            exception.Message.ShouldContain("receipt-read-refused");
        }
        finally { stream.Release.TrySetResult(); try { await read; } catch (IOException) { } }
    }

    // CARD-1318: a filter can stall the first read of a new image until the receipt deadline.
    // One more read adopts a file that then answers. A read that never answers stays unrecorded.
    [Test]
    public async Task Stalled_receipt_read_is_retried_once()
    {
        var stuck = new HostCleanupCheckpointFixture();
        stuck.Io.StreamFactory = () => throw new IOException("receipt stalled");
        stuck.Write.AdoptCreated(stuck.Payload, stuck.Window);
        stuck.Write.MatchesCreation(stuck.Payload).ShouldBeFalse("CARD-1318 a read that never answers stays unrecorded");
        stuck.Io.Count("stream").ShouldBe(2, "CARD-1318 a refused read is tried once more and then stops");

        var stalled = new HostCleanupCheckpointFixture();
        var first = new BlockedStream();
        var reads = 0;
        stalled.Io.StreamFactory = () =>
        {
            if (Interlocked.Increment(ref reads) == 1) return first;
            stalled.Io.StreamFactory = null;
            return new MemoryStream(Encoding.UTF8.GetBytes("original"));
        };
        var adopt = Task.Run(() => stalled.Write.AdoptCreated(stalled.Payload, stalled.Window));
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stalled.Clock.Advance(TimeSpan.FromSeconds(3));
        await adopt.WaitAsync(TimeSpan.FromSeconds(5));
        stalled.Write.MatchesCreation(stalled.Payload).ShouldBeTrue("CARD-1318 a stalled receipt read is adopted on the retry");
    }

    private sealed class BlockedStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0; }
    }

    [Test]
    public void Late_or_changed_entries_are_retained()
    {
        var f = new HostCleanupCheckpointFixture(); f.Seal();
        f.Io.Put(f.Payload, "changed!");
        f.Write.MatchesCreation(f.Payload).ShouldBeFalse("C987.Late_or_changed_entries_are_retained.hash");
        f.Ledger.Remove(f.Root).ShouldStartWith("output-ledger-mismatch");
        f.Delete.DeleteCreatedFile(f.Payload).Disposition.ShouldBe(DeleteDisposition.Refused, "C987.Late_or_changed_entries_are_retained.file-only");
        f.Io.Text(f.Payload).ShouldBe("changed!");
        var late = new HostCleanupCheckpointFixture(); late.Seal(); late.Io.Put(Path.Combine(late.Root, "late.txt"), "late");
        late.Ledger.Check(late.Root).ShouldContain("late.txt", Case.Sensitive, "C987.Late_or_changed_entries_are_retained.late-file");
        late.Ledger.Remove(late.Root).ShouldStartWith("output-ledger-mismatch");
        late.Io.Count("unlink").ShouldBe(0, "C987.Late_or_changed_entries_are_retained.late-file:entry");
    }

    [Test]
    public void Individual_deletes_preserve_late_additions()
    {
        var f = new HostCleanupCheckpointFixture(); f.Seal(); var reads = 0;
        f.Io.Before = (op, path) => { if (op == "open-read" && path == f.Payload && ++reads == 2) f.Io.Put(path, "changed!"); };
        f.Ledger.Remove(f.Root).ShouldStartWith("left in place", Case.Sensitive, "C987.Individual_deletes_preserve_late_additions.final-file");
        f.Io.Count("unlink", f.Payload).ShouldBe(0, "C987.Individual_deletes_preserve_late_additions.final-file:entry");
        f.Io.Text(f.Payload).ShouldBe("changed!");
        var dir = new HostCleanupCheckpointFixture(); var child = Path.Combine(dir.Root, "child");
        dir.Io.CreateDirectory(child); dir.Seal(); var linkedAt = -1;
        dir.Io.Before = (op, path) =>
        {
            if (op == "unlink" && linkedAt < 0)
            { dir.Io.Nodes[child].Target = Path.Combine(dir.Parent, "outside"); linkedAt = dir.Io.Events.Count; }
        };
        dir.Ledger.Remove(dir.Root).ShouldStartWith("left in place", Case.Sensitive, "C987.Individual_deletes_preserve_late_additions.final-directory");
        dir.Io.Events.Skip(linkedAt).ShouldContain(e => e.Operation == "link" && e.Path == child, "C987.Individual_deletes_preserve_late_additions.final-directory:preflight-entry");
        dir.Io.Count("rmdir", child).ShouldBe(0, "C987.Individual_deletes_preserve_late_additions.final-directory:entry");
        var late = new HostCleanupCheckpointFixture(); late.Seal();
        late.Io.Before = (op, path) => { if (op == "rmdir" && path == late.Root) late.Io.Put(Path.Combine(late.Root, "late.txt"), "keep"); };
        late.Ledger.Remove(late.Root).ShouldStartWith("left in place", Case.Sensitive, "C987.Individual_deletes_preserve_late_additions.no-recursion");
        late.Io.Text(Path.Combine(late.Root, "late.txt")).ShouldBe("keep", "C987.Individual_deletes_preserve_late_additions.no-recursion");
        late.Io.RecursiveIntents.ShouldBe(0, "C987.Individual_deletes_preserve_late_additions.no-recursion:intent");
    }

    [Test]
    public void Tracked_or_nested_repository_paths_are_retained()
    {
        var f = new HostCleanupCheckpointFixture(); f.Seal();
        f.Io.GitResult = request => new(0, request.Arguments.Contains("rev-parse") ? f.Parent : f.Payload + "\0", "", false);
        f.Write.Text(f.Payload, "new", f.Write.Under(f.Root)).ShouldBe("tracked", "C987.Tracked_or_nested_repository_paths_are_retained.tracked:write");
        f.Delete.DeleteCreatedTree(f.Root).Reason.ShouldBe("tracked", "C987.Tracked_or_nested_repository_paths_are_retained.tracked:delete");
        f.Io.Text(f.Payload).ShouldBe("original");
        var nested = new HostCleanupCheckpointFixture(); nested.Seal(); nested.Io.Put(Path.Combine(nested.Root, ".git"), "nested");
        nested.Delete.DeleteCreatedTree(nested.Root).Reason.ShouldBe("nested_repository", "C987.Tracked_or_nested_repository_paths_are_retained.nested-git");
        nested.Io.Count("unlink").ShouldBe(0);
        var outside = new HostCleanupCheckpointFixture(); outside.Seal();
        outside.Delete.Apply(outside.Root, new DeleteOwnership { IsolatedRoot = Path.Combine(outside.Parent, "other"), CreatedPaths = [outside.Root], AllowOutsideRepository = true })
            .Reason.ShouldBe("outside-isolated-root", "C987.Tracked_or_nested_repository_paths_are_retained.outside");
        var link = new HostCleanupCheckpointFixture(); link.Io.Nodes[link.Root].Target = Path.Combine(link.Parent, "outside");
        link.Write.Text(Path.Combine(link.Root, "new"), "new", link.Write.Under(link.Root)).ShouldBe("link-component", "C987.Tracked_or_nested_repository_paths_are_retained.ancestor-link");
        link.Io.Count("write").ShouldBe(0);
        var cap = new HostCleanupCheckpointFixture();
        for (var i = 0; i < 10001; i++) cap.Io.Put(Path.Combine(cap.Root, "entry-" + i), "");
        cap.Delete.DeleteCreatedTree(cap.Root).Reason.ShouldBe("unsafe-tree", "C987.Tracked_or_nested_repository_paths_are_retained.entry-cap");
        cap.Io.Count("unlink").ShouldBe(0);
    }

    [Test]
    public async Task Git_failure_and_deadline_refuse_before_mutation()
    {
        foreach (var variant in new[] { "nonzero", "start-failure", "timed-out" })
        {
            var f = new HostCleanupCheckpointFixture(); f.Seal();
            f.Io.Events.Clear();
            f.Io.GitResult = _ => variant == "start-failure" ? throw new FileNotFoundException() : new(1, "", "failed", variant == "timed-out");
            var reason = variant == "nonzero" ? "git-failed" : variant == "start-failure" ? "git-unavailable" : "git-timeout";
            f.Write.Text(f.Payload, "new", f.Write.Under(f.Root)).ShouldBe(reason, "C987.Git_failure_and_deadline_refuse_before_mutation." + variant + ":write");
            f.Delete.DeleteCreatedTree(f.Root).Reason.ShouldBe(reason, "C987.Git_failure_and_deadline_refuse_before_mutation." + variant + ":delete");
            f.Io.Count("write").ShouldBe(0); f.Io.Count("unlink").ShouldBe(0);
            f.Io.Text(f.Payload).ShouldBe("original");
        }
        var retry = new HostCleanupCheckpointFixture(); retry.Io.Put(Path.Combine("/bin", "git"), "executable");
        retry.Io.GitRequests.Clear();
        retry.Io.GitResult = _ => throw new Win32Exception(11);
        var operation = Task.Run(() => retry.Write.Preflight(retry.Root, [retry.Root], timeoutMs: 50));
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await retry.Clock.WaitForTimerAsync(e => e.Action == "create" && e.DueTime == TimeSpan.FromMilliseconds(25), ct: safety.Token);
            retry.Clock.Advance(TimeSpan.FromMilliseconds(25));
            await retry.Clock.WaitForTimerAsync(e => e.Action == "create" && e.RegisteredAt == retry.Clock.GetUtcNow(), afterSequence: retry.Clock.Events.Count(e => e.RegisteredAt < retry.Clock.GetUtcNow()), ct: safety.Token);
            retry.Clock.Advance(TimeSpan.FromMilliseconds(25));
            (await operation.WaitAsync(safety.Token)).ShouldBe("git-unavailable", "C987.Git_failure_and_deadline_refuse_before_mutation.retry-deadline");
            retry.Io.GitRequests.Select(r => r.TimeoutMilliseconds).ShouldBe(new[] { 50, 25 }, "C987.Git_failure_and_deadline_refuse_before_mutation.retry-deadline:remaining");
        }
        finally
        {
            // Release and join the finite retry loop even when a control fails its assertion.
            for (var i = 0; i < 16 && !operation.IsCompleted; i++)
            { retry.Clock.Advance(TimeSpan.FromSeconds(1)); await Task.WhenAny(operation, Task.Delay(20)); }
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var seen = false;
        var native = new CheckpointNativeGitRunner((start, timeout, git) =>
        {
            seen = true; git.ShouldBeTrue("C987.Git_failure_and_deadline_refuse_before_mutation.hardened-runner"); timeout.ShouldBe(37);
            start.FileName.ShouldBe("chosen-git"); start.WorkingDirectory.ShouldBe("/virtual");
            start.ArgumentList.ShouldBe(new[] { "ls-files", "--", "literal path" });
            return new CheckpointProcessRunner.Result { Exit = 0, Stdout = "raw" };
        });
        native.Run(new("chosen-git", "/virtual", ["ls-files", "--", "literal path"], 37)).Stdout.ShouldBe("raw");
        seen.ShouldBeTrue("C987.Git_failure_and_deadline_refuse_before_mutation.hardened-runner:entry");
    }

    [Test]
    public void Fresh_owned_output_can_be_removed()
    {
        var f = new HostCleanupCheckpointFixture(); f.Io.Put(Path.Combine(f.Parent, "evidence"), "preserved"); f.Seal();
        f.Ledger.Check(f.Root).ShouldBeNull("C987.Fresh_owned_output_can_be_removed.positive-remove:check");
        f.Delete.DeleteCreatedTree(f.Root).Disposition.ShouldBe(DeleteDisposition.Deleted, "C987.Fresh_owned_output_can_be_removed.positive-remove");
        f.Io.DirectoryExists(f.Root).ShouldBeFalse(); f.Io.Text(Path.Combine(f.Parent, "evidence")).ShouldBe("preserved");
        f.Io.Count("unlink", f.Payload).ShouldBe(1, "C987.Fresh_owned_output_can_be_removed.positive-remove:entry");
    }
}
