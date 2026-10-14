using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MigrateStagePinsScriptTests
{
    [Test]
    public async Task Dry_run_writes_nothing_and_records_before_images()
    {
        using var fixture = new Fixture();
        var run = await fixture.RunAsync();
        run.ExitCode.ShouldBe(0, run.Output);
        fixture.Api.Requests.Select(r => r.Method).Distinct().ShouldBe(new[] { "GET" });
        fixture.Api.Requests.Select(r => r.Path).ShouldBe(new[] {
            "/api/routing-pins", "/api/model-availability",
            "/api/complexity-chains?role=Plan", "/api/complexity-chains?role=TestDesign",
            "/api/complexity-chains?role=Code", "/api/complexity-chains?role=Review",
            "/api/complexity-chains?role=Investigate", "/api/complexity-chains?role=Debug",
            "/api/complexity-chains?role=Coverage", "/api/complexity-chains?role=Docs",
            "/api/complexity-chains?role=Commit", "/api/complexity-chains?role=Test",
            "/api/complexity-chains?role=Deploy", "/api/complexity-chains?role=Merge",
            "/api/complexity-chains?role=Mutation" });
        var snapshot = fixture.ReadSnapshot();
        snapshot["apiBase"]!.GetValue<string>().ShouldBe(fixture.Api.BaseUrl.TrimEnd('/'));
        DateTimeOffset.Parse(snapshot["createdAt"]!.GetValue<string>()).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        snapshot["pins"]!.AsArray().Count.ShouldBe(13);
        snapshot["cellsBefore"]!.AsArray().Count.ShouldBe(39);
        foreach (var cell in snapshot["cellsBefore"]!.AsArray())
            cell!["present"]!.GetValue<bool>().ShouldBeFalse();
        AssertPlan(snapshot["plan"]!.AsArray());
        run.Output.ShouldContain(fixture.SnapshotPath);
        run.Output.Split('\n').Count(line => line.StartsWith("Plan |", StringComparison.Ordinal)).ShouldBe(1);
        foreach (var role in Roles)
            run.Output.ShouldContain($"{role} |");
        run.Output.ShouldContain("effective BEFORE: Required pin Codex/Frontier,Codex/High,ClaudeCode/High");
        run.Output.ShouldContain("effective AFTER apply: Required pin Codex/Frontier,Codex/High,ClaudeCode/High");
        run.Output.ShouldContain("after separate pin clear: role Codex/Frontier,Codex/High,ClaudeCode/High");
        run.Output.ShouldContain("Read back with complexity-chain.ps1 get -Role Plan");
        run.Output.ShouldContain("routing-pin.ps1 clear -Role Plan");
        fixture.Api.Cells.Count.ShouldBe(0);

        // The explicit spelling used for an optional live read has the same GET-only boundary.
        var explicitDry = await fixture.RunAsync("-DryRun", "-Role", "Review");
        explicitDry.ExitCode.ShouldBe(0, explicitDry.Output);
        fixture.Api.Requests.Select(r => r.Method).Distinct().ShouldBe(new[] { "GET" });
        explicitDry.Output.ShouldContain("Review |");
        explicitDry.Output.ShouldNotContain("Plan |");
    }

    [Test]
    public async Task Manual_and_card_pins_are_listed_and_never_written()
    {
        using var fixture = new Fixture();
        fixture.Pin("Plan")["forbiddenAliases"] = new JsonArray("fable");
        fixture.Pin("Code")["notAfter"] = "2099-01-01T00:00:00Z";
        fixture.Pin("Review")["agentId"] = "d248ed59-8f72-4d15-bf5d-20fd57085829";
        fixture.Pin("Debug")["notBefore"] = "2026-01-01T00:00:00Z";
        var pinsBefore = fixture.PinsJson;
        var run = await fixture.RunAsync("-Apply");
        run.ExitCode.ShouldBe(0, run.Output);
        foreach (var role in new[] { "Plan", "Code", "Review", "Debug" })
        {
            run.Output.ShouldContain($"{role} | manual");
            fixture.Api.Requests.Any(r => r.Method == "PUT" && r.Path.Contains($"/{role}/")).ShouldBeFalse();
        }
        fixture.Api.Cells.Count.ShouldBe(27);
        run.Output.ShouldNotContain("card-only");
        fixture.ReadSnapshot()["plan"]!.ToJsonString().ShouldNotContain("card-only");
        fixture.PinsJson.ShouldBe(pinsBefore);
        AssertNoPinWrites(fixture.Api);
    }

    [Test]
    public async Task Apply_puts_three_cells_per_role_with_human_provenance_and_the_migration_reason()
    {
        using var fixture = new Fixture();
        var pinsBefore = fixture.PinsJson;
        var run = await fixture.RunAsync("-Apply");
        run.ExitCode.ShouldBe(0, run.Output);
        AssertWrites(fixture.Api.Requests.Where(r => r.Method == "PUT").ToArray());
        fixture.Api.Cells.Count.ShouldBe(39);
        fixture.PinsJson.ShouldBe(pinsBefore);
        AssertNoPinWrites(fixture.Api);

        // A changed live candidate, rather than a copied migration table, must drive the PUT.
        using var held = new Fixture();
        held.Pin("Plan")["candidates"] = JsonNode.Parse("""[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":false,"unavailableReason":"held"},{"agentKind":"Grok","modelLevel":"Frontier","alias":"grok-4","availableNow":false,"unavailableReason":"held"}]""");
        held.Pin("Plan")["candidateCount"] = 2;
        var heldRun = await held.RunAsync("-Apply", "-Role", "Plan");
        heldRun.ExitCode.ShouldBe(0, heldRun.Output);
        heldRun.Output.ShouldContain("HELD Plan ClaudeCode/Frontier (fable): operator fable hold");
        heldRun.Output.ShouldContain("HELD Plan Grok/Frontier (grok-4): operator Grok hold");
        held.Api.Cells.Count.ShouldBe(3);
        Pairs(held.Api.Cells["Plan/Hard"]["candidates"]!).ShouldBe("ClaudeCode/Frontier,Grok/Frontier");
        heldRun.Output.IndexOf("HELD Plan", StringComparison.Ordinal)
            .ShouldBeLessThan(heldRun.Output.IndexOf("written:", StringComparison.Ordinal));
        AssertNoPinWrites(held.Api);
    }

    [Test]
    [Arguments("apply")]
    [Arguments("restore")]
    public async Task A_non_2xx_stops_the_run_and_names_the_cells_written(string phase)
    {
        using var fixture = new Fixture();
        int start;
        (int ExitCode, string Output) run;
        if (phase == "apply")
        {
            fixture.Api.Respond("PUT", "/api/complexity-chains/TestDesign/Medium", 500,
                """{"detail":"cell refused","code":"test_migration_failed"}""");
            start = 0;
            run = await fixture.RunAsync("-Apply");
            run.Output.ShouldContain("written: Plan/Hard, Plan/Medium, Plan/Easy, TestDesign/Hard");
            run.Output.ShouldContain("not written: TestDesign/Easy");
            fixture.Api.Requests.Last().Path.ShouldBe("/api/complexity-chains/TestDesign/Medium");
            fixture.Api.Requests.Count(r => r.Method == "PUT").ShouldBe(5);
            fixture.Api.Requests.Any(r => r.Method == "DELETE").ShouldBeFalse();
        }
        else
        {
            fixture.SeedBefore("human");
            var apply = await fixture.RunAsync("-Apply");
            apply.ExitCode.ShouldBe(0, apply.Output);
            fixture.Api.Respond("DELETE", "/api/complexity-chains/TestDesign/Easy", 500,
                """{"detail":"cell refused","code":"test_migration_failed"}""");
            start = fixture.Api.RequestCount;
            run = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
            run.Output.ShouldContain("restored: Plan/Hard, Plan/Medium, Plan/Easy, TestDesign/Hard, TestDesign/Medium");
            run.Output.ShouldContain("not restored: Code/Hard");
            fixture.Api.Requests.Last().Path.ShouldBe("/api/complexity-chains/TestDesign/Easy");
            fixture.Api.Requests.Skip(start).Count(r => r.Method != "GET").ShouldBe(6);
        }
        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("500");
        run.Output.ShouldContain("cell refused");
        run.Output.ShouldContain("test_migration_failed");
        fixture.Api.Requests.Skip(start).Last().Method.ShouldBe(phase == "apply" ? "PUT" : "DELETE");
        AssertNoPinWrites(fixture.Api);
    }

    [Test]
    public async Task A_second_apply_sends_the_same_puts_and_leaves_the_store_unchanged()
    {
        using var fixture = new Fixture();
        var first = await fixture.RunAsync("-Apply");
        first.ExitCode.ShouldBe(0, first.Output);
        var snapshot = fixture.SnapshotPath;
        var before = fixture.CellsJson;
        var firstWrites = fixture.Api.Requests.Where(r => r.Method == "PUT").ToArray();
        AssertWrites(firstWrites);
        var start = fixture.Api.RequestCount;
        var second = await fixture.RunAsync("-Apply");
        second.ExitCode.ShouldBe(0, second.Output);
        var secondWrites = fixture.Api.Requests.Skip(start).Where(r => r.Method == "PUT").ToArray();
        AssertWrites(secondWrites);
        secondWrites.Select(r => r.Path + r.Body!.Value.GetRawText())
            .ShouldBe(firstWrites.Select(r => r.Path + r.Body!.Value.GetRawText()));
        fixture.CellsJson.ShouldBe(before);
        Directory.GetFiles(fixture.DirectoryPath, "*.json").Length.ShouldBe(2);
        File.Exists(snapshot).ShouldBeTrue();
        AssertNoPinWrites(fixture.Api);
    }

    [Test]
    [Arguments("human")]
    [Arguments("human-expired")]
    [Arguments("auto")]
    public async Task Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones(string kind)
    {
        using var fixture = new Fixture();
        fixture.SeedBefore(kind);
        var pinsBefore = fixture.PinsJson;
        var initialCells = fixture.CellsJson;
        var apply = await fixture.RunAsync("-Apply");
        apply.ExitCode.ShouldBe(0, apply.Output);
        var snapshot = fixture.ReadSnapshot();
        var prior = snapshot["cellsBefore"]!.AsArray().Single(c => c!["role"]!.GetValue<string>() == "Plan"
            && c["complexity"]!.GetValue<string>() == "Medium")!;
        prior["present"]!.GetValue<bool>().ShouldBeTrue();
        prior["provenance"]!.GetValue<string>().ShouldBe(kind == "auto" ? "Auto" : "Human");
        prior["reason"]!.GetValue<string>().ShouldBe("prior operator words");
        Pairs(prior["candidates"]!).ShouldBe("Grok/Frontier");
        if (kind == "human-expired")
        {
            prior["notAfter"] = "2026-01-01T00:00:00Z";
            await File.WriteAllTextAsync(fixture.SnapshotPath, snapshot.ToJsonString());
        }
        if (kind == "auto") apply.Output.ShouldContain("Auto (restore clears)");
        var start = fixture.Api.RequestCount;
        var restore = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
        restore.ExitCode.ShouldBe(0, restore.Output);
        var requests = fixture.Api.Requests.Skip(start).ToArray();
        requests.Take(13).Select(r => r.Method + " " + r.Path)
            .ShouldBe(Roles.Select(role => "GET /api/routing-pins?role=" + role));
        requests.Length.ShouldBe(52);
        var writes = requests.Skip(13).ToArray();
        writes.Select(r => r.Path).ShouldBe(CellPaths());
        writes.Count(r => r.Method == "PUT").ShouldBe(kind == "human" ? 1 : 0);
        writes.Count(r => r.Method == "DELETE").ShouldBe(kind == "human" ? 38 : 39);
        if (kind == "human")
        {
            var body = writes.Single(r => r.Method == "PUT").Body!.Value;
            writes.Single(r => r.Method == "PUT").Path.ShouldBe("/api/complexity-chains/Plan/Medium");
            body.GetProperty("provenance").GetString().ShouldBe("Human");
            body.GetProperty("reason").GetString().ShouldBe("prior operator words");
            body.GetProperty("notAfter").ValueKind.ShouldBe(JsonValueKind.Null);
            Pairs(JsonNode.Parse(body.GetProperty("candidates").GetRawText())!).ShouldBe("Grok/Frontier");
            fixture.CellsJson.ShouldBe(initialCells);
        }
        else
        {
            fixture.Api.Cells.Count.ShouldBe(0);
            restore.Output.ShouldContain(kind == "auto" ? "Auto before-image not re-created; cleared" : "expired before restore; cleared");
        }
        start = fixture.Api.RequestCount;
        var second = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
        second.ExitCode.ShouldBe(0, second.Output);
        fixture.Api.Requests.Skip(start).Select(r => r.Method + r.Path + r.Body?.GetRawText())
            .ShouldBe(requests.Select(r => r.Method + r.Path + r.Body?.GetRawText()));
        fixture.PinsJson.ShouldBe(pinsBefore);
        AssertNoPinWrites(fixture.Api);
    }

    [Test]
    public async Task Restore_refuses_until_each_migrated_roles_required_stage_pin_is_back()
    {
        using var fixture = new Fixture();
        var apply = await fixture.RunAsync("-Apply");
        apply.ExitCode.ShouldBe(0, apply.Output);
        var pins = fixture.Api.Pins.Select(p => p.DeepClone().AsObject()).ToArray();
        var cells = fixture.CellsJson;
        fixture.Api.Pins.Clear();
        var start = fixture.Api.RequestCount;
        var absent = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
        absent.ExitCode.ShouldBe(1, absent.Output);
        absent.Output.ShouldContain("restore refused: Plan has no Required stage pin");
        absent.Output.ShouldContain("routing-pin.ps1 set -Role Plan -Provenance Human -Strength Required -Candidates Codex/Frontier,Codex/High,ClaudeCode/High");
        fixture.Api.Requests.Skip(start).Select(r => r.Method).Distinct().ShouldBe(new[] { "GET" });
        fixture.CellsJson.ShouldBe(cells);
        fixture.Api.Pins.AddRange(pins);
        fixture.Pin("Review")["strength"] = "Preferred";
        start = fixture.Api.RequestCount;
        var preferred = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
        preferred.ExitCode.ShouldBe(1, preferred.Output);
        preferred.Output.ShouldContain("restore refused: Review has no Required stage pin");
        fixture.Api.Requests.Skip(start).Any(r => r.Method != "GET").ShouldBeFalse();
        fixture.CellsJson.ShouldBe(cells);
        fixture.Pin("Review")["strength"] = "Required";

        // Expired, future and card-only Required rows do not satisfy the rollback precondition.
        foreach (var field in new[] { "notAfter", "notBefore", "cardId" })
        {
            fixture.Pin("Review")[field] = field switch {
                "notAfter" => "2026-01-01T00:00:00Z", "notBefore" => "2099-01-01T00:00:00Z", _ => "card-a" };
            start = fixture.Api.RequestCount;
            var inactive = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
            inactive.ExitCode.ShouldBe(1, inactive.Output);
            inactive.Output.ShouldContain("restore refused: Review");
            fixture.Api.Requests.Skip(start).Any(r => r.Method != "GET").ShouldBeFalse();
            fixture.Api.Pins.Single(p => p["id"]!.GetValue<string>() == "50328b61-de56-4508-a037-d707d652d583")[field] = null;
        }
        var snapshot = fixture.ReadSnapshot();
        snapshot["apiBase"] = "http://different.invalid";
        var wrongBase = Path.Combine(fixture.DirectoryPath, "wrong-base.json");
        await File.WriteAllTextAsync(wrongBase, snapshot.ToJsonString());
        start = fixture.Api.RequestCount;
        var mismatch = await fixture.RunAsync("-Restore", "-Snapshot", wrongBase);
        mismatch.ExitCode.ShouldBe(1, mismatch.Output);
        mismatch.Output.ShouldContain("apiBase");
        fixture.Api.Requests.Skip(start).Any(r => r.Method != "GET").ShouldBeFalse();
        start = fixture.Api.RequestCount;
        var restored = await fixture.RunAsync("-Restore", "-Snapshot", fixture.SnapshotPath);
        restored.ExitCode.ShouldBe(0, restored.Output);
        fixture.Api.Requests.Skip(start).Count(r => r.Method == "DELETE").ShouldBe(39);
        fixture.Api.Cells.Count.ShouldBe(0);
        AssertNoPinWrites(fixture.Api);
    }

    private static string[] Roles => ["Plan", "TestDesign", "Code", "Review", "Investigate", "Debug", "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Mutation"];
    private static string[] CellPaths() => Roles.SelectMany(role => new[] {
        $"/api/complexity-chains/{role}/Hard", $"/api/complexity-chains/{role}/Medium", $"/api/complexity-chains/{role}/Easy" }).ToArray();
    private static string Pairs(JsonNode candidates) => string.Join(",", candidates.AsArray().Select(c => $"{c!["agentKind"]}/{c["modelLevel"]}"));
    private static string ExpectedPairs(string role) => role switch {
        "Plan" or "TestDesign" or "Code" or "Investigate" or "Debug" => "Codex/Frontier,Codex/High,ClaudeCode/High",
        "Review" => "ClaudeCode/High",
        _ => "Codex/High,Codex/Medium,ClaudeCode/High" };

    private static void AssertPlan(JsonArray plan)
    {
        plan.Count.ShouldBe(39);
        foreach (var row in plan)
        {
            var role = row!["role"]!.GetValue<string>();
            var body = row["body"]!;
            Pairs(body["candidates"]!).ShouldBe(ExpectedPairs(role));
            body["provenance"]!.GetValue<string>().ShouldBe("Human");
            body["reason"]!.GetValue<string>().ShouldContain("Operator words for " + role);
            body["reason"]!.GetValue<string>().ShouldEndWith($"(migrated from stage pin {Ids[Array.IndexOf(Roles, role)]})");
        }
    }

    private static void AssertWrites(ScriptedApi.Request[] writes)
    {
        writes.Select(r => r.Path).ShouldBe(CellPaths());
        foreach (var write in writes)
        {
            var role = write.Path.Split('/')[3];
            var body = write.Body!.Value;
            body.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "candidates", "provenance", "reason" });
            body.GetProperty("provenance").GetString().ShouldBe("Human");
            body.GetProperty("reason").GetString().ShouldBe($"Operator words for {role}: café\nsecond line (migrated from stage pin {Ids[Array.IndexOf(Roles, role)]})");
            Pairs(JsonNode.Parse(body.GetProperty("candidates").GetRawText())!).ShouldBe(ExpectedPairs(role));
            foreach (var candidate in body.GetProperty("candidates").EnumerateArray())
                candidate.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "agentKind", "modelLevel" });
        }
    }

    private static void AssertNoPinWrites(ScriptedApi api) => api.Requests
        .Any(r => r.Path.StartsWith("/api/routing-pins", StringComparison.Ordinal) && r.Method != "GET").ShouldBeFalse();

    private static string[] Ids => ["bbdce6e6-196f-45a5-a6de-f69d151a523c", "90e8d896-7829-40ec-bc39-36fcb984cd47",
        "4683c049-b5f0-4919-aee0-e1ccaad119c3", "50328b61-de56-4508-a037-d707d652d583", "de218932-d25f-4961-9a6f-e87f5e1da7d2",
        "909836ed-74dd-4016-933d-ffe2c729624f", "858afdbb-1a31-4d22-9cf3-228151d30609", "11a17ef6-051a-4df3-860c-2d00a145cb10",
        "68d8d684-6766-40b7-b59d-8eb2751f5654", "6855ae74-d13c-449e-9d11-bbf62d5a5f89", "f890319f-87b0-408d-848d-762aee01206c",
        "e6d574aa-28a1-47b5-be8e-63bac635db33", "64e43198-dd6c-4347-9011-1897f89d3cf2"];

    private sealed class Fixture : IDisposable
    {
        public ScriptedApi Api { get; } = new();
        public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("c097-migration-").FullName;
        public string SnapshotPath => Directory.GetFiles(DirectoryPath, "migrate-stage-pins-*.json").OrderBy(File.GetCreationTimeUtc).First();
        public string PinsJson => JsonSerializer.Serialize(Api.Pins);
        public string CellsJson => JsonSerializer.Serialize(Api.Cells.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value));
        public JsonObject Pin(string role) => Api.Pins.Single(p => p["role"]!.GetValue<string>() == role && p["cardId"] is null);
        public JsonObject ReadSnapshot() => JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();

        public Fixture()
        {
            // Same GET shape as the live census on 2026-10-14, including shorthand AND candidates.
            for (var i = 0; i < Roles.Length; i++)
            {
                var role = Roles[i];
                var candidates = role switch {
                    "Review" => """[{"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true,"unavailableReason":null}]""",
                    "Coverage" or "Docs" or "Commit" or "Test" or "Deploy" or "Merge" or "Mutation" => """[{"agentKind":"Codex","modelLevel":"High","alias":"gpt-6.1-sol","availableNow":true,"unavailableReason":null},{"agentKind":"Codex","modelLevel":"Medium","alias":"gpt-6.1-sol","availableNow":true,"unavailableReason":null},{"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true,"unavailableReason":null}]""",
                    _ => """[{"agentKind":"Codex","modelLevel":"Frontier","alias":"gpt-6-astra","availableNow":true,"unavailableReason":null},{"agentKind":"Codex","modelLevel":"High","alias":"gpt-6.1-sol","availableNow":true,"unavailableReason":null},{"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true,"unavailableReason":null}]""" };
                var pin = JsonNode.Parse("""{"cardId":null,"cardIdentifier":null,"provenance":"Human","strength":"Required","agentId":null,"forbiddenAliases":[],"notBefore":null,"notAfter":null,"sourceTaskId":null,"createdAt":"2026-09-05T21:12:33Z","updatedAt":"2026-10-14T10:08:58Z"}""")!.AsObject();
                pin["id"] = Ids[i];
                pin["role"] = role;
                pin["reason"] = $"Operator words for {role}: café\nsecond line";
                pin["candidates"] = JsonNode.Parse(candidates);
                pin["candidateCount"] = pin["candidates"]!.AsArray().Count;
                pin["agentKind"] = pin["candidates"]![0]!["agentKind"]!.DeepClone();
                pin["modelLevel"] = pin["candidates"]![0]!["modelLevel"]!.DeepClone();
                pin["modelAlias"] = pin["candidates"]![0]!["alias"]!.DeepClone();
                Api.Pins.Add(pin);
            }
            var card = Api.Pins[0].DeepClone().AsObject();
            card["id"] = "card-only";
            card["cardId"] = "card-a";
            Api.Pins.Add(card);
            Api.Respond("GET", "/api/model-availability", 200,
                """{"holds":[{"kind":"ClaudeCode","modelAlias":"fable","disabledUntil":"2099-01-01T00:00:00Z","reason":"operator fable hold"},{"kind":"Grok","modelAlias":"*","disabledUntil":"2099-01-01T00:00:00Z","reason":"operator Grok hold"}],"available":["opus","gpt-6-astra","gpt-6.1-sol"]}""");
        }

        public void SeedBefore(string kind)
        {
            var cell = JsonNode.Parse("""{"candidates":[{"agentKind":"Grok","modelLevel":"Frontier"}],"provenance":"Human","reason":"prior operator words","notAfter":null}""")!.AsObject();
            if (kind == "auto") cell["provenance"] = "Auto";
            if (kind == "human-expired") cell["notAfter"] = "2099-01-01T00:00:00Z";
            Api.Cells["Plan/Medium"] = cell;
        }

        public async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
        {
            var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
                Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "migrate-stage-pins-to-cells.ps1"), "-OutDir", DirectoryPath }.Concat(args))
                info.ArgumentList.Add(arg);
            info.Environment["ANTIPHON_API"] = Api.BaseUrl.TrimEnd('/');
            info.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
            using var process = Process.Start(info) ?? throw new InvalidOperationException("pwsh did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            return (process.ExitCode, await stdout + await stderr);
        }

        public void Dispose() { Api.Dispose(); Directory.Delete(DirectoryPath, recursive: true); }
    }
}
