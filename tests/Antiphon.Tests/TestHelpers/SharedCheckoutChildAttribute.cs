namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1807: marks a class in <c>Antiphon.Tests.Checkpoints</c>, a sub-namespace, or a nested
/// class of one of those, that starts a child process with the shared checkout
/// (<c>CheckpointFixtures.RepoRoot</c>) as its working directory. That checkout's build outputs
/// and git index are process-wide, so a marked class keeps
/// <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c>; every other limited Checkpoints class
/// carries <see cref="CheckpointToolSpawnLimit"/>. <c>ProcessSpawnLimitTests</c> derives the
/// limiter census from this marker and the limiter attributes. A syntax walk still requires the
/// marker when a class assigns <c>CheckpointFixtures.RepoRoot</c> to a <c>ProcessStartInfo</c>
/// working directory and both attributes are absent (CARD-1810). A new class needs no roster
/// entry. Not inherited: each class states its own reason.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SharedCheckoutChildAttribute : Attribute
{
}
