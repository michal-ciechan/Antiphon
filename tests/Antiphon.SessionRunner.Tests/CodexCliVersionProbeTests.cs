using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCliVersionProbeTests
{
    [Test]
    public void C959_Parses_and_orders_versions()
    {
        // Late binding permits this behavioral contract to run on the unchanged baseline.
        // An absent parser is unknown evidence, never a successful synthetic version.
        object? Parse(string? output) => typeof(RunnerCapabilitiesDto).Assembly
            .GetType("Antiphon.SessionRunner.Contracts.CodexCliVersion")?
            .GetMethod("ParseBanner", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, [output]);
        foreach (var value in new[] { "0.156.1", "0.159.0", "0.159.1", "0.160.0", "0.9.0", "0.1000.0",
                     "0.159.1-beta.1", "0.160.0-beta.1", "0.159.1+build.7" })
        {
            foreach (var terminator in new[] { "", "\n", "\r\n" })
                (Parse("codex-cli " + value + terminator)?.ToString())
                    .ShouldBe(value, "C959-v01-numeric " + value);
        }
        foreach (var banner in new[] { "codex 0.160.0", "codex version 0.160.0",
                     "codex-cli version v0.160.0", "CODEX-CLI 0.160.0" })
            (Parse(banner)?.ToString()).ShouldBe("0.160.0", "C959-v01-banner " + banner);

        var invalid = new (string? Output, int Guard)[]
        {
            ("codex-cli 0.159", 5), ("codex-cli 2147483648.0.0", 6),
            ("codex-cli 00.159.1", 7), ("codex-cli 0.159.1-beta.01", 8),
            ("codex-cli 0.159.1-bad_id", 9), ("codex-cli 0.159.1+build..7", 10),
            ("codex-cli 0.160.0\ncodex-cli 0.160.0\n", 11),
            ("prefix codex-cli 0.160.0", 12), ("codex-cli 0.160.0 extra", 13),
            (null, 11), ("", 11), ("  ", 11), ("0.160.0", 12),
            ("codex-cli 0.160.0\n\n", 11), ("codex-cli -1.159.1", 5),
            ("codex-cli 0.159.1-", 10), ("codex-cli 0.159.1+", 10),
            ("codex-cli 0.159.1-.beta", 10), ("codex-cli 0.159.1+_", 9)
        };
        foreach (var (output, guard) in invalid)
            Parse(output).ShouldBeNull($"C959-pc-{guard:000} {output}");

        foreach (var (left, right, expected, guard) in new[]
                 {
                     ("0.9.0", "0.159.1", -1, 1), ("0.1000.0", "0.159.1", 1, 1),
                     ("0.159.1-beta.1", "0.159.1", -1, 2),
                     ("0.160.0-beta.1", "0.159.1", 1, 2),
                     ("0.159.1-beta.2", "0.159.1-beta.10", -1, 3),
                     ("0.159.1+build.7", "0.159.1", 0, 4),
                     ("0.159.0", "0.159.1", -1, 1), ("0.159.1", "0.159.1", 0, 1)
                 })
        {
            var a = Parse("codex-cli " + left);
            var b = Parse("codex-cli " + right);
            a.ShouldNotBeNull("C959-v01-left " + left);
            b.ShouldNotBeNull("C959-v01-right " + right);
            Math.Sign(((IComparable)a!).CompareTo(b)).ShouldBe(expected, $"C959-pc-{guard:000} {left}/{right}");
            Math.Sign(((IComparable)b!).CompareTo(a)).ShouldBe(-expected, "C959-v01-reverse " + left);
        }
    }

    [Test]
    public async Task C959_Probe_is_version_only_and_auth_free()
    {
        using var kit = new CodexCliVersionTestFixture { Mode = "stdin" };
        var sample = await kit.Attempt();
        CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBe("0.160.0", "C959-v02-argv");
        var start = kit.Starts.Single();
        start.ArgumentList.ToArray().ShouldBe(["--version"], "C959-pc-014");
        start.UseShellExecute.ShouldBeFalse("C959-pc-016");
        foreach (var (name, label) in new[] { ("OPENAI_API_KEY", 17), ("ANTIPHON_TASK_TOKEN", 18),
                     ("HTTPS_PROXY", 19), ("HTTP_PROXY", 19), ("NODE_OPTIONS", 20), ("NODE_PATH", 20),
                     ("BASH_ENV", 21), ("ENV", 21), ("DOTNET_STARTUP_HOOKS", 21) })
            start.Environment.ContainsKey(name).ShouldBeFalse($"C959-pc-{label:000} {name}");
        start.Environment["CODEX_HOME"].ShouldNotBe(Environment.GetEnvironmentVariable("CODEX_HOME"), "C959-pc-022");
        start.WorkingDirectory.ShouldNotBe(kit.Root, "C959-pc-023");
        File.Exists(Path.Combine(kit.Root, "receipts-1", "stdin-eof")).ShouldBeTrue("C959-pc-024");
        kit.AuthOpens.ShouldBe(0, "C959-pc-025");
        CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBeNull("C959-pc-199");
        var absolute = await kit.Attempt(kit.Executable);
        CodexCliVersionTestFixture.Text(absolute, "codexCliVersion").ShouldBe("0.160.0", "C959-v02-native");
    }

    [Test]
    public async Task C959_Failure_bounds_and_cleanup()
    {
        using var kit = new CodexCliVersionTestFixture { Mode = "nonzero" };
        foreach (var (mode, error, label) in new[] { ("nonzero", "nonzero_exit", 28),
                     ("stderr", "stderr_output", 29), ("stdout-flood", "output_truncated", 30),
                     ("stderr-flood", "output_truncated", 31) })
        {
            kit.Mode = mode;
            var sample = await kit.Attempt();
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBe(error, $"C959-pc-{label:000}");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBeNull("C959-v03-failure " + mode);
            kit.Children.Last().HasExited.ShouldBeTrue("C959-v03-reaped " + mode);
        }
        var missing = await kit.Attempt(Path.Combine(kit.Root, "missing"));
        CodexCliVersionTestFixture.Text(missing, "codexCliVersionError").ShouldBe("executable_missing", "C959-pc-027");
    }

    [Test]
    public async Task C959_Refresh_replaces_evidence()
    {
        using var kit = new CodexCliVersionTestFixture();
        await kit.Refresh();
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersion").ShouldBe("0.160.0", "C959-v04-replaced");
        var at = CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionCheckedAtUtc");
        kit.Clock.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 10; i++)
            CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionCheckedAtUtc").ShouldBe(at, "C959-pc-041");
        kit.Starts.Count.ShouldBe(1, "C959-pc-060");
        kit.Mode = "nonzero";
        kit.Clock.Advance(TimeSpan.FromMinutes(4));
        await kit.Refresh();
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersion").ShouldBeNull("C959-pc-039");
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v04-failed");
        var samples = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => kit.Attempt(force: false)));
        kit.Starts.Count.ShouldBe(2, "C959-pc-043");
        samples.All(s => CodexCliVersionTestFixture.Text(s, "codexCliVersion") is null).ShouldBeTrue("C959-v04-single-flight");
    }

    [Test]
    public async Task C959_Local_and_registration_share_snapshot()
    {
        using var kit = new CodexCliVersionTestFixture();
        await kit.Refresh();
        foreach (var (mode, version, error) in new[] { ("success", "0.160.0", (string?)null),
                     ("nonzero", (string?)null, "nonzero_exit") })
        {
            kit.Mode = mode;
            await kit.Refresh();
            var local = kit.Local();
            var remote = kit.Registration();
            local.TryGetProperty("codexCliVersion", out _).ShouldBeTrue("C959-v05-local");
            CodexCliVersionTestFixture.Text(local, "codexCliVersion").ShouldBe(version, "C959-pc-050");
            CodexCliVersionTestFixture.Text(remote, "codexCliVersion").ShouldBe(version, "C959-pc-054");
            CodexCliVersionTestFixture.Text(local, "codexCliVersionError").ShouldBe(error, "C959-pc-052");
            CodexCliVersionTestFixture.Text(remote, "codexCliVersionError").ShouldBe(error, "C959-pc-056");
            foreach (var field in new[] { "codexCliVersionCheckedAtUtc", "codexCliLauncherFingerprint" })
            {
                CodexCliVersionTestFixture.Text(local, field).ShouldNotBeNull("C959-v05-field " + field);
                CodexCliVersionTestFixture.Text(remote, field).ShouldBe(CodexCliVersionTestFixture.Text(local, field), "C959-v05-parity " + field);
            }
            local.GetProperty("features").EnumerateArray().Select(v => v.GetString()).ShouldContain("codex-cli-version-v1", "C959-pc-058");
            remote.GetProperty("features").EnumerateArray().Select(v => v.GetString()).ShouldContain("codex-cli-version-v1", "C959-pc-059");
        }
        JsonSerializer.Deserialize<RunnerCapabilitiesDto>("{\"ptyBackend\":\"InboxConhost\",\"ptyBackendRequested\":\"inbox\",\"ptyBackendReason\":\"test\",\"ptyBackendFellBack\":false,\"future\":17}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull("C959-v05-legacy");
    }
}
