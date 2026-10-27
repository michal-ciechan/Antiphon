using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    [Test]
    public void C1032_CrashWorkerWindowsPathCustody()
    {
        const string root = @"C:\Temp\c1032-fence-owned";
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root,
            root + @"\crash-input.json", '\\').ShouldBeTrue();
        // Mirror bindings use forward slashes even when the worker root is native Windows.
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root,
            "C:/Temp/c1032-fence-owned/work/worktrees/task-deadbeef", '\\').ShouldBeTrue();
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root,
            root + "-foreign/work/worktrees/task-deadbeef", '\\').ShouldBeFalse();
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root,
            "C:/Temp/c1032-fence-owned-foreign/crash-input.json", '\\').ShouldBeFalse();
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root, root, '\\').ShouldBeFalse();
        CompletedCardRemovalCrashWorker.IsOwnedFixturePath(root,
            "D:/Temp/c1032-fence-owned/crash-input.json", '\\').ShouldBeFalse();
    }

    [Test]
    public async Task C1032_FixtureRemovesReadOnlyGitObjects()
    {
        var fixture = new CompletedCardRunnerFixture();
        var root = fixture.Root;
        try
        {
            var objects = Directory.GetFiles(Path.Combine(fixture.Origin, ".git", "objects"), "*",
                SearchOption.AllDirectories);
            objects.ShouldNotBeEmpty();
            foreach (var path in objects)
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            }
            await fixture.DisposeAsync();
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root)) ReadOnlyDirectory.Delete(root);
        }
    }
}
