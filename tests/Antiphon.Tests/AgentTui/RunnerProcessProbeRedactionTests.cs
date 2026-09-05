using System.Diagnostics;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

/// <summary>
/// CARD-0110 S5: redaction-shape cases drive <see cref="RunnerProcessProbe.Sanitize"/> directly.
/// Spawn-and-echo coverage stays on <see cref="RunnerProcessProbeTests"/> (a handful of e2e cases).
/// </summary>
[Category("Unit")]
public sealed class RunnerProcessProbeRedactionTests
{
    [Test]
    [Arguments(
        "clientApiKey=synthetic-credential-first status=ready note='ordinary value !@#$%^&*()'",
        "* status=ready note='ordinary value !@#$%^&*()'",
        false)]
    [Arguments(
        "status=ready,clientAuthToken=synthetic-credential-middle,count=2",
        "status=ready,*,count=2",
        false)]
    [Arguments(
        "status=ready message='ordinary value: punctuation !?' clientPassword=synthetic-credential-last",
        "status=ready message='ordinary value: punctuation !?' *",
        true)]
    [Arguments(
        """{"clientSecret":"synthetic-credential-json-first","status":"ok"}""",
        """{*,"status":"ok"}""",
        false)]
    [Arguments(
        """{"status":"ok","clientSecret":"synthetic-credential-quote-\"-slash-\\-end","note":"ordinary value, with punctuation!"}""",
        """{"status":"ok",*,"note":"ordinary value, with punctuation!"}""",
        false)]
    [Arguments(
        """{"status":"ok","clientAuthorization":"synthetic-credential-json-last"}""",
        """{"status":"ok",*}""",
        true)]
    [Arguments(
        "status=ok clientApiKey='synthetic-credential-one with spaces' clientPassword=\"synthetic-credential-two,with punctuation!\" tail='ordinary value'",
        "status=ok * * tail='ordinary value'",
        false)]
    [Arguments(
        "message=ordinary value with punctuation! clientRefreshToken=synthetic-credential-refresh status=ok",
        "message=ordinary value with punctuation! * status=ok",
        false)]
    [Arguments(
        "status=ok clientSecretHint='ordinary secret hint' authTokenType=ordinary servicePasswordPolicy=standard apiKeyLabel=public clientPrivateKey=synthetic-credential-private",
        "status=ok clientSecretHint='ordinary secret hint' authTokenType=ordinary servicePasswordPolicy=standard apiKeyLabel=public *",
        false)]
    [Arguments(
        """{"clientApiKey":"synthetic-credential-json-one","status":"ok","clientPassword":"synthetic-credential-json-two"}""",
        """{*,"status":"ok",*}""",
        false)]
    [Arguments(
        "Password=synthetic-credential-semicolon-first;Server=db;Encrypt=true",
        "*;Server=db;Encrypt=true",
        false)]
    [Arguments(
        "Server=db;servicePasswordPolicy=standard;Password=synthetic-credential-semicolon-middle;Encrypt=true",
        "Server=db;servicePasswordPolicy=standard;*;Encrypt=true",
        false)]
    [Arguments(
        "Server=db;User=app;clientSecret=synthetic-credential-semicolon-last",
        "Server=db;User=app;*",
        true)]
    [Arguments(
        "Server=db;Password=synthetic-credential-semicolon-one;clientApiKey=synthetic-credential-semicolon-two;Mode=read",
        "Server=db;*;*;Mode=read",
        false)]
    [Arguments(
        "status=ok&clientAuthToken=synthetic-credential-query&mode=read",
        "status=ok&*&mode=read",
        false)]
    [Arguments(
        "status=ok|clientPrivateKey=synthetic-credential-log|mode=read",
        "status=ok|*|mode=read",
        true)]
    [Arguments(
        "status=ok;clientPassword=\"synthetic-credential-quoted;&|value\";mode=read",
        "status=ok;*;mode=read",
        false)]
    [Arguments(
        "note='clientPassword=ordinary;clientApiKey=ordinary'|clientSecret=synthetic-credential-embedded|status=ok",
        "note='clientPassword=ordinary;clientApiKey=ordinary'|*|status=ok",
        false)]
    [Arguments(
        "Password=synthetic-credential-correct synthetic-credential-horse synthetic-credential-battery!",
        "*",
        false)]
    [Arguments(
        "Password=\"synthetic-credential-unterminated synthetic-credential-tail",
        "*",
        false)]
    [Arguments(
        "clientSecret='synthetic-credential-unterminated synthetic-credential-tail",
        "*",
        true)]
    [Arguments(
        "Password=\"synthetic-credential-top-secret status=synthetic-credential-still-secret tail",
        "*",
        false)]
    [Arguments(
        "clientSecret='synthetic-credential-top-secret mode=synthetic-credential-still-secret tail",
        "*",
        true)]
    [Arguments(
        "Password=\"synthetic-credential-top-secret clientSecret=synthetic-credential-still-secret tail",
        "*",
        false)]
    [Arguments(
        "clientSecret=synthetic-credential-alpha\tsynthetic-credential-beta\t synthetic-credential-gamma?",
        "*",
        true)]
    [Arguments(
        "Password=synthetic-credential-first synthetic-credential-value status=ok mode=read",
        "* status=ok mode=read",
        false)]
    [Arguments(
        "status=ok Password=synthetic-credential-middle synthetic-credential-value mode=read",
        "status=ok * mode=read",
        true)]
    [Arguments(
        "status=ok mode=read clientSecret=synthetic-credential-last synthetic-credential-value!",
        "status=ok mode=read *",
        false)]
    [Arguments(
        "status=ok Password=synthetic-credential-one synthetic-credential-two clientApiKey=synthetic-credential-three synthetic-credential-four mode=read",
        "status=ok * * mode=read",
        false)]
    [Arguments(
        "Password=synthetic-credential-before synthetic-credential-semicolon;Server=db",
        "*;Server=db",
        false)]
    [Arguments(
        "Password=synthetic-credential-line-one synthetic-credential-suffix\nstatus=ok clientSecret=synthetic-credential-line-two synthetic-credential-suffix\nnote=done",
        "*\nstatus=ok *\nnote=done",
        true)]
    public void Redacts_each_credential_assignment_without_consuming_neighbors(
        string output, string expected, bool writeToStandardError)
    {
        AssertRedacted(output, expected, writeToStandardError);
    }

