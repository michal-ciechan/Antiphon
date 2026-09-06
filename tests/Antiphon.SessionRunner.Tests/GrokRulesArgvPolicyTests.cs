using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0382 V-4: payload and argv policy boundaries. Validate-only — never transforms.</summary>
[Category("Unit")]
public sealed class GrokRulesArgvPolicyTests
{
    private const string Sentinel = "card0382-sentinel-policy";

    [Test]
    public void Cr_only_is_line_break()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload("keep\rit", isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Lf_only_is_line_break()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload("keep\nit", isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Crlf_is_line_break()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload("keep\r\nit", isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Nul_is_nul()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload("keep\0it", isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonNul, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Lf_plus_nul_reports_nul()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload("a\nb\0c", isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonNul, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Lf_plus_oversized_reports_line_break()
    {
        var v = GrokRulesArgvPolicy.ValidatePayload(
            "a\n" + new string('x', 4097), isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Four_thousand_ninety_seven_bmp_chars_is_token_too_long()
    {
        var payload = new string('x', 4097);
        var v = GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonTokenTooLong, GrokRulesArgvPolicy.RulesFlag);
        v!.Length.ShouldBe(4097);
        v.Limit.ShouldBe(4096);
        var formatted = GrokRulesArgvPolicy.Format(v);
        formatted.ShouldContain("4097");
        formatted.ShouldContain("4096");
        formatted.ShouldNotContain(payload);
    }

    [Test]
    public void Two_thousand_forty_eight_supplementary_plus_one_bmp_is_four_thousand_ninety_seven_utf16_units()
    {
        var payload = string.Concat(Enumerable.Repeat("\U0001F600", 2048)) + "x";
        payload.Length.ShouldBe(4097);
        var v = GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonTokenTooLong, GrokRulesArgvPolicy.RulesFlag);
        v!.Length.ShouldBe(4097);
    }

    [Test]
    public void Trailing_rules_flag_with_no_value_is_missing_value()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--always-approve", "--rules"], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonMissingValue, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Duplicate_good_then_bad_names_occurrence_two()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--rules", "good", "--rules", "bad\n" + Sentinel], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
        v!.OccurrenceIndex.ShouldBe(2);
        GrokRulesArgvPolicy.Format(v).ShouldContain("occurrence 2");
        GrokRulesArgvPolicy.Format(v).ShouldNotContain(Sentinel);
    }

    [Test]
    public void Duplicate_bad_then_good_names_occurrence_one()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--rules", "bad\n" + Sentinel, "--rules", "good"], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
        v!.OccurrenceIndex.ShouldBe(1);
        GrokRulesArgvPolicy.Format(v).ShouldNotContain(Sentinel);
    }

    [Test]
    public void Rules_equals_form_with_lf_is_refused()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--rules=a\nb"], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.RulesFlag);
    }

    [Test]
    public void Append_system_prompt_space_form_with_lf_is_refused()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--append-system-prompt", "a\nb"], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.AppendSystemPromptFlag);
    }

    [Test]
    public void Append_system_prompt_equals_form_with_lf_is_refused()
    {
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--append-system-prompt=a\nb"], isWindows: true, isGrok: true);
        AssertRefused(v, GrokRulesArgvPolicy.ReasonLineBreak, GrokRulesArgvPolicy.AppendSystemPromptFlag);
    }

