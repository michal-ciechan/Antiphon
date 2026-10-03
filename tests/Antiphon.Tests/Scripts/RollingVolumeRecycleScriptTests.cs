using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    public async Task C1008_Present_or_unknown_temp_keeps_null_refusal()
    {
        using (var good = new C1008WrapperFixture())
        {
            var run = await good.Run("retire-temp");
            run.Exit.ShouldBe(0, "retire-null-stays-closed: explicit null absent control; " + run.Output);
        }
        foreach (var field in new[] { "runnerSessions", "sessions", "queuedTasks", "available", "dispatchEligible", "acceptingNewWork", "retiredAt", "redirectTo", "draining", "retireWhenIdle" })
        {
            using var f = new C1008WrapperFixture();
            f.State["statuses"]!["server2-temp"]!.AsObject().Remove(field);
            var run = await f.Run("retire-temp");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("retire-null-stays-closed: missing " + field);
            run.Exit.ShouldBe(2);
        }
        using var present = new C1008WrapperFixture(); present.State["tempContainer"] = true;
        var blocked = await present.Run("retire-temp");
        blocked.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("retire-null-stays-closed: exited container retains null refusal");
        blocked.Output.ShouldContain("RunnerCounterUnknown");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Busy_routed_and_land_in_flight_refuse()
    {
        using (var good = new C1008WrapperFixture())
        {
            var run = await good.Run("retire-temp");
            run.Exit.ShouldBe(0, "recycle-work-gates: accepted closed census; " + run.Output);
        }
        foreach (var field in new[] { "sessions", "runnerSessions", "queuedTasks" })
        foreach (var value in new JsonNode?[] { JsonValue.Create(1), JsonValue.Create(-1), JsonValue.Create(0.5), JsonValue.Create("0"), JsonValue.Create(false) })
        {
            using var f = new C1008WrapperFixture(); f.State["statuses"]!["server2-temp"]![field] = value?.DeepClone();
            var run = await f.Run("retire-temp");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("recycle-work-gates: typed " + field);
        }
        foreach (var status in new[] { "Queued", "Dispatched", "Working", "Blocked", "Failed", "Succeeded" })
        {
            using var f = new C1008WrapperFixture();
            f.State["tasks"]!["items"]!.AsArray().Add(new JsonObject
            { ["id"] = "11111111-1111-1111-1111-111111111111", ["status"] = status, ["runnerId"] = "server2-temp",
                ["projectId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1", ["scopeSource"] = "Task",
                ["landRequestedAt"] = status == "Succeeded" ? "2026-10-03T09:00:00Z" : null, ["landStartedAt"] = null });
            var run = await f.Run("retire-temp");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("recycle-work-gates: retained owner/pending land " + status);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Same_sha_and_partial_retries_are_safe()
    {
        using var f = new C1008WrapperFixture();
        f.State["statuses"]!["server2"]!["draining"] = true;
        f.State["statuses"]!["server2"]!["redirectTo"] = "server2-temp";
        f.State["statuses"]!["server2"]!["acceptingNewWork"] = false;
        f.State["allowClear"] = true;
        var run = await f.Run("redeploy-old");
        run.Trace.Where(x => x["kind"]?.GetValue<string>() == "case").Select(x => x["name"]!.GetValue<string>())
            .ShouldBe(new[] { "verify-runner-caches" }, "recycle-wrapper-resume: healthy same SHA only verifies; " + run.Output);
        run.Exit.ShouldBe(0);
        foreach (var fail in new[] { "", "deploy", "cache", "no-resume" })
        {
            using var partial = new C1008WrapperFixture();
            partial.State["statuses"]!["server2"]!["draining"] = true;
            partial.State["statuses"]!["server2"]!["redirectTo"] = "server2-temp";
            partial.State["statuses"]!["server2"]!["acceptingNewWork"] = false;
            partial.State["statuses"]!["server2"]!["available"] = false;
            partial.State["statuses"]!["server2"]!["dispatchEligible"] = false;
            partial.State["statuses"]!["server2"]!["runnerSessions"] = null;
            partial.State["statuses"]!["server2-temp"] = JsonNode.Parse(File.ReadAllText(Path.Combine(
                DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-cases.json")))!["tempAccepting"]!.DeepClone();
            partial.State["allowClear"] = true;
            partial.State["failDeploy"] = fail == "deploy";
            partial.State["failVerify"] = fail == "cache" ? "server2" : "";
            var recovered = fail == "no-resume" ? await partial.Run("redeploy-old") :
                await partial.Run("redeploy-old", "-ResumeRecycle", "c100800000000000000000000000000000001");
            if (fail.Length == 0) recovered.Exit.ShouldBe(0, "recycle-wrapper-resume: request reaches the host's saved proof; " + recovered.Output);
            else
            {
                recovered.Exit.ShouldNotBe(0);
                recovered.Trace.Any(x => x["method"]?.GetValue<string>() == "POST").ShouldBeFalse(
                    "recycle-wrapper-resume: failure retains admission drain " + fail);
            }
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Option_manifest_is_strict()
    {
        using (var good = new C1008WrapperFixture())
        {
            var run = await good.Run("retire-temp", "-DryRun");
            run.Exit.ShouldBe(0, "recycle-manifest-strict: supported typed preview transport; " + run.Output);
        }
        foreach (var option in new[] { "-RecycleRunnerState", "-RecycleCaches" })
        {
            using var f = new C1008WrapperFixture(); var run = await f.Run("retire-temp", option);
            run.Trace.ShouldBeEmpty("recycle-manifest-strict: moved opt-ins rejected by parameter binding");
            run.Exit.ShouldNotBe(0);
        }
        foreach (var id in new[] { "../file", "C1008" + new string('a', 32), "c1008a;echo", "" })
        {
            using var f = new C1008WrapperFixture(); var run = await f.Run("retire-temp", "-ResumeRecycle", id);
            run.Trace.ShouldBeEmpty("recycle-manifest-strict: operation ID has no path authority");
            run.Exit.ShouldNotBe(0);
        }
        // Invoke both real validators with raw JSON before any switch binding.
        var raw = await C1008Process("pwsh", "-NoProfile", "-Command", $$"""
            $ErrorActionPreference='Stop'
            $repo='{{DelegateScriptRunner.RepoRoot.Replace("'", "''", StringComparison.Ordinal)}}'
            foreach ($pair in @(@('deploy-server2.ps1','Assert-RecycleContext'),@('c590-real.ps1','Assert-C1008BridgeContext'))) {
                $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo ('scripts/'+$pair[0])),[ref]$null,[ref]$null)
                $name=$pair[1]
                $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                if (-not $function) { throw 'real validator missing' }
                Invoke-Expression $function.Extent.Text
                foreach ($vector in @('false','true','string','null','number','omitted','unknown','volumes','project','operation')) {
                    $context='{"version":1,"project":"antiphon-runner-temp","operationId":"c100800000000000000000000000000000001","dryRun":false,"resume":false,"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"}' | ConvertFrom-Json
                    switch ($vector) {
                        true {$context.dryRun=$true}
                        string {$context.dryRun='false'}
                        null {$context.dryRun=$null}
                        number {$context.dryRun=0}
                        omitted {$context.PSObject.Properties.Remove('dryRun')}
                        unknown {$context | Add-Member -NotePropertyName recycleCaches -NotePropertyValue $false}
                        volumes {$context | Add-Member -NotePropertyName volumes -NotePropertyValue @('antiphon-runner_runner-state')}
                        project {$context.project='unsupported'}
                        operation {$context.operationId='../receipt'}
                    }
                    $accepted=$true
                    try { & $name -Context $context -Case 'retire-temp-runner' } catch {$accepted=$false}
                    if ($accepted -ne ($vector -in @('false','true'))) { throw "recycle-manifest-strict: $name $vector accepted=$accepted" }
                    Write-Output "$name/$vector checked"
                }
            }
            """);
        raw.Exit.ShouldBe(0, "recycle-manifest-strict: independent raw validators; " + raw.Output);
        foreach (var validator in new[] { "Assert-RecycleContext", "Assert-C1008BridgeContext" })
        foreach (var vector in new[] { "false", "true", "string", "null", "number", "omitted", "unknown", "volumes", "project", "operation" })
            raw.Output.ShouldContain(validator + "/" + vector + " checked");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Legacy_rolling_and_jq_rosters_remain()
    {
        foreach (var mode in new[] { "present", "absent", "missing-shell", "failing-shell" })
        {
            var run = await C1008Process("pwsh", "-NoProfile", "-File",
                Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/test-deploy-server2-jq.ps1"), "-Case", mode);
            run.Output.ShouldContain($"C973_JQ case={mode} assertions=31 failures=0", Case.Sensitive, "rolling-regressions-preserved: " + run.Output);
            if (mode == "present") run.Output.ShouldNotContain("C973_JQ_SKIPPED");
            run.Exit.ShouldBe(0);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Documentation_and_transport_pins_match()
    {
        using var f = new C1008HostFixture(main: false);
        var run = await f.Run("retire-temp-runner", "detect_lane() { LANE=nested; }");
        run.Output.ShouldContain("WrongLane", Case.Sensitive, "recycle-doc-contract: host operations refuse nested lane");
        f.Removed.ShouldBeEmpty();
        foreach (var script in new[] { "deploy-server2.ps1", "c590-real.ps1", "c590-remote.sh", "verify-docker-stack.ps1" })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", script)).All(x => x < 128)
                .ShouldBeTrue("recycle-doc-contract: ASCII transport " + script);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Refusal_receipts_do_not_leak_secrets()
    {
        using var f = new C1008WrapperFixture();
        f.State["taskError"] = "SENTINEL_C1008_HTTP_CREDENTIAL";
        var run = await f.Run("retire-temp");
        run.Output.ShouldContain("RecycleTaskCensusUnknown", Case.Sensitive, "recycle-receipt-custody: typed census refusal survives");
        run.Output.ShouldNotContain("SENTINEL_C1008_HTTP_CREDENTIAL");
        run.Output.ShouldNotContain("C1008_TEST_TOKEN_SENTINEL");
        run.Output.ShouldNotContain("UnhandledExit");
    }

    private static async Task<(int Exit, string Output)> C1008Process(string command, params string[] args)
    {
        var psi = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi)!; var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch { if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); } throw; }
        return (proc.ExitCode, await stdout + await stderr);
    }

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
        var request = run.Trace.Single(x => x["kind"]?.GetValue<string>() == "case" &&
            x["name"]?.GetValue<string>() == "retire-temp-runner");
        request["recycle"]!["dryRun"]!.GetValue<bool>().ShouldBeFalse();
        request["recycle"]!["project"]!.GetValue<string>().ShouldBe("antiphon-runner-temp");
        using var host = new C1008HostFixture(main: false);
        var operation = request["recycle"]!["operationId"]!.GetValue<string>();
        Regex.IsMatch(operation, "^c1008[0-9a-f]{32}$").ShouldBeTrue();
        var stamp = request["tempRetiredAt"]!.GetValue<string>();
        var retired = await host.Run("retire-temp-runner", $"C1008_OPERATION='{operation}'; C590_TEMP_RETIRED_AT='{stamp}'");
        retired.Exit.ShouldBe(0, "retire-absent-null-accepted: actual host entry using wrapper context; " + retired.Output);
        host.Removed.ShouldBe(new[] { "antiphon-runner-temp_work", "antiphon-runner-temp_runner-tmp",
            "antiphon-runner-temp_dind-data", "antiphon-runner-temp_runner-state" });
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
            ["scenario"] = "c1008", ["sha"] = vectors["sourceSha"]!.DeepClone(), ["oldDeployed"] = false,
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

internal sealed class C1008HostFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("c1008-host-").FullName;
    internal JsonObject Docker { get; }
    internal JsonObject Statuses { get; }
    internal string StatePath => Path.Combine(Root, "docker.json");
    internal string[] Removed => JsonNode.Parse(File.ReadAllText(StatePath))!["removed"]!.AsArray()
        .Select(x => x!.GetValue<string>()).ToArray();
    internal string[][] Trace => File.Exists(Path.Combine(Root, "docker-trace.jsonl"))
        ? File.ReadAllLines(Path.Combine(Root, "docker-trace.jsonl"))
            .Select(x => JsonNode.Parse(x)!.AsArray().Select(y => y!.GetValue<string>()).ToArray()).ToArray() : [];
    internal void ReloadDocker()
    {
        var persisted = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Docker.Clear();
        foreach (var (key, value) in persisted) Docker[key] = value?.DeepClone();
    }
    internal JsonObject Vectors { get; }
    internal JsonObject TaskScopes { get; } = new();
    internal JsonObject TaskDetails { get; } = new();

    internal C1008HostFixture(bool main = true)
    {
        Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1008-recycle-cases.json")))!.AsObject();
        TaskScopes["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"] = Vectors["emptyTasks"]!.DeepClone();
        Statuses = new JsonObject
        {
            ["server2"] = Vectors[main ? "mainDrained" : "mainAccepting"]!.DeepClone(),
            ["server2-temp"] = Vectors[main ? "tempAccepting" : "tempRetiredAbsent"]!.DeepClone()
        };
        var volumes = new JsonObject();
        var models = new JsonObject();
        foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" })
        {
            var model = new JsonObject();
            foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
            {
                var name = project + "_" + role;
                volumes[name] = Volume(name, new JsonObject
                { ["com.docker.compose.project"] = project, ["com.docker.compose.volume"] = role });
                model[role] = new JsonObject { ["name"] = name };
            }
            foreach (var (role, key) in new[] { ("nuget-packages", "runner-nuget-packages"),
                         ("nuget-scratch", "runner-nuget-scratch"), ("npm-content", "runner-npm-content") })
            {
                var name = "antiphon-runner-cache-" + role;
                if (!volumes.ContainsKey(name)) volumes[name] = Volume(name, new JsonObject
                { ["io.antiphon.owner"] = "server2-runner", ["io.antiphon.cache-schema"] = "1", ["io.antiphon.cache-role"] = role });
                model[key] = new JsonObject { ["name"] = name, ["external"] = true };
            }
            var mounts = new[] { ("work", "/work"), ("runner-state", "/state"), ("runner-tmp", "/tmp"),
                ("dind-data", "/var/lib/docker"), ("runner-nuget-packages", "/home/app/.nuget/packages"),
                ("runner-nuget-scratch", "/var/cache/antiphon/nuget-scratch"), ("runner-npm-content", "/home/app/.npm/_cacache") };
            JsonArray Mounts(int count) => new(mounts.Take(count).Select(x => (JsonNode)new JsonObject
                { ["type"] = "volume", ["source"] = x.Item1, ["target"] = x.Item2 }).ToArray());
            models[project] = new JsonObject { ["volumes"] = model, ["services"] = new JsonObject
                { ["session-runner"] = new JsonObject { ["volumes"] = Mounts(7) },
                    ["state-init"] = new JsonObject { ["volumes"] = Mounts(2) } } };
        }
        foreach (var name in new[] { "antiphon-runner_work-extra", "schoolrevision-staging", "openclaw-state" })
            volumes[name] = Volume(name, new JsonObject());
        var containers = new JsonArray();
        if (main)
        {
            containers.Add(Container('1', "antiphon-runner", "session-runner", true, "work", "runner-tmp", "dind-data", "runner-state"));
            containers.Add(Container('2', "antiphon-runner", "state-init", false, "work", "runner-state"));
        }
        containers.Add(Container('3', "antiphon-runner", "build-slots", true));
        Docker = new JsonObject { ["volumes"] = volumes, ["models"] = models,
            ["containers"] = containers, ["removed"] = new JsonArray(), ["fault"] = "" };
        Directory.CreateDirectory(Path.Combine(Root, "work"));
        Directory.CreateDirectory(Path.Combine(Root, "server/cache"));
        File.WriteAllText(Path.Combine(Root, "temp.env"), "RUNNER_GROK_STORE_DIR=/fixture/grok\n");
    }

    private JsonObject Volume(string name, JsonObject labels)
    {
        var mount = Path.Combine(Root, "volumes", name, "_data");
        Directory.CreateDirectory(mount);
        File.WriteAllText(Path.Combine(mount, "sentinel"), "sentinel:" + name + ":old\n");
        return new JsonObject { ["Name"] = name, ["Driver"] = "local", ["Options"] = new JsonObject(),
            ["CreatedAt"] = "2026-10-03T09:00:00Z", ["Mountpoint"] = mount, ["Labels"] = labels };
    }

    internal JsonObject Container(char id, string project, string service, bool running, params string[] roles) =>
        new() { ["Id"] = new string(id, 64), ["Image"] = "sha256:" + new string('a', 64),
            ["State"] = new JsonObject { ["Running"] = running, ["Status"] = running ? "running" : "exited" },
            ["Config"] = new JsonObject { ["Labels"] = new JsonObject
                { ["com.docker.compose.project"] = project, ["com.docker.compose.service"] = service } },
            ["Mounts"] = new JsonArray(roles.Select(role => (JsonNode)new JsonObject
                { ["Type"] = "volume", ["Name"] = project + "_" + role,
                    ["Source"] = Path.Combine(Root, "volumes", project + "_" + role, "_data"),
                    ["Destination"] = role == "work" ? "/work" : role == "runner-tmp" ? "/tmp" : role == "dind-data" ? "/var/lib/docker" : "/state", ["RW"] = true }).ToArray()) };

    internal async Task<(int Exit, string Output)> Run(string hostCase = "deploy-parent", string extra = "", bool dryRun = false)
    {
        File.WriteAllText(StatePath, Docker.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "statuses.json"), Statuses.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "tasks.json"), new JsonObject
            { ["scopes"] = TaskScopes.DeepClone(), ["details"] = TaskDetails.DeepClone() }.ToJsonString());
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var injection = $$"""
            C1008_FIXTURE_ROOT='{{Root}}'; export C1008_FIXTURE_ROOT
            SERVER2_ROOT='{{Root}}/server'; ROOT='{{Root}}'; EVIDENCE_ROOT='{{Root}}/evidence'; CASE_DIR="$EVIDENCE_ROOT/$CASE"
            SERVER2_ENV='{{Root}}/main.env'; SERVER2_TEMP_ENV='{{Root}}/temp.env'; mkdir -p "$CASE_DIR"
            RUNNER_GIT_USER_NAME=Fixture; RUNNER_GIT_USER_EMAIL=fixture@example.invalid
            C1008_OPERATION=c100800000000000000000000000000000001; C1008_CONTEXT=default; C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C1008_DRY_RUN={{(dryRun ? "1" : "0")}}; C590_TEMP_RETIRED_AT=2026-10-03T09:30:00Z
            docker() { bash '{{DelegateScriptRunner.RepoRoot}}/scripts/fixtures/c1008-fake-docker.sh' "$@"; }
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
        var script = Path.Combine(Root, "remote.sh");
        File.WriteAllText(script, source);
        var psi = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(script);
        psi.Environment["C590_CASE"] = hostCase;
        psi.Environment["C590_SHA"] = new string('a', 40);
        psi.Environment["C590_RUN"] = "c1008fixture";
        psi.Environment["C590_REEXEC"] = "1";
        psi.Environment["C604_SERVER_ORIGIN"] = "http://127.0.0.1:1";
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch { if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); } throw; }
        return (proc.ExitCode, await stdout + await stderr);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
