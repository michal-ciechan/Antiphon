using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RollingVolumeRecycleScriptTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Retired_absent_null_is_accepted()
    {
        using var fixture = new C1008WrapperFixture();
        var run = await fixture.Run("retire-temp");
        run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case" &&
            x["name"]?.GetValue<string>() == "retire-temp-runner").ShouldBeTrue(
                "retire-absent-null-accepted: the retired absent/null row must reach the retirement host case; " + run.Output);
        run.Exit.ShouldBe(0);
        run.Trace.Any(x => x["method"]?.GetValue<string>() == "POST").ShouldBeFalse(
            "retire-absent-null-accepted: retirement must remain stamped");
        JsonNode.Parse(File.ReadAllText(fixture.StatePath))!["statuses"]!["server2-temp"]!["retiredAt"]!
            .GetValue<string>().ShouldBe("2026-10-03T09:30:00Z");
    }
}

internal sealed class C1008WrapperFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("c1008-wrapper-").FullName;
    internal string StatePath => Path.Combine(Root, "state.json");
    internal string TracePath => Path.Combine(Root, "trace.jsonl");
    internal JsonObject State { get; }

    internal C1008WrapperFixture()
    {
        var vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1008-recycle-cases.json")))!;
        State = new JsonObject
        {
            ["scenario"] = "c1008", ["sha"] = vectors["sourceSha"]!.DeepClone(),
            ["tempContainer"] = false,
            ["statuses"] = new JsonObject
            {
                ["server2"] = vectors["mainAccepting"]!.DeepClone(),
                ["server2-temp"] = vectors["tempRetiredAbsent"]!.DeepClone()
            },
            ["tasks"] = vectors["emptyTasks"]!.DeepClone()
        };
        File.WriteAllText(Path.Combine(Root, "operator-token"), "C1008_TEST_TOKEN_SENTINEL");
    }

    internal async Task<(int Exit, string Output, JsonObject[] Trace)> Run(string phase, params string[] extra)
    {
        File.WriteAllText(StatePath, State.ToJsonString());
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/deploy-server2.ps1"), "-Rolling", "-Sha",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "-Phase", phase }.Concat(extra)) psi.ArgumentList.Add(arg);
        psi.Environment["ANTIPHON_OPERATOR_TOKEN_FILE"] = Path.Combine(Root, "operator-token");
        psi.Environment["ANTIPHON_TASK_TOKEN"] = "";
        psi.Environment["ANTIPHON_API"] = "http://127.0.0.1:1";
        psi.Environment["C727_TEST_HTTP_STUB"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c727-fake-http.ps1");
        psi.Environment["C727_TEST_VERIFY_STUB"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c727-fake-verify.ps1");
        psi.Environment["C727_TEST_STATE"] = StatePath;
        psi.Environment["C727_TEST_TRACE"] = TracePath;
        psi.Environment["C727_TEST_WAIT_MS"] = "100";
        psi.Environment["C727_TEST_POLL_MS"] = "5";
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("fixture child did not start");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch { if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); } throw; }
        var output = await stdout + await stderr;
        var trace = File.Exists(TracePath) ? File.ReadAllLines(TracePath).Select(x => JsonNode.Parse(x)!.AsObject()).ToArray() : [];
        return (proc.ExitCode, output, trace);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
