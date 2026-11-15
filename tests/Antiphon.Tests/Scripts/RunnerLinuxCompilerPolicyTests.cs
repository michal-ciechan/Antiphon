using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
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

    [Test]
    [Arguments("0")]
    [Arguments("1")]
    [NotInParallel]
    public async Task Rendered_policy_inherits_through_session_and_build_wrapper(string saved)
    {
        var modelOutput = await Run("--model-value=" + saved);
        var modelLine = modelOutput.Split('\n').Single(line => line.StartsWith("MODEL ", StringComparison.Ordinal));
        using var model = JsonDocument.Parse(modelLine[6..]);
        var policy = model.RootElement.GetProperty("services").GetProperty("session-runner")
            .GetProperty("environment").GetProperty("ANTIPHON_LINUX_COMPILER_POLICY").GetString();
        policy.ShouldBe(saved);
        using var fixture = new ComposeFixtureRoot("c1906-runtime-");
        var root = fixture.Root;
        var probe = Path.Combine(root, "probe.ps1");
        var observation = Path.Combine(root, "observed.txt");
        await File.WriteAllTextAsync(probe,
            "param([string]$ResultPath)\n$env:ANTIPHON_LINUX_COMPILER_POLICY | Set-Content -LiteralPath $ResultPath\n");
        var overrides = new Dictionary<string, string>
        {
            ["C589_SLOT_SHIM"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c589-slot-shim.ps1"),
            ["C589_SLOT_SCRIPT"] = "granted",
            ["C589_SLOT_LOG"] = Path.Combine(root, "slot.log"),
            ["C589_COMMAND_SHIM"] = ""
        };
        overrides.ContainsKey("ANTIPHON_LINUX_COMPILER_POLICY").ShouldBeFalse("the session must inherit its runner's setting");
        await using var session = new PtyAgentRunner("inbox");
        var previous = Environment.GetEnvironmentVariable("ANTIPHON_LINUX_COMPILER_POLICY");
        try
        {
            Environment.SetEnvironmentVariable("ANTIPHON_LINUX_COMPILER_POLICY", policy);
            await session.StartAsync("pwsh", new[]
            {
                "-NoProfile", "-File", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/build-slot.ps1"),
                "-Label", "c1906-inheritance-fixture", "--", "pwsh", "-NoProfile", "-File", probe,
                "-ResultPath", observation
            }, DelegateScriptRunner.RepoRoot, overrides);
        }
        finally { Environment.SetEnvironmentVariable("ANTIPHON_LINUX_COMPILER_POLICY", previous); }
        (await session.Exited.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBe(0, session.SnapshotText());
        File.ReadAllText(observation).Trim().ShouldBe(saved, "real PTY and build-slot child inheritance");
        Console.WriteLine("PASS native session/build-wrapper inherited=" + saved + " evidence=" + root);
    }

    private static async Task<string> Run(string selection)
    {
        using var fixture = new ComposeFixtureRoot("c1906-compose-");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1906-compiler-policy-compose.mjs"));
        start.ArgumentList.Add(selection);
        fixture.Bind(start);
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
