using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using System.Text.Json.Nodes;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0090 S1: <c>scripts/complexity-chain.ps1</c> driven as the REAL script under pwsh
/// against a stub API. A string match on the source would pass just as happily if the flag
/// never reached the request body.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ComplexityChainScriptTests
{
    [Test]
    public async Task C470_mutation_cell_round_trip()
    {
        using var server = new StubApi();
        var set = await RunAsync(server, "set", "-Role", "Mutation", "-Complexity", "Hard", "-Candidates", "Codex/Frontier,ClaudeCode/High", "-Provenance", "Human");
        set.ExitCode.ShouldBe(0, set.Output);
        server.LastPath.ShouldBe("/api/complexity-chains/Mutation/Hard");
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("provenance").GetString().ShouldBe("Human");
        body.GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Codex");
        var get = await RunAsync(server, "get", "-Role", "Mutation");
        get.ExitCode.ShouldBe(0, get.Output);
        server.LastPath.ShouldBe("/api/complexity-chains?role=Mutation");
        var clear = await RunAsync(server, "clear", "-Role", "Mutation", "-Complexity", "Hard");
        clear.ExitCode.ShouldBe(0, clear.Output);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/complexity-chains/Mutation/Hard");
    }

    [Test]
    public async Task Set_puts_the_tier_provenance_and_candidates()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "set", "-Complexity", "Hard",
            "-Candidates", "ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier",
            "-Provenance", "Human", "-Reason", "plan-grade work");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/complexity-chains/any/Hard");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("plan-grade work");
        var candidates = body.RootElement.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Count.ShouldBe(3);
        candidates[0].GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        candidates[0].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        candidates[2].GetProperty("agentKind").GetString().ShouldBe("Grok");
    }

    [Test]
    public async Task A_naive_NotAfter_is_refused_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "set", "-Complexity", "Hard",
            "-Candidates", "Grok/Frontier",
            "-NotAfter", "2026-09-05T00:00:00");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Naive");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Get_hits_the_list_endpoint()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "get");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/complexity-chains");
        run.Output.ShouldContain("Hard");
        run.Output.ShouldContain("fable");
    }

    [Test]
    public async Task Clear_deletes_by_complexity()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "clear", "-Complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/complexity-chains/any/Hard");
        run.Output.ShouldContain("cleared Hard");
    }

    [Test]
    public async Task Set_Role_Plan_puts_the_three_segment_cell_path()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "set", "-Role", "Plan", "-Complexity", "Hard",
            "-Candidates", "ClaudeCode/Frontier,Codex/Frontier");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/complexity-chains/Plan/Hard");
    }

    [Test]
    public async Task Get_Role_Plan_hits_the_role_query()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "get", "-Role", "Plan");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/complexity-chains?role=Plan");
    }

    [Test]
    public async Task Clear_Role_Plan_deletes_the_cell()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "clear", "-Role", "Plan", "-Complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/complexity-chains/Plan/Hard");
        run.Output.ShouldContain("cleared Plan/Hard");
    }

    [Test]
    public async Task Set_without_candidates_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "set", "-Complexity", "Hard");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Candidates");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Set_All_puts_Hard_Medium_Easy_in_order()
    {
        using var server = new ScriptedApi();
        var run = await RunAsync(server, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier", "-Reason", "r");
        run.ExitCode.ShouldBe(0, run.Output);
        RequestLines(server).ShouldBe(new[] {
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy" });
        foreach (var request in server.Requests)
        {
            var body = request.Body!.Value;
            body.GetProperty("provenance").GetString().ShouldBe("Human");
            body.GetProperty("reason").GetString().ShouldBe("r");
            body.GetProperty("candidates").GetArrayLength().ShouldBe(1);
            body.GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Codex");
            body.GetProperty("candidates")[0].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        }
        run.Output.ShouldContain("Hard written");
        run.Output.ShouldContain("Medium written");
        run.Output.ShouldContain("Easy written");

        // The file path is an independent input, including UTF-8 and a literal newline.
        var reasonFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(reasonFile, "review café\nsecond line");
            using var fileServer = new ScriptedApi();
            var fromFile = await RunAsync(fileServer, "set", "-Role", "Review", "-Complexity", "All",
                "-Candidates", "Codex/Frontier", "-ReasonFile", reasonFile);
            fromFile.ExitCode.ShouldBe(0, fromFile.Output);
            fileServer.RequestCount.ShouldBe(3);
            foreach (var request in fileServer.Requests)
                request.Body!.Value.GetProperty("reason").GetString().ShouldBe("review café\nsecond line");
            using var invalid = new ScriptedApi();
            var conflict = await RunAsync(invalid, "set", "-Complexity", "All", "-Candidates", "Codex/Frontier",
                "-Reason", "r", "-ReasonFile", reasonFile);
            conflict.ExitCode.ShouldBe(1);
            conflict.Output.ShouldContain("mutually exclusive");
            invalid.RequestCount.ShouldBe(0);
        }
        finally { File.Delete(reasonFile); }
    }

    [Test]
    public async Task Set_All_stops_at_the_first_failing_tier_and_names_written_tiers()
    {
        using var server = new ScriptedApi();
        server.Respond("PUT", "/api/complexity-chains/Review/Medium", 422,
            """{"errors":{"Candidates":["boom"]}}""");
        var run = await RunAsync(server, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier");
        run.ExitCode.ShouldBe(1, run.Output);
        RequestLines(server).ShouldBe(new[] {
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium" });
        server.Cells.Keys.ShouldBe(new[] { "Review/Hard" });
        run.Output.ShouldContain("Hard written; Medium refused:");
        run.Output.ShouldContain("boom");
        run.Output.ShouldContain("Easy not written");
    }

    [Test]
    public async Task ClearStagePin_requires_All_and_a_named_role()
    {
        string[][] invalidArguments = [
            ["set", "-Role", "Review", "-Complexity", "Hard", "-Candidates", "Codex/Frontier", "-ClearStagePin"],
            ["set", "-Role", "Any", "-Complexity", "All", "-Candidates", "Codex/Frontier", "-ClearStagePin"],
            ["set", "-Complexity", "All", "-Candidates", "Codex/Frontier", "-ClearStagePin"],
            ["clear", "-Role", "Review", "-Complexity", "All", "-ClearStagePin"],
            ["get", "-Role", "Review", "-Complexity", "All", "-ClearStagePin"] ];
        foreach (var args in invalidArguments)
        {
            using var server = new ScriptedApi();
            var run = await RunAsync(server, args);
            run.ExitCode.ShouldBe(1, run.Output);
            run.Output.ShouldContain("-ClearStagePin requires set -Complexity All and a named -Role");
            server.RequestCount.ShouldBe(0);
        }
    }

    [Test]
    public async Task ClearStagePin_deletes_the_stage_pin_only_after_three_2xx()
    {
        using var server = new ScriptedApi();
        server.Pins.Add(JsonNode.Parse("""{"id":"card-1","role":"Review","cardId":"card-a"}""")!.AsObject());
        server.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        server.Pins.Add(JsonNode.Parse("""{"id":"other-stage","role":"Code","cardId":null}""")!.AsObject());
        var run = await RunAsync(server, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier", "-ClearStagePin");
        run.ExitCode.ShouldBe(0, run.Output);
        RequestLines(server).ShouldBe(new[] {
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy",
            "GET /api/routing-pins?role=Review",
            "DELETE /api/routing-pins/stage-1" });
        server.Pins.Select(pin => pin["id"]!.GetValue<string>()).ShouldBe(new[] { "card-1", "other-stage" });
        run.Output.ShouldContain("cleared Review stage pin");

        using var absent = new ScriptedApi();
        var noPin = await RunAsync(absent, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier", "-ClearStagePin");
        noPin.ExitCode.ShouldBe(0, noPin.Output);
        absent.RequestCount.ShouldBe(4);
        absent.Requests.Last().Method.ShouldBe("GET");
        noPin.Output.ShouldContain("No active routing pin for that grain.");

        using var refused = new ScriptedApi();
        refused.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        refused.Respond("DELETE", "/api/routing-pins/stage-1", 409,
            """{"detail":"pin clear refused","code":"test_pin_refused"}""");
        var failedClear = await RunAsync(refused, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier", "-ClearStagePin");
        failedClear.ExitCode.ShouldBe(1);
        failedClear.Output.ShouldContain("pin clear refused");
        failedClear.Output.ShouldContain("test_pin_refused");
        refused.Cells.Count.ShouldBe(3);
        refused.Pins.Count.ShouldBe(1);
    }

    [Test]
    public async Task ClearStagePin_is_skipped_when_a_tier_failed()
    {
        using var server = new ScriptedApi();
        server.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        server.Respond("PUT", "/api/complexity-chains/Review/Easy", 422,
            """{"detail":"easy refused","code":"test_easy_refused"}""");
        var run = await RunAsync(server, "set", "-Role", "Review", "-Complexity", "All",
            "-Candidates", "Codex/Frontier", "-ClearStagePin");
        run.ExitCode.ShouldBe(1, run.Output);
        RequestLines(server).ShouldBe(new[] {
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy" });
        server.Pins.Single()["id"]!.GetValue<string>().ShouldBe("stage-1");
        run.Output.ShouldContain("stage pin kept");
        run.Output.ShouldContain("easy refused");
        run.Output.ShouldContain("test_easy_refused");
    }

    [Test]
    public async Task Get_Role_prints_the_effective_line()
    {
        using var server = new ScriptedApi();
        server.Respond("GET", "/api/complexity-chains?role=Review", 200,
            """
            {"chains":[{"role":"Review","complexity":"Medium","resolvedFrom":"role",
              "candidates":[{"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true}],
              "effective":{"source":"pin","candidates":[{"agentKind":"Codex","modelLevel":"Frontier","alias":"gpt-6-astra","availableNow":true}]}}]}
            """);
        var run = await RunAsync(server, "get", "-Role", "Review");
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Review/Medium");
        run.Output.ShouldContain("ClaudeCode/High (opus)");
        run.Output.ShouldContain("effective: pin Codex/Frontier (gpt-6-astra)");
        run.Output.ShouldNotContain("effective: pin ClaudeCode");

        using var noEffective = new ScriptedApi();
        noEffective.Respond("GET", "/api/complexity-chains?role=Review", 200,
            """{"chains":[{"role":"Review","complexity":"Medium","resolvedFrom":"none","candidates":[],"effective":null}]}""");
        var legacy = await RunAsync(noEffective, "get", "-Role", "Review");
        legacy.ExitCode.ShouldBe(0, legacy.Output);
        legacy.Output.ShouldContain("Review/Medium");
        legacy.Output.ShouldNotContain("effective:");
    }

    [Test]
    public async Task Clear_All_deletes_three_cells()
    {
        using var server = new ScriptedApi();
        var run = await RunAsync(server, "clear", "-Role", "Review", "-Complexity", "All");
        run.ExitCode.ShouldBe(0, run.Output);
        RequestLines(server).ShouldBe(new[] {
            "DELETE /api/complexity-chains/Review/Hard",
            "DELETE /api/complexity-chains/Review/Medium",
            "DELETE /api/complexity-chains/Review/Easy" });
        run.Output.ShouldContain("cleared Review/Hard");
        run.Output.ShouldContain("cleared Review/Medium");
        run.Output.ShouldContain("cleared Review/Easy");
    }

    private static string[] RequestLines(ScriptedApi server) =>
        server.Requests.Select(request => $"{request.Method} {request.Path}").ToArray();

    private static Task<(int ExitCode, string Output)> RunAsync(ScriptedApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "complexity-chain.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class StubApi : ScriptedApi
    {
        public StubApi()
        {
            Respond("GET", "/api/complexity-chains", 200, ListJson);
            Respond("PUT", "/api/complexity-chains/", 200, ChainJson);
        }

        private const string ChainJson =
            """
            {"complexity":"Hard","candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null}],"provenance":"Human","source":"pin","reason":"test","notAfter":null,"updatedAt":"2026-09-02T00:00:00Z"}
            """;

        private const string ListJson =
            """
            {"chains":[{"complexity":"Hard","candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null}],"provenance":"Human","source":"pin","reason":"test","notAfter":null,"updatedAt":"2026-09-02T00:00:00Z"},{"complexity":"Medium","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null},{"complexity":"Easy","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null}]}
            """;

    }
}
