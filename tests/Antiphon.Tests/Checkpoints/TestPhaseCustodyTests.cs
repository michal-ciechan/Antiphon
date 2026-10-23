using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class TestPhaseCustodyTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1514T_GateAndCustody(CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1514-custody-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var summaryPath = Path.Combine(root, "summary.json");
        var summaryText = "{\"outcome\":\"green\",\"coverageComplete\":false}";
        await File.WriteAllTextAsync(summaryPath, summaryText, cancellationToken);
        var before = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summaryText)));

        var cleanup = new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context);
        var missingInventory = cleanup.CleanOwnedOutputs(root, ["bin-owned"], 0, false, false, null, null);
        missingInventory.Refusals.ShouldContain("inventory-missing");

        var outside = Path.Combine(Path.GetTempPath(), "c1514-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "timing-sidecar.txt");
        await File.WriteAllTextAsync(outsideFile, "keep-outside", cancellationToken);
        var stray = Path.Combine(root, "stray.txt");
        await File.WriteAllTextAsync(stray, "keep-stray", cancellationToken);
        var unowned = Path.Combine(root, "bin-unowned");
        Directory.CreateDirectory(unowned);
        await File.WriteAllTextAsync(Path.Combine(unowned, "marker.txt"), "keep-bin", cancellationToken);

        var cleaned = cleanup.CleanOwnedOutputs(root, ["bin-owned"], 0, true, false, null, [outsideFile, stray, unowned]);
        cleaned.Refusals.ShouldContain("outside-isolated-root");
        cleaned.Refusals.ShouldContain("not-isolated-output");
        File.Exists(outsideFile).ShouldBeTrue("timing-outside-cleanup-root");
        File.Exists(stray).ShouldBeTrue("timing-outside-cleanup-root");
        Directory.Exists(unowned).ShouldBeTrue("timing-outside-cleanup-root");
        cleaned.Deleted.ShouldBeEmpty();

        var ran = await RunGateScript(root, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
        var after = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(summaryPath, cancellationToken)));
        after.ShouldBe(before);
        File.ReadAllText(summaryPath).ShouldBe(summaryText);
        var line = ran.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(item => item.StartsWith("C1514_DIGEST ", StringComparison.Ordinal));
        line.ShouldNotBeNull(ran.Text);
        var parts = line!["C1514_DIGEST ".Length..].Split(' ');
        parts.Length.ShouldBe(3);
        parts[0].ShouldBe(parts[1]);
        parts[2].ShouldBe("threw");
        File.Exists(Path.Combine(root, "timing", "malformed")).ShouldBeTrue();
    }

    private static async Task<(int ExitCode, string Text)> RunGateScript(string root, CancellationToken cancellationToken)
    {
        var script = Path.Combine(Path.GetTempPath(), "c1514-gate-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            $root = $args[0]
            $repo = $args[1]
            $lib = Join-Path $repo 'scripts/lib'
            . (Join-Path $lib 'nightly-common.ps1')
            . (Join-Path $lib 'release-gate.ps1')
            $manifest = New-ReleaseGateManifest -Fields @{ repository = 'michal-ciechan/Antiphon'; sha = 'abc'; profile = 'rc' }
            $before = Get-ReleaseGateManifestDigest -Manifest $manifest
            $timing = Join-Path $root 'timing'
            New-Item -ItemType Directory -Path $timing -Force | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $timing 'summary.json'), '{"complete":true}')
            [System.IO.File]::WriteAllText((Join-Path $timing 'malformed'), '{')
            $after = Get-ReleaseGateManifestDigest -Manifest $manifest
            $threw = $false
            try { New-ReleaseGateManifest -Fields @{ notAField = 1 } } catch { $threw = $true }
            if ($threw) { Write-Output ('C1514_DIGEST ' + $before + ' ' + $after + ' threw') }
            else { Write-Output ('C1514_DIGEST ' + $before + ' ' + $after + ' silent') }
            """, cancellationToken);
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(FindRepo());
        try
        {
            return await C1514.Run(start, cancellationToken);
        }
        finally
        {
            try { File.Delete(script); } catch (IOException) { }
        }
    }

    private static string FindRepo()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "scripts", "lib", "release-gate.ps1")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("scripts/lib/release-gate.ps1 not found");
    }

    private static class C1514
    {
        public static async Task<(int ExitCode, string Text)> Run(ProcessStartInfo start, CancellationToken cancellationToken)
        {
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var process = Process.Start(start)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, stdout + "\n" + stderr);
        }
    }
}
