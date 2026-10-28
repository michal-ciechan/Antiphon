using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Parent-swap witnesses for the path deletes that still call File.Delete / Directory.Delete.
// Each one is red until that site passes the recorded identity to DeleteAnchored.
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
    public void swapped_parent_cannot_redirect_temp_root_sweep_delete()
    {
        var sandbox = Scratch("sweep");
        var root = Path.Combine(sandbox, "c723-" + Guid.CreateVersion7().ToString("N"));
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "payload.bin");
        File.WriteAllText(sentinel, "foreign-sentinel");
        File.WriteAllBytes(Path.Combine(root, "payload.bin"), new byte[16]);
        var current = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(root, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(root)["c723-".Length..],
            RootPath = Path.GetFullPath(root),
            AttemptId = Guid.NewGuid().ToString("N"),
            AssemblyInvocationId = "anchored-witness",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = current with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        new CheckpointTempRootSweep(sandbox).Register(root);
        var io = new ParentSwapIo(outside);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var sweep = new CheckpointTempRootSweep(context, sandbox, options: new CheckpointSweepOptions
        {
            Grace = TimeSpan.Zero,
            Interval = TimeSpan.Zero,
            MaxDuration = TimeSpan.FromSeconds(5),
        });
        try
        {
            io.Arm("payload.bin");
            try { sweep.SweepOnce(); }
            catch (IOException) { }
            io.Swapped.ShouldBeTrue();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
        }
        finally { RemoveScratch(sandbox); }
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
    public bool FileExists(string path) => _inner.FileExists(path);
    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public FileAttributes Attributes(string path) => _inner.Attributes(path);
    public string? LinkTarget(string path) => _inner.LinkTarget(path);
    public long Length(string path) => _inner.Length(path);
    public IEnumerable<string> Enumerate(string path) => _inner.Enumerate(path);
    public CheckpointStat Stat(string path, bool portable = false) => _inner.Stat(path, portable);
    public CheckpointStat CustodyStat(string path) => _inner.CustodyStat(path);
    public int CreateExclusiveDirectory(string path) => _inner.CreateExclusiveDirectory(path);
    public void CreateDirectory(string path) => _inner.CreateDirectory(path);
    public Stream Open(string path, FileMode mode, FileAccess access, FileShare share) => _inner.Open(path, mode, access, share);
    public ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request) => _inner.OpenRead(path, request);
    public void WriteBytes(string path, ReadOnlySpan<byte> bytes) => _inner.WriteBytes(path, bytes);
    public void AppendText(string path, string text) => _inner.AppendText(path, text);
    public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);
    public virtual void Move(string source, string destination, bool overwrite) => _inner.Move(source, destination, overwrite);
    public virtual void Unlink(string path) => _inner.Unlink(path);
    public virtual void RemoveDirectory(string path, bool recursive) => _inner.RemoveDirectory(path, recursive);
    public virtual void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory) =>
        _inner.DeleteAnchored(path, expectedEntry, directory);
    public string? EnvironmentVariable(string name) => _inner.EnvironmentVariable(name);
}
