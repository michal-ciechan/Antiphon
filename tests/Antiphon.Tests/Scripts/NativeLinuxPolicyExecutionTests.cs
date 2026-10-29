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

    private static readonly string[] ProbeMarkerReasons =
    [
        "direct class runs on Linux",
        "direct method runs on Linux",
        "inherited method runs on Linux",
        "marked derived class runs on Linux",
        "nested class runs on Linux",
        "generic class runs on Linux",
        "overload int runs on Linux",
        "overload string runs on Linux",
    ];

    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [Test]
    [Timeout(60_000)]
    public async Task Native_child_observes_platform_outcomes_and_side_effects(CancellationToken cancellationToken)
    {
        const string portableSentinel = "portable.body\n";
        JudgeWindowsTrx(WindowsProbeTrx(ProbeMarkerReasons, markedRuns: false), portableSentinel).ShouldBeEmpty();
        var wrongReasons = (string[])ProbeMarkerReasons.Clone();
        wrongReasons[0] = "not the marker";
        string.Join("\n", JudgeWindowsTrx(WindowsProbeTrx(wrongReasons, markedRuns: false), portableSentinel))
            .ShouldContain("skip-reason");
        string.Join("\n", JudgeWindowsTrx(WindowsProbeTrx(ProbeMarkerReasons, markedRuns: true), portableSentinel))
            .ShouldContain("windows-counts");

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
            var sentinelText = File.Exists(sentinel) ? File.ReadAllText(sentinel) : "";
            var live = Problems(OperatingSystem.IsLinux(), trx.Passed, trx.Failed, trx.Skipped, trx.Results.Count,
                sentinelText, ReadSkipMessages(trxPath));
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
            Problems(linux: false, 1, 0, 8, 9, "portable.body\n", ExpectedSkipMessages()).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Directory.Exists(root).ShouldBeFalse();
    }

    private static List<string> Problems(bool linux, int passed, int failed, int skipped, int names, string sentinel, IReadOnlyList<string>? skipMessages = null)
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
            if (skipMessages is not null)
                AddSkipProblems(problems, skipMessages);
        }

        return problems;
    }

    private static string[] ExpectedSkipMessages()
    {
        var messages = new string[ProbeMarkerReasons.Length];
        for (var i = 0; i < ProbeMarkerReasons.Length; i++)
            messages[i] = "Skipped: " + ProbeMarkerReasons[i];
        return messages;
    }

    private static void AddSkipProblems(List<string> problems, IReadOnlyList<string> actual)
    {
        var pending = new List<string>(actual);
        foreach (var want in ExpectedSkipMessages())
        {
            var index = pending.FindIndex(item => string.Equals(item, want, StringComparison.Ordinal));
            if (index < 0)
                problems.Add("skip-reason " + want);
            else
                pending.RemoveAt(index);
        }

        foreach (var extra in pending)
            problems.Add("skip-extra " + extra);
    }

    private static List<string> ReadSkipMessages(string trxPath)
    {
        var document = XDocument.Parse(File.ReadAllText(trxPath));
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var messages = new List<string>();
        foreach (var result in document.Descendants(ns + "UnitTestResult"))
        {
            var outcome = (string?)result.Attribute("outcome") ?? "";
            if (!outcome.Equals("NotExecuted", StringComparison.Ordinal))
                continue;
            var traces = result.Elements(ns + "Output").Elements(ns + "DebugTrace").ToArray();
            messages.Add(traces.Length == 1 ? traces[0].Value : "<missing-or-ambiguous>");
        }

        return messages;
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

    private static List<string> JudgeWindowsTrx(string trxXml, string sentinel)
    {
        var root = Directory.CreateTempSubdirectory("c1613-trx-").FullName;
        try
        {
            var path = Path.Combine(root, "probe.trx");
            File.WriteAllText(path, trxXml);
            var trx = TrxReport.Parse(path);
            trx.Ok.ShouldBeTrue(trx.Error);
            return Problems(linux: false, trx.Passed, trx.Failed, trx.Skipped, trx.Results.Count, sentinel, ReadSkipMessages(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string WindowsProbeTrx(string[] reasons, bool markedRuns)
    {
        var definitions = new List<XElement>();
        var results = new List<XElement>();
        for (var i = 0; i < reasons.Length; i++)
        {
            var id = "marked-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var runs = markedRuns && i == 0;
            definitions.Add(new XElement(TrxNamespace + "UnitTest", new XAttribute("id", id),
                new XElement(TrxNamespace + "TestMethod", new XAttribute("className", "Probe.Marked" + i), new XAttribute("name", "Run"))));
            results.Add(new XElement(TrxNamespace + "UnitTestResult", new XAttribute("testId", id), new XAttribute("outcome", runs ? "Passed" : "NotExecuted"),
                runs ? null : new XElement(TrxNamespace + "Output", new XElement(TrxNamespace + "DebugTrace", "Skipped: " + reasons[i]))));
        }

        definitions.Add(new XElement(TrxNamespace + "UnitTest", new XAttribute("id", "portable"),
            new XElement(TrxNamespace + "TestMethod", new XAttribute("className", "Probe.Portable"), new XAttribute("name", "Portable"))));
        results.Add(new XElement(TrxNamespace + "UnitTestResult", new XAttribute("testId", "portable"), new XAttribute("outcome", "Passed")));
        var passed = markedRuns ? 2 : 1;
        var executed = passed;
        return new XDocument(new XElement(TrxNamespace + "TestRun",
            new XElement(TrxNamespace + "TestDefinitions", definitions),
            new XElement(TrxNamespace + "Results", results),
            new XElement(TrxNamespace + "ResultSummary", new XElement(TrxNamespace + "Counters",
                new XAttribute("total", "9"),
                new XAttribute("executed", executed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("passed", passed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("failed", "0"))))).ToString();
    }
}
