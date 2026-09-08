using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingRemovalControlTests
{
    [Test]
    public Task C448_V20_RemoveError()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("remove-error");

    [Test]
    public Task C448_V20_RemoveTimeout()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("remove-timeout");

    [Test]
    public Task C448_V20_WindowsFileHandle()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("windows-file-handle");

    [Test]
    public Task C448_V20_BranchMoved()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("branch-moved");

    [Test]
    public Task C448_V20_NewCheckout()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("new-checkout");

    [Test]
    public Task C448_V20_RegistrationError()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent("registration-error");

    [Test]
    public Task C448_V18_UnregisteredResidue()
        => new AgentTaskLandCleanupSafetyTests().C448_V18_CleanupRetryPreservesChangedWork("unregistered");

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.json")]
    [Arguments("bin-private/keep.txt")]
    public Task C448_V18_FirstIgnoredContentGuard(string path)
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(1, path);

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.json")]
    [Arguments("bin-private/keep.txt")]
    public Task C448_V18_FinalIgnoredContentGuard(string path)
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(2, path);

    [Test]
    public Task C448_V36_Authority_valid()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("valid");

    [Test]
    public Task C448_V36_Authority_task()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("task");

    [Test]
    public Task C448_V36_Authority_repository()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("repository");

    [Test]
    public Task C448_V36_Authority_path()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("path");

    [Test]
    public Task C448_V36_Authority_git_directory()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("git-directory");

    [Test]
    public Task C448_V36_Authority_source_ref()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("source-ref");

    [Test]
    public Task C448_V36_Authority_source_sha()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("source-sha");

    [Test]
    public Task C448_V36_Authority_target_sha()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("target-sha");

    [Test]
    public Task C448_V36_Authority_target_ref()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("target-ref");

    [Test]
    public Task C448_V36_Authority_forged_lease()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("forged-lease");

    [Test]
    public Task C448_V36_Authority_missing_lease()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("missing-lease");

    [Test]
    public Task C448_V36_Authority_disposed_lease()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("disposed-lease");

    [Test]
    public Task C448_V36_Authority_cleanup_intent()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("cleanup-intent");

    [Test]
    public Task C448_V36_Authority_schema()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("schema");

    [Test]
    public Task C448_V36_Authority_destination()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("destination");

    [Test]
    public Task C448_V36_Authority_operation_namespace()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("operation-namespace");

    [Test]
    public Task C448_V36_Authority_inactive()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("inactive");

    [Test]
    public Task C448_V36_Authority_unconfirmed()
        => new AgentTaskLandRemovalMatrixTests()
            .C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate("unconfirmed");

}
