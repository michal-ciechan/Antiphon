using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0330 S3 / CARD-0146: the distiller's deterministic gates. Pure; no database.
/// </summary>
[Category("Unit")]
public class OutputDistillationGateTests
{
    private const string Sha = "a1b2c3d4e5f6789";
    private const string Card = "CARD-0330";
    private const string Url = "https://example.com/x";
    private const string Amount = "$12.50";
    private const string Count = "3 failed";
    private const string Path = "server/Application/Services/Foo.cs:40";
    private const string AttachPath = "docs/tmp/f.md";

    [Test]
    public void empty_distilled_is_degraded_empty()
    {
        var result = OutputDistillationGate.Evaluate(LongRaw(), "");
        result.Verdict.ShouldBe(DistillationGateVerdict.DegradedEmpty);
    }

    [Test]
    public void distilled_shorter_than_120_is_degraded_empty()
    {
        var result = OutputDistillationGate.Evaluate(LongRaw(), new string('x', 119));
        result.Verdict.ShouldBe(DistillationGateVerdict.DegradedEmpty);
    }

    [Test]
    public void distilled_over_the_length_band_is_under_compressed()
    {
        var raw = LongRaw(2_000);
        var max = Math.Min(1_500, (int)Math.Floor(0.6 * raw.Length));
        var result = OutputDistillationGate.Evaluate(raw, Pad(max + 1, KeepAll()));
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedUnderCompressed);
    }

    [Test]
    public void a_distillation_that_keeps_every_anchor_class_passes()
    {
        var raw = LongRaw();
        var result = OutputDistillationGate.Evaluate(raw, Pad(200, KeepAll()));
        result.Passed.ShouldBeTrue(string.Join(",", result.MissingAnchors));
        result.Verdict.ShouldBe(DistillationGateVerdict.Pass);
    }

    [Test]
    [Arguments("sha:" + Sha, Sha)]
    [Arguments("card:" + Card, Card)]
    [Arguments("url:" + Url, Url)]
    [Arguments("attach:[[attach:", "[[attach:")]
    [Arguments("amount:" + Amount, Amount)]
    [Arguments("count:" + Count, Count)]
    [Arguments("path:" + Path, Path)]
    public void dropping_a_required_anchor_is_over_compressed(string missingPrefix, string drop)
    {
        var raw = LongRaw();
        var kept = KeepAll().Replace(drop, "omitted", StringComparison.Ordinal);
        var result = OutputDistillationGate.Evaluate(raw, Pad(200, kept));
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldContain(a => a.StartsWith(missingPrefix, StringComparison.Ordinal));
    }

    [Test]
    public void ten_or_fewer_paths_must_all_survive()
    {
        const string dropped = "zzz/unique-drop.cs";
        var kept = Enumerable.Range(1, 7).Select(i => $"zzz/keep{i}.cs").ToList();
        var raw = LongRaw(body: string.Join(" ", kept) + " " + dropped);
        var distilled = Pad(200, KeepAll() + " " + string.Join(" ", kept));
        var result = OutputDistillationGate.Evaluate(raw, distilled);
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed, string.Join(",", result.MissingAnchors));
        result.MissingAnchors.ShouldContain("path:" + dropped);
    }

    [Test]
    public void more_than_ten_paths_need_sixty_percent()
    {
        var paths = Enumerable.Range(1, 20).Select(i => $"server/file{i:00}.cs").ToList();
        var raw = LongRaw(body: string.Join(" ", paths));
        var keep12 = string.Join(" ", paths.Take(12));
        var result = OutputDistillationGate.Evaluate(raw, Pad(400, KeepAll() + " " + keep12));
        result.Passed.ShouldBeTrue(string.Join(",", result.MissingAnchors));
    }

    [Test]
    public void more_than_ten_paths_below_sixty_percent_is_over_compressed()
    {
        var paths = Enumerable.Range(1, 20).Select(i => $"server/file{i:00}.cs").ToList();
        var raw = LongRaw(body: string.Join(" ", paths));
        var keep5 = string.Join(" ", paths.Take(5));
        var result = OutputDistillationGate.Evaluate(raw, Pad(400, KeepAll() + " " + keep5));
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldContain(a => a.StartsWith("path:", StringComparison.Ordinal));
    }

    [Test]
    public void dropping_next_or_handoff_from_a_present_block_is_over_compressed()
    {
        var raw = LongRaw(body: """
            --- next stage ---
            next: review
            handoff: the gate must keep this sentence
            artifact: docs/superpowers/plans/2026-09-03-card-0330-output-distiller-plan.md
            """);
        var distilled = Pad(200, KeepAll() + " next: review");
        var result = OutputDistillationGate.Evaluate(raw, distilled);
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldContain("handoff:");
    }

    [Test]
    public void copying_next_and_handoff_passes_even_if_artifact_is_dropped()
    {
        var raw = LongRaw(body: """
            --- next stage ---
            next: review
            handoff: the gate must keep this sentence
            artifact: docs/superpowers/plans/2026-09-03-card-0330-output-distiller-plan.md
            """);
        var distilled = Pad(250, KeepAll()
            + "\n- next: review\n- handoff: the gate must keep this sentence");
        var result = OutputDistillationGate.Evaluate(raw, distilled);
        result.Passed.ShouldBeTrue(string.Join(",", result.MissingAnchors));
    }

    [Test]
    public void a_report_without_a_handoff_block_does_not_require_next()
    {
        var raw = LongRaw();
        var result = OutputDistillationGate.Evaluate(raw, Pad(200, KeepAll()));
        result.Passed.ShouldBeTrue();
        result.MissingAnchors.ShouldNotContain(a => a.StartsWith("next:", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("The HTTP/native transports now share the same behavior.")]
    [Arguments("Checked SE/dark and the Save/Cancel/Remove controls.")]
    [Arguments("The input/output/error streams are wired.")]
    [Arguments("The client/server/db layers and read/write/execute bits are configured.")]
    [Arguments("Task f1590e8a finished. [antiphon-report:f1590e8a done]")]
    public void weekly_review_false_positives_can_be_paraphrased(string report)
    {
        var result = OutputDistillationGate.Evaluate(
            LongRaw(body: report), Pad(200, KeepAll()));

        result.Verdict.ShouldBe(DistillationGateVerdict.Pass);
        result.MissingAnchors.ShouldBeEmpty();
    }

    [Test]
    [Arguments("Incidental value f1590e8a was logged.")]
    [Arguments("Task f1590e8a-1234-5678-90ab-123456789abc finished.")]
    [Arguments("The identifier prefixf1590e8asuffix is incidental.")]
    [Arguments("Commit discussion ended.\nIncidental value f1590e8a was logged.")]
    [Arguments("Commit " + "........................................." + "f1590e8a")]
    [Arguments("Commit task f1590e8a-1234-5678-90ab-123456789abc finished.")]
    [Arguments("Incidental `abcdef` and `abcdef0123456` values were logged.")]
    public void incidental_hex_is_not_a_commit_anchor(string report)
    {
        var result = OutputDistillationGate.Evaluate(
            LongRaw(body: report), Pad(200, KeepAll()));

        result.Verdict.ShouldBe(DistillationGateVerdict.Pass);
        result.MissingAnchors.ShouldBeEmpty();
    }

    [Test]
    [Arguments("Commit f1590e8a", "f1590e8a")]
    [Arguments("SHA: `f1590e8a`", "f1590e8a")]
    [Arguments("revision F1590E8", "F1590E8")]
    [Arguments("HEAD=f1590e8a", "f1590e8a")]
    [Arguments("Pushed f1590e8a", "f1590e8a")]
    [Arguments("Merged commit f1590e8a", "f1590e8a")]
    [Arguments("git show f1590e8a", "f1590e8a")]
    [Arguments("Commit f1590e8a..b1234567", "b1234567")]
    [Arguments("0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
    [Arguments("Task f1590e8a finished; commit f1590e8a was pushed.", "f1590e8a")]
    public void dropping_a_git_reference_still_fails(string report, string sha)
    {
        var result = OutputDistillationGate.Evaluate(
            LongRaw(body: report), Pad(200, KeepAll()));

        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldContain("sha:" + sha);
    }

    // Verbatim probes from review task 72ca7d63. Keep their intervening prose intact.
    [Test]
    [Arguments("Pushed to origin/master at 2f7acb48.")]
    [Arguments("Reverted to 2f7acb48.")]
    [Arguments("Cherry-picked 2f7acb48 onto master.")]
    [Arguments("Rebased onto 2f7acb48.")]
    [Arguments("The commit hash is 2f7acb48.")]
    [Arguments("Fixed in 2f7acb48.")]
    [Arguments("origin/master is now 2f7acb48.")]
    public void review_citation_probes_require_the_sha(string report)
    {
        AssertCitationRequiresSha(report, "2f7acb48");
    }

    // Corpus excerpts, not strings constructed to fit the regex:
    // docs/orchestration-findings.md; docs/features/004-agent-screen-working-meaning/implementation-spec.md;
    // docs/superpowers/specs/2026-08-11-card-0019-card-correction.md;
    // docs/investigations/2026-09-07-card-0420-code-verification.md;
    // docs/investigations/2026-09-06-card-0388-code-verification.md.
    [Test]
    [Arguments("shipped and closed.** `4bb65fb`", "4bb65fb")]
    [Arguments("The investigation's recommended **Option A landed in commit `1ce1084`**", "1ce1084")]
    [Arguments("investigation; fix landed in f078dd2", "f078dd2")]
    [Arguments("its single documentation change was cherry-picked onto this checkout as 928f8b2f before implementation.", "928f8b2f")]
    [Arguments("Code commits: `572c25f7`, `6d468ba8`, `3c8398a1`; subsequent verification-only updates are in this branch history.", "3c8398a1")]
    [Arguments("R1 landed in `89f1262`, R2 in `e1a46d1` (server)", "e1a46d1")]
    public void corpus_citations_require_the_sha_even_when_prose_is_preserved(string report, string sha)
    {
        AssertCitationRequiresSha(report, sha);
    }

    [Test]
    [Arguments("Commits 2f7acb48 and bf642f16 both landed.", "bf642f16")]
    [Arguments("Commits 2f7acb48, bf642f16, and 131872f2 both landed.", "131872f2")]
    [Arguments("Commits `2f7acb48`, `bf642f16` and `131872f2` landed.", "bf642f16")]
    [Arguments("git diff 2f7acb48...bf642f16", "bf642f16")]
    [Arguments("See `2f7acb48`..`bf642f16`.", "bf642f16")]
    public void dropping_only_one_sha_from_a_list_or_range_fails(string report, string sha)
    {
        AssertCitationRequiresSha(report, sha);
    }

    private static void AssertCitationRequiresSha(string report, string sha)
    {
        var raw = LongRaw(body: report);
        var preserved = OutputDistillationGate.Evaluate(raw, Pad(200, KeepAll() + " " + report));
        preserved.Verdict.ShouldBe(DistillationGateVerdict.Pass);

        var omitted = report.Replace(sha, "omitted", StringComparison.Ordinal);
        var result = OutputDistillationGate.Evaluate(raw, Pad(200, KeepAll() + " " + omitted));
        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldBe(new[] { "sha:" + sha });
    }

    [Test]
    [Arguments("/var/log/antiphon")]
    [Arguments(@"\logs\antiphon")]
    [Arguments(@"\\host\share\report")]
    [Arguments(@"C:\src\Antiphon\README.md")]
    [Arguments("./scripts")]
    [Arguments("../scripts")]
    [Arguments("docs/report.md")]
    [Arguments(@"docs\report.md")]
    [Arguments("server/Feature/Handlers")]
    [Arguments("Server/Feature/Handlers")]
    [Arguments("Application/Services/Foo")]
    [Arguments("docs/cards")]
    [Arguments("client/src")]
    [Arguments("server/Application/Services/")]
    [Arguments(@"Server\Application\Services\")]
    [Arguments("Custom/Feature/Handlers/")]
    [Arguments("custom/feature/handlers:42")]
    [Arguments("src/feature_name/output")]
    [Arguments("`docs/report.md`")]
    public void dropping_a_real_path_still_fails(string path)
    {
        // Isolate the path: KeepAll's Foo.cs path contains directory prefixes under test.
        var result = OutputDistillationGate.Evaluate(
            Pad(2_000, "Changed " + path), Pad(200, "The implementation is complete."));

        result.Verdict.ShouldBe(DistillationGateVerdict.RejectedOverCompressed);
        result.MissingAnchors.ShouldBe(new[] { "path:" + path.Trim('`') });
    }

    private static string KeepAll() =>
        $"- done {Card} sha {Sha} {Url} {Amount} {Count} {Path} [[attach: {AttachPath}]] {AttachPath}";

    private static string LongRaw(int minChars = 2_000, string? body = null)
    {
        var core = $"""
            Landed {Card} at {Sha}. See {Url}. Cost {Amount}. {Count}.
            Path {Path}. [[attach: {AttachPath}]] {AttachPath}
            {body}
            """;
        if (core.Length >= minChars)
            return core;
        return core + "\n" + new string('y', minChars - core.Length);
    }

    private static string Pad(int minChars, string text)
    {
        if (text.Length >= minChars)
            return text;
        return text + "\n- " + new string('x', minChars - text.Length);
    }
}
