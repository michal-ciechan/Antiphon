using System.Runtime.InteropServices;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

// Parent-swap witnesses for created-file and tool-copy deletes. Those sites pass the
// recorded identity to DeleteAnchored. The temp-root sweep uses the ancestor-safe
// walk in ListAnchored/UnlinkAnchored (CARD-1596).
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AnchoredPathDeleteWitnessTests
{
    [Test]
    public void swapped_parent_cannot_redirect_created_file_delete()
    {
        var sandbox = Scratch("created");
        var owned = Path.Combine(sandbox, "owned");
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "payload.txt");
        File.WriteAllText(sentinel, "foreign-sentinel");
        var io = new ParentSwapIo(outside);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var write = new CheckpointWrite(context);
        try
        {
            write.CreateFreshDirectory(owned, write.Under(sandbox)).ShouldBeNull();
            var path = Path.Combine(owned, "payload.txt");
            write.Text(path, "owned-payload", write.Under(owned)).ShouldBeNull();
            io.Arm("payload.txt");
            try { new CheckpointDelete(context).DeleteCreatedFile(path); }
            catch (IOException) { }
            io.Swapped.ShouldBeTrue();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void swapped_parent_cannot_redirect_tool_copy_delete()
    {
        var sandbox = Scratch("tool");
        var run = Path.Combine(sandbox, "run");
        var tool = Path.Combine(run, "tool");
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "payload.txt");
        File.WriteAllText(sentinel, "foreign-sentinel");
        var io = new ParentSwapIo(outside);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var write = new CheckpointWrite(context);
        try
        {
            write.CreateFreshDirectory(run, write.Under(sandbox)).ShouldBeNull();
            write.CreateFreshDirectory(tool, write.Under(run)).ShouldBeNull();
            write.Text(Path.Combine(tool, "payload.txt"), "owned-payload", write.Under(tool)).ShouldBeNull();
            new CheckpointOutputLedger(context).Seal(tool, DateTimeOffset.UtcNow).ShouldBeNull();
            var owner = new RunOwnership
            {
                RunId = "run",
                RunDirectory = run,
                Starter = new ProcessIdentityProbe(context).Current(),
                Phase = "launched",
            };
            owner.ToolCopy = ToolCopyAuthority.Seal(run, owner, context);
            var authority = ToolCopyAuthority.Load(run, owner, out var refusal, context);
            authority.ShouldNotBeNull(refusal);
            io.Arm("payload.txt");
            try { authority.Remove(tool); }
            catch (IOException) { }
            io.Swapped.ShouldBeTrue();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void nonregular_created_file_is_left_in_place()
    {
        var fixture = new HostCleanupCheckpointFixture();
        fixture.Seal();
        var payload = fixture.Io.Nodes[fixture.Payload];
        payload.Stat = payload.Stat with { Kind = "other" };
        var result = fixture.Delete.DeleteCreatedFile(fixture.Payload);
        result.Disposition.ShouldBe(DeleteDisposition.Refused);
        fixture.Io.Text(fixture.Payload).ShouldBe("original");
        fixture.Io.Count("unlink", fixture.Payload).ShouldBe(0);
    }

    [Test]
    public void nonempty_owned_directory_is_left_in_place()
    {
        var late = new HostCleanupCheckpointFixture();
        late.Seal();
        late.Io.Before = (op, path) =>
        {
            if (op == "rmdir" && path == late.Root)
                late.Io.Put(Path.Combine(late.Root, "late.txt"), "keep");
        };
        var refusal = late.Ledger.Remove(late.Root);
        refusal.ShouldNotBeNull();
        refusal.ShouldContain("left in place");
        late.Io.Text(Path.Combine(late.Root, "late.txt")).ShouldBe("keep");
        late.Io.RecursiveIntents.ShouldBe(0);
    }

    [Test]
    public void directory_not_empty_matches_win32_145_exactly()
    {
        foreach (var (message, left) in new (string Message, bool Left)[]
        {
            ("anchored-unlink-failed: win32=145", true),
            ("anchored-unlink-failed: win32=1450", false),
            ("anchored-unlink-failed: errno=39", true),
            ("anchored-unlink-failed: errno=390", false),
            ("directory not empty", true),
        })
        {
            var fixture = new HostCleanupCheckpointFixture();
            fixture.Seal();
            fixture.Io.Before = (op, path) =>
            {
                if (op == "rmdir" && path == fixture.Root)
                    throw new IOException(message);
            };
            if (left)
            {
                var refusal = fixture.Ledger.Remove(fixture.Root);
                refusal.ShouldNotBeNull(message);
                refusal.ShouldContain("left in place");
            }
            else
                Should.Throw<IOException>(() => fixture.Ledger.Remove(fixture.Root), message).Message.ShouldBe(message);
        }
    }

    [Test]
    public void windows_entry_name_rejects_an_alternate_data_stream()
    {
        AnchoredDelete.EntryNameSafe(windows: true, "owned:stream").ShouldBeFalse();
        AnchoredDelete.EntryNameSafe(windows: true, "owned").ShouldBeTrue();
        AnchoredDelete.EntryNameSafe(windows: false, "owned:stream").ShouldBeTrue();
        AnchoredDelete.EntryNameSafe(windows: true, "nested/name").ShouldBeFalse();
    }

    [Test]
    public void listed_name_accepts_a_linux_backslash_and_throws_when_refused()
    {
        AnchoredDelete.EntryNameSafe(windows: false, "C:\\Antiphon\\x.txt").ShouldBeTrue();
        AnchoredDelete.EntryNameSafe(windows: true, "C:\\Antiphon\\x.txt").ShouldBeFalse();
        AnchoredDelete.IncludeListedName(windows: false, "C:\\Antiphon\\x.txt").ShouldBeTrue();
        AnchoredDelete.IncludeListedName(windows: false, ".").ShouldBeFalse();
        AnchoredDelete.IncludeListedName(windows: false, "..").ShouldBeFalse();
        Should.Throw<IOException>(() => AnchoredDelete.IncludeListedName(windows: true, "C:\\Antiphon\\x.txt"))
            .Message.ShouldBe("anchored-name-refused");
        Should.Throw<IOException>(() => AnchoredDelete.IncludeListedName(windows: true, "owned:stream"))
            .Message.ShouldBe("anchored-name-refused");
    }

    [Test]
    public void missing_birth_time_matches_device_and_inode_only()
    {
        var recorded = new FilesystemIdentity(7, 11, 0);
        AnchoredDelete.Allowed(true, true, false, recorded, recorded, false).ShouldBeTrue();
        AnchoredDelete.Allowed(true, true, false, recorded, recorded with { Created = 1 }, false).ShouldBeFalse();
    }

    [NativeLinuxOnly("a colon is a legal Linux file name")]
    [Test]
    public void linux_colon_in_a_file_name_stays_deletable()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("a colon is a legal Linux file name");
        var sandbox = Scratch("colon");
        Directory.CreateDirectory(sandbox);
        var path = Path.Combine(sandbox, "owned:stream");
        try
        {
            File.WriteAllText(path, "owned-payload");
            var io = new CheckpointNativeIo();
            var identity = FilesystemIdentityReader.Read(io, path);
            identity.IsBound.ShouldBeTrue();
            io.DeleteAnchored(path, identity, directory: false);
            File.Exists(path).ShouldBeFalse();
        }
        finally { RemoveScratch(sandbox); }
    }

    [NativeLinuxOnly("Linux unlinkat removes one directory entry")]
    [Test]
    public void hard_link_sibling_survives_anchored_delete()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux unlinkat removes one directory entry");
        var sandbox = Scratch("hardlink");
        Directory.CreateDirectory(sandbox);
        var owned = Path.Combine(sandbox, "owned.txt");
        var foreign = Path.Combine(sandbox, "foreign.txt");
        try
        {
            File.WriteAllText(owned, "payload");
            Link(owned, foreign).ShouldBe(0, "hard link");
            var io = new CheckpointNativeIo();
            var identity = FilesystemIdentityReader.Read(io, owned);
            identity.IsBound.ShouldBeTrue();
            io.DeleteAnchored(owned, identity, directory: false);
            File.Exists(owned).ShouldBeFalse();
            File.ReadAllText(foreign).ShouldBe("payload");
        }
        finally { RemoveScratch(sandbox); }
    }

    [NativeLinuxOnly("requires a Linux directory symlink")]
    [Test]
    public void ancestor_symlink_to_a_different_inode_is_refused()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("requires a Linux directory symlink");
        var sandbox = Scratch("ancestor");
        var real = Path.Combine(sandbox, "real");
        var foreign = Path.Combine(sandbox, "foreign");
        Directory.CreateDirectory(Path.Combine(real, "owned"));
        Directory.CreateDirectory(Path.Combine(foreign, "owned"));
        var ownedFile = Path.Combine(real, "owned", "payload.txt");
        var sentinel = Path.Combine(foreign, "owned", "payload.txt");
        try
        {
            File.WriteAllText(ownedFile, "owned-payload");
            File.WriteAllText(sentinel, "foreign-sentinel");
            var io = new CheckpointNativeIo();
            var identity = FilesystemIdentityReader.Read(io, ownedFile);
            identity.IsBound.ShouldBeTrue();
            Directory.Move(real, real + ".aside");
            Directory.CreateSymbolicLink(real, foreign);
            Should.Throw<IOException>(() => io.DeleteAnchored(ownedFile, identity, directory: false))
                .Message.ShouldBe("anchored-identity-mismatch");
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
            File.ReadAllText(Path.Combine(real + ".aside", "owned", "payload.txt")).ShouldBe("owned-payload");
        }
        finally { RemoveScratch(sandbox); }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);

    [Test]
    public void native_nonempty_owned_directory_is_left_in_place()
    {
        var sandbox = Scratch("ledger");
        Directory.CreateDirectory(sandbox);
        var owned = Path.Combine(sandbox, "bin-owned");
        var io = new LateDirectoryIo(owned);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var write = new CheckpointWrite(context);
        try
        {
            write.CreateFreshDirectory(owned, write.Under(sandbox)).ShouldBeNull();
            File.WriteAllText(Path.Combine(owned, "payload.txt"), "owned-payload");
            new CheckpointOutputLedger(context).Seal(owned, DateTimeOffset.UtcNow).ShouldBeNull();
            io.Arm();
            var refusal = new CheckpointOutputLedger(context).Remove(owned);
            refusal.ShouldNotBeNull();
            refusal.ShouldContain("left in place");
            File.ReadAllText(Path.Combine(owned, "late.txt")).ShouldBe("keep");
        }
        finally { RemoveScratch(sandbox); }
    }

    private static string Scratch(string name) =>
        Path.Combine(Path.GetTempPath(), "antiphon-anchor-" + name + "-" + Guid.NewGuid().ToString("N"));

    private static void RemoveScratch(string sandbox)
    {
        try
        {
            if (!Directory.Exists(sandbox)) return;
            UnlinkDescendantLinks(sandbox);
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void UnlinkDescendantLinks(string directory)
    {
        if (DirectoryLink.IsLink(directory))
        {
            Directory.Delete(directory, recursive: false);
            return;
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (DirectoryLink.IsLink(entry)) Directory.Delete(entry, recursive: false);
            else if (Directory.Exists(entry)) UnlinkDescendantLinks(entry);
        }
    }
}

internal sealed class ParentSwapIo(string foreignDirectory) : ForwardingNativeIo
{
    private bool _armed;
    private string? _name;
    public bool Swapped { get; private set; }
    public void Arm(string fileName) { _armed = true; _name = fileName; }
    public override void Unlink(string path) { Swap(path); base.Unlink(path); }
    public override void RemoveDirectory(string path, bool recursive) { Swap(path); base.RemoveDirectory(path, recursive); }
    public override void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory)
    { Swap(path); base.DeleteAnchored(path, expectedEntry, directory); }

    private void Swap(string path)
    {
        if (!_armed || Swapped || !string.Equals(Path.GetFileName(path), _name, StringComparison.Ordinal))
            return;
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return;
        Swapped = true;
        Directory.Move(parent, parent + ".aside");
        if (OperatingSystem.IsWindows())
        {
            if (DirectoryLink.TryCreate(parent, foreignDirectory) is null)
                throw new IOException("junction-unavailable");
            return;
        }
        Directory.CreateSymbolicLink(parent, foreignDirectory);
    }
}