    [Test]
    [Arguments("ApiKey")]
    [Arguments("ApiToken")]
    [Arguments("AccessToken")]
    [Arguments("RefreshToken")]
    [Arguments("SecretAccessKey")]
    [Arguments("PrivateKey")]
    [Arguments("DatabaseUrl")]
    [Arguments("ConnectionString")]
    [Arguments("AuthToken")]
    [Arguments("Authorization")]
    [Arguments("Password")]
    [Arguments("Secret")]
    public void Redacts_camel_case_credential_suffixes_in_plain_and_json_assignments(string suffix)
    {
        const string plainValue = "synthetic-camel-plain-value";
        const string jsonValue = "synthetic-camel-json-value";
        var credentialName = $"client{suffix}";
        var output = $"{credentialName}={plainValue}\n{{\"{credentialName}\":\"{jsonValue}\"}}";
        var result = RunnerProcessProbe.Sanitize(output, "", []);
        result.SensitiveOutputDetected.ShouldBeTrue();
        result.StandardOutput.ShouldNotContain(credentialName);
        result.StandardOutput.ShouldNotContain(plainValue);
        result.StandardOutput.ShouldNotContain(jsonValue);
    }

    [Test]
    [Arguments("CLIENT_API_KEY")]
    [Arguments("client-api-token")]
    public void Preserves_snake_and_dash_credential_redaction(string credentialName)
    {
        const string plainValue = "synthetic-delimited-plain-value";
        const string jsonValue = "synthetic-delimited-json-value";
        var output = $"{credentialName}={plainValue}\n{{\"{credentialName}\":\"{jsonValue}\"}}";
        var result = RunnerProcessProbe.Sanitize(output, "", []);
        result.SensitiveOutputDetected.ShouldBeTrue();
        result.StandardOutput.ShouldNotContain(credentialName);
        result.StandardOutput.ShouldNotContain(plainValue);
        result.StandardOutput.ShouldNotContain(jsonValue);
    }

