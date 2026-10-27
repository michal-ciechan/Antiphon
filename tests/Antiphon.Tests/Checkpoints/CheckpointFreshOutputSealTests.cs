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
    public Task C1573_compiled_facade_seals_through_the_real_reader() =>
        Run("scripts/test-checkpoint-compiled-facade.ps1", "C1573 compiled-facade PASS", toolDll: true);

    private async Task Run(string script, string passLine, bool toolDll)
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
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new ShouldAssertException(script + " exceeded 180 seconds");
        }
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        (await stdout).ShouldContain(passLine);
    }
}
