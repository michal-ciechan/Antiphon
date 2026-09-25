using System.Xml.Linq;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class TrxReportTests
{
    [Test]
    public void joins_results_to_class_qualified_names()
    {
        var parsed = TrxReport.Parse(CheckpointFixtures.Trx("c585-green.trx"));
        parsed.Ok.ShouldBeTrue();
        parsed.ExecutedNames.ShouldBe([
            "Antiphon.Tests.Scripts.C585SampleTests.alpha_is_green",
            "Antiphon.Tests.Scripts.C585SampleTests.beta_is_green",
            "Antiphon.Tests.Scripts.C585OtherTests.gamma_is_green",
        ]);
        parsed.ExecutedNames.ShouldNotContain(name => name.Contains("display", StringComparison.Ordinal));
        parsed.Executed.ShouldBe(3);
        parsed.Passed.ShouldBe(3);
        parsed.Failed.ShouldBe(0);
    }

    [Test]
    public void counts_failed_error_timeout_aborted()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "mix.trx");
        CheckpointFixtures.WriteResults(path,
            ("Antiphon.Tests.Sample.ok", "Passed"),
            ("Antiphon.Tests.Sample.bad", "Failed"),
            ("Antiphon.Tests.Sample.err", "Error"),
            ("Antiphon.Tests.Sample.slow", "Timeout"),
            ("Antiphon.Tests.Sample.dead", "Aborted"));
        var parsed = TrxReport.Parse(path);
        parsed.FailureNames.Count.ShouldBe(4);
        parsed.Failures.Select(failure => failure.Outcome).ShouldBe(["Failed", "Error", "Timeout", "Aborted"]);
    }

    [Test]
    public void skips_not_executed()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "skip.trx");
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results",
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "1"), new XAttribute("outcome", "Passed"), new XAttribute("duration", "00:00:01")),
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "2"), new XAttribute("outcome", "NotExecuted"), new XAttribute("duration", "00:00:00"))),
            new XElement(ns + "TestDefinitions",
                new XElement(ns + "UnitTest", new XAttribute("id", "1"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "N.C"), new XAttribute("name", "ran"))),
                new XElement(ns + "UnitTest", new XAttribute("id", "2"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "N.C"), new XAttribute("name", "skipped")))),
            new XElement(ns + "ResultSummary",
                new XElement(ns + "Counters", new XAttribute("total", "2"), new XAttribute("executed", "1"), new XAttribute("passed", "1"), new XAttribute("failed", "0")))))
            .Save(path);
        var parsed = TrxReport.Parse(path);
        parsed.ExecutedNames.ShouldBe(["N.C.ran"]);
        parsed.Skipped.ShouldBe(1);
    }

    [Test]
    public void falls_back_when_counters_missing()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "nocounters.trx");
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results",
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "1"), new XAttribute("outcome", "Passed"), new XAttribute("duration", "00:00:01")),
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "2"), new XAttribute("outcome", "Passed"), new XAttribute("duration", "00:00:01"))),
            new XElement(ns + "TestDefinitions",
                new XElement(ns + "UnitTest", new XAttribute("id", "1"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "N.C"), new XAttribute("name", "a"))),
                new XElement(ns + "UnitTest", new XAttribute("id", "2"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "N.C"), new XAttribute("name", "b"))))))
            .Save(path);
        var parsed = TrxReport.Parse(path);
        parsed.Executed.ShouldBe(2);
        parsed.Passed.ShouldBe(2);
        parsed.Failed.ShouldBe(0);
    }

    [Test]
    public void reads_message_stack_and_stdout()
    {
        var parsed = TrxReport.Parse(CheckpointFixtures.Fixture("c723-failure-with-stack.trx"));
        var failure = parsed.Failures.ShouldHaveSingleItem();
        failure.Name.ShouldBe("Antiphon.Tests.Checkpoints.SampleTests.boom");
        failure.Message.ShouldContain("Expected 1 but was 2");
        failure.StackTrace.ShouldContain("Sample.cs:line 4");
        failure.StdOut.ShouldContain("stdout line one");
        parsed.ExecutedNames.ShouldNotContain(name => name.Contains("skipped", StringComparison.Ordinal));
    }

    [Test]
    public void aggregates_seconds_per_class()
    {
        var parsed = TrxReport.Parse(CheckpointFixtures.Trx("c585-green.trx"));
        var sample = parsed.SlowClasses.Single(item => item.ClassName == "Antiphon.Tests.Scripts.C585SampleTests");
        sample.Tests.ShouldBe(2);
        sample.Seconds.ShouldBe(0.03, 0.001);
    }

    [Test]
    public void malformed_trx_is_exit_2()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "bad.trx");
        File.WriteAllText(path, "this is not xml");
        var parsed = TrxReport.Parse(path);
        parsed.Ok.ShouldBeFalse();
        parsed.ExitCode.ShouldBe(2);
        TrxReport.Parse(Path.Combine(CheckpointFixtures.TempDir(), "missing.trx")).ExitCode.ShouldBe(2);
    }
}