internal sealed class LateDirectoryIo(string owned) : ForwardingNativeIo
{
    private bool _armed;
    public void Arm() => _armed = true;
    public override void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory)
    {
        if (_armed && directory && string.Equals(path, owned, StringComparison.Ordinal))
        {
            _armed = false;
            File.WriteAllText(Path.Combine(owned, "late.txt"), "keep");
        }
        base.DeleteAnchored(path, expectedEntry, directory);
    }
}

internal class ForwardingNativeIo : ICheckpointCleanupIo
{
    private readonly ICheckpointCleanupIo _inner = new CheckpointNativeIo();
    public int CurrentProcessId => _inner.CurrentProcessId;
    public string MachineName => _inner.MachineName;
    public string BootId => _inner.BootId;
    public string PidNamespace => _inner.PidNamespace;
    public string ImageDirectory => _inner.ImageDirectory;
    public string CurrentDirectory => _inner.CurrentDirectory;
    public CheckpointPlatform Platform => _inner.Platform;
    public uint Owner => _inner.Owner;
    public CheckpointProcessSnapshot Process(int pid) => _inner.Process(pid);
    public DateTime CreationTimeUtc(string path) => _inner.CreationTimeUtc(path);
    public DateTime LastWriteTimeUtc(string path) => _inner.LastWriteTimeUtc(path);
    public string StreamPath(Stream stream) => _inner.StreamPath(stream);
    public virtual bool FileExists(string path) => _inner.FileExists(path);
    public virtual bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public FileAttributes Attributes(string path) => _inner.Attributes(path);
    public string? LinkTarget(string path) => _inner.LinkTarget(path);
    public long Length(string path) => _inner.Length(path);
    public virtual IEnumerable<string> Enumerate(string path) => _inner.Enumerate(path);
    public CheckpointStat Stat(string path, bool portable = false) => _inner.Stat(path, portable);
    public CheckpointStat CustodyStat(string path) => _inner.CustodyStat(path);
    public int CreateExclusiveDirectory(string path) => _inner.CreateExclusiveDirectory(path);
    public void CreateDirectory(string path) => _inner.CreateDirectory(path);
    public virtual Stream Open(string path, FileMode mode, FileAccess access, FileShare share) => _inner.Open(path, mode, access, share);
    public virtual ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request) => _inner.OpenRead(path, request);
    public void WriteBytes(string path, ReadOnlySpan<byte> bytes) => _inner.WriteBytes(path, bytes);
    public void AppendText(string path, string text) => _inner.AppendText(path, text);
    public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);
    public virtual void Move(string source, string destination, bool overwrite) => _inner.Move(source, destination, overwrite);
    public virtual void Unlink(string path) => _inner.Unlink(path);
    public virtual void RemoveDirectory(string path, bool recursive) => _inner.RemoveDirectory(path, recursive);
    public virtual void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory) =>
        _inner.DeleteAnchored(path, expectedEntry, directory);
    public virtual IReadOnlyList<AnchoredChild> ListAnchored(string directory) => _inner.ListAnchored(directory);
    public virtual void UnlinkAnchored(string directory, string name, bool directoryEntry, ulong device = 0, ulong inode = 0) =>
        _inner.UnlinkAnchored(directory, name, directoryEntry, device, inode);
    public virtual void TruncateAnchored(string directory, string name, long length) =>
        _inner.TruncateAnchored(directory, name, length);
    public string? EnvironmentVariable(string name) => _inner.EnvironmentVariable(name);
}
