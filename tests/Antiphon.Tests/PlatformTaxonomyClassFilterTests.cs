using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// CARD-1832 S1. A class attribute is shorthand for every method. The body passes
/// only when the include lane selected this Linux-only class, or when no probe row
/// set the lane.
/// </summary>
[Category("Unit")]
[LinuxOnly("class shorthand")]
public sealed class PlatformTaxonomyClassFilterTests
{
    [Test]
    public void Class_shorthand_is_selected_with_the_linux_only_property()
    {
        var attributes = typeof(PlatformTaxonomyClassFilterTests)
            .GetCustomAttributes(typeof(LinuxOnlyAttribute), inherit: false)
            .Cast<LinuxOnlyAttribute>()
            .ToArray();
        attributes.Length.ShouldBe(1);
        (attributes[0] is PropertyAttribute).ShouldBeTrue();
        (attributes[0] is SkipAttribute).ShouldBeFalse();
        attributes[0].Name.ShouldBe("Platform");
        attributes[0].Value.ShouldBe("LinuxOnly");
        attributes[0].Reason.ShouldBe("class shorthand");
        var lane = Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER");
        if (!string.IsNullOrEmpty(lane))
            lane.ShouldBe("include");
    }
}
