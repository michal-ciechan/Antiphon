using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointFreshOutputSealTests : CheckpointTestBase
{
    [Test]
    public Task C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse() =>
        Run("scripts/test-checkpoint-output-inventory.ps1", "C1573 inventory PASS", toolDll: false);

    [Test]
    public Task C1573_linux_only_cases_skip_when_stat_is_absent() =>
        Run(
            "scripts/test-checkpoint-output-inventory.ps1",
            "C1573 inventory PASS",
            toolDll: false,
            hideStat: true,
            requiredLines:
            [
                "SKIP inode-reuse (Linux only; pending native Windows witness)",
                "SKIP symlink-refused (Linux only; pending native Windows witness)",
                "SKIP fifo-refused (Linux only; pending native Windows witness)",
                "PASS appended-refused",
                "PASS truncated-refused",
                "PASS swapped-same-size-refused",
            ]);

    [Test]
    public Task C1573_compiled_facade_seals_through_the_real_reader() =>
        Run("scripts/test-checkpoint-compiled-facade.ps1", "C1573 compiled-facade PASS", toolDll: true);

    private async Task Run(string script, string passLine, bool toolDll, bool hideStat = false, string[]? requiredLines = null)
    {
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(CheckpointFixtures.RepoRoot, script));
        psi.ArgumentList.Add("-Root");
        psi.ArgumentList.Add(TempDir());
        if (toolDll)
        {
            psi.ArgumentList.Add("-ToolDll");
            psi.ArgumentList.Add(typeof(Antiphon.Checkpoints.Program).Assembly.Location);
        }
        if (hideStat) psi.ArgumentList.Add("-HideStat");
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // Review 131acfcb measured 4-6s. 30s covers cold pwsh plus the 70 MiB seal (CARD-1573).
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new ShouldAssertException(script + " exceeded 30 seconds");
        }
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        var text = await stdout;
        text.ShouldContain(passLine);
        if (requiredLines is null) return;
        foreach (var line in requiredLines) text.ShouldContain(line);
        text.ShouldNotContain("is not recognized");
        text.ShouldNotContain("FAIL inode-reuse");
    }
}
