using System.Diagnostics;
using System.Text;
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
        foreach (var (field, value) in new (string, JsonNode?)[] {
            ("retiredAt", JsonValue.Create("not-a-date")), ("retiredAt", null),
            ("draining", JsonValue.Create(false)), ("retireWhenIdle", JsonValue.Create(false)),
            ("redirectTo", JsonValue.Create("foreign")), ("redirectTo", null),
            ("available", JsonValue.Create(true)), ("dispatchEligible", JsonValue.Create(true)),
            ("acceptingNewWork", JsonValue.Create(true)), ("runnerSessions", JsonValue.Create("0")),
            ("runnerSessions", JsonValue.Create(false)) })
        {
            using var invalid = new C1008WrapperFixture(); invalid.State["statuses"]!["server2-temp"]![field] = value?.DeepClone();
            var refused = await invalid.Run("retire-temp");
            refused.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("retire-null-stays-closed: corrupt " + field);
            refused.Exit.ShouldBe(2);
        }
        using var failedCensus = new C1008WrapperFixture(); failedCensus.State["censusError"] = true;
        var unknown = await failedCensus.Run("retire-temp");
        unknown.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("retire-null-stays-closed: empty failed census is unknown");
        unknown.Output.ShouldContain("TempContainerCensusUnavailable");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Busy_routed_and_land_in_flight_refuse()
    {
        foreach (var vector in new[] { "closed", "active-excluded", "unscoped", "withheld", "wrong-count", "inconsistent" })
        {
            using var scoped = new C1008WrapperFixture();
            var root = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
            var other = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2";
            var id = "22222222-2222-2222-2222-222222222222";
            var first = scoped.State["tasks"]!.DeepClone();
            var second = first.DeepClone(); second["scope"]!["projectId"] = other;
            var row = new JsonObject { ["id"] = id, ["status"] = vector is "active-excluded" or "unscoped" ? "Queued" : "Succeeded",
                ["runnerId"] = "server2-temp", ["projectId"] = vector == "unscoped" ? null : other,
                ["scopeSource"] = vector == "unscoped" ? "None" : "Task", ["landRequestedAt"] = null, ["landStartedAt"] = null };
            if (vector == "unscoped") first["items"]!.AsArray().Add(row.DeepClone());
            else {
                first["excluded"]!["total"] = vector == "wrong-count" ? 2 : 1;
                first["excluded"]!["byProject"]!.AsArray().Add(new JsonObject { ["projectId"] = other, ["count"] = 1 });
                if (vector != "withheld") second["items"]!.AsArray().Add(row.DeepClone());
                if (vector == "inconsistent") first["items"]!.AsArray().Add(new JsonObject { ["id"] = id, ["status"] = "Succeeded",
                    ["runnerId"] = "other", ["projectId"] = null, ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null });
            }
            scoped.State["taskScopes"] = new JsonObject { [root] = first, [other] = second };
            scoped.State["details"] = new JsonObject { [id] = new JsonObject {
                ["summary"] = vector == "inconsistent" ? first["items"]![0]!.DeepClone() : row.DeepClone(), ["landRequest"] = null } };
            var result = await scoped.Run("retire-temp");
            result.Exit.ShouldBe(vector == "closed" ? 0 : 2, "recycle-work-gates: actual scoped closure " + vector + "; " + result.Output);
            if (vector != "closed") result.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
        }
        using (var held = new C1008WrapperFixture())
        {
            held.State["statuses"]!["server2"]!["draining"] = true;
            held.State["statuses"]!["server2"]!["redirectTo"] = "server2-temp";
            held.State["statuses"]!["server2"]!["acceptingNewWork"] = false;
            held.State["allowClear"] = true;
            var file = Path.Combine(held.Root, "rollout.lock"); held.State["rolloutLockFile"] = file;
            var lease = File.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var running = held.Run("redeploy-old");
            try {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((!File.Exists(held.TracePath) || !File.ReadAllText(held.TracePath).Contains("lock-request", StringComparison.Ordinal)) && DateTime.UtcNow < deadline)
                    await Task.Delay(25);
                File.ReadAllText(held.TracePath).ShouldContain("lock-request", Case.Sensitive, "recycle-work-gates: real admission lock barrier reached");
                await Task.Delay(150);
                File.ReadAllText(held.TracePath).ShouldNotContain("\"POST\"", Case.Sensitive, "recycle-work-gates: held rollout lock excludes POST entry");
            } finally { lease.Dispose(); await running; }
            (await running).Exit.ShouldBe(0);
        }
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
        foreach (var fault in new[] { "sessions-null", "runnerSessions-null", "queuedTasks-null", "omitted", "accepting", "drain", "redirect", "counterpart" })
        {
            using var invalid = new C1008WrapperFixture(); invalid.MainDrained();
            var status = invalid.State["statuses"]!["server2"]!;
            if (fault.EndsWith("-null", StringComparison.Ordinal)) status[fault[..^5]] = null;
            else if (fault == "omitted") status.AsObject().Remove("runnerSessions");
            else if (fault == "accepting") status["acceptingNewWork"] = true;
            else if (fault == "drain") status["draining"] = false;
            else if (fault == "redirect") status["redirectTo"] = null;
            else invalid.State["statuses"]!["server2-temp"]!["acceptingNewWork"] = false;
            var refused = await invalid.Run("redeploy-old");
            refused.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("recycle-work-gates: main proof " + fault);
            refused.Exit.ShouldBe(2);
        }
        foreach (var state in new[] { "Queued", "Held", "Running", "NeedsResolution", "Unknown", "omitted", "started" })
        {
            using var invalid = new C1008WrapperFixture();
            var row = new JsonObject { ["id"] = "11111111-1111-1111-1111-111111111111", ["status"] = "Succeeded", ["runnerId"] = "other",
                ["projectId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1", ["scopeSource"] = "Task", ["landRequestedAt"] = null,
                ["landStartedAt"] = state == "started" ? "2026-10-03T09:00:00Z" : null };
            invalid.State["tasks"]!["items"]!.AsArray().Add(row);
            var detail = new JsonObject { ["summary"] = row.DeepClone() };
            if (state != "omitted") detail["landRequest"] = state == "started" ? null : new JsonObject { ["state"] = state };
            invalid.State["details"] = new JsonObject { [row["id"]!.GetValue<string>()] = detail };
            var refused = await invalid.Run("retire-temp");
            refused.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("recycle-work-gates: succeeded pending land " + state);
            refused.Exit.ShouldBe(2);
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
        using (var incomplete = new C1008WrapperFixture())
        {
            incomplete.State["incompleteRecycle"] = true;
            var refused = await incomplete.Run("redeploy-old");
            refused.Trace.Any(x => x["method"]?.GetValue<string>() == "POST").ShouldBeFalse("recycle-wrapper-resume: healthy same SHA cannot hide an incomplete journal");
            refused.Output.ShouldContain("RecycleResumeRequired");
        }
        foreach (var fail in new[] { "", "deploy", "cache", "wrong-sha", "no-resume" })
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
            partial.State["wrongDeploySha"] = fail == "wrong-sha";
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
        using (var legacy = new C1008HostFixture())
        {
            var direct = await legacy.Run(extra: "C1008_CONTEXT=''");
            direct.Exit.ShouldBe(0, "recycle-manifest-strict: direct deploy-parent remains available; " + direct.Output);
            legacy.Removed.ShouldBeEmpty("recycle-manifest-strict: no context cannot authorize volume removal");
            legacy.Trace.Any(a => a[0] == "stop" || a[0] == "rm").ShouldBeFalse();
        }
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
            var arguments = new List<string> { "-NoProfile", "-File",
                Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/test-deploy-server2-jq.ps1"), "-Case", mode };
            if (mode == "present")
            {
                arguments.Add("-RequireJq");
                arguments.ShouldContain("-RequireJq", customMessage: "c983-consumer-requires-jq");
            }
            var run = await C1008Process("pwsh", arguments.ToArray());
            Console.WriteLine(run.Output);
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
        using (var git = new C1008HostFixture())
        {
            Directory.CreateDirectory(Path.Combine(git.Root, "work", "SENTINEL_C1008_FILENAME_CREDENTIAL", ".git"));
            git.Docker["gitStderr"] = "SENTINEL_C1008_GIT_CREDENTIAL";
            var refused = await git.Run();
            refused.Exit.ShouldBe(2, "recycle-receipt-custody: malformed Git is refused");
            refused.Output.ShouldContain("RecycleGitAuditUnknown");
            refused.Output.ShouldNotContain("UnhandledExit");
            foreach (var publicText in new[] { refused.Output,
                File.ReadAllText(Path.Combine(git.Root, "server/recycle/c100800000000000000000000000000000001.json")) })
            {
                publicText.ShouldNotContain("SENTINEL_C1008_FILENAME_CREDENTIAL");
                publicText.ShouldNotContain("SENTINEL_C1008_GIT_CREDENTIAL");
            }
            git.Removed.ShouldBeEmpty();
        }
        using (var host = new C1008HostFixture())
        {
            host.Docker["containers"]![0]!["Config"]!["Env"] = new JsonArray("SENTINEL_C1008_DOCKER_CREDENTIAL");
            host.Docker["containers"]![0]!["Config"]!["Labels"]!["foreign.secret"] = "SENTINEL_C1008_LABEL_CREDENTIAL";
            var accepted = await host.Run();
            accepted.Exit.ShouldBe(0);
            var record = File.ReadAllText(Path.Combine(host.Root, "server/recycle/c100800000000000000000000000000000001.json"));
            record.ShouldNotContain("SENTINEL_C1008_DOCKER_CREDENTIAL", Case.Sensitive, "recycle-receipt-custody: approved inspection fields only");
            record.ShouldNotContain("SENTINEL_C1008_LABEL_CREDENTIAL");
        }
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

    internal void MainDrained()
    {
        var vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-cases.json")))!;
        State["statuses"]!["server2"] = vectors["mainDrained"]!.DeepClone();
        State["statuses"]!["server2-temp"] = vectors["tempAccepting"]!.DeepClone();
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
    internal string Root { get; }
    internal bool Windows { get; }
    internal C1008FixtureOptions Options { get; }
    private string? _shellRoot;
    private string? _shellRepo;
    private readonly List<C1008Child> _children = [];
    internal string ShellRoot { get { PreparePaths(); return _shellRoot!; } }
    internal string ShellRepo { get { PreparePaths(); return _shellRepo!; } }
    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    internal static string HolderProgram(string root) => $"exec 8>'{root}/server/locks/rollout.lock'; flock 8; touch '{root}/held'; read -r release";

    private void PreparePaths()
    {
        if (_shellRoot is not null) return;
        if (!Windows) { _shellRoot = Root; _shellRepo = DelegateScriptRunner.RepoRoot; return; }
        File.WriteAllText(Path.Combine(Root, ".c1030-roundtrip"), "fixture-owned");
        // The trusted prelude is never fed back through C980's raw-body guard.
        var root = ConvertPath(Options.NativeRoot ?? Root, "root", ".c1030-roundtrip");
        var repo = ConvertPath(Options.NativeRepo ?? DelegateScriptRunner.RepoRoot, "repo", "AGENTS.md");
        _shellRepo = repo;
        _shellRoot = root;
    }

    private string ConvertPath(string native, string variable, string probe)
    {
        var program = RemoteScriptContractTests.PrepareLinuxShellScript(
            $"test -f \"${variable}/{probe}\" || {{ echo C1030_PATH_UNREACHABLE >&2; exit 24; }}\n" +
            $"printf '%s' \"${variable}\"\n", variable, native, true);
        var result = Execute("convert", Options.ConverterPrelude + program).GetAwaiter().GetResult();
        if (result.Exit != 0) throw new InvalidOperationException($"C1030 conversion exit={result.Exit}: {result.Output}");
        return result.Stdout;
    }

    internal string ShellPath(string native)
    {
        if (!Windows) return native;
        foreach (var candidate in new[] { Options.NativeRoot, Root })
        {
            if (candidate is null) continue;
            var root = candidate.TrimEnd('\\', '/');
            if (native.Equals(root, StringComparison.OrdinalIgnoreCase)) return ShellRoot;
            if (native.Length > root.Length && native.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                native[root.Length] is '/' or '\\')
                return ShellRoot + native[root.Length..].Replace('\\', '/');
        }
        return native;
    }

    internal JsonObject AdaptPaths(JsonObject model)
    {
        if (!Windows) return model;
        var clone = model.DeepClone().AsObject();
        if (clone["volumes"] is JsonObject volumes)
            foreach (var (_, volume) in volumes)
                if (volume is JsonObject item) ConvertField(item, "Mountpoint");
        if (clone["containers"] is JsonArray containers)
            foreach (var container in containers) ConvertMounts(container);
        ConvertMounts(clone["runner"]);
        return clone;

        void ConvertField(JsonObject item, string field)
        {
            if (item[field] is JsonValue value && value.TryGetValue<string>(out var path))
                item[field] = ShellPath(path);
        }
        void ConvertMounts(JsonNode? container)
        {
            if (container is JsonObject obj && obj["Mounts"] is JsonArray mounts)
                foreach (var mount in mounts)
                    if (mount is JsonObject item) ConvertField(item, "Source");
        }
    }

    internal void WriteRecreated(JsonObject model) =>
        File.WriteAllText(Path.Combine(Root, "recreated.json"), AdaptPaths(model).ToJsonString());

    internal ProcessStartInfo ShellStart(string entry, string body, string? file = null)
    {
        var start = new ProcessStartInfo(Windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe") : "/bin/bash")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (Windows)
        {
            start.ArgumentList.Add("-e"); start.ArgumentList.Add("/bin/bash"); start.ArgumentList.Add("-s");
        }
        else if (file is null) { start.ArgumentList.Add("-c"); start.ArgumentList.Add(body); }
        else start.ArgumentList.Add(file);
        start.Environment["PATH"] = Options.ToolPath;
        // WSL can retain its startup directory beyond the shell's exit. Keep that
        // directory outside the owned tree that Dispose removes; shell paths are absolute.
        if (Windows) start.WorkingDirectory = Path.GetTempPath();
        return start;
    }

    internal Dictionary<string, string> CaseEnvironment(string hostCase = "deploy-parent") => new()
    {
        ["C590_CASE"] = hostCase, ["C590_SHA"] = new string('a', 40), ["C590_RUN"] = "c1008fixture",
        ["C590_REEXEC"] = "1", ["C604_SERVER_ORIGIN"] = "http://127.0.0.1:1"
    };

    internal string PrepareInput(string entry, string body, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!Windows) return body;
        var input = "export PATH=" + Quote(Options.ToolPath) + "\n";
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            input += "export " + key + "=" + Quote(value) + "\n";
        var required = entry switch { "run" or "bridge" => "bash git node flock jq pwsh", "git" => "git", "holder" => "flock", _ => "" };
        if (required.Length > 0)
            input += "for c1030_tool in " + required + "; do type -P \"$c1030_tool\" >/dev/null || { printf 'C1030_PREREQUISITE_MISSING %s\\n' \"$c1030_tool\" >&2; exit 127; }; done\n";
        if (entry is "run" or "bridge") input += Options.BeforeSource;
        return (input + body).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal async Task<C1008Child> StartChild(string entry, string body, string? file = null,
        IReadOnlyDictionary<string, string>? environment = null, bool retainInput = false)
    {
        var start = ShellStart(entry, body, file);
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) start.Environment[key] = value;
        var input = PrepareInput(entry, body, environment);
        Options.ObserveLaunch?.Invoke(entry, start, input);
        // This seam replaces only the physical executable in Linux's Windows-preparation witness.
        // The real entry specification is observed before that substitution.
        Options.SelectExecutor?.Invoke(start);
        var process = Process.Start(start) ?? throw new InvalidOperationException("C1030 child did not start");
        var child = new C1008Child(process, Options.BeforeExitWait);
        _children.Add(child);
        try
        {
            Options.AfterStart?.Invoke(child);
            if (Windows) { await process.StandardInput.WriteAsync(input); await process.StandardInput.FlushAsync(); }
            if (!retainInput) process.StandardInput.Close();
            return child;
        }
        catch { await child.DisposeAsync(); throw; }
    }

    internal async Task<C1008Result> Execute(string entry, string body, string? file = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        await using var child = await StartChild(entry, body, file, environment);
        return await child.Wait(TimeSpan.FromSeconds(30));
    }

    internal async Task<C1008Child> HoldLock(string? overrideBody = null)
    {
        Directory.CreateDirectory(Path.Combine(Root, "server/locks"));
        var body = overrideBody ?? HolderProgram(Windows ? ShellRoot.Replace("'", "'\\''", StringComparison.Ordinal) : ShellRoot);
        if (Windows)
        {
            File.WriteAllText(Path.Combine(Root, "holder.sh"), body.Replace("\r\n", "\n"), new UTF8Encoding(false));
            body = "source " + Quote(ShellRoot + "/holder.sh") + "\n";
        }
        var child = await StartChild("holder", body, retainInput: true);
        try
        {
            Options.AtReadiness?.Invoke(child);
            var ready = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(Root, "held")) && !child.Process.HasExited && ready.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(25);
            if (!File.Exists(Path.Combine(Root, "held")))
            {
                var result = await child.Stop();
                throw new InvalidOperationException($"C1030 holder not ready: exit={result.Exit}; stderr={result.Stderr}");
            }
            return child;
        }
        catch { await child.DisposeAsync(); throw; }
    }

    internal async Task CreateEscapingLink(string? target = null)
    {
        if (!Windows) { Directory.CreateSymbolicLink(Path.Combine(Root, "work/escape"), target ?? Root); return; }
        var result = await Execute("git", "ln -s -- " + Quote(target ?? ShellRoot) + " " + Quote(ShellRoot + "/work/escape"));
        if (result.Exit != 0) throw new InvalidOperationException(result.Output);
    }

    internal async Task WriteLinkedLock()
    {
        if (!Windows)
        {
            var gitfile = File.ReadAllText(Path.Combine(Root, "work/linked clean/.git"));
            File.WriteAllText(Path.Combine(gitfile["gitdir: ".Length..].Trim(), "index.lock"), "unknown");
            return;
        }
        var result = await Execute("git", "set -e\ncd " + Quote(ShellRoot + "/work/linked clean") +
            "\nc1030_gitdir=\"$(git rev-parse --absolute-git-dir)\"\nprintf unknown > \"$c1030_gitdir/index.lock\"\n");
        if (result.Exit != 0) throw new InvalidOperationException(result.Output);
    }

    internal string BridgeResumeCommand()
    {
        var file = Path.Combine(Root, "remote.sh");
        var body = File.ReadAllText(file);
        var environment = CaseEnvironment();
        environment["C1008_RESUME"] = "1";
        if (Windows) environment["C1008_FIXTURE_WINDOWS"] = "1";
        var start = ShellStart("bridge", body, file);
        var input = PrepareInput("bridge", body, environment);
        Options.ObserveLaunch?.Invoke("bridge", start, input);
        Options.SelectExecutor?.Invoke(start);
        if (!Windows) return "& /bin/bash " + PsQuote(file) + " | Out-Null\nreturn $LASTEXITCODE";
        var inputFile = Path.Combine(Root, "bridge-input.sh");
        File.WriteAllText(inputFile, input, new UTF8Encoding(false));
        var arguments = string.Join("\n", start.ArgumentList.Select(a => "$c1030Start.ArgumentList.Add(" + PsQuote(a) + ")"));
        return $$"""
            $c1030Start=[System.Diagnostics.ProcessStartInfo]::new({{PsQuote(start.FileName)}})
            $c1030Start.UseShellExecute=$false
            $c1030Start.RedirectStandardInput=$true
            $c1030Start.RedirectStandardOutput=$true
            $c1030Start.RedirectStandardError=$true
            $c1030Start.StandardInputEncoding=[System.Text.UTF8Encoding]::new($false)
            {{arguments}}
            $c1030Child=[System.Diagnostics.Process]::Start($c1030Start)
            $c1030Out=$c1030Child.StandardOutput.ReadToEndAsync()
            $c1030Err=$c1030Child.StandardError.ReadToEndAsync()
            try {
                $c1030Child.StandardInput.Write([System.IO.File]::ReadAllText({{PsQuote(inputFile)}}))
                $c1030Child.StandardInput.Close()
                if (-not $c1030Child.WaitForExit(30000)) { throw 'C1030 bridge child timeout' }
                $c1030Child.WaitForExit()
                $null=$c1030Out.GetAwaiter().GetResult()
                [Console]::Error.Write($c1030Err.GetAwaiter().GetResult())
                return $c1030Child.ExitCode
            } finally {
                if (-not $c1030Child.HasExited) { $c1030Child.Kill($true); $c1030Child.WaitForExit() }
                $c1030Child.Dispose()
            }
            """;
    }
    internal static string PsQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal void CopyFake(string? source = null)
    {
        source ??= File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-fake-docker.sh"));
        File.WriteAllText(Path.Combine(Root, "fake-docker.sh"), source.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }
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

    internal C1008HostFixture(bool main = true, string? root = null, bool? windows = null, C1008FixtureOptions? options = null)
    {
        Root = root ?? Directory.CreateTempSubdirectory("c1008-host-").FullName;
        Directory.CreateDirectory(Root);
        Windows = windows ?? OperatingSystem.IsWindows();
        Options = options ?? new C1008FixtureOptions();
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
            containers.Add(Container('1', "antiphon-runner", "session-runner", true, "work", "runner-tmp", "dind-data", "runner-state",
                "cache-nuget-packages", "cache-nuget-scratch", "cache-npm-content"));
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

    private static string VolumeName(string project, string role) => role.StartsWith("cache-", StringComparison.Ordinal)
        ? "antiphon-runner-" + role : project + "_" + role;
    private static string Destination(string role) => role switch
    {
        "work" => "/work", "runner-tmp" => "/tmp", "dind-data" => "/var/lib/docker", "runner-state" => "/state",
        "cache-nuget-packages" => "/home/app/.nuget/packages", "cache-nuget-scratch" => "/var/cache/antiphon/nuget-scratch",
        "cache-npm-content" => "/home/app/.npm/_cacache", _ => "/unknown"
    };
    internal JsonObject Container(char id, string project, string service, bool running, params string[] roles) =>
        new() { ["Id"] = new string(id, 64), ["Image"] = "sha256:" + new string('a', 64),
            ["State"] = new JsonObject { ["Running"] = running, ["Status"] = running ? "running" : "exited" },
            ["Config"] = new JsonObject { ["Labels"] = new JsonObject
                { ["com.docker.compose.project"] = project, ["com.docker.compose.service"] = service } },
            ["Mounts"] = new JsonArray(roles.Select(role => (JsonNode)new JsonObject
                { ["Type"] = "volume", ["Name"] = VolumeName(project, role),
                    ["Source"] = Path.Combine(Root, "volumes", VolumeName(project, role), "_data"),
                    ["Destination"] = Destination(role), ["RW"] = true }).ToArray()) };

    internal async Task<(int Exit, string Output)> Run(string hostCase = "deploy-parent", string extra = "", bool dryRun = false)
    {
        File.WriteAllText(StatePath, AdaptPaths(Docker).ToJsonString());
        File.WriteAllText(Path.Combine(Root, "statuses.json"), Statuses.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "tasks.json"), new JsonObject
            { ["scopes"] = TaskScopes.DeepClone(), ["details"] = TaskDetails.DeepClone() }.ToJsonString());
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var shellRoot = ShellRoot;
        var shellRepo = ShellRepo;
        source = ComposeProgram(Windows ? shellRoot.Replace("'", "'\\''", StringComparison.Ordinal) : shellRoot,
            Windows ? shellRepo.Replace("'", "'\\''", StringComparison.Ordinal) : shellRepo, source, extra, dryRun);
        if (Windows)
        {
            CopyFake();
            source = source.Replace("bash " + Quote(shellRepo + "/scripts/fixtures/c1008-fake-docker.sh"),
                "/bin/bash " + Quote(shellRoot + "/fake-docker.sh"), StringComparison.Ordinal);
            source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        var script = Path.Combine(Root, "remote.sh");
        File.WriteAllText(script, source);
        var environment = CaseEnvironment(hostCase);
        if (Windows) environment["C1008_FIXTURE_WINDOWS"] = "1";
        var result = await Execute("run", source, script, environment);
        return (result.Exit, result.Output);
    }

    internal static string ComposeProgram(string Root, string repoRoot, string source, string extra, bool dryRun)
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

    public void Dispose()
    {
        foreach (var child in _children.ToArray()) child.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // Unlink the owned name; never follow its target during recursive cleanup.
        if (Windows && _shellRoot is not null)
        {
            var removed = Execute("cleanup", "if [ -L " + Quote(_shellRoot + "/work/escape") +
                " ]; then rm -- " + Quote(_shellRoot + "/work/escape") + "; fi").GetAwaiter().GetResult();
            if (removed.Exit != 0) throw new InvalidOperationException(removed.Output);
        }
        Directory.Delete(Root, recursive: true);
    }
}

// Instance-only seams expose the actual entry wiring without changing global process state.
internal sealed class C1008FixtureOptions
{
    // Optional, explicitly qualified tool list supplied only to an isolated test process.
    // Ordinary Windows/WSL qualification uses the fixed Linux directories.
    internal string ToolPath { get; set; } = Environment.GetEnvironmentVariable("C1030_FIXTURE_TOOL_PATH")
        ?? "/usr/local/bin:/usr/bin:/bin";
    internal string? NativeRoot { get; set; }
    internal string? NativeRepo { get; set; }
    internal string ConverterPrelude { get; set; } = "";
    internal string BeforeSource { get; set; } = "";
    internal Action<string, ProcessStartInfo, string>? ObserveLaunch { get; set; }
    internal Action<ProcessStartInfo>? SelectExecutor { get; set; }
    internal Action<C1008Child>? AfterStart { get; set; }
    internal Action<C1008Child>? AtReadiness { get; set; }
    internal Func<Task>? BeforeExitWait { get; set; }
}

internal sealed record C1008Result(int Exit, string Stdout, string Stderr)
{
    internal string Output => Stdout + Stderr;
}

internal sealed class C1008Child : IAsyncDisposable
{
    internal Process Process { get; }
    internal int Id { get; }
    internal DateTime StartTime { get; }
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    private readonly Func<Task>? _beforeExitWait;
    private bool _disposed;
    private C1008Result? _result;

    internal C1008Child(Process process, Func<Task>? beforeExitWait)
    {
        Process = process;
        Id = process.Id;
        StartTime = process.StartTime.ToUniversalTime();
        _beforeExitWait = beforeExitWait;
        _stdout = process.StandardOutput.ReadToEndAsync();
        _stderr = process.StandardError.ReadToEndAsync();
    }

    internal async Task<C1008Result> Wait(TimeSpan timeout)
    {
        if (_result is not null) return _result;
        using var deadline = new CancellationTokenSource(timeout);
        try { await Process.WaitForExitAsync(deadline.Token); }
        catch { await Stop(); throw; }
        return _result = new(Process.ExitCode, await _stdout, await _stderr);
    }

    internal async Task<C1008Result> Release()
    {
        if (!Process.HasExited)
        {
            await Process.StandardInput.WriteLineAsync("release");
            await Process.StandardInput.FlushAsync();
            Process.StandardInput.Close();
        }
        return await Wait(TimeSpan.FromSeconds(10));
    }

    internal async Task<C1008Result> Stop()
    {
        if (_result is not null) return _result;
        if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        if (_beforeExitWait is not null) await _beforeExitWait();
        await Process.WaitForExitAsync();
        return _result = new(Process.ExitCode, await _stdout, await _stderr);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await Stop();
        Process.Dispose();
        _disposed = true;
    }
}
