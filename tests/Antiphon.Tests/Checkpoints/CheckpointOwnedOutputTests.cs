using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointOwnedOutputTests : CheckpointTestBase
{

    // case: forged disk ledger adds a user file; mutation: disk ledger authorizes deletion;
    // red: USER bytes disappear. Missing/truncated/outside ledgers must also retain residue.
    [Test]
    [Arguments("forged")]
    [Arguments("missing")]
    [Arguments("truncated")]
    [Arguments("outside")]
    public void disk_ledger_cannot_authorize_user_deletion(string variant)
    {
        var root = InitRepo();
        var output = Path.Combine(root, "bin-ledger");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(output, "compiler.dll"), "COMPILER", SafeWrite.Under(output)));
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var user = Path.Combine(output, "user.txt");
        File.WriteAllText(user, "USER");
        var ledger = Path.Combine(output, ".checkpoint-output-ledger.json");
        var entries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(ledger))!;
        entries[variant == "outside" ? "../outside.txt" : "user.txt"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("USER")));
        if (variant == "missing") File.Delete(ledger);
        else File.WriteAllText(ledger, variant == "truncated" ? "{" : System.Text.Json.JsonSerializer.Serialize(entries));
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-ledger"], 0, false, false, createdPaths: [output]);
        File.Exists(user).ShouldBeTrue("cleanup deleted a user file using " + variant + " ledger");
        File.ReadAllText(user).ShouldBe("USER");
        result.Refusals.ShouldNotBeEmpty();
        result.Deleted.ShouldBeEmpty();
    }


    // case: a real exclusive gate; mutation: read its pathname while the exclusive handle is held;
    // red at 8035e4a: OpenGate throws IOException instead of returning the held stream.
    [Test]
    public void exclusive_gate_returns_its_held_stream()
    {
        var root = InitRepo();
        var path = Path.Combine(root, ".gate");
        FileStream? stream = null;
        try
        {
            SafeWrite.OpenGate(path, SafeWrite.Under(root), out stream).ShouldBeNull();
            stream.ShouldNotBeNull();
            stream.CanWrite.ShouldBeTrue();
            Should.Throw<IOException>(() => { using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        }
        finally { stream?.Dispose(); }
    }

    private string InitRepo()
    {
        var root = TempDir();
        using var process = Process.Start(new ProcessStartInfo("git") { WorkingDirectory = root, ArgumentList = { "init", "-q" } });
        process.ShouldNotBeNull();
        RegisterCheckpointChild(process);
        process.WaitForExit(30_000).ShouldBeTrue();
        process.ExitCode.ShouldBe(0);
        return root;
    }

    // case -> unrecorded addition/change/link -> red if cleanup removes user residue. appended, truncated, swapped (same size,
    // mtime restored) and reused (deleted and recreated at the same size and mtime, the inode often reused on Linux) are the
    // seal-time mutation cases of the deleted output-inventory driver: the creation record's on-disk digest recheck refuses each.
    [Test]
    [Arguments("file")]
    [Arguments("changed")]
    [Arguments("directory")]
    [Arguments("link")]
    [Arguments("appended")]
    [Arguments("truncated")]
    [Arguments("swapped")]
    [Arguments("reused")]
    public void changed_inventory_is_refused(string variant)
    {
        var root = InitRepo();
        var output = Path.Combine(root, "bin-ledger");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(output, "compiler.dll"), "COMPILER", SafeWrite.Under(output)));
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var extra = Path.Combine(output, "user.txt");
        if (variant == "file") File.WriteAllText(extra, "USER");
        if (variant == "changed") File.WriteAllText(Path.Combine(output, "compiler.dll"), "USER");
        var compiler = Path.Combine(output, "compiler.dll");
        var modified = File.GetLastWriteTimeUtc(compiler);
        if (variant == "appended") File.AppendAllText(compiler, "USER");
        if (variant == "truncated") { using var stream = File.Open(compiler, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); stream.SetLength(3); }
        if (variant is "swapped" or "reused")
        {
            if (variant == "reused") File.Delete(compiler);
            File.WriteAllText(compiler, "USERUSER");
            File.SetLastWriteTimeUtc(compiler, modified);
        }
        if (variant == "directory") Directory.CreateDirectory(extra);
        if (variant == "link" && !OperatingSystem.IsWindows()) File.CreateSymbolicLink(extra, Path.Combine(root, "outside.txt"));
        var junctionTarget = Path.Combine(root, "outside");
        try
        {
            if (variant == "link" && OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(junctionTarget);
                File.WriteAllText(Path.Combine(junctionTarget, "keep.txt"), "OUTSIDE");
                var start = new ProcessStartInfo("cmd.exe");
                foreach (var arg in new[] { "/d", "/c", "mklink", "/J", extra, junctionTarget })
                    start.ArgumentList.Add(arg);
                var made = CheckpointFixtureProcess.Run(start, 30_000);
                made.Exit.ShouldBe(0, made.Stdout + made.Stderr);
                File.GetAttributes(extra).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            }
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-ledger"], 0, false, false, createdPaths: [output]);
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldNotBeEmpty();
            Directory.Exists(output).ShouldBeTrue();
            if (variant == "file") File.ReadAllText(extra).ShouldBe("USER");
            if (variant == "changed") File.ReadAllText(Path.Combine(output, "compiler.dll")).ShouldBe("USER");
            if (variant == "appended") File.ReadAllText(compiler).ShouldBe("COMPILERUSER");
            if (variant == "truncated") File.ReadAllText(compiler).ShouldBe("COM");
            if (variant is "swapped" or "reused") File.ReadAllText(compiler).ShouldBe("USERUSER");
            if (variant == "directory") Directory.Exists(extra).ShouldBeTrue();
            if (variant == "link" && OperatingSystem.IsWindows())
            {
                new DirectoryInfo(extra).LinkTarget.ShouldNotBeNull();
                File.ReadAllText(Path.Combine(junctionTarget, "keep.txt")).ShouldBe("OUTSIDE");
                File.ReadAllText(Path.Combine(output, "compiler.dll")).ShouldBe("COMPILER");
            }
            else if (variant == "link") new FileInfo(extra).LinkTarget.ShouldNotBeNull();
        }
        finally
        {
            if (variant == "link" && OperatingSystem.IsWindows())
            {
                if (new DirectoryInfo(extra).LinkTarget is not null) Directory.Delete(extra);
            }
            else if (variant == "link") new FileInfo(extra).Delete();
        }
    }

    // CARD-0987 (the deleted owned-output driver's native cases, now on the library type the row verb uses): the exclusive
    // create makes the directory once, private on Unix; a second create is pre-existing-output and leaves the directory, its
    // contents and its identity as they were; a missing parent is output-create-failed.
    [Test]
    public void exclusive_directory_is_created_once_and_a_collision_changes_nothing()
    {
        var root = TempDir();
        var child = Path.Combine(root, "native-" + Guid.NewGuid().ToString("N"));
        ExclusiveDirectory.Create(child).ShouldBeNull();
        Directory.Exists(child).ShouldBeTrue();
        if (!OperatingSystem.IsWindows()) File.GetUnixFileMode(child).ShouldBe((UnixFileMode)448);
        var sentinel = Path.Combine(child, "sentinel");
        File.WriteAllBytes(sentinel, [1, 2, 3, 4, 5]);
        var io = CheckpointCleanupDefaults.Context.Io;
        var identity = Antiphon.HostCleanup.Checkpoints.FilesystemIdentityReader.Read(io, child);
        ExclusiveDirectory.Create(child).ShouldBe("pre-existing-output");
        File.ReadAllBytes(sentinel).ShouldBe(new byte[] { 1, 2, 3, 4, 5 });
        identity.SameAs(Antiphon.HostCleanup.Checkpoints.FilesystemIdentityReader.Read(io, child)).ShouldBeTrue("the collision left the directory");
        ExclusiveDirectory.Create(Path.Combine(root, "absent-parent", "child")).ShouldStartWith("output-create-failed:");
    }

    // case -> valid ledger -> red if cleanup cannot reclaim its own unchanged payload.
    [Test]
    public void unchanged_owned_inventory_is_removed()
    {
        var root = InitRepo();
        var output = Path.Combine(root, "bin-ledger");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(output, "compiler.dll"), "COMPILER", SafeWrite.Under(output)));
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-ledger"], 0, false, false, createdPaths: [output]);
        result.Refusals.ShouldBeEmpty();
        result.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
    }
}
