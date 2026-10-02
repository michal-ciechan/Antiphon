using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointRepeatHostTests : CheckpointTestBase
{
    [Test]
    [Timeout(180_000)]
    public async Task native_repeat_reports_five_sets_in_one_process_and_lifecycle(CancellationToken cancellationToken)
    {
        var result = await RunHost(null, null, cancellationToken);
        result.ExitCode.ShouldBe(0, result.Text);
        var trx = TrxReport.Parse(result.Trx);
        trx.Ok.ShouldBeTrue(trx.Error);
        trx.Executed.ShouldBe(15, "native-five-sets: three base cases repeated five times");
        var proof = RepeatEvidenceValidator.Validate(trx, 5, result.Nonce, 3, ["RepeatHostTests"]);
        proof.Ok.ShouldBeTrue(proof.Error);
        proof.Evidence.HostInvocations.ShouldBe(1, "host-run-count-one");
        proof.Evidence.Repetitions.Select(round => round.Executed).ShouldBe([3, 3, 3, 3, 3]);
        var events = File.ReadAllLines(result.Log);
        events.Count(line => line == "assembly-setup").ShouldBe(1, "assembly-setup-once");
        events.Count(line => line == "assembly-teardown").ShouldBe(1, "assembly-teardown-once");
        events.Count(line => line.StartsWith("before:")).ShouldBe(15, "per-test-hooks-retained");
        events.Count(line => line.StartsWith("after:")).ShouldBe(15, "per-test-hooks-retained");
        events.Where(line => line.StartsWith("body:")).Select(line => line.Split(':')[^1]).Distinct().Count()
            .ShouldBe(15, "fresh-per-test-instances");
    }

    [Test]
    [Timeout(180_000)]
    public async Task native_failure_in_middle_ordinal_survives_later_passes(CancellationToken cancellationToken)
    {
        var result = await RunHost("2", null, cancellationToken);
        var trx = TrxReport.Parse(result.Trx);
        trx.Failed.ShouldBe(1, "original-repeat-failure-retained");
        trx.Passed.ShouldBe(14);
        trx.Failures.Single().Message.ShouldContain("deliberate-repeat-failure-ordinal-2");
        var proof = RepeatEvidenceValidator.Validate(trx, 5, result.Nonce, 3, ["RepeatHostTests"]);
        proof.ExitCode.ShouldBe(ExitCodes.FailedTests, "original-repeat-failure-retained");
        proof.Evidence.Passed.ShouldBe(4, "later pass cannot hide ordinal 2");
    }

    [Test]
    [Timeout(180_000)]
    public async Task native_skip_or_missing_markers_cannot_certify_five(CancellationToken cancellationToken)
    {
        var result = await RunHost(null, "2", cancellationToken);
        var trx = TrxReport.Parse(result.Trx);
        RepeatEvidenceValidator.Validate(trx, 5, result.Nonce, 3, ["RepeatHostTests"]).Ok
            .ShouldBeFalse("skip-cannot-certify-five");

        var complete = await RunHost(null, null, cancellationToken);
        var text = File.ReadAllText(complete.Trx);
        text.ShouldContain("C885_REPEAT_END", Case.Insensitive, "fixture must retain actual receiver markers");
        var changed = Path.Combine(Path.GetDirectoryName(complete.Trx)!, "missing-end.trx");
        File.WriteAllText(changed, text.Replace("C885_REPEAT_END", "C885_MISSING_END", StringComparison.Ordinal));
        RepeatEvidenceValidator.Validate(TrxReport.Parse(changed), 5, complete.Nonce, 3, ["RepeatHostTests"]).Ok
            .ShouldBeFalse("missing-end-refused");
    }

    private async Task<(int ExitCode, string Text, string Trx, string Log, string Nonce)> RunHost(
        string? failOrdinal, string? skipOrdinal, CancellationToken cancellationToken)
    {
        var root = TempDir();
        var staged = Path.Combine(AppContext.BaseDirectory, "checkpoint-repeat-host", "Antiphon.Checkpoints.RepeatHost.dll");
        File.Exists(staged).ShouldBeTrue("repeat host must be staged by the parent build");
        var log = Path.Combine(root, "events.log");
        var nonce = Guid.NewGuid().ToString("N");
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { staged, "--treenode-filter", "/*/*/RepeatHostTests*/*", "--report-trx",
                     "--report-trx-filename", "run.trx", "--results-directory", root })
            psi.ArgumentList.Add(argument);
        psi.Environment["C885_PROBE_LOG"] = log;
        psi.Environment["ANTIPHON_CHECKPOINT_NONCE"] = nonce;
        psi.Environment["TUNIT_MAX_PARALLEL_TESTS"] = "1";
        if (failOrdinal is not null) psi.Environment["C885_FAIL_ORDINAL"] = failOrdinal;
        if (skipOrdinal is not null) psi.Environment["C885_SKIP_ORDINAL"] = skipOrdinal;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdout + "\n" + await stderr, Path.Combine(root, "run.trx"), log, nonce);
    }
}
