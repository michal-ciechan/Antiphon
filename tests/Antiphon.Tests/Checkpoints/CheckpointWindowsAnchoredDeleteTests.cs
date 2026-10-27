using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointWindowsAnchoredDeleteTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void native_owned_entry_is_removed(bool directory)
    {
        using var fixture = new NativeFixture();
        var path = Path.Combine(fixture.Root, "owned-entry");
        if (directory) Directory.CreateDirectory(path);
        else File.WriteAllText(path, "owned-payload");
        var identity = FilesystemIdentityReader.Read(fixture.Io, path);
        identity.IsBound.ShouldBeTrue();

        fixture.Io.DeleteAnchored(path, identity, directory);

        File.Exists(path).ShouldBeFalse();
        Directory.Exists(path).ShouldBeFalse();
        fixture.AssertSentinel();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void native_mismatched_identity_is_refused(bool directory)
    {
        using var fixture = new NativeFixture();
        var path = Path.Combine(fixture.Root, "owned-entry");
        if (directory) Directory.CreateDirectory(path);
        else File.WriteAllText(path, "owned-payload");
        var identity = FilesystemIdentityReader.Read(fixture.Io, path);
        identity.IsBound.ShouldBeTrue();
        var other = identity with { FileId = identity.FileId ^ 1UL };

        Should.Throw<IOException>(() => fixture.Io.DeleteAnchored(path, other, directory))
            .Message.ShouldBe("anchored-identity-mismatch");

        if (directory) Directory.Exists(path).ShouldBeTrue();
        else File.ReadAllText(path).ShouldBe("owned-payload");
        fixture.AssertSentinel();
    }

    [Test]
    public void native_junction_parent_is_refused_without_touching_its_target()
    {
        using var fixture = new NativeFixture();
        var parent = Path.Combine(fixture.Root, "junction");
        using var link = DirectoryLink.TryCreate(parent, fixture.Outside);
        link.ShouldNotBeNull("native Windows qualification requires a real junction");
        File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
        var identity = FilesystemIdentityReader.Read(fixture.Io, fixture.Sentinel);
        identity.IsBound.ShouldBeTrue();

        // Even the target's matching identity must not authorize traversing the junction.
        var error = Should.Throw<IOException>(() => fixture.Io.DeleteAnchored(
            Path.Combine(parent, "keep.txt"), identity, directory: false));
        error.Message.ShouldNotContain("C000000D");

        DirectoryLink.IsLink(parent).ShouldBeTrue();
        fixture.AssertSentinel();
    }

    [Test]
    public void native_owned_ledger_is_removed()
    {
        using var fixture = new NativeFixture();
        var output = fixture.SealedOutput();

        new CheckpointOutputLedger(fixture.Context).Remove(output).ShouldBeNull();

        Directory.Exists(output).ShouldBeFalse();
        fixture.AssertSentinel();
    }

    [Test]
    public void native_cleanup_loop_removes_owned_output_without_throwing()
    {
        using var fixture = new NativeFixture();
        var output = fixture.SealedOutput();

        var result = new CheckpointOutputCleanup(fixture.Context).CleanOwnedOutputs(
            fixture.Root, ["bin-owned"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldBeEmpty();
        result.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
        fixture.AssertSentinel();
    }

    private sealed class NativeFixture : IDisposable
    {
        public string Root { get; }
        public string Outside { get; }
        public string Sentinel { get; }
        public CheckpointNativeIo Io { get; } = new();
        public CheckpointCleanupContext Context { get; }

        public NativeFixture()
        {
            if (!OperatingSystem.IsWindows()) Skip.Test("requires native Windows filesystem handles");
            Root = Path.Combine(Path.GetTempPath(), "antiphon-native-delete-" + Guid.NewGuid().ToString("N"));
            Outside = Path.Combine(Root, "outside-owned-root");
            Sentinel = Path.Combine(Outside, "keep.txt");
            Directory.CreateDirectory(Outside);
            File.WriteAllText(Sentinel, "outside-sentinel");
            Context = new CheckpointCleanupContext(Io, new CheckpointNativeGitRunner());
        }

        public string SealedOutput()
        {
            CheckpointFixtureProcess.Git(Root, "init", "-q");
            var output = Path.Combine(Root, "bin-owned");
            var write = new CheckpointWrite(Context);
            write.CreateFreshDirectory(output, write.Under(Root)).ShouldBeNull();
            write.Text(Path.Combine(output, "nested", "payload.txt"), "owned-payload", write.Under(output))
                .ShouldBeNull();
            var ledger = new CheckpointOutputLedger(Context);
            ledger.Seal(output).ShouldBeNull();
            ledger.Check(output).ShouldBeNull();
            return output;
        }

        public void AssertSentinel() => File.ReadAllText(Sentinel).ShouldBe("outside-sentinel");

        public void Dispose()
        {
            // Only this fixture's freshly created scratch tree is eligible for teardown.
            // Remove a junction itself before recursing, including failed setup/assertions.
            var junction = Path.Combine(Root, "junction");
            if (DirectoryLink.IsLink(junction)) Directory.Delete(junction, recursive: false);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
