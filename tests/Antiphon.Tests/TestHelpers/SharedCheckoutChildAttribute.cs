namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1807: marks a <c>Antiphon.Tests.Checkpoints</c> class that starts a child process with
/// the shared checkout (<c>CheckpointFixtures.RepoRoot</c>) as its working directory. That
/// checkout's build outputs and git index are process-wide, so a marked class keeps
/// <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c>; every other limited Checkpoints class
/// carries <see cref="CheckpointToolSpawnLimit"/>. <c>ProcessSpawnLimitTests</c> derives the
/// Checkpoints census from this marker and the limiter attributes alone, so a new class needs
/// no roster entry. Not inherited: each class states its own reason.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SharedCheckoutChildAttribute : Attribute
{
}
