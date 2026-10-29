using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

// One limiter type per load-timeout class. TUnit keys the semaphore by type,
// so a class's rows do not queue behind another's. Four matches
// TerminalSeatReleaseLimit. A hung row holds one slot for that class's
// [Timeout], and a packed chunk's hung wall is the max of those figures.
//
// Packing is scripts/lib/nightly-class-costs.json through
// Split-NightlyItemsBySeconds: 900s target, 30 operands, largest first,
// per namespace. The five classes priced under the 2400s lone-class budget
// each fill a separate 900s bin (no two share a chunk):
//   Pipeline ceil(69/4)*120s = 2160s
//   Land     ceil(35/4)*120s = 1080s
//   Boot     ceil(59/4)*120s = 1800s
//   Spill    ceil(5/4)*180s = 360s
//   Specialist ceil(15/4)*180s = 720s
// Delegation is split (3505s > 2400s). At the 3505/381 s pace a 900s method
// chunk holds at most 97 rows, and the largest method is 73 rows, so
// ceil(97/4)*90s = 2250s. None exceed the 3600s chunk cap. Run 79b742de's
// all-group-30 shared this type across Pipeline, Land and Boot:
// ceil(163/4)*120s = 4920s. Separate types keep that co-bin at 2160s.

/// <summary>
/// Width-4 cap for <c>AgentTaskPipelineStatusTests</c>. ceil(69/4)*120s = 2160s.
/// </summary>
public sealed class PipelineStatusLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>AgentTaskLandSourcePersistenceTests</c>. ceil(35/4)*120s = 1080s.
/// </summary>
public sealed class LandSourcePersistenceLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>BootStallDetectionTests</c>. ceil(59/4)*120s = 1800s.
/// </summary>
public sealed class BootStallDetectionLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>DelegationDispatchRecoveryBoundaryTests</c>.
/// A 900s method chunk holds at most 97 rows: ceil(97/4)*90s = 2250s.
/// </summary>
public sealed class DelegationRecoveryLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>SessionMessageQueueDeliveredSpillRecoveryTests</c>.
/// ceil(5/4)*180s = 360s.
/// </summary>
public sealed class DeliveredSpillRecoveryLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>SpecialistQualificationTests</c>. ceil(15/4)*180s = 720s.
/// </summary>
public sealed class SpecialistQualificationLoadLimit : IParallelLimit
{
    public int Limit => 4;
}

/// <summary>
/// Width-4 cap for <c>PhoneHomeRestartResumeTests</c>. Each row starts a Kestrel
/// phone-home host. Two rows: ceil(2/4)*8s = 8s.
/// </summary>
public sealed class PhoneHomeRestartResumeLoadLimit : IParallelLimit
{
    public int Limit => 4;
}
