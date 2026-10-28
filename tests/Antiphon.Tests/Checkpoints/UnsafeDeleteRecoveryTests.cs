using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Round-2 witnesses for review cc28539f. They exercise the production ledger and state store.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class UnsafeDeleteRecoveryTests
{
    [Test]
    public void swapped_parent_symlink_cannot_redirect_owned_delete()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-race-" + Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "bin-owned");
        var outside = Path.Combine(root, "outside-repository");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "payload.txt");
        File.WriteAllText(sentinel, "foreign-sentinel");
        File.WriteAllText(Path.Combine(outside, "keeper.txt"), "keep");
        var io = new RaceIo(owned);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var write = new CheckpointWrite(context);
        try
        {
            write.CreateFreshDirectory(owned, write.Under(root)).ShouldBeNull();
            File.WriteAllText(Path.Combine(owned, "payload.txt"), "owned-payload");
            new CheckpointOutputLedger(context).Seal(owned, DateTimeOffset.UtcNow).ShouldBeNull();
            io.Arm();
            try { new CheckpointOutputLedger(context).Remove(owned); }
            catch (IOException) { }
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
            File.ReadAllText(Path.Combine(outside, "keeper.txt")).ShouldBe("keep");
            io.Swapped.ShouldBeTrue();
        }
        finally
        {
            try { if (Directory.Exists(owned)) Directory.Delete(owned, true); } catch (IOException) { }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void anchored_delete_requires_a_nofollow_directory_and_the_recorded_identity()
    {
        var recorded = new FilesystemIdentity(7, 11, 13);
        AnchoredDelete.Allowed(true, true, false, recorded, recorded, false).ShouldBeTrue();
        AnchoredDelete.Allowed(false, true, false, recorded, recorded, false).ShouldBeFalse();
        AnchoredDelete.Allowed(true, false, false, recorded, recorded, false).ShouldBeFalse();
        AnchoredDelete.Allowed(true, true, true, recorded, recorded, false).ShouldBeFalse();
        AnchoredDelete.Allowed(true, true, false, recorded, recorded, true).ShouldBeFalse();
        AnchoredDelete.Allowed(true, true, false, recorded, new FilesystemIdentity(7, 99, 13), false).ShouldBeFalse();
        AnchoredDelete.Allowed(true, true, false, default, default, false).ShouldBeFalse();
    }

    [Test]
    [Arguments(0x498)]
    [Arguments(0x499)]
    [Arguments(0)]
    public void replacement_loss_keeps_a_recoverable_generation(int win32)
    {
        var directory = Path.Combine(Path.GetTempPath(), "antiphon-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        var io = new LossIo(path, win32);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var store = new RunStateStore(context);
        try
        {
            store.Write(path, new RunState { RunId = "fixture", Marker = "old" }).ShouldBeNull();
            io.Arm();
            Exception? caught = null;
            string? refusal = null;
            try { refusal = store.Write(path, new RunState { RunId = "fixture", Marker = "new" }); }
            catch (Exception ex) { caught = ex; }
            File.Exists(path).ShouldBeTrue("the destination must be restored or published; refusal=" + refusal + " " + caught);
            var text = File.ReadAllText(path);
            var temps = Directory.GetFiles(directory, "*.tmp");
            var recovered = temps.Any(temp => File.ReadAllText(temp).Contains("\"marker\": \"new\"", StringComparison.Ordinal));
            (text.Contains("\"marker\": \"new\"", StringComparison.Ordinal) || recovered).ShouldBeTrue(
                "the new generation must survive a replacement that removes the destination");
            if (!text.Contains("\"marker\": \"new\"", StringComparison.Ordinal))
                text.ShouldContain("\"marker\": \"old\"");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class RaceIo(string owned) : ForwardingCleanupIo
    {
        private bool _armed;
        public bool Swapped { get; private set; }
        public void Arm() => _armed = true;
        public override void Unlink(string path)
        {
            SwapIfPayload(path);
            base.Unlink(path);
        }
        public override void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory)
        {
            SwapIfPayload(path);
            base.DeleteAnchored(path, expectedEntry, directory);
        }
        private void SwapIfPayload(string path)
        {
            if (!_armed || Swapped || !string.Equals(Path.GetFileName(path), "payload.txt", StringComparison.Ordinal))
                return;
            Swapped = true;
            var aside = owned + ".aside";
            Directory.Move(owned, aside);
            Directory.CreateSymbolicLink(owned, Path.GetDirectoryName(aside) is { } parent
                ? Path.Combine(parent, "outside-repository") : owned);
        }
    }

    private sealed class LossIo(string target, int win32) : ForwardingCleanupIo
    {
        private bool _armed;
        public void Arm() => _armed = true;
        public override void Move(string source, string destination, bool overwrite)
        {
            if (_armed && overwrite && string.Equals(destination, target, StringComparison.Ordinal))
            {
                _armed = false;
                if (File.Exists(destination)) File.Delete(destination);
                var hr = win32 == 0 ? unchecked((int)0x80070070) : unchecked((int)0x80070000) | win32;
                throw new IOException(win32 == 0 ? "injected interruption" : "injected replacement loss", hr);
            }
            base.Move(source, destination, overwrite);
        }
    }

    private class ForwardingCleanupIo : ICheckpointCleanupIo
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
        public void RemoveDirectory(string path, bool recursive) => _inner.RemoveDirectory(path, recursive);
        public virtual void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory) =>
            _inner.DeleteAnchored(path, expectedEntry, directory);
        public virtual IReadOnlyList<AnchoredChild> ListAnchored(string directory) => _inner.ListAnchored(directory);
        public virtual void UnlinkAnchored(string directory, string name, bool directoryEntry) =>
            _inner.UnlinkAnchored(directory, name, directoryEntry);
        public virtual void TruncateAnchored(string directory, string name, long length) =>
            _inner.TruncateAnchored(directory, name, length);
        public string? EnvironmentVariable(string name) => _inner.EnvironmentVariable(name);
    }
}
