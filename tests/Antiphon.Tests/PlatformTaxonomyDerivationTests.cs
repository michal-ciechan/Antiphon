using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// CARD-1832 S2. Class labels are derived from method declarations.
/// Replay rows are in-memory shapes from the plan, not a selector run.
/// </summary>
[Category("Unit")]
public sealed class PlatformTaxonomyDerivationTests
{
    [Test]
    public void Every_method_linux_only_derives_linux_only()
    {
        var expected = Exclusive(linuxOnly: true);
        Derive(Test(PlatformDeclaration.LinuxOnly)).ShouldBe(expected);
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.LinuxOnly)).ShouldBe(expected);
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Helper(PlatformDeclaration.HasWindowsSpecifics)).ShouldBe(expected);
    }

    [Test]
    public void Every_method_windows_only_derives_windows_only()
    {
        var expected = Exclusive(windowsOnly: true);
        Derive(Test(PlatformDeclaration.WindowsOnly)).ShouldBe(expected);
        Derive(
            Test(PlatformDeclaration.WindowsOnly),
            Test(PlatformDeclaration.WindowsOnly),
            Helper(PlatformDeclaration.LinuxOnly)).ShouldBe(expected);
    }

    [Test]
    public void Mixed_linux_only_and_unannotated_derives_both_with_has_linux_specifics()
    {
        var expected = Both(hasLinuxSpecifics: true);
        // Plan replay row 9, LandGraphEvaluationTests: EvaluationBudgetRefusesSlowDotnet
        // is LinuxOnly and a sibling test is unannotated, so the class is not LinuxOnly.
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.Unannotated)).ShouldBe(expected, "row 9 LandGraphEvaluationTests EvaluationBudgetRefusesSlowDotnet");
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.HasLinuxSpecifics)).ShouldBe(expected);
    }

    [Test]
    public void All_has_linux_specifics_is_not_linux_only()
    {
        var expected = Both(hasLinuxSpecifics: true);
        Derive(Test(PlatformDeclaration.HasLinuxSpecifics)).ShouldBe(expected);
        Derive(
            Test(PlatformDeclaration.HasLinuxSpecifics),
            Test(PlatformDeclaration.HasLinuxSpecifics)).ShouldBe(expected);
        Derive(
            Test(PlatformDeclaration.HasLinuxSpecifics),
            Test(PlatformDeclaration.Unannotated)).ShouldBe(expected);
    }

    [Test]
    public void Mixed_windows_declarations_derive_both_with_has_windows_specifics()
    {
        var windows = Both(hasWindowsSpecifics: true);
        Derive(
            Test(PlatformDeclaration.WindowsOnly),
            Test(PlatformDeclaration.Unannotated)).ShouldBe(windows);
        Derive(Test(PlatformDeclaration.HasWindowsSpecifics)).ShouldBe(windows);
        Derive(
            Test(PlatformDeclaration.HasWindowsSpecifics),
            Test(PlatformDeclaration.HasWindowsSpecifics)).ShouldBe(windows);
        Derive(
            Test(PlatformDeclaration.WindowsOnly),
            Test(PlatformDeclaration.HasWindowsSpecifics)).ShouldBe(windows);

        var bothFlags = Both(hasLinuxSpecifics: true, hasWindowsSpecifics: true);
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.Unannotated),
            Test(PlatformDeclaration.HasWindowsSpecifics)).ShouldBe(bothFlags);
        Derive(
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.WindowsOnly)).ShouldBe(bothFlags);
    }

    [Test]
    public void Unannotated_replay_rows_derive_both_with_no_specifics()
    {
        var expected = PlatformClassLabel.Both;
        foreach (var row in UnannotatedReplayRows())
            Derive(row.Members).ShouldBe(expected, $"row {row.Row} {row.ClassName}");
    }

    [Test]
    public void Derive_is_pure_and_order_independent()
    {
        PlatformTaxonomy.Derive(Array.Empty<PlatformMember>()).ShouldBe(PlatformClassLabel.Both);
        PlatformTaxonomy.Derive(
        [
            Helper(PlatformDeclaration.LinuxOnly),
            Helper(PlatformDeclaration.WindowsOnly),
        ]).ShouldBe(PlatformClassLabel.Both);
        Derive(
            Helper(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.Unannotated)).ShouldBe(PlatformClassLabel.Both);

        var members = new List<PlatformMember>
        {
            Test(PlatformDeclaration.LinuxOnly),
            Test(PlatformDeclaration.Unannotated),
            Test(PlatformDeclaration.HasWindowsSpecifics),
            Helper(PlatformDeclaration.WindowsOnly),
        };
        var before = members.ToArray();
        var forward = PlatformTaxonomy.Derive(members);
        var reversed = new[] { before[3], before[2], before[1], before[0] };
        var rotated = new[] { before[1], before[2], before[3], before[0] };
        PlatformTaxonomy.Derive(reversed).ShouldBe(forward);
        PlatformTaxonomy.Derive(rotated).ShouldBe(forward);
        PlatformTaxonomy.Derive(members).ShouldBe(forward);
        for (var i = 0; i < before.Length; i++)
            members[i].ShouldBe(before[i]);
        forward.ShouldBe(Both(hasLinuxSpecifics: true, hasWindowsSpecifics: true));

        Should.Throw<ArgumentNullException>(() => PlatformTaxonomy.Derive(null!));
    }

    private static IEnumerable<(int Row, string ClassName, PlatformMember[] Members)> UnannotatedReplayRows()
    {
        // Row 2. The IsWindows helper is portability, not a test method.
        yield return (2, "ProcessSpawnLimitTests",
        [
            Helper(PlatformDeclaration.Unannotated),
            Test(PlatformDeclaration.Unannotated),
            Test(PlatformDeclaration.Unannotated),
        ]);
        yield return (4, "RepositoryMutationLeaseTests", [Test(PlatformDeclaration.Unannotated)]);
        yield return (7, "C1677TempFamilyRegistryTests", [Test(PlatformDeclaration.Unannotated)]);
        yield return (14, "NightlyCoreTests", [Test(PlatformDeclaration.Unannotated)]);
    }

    private static PlatformClassLabel Derive(params PlatformMember[] members) =>
        PlatformTaxonomy.Derive(members);

    private static PlatformMember Test(PlatformDeclaration declaration) => new(true, declaration);

    private static PlatformMember Helper(PlatformDeclaration declaration) => new(false, declaration);

    private static PlatformClassLabel Exclusive(bool linuxOnly = false, bool windowsOnly = false) =>
        new(
            RunsOnLinux: linuxOnly,
            RunsOnWindows: windowsOnly,
            LinuxOnly: linuxOnly,
            WindowsOnly: windowsOnly,
            HasLinuxSpecifics: false,
            HasWindowsSpecifics: false);

    private static PlatformClassLabel Both(bool hasLinuxSpecifics = false, bool hasWindowsSpecifics = false) =>
        new(
            RunsOnLinux: true,
            RunsOnWindows: true,
            LinuxOnly: false,
            WindowsOnly: false,
            HasLinuxSpecifics: hasLinuxSpecifics,
            HasWindowsSpecifics: hasWindowsSpecifics);
}