    [Test]
    public void Single_line_with_spaces_quotes_backticks_dollar_and_non_ascii_is_allowed()
    {
        var payload = "keep it terse; quotes \"dq\" and `ticks` and $5 and café " + Sentinel;
        GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules", payload], isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Spaces_only_is_allowed()
    {
        GrokRulesArgvPolicy.ValidatePayload("   ", isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules", "   "], isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Empty_string_is_allowed()
    {
        GrokRulesArgvPolicy.ValidatePayload("", isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules="], isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Exactly_four_thousand_ninety_six_bmp_chars_is_allowed()
    {
        var payload = new string('x', 4096);
        GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Exactly_two_thousand_forty_eight_supplementary_characters_are_four_thousand_ninety_six_units_and_allowed()
    {
        var payload = string.Concat(Enumerable.Repeat("\U0001F600", 2048));
        payload.Length.ShouldBe(4096);
        GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Any_payload_is_allowed_when_not_windows()
    {
        GrokRulesArgvPolicy.ValidatePayload("a\nb\0" + new string('x', 5000), isWindows: false, isGrok: true)
            .ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules", "a\nb"], isWindows: false, isGrok: true)
            .ShouldBeNull();
    }

    [Test]
    public void Any_payload_is_allowed_when_not_grok()
    {
        GrokRulesArgvPolicy.ValidatePayload("a\nb", isWindows: true, isGrok: false).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules", "a\nb"], isWindows: true, isGrok: false)
            .ShouldBeNull();
    }

    [Test]
    public void Rules_after_a_bare_option_terminator_is_not_scanned()
    {
        GrokRulesArgvPolicy.ValidateArgv(
            ["--", "--rules", "a\nb" + Sentinel], isWindows: true, isGrok: true)
            .ShouldBeNull();
    }

    [Test]
    public void Path_shaped_single_line_values_pass_untouched_for_card_0395()
    {
        var id = Guid.NewGuid().ToString("N");
        var path = $@"C:\Antiphon\logs\instructions\grok\{id}\rules.md";
        GrokRulesArgvPolicy.ValidateArgv(["--rules", path], isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules", "@" + path], isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidateArgv(["--rules=" + path], isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidatePayload(path, isWindows: true, isGrok: true).ShouldBeNull();
    }

    [Test]
    public void Diagnostics_never_contain_the_payload_an_env_value_or_override()
    {
        var envValue = "secret-env-value-" + Sentinel;
        var v = GrokRulesArgvPolicy.ValidateArgv(
            ["--rules", "line\n" + Sentinel],
            isWindows: true,
            isGrok: true,
            envTokenNames: [null, "RULES"]);
        v.ShouldNotBeNull();
        var formatted = GrokRulesArgvPolicy.Format(v!);
        formatted.ShouldContain(GrokRulesArgvPolicy.RulesFlag);
        formatted.ShouldContain(GrokRulesArgvPolicy.ReasonLineBreak);
        formatted.ShouldContain(GrokRulesArgvPolicy.ProblemCode);
        formatted.ShouldContain("RULES");
        formatted.ShouldNotContain(Sentinel);
        formatted.ShouldNotContain(envValue);
        formatted.ShouldNotContain("override");
    }

    [Test]
    public void Validate_methods_return_the_same_payload_they_were_given_they_do_not_transform()
    {
        // The APIs return a violation or null — there is no rewritten value to compare. Pin that
        // a legal payload stays legal (byte-identical pass) and an illegal one is refused rather
        // than normalized into a pass.
        var legal = "keep it terse " + Sentinel;
        GrokRulesArgvPolicy.ValidatePayload(legal, isWindows: true, isGrok: true).ShouldBeNull();
        GrokRulesArgvPolicy.ValidatePayload(legal.Replace(' ', '\n'), isWindows: true, isGrok: true)
            .ShouldNotBeNull();
    }

    [Test]
    public void Next_element_after_a_flag_is_the_value_even_when_it_looks_like_a_flag()
    {
        GrokRulesArgvPolicy.ValidateArgv(
            ["--rules", "--always-approve"], isWindows: true, isGrok: true)
            .ShouldBeNull();
    }

    private static void AssertRefused(GrokRulesArgvViolation? v, string reason, string flag)
    {
        v.ShouldNotBeNull();
        v!.Reason.ShouldBe(reason);
        v.Flag.ShouldBe(flag);
        var formatted = GrokRulesArgvPolicy.Format(v);
        formatted.ShouldContain(flag);
        formatted.ShouldContain(reason);
        formatted.ShouldContain(GrokRulesArgvPolicy.ProblemCode);
        formatted.ShouldNotContain(Sentinel);
        formatted.ShouldNotContain("override");
    }
}
