using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class ClaudeEffortPromptTests
{
    [Test, Arguments(0), Arguments(1), Arguments(2)]
    public void Captured_screens_are_effort_choices(int capture)
    {
        var screen = EffortTestScreen.Capture(capture);
        ClaudeBlockingPromptDetector.Detect(screen)!.Kind.ShouldBe(ClaudeBlockingPromptKind.EffortChoice);
        ClaudeBlockingPromptDetector.IsBlocked(screen).ShouldBeTrue();
        ClaudeEffortPrompt.Parse(screen).ShouldBe(new("Fable 5.1", "xhigh", "high", ClaudeEffortOption.Keep));
    }

    [Test, Arguments("marker"), Arguments("CRLF"), Arguments("borders"), Arguments("case-space"), Arguments("wrapped-question"), Arguments("wrapped-switch")]
    public void Synthetic_layout_variants_preserve_option_structure(string variant)
    {
        var screen = EffortTestScreen.Capture(0);
        screen = variant switch
        {
            "marker" => screen.Replace("> Keep", "❯ Keep"),
            "CRLF" => screen.Replace("\n", "\r\n"),
            "borders" => string.Join("\n", screen.Split('\n').Select(r => "│ " + r + " │")),
            "case-space" => screen.Replace("Use Fable", "USE   Fable").Replace("Keep xhigh", "KEEP   XHIGH"),
            "wrapped-question" => screen.Replace("Fable 5.1 at", "Fable 5.1\n at"),
            _ => screen.Replace("to high effort", "to\n high effort")
        };
        var menu = ClaudeEffortPrompt.Parse(screen);
        menu.ShouldNotBeNull();
        menu.ShouldBe(new("Fable 5.1", "xhigh", "high", ClaudeEffortOption.Keep));
    }

    [Test, Arguments("separate"), Arguments("equals"), Arguments("uppercase"), Arguments("duplicate"), Arguments("absent"), Arguments("prose"), Arguments("terminator")]
    public void Launch_effort_reader_preserves_explicit_intent(string row)
    {
        string[] args = row switch
        {
            "separate" => ["--effort", "high"], "equals" => ["--effort=high"],
            "uppercase" => ["--effort", "HIGH"], "duplicate" => ["--effort", "high", "--effort=high"],
            "prose" => ["--append-system-prompt", "text --effort high"],
            "terminator" => ["--", "--effort", "high"], _ => []
        };
        ClaudeEffortIntent.Read(args).ShouldBe(new(row is "absent" or "prose" or "terminator" ? null : "high"));
    }

    [Test, Arguments("low"), Arguments("medium"), Arguments("high"), Arguments("xhigh"), Arguments("max"), Arguments("switch"), Arguments("absent"), Arguments("equal")]
    public void Requested_effort_selects_the_matching_option(string row)
    {
        var menu = new ClaudeEffortMenu("Nimble 9", row is "switch" or "absent" or "equal" ? "xhigh" : row,
            row == "high" ? "low" : row == "equal" ? "xhigh" : "high", ClaudeEffortOption.Switch);
        menu.Select(new(row == "absent" ? null : row == "switch" ? "high" : row == "equal" ? "xhigh" : row))
            .ShouldBe(row == "switch" ? ClaudeEffortOption.Switch : ClaudeEffortOption.Keep);
    }

    [Test, Arguments("missing"), Arguments("empty"), Arguments("unsupported"), Arguments("conflict"), Arguments("unmatched")]
    public async Task Ambiguous_or_unmatched_intent_types_nothing(string row)
    {
        var fake = new EffortTestScreen();
        string[] args = row switch { "missing" => ["--effort"], "empty" => ["--effort", ""],
            "unsupported" => ["--effort", "bogus"], "conflict" => ["--effort=high", "--effort=xhigh"], _ => ["--effort=max"] };
        var result = await fake.ResolveAsync(args);
        result.Cleared.ShouldBeFalse(fake.Evidence);
        fake.Writes.ShouldBeEmpty(fake.Evidence);
        fake.AppliedEffort.ShouldBeNull();
    }

    [Test, Arguments("Fable 5.1", "xhigh", "xhigh", 2, 1), Arguments("Nimble 9", "medium", "medium", 2, 1), Arguments("Fable 5.1", "xhigh", "high", 1, 2)]
    public async Task Requested_effort_is_applied(string model, string current, string requested, int highlight, int accepted)
    {
        var fake = new EffortTestScreen { Model = model, Current = current, Highlight = highlight };
        var result = await fake.ResolveAsync(["--effort", requested]);
        result.Cleared.ShouldBeTrue(fake.Evidence);
        fake.AppliedEffort.ShouldBe(requested, fake.Evidence);
        fake.AcceptedOption.ShouldBe(accepted);
        fake.Writes[0].Key.ShouldBe("j");
        fake.Writes.First(w => w.Key == "\r").Highlight.ShouldBe(accepted);
        ClaudeEffortPrompt.CurrentEffort(fake.Screen).ShouldBe(requested);
        fake.ClearObservations.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test, Arguments("immovable"), Arguments("missing"), Arguments("duplicate")]
    public async Task An_unmovable_wrong_highlight_withholds_Enter(string row)
    {
        var fake = new EffortTestScreen { Highlight = 2, MovesOn = null };
        if (row == "missing") fake.Highlight = 0;
        if (row == "duplicate") { fake.Highlight = 1; fake.Template = fake.Template.Replace("     Switch", "   > Switch"); }
        (await fake.ResolveAsync()).Cleared.ShouldBeFalse(fake.Evidence);
        fake.Writes.Select(w => w.Key).ShouldNotContain("\r", fake.Evidence);
        fake.AppliedEffort.ShouldBeNull();
    }

    [Test, Arguments("permission"), Arguments("different-effort")]
    public async Task A_changed_dialog_identity_stops_further_input(string row)
    {
        var fake = new EffortTestScreen { Highlight = 2 };
        var armed = false;
        fake.AfterWrite = (f, key) => { if (key == "j") armed = true; };
        var seenSelected = false;
        fake.OnSnapshot = f =>
        {
            if (!armed) return;
            if (!seenSelected) { seenSelected = true; return; }
            if (row == "permission") f.Override = "Do you want to proceed?\n1. Yes\n2. No";
            else f.Model = "Nimble 9";
        };
        (await fake.ResolveAsync()).Cleared.ShouldBeFalse(fake.Evidence);
        fake.Writes.Select(w => w.Key).ShouldBe(["j"], fake.Evidence);
    }

    [Test]
    public async Task Generic_answering_cannot_confirm_an_effort_choice()
    {
        var fake = new EffortTestScreen();
        var result = await ClaudeBlockingPromptDetector.TryAnswerDetailedAsync(fake.SnapshotAsync, fake.WriteAsync,
            ClaudeBlockingPromptDetector.Detect(fake.Screen)!, TimeSpan.FromMilliseconds(100));
        result.Cleared.ShouldBeFalse();
        fake.Writes.ShouldBeEmpty();
    }

    [Test]
    public async Task Malformed_effort_remnants_do_not_count_as_clearance()
    {
        var fake = new EffortTestScreen();
        fake.AfterWrite = (f, key) => { if (key == "\r") f.Override = "Use Fable 5.1 at high effort by default?\n> Keep xhigh\n? for shortcuts"; };
        (await fake.ResolveAsync(budgetMs: 2200)).Cleared.ShouldBeFalse(fake.Evidence);
        fake.TokenWrites.ShouldBe(0);
    }

    [Test]
    public async Task A_swallowed_Enter_is_retried_only_while_the_same_target_is_selected()
    {
        var fake = new EffortTestScreen { SwallowEnters = 1 };
        (await fake.ResolveAsync()).Cleared.ShouldBeTrue(fake.Evidence);
        fake.Writes.Count(w => w.Key == "\r").ShouldBe(2);
        fake.AppliedEffort.ShouldBe("xhigh");
    }

    [Test]
    public async Task Retries_respect_settle_and_attempt_limits()
    {
        var fake = new EffortTestScreen { SwallowEnters = 100 };
        using var cts = new CancellationTokenSource();
        var task = fake.ResolveAsync(budgetMs: 7000, ct: cts.Token);
        try
        {
            (await Task.WhenAny(task, Task.Delay(9000))).ShouldBe(task, "operation completed before watchdog");
            (await task).Cleared.ShouldBeFalse();
            var enters = fake.Writes.Where(w => w.Key == "\r").ToArray();
            enters.Length.ShouldBe(3);
            enters[0].At.TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(1500);
            for (var i = 1; i < enters.Length; i++) (enters[i].At - enters[i - 1].At).TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(1500);
        }
        finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Test, Arguments("1. Yes"), Arguments("2. No"), Arguments("Enter to confirm"), Arguments("Esc to cancel"), Arguments("")]
    public void Legacy_choice_markers_keep_their_existing_classification(string marker) =>
        ClaudeBlockingPromptDetector.Detect("Choose an option?\n" + marker)?.Kind
            .ShouldBe(marker == "" ? null : ClaudeBlockingPromptKind.Choice);

    [Test, Arguments("model"), Arguments("effort"), Arguments("keep"), Arguments("switch"), Arguments("prose"), Arguments("fences"), Arguments("model-list"), Arguments("effort-list"), Arguments("permission")]
    public async Task Unrelated_or_inconsistent_screens_are_not_effort_choices(string row)
    {
        var screen = EffortTestScreen.Capture(0);
        screen = row switch
        {
            "model" => screen.Replace("Switch Fable", "Switch Nimble"),
            "effort" => screen.Replace("to high effort", "to medium effort"),
            "keep" => screen.Replace("> Keep xhigh", ""), "switch" => screen.Replace("Switch Fable 5.1 to high effort", ""),
            "prose" => "The question is Use Fable 5.1 at high effort by default? Keep xhigh and Switch Fable 5.1 to high effort are labels.",
            "fences" => "```\nUse Fable 5.1 at high effort by default?\n```\n```\n> Keep xhigh\nSwitch Fable 5.1 to high effort\n```",
            "permission" => "Do you want to proceed?\n1. Yes",
            _ => "/" + row + "\n> high\n low\nEnter to confirm"
        };
        var prompt = ClaudeBlockingPromptDetector.Detect(screen);
        prompt?.Kind.ShouldNotBe(ClaudeBlockingPromptKind.EffortChoice);
        if (row.EndsWith("list")) prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.Choice);
        if (row == "permission") prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.ToolPermission);
        var writes = new List<string>();
        (await ClaudeEffortPrompt.ResolveAsync(_ => Task.FromResult(screen), (k, _) => { writes.Add(k); return Task.CompletedTask; },
            new("xhigh"), TimeSpan.FromMilliseconds(100), CancellationToken.None)).Cleared.ShouldBeFalse();
        writes.ShouldBeEmpty();
    }
}
