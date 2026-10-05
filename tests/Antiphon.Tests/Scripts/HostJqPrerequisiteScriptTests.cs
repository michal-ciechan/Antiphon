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
            p["lookupPath"]!.GetValue<string>().ShouldBe(f.Destination, "found-path-recorded");
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
        using (var home = new HostJqFixture())
        {
            home.Existing(); home.HomeShadow(); await home.InitializeRepo();
            var expectedHash = home.Hash(home.Destination); var homeHash = home.Hash(home.HomeJq);
            homeHash.ShouldBe(expectedHash, "canonical-host-path: independently working identical bytes still cannot shadow the host path");
            (await home.Run(home.HomeJq, "--version")).Stdout.Trim().ShouldBe("jq-1.7.1");
            (await home.Run(home.HomeJq, "-en", "true")).Exit.ShouldBe(0);
            (await home.Run(home.HomeJq, "-en", "false")).Exit.ShouldBe(1);
            foreach (var mode in new[] { "check", "provision" })
            {
                var direct = await home.Helper(mode);
                direct.Exit.ShouldBe(2, "canonical-host-path: a working user-home binary ahead on PATH cannot qualify");
                var rejected = JsonNode.Parse(direct.Stdout)!;
                rejected["reason"]!.GetValue<string>().ShouldBe("HostJqPathUnapproved");
                rejected["lookupPath"]!.GetValue<string>().ShouldBe(home.HomeJq, "found-path-recorded");
                rejected["path"]!.GetValue<string>().ShouldBe(home.HomeJq);
                var phase = mode == "check" ? "check-host-jq" : "provision-host-jq";
                var start = DateTime.UtcNow; var wrapped = await home.Wrapper(phase);
                wrapped.Exit.ShouldBe(2, "canonical-host-path: wrapper cannot qualify user-home jq");
                wrapped.Output.ShouldContain("HostJqPathUnapproved");
                wrapped.Output.ShouldNotContain("Host jq qualified");
                home.AssertRefusalReceipt(phase, mode, start, DateTime.UtcNow);
            }
            home.InstallEffects.ShouldBeEmpty("canonical-host-path: no replacement, download or elevation");
            home.Receipts.ShouldBeEmpty("canonical-host-path: no success receipt");
            home.Hash(home.Destination).ShouldBe(expectedHash); home.Hash(home.HomeJq).ShouldBe(homeHash);
        }
        using (var alias = new HostJqFixture())
        {
            alias.Existing(); File.CreateSymbolicLink(alias.OtherJq, alias.Destination); await alias.InitializeRepo();
            var direct = await alias.Helper("check"); direct.Exit.ShouldBe(0, "canonical-host-alias: symlink resolves to canonical jq");
            var p = JsonNode.Parse(direct.Stdout)!;
            p["lookupPath"]!.GetValue<string>().ShouldBe(alias.OtherJq, "found-path-recorded: preserve the alias actually found");
            p["path"]!.GetValue<string>().ShouldBe(alias.Destination, "canonical-host-alias: resolved path");
            (await alias.Wrapper("check-host-jq")).Exit.ShouldBe(0);
            var receipt = JsonNode.Parse(File.ReadAllText(alias.Receipts.Single()))!;
            receipt["lookupPath"]!.GetValue<string>().ShouldBe(alias.OtherJq, "found-path-recorded: persisted alias");
            receipt["path"]!.GetValue<string>().ShouldBe(alias.Destination);
            alias.InstallEffects.ShouldBeEmpty();
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Check_rejects_canonical_leaf_symlink()
    {
        using var f = new HostJqFixture();
        f.HomeShadow(); f.PathPrefix = ""; // Keep the destination ON PATH and resolve the canonical leaf.
        File.WriteAllText(f.HomeJq, File.ReadAllText(f.Payload)); // Working jq with an execution trace.
        File.CreateSymbolicLink(f.Destination, f.HomeJq);
        await f.InitializeRepo();
        var homeHash = f.Hash(f.HomeJq); var homeInode = await f.Inode(f.HomeJq);
        foreach (var mode in new[] { "check", "provision" })
        {
            var direct = await f.Helper(mode);
            direct.Exit.ShouldBe(2, "canonical-leaf-symlink: working home target cannot qualify" + direct.Output);
            direct.Stderr.ShouldContain("HostJqPathUnapproved");
            var proof = JsonNode.Parse(direct.Stdout)!;
            proof["lookupPath"]!.GetValue<string>().ShouldBe(f.Destination, "canonical leaf must actually be found on PATH");
            proof["path"]!.GetValue<string>().ShouldBe(f.HomeJq);
            var wrapped = await f.Wrapper(mode + "-host-jq");
            wrapped.Exit.ShouldBe(2, wrapped.Output);
            wrapped.Output.ShouldContain("HostJqPathUnapproved");
            wrapped.Output.ShouldNotContain("Host jq qualified");
            var observation = JsonNode.Parse(File.ReadAllText(f.RefusalReceipts.Last()))!;
            observation["qualified"]!.GetValue<bool>().ShouldBeFalse();
            observation["lookupPath"]!.GetValue<string>().ShouldBe(f.Destination);
            observation["path"]!.GetValue<string>().ShouldBe(f.HomeJq);
        }
        f.Receipts.ShouldBeEmpty("canonical-leaf-symlink: no success receipt");
        f.InstallEffects.ShouldBeEmpty("canonical-leaf-symlink: no download, replacement or elevation");
        f.Trace.ShouldNotContain("jq-call", Case.Sensitive, "unapproved target must not execute");
        f.Hash(f.HomeJq).ShouldBe(homeHash); (await f.Inode(f.HomeJq)).ShouldBe(homeInode);
        new FileInfo(f.Destination).LinkTarget.ShouldBe(f.HomeJq);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Receipt_rejects_canonical_lookup_with_unapproved_target()
    {
        foreach (var mode in new[] { "check", "provision" })
        {
            using var f = new HostJqFixture(); f.Existing();
            var proof = JsonNode.Parse((await f.Helper(mode)).Stdout)!;
            f.HomeShadow(); f.PathPrefix = "";
            File.Delete(f.Destination); File.CreateSymbolicLink(f.Destination, f.HomeJq);
            proof["path"] = f.HomeJq;
            f.SyntheticProof = proof.ToJsonString(); // Otherwise valid success proof bypasses the helper guard.
            await f.InitializeRepo();
            var result = await f.Wrapper(mode + "-host-jq");
            result.Exit.ShouldBe(2, "canonical-proof-target: canonical lookup alone cannot admit proof" + result.Output);
            result.Output.ShouldContain("HostJqProofInvalid");
            result.Output.ShouldNotContain("Host jq qualified");
            f.Receipts.ShouldBeEmpty("canonical-proof-target: no success receipt");
            f.RefusalReceipts.ShouldBeEmpty("synthetic success is not a trusted refusal observation");
            f.InstallEffects.ShouldBeEmpty();
        }
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
            var r = await f.Helper("provision"); r.Exit.ShouldBe(elsewhere ? 2 : 0, r.Output);
            if (elsewhere) JsonNode.Parse(r.Stdout)!["reason"]!.GetValue<string>().ShouldBe("HostJqPathUnapproved", "canonical-host-path");
            else JsonNode.Parse(r.Stdout)!["installed"]!.GetValue<bool>().ShouldBeFalse("existing-no-op");
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
            // Other vectors reach missing-destination admission; the symlink stays ON PATH.
            using var f = new HostJqFixture { IncludeDestination = vector == "symlink" };
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
            if (fault == "final-path") JsonNode.Parse(r.Stdout)!["reason"]!.GetValue<string>().ShouldBe("HostJqPathUnapproved", "final-path: no successful proof");
            else r.Stdout.ShouldBeEmpty(fault + ": success receipts=0");
            File.Exists(f.Destination).ShouldBeTrue("published file left for diagnosis");
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
        var extra = (JsonObject)valid.DeepClone(); extra["unexpected"] = true; corruptions.Add(extra.ToJsonString());
        var numericBoolean = (JsonObject)valid.DeepClone(); numericBoolean["installed"] = 0; corruptions.Add(numericBoolean.ToJsonString());
        var fractionalOwner = (JsonObject)valid.DeepClone(); fractionalOwner["uid"] = 0.5; corruptions.Add(fractionalOwner.ToJsonString());
        var unapproved = (JsonObject)valid.DeepClone();
        unapproved["lookupPath"] = "/home/app/.local/bin/jq"; unapproved["path"] = "/home/app/.local/bin/jq";
        corruptions.Add(unapproved.ToJsonString());
        foreach (var bad in corruptions)
        {
            using var f = new HostJqFixture();
            f.SyntheticProof = bad.Replace(template.Destination, f.Destination, StringComparison.Ordinal);
            await f.InitializeRepo();
            var r = await f.Wrapper("check-host-jq"); r.Exit.ShouldBe(2, "proof-shape: " + bad + r.Output);
            f.Receipts.ShouldBeEmpty("proof-shape: no success receipt");
        }
        var refusal = new JsonObject { ["schema"] = 1, ["lane"] = "host", ["mode"] = "check",
            ["lookupPath"] = "/home/app/.local/bin/jq", ["path"] = "/home/app/.local/bin/jq", ["reason"] = "HostJqPathUnapproved" };
        var badRefusals = new List<string> { "{", "[]", refusal.ToJsonString() + refusal.ToJsonString(),
            refusal.ToJsonString().Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal) };
        foreach (var key in refusal.Select(x => x.Key))
        {
            var missing = (JsonObject)refusal.DeepClone(); missing.Remove(key); badRefusals.Add(missing.ToJsonString());
            var wrong = (JsonObject)refusal.DeepClone(); wrong[key] = new JsonArray(); badRefusals.Add(wrong.ToJsonString());
        }
        var extraRefusal = (JsonObject)refusal.DeepClone(); extraRefusal["unexpected"] = true; badRefusals.Add(extraRefusal.ToJsonString());
        foreach (var (key, value) in new[] { ("lane", "nested"), ("mode", "provision"), ("reason", "remote-secret"), ("lookupPath", "relative"), ("path", "relative") })
        { var bad = (JsonObject)refusal.DeepClone(); bad[key] = value; badRefusals.Add(bad.ToJsonString()); }
        foreach (var bad in badRefusals)
        {
            using var f = new HostJqFixture(); f.SyntheticProof = bad; f.Transport = "ssh-refused"; await f.InitializeRepo();
            var result = await f.Wrapper("check-host-jq"); result.Exit.ShouldBe(2);
            f.RefusalReceipts.ShouldBeEmpty("refusal-proof-shape: unknown or malformed diagnostic cannot become an observation");
            f.Receipts.ShouldBeEmpty("refusal-proof-shape: no successful proof");
            result.Output.ShouldNotContain("remote-secret", Case.Sensitive, "diagnostic-custody");
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
        using var shadow = new HostJqFixture(); shadow.Existing(); shadow.HomeShadow(); shadow.Transport = "block-refusal"; await shadow.InitializeRepo();
        var failedObservation = await shadow.Wrapper("check-host-jq"); failedObservation.Exit.ShouldBe(2);
        failedObservation.Output.ShouldContain("HostJqReceiptUnavailable", Case.Sensitive, "refusal-receipt-required");
        shadow.RefusalReceipts.ShouldBeEmpty("refusal-receipt-required"); shadow.Receipts.ShouldBeEmpty();
        shadow.Transport = ""; var fresh = DateTime.UtcNow;
        var refused = await shadow.Wrapper("check-host-jq"); refused.Exit.ShouldBe(2);
        shadow.AssertRefusalReceipt("check-host-jq", "check", fresh, DateTime.UtcNow);
        foreach (var phase in new[] { "deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp" })
        {
            using var entry = new HostJqFixture(); entry.Existing(); entry.Transport = "block-receipt"; await entry.InitializeRepo();
            var blocked = await entry.Wrapper(phase);
            blocked.Exit.ShouldBe(2, blocked.Output);
            blocked.Output.ShouldContain("HostJqReceiptUnavailable", Case.Sensitive, "receipt-required");
            entry.Trace.ShouldNotContain("HTTP", Case.Sensitive, "receipt-required: no body effects");
            entry.Trace.ShouldNotContain("case ", Case.Sensitive, "receipt-required: no case effects");
            entry.Receipts.ShouldBeEmpty();
        }
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

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Preflight_precedes_every_phase()
    {
        var faults = new[] { "healthy", "missing", "invalid", "ssh-failed", "malformed-proof", "receipt-write-failed" };
        foreach (var shape in C1025RolloutFixture.Shapes)
        foreach (var fault in faults)
        {
            using var f = new C1025RolloutFixture(shape);
            f.Sequence(fault);
            var start = DateTime.UtcNow;
            var r = await f.Run();
            r.Trace.ShouldNotBeEmpty(shape + ": " + r.Output);
            r.Trace[0]["kind"]!.GetValue<string>().ShouldBe("prerequisite", "preflight-first: " + shape);
            f.AssertChecks(r.Trace, 1);
            if (fault == "healthy")
            {
                r.Exit.ShouldBe(0, shape + ": " + r.Output);
                r.Trace.Length.ShouldBeGreaterThan(1, "healthy reaches phase body: " + shape);
                f.AssertReceipt(r.Trace[0], start, DateTime.UtcNow);
            }
            else
            {
                r.Exit.ShouldBe(2, shape + ": " + fault + ": " + r.Output);
                r.Output.ShouldContain(C1025RolloutFixture.Diagnosis(fault));
                r.Trace.Length.ShouldBe(1, "preflight-first: failed prerequisite has phase-body trace count=0: " + shape + "/" + fault);
                File.Exists(C1025RolloutFixture.ReceiptPath(r.Trace[0])).ShouldBeFalse("failed proof cannot persist success");
            }
        }

        foreach (var shape in new[] { "deploy-temp", "redeploy-old", "retire-temp", "redeploy-old:same-sha" })
        foreach (var fault in new[] { "missing", "healthy" })
        {
            using var f = new C1025RolloutFixture(shape);
            f.Sequence(fault);
            if (shape.EndsWith(":same-sha", StringComparison.Ordinal)) f.State["incompleteRecycle"] = true;
            else f.Busy();
            var r = await f.Run();
            r.Exit.ShouldBe(2, r.Output);
            r.Trace[0]["kind"]!.GetValue<string>().ShouldBe("prerequisite", "preflight-first");
            r.Output.ShouldContain(fault == "missing" ? "HostJqMissing" : shape.Contains(':') ? "RecycleResumeRequired" : "RunnerBusy");
            if (fault == "missing") r.Trace.Length.ShouldBe(1, "missing wins before busy/journal");
            else f.AssertReceipt(r.Trace[0], f.Started, DateTime.UtcNow);
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Preflight_is_fresh_for_each_entry()
    {
        var phases = new[] { "deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp" };
        for (var failAt = 0; failAt <= 5; failAt++)
        {
            using var f = new C1025RolloutFixture("all");
            f.Sequence(Enumerable.Range(1, 5).Select(i => i == failAt ? "missing" : "healthy").ToArray());
            var r = await f.Run();
            r.Exit.ShouldBe(failAt == 0 ? 0 : 2, r.Output);
            var checks = r.Trace.Where(x => x["kind"]!.GetValue<string>() == "prerequisite").ToArray();
            var count = failAt == 0 ? 5 : failAt;
            f.AssertChecks(r.Trace, count);
            checks.Select(x => x["phase"]!.GetValue<string>()).ShouldBe(phases.Take(count).ToArray(), "fresh-preflight: phase order");
            for (var i = 0; i < count; i++)
            {
                if (failAt > 0 && i == count - 1)
                {
                    Array.IndexOf(r.Trace, checks[i]).ShouldBe(r.Trace.Length - 1,
                        "fresh-preflight: refused entry and later phases have no body effects");
                    File.Exists(C1025RolloutFixture.ReceiptPath(checks[i])).ShouldBeFalse();
                }
                else
                {
                    f.AssertReceipt(checks[i], f.Started, DateTime.UtcNow);
                    var next = i + 1 < count ? Array.IndexOf(r.Trace, checks[i + 1]) : r.Trace.Length;
                    (next - Array.IndexOf(r.Trace, checks[i])).ShouldBeGreaterThan(1, "each healthy phase reaches its own body");
                }
            }
            if (failAt == 2) checks.Length.ShouldBe(2, "fresh-preflight: fail-second all has exactly two check attempts");
            var evidence = checks[0]["evidenceRoot"]!.GetValue<string>();
            Directory.GetFiles(evidence, "host-jq-*.json").Count(x => !x.EndsWith(".manifest.json", StringComparison.Ordinal))
                .ShouldBe(failAt == 0 ? 5 : failAt - 1, "fresh-preflight: independent persisted receipts");
        }
        foreach (var shape in C1025RolloutFixture.Shapes.Where(x => x.Contains(':')))
        {
            using var f = new C1025RolloutFixture(shape);
            f.Sequence("healthy");
            var first = await f.Run(); first.Exit.ShouldBe(0, first.Output);
            f.AssertReceipt(first.Trace[0], f.Started, DateTime.UtcNow);
            f.Sequence("missing");
            var second = await f.Run(); second.Exit.ShouldBe(2, second.Output);
            second.Output.ShouldContain("HostJqMissing");
            f.AssertChecks(second.Trace, 1);
            second.Trace.Length.ShouldBe(1, "fresh-preflight: earlier receipt cannot admit a fresh invocation");
            second.Trace[0]["runId"]!.GetValue<string>().ShouldNotBe(first.Trace[0]["runId"]!.GetValue<string>());
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1025_Final_recycle_guard_remains()
    {
        OperatingSystem.IsLinux().ShouldBeTrue("C1025 prerequisite: native Linux");
        foreach (var hostCase in new[] { "deploy-parent", "retire-temp-runner" })
        foreach (var mode in new[] { "normal", "preview", "resume" })
        {
            using var f = new C1008HostFixture(main: hostCase == "deploy-parent");
            var sentinels = Directory.GetFiles(f.Root + "/volumes", "sentinel", SearchOption.AllDirectories)
                .ToDictionary(x => x, File.ReadAllText);
            // Intercept only jq lookup; all other command discovery stays native.
            var extra = "command() { if [ \"$1\" = -v ] && [ \"${2:-}\" = jq ]; then return 1; fi; builtin command \"$@\"; }\n" +
                (mode == "resume" ? "C1008_RESUME=1" : "");
            var r = await f.Run(hostCase, extra, dryRun: mode == "preview");
            r.Exit.ShouldBe(2, r.Output);
            r.Output.ShouldContain("RecycleToolsMissing", Case.Sensitive, "final-recycle-guard: " + hostCase + "/" + mode);
            f.Removed.ShouldBeEmpty(); f.Trace.ShouldBeEmpty("missing jq precedes compose/status/removal");
            foreach (var (path, bytes) in sentinels) File.ReadAllText(path).ShouldBe(bytes, "unchanged volume sentinel");
            Directory.Exists(f.Root + "/server/recycle").ShouldBeFalse("missing jq precedes journal");
        }
    }
}

// Uses the existing HTTP/case boundaries unchanged. Generic C727 state permits
// real wrapper deploy/drain/all flows; C1008 state preserves shortcut/refusal semantics.
internal sealed class C1025RolloutFixture : IDisposable
{
    internal static string[] Shapes => ["deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp",
        "deploy-temp:same-sha", "redeploy-old:same-sha", "redeploy-old:preview", "redeploy-old:resume", "retire-temp:preview", "retire-temp:resume"];
    private readonly C1008WrapperFixture _wrapper = new();
    private readonly string _phase;
    private readonly string[] _options;
    internal JsonObject State => _wrapper.State;
    internal DateTime Started { get; private set; }

    internal C1025RolloutFixture(string shape)
    {
        OperatingSystem.IsLinux().ShouldBeTrue("C1025 prerequisite: native Linux");
        _phase = shape.Split(':')[0];
        var variant = shape.Split(':').ElementAtOrDefault(1) ?? "";
        _options = variant == "preview" ? ["-DryRun"] : variant == "resume" ? ["-ResumeRecycle", "c100800000000000000000000000000000001"] : [];
        if (_phase == "all" || (shape == "deploy-temp"))
        {
            State.Clear();
            foreach (var (key, value) in new JsonObject { ["scenario"]="c1025", ["sha"]=new string('a',40), ["clockMs"]=0,
                ["tempDeployed"]=false, ["oldDeployed"]=false, ["oldDraining"]=false, ["tempDraining"]=false,
                ["tempRetiredAt"]=null, ["tempContainer"]=false, ["tempOffline"]=false,
                ["tempRedirectTo"]="server2", ["tempRetireWhenIdle"]=true,
                ["faultRunner"]="", ["faultField"]="", ["faultKind"]="", ["faultValue"]=null, ["failVerify"]="" })
                State[key] = value?.DeepClone();
        }
        else if (_phase == "drain-old" || (_phase == "redeploy-old" && variant != "same-sha"))
        {
            _wrapper.MainDrained(); State["allowClear"] = true;
        }
        else if (_phase == "deploy-temp")
        {
            var vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-cases.json")))!;
            State["statuses"]!["server2-temp"] = vectors["tempAccepting"]!.DeepClone();
        }
    }
    internal void Sequence(params string[] outcomes) => State["hostJqSequence"] = new JsonArray(outcomes.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    internal void Busy()
    {
        if (State["scenario"]!.GetValue<string>() != "c1008")
        {
            State["faultRunner"]="server2-temp"; State["faultField"]="sessions"; State["faultKind"]="value"; State["faultValue"]=1;
        }
        else State["statuses"]![_phase == "redeploy-old" ? "server2" : "server2-temp"]!["sessions"] = 1;
    }
    internal async Task<(int Exit, string Output, JsonObject[] Trace)> Run()
    {
        if (File.Exists(_wrapper.TracePath)) File.Delete(_wrapper.TracePath);
        Started = DateTime.UtcNow;
        return await _wrapper.Run(_phase, _options);
    }
    internal void AssertChecks(JsonObject[] trace, int count)
    {
        var checks = trace.Where(x => x["kind"]!.GetValue<string>() == "prerequisite").ToArray();
        checks.Length.ShouldBe(count, "fresh-preflight: exact check attempts");
        foreach (var check in checks)
        {
            check["mode"]!.GetValue<string>().ShouldBe("check", "no implicit provision");
            check["name"]!.GetValue<string>().ShouldBe("host-jq-prerequisite");
        }
    }
    internal static string ReceiptPath(JsonObject check) => Path.Combine(check["evidenceRoot"]!.GetValue<string>(), "host-jq-" + check["phase"]!.GetValue<string>() + ".json");
    internal void AssertReceipt(JsonObject check, DateTime start, DateTime end)
    {
        var p = JsonNode.Parse(File.ReadAllText(ReceiptPath(check)))!;
        p["schema"]!.GetValue<int>().ShouldBe(1); p["lane"]!.GetValue<string>().ShouldBe("host");
        p["sourceSha"]!.GetValue<string>().ShouldBe(new string('a',40), "receipt-sha");
        p["runId"]!.GetValue<string>().ShouldBe(Path.GetFileName(check["evidenceRoot"]!.GetValue<string>()), "receipt-run");
        p["selectedPhase"]!.GetValue<string>().ShouldBe(_phase, "receipt-phase");
        p["phase"]!.GetValue<string>().ShouldBe(check["phase"]!.GetValue<string>(), "receipt-phase");
        p["mode"]!.GetValue<string>().ShouldBe("check", "receipt-mode"); p["sshExit"]!.GetValue<int>().ShouldBe(0);
        var observed = DateTime.Parse(p["observedAtUtc"]!.GetValue<string>()).ToUniversalTime();
        (observed >= start && observed <= end).ShouldBeTrue("receipt-time");
        p["lookupPath"]!.GetValue<string>().ShouldBe("/usr/local/bin/jq"); p["path"]!.GetValue<string>().ShouldBe("/usr/local/bin/jq");
        p["version"]!.GetValue<string>().ShouldBe("jq-1.7.1"); p["digest"]!.GetValue<string>().ShouldBe(HostJqFixture.Pin);
        p["uid"]!.GetValue<int>().ShouldBe(0); p["gid"]!.GetValue<int>().ShouldBe(0); p["permissions"]!.GetValue<string>().ShouldBe("755");
        p["trueExit"]!.GetValue<int>().ShouldBe(0); p["falseExit"]!.GetValue<int>().ShouldBe(1);
        p["installed"]!.GetValue<bool>().ShouldBeFalse("no implicit install"); p["outcome"]!.GetValue<string>().ShouldBe("existing");
    }
    internal static string Diagnosis(string fault) => fault switch { "missing"=>"HostJqMissing", "invalid"=>"HostJqInvalid",
        "ssh-failed"=>"HostJqRemoteRefused", "malformed-proof"=>"HostJqProofInvalid", "receipt-write-failed"=>"HostJqReceiptUnavailable", _=>throw new ArgumentException(fault) };
    public void Dispose() => _wrapper.Dispose();
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
    internal string HomeJq => Root + "/home/app/.local/bin/jq";
    internal string PathPrefix { get; set; } = "";
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
    private string _executingPhase = "check-host-jq";
    internal bool IncludeDestination { get; set; } = true;
    internal string? SyntheticProof { get; set; }
    internal string Trace => File.Exists(Root + "/trace") ? File.ReadAllText(Root + "/trace") : "";
    internal string[] InstallEffects => Trace.Split('\n').Where(x => new[] { "sudo ", "download ", "stage ", "publish ", "unlink " }.Any(x.StartsWith)).ToArray();
    internal string[] Receipts => Observations.Where(x => !x.EndsWith("-refused.json", StringComparison.Ordinal)).ToArray();
    internal string[] RefusalReceipts => Observations.Where(x => x.EndsWith("-refused.json", StringComparison.Ordinal)).ToArray();
    private string[] Observations => Directory.Exists(WrapperRoot + "/.antiphon/rolling-server2") ? Directory.GetFiles(WrapperRoot + "/.antiphon/rolling-server2", "host-jq-*.json", SearchOption.AllDirectories) : [];
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
        WriteTool("ssh", "printf 'ssh %s\\n' \"$*\" >> \"$HJ_ROOT/trace\"\necho $$ > \"$HJ_ROOT/ssh-pid\"\n/usr/bin/cat > \"$HJ_ROOT/stdin\"\nif [ \"$HJ_TRANSPORT\" = block-receipt ]; then\n for d in \"$HJ_WRAPPER\"/.antiphon/rolling-server2/*; do /usr/bin/mkdir \"$d/host-jq-${HJ_PHASE}.json\"; done\nfi\nif [ \"$HJ_TRANSPORT\" = block-refusal ]; then\n for d in \"$HJ_WRAPPER\"/.antiphon/rolling-server2/*; do /usr/bin/mkdir \"$d/host-jq-${HJ_PHASE}-refused.json\"; done\nfi\nprintf 'remote-C1025-secret\\n' >&2\nif [ \"$HJ_TRANSPORT\" = late ]; then /usr/bin/sleep .5; fi\nif [ \"$HJ_TRANSPORT\" = empty ]; then exit 0; fi\nif [ \"$HJ_TRANSPORT\" = truncated ]; then echo '{'; exit 0; fi\nif [ -f \"$HJ_ROOT/proof\" ]; then /usr/bin/cat \"$HJ_ROOT/proof\"; else /usr/bin/bash -s -- \"${!#}\" < \"$HJ_ROOT/stdin\"; code=$?; [ \"$code\" = 0 ] || exit \"$code\"; fi\n[ \"$HJ_TRANSPORT\" != ssh-exit ] || exit 255\n[ \"$HJ_TRANSPORT\" != ssh-refused ] || exit 2");
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
    internal void HomeShadow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HomeJq)!);
        File.Copy(_jq, HomeJq); Executable(HomeJq);
        PathPrefix = Path.GetDirectoryName(HomeJq)! + ":";
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
        source = ReplaceOnce(source, "$hostJqDestination = '/usr/local/bin/jq'", "$hostJqDestination = '" + Destination + "'");
        if (shortDeadline) source = ReplaceOnce(source, "{ 30000 } else { 180000 }", "{ 100 } else { 100 }");
        File.WriteAllText(Root + "/scripts/deploy-server2.ps1", source);
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-real.ps1"), Root + "/scripts/c590-real.ps1");
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/lib/runner-operator-token.ps1"), Root + "/scripts/lib/runner-operator-token.ps1");
        File.WriteAllText(Root + "/.gitignore", ".antiphon/\n/tools/\n/home/\n/foreign/\n/destination/\n/lock/\n/temporary/\n/payload\n/fault\n/dockerenv\n/trace\n/stdin\n/ssh-pid\n/proof\n/token\n/*-ready\n/*-release\n");
        (await Git("init", "-q")).Exit.ShouldBe(0);
        (await Git("config", "user.name", "fixture")).Exit.ShouldBe(0); (await Git("config", "user.email", "fixture@example.invalid")).Exit.ShouldBe(0);
        (await Git("add", ".")).Exit.ShouldBe(0); (await Git("commit", "-qm", "private baseline")).Exit.ShouldBe(0);
        RequestSha = (await Git("rev-parse", "HEAD")).Stdout.Trim(); ClearTrace();
    }
    internal async Task<HostJqRun> Wrapper(string phase)
    {
        _executingPhase = phase;
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
        p["lookupPath"]!.GetValue<string>().ShouldBe(Destination, "found-path-recorded");
        p["trueExit"]!.GetValue<int>().ShouldBe(0); p["falseExit"]!.GetValue<int>().ShouldBe(1);
        p["installed"]!.GetValue<bool>().ShouldBe(installed);
        p["outcome"]!.GetValue<string>().ShouldBe(installed ? "installed" : "existing");
        p["digest"]!.GetValue<string>().ShouldBe(installed ? Pin : Hash(Destination));
        p["version"]!.GetValue<string>().ShouldBe("jq-1.7.1");
        p["uid"]!.GetValue<int>().ShouldBe(0); p["gid"]!.GetValue<int>().ShouldBe(0); p["permissions"]!.GetValue<string>().ShouldBe("755");
    }
    internal void AssertRefusalReceipt(string phase, string mode, DateTime start, DateTime end)
    {
        var file = RefusalReceipts.Single(x => Path.GetFileName(x) == "host-jq-" + phase + "-refused.json");
        var p = JsonNode.Parse(File.ReadAllText(file))!;
        p["qualified"]!.GetValue<bool>().ShouldBeFalse("refusal-observation: cannot claim qualification");
        p["reason"]!.GetValue<string>().ShouldBe("HostJqPathUnapproved");
        p["lookupPath"]!.GetValue<string>().ShouldBe(HomeJq, "refusal-found-path");
        p["path"]!.GetValue<string>().ShouldBe(HomeJq, "refusal-resolved-path");
        p["sourceSha"]!.GetValue<string>().ShouldBe(RequestSha, "refusal-source-sha");
        p["runId"]!.GetValue<string>().ShouldBe(Path.GetFileName(Path.GetDirectoryName(file)), "refusal-run-id");
        p["selectedPhase"]!.GetValue<string>().ShouldBe(phase, "refusal-phase");
        p["phase"]!.GetValue<string>().ShouldBe(phase, "refusal-phase");
        p["mode"]!.GetValue<string>().ShouldBe(mode, "refusal-mode");
        p["lane"]!.GetValue<string>().ShouldBe("host"); p["sshExit"]!.GetValue<int>().ShouldBe(2);
        var observed = DateTime.Parse(p["observedAtUtc"]!.GetValue<string>()).ToUniversalTime();
        (observed >= start && observed <= end).ShouldBeTrue("refusal-time");
    }
    internal Process Start(string executable, params string[] args)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["PATH"] = PathPrefix + Tools + (!IncludeDestination || (File.Exists(Root + "/fault") && File.ReadAllText(Root + "/fault") == "path-absent") ? "" : ":" + Parent);
        psi.Environment["TMPDIR"] = Temp; psi.Environment["HJ_ROOT"] = Root; psi.Environment["HJ_DEST"] = Destination;
        psi.Environment["HJ_DEST_PARENT"] = Parent; psi.Environment["HJ_LOCK"] = Lock; psi.Environment["HJ_WRAPPER"] = WrapperRoot;
        psi.Environment["HJ_PHASE"] = _executingPhase;
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
