using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0710 V-12 and V-17. Stage bundles and owner docs name the platform contract.</summary>
[Category("Unit")]
public sealed class TaskPlatformGuidanceTests
{
    [Test]
    public void Stage_bundles_leave_twenty_characters_below_the_size_cap()
    {
        foreach (var name in new[] { "stage-investigate.md", "stage-plan.md", "stage-test-design.md", "stage-code.md", "stage-mutation.md", "stage-review.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", name))
                .Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            text.Length.ShouldBeLessThanOrEqualTo(2_480, name);
        }
    }

    [Test]
    public void Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes()
    {
        foreach (var name in new[] { "stage-code.md", "stage-review.md", "stage-mutation.md", "stage-plan.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", name));
            text.ShouldContain("Omit -Runner unless pinning one host.");
            text.ShouldContain("Omit -Platform unless OS needed");
            text.ShouldContain("-Platform Any unpins.");
            text.ShouldContain("GET /api/runner-defaults");
            text.ShouldContain("/api/session-runners");
            text.ShouldNotContain("pass -Platform Any explicitly");
            if (name is "stage-code.md" or "stage-mutation.md")
                text.ShouldContain("Do not embed a fleet location.");
        }

        var orchestrator = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("inherits its predecessor's platform");
        orchestrator.ShouldContain("inherits the card's platform");
        orchestrator.ShouldContain("unpinned (Any)");
        orchestrator.ShouldContain("runtime default places it");
        orchestrator.ShouldContain("pass -Platform Any explicitly");
        orchestrator.ShouldContain("only when that piece of work requires it");
        orchestrator.ShouldContain("scope a platform-pinned task to just the OS-specific part");
        orchestrator.ShouldContain("habit, a stage name");
        orchestrator.ShouldContain("Normally omit -Runner; the runtime default places the task.");
        orchestrator.ShouldContain("To unpin a stage on a pinned card");
        orchestrator.ShouldContain("with its own filter and budget; leave the rest unpinned");
        orchestrator.ShouldContain("Do not embed a fleet location.");
    }

    [Test]
    public void Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "orchestration-loop.md"));
        text.ShouldContain("a follow-up inherits its predecessor's platform");
        text.ShouldContain("a stage inherits the card's platform");
        text.ShouldContain("else the task is unpinned (`Any`)");
        text.ShouldContain("pass `-Platform Any` explicitly");
        text.ShouldContain("Pass a specific platform only");
        text.ShouldContain("scope a platform-pinned task to just the OS-specific part");
        text.ShouldContain("Normally omit `-Runner`; the runtime default places the task.");
        text.ShouldContain("the runtime default places");
        text.ShouldContain("To unpin a stage on a pinned card");
        text.ShouldContain("Habit, a stage name");
        text.ShouldContain("with its own filter and budget; leave the rest unpinned");
    }

    [Test]
    public void Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "stage-review.md"));
        text.ShouldContain("self-compare, constant, no outcome assertion");
        text.ShouldContain("Review build vs plan.");
        text.ShouldContain("empty variables exit nonzero");
        text.ShouldContain("quote expansions");
        text.ShouldContain("resolve target in scratch root");
        text.ShouldContain("Full only when the whole required selection ran.");
        text.ShouldContain("bare, unfenced, unindented, unquoted lines");
        text.ShouldContain("Carry original Code landing owner.");
        text.ShouldContain("adopt: source");
        text.ShouldContain("Review ID/`-StartRef` cannot name source");
        foreach (var flag in new[] { "-Land <owner>", "-FromTask <source>", "-ExpectedSourceSha <sha>",
                     "-ReviewEvidenceId <evidence>", "-RecoverReviewedSource", "-Card", "-StartRef" })
            text.ShouldContain(flag);
        var ownerDoc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "orchestration-loop.md"));
        ownerDoc.ShouldContain("| Adopt another eligible task's reviewed branch | Source task named by `-FromTask` |");
        ownerDoc.ShouldContain("A follow-up Review must name its `FollowUpOfTaskId`");
        text.ShouldContain("GET /api/runner-defaults");
        text.ShouldContain("GET /api/session-runners");
        text.ShouldContain("embed no fleet location");
        text.ShouldContain("the brief's verification profile governs");
        text.ShouldContain("V/R evidence via real queue");
        text.ShouldContain("Require matching complete UserPrompt transcript");
        text.ShouldContain("Re-run claimed Unit + named affected integration classes");
        text.ShouldContain("Reject missing tests or evidence.");
        text.ShouldContain("code when there are defects (name them in handoff:)");
    }

    [Test]
    public void Review_guidance_preserves_every_instruction()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "stage-review.md"));
        AssertBaseInstructions(text);

        // Temporarily remove a base instruction in memory. The production assertion must go red;
        // the file remains intact for other Unit tests and for the checkpoint's bundle parser.
        var missingCheckpointInstruction = text.Replace(
            "use it for repeated class runs.", "", StringComparison.Ordinal);
        missingCheckpointInstruction.ShouldNotBe(text);
        Should.Throw<ShouldAssertException>(() => AssertBaseInstructions(missingCheckpointInstruction))
            .Message.ShouldContain("Use checkpoint tool for repeated class runs");
    }

    private static void AssertBaseInstructions(string text)
    {
        // Checklist mechanically transcribed from each instruction in
        // `git show 16da56da5:server/Bundles/stage-review.md`. Left is the base instruction;
        // right is its required place in the current bundle. The subject placeholder is mapped
        // to the reviewed source identity under CARD-0807 D-3.
        foreach (var (baseInstruction, currentPin) in new (string Base, string Pin)[]
        {
            // Base line 1: role.
            ("reviewing the build against its plan", "Review build vs plan."),
            // Base line 3: scope, checkpoint audit, and every defect case.
            ("Unit plus named affected integration classes", "Re-run claimed Unit + named affected integration classes"),
            ("one checkpoint-tool run", "in one checkpoint-tool run"),
            ("Executed PCs are not a prerequisite", "Executed PCs are not a prerequisite."),
            ("Code report's CP-n lines against plan table", "CP-n lines vs ### Checkpoints:"),
            ("missing row", "missing row"),
            ("zero count", "zero count"),
            ("unlisted build/test run without a reason", "unlisted build/test run without a reason"),
            ("build or test driver outside the slot gate", "build or test driver outside the slot gate"),
            ("broad run without named invariant/cost", "broad run without named invariant/cost"),
            ("new test that cannot go red", "test cannot go red (self-compare, constant, no outcome assertion)"),
            // Base line 5: repeated classes and destructive cleanup.
            ("Use checkpoint tool for repeated class runs", "use it for repeated class runs."),
            ("reject empty variables before rm", "empty variables exit nonzero"),
            ("quote expansions before rm", "quote expansions"),
            ("confine resolved target to scratch root", "resolve target in scratch root"),
            // Base line 7: round and evidence floor.
            ("brief's verification profile governs", "the brief's verification profile governs."),
            ("Final reruns complete ordinary scope", "A Final Review reruns the complete ordinary scope itself"),
            ("include every Interim-deferred row", "including every row an Interim round deferred;"),
            ("Interim never discharges Final", "an Interim pass never discharges it."),
            ("fresh executed identities", "fresh executed identities"),
            ("nonzero counts", "nonzero counts"),
            ("exit 0 is not evidence", "exit 0, --list-tests or missing parameter rows are not evidence."),
            ("--list-tests is not evidence", "--list-tests or missing parameter rows are not evidence."),
            ("missing parameter rows are not evidence", "missing parameter rows are not evidence."),
            ("required manual work stays pending", "Required manual work stays pending"),
            ("nightly never satisfies manual or PC checks", "nightly green never satisfies manual or PC checks."),
            // Base line 9: review invariants and defect format.
            ("Read-only", "Read-only."),
            ("Do not fix anything", "Do not fix anything."),
            ("Check V/R", "Check V/R"),
            ("judge PC evidence read-only; PCs pending", "judge PC evidence read-only (PCs stay pending)"),
            ("Reject missing tests or evidence", "Reject missing tests or evidence."),
            ("Carry original Code landing owner", "Carry original Code landing owner."),
            ("Defects: Where/Failure/Why/Fix", "Defects: Where/Failure/Why/Fix."),
            // Base line 11: producer-to-recipient delivery evidence.
            ("producer", "Audit producer/"),
            ("destination", "/destination/"),
            ("persistence", "/persistence/"),
            ("recovery", "/recovery/"),
            ("observable receipt", "/observable receipt and durable identity."),
            ("durable identity", "observable receipt and durable identity."),
            ("ordinary V/R through real queue", "Trace ordinary V/R evidence via real queue"),
            ("busy and eligible recipients", "busy/eligible"),
            ("crash and enqueue failures", "crash/enqueue"),
            ("matching complete UserPrompt transcript", "matching complete UserPrompt transcript"),
            ("queue insert insufficient", "queue insert/event/Sent flag/ack insufficient."),
            ("event insufficient", "insert/event/Sent flag/ack insufficient."),
            ("Sent flag insufficient", "event/Sent flag/ack insufficient."),
            ("ack insufficient", "Sent flag/ack insufficient."),
            ("missing producer-to-recipient test", "Reject a missing producer-to-recipient test"),
            ("missing recipient evidence", "or recipient evidence"),
            // Base lines 13-22: one bare evidence block and landing identity.
            ("emit one review-evidence block before next-stage", "Emit one review-evidence block as bare, unfenced, unindented, unquoted lines before next-stage:"),
            ("review evidence heading", "--- review evidence ---"),
            ("full subject GUID (D-3: reviewed source)", "subjectTaskId: <full GUID of task whose exact pushed tip was reviewed>"),
            ("full reviewed SHA", "reviewedSourceSha: <full SHA actually reviewed>"),
            ("Full|Interim|None scope", "ordinaryScopeCompleted: <Full|Interim|None>"),
            ("Full only after whole required selection", "Full only when the whole required selection ran."),
            ("original Code owner for ordinary/recovery", "Ordinary/recovery: Code owner"),
            ("caller lands Code owner", "-Land <owner> -FromTask <source>"),
            ("ExpectedSourceSha from evidence", "-ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>"),
            // D-3 additions retain the original owner while identifying an adopted source.
            ("adoption source identity", "adopt: source"),
            ("adoption command", "-Land <owner> -FromTask <source> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>"),
            ("ordinary owner omits FromTask", "owner: no `-FromTask`"),
            ("recovery flag", "recovery: `-RecoverReviewedSource`"),
            ("StartRef cannot identify source", "Review ID/`-StartRef` cannot name source"),
            ("brief names owner and source", "name both."),
            ("explicit card binding", "Use `-Card`"),
            ("follow-up matches subject", "follow-up must match FollowUpOfTaskId"),
            ("otherwise fresh same-card Review", "else fresh same-card Review."),
            // Base line 24: runtime placement.
            ("runner defaults endpoint", "GET /api/runner-defaults"),
            ("session runners endpoint", "GET /api/session-runners"),
            ("embed no fleet location", "embed no fleet location."),
            ("Runner only to pin one host", "Omit -Runner unless pinning one host."),
            ("Platform only when OS needed", "Omit -Platform unless OS needed"),
            ("Platform Any unpins", "-Platform Any unpins."),
            // Base line 26: all next-stage routes.
            ("land for clean Final", "next: land when there are no defects and this was a Final Review;"),
            ("review Final for clean Interim", "review (Final) when a clean Interim;"),
            ("code for defects named in handoff", "code when there are defects (name them in handoff:)"),
            ("decide when a human choice blocks", "decide when a human choice blocks.")
        })
            text.ShouldContain(currentPin, Case.Sensitive, $"Missing base Review instruction: {baseInstruction}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}

[Category("Unit")]
public sealed class RunnerDefaultGuidanceTests
{
    [Test]
    public void Bundles_read_runtime_defaults_without_pinning_location()
    {
        var orchestrator = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("GET /api/runner-defaults");
        orchestrator.ShouldContain("GET /api/session-runners");
        orchestrator.ShouldNotContain("always pass -Runner server2");
    }

    [Test]
    public void Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract()
    {
        var plan = File.ReadAllText(Path.Combine(Root(), "docs", "superpowers", "plans", "2026-09-25-card-0710-task-platform-placement-plan.md"));
        plan.ShouldContain("CP-13");
        plan.ShouldContain("desktop / Windows");
        plan.ShouldContain("| CP-2 |");
        plan.ShouldContain("server2");
        foreach (var name in new[] { "stage-plan.md", "stage-code.md", "stage-review.md", "stage-mutation.md" })
        {
            var text = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", name));
            text.ShouldContain("Omit -Platform unless OS needed");
            text.ShouldContain("-Platform Any unpins.");
            text.ShouldContain("Omit -Runner unless pinning one host.");
            text.Contains("CP-13", StringComparison.Ordinal).ShouldBeFalse(name + " names CP-13");
            text.Contains("server2", StringComparison.Ordinal).ShouldBeFalse(name + " names server2");
        }
        var orchestrator = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("inherits its predecessor's platform");
        orchestrator.ShouldContain("pass -Platform Any explicitly");
        orchestrator.ShouldContain("OS-only probe");
    }

    [Test]
    public void Reroute_diagnostic_names_codex()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "server", "Application", "Services", "AgentTaskService.cs"));
        text.ShouldNotContain("Reroute to Grok or ClaudeCode.");
        text.ShouldContain("Reroute to Grok, ClaudeCode or Codex.");
    }

    [Test]
    public void Legacy_default_is_import_only_in_documented_configuration()
    {
        var settings = File.ReadAllText(Path.Combine(Root(), "server", "Application", "Settings", "DelegationSettings.cs"));
        settings.ShouldContain("import input only");
        var testing = File.ReadAllText(Path.Combine(Root(), "docs", "testing-and-build.md"));
        testing.ShouldContain("Delegation:DefaultRunnerId");
        testing.ShouldContain("import");
        testing.ShouldNotContain("automatic default placement still keeps Codex on the desktop");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
