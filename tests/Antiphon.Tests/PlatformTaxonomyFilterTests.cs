using System.Reflection;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// CARD-1832 S1. Each body passes only on the lane that should select it, so the
/// other treenode filter fails the method instead of looking green.
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
        Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER").ShouldBe("include");
    }

    [Test]
    public void Unannotated_sibling_is_excluded_from_the_linux_only_selection()
    {
        var method = typeof(PlatformTaxonomyFilterTests).GetMethod(nameof(Unannotated_sibling_is_excluded_from_the_linux_only_selection));
        method.ShouldNotBeNull();
        method!.GetCustomAttributes(typeof(PropertyAttribute), inherit: false).ShouldBeEmpty();
        Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER").ShouldBe("exclude");
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