    [Test]
    [Arguments("clientSecretHint")]
    [Arguments("authTokenType")]
    [Arguments("servicePasswordPolicy")]
    [Arguments("apiKeyLabel")]
    [Arguments("notasecret")]
    public void Leaves_non_credential_near_misses_unchanged(string name)
    {
        var output = $"{name}=synthetic-near-miss-plain\n{{\"{name}\":\"synthetic-near-miss-json\"}}";
        var result = RunnerProcessProbe.Sanitize(output, "", []);
        result.SensitiveOutputDetected.ShouldBeFalse();
        result.StandardOutput.ShouldBe(output);
        result.StandardError.ShouldBeEmpty();
    }

    [Test]
    [Arguments("clientSecret=unknown-secret", "*", false, "=", "", "")]
    [Arguments("prefix abcdef suffix", "prefix * suffix", false, "abc", "abcdef", "")]
    [Arguments("prefix abcdef suffix", "prefix * suffix", true, "abcdef", "abc", "")]
    [Arguments("abcabcdef", "*", false, "bcde", "abc", "abcdef")]
    [Arguments("aaaaa", "*", true, "aaa", "", "")]
    [Arguments("Password=\"alpha\nbeta\"", "*\"", false, "alpha\nbeta", "", "")]
    [Arguments("clientSecret=alpha\r\nbeta status=ok", "* status=ok", true, "alpha\r\nbeta", "", "")]
    [Arguments("Bearer alpha\nbeta", "*", false, "alpha\nbeta", "", "")]
    [Arguments("prefix alpha\nbeta\ngamma suffix", "prefix * suffix", false, "alpha\nbeta", "beta\ngamma", "")]
    [Arguments("prefix alpha\nbetaalpha\nbeta suffix", "prefix * suffix", true, "alpha\nbeta", "", "")]
    [Arguments("clientSecret=alpha=beta;status=ok&gamma", "*", true, "alpha=beta;status=ok&gamma", "", "")]
    public void Redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks(
        string output,
        string expected,
        bool writeToStandardError,
        string firstSecret,
        string secondSecret,
        string thirdSecret)
    {
        var secrets = new[] { firstSecret, secondSecret, thirdSecret }
            .Where(secret => secret.Length > 0)
            .ToArray();
        AssertRedacted(output, expected, writeToStandardError, secrets);
        foreach (var secret in secrets)
            (writeToStandardError
                ? RunnerProcessProbe.Sanitize("", output, secrets).StandardError
                : RunnerProcessProbe.Sanitize(output, "", secrets).StandardOutput)
                .ShouldNotContain(secret);
    }

    [Test]
    public void Exact_secret_redaction_handles_repetitive_adversarial_input_within_linear_budget()
    {
        const int outputLength = 64 * 1024;
        const int secretCount = 64;
        const int maximumSecretLength = 4_000;
        var output = new string('a', outputLength);
        var secrets = Enumerable.Range(0, secretCount)
            .Select(index => new string('a', maximumSecretLength - index))
            .ToArray();

        RunnerProcessProbe.Sanitize("aaaa", "", ["aa"]);
        var baseline = Stopwatch.StartNew();
        RunnerProcessProbe.Sanitize(output, "", [secrets[0]]);
        baseline.Stop();

        var adversarial = Stopwatch.StartNew();
        var result = RunnerProcessProbe.Sanitize(output, "", secrets);
        adversarial.Stop();

        result.SensitiveOutputDetected.ShouldBeTrue();
        result.StandardOutput.ShouldBe("*");
        var comparativeBudget = TimeSpan.FromTicks(Math.Max(
            TimeSpan.FromMilliseconds(750).Ticks,
            baseline.Elapsed.Ticks * 12));
        adversarial.Elapsed.ShouldBeLessThan(
            comparativeBudget,
            $"adversarial {adversarial.Elapsed.TotalMilliseconds:F0} ms; "
            + $"single-pattern baseline {baseline.Elapsed.TotalMilliseconds:F0} ms");
        adversarial.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }

    private static void AssertRedacted(
        string output, string expected, bool writeToStandardError, IReadOnlyList<string>? secrets = null)
    {
        secrets ??= [];
        var result = writeToStandardError
            ? RunnerProcessProbe.Sanitize("", output, secrets)
            : RunnerProcessProbe.Sanitize(output, "", secrets);
        result.SensitiveOutputDetected.ShouldBeTrue();
        var actual = writeToStandardError ? result.StandardError : result.StandardOutput;
        actual.ShouldBe(expected);
        actual.ShouldNotContain("synthetic-credential");
        (writeToStandardError ? result.StandardOutput : result.StandardError).ShouldBeEmpty();
    }
}
