using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0179 R1 — one case per redaction rule, plus a sweep that no secret-shaped string survives.</summary>
public class DiagnosticsRedactorTests
{
    [Test]
    public void Key_placeholder_bodies_are_redacted()
    {
        var redacted = new DiagnosticsRedactor(includePaths: true)
            .Redact("env={{key:ANTHROPIC_API_KEY}} and {{key:SLACK_BOT_TOKEN}}");
        redacted.ShouldNotContain("ANTHROPIC_API_KEY");
        redacted.ShouldNotContain("SLACK_BOT_TOKEN");
        redacted.ShouldContain("{{key:***}}");
    }

    [Test]
    public void Sk_shaped_keys_are_redacted()
    {
        const string secret = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact($"Authorization {secret}");
        redacted.ShouldNotContain(secret);
        redacted.ShouldContain("sk-***");
    }

    [Test]
    public void Slack_bot_and_user_tokens_are_redacted()
    {
        const string bot = "xoxb-test-fixture-token";
        const string user = "xoxp-test-fixture-token";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact($"{bot} {user}");
        redacted.ShouldNotContain(bot);
        redacted.ShouldNotContain(user);
        redacted.ShouldContain("xox*-***");
    }

    [Test]
    public void Github_pats_are_redacted()
    {
        const string classic = "ghp_testfixturetoken00000";
        const string fine = "github_pat_testfixturetoken0000000000";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact($"{classic} {fine}");
        redacted.ShouldNotContain(classic);
        redacted.ShouldNotContain(fine);
        redacted.ShouldContain("ghp_***");
        redacted.ShouldContain("github_pat_***");
    }

    [Test]
    public void Bearer_tokens_are_redacted()
    {
        const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.sig";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact($"Authorization: Bearer {token}");
        redacted.ShouldNotContain(token);
        redacted.ShouldContain("Bearer ***");
    }

    [Test]
    public void Telegram_bot_tokens_are_redacted()
    {
        const string token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsawx";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact($"bot{token}/sendMessage");
        redacted.ShouldNotContain(token);
        redacted.ShouldContain("***TELEGRAM_TOKEN***");
    }

    [Test]
    public void Connection_string_passwords_are_redacted()
    {
        const string password = "SuperSecretPassw0rd";
        var redacted = new DiagnosticsRedactor(includePaths: true)
            .Redact($"Host=db;Username=antiphon;Password={password};Database=antiphon");
        redacted.ShouldNotContain(password);
        redacted.ShouldContain("Password=***");
    }

    [Test]
    public void Home_path_is_kept_only_with_IncludePaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        home.ShouldNotBeNullOrWhiteSpace();
        var withPaths = new DiagnosticsRedactor(includePaths: true).Redact($"cwd {home}\\.claude");
        withPaths.ShouldContain(home);

        var redacted = new DiagnosticsRedactor(includePaths: false).Redact($"cwd {home}\\.claude");
        redacted.ShouldNotContain(home);
        redacted.ShouldContain("~");
    }

    [Test]
    public void Project_directories_are_replaced_unless_IncludePaths()
    {
        const string project = @"C:\src\Antiphon";
        var redacted = new DiagnosticsRedactor(includePaths: false, [project])
            .Redact(@"cwd C:\src\Antiphon\server");
        redacted.ShouldNotContain(project);
        redacted.ShouldContain("<project-1>");

        var kept = new DiagnosticsRedactor(includePaths: true, [project])
            .Redact(@"cwd C:\src\Antiphon\server");
        kept.ShouldContain(project);
    }

    [Test]
    [Arguments("{{key:ANTHROPIC_API_KEY}}")]
    [Arguments("sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345")]
    [Arguments("xoxb-test-fixture-token")]
    [Arguments("xoxp-test-fixture-token")]
    [Arguments("ghp_testfixturetoken00000")]
    [Arguments("github_pat_testfixturetoken0000000000")]
    [Arguments("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.sig")]
    [Arguments("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsawx")]
    [Arguments("SuperSecretPassw0rd")]
    public void No_secret_shaped_string_survives_redaction(string secret)
    {
        var wrapped = secret == "SuperSecretPassw0rd"
            ? $"Host=db;Password={secret};"
            : $"prefix {secret} suffix";
        var redacted = new DiagnosticsRedactor(includePaths: true).Redact(wrapped);
        redacted.ShouldNotContain(secret);
    }
}
