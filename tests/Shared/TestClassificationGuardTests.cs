using System.Reflection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.TestSupport;

[Category("Unit")]
public sealed class TestClassificationGuardTests
{
    [Test]
    public void Registry_matches_compiled_metadata()
    {
        var assembly = typeof(TestClassificationGuardTests).Assembly;
        if (string.Equals(assembly.GetName().Name, "Antiphon.E2E", StringComparison.OrdinalIgnoreCase))
        {
            var shared = assembly.GetType("Antiphon.E2E.Fixtures.SharedApp");
            if (shared is not null)
            {
                var field = shared.GetField("_fixture", BindingFlags.NonPublic | BindingFlags.Static);
                field?.GetValue(null).ShouldBeNull("E2E metadata-only classification must not start SharedApp");
            }
        }

        var path = TestClassificationMetadata.FindRegistryPath(assembly);
        var errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true);
        errors.ShouldBeEmpty(errors);
    }
}
