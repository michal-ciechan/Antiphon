using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class ProviderEndpointProjectionInvariantTests
{
    [Test]
    public void C1827_six_class_guard_subset_is_not_described_as_78()
    {
        const string count = "The six-class Antiphon.Tests subset is 53 results. Do not describe that subset as 78.";
        const string wider = "78 is the nine-class Antiphon.Tests union of that subset plus MechanicalPcContractTests, TaskPlatformGuidanceTests and C1729LayeringGuardTests.";
        RepoFile("docs/testing-and-build.md").ShouldContain("/*/*/*/*[Category=RepositoryInvariant]", Case.Sensitive);
        foreach (var relative in new[]
                 {
                     "docs/testing-and-build.md",
                     ".claude/skills/antiphon-orchestrator/SKILL.md",
                 })
        {
            var text = RepoFile(relative);
            text.ShouldContain(count, Case.Sensitive, relative);
            text.ShouldContain(wider, Case.Sensitive, relative);
            text.ShouldNotContain("six-class Antiphon.Tests subset is 78", Case.Sensitive, relative);
        }
    }

    private static string RepoFile(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "docs", "orchestration-loop.md")))
                return File.ReadAllText(Path.Combine(directory, relative));
            directory = Path.GetDirectoryName(directory)!;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
