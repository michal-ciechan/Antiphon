using System.Diagnostics;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NativeLinuxPolicyExecutionTests
{
    private static readonly (string Token, int Count)[] LinuxEffects =
    [
        ("direct-class.field", 1), ("direct-class.ctor", 1), ("direct-class.before", 1), ("direct-class.body", 1),
        ("direct-method.field", 1), ("direct-method.ctor", 1), ("direct-method.before", 1), ("direct-method.body", 1),
        ("inherited.field", 1), ("inherited.ctor", 1), ("inherited.before", 1), ("inherited.body", 1),
        ("marked-derived.field", 1), ("marked-derived.ctor", 1), ("marked-derived.before", 1), ("marked-derived.body", 1),
        ("nested.field", 1), ("nested.ctor", 1), ("nested.before", 1), ("nested.body", 1),
        ("generic.field", 1), ("generic.ctor", 1), ("generic.before", 1), ("generic.body", 1),
        ("overload.field", 2), ("overload.ctor", 2), ("overload.before", 2),
        ("overload-int.body", 1), ("overload-string.body", 1),
        ("portable.body", 1),
    ];

    [Test]
    [Timeout(60_000)]
    public async Task Native_child_observes_platform_outcomes_and_side_effects(CancellationToken cancellationToken)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "native-linux-probe", "Antiphon.NativeLinuxPolicy.Probe.dll");
        File.Exists(dll).ShouldBeTrue(dll);
        var root = Directory.CreateTempSubdirectory("c1613-probe-").FullName;
        var sentinel = Path.Combine(root, "sentinel.txt");
        try
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
            start.ArgumentList.Add(dll);
            start.ArgumentList.Add("--report-trx");
            start.ArgumentList.Add("--report-trx-filename");
            start.ArgumentList.Add("probe.trx");
            start.ArgumentList.Add("--results-directory");
            start.ArgumentList.Add(root);
            start.Environment["TUNIT_MAX_PARALLEL_TESTS"] = "1";
            start.Environment["NATIVE_LINUX_PROBE_SENTINEL"] = sentinel;
            var ran = await C1514Support.RunProcess(start, cancellationToken);
            var trxPath = Path.Combine(root, "probe.trx");
            File.Exists(trxPath).ShouldBeTrue(ran.Text);
            var trx = TrxReport.Parse(trxPath);
            trx.Ok.ShouldBeTrue(trx.Error ?? ran.Text);
            var live = Problems(OperatingSystem.IsLinux(), trx.Passed, trx.Failed, trx.Skipped, trx.Results.Count,
                File.Exists(sentinel) ? File.ReadAllText(sentinel) : "");
            live.ShouldBeEmpty(string.Join("; ", live) + "\n" + ran.Text);
            ran.ExitCode.ShouldBe(0, ran.Text);
            if (!OperatingSystem.IsLinux())
            {
                var document = XDocument.Load(trxPath);
                AssertWindowsResults(document);
                var changed = new XDocument(document);
                changed.Descendants(document.Root!.Name.Namespace + "DebugTrace").First().Value += "changed";
                Should.Throw<ShouldAssertException>(() => AssertWindowsResults(changed));
            }

            Problems(linux: true, 9, 0, 0, 0, "").ShouldContain("zero-nodes");
            Problems(linux: true, 9, 0, 0, 9, "").ShouldContain("missing-sentinel");
            Problems(linux: false, 1, 0, 8, 9, "direct-class.body\n").ShouldContain("unexpected-work");
            Problems(linux: false, 1, 0, 8, 9, "portable.body\n").ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Directory.Exists(root).ShouldBeFalse();
    }

    private static List<string> Problems(bool linux, int passed, int failed, int skipped, int names, string sentinel)
    {
        var problems = new List<string>();
        if (names == 0)
            problems.Add("zero-nodes");
        if (linux)
        {
            if (passed != 9 || failed != 0 || skipped != 0)
                problems.Add($"linux-counts passed={passed} failed={failed} skipped={skipped}");
            if (sentinel.Length == 0)
                problems.Add("missing-sentinel");
            foreach (var (token, count) in LinuxEffects)
            {
                var seen = Count(sentinel, token);
                if (seen != count)
                    problems.Add($"{token} seen={seen} want={count}");
            }
        }
        else
        {
            if (passed != 1 || failed != 0 || skipped != 8)
                problems.Add($"windows-counts passed={passed} failed={failed} skipped={skipped}");
            if (Count(sentinel, "portable.body") != 1)
                problems.Add("missing-portable-sentinel");
            if (sentinel.Contains("direct-class.", StringComparison.Ordinal)
                || sentinel.Contains("direct-method.", StringComparison.Ordinal)
                || sentinel.Contains("inherited.", StringComparison.Ordinal)
                || sentinel.Contains("marked-derived.", StringComparison.Ordinal)
                || sentinel.Contains("nested.", StringComparison.Ordinal)
                || sentinel.Contains("generic.", StringComparison.Ordinal)
                || sentinel.Contains("overload", StringComparison.Ordinal))
                problems.Add("unexpected-work");
        }

        return problems;
    }

    private static void AssertWindowsResults(XDocument document)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Runs"] = "direct class runs on Linux",
            ["Marked"] = "direct method runs on Linux",
            ["Inherited"] = "inherited method runs on Linux",
            ["FromBase"] = "marked derived class runs on Linux",
            ["Nested"] = "nested class runs on Linux",
            ["GenericCase"] = "generic class runs on Linux",
            ["Case(1)"] = "overload int runs on Linux",
            ["Case(a)"] = "overload string runs on Linux",
        };
        var ns = document.Root!.Name.Namespace;
        var results = document.Descendants(ns + "UnitTestResult").ToArray();
        var definitions = document.Descendants(ns + "UnitTest").ToArray();
        results.Select(result => (string?)result.Attribute("testName") ?? "").Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(expected.Keys.Append("Portable").Order(StringComparer.Ordinal).ToArray(), "native child roster");
        definitions.Length.ShouldBe(9, "native child definitions");
        results.Select(result => (string?)result.Attribute("testId")).Distinct().Count().ShouldBe(9, "native child result identities");
        foreach (var result in results)
        {
            var definition = definitions.Single(item => (string?)item.Attribute("id") == (string?)result.Attribute("testId"));
            definition.Element(ns + "TestMethod").ShouldNotBeNull("native child definition binding");
            var name = (string)result.Attribute("testName")!;
            var skipped = expected.TryGetValue(name, out var reason);
            ((string?)result.Attribute("outcome")).ShouldBe(skipped ? "NotExecuted" : "Passed", name);
            if (skipped)
                (result.Element(ns + "Output")?.Element(ns + "DebugTrace")?.Value)
                    .ShouldBe("Skipped: " + reason, name + " exact skip reason");
        }
    }

    private static int Count(string text, string token)
    {
        var seen = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Equals(token, StringComparison.Ordinal))
                seen++;
        }

        return seen;
    }
}
