using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0885 repeat proof through the row verb (scripts/run-checkpoint.ps1 -Repeat N launches it since unit 20 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), in process: one build and one run carry the reserved
/// repeat properties, the report line and the schema 3 report.json carry the repeat evidence, which the validate verb
/// (validate-checkpoint-receipt.ps1's body) qualifies only for the same N. CheckpointRepeatFixture plays dotnet.
/// </summary>
[Category("Integration")]
public sealed class RunCheckpointRepeatScriptTests : CheckpointTestBase
{
    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatBuildAndSingleRun(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var template = CheckpointRepeatFixture.WriteTemplate(root);
        var result = await CheckpointRepeatFixture.RunRowAsync(root, 5, template, cancellationToken);
        result.ExitCode.ShouldBe(0, result.Text);
        result.Calls.Length.ShouldBe(2, "one-build-one-run");
        result.Calls.Count(line => line.StartsWith("build ", StringComparison.Ordinal)).ShouldBe(1);
        result.Calls.Count(line => line.StartsWith("run ", StringComparison.Ordinal)).ShouldBe(1);
        result.Calls.All(line => line.Contains("AntiphonCheckpointRepeat=5", StringComparison.Ordinal))
            .ShouldBeTrue("repeat-property-both-phases");
        result.Calls.All(line => line.Contains("AntiphonCheckpointRepeatProject=", StringComparison.Ordinal))
            .ShouldBeTrue("selected-project-bound");
        result.Text.ShouldContain("repeat=5 repetitions=5/5 hostInvocations=1", Case.Sensitive, "repeat-line");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatReceiptAndTimings(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var result = await CheckpointRepeatFixture.RunRowAsync(root, 5, CheckpointRepeatFixture.WriteTemplate(root), cancellationToken);
        result.ExitCode.ShouldBe(0, result.Text);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.Evidence!));
        var data = doc.RootElement;
        data.GetProperty("schemaVersion").GetInt32().ShouldBe(3, "repeat-report-schema-3");
        var row = data.GetProperty("rows")[0];
        row.GetProperty("repeat").GetProperty("requested").GetInt32().ShouldBe(5);
        row.GetProperty("repeat").GetProperty("repetitions").GetArrayLength().ShouldBe(5, "five-ordinals");
        foreach (var ordinal in row.GetProperty("repeat").GetProperty("repetitions").EnumerateArray())
            ordinal.GetProperty("executed").GetInt32().ShouldBe(3, "ordinal-floor");
        row.GetProperty("timings").TryGetProperty("unavailableReason", out _).ShouldBeTrue("timing-reason-present");
        var sha = data.GetProperty("source").GetProperty("start").GetProperty("commit").GetString()!;
        (await ValidateAsync(result.Evidence!, sha, 5)).ShouldBe(0, "expected-repeat-qualifies");
        (await ValidateAsync(result.Evidence!, sha, 1)).ShouldBe(2, "expected-repeat-downgrade-refused");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatFailurePartialAndInvalidInput(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var failed = await CheckpointRepeatFixture.RunRowAsync(root, 5,
            CheckpointRepeatFixture.WriteTemplate(root, failingOrdinal: 2), cancellationToken);
        failed.ExitCode.ShouldBe(1, failed.Text);
        using (var doc = JsonDocument.Parse(File.ReadAllText(failed.Evidence!)))
        {
            var row = doc.RootElement.GetProperty("rows")[0];
            row.GetProperty("failed").GetInt32().ShouldBe(1, "original-failure-retained");
            row.GetProperty("repeat").GetProperty("passed").GetInt32().ShouldBe(4);
        }
        var invalidRoot = TempDir();
        var invalid = await CheckpointRepeatFixture.RunRowAsync(invalidRoot, 0,
            CheckpointRepeatFixture.WriteTemplate(invalidRoot), cancellationToken);
        invalid.ExitCode.ShouldBe(2, "invalid-repeat-no-launch");
        invalid.Calls.ShouldBeEmpty("invalid-repeat-no-launch");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatNoBuildAndLeaseCustody(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var template = CheckpointRepeatFixture.WriteTemplate(root);
        var first = await CheckpointRepeatFixture.RunRowAsync(root, 5, template, cancellationToken);
        first.ExitCode.ShouldBe(0, first.Text);
        using var doc = JsonDocument.Parse(File.ReadAllText(first.Evidence!));
        var sha = doc.RootElement.GetProperty("source").GetProperty("start").GetProperty("commit").GetString()!;
        var reused = await CheckpointRepeatFixture.RunRowAsync(root, 5, template, cancellationToken, noBuild: true, expectedSha: sha);
        reused.ExitCode.ShouldBe(0, reused.Text);
        reused.Text.ShouldContain("build=reused", Case.Sensitive, "repeat-stamp-reused");
        var wrong = await CheckpointRepeatFixture.RunRowAsync(root, 1, template, cancellationToken, noBuild: true, expectedSha: sha);
        wrong.ExitCode.ShouldBe(2, "repeat-binding-refused");
        wrong.Text.ShouldContain("build_source_mismatch", Case.Sensitive, "repeat-binding-refused");
    }

    private static Task<int> ValidateAsync(string evidence, string sha, int expected) =>
        Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", evidence, "--expected-source-sha", sha,
            "--expected-repeat", expected.ToString()], new CheckpointApp.Runtime { Output = TextWriter.Null });
}
