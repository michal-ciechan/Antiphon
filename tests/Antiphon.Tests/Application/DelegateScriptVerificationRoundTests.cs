using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-2 / G-10. The delegate command (<c>Antiphon.Nightly.DelegateCli</c>, which replaced
/// <c>scripts/delegate.ps1</c> in unit 28) in process against loopback stubs, and the card command (<c>Antiphon.Nightly.CardCli</c>, which replaced <c>scripts/card.ps1</c> in unit 27
/// of the 2026-11-22 plan) in process against a stub transport: the exact camelCase wire body, full
/// GUIDs, the selection file's JSON object (never its path), local refusal before any POST, and server
/// refusals surviving to a nonzero exit. The stubs prove the CLI contract, not server admission.
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptVerificationRoundTests
{
    private static readonly Guid Subject = Guid.Parse("c5440000-1111-2222-3333-4444444444aa");
    private static readonly Guid Baseline = Guid.Parse("c5440000-1111-2222-3333-4444444444bb");
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task C544_RequestRoundTrip()
    {
        using var temp = new TempDir();
        var selectionFile = Path.Combine(temp.Path, "c544-selection.json");
        await File.WriteAllTextAsync(selectionFile,
            $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"Round selection"}""");
        foreach (var (row, roleArgs) in new[]
                 {
                     ("code-worktree", new[] { "--role", "Code", "--worktree", "--repair-source", Subject.ToString("D") }),
                     ("review-readonly", new[] { "--role", "Review", "--read-only" }),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                ["create", .. roleArgs, "--goal", "repair round", "--verification-round", "Interim",
                    "--verification-subject", Subject.ToString("D").ToUpperInvariant(),
                    "--verification-baseline-outcome", Baseline.ToString("D"),
                    "--verification-selection-file", selectionFile]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            server.RequestCount.ShouldBe(1, row);
            var body = server.LastBody.ShouldNotBeNull(row).RootElement;
            body.GetProperty("verificationRound").GetString().ShouldBe("Interim", row);
            body.GetProperty("verificationSubjectTaskId").GetString().ShouldBe(Subject.ToString("D"), row);
            body.GetProperty("verificationBaselineOutcomeId").GetString().ShouldBe(Baseline.ToString("D"), row);
            var selection = body.GetProperty("verificationSelection");
            selection.ValueKind.ShouldBe(JsonValueKind.Object, row);
            selection.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
                .ToArray().ShouldBe(new[] { "artifactCommitSha", "artifactPath", "section" }, customMessage: row);
            selection.GetProperty("artifactPath").GetString().ShouldBe("docs/plans/c544.md", row);
            selection.GetProperty("artifactCommitSha").GetString().ShouldBe(Sha, row);
            selection.GetProperty("section").GetString().ShouldBe("Round selection", row);
            var raw = body.GetRawText();
            raw.ShouldNotContain("c544-selection.json", Case.Insensitive, row + ": the local filename never reaches the API");
            raw.ShouldNotContain("verificationSelectionFile", Case.Insensitive, row);
        }
    }

    [Test]
    public async Task C544_OmittedRound()
    {
        foreach (var (row, args, expected) in new (string, string[], string?)[]
                 {
                     ("code-omitted", ["--role", "Code", "--worktree"], null),
                     ("review-omitted", ["--role", "Review", "--read-only"], null),
                     ("code-explicit-final", ["--role", "Code", "--worktree", "--verification-round", "Final"], "Final"),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, ["create", .. args, "--goal", "full round"]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            var body = server.LastBody.ShouldNotBeNull(row).RootElement;
            if (expected is null)
                body.TryGetProperty("verificationRound", out _).ShouldBeFalse(row + ": omitted stays omitted (server resolves Final)");
            else
                body.GetProperty("verificationRound").GetString().ShouldBe(expected, row);
            foreach (var name in new[] { "verificationSubjectTaskId", "verificationBaselineOutcomeId", "verificationSelection" })
                body.TryGetProperty(name, out _).ShouldBeFalse($"{row}: {name}");
        }
    }

    [Test]
    public async Task C544_InvalidArguments()
    {
        using var temp = new TempDir();
        var good = Path.Combine(temp.Path, "good.json");
        await File.WriteAllTextAsync(good, $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"S"}""");
        var malformed = Path.Combine(temp.Path, "malformed.json");
        await File.WriteAllTextAsync(malformed, "{\"artifactPath\": \"docs/plans/c544.md\",");
        var array = Path.Combine(temp.Path, "array.json");
        await File.WriteAllTextAsync(array, "[\"docs/plans/c544.md\"]");
        string[] Interim(string subject, string baseline, string file) =>
            ["--verification-round", "Interim", "--verification-subject", subject, "--verification-baseline-outcome", baseline,
                "--verification-selection-file", file];
        var full = Subject.ToString("D");
        var rows = new (string Row, string[] Args, string Needle)[]
        {
            ("invalid-role", ["--role", "Docs", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("orchestrator", ["--role", "Code", "--orchestrator", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("mutation-final", ["--role", "Mutation", "--worktree", "--verification-round", "Final"], "verification_round_role"),
            // CARD-0644 D-5: Interim Code runs in the default Worktree; an explicit Shared is the invalid shape.
            ("code-shared", ["--role", "Code", "--shared", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("review-worktree", ["--role", "Review", "--worktree", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("short-subject", ["--role", "Review", "--read-only", .. Interim(full[..8], Baseline.ToString("D"), good)], "verification_baseline_invalid"),
            ("short-baseline", ["--role", "Review", "--read-only", .. Interim(full, Baseline.ToString("N"), good)], "verification_baseline_invalid"),
            ("malformed-json", ["--role", "Review", "--read-only", .. Interim(full, Baseline.ToString("D"), malformed)], "verification_selection_invalid"),
            ("array-json", ["--role", "Review", "--read-only", .. Interim(full, Baseline.ToString("D"), array)], "verification_selection_invalid"),
            ("missing-file", ["--role", "Review", "--read-only", .. Interim(full, Baseline.ToString("D"), Path.Combine(temp.Path, "absent.json"))], "verification_selection_invalid"),
            ("fields-without-interim", ["--role", "Review", "--read-only", "--verification-subject", full], "verification_round_role"),
            ("bad-enum", ["--role", "Review", "--read-only", "--verification-round", "Partial"], "--verification-round"),
        };
        foreach (var (row, args, needle) in rows)
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, ["create", .. args, "--goal", "invalid"]);
            run.ExitCode.ShouldNotBe(0, $"{row}: {run.Output}");
            run.Output.ShouldContain(needle, Case.Insensitive, row);
            server.RequestCount.ShouldBe(0, row + ": no POST and no server-side admission");
        }
    }

    [Test]
    public async Task C544_ServerRefusal()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "selection.json");
        await File.WriteAllTextAsync(file, $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"S"}""");
        foreach (var code in new[] { "verification_interim_disallowed", "verification_backstop_unready" })
        {
            using var server = new DelegateCreateStubApi($$"""{"code":"{{code}}","title":"Conflict","detail":"refused {{code}}"}""", 409);
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                "create", "--role", "Review", "--read-only", "--goal", "interim", "--verification-round", "Interim",
                "--verification-subject", Subject.ToString("D"), "--verification-baseline-outcome", Baseline.ToString("D"),
                "--verification-selection-file", file);
            run.ExitCode.ShouldNotBe(0, code);
            run.Output.ShouldContain(code, Case.Sensitive, code);
            server.RequestCount.ShouldBe(1, code + ": exactly one attempt, no silent Final resubmission");
            server.LastBody.ShouldNotBeNull(code).RootElement.GetProperty("verificationRound").GetString().ShouldBe("Interim", code);
        }
    }

    [Test]
    public void C544_CardPolicyPatch()
    {
        foreach (var (row, args, expectCode, expectReview) in new (string, string[], string?, string?)[]
                 {
                     ("code-only", ["--code-verification-policy", "AllowInterim"], "AllowInterim", null),
                     ("review-only", ["--review-verification-policy", "FullOnly"], null, "FullOnly"),
                     ("both", ["--code-verification-policy", "FullOnly", "--review-verification-policy", "AllowInterim"], "FullOnly", "AllowInterim"),
                 })
        {
            var stub = new CardStubApi();
            var run = RunCard(stub, ["edit", "CARD-0544", .. args, "--reason", $"C544 {row}"]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            var patch = stub.LastPatch.ShouldNotBeNull(row).RootElement;
            patch.GetProperty("concurrencyToken").GetString().ShouldBe(CardStubApi.Token, row + ": fresh token");
            patch.GetProperty("reason").GetString().ShouldBe($"C544 {row}", row);
            AssertOptional(patch, "codeVerificationPolicy", expectCode, row);
            AssertOptional(patch, "reviewVerificationPolicy", expectReview, row);
        }

        var refused = new CardStubApi();
        var bad = RunCard(refused, ["edit", "CARD-0544", "--code-verification-policy", "Sometimes", "--reason", "bad"]);
        bad.ExitCode.ShouldNotBe(0, "bad-enum");
        refused.LastPatch.ShouldBeNull("bad-enum: no PATCH");
    }

    private static void AssertOptional(JsonElement body, string name, string? expected, string row)
    {
        if (expected is null)
            body.TryGetProperty(name, out _).ShouldBeFalse($"{row}: omitted {name} preserves the stored value");
        else
            body.GetProperty(name).GetString().ShouldBe(expected, $"{row}: {name}");
    }

    private static (int ExitCode, string Output) RunCard(CardStubApi api, string[] args)
    {
        var run = Antiphon.Tests.Scripts.CardCommandRun.Run(api.Transport, args, taskToken: "");
        return (run.Exit, run.Output + run.Error);
    }

    /// <summary>The card API stand-in: GET limits, GET card (token), PATCH content. Never the production board.</summary>
    private sealed class CardStubApi
    {
        public const string Token = "c5440000-aaaa-bbbb-cccc-000000000001";
        private const string CardId = "c5440000-aaaa-bbbb-cccc-0000000000cc";

        public JsonDocument? LastPatch { get; private set; }

        private static string CardJson => $$"""
            {"id":"{{CardId}}","boardId":"c5440000-aaaa-bbbb-cccc-0000000000bb","identifier":"CARD-0544","title":"fixture",
             "status":"Backlog","importance":"Normal","urgency":"Normal","rank":1,"importanceProvenance":"Auto","labels":[],
             "concurrencyToken":"{{Token}}","revisionCount":3,"codeVerificationPolicy":"FullOnly","reviewVerificationPolicy":"FullOnly"}
            """;

        public Antiphon.Nightly.NightlyReportHttp Transport => (method, uri, _, body) =>
        {
            var path = new Uri(uri).AbsolutePath;
            if (method == "GET" && path == "/api/cards/limits")
                return new(200, """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxPrivateNotesLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":6,"importanceValues":[],"urgencyValues":[]}""");
            if (method == "GET" && path.StartsWith("/api/cards/", StringComparison.Ordinal))
                return new(200, CardJson);
            if (method == "PATCH" && path == $"/api/cards/{CardId}/content")
            {
                LastPatch = JsonDocument.Parse(body!);
                return new(200, CardJson);
            }
            return new(404, """{"detail":"not stubbed"}""");
        };
    }
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c544-cli").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
