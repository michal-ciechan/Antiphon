using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The two CARD-0478 land-crash cuts that start and kill a <see cref="PostLandMutationDeliveryWorker"/>
/// child process. They stay on the 1-wide <see cref="ProcessSpawnLimit"/> lane; the rest of the
/// matrix, which only starts git, is in <see cref="PostLandMutationDeliveryTests"/> on the git lane.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationDeliveryWorkerTests
{
    [Test]
    [Arguments("publication-commit", false)]
    [Arguments("post-submit-process", false)]
    public async Task C478_V09a_LandCrashMatrix(string cut, bool busy) =>
        await PostLandMutationDeliveryTests.LandCrashAsync(cut, busy);
}
