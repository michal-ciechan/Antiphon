using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunCheckpointRepeatScriptTests : CheckpointTestBase
{
    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatBuildAndSingleRun(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var template = CheckpointRepeatFixture.WriteTemplate(root);
        var result = await CheckpointRepeatFixture.RunScriptAsync(root, 5, template, cancellationToken);
        result.ExitCode.ShouldBe(0, result.Text);
        result.Calls.Length.ShouldBe(2, "script-one-build-one-run");
        result.Calls.Count(line => line.StartsWith("build ", StringComparison.Ordinal)).ShouldBe(1);
        result.Calls.Count(line => line.StartsWith("run ", StringComparison.Ordinal)).ShouldBe(1);
        result.Calls.All(line => line.Contains("AntiphonCheckpointRepeat=5", StringComparison.Ordinal))
            .ShouldBeTrue("script-repeat-property-both-phases");
        result.Calls.All(line => line.Contains("AntiphonCheckpointRepeatProject=", StringComparison.Ordinal))
            .ShouldBeTrue("script-selected-project-bound");
        result.Text.ShouldContain("repeat=5 repetitions=5/5 hostInvocations=1", Case.Sensitive, "script-repeat-line");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatReceiptAndTimings(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var result = await CheckpointRepeatFixture.RunScriptAsync(root, 5,
            CheckpointRepeatFixture.WriteTemplate(root), cancellationToken);
        result.ExitCode.ShouldBe(0, result.Text);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.Evidence!));
        var data = doc.RootElement;
        data.GetProperty("version").GetInt32().ShouldBe(2, "script-envelope-v2");
        data.GetProperty("repeat").GetProperty("requested").GetInt32().ShouldBe(5);
        data.GetProperty("repeat").GetProperty("repetitions").GetArrayLength().ShouldBe(5, "script-five-ordinals");
        foreach (var ordinal in data.GetProperty("repeat").GetProperty("repetitions").EnumerateArray())
            ordinal.GetProperty("executed").GetInt32().ShouldBe(3, "script-ordinal-floor");
        data.GetProperty("timings").GetProperty("slotWaitSeconds").GetDouble().ShouldBe(0);
        data.GetProperty("timings").TryGetProperty("unavailableReason", out _).ShouldBeTrue("script-timing-reason-present");
        var sha = data.GetProperty("source").GetProperty("start").GetProperty("commit").GetString()!;
        var valid = await ValidateAsync(result.Evidence!, sha, 5, cancellationToken);
        valid.ExitCode.ShouldBe(0, valid.Text);
        var downgrade = await ValidateAsync(result.Evidence!, sha, 1, cancellationToken);
        downgrade.ExitCode.ShouldBe(2, "expected-repeat-downgrade-refused");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C885_RepeatFailurePartialAndInvalidInput(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var failed = await CheckpointRepeatFixture.RunScriptAsync(root, 5,
            CheckpointRepeatFixture.WriteTemplate(root, failingOrdinal: 2), cancellationToken);
        failed.ExitCode.ShouldBe(1, failed.Text);
        using (var doc = JsonDocument.Parse(File.ReadAllText(failed.Evidence!)))
        {
            doc.RootElement.GetProperty("failed").GetInt32().ShouldBe(1, "script-original-failure-retained");
            doc.RootElement.GetProperty("repeat").GetProperty("passed").GetInt32().ShouldBe(4);
        }
        var invalidRoot = TempDir();
        var invalid = await CheckpointRepeatFixture.RunScriptAsync(invalidRoot, 0,
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
        var first = await CheckpointRepeatFixture.RunScriptAsync(root, 5, template, cancellationToken);
        first.ExitCode.ShouldBe(0, first.Text);
        using var doc = JsonDocument.Parse(File.ReadAllText(first.Evidence!));
        var sha = doc.RootElement.GetProperty("source").GetProperty("start").GetProperty("commit").GetString()!;
        var reused = await CheckpointRepeatFixture.RunScriptAsync(root, 5, template, cancellationToken,
            noBuild: true, expectedSha: sha);
        reused.ExitCode.ShouldBe(0, reused.Text);
        reused.Text.ShouldContain("build=reused", Case.Sensitive, "script-repeat-stamp-reused");
        var wrong = await CheckpointRepeatFixture.RunScriptAsync(root, 1, template, cancellationToken,
            noBuild: true, expectedSha: sha);
        wrong.ExitCode.ShouldBe(2, "script-repeat-binding-refused");
        wrong.Text.ShouldContain("build_source_mismatch", Case.Sensitive, "script-repeat-binding-refused");
    }

    private static async Task<(int ExitCode, string Text)> ValidateAsync(string evidence, string sha, int expected,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "validate-checkpoint-receipt.ps1"),
                     "-Evidence", evidence, "-ExpectedSourceSha", sha, "-ExpectedRepeat", expected.ToString() })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdout + "\n" + await stderr);
    }
}
