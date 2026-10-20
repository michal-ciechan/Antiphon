using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointInheritedFileTests : CheckpointTestBase
{
    [Test]
    [Arguments("R1")]
    [Arguments("R2")]
    [Arguments("R3")]
    [Arguments("R4")]
    [Timeout(60_000)]
    public async Task C1233_FileCallers(string site, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Linux FIFO admission proof");

        var healthy = await InvokeAsync(site, "ordinary", cancellationToken);
        healthy.Exit.ShouldBe(0, healthy.Stdout + healthy.Stderr);
        healthy.Watchdog.ShouldBeFalse();
        if (site is "R3" or "R4")
        {
            healthy.Stdout.ShouldContain("PASS C585 AsciiOnly run-checkpoint.ps1 is ASCII-only");
            healthy.Stdout.ShouldContain("PASS C585 AsciiOnly the harness is ASCII-only");
            var nonAscii = await InvokeAsync(site, "non-ascii", cancellationToken);
            nonAscii.Exit.ShouldNotBe(0, nonAscii.Stdout + nonAscii.Stderr);
            nonAscii.Stdout.ShouldContain(site == "R3"
                ? "FAIL C585 AsciiOnly run-checkpoint.ps1 is ASCII-only - 1"
                : "FAIL C585 AsciiOnly the harness is ASCII-only - 1");
        }
        else
        {
            healthy.Stdout.ShouldContain("CHECKPOINT USAGE phase=Namespace");
            var changed = await InvokeAsync(site, "changed", cancellationToken);
            changed.Exit.ShouldNotBe(0, changed.Stdout + changed.Stderr);
            changed.Stderr.ShouldContain("DLL digest changed");
        }

        var blocked = await InvokeAsync(site, "fifo", cancellationToken);
        blocked.Watchdog.ShouldBeFalse($"{site} reached an unbounded file read and required the 8 s watchdog");
        blocked.Milliseconds.ShouldBeLessThan(4_000, $"{site} file admission must finish within 4 s");
        blocked.Exit.ShouldNotBe(0, blocked.Stdout + blocked.Stderr);
        (blocked.Stdout + blocked.Stderr).ShouldContain("selected file is not regular");
    }

    private async Task<CheckpointBoundedProbe.Result> InvokeAsync(string site, string shape,
        CancellationToken cancellationToken)
    {
        var root = TempDir();
        var scripts = Path.Combine(root, "scripts");
        Directory.CreateDirectory(Path.Combine(scripts, "lib"));
        Directory.CreateDirectory(Path.Combine(scripts, "fixtures"));
        var nonce = Guid.NewGuid().ToString("N");
        var shim = Path.Combine(root, "shims");
        Directory.CreateDirectory(shim);
        Copy("lib/checkpoint-file-reader.ps1");
        var wrapper = Path.Combine(root, "invoke.ps1");
        var ready = """
            $ErrorActionPreference = 'Stop'
            $stat = [IO.File]::ReadAllText("/proc/$PID/stat")
            $startToken = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')[19]
            $identity = "$PID`:$startToken`:$env:C1233_NONCE"
            Write-Output "C1233_READY $identity"
            """;
        string? binding = null;
        byte[] bytes = [0, 13, 10, 128, 255, 65];
        if (site is "R1" or "R2")
        {
            Copy("verify-checkpoint-temp-usage.ps1");
            var output = Path.Combine(root, "tests/Antiphon.Tests/bin-probe");
            Directory.CreateDirectory(output);
            var dll = Path.Combine(output, "Antiphon.Tests.dll");
            if (site == "R1" && shape == "fifo")
                CheckpointBoundedProbe.Fifo(dll);
            else
                File.WriteAllBytes(dll, bytes);
            var sandbox = Path.Combine(root, "sandbox");
            Directory.CreateDirectory(sandbox);
            const string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            binding = Path.Combine(root, ".antiphon/c804-usage", sha, "namespace/pair.json");
            Directory.CreateDirectory(Path.GetDirectoryName(binding)!);
            File.WriteAllText(binding, JsonSerializer.Serialize(new
            {
                sha, digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                phase = "Namespace", output = output + Path.DirectorySeparatorChar, sandbox
            }));
            CheckpointBoundedProbe.Executable(Path.Combine(shim, "git"), $"#!/bin/sh\nprintf '%s\\n' '{sha}'\n");
            CheckpointBoundedProbe.Executable(Path.Combine(shim, "dotnet"), """
                #!/bin/sh
                set -eu
                if [ "$C1233_SITE" = R2 ] && [ "$C1233_SHAPE" = fifo ]; then
                  rm "$1"
                  mkfifo "$1"
                elif [ "$C1233_SHAPE" = changed ]; then
                  printf changed > "$1"
                fi
                printf '%s' "$C1233_NONCE" > "$C1233_CHILD_DONE"
                exit 0
                """);
            // Only acceptance accounting is stubbed; both production hash sites and the
            // real child start/join execute, without invoking the full usage lane.
            File.WriteAllText(Path.Combine(scripts, "lib/checkpoint-usage.ps1"), """
                function Get-Snapshot { param($Path) return @{ count = 0; allocatedBytes = 0; roots = @() } }
                function Get-Allocated { param($Path) return 0 }
                function Test-UsageEvidence {
                    param($Phase, $RosterPath, $TrxPath, $EventsPath)
                    return @{ errors = @(); rosterHash = 'fixture'; selected = 1; executed = 1; failed = 0; skipped = 0
                        createdRoots = 0; deletedRoots = 0; copiedLogicalBytes = 0; eventHighWaterRoots = 0; eventHighWaterCopiedBytes = 0 }
                }
                """);
            File.WriteAllText(wrapper, ready + "\n& \"$PSScriptRoot/scripts/verify-checkpoint-temp-usage.ps1\" -Phase Namespace -Pass 1 -OutputPath 'bin-probe/'\n");
        }
        else
        {
            Copy("test-run-checkpoint.ps1");
            Copy("lib/nightly-common.ps1");
            Copy("lib/c487-harness.ps1");
            var runner = Path.Combine(scripts, "run-checkpoint.ps1");
            if (site == "R3" && shape == "fifo")
                CheckpointBoundedProbe.Fifo(runner);
            else
                File.WriteAllBytes(runner, site == "R3" && shape == "non-ascii" ? [65, 255] : [65]);
            // This fixture is sourced after PowerShell parses the real harness and before
            // it calls AsciiOnly, so R4 changes only the file read by that second check.
            File.WriteAllText(Path.Combine(scripts, "fixtures/c889-c578-observation.ps1"), """
                if ($env:C1233_SITE -eq 'R4') {
                    $target = Join-Path $here 'test-run-checkpoint.ps1'
                    if ($env:C1233_SHAPE -eq 'fifo') {
                        [IO.File]::Delete($target)
                        & mkfifo $target
                        if ($LASTEXITCODE -ne 0) { throw 'fixture mkfifo failed' }
                    } elseif ($env:C1233_SHAPE -eq 'non-ascii') {
                        [IO.File]::WriteAllBytes($target, [byte[]]@(65, 255))
                    }
                }
                """);
            File.WriteAllText(wrapper, ready + "\n& \"$PSScriptRoot/scripts/test-run-checkpoint.ps1\" -Case C585_AsciiOnly -ResultsDirectory \"$PSScriptRoot/results\"\nexit $LASTEXITCODE\n");
        }

        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper })
            start.ArgumentList.Add(arg);
        start.Environment["PATH"] = shim + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["C1233_NONCE"] = nonce;
        start.Environment["C1233_SITE"] = site;
        start.Environment["C1233_SHAPE"] = shape;
        var childDone = Path.Combine(root, "child.done");
        start.Environment["C1233_CHILD_DONE"] = childDone;
        var result = await CheckpointBoundedProbe.RunAsync(start, nonce, RegisterCheckpointChild, cancellationToken);
        if (site is "R1" or "R2")
        {
            if (site == "R1" && shape == "fifo")
                File.Exists(childDone).ShouldBeFalse("pre-run refusal must precede the child");
            else
            {
                File.Exists(childDone).ShouldBeTrue(result.Stdout + result.Stderr);
                File.ReadAllText(childDone).ShouldBe(nonce, "the post-run check must follow the fake child");
            }
            if (shape == "ordinary")
            {
                using var pair = JsonDocument.Parse(File.ReadAllText(binding!));
                pair.RootElement.GetProperty("digest").GetString().ShouldBe(
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }
        }
        return result;

        void Copy(string relative) => File.Copy(Path.Combine(CheckpointFixtures.RepoRoot, "scripts", relative),
            Path.Combine(scripts, relative));
    }
}
