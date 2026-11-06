using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;

namespace Antiphon.Tests.Checkpoints;

// Literal inputs, independent of the production serializers and diagnostic fixture writer.
internal static class C1217ConditionalSkipFixture
{
    // XML 1.0 Char boundaries, with literal spellings independent of the parser.
    internal static IEnumerable<(string Reference, string? Value)> CharacterReferences()
    {
        foreach (var value in Enumerable.Range(0, 32).Except([9, 10, 13])
            .Concat([0xD800, 0xDBFF, 0xDC00, 0xDFFF, 0xFFFE, 0xFFFF, 0x110000, int.MaxValue]))
        {
            yield return ($"&#{value};", null);
            yield return ($"&#x{value:X};", null);
        }
        foreach (var reference in new[] { "&#;", "&#x;", "&#xZZ;", "&#-1;", "&#X20;", "&#999999999999999999999;", "&#xFFFFFFFFFFFFFFFF;" })
            yield return (reference, null);
        foreach (var value in new[] { 9, 10, 13, 0x20, 0xD7FF, 0xE000, 0xFFFD, 0x10000, 0x10FFFF })
        {
            yield return ($"&#{value};", char.ConvertFromUtf32(value));
            yield return ($"&#x{value:X};", char.ConvertFromUtf32(value));
        }
    }

    internal static string WithReference(string xml, string reference, string context) => context switch
    {
        "attribute" => xml.Replace("testName=\"same display\"", "testName=\"first" + reference + "second\"", StringComparison.Ordinal),
        "text" => xml.Replace("<Results>", "<Note>first" + reference + "second</Note><Results>", StringComparison.Ordinal),
        "reason" => xml.Replace("fixture capability unavailable", "first" + reference + "second", StringComparison.Ordinal),
        _ => throw new ArgumentException("unknown XML context", nameof(context)),
    };

    internal const string Catalog = """
        {"schemaVersion":1,"contracts":[{"id":"c1217-fixture-v1","probe":"file-symlink-create-v1","project":"tests/Fixture","filter":"/*/Fixture/Cases/*","expectedTotal":3,"minExecuted":1,"skips":[{"className":"Fixture.Cases","method":"LinkA","count":1,"reason":"fixture capability unavailable"},{"className":"Fixture.Cases","method":"LinkB","count":1,"reason":"fixture capability unavailable"}]}]}
        """;

    internal const string Yaml = """
        schemaVersion: 1
        builds:
          - id: bin-fixture
            project: tests/Fixture
            outputPath: bin-fixture/
        checkpoints:
          - id: CP-1
            after: [S1]
            build: bin-fixture
            filter: /*/Fixture/Cases/*
            minExecuted: 1
        """;

    internal const string Table = """
        ### Checkpoints
        | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | ConditionalSkips |
        |---|---|---|---|---|---|---|---|---|---|
        | CP-1 | S1 | tests/Fixture -> bin-fixture/ | fixture | /*/Fixture/Cases/* | V-1 | green | 1 | 1 | c1217-fixture-v1 |
        """;

    internal const string UnavailableTrx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="s" executionId="es" testName="same display" outcome="Passed" />
            <UnitTestResult testId="a" executionId="ea" testName="same display" outcome="NotExecuted"><Output><DebugTrace>Skipped: fixture capability unavailable</DebugTrace></Output></UnitTestResult>
            <UnitTestResult testId="b" executionId="eb" testName="same display" outcome="NotExecuted"><Output><DebugTrace>Skipped: fixture capability unavailable</DebugTrace></Output></UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest id="s"><TestMethod className="Fixture.Cases" name="Sentinel" /></UnitTest>
            <UnitTest id="a"><TestMethod className="Fixture.Cases" name="LinkA" /></UnitTest>
            <UnitTest id="b"><TestMethod className="Fixture.Cases" name="LinkB" /></UnitTest>
          </TestDefinitions>
          <ResultSummary><Counters total="3" executed="1" passed="1" failed="0" notExecuted="2" /></ResultSummary>
        </TestRun>
        """;

    internal const string AvailableTrx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="s" executionId="es" testName="same display" outcome="Passed" />
            <UnitTestResult testId="a" executionId="ea" testName="same display" outcome="Passed" />
            <UnitTestResult testId="b" executionId="eb" testName="same display" outcome="Passed" />
          </Results>
          <TestDefinitions>
            <UnitTest id="s"><TestMethod className="Fixture.Cases" name="Sentinel" /></UnitTest>
            <UnitTest id="a"><TestMethod className="Fixture.Cases" name="LinkA" /></UnitTest>
            <UnitTest id="b"><TestMethod className="Fixture.Cases" name="LinkB" /></UnitTest>
          </TestDefinitions>
          <ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0" /></ResultSummary>
        </TestRun>
        """;

    internal static ConditionalSkipCatalog ParseCatalog(string json) => ConditionalSkipPolicyReader.Parse(Encoding.UTF8.GetBytes(json));
    internal static JsonElement Strict(string path, int total = 3) =>
        JsonSerializer.SerializeToElement(TrxReport.ParseStrict(path, total));
    internal static ConditionalSkipContract Resolve(ConditionalSkipCatalog catalog, CheckpointManifest manifest, CheckpointSpec row) =>
        ConditionalSkipPolicyReader.Resolve(catalog, manifest, row);
    internal static void Combinations(CheckpointManifest manifest, bool merged) =>
        ConditionalSkipContract.ValidateCombinations(manifest, merged);
}
