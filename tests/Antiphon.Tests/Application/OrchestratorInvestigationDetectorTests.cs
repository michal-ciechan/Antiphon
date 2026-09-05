using System.Text.Json;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0247 S3 — the C# port of <c>scripts/hooks/orchestrator-investigation.mjs</c>, validated
/// against the same cefed08a fixtures S1 used so the two implementations share ground truth.
/// </summary>
[Category("Unit")]
public class OrchestratorInvestigationDetectorTests
{
    private static string FixturesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            var root = dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
            return Path.Combine(root, "scripts", "hooks", "__tests__", "fixtures");
        }
    }

    [Test]
    public async Task Thresholds_match_the_JS_constants()
    {
        await Task.CompletedTask;
        OrchestratorInvestigationDetector.R.ShouldBe(3);
        OrchestratorInvestigationDetector.NReport.ShouldBe(25);
        OrchestratorInvestigationDetector.NDispatch.ShouldBe(10);
    }

    [Test]
    public async Task Treats_repo_relative_and_absolute_Windows_source_paths_as_source_reads()
    {
        await Task.CompletedTask;
        OrchestratorInvestigationDetector.IsSourcePath("server/Application/Services/AgentControlService.cs")
            .ShouldBeTrue();
        OrchestratorInvestigationDetector.IsSourcePath(
            @"C:\src\Antiphon\server\Application\Services\AgentControlService.cs").ShouldBeTrue();
        OrchestratorInvestigationDetector.IsSourcePath(
            @"C:\src\Antiphon\src\Antiphon.Agents.Pty\Foo.cs").ShouldBeTrue();
        OrchestratorInvestigationDetector.IsSourcePath(
            @"C:\src\Antiphon\docs\orchestration-loop.md").ShouldBeFalse();
        OrchestratorInvestigationDetector.IsSourcePath(
            @"C:\src\Antiphon\.antiphon\task.md").ShouldBeFalse();
        OrchestratorInvestigationDetector.IsSourcePath(
            @"C:\Users\lndco\AppData\Local\Temp\claude\C--src-Antiphon\cefed08a\scratchpad\x.md")
            .ShouldBeFalse();
    }

    [Test]
    public async Task Counts_grep_rn_whose_target_is_a_source_directory_as_a_source_read()
    {
        await Task.CompletedTask;
        var r = OrchestratorInvestigationDetector.ClassifyCall(
            "Bash",
            """{"command":"cd C:/src/Antiphon && grep -rn \"class AgentLaunchResolution\" server/ --include=\"*.cs\""}""");
        r.IsSourceRead.ShouldBeTrue();
    }

    [Test]
    public async Task Never_treats_git_as_a_source_read()
    {
        await Task.CompletedTask;
        var r = OrchestratorInvestigationDetector.ClassifyCall(
            "Bash",
            """{"command":"cd C:/src/Antiphon && git status --short && git log -1 --oneline"}""");
        r.IsSourceRead.ShouldBeFalse();
    }

    [Test]
    public async Task Never_treats_delegate_card_or_dotnet_as_source_reads()
    {
        await Task.CompletedTask;
        OrchestratorInvestigationDetector.ClassifyCall(
            "PowerShell",
            """{"command":"pwsh -File scripts/delegate.ps1 -Role Debug -Title \"x\""}""")
            .Kind.ShouldBe(OrchestratorInvestigationDetector.EventKind.Dispatch);
        OrchestratorInvestigationDetector.ClassifyCall(
            "Bash",
            """{"command":"pwsh -File scripts/card.ps1 get CARD-0001"}""")
            .IsSourceRead.ShouldBeFalse();
        OrchestratorInvestigationDetector.ClassifyCall(
            "Bash",
            """{"command":"dotnet build server/Antiphon.Server.csproj"}""")
            .IsSourceRead.ShouldBeFalse();
    }

    [Test]
    public async Task Counts_Read_of_a_source_file_including_unparsed_JSON()
    {
        await Task.CompletedTask;
        OrchestratorInvestigationDetector.ClassifyCall(
            "Read",
            """{"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs"}""")
            .IsSourceRead.ShouldBeTrue();
        OrchestratorInvestigationDetector.ClassifyCall(
            "Read",
            """{"__unparsedToolInput":{"raw":"{\"file_path\": \"C:\\\\src\\\\Antiphon\\\\server\\\\Application\\\\Services\\\\AgentTuiLaunchResolver.cs\", \"offset\": 30, 80}"}}""")
            .IsSourceRead.ShouldBeTrue();
    }

    [Test]
    public async Task Codex_and_Grok_file_read_vocabulary_is_classified_the_same_way()
    {
        await Task.CompletedTask;
        OrchestratorInvestigationDetector.ClassifyCall(
            "read_file",
            """{"target_file":"C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs"}""")
            .IsSourceRead.ShouldBeTrue();
        OrchestratorInvestigationDetector.ClassifyCall(
            "run_terminal_command",
            """{"command":"grep -n ModelTier server/Application/Services/AgentLaunchSpec.cs"}""")
            .IsSourceRead.ShouldBeTrue();
        OrchestratorInvestigationDetector.ClassifyCall(
            "grep_search",
            """{"pattern":"class Foo","path":"server/"}""")
            .IsSourceRead.ShouldBeTrue();
        OrchestratorInvestigationDetector.ClassifyCall(
            "run_terminal_command",
            """{"command":"git status --short"}""")
            .IsSourceRead.ShouldBeFalse();
    }

    [Test]
    public async Task IdentifiersFromText_picks_CARD_paths_basenames_and_PascalCase()
    {
        await Task.CompletedTask;
        var ids = OrchestratorInvestigationDetector.IdentifiersFromText(
            "Fixed AgentControlService.cs (class AgentControlService) for CARD-0246");
        ids.ShouldContain("CARD-0246");
        ids.ShouldContain("AgentControlService.cs");
        ids.ShouldContain("AgentControlService");
    }

    [Test]
    [Arguments("cefed08a-cold-assignment-policy.jsonl")]
    [Arguments("cefed08a-cold-cleanup-script.jsonl")]
    [Arguments("cefed08a-cold-reply-service.jsonl")]
    public async Task Other_real_cold_runs_find_one_investigation_run_of_at_least_R(string file)
    {
        await Task.CompletedTask;
        var events = ParseJsonl(await File.ReadAllTextAsync(Path.Combine(FixturesDir, file)));
        var runs = OrchestratorInvestigationDetector.FindRuns(events);
        runs.ShouldHaveSingleItem();
        runs[0].ReadCount.ShouldBeGreaterThanOrEqualTo(OrchestratorInvestigationDetector.R);
    }

    [Test]
    public async Task Card0246_real_tail_finds_one_run_with_the_measured_source_reads()
    {
        await Task.CompletedTask;
        var events = ParseJsonl(await File.ReadAllTextAsync(
            Path.Combine(FixturesDir, "cefed08a-card0246.jsonl")));
        var sourceReads = events.Where(e => e.Kind == OrchestratorInvestigationDetector.EventKind.SourceRead)
            .ToList();
        // S1: 10 source reads in the CARD-0246 run (the plan's "8" predated counting grep -rn).
        sourceReads.Count.ShouldBeGreaterThanOrEqualTo(8);

        var runs = OrchestratorInvestigationDetector.FindRuns(events);
        runs.ShouldHaveSingleItem("exactly one investigation run in the CARD-0246 tail");
        runs[0].ReadCount.ShouldBeGreaterThanOrEqualTo(8);
        runs[0].Files.Count.ShouldBeGreaterThanOrEqualTo(4);
        runs[0].Nudged.ShouldBeFalse("the fixture predates the S2 hook");
        var message = OrchestratorInvestigationDetector.FormatMessage(runs[0]);
        message.ShouldContain("reads over");
        message.ShouldContain("files, no dispatch; nudged=no");
    }

    [Test]
    public async Task Does_not_flag_when_the_read_names_a_file_the_last_report_named()
    {
        await Task.CompletedTask;
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>
        {
            Report(1, "[task abcdef12 done] Updated AgentControlService.cs and covered AgentControlServiceTests"),
        };
        for (var i = 0; i < 30; i++)
            events.Add(Other(2 + i, "Bash", "git status"));
        events.Add(Read(40, @"C:\src\Antiphon\server\Application\Services\AgentControlService.cs"));
        events.Add(Read(41, @"C:\src\Antiphon\tests\Antiphon.Tests\Application\AgentControlServiceTests.cs"));
        events.Add(Read(42, @"C:\src\Antiphon\server\Application\Services\AgentControlService.cs"));

        OrchestratorInvestigationDetector.FindRuns(events).ShouldBeEmpty();
    }

    [Test]
    public async Task Does_not_flag_three_unnamed_reads_within_N_report_of_a_delegate_report()
    {
        await Task.CompletedTask;
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>
        {
            Report(1, "[task abcdef12 done] shipped CARD-0110 S2 migrate-once template"),
            Read(2, @"C:\src\Antiphon\server\Application\Services\UnrelatedAlpha.cs"),
            Read(3, @"C:\src\Antiphon\server\Application\Services\UnrelatedBeta.cs"),
            Read(4, @"C:\src\Antiphon\server\Application\Services\UnrelatedGamma.cs"),
        };
        OrchestratorInvestigationDetector.FindRuns(events).ShouldBeEmpty();
    }

    [Test]
    public async Task Does_not_flag_three_reads_within_N_dispatch_of_a_delegate_launch()
    {
        await Task.CompletedTask;
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>
        {
            Dispatch(1, "pwsh -File scripts/delegate.ps1 -Role Debug -Title \"look at Foo\""),
            Read(2, @"C:\src\Antiphon\server\Application\Services\UnrelatedAlpha.cs"),
            Read(3, @"C:\src\Antiphon\server\Application\Services\UnrelatedBeta.cs"),
            Read(4, @"C:\src\Antiphon\server\Application\Services\UnrelatedGamma.cs"),
        };
        OrchestratorInvestigationDetector.FindRuns(events).ShouldBeEmpty();
    }

    [Test]
    public async Task Flags_a_synthetic_cold_run_of_exactly_R_source_reads_after_a_human_prompt()
    {
        await Task.CompletedTask;
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>
        {
            Human(1, "Please look into why launches fail"),
            Read(2, @"C:\src\Antiphon\server\Application\Services\Foo.cs"),
            Read(3, @"C:\src\Antiphon\server\Application\Services\Bar.cs"),
            Read(4, @"C:\src\Antiphon\server\Application\Services\Baz.cs"),
        };
        var runs = OrchestratorInvestigationDetector.FindRuns(events);
        runs.ShouldHaveSingleItem();
        runs[0].ReadCount.ShouldBe(3);
        runs[0].StartSequence.ShouldBe(2);
    }

    [Test]
    public async Task Nudged_is_yes_when_the_marker_appears_in_the_run_window()
    {
        await Task.CompletedTask;
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>
        {
            Human(1, "look into this"),
            Read(2, @"C:\src\Antiphon\server\Foo.cs"),
            Read(3, @"C:\src\Antiphon\server\Bar.cs"),
            Read(4, @"C:\src\Antiphon\server\Baz.cs",
                text: OrchestratorInvestigationDetector.NudgeMarker + " This is the 3rd consecutive"),
        };
        var runs = OrchestratorInvestigationDetector.FindRuns(events);
        runs.ShouldHaveSingleItem().Nudged.ShouldBeTrue();
        OrchestratorInvestigationDetector.FormatMessage(runs[0]).ShouldContain("nudged=yes");
    }

    private static List<OrchestratorInvestigationDetector.ClassifiedEvent> ParseJsonl(string tail)
    {
        var events = new List<OrchestratorInvestigationDetector.ClassifiedEvent>();
        long seq = 0;
        foreach (var line in tail.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            JsonElement rec;
            try { rec = JsonDocument.Parse(line).RootElement.Clone(); }
            catch (JsonException) { continue; }

            var type = rec.TryGetProperty("type", out var t) ? t.GetString() : null;
            DateTime? ts = null;
            if (rec.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.String
                && DateTime.TryParse(tsEl.GetString(), out var parsed))
                ts = parsed.ToUniversalTime();

            if (type == "assistant")
            {
                if (!rec.TryGetProperty("message", out var msg)
                    || !msg.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind != JsonValueKind.Object)
                        continue;
                    if ((block.TryGetProperty("type", out var bt) ? bt.GetString() : null) != "tool_use")
                        continue;
                    var name = block.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var input = block.TryGetProperty("input", out var inputEl)
                        ? inputEl.GetRawText()
                        : null;
                    var call = OrchestratorInvestigationDetector.ClassifyCall(name, input);
                    events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                        call.Kind, ++seq, ts, id, name,
                        OrchestratorInvestigationDetector.IdentifiersFromCall(name, input)));
                }
            }
            else if (type == "user")
            {
                if (IsToolResultOnly(rec))
                    continue;
                var text = UserText(rec);
                if (OrchestratorInvestigationDetector.IsReportText(text))
                {
                    events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                        OrchestratorInvestigationDetector.EventKind.Report, ++seq, ts, null, null,
                        OrchestratorInvestigationDetector.IdentifiersFromText(text), text));
                }
                else if (IsHumanRecord(rec, text))
                {
                    events.Add(new OrchestratorInvestigationDetector.ClassifiedEvent(
                        OrchestratorInvestigationDetector.EventKind.Human, ++seq, ts, null, null,
                        OrchestratorInvestigationDetector.IdentifiersFromText(text), text));
                }
            }
        }

        return events;
    }

    private static bool IsHumanRecord(JsonElement rec, string text)
    {
        if (rec.TryGetProperty("origin", out var origin)
            && origin.ValueKind == JsonValueKind.Object
            && origin.TryGetProperty("kind", out var kind)
            && kind.GetString() == "human")
            return true;
        return OrchestratorInvestigationDetector.IsHumanPrompt(text);
    }

    private static bool IsToolResultOnly(JsonElement rec)
    {
        if (!rec.TryGetProperty("message", out var msg)
            || !msg.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array
            || content.GetArrayLength() == 0)
            return false;
        return content.EnumerateArray().All(b =>
        {
            var t = b.TryGetProperty("type", out var bt) ? bt.GetString() : null;
            return t is "tool_result" or "thinking";
        });
    }

    private static string UserText(JsonElement rec)
    {
        if (!rec.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content))
            return "";
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join('\n', content.EnumerateArray()
            .Where(b => (b.TryGetProperty("type", out var t) ? t.GetString() : null) == "text")
            .Select(b => b.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : ""));
    }

    private static OrchestratorInvestigationDetector.ClassifiedEvent Read(
        long seq, string path, string? text = null) =>
        new(OrchestratorInvestigationDetector.EventKind.SourceRead, seq, DateTime.UtcNow,
            $"toolu_{seq}", "Read",
            OrchestratorInvestigationDetector.IdentifiersFromCall("Read",
                JsonSerializer.Serialize(new { file_path = path })),
            text);

    private static OrchestratorInvestigationDetector.ClassifiedEvent Other(long seq, string name, string command) =>
        new(OrchestratorInvestigationDetector.EventKind.OtherTool, seq, DateTime.UtcNow,
            $"toolu_{seq}", name, [], command);

    private static OrchestratorInvestigationDetector.ClassifiedEvent Dispatch(long seq, string command) =>
        new(OrchestratorInvestigationDetector.EventKind.Dispatch, seq, DateTime.UtcNow,
            $"toolu_{seq}", "PowerShell", [], command);

    private static OrchestratorInvestigationDetector.ClassifiedEvent Report(long seq, string text) =>
        new(OrchestratorInvestigationDetector.EventKind.Report, seq, DateTime.UtcNow, null, null,
            OrchestratorInvestigationDetector.IdentifiersFromText(text), text);

    private static OrchestratorInvestigationDetector.ClassifiedEvent Human(long seq, string text) =>
        new(OrchestratorInvestigationDetector.EventKind.Human, seq, DateTime.UtcNow, null, null, [], text);
}
