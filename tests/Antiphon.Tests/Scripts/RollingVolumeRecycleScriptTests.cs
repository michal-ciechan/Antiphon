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
    public async Task C1087_Read_failures_name_their_cause()
    {
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string task = "11111111-1111-1111-1111-111111111111";
        var listPath = $"/api/agent-tasks?projectId={project}&unscoped=include&includeChecks=true";
        foreach (var path in new[] { "/api/projects?includeArchived=true", listPath, $"/api/agent-tasks/{task}" })
        foreach (var (fault, cause) in new[] { ("timeout", "Timeout"), ("503", "Http"), ("400", "Http"),
                     ("401", "Http"), ("transport", "Transport"), ("empty", "Empty"), ("malformed", "Malformed") })
        {
            using var f = new C1008WrapperFixture();
            f.State["tasks"]!["items"]!.AsArray().Add(C1087TaskRow(task, project));
            f.State["readFaults"] = new JsonObject { [path] = fault };
            var run = await f.Run("retire-temp");
            run.Exit.ShouldBe(2, path + " " + fault + "; " + run.Output);
            run.Output.ShouldContain("RecycleTaskCensusUnknown cause=" + cause);
            run.Output.ShouldContain("path=" + path);
            if (cause == "Http") run.Output.ShouldContain("status=" + fault);
            if (cause == "Malformed") run.Output.ShouldContain("field=body");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
            var receipt = C1087Receipt(run.Trace);
            var read = receipt["reads"]!.AsArray().Last()!;
            read["path"]!.GetValue<string>().ShouldBe(path);
            read["outcome"]!.GetValue<string>().ShouldBe(cause);
            read["elapsedMs"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
            if (cause == "Http") read["status"]!.GetValue<int>().ShouldBe(int.Parse(fault));
            foreach (var text in new[] { run.Output, receipt.ToJsonString() })
            {
                text.ShouldNotContain("SENTINEL");
                text.ShouldNotContain("http://127.0.0.1:1");
            }
        }

        foreach (var field in new[] { "items", "excluded.total", "excluded.byProject", "scope.projectId", "status", "runnerId" })
        {
            using var f = new C1008WrapperFixture();
            var row = C1087TaskRow(task, project);
            f.State["tasks"]!["items"]!.AsArray().Add(row);
            switch (field)
            {
                case "items": f.State["tasks"]!["items"] = "SENTINEL_SHAPE"; break;
                case "excluded.total": f.State["tasks"]!["excluded"]!["total"] = "SENTINEL_SHAPE"; break;
                case "excluded.byProject": f.State["tasks"]!["excluded"]!["byProject"] = null; break;
                case "scope.projectId": f.State["tasks"]!["scope"]!["projectId"] = "SENTINEL_SHAPE"; break;
                default: row[field] = new JsonObject { ["SENTINEL_SHAPE"] = true }; break;
            }
            var run = await f.Run("retire-temp");
            run.Exit.ShouldBe(2, run.Output);
            run.Output.ShouldContain($"RecycleTaskCensusUnknown cause=Malformed field={field} path={listPath}");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
            var receipt = C1087Receipt(run.Trace);
            receipt["reads"]!.AsArray().Last()!["outcome"]!.GetValue<string>().ShouldBe("Malformed");
            (run.Output + receipt.ToJsonString()).ShouldNotContain("SENTINEL");
        }

        using var good = new C1008WrapperFixture();
        var safeRow = C1087TaskRow(task, project);
        good.State["tasks"]!["items"]!.AsArray().Add(safeRow);
        good.State["details"] = new JsonObject { [task] = new JsonObject {
            ["summary"] = safeRow.DeepClone(), ["goal"] = "SENTINEL_DETAIL",
            ["landRequest"] = new JsonObject { ["state"] = "Completed", ["terminalEventId"] = task,
                ["result"] = "SENTINEL_LAND" } } };
        var accepted = await good.Run("retire-temp");
        accepted.Exit.ShouldBe(0, accepted.Output);
        var success = C1087Receipt(accepted.Trace);
        success["reads"]!.AsArray().All(r => r!["outcome"]!.GetValue<string>() == "ok").ShouldBeTrue();
        success["reads"]!.AsArray().Where(r => r!["path"]!.GetValue<string>() == listPath)
            .All(r => r!["items"]!.GetValue<int>() == 1 && r["excludedTotal"]!.GetValue<int>() == 0).ShouldBeTrue();
        var snapshot = success["tasks"]!.AsArray().Single()!;
        snapshot["id"]!.GetValue<string>().ShouldBe(task);
        snapshot["landRequest"]!["state"]!.GetValue<string>().ShouldBe("Completed");
        snapshot["landRequest"]!["terminalEventId"]!.GetValue<string>().ShouldBe(task);
        success.ToJsonString().ShouldNotContain("SENTINEL");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1087_Project_resolves_by_repository_identity()
    {
        const string id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string other = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2";
        const string url = "https://github.com/michal-ciechan/Antiphon.git";
        var root = DelegateScriptRunner.RepoRoot;
        var normalizedPath = Path.Combine(root, "scripts", "..") + Path.DirectorySeparatorChar;
        if (OperatingSystem.IsWindows()) normalizedPath = normalizedPath.Replace('\\', '/').ToUpperInvariant();
        foreach (var (git, path, resolvedBy) in new[] {
                     ("", normalizedPath, "path"), (url, "", "url"), (url, root, "both"),
                     (" https://GITHUB.COM/michal-ciechan/Antiphon ", "", "url"),
                     ("git@github.com:michal-ciechan/Antiphon.git", "", "url") })
        {
            using var f = new C1008WrapperFixture();
            f.State["projects"] = new JsonArray(C1087Project(id, git, path));
            var run = await f.Run("retire-temp");
            run.Exit.ShouldBe(0, resolvedBy + "; " + run.Output);
            var project = C1087Receipt(run.Trace)["project"]!;
            project["id"]!.GetValue<string>().ShouldBe(id);
            project["resolvedBy"]!.GetValue<string>().ShouldBe(resolvedBy);
            run.Trace.Any(x => x["method"]?.GetValue<string>() == "PUT").ShouldBeFalse();
        }
        foreach (var vector in new[] { "no-match", "ambiguous", "archived", "malformed-id", "missing-archivedAt",
                     "missing-gitRepositoryUrl", "bad-path", "explicit", "not-found", "explicit-archived", "invalid-id", "upper-id", "empty-id" })
        {
            using var f = new C1008WrapperFixture();
            var project = C1087Project(id, url, "");
            f.State["projects"] = new JsonArray(project);
            var args = Array.Empty<string>();
            string expected;
            switch (vector)
            {
                case "no-match": project["gitRepositoryUrl"] = "https://github.com/other/repo.git";
                    project["localRepositoryPath"] = root + "-sibling";
                    expected = "cause=NoMatch candidates=1 urlMatches=0 pathMatches=0"; break;
                case "ambiguous": f.State["projects"]!.AsArray().Add(C1087Project(other, "", root));
                    expected = "cause=Ambiguous candidates=2 matches=2"; break;
                case "archived": project["archivedAt"] = "2026-10-03T09:00:00Z"; expected = "cause=Archived id=" + id; break;
                case "malformed-id": project["id"] = "SENTINEL_BAD_ID"; expected = "cause=Malformed field=id"; break;
                case "missing-archivedAt": project.Remove("archivedAt"); expected = "cause=Malformed field=archivedAt"; break;
                case "missing-gitRepositoryUrl": project.Remove("gitRepositoryUrl"); expected = "cause=Malformed field=gitRepositoryUrl"; break;
                case "bad-path": project["localRepositoryPath"] = "bad\0path"; expected = "cause=Malformed field=localRepositoryPath"; break;
                case "invalid-id": case "upper-id": case "empty-id":
                    args = ["-ProjectId", vector == "empty-id" ? "" : vector == "upper-id" ? id.ToUpperInvariant() : "SENTINEL_BAD_ID"];
                    expected = "cause=InvalidId"; break;
                default:
                    args = ["-ProjectId", id];
                    project["gitRepositoryUrl"] = "";
                    if (vector == "explicit-archived") project["archivedAt"] = "2026-10-03T09:00:00Z";
                    f.State["projectDetail"] = vector == "not-found" ? new JsonObject() : new JsonObject { [id] = project.DeepClone() };
                    expected = vector == "not-found" ? "cause=NotFound id=" + id : "cause=Archived id=" + id;
                    break;
            }
            var run = await f.Run("retire-temp", args);
            run.Exit.ShouldBe(vector == "explicit" ? 0 : 2, vector + "; " + run.Output);
            if (vector == "explicit")
            {
                var receipt = C1087Receipt(run.Trace)["project"]!;
                receipt["resolvedBy"]!.GetValue<string>().ShouldBe("explicit");
                receipt["name"]!.GetValue<string>().ShouldBe("Antiphon");
                run.Trace.Any(x => x["path"]?.GetValue<string>() == "/api/projects?includeArchived=true").ShouldBeFalse();
            }
            else
            {
                run.Output.ShouldContain("RecycleProjectUnresolved " + expected);
                run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
                run.Output.ShouldNotContain("SENTINEL");
            }
        }
    }

    private static JsonObject C1087Project(string id, string url, string path) => new()
    {
        ["id"] = id, ["name"] = "Antiphon", ["gitRepositoryUrl"] = url,
        ["localRepositoryPath"] = path, ["archivedAt"] = null
    };

    private static JsonObject C1087TaskRow(string id, string project) => new()
    {
        ["id"] = id, ["status"] = "Succeeded", ["runnerId"] = "other", ["projectId"] = project,
        ["scopeSource"] = "Task", ["landRequestedAt"] = null, ["landStartedAt"] = null,
        ["title"] = "SENTINEL_TITLE", ["goal"] = "SENTINEL_GOAL", ["result"] = "SENTINEL_RESULT"
    };

    private static JsonObject C1087Receipt(JsonObject[] trace)
    {
        var root = trace.First(t => t["kind"]?.GetValue<string>() == "prerequisite")["evidenceRoot"]!.GetValue<string>();
        var file = Path.Combine(root, "census-retire-temp-server2-temp.json");
        File.Exists(file).ShouldBeTrue("partial and successful census receipts must be persisted: " + file);
        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

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
        blocked.Trace.Any(x => x["name"]?.GetValue<string>() == "retire-temp-runner").ShouldBeFalse("retire-null-stays-closed: live/unknown container retains volume refusal");
        blocked.Output.ShouldContain("TempContainerStillRunning");
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
        unknown.Trace.Any(x => x["name"]?.GetValue<string>() == "retire-temp-runner").ShouldBeFalse("retire-null-stays-closed: empty failed census is unknown");
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
        C1008HostFixture.RequireNativeLinux();
        using (var legacy = new C1008HostFixture())
        {
            var direct = await legacy.Run(extra: "C1008_CONTEXT=''");
            direct.Exit.ShouldBe(0, "recycle-manifest-strict: direct deploy-parent remains available; " + direct.Output);
            legacy.Removed.ShouldBeEmpty("recycle-manifest-strict: no context cannot authorize volume removal");
            legacy.Trace.Any(a => a[0] == "stop" || a[0] == "rm").ShouldBeFalse();
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Wrapper_option_manifest_is_strict()
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
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture(main: false);
        var run = await f.Run("retire-temp-runner", "detect_lane() { LANE=nested; }");
        run.Output.ShouldContain("WrongLane", Case.Sensitive, "recycle-doc-contract: host operations refuse nested lane");
        f.Removed.ShouldBeEmpty();
    }

    [Test]
    public void C1008_Transport_scripts_are_ascii()
    {
        foreach (var script in new[] { "deploy-server2.ps1", "c590-real.ps1", "c590-remote.sh", "verify-docker-stack.ps1" })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", script)).All(x => x < 128)
                .ShouldBeTrue("recycle-doc-contract: ASCII transport " + script);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Refusal_receipts_do_not_leak_secrets()
    {
        C1008HostFixture.RequireNativeLinux();
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
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Wrapper_refusal_receipts_do_not_leak_secrets()
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
    public async Task C1008_Retired_absent_null_is_accepted(CancellationToken cancellationToken)
    {
        C1008HostFixture.RequireNativeLinux();
        var request = await GetRetirementRequestAsync(cancellationToken);
        using var host = new C1008HostFixture(main: false);
        var operation = request["recycle"]!["operationId"]!.GetValue<string>();
        var stamp = request["tempRetiredAt"]!.GetValue<string>();
        var retired = await host.Run("retire-temp-runner", $"C1008_OPERATION='{operation}'; C590_TEMP_RETIRED_AT='{stamp}'");
        retired.Exit.ShouldBe(0, "retire-absent-null-accepted: actual host entry using wrapper context; " + retired.Output);
        host.Removed.ShouldBe(new[] { "antiphon-runner-temp_work", "antiphon-runner-temp_runner-tmp",
            "antiphon-runner-temp_dind-data", "antiphon-runner-temp_runner-state" });
        var journalPath = Path.Combine(host.Root, "server/recycle", operation + ".json");
        File.Exists(journalPath).ShouldBeTrue("c1050-host-operation: actual wrapper operation names the persisted journal");
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!;
        journal["operationId"]!.GetValue<string>().ShouldBe(operation, "c1050-host-operation: wrapper identity reaches the host");
        DateTimeOffset.Parse(journal["retiredAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
            .ToUniversalTime().ShouldBe(DateTimeOffset.Parse(stamp, System.Globalization.CultureInfo.InvariantCulture)
                .ToUniversalTime(), "c1050-host-retirement: wrapper stamp reaches the host as a UTC instant");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Wrapper_retired_absent_null_is_accepted(CancellationToken cancellationToken)
    {
        await GetRetirementRequestAsync(cancellationToken);
    }

    private static async Task<JsonObject> GetRetirementRequestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        var operation = request["recycle"]!["operationId"]!.GetValue<string>();
        Regex.IsMatch(operation, "^c1008[0-9a-f]{32}$").ShouldBeTrue();
        return request.DeepClone().AsObject();
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
            ["tempContainer"] = false, ["clockMs"] = 0,
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
    internal static void RequireNativeLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException(
                "CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.");
    }

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
            var psi = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = DelegateScriptRunner.RepoRoot };
            foreach (var arg in new[] { "scripts/fixtures/c994-production-compose-model.mjs", Root, project,
                project == "antiphon-runner-temp" ? "true" : "false" }) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new InvalidOperationException("Compose config timeout"); }
            if (process.ExitCode != 0) throw new InvalidOperationException("Compose config: " + stderr.GetAwaiter().GetResult());
            var resolved = JsonNode.Parse(stdout.GetAwaiter().GetResult())!["model"]!.DeepClone();
            // JSON member order is not Compose topology. Keep the existing fake
            // down boundary's deterministic deletion order without changing its assertion.
            var declarations = resolved["volumes"]!.AsObject();
            var ordered = new JsonObject();
            foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
                ordered[role] = declarations[role]!.DeepClone();
            foreach (var (key, value) in declarations)
                if (!ordered.ContainsKey(key)) ordered[key] = value?.DeepClone();
            resolved["volumes"] = ordered;
            models[project] = resolved;

        }
        foreach (var name in new[] { "antiphon-runner_work-extra", "schoolrevision-staging", "openclaw-state" })
            volumes[name] = Volume(name, new JsonObject());
        Docker = new JsonObject { ["volumes"] = volumes, ["models"] = models,
            ["containers"] = new JsonArray(), ["removed"] = new JsonArray(), ["fault"] = "" };
        var containers = Docker["containers"]!.AsArray();
        if (main)
        {
            containers.Add(Container('1', "antiphon-runner", "session-runner", true, "work", "runner-tmp", "dind-data", "runner-state",
                "cache-nuget-packages", "cache-nuget-scratch", "cache-npm-content"));
            containers.Add(Container('2', "antiphon-runner", "state-init", false, "work", "runner-state"));
        }
        containers.Add(Container('3', "antiphon-runner", "build-slots", true));

        Directory.CreateDirectory(Path.Combine(Root, "work"));
        Directory.CreateDirectory(Path.Combine(Root, "server/cache"));
        File.WriteAllText(Path.Combine(Root, "temp.env"), "RUNNER_GROK_STORE_DIR=" + Root + "/grok\n");
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
    internal JsonObject Container(char id, string project, string service, bool running, params string[] roles)
    {
        var mounts = new JsonArray();
        var hostConfig = new JsonObject { ["Tmpfs"] = new JsonObject(), ["Mounts"] = new JsonArray() };
        if (service is "state-init" or "session-runner")
        {
            var model = Docker["models"]![project]!;
            foreach (var m in model["services"]![service]!["volumes"]!.AsArray())
            {
                var volume = m!["type"]!.GetValue<string>() == "volume";
                var name = volume ? model["volumes"]![m["source"]!.GetValue<string>()]!["name"]!.GetValue<string>() : null;
                mounts.Add(new JsonObject { ["Type"] = volume ? "volume" : "bind", ["Name"] = name,
                    ["Source"] = volume ? Docker["volumes"]![name!]!["Mountpoint"]!.DeepClone() : m["source"]!.DeepClone(),
                    ["Destination"] = m["target"]!.DeepClone(), ["RW"] = !(m["read_only"]?.GetValue<bool>() ?? false) });
            }
            foreach (var secret in model["services"]![service]!["secrets"]?.AsArray() ?? new JsonArray())
            {
                var source = secret!["source"]!.GetValue<string>();
                var target = secret["target"]!.GetValue<string>();
                mounts.Add(new JsonObject { ["Type"] = "bind", ["Source"] = model["secrets"]![source]!["file"]!.DeepClone(),
                    ["Destination"] = target.StartsWith('/') ? target : "/run/secrets/" + target, ["RW"] = false });
            }
            if (service == "session-runner") hostConfig["Tmpfs"] = new JsonObject { ["/run/antiphon"] = "" };
        }
        else foreach (var role in roles) mounts.Add(new JsonObject { ["Type"] = "volume", ["Name"] = VolumeName(project, role),
            ["Source"] = Path.Combine(Root, "volumes", VolumeName(project, role), "_data"), ["Destination"] = Destination(role), ["RW"] = true });
        return new JsonObject { ["Id"] = new string(id, 64), ["Image"] = "sha256:" + new string('a', 64),
            ["State"] = new JsonObject { ["Running"] = running, ["Status"] = running ? "running" : "exited" },
            ["Config"] = new JsonObject { ["Labels"] = new JsonObject { ["com.docker.compose.project"] = project,
                ["com.docker.compose.service"] = service } }, ["Mounts"] = mounts, ["HostConfig"] = hostConfig };
    }

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
            DEPLOY_KEY='{{Root}}/deploy-key'; PHONE_HOME_SECRET='{{Root}}/phone-home'
            CLAUDE_OAUTH_TOKEN_PATH='{{Root}}/claude-token'; GIT_IDENTITY_PATH='{{Root}}/gitconfig'; CODEX_HOME_PATH='{{Root}}/codex'; RUNNER_GROK_STORE_DIR='{{Root}}/grok'
            GITHUB_TOKEN_DIR_PATH='{{Root}}/github-token'
            RUNNER_GIT_USER_NAME=Fixture; RUNNER_GIT_USER_EMAIL=fixture@example.invalid
            C1008_OPERATION=c100800000000000000000000000000000001; C1008_CONTEXT={{(hostCase == "retire-temp-containers" ? "''" : "default")}}; C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C994_VERSION=1; C994_OPERATION=c99400000000000000000000000000000001; C994_PROJECT=antiphon-runner-temp
            C994_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1; C994_RETIRED_AT=2026-10-03T09:30:00Z; C994_DRY_RUN={{(dryRun ? "1" : "0")}}
            C1008_DRY_RUN={{(dryRun && hostCase != "retire-temp-containers" ? "1" : "0")}}; C590_TEMP_RETIRED_AT=2026-10-03T09:30:00Z
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
            c849_status_body() { jq -ec --arg runner "$1" '.[$runner]' '{{Root}}/statuses.json'; }
            c1008_http() { local key="$1"; if [[ "$key" == *projectId=* ]]; then key="${key#*projectId=}"; key="${key%%&*}"; jq -ec --arg key "$key" '.scopes[$key]' '{{Root}}/tasks.json'; else jq -ec --arg key "${key##*/}" '.details[$key]' '{{Root}}/tasks.json'; fi; }
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
