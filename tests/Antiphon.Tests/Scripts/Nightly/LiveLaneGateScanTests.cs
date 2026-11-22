using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S23. The compiled gate catalog writer (antiphon-scripts live-lane-scan-gates, the old ScanGates case of
/// scripts/test-live-lane-manifest.ps1) over the repository's own sources, policy and tests/live-lanes.json. The
/// catalog may lag the sources (a new gate has no row yet: EveryLiveGateHasDisposition reports it), so the rewrite is
/// compared row by row: the core and additiveContract rows come back as committed, every committed row of a gate the
/// scan still finds comes back with the same text, and there is one row per scanned gate.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneGateScanTests
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

    [Test]
    public void ScansHeadedFlagReadsAndIgnoresFixtureValues()
    {
        using var fixture = NightlyFixture.Create("live-lane-headed-gate");
        var source = fixture.PathOf("HeadedGateCases.cs");
        // Assemble the name at runtime to keep this scratch source out of the repository scan.
        const string flag = "ANTIPHON_" + "HEADED_TESTS";
        File.WriteAllText(source, $$"""
            namespace Scratch;
            [Category("Unit")]
            public class HeadedGateCases
            {
                [Test]
                public void RequiresFlag()
                {
                    if (Environment.GetEnvironmentVariable("{{flag}}") != "1")
                        throw new InvalidOperationException();
                }

                [Test]
                public void RequiresFlagWithNamedArgument()
                {
                    if (Environment.GetEnvironmentVariable(variable: "{{flag}}") != "1")
                        throw new InvalidOperationException();
                }

                [Test]
                public void SeedsChildEnvironment()
                {
                    var childEnvironment = new Dictionary<string, string> { ["{{flag}}"] = "1" };
                }

                [Test]
                public void LabelsChildEnvironmentCheck() => Console.WriteLine("{{flag}} is cleared");
            }
            """);

        var scan = LiveLaneGateScan.Scan(fixture.Root, source);
        scan.Unresolved.ShouldBe(0);
        scan.Gates.Select(gate => gate.Method).ShouldBe(["RequiresFlag", "RequiresFlagWithNamedArgument"]);
        foreach (var gate in scan.Gates)
            gate.Shapes.ShouldBe([flag]);
    }

    private static string Key(JsonElement row) =>
        LiveLaneGateScan.IdentityKey(row.GetProperty("project").GetString(), row.GetProperty("namespace").GetString(),
            row.GetProperty("class").GetString(), row.GetProperty("method").GetString(),
            row.GetProperty("arguments").EnumerateArray().Select(a => a.GetString() ?? ""));
}
