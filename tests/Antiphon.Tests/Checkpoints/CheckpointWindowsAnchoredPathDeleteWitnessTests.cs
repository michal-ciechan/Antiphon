using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Desktop lane for created-file and tool-copy anchored deletes, colon refusal,
// win32 145 left-in-place, and an NTFS hard-link sibling. Linux skips every row.
// The temp-root sweep stays on the base path delete and is not witnessed here.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointWindowsAnchoredPathDeleteWitnessTests
{
    [Test]
    public void windows_colon_name_is_refused_before_any_delete()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
        var sandbox = Scratch("ads");
        Directory.CreateDirectory(sandbox);
        try
        {
            var path = Path.Combine(sandbox, "owned:stream");
            Should.Throw<IOException>(() => new CheckpointNativeIo().DeleteAnchored(
                    path, new FilesystemIdentity(1, 1, 1), directory: false))
                .Message.ShouldBe("anchored-path-unsafe");
            File.Exists(Path.Combine(sandbox, "owned")).ShouldBeFalse();
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void windows_nonempty_owned_directory_is_left_in_place()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
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

    [Test]
    public void windows_junction_cannot_redirect_created_file_delete()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
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
    public void windows_junction_cannot_redirect_tool_copy_delete()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
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
            try { authority!.Remove(tool); }
            catch (IOException) { }
            io.Swapped.ShouldBeTrue();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void windows_hard_link_sibling_survives_anchored_delete()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
        var sandbox = Scratch("hardlink");
        Directory.CreateDirectory(sandbox);
        NativeHardLink.RequireNtfs(sandbox);
        var owned = Path.Combine(sandbox, "owned.txt");
        var foreign = Path.Combine(sandbox, "foreign.txt");
        try
        {
            File.WriteAllText(owned, "payload");
            NativeHardLink.Create(foreign, owned);
            var io = new CheckpointNativeIo();
            var identity = FilesystemIdentityReader.Read(io, owned);
            identity.IsBound.ShouldBeTrue();
            io.DeleteAnchored(owned, identity, directory: false);
            File.Exists(owned).ShouldBeFalse();
            File.ReadAllText(foreign).ShouldBe("payload");
        }
        finally { RemoveScratch(sandbox); }
    }

    private static string Scratch(string name) =>
        Path.Combine(Path.GetTempPath(), "antiphon-anchor-win-" + name + "-" + Guid.NewGuid().ToString("N"));

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
