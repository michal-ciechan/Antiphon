using TUnit.Core;

namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1832. Method declaration that the Linux lane runs and the Windows lane excludes.
/// The reason is stored for the diff and the census. The filter value is <c>LinuxOnly</c>.
/// TUnit0028 forbids a second AttributeUsage, so this inherits PropertyAttribute's usage.
/// </summary>
public sealed class LinuxOnlyAttribute : PropertyAttribute
{
    public string Reason { get; }

    public LinuxOnlyAttribute(string reason)
        : base("Platform", "LinuxOnly")
    {
        Reason = PlatformTaxonomyReason.Require(reason);
    }
}

/// <summary>
/// CARD-1832. Method declaration that the Windows lane runs and the Linux lane excludes.
/// </summary>
public sealed class WindowsOnlyAttribute : PropertyAttribute
{
    public string Reason { get; }

    public WindowsOnlyAttribute(string reason)
        : base("Platform", "WindowsOnly")
    {
        Reason = PlatformTaxonomyReason.Require(reason);
    }
}

/// <summary>
/// CARD-1832. The method runs on both lanes, and a Linux-concerning diff must schedule it.
/// </summary>
public sealed class HasLinuxSpecificsAttribute : PropertyAttribute
{
    public string Reason { get; }

    public HasLinuxSpecificsAttribute(string reason)
        : base("Platform", "HasLinuxSpecifics")
    {
        Reason = PlatformTaxonomyReason.Require(reason);
    }
}

/// <summary>
/// CARD-1832. The method runs on both lanes, and a Windows-concerning diff must schedule it.
/// </summary>
public sealed class HasWindowsSpecificsAttribute : PropertyAttribute
{
    public string Reason { get; }

    public HasWindowsSpecificsAttribute(string reason)
        : base("Platform", "HasWindowsSpecifics")
    {
        Reason = PlatformTaxonomyReason.Require(reason);
    }
}

internal static class PlatformTaxonomyReason
{
    public static string Require(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            throw new ArgumentException("A platform taxonomy reason must be nonempty.", nameof(reason));
        return reason;
    }
}

/// <summary>
/// CARD-1832. One closed method declaration. Unannotated means the method runs on both lanes.
/// </summary>
public enum PlatformDeclaration
{
    Unannotated = 0,
    LinuxOnly,
    WindowsOnly,
    HasLinuxSpecifics,
    HasWindowsSpecifics,
}

/// <summary>
/// CARD-1832. A class member passed to <see cref="PlatformTaxonomy.Derive"/>.
/// Non-test members are ignored, including any declaration on them.
/// </summary>
public readonly record struct PlatformMember(bool IsTest, PlatformDeclaration Declaration);

/// <summary>
/// CARD-1832. Class label derived from test-method declarations.
/// An exclusive label is not also that operating system's specifics flag.
/// </summary>
public readonly record struct PlatformClassLabel(
    bool RunsOnLinux,
    bool RunsOnWindows,
    bool LinuxOnly,
    bool WindowsOnly,
    bool HasLinuxSpecifics,
    bool HasWindowsSpecifics)
{
    public static PlatformClassLabel Both { get; } = new(
        RunsOnLinux: true,
        RunsOnWindows: true,
        LinuxOnly: false,
        WindowsOnly: false,
        HasLinuxSpecifics: false,
        HasWindowsSpecifics: false);
}

/// <summary>
/// CARD-1832. Derives the class platform label from method declarations.
/// </summary>
public static class PlatformTaxonomy
{
    public static PlatformClassLabel Derive(IReadOnlyList<PlatformMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var testCount = 0;
        var allLinuxOnly = true;
        var allWindowsOnly = true;
        var hasLinuxSpecifics = false;
        var hasWindowsSpecifics = false;
        foreach (var member in members)
        {
            if (!member.IsTest)
                continue;
            if (!Enum.IsDefined(member.Declaration))
                throw new ArgumentOutOfRangeException(nameof(members), member.Declaration, "A test method platform declaration is outside the closed set.");

            testCount++;
            if (member.Declaration != PlatformDeclaration.LinuxOnly)
                allLinuxOnly = false;
            if (member.Declaration != PlatformDeclaration.WindowsOnly)
                allWindowsOnly = false;
            if (member.Declaration is PlatformDeclaration.LinuxOnly or PlatformDeclaration.HasLinuxSpecifics)
                hasLinuxSpecifics = true;
            if (member.Declaration is PlatformDeclaration.WindowsOnly or PlatformDeclaration.HasWindowsSpecifics)
                hasWindowsSpecifics = true;
        }

        if (testCount == 0)
            return PlatformClassLabel.Both;

        // Every test method is LinuxOnly. That class is not also HasLinuxSpecifics.
        if (allLinuxOnly)
        {
            return new PlatformClassLabel(
                RunsOnLinux: true,
                RunsOnWindows: false,
                LinuxOnly: true,
                WindowsOnly: false,
                HasLinuxSpecifics: false,
                HasWindowsSpecifics: false);
        }

        if (allWindowsOnly)
        {
            return new PlatformClassLabel(
                RunsOnLinux: false,
                RunsOnWindows: true,
                LinuxOnly: false,
                WindowsOnly: true,
                HasLinuxSpecifics: false,
                HasWindowsSpecifics: false);
        }

        return new PlatformClassLabel(
            RunsOnLinux: true,
            RunsOnWindows: true,
            LinuxOnly: false,
            WindowsOnly: false,
            HasLinuxSpecifics: hasLinuxSpecifics,
            HasWindowsSpecifics: hasWindowsSpecifics);
    }
}
