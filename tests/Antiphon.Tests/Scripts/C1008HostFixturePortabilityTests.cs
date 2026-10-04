using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class C1008HostFixturePortabilityTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1030_Linux_programs_preserve_legacy_bytes()
    {
        const string root = "/tmp/c1008-host-legacy", repo = "/fixture/repo";
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        foreach (var dry in new[] { false, true })
        foreach (var extra in new[] { "echo one\necho two\n", "echo one\r\necho two\r\n", "echo no-final-newline" })
            Encoding.UTF8.GetBytes(C1008HostFixture.ComposeProgram(root, repo, source, extra, dry))
                .ShouldBe(Encoding.UTF8.GetBytes(LegacyRun(root, repo, source, extra, dry)), "c1030-linux-run");
        foreach (var fault in new[] { "", "origin-failed", "head", "branch", "tag", "second-remote", "linked", "detached", "bare", "layouts", "dirty", "staged", "untracked" })
            Encoding.UTF8.GetBytes(RemoteScriptContractTests.C1008GitProgram(root, fault))
                .ShouldBe(Encoding.UTF8.GetBytes(LegacyGit(root, fault)), "c1030-linux-git");
        C1008HostFixture.HolderProgram(root).ShouldBe("exec 8>'/tmp/c1008-host-legacy/server/locks/rollout.lock'; flock 8; touch '/tmp/c1008-host-legacy/held'; read -r release", "c1030-linux-holder");
        using var f = new C1008HostFixture(windows: false);
        const string json = "{\"volumes\":{\"v\":{\"Mountpoint\":\"/tmp/c1008-host-legacy/volumes/v/_data\",\"Options\":null}},\"containers\":[{\"Mounts\":[{\"Type\":\"bind\",\"Source\":\"/tmp/c1008-host-legacy/work\",\"RW\":false}]}],\"fault\":\"inspect-error\"}";
        f.AdaptPaths(JsonNode.Parse(json)!.AsObject()).ToJsonString().ShouldBe(json, "c1030-linux-json");
        if (!OperatingSystem.IsWindows())
        {
            var digests = new[] { "390d5d349fd77f9a7bb805d2043d83cdf32daed8aa611b04c962fd89d0c88e99", "7cfea54fff2332b550be7c270878e171973edb32db0e6086df09cae5994cb9f2", "52d973dd3478468118230510fcfe1b042ae07bbc04f794cf30dc00d7a07faa41", "8ebb5d09bbda46e468824fc82c744a02488922b6c4fdf45bcd9f22c23cd624a0", "6354bd99a9ced9457597554cd587c51a6f8fbf0300a45216f858c6e54371f841", "46750b5e25728df8b24e059d724fecc60d68a45e030e040c09fc8e5d121453c6" };
            var i = 0;
            foreach (var dry in new[] { false, true })
            foreach (var extra in new[] { "echo one\necho two\n", "echo one\r\necho two\r\n", "echo no-final-newline" })
                Hash(C1008HostFixture.ComposeProgram(root, repo, source, extra, dry)).ShouldBe(digests[i++], "c1030-linux-run: pinned LF blob digest");
            Hash(RemoteScriptContractTests.C1008GitProgram(root, "")).ShouldBe("02e15c6b4fbf0a8db8af50acb9036408211fdb6a9a90ca6c3633a27bc0f68732", "c1030-linux-git");
            f.Options.ObserveLaunch = (entry, _, _) => entry.ShouldNotBe("convert", "c1030-linux-run: converter must not run");
            var result = await f.Run(extra: "exit 0"); result.Exit.ShouldBe(0, result.Output);
            File.ReadAllBytes(Path.Combine(f.Root, "remote.sh")).ShouldBe(
                Encoding.UTF8.GetBytes(LegacyRun(f.Root, DelegateScriptRunner.RepoRoot, source, "exit 0", false)), "c1030-linux-run: actual file");
        }
        // Observe the actual Node fake's argv to its child; expected bytes never come from its remapper.
        using var remap = new C1008HostFixture();
        var remapRoot = remap.ShellRoot;
        Directory.CreateDirectory(Path.Combine(remap.Root, "capture-bin"));
        File.WriteAllText(Path.Combine(remap.Root, "capture-bin/bash"), "#!/bin/bash\nprintf '%s' \"$2\" > " + Q(remapRoot + "/captured-program") + "\n");
        (await remap.Execute("probe", "chmod +x " + Q(remapRoot + "/capture-bin/bash"))).Exit.ShouldBe(0);
        remap.Options.ToolPath = remapRoot + "/capture-bin:/usr/local/bin:/usr/bin:/bin";
        remap.CopyFake();
        var pairs = new[]
        {
            ("cd /work; printf '%s' /worktrees /work/file", "cd " + remapRoot + "/work; printf '%s' /worktrees " + remapRoot + "/work/file"),
            ("cd \"/work\"; cd '/work'; echo /work\n", "cd \"" + remapRoot + "/work\"; cd '" + remapRoot + "/work'; echo " + remapRoot + "/work\n"),
            ("cd /work\r\nprintf '/work'", "cd " + remapRoot + "/work\r\nprintf '" + remapRoot + "/work'")
        };
        foreach (var (program, expected) in pairs)
        {
            remap.Docker["containers"] = new JsonArray(new JsonObject { ["Id"] = "audit", ["State"] = new JsonObject() });
            remap.Docker["auditProgram"] = program;
            File.WriteAllText(remap.StatePath, remap.Docker.ToJsonString());
            var result = await remap.Execute("probe", "/bin/bash " + Q(remapRoot + "/fake-docker.sh") + " start audit",
                environment: new Dictionary<string, string> { ["C1008_FIXTURE_ROOT"] = remapRoot, ["C1008_FIXTURE_WINDOWS"] = "0" });
            result.Exit.ShouldBe(0, "c1030-linux-remap; " + result.Output);
            File.ReadAllBytes(Path.Combine(remap.Root, "captured-program")).ShouldBe(Encoding.UTF8.GetBytes(expected), "c1030-linux-remap");
        }

    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1030_Windows_paths_convert_before_fixture_effects()
    {
        var spellings = new[] { @"Q:\fixture\plain", "Q:/fixture/plain", @"Q:\fixture\space here",
            @"Q:\fixture\it's", @"Q:\fixture\$cash", @"Q:\fixture\`tick`", @"Q:\fixture\café",
            @"Q:\fixture\it's $cash `tick` café", "Q:/fixture/it's $cash `tick` café", "/already/posix" };
        foreach (var spelling in spellings)
        {
            using var f = PreparedWindowsFixture();
            f.Options.NativeRoot = spelling;
            ConfigureConverter(f);
            var root = f.ShellRoot;
            var repo = f.ShellRepo;
            root.ShouldBe(OperatingSystem.IsWindows() ? NativeToLinux(f.Root) : f.Root, "c1030-root-converted");
            repo.ShouldBe(OperatingSystem.IsWindows() ? NativeToLinux(DelegateScriptRunner.RepoRoot) : DelegateScriptRunner.RepoRoot, "c1030-repo-converted");
            _ = f.ShellRoot; _ = f.ShellRepo;
            var calls = File.ReadAllLines(Path.Combine(f.Root, "converter.log"));
            calls.ShouldBe(new[] { Enc("-u") + " " + Enc(spelling), Enc("-u") + " " + Enc(f.Options.NativeRepo!) }, "c1030-converter-argv");
            var body = await f.Execute("probe", "printf reached > " + Q(root + "/body"));
            body.Exit.ShouldBe(0);
            File.ReadAllText(Path.Combine(f.Root, "body")).ShouldBe("reached", "c1030-root-converted: native file round trip");
        }
        foreach (var status in new[] { 0, 23 })
        foreach (var output in new[] { "absolute", "", "relative/path" })
        {
            using var f = PreparedWindowsFixture();
            var converted = output == "absolute" ? f.Root : output;
            f.Options.ConverterPrelude = "wslpath() { printf '%s' " + Q(converted) + "; return " + status + "; }\n";
            // On the sole successful root row, translate the independent repository correctly too.
            if (status == 0 && output == "absolute") ConfigureConverter(f);
            Exception? failure = null;
            try { var root = f.ShellRoot; await f.Execute("probe", "touch " + Q(root + "/body")); }
            catch (InvalidOperationException ex) { failure = ex; }
            var success = status == 0 && output == "absolute";
            (failure is null).ShouldBe(success, status != 0 ? "c1030-conversion-status" : "c1030-conversion-absolute");
            File.Exists(Path.Combine(f.Root, "body")).ShouldBe(success, "c1030-conversion-status: no body on refusal");
        }
        foreach (var mode in new[] { "absent", "unmapped" })
        {
            using var f = PreparedWindowsFixture();
            f.Options.ConverterPrelude = mode == "absent" ? "unset -f wslpath; PATH=/c1030-no-tools\n"
                : "wslpath() { printf /c1030-unreachable-drive; }\n";
            Should.Throw<InvalidOperationException>(() => _ = f.ShellRoot,
                mode == "absent" ? "c1030-conversion-status" : "c1030-root-converted: unreachable absolute result");
            File.Exists(Path.Combine(f.Root, "body")).ShouldBeFalse();
        }
        const string native = @"Q:\Fixture\It's a Repo";
        foreach (var spelling in new[] { native, "Q:/Fixture/It's a Repo", @"Q:\Fixture\It'\''s a Repo", "Q:/Fixture/It'\\''s a Repo" })
        foreach (var bodyRoot in new[] { spelling, spelling.ToUpperInvariant() })
        foreach (var alias in new string?[] { null, "root", "repo" })
            Should.Throw<InvalidOperationException>(() => RemoteScriptContractTests.PrepareLinuxShellScript(
                "# " + bodyRoot + "\necho body", alias, native, true), "c1030-raw-root");
        foreach (var alias in new[] { "", "ROOT", "path", "root;false" })
            Should.Throw<ArgumentException>(() => RemoteScriptContractTests.PrepareLinuxShellScript("echo body", alias, native, true), "c1030-alias");
        foreach (var alias in new string?[] { null, "root", "repo" })
            RemoteScriptContractTests.PrepareLinuxShellScript("echo body", alias, native, true).ShouldEndWith("echo body");
        using var first = PreparedWindowsFixture();
        using var second = PreparedWindowsFixture();
        first.ShellRoot.ShouldBe(OperatingSystem.IsWindows() ? NativeToLinux(first.Root) : first.Root, "c1030-instance-root");
        second.ShellRoot.ShouldBe(OperatingSystem.IsWindows() ? NativeToLinux(second.Root) : second.Root, "c1030-instance-root");
        first.ShellRoot.ShouldNotBe(second.ShellRoot, "c1030-instance-root");
        if (OperatingSystem.IsWindows())
        {
            using var nativeFixture = new C1008HostFixture();
            var result = await nativeFixture.Execute("probe", "test -f " + Q(nativeFixture.ShellRoot + "/.c1030-roundtrip"));
            result.Exit.ShouldBe(0, "c1030-root-converted: actual WSL round trip; " + result.Output);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1030_Windows_transport_ignores_ambient_launchers()
    {
        using var f = LiveWindowsFixture();
        var entries = new HashSet<string>();
        f.Options.ObserveLaunch = (entry, start, input) =>
        {
            entries.Add(entry);
            start.FileName.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"), "c1030-" + entry + "-launch");
            start.ArgumentList.ShouldBe(new[] { "-e", "/bin/bash", "-s" }, customMessage: "c1030-" + entry + "-launch");
            start.StandardInputEncoding!.GetPreamble().ShouldBeEmpty("c1030-stdin-bom");
            input.ShouldNotContain("\r", customMessage: "c1030-stdin-lf");
        };
        var root = f.ShellRoot;
        var poison = Path.Combine(f.Root, "poison"); Directory.CreateDirectory(poison);
        foreach (var name in new[] { "bash", "git", "sudo", "docker", "ssh", "curl" })
        {
            var file = Path.Combine(poison, name);
            File.WriteAllText(file, "#!/bin/bash\nprintf poison > " + Q(root + "/poison-ran") + "\nexit 99\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Poison is supplied to each physical parent environment; the stdin bootstrap must seal it.
        var executor = f.Options.SelectExecutor;
        f.Options.SelectExecutor = start => { executor?.Invoke(start); start.Environment["PATH"] = poison; };
        f.Options.BeforeSource = "printf '%s\\n' \"$C590_CASE\" \"$C590_SHA\" \"$C590_RUN\" \"$C590_REEXEC\" \"$C604_SERVER_ORIGIN\" > " + Q(root + "/environment") + "\n";
        await RemoteScriptContractTests.C1008GitGraph(f);
        File.Exists(Path.Combine(f.Root, "work/repo/.git/refs/heads/master")).ShouldBeTrue("c1030-git-launch: real commit");
        await using (var holder = await f.HoldLock())
        {
            var running = f.Run(extra: "flock() { touch \"$C1008_FIXTURE_ROOT/lock-wait\"; command flock \"$@\"; }\r\n");
            try
            {
                await WaitForFile(Path.Combine(f.Root, "lock-wait"), running);
                holder.Process.HasExited.ShouldBeFalse("c1030-holder-retained");
                running.IsCompleted.ShouldBeFalse("c1030-lock-exclusion");
                f.Trace.Any(a => a[0] is "stop" or "rm").ShouldBeFalse("c1030-lock-exclusion");
            }
            finally
            {
                try { (await holder.Release()).Exit.ShouldBe(0, "c1030-release-completed"); }
                finally { await running; }
            }
            var result = await running;
            result.Exit.ShouldBe(0, "c1030-release-completed; " + result.Output);
        }
        var values = File.ReadAllLines(Path.Combine(f.Root, "environment"));
        var expected = new[] { "deploy-parent", new string('a', 40), "c1008fixture", "1", "http://127.0.0.1:1" };
        var labels = new[] { "case", "sha", "run", "reexec", "origin" };
        for (var i = 0; i < expected.Length; i++) values[i].ShouldBe(expected[i], "c1030-env-" + labels[i]);
        File.Exists(Path.Combine(f.Root, "poison-ran")).ShouldBeFalse("c1030-sealed-tools");
        var bridge = await NativePwsh("$r=& {\n" + f.BridgeResumeCommand() + "\n}; exit $r");
        bridge.Exit.ShouldBe(0, "c1030-bridge-launch; " + bridge.Output);
        entries.IsSupersetOf(new[] { "run", "git", "holder", "bridge" }).ShouldBeTrue("c1030-entry-wiring");
        f.Removed.Length.ShouldBe(3, "c1030-bridge-launch: resume does not remove replacements");

        using (var missing = LiveWindowsFixture())
        {
            var empty = Path.Combine(missing.Root, "owned-tools"); Directory.CreateDirectory(empty);
            var shellEmpty = missing.ShellRoot + "/owned-tools";
            missing.Options.ToolPath = shellEmpty;
            var result = await missing.Execute("git", "printf BODY_RAN");
            result.Exit.ShouldBe(127, "c1030-sealed-tools");
            result.Output.ShouldContain("C1030_PREREQUISITE_MISSING git", customMessage: "c1030-sealed-tools");
            result.Output.ShouldNotContain("BODY_RAN");
        }
        using (var failed = LiveWindowsFixture())
        {
            var failure = await Should.ThrowAsync<InvalidOperationException>(() => failed.HoldLock("echo C1030_HOLDER_STDERR >&2; exit 23"));
            failure.Message.ShouldContain("exit=23", customMessage: "c1030-holder-exit");
            failure.Message.ShouldContain("C1030_HOLDER_STDERR", customMessage: "c1030-holder-stderr");
        }
        using (var flood = LiveWindowsFixture())
        {
            var result = await flood.Execute("probe", "head -c 1048576 /dev/zero; printf STDOUT_TAIL; head -c 1048576 /dev/zero >&2; printf STDERR_TAIL >&2");
            result.Exit.ShouldBe(0, "c1030-stdout-drained");
            result.Stdout.ShouldEndWith("STDOUT_TAIL", customMessage: "c1030-stdout-drained");
            result.Stderr.ShouldEndWith("STDERR_TAIL", customMessage: "c1030-stderr-drained");
        }
        await CheckInputBytes();
        foreach (var stage in new[] { "start", "readiness" })
        {
            using var failure = LiveWindowsFixture();
            _ = failure.ShellRoot;
            C1008Child? owned = null;
            void Inject(C1008Child child) { owned = child; throw new InvalidOperationException("C1030 injected " + stage); }
            if (stage == "start") failure.Options.AfterStart = Inject;
            else failure.Options.AtReadiness = Inject;
            try
            {
                await Should.ThrowAsync<InvalidOperationException>(() => failure.HoldLock(), "c1030-owned-tree-exited");
                owned.ShouldNotBeNull();
                Alive(owned!.Id, owned.StartTime).ShouldBeFalse("c1030-owned-tree-exited");
            }
            finally
            {
                failure.Options.AfterStart = null; failure.Options.AtReadiness = null;
                if (owned is not null) Rescue(owned.Id, owned.StartTime);
            }
        }
        using (var gated = LiveWindowsFixture())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gated.Options.BeforeExitWait = async () => { entered.TrySetResult(); await release.Task; };
            var child = await gated.StartChild("probe", "sleep 30\n", retainInput: false);
            var stop = child.Stop();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stop.IsCompleted.ShouldBeFalse("c1030-exit-awaited");
            }
            finally { release.TrySetResult(); await stop; Rescue(child.Id, child.StartTime); }
            Alive(child.Id, child.StartTime).ShouldBeFalse("c1030-owned-tree-exited");
        }
    }

    private static async Task CheckInputBytes()
    {
        using var f = LiveWindowsFixture();
        var root = f.ShellRoot;
        f.Options.SelectExecutor = start =>
        {
            start.FileName = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe") : "/bin/bash";
            start.ArgumentList.Clear();
            if (OperatingSystem.IsWindows()) { start.ArgumentList.Add("-e"); start.ArgumentList.Add("/bin/bash"); }
            start.ArgumentList.Add("-c"); start.ArgumentList.Add("/bin/cat > " + Q(root + "/stdin-bytes"));
        };
        (await f.Execute("probe", "echo one\r\necho two\r\n")).Exit.ShouldBe(0);
        var bytes = File.ReadAllBytes(Path.Combine(f.Root, "stdin-bytes"));
        bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }).ShouldBeFalse("c1030-stdin-bom");
        bytes.Contains((byte)'\r').ShouldBeFalse("c1030-stdin-lf");
        f.Options.SelectExecutor = null;
        if (!OperatingSystem.IsWindows()) UseLinuxExecutor(f.Options);
    }

    private static C1008HostFixture PreparedWindowsFixture()
    {
        var options = new C1008FixtureOptions { NativeRoot = @"Q:\fixture\root", NativeRepo = @"R:\fixture\repo" };
        UseLinuxExecutor(options);
        var fixture = new C1008HostFixture(windows: true, options: options);
        ConfigureConverter(fixture);
        return fixture;
    }

    private static C1008HostFixture LiveWindowsFixture(bool main = true, string? root = null)
    {
        if (OperatingSystem.IsWindows()) return new C1008HostFixture(main, root);
        var options = new C1008FixtureOptions { NativeRoot = @"Q:\fixture\root", NativeRepo = @"R:\fixture\repo" };
        UseLinuxExecutor(options);
        var fixture = new C1008HostFixture(main, root, windows: true, options: options);
        ConfigureConverter(fixture);
        return fixture;
    }

    private static void UseLinuxExecutor(C1008FixtureOptions options)
    {
        if (OperatingSystem.IsWindows()) return;
        options.SelectExecutor = start =>
        {
            start.FileName = "/bin/bash"; start.ArgumentList.Clear(); start.ArgumentList.Add("-s");
        };
    }

    private static void ConfigureConverter(C1008HostFixture fixture)
    {
        // The simulated converter's outputs are independently supplied physical paths.
        // Native Windows qualification uses LiveWindowsFixture with real wslpath instead.
        var physicalRoot = OperatingSystem.IsWindows() ? NativeToLinux(fixture.Root) : fixture.Root;
        var physicalRepo = OperatingSystem.IsWindows() ? NativeToLinux(DelegateScriptRunner.RepoRoot) : DelegateScriptRunner.RepoRoot;
        fixture.Options.ConverterPrelude = $$"""
            wslpath() {
                [ "$#" -eq 2 ] || return 29
                printf '%s %s\n' "$(printf '%s' "$1" | base64 -w0)" "$(printf '%s' "$2" | base64 -w0)" >> {{Q(physicalRoot + "/converter.log")}}
                case "$2" in
                    {{Q(fixture.Options.NativeRoot!)}}) printf '%s' {{Q(physicalRoot)}} ;;
                    {{Q(fixture.Options.NativeRepo!)}}) printf '%s' {{Q(physicalRepo)}} ;;
                    *) return 23 ;;
                esac
            }
            """ + "\n";
    }

    private static string NativeToLinux(string native)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var arg in new[] { "-e", "/bin/bash", "-s" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write("wslpath -u " + Q(native) + "\n"); process.StandardInput.Close();
        if (!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("expected-path conversion"); }
        process.ExitCode.ShouldBe(0, stderr.GetAwaiter().GetResult());
        var result = stdout.GetAwaiter().GetResult().TrimEnd('\n', '\r');
        result.ShouldStartWith("/"); return result;
    }
    private static string Enc(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Q(string value) => C1008HostFixture.Quote(value);
    private static async Task WaitForFile(string path, Task task)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(path) && !task.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(25);
        File.Exists(path).ShouldBeTrue("c1030-lock-exclusion: contender reached actual flock");
    }
    private static bool Alive(int id, DateTime start)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime() == start; }
        catch (ArgumentException) { return false; }
    }
    private static void Rescue(int id, DateTime start)
    {
        if (!Alive(id, start)) return;
        using var process = Process.GetProcessById(id); process.Kill(entireProcessTree: true); process.WaitForExit();
    }
    private static async Task<C1008Result> NativePwsh(string script)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh" : "/usr/local/bin/pwsh")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1030_Fixture_path_data_preserves_faults_and_round_trips()
    {
        using (var f = LiveWindowsFixture())
        {
            var native = f.Options.NativeRoot ?? f.Root;
            var shell = f.ShellRoot;
            var vectors = new JsonNode?[] { null, JsonValue.Create(true), JsonValue.Create(12), new JsonArray("x"),
                new JsonObject { ["path"] = native }, JsonValue.Create(native + "-sibling/work"),
                JsonValue.Create("/foreign/work"), JsonValue.Create(shell + "/already"), JsonValue.Create(native), JsonValue.Create(native + "/work") };
            foreach (var value in vectors)
            foreach (var options in new JsonNode?[] { null, JsonValue.Create("malformed"), new JsonObject { ["path"] = native } })
            {
                var model = new JsonObject
                {
                    ["volumes"] = new JsonObject { ["v"] = new JsonObject { ["Mountpoint"] = value?.DeepClone(),
                        ["Options"] = options?.DeepClone(), ["CreatedAt"] = "original", ["Labels"] = new JsonObject { ["fault"] = native } } },
                    ["containers"] = new JsonArray(new JsonObject { ["Mounts"] = new JsonArray(
                        new JsonObject { ["Type"] = "bind", ["Source"] = value?.DeepClone(), ["RW"] = false },
                        new JsonObject { ["Type"] = "volume", ["Source"] = value?.DeepClone() }) }),
                    ["runner"] = new JsonObject { ["Mounts"] = new JsonArray(new JsonObject { ["Source"] = value?.DeepClone() }) },
                    ["fault"] = native
                };
                var before = model.DeepClone();
                var expected = model.DeepClone().AsObject();
                if (value is JsonValue json && json.TryGetValue<string>(out var text) && (text == native || text == native + "/work"))
                {
                    var translated = text == native ? shell : shell + "/work";
                    expected["volumes"]!["v"]!["Mountpoint"] = translated;
                    expected["containers"]![0]!["Mounts"]![0]!["Source"] = translated;
                    expected["containers"]![0]!["Mounts"]![1]!["Source"] = translated;
                    expected["runner"]!["Mounts"]![0]!["Source"] = translated;
                }
                var actual = f.AdaptPaths(model);
                JsonNode.DeepEquals(actual["volumes"]!["v"]!["Mountpoint"], expected["volumes"]!["v"]!["Mountpoint"]).ShouldBeTrue("c1030-mountpoint");
                JsonNode.DeepEquals(actual["containers"], expected["containers"]).ShouldBeTrue("c1030-mount-source");
                JsonNode.DeepEquals(actual, expected).ShouldBeTrue("c1030-faults-preserved; c1030-root-boundary");
                JsonNode.DeepEquals(model, before).ShouldBeTrue("c1030-input-unchanged");
                f.WriteRecreated(model);
                JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(f.Root, "recreated.json"))), expected).ShouldBeTrue("c1030-recreated");
                File.WriteAllText(f.StatePath, actual.ToJsonString()); f.ReloadDocker();
                JsonNode.DeepEquals(f.AdaptPaths(f.Docker), actual).ShouldBeTrue("c1030-roundtrip");
                // Absent fields are distinct from explicit null fields.
                model["volumes"]!["v"]!.AsObject().Remove("Mountpoint");
                model["volumes"]!["v"]!.AsObject().Remove("Options");
                f.AdaptPaths(model)["volumes"]!["v"]!.AsObject().ContainsKey("Mountpoint").ShouldBeFalse("c1030-faults-preserved");
                f.AdaptPaths(model)["volumes"]!["v"]!.AsObject().ContainsKey("Options").ShouldBeFalse("c1030-faults-preserved");
            }
            f.CopyFake("#!/bin/bash\r\nprintf copied\r\n");
            var fakeBytes = File.ReadAllBytes(Path.Combine(f.Root, "fake-docker.sh"));
            fakeBytes.Contains((byte)'\r').ShouldBeFalse("c1030-fake-lf");
            fakeBytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }).ShouldBeFalse("c1030-fake-bom");
        }
        foreach (var main in new[] { false, true })
        foreach (var dry in new[] { false, true })
        {
            using var f = LiveWindowsFixture(main);
            var result = await f.Run(main ? "deploy-parent" : "retire-temp-runner", dryRun: dry);
            result.Exit.ShouldBe(0, "c1030-roundtrip; " + result.Output);
            f.Removed.Length.ShouldBe(dry ? 0 : main ? 3 : 4, "c1030-roundtrip");
            f.ReloadDocker();
            result = await f.Run(main ? "deploy-parent" : "retire-temp-runner", extra: dry ? "" : "C1008_RESUME=1", dryRun: dry);
            result.Exit.ShouldBe(0, "c1030-roundtrip: persisted resume; " + result.Output);
        }
        // Exercise both fields of the independently persisted replacement generation.
        using (var f = LiveWindowsFixture())
        {
            var replacement = new JsonObject();
            foreach (var name in new[] { "antiphon-runner_work", "antiphon-runner_runner-tmp", "antiphon-runner_dind-data" })
            {
                replacement[name] = f.Docker["volumes"]![name]!.DeepClone();
                replacement[name]!["CreatedAt"] = "2026-10-03T10:00:00Z";
            }
            f.WriteRecreated(new JsonObject { ["volumes"] = replacement, ["runner"] = f.Container('5', "antiphon-runner", "session-runner", true,
                "work", "runner-tmp", "dind-data", "runner-state") });
            var interrupted = await f.Run(extra: "build_server2_images() { node -e 'const fs=require(\"fs\"),p=process.argv[1],s=JSON.parse(fs.readFileSync(p)),r=JSON.parse(fs.readFileSync(process.argv[2]));Object.assign(s.volumes,r.volumes);s.containers.push(r.runner);fs.writeFileSync(p,JSON.stringify(s))' \"$C1008_FIXTURE_ROOT/docker.json\" \"$C1008_FIXTURE_ROOT/recreated.json\"; c1008_record_recreated; write_result false InterruptedVerification 2; }");
            interrupted.Exit.ShouldBe(2, "c1030-recreated"); f.ReloadDocker();
            (await f.Run(extra: "C1008_RESUME=1")).Exit.ShouldBe(0, "c1030-recreated");
            f.Removed.Length.ShouldBe(3, "c1030-recreated: no second generation deletion");
        }
        var quotedRoot = Path.Combine(Path.GetTempPath(), "c1008-host-" + Guid.NewGuid().ToString("N") + " it's $cash `tick` café $(touch injected)");
        var foreign = Directory.CreateTempSubdirectory("c1030-foreign-").FullName;
        File.WriteAllText(Path.Combine(foreign, "sentinel"), "foreign-owned");
        var quoted = LiveWindowsFixture(root: quotedRoot);
        try
        {
            await RemoteScriptContractTests.C1008GitGraph(quoted, "layouts");
            var result = await quoted.Run();
            result.Exit.ShouldBe(0, "c1030-quoted-audit; " + result.Output);
            var journal = JsonNode.Parse(File.ReadAllText(Path.Combine(quoted.Root, "server/recycle/c100800000000000000000000000000000001.json")))!;
            var hashes = System.Text.RegularExpressions.Regex.Matches(journal["audit"]!.GetValue<string>(), "repo=([0-9a-f]{64})")
                .Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
            hashes.ShouldBe(new[] { "repo", "linked clean", "standalone", "bare.git" }
                .Select(name => Hash(quoted.ShellRoot + "/work/" + name)).Order().ToArray(), "c1030-audit-hashes");
            File.Exists(Path.Combine(quoted.Root, "injected")).ShouldBeFalse("c1030-quoted-audit");
            using var lockedFixture = LiveWindowsFixture();
            await RemoteScriptContractTests.C1008GitGraph(lockedFixture, "layouts");
            await lockedFixture.WriteLinkedLock();
            var locked = await lockedFixture.Execute("git", "cd " + Q(lockedFixture.ShellRoot + "/work/linked clean") + "; test -f \"$(git rev-parse --absolute-git-dir)/index.lock\"");
            locked.Exit.ShouldBe(0, "c1030-linked-lock");
            // A new operation uses the same real repositories and retains the original refusal.
            var lockRefusal = await lockedFixture.Run();
            lockRefusal.Output.ShouldContain("RecycleGitAuditUnknown", customMessage: "c1030-linked-lock");
            var shellForeign = OperatingSystem.IsWindows() ? NativeToLinux(foreign) : foreign;
            await quoted.CreateEscapingLink(shellForeign);
            var link = await quoted.Execute("probe", "test -L " + Q(quoted.ShellRoot + "/work/escape") +
                " && readlink -- " + Q(quoted.ShellRoot + "/work/escape"));
            link.Exit.ShouldBe(0, "c1030-escaping-link");
            link.Stdout.TrimEnd('\n').ShouldBe(shellForeign, "c1030-escaping-link");
            using var linkFixture = LiveWindowsFixture();
            await RemoteScriptContractTests.C1008GitGraph(linkFixture);
            await linkFixture.CreateEscapingLink();
            var linkRefusal = await linkFixture.Run();
            linkRefusal.Output.ShouldContain("RecycleGitAuditUnknown", customMessage: "c1030-escaping-link");
            await CheckFakeRefusalsAndRemap(quoted);
        }
        finally
        {
            quoted.Dispose();
            try
            {
                Directory.Exists(quotedRoot).ShouldBeFalse("c1030-owned-cleanup");
                File.ReadAllText(Path.Combine(foreign, "sentinel")).ShouldBe("foreign-owned", "c1030-foreign-target-preserved");
            }
            finally { if (Directory.Exists(foreign)) Directory.Delete(foreign, true); }
        }
    }

    private static async Task CheckFakeRefusalsAndRemap(C1008HostFixture f)
    {
        f.CopyFake();
        var shell = f.ShellRoot;
        f.Docker["containers"] = new JsonArray(new JsonObject { ["Id"] = "audit", ["State"] = new JsonObject() });
        f.Docker["auditProgram"] = "printf '%s\\n' /worktrees scratch/work /work \"/work/file\" '/work/file'";
        File.WriteAllText(f.StatePath, f.AdaptPaths(f.Docker).ToJsonString());
        var environment = new Dictionary<string, string> { ["C1008_FIXTURE_ROOT"] = shell, ["C1008_FIXTURE_WINDOWS"] = "1" };
        var remap = await f.Execute("probe", "/bin/bash " + Q(shell + "/fake-docker.sh") + " start audit", environment: environment);
        remap.Exit.ShouldBe(0, "c1030-quoted-audit; " + remap.Output);
        remap.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe(
            new[] { "/worktrees", "scratch/work", shell + "/work", shell + "/work/file", shell + "/work/file" }, "c1030-windows-remap-boundary");
        var before = File.ReadAllText(f.StatePath);
        var unknown = await f.Execute("probe", "/bin/bash " + Q(shell + "/fake-docker.sh") + " unknown-operation", environment: environment);
        unknown.Exit.ShouldBe(2, "c1030-fake-unknown");
        File.ReadAllText(f.StatePath).ShouldBe(before, "c1030-fake-unknown");
        var invalid = Directory.CreateDirectory(Path.Combine(f.Root, "invalid-root")).FullName;
        File.WriteAllText(Path.Combine(invalid, "docker.json"), before);
        environment["C1008_FIXTURE_ROOT"] = shell + "/invalid-root";
        var refused = await f.Execute("probe", "/bin/bash " + Q(shell + "/fake-docker.sh") + " info", environment: environment);
        refused.Exit.ShouldNotBe(0, "c1030-fake-root");
        refused.Output.ShouldContain("fixture root invalid", customMessage: "c1030-fake-root");
        File.Exists(Path.Combine(invalid, "docker-trace.jsonl")).ShouldBeFalse("c1030-fake-root");
        File.ReadAllText(Path.Combine(invalid, "docker.json")).ShouldBe(before, "c1030-fake-root");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // Independent literals copied before refactoring from the pinned post-C980 source.
    private static string LegacyRun(string Root, string repoRoot, string source, string extra, bool dryRun)
    {
        var injection = $$"""
            C1008_FIXTURE_ROOT='{{Root}}'; export C1008_FIXTURE_ROOT
            SERVER2_ROOT='{{Root}}/server'; ROOT='{{Root}}'; EVIDENCE_ROOT='{{Root}}/evidence'; CASE_DIR="$EVIDENCE_ROOT/$CASE"
            SERVER2_ENV='{{Root}}/main.env'; SERVER2_TEMP_ENV='{{Root}}/temp.env'; mkdir -p "$CASE_DIR"
            RUNNER_GIT_USER_NAME=Fixture; RUNNER_GIT_USER_EMAIL=fixture@example.invalid
            C1008_OPERATION=c100800000000000000000000000000000001; C1008_CONTEXT=default; C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C1008_DRY_RUN={{(dryRun ? "1" : "0")}}; C590_TEMP_RETIRED_AT=2026-10-03T09:30:00Z
            docker() { bash '{{repoRoot}}/scripts/fixtures/c1008-fake-docker.sh' "$@"; }
            compose_host() { docker compose -p "$HOST_PROJECT" "$@"; }
            compose_temp() { docker compose -p "$TEMP_PROJECT" "$@"; }
            detect_lane() { LANE=host; }
            ensure_dirs() { printf 'MUTATION ensure_dirs\n'; }
            ensure_checkout() { printf 'MUTATION ensure_checkout\n'; }
            ensure_runner_boot_files() { printf 'MUTATION boot_files\n'; }
            retire_c590_leftovers() { :; }
            broker_sha12() { echo aaaaaaaaaaaa; }
            build_server2_images() { write_result true '' 0; }
            c849_lock() { :; }
            c849_budget_gate() { :; }
            c849_status_body() { node -e 'process.stdout.write(JSON.stringify(JSON.parse(require("fs").readFileSync(process.argv[1]))[process.argv[2]]))' '{{Root}}/statuses.json' "$1"; }
            c1008_http() { node -e 'const fs=require("fs"),s=JSON.parse(fs.readFileSync(process.argv[1])),p=process.argv[2],u=new URL(p,"http://fixture.invalid"),v=u.searchParams.has("projectId")?s.scopes[u.searchParams.get("projectId")]:s.details[u.pathname.split("/").at(-1)];if(!v)process.exit(2);process.stdout.write(JSON.stringify(v))' '{{Root}}/tasks.json' "$1"; }
            sudo() { [ "$1" = -n ] && shift; if [ "$1" = install ]; then mkdir -p "${@: -1}"; elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 99999999 1 25000000 1%% /fixture\n'; else "$@"; fi; }
            {{extra}}
            """;
        source = source.Replace("trap 'ec=$?;", injection + "\ntrap 'ec=$?;", StringComparison.Ordinal);
        return source;
    }
    private static string LegacyGit(string root, string fault)
    {
        var script = $$"""
            set -e
            git init -q --bare '{{root}}/origin'
            git init -q -b master '{{root}}/work/repo'
            git -C '{{root}}/work/repo' config user.name Fixture
            git -C '{{root}}/work/repo' config user.email fixture@example.invalid
            git -C '{{root}}/work/repo' remote add origin '{{root}}/origin'
            echo A > '{{root}}/work/repo/file'
            git -C '{{root}}/work/repo' add file
            git -C '{{root}}/work/repo' commit -qm A
            git -C '{{root}}/work/repo' push -q origin master
            """;
        if (fault == "origin-failed") script += $"\ngit -C '{root}/work/repo' remote set-url origin '{root}/missing-origin'\n";
        if (fault is "head" or "branch" or "tag" or "second-remote" or "linked" or "detached" or "bare") script += $$"""

            git -C '{{root}}/work/repo' commit -qm B --allow-empty
            {{(fault == "branch" ? $"git -C '{root}/work/repo' branch unpublished; git -C '{root}/work/repo' checkout -q --detach HEAD~1" : fault == "tag" ? $"git -C '{root}/work/repo' tag unpublished; git -C '{root}/work/repo' reset -q --hard HEAD~1" : "")}}
            """;
        if (fault == "layouts") script += $"\ngit -C '{root}/work/repo' worktree add -q --detach '{root}/work/linked clean' HEAD\ngit clone -q '{root}/origin' '{root}/work/standalone'\ngit clone -q --mirror '{root}/origin' '{root}/work/bare.git'\n";
        if (fault == "second-remote") script += $"\ngit init -q --bare '{root}/second'\ngit -C '{root}/work/repo' remote add other '{root}/second'\ngit -C '{root}/work/repo' push -q other master\n";
        if (fault is "linked" or "detached") script += $"\ngit -C '{root}/work/repo' worktree add -q --detach '{root}/work/unpublished space' HEAD\ngit -C '{root}/work/repo' reset -q --hard HEAD~1\n";
        if (fault == "bare") script += $"\ngit clone -q --mirror '{root}/work/repo' '{root}/work/bare.git'\ngit -C '{root}/work/bare.git' remote set-url origin '{root}/origin'\ngit -C '{root}/work/bare.git' branch unpublished HEAD\ngit -C '{root}/work/bare.git' update-ref refs/heads/master HEAD~1\ngit -C '{root}/work/repo' reset -q --hard HEAD~1\n";
        if (fault is "dirty" or "staged") script += $"\necho B >> '{root}/work/repo/file'\n";
        if (fault == "staged") script += $"git -C '{root}/work/repo' add file\n";
        if (fault == "untracked") script += $"\necho B > '{root}/work/repo/new'\n";
        return script;
    }
}
