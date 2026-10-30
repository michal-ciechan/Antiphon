using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class RollingVolumeRecycleScriptTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1374_Wrapper_census_recovers_transient_reads()
        => await C1374CensusProbes.Check(host: false);

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1087_Read_failures_name_their_cause()
    {
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string task = "11111111-1111-1111-1111-111111111111";
        var listPath = C1087ListPath(project, "open");
        foreach (var path in new[] { "/api/projects?includeArchived=true", listPath, $"/api/agent-tasks/{task}" })
        foreach (var (fault, cause) in new[] { ("timeout", "Timeout"), ("503", "Http"), ("400", "Http"),
                     ("401", "Http"), ("transport", "Transport"), ("empty", "Empty"), ("malformed", "Malformed") })
        {
            using var f = new C1008WrapperFixture();
            f.State["tasks"]!["items"]!.AsArray().Add(C1087TaskRow(task, project));
            f.State["readFaults"] = new JsonObject { [path] = fault };
            var run = await f.Run("retire-temp");
            run.Exit.ShouldBe(2, path + " " + fault + "; " + run.Output);
            var exhausted = path.StartsWith("/api/agent-tasks", StringComparison.Ordinal) && fault is "timeout" or "503";
            run.Output.ShouldContain("RecycleTaskCensusUnknown cause=" + (exhausted ? "Http" : cause));
            if (exhausted) { run.Output.ShouldContain("attempts=3"); run.Output.ShouldContain("failure=" + cause); }
            run.Output.ShouldContain("path=" + path);
            if (cause == "Http") run.Output.ShouldContain("status=" + fault);
            if (cause == "Malformed") run.Output.ShouldContain("field=body");
            run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
            var receipt = C1087Receipt(run.Trace);
            receipt["reads"]!.AsArray().Count(r => r!["path"]!.GetValue<string>() == path).ShouldBe(exhausted ? 3 : 1);
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
            f.State["taskQueries"] = new JsonObject { [listPath] = f.State["tasks"]!.DeepClone() };
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

    internal static string C1087ListPath(string project, string kind) =>
        $"/api/agent-tasks?projectId={project}&unscoped=include&includeChecks=true&" +
        (kind == "open" ? "status=Queued,Dispatched,Working,Blocked" : "landPending=true");

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1087_Census_filters_open_work_and_pending_lands()
    {
        const string project = C994TaskVectors.Project;
        const string id = "11111111-1111-1111-1111-111111111111";
        foreach (var status in new[] { "Failed", "Canceled", "Succeeded", "Queued", "Dispatched", "Working", "Blocked" })
        {
            using var f = new C1008WrapperFixture();
            var row = C1087TaskRow(id, project); row["status"] = status; row["runnerId"] = "server2-temp";
            f.State["tasks"]!["items"]!.AsArray().Add(row);
            var run = await f.Run("retire-temp");
            var bound = status is "Queued" or "Dispatched" or "Working" or "Blocked";
            run.Exit.ShouldBe(bound ? 2 : 0, status + ": " + run.Output);
            if (bound)
            {
                run.Output.ShouldContain($"RecycleBoundTasks {id} status={status} runner=server2-temp");
                run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
                C1087Receipt(run.Trace)["tasks"]!.AsArray().Single()!["status"]!.GetValue<string>().ShouldBe(status);
            }
            else
            {
                run.Trace.Where(t => t["path"]?.GetValue<string>().StartsWith("/api/agent-tasks?") == true)
                    .Select(t => t["path"]!.GetValue<string>()).Distinct().Order()
                    .ShouldBe(new[] { C1087ListPath(project, "open"), C1087ListPath(project, "land") }.Order());
                run.Trace.Any(t => t["path"]?.GetValue<string>() == "/api/agent-tasks/" + id).ShouldBeFalse();
            }
        }
        // CARD-1353 D-1: a pending land refuses only when its row is bound to the recycled runner
        // (server2-temp for retire-temp). The off-runner pending-* vectors pass and reach the host case.
        foreach (var vector in new[] { "pending-requested", "pending-started", "bound-pending-requested", "bound-pending-started",
                     "unfiltered-open", "unfiltered-land", "unstable", "closure" })
        {
            using var f = new C1008WrapperFixture();
            var empty = f.State["tasks"]!.DeepClone();
            var row = C1087TaskRow(id, project);
            var envelope = empty.DeepClone(); envelope["items"]!.AsArray().Add(row);
            var queries = new JsonObject(); f.State["taskQueries"] = queries;
            var expected = "RecycleLandInFlight " + id;
            var accepted = false;
            switch (vector)
            {
                case "pending-requested": case "pending-started": case "bound-pending-requested": case "bound-pending-started":
                    row["status"] = "Succeeded";
                    if (vector.StartsWith("bound-", StringComparison.Ordinal)) row["runnerId"] = "server2-temp";
                    else accepted = true;
                    row[vector.EndsWith("-requested", StringComparison.Ordinal) ? "landRequestedAt" : "landStartedAt"] = "2020-01-01T00:00:00Z";
                    queries[C1087ListPath(project, "land")] = envelope; break;
                case "unfiltered-open":
                    row["status"] = "Succeeded"; queries[C1087ListPath(project, "open")] = envelope;
                    expected = "RecycleTaskCensusUnknown cause=UnfilteredRow id=" + id; break;
                case "unfiltered-land":
                    queries[C1087ListPath(project, "land")] = envelope;
                    expected = "RecycleTaskCensusUnknown cause=UnfilteredRow id=" + id; break;
                case "unstable":
                    // A no-land appearance is CARD-1324. This vector keeps the land-request refusal.
                    queries[C1087ListPath(project, "open")] = new JsonArray(empty.DeepClone(), envelope);
                    f.State["details"] = new JsonObject { [id] = new JsonObject { ["summary"] = row.DeepClone(), ["landRequest"] = new JsonObject {
                        ["state"] = "Completed", ["terminalEventId"] = "44444444-4444-4444-4444-444444444444" } } };
                    expected = "RecycleTaskCensusUnknown cause=Unstable"; break;
                case "closure":
                    const string other = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
                    // Both closures independently walk the excluded scope, with their own filter.
                    foreach (var kind in new[] { "open", "land" })
                    {
                        var entry = empty.DeepClone(); entry["excluded"]!["total"] = 1;
                        entry["excluded"]!["byProject"]!.AsArray().Add(new JsonObject { ["projectId"] = other, ["count"] = 1 });
                        queries[C1087ListPath(project, kind)] = entry;
                        var next = envelope.DeepClone(); next["scope"]!["projectId"] = other;
                        next["items"]![0]!["projectId"] = other;
                        // The nested land row is bound to the recycled runner so the nested walk still refuses.
                        if (kind == "land") { next["items"]![0]!["landRequestedAt"] = "2020-01-01T00:00:00Z"; next["items"]![0]!["runnerId"] = "server2-temp"; }
                        else f.State["details"] = new JsonObject { [id] = new JsonObject { ["summary"] = next["items"]![0]!.DeepClone(), ["landRequest"] = null } };
                        queries[C1087ListPath(other, kind)] = next;
                    }
                    break;
            }
            var run = await f.Run("retire-temp");
            if (accepted)
            {
                run.Exit.ShouldBe(0, vector + ": " + run.Output);
                run.Output.ShouldNotContain("RecycleLandInFlight");
                run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case" && t["name"]?.GetValue<string>() == "retire-temp-runner")
                    .ShouldBeTrue(vector + ": an off-runner pending land reaches the host case; " + run.Output);
                C1087Receipt(run.Trace)["tasks"]!.AsArray().Any(t => t!["id"]!.GetValue<string>() == id)
                    .ShouldBeTrue(vector + ": the off-runner land is still recorded in the receipt");
            }
            else
            {
                run.Exit.ShouldBe(2, vector + ": " + run.Output); run.Output.ShouldContain(expected);
                run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case").ShouldBeFalse();
            }
            var paths = run.Trace.Select(t => t["path"]?.GetValue<string>()).Where(p => p?.StartsWith("/api/agent-tasks?") == true).ToArray();
            paths.All(p => p!.EndsWith("&status=Queued,Dispatched,Working,Blocked") || p.EndsWith("&landPending=true")).ShouldBeTrue();
            if (vector == "closure") paths.ShouldContain(C1087ListPath("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "land"));
            C1087Receipt(run.Trace).ToJsonString().ShouldNotContain("SENTINEL");
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1087_Check_census_phase_is_read_only()
    {
        const string project = C994TaskVectors.Project;
        using var f = new C1008WrapperFixture();
        var id = "11111111-1111-1111-1111-111111111111";
        var row = C1087TaskRow(id, project); row["runnerId"] = "server2";
        f.State["tasks"]!["items"]!.AsArray().Add(row);
        var landed = C1087TaskRow("22222222-2222-2222-2222-222222222222", project);
        landed["status"] = "Succeeded"; landed["landRequestedAt"] = "2020-01-01T00:00:00Z";
        f.State["tasks"]!["items"]!.AsArray().Add(landed);
        // CARD-1353 D-7: landPending keeps counting every pending land; boundLandPending counts the
        // lands whose row is bound to the reported runner.
        var started = C1087TaskRow("33333333-3333-3333-3333-333333333333", project);
        started["status"] = "Succeeded"; started["landStartedAt"] = "2020-01-01T00:00:00Z"; started["runnerId"] = "server2-temp";
        f.State["tasks"]!["items"]!.AsArray().Add(started);
        var run = await f.Run("check-census");
        run.Exit.ShouldBe(0, run.Output);
        run.Output.ShouldContain($"RECYCLE_PROJECT id={project} name=Antiphon resolvedBy=url");
        run.Output.ShouldContain("RECYCLE_CENSUS runner=server2 open=1 boundOpen=1 landPending=2 boundLandPending=0 scopes=1 elapsedMs=");
        run.Output.ShouldContain("RECYCLE_CENSUS runner=server2-temp open=1 boundOpen=0 landPending=2 boundLandPending=1 scopes=1 elapsedMs=");
        run.Trace.All(t => t["kind"]?.GetValue<string>() == "http" && t["method"]?.GetValue<string>() == "GET").ShouldBeTrue("no host jq, SSH, verify or POST");
        var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)").Groups[1].Value.Trim();
        foreach (var runner in new[] { "server2", "server2-temp" })
        {
            var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, $"census-check-census-{runner}.json")))!;
            receipt["project"]!["id"]!.GetValue<string>().ShouldBe(project);
            receipt["reads"]!.AsArray().Count.ShouldBeGreaterThanOrEqualTo(4);
            receipt.ToJsonString().ShouldNotContain("SENTINEL");
        }
        foreach (var fault in new[] { "project", "read" })
        {
            using var bad = new C1008WrapperFixture();
            if (fault == "project") bad.State["projects"] = new JsonArray();
            else bad.State["readFaults"] = new JsonObject { ["&landPending=true"] = "400" };
            var refused = await bad.Run("check-census");
            refused.Exit.ShouldBe(2, refused.Output);
            refused.Output.ShouldContain(fault == "project" ? "RecycleProjectUnresolved cause=NoMatch" : "RecycleTaskCensusUnknown cause=Http status=400");
            refused.Trace.All(t => t["kind"]?.GetValue<string>() == "http" && t["method"]?.GetValue<string>() == "GET").ShouldBeTrue();
        }
    }

    private static JsonObject C1087Project(string id, string url, string path) => new()
    {
        ["id"] = id, ["name"] = "Antiphon", ["gitRepositoryUrl"] = url,
        ["localRepositoryPath"] = path, ["archivedAt"] = null
    };

    private static JsonObject C1087TaskRow(string id, string project) => new()
    {
        ["id"] = id, ["status"] = "Blocked", ["runnerId"] = "other", ["projectId"] = project,
        ["scopeSource"] = "Task", ["landRequestedAt"] = null, ["landStartedAt"] = null,
        ["title"] = "SENTINEL_TITLE", ["goal"] = "SENTINEL_GOAL", ["result"] = "SENTINEL_RESULT"
    };

    private static JsonObject C1087Receipt(JsonObject[] trace) => C1087Receipt(trace, "census-retire-temp-server2-temp.json");

    private static JsonObject C1087Receipt(JsonObject[] trace, string name)
    {
        var root = trace.First(t => t["kind"]?.GetValue<string>() == "prerequisite")["evidenceRoot"]!.GetValue<string>();
        var file = Path.Combine(root, name);
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
            var row = new JsonObject { ["id"] = id, ["status"] = vector is "active-excluded" or "unscoped" ? "Queued" : "Blocked",
                ["runnerId"] = vector is "active-excluded" or "unscoped" ? "server2-temp" : "other", ["projectId"] = vector == "unscoped" ? null : other,
                ["scopeSource"] = vector == "unscoped" ? "None" : "Task", ["landRequestedAt"] = null, ["landStartedAt"] = null };
            if (vector == "unscoped") first["items"]!.AsArray().Add(row.DeepClone());
            else {
                first["excluded"]!["total"] = vector == "wrong-count" ? 2 : 1;
                first["excluded"]!["byProject"]!.AsArray().Add(new JsonObject { ["projectId"] = other, ["count"] = 1 });
                if (vector != "withheld") second["items"]!.AsArray().Add(row.DeepClone());
                if (vector == "inconsistent") first["items"]!.AsArray().Add(new JsonObject { ["id"] = id, ["status"] = "Blocked",
                    ["runnerId"] = "other", ["projectId"] = null, ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null });
            }
            scoped.State["taskQueries"] = new JsonObject {
                [C1087ListPath(root, "open")] = first, [C1087ListPath(other, "open")] = second,
                [C1087ListPath(root, "land")] = scoped.State["tasks"]!.DeepClone() };
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
                while (!held.ReadTraceText().Contains("lock-request", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                    await Task.Delay(25);
                held.ReadTraceText().ShouldContain("lock-request", Case.Sensitive, "recycle-work-gates: real admission lock barrier reached");
                await Task.Delay(150);
                held.ReadTraceText().ShouldNotContain("\"POST\"", Case.Sensitive, "recycle-work-gates: held rollout lock excludes POST entry");
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
        foreach (var status in new[] { "Queued", "Dispatched", "Working", "Blocked", "Succeeded" })
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
        // CARD-1353 D-1/D-5: an off-runner row ("other") with an in-flight land state or a land
        // timestamp is recorded and counted, not refused; an unknown or omitted land proof still
        // refuses RecycleLandUnknown; a bound row (server2-temp) with a land timestamp still refuses.
        foreach (var state in new[] { "Queued", "Held", "Running", "NeedsResolution", "Unknown", "omitted", "started", "bound-started" })
        {
            using var invalid = new C1008WrapperFixture();
            var started = state is "started" or "bound-started";
            var row = new JsonObject { ["id"] = "11111111-1111-1111-1111-111111111111", ["status"] = started ? "Succeeded" : "Blocked",
                ["runnerId"] = state == "bound-started" ? "server2-temp" : "other",
                ["projectId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1", ["scopeSource"] = "Task", ["landRequestedAt"] = null,
                ["landStartedAt"] = started ? "2026-10-03T09:00:00Z" : null };
            invalid.State["tasks"]!["items"]!.AsArray().Add(row);
            var detail = new JsonObject { ["summary"] = row.DeepClone() };
            if (state != "omitted") detail["landRequest"] = started ? null : new JsonObject { ["state"] = state };
            invalid.State["details"] = new JsonObject { [row["id"]!.GetValue<string>()] = detail };
            var run = await invalid.Run("retire-temp");
            var accepted = state is "Queued" or "Held" or "Running" or "NeedsResolution" or "started";
            if (accepted)
            {
                run.Exit.ShouldBe(0, "recycle-work-gates: off-runner land " + state + "; " + run.Output);
                run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case" && x["name"]?.GetValue<string>() == "retire-temp-runner")
                    .ShouldBeTrue("recycle-work-gates: off-runner land " + state + " reaches the host case; " + run.Output);
            }
            else
            {
                run.Trace.Any(x => x["kind"]?.GetValue<string>() == "case").ShouldBeFalse("recycle-work-gates: open detail or terminal pending land " + state);
                run.Exit.ShouldBe(2);
                run.Output.ShouldContain(state == "bound-started" ? "RecycleLandInFlight 11111111-1111-1111-1111-111111111111" : "RecycleLandUnknown");
            }
        }
    }

    // CARD-1353 D-3/D-4. A land bound to the recycled runner makes every census call site wait,
    // bounded, instead of refusing on the first read. Only RecycleLandInFlight is retried; on expiry
    // the refusal keeps its first token and reports the polls it waited.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1353_Wrapper_waits_for_a_bound_land_then_proceeds()
    {
        const string project = C994TaskVectors.Project;
        const string id = "11111111-1111-1111-1111-111111111111";
        var landPath = C1087ListPath(project, "land");
        foreach (var arm in new[] { "clears", "expires", "zero" })
        {
            using var f = new C1008WrapperFixture();
            f.MainDrained(); f.State["allowClear"] = true;
            var pending = f.State["tasks"]!.DeepClone();
            var row = C1087TaskRow(id, project); row["status"] = "Succeeded"; row["runnerId"] = "server2";
            row["landRequestedAt"] = "2020-01-01T00:00:00Z";
            pending["items"]!.AsArray().Add(row);
            var queries = new JsonObject(); f.State["taskQueries"] = queries;
            // The stub keeps serving the last sequence element: a refusing census consumes one land
            // read, so [pending, empty] is one refusal and then a clean two-pass census.
            queries[landPath] = arm == "clears" ? new JsonArray(pending, f.State["tasks"]!.DeepClone()) : pending;
            if (arm == "clears") f.Environment["C727_TEST_LAND_WAIT_MS"] = "60000";
            else if (arm == "expires") f.Environment["C727_TEST_LAND_WAIT_MS"] = "1";
            var run = arm == "zero" ? await f.Run("redeploy-old", "-LandWaitMinutes", "0") : await f.Run("redeploy-old");
            var landReads = run.Trace.Count(t => t["path"]?.GetValue<string>() == landPath);
            var waitLines = Regex.Matches(run.Output, "(?m)^RECYCLE_LAND_WAIT runner=server2 id=" + id + " polls=\\d+\r?$").Count;
            var receipt = C1087Receipt(run.Trace, "census-redeploy-old-server2.json");
            switch (arm)
            {
                case "clears":
                    run.Exit.ShouldBe(0, arm + ": " + run.Output);
                    run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case" && t["name"]?.GetValue<string>() == "deploy-parent")
                        .ShouldBeTrue(arm + ": the recycle proceeds once the bound land has cleared; " + run.Output);
                    waitLines.ShouldBe(1, arm + ": one wait line; " + run.Output);
                    landReads.ShouldBeGreaterThanOrEqualTo(3, arm + ": one refusing read then a clean two-pass census");
                    receipt["landWait"]!["outcome"]!.GetValue<string>().ShouldBe("cleared", arm);
                    receipt["landWait"]!["polls"]!.GetValue<int>().ShouldBe(1, arm);
                    break;
                case "expires":
                    run.Exit.ShouldBe(2, arm + ": " + run.Output);
                    run.Output.ShouldContain($"RecycleLandInFlight {id} runner=server2 waited=");
                    run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case").ShouldBeFalse(arm + ": no host case after an expired wait");
                    receipt["landWait"]!["outcome"]!.GetValue<string>().ShouldBe("expired", arm);
                    break;
                default:
                    run.Exit.ShouldBe(2, arm + ": " + run.Output);
                    run.Output.ShouldContain($"RecycleLandInFlight {id} runner=server2 waited=0polls");
                    landReads.ShouldBe(1, arm + ": -LandWaitMinutes 0 refuses on the first land read");
                    waitLines.ShouldBe(0, arm);
                    run.Trace.Any(t => t["kind"]?.GetValue<string>() == "case").ShouldBeFalse(arm);
                    break;
            }
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
    [Arguments("present")]
    [Arguments("absent")]
    [Arguments("missing-shell")]
    [Arguments("failing-shell")]
    [Timeout(400_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Legacy_rolling_and_jq_rosters_remain(string mode, CancellationToken cancellationToken)
    {
        // Each mode runs the complete 62/66-invocation roster. Keep its assertions and
        // serial process limiter, with a deadline per mode rather than for all 252 children.
        var arguments = new List<string> { "-NoProfile", "-File",
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/test-deploy-server2-jq.ps1"), "-Case", mode };
        if (mode == "present")
        {
            arguments.Add("-RequireJq");
            arguments.ShouldContain("-RequireJq", customMessage: "c983-consumer-requires-jq");
        }
        var run = await C1008Process("pwsh", cancellationToken, arguments.ToArray());
        Console.WriteLine(run.Output);
        run.Output.ShouldContain($"C973_JQ case={mode} assertions=31 failures=0", Case.Sensitive, "rolling-regressions-preserved: " + run.Output);
        if (mode == "present") run.Output.ShouldNotContain("C973_JQ_SKIPPED");
        run.Exit.ShouldBe(0);
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

    private static Task<(int Exit, string Output)> C1008Process(string command, params string[] args)
        => C1008Process(command, CancellationToken.None, args);

    private static async Task<(int Exit, string Output)> C1008Process(string command, CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi)!; var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(12));
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
        var retiredExtra = $"C1008_OPERATION='{operation}'; C590_TEMP_RETIRED_AT='{stamp}'";
        await VolumeRetirementFixture.ReadyPreview(host, retiredExtra);
        var retired = await host.Run("retire-temp-runner", retiredExtra);
        retired.Exit.ShouldBe(0, "retire-absent-null-accepted: actual host entry using wrapper context; " + retired.Output);
        host.Removed.ShouldBe(new[] { "antiphon-runner-temp_runner-state", "antiphon-runner-temp_dind-data",
            "antiphon-runner-temp_runner-tmp", "antiphon-runner-temp_work" });
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

    // A retire-temp apply writes the session image. The next preview cleanup keeps
    // that image. A bound dry-run runs the same publication audit inside that pinned
    // image: both volumes read-only, the same network as apply, every capability
    // dropped, no-new-privileges, --init, --rm, and a hard timeout. The helper name
    // and label belong to this preview run. That helper is the only container a
    // preview may start. The host does not invoke Git. An unbound scan still starts
    // no container. Apply still requires a dryRun false receipt.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt()
    {
        C1008HostFixture.RequireNativeLinux();
        const string bound = "C1008_CLEANUP_OPERATION=" + PreviewCleanupOperation;

        // Explicit root, still named c1008-host-*. The stub covers /tmp with a scratch
        // tmpfs, then bind-mounts this root back on top, so a /var/tmp root and a
        // default /tmp root both stay visible to the audit.
        var root = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var clean = new C1008HostFixture(main: false, root);
            var image = await RetainPreviewImage(clean);
            await PublishRepo(clean);
            var description = Path.Combine(WorkMount(clean), "repo", ".git", "description");
            var before = await File.ReadAllBytesAsync(description);
            await InstallPreviewHook(clean, description, WorkMount(clean));
            ResetPreviewTrace(clean);
            var hostWork = await HostWorkMount();
            var run = await clean.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "recycle-preview-receipt: dry-run receipt is preview authority; " + run.Output);
            run.Output.ShouldContain("C1008_PREVIEW remove=antiphon-runner-temp_work");
            run.Output.ShouldContain("auditPending=false");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("RecycleUnpublishedWork");
            // The hook remounts read-write and a detached child writes after the audit returns.
            await Task.Delay(TimeSpan.FromSeconds(4));
            (await File.ReadAllBytesAsync(description)).ShouldBe(before);
            File.ReadAllText(Path.Combine(clean.Root, "hook-result")).ShouldContain("Read-only file system");
            AssertReadonlyPreview(clean, image, audited: true);
            (await HostWorkMount()).ShouldBe(hostWork, "recycle-preview-receipt: the helper leaves the host /work mount alone");
            CleanupReceipt(clean, PreviewCleanupOperation)["image"]!.GetValue<string>().ShouldBe(image,
                "recycle-preview-receipt: recycle does not rewrite the producer image");

            ResetPreviewTrace(clean);
            var apply = await clean.Run("retire-temp-runner", bound);
            apply.Exit.ShouldBe(2, "recycle-preview-receipt: apply refuses a dry-run receipt; " + apply.Output);
            apply.Output.ShouldContain("RecycleGitAuditUnknown");
            apply.Output.ShouldNotContain("C1008_PREVIEW");
            apply.Output.ShouldNotContain("RecycleWorktreeDirty");
            clean.Trace.Any(a => a.Length > 0 && a[0] == "create").ShouldBeFalse(
                "recycle-preview-receipt: apply does not audit a preview receipt");
            clean.Removed.ShouldBeEmpty("recycle-preview-receipt: a dry-run receipt removes nothing");

            File.Delete(CleanupReceiptPath(clean, AppliedCleanupOperation));
            ResetPreviewTrace(clean);
            var unbound = await clean.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            unbound.Exit.ShouldBe(0, "recycle-preview-receipt: unbound preview is not authority; " + unbound.Output);
            unbound.Output.ShouldContain("auditPending=true");
            unbound.Output.ShouldNotContain("auditPending=false");
            AssertReadonlyPreview(clean, image, audited: true);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        using (var dirty = new C1008HostFixture(main: false))
        {
            var dirtyImage = await RetainPreviewImage(dirty);
            await PublishRepo(dirty);
            ShareVolumeWithApply(dirty);
            var dirtyFile = Path.Combine(WorkMount(dirty), "repo", "dirty");
            await File.WriteAllTextAsync(dirtyFile, "unpublished-sentinel");
            ResetPreviewTrace(dirty);
            var run = await dirty.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(2, "recycle-preview-receipt: dirty worktree still refuses; " + run.Output);
            run.Output.ShouldContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            run.Output.ShouldNotContain("RecycleUnpublishedWork");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            AssertReadonlyPreview(dirty, dirtyImage, audited: true);
            File.ReadAllText(dirtyFile).ShouldBe("unpublished-sentinel");
            await AssertApplyMatchesPreview(dirty, "RecycleWorktreeDirty");
        }

        // Same explicit root as the clean case. Origin lives inside that root, which
        // the stub bind-mounts above its scratch tmpfs.
        var unpublishedRoot = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unpublishedRoot);
        try
        {
            using var unpublished = new C1008HostFixture(main: false, unpublishedRoot);
            var unpublishedImage = await RetainPreviewImage(unpublished);
            await PublishRepo(unpublished);
            ShareVolumeWithApply(unpublished);
            var repo = Path.Combine(WorkMount(unpublished), "repo");
            var local = Path.Combine(repo, "local");
            await File.WriteAllTextAsync(local, "unpublished-sentinel");
            var add = await C994ScriptProcess.Run("git", "-C", repo, "add", "local");
            add.Exit.ShouldBe(0, "recycle-preview-receipt: unpublished add; " + add.Output);
            var commit = await C994ScriptProcess.Run("git", "-C", repo, "commit", "-qm", "unpublished");
            commit.Exit.ShouldBe(0, "recycle-preview-receipt: unpublished commit; " + commit.Output);
            ResetPreviewTrace(unpublished);
            var run = await unpublished.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(2, "recycle-preview-receipt: unpublished work still refuses; " + run.Output);
            run.Output.ShouldContain("RecycleUnpublishedWork");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            run.Output.ShouldNotContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            AssertReadonlyPreview(unpublished, unpublishedImage, audited: true);
            File.ReadAllText(local).ShouldBe("unpublished-sentinel");
            await AssertApplyMatchesPreview(unpublished, "RecycleUnpublishedWork");
        }
        finally
        {
            if (Directory.Exists(unpublishedRoot)) Directory.Delete(unpublishedRoot, recursive: true);
        }

        using (var unknown = new C1008HostFixture(main: false))
        {
            var unknownImage = await RetainPreviewImage(unknown);
            Directory.CreateDirectory(Path.Combine(WorkMount(unknown), "broken", ".git"));
            ResetPreviewTrace(unknown);
            var run = await unknown.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(2, "recycle-preview-receipt: unreadable Git still refuses; " + run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            run.Output.ShouldNotContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("RecycleUnpublishedWork");
            AssertReadonlyPreview(unknown, unknownImage, audited: true);
        }

        var linkedRoot = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(linkedRoot);
        try
        {
            using var linked = new C1008HostFixture(main: false, linkedRoot);
            var linkedImage = await RetainPreviewImage(linked);
            await PublishLinkedWorktree(linked);
            ResetPreviewTrace(linked);
            var hostWork = await HostWorkMount();
            var run = await linked.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "recycle-preview-receipt: /work gitdir pointers resolve; " + run.Output);
            run.Output.ShouldContain("auditPending=false");
            run.Output.ShouldNotContain("worktree-confine");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("RecycleUnpublishedWork");
            AssertReadonlyPreview(linked, linkedImage, audited: true);
            (await HostWorkMount()).ShouldBe(hostWork, "recycle-preview-receipt: linked-worktree preview leaves the host /work mount alone");
        }
        finally
        {
            if (Directory.Exists(linkedRoot)) Directory.Delete(linkedRoot, recursive: true);
        }
    }

    // A temp retired at SHA A keeps its completed cleanup image. Preview and apply at
    // SHA B must audit with that image. A different retirement, a missing image, and
    // no record do not.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1594_Cross_sha_cleanup_image_binds_the_retired_temp()
    {
        C1008HostFixture.RequireNativeLinux();
        const string boundEmpty = "C1008_CLEANUP_OPERATION=" + CrossEmptyOperation;

        using (var clean = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(clean);
            var cross = CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            var empty = CloneReceipt(seed, CrossEmptyOperation, image: "");
            ReplaceReceipts(clean, cross, empty);
            TouchNewer(clean, CrossEmptyOperation);
            await PublishRepo(clean);
            ShareVolumeWithApply(clean);
            ResetPreviewTrace(clean);
            var run = await clean.Run("retire-temp-runner", boundEmpty + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-cross-sha: preview audits with the retired image; " + run.Output);
            run.Output.ShouldContain("auditPending=false");
            run.Output.ShouldNotContain("C994_IMAGE_UNPINNED");
            AssertReadonlyPreview(clean, CrossShaImage, audited: true);

            ResetPreviewTrace(clean);
            var applied = await clean.Run("retire-temp-runner", boundEmpty);
            applied.Exit.ShouldBe(0, "c1594-cross-sha: apply audits with the retired image; " + applied.Output);
            var created = clean.Trace.Where(args => args.Length > 0 && args[0] == "create" && args.Contains("--entrypoint")).ToArray();
            created.Length.ShouldBe(2, "c1594-cross-sha: apply audits before and after removal");
            foreach (var args in created)
            {
                args.ShouldContain(CrossShaImage);
                args.ShouldContain("--user");
                args.ShouldContain("1654:1654");
            }
        }

        using (var dirty = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(dirty);
            ReplaceReceipts(dirty,
                CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage),
                CloneReceipt(seed, CrossEmptyOperation, image: ""));
            await PublishRepo(dirty);
            ShareVolumeWithApply(dirty);
            await File.WriteAllTextAsync(Path.Combine(WorkMount(dirty), "repo", "dirty"), "unpublished-sentinel");
            ResetPreviewTrace(dirty);
            var run = await dirty.Run("retire-temp-runner", boundEmpty + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(2, "c1594-cross-sha: dirty work still refuses; " + run.Output);
            run.Output.ShouldContain("RecycleWorktreeDirty");
            run.Output.ShouldNotContain("auditPending=true");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            AssertReadonlyPreview(dirty, CrossShaImage, audited: true);
            dirty.Removed.ShouldBeEmpty();
        }

        var unpublishedRoot = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unpublishedRoot);
        try
        {
            using var unpublished = new C1008HostFixture(main: false, unpublishedRoot);
            var seed = await CaptureCleanupSeed(unpublished);
            ReplaceReceipts(unpublished,
                CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage),
                CloneReceipt(seed, CrossEmptyOperation, image: ""));
            await PublishRepo(unpublished);
            ShareVolumeWithApply(unpublished);
            var repo = Path.Combine(WorkMount(unpublished), "repo");
            var local = Path.Combine(repo, "local");
            await File.WriteAllTextAsync(local, "unpublished-sentinel");
            var add = await C994ScriptProcess.Run("git", "-C", repo, "add", "local");
            add.Exit.ShouldBe(0, "c1594-cross-sha: unpublished add; " + add.Output);
            var commit = await C994ScriptProcess.Run("git", "-C", repo, "commit", "-qm", "unpublished");
            commit.Exit.ShouldBe(0, "c1594-cross-sha: unpublished commit; " + commit.Output);
            ResetPreviewTrace(unpublished);
            var run = await unpublished.Run("retire-temp-runner", boundEmpty + "\n" + PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(2, "c1594-cross-sha: unpublished work still refuses; " + run.Output);
            run.Output.ShouldContain("RecycleUnpublishedWork");
            run.Output.ShouldNotContain("auditPending=true");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            AssertReadonlyPreview(unpublished, CrossShaImage, audited: true);
            unpublished.Removed.ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(unpublishedRoot)) Directory.Delete(unpublishedRoot, recursive: true);
        }

        foreach (var (field, value) in new (string, string)[]
        {
            ("retiredAt", "2026-10-04T09:30:00.000000000Z"),
            ("projectId", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ("project", "antiphon-runner")
        })
        {
            using var refused = new C1008HostFixture(main: false);
            var seed = await CaptureCleanupSeed(refused);
            var foreign = CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            foreign[field] = value;
            ReplaceReceipts(refused, foreign);
            ResetPreviewTrace(refused);
            var run = await refused.Run("retire-temp-runner", "C1008_CLEANUP_OPERATION=" + CrossShaOperation, dryRun: true);
            run.Exit.ShouldBe(2, "c1594-cross-sha: " + field + " is not this retirement; " + run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("auditPending=true");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            refused.Trace.Any(args => args.Length > 0 && args[0] == "run").ShouldBeFalse("c1594-cross-sha: " + field);
            refused.Removed.ShouldBeEmpty();
        }

        using (var missing = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(missing);
            ReplaceReceipts(missing, CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha,
                image: "sha256:" + new string('b', 64)));
            ResetPreviewTrace(missing);
            missing.Docker["fault"] = "original-image-missing";
            var run = await missing.Run("retire-temp-runner", "C1008_CLEANUP_OPERATION=" + CrossShaOperation, dryRun: true);
            run.Exit.ShouldBe(2, "c1594-cross-sha: a missing pinned image refuses; " + run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("auditPending=true");
            run.Output.ShouldNotContain("C1008_PREVIEW");
            missing.Trace.Any(args => args.Length > 0 && args[0] == "run").ShouldBeFalse("c1594-cross-sha: missing image starts no helper");
            missing.Removed.ShouldBeEmpty();
        }

        using (var unpinned = new C1008HostFixture(main: false))
        {
            var preview = await unpinned.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            preview.Exit.ShouldBe(0, "c1594-cross-sha: no record stays an unpinned preview; " + preview.Output);
            preview.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
            preview.Output.ShouldContain("auditPending=true");
            preview.Output.ShouldNotContain("auditPending=false");
            AssertReadonlyPreview(unpinned, C1008HostFixture.SessionImage, audited: true);
            ResetPreviewTrace(unpinned);
            unpinned.Docker["fault"] = "image-inspect-error";
            var apply = await unpinned.Run("retire-temp-runner");
            apply.Exit.ShouldBe(2, "c1594-cross-sha: unpinned apply refuses when the current image is absent; " + apply.Output);
            apply.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
            apply.Output.ShouldContain("RecycleGitAuditUnknown");
            unpinned.Removed.ShouldBeEmpty();
        }

        using (var preferred = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(preferred);
            var current = CloneReceipt(seed, AppliedCleanupOperation, image: C1008HostFixture.SessionImage);
            var newer = CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            ReplaceReceipts(preferred, current, newer);
            TouchNewer(preferred, CrossShaOperation);
            ResetPreviewTrace(preferred);
            var run = await preferred.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-cross-sha: the current SHA image still wins; " + run.Output);
            run.Output.ShouldContain("auditPending=false");
            AssertReadonlyPreview(preferred, C1008HostFixture.SessionImage, audited: true);
        }
    }

    // A receipt from another retirement is history. The same retirement's completed
    // cross-SHA image still binds. Partial and dry-run cross-SHA receipts do not.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1594_Older_retirement_receipt_is_ignored()
    {
        C1008HostFixture.RequireNativeLinux();
        const string olderOperation = "c99400000000000000000000000000000019";
        const string partialOperation = "c9940000000000000000000000000000001a";
        const string dryOperation = "c9940000000000000000000000000000001b";

        using (var older = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(older);
            var foreign = CloneReceipt(seed, olderOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            foreign["retiredAt"] = "2026-10-02T09:30:00.000000000Z";
            ReplaceReceipts(older, foreign);
            ResetPreviewTrace(older);
            var run = await older.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-older-retirement: a different retirement warns; " + run.Output);
            run.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            run.Output.ShouldContain("auditPending=true");
            AssertReadonlyPreview(older, C1008HostFixture.SessionImage, audited: true);
        }

        using (var bound = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(bound);
            var foreign = CloneReceipt(seed, olderOperation, sourceSha: CrossSourceSha, image: "sha256:" + new string('d', 64));
            foreign["retiredAt"] = "2026-10-02T09:30:00.000000000Z";
            var cross = CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            ReplaceReceipts(bound, foreign, cross);
            await PublishRepo(bound);
            ShareVolumeWithApply(bound);
            ResetPreviewTrace(bound);
            var run = await bound.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-older-retirement: the same retirement still binds; " + run.Output);
            run.Output.ShouldNotContain("C994_IMAGE_UNPINNED");
            run.Output.ShouldContain("auditPending=false");
            AssertReadonlyPreview(bound, CrossShaImage, audited: true);
        }

        using (var partial = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(partial);
            var record = CloneReceipt(seed, partialOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            record["outcome"] = "partial";
            ReplaceReceipts(partial, record);
            ResetPreviewTrace(partial);
            var run = await partial.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-older-retirement: a partial cross-SHA receipt does not count; " + run.Output);
            run.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            AssertReadonlyPreview(partial, C1008HostFixture.SessionImage, audited: true);
        }

        using (var dry = new C1008HostFixture(main: false))
        {
            var seed = await CaptureCleanupSeed(dry);
            var record = CloneReceipt(seed, dryOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
            record["dryRun"] = true;
            record["outcome"] = "preview";
            ReplaceReceipts(dry, record);
            ResetPreviewTrace(dry);
            var run = await dry.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
            run.Exit.ShouldBe(0, "c1594-older-retirement: a dry-run cross-SHA receipt does not count; " + run.Output);
            run.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
            run.Output.ShouldNotContain("RecycleGitAuditUnknown");
            AssertReadonlyPreview(dry, C1008HostFixture.SessionImage, audited: true);
        }
    }

    // Two completed receipts from other SHAs for this retirement must agree.
    // The newest image is not a tie-break when they differ.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1594_Disagreeing_cross_sha_images_refuse()
    {
        C1008HostFixture.RequireNativeLinux();
        const string otherOperation = "c9940000000000000000000000000000001c";
        const string otherSha = "dddddddddddddddddddddddddddddddddddddddd";
        var newerImage = "sha256:" + new string('e', 64);
        using var fixture = new C1008HostFixture(main: false);
        var seed = await CaptureCleanupSeed(fixture);
        var first = CloneReceipt(seed, CrossShaOperation, sourceSha: CrossSourceSha, image: CrossShaImage);
        var second = CloneReceipt(seed, otherOperation, sourceSha: otherSha, image: newerImage);
        ReplaceReceipts(fixture, first, second);
        TouchNewer(fixture, otherOperation);
        ResetPreviewTrace(fixture);
        var run = await fixture.Run("retire-temp-runner", PreviewWitnessProbe, dryRun: true);
        run.Exit.ShouldBe(0, "c1594-cross-sha: different images from other SHAs warn; content is the gate; " + run.Output);
        run.Output.ShouldContain("RecycleGitAuditUnknown");
        run.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        run.Output.ShouldNotContain(newerImage);
        fixture.Trace.Any(args => args.Length > 0 && args[0] == "create").ShouldBeFalse(
            "c1594-cross-sha: a disagreement does not authorize a delete");
        fixture.Removed.ShouldBeEmpty();
    }

    // CARD-1594. antiphon-evidence/ and review-evidence/ under the common git
    // directory are agent output. Plain files pass, including .antiphon/x.md,
    // .gitignore, config, index, hooks, info, description, modules, *.lock and
    // report.md~. A symlink, nested .git, bare repo, .git pointer, deep repo,
    // reftable, lone commondir, newline name, refs, objects, HEAD, packed-refs,
    // unreadable file, fifo, outside hard link, or the same directory name in a
    // linked admin refuses gitdir-entry and names that entry. antiphon/ is not
    // walked, so an interior symlink still passes.
    [Test]
    [Arguments("antiphon-evidence")]
    [Arguments("antiphon-evidence-symlink")]
    [Arguments("antiphon-evidence-symlink-root")]
    [Arguments("antiphon-evidence-git")]
    [Arguments("antiphon-evidence-refs")]
    [Arguments("antiphon-evidence-objects")]
    [Arguments("antiphon-evidence-head")]
    [Arguments("antiphon-evidence-packed-refs")]
    [Arguments("antiphon-evidence-odd")]
    [Arguments("antiphon-evidence-unreadable")]
    [Arguments("antiphon-evidence-hardlink")]
    [Arguments("antiphon-evidence-dot")]
    [Arguments("antiphon-evidence-linked")]
    [Arguments("review-evidence-reports")]
    [Arguments("review-evidence-repo")]
    [Arguments("review-evidence-bare")]
    [Arguments("review-evidence-gitfile")]
    [Arguments("review-evidence-deep")]
    [Arguments("review-evidence-reftable")]
    [Arguments("review-evidence-commondir")]
    [Arguments("review-evidence-newline")]
    [Arguments("review-evidence-symlink")]
    [Arguments("review-evidence-head")]
    [Arguments("review-evidence-refs")]
    [Arguments("review-evidence-objects")]
    [Arguments("review-evidence-packed-refs")]
    [Arguments("review-evidence-odd")]
    [Arguments("review-evidence-unreadable")]
    [Arguments("review-evidence-hardlink")]
    [Arguments("review-evidence-dot")]
    [Arguments("review-evidence-config")]
    [Arguments("review-evidence-index")]
    [Arguments("review-evidence-hooks")]
    [Arguments("review-evidence-info")]
    [Arguments("review-evidence-description")]
    [Arguments("review-evidence-modules")]
    [Arguments("review-evidence-lock")]
    [Arguments("review-evidence-antiphon")]
    [Arguments("review-evidence-backup")]
    [Arguments("antiphon-symlink")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1594_Antiphon_evidence_gitdir(string variant)
    {
        C1008HostFixture.RequireNativeLinux();
        var output = RemoteScriptContractTests.LinuxShell(
            "node \"$repo/scripts/fixtures/c1105-git-audit.cjs\" " + variant
            + " \"$repo/scripts/c590-remote.sh\"\n", "repo");
        output.ShouldContain("PASS " + variant);
    }

    private const string CrossSourceSha = "f84228f2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CrossShaImage = "sha256:4ae919704ae919704ae919704ae919704ae919704ae919704ae919704ae91970";
    private const string CrossShaOperation = "c99400000000000000000000000000000011";
    private const string CrossEmptyOperation = "c99400000000000000000000000000000012";

    private static async Task<JsonObject> CaptureCleanupSeed(C1008HostFixture fixture)
    {
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var sha = new string('a', 40);
        fixture.Statuses["server2-temp"]!["buildVersion"] = sha;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "temp.env"),
            "RUNNER_GROK_STORE_DIR=" + fixture.Root + "/grok\nSOURCE_REVISION=" + sha + "\n");
        var applied = await fixture.Run("retire-temp-containers");
        applied.Exit.ShouldBe(0, "c1594-cross-sha: seed cleanup; " + applied.Output);
        var seed = CleanupReceipt(fixture, AppliedCleanupOperation);
        fixture.ReloadDocker();
        return seed;
    }

    private static JsonObject CloneReceipt(JsonObject seed, string operation, string? sourceSha = null, string? image = null)
    {
        var copy = JsonNode.Parse(seed.ToJsonString())!.AsObject();
        copy["operationId"] = operation;
        copy["dryRun"] = false;
        copy["outcome"] = "completed";
        if (sourceSha is not null) copy["sourceSha"] = sourceSha;
        if (image is not null) copy["image"] = image;
        copy.Remove("originalCleanupOperationId");
        return copy;
    }

    private static void ReplaceReceipts(C1008HostFixture fixture, params JsonObject[] records)
    {
        var dir = Path.Combine(fixture.Root, "server", "temp-container-retirement");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(dir, "c994*.json")) File.Delete(file);
        foreach (var record in records)
            File.WriteAllText(Path.Combine(dir, record["operationId"]!.GetValue<string>() + ".json"), record.ToJsonString());
    }

    private static void TouchNewer(C1008HostFixture fixture, string operation)
    {
        File.SetLastWriteTimeUtc(
            Path.Combine(fixture.Root, "server", "temp-container-retirement", operation + ".json"),
            DateTime.UtcNow.AddMinutes(5));
    }

    private const string AppliedCleanupOperation = "c99400000000000000000000000000000001";
    private const string PreviewCleanupOperation = "c99400000000000000000000000000000002";

    private static string WorkMount(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "volumes", "antiphon-runner-temp_work", "_data");

    // Apply's fixture helper reads the checkout through the fixture work directory.
    // The preview mounts the volume. The same tree has to satisfy both.
    private static void ShareVolumeWithApply(C1008HostFixture fixture)
    {
        var link = Path.Combine(fixture.Root, "work");
        Directory.Delete(link);
        Directory.CreateSymbolicLink(link, WorkMount(fixture));
    }

    private static string CleanupReceiptPath(C1008HostFixture fixture, string operation) =>
        Path.Combine(fixture.Root, "server", "temp-container-retirement", operation + ".json");

    private static JsonObject CleanupReceipt(C1008HostFixture fixture, string operation) =>
        JsonNode.Parse(File.ReadAllText(CleanupReceiptPath(fixture, operation)))!.AsObject();

    private static async Task<string> RetainPreviewImage(C1008HostFixture fixture)
    {
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var sha = new string('a', 40);
        fixture.Statuses["server2-temp"]!["buildVersion"] = sha;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "temp.env"),
            "RUNNER_GROK_STORE_DIR=" + fixture.Root + "/grok\nSOURCE_REVISION=" + sha + "\n");
        var applied = await fixture.Run("retire-temp-containers");
        applied.Exit.ShouldBe(0, "recycle-preview-receipt: apply cleanup; " + applied.Output);
        var appliedReceipt = CleanupReceipt(fixture, AppliedCleanupOperation);
        var image = appliedReceipt["image"]!.GetValue<string>();
        image.ShouldBe(C1008HostFixture.SessionImage, "recycle-preview-receipt: natural session image");
        appliedReceipt["dryRun"]!.GetValue<bool>().ShouldBeFalse("recycle-preview-receipt: applied receipt is not a preview");
        fixture.ReloadDocker();
        var preview = await fixture.Run("retire-temp-containers", "C994_OPERATION=" + PreviewCleanupOperation, dryRun: true);
        preview.Exit.ShouldBe(0, "recycle-preview-receipt: preview cleanup; " + preview.Output);
        var previewReceipt = CleanupReceipt(fixture, PreviewCleanupOperation);
        previewReceipt["image"]!.GetValue<string>().ShouldBe(image, "recycle-preview-receipt: preview retains the applied image");
        previewReceipt["dryRun"]!.GetValue<bool>().ShouldBeTrue("recycle-preview-receipt: witness receipt is a preview");
        previewReceipt["outcome"]!.GetValue<string>().ShouldBe("preview");
        previewReceipt["originalCleanupOperationId"]!.GetValue<string>().ShouldBe(AppliedCleanupOperation);
        fixture.ReloadDocker();
        return image;
    }

    private static async Task PublishRepo(C1008HostFixture fixture)
    {
        var origin = Path.Combine(fixture.Root, "origin");
        var repo = Path.Combine(WorkMount(fixture), "repo");
        var setup = await C994ScriptProcess.Run("bash", "-c", $$"""
            set -euo pipefail
            git init -q --bare '{{origin}}'
            git init -q -b main '{{repo}}'
            git -C '{{repo}}' config user.name Fixture
            git -C '{{repo}}' config user.email fixture@example.invalid
            printf published > '{{repo}}/sentinel'
            git -C '{{repo}}' add sentinel
            git -C '{{repo}}' commit -qm published
            git -C '{{repo}}' remote add origin '{{origin}}'
            git -C '{{repo}}' push -qu origin main
            """);
        setup.Exit.ShouldBe(0, "recycle-preview-receipt: git setup; " + setup.Output);
    }

    private static async Task InstallPreviewHook(C1008HostFixture fixture, string description, string volume)
    {
        var repo = Path.Combine(WorkMount(fixture), "repo");
        var hook = Path.Combine(fixture.Root, "fsmonitor");
        var result = Path.Combine(fixture.Root, "hook-result");
        // Remount the volume and /work, then write. A detached child writes again after
        // the audit returns. Capture the shell stderr before the append's own redirection.
        await File.WriteAllTextAsync(hook, "#!/bin/bash\n" +
            "exec 2>" + BashSingle(result + ".err") + "\n" +
            "mount -o remount,bind,rw -- " + BashSingle(volume) + " 2>>" + BashSingle(result + ".err") + " || true\n" +
            "mount -o remount,bind,rw /work 2>>" + BashSingle(result + ".err") + " || true\n" +
            "if printf ' mutated' >> " + BashSingle(description) + "; then\n" +
            "  printf writable > " + BashSingle(result) + "\n" +
            "else\n" +
            "  cat " + BashSingle(result + ".err") + " > " + BashSingle(result) + "\n" +
            "fi\n" +
            "setsid bash -c " + BashSingle("sleep 2; mount -o remount,bind,rw -- \"$1\" 2>/dev/null || true; mount -o remount,bind,rw /work 2>/dev/null || true; printf ' late' >> \"$2\"") +
            " _ " + BashSingle(volume) + " " + BashSingle(description) + " </dev/null >/dev/null 2>&1 &\n" +
            "exit 0\n");
        var setup = await C994ScriptProcess.Run("bash", "-c",
            "chmod 0755 " + BashSingle(hook) + " && git -C " + BashSingle(repo) + " config core.fsmonitor " + BashSingle(hook));
        setup.Exit.ShouldBe(0, "recycle-preview-receipt: fsmonitor hook; " + setup.Output);
    }

    private static async Task PublishLinkedWorktree(C1008HostFixture fixture)
    {
        await PublishRepo(fixture);
        var mount = WorkMount(fixture);
        var repo = Path.Combine(mount, "repo");
        var wt = Path.Combine(mount, "wt");
        // main is already checked out in repo. A second linked worktree at that
        // commit has to be detached; checking main out twice is refused.
        var setup = await C994ScriptProcess.Run("git", "-C", repo, "worktree", "add", "--detach", "--quiet", wt, "main");
        setup.Exit.ShouldBe(0, "recycle-preview-receipt: linked worktree; " + setup.Output);
        RewriteWorkPointer(Path.Combine(repo, ".git", "worktrees", "wt", "gitdir"), mount);
        RewriteWorkPointer(Path.Combine(wt, ".git"), mount);
    }

    private static void RewriteWorkPointer(string path, string mount)
    {
        var text = File.ReadAllText(path);
        text.Contains(mount, StringComparison.Ordinal).ShouldBeTrue("recycle-preview-receipt: " + path + " records the host mount");
        File.WriteAllText(path, text.Replace(mount, "/work", StringComparison.Ordinal));
    }

    private static string BashSingle(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static async Task<string> HostWorkMount()
    {
        var run = await C994ScriptProcess.Run("findmnt", "-n", "-o", "SOURCE,OPTIONS", "-T", "/work");
        return run.Exit + "\n" + run.Output;
    }

    // The parent shell records a host Git call. The fixture helper sets
    // C1008_SIMULATED_HELPER so Git inside that container is not a host call.
    // timeout() stays in this shell: an external timeout would skip the docker() stub.
    private const string PreviewWitnessProbe = """
        timeout() {
            printf '%s\n' "$*" >> "$C1008_FIXTURE_ROOT/timeout-invocations"
            local saw=0
            while [ $# -gt 0 ]; do
                case "$1" in
                    --kill-after=*|--foreground|--preserve-status) shift ;;
                    --kill-after) shift 2 ;;
                    *s|[0-9]*) shift; saw=1; break ;;
                    *) break ;;
                esac
            done
            [ "$saw" = 1 ]
            "$@"
        }
        git() {
            if [ -z "${C1008_SIMULATED_HELPER:-}" ]; then
                printf '%s\n' "$*" >> "$C1008_FIXTURE_ROOT/host-git"
            fi
            command git "$@"
        }
        export -f git
        """;

    private static void ResetPreviewTrace(C1008HostFixture fixture)
    {
        foreach (var name in new[] { "docker-trace.jsonl", "host-git", "timeout-invocations", "preview-helper-running" })
        {
            var path = Path.Combine(fixture.Root, name);
            if (File.Exists(path)) File.Delete(path);
        }
        fixture.ReloadDocker();
    }

    private static void AssertReadonlyPreview(C1008HostFixture fixture, string image, bool audited)
    {
        var runs = fixture.Trace.Where(args => args.Length > 0 && args[0] == "run").ToArray();
        if (!audited)
            runs.ShouldBeEmpty("recycle-preview-readonly: an unbound preview starts no helper");
        else
        {
            runs.Length.ShouldBe(1, "recycle-preview-readonly: one pinned helper");
            var args = runs[0];
            args.ShouldContain("--rm");
            args.ShouldContain("--init");
            Array.IndexOf(args, "--network").ShouldBe(-1, "recycle-preview-readonly: preview uses apply's network");
            var security = Array.IndexOf(args, "--security-opt");
            security.ShouldBeGreaterThanOrEqualTo(0, "recycle-preview-readonly: preview sets no-new-privileges");
            args[security + 1].ShouldBe("no-new-privileges");
            var named = Array.IndexOf(args, "--name");
            named.ShouldBeGreaterThanOrEqualTo(0);
            Regex.IsMatch(args[named + 1], "^c1008-preview-c100800000000000000000000000000000001-[0-9a-f]{16}$")
                .ShouldBeTrue("recycle-preview-readonly: name is this operation plus a private token: " + args[named + 1]);
            var label = Array.IndexOf(args, "--label");
            label.ShouldBeGreaterThanOrEqualTo(0);
            args[label + 1].ShouldStartWith("io.antiphon.preview-audit=c100800000000000000000000000000000001:");
            args.ShouldContain("--cidfile");
            var dropped = Array.IndexOf(args, "--cap-drop");
            dropped.ShouldBeGreaterThanOrEqualTo(0);
            args[dropped + 1].ShouldBe("ALL");
            args.ShouldNotContain("--privileged");
            args.ShouldNotContain("--cap-add");
            args.ShouldNotContain("-v");
            args.ShouldNotContain("--volume");
            var program = Array.IndexOf(args, "-c");
            program.ShouldBeGreaterThan(0);
            args[program - 1].ShouldBe(image);
            args.ShouldContain("type=volume,source=antiphon-runner-temp_work,target=/work,readonly");
            args.ShouldContain("type=volume,source=antiphon-runner-temp_runner-tmp,target=/runner-tmp,readonly");
            args.Where(arg => arg.StartsWith("type=volume,", StringComparison.Ordinal))
                .ShouldAllBe(arg => arg.EndsWith(",readonly", StringComparison.Ordinal));
            var timeoutLog = Path.Combine(fixture.Root, "timeout-invocations");
            File.Exists(timeoutLog).ShouldBeTrue("recycle-preview-readonly: the helper has a hard timeout");
            var timeout = File.ReadAllText(timeoutLog);
            timeout.ShouldContain("--kill-after=10s");
            timeout.ShouldContain("docker run");
        }
        fixture.Trace.Where(args => args.Length > 0 && (args[0] is "create" or "start" or "stop" or "kill" or "prune"
            || args.Contains("down")
            || (args.Length >= 2 && args[0] == "volume" && args[1] != "ls" && args[1] != "inspect")))
            .ShouldBeEmpty("recycle-preview-readonly: no rollout mutation");
        File.Exists(Path.Combine(fixture.Root, "host-git")).ShouldBeFalse(
            "recycle-preview-readonly: the host does not invoke git");
        File.Exists(Path.Combine(fixture.Root, "server", "recycle", "c100800000000000000000000000000000001.json"))
            .ShouldBeFalse("recycle-preview-receipt: pinned preview writes no journal");
        File.Exists(Path.Combine(fixture.Root, "evidence", "retire-temp-runner", "recycle.json"))
            .ShouldBeFalse("recycle-preview-receipt: pinned preview writes no journal copy");
        fixture.Removed.ShouldBeEmpty("recycle-preview-receipt: preview removes nothing");
        fixture.ReloadDocker();
        foreach (var container in fixture.Docker["containers"]!.AsArray())
        {
            container!["Config"]!["Labels"]!["io.antiphon.audit"].ShouldBeNull(
                "recycle-preview-readonly: --rm leaves no audit container");
            container["Config"]!["Labels"]!["io.antiphon.preview-audit"].ShouldBeNull(
                "recycle-preview-readonly: the owned helper is gone");
        }
    }

    private static async Task AssertApplyMatchesPreview(C1008HostFixture fixture, string refusal)
    {
        var preview = fixture.Trace.Single(args => args.Length > 0 && args[0] == "run");
        var previewNetwork = FlagValue(preview, "--network");
        ResetPreviewTrace(fixture);
        var applied = await fixture.Run("retire-temp-runner", "C1008_CLEANUP_OPERATION=" + AppliedCleanupOperation);
        applied.Exit.ShouldBe(2, "recycle-preview-receipt: apply refusal matches the preview; " + applied.Output);
        applied.Output.ShouldContain(refusal);
        foreach (var other in new[] { "RecycleWorktreeDirty", "RecycleUnpublishedWork", "RecycleGitAuditUnknown" })
            if (other != refusal) applied.Output.ShouldNotContain(other);
        fixture.Removed.ShouldBeEmpty("recycle-preview-receipt: a refusing apply removes nothing");
        var created = fixture.Trace.Single(args => args.Length > 0 && args[0] == "create" && args.Contains("--entrypoint"));
        FlagValue(created, "--network").ShouldBe(previewNetwork, "recycle-preview-readonly: preview network matches apply");
    }

    private static string? FlagValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index < 0 ? null : args[index + 1];
    }

    // Timeout and abort must kill and remove the helper this run created. A name
    // that already belongs to another container is refused and left in place.
    // The hang stub ignores SIGTERM, so a client-only timeout leaves it running.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Preview_helper_removal_is_owned()
    {
        C1008HostFixture.RequireNativeLinux();
        const string bound = "C1008_CLEANUP_OPERATION=" + PreviewCleanupOperation;
        var root = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        C1008HostFixture? fixture = null;
        try
        {
            fixture = new C1008HostFixture(main: false, root);
            await RetainPreviewImage(fixture);

            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "preview-hang"), "1");
            ResetPreviewTrace(fixture);
            // GNU timeout cannot see the docker() function. This shell timeout runs the
            // stub, then signals that process group the way timeout --kill-after does.
            var hang = await fixture.Run("retire-temp-runner", bound + "\n" + PreviewEnforcingTimeout, dryRun: true);
            hang.Exit.ShouldBe(2, "recycle-preview-owned: a timed-out helper refuses; " + hang.Output);
            hang.Output.ShouldContain("RecycleGitAuditUnknown");
            hang.Output.ShouldNotContain("C1008_PREVIEW");
            AssertOwnedRemoval(fixture, "timeout");
            ReapPreviewSleepers(fixture);

            ResetPreviewTrace(fixture);
            var abort = await fixture.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe + "\n" + PreviewAbortProbe, dryRun: true);
            abort.Exit.ShouldNotBe(0, "recycle-preview-owned: an aborted preview does not succeed; " + abort.Output);
            AssertOwnedRemoval(fixture, "abort");
            ReapPreviewSleepers(fixture);

            File.Delete(Path.Combine(fixture.Root, "preview-hang"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "preview-name-taken"), "1");
            ResetPreviewTrace(fixture);
            var collision = await fixture.Run("retire-temp-runner", bound + "\n" + PreviewWitnessProbe, dryRun: true);
            collision.Exit.ShouldBe(2, "recycle-preview-owned: a taken name refuses; " + collision.Output);
            collision.Output.ShouldContain("RecycleGitAuditUnknown");
            collision.Output.ShouldNotContain("C1008_PREVIEW");
            AssertForeignPreviewKept(fixture);
        }
        finally
        {
            if (fixture is not null) ReapPreviewSleepers(fixture);
            fixture?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // Same argument shape as GNU timeout. The sleeper is the docker() stub, so the
    // signal has to reach that process group; an external timeout would not.
    private const string PreviewEnforcingTimeout = """
        C1008_AUDIT_TIMEOUT_SECONDS=2
        timeout() {
            printf '%s\n' "$*" >> "$C1008_FIXTURE_ROOT/timeout-invocations"
            local kill_after=10 duration=0
            while [ $# -gt 0 ]; do
                case "$1" in
                    --kill-after=*) kill_after="${1#*=}"; shift ;;
                    --kill-after) shift; kill_after="$1"; shift ;;
                    --foreground|--preserve-status) shift ;;
                    *) break ;;
                esac
            done
            duration="${1%s}"
            shift
            kill_after="${kill_after%s}"
            kill_tree() {
                local sig="$1" pid="$2" child
                for child in $(ps -o pid= --ppid "$pid" 2>/dev/null); do
                    kill_tree "$sig" "$child"
                done
                kill "$sig" "$pid" 2>/dev/null || true
            }
            "$@" &
            local child=$!
            (
                sleep "$duration"
                kill_tree -TERM "$child"
                sleep "$kill_after"
                kill_tree -KILL "$child"
            ) &
            local watcher=$! status=0
            wait "$child" || status=$?
            kill "$watcher" 2>/dev/null || true
            wait "$watcher" 2>/dev/null || true
            return "$status"
        }
        """;

    private const string PreviewAbortProbe = """
        C1008_AUDIT_TIMEOUT_SECONDS=30
        (
            deadline=$((SECONDS + 20))
            while [ ! -s "$C1008_FIXTURE_ROOT/preview-helper-running" ]; do
                [ "$SECONDS" -ge "$deadline" ] && exit 0
                sleep 0.1
            done
            kill -TERM $$
        ) &
        """;

    private static void AssertOwnedRemoval(C1008HostFixture fixture, string path)
    {
        fixture.ReloadDocker();
        var left = fixture.Docker["containers"]!.AsArray().Where(container =>
        {
            var name = container?["Name"]?.GetValue<string>();
            var label = container?["Config"]?["Labels"]?["io.antiphon.preview-audit"];
            return (name?.StartsWith("c1008-preview-", StringComparison.Ordinal) ?? false) || label is not null;
        }).ToArray();
        left.ShouldBeEmpty("recycle-preview-owned: " + path + " leaves no preview helper");
        fixture.Trace.Any(args => args.Length > 0 && args[0] == "kill")
            .ShouldBeTrue("recycle-preview-owned: " + path + " kills the owned helper");
        fixture.Trace.Any(args => args.Length > 0 && args[0] == "rm" && (args.Contains("-f") || args.Contains("--force")))
            .ShouldBeTrue("recycle-preview-owned: " + path + " force-removes the owned helper");
    }

    private static void AssertForeignPreviewKept(C1008HostFixture fixture)
    {
        fixture.ReloadDocker();
        var foreign = fixture.Docker["containers"]!.AsArray()
            .Where(container => container?["Id"]?.GetValue<string>() == new string('9', 64)).ToArray();
        foreign.Length.ShouldBe(1, "recycle-preview-owned: a pre-existing name is not removed");
        foreign[0]!["Config"]!["Labels"]!["io.antiphon.foreign"]!.GetValue<string>().ShouldBe("1");
        fixture.Trace.Any(args => args.Length > 0 && args[0] is "rm" or "kill")
            .ShouldBeFalse("recycle-preview-owned: refusal does not remove a foreign container");
    }

    private static void ReapPreviewSleepers(C1008HostFixture fixture)
    {
        if (!File.Exists(fixture.StatePath)) return;
        JsonObject docker;
        try { docker = JsonNode.Parse(File.ReadAllText(fixture.StatePath))!.AsObject(); }
        catch (Exception) { return; }
        var containers = docker["containers"]?.AsArray();
        if (containers is null) return;
        foreach (var container in containers)
        {
            var pidNode = container?["State"]?["Pid"];
            if (pidNode is null) continue;
            int pid;
            try { pid = pidNode.GetValue<int>(); }
            catch (Exception) { continue; }
            if (pid <= 1) continue;
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1317_Census_tolerates_a_task_that_settles_without_a_land()
    {
        const string settled = "11111111-1111-1111-1111-111111111111";
        const string unknown = "22222222-2222-2222-2222-222222222222";
        const string unproven = "33333333-3333-3333-3333-333333333333";
        var report = RemoteScriptContractTests.LinuxShell(C1317HostCensusScript());
        report += await C1317WrapperCensus("wrapper-unknown", unknown, "Working", "Working", null, omitLandRequest: true, pass: false);
        report += await C1317WrapperCensus("wrapper-unproven", unproven, "Dispatched", "Succeeded",
            new JsonObject { ["state"] = "Completed" }, omitLandRequest: false, pass: false);
        report += await C1317WrapperCensus("wrapper-settled", settled, "Queued", "Succeeded", null, omitLandRequest: false, pass: true);
        report.IndexOf("FAIL ", StringComparison.Ordinal).ShouldBe(-1, report);
        report.IndexOf("HOST_DONE", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
        foreach (var name in new[]
        {
            "host-unknown", "host-unproven", "host-settled",
            "wrapper-unknown", "wrapper-unproven", "wrapper-settled"
        })
            report.IndexOf("PASS " + name, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
    }

    private static async Task<string> C1317WrapperCensus(string name, string id, string listStatus, string detailStatus,
        JsonNode? landRequest, bool omitLandRequest, bool pass)
    {
        using var f = new C1008WrapperFixture();
        var row = new JsonObject
        {
            ["id"] = id, ["status"] = listStatus, ["runnerId"] = "desktop", ["projectId"] = null,
            ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null
        };
        f.State["tasks"]!["items"]!.AsArray().Add(row);
        var summary = row.DeepClone().AsObject();
        summary["status"] = detailStatus;
        var detail = new JsonObject { ["summary"] = summary };
        if (!omitLandRequest) detail["landRequest"] = landRequest is null ? null : landRequest.DeepClone();
        f.State["details"] = new JsonObject { [id] = detail };
        var run = await f.Run("check-census");
        var proved = false;
        if (pass && run.Exit == 0)
        {
            var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
            var receiptPath = Path.Combine(root, "census-check-census-server2.json");
            var receiptText = File.ReadAllText(receiptPath);
            var task = JsonNode.Parse(receiptText)!["tasks"]!.AsArray().Single()!;
            proved = task["id"]!.GetValue<string>() == id
                && task["status"]!.GetValue<string>() == listStatus
                && task["landRequest"] is null
                && receiptText.Contains("\"landRequest\": null", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2 open=1 boundOpen=0 landPending=0", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp open=1 boundOpen=0 landPending=0", StringComparison.Ordinal)
                && run.Trace.Any(t => t["path"]?.GetValue<string>() == "/api/agent-tasks/" + id);
        }
        var refused = !pass && run.Exit == 2
            && run.Output.Contains("RecycleLandUnknown", StringComparison.Ordinal)
            && !run.Output.Contains("Census check complete:", StringComparison.Ordinal)
            && run.Trace.All(t => t["kind"]?.GetValue<string>() != "case");
        if (proved || refused) return "PASS " + name + "\n";
        return "FAIL " + name + " exit=" + run.Exit + "\n" + run.Output + "\n";
    }

    private static string C1317HostCensusScript()
    {
        var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
        var fixture = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "c1087-host-http.sh"));
        return C1317ShellFunction(fixture, "c1087_fixture_body") + "\n" + """
            c1008_http() {
                printf '%s\n' "$1" >> "$C1008_FIXTURE_ROOT/http-trace"
                C1008_HTTP_BODY="$(c1087_fixture_body "$1")" || {
                    C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                    return 2
                }
            }
            """ + "\n" + C1317ShellFunction(remote, "c1008_tasks_collect") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks") + "\n" + """
            set -u
            root="$(mktemp -d)"
            trap 'rm -rf -- "$root"' EXIT
            CASE_DIR="$root/case"
            mkdir -p "$CASE_DIR"
            CASE=deploy-parent
            C1008_RUNNER=server2
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C1008_FIXTURE_ROOT="$root"
            probe() {
                local name="$1" id="$2" list="$3" detail_status="$4" mode="$5" status=0 land='null'
                if [ "$mode" = unproven ]; then land='{"state":"Completed"}'; fi
                jq -n --arg id "$id" --arg list "$list" --arg detail "$detail_status" --argjson land "$land" \
                    '{scopes:{"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1":{items:[{id:$id,status:$list,runnerId:"desktop",projectId:null,scopeSource:"None",landRequestedAt:null,landStartedAt:null}],scope:{projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",unscoped:"include"},excluded:{total:0,unscoped:0,byProject:[]}}},details:{($id):{summary:{id:$id,status:$detail,runnerId:"desktop",projectId:null,scopeSource:"None",landRequestedAt:null,landStartedAt:null},landRequest:$land}},faults:{}}' \
                    > "$root/tasks.json"
                if [ "$mode" = omit ]; then
                    jq --arg id "$id" 'del(.details[$id].landRequest)' "$root/tasks.json" > "$root/tasks-next.json"
                    mv "$root/tasks-next.json" "$root/tasks.json"
                fi
                : > "$root/http-trace"
                c1008_tasks > "$root/snapshot.json" || status=$?
                if [ "$mode" = pass ]; then
                    if [ "$status" = 0 ] \
                        && jq -e --arg id "$id" '.open.land|has($id) and .[$id]==null' "$root/snapshot.json" >/dev/null \
                        && jq -e --arg id "$id" '.tasks[$id].landRequest==null' "$CASE_DIR/census-deploy-parent-server2.json" >/dev/null \
                        && grep -F -q "/api/agent-tasks/$id" "$root/http-trace"; then
                        printf 'PASS host-%s\n' "$name"
                    else
                        printf 'FAIL host-%s status=%s error=%s\n' "$name" "$status" "$C1008_TASK_ERROR"
                    fi
                elif [ "$status" = 2 ] && [ "$C1008_TASK_ERROR" = RecycleLandUnknown ]; then
                    printf 'PASS host-%s\n' "$name"
                else
                    printf 'FAIL host-%s status=%s error=%s\n' "$name" "$status" "$C1008_TASK_ERROR"
                fi
            }
            probe unknown 22222222-2222-2222-2222-222222222222 Working Working omit
            probe unproven 33333333-3333-3333-3333-333333333333 Dispatched Succeeded unproven
            probe settled 11111111-1111-1111-1111-111111111111 Queued Succeeded pass
            printf 'HOST_DONE\n'
            """;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1()
    {
        var report = RemoteScriptContractTests.LinuxShell(C1319HostCensusScript());
        report += await C1319WrapperProbe("settled-vanish", pass: true);
        report += await C1319WrapperProbe("settled-companion", pass: true);
        report += await C1319WrapperProbe("open-vanish", pass: false);
        report += await C1319WrapperProbe("settled-appear", pass: true);
        report.IndexOf("FAIL ", StringComparison.Ordinal).ShouldBe(-1, report);
        report.IndexOf("HOST_DONE", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
        foreach (var name in new[]
        {
            "host-settled-vanish", "host-settled-companion", "host-open-vanish", "host-settled-appear",
            "wrapper-settled-vanish", "wrapper-settled-companion", "wrapper-open-vanish", "wrapper-settled-appear"
        })
            report.IndexOf("PASS " + name, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
    }

    private static async Task<string> C1319WrapperProbe(string mode, bool pass)
    {
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string settled = "11111111-1111-1111-1111-111111111111";
        const string companion = "22222222-2222-2222-2222-222222222222";
        const string appeared = "33333333-3333-3333-3333-333333333333";
        using var f = new C1008WrapperFixture();
        var settledRow = C1319Row(settled, "Queued");
        var companionRow = C1319Row(companion, "Blocked");
        var appearedRow = C1319Row(appeared, "Working");
        JsonObject first;
        JsonObject second;
        var details = new JsonObject();
        switch (mode)
        {
            case "settled-vanish":
                first = C1319Envelope(f, settledRow);
                second = C1319Envelope(f);
                details[settled] = C1319Detail(settledRow, "Succeeded");
                break;
            case "settled-companion":
                first = C1319Envelope(f, settledRow, companionRow);
                second = C1319Envelope(f, companionRow);
                details[settled] = C1319Detail(settledRow, "Succeeded");
                details[companion] = C1319Detail(companionRow, "Blocked");
                break;
            case "open-vanish":
                first = C1319Envelope(f, settledRow);
                second = C1319Envelope(f);
                details[settled] = C1319Detail(settledRow, "Queued");
                break;
            default:
                first = C1319Envelope(f, settledRow);
                second = C1319Envelope(f, appearedRow);
                details[settled] = C1319Detail(settledRow, "Succeeded");
                details[appeared] = C1319Detail(appearedRow, "Working");
                break;
        }
        f.State["taskQueries"] = new JsonObject { [C1087ListPath(project, "open")] = new JsonArray(first, second) };
        f.State["details"] = details;
        var run = await f.Run("check-census");
        var proved = pass && run.Exit == 0
            && run.Output.Contains("Census check complete:", StringComparison.Ordinal)
            && !run.Output.Contains("Unstable", StringComparison.Ordinal)
            && !run.Output.Contains("RecycleLandUnknown", StringComparison.Ordinal);
        var refused = !pass && run.Exit == 2
            && run.Output.Contains("RecycleTaskCensusUnknown cause=Unstable", StringComparison.Ordinal)
            && !run.Output.Contains("Census check complete:", StringComparison.Ordinal);
        if (proved && mode == "settled-vanish")
            proved = run.Output.Contains("RECYCLE_CENSUS runner=server2 open=0 ", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp open=0 ", StringComparison.Ordinal);
        if (proved && mode == "settled-companion")
        {
            proved = run.Output.Contains("RECYCLE_CENSUS runner=server2 open=1 ", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp open=1 ", StringComparison.Ordinal);
            if (proved)
            {
                var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
                var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-check-census-server2.json")))!["snapshot"]!;
                var tasks = snapshot["open"]!["tasks"]!.AsObject();
                proved = !tasks.ContainsKey(settled) && tasks.ContainsKey(companion)
                    && snapshot["open"]!["land"]!.AsObject().ContainsKey(companion)
                    && !snapshot["open"]!["land"]!.AsObject().ContainsKey(settled);
            }
        }
        if (proved && mode == "settled-appear")
        {
            proved = run.Output.Contains("RECYCLE_CENSUS runner=server2 open=1 ", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp open=1 ", StringComparison.Ordinal);
            if (proved)
            {
                var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
                var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-check-census-server2.json")))!["snapshot"]!;
                var tasks = snapshot["open"]!["tasks"]!.AsObject();
                proved = !tasks.ContainsKey(settled) && tasks.ContainsKey(appeared)
                    && snapshot["open"]!["land"]!.AsObject().ContainsKey(appeared)
                    && snapshot["open"]!["land"]![appeared] is null
                    && !snapshot["open"]!["land"]!.AsObject().ContainsKey(settled);
            }
        }
        if (proved || refused) return "PASS wrapper-" + mode + "\n";
        return "FAIL wrapper-" + mode + " exit=" + run.Exit + "\n" + run.Output + "\n";
    }

    private static JsonObject C1319Row(string id, string status) => new()
    {
        ["id"] = id, ["status"] = status, ["runnerId"] = "desktop", ["projectId"] = null,
        ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null
    };

    private static JsonObject C1319Envelope(C1008WrapperFixture fixture, params JsonObject[] rows)
    {
        var envelope = fixture.State["tasks"]!.DeepClone().AsObject();
        foreach (var row in rows) envelope["items"]!.AsArray().Add(row.DeepClone());
        return envelope;
    }

    private static JsonObject C1319Detail(JsonObject row, string status)
    {
        var summary = row.DeepClone().AsObject();
        summary["status"] = status;
        return new JsonObject { ["summary"] = summary, ["landRequest"] = null };
    }

    private static string C1319HostCensusScript()
    {
        var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
        return """
            c1008_http() {
                printf '%s\n' "$1" >> "$root/http-trace"
                local body id
                case "$1" in
                    *'&status='*)
                        open_reads=$((open_reads + 1))
                        if [ "$open_reads" -eq 1 ]; then body="$open_first"; else body="$open_later"; fi
                        ;;
                    *landPending=true*) body="$empty" ;;
                    /api/agent-tasks/*)
                        id="${1##*/}"
                        body="$(printf '%s' "$details" | jq -ec --arg id "$id" '.[$id]')" || {
                            C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                            return 2
                        }
                        ;;
                    *)
                        C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                        return 2
                        ;;
                esac
                C1008_HTTP_BODY="$body"
            }
            """ + "\n" + C1317ShellFunction(remote, "c1008_tasks_collect") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks") + "\n" + """
            set -u
            root="$(mktemp -d)"
            trap 'rm -rf -- "$root"' EXIT
            CASE_DIR="$root/case"
            mkdir -p "$CASE_DIR"
            CASE=deploy-parent
            C1008_RUNNER=server2
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            empty='{"items":[],"scope":{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1","unscoped":"include"},"excluded":{"total":0,"unscoped":0,"byProject":[]}}'
            open_first="$empty"
            open_later="$empty"
            details='{}'
            open_reads=0
            row_of() { jq -nc --arg id "$1" --arg status "$2" '{id:$id,status:$status,runnerId:"desktop",projectId:null,scopeSource:"None",landRequestedAt:null,landStartedAt:null}'; }
            envelope() {
                local rows='[]' row
                for row in "$@"; do rows="$(printf '%s' "$rows" | jq -c --argjson row "$row" '. + [$row]')"; done
                jq -nc --argjson items "$rows" '{items:$items,scope:{projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",unscoped:"include"},excluded:{total:0,unscoped:0,byProject:[]}}'
            }
            detail_of() { jq -nc --argjson row "$1" --arg status "$2" '{summary:($row + {status:$status}),landRequest:null}'; }
            probe() {
                local name="$1" mode="$2" expect="$3" status=0 extra=ok
                local settled=11111111-1111-1111-1111-111111111111
                local companion=22222222-2222-2222-2222-222222222222
                local appeared=33333333-3333-3333-3333-333333333333
                local a b c
                a="$(row_of "$settled" Queued)"
                b="$(row_of "$companion" Blocked)"
                c="$(row_of "$appeared" Working)"
                case "$mode" in
                    settled-vanish)
                        open_first="$(envelope "$a")"
                        open_later="$(envelope)"
                        details="$(jq -nc --arg id "$settled" --argjson d "$(detail_of "$a" Succeeded)" '{($id):$d}')"
                        ;;
                    settled-companion)
                        open_first="$(envelope "$a" "$b")"
                        open_later="$(envelope "$b")"
                        details="$(jq -nc --arg ida "$settled" --arg idb "$companion" --argjson da "$(detail_of "$a" Succeeded)" --argjson db "$(detail_of "$b" Blocked)" '{($ida):$da,($idb):$db}')"
                        ;;
                    open-vanish)
                        open_first="$(envelope "$a")"
                        open_later="$(envelope)"
                        details="$(jq -nc --arg id "$settled" --argjson d "$(detail_of "$a" Queued)" '{($id):$d}')"
                        ;;
                    settled-appear)
                        open_first="$(envelope "$a")"
                        open_later="$(envelope "$c")"
                        details="$(jq -nc --arg ida "$settled" --arg idc "$appeared" --argjson da "$(detail_of "$a" Succeeded)" --argjson dc "$(detail_of "$c" Working)" '{($ida):$da,($idc):$dc}')"
                        ;;
                esac
                open_reads=0
                : > "$root/http-trace"
                C1008_TASK_ERROR=''
                c1008_tasks > "$root/snapshot.json" || status=$?
                if [ "$expect" = pass ] && [ "$status" = 0 ]; then
                    if [ "$mode" = settled-vanish ]; then
                        jq -e '.open.tasks=={} and .open.land=={}' "$root/snapshot.json" >/dev/null || extra=snapshot
                    elif [ "$mode" = settled-companion ]; then
                        jq -e --arg a "$settled" --arg b "$companion" '(.open.tasks|has($a)|not) and (.open.tasks|has($b)) and (.open.land|has($a)|not) and (.open.land[$b]==null)' "$root/snapshot.json" >/dev/null || extra=snapshot
                    elif [ "$mode" = settled-appear ]; then
                        jq -e --arg a "$settled" --arg c "$appeared" '(.open.tasks|has($a)|not) and (.open.tasks|has($c)) and (.open.land|has($a)|not) and (.open.land|has($c)) and (.open.land[$c]==null) and ((.land.tasks|has($c))|not)' "$root/snapshot.json" >/dev/null || extra=snapshot
                    fi
                fi
                if [ "$expect" = pass ] && [ "$status" = 0 ] && [ "$extra" = ok ] && [ -z "$C1008_TASK_ERROR" ]; then
                    printf 'PASS host-%s\n' "$name"
                elif [ "$expect" = unstable ] && [ "$status" = 2 ] && [ "$C1008_TASK_ERROR" = 'RecycleTaskCensusUnknown cause=Unstable' ]; then
                    printf 'PASS host-%s\n' "$name"
                else
                    printf 'FAIL host-%s status=%s extra=%s error=%s\n' "$name" "$status" "$extra" "$C1008_TASK_ERROR"
                fi
            }
            probe settled-vanish settled-vanish pass
            probe settled-companion settled-companion pass
            probe open-vanish open-vanish unstable
            probe settled-appear settled-appear pass
            printf 'HOST_DONE\n'
            """;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1()
    {
        var report = RemoteScriptContractTests.LinuxShell(C1324HostCensusScript());
        report += await C1324WrapperProbe("appears-no-land", "pass");
        report += await C1324WrapperProbe("appears-with-landRequest", "unstable");
        report += await C1324WrapperProbe("appears-with-unknown-detail", "land-unknown");
        report += await C1324WrapperProbe("changed-row", "unstable");
        report.IndexOf("FAIL ", StringComparison.Ordinal).ShouldBe(-1, report);
        report.IndexOf("HOST_DONE", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
        foreach (var name in new[]
        {
            "host-appears-no-land", "host-appears-with-landRequest", "host-appears-with-unknown-detail", "host-changed-row",
            "wrapper-appears-no-land", "wrapper-appears-with-landRequest", "wrapper-appears-with-unknown-detail", "wrapper-changed-row"
        })
            report.IndexOf("PASS " + name, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
    }

    private static async Task<string> C1324WrapperProbe(string mode, string expect)
    {
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string appeared = "33333333-3333-3333-3333-333333333333";
        const string changed = "11111111-1111-1111-1111-111111111111";
        using var f = new C1008WrapperFixture();
        JsonObject first;
        JsonObject second;
        var details = new JsonObject();
        switch (mode)
        {
            case "appears-with-landRequest":
                var landed = C1319Row(appeared, "Working");
                first = C1319Envelope(f);
                second = C1319Envelope(f, landed);
                var requested = C1319Detail(landed, "Working");
                requested["landRequest"] = new JsonObject
                {
                    ["state"] = "Completed",
                    ["terminalEventId"] = "44444444-4444-4444-4444-444444444444"
                };
                details[appeared] = requested;
                break;
            case "appears-with-unknown-detail":
                var unknown = C1319Row(appeared, "Working");
                first = C1319Envelope(f);
                second = C1319Envelope(f, unknown);
                var omitted = C1319Detail(unknown, "Working");
                omitted.Remove("landRequest");
                details[appeared] = omitted;
                break;
            case "changed-row":
                var before = C1319Row(changed, "Queued");
                var after = C1319Row(changed, "Working");
                after["runnerId"] = "other";
                first = C1319Envelope(f, before);
                second = C1319Envelope(f, after);
                details[changed] = new JsonArray(C1319Detail(before, "Queued"), C1319Detail(after, "Working"));
                break;
            default:
                var fresh = C1319Row(appeared, "Working");
                first = C1319Envelope(f);
                second = C1319Envelope(f, fresh);
                details[appeared] = C1319Detail(fresh, "Working");
                break;
        }
        f.State["taskQueries"] = new JsonObject { [C1087ListPath(project, "open")] = new JsonArray(first, second) };
        f.State["details"] = details;
        var run = await f.Run("check-census");
        var proved = false;
        if (expect == "pass" && run.Exit == 0
            && run.Output.Contains("Census check complete:", StringComparison.Ordinal)
            && !run.Output.Contains("Unstable", StringComparison.Ordinal)
            && !run.Output.Contains("RecycleLandUnknown", StringComparison.Ordinal))
        {
            proved = run.Output.Contains("RECYCLE_CENSUS runner=server2 open=1 ", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp open=1 ", StringComparison.Ordinal);
            if (proved)
            {
                var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
                var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-check-census-server2.json")))!["snapshot"]!;
                var tasks = snapshot["open"]!["tasks"]!.AsObject();
                var land = snapshot["open"]!["land"]!.AsObject();
                proved = tasks.ContainsKey(appeared)
                    && land.ContainsKey(appeared)
                    && land[appeared] is null
                    && !snapshot["land"]!["tasks"]!.AsObject().ContainsKey(appeared);
            }
        }
        var refused = expect == "unstable" && run.Exit == 2
            && run.Output.Contains("RecycleTaskCensusUnknown cause=Unstable", StringComparison.Ordinal)
            && !run.Output.Contains("Census check complete:", StringComparison.Ordinal);
        var unknownRefused = expect == "land-unknown" && run.Exit == 2
            && run.Output.Contains("RecycleLandUnknown", StringComparison.Ordinal)
            && !run.Output.Contains("Census check complete:", StringComparison.Ordinal)
            && !run.Output.Contains("cause=Unstable", StringComparison.Ordinal);
        if (proved || refused || unknownRefused) return "PASS wrapper-" + mode + "\n";
        return "FAIL wrapper-" + mode + " exit=" + run.Exit + "\n" + run.Output + "\n";
    }

    private static string C1324HostCensusScript()
    {
        var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
        return """
            c1008_http() {
                printf '%s\n' "$1" >> "$root/http-trace"
                local body id
                case "$1" in
                    *'&status='*)
                        open_reads=$((open_reads + 1))
                        if [ "$open_reads" -eq 1 ]; then body="$open_first"; else body="$open_later"; fi
                        ;;
                    *landPending=true*) body="$empty" ;;
                    /api/agent-tasks/*)
                        id="${1##*/}"
                        if [ "$detail_mode" = sequence ]; then
                            if [ "$open_reads" -le 1 ]; then body="$detail_first"; else body="$detail_later"; fi
                        else
                            body="$(printf '%s' "$details" | jq -ec --arg id "$id" '.[$id]')" || {
                                C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                                return 2
                            }
                            if [ "$detail_mode" = omit ]; then body="$(printf '%s' "$body" | jq -c 'del(.landRequest)')"; fi
                        fi
                        ;;
                    *)
                        C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                        return 2
                        ;;
                esac
                C1008_HTTP_BODY="$body"
            }
            """ + "\n" + C1317ShellFunction(remote, "c1008_tasks_collect") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks") + "\n" + """
            set -u
            root="$(mktemp -d)"
            trap 'rm -rf -- "$root"' EXIT
            CASE_DIR="$root/case"
            mkdir -p "$CASE_DIR"
            CASE=deploy-parent
            C1008_RUNNER=server2
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            empty='{"items":[],"scope":{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1","unscoped":"include"},"excluded":{"total":0,"unscoped":0,"byProject":[]}}'
            open_first="$empty"
            open_later="$empty"
            details='{}'
            detail_mode=object
            detail_first='{}'
            detail_later='{}'
            open_reads=0
            row_of() { jq -nc --arg id "$1" --arg status "$2" '{id:$id,status:$status,runnerId:"desktop",projectId:null,scopeSource:"None",landRequestedAt:null,landStartedAt:null}'; }
            envelope() {
                local rows='[]' row
                for row in "$@"; do rows="$(printf '%s' "$rows" | jq -c --argjson row "$row" '. + [$row]')"; done
                jq -nc --argjson items "$rows" '{items:$items,scope:{projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",unscoped:"include"},excluded:{total:0,unscoped:0,byProject:[]}}'
            }
            detail_of() { jq -nc --argjson row "$1" --arg status "$2" '{summary:($row + {status:$status}),landRequest:null}'; }
            probe() {
                local name="$1" mode="$2" expect="$3" status=0 extra=ok
                local appeared=33333333-3333-3333-3333-333333333333
                local changed=11111111-1111-1111-1111-111111111111
                local fresh before after
                fresh="$(row_of "$appeared" Working)"
                before="$(row_of "$changed" Queued)"
                after="$(row_of "$changed" Working)"
                detail_mode=object
                case "$mode" in
                    appears-no-land)
                        open_first="$(envelope)"
                        open_later="$(envelope "$fresh")"
                        details="$(jq -nc --arg id "$appeared" --argjson d "$(detail_of "$fresh" Working)" '{($id):$d}')"
                        ;;
                    appears-with-landRequest)
                        open_first="$(envelope)"
                        open_later="$(envelope "$fresh")"
                        details="$(jq -nc --arg id "$appeared" --argjson d "$(detail_of "$fresh" Working)" '{($id):($d + {landRequest:{state:"Completed",terminalEventId:"44444444-4444-4444-4444-444444444444"}})}')"
                        ;;
                    appears-with-unknown-detail)
                        open_first="$(envelope)"
                        open_later="$(envelope "$fresh")"
                        details="$(jq -nc --arg id "$appeared" --argjson d "$(detail_of "$fresh" Working)" '{($id):$d}')"
                        detail_mode=omit
                        ;;
                    changed-row)
                        after="$(printf '%s' "$after" | jq -c '.runnerId="other"')"
                        open_first="$(envelope "$before")"
                        open_later="$(envelope "$after")"
                        detail_mode=sequence
                        detail_first="$(detail_of "$before" Queued)"
                        detail_later="$(detail_of "$after" Working)"
                        ;;
                esac
                open_reads=0
                : > "$root/http-trace"
                C1008_TASK_ERROR=''
                c1008_tasks > "$root/snapshot.json" || status=$?
                if [ "$expect" = pass ] && [ "$status" = 0 ]; then
                    jq -e --arg id "$appeared" '(.open.tasks|has($id)) and (.open.land|has($id)) and (.open.land[$id]==null) and ((.land.tasks|has($id))|not)' "$root/snapshot.json" >/dev/null || extra=snapshot
                fi
                if [ "$expect" = pass ] && [ "$status" = 0 ] && [ "$extra" = ok ] && [ -z "$C1008_TASK_ERROR" ]; then
                    printf 'PASS host-%s\n' "$name"
                elif [ "$expect" = unstable ] && [ "$status" = 2 ] && [ "$C1008_TASK_ERROR" = 'RecycleTaskCensusUnknown cause=Unstable' ]; then
                    printf 'PASS host-%s\n' "$name"
                elif [ "$expect" = land-unknown ] && [ "$status" = 2 ] && [ "$C1008_TASK_ERROR" = RecycleLandUnknown ]; then
                    printf 'PASS host-%s\n' "$name"
                else
                    printf 'FAIL host-%s status=%s extra=%s error=%s\n' "$name" "$status" "$extra" "$C1008_TASK_ERROR"
                fi
            }
            probe appears-no-land appears-no-land pass
            probe appears-with-landRequest appears-with-landRequest unstable
            probe appears-with-unknown-detail appears-with-unknown-detail land-unknown
            probe changed-row changed-row unstable
            printf 'HOST_DONE\n'
            """;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance()
    {
        // CARD-1353 D-2/D-6: the compared land closure keeps bound rows only. An appearing open row
        // with a land timestamp is still Unstable for every runner; a land-closure appearance is
        // Unstable only for a row bound to the recycled runner and passes for an off-runner row.
        var report = RemoteScriptContractTests.LinuxShell(C1326HostCensusScript());
        report += await C1326WrapperProbe("appears-with-land-timestamp", "unstable");
        report += await C1326WrapperProbe("appears-with-land-started", "unstable");
        report += await C1326WrapperProbe("appears-in-land-closure-bound", "unstable");
        report += await C1326WrapperProbe("appears-in-land-closure-other", "pass");
        await C1361CensusProbes.AppearanceBoundary();
        report.IndexOf("FAIL ", StringComparison.Ordinal).ShouldBe(-1, report);
        report.IndexOf("HOST_DONE", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
        foreach (var name in new[]
        {
            "host-appears-with-land-timestamp", "host-appears-in-land-closure-bound", "host-appears-in-land-closure-other",
            "wrapper-appears-with-land-timestamp", "wrapper-appears-in-land-closure-bound", "wrapper-appears-in-land-closure-other"
            , "host-appears-with-land-started", "wrapper-appears-with-land-started"
        })
            report.IndexOf("PASS " + name, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
    }

    private static async Task<string> C1326WrapperProbe(string mode, string expect)
    {
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
        const string appeared = "33333333-3333-3333-3333-333333333333";
        using var f = new C1008WrapperFixture();
        var openRow = C1319Row(appeared, "Working");
        var queries = new JsonObject();
        if (mode is "appears-with-land-timestamp" or "appears-with-land-started")
        {
            openRow[mode == "appears-with-land-started" ? "landStartedAt" : "landRequestedAt"] = "2020-01-01T00:00:00Z";
            queries[C1087ListPath(project, "open")] = new JsonArray(C1319Envelope(f), C1319Envelope(f, openRow));
        }
        else
        {
            var landRow = C1319Row(appeared, "Succeeded");
            landRow["landRequestedAt"] = "2020-01-01T00:00:00Z";
            // check-census reports server2 first: a server2 land is bound there, a desktop land is bound nowhere.
            landRow["runnerId"] = mode == "appears-in-land-closure-bound" ? "server2" : "desktop";
            var land = C1319Envelope(f, landRow);
            queries[C1087ListPath(project, "open")] = new JsonArray(C1319Envelope(f), C1319Envelope(f, openRow));
            queries[C1087ListPath(project, "land")] = new JsonArray(land, land.DeepClone().AsObject());
        }
        f.State["taskQueries"] = queries;
        f.State["details"] = new JsonObject { [appeared] = C1319Detail(openRow, "Working") };
        var run = await f.Run("check-census");
        bool passed;
        if (expect == "unstable")
            passed = run.Exit == 2
                && run.Output.Contains("RecycleTaskCensusUnknown cause=Unstable", StringComparison.Ordinal)
                && run.Output.Contains("RecycleLandInFlight", StringComparison.Ordinal) == false
                && run.Output.Contains("Census check complete:", StringComparison.Ordinal) == false;
        else
            passed = run.Exit == 0
                && run.Output.Contains("Census check complete:", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2 ", StringComparison.Ordinal)
                && run.Output.Contains("RECYCLE_CENSUS runner=server2-temp ", StringComparison.Ordinal)
                && run.Output.Contains("cause=Unstable", StringComparison.Ordinal) == false
                && run.Output.Contains("RecycleLandInFlight", StringComparison.Ordinal) == false;
        if (passed) return "PASS wrapper-" + mode + "\n";
        return "FAIL wrapper-" + mode + " exit=" + run.Exit + "\n" + run.Output + "\n";
    }

    private static string C1326HostCensusScript()
    {
        var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
        return """
            c1008_http() {
                printf '%s\n' "$1" >> "$root/http-trace"
                local body id
                case "$1" in
                    *'&status='*)
                        open_reads=$((open_reads + 1))
                        if [ "$open_reads" -eq 1 ]; then body="$open_first"; else body="$open_later"; fi
                        ;;
                    *landPending=true*)
                        land_reads=$((land_reads + 1))
                        if [ "$land_reads" -eq 1 ]; then body="$land_first"; else body="$land_later"; fi
                        ;;
                    /api/agent-tasks/*)
                        id="${1##*/}"
                        body="$(printf '%s' "$details" | jq -ec --arg id "$id" '.[$id]')" || {
                            C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                            return 2
                        }
                        ;;
                    *)
                        C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                        return 2
                        ;;
                esac
                C1008_HTTP_BODY="$body"
            }
            """ + "\n" + C1317ShellFunction(remote, "c1008_tasks_collect") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks") + "\n" + """
            set -u
            root="$(mktemp -d)"
            trap 'rm -rf -- "$root"' EXIT
            CASE_DIR="$root/case"
            mkdir -p "$CASE_DIR"
            CASE=deploy-parent
            C1008_RUNNER=server2
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            empty='{"items":[],"scope":{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1","unscoped":"include"},"excluded":{"total":0,"unscoped":0,"byProject":[]}}'
            open_first="$empty"
            open_later="$empty"
            land_first="$empty"
            land_later="$empty"
            details='{}'
            open_reads=0
            land_reads=0
            row_of() {
                jq -nc --arg id "$1" --arg status "$2" --arg requested "$3" --arg started "$4" --arg runner "$5" \
                    '{id:$id,status:$status,runnerId:$runner,projectId:null,scopeSource:"None",landRequestedAt:(if $requested=="" then null else $requested end),landStartedAt:(if $started=="" then null else $started end)}'
            }
            envelope() {
                local rows='[]' row
                for row in "$@"; do rows="$(printf '%s' "$rows" | jq -c --argjson row "$row" '. + [$row]')"; done
                jq -nc --argjson items "$rows" '{items:$items,scope:{projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",unscoped:"include"},excluded:{total:0,unscoped:0,byProject:[]}}'
            }
            detail_of() { jq -nc --argjson row "$1" --arg status "$2" '{summary:($row + {status:$status}),landRequest:null}'; }
            # CARD-1353: row_of takes the runner. C1008_RUNNER stays server2, so a server2 row is
            # bound and a desktop row is off-runner.
            probe() {
                local name="$1" mode="$2" expect="$3" runner="$4" status=0 extra=ok
                local appeared=33333333-3333-3333-3333-333333333333
                local fresh landed
                fresh="$(row_of "$appeared" Working "" "" "$runner")"
                landed="$(row_of "$appeared" Succeeded "2020-01-01T00:00:00Z" "" "$runner")"
                case "$mode" in
                    appears-with-land-timestamp|appears-with-land-started)
                        fresh="$(row_of "$appeared" Working "2020-01-01T00:00:00Z" "" "$runner")"
                        if [ "$mode" = appears-with-land-started ]; then fresh="$(row_of "$appeared" Working "" "2020-01-01T00:00:00Z" "$runner")"; fi
                        open_first="$(envelope)"
                        open_later="$(envelope "$fresh")"
                        land_first="$empty"
                        land_later="$empty"
                        ;;
                    appears-in-land-closure)
                        open_first="$(envelope)"
                        open_later="$(envelope "$fresh")"
                        land_first="$(envelope "$landed")"
                        land_later="$(envelope "$landed")"
                        ;;
                esac
                details="$(jq -nc --arg id "$appeared" --argjson d "$(detail_of "$fresh" Working)" '{($id):$d}')"
                open_reads=0
                land_reads=0
                : > "$root/http-trace"
                C1008_TASK_ERROR=''
                c1008_tasks > "$root/snapshot.json" || status=$?
                if [ "$expect" = pass ] && [ "$status" = 0 ]; then
                    jq -e --arg id "$appeared" '(.open.tasks|has($id)) and (.open.land|has($id)) and (.open.land[$id]==null) and ((.land.tasks|has($id))|not)' "$root/snapshot.json" >/dev/null || extra=snapshot
                fi
                if [ "$expect" = pass ] && [ "$status" = 0 ] && [ "$extra" = ok ] && [ -z "$C1008_TASK_ERROR" ]; then
                    printf 'PASS host-%s\n' "$name"
                elif [ "$expect" = unstable ] && [ "$status" = 2 ] && [ "$C1008_TASK_ERROR" = 'RecycleTaskCensusUnknown cause=Unstable' ]; then
                    printf 'PASS host-%s\n' "$name"
                elif [ "$expect" = land-in-flight ] && [ "$status" = 3 ] && [ "$C1008_TASK_ERROR" = "RecycleLandInFlight $appeared" ]; then
                    printf 'PASS host-%s\n' "$name"
                else
                    printf 'FAIL host-%s status=%s extra=%s error=%s\n' "$name" "$status" "$extra" "$C1008_TASK_ERROR"
                fi
            }
            probe appears-with-land-timestamp appears-with-land-timestamp unstable desktop
            probe appears-with-land-started appears-with-land-started unstable desktop
            probe appears-in-land-closure-bound appears-in-land-closure land-in-flight server2
            probe appears-in-land-closure-other appears-in-land-closure pass desktop
            printf 'HOST_DONE\n'
            """;
    }

    // CARD-1353 D-1 to D-5 on the host census: off-runner lands pass and are recorded, bound lands
    // refuse, an off-runner in-flight proof is recorded as {state, terminalEventId}, the match-record
    // compare ignores an off-runner land, and c1008_tasks_wait clears, expires with the waited suffix
    // or refuses an invalid bound before any read. sleep is stubbed; the polls are counted.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands()
    {
        var report = RemoteScriptContractTests.LinuxShell(C1353HostCensusScript());
        report.IndexOf("FAIL ", StringComparison.Ordinal).ShouldBe(-1, report);
        report.IndexOf("HOST_DONE", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
        foreach (var name in new[]
        {
            "off-runner-pending", "bound-pending-refuses", "off-runner-in-flight-detail", "match-record-ignores-off-runner-land",
            "wait-clears", "wait-expires", "wait-invalid-env"
        })
            report.IndexOf("PASS host-" + name, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, report);
    }

    private static string C1353HostCensusScript()
    {
        var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
        return """
            c1008_http() {
                printf '%s\n' "$1" >> "$root/http-trace"
                local body id index
                case "$1" in
                    *'&status='*) body="$open_body" ;;
                    *landPending=true*)
                        # Per-read sequence; the last element keeps being served once consumed.
                        index="$land_reads"
                        land_reads=$((land_reads + 1))
                        if [ "$index" -ge "${#land_seq[@]}" ]; then index=$((${#land_seq[@]} - 1)); fi
                        body="${land_seq[$index]}"
                        ;;
                    /api/agent-tasks/*)
                        id="${1##*/}"
                        body="$(printf '%s' "$details" | jq -ec --arg id "$id" '.[$id]')" || {
                            C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                            return 2
                        }
                        ;;
                    *)
                        C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"
                        return 2
                        ;;
                esac
                C1008_HTTP_BODY="$body"
            }
            """ + "\n" + C1317ShellFunction(remote, "c1008_tasks_collect") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks_wait") + "\n"
            + C1317ShellFunction(remote, "c1008_tasks_match_record") + "\n" + """
            set -u
            sleep() { sleep_calls=$((sleep_calls + 1)); }
            sleep_calls=0
            root="$(mktemp -d)"
            trap 'rm -rf -- "$root"' EXIT
            CASE_DIR="$root/case"
            mkdir -p "$CASE_DIR"
            CASE=deploy-parent
            C1008_RUNNER=server2
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            empty='{"items":[],"scope":{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1","unscoped":"include"},"excluded":{"total":0,"unscoped":0,"byProject":[]}}'
            receipt="$CASE_DIR/census-deploy-parent-server2.json"
            id=22222222-2222-2222-2222-222222222222
            row_of() {
                jq -nc --arg id "$1" --arg status "$2" --arg runner "$3" --arg requested "$4" \
                    '{id:$id,status:$status,runnerId:$runner,projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",scopeSource:"Task",landRequestedAt:(if $requested=="" then null else $requested end),landStartedAt:null}'
            }
            envelope() {
                local rows='[]' row
                for row in "$@"; do rows="$(printf '%s' "$rows" | jq -c --argjson row "$row" '. + [$row]')"; done
                jq -nc --argjson items "$rows" '{items:$items,scope:{projectId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",unscoped:"include"},excluded:{total:0,unscoped:0,byProject:[]}}'
            }
            reset() {
                open_body="$empty"; land_seq=("$empty"); details='{}'; land_reads=0; sleep_calls=0
                : > "$root/http-trace"; rm -f -- "$receipt"
                C1008_TASK_ERROR=''; C1008_LAND_WAIT_SECONDS=''; C1008_LAND_POLL_SECONDS=''
            }
            verdict() {
                local name="$1" ok="$2" status="$3"
                if [ "$ok" = ok ]; then printf 'PASS host-%s\n' "$name"
                else printf 'FAIL host-%s status=%s detail=%s error=%s\n' "$name" "$status" "$ok" "$C1008_TASK_ERROR"; fi
            }
            off_runner="$(row_of "$id" Succeeded server2-temp 2020-01-01T00:00:00Z)"
            bound="$(row_of "$id" Succeeded server2 2020-01-01T00:00:00Z)"

            # off-runner-pending: a Succeeded server2-temp land passes the server2 census, is kept out of
            # the compared land snapshot and is still recorded in the receipt.
            reset; land_seq=("$(envelope "$off_runner")"); status=0; ok=ok
            c1008_tasks > "$root/snapshot.json" || status=$?
            [ "$status" = 0 ] || ok=status
            [ "$ok" != ok ] || jq -e '.land.tasks == {}' "$root/snapshot.json" >/dev/null || ok=snapshot-land
            [ "$ok" != ok ] || jq -e --arg id "$id" '.tasks|has($id)' "$receipt" >/dev/null || ok=receipt-tasks
            [ "$ok" != ok ] || jq -e '.landWaitPolls == 0' "$receipt" >/dev/null || ok=receipt-polls
            verdict off-runner-pending "$ok" "$status"
            live="$(cat "$root/snapshot.json")"

            # bound-pending-refuses: the same land bound to server2 refuses RecycleLandInFlight.
            reset; land_seq=("$(envelope "$bound")"); status=0; ok=ok
            c1008_tasks > /dev/null || status=$?
            [ "$status" = 3 ] && [ "$C1008_TASK_ERROR" = "RecycleLandInFlight $id" ] || ok=refusal
            verdict bound-pending-refuses "$ok" "$status"

            # off-runner-in-flight-detail: a Blocked other-runner row whose detail land is Running is
            # recorded as {state, terminalEventId:null}, not refused.
            reset; status=0; ok=ok
            blocked="$(row_of "$id" Blocked other "")"
            open_body="$(envelope "$blocked")"
            details="$(jq -nc --arg id "$id" --argjson row "$blocked" '{($id):{summary:$row,landRequest:{state:"Running",terminalEventId:null}}}')"
            c1008_tasks > "$root/snapshot.json" || status=$?
            [ "$status" = 0 ] || ok=status
            [ "$ok" != ok ] || jq -e --arg id "$id" '.open.land[$id] == {state:"Running",terminalEventId:null}' "$root/snapshot.json" >/dev/null || ok=proof
            verdict off-runner-in-flight-detail "$ok" "$status"

            # match-record-ignores-off-runner-land: a containersRemoved record with no lands matches a
            # live census that saw an off-runner land; a bound land in the live land snapshot does not.
            status=0; ok=ok
            C1008_RECORD='{"phase":"containersRemoved","tasks":{"open":{"scopes":{},"tasks":{},"land":{}},"land":{"tasks":{}}}}'
            c1008_tasks_match_record "$live" || ok=off-runner-mismatch
            live_bound="$(printf '%s' "$live" | jq -c --arg id "$id" --argjson row "$bound" '.land.tasks[$id]=$row')"
            if c1008_tasks_match_record "$live_bound"; then ok=bound-matched; fi
            verdict match-record-ignores-off-runner-land "$ok" "$status"

            # wait-clears: pending, pending, empty, empty. Two refusing censuses consume one land read
            # each, the third census reads twice and passes; the receipt records the polls.
            reset; status=0; ok=ok
            C1008_LAND_WAIT_SECONDS=3; C1008_LAND_POLL_SECONDS=1
            land_seq=("$(envelope "$bound")" "$(envelope "$bound")" "$empty" "$empty")
            c1008_tasks_wait > "$root/wait.out" || status=$?
            [ "$status" = 0 ] || ok=status
            [ "$ok" != ok ] || [ "$(grep -c '^C1008_LAND_WAIT runner=server2 reason=RecycleLandInFlight '"$id"' polls=' "$root/wait.out")" = 2 ] || ok=wait-lines
            [ "$ok" != ok ] || jq -e '.landWaitPolls == 2' "$receipt" >/dev/null || ok=receipt-polls
            [ "$ok" != ok ] || [ "$sleep_calls" = 2 ] || ok=sleeps
            [ "$ok" != ok ] || [ "$land_reads" = 4 ] || ok=land-reads
            verdict wait-clears "$ok" "$status"

            # wait-expires: always pending; ceil(3/1)=3 polls, then the refusal keeps its first token.
            reset; status=0; ok=ok
            C1008_LAND_WAIT_SECONDS=3; C1008_LAND_POLL_SECONDS=1
            land_seq=("$(envelope "$bound")")
            c1008_tasks_wait > "$root/wait.out" || status=$?
            [ "$status" = 3 ] || ok=status
            [ "$ok" != ok ] || [ "$C1008_TASK_ERROR" = "RecycleLandInFlight $id runner=server2 waited=3polls" ] || ok=error
            [ "$ok" != ok ] || [ "$(grep -c '^C1008_LAND_WAIT ' "$root/wait.out")" = 3 ] || ok=wait-lines
            [ "$ok" != ok ] || [ "$sleep_calls" = 3 ] || ok=sleeps
            verdict wait-expires "$ok" "$status"

            # wait-invalid-env: a zero bound refuses RecycleContextInvalid before any read.
            reset; status=0; ok=ok
            C1008_LAND_WAIT_SECONDS=0
            land_seq=("$(envelope "$bound")")
            c1008_tasks_wait > "$root/wait.out" || status=$?
            [ "$status" = 2 ] || ok=status
            [ "$ok" != ok ] || [ "$C1008_TASK_ERROR" = RecycleContextInvalid ] || ok=error
            [ "$ok" != ok ] || [ ! -s "$root/http-trace" ] || ok=reads
            verdict wait-invalid-env "$ok" "$status"
            printf 'HOST_DONE\n'
            """;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail()
        => await C1361CensusProbes.Status(host: false, passes: false);

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1361_Wrapper_census_tolerates_open_status_changes_between_passes()
        => await C1361CensusProbes.Status(host: false, passes: true);

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts()
        => await C1361CensusProbes.Counts();

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1371_Post_removal_count_drift_does_not_abort()
        => await C1361CensusProbes.PostRemoval();

    private static string C1317ShellFunction(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith(name + "() {", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "function " + name + " is missing");
        var end = Array.FindIndex(lines, start + 1, line => line == "}");
        end.ShouldBeGreaterThan(start, "function " + name + " never closes");
        return string.Join('\n', lines[start..(end + 1)]);
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

    // Private-origin publication is proved only by a host ref snapshot bound to this
    // operation. Authentication failure is the only ls-remote result that may read it.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Host_proof_admits_only_an_authenticated_private_origin()
    {
        C1008HostFixture.RequireNativeLinux();
        const string bound = "C1008_CLEANUP_OPERATION=" + PreviewCleanupOperation;
        const string operation = "c100800000000000000000000000000000001";
        const long started = 1700000000;
        const string repoRel = "repos/school-revision";
        var root = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var fixture = new C1008HostFixture(main: false, root);
            await RetainPreviewImage(fixture);
            var repo = Path.Combine(WorkMount(fixture), repoRel);
            var origin = Path.Combine(root, "auth-remote.sh");
            await File.WriteAllTextAsync(origin, AuthRemote("fatal: could not read Username for 'https://github.com': terminal prompts disabled", 128));
            var setup = await C994ScriptProcess.Run("bash", "-c",
                "chmod 0755 " + BashSingle(origin) + " && git init -q -b main " + BashSingle(repo) +
                " && git -C " + BashSingle(repo) + " config user.name Fixture" +
                " && git -C " + BashSingle(repo) + " config user.email fixture@example.invalid" +
                " && printf published > " + BashSingle(repo) + "/sentinel" +
                " && git -C " + BashSingle(repo) + " add sentinel" +
                " && git -C " + BashSingle(repo) + " commit -qm published" +
                " && git -C " + BashSingle(repo) + " config protocol.ext.allow always" +
                " && git -C " + BashSingle(repo) + " remote add origin " + BashSingle("ext::" + origin));
            setup.Exit.ShouldBe(0, "private-origin: git setup; " + setup.Output);
            ShareVolumeWithApply(fixture);
            var url = (await C994ScriptProcess.Run("git", "-C", repo, "remote", "get-url", "origin")).Output.Trim();
            var tip = (await C994ScriptProcess.Run("git", "-C", repo, "rev-parse", "HEAD")).Output.Trim();
            var proofDir = Path.Combine(root, "host-proof");
            var proofEnv = "export C1008_HOST_PROOF=" + BashSingle(proofDir) + "\nexport C1008_OPERATION_STARTED=" + started + "\n";
            WriteBoundProof(proofDir, repoRel, url, operation, started, tip + "\trefs/heads/main\n");

            ResetPreviewTrace(fixture);
            var admitted = await fixture.Run("retire-temp-runner", bound + "\n" + proofEnv + PreviewWitnessProbe, dryRun: true);
            admitted.Exit.ShouldBe(0, "private-origin: a bound proof admits the authenticated tip; " + admitted.Output);
            admitted.Output.ShouldContain("C1008_PREVIEW");
            admitted.Output.ShouldNotContain("RecycleGitAuditUnknown");
            admitted.Output.ShouldNotContain("RecycleUnpublishedWork");
            admitted.Output.ShouldNotContain("RecycleWorktreeDirty");
            File.Exists(Path.Combine(root, "host-git")).ShouldBeFalse("private-origin: the remote host does not invoke git");
            var preview = fixture.Trace.Single(args => args.Length > 0 && args[0] == "run");
            preview.ShouldContain("type=bind,source=" + proofDir + ",target=/c1008-host-proof,readonly");
            preview.ShouldContain("C1008_HOST_PROOF=/c1008-host-proof");
            preview.ShouldContain("C1008_OPERATION=" + operation);
            preview.ShouldContain("C1008_OPERATION_STARTED=" + started);
            preview.ShouldContain("--cap-drop");
            preview.ShouldContain("1654:1654");
            fixture.Removed.ShouldBeEmpty("private-origin: preview removes nothing");

            await AssertPrivateRefusal(fixture, bound, "RecycleGitAuditUnknown",
                "audit check=ls-remote status=128 repo=" + repoRel, "private-origin: no proof refuses like today");

            var child = await C994ScriptProcess.Run("bash", "-c",
                "git -C " + BashSingle(repo) + " commit -q --allow-empty -m unpublished && git -C " + BashSingle(repo) + " rev-parse HEAD");
            child.Exit.ShouldBe(0, "private-origin: unpublished child; " + child.Output);
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleUnpublishedWork",
                "repo=" + repoRel, "private-origin: a proof that omits the tip refuses");
            var reset = await C994ScriptProcess.Run("git", "-C", repo, "reset", "--hard", tip);
            reset.Exit.ShouldBe(0, "private-origin: restore the published tip; " + reset.Output);

            WriteBoundProof(proofDir, repoRel, url, "c100800000000000000000000000000000002", started, tip + "\trefs/heads/main\n");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "reason=proof-operation", "private-origin: another operation id refuses");
            WriteBoundProof(proofDir, repoRel, url, operation, started - 1, tip + "\trefs/heads/main\n");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "reason=proof-stale", "private-origin: a proof older than the operation refuses");
            WriteProofText(proofDir, repoRel, url,
                "c1008-host-proof 1\noperation " + operation + "\norigin " + new string('0', 64) +
                "\nrepo " + repoRel + "\ncreated " + started + "\n" + tip + "\trefs/heads/main\n");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "reason=proof-origin", "private-origin: a different origin hash refuses");
            WriteProofText(proofDir, repoRel, url,
                "c1008-host-proof 1\noperation " + operation + "\norigin " + Sha256Hex(url) +
                "\nrepo repos/other\ncreated " + started + "\n" + tip + "\trefs/heads/main\n");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "reason=proof-repo", "private-origin: a different repository refuses");
            WriteProofText(proofDir, repoRel, url, "");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "reason=proof-missing", "private-origin: an empty proof refuses");

            WriteBoundProof(proofDir, repoRel, url, operation, started, tip + "\trefs/heads/main\n");
            await File.WriteAllTextAsync(origin, AuthRemote("fatal: repository 'https://github.com/example/missing.git' not found", 128));
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "check=ls-remote", "private-origin: a 404 is not authentication and the proof cannot admit it");
            await File.WriteAllTextAsync(origin, AuthRemote("fatal: Could not resolve host: github.com", 128));
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "check=ls-remote", "private-origin: a DNS failure is not authentication and the proof cannot admit it");
            await File.WriteAllTextAsync(origin, AuthRemote("fatal: Connection timed out", 124));
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleGitAuditUnknown",
                "check=ls-remote", "private-origin: a timeout is not authentication and the proof cannot admit it");
            await File.WriteAllTextAsync(origin, AuthRemote("fatal: could not read Username for 'https://github.com': terminal prompts disabled", 128));

            var dirtyFile = Path.Combine(repo, "dirty");
            await File.WriteAllTextAsync(dirtyFile, "unpublished-sentinel");
            await AssertPrivateRefusal(fixture, bound + "\n" + proofEnv, "RecycleWorktreeDirty",
                "repo=" + repoRel, "private-origin: a dirty worktree refuses even with a valid proof");
            File.Delete(dirtyFile);

            var bare = Path.Combine(root, "public-origin");
            // reset --hard records the abandoned child in ORIG_HEAD. The audit
            // reads that pseudoref, so drop it with the reflog files first.
            var publish = await C994ScriptProcess.Run("bash", "-c",
                "if [ -d " + BashSingle(repo) + "/.git/logs ]; then find " + BashSingle(repo) + "/.git/logs -type f -delete; fi" +
                " && rm -f -- " + BashSingle(Path.Combine(repo, ".git", "ORIG_HEAD")) +
                " && git init -q --bare " + BashSingle(bare) +
                " && git -C " + BashSingle(repo) + " remote set-url origin " + BashSingle(bare) +
                " && git -C " + BashSingle(repo) + " push -q origin main");
            publish.Exit.ShouldBe(0, "private-origin: public stand-in; " + publish.Output);
            WriteBoundProof(proofDir, repoRel, url, "c100800000000000000000000000000000002", started, tip + "\trefs/heads/main\n");
            ResetPreviewTrace(fixture);
            var ignored = await fixture.Run("retire-temp-runner", bound + "\n" + proofEnv + PreviewWitnessProbe, dryRun: true);
            ignored.Exit.ShouldBe(0, "private-origin: a successful ls-remote ignores a mismatched proof; " + ignored.Output);
            ignored.Output.ShouldNotContain("RecycleGitAuditUnknown");
            var restore = await C994ScriptProcess.Run("git", "-C", repo, "remote", "set-url", "origin", "ext::" + origin);
            restore.Exit.ShouldBe(0, "private-origin: restore the private origin; " + restore.Output);
            WriteBoundProof(proofDir, repoRel, url, operation, started, tip + "\trefs/heads/main\n");

            ResetPreviewTrace(fixture);
            var inventory = await fixture.Run("retire-temp-inventory", "C1008_CLEANUP_OPERATION=" + AppliedCleanupOperation + "\n" + PreviewWitnessProbe);
            inventory.Exit.ShouldBe(0, "private-origin: inventory lists the authentication failure; " + inventory.Output);
            var originHash = Sha256Hex(url);
            var proofName = Sha256Hex(repoRel + "\n" + url);
            inventory.Output.ShouldContain(
                "C1008_PRIVATE_ORIGIN repo=" + repoRel + " origin=" + url + " originHash=" + originHash + " proofName=" + proofName,
                Case.Sensitive);
            inventory.Output.ShouldNotContain("terminal prompts disabled");
            inventory.Output.ShouldNotContain("could not read Username");
            fixture.Removed.ShouldBeEmpty("private-origin: inventory removes nothing");

            // Refusal vectors replace the operation's Ready artifact with Refused,
            // and the public-origin control changes the inventory. Apply needs a
            // fresh real preview after restoring the private origin and proof.
            await VolumeRetirementFixture.ReadyPreview(fixture,
                "C1008_CLEANUP_OPERATION=" + AppliedCleanupOperation + "\n" + proofEnv);
            ResetPreviewTrace(fixture);
            var applied = await fixture.Run("retire-temp-runner",
                "C1008_CLEANUP_OPERATION=" + AppliedCleanupOperation + "\n" + proofEnv);
            applied.Exit.ShouldBe(0, "private-origin: apply reuses the same proof check; " + applied.Output);
            applied.Output.ShouldNotContain("RecycleGitAuditUnknown");
            applied.Output.ShouldNotContain("RecycleUnpublishedWork");
            applied.Output.ShouldNotContain("RecycleWorktreeDirty");
            var audits = fixture.Trace.Where(args => args.Length > 0 && args[0] == "run" && args.Contains("--entrypoint")).ToArray();
            audits.Length.ShouldBeGreaterThan(0, "private-origin: apply forwards the proof bind");
            foreach (var args in audits)
            {
                args.ShouldContain("--rm");
                args.ShouldContain("--init");
                args.ShouldContain("--cap-drop");
                args.ShouldContain("1654:1654");
                args.ShouldContain("C1609_CONTENT_V2=1");
                args.ShouldContain("type=bind,source=" + proofDir + ",target=/c1008-host-proof,readonly");
                args.ShouldContain("C1008_HOST_PROOF=/c1008-host-proof");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Host_proof_file_omits_credentials()
    {
        C1008HostFixture.RequireNativeLinux();
        const string token = "ghp_FAKE_TOKEN_SHOULD_NOT_LEAK";
        var root = Path.Combine("/var/tmp", "c1008-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        var proof = Path.Combine(root, "proof");
        var log = Path.Combine(root, "proof.log");
        Directory.CreateDirectory(proof);
        try
        {
            var origin = Path.Combine(root, "origin.git");
            var seed = Path.Combine(root, "seed");
            var prepared = await C994ScriptProcess.Run("bash", "-c", $$"""
                set -euo pipefail
                git init -q --bare {{BashSingle(origin)}}
                git init -q -b main {{BashSingle(seed)}}
                git -C {{BashSingle(seed)}} -c user.name=Fixture -c user.email=fixture@example.invalid commit -qm seed --allow-empty
                git -C {{BashSingle(seed)}} push -q {{BashSingle(origin)}} main
                git -C {{BashSingle(seed)}} rev-parse HEAD
                """);
            prepared.Exit.ShouldBe(0, prepared.Output);
            var tip = prepared.Output.Trim();
            var resolvedGit = await C994ScriptProcess.Run("bash", "-c", "command -v git");
            resolvedGit.Exit.ShouldBe(0, resolvedGit.Output);
            var git = Path.Combine(root, "bin", "git");
            await File.WriteAllTextAsync(git,
                "#!/bin/bash\nset -euo pipefail\nprintf '%s\\n' \"$*\" >> \"$C1008_FAKE_GIT_ARGS\"\n" +
                "args=(); for arg in \"$@\"; do\n" +
                "  if [ \"$arg\" = https://github.com/example/private.git ]; then arg=" + BashSingle(origin) + "; fi\n" +
                "  args+=(\"$arg\"); done\n" +
                "if [ \"$1\" = ls-remote ]; then\n" +
                "  printf '%s\\n' \"password=" + token + "\" >&2\n" +
                "  printf '%s\\n' \"note " + token + "\"\nfi\n" +
                "exec " + BashSingle(resolvedGit.Output.Trim()) + " \"${args[@]}\"\n");
            var mode = await C994ScriptProcess.Run("chmod", "0755", git);
            mode.Exit.ShouldBe(0, "private-origin: fake git; " + mode.Output);
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
            var psi = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = DelegateScriptRunner.RepoRoot
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(
                "$ErrorActionPreference='Stop'; . " + BashSingle(script) + "; " +
                "Write-C1008HostOriginProof -OriginUrl $env:C1008_TEST_ORIGIN -RepoRelative 'repos/school-revision' " +
                "-OperationId 'c100800000000000000000000000000000001' -ProofDirectory $env:C1008_TEST_PROOF " +
                "-CreatedUnix 1700000000 -LogPath $env:C1008_TEST_LOG");
            psi.Environment["PATH"] = Path.Combine(root, "bin") + ":" + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["C1008_FAKE_GIT_ARGS"] = Path.Combine(root, "git-args.txt");
            psi.Environment["C1008_TEST_ORIGIN"] = "https://" + token + "@github.com/example/private.git";
            psi.Environment["C1008_TEST_PROOF"] = proof;
            psi.Environment["C1008_TEST_LOG"] = log;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await proc.WaitForExitAsync(deadline.Token);
            var output = stdout + stderr;
            proc.ExitCode.ShouldBe(0, "private-origin: host proof writer; " + output);
            var proofText = string.Join('\n', Directory.GetFiles(proof).Select(File.ReadAllText));
            proofText.ShouldContain(tip + "\trefs/heads/main");
            proofText.ShouldContain("operation c100800000000000000000000000000000001");
            var bundle = Directory.GetFiles(proof, "*.bundle").ShouldHaveSingleItem();
            var heads = await C994ScriptProcess.Run(resolvedGit.Output.Trim(), "bundle", "list-heads", bundle);
            heads.Exit.ShouldBe(0, heads.Output);
            heads.Output.ShouldContain(tip + " refs/heads/main");
            foreach (var artifact in new[] { proofText, output, File.Exists(log) ? File.ReadAllText(log) : "",
                         File.Exists(Path.Combine(root, "git-args.txt")) ? File.ReadAllText(Path.Combine(root, "git-args.txt")) : "" })
                artifact.ShouldNotContain(token);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string AuthRemote(string message, int code) =>
        "#!/bin/bash\nprintf '%s\\n' " + BashSingle(message) + " >&2\nexit " + code + "\n";

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string ProofFileName(string repo, string origin) => Sha256Hex(repo + "\n" + origin);

    private static void WriteProofText(string directory, string repo, string origin, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ProofFileName(repo, origin)), text);
    }

    private static void WriteBoundProof(string directory, string repo, string origin, string operation, long created, string refs)
    {
        var body = "c1008-host-proof 1\noperation " + operation + "\norigin " + Sha256Hex(origin) +
                   "\nrepo " + repo + "\ncreated " + created + "\n" + refs;
        WriteProofText(directory, repo, origin, body);
    }

    private static async Task AssertPrivateRefusal(C1008HostFixture fixture, string extra, string diagnosis, string detail, string because)
    {
        ResetPreviewTrace(fixture);
        var run = await fixture.Run("retire-temp-runner", extra + "\n" + PreviewWitnessProbe, dryRun: true);
        run.Exit.ShouldBe(2, because + "; " + run.Output);
        run.Output.ShouldContain(diagnosis, Case.Sensitive, because);
        run.Output.ShouldContain(detail, Case.Sensitive, because);
        run.Output.ShouldNotContain("C1008_PREVIEW");
    }
}

internal sealed class C1008WrapperFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("c1008-wrapper-").FullName;
    internal string StatePath => Path.Combine(Root, "state.json");
    internal string TracePath => Path.Combine(Root, "trace.jsonl");
    internal string ReadTraceText()
    {
        if (!File.Exists(TracePath)) return "";
        using var stream = new FileStream(TracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    internal JsonObject State { get; }
    // Extra child environment (CARD-1353: C727_TEST_LAND_WAIT_MS). Merged into Run's process after the defaults.
    internal Dictionary<string, string> Environment { get; } = new();

    internal C1008WrapperFixture()
    {
        using var setupPhase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "seed");
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
        // CARD-1353: a land bound to the recycled runner is waited for (default 30 min). The fixture
        // bounds that wait so a bound-land vector still refuses inside the 30 s run deadline.
        psi.Environment["C727_TEST_LAND_WAIT_MS"] = "100";
        foreach (var (key, value) in Environment) psi.Environment[key] = value;
        using var processPhase = TestTiming.BeginProcess("pwsh", psi.ArgumentList.Count, "cli");
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

    public void Dispose()
    {
        using var teardownPhase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "delete-tree");
        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class C1008HostFixture : IDisposable
{
    internal const string PreviousSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string PreviousSha12 = "bbbbbbbbbbbb";
    // generation.imageId is the session-runner image. state-init runs the server image.
    internal static readonly string SessionImage = "sha256:" + new string('a', 64);
    internal static readonly string ServerImage = "sha256:" + new string('c', 64);

    internal static void RequireNativeLinux()
    {
        NativeLinuxGate.Require("CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.");
    }

    internal string Root { get; }
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
    internal JsonObject HttpFaults { get; } = new();
    internal bool ExerciseHttpClassifier { get; set; }

    internal C1008HostFixture(bool main = true, string? root = null)
    {
        using var setupPhase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "seed");
        Root = root ?? Directory.CreateTempSubdirectory("c1008-host-").FullName;
        Directory.CreateDirectory(Root);
        Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1008-recycle-cases.json")))!.AsObject();
        TaskScopes["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"] = Vectors["emptyTasks"]!.DeepClone();
        Statuses = new JsonObject
        {
            ["server2"] = Vectors[main ? "mainDrained" : "mainAccepting"]!.DeepClone(),
            ["server2-temp"] = Vectors[main ? "tempAccepting" : "tempRetiredAbsent"]!.DeepClone()
        };
        var volumes = new JsonObject();
        var seed = SharedDataSources.GetOrCreate(SharedType.PerTestSession, typeof(C1008HostFixture),
            "c1008-compose", () => new C1008ComposeSeed());
        var models = seed.CloneInto(Root);
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
        }
        foreach (var name in new[] { "antiphon-runner_work-extra", "schoolrevision-staging", "openclaw-state" })
            volumes[name] = Volume(name, new JsonObject());
        Docker = new JsonObject { ["volumes"] = volumes, ["models"] = models,
            ["containers"] = new JsonArray(), ["removed"] = new JsonArray(), ["fault"] = "" };
        var containers = Docker["containers"]!.AsArray();
        if (main)
        {
            containers.Add(Container('1', "antiphon-runner", "session-runner", true, previousGeneration: true, "work", "runner-tmp", "dind-data", "runner-state",
                "cache-nuget-packages", "cache-nuget-scratch", "cache-npm-content"));
            containers.Add(Container('2', "antiphon-runner", "state-init", false, previousGeneration: true, "work", "runner-state"));
            Statuses["server2"]!["buildVersion"] = PreviousSha;
        }
        else Statuses["server2-temp"]!["buildVersion"] = PreviousSha;
        containers.Add(Container('3', "antiphon-runner", "build-slots", true));

        Directory.CreateDirectory(Path.Combine(Root, "work"));
        Directory.CreateDirectory(Path.Combine(Root, "server/cache"));
        File.WriteAllText(Path.Combine(Root, "main.env"), "SOURCE_REVISION=" + PreviousSha + "\n");
        File.WriteAllText(Path.Combine(Root, "temp.env"), "RUNNER_GROK_STORE_DIR=" + Root + "/grok\nSOURCE_REVISION=" + PreviousSha + "\n");
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
        => Container(id, project, service, running, false, roles);

    internal JsonObject Container(char id, string project, string service, bool running, bool previousGeneration, params string[] roles)
    {
        var mounts = new JsonArray();
        var hostConfig = new JsonObject { ["Tmpfs"] = new JsonObject(), ["Mounts"] = new JsonArray() };
        if (service is "state-init" or "session-runner")
        {
            var model = Docker["models"]![previousGeneration ? project + "@" + PreviousSha12 : project]!;
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
        var image = service == "state-init" ? ServerImage : SessionImage;
        return new JsonObject { ["Id"] = new string(id, 64), ["Image"] = image,
            ["State"] = new JsonObject { ["Running"] = running, ["Status"] = running ? "running" : "exited" },
            ["Config"] = new JsonObject { ["Labels"] = new JsonObject { ["com.docker.compose.project"] = project,
                ["com.docker.compose.service"] = service } }, ["Mounts"] = mounts, ["HostConfig"] = hostConfig };
    }

    internal static JsonObject LoadComposeV218(string root)
    {
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,
            "scripts/fixtures/c1105-compose-v218-config.json")).Replace("/inert/c1105", root, StringComparison.Ordinal);
        return JsonNode.Parse(text)!.AsObject();
    }

    internal async Task<(int Exit, string Output)> Run(string hostCase = "deploy-parent", string extra = "", bool dryRun = false,
        Func<string, string>? sourceTransform = null)
    {
        using var runPhase = TestTiming.BeginPhase(TestPhaseKind.OurCode, "run");
        // GNU timeout execs by name rather than calling the shell function below.
        // Keep its real deadline and put the owned fake CLI on the child PATH.
        // This also prevents an unprobed apply from reaching the worker daemon.
        var dockerBin = Path.Combine(Root, "docker-cli");
        Directory.CreateDirectory(dockerBin);
        var dockerCli = Path.Combine(dockerBin, "docker");
        await File.WriteAllTextAsync(dockerCli, "#!/bin/sh\nexec bash " +
            VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-fake-docker.sh")) + " \"$@\"\n");
        var executable = await C994ScriptProcess.Run("chmod", "0755", dockerCli);
        if (executable.Exit != 0) throw new InvalidOperationException("fake Docker executable: " + executable.Output);
        File.WriteAllText(StatePath, Docker.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "statuses.json"), Statuses.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "tasks.json"), new JsonObject
            { ["scopes"] = TaskScopes.DeepClone(), ["details"] = TaskDetails.DeepClone(), ["faults"] = HttpFaults.DeepClone() }.ToJsonString());
        var runnerCompose = Path.Combine(DelegateScriptRunner.RepoRoot, "docker-compose.server2-runner.yml");
        var tempCompose = Path.Combine(DelegateScriptRunner.RepoRoot, "docker-compose.server2-runner.temp.yml");
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        // A caller-local defect witness may transform only this fixture's disposable copy.
        if (sourceTransform is not null) source = sourceTransform(source);
        var injection = $$"""
            C1008_FIXTURE_ROOT='{{Root}}'; export C1008_FIXTURE_ROOT
            C1609_CONTENT_AUDIT_FILE='{{DelegateScriptRunner.RepoRoot}}/scripts/c1008-content-audit.sh'; export C1609_CONTENT_AUDIT_FILE
            C1609_VOLUME_METADATA_FILE='{{DelegateScriptRunner.RepoRoot}}/scripts/c1008-volume-metadata.sh'; export C1609_VOLUME_METADATA_FILE
            C1609_RETIREMENT_FILE='{{DelegateScriptRunner.RepoRoot}}/scripts/c1008-retirement.sh'; export C1609_RETIREMENT_FILE
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
            SERVER2_COMPOSE='{{Root}}/target-compose.yml'
            SERVER2_TEMP_COMPOSE='{{Root}}/target-compose.temp.yml'
            cp -f -- '{{runnerCompose}}' "$SERVER2_COMPOSE"
            cp -f -- '{{tempCompose}}' "$SERVER2_TEMP_COMPOSE"
            C1008_COMPOSE_SOURCE_BODY="$(declare -f c1008_compose_source 2>/dev/null || true)"
            c1008_compose_source() { mkdir -p -- "$2" || return 2; cp -f -- "$SERVER2_COMPOSE" "$2/docker-compose.server2-runner.yml" || return 2; cp -f -- "$SERVER2_TEMP_COMPOSE" "$2/docker-compose.server2-runner.temp.yml" || return 2; }
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
            C1087_REAL_HTTP={{(ExerciseHttpClassifier ? "1" : "0")}}
            source '{{DelegateScriptRunner.RepoRoot}}/scripts/fixtures/c1087-host-http.sh'
            C1008_LAND_WAIT_SECONDS=1; C1008_LAND_POLL_SECONDS=1
            sudo() { [ "$1" = -n ] && shift; if [ "$1" = install ]; then mkdir -p "${@: -1}"; elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 99999999 1 25000000 1%% /fixture\n'; else "$@"; fi; }
            {{extra}}
            """;
        source = source.Replace("trap 'ec=$?;", injection + "\ntrap 'ec=$?;", StringComparison.Ordinal);
        var script = Path.Combine(Root, "remote.sh");
        File.WriteAllText(script, source);
        var psi = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["PATH"] = dockerBin + ":" + Environment.GetEnvironmentVariable("PATH");
        psi.ArgumentList.Add(script);
        psi.Environment["C590_CASE"] = hostCase;
        psi.Environment["C590_SHA"] = new string('a', 40);
        psi.Environment["C590_RUN"] = "c1008fixture";
        psi.Environment["C590_REEXEC"] = "1";
        psi.Environment["C604_SERVER_ORIGIN"] = "http://127.0.0.1:1";
        using var processPhase = TestTiming.BeginProcess("bash", psi.ArgumentList.Count, "cli");
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
        // CARD-1130. A deploy-parent Run measures 22-30s and exceeds 30s at load 19-23
        // (method wall 47s idle, 53s at that load). 120s still cancels a hang inside the row timeout.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch { if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); } throw; }
        return (proc.ExitCode, await stdout + await stderr);
    }

    public void Dispose()
    {
        using var teardownPhase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "delete-tree");
        Directory.Delete(Root, recursive: true);
    }
}
