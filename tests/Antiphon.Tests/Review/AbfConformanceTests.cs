using System.Text.Json.Nodes;
using Antiphon.Server.Application.Review.Abf;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// Runs every case of the ABF v1 conformance corpus (docs/specs/block-format/conformance) through the
/// C# parser, with the README's comparison rules. The legacy cases are also checked against today's
/// PipelineHandoff and ReviewEvidence parsers (currentParsers where the case has it).
/// </summary>
[Category("Unit")]
public class AbfConformanceTests
{
    private static readonly string CorpusRoot = FindCorpus();

    public static IEnumerable<Func<string>> Cases()
    {
        foreach (var folder in new[] { "valid", "tolerant", "invalid", "legacy" })
        {
            foreach (var json in Directory.GetFiles(Path.Combine(CorpusRoot, folder), "*.json").Order(StringComparer.Ordinal))
            {
                var name = $"{folder}/{Path.GetFileNameWithoutExtension(json)}";
                yield return () => name;
            }
        }
    }

    [Test]
    public async Task The_corpus_has_the_41_cases_the_spec_lists()
    {
        Cases().Count().ShouldBe(41);
        await Task.CompletedTask;
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task Corpus_case_parses_as_expected(string name)
    {
        var folder = name.Split('/')[0];
        var stem = Path.Combine(CorpusRoot, name.Replace('/', Path.DirectorySeparatorChar));
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(stem + ".json"))!.AsObject();
        var input = await File.ReadAllTextAsync(stem + (folder == "legacy" ? ".txt" : ".abf"));

        switch (folder)
        {
            case "valid":
                AssertDocument(Parse(name, input, $"{expected["type"]}/{expected["version"]}", AbfMode.Strict), expected);
                break;
            case "tolerant":
                AssertDocument(Parse(name, input, $"{expected["type"]}/{expected["version"]}", AbfMode.Tolerant), expected);
                break;
            case "invalid":
                AssertErrors(Parse(name, input, expected["expect"]!.GetValue<string>(), AbfMode.Strict), expected["errors"]!.AsArray());
                break;
            case "legacy":
                AssertLegacy(input, expected);
                break;
        }
    }

    private static AbfDocument Parse(string name, string input, string expect, AbfMode mode)
    {
        var options = AbfParseOptions.Expecting(expect, mode);
        if (options.Expect!.Value.Type == "decision-response")
        {
            // README: valid/005 and tolerant/010 answer the request in valid/004.
            var request = AbfParser.ParseDecisionRequest(File.ReadAllText(Path.Combine(CorpusRoot, "valid", "004-decision-request.abf")));
            request.HasErrors.ShouldBeFalse(name);
            options = options with { Request = request.Value };
        }

        return AbfParser.Parse(input, options);
    }

    private static void AssertDocument(AbfDocument document, JsonObject expected)
    {
        var actual = Reparse(AbfCanonicalJson.ToJson(document));
        foreach (var member in new[] { "abf", "type", "version", "records" })
            AssertJsonEqual(actual[member], expected[member], member);
        AssertDiagnostics(actual["diagnostics"]!.AsArray(), expected["diagnostics"]!.AsArray(), compareSeverity: true);
    }

    private static void AssertErrors(AbfDocument document, JsonArray expected)
    {
        var errors = AbfCanonicalJson.Diagnostics(document.Diagnostics.Where(d => d.Severity == AbfSeverity.Error));
        AssertDiagnostics(Reparse(errors).AsArray(), expected, compareSeverity: false);
    }

    /// <summary>README: compared on line, code and severity, plus name when the expected entry has one.</summary>
    private static void AssertDiagnostics(JsonArray actual, JsonArray expected, bool compareSeverity)
    {
        static string Key(JsonNode d, bool withName, bool withSeverity) =>
            $"{d["line"]}:{d["code"]}" + (withSeverity ? $":{d["severity"]}" : "") + (withName ? $":{d["name"]}" : "");

        var actualKeys = actual.Select((d, i) =>
            Key(d!, i < expected.Count && expected[i]!["name"] is not null, compareSeverity)).ToList();
        var expectedKeys = expected.Select(d => Key(d!, d!["name"] is not null, compareSeverity)).ToList();
        actualKeys.ShouldBe(expectedKeys, $"actual diagnostics: {actual.ToJsonString()}");
    }

    private static void AssertLegacy(string input, JsonObject expected)
    {
        var document = AbfLegacy.Read(input);
        AssertJsonEqual(Reparse(document.ToJson()), expected["legacy"], "legacy");
        AssertJsonEqual(Reparse(document.ValuesJson()), expected["values"], "values");

        var current = (expected["currentParsers"] ?? expected["values"])!;
        var handoff = PipelineHandoff.TryParse(input);
        var evidence = ReviewEvidence.TryParse(input);
        var currentJson = new JsonObject
        {
            ["next-stage"] = new JsonObject
            {
                ["found"] = handoff.Found,
                ["kind"] = handoff.Kind is { } kind ? PipelineHandoff.Token(kind) : null,
                ["rawToken"] = handoff.RawToken,
                ["handoff"] = handoff.Handoff,
                ["artifactPath"] = handoff.ArtifactPath,
            },
            ["review-evidence"] = new JsonObject
            {
                ["found"] = evidence.Found,
                ["usable"] = evidence.Usable,
                ["subjectTaskId"] = evidence.SubjectTaskId?.ToString("D"),
                ["reviewedSourceSha"] = evidence.ReviewedSourceSha,
                ["warning"] = evidence.Warning,
                ["scope"] = evidence.Scope.ToString(),
                ["reviewedSourceClean"] = evidence.ReviewedSourceClean,
            },
        };
        AssertJsonEqual(Reparse(currentJson), current, "currentParsers");
    }

    private static JsonNode Reparse(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    private static void AssertJsonEqual(JsonNode? actual, JsonNode? expected, string member)
    {
        if (!JsonNode.DeepEquals(actual, expected))
            throw new ShouldAssertException($"{member} differs.\nexpected: {expected?.ToJsonString()}\nactual:   {actual?.ToJsonString()}");
    }

    private static string FindCorpus()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "specs", "block-format", "conformance");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        throw new DirectoryNotFoundException("docs/specs/block-format/conformance not found above the test output.");
    }
}
