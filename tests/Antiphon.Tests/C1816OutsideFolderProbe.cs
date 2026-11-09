using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints.Sub;

// CARD-1816: this file is outside tests/Antiphon.Tests/Checkpoints. The source census
// follows the Checkpoints namespace, so this probe stays visible to that walk. It is not a test.
[ParallelLimiter<ProcessSpawnLimit>]
[SharedCheckoutChild]
internal static class C1816OutsideFolderProbe
{
    internal static ProcessStartInfo Start() => new("dotnet")
    {
        WorkingDirectory = Antiphon.Tests.Checkpoints.CheckpointFixtures.RepoRoot,
    };
}
