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
