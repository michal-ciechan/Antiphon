using TUnit.Core;

namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1832. Method declaration that the Linux lane runs and the Windows lane excludes.
/// The reason is stored for the diff and the census. The filter value is <c>LinuxOnly</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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
