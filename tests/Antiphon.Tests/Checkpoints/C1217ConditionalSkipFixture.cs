using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;

namespace Antiphon.Tests.Checkpoints;

// Literal inputs, independent of the production serializers and diagnostic fixture writer.
internal static class C1217ConditionalSkipFixture
{
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
