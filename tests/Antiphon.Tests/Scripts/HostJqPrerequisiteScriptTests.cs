using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class HostJqPrerequisiteScriptTests
{
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Check_qualifies_deployment_shell()
    {
        using (var f = new HostJqFixture())
        {
            f.Existing();
            var r = await f.Helper("check"); r.Exit.ShouldBe(0, r.Output);
            var p = JsonNode.Parse(r.Stdout)!;
            p["path"]!.GetValue<string>().ShouldBe(f.Destination, "deployment-path");
            p["version"]!.GetValue<string>().ShouldBe((await f.NativeJqVersion()).Trim());
            p["digest"]!.GetValue<string>().ShouldBe(f.Hash(f.Destination));
            p["uid"]!.GetValue<int>().ShouldBe(0); p["gid"]!.GetValue<int>().ShouldBe(0);
            p["permissions"]!.GetValue<string>().ShouldBe("755");
            p["trueExit"]!.GetValue<int>().ShouldBe(0); p["falseExit"]!.GetValue<int>().ShouldBe(1);
            f.Existing("version-other"); (await f.Helper("check")).Exit.ShouldBe(0, "existing jq need not match pin");
        }
        foreach (var fault in new[] { "missing", "nonexec", "version-exit", "version-empty", "true", "false" })
        {
            using var f = new HostJqFixture();
            if (fault != "missing") f.Existing(fault);
            var r = await f.Helper("check"); r.Exit.ShouldBe(2, fault + r.Output);
            r.Output.ShouldContain(fault == "missing" ? "HostJqMissing" : "HostJqInvalid",
                Case.Sensitive, fault == "false" ? "false-probe-refused" : fault == "true" ? "true-probe-refused" : "version-refused");
            r.Stdout.ShouldBeEmpty("success receipts=0");
        }
        using var shadow = new HostJqFixture(); shadow.Existing("true", elsewhere: true);
        (await shadow.Helper("check")).Exit.ShouldBe(2, "deployment-path: healthy privileged path does not override shadow");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Check_has_no_install_effects()
    {
        foreach (var state in new[] { "healthy", "missing", "true" })
        {
            using var f = new HostJqFixture(); if (state != "missing") f.Existing(state == "healthy" ? "healthy" : state);
            await f.InitializeRepo();
            var before = f.Sentinels();
            var direct = await f.Helper("check");
            direct.Exit.ShouldBe(state == "healthy" ? 0 : 2, direct.Output);
            var wrapper = await f.Wrapper("check-host-jq");
            wrapper.Exit.ShouldBe(state == "healthy" ? 0 : 2, wrapper.Output);
            f.InstallEffects.ShouldBeEmpty("check-read-only: sudo/download/install/publish/unlink calls=0");
            f.Sentinels().ShouldBe(before, "read-only foreign filesystem sentinels");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_requires_missing_jq()
    {
        foreach (var elsewhere in new[] { false, true })
        {
            using var f = new HostJqFixture(); f.Existing(elsewhere: elsewhere);
            var path = elsewhere ? f.OtherJq : f.Destination; var bytes = File.ReadAllBytes(path); var inode = await f.Inode(path);
            var r = await f.Helper("provision"); r.Exit.ShouldBe(0, r.Output);
            JsonNode.Parse(r.Stdout)!["installed"]!.GetValue<bool>().ShouldBeFalse("existing-no-op");
            f.InstallEffects.ShouldBeEmpty("existing-no-op: download calls=0");
            File.ReadAllBytes(path).ShouldBe(bytes); (await f.Inode(path)).ShouldBe(inode);
        }
        using (var f = new HostJqFixture())
        {
            var first = await f.Helper("provision"); first.Exit.ShouldBe(0, first.Output);
            JsonNode.Parse(first.Stdout)!["installed"]!.GetValue<bool>().ShouldBeTrue();
            var inode = await f.Inode(f.Destination); var bytes = File.ReadAllBytes(f.Destination); f.ClearTrace();
            var second = await f.Helper("provision"); second.Exit.ShouldBe(0, second.Output);
            JsonNode.Parse(second.Stdout)!["installed"]!.GetValue<bool>().ShouldBeFalse();
            f.InstallEffects.ShouldBeEmpty("existing-no-op"); (await f.Inode(f.Destination)).ShouldBe(inode);
            File.ReadAllBytes(f.Destination).ShouldBe(bytes);
        }
        foreach (var fault in new[] { "true", "nonexec", "version-exit" })
        {
            using var f = new HostJqFixture(); f.Existing(fault);
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, r.Output);
            f.Trace.ShouldNotContain("download", Case.Sensitive, "invalid-not-missing");
            f.Trace.ShouldNotContain("publish", Case.Sensitive, "invalid-not-missing");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_admits_host_prerequisites()
    {
        foreach (var args in new[] { Array.Empty<string>(), new[] { "bogus" }, new[] { "check", "extra" } })
        {
            using var f = new HostJqFixture(); var r = await f.Helper(args);
            r.Exit.ShouldBe(2, "mode-refused"); f.InstallEffects.ShouldBeEmpty("mode-refused: install calls=0");
        }
        foreach (var fault in new[] { "nested", "sibling", "no-daemon", "hostname", "os", "arch", "sudo" })
        {
            using var f = new HostJqFixture(); f.Fault(fault);
            var label = fault switch { "os" => "os-refused", "arch" => "arch-refused", "sudo" => "privilege-refused", _ => "lane-refused" };
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, label + ": " + fault + r.Output);
            r.Stdout.ShouldBeEmpty("lane-refused: success receipts=0"); f.Trace.ShouldNotContain("download");
        }
        foreach (var tool in HostJqFixture.ProvisionTools)
        {
            using var f = new HostJqFixture(); File.Delete(Path.Combine(f.Tools, tool));
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, r.Output);
            r.Output.ShouldContain("HostJqToolMissing:" + tool, Case.Sensitive, "tools-refused");
            f.Trace.ShouldNotContain("download", Case.Sensitive, "tools-refused: before transfer");
        }
        using var check = new HostJqFixture(); check.Existing();
        foreach (var tool in new[] { "curl", "install", "mktemp", "flock", "ln", "rm", "uname", "sudo" }) File.Delete(Path.Combine(check.Tools, tool));
        (await check.Helper("check")).Exit.ShouldBe(0, "check does not need provision-only tools");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_refuses_unsafe_destination()
    {
        foreach (var vector in new[] { "file", "symlink", "dangling", "directory", "fifo", "parent-link", "parent-stat", "parent-write" })
        {
            // Reach destination admission with missing jq; lookup must not mask this guard.
            using var f = new HostJqFixture { IncludeDestination = false };
            switch (vector)
            {
                case "file": File.WriteAllText(f.Destination, "foreign"); break;
                case "symlink": File.CreateSymbolicLink(f.Destination, f.Foreign); break;
                case "dangling": File.CreateSymbolicLink(f.Destination, f.Root + "/absent"); break;
                case "directory": Directory.CreateDirectory(f.Destination); break;
                case "fifo": (await f.Run("/usr/bin/mkfifo", f.Destination)).Exit.ShouldBe(0); break;
                case "parent-link": Directory.Delete(f.Parent); Directory.CreateSymbolicLink(f.Parent, f.ForeignDirectory); break;
                default: f.Fault(vector); break;
            }
            var sentinel = f.Sentinels();
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, "destination/parent-refused: " + vector + r.Output);
            f.Trace.ShouldNotContain("download", Case.Sensitive, "destination-refused");
            f.Trace.ShouldNotContain("publish"); f.Sentinels().ShouldBe(sentinel, "foreign inode/hash unchanged");
        }
        using var good = new HostJqFixture(); (await good.Helper("provision")).Exit.ShouldBe(0, "absent destination accepted control");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_serializes_and_rechecks()
    {
        using (var f = new HostJqFixture())
        {
            using var held = f.Start("/usr/bin/bash", "-c", "exec 9<\"$HJ_LOCK\"; /usr/bin/flock 9; echo ready; read -r release");
            (await held.StandardOutput.ReadLineAsync()).ShouldBe("ready");
            var a = f.Helper("provision"); var b = f.Helper("provision");
            try
            {
                await f.WaitTrace("flock-request", 2);
                f.Trace.ShouldNotContain("download", Case.Sensitive, "lock-held");
            }
            finally { held.StandardInput.WriteLine("release"); held.StandardInput.Close(); await held.WaitForExitAsync(); }
            var results = await Task.WhenAll(a, b);
            foreach (var r in results) r.Exit.ShouldBe(0, r.Output);
            f.Trace.Split('\n').Count(x => x.StartsWith("download ", StringComparison.Ordinal)).ShouldBe(1, "lock-recheck: total download calls=1");
            results.Count(x => JsonNode.Parse(x.Stdout)!["installed"]!.GetValue<bool>()).ShouldBe(1, "lock-recheck: second outcome=no-op");
        }
        using (var f = new HostJqFixture())
        {
            using var held = f.Start("/usr/bin/bash", "-c", "exec 9<\"$HJ_LOCK\"; /usr/bin/flock 9; echo ready; read -r release");
            (await held.StandardOutput.ReadLineAsync()).ShouldBe("ready");
            var pending = f.Helper("provision"); await f.WaitTrace("flock-request", 1); f.Existing("true");
            held.StandardInput.WriteLine("release"); held.StandardInput.Close(); await held.WaitForExitAsync();
            (await pending).Exit.ShouldBe(2, "lock-recheck: invalid destination appeared"); f.Trace.ShouldNotContain("download");
        }
        using var locked = new HostJqFixture(); locked.WriteTool("flock", "exit 1");
        (await locked.Helper("provision")).Exit.ShouldBe(2); locked.Trace.ShouldNotContain("download", Case.Sensitive, "lock refusal cannot continue");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_verifies_download_before_use()
    {
        using (var good = new HostJqFixture())
        {
            var r = await good.Helper("provision"); r.Exit.ShouldBe(0, r.Output);
            good.Trace.ShouldContain(HostJqFixture.Url); good.Trace.ShouldContain("download-mode 700", Case.Sensitive, "private-download");
            good.Trace.IndexOf("hash ", StringComparison.Ordinal).ShouldBeLessThan(good.Trace.IndexOf("stage ", StringComparison.Ordinal), "digest-before-use");
        }
        foreach (var fault in new[] { "curl-exit", "truncated", "digest", "hash-exit", "tamper" })
        {
            using var f = new HostJqFixture(); f.Fault(fault);
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, fault + r.Output);
            if (fault != "tamper") f.Trace.ShouldNotContain("stage ", Case.Sensitive, "digest-before-use: rejected download cannot reach staging");
            f.Trace.ShouldNotContain("publish", Case.Sensitive, fault == "curl-exit" ? "transfer-refused" : "digest-before-use");
            f.Trace.ShouldNotContain("jq-call", Case.Sensitive, "rejected artifact must not execute");
            File.Exists(f.Destination).ShouldBeFalse();
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_publishes_complete_no_clobber()
    {
        using (var f = new HostJqFixture())
        {
            f.Fault("publish-barrier"); var run = f.Helper("provision");
            await f.WaitMarker("publish-ready");
            File.Exists(f.Destination).ShouldBeFalse("atomic-visible: absent before publication");
            f.Trace.ShouldContain("stage-owner 0:0:755", Case.Sensitive, "stage-owner; stage-mode");
            var samples = new List<byte[]>();
            File.WriteAllText(f.Root + "/publish-release", "go");
            while (!run.IsCompleted)
            {
                if (File.Exists(f.Destination)) samples.Add(File.ReadAllBytes(f.Destination));
                await Task.Delay(1);
            }
            var result = await run; result.Exit.ShouldBe(0, result.Output);
            foreach (var sample in samples) sample.ShouldBe(File.ReadAllBytes(f.Payload), "atomic-visible: every reader sample is absent or complete verified bytes");
            File.ReadAllBytes(f.Destination).ShouldBe(File.ReadAllBytes(f.Payload), "atomic-visible: complete verified bytes");
        }
        using (var f = new HostJqFixture())
        {
            f.Fault("publish-barrier"); var run = f.Helper("provision"); await f.WaitMarker("publish-ready");
            File.WriteAllText(f.Destination, "racer foreign bytes"); var hash = f.Hash(f.Destination); var inode = await f.Inode(f.Destination);
            File.WriteAllText(f.Root + "/publish-release", "go");
            (await run).Exit.ShouldBe(2); f.Hash(f.Destination).ShouldBe(hash, "no-clobber"); (await f.Inode(f.Destination)).ShouldBe(inode, "no-clobber");
        }
        foreach (var fault in new[] { "stage-exit", "publish-exit", "stage-owner", "stage-mode" })
        {
            using var f = new HostJqFixture(); f.Fault(fault);
            (await f.Helper("provision")).Exit.ShouldBe(2); File.Exists(f.Destination).ShouldBeFalse("stage/publish refusal");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_cleans_only_owned_staging()
    {
        foreach (var fault in new[] { "curl-exit", "digest", "stage-exit", "publish-exit", "final-true" })
        {
            using var f = new HostJqFixture(); f.Fault(fault); var sentinels = f.Sentinels();
            (await f.Helper("provision")).Exit.ShouldBe(2);
            Directory.GetDirectories(f.Temp).ShouldBeEmpty("cleanup-owned");
            Directory.GetDirectories(f.Parent, ".antiphon-jq.*").ShouldBeEmpty("cleanup-owned");
            f.Sentinels().ShouldBe(sentinels, "cleanup-custody: foreign sentinel original bytes");
            Directory.Exists(f.Lock).ShouldBeTrue("standing lock retained");
        }
        foreach (var signal in new[] { "TERM", "KILL" })
        {
            using var f = new HostJqFixture(); f.Fault("download-barrier");
            using var process = f.Start("/usr/bin/bash", f.HelperPath, "provision");
            var collecting = f.Collect(process); await f.WaitMarker("download-ready");
            (await f.Run("/usr/bin/kill", "-" + signal, process.Id.ToString())).Exit.ShouldBe(0);
            await collecting;
            if (signal == "TERM") Directory.GetDirectories(f.Temp).ShouldBeEmpty("cleanup-owned: handled signal");
            else Directory.GetDirectories(f.Temp).ShouldNotBeEmpty("hard-kill residue retained");
            File.Exists(f.Foreign).ShouldBeTrue("cleanup-custody");
            f.Fault(""); (await f.Helper("provision")).Exit.ShouldBe(0, "fresh call can acquire released lock after interruption");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_requalifies_published_jq()
    {
        foreach (var fault in new[] { "final-path", "final-version", "final-digest", "final-owner", "final-group", "final-mode", "final-true", "final-false", "path-absent" })
        {
            using var f = new HostJqFixture(); f.Fault(fault);
            var r = await f.Helper("provision"); r.Exit.ShouldBe(2, fault + r.Output);
            r.Stdout.ShouldBeEmpty(fault + ": success receipts=0"); File.Exists(f.Destination).ShouldBeTrue("published file left for diagnosis");
            f.Trace.Split('\n').Count(x => x.StartsWith("download ", StringComparison.Ordinal)).ShouldBe(1, "never retry/reinstall");
        }
        using var good = new HostJqFixture(); var accepted = await good.Helper("provision"); accepted.Exit.ShouldBe(0, accepted.Output);
        var proof = JsonNode.Parse(accepted.Stdout)!;
        proof["installed"]!.GetValue<bool>().ShouldBeTrue(); proof["digest"]!.GetValue<string>().ShouldBe(HostJqFixture.Pin);
        proof["version"]!.GetValue<string>().ShouldBe("jq-1.7.1");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Provision_requires_canonical_source()
    {
        using (var good = new HostJqFixture())
        {
            good.Existing(); await good.InitializeRepo();
            (await good.Wrapper("provision-host-jq")).Exit.ShouldBe(0, "canonical-source: clean control");
        }
        foreach (var fault in new[] { "staged", "unstaged", "untracked", "sha", "git", "worktree" })
        {
            using var f = new HostJqFixture(); f.Existing(); await f.InitializeRepo();
            switch (fault)
            {
                case "staged": File.AppendAllText(f.HelperPath, "\n# dirt\n"); await f.Git("add", "scripts/server2-host-jq.sh"); break;
                case "unstaged": File.AppendAllText(f.HelperPath, "\n# dirt\n"); break;
                case "untracked": File.WriteAllText(f.Root + "/untracked", "dirt"); break;
                case "sha": f.RequestSha = new string('a', 40); break;
                case "git": f.WriteTool("git", "exit 1"); break;
                case "worktree":
                    var linked = f.Root + "-linked"; f.Linked = linked;
                    (await f.Git("worktree", "add", "-b", "fixture", linked)).Exit.ShouldBe(0);
                    f.WrapperRoot = linked; break;
            }
            var r = await f.Wrapper("provision-host-jq"); r.Exit.ShouldBe(2, fault + r.Output);
            r.Output.ShouldContain("HostJqCanonicalSourceRequired", Case.Sensitive, fault == "sha" ? "source-sha" : "clean-source/canonical-source");
            f.Trace.ShouldNotContain("ssh", Case.Sensitive, "source admission SSH calls=0");
            if (fault == "worktree") (await f.Wrapper("check-host-jq")).Exit.ShouldBe(0, "read-only check usable from worktree");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Transport_is_bounded_and_reaped()
    {
        foreach (var fault in new[] { "", "ssh-exit", "empty", "truncated", "late", "start" })
        {
            using var f = new HostJqFixture(); f.Existing(); f.Transport = fault; await f.InitializeRepo(shortDeadline: fault == "late");
            Process? observer = null;
            var execution = f.Wrapper("check-host-jq");
            try
            {
                if (fault == "late")
                {
                    await f.WaitMarker("ssh-pid");
                    observer = Process.GetProcessById(int.Parse(File.ReadAllText(f.Root + "/ssh-pid")));
                    observer.StartTime.ToUniversalTime().ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-10), "owned child start identity");
                }
                var r = await execution;
                r.Exit.ShouldBe(fault == "" ? 0 : 2, "ssh-exit/ssh-deadline: " + fault + r.Output);
                if (observer is not null) observer.HasExited.ShouldBeTrue("ssh-reaped: independent child handle exited before wrapper return");
            if (fault == "late") r.Output.ShouldContain("HostJqTransportTimeout", Case.Sensitive, "ssh-deadline");
            if (fault != "start")
            {
                f.Trace.ShouldContain("BatchMode=yes"); f.Trace.ShouldContain("ConnectTimeout=15");
                f.Trace.ShouldContain("bash -s -- check"); f.Trace.ShouldNotContain("bash -l");
                File.ReadAllText(f.Root + "/stdin").ShouldBe(File.ReadAllText(f.HelperPath), "streamed reviewed helper");
                var pid = int.Parse(File.ReadAllText(f.Root + "/ssh-pid"));
                HostJqFixture.Alive(pid).ShouldBeFalse("ssh-reaped: recorded child exited when wrapper returned");
            }
            }
            finally
            {
                if (observer is not null)
                {
                    if (!observer.HasExited) observer.Kill(entireProcessTree: true);
                    await observer.WaitForExitAsync(); observer.Dispose();
                }
                await execution;
            }
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Receipt_requires_complete_current_proof()
    {
        using (var good = new HostJqFixture())
        {
            good.Existing(); await good.InitializeRepo(); var start = DateTime.UtcNow;
            var run = await good.Wrapper("check-host-jq"); run.Exit.ShouldBe(0, run.Output);
            good.AssertReceipt("check-host-jq", "check", start, DateTime.UtcNow, installed: false);
        }
        using (var install = new HostJqFixture())
        {
            await install.InitializeRepo(); var start = DateTime.UtcNow;
            var r = await install.Wrapper("provision-host-jq"); r.Exit.ShouldBe(0, r.Output);
            install.AssertReceipt("provision-host-jq", "provision", start, DateTime.UtcNow, installed: true);
        }
        using var template = new HostJqFixture(); template.Existing();
        var valid = JsonNode.Parse((await template.Helper("check")).Stdout)!.AsObject();
        var corruptions = new List<string> { "{", valid.ToJsonString() + valid.ToJsonString(), "[]", valid.ToJsonString().Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal) };
        foreach (var key in valid.Select(x => x.Key))
        {
            var missing = (JsonObject)valid.DeepClone(); missing.Remove(key); corruptions.Add(missing.ToJsonString());
            var wrong = (JsonObject)valid.DeepClone(); wrong[key] = new JsonArray(); corruptions.Add(wrong.ToJsonString());
        }
        foreach (var (key, value) in new (string, JsonNode)[] {
            ("schema", JsonValue.Create(2)!), ("lane", JsonValue.Create("nested")!), ("mode", JsonValue.Create("provision")!),
            ("trueExit", JsonValue.Create(1)!), ("falseExit", JsonValue.Create(0)!), ("installed", JsonValue.Create(true)!),
            ("outcome", JsonValue.Create("installed")!), ("path", JsonValue.Create("relative")!), ("digest", JsonValue.Create("bad")!),
            ("version", JsonValue.Create("")!), ("uid", JsonValue.Create(-1)!), ("gid", JsonValue.Create(-1)!), ("permissions", JsonValue.Create("888")!) })
        { var bad = (JsonObject)valid.DeepClone(); bad[key] = value.DeepClone(); corruptions.Add(bad.ToJsonString()); }
        foreach (var bad in corruptions)
        {
            using var f = new HostJqFixture(); f.SyntheticProof = bad; await f.InitializeRepo();
            var r = await f.Wrapper("check-host-jq"); r.Exit.ShouldBe(2, "proof-shape: " + bad + r.Output);
            f.Receipts.ShouldBeEmpty("proof-shape: no success receipt");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Receipt_persistence_is_required()
    {
        using var f = new HostJqFixture(); f.Existing(); f.Transport = "block-receipt"; await f.InitializeRepo();
        var r = await f.Wrapper("check-host-jq"); r.Exit.ShouldBe(2, r.Output);
        r.Output.ShouldContain("HostJqReceiptUnavailable", Case.Sensitive, "receipt-required");
        r.Output.ShouldNotContain("Host jq qualified", Case.Sensitive, "receipt-required: no success banner");
        f.Receipts.ShouldBeEmpty();
        f.Transport = ""; var start = DateTime.UtcNow;
        (await f.Wrapper("check-host-jq")).Exit.ShouldBe(0);
        f.AssertReceipt("check-host-jq", "check", start, DateTime.UtcNow, false);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Transport_preserves_secret_custody()
    {
        foreach (var fault in new[] { "", "ssh-exit", "truncated", "start" })
        {
            using var f = new HostJqFixture(); f.Existing(); f.Transport = fault; await f.InitializeRepo();
            var r = await f.Wrapper("check-host-jq"); r.Exit.ShouldBe(fault == "" ? 0 : 2, r.Output);
            foreach (var sentinel in new[] { "operator-C1025-secret", "task-C1025-secret", "remote-C1025-secret", "download-C1025-secret" })
            {
                r.Output.ShouldNotContain(sentinel, Case.Sensitive, "diagnostic-custody");
                f.Trace.ShouldNotContain(sentinel, Case.Sensitive, "token-not-forwarded");
                if (File.Exists(f.Root + "/stdin")) File.ReadAllText(f.Root + "/stdin").ShouldNotContain(sentinel, Case.Sensitive, "token-not-forwarded");
                foreach (var path in f.Receipts) File.ReadAllText(path).ShouldNotContain(sentinel, Case.Sensitive, "diagnostic-custody");
            }
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Explicit_tool_phases_never_deploy()
    {
        foreach (var phase in new[] { "check-host-jq", "provision-host-jq" })
        foreach (var state in new[] { "existing", "missing", "invalid" })
        {
            using var f = new HostJqFixture(); if (state != "missing") f.Existing(state == "invalid" ? "true" : "healthy");
            await f.InitializeRepo();
            var r = await f.Wrapper(phase);
            f.Trace.ShouldNotContain("HTTP", Case.Sensitive, "explicit-only: HTTP/case/POST count=0");
            f.Trace.ShouldNotContain("case", Case.Sensitive, "explicit-only");
            r.Exit.ShouldBe(state == "invalid" || (phase == "check-host-jq" && state == "missing") ? 2 : 0, r.Output);
            f.Trace.Split('\n').Count(x => x.StartsWith("ssh ", StringComparison.Ordinal)).ShouldBe(1);
            f.Trace.ShouldContain("bash -s -- " + (phase == "check-host-jq" ? "check" : "provision"));
        }
    }
}

// All mutations/effects are private; only fixed filesystem literals and test deadlines are copied.
internal sealed class HostJqFixture : IDisposable
{
    internal const string Pin = "5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5";
    internal const string Url = "https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-linux-amd64";
    internal static string[] ProvisionTools => ["curl", "sha256sum", "install", "mktemp", "flock", "ln", "stat", "readlink", "rm", "uname", "test"];
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "c1025-" + Guid.NewGuid().ToString("N"));
    internal string Tools => Root + "/tools";
    internal string Parent => Root + "/destination";
    internal string Destination => Parent + "/jq";
    internal string OtherJq => Tools + "/jq";
    internal string Lock => Root + "/lock";
    internal string Temp => Root + "/temporary";
    internal string Foreign => Root + "/foreign/sentinel";
    internal string ForeignDirectory => Root + "/foreign";
    internal string Payload => Root + "/payload";
    internal string HelperPath => Root + "/scripts/server2-host-jq.sh";
    internal string WrapperRoot { get; set; }
    internal string? Linked { get; set; }
    internal string RequestSha { get; set; } = "";
    internal string Transport { get; set; } = "";
    internal bool IncludeDestination { get; set; } = true;
    internal string? SyntheticProof { get; set; }
    internal string Trace => File.Exists(Root + "/trace") ? File.ReadAllText(Root + "/trace") : "";
    internal string[] InstallEffects => Trace.Split('\n').Where(x => new[] { "sudo ", "download ", "stage ", "publish ", "unlink " }.Any(x.StartsWith)).ToArray();
    internal string[] Receipts => Directory.Exists(WrapperRoot + "/.antiphon/rolling-server2") ? Directory.GetFiles(WrapperRoot + "/.antiphon/rolling-server2", "host-jq-*.json", SearchOption.AllDirectories) : [];
    private readonly string _jq;
    private readonly string _pwsh;
    private readonly string _git;

    internal HostJqFixture()
    {
        OperatingSystem.IsLinux().ShouldBeTrue("C1025 prerequisite: native Linux");
        _jq = Locate("jq"); _pwsh = Locate("pwsh"); _git = Locate("git");
        foreach (var tool in new[] { "bash", "node", "curl", "sha256sum", "flock", "install", "mktemp", "ln", "stat", "readlink", "rm", "uname", "cmp", "cat", "sleep" }) Locate(tool);
        WrapperRoot = Root;
        foreach (var dir in new[] { Tools, Parent, Lock, Temp, ForeignDirectory, Root + "/scripts/lib" }) Directory.CreateDirectory(dir);
        File.WriteAllText(Foreign, "foreign-custody"); File.WriteAllText(Lock + "/neighbor", "foreign-lock-file");
        var helper = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/server2-host-jq.sh"));
        helper.ShouldContain(Url); helper.ShouldContain(Pin); helper.All(c => c <= 127).ShouldBeTrue("ASCII transport");
        helper = ReplaceOnce(helper, "DESTINATION=/usr/local/bin/jq", "DESTINATION=" + Destination);
        helper = ReplaceOnce(helper, "LOCK_ROOT=/var/lock/antiphon-host-jq", "LOCK_ROOT=" + Lock);
        helper = ReplaceOnce(helper, "CONTAINER_MARKER=/.dockerenv", "CONTAINER_MARKER=" + Root + "/dockerenv");
        File.WriteAllText(HelperPath, helper);
        File.WriteAllText(Root + "/scripts/fake-http.ps1", "param($Method,$RunnerId,$Suffix,$BodyJson,$Path)\n[IO.File]::AppendAllText($env:HJ_ROOT+'/trace', 'HTTP '+$Method+[Environment]::NewLine)\n'__503__'\nexit 0\n");
        File.WriteAllText(Root + "/scripts/fake-verify.ps1", "param($Case,$Manifest)\n[IO.File]::AppendAllText($env:HJ_ROOT+'/trace', 'case '+$Case+[Environment]::NewLine)\nexit 2\n");
        foreach (var tool in ProvisionTools.Concat(new[] { "bash", "git", "pwsh" })) File.CreateSymbolicLink(Tools + "/" + tool, Locate(tool));
        File.WriteAllText(Payload, JqScript("healthy")); Executable(Payload);
        WriteTool("docker", "[ \"$fault\" != no-daemon ] || exit 1\n[ \"$fault\" != sibling ] || { echo sibling; exit 0; }\necho fixture-host");
        WriteTool("hostname", "[ \"$fault\" != hostname ] || exit 1\necho fixture-host");
        WriteTool("uname", "if [ \"$1\" = -s ]; then [ \"$fault\" != os ] || { echo Darwin; exit 0; }; echo Linux; else [ \"$fault\" != arch ] || { echo aarch64; exit 0; }; echo x86_64; fi");
        WriteTool("sudo", "printf 'sudo %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\n[ \"$fault\" != sudo ] || exit 1\n[ \"$1\" = -n ] || exit 1\nshift; [ \"$1\" != -- ] || shift\nif [ \"$1\" = test ]; then [ \"$fault\" != parent-write ] || exit 1; fi\nexec \"$@\"");
        WriteTool("stat", "last=${!#}\n[ \"$fault\" != parent-stat ] || [ \"$last\" != \"$HJ_DEST_PARENT\" ] || exit 1\nformat=$2\nif [ -d \"$last\" ] && [ \"$format\" = '%u %g %a' ]; then echo '0 0 755'; exit 0; fi\nif [ \"$last\" = \"$HJ_LOCK\" ]; then echo '0:0:755'; exit 0; fi\nif [ \"$format\" = '%u %g %a' ] && [ \"$last\" = \"$HJ_DEST\" ]; then\n case \"$fault\" in final-owner) echo '1 0 755';; final-group) echo '0 1 755';; final-mode) echo '0 0 777';; *) echo '0 0 755';; esac; exit 0; fi\nif [[ \"$last\" = */.antiphon-jq.*/jq ]] && [ \"$format\" = '%u:%g:%a' ]; then\n [ -f \"${last%/*}/owner\" ] || { echo '1654:1654:755'; exit 0; }\n case \"$fault\" in stage-owner) echo '1:0:755';; stage-mode) echo '0:0:777';; *) printf '0:0:'; /usr/bin/stat -c '%a' \"$last\";; esac; exit 0; fi\nexec /usr/bin/stat \"$@\"");
        WriteTool("sha256sum", "last=${!#}\nprintf 'hash %s\\n' \"$last\" >> \"$HJ_ROOT/trace\"\n[ \"$fault\" != hash-exit ] || exit 1\nif [ \"$fault\" = final-digest ] && [ \"$last\" = \"$HJ_DEST\" ]; then echo 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  file'; exit 0; fi\nif /usr/bin/cmp -s \"$last\" \"$HJ_ROOT/payload\"; then printf '%s  %s\\n' '" + Pin + "' \"$last\"; else exec /usr/bin/sha256sum \"$@\"; fi");
        WriteTool("curl", "printf 'download %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\nwhile [ \"$1\" != --output ]; do shift; done; out=$2\nprintf 'download-mode %s\\n' \"$(/usr/bin/stat -c %a \"${out%/*}\")\" >> \"$HJ_ROOT/trace\"\nif [ \"$fault\" = download-barrier ]; then echo ready > \"$HJ_ROOT/download-ready\"; /usr/bin/sleep .5; fi\nif [ \"$fault\" = truncated ] || [ \"$fault\" = digest ]; then printf bad > \"$out\"; else /usr/bin/cp \"$HJ_ROOT/payload\" \"$out\"; fi\n[ \"$fault\" != curl-exit ] || exit 1");
        WriteTool("install", "original=\"$*\"\nargs=(); owner=false; group=false\nwhile [ \"$#\" -gt 0 ]; do case \"$1\" in -o) owner=true; shift 2;; -g) group=true; shift 2;; *) args+=(\"$1\"); shift;; esac; done\nlast=${args[${#args[@]}-1]}\nif [[ \"$last\" = */.antiphon-jq.*/jq ]]; then\n printf 'stage %s\\n' \"$original\" >> \"$HJ_ROOT/trace\"\n [ \"$fault\" != stage-exit ] || exit 1\n if $owner && $group; then echo root > \"${last%/*}/owner\"; fi\nfi\n/usr/bin/install \"${args[@]}\" || exit $?\nif [ \"$fault\" = tamper ] && [[ \"$last\" = */.antiphon-jq.*/jq ]]; then printf corrupt >> \"$last\"; fi");
        WriteTool("ln", "printf 'publish %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\nsource=$3\nprintf 'stage-owner %s\\n' \"$(stat -c '%u:%g:%a' -- \"$source\")\" >> \"$HJ_ROOT/trace\"\nif [ \"$fault\" = publish-barrier ]; then echo ready > \"$HJ_ROOT/publish-ready\"; while [ ! -f \"$HJ_ROOT/publish-release\" ]; do /usr/bin/sleep .01; done; fi\n[ \"$fault\" != publish-exit ] || exit 1\n/usr/bin/ln \"$@\" || exit $?\nif [ \"$fault\" = final-path ]; then /usr/bin/cp \"$HJ_ROOT/payload\" \"$HJ_ROOT/tools/jq\"; /usr/bin/chmod 755 \"$HJ_ROOT/tools/jq\"; fi");
        WriteTool("rm", "printf 'unlink %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\nexec /usr/bin/rm \"$@\"");
        WriteTool("flock", "echo flock-request >> \"$HJ_ROOT/trace\"\nexec /usr/bin/flock \"$@\"");
        // Boundary for the planned non-atomic-copy control; production uses native ln.
        WriteTool("cp", "source=${@: -2:1}; destination=${@: -1}\nif [ \"$destination\" != \"$HJ_DEST\" ]; then exec /usr/bin/cp \"$@\"; fi\nprintf 'stage-owner %s\\n' \"$(stat -c '%u:%g:%a' -- \"$source\")\" >> \"$HJ_ROOT/trace\"\necho ready > \"$HJ_ROOT/publish-ready\"\nwhile [ ! -f \"$HJ_ROOT/publish-release\" ]; do /usr/bin/sleep .01; done\n/usr/bin/head -c 10 \"$source\" > \"$destination\"\n/usr/bin/sleep .1\n/usr/bin/cp -- \"$source\" \"$destination\"");
        WriteTool("ssh", "printf 'ssh %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\necho $$ > \"$HJ_ROOT/ssh-pid\"\n/usr/bin/cat > \"$HJ_ROOT/stdin\"\nif [ \"$HJ_TRANSPORT\" = block-receipt ]; then\n for d in \"$HJ_WRAPPER\"/.antiphon/rolling-server2/*; do /usr/bin/mkdir \"$d/host-jq-check-host-jq.json\"; done\nfi\nprintf 'remote-C1025-secret\\n' >&2\nif [ \"$HJ_TRANSPORT\" = late ]; then /usr/bin/sleep .5; fi\nif [ \"$HJ_TRANSPORT\" = empty ]; then exit 0; fi\nif [ \"$HJ_TRANSPORT\" = truncated ]; then echo '{'; exit 0; fi\nif [ -f \"$HJ_ROOT/proof\" ]; then /usr/bin/cat \"$HJ_ROOT/proof\"; else /usr/bin/bash -s -- \"${!#}\" < \"$HJ_ROOT/stdin\"; code=$?; [ \"$code\" = 0 ] || exit \"$code\"; fi\n[ \"$HJ_TRANSPORT\" != ssh-exit ] || exit 255");
    }

    private string JqScript(string fault) => "#!/usr/bin/bash\nprintf 'jq-call %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\nfault=$(/usr/bin/cat \"$HJ_ROOT/fault\" 2>/dev/null)\nif [ \"$1\" = --version ]; then\n" + (fault == "version-exit" ? "exit 1\n" : fault == "version-empty" ? "exit 0\n" : fault == "version-other" ? "echo jq-other; exit 0\n" : "[ \"$fault\" != final-version ] || { echo jq-other; exit 0; }\n") +
        "fi\n" + (fault == "true" ? "[ \"$1\" != -en ] || { echo false; exit 1; }\n" : fault == "false" ? "[ \"$1\" != -en ] || { echo true; exit 0; }\n" : "") +
        "if [ \"$1\" = -en ] && [ \"$fault\" = final-true ]; then echo false; exit 1; fi\nif [ \"$1\" = -en ] && [ \"$fault\" = final-false ]; then echo true; exit 0; fi\nexec '" + _jq + "' \"$@\"\n";

    internal void Existing(string fault = "healthy", bool elsewhere = false)
    {
        var path = elsewhere ? OtherJq : Destination;
        if (fault == "healthy") File.Copy(_jq, path, overwrite: true);
        else File.WriteAllText(path, JqScript(fault));
        File.SetUnixFileMode(path, fault == "nonexec" ? UnixFileMode.UserRead | UnixFileMode.UserWrite : (UnixFileMode)Convert.ToInt32("755", 8));
    }
    internal void Fault(string value)
    {
        File.WriteAllText(Root + "/fault", value);
        if (value == "nested") File.WriteAllText(Root + "/dockerenv", "container");
    }
    internal void ClearTrace() => File.WriteAllText(Root + "/trace", "");
    internal string[] Sentinels() => new[] { File.ReadAllText(Foreign), File.ReadAllText(Lock + "/neighbor") };
    internal string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal Task<HostJqRun> Helper(params string[] args) => Run("/usr/bin/bash", [HelperPath, .. args]);
    internal async Task<string> NativeJqVersion() => (await Run(_jq, "--version")).Stdout;
    internal async Task<string> Inode(string path) => (await Run("/usr/bin/stat", "-c", "%i", path)).Stdout;
    internal Task<HostJqRun> Git(params string[] args) => Run(_git, ["-C", Root, .. args]);
    internal void WriteTool(string tool, string body)
    {
        var path = Tools + "/" + tool; File.Delete(path);
        File.WriteAllText(path, "#!/usr/bin/bash\nfault=$(/usr/bin/cat \"$HJ_ROOT/fault\" 2>/dev/null)\n" + body + "\n"); Executable(path);
    }
    internal async Task InitializeRepo(bool shortDeadline = false)
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/deploy-server2.ps1"));
        // The wrapper's fixed installed-path assertion follows the helper's private destination.
        source = ReplaceOnce(source, "$proof.path -cne '/usr/local/bin/jq'", "$proof.path -cne '" + Destination + "'");
        if (shortDeadline) source = ReplaceOnce(source, "{ 30000 } else { 180000 }", "{ 100 } else { 100 }");
        File.WriteAllText(Root + "/scripts/deploy-server2.ps1", source);
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/lib/runner-operator-token.ps1"), Root + "/scripts/lib/runner-operator-token.ps1");
        File.WriteAllText(Root + "/.gitignore", ".antiphon/\n/tools/\n/foreign/\n/destination/\n/lock/\n/temporary/\n/payload\n/fault\n/dockerenv\n/trace\n/stdin\n/ssh-pid\n/proof\n/token\n/*-ready\n/*-release\n");
        (await Git("init", "-q")).Exit.ShouldBe(0);
        (await Git("config", "user.name", "fixture")).Exit.ShouldBe(0); (await Git("config", "user.email", "fixture@example.invalid")).Exit.ShouldBe(0);
        (await Git("add", ".")).Exit.ShouldBe(0); (await Git("commit", "-qm", "private baseline")).Exit.ShouldBe(0);
        RequestSha = (await Git("rev-parse", "HEAD")).Stdout.Trim(); ClearTrace();
    }
    internal async Task<HostJqRun> Wrapper(string phase)
    {
        if (SyntheticProof is not null) File.WriteAllText(Root + "/proof", SyntheticProof);
        File.WriteAllText(Root + "/token", "operator-C1025-secret");
        if (Transport == "start") { File.Delete(Tools + "/ssh"); File.WriteAllText(Tools + "/ssh", "not executable"); }
        return await Run(_pwsh, "-NoProfile", "-File", WrapperRoot + "/scripts/deploy-server2.ps1", "-Rolling", "-Sha", RequestSha, "-Phase", phase);
    }
    internal void AssertReceipt(string phase, string mode, DateTime start, DateTime end, bool installed)
    {
        var path = Receipts.Last(); var p = JsonNode.Parse(File.ReadAllText(path))!;
        p["schema"]!.GetValue<int>().ShouldBe(1); p["lane"]!.GetValue<string>().ShouldBe("host");
        p["sourceSha"]!.GetValue<string>().ShouldBe(RequestSha, "receipt-sha");
        p["runId"]!.GetValue<string>().ShouldBe(Path.GetFileName(Path.GetDirectoryName(path)), "receipt-run");
        p["selectedPhase"]!.GetValue<string>().ShouldBe(phase, "receipt-phase"); p["phase"]!.GetValue<string>().ShouldBe(phase, "receipt-phase");
        p["mode"]!.GetValue<string>().ShouldBe(mode, "receipt-mode");
        var observed = DateTime.Parse(p["observedAtUtc"]!.GetValue<string>()).ToUniversalTime();
        (observed >= start && observed <= end).ShouldBeTrue("receipt-time");
        p["sshExit"]!.GetValue<int>().ShouldBe(0); p["path"]!.GetValue<string>().ShouldBe(Destination);
        p["trueExit"]!.GetValue<int>().ShouldBe(0); p["falseExit"]!.GetValue<int>().ShouldBe(1);
        p["installed"]!.GetValue<bool>().ShouldBe(installed);
        p["outcome"]!.GetValue<string>().ShouldBe(installed ? "installed" : "existing");
        p["digest"]!.GetValue<string>().ShouldBe(installed ? Pin : Hash(Destination));
        p["version"]!.GetValue<string>().ShouldBe("jq-1.7.1");
        p["uid"]!.GetValue<int>().ShouldBe(0); p["gid"]!.GetValue<int>().ShouldBe(0); p["permissions"]!.GetValue<string>().ShouldBe("755");
    }
    internal Process Start(string executable, params string[] args)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["PATH"] = Tools + (!IncludeDestination || (File.Exists(Root + "/fault") && File.ReadAllText(Root + "/fault") == "path-absent") ? "" : ":" + Parent);
        psi.Environment["TMPDIR"] = Temp; psi.Environment["HJ_ROOT"] = Root; psi.Environment["HJ_DEST"] = Destination;
        psi.Environment["HJ_DEST_PARENT"] = Parent; psi.Environment["HJ_LOCK"] = Lock; psi.Environment["HJ_WRAPPER"] = WrapperRoot;
        psi.Environment["HJ_TRANSPORT"] = Transport; psi.Environment["ANTIPHON_OPERATOR_TOKEN_FILE"] = Root + "/token";
        psi.Environment["ANTIPHON_TASK_TOKEN"] = "task-C1025-secret";
        psi.Environment["C727_TEST_VERIFY_STUB"] = Root + "/scripts/fake-verify.ps1";
        psi.Environment["C727_TEST_HTTP_STUB"] = Root + "/scripts/fake-http.ps1";
        return Process.Start(psi)!;
    }
    internal async Task<HostJqRun> Run(string executable, params string[] args)
    { using var p = Start(executable, args); p.StandardInput.Close(); return await Collect(p); }
    internal async Task<HostJqRun> Collect(Process p)
    {
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await p.WaitForExitAsync(watchdog.Token); return new(p.ExitCode, await stdout.WaitAsync(watchdog.Token), await stderr.WaitAsync(watchdog.Token)); }
        finally { if (!p.HasExited) p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
    }
    internal async Task WaitMarker(string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(Root + "/" + name) && DateTime.UtcNow < deadline) await Task.Delay(10);
        File.Exists(Root + "/" + name).ShouldBeTrue("barrier reached: " + name + Trace);
    }
    internal async Task WaitTrace(string text, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Trace.Split(text, StringSplitOptions.None).Length - 1 < count && DateTime.UtcNow < deadline) await Task.Delay(10);
        (Trace.Split(text, StringSplitOptions.None).Length - 1).ShouldBeGreaterThanOrEqualTo(count, "barrier reached: " + text);
    }
    internal static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
    private static string ReplaceOnce(string source, string before, string after)
    {
        source.Split(before, StringSplitOptions.None).Length.ShouldBe(2, "exact fixture replacement inventory: " + before);
        return source.Replace(before, after, StringComparison.Ordinal);
    }
    private static string Locate(string tool)
    {
        var found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Select(p => Path.Combine(p, tool)).FirstOrDefault(File.Exists);
        found.ShouldNotBeNull("C1025 prerequisite missing: " + tool); return found!;
    }
    private static void Executable(string path) => File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("755", 8));
    public void Dispose()
    {
        if (Linked is not null && Directory.Exists(Linked)) Directory.Delete(Linked, true);
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
internal sealed record HostJqRun(int Exit, string Stdout, string Stderr) { internal string Output => Stdout + Stderr; }
