using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0307 S2 — <c>scripts/test-client.ps1 &lt;filter&gt;</c> must scope the vitest run to the named
/// file. The wrapper used to call <c>npx vitest run @args</c>; on Windows <c>npx</c> is the installer's
/// <c>npx.ps1</c>, which rebuilds argv from the SOURCE TEXT of the calling line, so the literal
/// <c>@args</c> reached Node and every scoped run silently became the whole 5–7 minute suite. The
/// wrapper now runs <c>node node_modules/vitest/vitest.mjs run @args</c>. This test drives the real
/// script under pwsh with the <c>attentionVisuals.test</c> filter and pins that exactly one test file
/// ran — its neighbour <c>AttentionPanel.test.tsx</c>, which the full suite would name, must be absent.
/// It does not run the unfiltered wrapper (that is the full suite, nightly's job).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestClientFilterTests
{
    private static readonly Regex AnsiEscape = new("\x1b\\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    [Test]
    public async Task Filter_argument_reaches_vitest_and_runs_exactly_one_file()
    {
        var repoRoot = DelegateScriptRunner.RepoRoot;
        var vitest = Path.Combine(repoRoot, "client", "node_modules", "vitest", "vitest.mjs");
        if (!File.Exists(vitest))
            throw new SkipTestException($"client/node_modules is not installed ({vitest}); run npm ci in client/.");

        var run = await RunWrapperAsync(repoRoot, "attentionVisuals.test");
        var output = AnsiEscape.Replace(run.Output, string.Empty);

        output.ShouldContain("CLIENT TESTS EXIT CODE:", customMessage: output);
        // vitest's summary shape: " Test Files  1 passed (1)" (or "1 failed (1)" if that file is red —
        // still ONE file, which is the claim under test).
        Regex.IsMatch(output, @"Test Files\s+1 (passed|failed)").ShouldBeTrue(
            "expected vitest to run exactly one test file for the attentionVisuals.test filter. Output:\n" + output);
        output.ShouldNotContain("AttentionPanel.test",
            customMessage: "the neighbouring AttentionPanel.test.tsx ran, so the filter was ignored (full suite). Output:\n" + output);
    }

    private static async Task<(int ExitCode, string Output)> RunWrapperAsync(string repoRoot, params string[] args)
    {
        var scriptPath = Path.Combine(repoRoot, "scripts", "test-client.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        // tinyrainbow always colours on win32 regardless of TTY; ask for plain text so the summary
        // line is matchable (the ANSI strip above is belt-and-braces).
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // One file is ~15s idle, ~35s on a loaded machine; vitest's own worker-start timeout is 60s.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new System.TimeoutException("scripts/test-client.ps1 attentionVisuals.test did not finish within 180s — a scoped run is seconds, so the filter is probably being ignored again (full suite).");
        }
        return (process.ExitCode, await stdout + await stderr);
    }
}
