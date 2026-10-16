using System.Xml.Linq;
using Antiphon.Checkpoints;
using Shouldly;
using Antiphon.Tests.Checkpoints;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1313 S5 red. Duration-sum order is not the wall ranking.
[Category("Unit")]
public sealed class TestTimingReportTests : CheckpointTestBase
{
    [Test]
    public void C1313_ComparableHistory()
    {
        var path = Path.Combine(TempDir(), "slow.trx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results",
                Result(ns, "1", "00:00:08"),
                Result(ns, "2", "00:00:06")),
            new XElement(ns + "TestDefinitions",
                Definition(ns, "1", "X", "a"),
                Definition(ns, "2", "Y", "b")),
            new XElement(ns + "ResultSummary",
                new XElement(ns + "Counters",
                    new XAttribute("total", 2),
                    new XAttribute("executed", 2),
                    new XAttribute("passed", 2),
                    new XAttribute("failed", 0))))).Save(path);
        var order = string.Join(',', TrxReport.Parse(path).SlowClasses.Select(item => item.ClassName));
        order.ShouldBe("Y,X", "wall-ranking");
    }

    private static XElement Result(XNamespace ns, string id, string duration) =>
        new(ns + "UnitTestResult",
            new XAttribute("testId", id),
            new XAttribute("testName", id),
            new XAttribute("outcome", "Passed"),
            new XAttribute("duration", duration));

    private static XElement Definition(XNamespace ns, string id, string className, string method) =>
        new(ns + "UnitTest",
            new XAttribute("id", id),
            new XElement(ns + "TestMethod",
                new XAttribute("className", className),
                new XAttribute("name", method)));
}
