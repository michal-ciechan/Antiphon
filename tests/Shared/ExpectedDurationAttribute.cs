namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1842. Optional annotation of a test class's expected serial seconds.
/// The class-cost row in scripts/lib/nightly-class-costs.json is the store, and
/// tests/Shared/expected-duration-roster.json projects it. The packer never reads
/// this attribute, so ExpectedDurationSyncTests requires every application to name a
/// roster class and to equal that class's roster seconds.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ExpectedDurationAttribute : Attribute
{
    public double Seconds { get; }

    public ExpectedDurationAttribute(double seconds)
    {
        Seconds = seconds;
    }
}
