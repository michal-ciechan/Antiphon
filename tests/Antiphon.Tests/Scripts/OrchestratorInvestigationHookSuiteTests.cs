using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0247: the Node hook-classifier tests (scripts/hooks/__tests__/*.test.mjs) under node's own test runner. The hook the
/// committed .claude/settings.json runs is JavaScript, so its tests stay JavaScript; this class is their runner. It replaced
/// scripts/test-hooks.ps1 and its census row in the nightly scripts suite (unit 33 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The live claude -p probe (live-s2-probe.mjs) is not a
/// .test.mjs and is not run. The C# port of the classifier is OrchestratorInvestigationDetectorTests' subject.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class OrchestratorInvestigationHookSuiteTests
{
    private static readonly string[] Suites =
    [
        "./__tests__/orchestrator-investigation.test.mjs",
        "./__tests__/orchestrator-investigation-hook.test.mjs",
    ];

    [Test]
    public async Task Hook_classifier_node_tests_pass()
    {
        var hooks = Path.Combine(C1514Support.RepoRoot(), "scripts", "hooks");
        foreach (var suite in Suites)
            File.Exists(Path.Combine(hooks, suite)).ShouldBeTrue("missing " + suite);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = hooks,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "--test", "--test-reporter=tap" }.Concat(Suites))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("node --test exceeded 120 seconds");
        }
        var output = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, output);
        // A green run that ran nothing proves nothing: the TAP summary names the tests run and no failure.
        var tests = Regex.Match(output, @"(?m)^# tests (\d+)\s*$");
        tests.Success.ShouldBeTrue(output);
        int.Parse(tests.Groups[1].Value).ShouldBeGreaterThan(0, output);
        Regex.IsMatch(output, @"(?m)^# fail 0\s*$").ShouldBeTrue(output);
    }
}
