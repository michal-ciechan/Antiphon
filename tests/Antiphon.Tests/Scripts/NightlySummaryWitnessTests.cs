using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2005. The production summary path streams a 1,274-row document inside a
/// 768 MiB heap, and ConvertTo-Json never receives the whole chunk list.
/// Windows PowerShell 5.1 does not run the writer: launch-rc.ps1 re-executes the
/// current pwsh 7 image.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlySummaryWitnessTests
{
    [Test]
    public void C2005_SummaryBuildDoesNotConvertTheChunkList()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "nightly-tests-impl.ps1");
        var text = File.ReadAllText(path);
        var body = FunctionBody(text, "Write-NightlyRunSummary");
        body.ShouldContain("Write-NightlyJsonFile -Path $summaryPath -Value $summary -Depth 8");
        body.ShouldContain("suitesFromSummary=$true");
        body.ShouldNotContain("suites=@(");
        body.ShouldNotContain("suites = @(");
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;
            if (!trimmed.Contains("ConvertTo-Json", StringComparison.Ordinal)) continue;
            trimmed.Contains("| ConvertTo-Json", StringComparison.Ordinal).ShouldBeFalse(
                "summary build path hands the whole chunk list to ConvertTo-Json: " + trimmed);
            var inputs = Regex.Matches(trimmed, @"-InputObject\s+(\$[A-Za-z_][A-Za-z0-9_]*)");
            inputs.Count.ShouldBeGreaterThan(0,
                "summary build path hands the whole chunk list to ConvertTo-Json: " + trimmed);
            foreach (Match input in inputs)
            {
                var name = input.Groups[1].Value;
                (name == "$firstList" || name == "$list").ShouldBeTrue(
                    "summary build path hands the whole chunk list to ConvertTo-Json: " + trimmed);
            }
        }

        var calls = Regex.Matches(text,
            @"Write-NightlyRunSummary -summary \$summary -suiteResults \$suiteResults");
        calls.Count.ShouldBe(1, "Invoke-AntiphonNightlyTests must call the production summary writer");
        Regex.Matches(text, @"Write-NightlyJsonFile -Path \$summaryPath").Count.ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C2005_SummaryWriterStreamsWithinABoundedHeap(CancellationToken cancellationToken)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-summary-witness.ps1");
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            ArgumentList = { "-NoProfile", "-File", script },
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ANTIPHON_LINUX_COMPILER_POLICY"] = "0";
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x30000000";
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var detail = stdout + "\n" + stderr;
        process.ExitCode.ShouldBe(0, detail);
        stdout.ShouldContain("HEAP limit=0x30000000");
        stdout.ShouldContain("WROTE bytes=");
        stdout.ShouldContain("suites=1274 progress=1274");
        stdout.ShouldContain("UID-ONCE");
        stdout.ShouldContain("KEYS coverageComplete runId liveness");
        stdout.ShouldContain("REPORT-ACCEPTED");
        stdout.ShouldContain("C2005-OK");
    }

    private static string FunctionBody(string text, string name)
    {
        var marker = "function " + name + " {";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, name);
        var depth = 0;
        for (var i = text.IndexOf('{', at); i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(at, i - at + 1);
            }
        }

        throw new InvalidOperationException("unclosed " + name);
    }
}
