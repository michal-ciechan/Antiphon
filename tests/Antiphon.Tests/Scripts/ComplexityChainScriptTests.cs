using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0090 S1, CARD-0332, CARD-0097 S-D, CARD-1275: the complexity-chain command (Antiphon.Nightly.ComplexityChainCli, which
/// replaced scripts/complexity-chain.ps1 in unit 26 of the 2026-11-22 plan) in process against the recording API stand-in
/// (<see cref="ScriptedApi"/>), with the reason file on an in-memory file system. The request bodies are read back, so a flag
/// that never reached the body fails here. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class ComplexityChainScriptTests
{
    [Test]
    public void C470_mutation_cell_round_trip()
    {
        var server = new StubApi();
        var set = Run(server, "set", "--role", "Mutation", "--complexity", "Hard", "--candidates", "Codex/Frontier,ClaudeCode/High", "--provenance", "Human");
        set.Exit.ShouldBe(0, set.All);
        server.LastPath.ShouldBe("/api/complexity-chains/Mutation/Hard");
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("provenance").GetString().ShouldBe("Human");
        body.GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Codex");
        var get = Run(server, "get", "--role", "Mutation");
        get.Exit.ShouldBe(0, get.All);
        server.LastPath.ShouldBe("/api/complexity-chains?role=Mutation");
        var clear = Run(server, "clear", "--role", "Mutation", "--complexity", "Hard");
        clear.Exit.ShouldBe(0, clear.All);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe($"/api/complexity-chains/Mutation/Hard?concurrencyToken={EmptyToken}");
    }

    [Test]
    public void Set_puts_the_tier_provenance_and_candidates()
    {
        var server = new StubApi();
        var run = Run(server, "set", "--complexity", "Hard", "--candidates", "ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier",
            "--provenance", "Human", "--reason", "plan-grade work");

        run.Exit.ShouldBe(0, run.All);
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
        body.RootElement.GetProperty("concurrencyToken").GetString().ShouldBe(StubHardToken);
        body.RootElement.TryGetProperty("notAfter", out _).ShouldBeFalse("an omitted --not-after sends nothing");
        // One row line: label, source/provenance, the candidates, then the reason.
        run.Output.Trim().ShouldBe("Hard  pin/Human  ClaudeCode/Frontier (fable) available  test");
    }

    [Test]
    public void A_naive_NotAfter_is_refused_before_any_request()
    {
        var server = new StubApi();
        var run = Run(server, "set", "--complexity", "Hard", "--candidates", "Grok/Frontier", "--not-after", "2026-09-05T00:00:00");

        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("Naive");
        run.Error.ShouldStartWith("--not-after must be ISO-8601 UTC", Case.Sensitive);
        server.RequestCount.ShouldBe(0);

        // A Z or a numeric offset is sent as given.
        var dated = Run(server, "set", "--complexity", "Hard", "--candidates", "Grok/Frontier", "--not-after", "2026-09-05T00:00:00+01:00");
        dated.Exit.ShouldBe(0, dated.All);
        server.LastBody!.RootElement.GetProperty("notAfter").GetString().ShouldBe("2026-09-05T00:00:00+01:00");
    }

    [Test]
    public void Get_hits_the_list_endpoint()
    {
        var server = new StubApi();
        var run = Run(server, "get");

        run.Exit.ShouldBe(0, run.All);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/complexity-chains");
        run.Output.ShouldContain("Hard");
        run.Output.ShouldContain("fable");
        Lines(run.Output).ShouldBe([
            "  Hard  pin/Human  ClaudeCode/Frontier (fable) available  test",
            "  Medium  config/none  (empty)",
            "  Easy  config/none  (empty)"]);
    }

    [Test]
    public void Clear_deletes_by_complexity()
    {
        var server = new StubApi();
        var run = Run(server, "clear", "--complexity", "Hard");

        run.Exit.ShouldBe(0, run.All);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe($"/api/complexity-chains/any/Hard?concurrencyToken={StubHardToken}");
        run.Output.ShouldContain("cleared Hard");
        run.Output.Trim().ShouldBe("cleared Hard (config default applies again)");
    }

    [Test]
    public void Set_Role_Plan_puts_the_three_segment_cell_path()
    {
        var server = new StubApi();
        var run = Run(server, "set", "--role", "Plan", "--complexity", "Hard", "--candidates", "ClaudeCode/Frontier,Codex/Frontier");

        run.Exit.ShouldBe(0, run.All);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/complexity-chains/Plan/Hard");

        // A role is matched ignoring case and sent in its listed spelling; Any is the any-role row.
        var lower = Run(server, "set", "--role", "testdesign", "--complexity", "easy", "--candidates", "Grok/Frontier");
        lower.Exit.ShouldBe(0, lower.All);
        server.LastPath.ShouldBe("/api/complexity-chains/TestDesign/Easy");
        var any = Run(server, "set", "--role", "Any", "--complexity", "Medium", "--candidates", "Grok/Frontier");
        any.Exit.ShouldBe(0, any.All);
        server.LastPath.ShouldBe("/api/complexity-chains/any/Medium");
    }

    [Test]
    public void Get_Role_Plan_hits_the_role_query()
    {
        var server = new StubApi();
        var run = Run(server, "get", "--role", "Plan");

        run.Exit.ShouldBe(0, run.All);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/complexity-chains?role=Plan");

        // get --role Any reads the unfiltered list (the any-role rows are not an effective overlay).
        var any = Run(server, "get", "--role", "Any");
        any.Exit.ShouldBe(0, any.All);
        server.LastPath.ShouldBe("/api/complexity-chains");
    }

    [Test]
    public void Clear_Role_Plan_deletes_the_cell()
    {
        var server = new StubApi();
        var run = Run(server, "clear", "--role", "Plan", "--complexity", "Hard");

        run.Exit.ShouldBe(0, run.All);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe($"/api/complexity-chains/Plan/Hard?concurrencyToken={EmptyToken}");
        run.Output.ShouldContain("cleared Plan/Hard");
    }

    [Test]
    public void Set_without_candidates_is_refused_locally()
    {
        var server = new StubApi();
        var run = Run(server, "set", "--complexity", "Hard");

        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("--candidates");
        server.RequestCount.ShouldBe(0);

        foreach (var (raw, refusal) in new[]
        {
            ("Codex/", "'Codex/' is not a Kind/Level pair. Use ClaudeCode/Frontier, Codex/High, Grok/Frontier."),
            ("Grok", "'Grok' is not a Kind/Level pair. Use ClaudeCode/Frontier, Codex/High, Grok/Frontier."),
            (" , ", "set requires at least one Kind/Level pair in --candidates."),
        })
        {
            var refused = Run(server, "set", "--complexity", "Hard", "--candidates", raw);
            refused.Exit.ShouldBe(1, refused.All);
            refused.Error.Trim().ShouldBe(refusal);
        }
        server.RequestCount.ShouldBe(0);
        ComplexityChainCli.ParseCandidates(" ClaudeCode/Frontier , Grok/High,", out _)!.ToJsonString().ShouldBe(
            """[{"agentKind":"ClaudeCode","modelLevel":"Frontier"},{"agentKind":"Grok","modelLevel":"High"}]""");
    }

    [Test]
    public void Set_All_puts_Hard_Medium_Easy_in_order()
    {
        var server = new ScriptedApi();
        var run = Run(server, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--reason", "r");
        run.Exit.ShouldBe(0, run.All);
        RequestLines(server).ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy" });
        foreach (var request in server.Requests.Where(r => r.Method == "PUT"))
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
        var files = new MockFileSystem();
        var reasonFile = Path.Combine(Path.GetTempPath(), "complexity-chain-" + Guid.NewGuid().ToString("N"), "reason.txt");
        files.AddFile(reasonFile, new MockFileData(new UTF8Encoding(false).GetBytes("review café\nsecond line")));
        var fileServer = new ScriptedApi();
        var fromFile = Run(fileServer, files, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--reason-file", reasonFile);
        fromFile.Exit.ShouldBe(0, fromFile.All);
        fileServer.RequestCount.ShouldBe(4);
        foreach (var request in fileServer.Requests.Where(r => r.Method == "PUT"))
            request.Body!.Value.GetProperty("reason").GetString().ShouldBe("review café\nsecond line");

        var invalid = new ScriptedApi();
        var conflict = Run(invalid, files, "set", "--complexity", "All", "--candidates", "Codex/Frontier", "--reason", "r", "--reason-file", reasonFile);
        conflict.Exit.ShouldBe(2);
        conflict.Error.ShouldContain("mutually exclusive");
        var notSet = Run(invalid, files, "clear", "--complexity", "All", "--reason-file", reasonFile);
        notSet.Exit.ShouldBe(2);
        notSet.Error.ShouldContain("--reason-file requires set");
        var missing = Run(invalid, files, "set", "--complexity", "All", "--candidates", "Codex/Frontier", "--reason-file", reasonFile + ".missing");
        missing.Exit.ShouldBe(1);
        missing.Error.ShouldContain("does not exist");
        invalid.RequestCount.ShouldBe(0);
    }

    [Test]
    public void Set_All_stops_at_the_first_failing_tier_and_names_written_tiers()
    {
        var server = new ScriptedApi();
        server.Respond("PUT", "/api/complexity-chains/Review/Medium", 422, """{"errors":{"Candidates":["boom"]}}""");
        var run = Run(server, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier");
        run.Exit.ShouldBe(1, run.All);
        RequestLines(server).ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium" });
        server.Cells.Keys.ShouldBe(new[] { "Review/Hard" });
        run.Output.ShouldContain("Hard written; Medium refused:");
        run.Output.ShouldContain("boom");
        run.Output.ShouldContain("Easy not written");
        run.Output.ShouldContain("Medium refused: Antiphon PUT /api/complexity-chains/Review/Medium failed: HTTP 422");
    }

    [Test]
    public void ClearStagePin_requires_All_and_a_named_role()
    {
        string[][] invalidArguments = [
            ["set", "--role", "Review", "--complexity", "Hard", "--candidates", "Codex/Frontier", "--clear-stage-pin"],
            ["set", "--role", "Any", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin"],
            ["set", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin"],
            ["clear", "--role", "Review", "--complexity", "All", "--clear-stage-pin"],
            ["get", "--role", "Review", "--complexity", "All", "--clear-stage-pin"] ];
        foreach (var args in invalidArguments)
        {
            var server = new ScriptedApi();
            var run = Run(server, args);
            run.Exit.ShouldBe(2, run.All);
            run.Error.ShouldContain("--clear-stage-pin requires set --complexity All and a named --role (not Any)");
            server.RequestCount.ShouldBe(0);
        }
    }

    [Test]
    public void ClearStagePin_deletes_the_stage_pin_only_after_three_2xx()
    {
        var server = new ScriptedApi();
        server.Pins.Add(JsonNode.Parse("""{"id":"card-1","role":"Review","cardId":"card-a"}""")!.AsObject());
        server.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        server.Pins.Add(JsonNode.Parse("""{"id":"other-stage","role":"Code","cardId":null}""")!.AsObject());
        var run = Run(server, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin");
        run.Exit.ShouldBe(0, run.All);
        RequestLines(server).ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy",
            "GET /api/routing-pins?role=Review",
            "DELETE /api/routing-pins/stage-1" });
        server.Pins.Select(pin => pin["id"]!.GetValue<string>()).ShouldBe(new[] { "card-1", "other-stage" });
        run.Output.ShouldContain("cleared Review stage pin");

        var absent = new ScriptedApi();
        var noPin = Run(absent, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin");
        noPin.Exit.ShouldBe(0, noPin.All);
        absent.RequestCount.ShouldBe(5);
        absent.Requests.Last().Method.ShouldBe("GET");
        noPin.Output.ShouldContain("No active routing pin for that grain.");

        var refused = new ScriptedApi();
        refused.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        refused.Respond("DELETE", "/api/routing-pins/stage-1", 409, """{"detail":"pin clear refused","code":"test_pin_refused"}""");
        var failedClear = Run(refused, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin");
        failedClear.Exit.ShouldBe(1);
        failedClear.Error.ShouldContain("pin clear refused");
        failedClear.Error.ShouldContain("test_pin_refused");
        refused.Cells.Count.ShouldBe(3);
        refused.Pins.Count.ShouldBe(1);
    }

    [Test]
    public void ClearStagePin_is_skipped_when_a_tier_failed()
    {
        var server = new ScriptedApi();
        server.Pins.Add(JsonNode.Parse("""{"id":"stage-1","role":"Review","cardId":null}""")!.AsObject());
        server.Respond("PUT", "/api/complexity-chains/Review/Easy", 422, """{"detail":"easy refused","code":"test_easy_refused"}""");
        var run = Run(server, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier", "--clear-stage-pin");
        run.Exit.ShouldBe(1, run.All);
        RequestLines(server).ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy" });
        server.Pins.Single()["id"]!.GetValue<string>().ShouldBe("stage-1");
        run.Output.ShouldContain("stage pin kept");
        run.Output.ShouldContain("easy refused");
        run.Output.ShouldContain("test_easy_refused");
    }

    [Test]
    public void Get_Role_prints_the_effective_line()
    {
        var server = new ScriptedApi();
        server.Respond("GET", "/api/complexity-chains?role=Review", 200,
            """
            {"chains":[{"role":"Review","complexity":"Medium","resolvedFrom":"role",
              "candidates":[{"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true}],
              "effective":{"source":"pin","candidates":[{"agentKind":"Codex","modelLevel":"Frontier","alias":"gpt-6-astra","availableNow":true}]}}]}
            """);
        var run = Run(server, "get", "--role", "Review");
        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldContain("Review/Medium");
        run.Output.ShouldContain("ClaudeCode/High (opus)");
        run.Output.ShouldContain("effective: pin Codex/Frontier (gpt-6-astra)");
        run.Output.ShouldNotContain("effective: pin ClaudeCode");
        Lines(run.Output).ShouldBe([
            "  Review/Medium  (own)  ClaudeCode/High (opus) available",
            "    effective: pin Codex/Frontier (gpt-6-astra) available"]);

        var noEffective = new ScriptedApi();
        noEffective.Respond("GET", "/api/complexity-chains?role=Review", 200,
            """{"chains":[{"role":"Review","complexity":"Medium","resolvedFrom":"none","candidates":[],"effective":null}]}""");
        var legacy = Run(noEffective, "get", "--role", "Review");
        legacy.Exit.ShouldBe(0, legacy.All);
        legacy.Output.ShouldContain("Review/Medium");
        legacy.Output.ShouldNotContain("effective:");
        legacy.Output.Trim().ShouldBe("Review/Medium  (empty)  (empty - Blocked until set)");
    }

    [Test]
    public void Get_Role_marks_each_fallback_and_a_withheld_cell()
    {
        var server = new ScriptedApi();
        server.Respond("GET", "/api/complexity-chains?role=Code", 200,
            """
            {"chains":[
              {"role":"Code","complexity":"Hard","resolvedFrom":"any","candidates":[{"agentKind":"Grok","modelLevel":"Frontier","alias":"grok-4","availableNow":false,"unavailableReason":"held"}],
               "effective":{"source":"chain","candidates":[],"withheldReason":"every candidate is held"}},
              {"role":"Code","complexity":"Medium","resolvedFrom":"config","candidates":[{"agentKind":"Codex","modelLevel":"High","alias":"sol","availableNow":false}],
               "effective":{"source":"chain","candidates":[]}},
              {"role":"Code","complexity":"Easy","resolvedFrom":"config","candidates":[]}]}
            """);
        var run = Run(server, "get", "--role", "Code");
        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe([
            "  Code/Hard  (via any)  Grok/Frontier (grok-4) held",
            "    effective: chain not launchable: every candidate is held",
            "  Code/Medium  (via config)  Codex/High (sol) unavailable",
            "    effective: chain (empty)",
            "  Code/Easy  (via config)  (empty)"]);

        // A row line carries notAfter and the reason; an empty list says so.
        var empty = new ScriptedApi();
        empty.Respond("GET", "/api/complexity-chains", 200, """{"chains":[]}""");
        Run(empty, "get").Output.Trim().ShouldBe("No complexity chains.");
        ComplexityChainCli.ChainLine(JsonNode.Parse(
            """{"role":null,"complexity":"Easy","source":"chain:Easy","provenance":"Auto","notAfter":"2026-12-01T00:00:00Z","reason":"cheap checks","candidates":[]}"""), false)
            .ShouldBe("Easy  chain:Easy/Auto  (empty)  notAfter 2026-12-01T00:00:00Z, cheap checks");
    }

    [Test]
    public void Clear_All_deletes_three_cells()
    {
        var server = new ScriptedApi();
        var run = Run(server, "clear", "--role", "Review", "--complexity", "All");
        run.Exit.ShouldBe(0, run.All);
        RequestLines(server).ShouldBe(new[] {
            "GET /api/complexity-chains",
            $"DELETE /api/complexity-chains/Review/Hard?concurrencyToken={EmptyToken}",
            $"DELETE /api/complexity-chains/Review/Medium?concurrencyToken={EmptyToken}",
            $"DELETE /api/complexity-chains/Review/Easy?concurrencyToken={EmptyToken}" });
        run.Output.ShouldContain("cleared Review/Hard");
        run.Output.ShouldContain("cleared Review/Medium");
        run.Output.ShouldContain("cleared Review/Easy");
    }

    [Test]
    public void Set_and_clear_send_each_cells_token_from_one_read_and_stop_on_stale()
    {
        var server = new ScriptedApi();
        var seed = Run(server, "set", "--role", "Review", "--complexity", "Hard", "--candidates", "Grok/Frontier");
        seed.Exit.ShouldBe(0, seed.All);
        var hardToken = server.Tokens["Review/Hard"];

        var all = Run(server, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier");
        all.Exit.ShouldBe(0, all.All);
        var puts = server.Requests.Skip(2).ToArray();
        puts.Select(r => $"{r.Method} {r.Path}").ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium",
            "PUT /api/complexity-chains/Review/Easy" });
        puts[1].Body!.Value.GetProperty("concurrencyToken").GetString().ShouldBe(hardToken);
        puts[2].Body!.Value.GetProperty("concurrencyToken").GetString().ShouldBe(EmptyToken);
        puts[3].Body!.Value.GetProperty("concurrencyToken").GetString().ShouldBe(EmptyToken);

        var tokens = new Dictionary<string, string>(server.Tokens);
        var clear = Run(server, "clear", "--role", "Review", "--complexity", "All");
        clear.Exit.ShouldBe(0, clear.All);
        RequestLines(server).Skip(6).ShouldBe(new[] {
            "GET /api/complexity-chains",
            $"DELETE /api/complexity-chains/Review/Hard?concurrencyToken={tokens["Review/Hard"]}",
            $"DELETE /api/complexity-chains/Review/Medium?concurrencyToken={tokens["Review/Medium"]}",
            $"DELETE /api/complexity-chains/Review/Easy?concurrencyToken={tokens["Review/Easy"]}" });

        var stale = new ScriptedApi();
        stale.Respond("PUT", "/api/complexity-chains/Review/Medium", 409,
            """{"detail":"The Review/Medium chain changed since it was read","code":"complexity_chain_stale"}""");
        var refused = Run(stale, "set", "--role", "Review", "--complexity", "All", "--candidates", "Codex/Frontier");
        refused.Exit.ShouldBe(1, refused.All);
        RequestLines(stale).ShouldBe(new[] {
            "GET /api/complexity-chains",
            "PUT /api/complexity-chains/Review/Hard",
            "PUT /api/complexity-chains/Review/Medium" });
        refused.Output.ShouldContain("Hard written; Medium refused:");
        refused.Output.ShouldContain("code complexity_chain_stale");
        refused.Output.ShouldContain("Easy not written");

        // A clear stops on a refused tier; the tiers before it stay cleared.
        var staleClear = new ScriptedApi();
        staleClear.Respond("DELETE", "/api/complexity-chains/Review/Medium", 409, """{"detail":"changed","code":"complexity_chain_stale"}""");
        var stopped = Run(staleClear, "clear", "--role", "Review", "--complexity", "All");
        stopped.Exit.ShouldBe(1);
        stopped.Output.Trim().ShouldBe("cleared Review/Hard (config default applies again)");
        stopped.Error.ShouldContain("Antiphon DELETE /api/complexity-chains/Review/Medium?concurrencyToken=" + EmptyToken + " failed: changed");
        staleClear.Requests.Count(r => r.Method == "DELETE").ShouldBe(2);
    }

    [Test]
    public void Json_prints_the_answers()
    {
        var server = new StubApi();
        var get = Run(server, "get", "--json");
        get.Exit.ShouldBe(0, get.All);
        JsonNode.Parse(get.Output)!["chains"]!.AsArray().Count.ShouldBe(3);

        var one = Run(server, "set", "--complexity", "Hard", "--candidates", "Grok/Frontier", "--json");
        one.Exit.ShouldBe(0, one.All);
        JsonNode.Parse(one.Output)!["complexity"]!.GetValue<string>().ShouldBe("Hard");

        var scripted = new ScriptedApi();
        var all = Run(scripted, "set", "--role", "Review", "--complexity", "All", "--candidates", "Grok/Frontier", "--json");
        all.Exit.ShouldBe(0, all.All);
        JsonNode.Parse(all.Output)!.AsArray().Select(chain => chain!["complexity"]!.GetValue<string>()).ShouldBe(["Hard", "Medium", "Easy"]);
        all.Output.ShouldNotContain("written");

        var withPin = Run(scripted, "set", "--role", "Review", "--complexity", "All", "--candidates", "Grok/Frontier", "--clear-stage-pin", "--json");
        withPin.Exit.ShouldBe(0, withPin.All);
        var answer = JsonNode.Parse(withPin.Output)!;
        answer["chains"]!.AsArray().Count.ShouldBe(3);
        answer["stagePin"]!.GetValue<string>().ShouldBe("No active routing pin for that grain.");
    }

    [Test]
    public void The_task_token_is_sent_and_usage_errors_are_exit_2()
    {
        var server = new StubApi();
        var run = BoardCommandRun.Run(host => ComplexityChainCli.Run(
            ComplexityChainCliOptions.Parse(["set", "--complexity", "Hard", "--candidates", "Grok/Frontier"], ScriptedApi.BaseUrl, "task-secret"), host,
            new MockFileSystem(), server.Transport));
        run.Exit.ShouldBe(0, run.All);
        server.Requests.ShouldAllBe(request => request.Headers[OperatorApi.TaskTokenHeader] == "task-secret");
        run.All.ShouldNotContain("task-secret");

        foreach (var (args, message) in new (string[], string)[]
        {
            (["set", "--candidates", "Grok/Frontier"], "set requires --complexity Hard|Medium|Easy|All"),
            (["clear", "--role", "Plan"], "clear requires --complexity Hard|Medium|Easy|All"),
            (["get", "--complexity", "Hard"], "--complexity does not apply to get"),
            (["clear", "--complexity", "Hard", "--candidates", "Grok/Frontier"], "--candidates does not apply to clear"),
            (["set", "--role", "Check", "--complexity", "Hard"], "--role must be one of Investigate, Plan,"),
            (["set", "--complexity", "Extreme"], "--complexity must be one of Hard, Medium, Easy, All, not 'Extreme'"),
            (["set", "--complexity", "Hard", "--provenance", "Robot"], "--provenance must be one of Human, Auto"),
            (["list"], "the first argument is the verb (get, set, clear), not 'list'"),
            (["get", "-Role", "Plan"], "unexpected argument '-Role'"),
        })
        {
            var refused = Run(server, args);
            refused.Exit.ShouldBe(2, string.Join(" ", args));
            refused.Error.ShouldContain(message);
        }
        ComplexityChainCliOptions.Parse([], null, null).Verb.ShouldBe("get");
        ComplexityChainCliOptions.Parse([], null, null).Api.ShouldBe(OperatorApi.DefaultApi);
    }

    [Test]
    public void Commands_are_ascii_only()
    {
        foreach (var name in new[] { "ComplexityChainCli.cs", "StagePinMigration.cs", "OrchestratorWorkspace.cs" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            Array.FindIndex(bytes, static b => b > 127).ShouldBe(-1, name + " has a non-ASCII byte");
        }
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "complexity-chain.ps1")).ShouldBeFalse("the command replaced the script; no shim stays");
    }

    private const string EmptyToken = "00000000-0000-0000-0000-000000000000";
    private const string StubHardToken = "11111111-2222-3333-4444-555555555555";

    private static string[] RequestLines(ScriptedApi server) =>
        server.Requests.Select(request => $"{request.Method} {request.Path}").ToArray();

    private static string[] Lines(string output) =>
        output.Replace("\r\n", "\n").Split('\n').Where(line => line.Length > 0).ToArray();

    private static BoardCommandRun.Result Run(ScriptedApi server, params string[] args) => Run(server, new MockFileSystem(), args);

    private static BoardCommandRun.Result Run(ScriptedApi server, MockFileSystem files, params string[] args) =>
        BoardCommandRun.Run(host => ComplexityChainCli.Run(ComplexityChainCliOptions.Parse(args, ScriptedApi.BaseUrl, null), host, files, server.Transport));

    // The CARD-0090 list and row answers: GET /api/complexity-chains the three any-role tiers (Hard with a row), a PUT the Hard row.
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
            {"chains":[{"complexity":"Hard","candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null}],"provenance":"Human","source":"pin","reason":"test","notAfter":null,"updatedAt":"2026-09-02T00:00:00Z","concurrencyToken":"11111111-2222-3333-4444-555555555555"},{"complexity":"Medium","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null},{"complexity":"Easy","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null}]}
            """;
    }
}
