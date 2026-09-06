using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed class GrokRulesLiveEvidenceTests
{
    // This validates the saved real-model experiment, not a synthetic substitute for it.
    // PC-31 mutates only a copy of the captured artifact and restores its original bytes.
    [Test]
    public void Captured_live_rows_match_independent_oracles_and_two_distinct_native_boundaries()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Agents", "Fixtures", "grok-rules-live-inline-1.0.13.json")));
        var root = document.RootElement;
        var manifest = root.GetProperty("manifest");
        manifest.GetProperty("outcome").GetString().ShouldBe("passed");
        manifest.GetProperty("fileArm").GetBoolean().ShouldBeFalse("This fixture certifies the baseline only");
        var ids = Strings(manifest.GetProperty("compactIds"));
        ids.Length.ShouldBe(2);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(2, "Two copied identities cannot certify two genuine boundaries");
        ids.ShouldBe(manifest.GetProperty("nativeBoundaries").EnumerateArray().Select(b => b.GetProperty("Id").GetString()).ToArray());
        var checkpoints = root.GetProperty("checkpoints").EnumerateArray().ToArray();
        checkpoints.Length.ShouldBe(5);
        var oracle = root.GetProperty("expected").GetProperty("expected").EnumerateArray().ToArray();
        for (var i = 0; i < checkpoints.Length; i++)
        {
            var row = checkpoints[i];
            var expected = Strings(row.GetProperty("Expected"));
            var actual = Strings(row.GetProperty("Actual"));
            expected.ShouldBe(Strings(oracle[i]), "A captured expected-answer row must match the separately saved pre-run oracle");
            var verdict = actual.SequenceEqual(expected) ? "survived" : actual.Any(a => a != "<absent>") ? "mixed" : "lost";
            row.GetProperty("Verdict").GetString().ShouldBe(verdict);
            row.GetProperty("EndSequence").GetInt64().ShouldBeGreaterThan(row.GetProperty("PromptSequence").GetInt64());
            row.GetProperty("EndedSeconds").GetDouble().ShouldBeGreaterThan(row.GetProperty("StartedSeconds").GetDouble());
        }
        new[] { checkpoints[1], checkpoints[3] }.Select(r => r.GetProperty("BoundaryId").GetString()).ShouldBe(ids);
    }

    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()).ToArray();
}
