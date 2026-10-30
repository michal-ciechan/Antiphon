using Antiphon.LandGraphAudit;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    [Test]
    public void C1483_CanaryEntries_WindowsSeparatorCollapses()
    {
        var entries = CanaryEntries.Build(["docs/x.md"], ["docs\\x.md"], '\\');
        entries.Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/x.md" });
    }

    [Test]
    public void C1483_CanaryEntries_CaseStaysDistinct()
    {
        var entries = CanaryEntries.Build(["src/S.cs", "src/s.cs", "docs/A.md"], ["docs\\a.md"], '\\');
        entries.Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/A.md", "docs/a.md", "src/S.cs", "src/s.cs" });
    }

    [Test]
    public void C1483_CanaryEntries_EmptyDuplicateTrailingSeparatorAndParent()
    {
        CanaryEntries.Build([], [], '\\').ShouldBeEmpty();

        CanaryEntries.Build(["docs/a.md", "docs/a.md"], ["docs/a.md", "docs\\a.md"], '\\')
            .Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/a.md" });

        CanaryEntries.Build([""], ["", ""], '\\')
            .Order(StringComparer.Ordinal).ShouldBe(new[] { "" });

        CanaryEntries.Build(["docs/x.md"], ["docs\\x.md\\"], '\\')
            .Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/x.md", "docs/x.md/" });

        var parent = CanaryEntries.Build(["docs/../secret.md"], ["docs\\..\\secret.md"], '\\');
        parent.Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/../secret.md" });
        parent.ShouldNotContain("secret.md");

        CanaryEntries.Build(["secret.md"], ["docs\\..\\secret.md"], '\\')
            .Order(StringComparer.Ordinal).ShouldBe(new[] { "docs/../secret.md", "secret.md" });
    }
}
