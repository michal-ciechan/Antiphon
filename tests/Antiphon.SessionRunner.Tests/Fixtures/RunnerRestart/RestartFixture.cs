using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

internal sealed class RestartFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "antiphon-c420-" + Guid.NewGuid().ToString("N"));
    public static string Repo
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException();
        }
    }
    public Dictionary<string, object?> Config { get; } = new()
    { ["healthyAt"] = 0, ["status"] = 200, ["startMs"] = 0 };
    public RestartFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "scripts"));
        File.WriteAllText(Path.Combine(Root, "state"), "stopped-sentinel");
        File.WriteAllText(Path.Combine(Root, "pid"), "pid-sentinel");
        var source = Path.Combine(Repo, "scripts", "restart-session-runner.ps1");
        var copy = Path.Combine(Root, "scripts", "restart-session-runner.ps1");
        File.Copy(source, copy);
        SHA256.HashData(File.ReadAllBytes(copy)).ShouldBe(SHA256.HashData(File.ReadAllBytes(source)));
        var helper = Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1");
        var fake = Path.Combine(Repo, "tests", "Antiphon.SessionRunner.Tests", "Fixtures", "RunnerRestart", "platform.ps1");
        File.WriteAllText(Path.Combine(Root, "scripts", "session-runner-restart-health.ps1"), $". '{Quote(helper)}'\n. '{Quote(fake)}'\n");
    }
    public static string Quote(string value) => value.Replace("'", "''");
    public async Task<(int Exit, string Output)> Script(string script, string shell = "pwsh.exe", params string[] args)
    {
        var path = Path.Combine(Root, "driver.ps1");
        File.WriteAllText(path, script);
        return await Process(path, shell, args);
    }
    private async Task<(int Exit, string Output)> Process(string path, string shell, params string[] args)
    {
        var psi = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path }.Concat(args)) psi.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        return (process.ExitCode, (await stdout) + (await stderr));
    }
    public async Task<RestartResult> Run(string shell = "pwsh.exe", params string[] args)
    {
        // Parse before importing/executing: a future direct OS call in either front
        // door must fail the fixture rather than escape dependency interception.
        var boundary = await Script($$"""
            $ErrorActionPreference='Stop'
            $entry=[System.Management.Automation.Language.Parser]::ParseFile('{{Quote(Path.Combine(Root, "scripts", "restart-session-runner.ps1"))}}',[ref]$null,[ref]$null)
            $helper=[System.Management.Automation.Language.Parser]::ParseFile('{{Quote(Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1"))}}',[ref]$null,[ref]$null)
            foreach($statement in $helper.EndBlock.Statements) {
                if($statement -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { throw 'helper import is not inert' }
            }
            $core=$helper.Find({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-RunnerRestart'},$true)
            foreach($command in $entry.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true)) {
                $name=$command.GetCommandName()
                if($name -and $name -notin @('Join-Path','Split-Path','New-RunnerRestartPlatform','Invoke-RunnerRestart','Write-Host','ConvertTo-Json')) { throw "entry escaped thin boundary: $name" }
                if(-not $name -and $command.InvocationOperator -ne 'Dot') { throw 'entry contains unchecked dynamic command' }
            }
            foreach($call in $entry.FindAll({param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst]},$true)) {
                if($call.Member.Value -notin @('StartNew','ToString')) { throw 'entry contains unchecked member call' }
            }
            foreach($command in $core.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true)) {
                $name=$command.GetCommandName()
                if($name -and $name -notin @('Write-Host','New-RunnerMilestoneReader','Read-RunnerMilestones')) { throw "core bypassed platform: $name" }
                if(-not $name -and -not $command.Extent.Text.StartsWith('& $Platform.')) { throw 'core contains unchecked dynamic command' }
            }
            """, shell);
        boundary.Exit.ShouldBe(0, boundary.Output);
        File.WriteAllText(Path.Combine(Root, "config.json"), JsonSerializer.Serialize(Config));
        var run = await Process(Path.Combine(Root, "scripts", "restart-session-runner.ps1"), shell, args);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var final = lines.Where(l => l.StartsWith("RUNNER RESTART RESULT: ")).ToArray();
        final.Length.ShouldBe(1, run.Output); lines.Last().ShouldBe(final[0]);
        var result = JsonDocument.Parse(final[0][23..]).RootElement.Clone();
        result.GetProperty("exitCode").GetInt32().ShouldBe(run.Exit, run.Output);
        var artifact = Path.Combine(Repo, ".antiphon", "c420-evidence", Path.GetFileName(Root));
        Directory.CreateDirectory(artifact);
        File.WriteAllText(Path.Combine(artifact, "output.txt"), run.Output);
        foreach (var name in new[] { "config.json", "trace.jsonl", "startup.log" })
            if (File.Exists(Path.Combine(Root, name))) File.Copy(Path.Combine(Root, name), Path.Combine(artifact, name), true);
        return new(run.Exit, result, run.Output, Trace());
    }
    public JsonElement[] Trace() => File.Exists(Path.Combine(Root, "trace.jsonl"))
        ? File.ReadAllLines(Path.Combine(Root, "trace.jsonl")).Select(x => JsonDocument.Parse(x).RootElement.Clone()).ToArray() : [];
    public async Task DecodeCapturedMilestones(string content, int producerPid, string processStart, string producer, string expectedPhase)
    {
        File.WriteAllText(Path.Combine(Root, "captured.log"), content);
        var script = $$"""
            $ErrorActionPreference='Stop'
            . '{{Quote(Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1"))}}'
            $identity=@{pid={{producerPid}};startTimeUtc='{{processStart}}';path='fixture-owned'}
            $p=@{
                Process={param($id) if($id -eq {{producerPid}}){$identity} }
                Verify={param($i) $i.pid -eq {{producerPid}} }
                Census={ @{supervisor={{(producer == "supervisor" ? "$identity" : "$null")}} } }
            }
            $reader=New-RunnerMilestoneReader '{{Quote(Path.Combine(Root, "captured.log"))}}'
            Read-RunnerMilestones $reader $p '2000-01-01T00:00:00Z'
            if($reader.Phase.event -ne '{{expectedPhase}}'){throw "production decoder did not accept producer records: $($reader.Phase.event)"}
            """;
        var result = await Script(script); result.Exit.ShouldBe(0, result.Output);
    }
    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}
internal sealed record RestartResult(int Exit, JsonElement Json, string Output, JsonElement[] Trace)
{
    public string Outcome => Json.GetProperty("outcome").GetString()!;
    public double Wait => Json.GetProperty("waitElapsedMs").GetDouble();
    public string[] Mutations => Trace.Select(t => t.GetProperty("op").GetString()!).Where(op => op is not ("probe" or "sleep")).ToArray();
}
