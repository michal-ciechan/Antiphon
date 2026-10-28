using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Roster failure: File.ReadAllText("/proc/<pid>/stat") throws DirectoryNotFoundException
// after GetProcessById still saw the child. That must be Dead, and Register must not keep it.
[Category("Unit")]
public sealed class ReapedProcStatIdentityTests
{
    private const string RosterMessage = "Could not find a part of the path '/proc/19323/stat'.";

    [Test]
    public void observe_treats_a_vanished_proc_directory_as_dead()
    {
        var io = new ReapedProcStatIo();
        var probe = new ProcessIdentityProbe(new CheckpointCleanupContext(io, new UnusedGit()));
        var observed = probe.Observe(new ProcessIdentity(19323, 1, io.MachineName, io.BootId, io.PidNamespace));
        observed.Verdict.ShouldBe(ProcessVerdict.Dead, observed.Reason);
        observed.Reason.ShouldBe("identity-dead");
    }

    [Test]
    public async Task register_drops_a_child_whose_capture_lost_the_proc_directory()
    {
        var io = new ReapedProcStatIo();
        var context = new CheckpointCleanupContext(io, new UnusedGit());
        await using var scope = new CheckpointTestScope(context, Path.GetTempPath(), new ProcessIdentityProbe(context));
        using var process = Process.GetCurrentProcess();
        scope.Register(process);
        await scope.DisposeAsync();
    }

    private static Exception Unused() => new NotSupportedException("reaped proc stat io");

    private sealed class UnusedGit : ICheckpointGitRunner
    {
        public CheckpointGitResult Run(CheckpointGitRequest request) => throw Unused();
    }

    private sealed class ReapedProcStatIo : ICheckpointCleanupIo
    {
        public int CurrentProcessId => 19323;
        public string MachineName => "reaped-host";
        public string BootId => "reaped-boot";
        public string PidNamespace => "reaped-pidns";
        public string ImageDirectory => "/virtual";
        public string CurrentDirectory => "/virtual";
        public CheckpointPlatform Platform => CheckpointPlatform.Linux;
        public uint Owner => 1000;

        public CheckpointProcessSnapshot Process(int pid) =>
            throw new DirectoryNotFoundException(RosterMessage);

        public DateTime CreationTimeUtc(string path) => throw Unused();
        public DateTime LastWriteTimeUtc(string path) => throw Unused();
        public string StreamPath(Stream stream) => throw Unused();
        public bool FileExists(string path) => throw Unused();
        public bool DirectoryExists(string path) => throw Unused();
        public FileAttributes Attributes(string path) => throw Unused();
        public string? LinkTarget(string path) => throw Unused();
        public long Length(string path) => throw Unused();
        public IEnumerable<string> Enumerate(string path) => throw Unused();
        public CheckpointStat Stat(string path, bool portable = false) => throw Unused();
        public CheckpointStat CustodyStat(string path) => throw Unused();
        public int CreateExclusiveDirectory(string path) => throw Unused();
        public void CreateDirectory(string path) => throw Unused();
        public Stream Open(string path, FileMode mode, FileAccess access, FileShare share) => throw Unused();
        public ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request) => throw Unused();
        public void WriteBytes(string path, ReadOnlySpan<byte> bytes) => throw Unused();
        public void AppendText(string path, string text) => throw Unused();
        public void Copy(string source, string destination, bool overwrite) => throw Unused();
        public void Move(string source, string destination, bool overwrite) => throw Unused();
        public void Unlink(string path) => throw Unused();
        public void RemoveDirectory(string path, bool recursive) => throw Unused();
        public void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory) => throw Unused();
        public IReadOnlyList<AnchoredChild> ListAnchored(string directory) => throw Unused();
        public void UnlinkAnchored(string directory, string name, bool directoryEntry) => throw Unused();
        public void TruncateAnchored(string directory, string name, long length) => throw Unused();
        public string? EnvironmentVariable(string name) => throw Unused();
    }
}
