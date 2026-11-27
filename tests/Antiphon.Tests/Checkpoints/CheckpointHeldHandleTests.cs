using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointHeldHandleTests : CheckpointTestBase
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void gate_and_append_record_identity_without_reopening(bool gate)
    {
        var root = TempDir();
        var path = Path.Combine(root, gate ? "publication.lock" : "usage.jsonl");
        File.WriteAllBytes(path, [1]);
        var io = new ExclusiveOpenIo(path);
        var context = new CheckpointCleanupContext(io, CheckpointCleanupDefaults.Context.Git);
        var write = new CheckpointWrite(context);
        var expected = FilesystemIdentityReader.Read(io, path);
        expected.IsBound.ShouldBeTrue();

        var refusal = gate
            ? write.OpenGate(path, write.Under(root), out var stream)
            : write.OpenAppend(path, write.Under(root), out stream, FileShare.None);
        using (stream)
        {
            refusal.ShouldBeNull();
            var file = stream.ShouldBeOfType<FileStream>();
            io.ExclusivePathProbes.ShouldBe(0, "identity must come from the retained handle");
            write.WasCreated(path).ShouldBeTrue();
            context.Registry.Identity(path).ShouldBe(expected);
            if (OperatingSystem.IsWindows())
            {
                var conflict = Should.Throw<IOException>(() =>
                {
                    using var contender = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                });
                (conflict.HResult & 0xffff).ShouldBe(32);
            }
            if (!gate)
                file.WriteByte(2);
        }

        File.ReadAllBytes(path).ShouldBe(gate ? new byte[] { 1 } : new byte[] { 1, 2 });
    }

    [Test]
    public void open_create_disposes_the_stream_when_recording_identity_throws()
    {
        var root = TempDir();
        var path = Path.Combine(root, "staged.seal");
        var io = new ThrowingHandleIo(path);
        var context = new CheckpointCleanupContext(io, CheckpointCleanupDefaults.Context.Git);
        var write = new CheckpointWrite(context);
        Stream? returned = null;
        try
        {
            var fault = Should.Throw<IOException>(() => write.OpenCreate(path, write.Under(root), out returned));
            fault.ShouldBeSameAs(io.Fault);
            io.OpenedHandle.ShouldNotBeNull().IsClosed.ShouldBeTrue("the failed handoff must release the exclusive handle");
            returned.ShouldBeNull();
            write.WasCreated(path).ShouldBeFalse();
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        }
        finally
        {
            io.Opened?.Dispose();
        }
    }

    private sealed class ThrowingHandleIo(string path) : ForwardingNativeIo
    {
        internal IOException Fault { get; } = new("injected handle inspection failure");
        internal ThrowingHandleFile? Opened { get; private set; }
        internal SafeFileHandle? OpenedHandle { get; private set; }

        public override Stream Open(string target, FileMode mode, FileAccess access, FileShare share)
        {
            if (target != path)
                return base.Open(target, mode, access, share);
            var file = new ThrowingHandleFile(target, mode, access, share, Fault);
            Opened = file;
            OpenedHandle = file.NativeHandle;
            return file;
        }
    }

    private sealed class ThrowingHandleFile(string path, FileMode mode, FileAccess access, FileShare share, IOException fault)
        : FileStream(path, mode, access, share)
    {
        internal SafeFileHandle NativeHandle => base.SafeFileHandle;
        public override SafeFileHandle SafeFileHandle => throw fault;
    }
}

internal sealed class ExclusiveOpenIo(string protectedPath) : ForwardingNativeIo, ICheckpointCleanupIo
{
    private Stream? _exclusive;
    internal int ExclusivePathProbes { get; private set; }
    CheckpointPlatform ICheckpointCleanupIo.Platform => CheckpointPlatform.Windows;
    public override Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var opened = CheckpointCleanupDefaults.Context.Io.Open(path, mode, access, share);
        if (path == protectedPath && share == FileShare.None)
            _exclusive = opened;
        return opened;
    }
    CheckpointStat ICheckpointCleanupIo.CustodyStat(string path)
    {
        if (path != protectedPath || _exclusive?.CanWrite != true)
            return base.CustodyStat(path);
        ExclusivePathProbes++;
        return new CheckpointStat("unknown", 0, 0, 0, 0, 0);
    }
}
