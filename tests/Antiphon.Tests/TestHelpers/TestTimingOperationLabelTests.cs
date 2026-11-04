using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1620: the recorder kept only a fixed operation vocabulary, so every label a call site
// passed outside it (shape-git, card1619-*, c1653-*, seed, prepare, ...) was written as
// "omitted" and a slow span could not be attributed. Each literal a test passes to
// BeginPhase/BeginFixturePhase must survive into the complete record; any other text stays
// "omitted" so no command line, path or secret becomes a label.
[Category("Unit")]
public sealed partial class TestTimingOperationLabelTests
{
    [GeneratedRegex("""Begin(?:Fixture)?Phase\((?:[^;]*?,\s*)?TestPhaseKind\.[A-Za-z]+\s*,\s*"([^"]*)"\s*\)""")]
    private static partial Regex CallSite();

    [Test]
    public void C1620_EveryCallSiteOperationLabelIsKept()
    {
        var repo = C1514Support.RepoRoot();
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
            if (relative.Contains("/bin", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            foreach (Match match in CallSite().Matches(File.ReadAllText(file)))
                labels.Add(match.Groups[1].Value);
        }

        labels.ShouldContain("shape-git", "call-site-scan-finds-checkpoint-labels");
        labels.ShouldContain("clone", "call-site-scan-finds-recorder-labels");
        var kept = Record(labels);
        var dropped = labels.Where((label, index) => kept[index] != label).ToArray();
        dropped.ShouldBeEmpty("operation labels written as omitted: " + string.Join(", ", dropped));
    }

    [Test]
    public void C1620_UnlistedOperationTextStaysOmitted()
    {
        string[] free =
        [
            "git status --porcelain", "Password=secret", "sk-probe-should-not-leak", "/tmp/repo",
            "SHAPE-GIT", "shape-git ", "shape-git-extra", "",
        ];
        Record(free).ShouldAllBe(label => label == "omitted", "free-text-operation-omitted");
    }

    private static string[] Record(IReadOnlyCollection<string> operations)
    {
        var root = C1514Support.FreshRoot();
        try
        {
            using var session = C1514Support.Open(root);
            session.BeginCase("case-a", "TimingProbe", "Labels", [], 0);
            var at = 1;
            foreach (var operation in operations)
            {
                var id = session.OpenAt("case-a", TestPhaseKind.OurCode, operation, C1514Support.Ms(at), null);
                session.CloseAt(id, C1514Support.Ms(at + 1));
                at += 2;
            }

            session.EndCase("case-a", "Passed", C1514Support.Ms(at));
            var row = C1514Support.Complete(session.ShardPath!, "case-a");
            var recorded = row.GetProperty("phases").EnumerateArray()
                .Select(phase => phase.GetProperty("operation").GetString() ?? "")
                .ToArray();
            recorded.Length.ShouldBe(operations.Count, "one-span-per-operation");
            return recorded;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
