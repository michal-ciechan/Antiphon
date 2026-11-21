using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Rc;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S5a: owned synthetic evidence only, in-process (moved from test-rc-historical-evidence.ps1). Extraction
/// streams a large summary with bounded buffers, binds every byte it read (a tampered bundle, or a source that changes
/// while it is read, refuses), and refuses duplicate, torn, traversing, linked or over-deep input.
/// </summary>
[Category("Integration")]
[Timeout(180_000)]
public sealed class RcHistoricalEvidenceTests : IDisposable
{
    // RunnerTempSweep pins this prefix to this file (family "rh", ^rh-[0-9a-f]{32}\z).
    private readonly string results = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rh-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => ReadOnlyDirectory.Delete(results);

    private static string Checkout => RcInputFixtureRepository.SourceRoot;

    private string NewRoot(string prefix) => Directory.CreateDirectory(Path.Combine(results, prefix + "-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    private string NewEvidenceRoot() => Path.Combine(results, Guid.NewGuid().ToString("N"));

    private HistoricalEvidence.Result ExportFixture(string root, params string[] paths) =>
        HistoricalEvidence.Export(root, NewEvidenceRoot(), Checkout, Checkout, paths.Length == 0 ? ["summary.json"] : paths, null);

    private static void Refuses(Action action, string reason)
    {
        var message = "";
        try { action(); }
        catch (Exception e) { message = e.ToString(); }
        message.ShouldContain(reason, Case.Sensitive, reason + " => " + message);
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // Default-deny extraction must still produce a verified immutable bundle before any case runs.
    private void RequireExtraction()
    {
        var probe = NewRoot("probe");
        WriteText(Path.Combine(probe, "summary.json"), "{\"runId\":\"probe\"}");
        ExportFixture(probe).valid.ShouldBeTrue("default-deny extraction did not produce a verified immutable bundle");
    }

    private void NativeHistoricalHandle()
    {
        var nativeSource = NewRoot("native-handle");
        WriteText(Path.Combine(nativeSource, "summary.json"), "{\"runId\":\"native-handle\"}");
        var nativeBundle = HistoricalEvidence.Export(nativeSource, NewEvidenceRoot(), Checkout, Checkout, ["summary.json"], null);
        nativeBundle.valid.ShouldBeTrue("production native handle factory failed");
        nativeBundle.files[0].length.ShouldBe(25, "native handle read wrong bytes");
    }

    // The working-set delta is the test process's: run alone.
    [Test, NotInParallel]
    public void LargeSummaryIsBounded()
    {
        RequireExtraction();
        var source = NewRoot("large");
        var summary = Path.Combine(source, "summary.json");
        var watch = Stopwatch.StartNew();
        using (var writer = new FileStream(summary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, FileOptions.SequentialScan))
        {
            var prefix = Encoding.UTF8.GetBytes("{\"runId\":\"fixture-run\",\"candidateSha\":\"0123456789012345678901234567890123456789\",\"stdout\":\"");
            writer.Write(prefix, 0, prefix.Length);
            var block = Encoding.ASCII.GetBytes(new string('x', 1048576));
            for (var i = 0; i < 885; i++) writer.Write(block, 0, block.Length);
            var suffix = Encoding.UTF8.GetBytes("\",\"nested\":[{\"ignored\":true}],\"complete\":true}");
            writer.Write(suffix, 0, suffix.Length);
        }
        var generationSeconds = watch.Elapsed.TotalSeconds;
        WriteText(Path.Combine(source, "chunk.timing.json"), "{\"suite\":\"antiphon\",\"chunk\":\"one\",\"runWallSeconds\":1.25,\"lastPhase\":\"complete\"}");
        WriteText(Path.Combine(source, "chunk.trx"),
            "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results><UnitTestResult testId=\"a\" outcome=\"Passed\" duration=\"00:00:01\" /></Results>"
            + "<TestDefinitions><UnitTest id=\"a\"><TestMethod className=\"Fixture.Tests\" name=\"Runs\" /></UnitTest></TestDefinitions></TestRun>");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var baseline = Process.GetCurrentProcess().PeakWorkingSet64;
        var exportClock = Stopwatch.StartNew();
        var result = ExportFixture(source, "summary.json", "chunk.timing.json", "chunk.trx");
        var exportSeconds = exportClock.Elapsed.TotalSeconds;
        var peak = Process.GetCurrentProcess().PeakWorkingSet64 - baseline;
        result.files.Count.ShouldBe(3, "inventory lost files");
        var entry = result.files.First(f => f.relativePath == "summary.json");
        entry.length.ShouldBeGreaterThan(885L * 1048576, "fixture is smaller than 885 MiB");
        entry.sha256.ShouldBe(FileSha256(summary), "raw digest mismatch");
        (result.maxBufferedBytes <= 1048576 && result.maxBufferedBytes > 0).ShouldBeTrue("parser buffer bound");
        peak.ShouldBeLessThanOrEqualTo(268435456, "working set delta: " + peak);
        var rows = File.ReadAllLines(Path.Combine(result.evidenceRoot, "rows.jsonl")).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        static string? Text(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static string? AttributeOf(JsonElement row, string name) => row.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? Text(a, name) : null;
        rows.Count(r => Text(r, "field") == "runId" && Text(r, "value") == "fixture-run").ShouldBe(1, "normalized summary runId");
        rows.Count(r => Text(r, "kind") == "UnitTestResult" && AttributeOf(r, "outcome") == "Passed").ShouldBe(1, "TRX result row");
        rows.Count(r => Text(r, "kind") == "TestMethod" && AttributeOf(r, "className") == "Fixture.Tests").ShouldBe(1, "TRX method row");
        rows.Count(r => Text(r, "field") == "runWallSeconds" && r.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.GetDouble() == 1.25)
            .ShouldBe(1, "timing scalar row");
        HistoricalEvidence.Verify(result.evidenceRoot, null).valid.ShouldBeTrue("fresh bundle did not reverify");
        watch.Elapsed.TotalSeconds.ShouldBeLessThanOrEqualTo(60, "streaming extraction exceeded the 60s target: " + watch.Elapsed.TotalSeconds);
        Console.WriteLine("RH metrics bytes=" + entry.length + " peakDelta=" + peak + " maxBufferedBytes=" + result.maxBufferedBytes + " elapsed=" + watch.Elapsed.TotalSeconds
            + " generation=" + generationSeconds + " export=" + exportSeconds + " freeze=" + result.freezeSeconds + " parse=" + result.parseSeconds + " verify=" + result.verifySeconds);
    }

    [Test]
    public void TamperAndChangingReadRefuse()
    {
        RequireExtraction();
        NativeHistoricalHandle();
        var source = NewRoot("tamper");
        var path = Path.Combine(source, "summary.json");
        WriteText(path, "{\"runId\":\"first\"}");
        var before = FileSha256(path);
        var bundle = ExportFixture(source);
        FileSha256(path).ShouldBe(before, "extractor changed source");
        WriteText(path, "{\"runId\":\"other\"}");
        var next = ExportFixture(source);
        bundle.files[0].sha256.ShouldNotBe(next.files[0].sha256, "same path reused cached digest");
        WriteText(Path.Combine(bundle.evidenceRoot, bundle.files[0].bundlePath), "{\"runId\":\"other\"}");
        Refuses(() => HistoricalEvidence.Verify(bundle.evidenceRoot, null), "evidence-digest");
        // The hook executes after opening/inspecting the handle, before copying it.
        var changing = Path.Combine(results, "changing");
        Refuses(() => HistoricalEvidence.Export(source, changing, Checkout, Checkout, ["summary.json"], () => WriteText(path, "{\"runId\":\"third\"}")), "evidence-changing");
        File.Exists(Path.Combine(changing, "inventory.json")).ShouldBeFalse("changing read published inventory");
        Refuses(() => HistoricalEvidence.Verify(changing, null), "inventory.json");
        // Replacement with equal length and a restored mtime must also refuse.
        Refuses(() => HistoricalEvidence.Export(source, Path.Combine(results, "same-stat"), Checkout, Checkout, ["summary.json"], () =>
        {
            var time = File.GetLastWriteTimeUtc(path);
            WriteText(path, "{\"runId\":\"forth\"}");
            File.SetLastWriteTimeUtc(path, time);
        }), "evidence-changing");
    }

    [Test]
    public void DuplicateTraversalAndTornInputRefuse()
    {
        RequireExtraction();
        var source = NewRoot("invalid");
        var path = Path.Combine(source, "summary.json");
        foreach (var bad in new[]
                 {
                     "{\"a\":1,\"a\":2}", "{\"nested\":{\"a\":1,\"A\":2}}", "{\"runId\":\"torn", "{\"x\":[1,]}", "{\"x\":01}", @"{""x"":""\uD800""}",
                     @"{""x"":""bad\q""}",
                 })
        {
            WriteText(path, bad);
            Refuses(() => ExportFixture(source), "evidence-json");
        }
        File.WriteAllBytes(path, [123, 34, 120, 34, 58, 34, 255, 34, 125]);
        Refuses(() => ExportFixture(source), "evidence-json");
        WriteText(path, "{\"runId\":\"valid\"}");
        Refuses(() => ExportFixture(source, "../summary.json"), "evidence-path");
        Refuses(() => ExportFixture(source, "summary.json", "SUMMARY.json"), "evidence-path");
        Refuses(() => ExportFixture(source, "summary.json", "summary.json"), "evidence-path");
        Refuses(() => ExportFixture(source, "a/one.json", "A/two.json"), "evidence-path");
        Refuses(() => HistoricalEvidence.Export(source, Path.Combine(source, "output"), Checkout, Checkout, null, null), "evidence-output");
        Refuses(() => HistoricalEvidence.Export(source, Path.Combine(Checkout, ".antiphon", "rh-output"), Checkout, Checkout, null, null), "evidence-output");
        var link = Path.Combine(source, "link.json");
        File.CreateSymbolicLink(link, path);
        Refuses(() => ExportFixture(source, "link.json"), "evidence-link");
        // Real hard links, not a mocked link count.
        NativeHardLink.Create(Path.Combine(source, "hard.json"), path);
        Refuses(() => ExportFixture(source), "evidence-link");
        WriteText(Path.Combine(source, "unsafe.trx"), "<!DOCTYPE TestRun [<!ENTITY x \"boom\">]><TestRun>&x;</TestRun>");
        Refuses(() => ExportFixture(source, "unsafe.trx"), "evidence-xml");
        WriteText(Path.Combine(source, "deep.json"), new string('[', 65) + "0" + new string(']', 65));
        Refuses(() => ExportFixture(source, "deep.json"), "evidence-depth");
        WriteText(Path.Combine(source, "scalar.json"), "{\"runId\":\"" + new string('x', 1048577) + "\"}");
        Refuses(() => ExportFixture(source, "scalar.json"), "evidence-scalar");
    }
}
