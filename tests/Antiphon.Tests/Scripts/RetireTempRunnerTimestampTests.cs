using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0780. Drive the real verifier's ConvertFrom-Json and live bridge without contacting server2.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RetireTempRunnerTimestampTests
{
    [Test]
    [Arguments("2026-09-27T18:47:03.0367129Z", "2026-09-27T18:47:03.0367129Z")]
    [Arguments("2026-09-27T18:47:03.0367129+00:00", "2026-09-27T18:47:03.0367129Z")]
    [Arguments("2026-09-27T19:47:03.0367129+01:00", "2026-09-27T18:47:03.0367129Z")]
    public async Task Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc(string input, string expected)
    {
        var run = await ProbeAsync(input);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("DIAGNOSIS=c780-fake-remote");
        run.Output.ShouldNotContain("tempRetiredAt rejected");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(run.ResultPath));
        result.RootElement.GetProperty("accepted").GetBoolean().ShouldBeTrue();
        (await File.ReadAllTextAsync(run.TracePath))
            .ShouldContain($"export C590_TEMP_RETIRED_AT='{expected}'");
    }

    [Test]
    public async Task Bridge_still_rejects_a_non_timestamp_before_any_remote_call()
    {
        var run = await ProbeAsync("2026'; touch /tmp/c780; '");
        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("tempRetiredAt rejected");
        File.Exists(run.TracePath).ShouldBeFalse("the bridge must reject before calling ssh or scp");
    }

    private static async Task<Probe> ProbeAsync(string timestamp)
    {
        var root = Path.Combine(Path.GetTempPath(), "c780-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        var manifestPath = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            evidenceRoot = evidence,
            sourceSha = new string('a', 40),
            runId = "c780test",
            c604Branch = "master",
            tempRetiredAt = timestamp,
        }));
        var probePath = Path.Combine(root, "probe.ps1");
        await File.WriteAllTextAsync(probePath, """
            $ErrorActionPreference = 'Stop'
            function ssh { Add-Content -LiteralPath $env:C780_TRACE -Value ('ssh ' + ($args -join ' ')) }
            function scp {
                Add-Content -LiteralPath $env:C780_TRACE -Value ('scp ' + ($args -join ' '))
                if ($args -contains '-r') {
                    $dest = [string]$args[-1]
                    $case = ([string]$args[-2]).Split('/')[-1]
                    $dir = Join-Path $dest $case
                    New-Item -ItemType Directory -Force -Path $dir | Out-Null
                    '{"accepted":true,"diagnosis":"c780-fake-remote","exit":0}' |
                        Set-Content -LiteralPath (Join-Path $dir 'c590-result.json') -Encoding ascii
                }
            }
            . $env:C780_VERIFY -Case retire-temp-runner -Manifest $env:C780_MANIFEST
            """);

        var tracePath = Path.Combine(root, "trace.txt");
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["C780_VERIFY"] = Path.Combine(C590Harness.RepoRoot, "scripts", "verify-docker-stack.ps1");
        psi.Environment["C780_MANIFEST"] = manifestPath;
        psi.Environment["C780_TRACE"] = tracePath;
        psi.Environment.Remove("ANTIPHON_C590_STUB");
        foreach (var arg in new[] { "-NoProfile", "-File", probePath })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Probe(process.ExitCode, stdout + stderr, tracePath, Path.Combine(evidence, "c590-result.json"));
    }

    private sealed record Probe(int ExitCode, string Output, string TracePath, string ResultPath);
}
