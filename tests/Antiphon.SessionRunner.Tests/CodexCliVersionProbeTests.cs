using System.Reflection;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
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
                Parse("codex-cli " + value + terminator)?.ToString()
                    .ShouldBe(value, "C959-v01-numeric " + value);
        }
        foreach (var banner in new[] { "codex 0.160.0", "codex version 0.160.0",
                     "codex-cli version v0.160.0", "CODEX-CLI 0.160.0" })
            Parse(banner)?.ToString().ShouldBe("0.160.0", "C959-v01-banner " + banner);

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
}
