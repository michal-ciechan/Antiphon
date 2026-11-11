using System.Reflection;

namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1842. One roster class: the class-cost row's seconds and basis, and the
/// measurement identity when the row has one.
/// </summary>
public sealed record ExpectedDurationEntry(
    double Seconds,
    string Basis,
    string? HostId,
    string? Os,
    string? MeasuredRunId);

/// <summary>
/// CARD-1842. The roster file, or the projection of the class-cost file it must equal.
/// </summary>
public sealed record ExpectedDurationRosterDocument(
    string Platform,
    IReadOnlyDictionary<string, ExpectedDurationEntry> Classes);

/// <summary>
/// CARD-1842. One [ExpectedDuration] application. Seconds is null when the argument
/// is not a single numeric literal.
/// </summary>
public sealed record ExpectedDurationApplication(string ClassName, string Location, double? Seconds);

/// <summary>
/// CARD-1842 red scaffold: the roster reader and comparer are not implemented yet.
/// </summary>
public static class ExpectedDurationRoster
{
    public const string Format = "antiphon-expected-duration-roster-v1";
    public const string CostFormat = "antiphon-nightly-class-costs-v1";
    public const string RelativePath = "tests/Shared/expected-duration-roster.json";
    public const string CostFileRelativePath = "scripts/lib/nightly-class-costs.json";

    public static ExpectedDurationRosterDocument Load(string path) => throw new NotImplementedException("CARD-1842 S2");

    public static ExpectedDurationRosterDocument ProjectCostFile(string path) => throw new NotImplementedException("CARD-1842 S2");

    public static IReadOnlyList<string> Differences(
        ExpectedDurationRosterDocument roster, ExpectedDurationRosterDocument expected) =>
        throw new NotImplementedException("CARD-1842 S2");

    public static IReadOnlyList<ExpectedDurationApplication> FindApplications(string source, string location) =>
        throw new NotImplementedException("CARD-1842 S2");

    public static IReadOnlyList<ExpectedDurationApplication> FindApplications(Assembly assembly) =>
        throw new NotImplementedException("CARD-1842 S2");

    public static IReadOnlyList<string> Check(
        ExpectedDurationRosterDocument roster, IEnumerable<ExpectedDurationApplication> applications) =>
        throw new NotImplementedException("CARD-1842 S2");
}
