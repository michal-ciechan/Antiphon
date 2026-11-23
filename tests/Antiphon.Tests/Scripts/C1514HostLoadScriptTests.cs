using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Application;
using Microsoft.AspNetCore.Http;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Scripts;
/// <summary>
/// CARD-1514 S1: the host-load command (Antiphon.Nightly.HostLoad, which replaced scripts/host-load.ps1 and its process adapter
/// scripts/test-host-load.ps1 in unit 21 of the 2026-11-22 plan) in process, on its production transport, against the owned
/// loopback server. The rows are the script's.
/// </summary>
[Category("Integration"), C1514ProgressBudget(60_000), NotInParallel("C1514HostLoadServer")]
[TUnit.Core.Executors.TestExecutor<C1514HostLoadProgressExecutor>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadScriptTests(C1514HostLoadFixture fixture)
{
    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();
    private const string OwnedToken = "c1514-owned-token-never-print";
    [Test]
    public async Task JsonIsOneValueWithArithmetic()
    {
        await using var world = await fixture.WorldAsync(); Recorder(world);
        var scoped = await RunAsync(world, json: true, project: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", role: "Code");
        scoped.ExitCode.ShouldBe(0, scoped.Stderr); scoped.Stderr.ShouldBeEmpty();
        scoped.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1, "--json prints exactly one value");
        using var json = JsonDocument.Parse(scoped.Stdout); var root = json.RootElement;
        root.GetProperty("current").GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
        root.GetProperty("lastHour").GetProperty("cpuPercent").GetProperty("p95").GetDouble().ShouldBe(20);
        var rec = root.GetProperty("recommendation"); rec.GetProperty("recommendedAdditionalTasks").GetInt32().ShouldBe(1);
        rec.GetProperty("pressurePercent").GetDouble().ShouldBe(44); rec.GetProperty("startupDebtPercent").GetDouble().ShouldBe(10); rec.GetProperty("cpuHeadroom").GetInt32().ShouldBe(2);
        var hostOnly = await RunAsync(world, json: true); hostOnly.ExitCode.ShouldBe(0, hostOnly.Stderr);
        using var host = JsonDocument.Parse(hostOnly.Stdout); host.RootElement.GetProperty("recommendation").GetProperty("recommendedAdditionalTasks").GetInt32().ShouldBe(2);
        Recorder(world, incomplete: true); var partial = await RunAsync(world, json: true); partial.ExitCode.ShouldBe(0, partial.Stderr);
        using var unknown = JsonDocument.Parse(partial.Stdout); unknown.RootElement.GetProperty("current").ValueKind.ShouldBe(JsonValueKind.Null);
        unknown.RootElement.GetProperty("recommendation").GetProperty("recommendedAdditionalTasks").GetInt32().ShouldBe(0);
    }
    [Test]
    public async Task DefaultsAndScopeResolveFromLiveReads()
    {
        await using var world = await fixture.WorldAsync(); Recorder(world); world.DefaultRevision = 17; world.DefaultRunner = "grok-linux";
        var first = await RunAsync(world, json: true, project: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", role: "Code"); first.ExitCode.ShouldBe(0, first.Stderr);
        using (var json = JsonDocument.Parse(first.Stdout)) { json.RootElement.GetProperty("placement").GetProperty("revision").GetInt64().ShouldBe(17); json.RootElement.GetProperty("placement").GetProperty("runnerId").GetString().ShouldBe("grok-linux"); }
        world.DefaultRevision = 18; world.DefaultRunner = "desktop";
        var next = await RunAsync(world, json: true); next.ExitCode.ShouldBe(0, next.Stderr);
        using (var json = JsonDocument.Parse(next.Stdout)) { json.RootElement.GetProperty("placement").GetProperty("runnerId").GetString().ShouldBe("desktop"); json.RootElement.GetProperty("placement").GetProperty("revision").GetInt64().ShouldBe(18); }
        var before = world.Hits.Count; var explicitHost = await RunAsync(world, json: true, hostId: "grok-linux"); explicitHost.ExitCode.ShouldBe(0, explicitHost.Stderr);
        world.Hits.Skip(before).ShouldNotContain(h => h.Path == "/api/runner-defaults");
        world.Hits.ShouldContain(h => h.Path == "/api/hosts/grok-linux/load?projectId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa&role=Code");
        world.Hits.ShouldContain(h => h.Path == "/api/hosts/desktop/load"); world.Hits.ShouldAllBe(h => h.Method == "GET");
    }
    [Test]
    public async Task FailuresNeverPrintPositiveCapacity()
    {
        await using var world = await fixture.WorldAsync();
        foreach (var failure in new[] { "http", "not-found", "malformed", "truncated", "defaults", "mandatory", "summary", "source" })
        {
            Recorder(world);
            var normal = world.Override!;
            world.Override = async c =>
            {
                if ((failure == "defaults" && c.Request.Path == "/api/runner-defaults") || (failure != "defaults" && c.Request.Path.Value!.EndsWith("/load", StringComparison.Ordinal)))
                {
                    if (failure is "http" or "defaults") { c.Response.StatusCode = 503; return; }
                    if (failure == "not-found") { c.Response.StatusCode = 404; return; }
                    if (failure is "malformed" or "truncated") { await c.Response.WriteAsync(failure == "malformed" ? "not-json" : "{\"recommendation\":"); return; }
                    var payload = JsonSerializer.SerializeToNode(Response("grok-linux", false, false), PhoneHomeFraming.Json)!.AsObject();
                    if (failure == "summary") payload["lastHour"]!.AsObject().Remove("cpuPercent");
                    else if (failure == "source") payload["current"]!.AsObject().Remove("source");
                    else payload.Remove("recommendation");
                    await c.Response.WriteAsJsonAsync(payload); return;
                }
                await normal(c);
            };
            var result = await RunAsync(world, json: true); result.ExitCode.ShouldNotBe(0, failure); result.Stdout.ShouldBeEmpty(failure); result.Stderr.ShouldNotContain("MORE tasks=1");
        }
    }
    [Test]
    public async Task TokenStaysInHeaders()
    {
        await using var world = await fixture.WorldAsync(); Recorder(world);
        foreach (var json in new[] { true, false })
        {
            var result = await RunAsync(world, json: json, token: OwnedToken); result.ExitCode.ShouldBe(0, result.Stderr);
            result.Stdout.ShouldNotContain(OwnedToken); result.Stderr.ShouldNotContain(OwnedToken);
            if (!json)
            {
                var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                lines.Length.ShouldBe(4, result.Stdout);
                lines[0].ShouldBe("Host grok-linux; age=0s; mode=Lease; shadow=True");
                lines[2].ShouldStartWith("MORE tasks=2; scope=hostOnly; pressure=44; startup debt=10; CPU headroom=2;");
            }
        }
        world.Hits.ShouldAllBe(h => h.Token == OwnedToken);
        world.Override = async c => { c.Response.StatusCode = 503; await c.Response.WriteAsync("owned error echo " + c.Request.Headers["X-Antiphon-Task-Token"].ToString()); };
        var failed = await RunAsync(world, json: true, token: OwnedToken); failed.ExitCode.ShouldNotBe(0); failed.Stdout.ShouldBeEmpty(); failed.Stderr.ShouldNotContain(OwnedToken);
    }
    /// <summary>
    /// The host-load command in process (its production transport: 3 s per read, 1 MiB cap) with ANTIPHON_API as the world's
    /// server and the token as ANTIPHON_TASK_TOKEN would pass it; stdout and stderr apart. The read is synchronous, so it runs on
    /// the pool while the owned server answers.
    /// </summary>
    internal static async Task<ScriptHarnessResult> RunAsync(C1514HostLoadWorld world, bool json, string? hostId = null,
        string? project = null, string? role = null, string? token = null)
    {
        var args = new List<string>();
        if (json) args.Add("--json");
        if (hostId is not null) args.AddRange(["--host-id", hostId]); if (project is not null) args.AddRange(["--project", project]); if (role is not null) args.AddRange(["--role", role]);
        var options = HostLoadOptions.Parse(args, world.Api.ToString(), token);
        var result = await world.Own(Task.Run(() =>
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = HostLoad.Run(options, NightlyHost.Production(stdout, stderr), HostLoad.Transport());
            return new ScriptHarnessResult(exit, stdout.ToString(), stderr.ToString());
        }, world.Token));
        world.Progress("host-load completed"); return result;
    }
    private static void Recorder(C1514HostLoadWorld world, bool incomplete = false)
    {
        world.Hits.Clear(); // Record the script's reads, after owned peer registration has finished.
        world.Override = async context =>
        {
        if (context.Request.Path == "/api/runner-defaults") { await context.Response.WriteAsJsonAsync(new { revision = world.DefaultRevision, globalRunnerId = world.DefaultRunner }); return; }
        if (context.Request.Path == "/api/session-runners") { await context.Response.WriteAsJsonAsync(new[] { new { runnerId = "grok-linux" }, new { runnerId = "desktop" } }); return; }
        var host = context.Request.Path.Value!.Contains("/desktop/", StringComparison.Ordinal) ? "desktop" : "grok-linux";
        var scoped = context.Request.Query.ContainsKey("projectId") && context.Request.Query.ContainsKey("role");
        await context.Response.WriteAsJsonAsync(Response(host, scoped, incomplete));
        };
    }
    private static HostLoadCurrentDto Response(string host, bool scoped, bool incomplete)
    {
        var sample = new HostLoadSample { Source = new(host, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 101),
            SampledAt = C1514HostLoadWorld.T0, PhysicalHostKey = "owned-host", CpuPercent = 44 };
        return new(1, host, "owned-host", C1514HostLoadWorld.T0, incomplete ? null : sample, incomplete ? null : 0,
            new Dictionary<string, HostLoadMetricSummary> { ["cpuPercent"] = new(12.5, 0, 100, 20, 500, 40, 720, 40d / 720, false, false, 0) }, 40d / 720,
            new("Lease", 7), new("owned", "owned-host", 7, C1514HostLoadWorld.T0, "known", "Lease", 4, 6, 1, null, 12),
            new(44, 44, "eligible", "eligible", [], []), new(incomplete ? 0 : scoped ? 1 : 2, scoped ? "projectRole" : "hostOnly", scoped ? [] : ["project", "role"],
                incomplete ? null : 44, 10, incomplete ? 0 : 2, 4, 3, scoped ? 2 : null, scoped ? 1 : null, C1514HostLoadWorld.T0, incomplete ? ["sample_unknown"] : ["startup_debt"]));
    }
}
