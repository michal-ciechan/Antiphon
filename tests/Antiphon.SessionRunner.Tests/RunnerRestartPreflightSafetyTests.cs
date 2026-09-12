using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerRestartPreflightSafetyTests
{
    [Test]
    [Arguments("pwsh.exe")]
    [Arguments("powershell.exe")]
    public async Task Same_content_in_two_roots_validates_once_and_executes_twice(string shell)
    {
        var cache = new RestartPreflightCache();
        using var a = new RestartFixture(cache);
        using var b = new RestartFixture(cache);
        var healthy = await a.Run(shell);
        healthy.Outcome.ShouldBe("healthy");
        a.ValidatorChildStarts.ShouldBe(1);
        a.ExecutionChildStarts.ShouldBe(1);
        b.Config["healthyAt"] = 999999;
        var expired = await b.Run(shell, "-WaitOnly", "-TimeoutSec", "1");
        expired.Outcome.ShouldBe("wait-expired");
        expired.Exit.ShouldBe(2);
        b.ValidatorChildStarts.ShouldBe(0);
        b.ExecutionChildStarts.ShouldBe(1);
        using var preflight = JsonDocument.Parse(File.ReadAllText(Path.Combine(b.EvidenceRoot, "preflight-1.json")));
        preflight.RootElement.GetProperty("cached").GetBoolean().ShouldBeTrue();
        preflight.RootElement.GetProperty("validatorChildStarted").GetBoolean().ShouldBeFalse();
        preflight.RootElement.GetProperty("executionChildStarted").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task Each_shell_misses_once_then_hits()
    {
        var cache = new RestartPreflightCache();
        using var f = new RestartFixture(cache);
        await f.Run("pwsh.exe");
        f.ValidatorChildStarts.ShouldBe(1);
        f.ExecutionChildStarts.ShouldBe(1);
        await f.Run("pwsh.exe");
        f.ValidatorChildStarts.ShouldBe(1);
        f.ExecutionChildStarts.ShouldBe(2);
        await f.Run("powershell.exe");
        f.ValidatorChildStarts.ShouldBe(2);
        f.ExecutionChildStarts.ShouldBe(3);
        await f.Run("powershell.exe");
        f.ValidatorChildStarts.ShouldBe(2);
        f.ExecutionChildStarts.ShouldBe(4);
        var pwsh = new ShellIdentityResolver().Resolve("pwsh.exe");
        var powershell = new ShellIdentityResolver().Resolve("powershell.exe");
        pwsh.NormalizedPath.ShouldNotBe(powershell.NormalizedPath);
        pwsh.ExeSha256.ShouldNotBe(powershell.ExeSha256);
        pwsh.Version.ShouldNotBe(powershell.Version);
    }

    [Test]
    [Arguments("pwsh.exe")]
    [Arguments("powershell.exe")]
    public async Task Resolved_shell_is_the_process_that_runs(string shell)
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        var identity = new ShellIdentityResolver().Resolve(shell);
        var result = await f.Script("""
            $p=(Get-Process -Id $PID).Path
            Write-Output $p
            Write-Output ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($p).FileVersion)
            Write-Output ($PSVersionTable.PSVersion.ToString())
            """
            , shell);
        result.Exit.ShouldBe(0, result.Output);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0].ShouldBe(identity.NormalizedPath);
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(identity.NormalizedPath))).ShouldBe(identity.ExeSha256);
        if (shell == "pwsh.exe")
        {
            var adjacent = Path.Combine(Path.GetDirectoryName(identity.NormalizedPath)!, "System.Management.Automation.dll");
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adjacent))).ShouldBe(identity.EngineSha256);
        }
        else
        {
            var gac = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll");
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gac))).ShouldBe(identity.EngineSha256);
        }

        lines[1].ShouldBe(identity.Version);
    }

    [Test]
    [Arguments("pwsh.exe", "forbidden-command")]
    [Arguments("pwsh.exe", "member-call")]
    [Arguments("pwsh.exe", "dynamic-command")]
    [Arguments("powershell.exe", "forbidden-command")]
    [Arguments("powershell.exe", "member-call")]
    [Arguments("powershell.exe", "dynamic-command")]
    public async Task Warm_cache_rejects_a_tampered_entry_before_any_child(string shell, string tamper)
    {
        var cache = new RestartPreflightCache();
        using var f = new RestartFixture(cache);
        var owned = Path.Combine(f.Root, "owned.txt");
        await File.WriteAllTextAsync(owned, "keep");
        await f.Run(shell);
        var trace = File.Exists(Path.Combine(f.Root, "trace.jsonl")) ? File.ReadAllBytes(Path.Combine(f.Root, "trace.jsonl")) : [];
        var state = File.ReadAllText(Path.Combine(f.Root, "state"));
        var pid = File.ReadAllText(Path.Combine(f.Root, "pid"));
        var quoted = RestartFixture.Quote(owned);
        var extra = tamper switch
        {
            "forbidden-command" => $"\nRemove-Item -LiteralPath '{quoted}'\n",
            "member-call" => $"\n[System.IO.File]::Delete('{quoted}')\n",
            _ => $"\n$c='Remove-Item'; & $c -LiteralPath '{quoted}'\n"
        };
        File.AppendAllText(f.CopiedEntry, extra);
        var ex = await Should.ThrowAsync<Exception>(() => f.Run(shell));
        if (tamper == "forbidden-command")
            ex.Message.ShouldContain("Remove-Item");
        else if (tamper == "member-call")
            ex.Message.ShouldContain("member call");
        else
            ex.Message.ShouldContain("dynamic command");
        f.ExecutionChildStarts.ShouldBe(1);
        f.ValidatorChildStarts.ShouldBe(2);
        File.Exists(owned).ShouldBeTrue();
        var after = File.Exists(Path.Combine(f.Root, "trace.jsonl")) ? File.ReadAllBytes(Path.Combine(f.Root, "trace.jsonl")) : [];
        after.ShouldBe(trace);
        File.ReadAllText(Path.Combine(f.Root, "state")).ShouldBe(state);
        File.ReadAllText(Path.Combine(f.Root, "pid")).ShouldBe(pid);
    }

    [Test]
    [Arguments("pwsh.exe", "parser-error")]
    [Arguments("pwsh.exe", "non-inert-helper")]
    [Arguments("pwsh.exe", "missing-core")]
    [Arguments("pwsh.exe", "core-bypass")]
    [Arguments("powershell.exe", "parser-error")]
    [Arguments("powershell.exe", "non-inert-helper")]
    [Arguments("powershell.exe", "missing-core")]
    [Arguments("powershell.exe", "core-bypass")]
    public async Task Malformed_syntax_non_inert_helper_missing_core_and_core_bypass_are_rejected(string shell, string shape)
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        var owned = Path.Combine(f.Root, "owned.txt");
        switch (shape)
        {
            case "parser-error":
                File.AppendAllText(f.CopiedEntry, "\nif (\n");
                break;
            case "non-inert-helper":
                File.AppendAllText(f.CopiedHelper, $"\nSet-Content -LiteralPath '{RestartFixture.Quote(owned)}' -Value x\n");
                break;
            case "missing-core":
                File.WriteAllText(f.CopiedHelper, File.ReadAllText(f.CopiedHelper).Replace("function Invoke-RunnerRestart", "function Invoke-RunnerRestartX"));
                break;
            default:
                InsertIntoCoreBody(f.CopiedHelper, "Get-Random | Out-Null\n");
                break;
        }

        var ex = await Should.ThrowAsync<Exception>(() => f.Run(shell));
        if (shape == "parser-error")
            ex.Message.ShouldContain("parser");
        else if (shape == "non-inert-helper")
            ex.Message.ShouldContain("not inert");
        else if (shape == "missing-core")
            ex.Message.ShouldContain("Invoke-RunnerRestart");
        else
            ex.Message.ShouldContain("core bypassed platform: Get-Random");
        f.ExecutionChildStarts.ShouldBe(0);
        if (shape == "non-inert-helper")
            File.Exists(owned).ShouldBeFalse();
    }

    [Test]
    [Arguments("wrapper-absolute-import")]
    [Arguments("wrapper-extra-line")]
    [Arguments("platform-missing")]
    [Arguments("helper-missing")]
    public async Task Wrapper_tampering_and_missing_inputs_refuse_before_any_child(string shape)
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        switch (shape)
        {
            case "wrapper-absolute-import":
                File.WriteAllText(f.WrapperPath, ". '" + RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1")) + "'\n");
                break;
            case "wrapper-extra-line":
                File.WriteAllBytes(f.WrapperPath, RestartAstValidator.WrapperBytes.Concat("\n"u8.ToArray()).ToArray());
                break;
            case "platform-missing":
                File.Delete(f.CopiedPlatform);
                break;
            default:
                File.Delete(f.CopiedHelper);
                break;
        }

        var ex = await Should.ThrowAsync<Exception>(() => f.Run());
        if (shape.StartsWith("wrapper", StringComparison.Ordinal))
            ex.Message.ShouldContain("wrapper contract");
        else
            ex.Message.ShouldContain("missing");
        f.ValidatorChildStarts.ShouldBe(0);
        f.ExecutionChildStarts.ShouldBe(0);
    }

    [Test]
    [Arguments("pwsh.exe")]
    [Arguments("powershell.exe")]
    [Arguments("pre-held-write-handle")]
    public async Task Bound_inputs_deny_writes_until_the_child_exits(string shape)
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        if (shape == "pre-held-write-handle")
        {
            using var hold = new FileStream(f.CopiedEntry, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Should.ThrowAsync<PreflightRefusalException>(() => f.Run("pwsh.exe"));
            f.ValidatorChildStarts.ShouldBe(0);
            f.ExecutionChildStarts.ShouldBe(0);
            return;
        }

        IOException? write = null, delete = null, move = null;
        f.OnBeforeExecution = () =>
        {
            write = Should.Throw<IOException>(() => File.WriteAllText(f.CopiedEntry, "tamper"));
            delete = Should.Throw<IOException>(() => File.Delete(f.CopiedPlatform));
            move = Should.Throw<IOException>(() => File.Move(f.WrapperPath, f.WrapperPath + ".moved"));
        };
        var result = await f.Run(shape);
        result.Outcome.ShouldBe("healthy");
        write.ShouldNotBeNull();
        delete.ShouldNotBeNull();
        move.ShouldNotBeNull();
        File.WriteAllText(f.CopiedEntry, File.ReadAllText(f.CopiedEntry));
        File.Copy(f.CopiedPlatform, f.CopiedPlatform + ".bak", true);
        File.Move(f.WrapperPath, f.WrapperPath + ".moved");
        File.Move(f.WrapperPath + ".moved", f.WrapperPath);
    }

    [Test]
    [Arguments("pwsh.exe")]
    [Arguments("powershell.exe")]
    public async Task Safe_same_length_edit_with_restored_timestamp_revalidates(string shell)
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        (await f.Run(shell)).Outcome.ShouldBe("healthy");
        var bytes = File.ReadAllBytes(f.CopiedEntry);
        var text = Encoding.UTF8.GetString(bytes);
        var idx = text.IndexOf("Restart the runner", StringComparison.Ordinal);
        idx.ShouldBeGreaterThan(0);
        var edited = text.Remove(idx, 1).Insert(idx, "S");
        edited.Length.ShouldBe(text.Length);
        var stamp = File.GetLastWriteTimeUtc(f.CopiedEntry);
        File.WriteAllText(f.CopiedEntry, edited, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.SetLastWriteTimeUtc(f.CopiedEntry, stamp);
        (await f.Run(shell)).Outcome.ShouldBe("healthy");
        f.ValidatorChildStarts.ShouldBe(2);
        f.ExecutionChildStarts.ShouldBe(2);
    }

    [Test]
    public async Task Fixture_executes_its_copied_helper_not_the_repository()
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        var repoHelper = Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1");
        var before = File.ReadAllBytes(repoHelper);
        var marker = Path.Combine(f.Root, "snapshot-marker");
        InsertIntoCoreBody(
            f.CopiedHelper,
            $"[System.IO.File]::WriteAllText('{RestartFixture.Quote(marker)}','x')\n");
        (await f.Run()).Outcome.ShouldBe("healthy");
        File.Exists(marker).ShouldBeTrue();
        File.ReadAllBytes(repoHelper).ShouldBe(before);
    }

    [Test]
    public async Task Script_and_decoder_launch_a_child_every_call()
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        await f.Run();
        var validator = f.ValidatorChildStarts;
        var execution = f.ExecutionChildStarts;
        (await f.Script("exit 0")).Exit.ShouldBe(0);
        (await f.Script("exit 0")).Exit.ShouldBe(0);
        (await f.Script("exit 0")).Exit.ShouldBe(0);
        var line = "ANTIPHON_STARTUP {\"version\":1,\"event\":\"build-start\",\"utc\":\"2026-09-07T10:00:00.0000000Z\",\"producer\":\"supervisor\",\"producerPid\":900002,\"producerStartTimeUtc\":\"2026-09-07T09:58:00.0000000Z\",\"attemptId\":\"attempt-one\",\"exitCode\":0}\n";
        await f.DecodeCapturedMilestones(line, 900002, "2026-09-07T09:58:00.0000000Z", "supervisor", "build-start");
        await f.DecodeCapturedMilestones(line, 900002, "2026-09-07T09:58:00.0000000Z", "supervisor", "build-start");
        f.ScriptChildStarts.ShouldBe(5);
        f.ValidatorChildStarts.ShouldBe(validator);
        f.ExecutionChildStarts.ShouldBe(execution);
    }

    [Test]
    public async Task Hung_child_is_killed_at_the_fixture_deadline()
    {
        using var f = new RestartFixture(new RestartPreflightCache());
        f.ChildTimeout.ShouldBe(TimeSpan.FromSeconds(45));
        f.ChildTimeout = TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        await Should.ThrowAsync<TimeoutException>(() => f.Script("Start-Sleep 10"));
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
        Should.Throw<ArgumentException>(() => Process.GetProcessById(f.LastChildPid));
    }

    private static void InsertIntoCoreBody(string helperPath, string insertion)
    {
        var text = File.ReadAllText(helperPath);
        var idx = text.IndexOf("function Invoke-RunnerRestart", StringComparison.Ordinal);
        idx.ShouldBeGreaterThanOrEqualTo(0);
        var brace = text.IndexOf('{', idx);
        brace.ShouldBeGreaterThan(idx);
        File.WriteAllText(helperPath, text.Insert(brace + 1, "\n" + insertion));
    }
}
