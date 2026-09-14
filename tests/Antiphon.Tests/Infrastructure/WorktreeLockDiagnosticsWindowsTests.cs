using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeLockDiagnosticsWindowsTests
{
    [Test] public Task C443_NativeFileSharing32() => NativeSharingAsync(false);
    [Test] public Task C443_NativeRootDirectorySharing32() => NativeSharingAsync(true);

    private static async Task NativeSharingAsync(bool directory)
    {
        OperatingSystem.IsWindows().ShouldBeTrue("Native qualification requires Windows");
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c443-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "held.bin");
        await File.WriteAllBytesAsync(file, [0, 1, 127, 128, 255]);
        var attributes = File.GetAttributes(file);
        try
        {
            await using var holder = await WorktreeLockChild.StartAsync(directory ? "directory" : "file", directory ? root : file);
            var result = await new WindowsWorktreeDeleteAccessProbe(new WorktreeNativeIO(), TimeProvider.System)
                .ObserveAsync(new(root, Path.Combine(root, ".git"), Path.Combine(root, ".git")), [], default);
            var observed = result.Observations.Single(o => o.RelativePath == (directory ? "." : "held.bin"));
            observed.Operation.ShouldBe("DeleteAccessOpen"); observed.Succeeded.ShouldBeFalse();
            observed.NativeErrorCode.ShouldBe(32); observed.IdentityVerified.ShouldBeTrue(); result.HasSharingConflict.ShouldBeTrue();
            holder.Exited.ShouldBeFalse();
            (await File.ReadAllBytesAsync(file)).ShouldBe(new byte[] { 0, 1, 127, 128, 255 });
            File.GetAttributes(file).ShouldBe(attributes);
            await holder.ReleaseAsync(); holder.Exited.ShouldBeTrue();
            var released = await new WindowsWorktreeDeleteAccessProbe(new WorktreeNativeIO(), TimeProvider.System)
                .ObserveAsync(new(root, Path.Combine(root, ".git"), Path.Combine(root, ".git")), [], default);
            released.HasSharingConflict.ShouldBeFalse();
        }
        finally
        {
            var full = Path.GetFullPath(root);
            if (!WorktreeNativeIO.Within(full, Path.GetTempPath()) || !Path.GetFileName(full).StartsWith("antiphon-c443-native-", StringComparison.Ordinal))
                throw new InvalidOperationException("Native fixture cleanup escaped owned root");
            Directory.Delete(full, recursive: true);
        }
    }
}
