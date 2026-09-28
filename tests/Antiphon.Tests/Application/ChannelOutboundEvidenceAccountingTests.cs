using System.Xml.Linq;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ChannelOutboundEvidenceAccountingTests
{
    private const string Method = "Antiphon.Tests.Application.Sample.Named_case";

    [Test]
    public void Zero_tests_skips_and_missing_required_ids_cannot_claim_ordinary_completion()
    {
        var root = Directory.CreateTempSubdirectory("c0418-evidence-").FullName;
        try
        {
            var green = WriteTrx(root, "green", "Passed");
            var zero = WriteTrx(root, "zero", null);
            var skipped = WriteTrx(root, "skipped", "NotExecuted");
            ChannelOutboundEvidenceAccounting.ValidateOrdinary(["V-1"],
                [new("V-1", Method, green)]).ShouldBeEmpty();
            ChannelOutboundEvidenceAccounting.ValidateOrdinary(["V-1", "R-1"],
                [new("V-1", Method, green)]).ShouldContain(e => e.Contains("R-1"));
            ChannelOutboundEvidenceAccounting.ValidateFullOrdinary(
                [new("V-1", Method, green)]).Count.ShouldBe(37);
            ChannelOutboundEvidenceAccounting.RequiredControlIds.Count.ShouldBe(30);
            ChannelOutboundEvidenceAccounting.ValidateOrdinary(["V-1"],
                [new("V-1", Method, zero)]).ShouldContain(e => e.Contains("non-skipped green"));
            ChannelOutboundEvidenceAccounting.ValidateOrdinary(["V-1"],
                [new("V-1", Method, skipped)]).ShouldContain(e => e.Contains("non-skipped green"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Positive_control_needs_named_assertion_red_and_separate_restored_green()
    {
        var root = Directory.CreateTempSubdirectory("c0418-control-").FullName;
        try
        {
            var baseline = WriteTrx(root, "baseline", "Passed");
            var red = WriteTrx(root, "red", "Failed", "Expected source hash mismatch", "at Shouldly.Assert");
            var restored = WriteTrx(root, "restored", "Passed");
            var claim = new ChannelOutboundEvidenceAccounting.Control("PC-1", "default-off", Method,
                baseline, red, restored, "source hash mismatch");
            ChannelOutboundEvidenceAccounting.ValidateControls([("PC-1", "default-off")],
                [claim]).ShouldBeEmpty();
            ChannelOutboundEvidenceAccounting.ValidateControls([("PC-1", "default-off")],
                [claim with { RestoredTrx = red }]).ShouldContain(e => e.Contains("restored"));
            ChannelOutboundEvidenceAccounting.ValidateControls([("PC-1", "default-off")],
                [claim with { RedTrx = WriteTrx(root, "fixture-failure", "Failed",
                    "Docker unavailable", "at Fixture.Start") }])
                .ShouldContain(e => e.Contains("assertion-red"));
            ChannelOutboundEvidenceAccounting.ValidateControls([("PC-1", "default-off")], [])
                .ShouldContain(e => e.Contains("expected one control"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Local_marker_cannot_discharge_the_native_receipt_gate()
    {
        var root = Directory.CreateTempSubdirectory("c0418-live-accounting-").FullName;
        try
        {
            var fake = Path.Combine(root, "local-pdf.pdf");
            File.WriteAllBytes(fake, "%PDF-local"u8.ToArray());
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fake)));
            ChannelOutboundEvidenceAccounting.ValidateLiveReceipt(null)
                .ShouldContain(e => e.Contains("native receipt absent"));
            var local = new ChannelOutboundEvidenceAccounting.LiveReceipt("isolated fixture", "fake Slack",
                "synthetic-message", "synthetic-file", fake, sha, false);
            ChannelOutboundEvidenceAccounting.ValidateLiveReceipt(local)
                .ShouldContain(e => e.Contains("actual installation"));
            ChannelOutboundEvidenceAccounting.ValidateLiveReceipt(local with
                { IsActualInstallation = true, Installation = "claimed live", Destination = "claimed thread" })
                .ShouldContain(e => e.Contains("independent native destination review"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string WriteTrx(string root, string name, string? outcome,
        string? message = null, string? stack = null)
    {
        var path = Path.Combine(root, name + ".trx");
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        var result = outcome is null ? null : new XElement(ns + "UnitTestResult",
            new XAttribute("testId", "1"), new XAttribute("testName", Method),
            new XAttribute("outcome", outcome),
            outcome == "Failed" ? new XElement(ns + "Output", new XElement(ns + "ErrorInfo",
                new XElement(ns + "Message", message ?? ""),
                new XElement(ns + "StackTrace", stack ?? ""))) : null);
        var executed = outcome is null or "NotExecuted" ? 0 : 1;
        var doc = new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "TestDefinitions", new XElement(ns + "UnitTest",
                new XAttribute("id", "1"), new XElement(ns + "TestMethod",
                    new XAttribute("className", "Antiphon.Tests.Application.Sample"),
                    new XAttribute("name", "Named_case")))),
            new XElement(ns + "Results", result),
            new XElement(ns + "ResultSummary", new XElement(ns + "Counters",
                new XAttribute("total", outcome is null ? 0 : 1),
                new XAttribute("executed", executed),
                new XAttribute("passed", outcome == "Passed" ? 1 : 0),
                new XAttribute("failed", outcome == "Failed" ? 1 : 0)))));
        doc.Save(path);
        return path;
    }
}
