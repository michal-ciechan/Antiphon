using Antiphon.Tests.Application;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceScriptReuseTests
{
    [Test]
    public async Task Session_driver_matches_process_receipts()
    {
        using var fixture = new CheckpointSourceScriptFixture();
        var process = await ReplayAsync(fixture);
        fixture.RestoreSeedForParity();
        fixture.SessionMode = true;
        var offset = fixture.Invocations.Count;
        var session = await ReplayAsync(fixture);
        var workers = fixture.Invocations.Skip(offset).ToArray();
        workers.Length.ShouldBe(18, "script-process-parity");
        workers.Select(w => (w.ProcessId, w.StartTicks)).Distinct().Count().ShouldBe(1, "script-worker-created-once");
        workers.Select(w => w.RunspaceId).Distinct().Count().ShouldBe(18, "script-worker-created-once");
        workers.ShouldNotContain(w => w.RunspaceId == Guid.Empty, "script-worker-created-once");
        session.ShouldBe(process, "script-process-parity");
    }

    private static async Task<string[]> ReplayAsync(CheckpointSourceScriptFixture fixture)
    {
        var observations = new List<string>();
        async Task<Result> Run(string? sha = null, bool reuse = false, string? trx = "c585-green.trx", int build = 0,
            string? drift = null, bool slot = false, bool busy = false)
        {
            var result = await fixture.RunAsync(sha, reuse, trx, build, drift, slot, busySlot: busy);
            var json = JsonNode.Parse(result.Source.GetRawText())!.AsObject();
            json.Remove("timings");
            foreach (var point in new[] { "start", "end" })
                if (json[point] is JsonObject snapshot)
                {
                    DateTimeOffset.TryParse((string?)snapshot["observedAtUtc"], out _).ShouldBeTrue("script-process-parity timestamp shape");
                    snapshot["observedAtUtc"] = "<time>";
                }
            var artifacts = Directory.GetFiles(Path.GetDirectoryName(result.Evidence)!).Select(Path.GetFileName).Order(StringComparer.Ordinal);
            observations.Add(Normalize($"{result.Exit}|{fixture.Calls}|{json.ToJsonString()}|{string.Join(',', artifacts)}|{result.Output}", fixture));
            result.Output.Split('\n').Count(l => l.StartsWith("CHECKPOINT CP-2 commit=", StringComparison.Ordinal)).ShouldBe(1, "script-process-parity");
            return result;
        }
        fixture.RestoreSeedForParity();
        await Run(fixture.Head); await Run(fixture.Head, reuse: true);
        File.Delete(fixture.Stamp); await Run(fixture.Head, reuse: true);
        fixture.RestoreSeedForParity();
        await Run(fixture.Head);
        var stamp = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Stamp))!.AsObject();
        stamp["fingerprint"] = new string('f', 64);
        await File.WriteAllTextAsync(fixture.Stamp, stamp.ToJsonString());
        var mismatch = await Run(fixture.Head, reuse: true);
        stamp["fingerprint"] = mismatch.Source.GetProperty("start").GetProperty("fingerprint").GetString();
        stamp["sourceState"] = "dirty";
        await File.WriteAllTextAsync(fixture.Stamp, stamp.ToJsonString());
        await Run(fixture.Head, reuse: true);
        fixture.RestoreSeedForParity();
        await Run(fixture.Head); await Run(fixture.Head, build: 37); await Run(fixture.Head, reuse: true);
        fixture.RestoreSeedForParity(); await Run(drift: "run");
        fixture.RestoreSeedForParity(); fixture.Write("tracked.txt", "first dirty value"); await Run(drift: "same-count-run");
        fixture.RestoreSeedForParity(); await Run(drift: "build");
        fixture.RestoreSeedForParity();
        var head = await Run(drift: "head-run");
        var actual = await CheckpointSourceScriptFixture.RunAsync("git", fixture.Repo, ["rev-parse", "HEAD"], null);
        actual.Exit.ShouldBe(0, "script-process-parity");
        head.Source.GetProperty("end").GetProperty("commit").GetString().ShouldBe(actual.Output.Trim(), "script-process-parity");
        fixture.RestoreSeedForParity(); fixture.Write("tracked.txt", "dirty before driver"); await Run(drift: "restore-run");
        fixture.RestoreSeedForParity(); (await Run(trx: "c585-failures.trx")).Exit.ShouldBe(1, "script-process-parity");
        fixture.RestoreSeedForParity(); (await Run(trx: "c585-zero.trx")).Exit.ShouldBe(3, "script-process-parity");
        fixture.RestoreSeedForParity(); (await Run(slot: true, busy: true)).Exit.ShouldBe(4, "script-process-parity");
        fixture.Calls.ShouldBe(0, "script-process-parity");
        var invalid = await fixture.InvokeAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "run-checkpoint.ps1"),
            ["-Name", "CP-2", "-Project", "sample", "-OutputPath", "bin-c835/", "-Filter", "/*/*/*/*", "-MinExecuted", "not-an-int"]);
        invalid.Exit.ShouldBe(1, "script-process-parity");
        invalid.Terminated.ShouldBeTrue("script-process-parity");
        // Error formatting differs by PSHost; compare its semantic parameter/type evidence in both modes.
        invalid.Stderr.ShouldContain("MinExecuted", Case.Sensitive, "script-process-parity");
        observations.Add($"{invalid.Exit}|{invalid.Terminated}");
        return observations.ToArray();
    }

    private static string Normalize(string value, CheckpointSourceScriptFixture fixture)
    {
        value = value.Replace(fixture.Root, "<root>", StringComparison.Ordinal).Replace("\\r", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
        value = Regex.Replace(value, @"results-\d+|round-\d+", "<request>");
        value = Regex.Replace(value, @"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)", "<time>");
        value = Regex.Replace(value, @"(?:waited|wait|held|build|startup|teardown|hostWall|checkpointWall|testsWall)=\d+(?:\.\d+)?s", "<elapsed>");
        value = Regex.Replace(value, @"(?:waitedSeconds|elapsedSeconds)\"":\d+(?:\.\d+)?", "elapsed\":0");
        return value;
    }

    [Test]
    public async Task Session_driver_resets_environment_and_scope()
    {
        using var fixture = new CheckpointSourceScriptFixture { SessionMode = true };
        var poison = Path.Combine(fixture.External, "poison.ps1");
        var probe = Path.Combine(fixture.External, "probe.ps1");
        await File.WriteAllTextAsync(poison, """
            param([switch]$Terminate)
            $env:C835_PROBE = 'changed'
            $env:C835_EMPTY = 'changed'
            $env:C835_ABSENT = 'changed'
            [Environment]::CurrentDirectory = [IO.Path]::GetTempPath()
            Set-Location ([IO.Path]::GetTempPath())
            $global:C835Leaked = 73
            function global:C835LeakedFunction { 42 }
            Set-Alias -Scope Global C835LeakedAlias Get-Item
            $global:LASTEXITCODE = 91
            $global:ErrorActionPreference = 'SilentlyContinue'
            if ($Terminate) { $ErrorActionPreference = 'Stop'; throw 'fixture-primary-failure' }
            exit 2
            """);
        await File.WriteAllTextAsync(probe, """
            [ordered]@{
                probe=$env:C835_PROBE; empty=$env:C835_EMPTY
                absent=[Environment]::GetEnvironmentVariables().Contains('C835_ABSENT')
                cwd=[Environment]::CurrentDirectory
                variable=[bool](Get-Variable C835Leaked -Scope Global -ErrorAction SilentlyContinue)
                function=[bool](Get-Command C835LeakedFunction -ErrorAction SilentlyContinue)
                alias=[bool](Get-Alias C835LeakedAlias -ErrorAction SilentlyContinue)
                last=$LASTEXITCODE; preference=[string]$ErrorActionPreference
            } | ConvertTo-Json -Compress
            """);
        foreach (var terminating in new[] { false, true })
        {
            var failed = await fixture.InvokeAsync(poison, terminating ? ["-Terminate"] : [],
                new Dictionary<string, string?> { ["C835_PROBE"] = "before", ["C835_EMPTY"] = "", ["C835_ABSENT"] = null });
            failed.Exit.ShouldBe(terminating ? 1 : 2, "script-next-exit-independent");
            var idle = await fixture.ProbeIdleAsync();
            idle.GetProperty("cwd").GetString().ShouldBe(fixture.Repo, "script-environment-reset");
            idle.GetProperty("probe").ValueKind.ShouldBe(JsonValueKind.Null, "script-environment-reset");
            idle.GetProperty("absent").GetBoolean().ShouldBeFalse("script-environment-reset");
            var next = await fixture.InvokeAsync(probe, environment: new Dictionary<string, string?> { ["C835_PROBE"] = "before", ["C835_EMPTY"] = "", ["C835_ABSENT"] = null });
            next.Exit.ShouldBe(0, "script-next-exit-independent");
            using var json = JsonDocument.Parse(next.Stdout);
            json.RootElement.GetProperty("probe").GetString().ShouldBe("before", "script-environment-reset");
            json.RootElement.GetProperty("empty").GetString().ShouldBe("", "script-environment-reset");
            json.RootElement.GetProperty("absent").GetBoolean().ShouldBeFalse("script-environment-reset");
            foreach (var key in new[] { "variable", "function", "alias" })
                json.RootElement.GetProperty(key).GetBoolean().ShouldBeFalse("script-scope-reset");
            json.RootElement.GetProperty("last").ValueKind.ShouldBe(JsonValueKind.Null, "script-scope-reset");
            json.RootElement.GetProperty("preference").GetString().ShouldBe("Continue", "script-scope-reset");
        }
    }

    [Test]
    public async Task Script_family_restores_stamp_head_and_counters()
    {
        using var fixture = new CheckpointSourceScriptFixture { SessionMode = true };
        var first = await fixture.RunAsync(expectedSha: fixture.Head);
        first.Exit.ShouldBe(0, first.Output);
        var retained = File.ReadAllBytes(first.Evidence);
        fixture.Write("tracked.txt", "poison");
        (await CheckpointSourceScriptFixture.RunAsync("git", fixture.Repo, ["add", "tracked.txt"], null)).Exit.ShouldBe(0);
        (await CheckpointSourceScriptFixture.RunAsync("git", fixture.Repo, ["commit", "-qm", "poison"], null)).Exit.ShouldBe(0);
        fixture.Write("extra.txt", "poison");
        await File.WriteAllTextAsync(Path.Combine(fixture.External, "slot-calls.txt"), "poison\n");
        fixture.ResetScenario();
        fixture.Calls.ShouldBe(0, "script-scenario-counters-reset");
        fixture.SlotCalls.ShouldBe(0, "script-scenario-counters-reset");
        File.Exists(fixture.Stamp).ShouldBeFalse("script-fixture-reset");
        var actual = await CheckpointSourceScriptFixture.RunAsync("git", fixture.Repo, ["rev-parse", "HEAD"], null);
        actual.Exit.ShouldBe(0, "script-fixture-reset");
        actual.Output.Trim().ShouldBe(fixture.Head, "script-fixture-reset");
        (await CheckpointSourceScriptFixture.RunAsync("git", fixture.Repo, ["status", "--porcelain"], null)).Output.ShouldBe("", "script-fixture-reset");
        File.ReadAllText(Path.Combine(fixture.Repo, "tracked.txt")).ShouldBe("seed", "script-fixture-reset");
        var next = await fixture.RunAsync(expectedSha: fixture.Head);
        next.Exit.ShouldBe(0, next.Output);
        next.Evidence.ShouldNotBe(first.Evidence, "script-results-distinct");
        File.ReadAllBytes(first.Evidence).ShouldBe(retained, "script-results-distinct");
    }

    [Test]
    public async Task Session_driver_joins_children_on_failure()
    {
        foreach (var workerFailure in new[] { false, true })
        {
            var fixture = new CheckpointSourceScriptFixture { SessionMode = true };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var cancel = new CancellationTokenSource();
            var script = Path.Combine(fixture.External, "child.ps1");
            var ready = Path.Combine(fixture.External, "child-ready");
            await File.WriteAllTextAsync(script, """
                param([string]$Ready)
                $start = [Diagnostics.ProcessStartInfo]::new('pwsh')
                $start.UseShellExecute = $false
                $start.RedirectStandardInput = $true
                foreach ($arg in @('-NoProfile','-NonInteractive','-Command','[Console]::In.ReadLine() | Out-Null')) { $start.ArgumentList.Add($arg) }
                $child = [Diagnostics.Process]::Start($start)
                $identity = [string]$child.StartTime.ToUniversalTime().Ticks
                if ($IsLinux) {
                    $stat = [IO.File]::ReadAllText('/proc/' + $child.Id + '/stat')
                    $identity = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[19]
                }
                [IO.File]::WriteAllText(($Ready + '.tmp'), ([string]$child.Id + '|' + $identity))
                [IO.File]::Move(($Ready + '.tmp'), $Ready)
                $child.WaitForExit()
                """);
            var readySignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(fixture.External) { EnableRaisingEvents = true };
            watcher.Renamed += (_, e) => { if (e.FullPath == ready) readySignal.TrySetResult(); };
            watcher.Created += (_, e) => { if (e.FullPath == ready) readySignal.TrySetResult(); };
            var disposalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var joinedAtDeletion = false;
            fixture.DisposalStarted = () => disposalStarted.TrySetResult();
            fixture.BeforeInvocationCompletion = async () => { finishing.TrySetResult(); await releaseFinish.Task; };
            using var sentinel = new Process { StartInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false,
            } };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::In.ReadLine() | Out-Null" }) sentinel.StartInfo.ArgumentList.Add(arg);
            sentinel.Start();
            var sentinelOut = sentinel.StandardOutput.ReadToEndAsync();
            var sentinelError = sentinel.StandardError.ReadToEndAsync();
            Process? child = null;
            Task<CheckpointSourceScriptFixture.DriverResult>? running = null;
            Task? disposal = null;
            try
            {
                running = fixture.InvokeAsync(script, ["-Ready", ready], cancellationToken: cancel.Token);
                await readySignal.Task.WaitAsync(deadline.Token);
                var identity = (await File.ReadAllTextAsync(ready, deadline.Token)).Split('|');
                child = Process.GetProcessById(int.Parse(identity[0]));
                var observedIdentity = child.StartTime.ToUniversalTime().Ticks.ToString();
                if (OperatingSystem.IsLinux())
                {
                    var stat = await File.ReadAllTextAsync($"/proc/{child.Id}/stat", deadline.Token);
                    observedIdentity = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[19];
                }
                observedIdentity.ShouldBe(identity[1], "script-owned-children-joined identity");
                fixture.BeforeRootDeletion = () => joinedAtDeletion = child.HasExited && running.IsCompleted;
                if (workerFailure) fixture.FailActiveInvocation(); else cancel.Cancel();
                await finishing.Task.WaitAsync(deadline.Token);
                disposal = Task.Run(fixture.Dispose);
                await disposalStarted.Task.WaitAsync(deadline.Token);
                Directory.Exists(fixture.Root).ShouldBeTrue("script-owned-children-joined root survives until completion");
                releaseFinish.TrySetResult();
                if (workerFailure)
                    (await Should.ThrowAsync<IOException>(async () => await running, "script-primary-failure-preserved")).Message.ShouldContain("fixture-primary-failure");
                else await Should.ThrowAsync<OperationCanceledException>(async () => await running, "script-primary-failure-preserved");
                await disposal.WaitAsync(deadline.Token);
                joinedAtDeletion.ShouldBeTrue("script-owned-children-joined");
                sentinel.HasExited.ShouldBeFalse("script-owned-children-joined unrelated sentinel remains alive");
            }
            finally
            {
                releaseFinish.TrySetResult();
                cancel.Cancel();
                if (running is not null) { try { await running; } catch (Exception) { } }
                if (disposal is not null) await disposal;
                if (child is { HasExited: false }) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                child?.Dispose();
                if (!sentinel.HasExited) { sentinel.Kill(); await sentinel.WaitForExitAsync(); }
                await Task.WhenAll(sentinelOut, sentinelError);
                fixture.Dispose();
            }
        }
    }
}
