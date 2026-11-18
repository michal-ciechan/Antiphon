using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1981. A fixture that leaves its scratch directory fails. The witnesses call the
/// producers' own cleanup and then remove anything still left so the assertion itself
/// does not add a /tmp entry.
/// </summary>
[Category("Unit")]
[Timeout(10_000)]
public sealed class OwnedTempLeakTests
{
    [Test]
    public void C459_release_removes_the_scratch_directory()
    {
        var root = Antiphon.Tests.Application.TaskWorktreeRetirementTests.RetirementScratch.Create(Guid.NewGuid());
        try
        {
            Antiphon.Tests.Application.TaskWorktreeRetirementTests.RetirementScratch.Release(root);
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Bridge_temp_root_is_removed_when_cleanup_throws()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "antiphon-bridge-queue-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                BridgeQueueHarness.TempRootCleanup.FinishAsync(root, preserveDatabase: false,
                    () => throw new InvalidOperationException("db")));
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
