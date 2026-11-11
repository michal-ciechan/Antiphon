using System.Reflection;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// CARD-1832 S1. Each body passes only on the lane that should select it, so the
/// other treenode filter fails the method instead of looking green. Only the
/// CP-1/CP-2 probe rows set ANTIPHON_PLATFORM_FILTER; an ordinary lane has no
/// probe expectation and still checks the attribute shape.
/// </summary>
[Category("Unit")]
public sealed class PlatformTaxonomyFilterTests
{
    [Test]
    [LinuxOnly("witness")]
    public void LinuxOnly_is_a_property_not_a_skip()
    {
        AssertPlatform(new LinuxOnlyAttribute("witness"), "LinuxOnly", "witness");
        AssertPlatform(new WindowsOnlyAttribute("win"), "WindowsOnly", "win");
        AssertPlatform(new HasLinuxSpecificsAttribute("linux-specifics"), "HasLinuxSpecifics", "linux-specifics");
        AssertPlatform(new HasWindowsSpecificsAttribute("windows-specifics"), "HasWindowsSpecifics", "windows-specifics");
        Should.Throw<ArgumentException>(() => new LinuxOnlyAttribute(""));
        Should.Throw<ArgumentException>(() => new WindowsOnlyAttribute(""));
        Should.Throw<ArgumentException>(() => new HasLinuxSpecificsAttribute(""));
        Should.Throw<ArgumentException>(() => new HasWindowsSpecificsAttribute(""));
        Should.Throw<ArgumentException>(() => new LinuxOnlyAttribute(null!));
        AssertProbeLane("include");
    }

    [Test]
    public void Unannotated_sibling_is_excluded_from_the_linux_only_selection()
    {
        var method = typeof(PlatformTaxonomyFilterTests).GetMethod(nameof(Unannotated_sibling_is_excluded_from_the_linux_only_selection));
        method.ShouldNotBeNull();
        method!.GetCustomAttributes(typeof(PropertyAttribute), inherit: false).ShouldBeEmpty();
        AssertProbeLane("exclude");
    }

    internal static void AssertProbeLane(string expected)
    {
        var lane = Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER");
        if (!string.IsNullOrEmpty(lane))
            lane.ShouldBe(expected);
    }

    private static void AssertPlatform(object attribute, string value, string reason)
    {
        (attribute is PropertyAttribute).ShouldBeTrue(attribute.GetType().FullName);
        (attribute is SkipAttribute).ShouldBeFalse(attribute.GetType().FullName);
        var property = (PropertyAttribute)attribute;
        property.Name.ShouldBe("Platform");
        property.Value.ShouldBe(value);
        attribute.GetType().GetProperty("Reason")!.GetValue(attribute).ShouldBe(reason);
    }
}
