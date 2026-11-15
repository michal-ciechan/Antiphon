using System.Diagnostics;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[NativeLinuxOnly("Linux deployment grammar and Compose rendering use bash and POSIX fixture paths; no daemon is contacted.")]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerLinuxCompilerPolicyTests
{
    [Test]
    [Arguments("empty")]
    [Arguments("space-only")]
    [Arguments("tab-only")]
    [Arguments("leading-space-key")]
    [Arguments("leading-tab-key")]
    [Arguments("space-before-equals")]
    [Arguments("tab-before-equals")]
    [Arguments("space-after-equals")]
    [Arguments("tab-after-equals")]
    [Arguments("trailing-space")]
    [Arguments("trailing-tab")]
    [Arguments("crlf")]
    [Arguments("boolean-true")]
    [Arguments("boolean-false")]
    [Arguments("numeric-two")]
    [Arguments("leading-zero")]
    [Arguments("lowercase-key")]
    [Arguments("mixed-case-key")]
    [Arguments("export-prefix")]
    [Arguments("quoted-value")]
    [Arguments("inline-comment")]
    [Arguments("comment-without-space")]
    [Arguments("indented-duplicate")]
    [Arguments("tabbed-duplicate")]
    [Arguments("spaced-duplicate")]
    [Arguments("comment-looking-duplicate")]
    [Arguments("case-duplicate")]
    [Arguments("same-value-duplicate")]
    [Arguments("later-duplicate")]
    [Arguments("invalid-value")]
    [Arguments("duplicate")]
    [Arguments("shell-value")]
    public async Task Saved_invalid_setting_refuses_before_side_effects(string shape)
    {
        var output = await Run("--refusal-case=" + shape);
        output.ShouldContain("PASS refusals=4 selected-case=" + shape);
    }

    [Test]
    public async Task Saved_values_render_and_survive_each_named_phase_independently()
    {
        var output = await Run("--models-only");
        output.ShouldContain("PASS matrices=16 refusals=0 missing-file=1 base-equivalence=1");
    }

    private static async Task<string> Run(string selection)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1906-compiler-policy-compose.mjs"));
        start.ArgumentList.Add(selection);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            throw;
        }
        var output = await stdout + await stderr;
        Console.WriteLine(output);
        child.ExitCode.ShouldBe(0, output);
        return output;
    }
}
