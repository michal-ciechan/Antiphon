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
    public void DetectsHeadedLookupWhenTargetIsNamedFirst()
    {
        var scan = ScanHeadedGateCases($$"""
            [Test]
            public void RequiresFlagWithTargetFirst()
            {
                if (Environment.GetEnvironmentVariable(target: EnvironmentVariableTarget.Process, variable: "{{HeadedFlag}}") != "1")
                    throw new InvalidOperationException();
            }

            [Test]
            public void RequiresFlagWithVariableFirst()
            {
                if (Environment.GetEnvironmentVariable(variable: "{{HeadedFlag}}") != "1")
                    throw new InvalidOperationException();
            }

            [Test]
            public void SeedsChildEnvironment()
            {
                var childEnvironment = new Dictionary<string, string> { ["{{HeadedFlag}}"] = "1" };
                _ = Environment.GetEnvironmentVariable(target: EnvironmentVariableTarget.Process, variable: "OTHER_FLAG");
            }
            """);

        scan.Gates.Select(gate => gate.Method).ShouldBe(["RequiresFlagWithTargetFirst", "RequiresFlagWithVariableFirst"]);
        foreach (var gate in scan.Gates)
            gate.Shapes.ShouldBe([HeadedFlag]);
    }

    [Test]
    public void IgnoresPrintedLookupTextButDetectsExecutableLookup()
    {
        var scan = ScanHeadedGateCases($$"""
            [Test]
            public void PrintsLookupText() =>
                Console.WriteLine("Environment.GetEnvironmentVariable(variable: {{HeadedFlag}})");

            [Test]
            public void PrintsQuotedLookupText() =>
                Console.WriteLine("Environment.GetEnvironmentVariable(\"{{HeadedFlag}}\")");

            [Test]
            public void RequiresFlagFromLookupResult()
            {
                if ($"{Environment.GetEnvironmentVariable("{{HeadedFlag}}")}" != "1")
                    throw new InvalidOperationException();
            }
            """);

        scan.Gates.Select(gate => gate.Method).ShouldBe(["RequiresFlagFromLookupResult"]);
        scan.Gates.Single().Shapes.ShouldBe([HeadedFlag]);
    }

    // Split the name so that the repository scan does not inspect the fixture templates.
    private const string HeadedFlag = "ANTIPHON_" + "HEADED_TESTS";

    private static LiveLaneGateScanResult ScanHeadedGateCases(string methods)
    {
        using var fixture = NightlyFixture.Create("live-lane-headed-gates");
        var source = fixture.PathOf("HeadedGateCases.cs");
        File.WriteAllText(source, $$"""
            namespace Scratch;
            [Category("Unit")]
            public class HeadedGateCases
            {
                {{methods}}
            }
            """);

        var scan = LiveLaneGateScan.Scan(fixture.Root, source);
        scan.Unresolved.ShouldBe(0);
        return scan;
    }

    internal static string Key(JsonElement row) =>
        LiveLaneGateScan.IdentityKey(row.GetProperty("project").GetString(), row.GetProperty("namespace").GetString(),
            row.GetProperty("class").GetString(), row.GetProperty("method").GetString(),
            row.GetProperty("arguments").EnumerateArray().Select(a => a.GetString() ?? ""));
}
