using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts.Nightly;
using static Antiphon.Tests.Scripts.Nightly.LiveLaneGateScanTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class LiveLaneGateScanInvariantTests
{
    [Test]
    public void ScanGates()
    {
        var repo = LiveLaneInventoryTests.LiveLaneProcess.FindRepo();
        var output = Path.Combine(Path.GetTempPath(), "antiphon-gate-catalog-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var written = LiveLaneGateScan.WriteCatalog(repo, output);
            written.Path.ShouldBe(output);
            var scan = LiveLaneGateScan.Scan(repo);
            written.Count.ShouldBe(scan.Gates.Count);
            written.Unresolved.ShouldBe(scan.Unresolved);

            var committedText = File.ReadAllText(Path.Combine(repo, "tests", "live-lanes.json")).ReplaceLineEndings("\n");
            var rewrittenText = File.ReadAllText(output).ReplaceLineEndings("\n");
            using var committed = JsonDocument.Parse(committedText);
            using var rewritten = JsonDocument.Parse(rewrittenText);
            rewrittenText.ShouldStartWith("{\n  \"format\": \"antiphon-live-lanes-v1\",\n  \"core\": [");
            rewrittenText.ShouldEndWith("\n}\n");
            foreach (var name in new[] { "core", "additiveContract" })
                rewritten.RootElement.GetProperty(name).GetRawText().ShouldBe(committed.RootElement.GetProperty(name).GetRawText(), name);

            var rows = rewritten.RootElement.GetProperty("gates").EnumerateArray().ToList();
            rows.Count.ShouldBe(scan.Gates.Count);
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows) byKey.TryAdd(Key(row), row.GetRawText());
            foreach (var gate in scan.Gates) byKey.ShouldContainKey(gate.Key);
            var carried = 0;
            foreach (var row in committed.RootElement.GetProperty("gates").EnumerateArray())
            {
                if (!byKey.TryGetValue(Key(row), out var text)) continue;
                text.ShouldBe(row.GetRawText(), Key(row));
                carried++;
            }
            carried.ShouldBeGreaterThan(0);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
