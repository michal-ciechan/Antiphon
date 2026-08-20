using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S1: what a delegate brief and a mid-flight refinement look like when the composer on
/// the other end JOINS the lines we type into it.
///
/// <para>Measured for grok 1.0.5 (<c>GrokCanaryTests</c>, <c>FakeGrokContractTests</c>): every LF
/// is dropped and the lines join with NO separator — 4 450 characters sent, 4 389 recorded, exactly
/// the newline count. Nothing is lost, so delivery verification survives it (CARD-0080 S2's
/// whitespace-free confirm arm). What does not survive is STRUCTURE: the last word of each line
/// grows the first word of the next, and in a brief that is where the correctness hazard lives — a
/// spill path, a test filter or a command silently acquires a suffix.</para>
///
/// <para>These pin the two halves of the fix as arithmetic and rendering; the capstone that drives
/// the same gate through a real ConPTY into fakegrok is
/// <c>SessionMessageQueueGrokPtyIntegrationTests</c>.</para>
/// </summary>
[Category("Unit")]
public class GrokDeliveryShapeTests
{
    private const string NL = "\n";
    private const string AnyReason = "test";

    /// <summary>Where the real gate writes its spill files when it decides to spill.</summary>
    private static readonly Lazy<string> SpillRoot = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "antiphon-card84-grok-shape");
        Directory.CreateDirectory(dir);
        return dir;
    });

    private static AgentTask NewTask(string? goal = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "CARD-0084 delivery shape probe",
        Goal = goal ?? "Line one of the goal.\nLine two of the goal.\nLine three of the goal.",
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Code,
        ModelLevel = AgentModelLevel.High,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = SpillRoot.Value,
        Status = AgentTaskStatus.Dispatched,
    };

    /// <summary>
    /// Grok's join, applied to the body the composer actually receives. The real delivery path
    /// LF-normalizes every body before anything touches the pty
    /// (<see cref="PtyInputEncoding.NormalizeBody"/>, called from
    /// <c>SessionMessageQueueService.DeliverAsync</c>), so the drop has to be simulated over the
    /// NORMALIZED form. Dropping LFs from raw text instead leaves a CR standing between the lines
    /// on Windows, where this source file's own CRLF endings reach the un-flattened pointer through
    /// <c>StringBuilder.AppendLine</c> and the raw string literals — an artefact of the build
    /// machine's line endings, which the composer never sees and which made the join look
    /// survivable here while it is not.
    /// </summary>
    private static string AsGrokWouldReceive(string typed) =>
        PtyInputEncoding.NormalizeBody(typed).Replace("\n", "");

    // ---- the ceiling ---------------------------------------------------------------------------

    /// <summary>
    /// The exposure this slice closes. A real brief runs 1.5-6 KB and the modern backend's inline
    /// ceiling is 43 200 bytes, so before this card EVERY brief to a Grok delegate was typed inline
    /// — and arrived as one run-on line. The kind, not the backend, is what decides it.
    /// </summary>
    [Test]
    public void A_composer_that_joins_typed_lines_spills_a_brief_the_backend_would_have_typed_inline()
    {
        var settings = new DelegationSettings();
        var task = NewTask(string.Join("\n", Enumerable.Range(0, 40).Select(i => $"goal line {i:D4}")));
        var modern = settings.CeilingsFor(PtyBackend.ModernConPty, AnyReason);

        var forClaude = AgentTaskDispatcher.FitBriefForTyping(task, settings, modern, null, AgentKind.ClaudeCode);
        var forGrok = AgentTaskDispatcher.FitBriefForTyping(task, settings, modern, null, AgentKind.Grok);

        forClaude.ShouldContain("goal line 0039",
            customMessage: "the backend alone would have typed this brief inline — that is the payoff CARD-0037 bought");
        forGrok.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE",
            customMessage: "and for a joining composer the same brief must take the spill path instead");
        forGrok.ShouldNotContain("goal line 0039",
            customMessage: "the body itself must not be typed at all");
    }

    /// <summary>
    /// Only the INLINE ceiling moves. The other two are properties of the pseudoconsole — what the
    /// transport was measured to carry whole, and how large a report may be forwarded back — and
    /// Grok has no CARD-0027 clip mode at all, so narrowing them would raise oversize incidents
    /// about a transport that is fine.
    /// </summary>
    [Test]
    [Arguments(PtyBackend.InboxConhost)]
    [Arguments(PtyBackend.ModernConPty)]
    public void Narrowing_for_a_joining_composer_touches_the_inline_ceiling_and_nothing_else(PtyBackend backend)
    {
        var baseline = new DelegationSettings().CeilingsFor(backend, AnyReason);

        var narrowed = baseline.ForAgentKind(AgentKind.Grok);

        narrowed.BriefInlineMaxBytes.ShouldBe(0, "every brief and refinement must spill");
        narrowed.SingleWriteMaxBytes.ShouldBe(baseline.SingleWriteMaxBytes);
        narrowed.ReplyInlineMaxChars.ShouldBe(baseline.ReplyInlineMaxChars);
        narrowed.Backend.ShouldBe(baseline.Backend);
        narrowed.Reason.ShouldContain("Grok", customMessage: "a 0 ceiling in a log line has to explain itself");
    }

    /// <summary>
    /// Claude delivery is untouched — by value, not by inspection. The one kind whose composer HAS
    /// been measured to keep the newlines we type gets exactly the ceilings the backend resolved.
    /// </summary>
    [Test]
    public void The_one_measured_keeps_newlines_kind_gets_the_backends_ceilings_unchanged()
    {
        var baseline = new DelegationSettings().CeilingsFor(PtyBackend.ModernConPty, AnyReason);

        baseline.ForAgentKind(AgentKind.ClaudeCode).ShouldBe(baseline);
        PtyDeliveryCeilings.RequiresJoinSafeDelivery(AgentKind.ClaudeCode).ShouldBeFalse();
    }

    /// <summary>
    /// CARD-0099 S2: the gate is DEFAULT-DENY. A kind whose composer nobody has measured spills its
    /// brief for the same reason Grok's does — not because it is known to join, but because it is
    /// not known not to, and a fused command line in a Code delegate's brief is a silent, expensive
    /// failure. Codex is the kind this was widened for; OpenCode and Raw come along because the rule
    /// is "measured or spilled", not a per-kind allowlist somebody has to remember to extend.
    ///
    /// <para>Measurement can only ever move a kind OUT of this set, by naming it in
    /// <see cref="PtyDeliveryCeilings.RequiresJoinSafeDelivery"/> with a canary behind it — see
    /// <c>CodexCanaryTests</c>. Adding a kind to the enum must not need an edit here to be safe.</para>
    /// </summary>
    [Test]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Raw)]
    public void An_unmeasured_kind_is_join_safe_by_default(AgentKind kind)
    {
        var baseline = new DelegationSettings().CeilingsFor(PtyBackend.ModernConPty, AnyReason);

        PtyDeliveryCeilings.RequiresJoinSafeDelivery(kind).ShouldBeTrue(
            "a composer nobody has put a canary on is assumed to join");

        var narrowed = baseline.ForAgentKind(kind);
        narrowed.BriefInlineMaxBytes.ShouldBe(0, "every brief and refinement must spill");
        narrowed.SingleWriteMaxBytes.ShouldBe(baseline.SingleWriteMaxBytes);
        narrowed.ReplyInlineMaxChars.ShouldBe(baseline.ReplyInlineMaxChars);
        narrowed.Reason.ShouldContain("no measured composer contract",
            customMessage: "a 0 ceiling that is a default, not a measurement, has to say so in the log line");
    }

    /// <summary>
    /// The exposure CARD-0099 S2 closes, stated the way the Grok one above is: a real brief runs
    /// 1.5-6 KB and the modern backend's inline ceiling is 43 200 bytes, so before this EVERY brief
    /// to a Codex delegate would have been typed inline into a composer with no measured contract.
    /// </summary>
    [Test]
    public void A_codex_brief_spills_where_the_backend_alone_would_have_typed_it_inline()
    {
        var settings = new DelegationSettings();
        var task = NewTask(string.Join(NL, Enumerable.Range(0, 40).Select(i => $"goal line {i:D4}")));
        var modern = settings.CeilingsFor(PtyBackend.ModernConPty, AnyReason);

        var forCodex = AgentTaskDispatcher.FitBriefForTyping(task, settings, modern, null, AgentKind.Codex);

        forCodex.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
        forCodex.ShouldNotContain("goal line 0039", customMessage: "the body itself must not be typed at all");
        forCodex.ShouldNotContain(NL, customMessage: "and the pointer that replaces it is flattened join-safe");
    }

    // ---- the pointer ---------------------------------------------------------------------------

    /// <summary>
    /// The bug the rendering exists to prevent, stated as the composer would state it. This is the
    /// exact fusion named in the plan: the spill path grows the next line's first word, so a
    /// delegate that reads the pointer literally looks for a file that does not exist.
    /// </summary>
    [Test]
    public void The_default_pointer_fuses_the_spill_path_with_the_next_line_when_joined()
    {
        var task = NewTask();
        var spill = Path.Combine("C:", "src", "antiphon", ".antiphon", "task-7f3a2b91-brief.md");

        var joined = AsGrokWouldReceive(DelegationReportFormatter.BuildBriefPointer(
            task, new DelegationSettings(), spill, fullLength: 5_203, AgentKind.ClaudeCode));

        joined.ShouldContain("brief.mdEverything you need is there",
            customMessage: "without the join-safe rendering the path is unreadable — this is the defect");
    }

    /// <summary>
    /// And the fix: we perform the join ourselves, with a space, so the composer's join is a no-op.
    /// Everything correlation and comprehension depend on survives — the marker at each end, the
    /// path delimited from what follows it, and the path QUOTED so a directory containing a space
    /// is still one token on a line that is now all one line.
    /// </summary>
    [Test]
    public void A_join_safe_pointer_is_already_one_line_and_keeps_the_path_delimited()
    {
        var task = NewTask();
        var spill = Path.Combine("C:", "Program Files", "antiphon", ".antiphon", "task-7f3a2b91-brief.md");
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        var pointer = DelegationReportFormatter.BuildBriefPointer(
            task, new DelegationSettings(), spill, fullLength: 5_203, AgentKind.Grok);

        pointer.ShouldNotContain("\n", customMessage: "we choose the separator, so there is nothing left to drop");
        AsGrokWouldReceive(pointer).ShouldBe(pointer, customMessage: "the composer's join must be a no-op");
        pointer.ShouldStartWith(marker);
        pointer.ShouldEndWith(marker, customMessage: "the tail marker is the fragment that survives every measured loss");
        pointer.ShouldContain($"'{spill}' Everything you need is there",
            customMessage: "quoted AND separated: a path with a space in it is still one token");
        pointer.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
        pointer.ShouldContain("5,203 characters");
    }

    /// <summary>
    /// The refinement pointer takes the same treatment, for the same reason: it too names a file and
    /// carries the marker its correlation depends on.
    /// </summary>
    [Test]
    public void A_join_safe_refinement_pointer_keeps_its_markers_and_its_path()
    {
        var task = NewTask();
        var spill = Path.Combine("C:", "src", "antiphon", ".antiphon", "task-7f3a2b91-refinement-20260818120000.md");
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        var pointer = DelegationReportFormatter.BuildRefinementPointer(
            task, new DelegationSettings(), spill, fullLength: 4_100, AgentKind.Grok);

        pointer.ShouldNotContain("\n");
        pointer.ShouldStartWith($"{marker} REFINEMENT");
        pointer.ShouldEndWith(marker);
        pointer.ShouldContain($"'{spill}' Your brief stands except as amended there",
            customMessage: "the path must not grow the sentence that follows it");
    }

    /// <summary>
    /// With no file written the pointer falls back to the API, which needs no filesystem — and the
    /// fallback has to survive the join too, since it is the arm that runs when the workspace is
    /// unwritable, i.e. exactly when the delegate can least afford an unreadable instruction.
    /// </summary>
    [Test]
    public void The_api_fallback_survives_the_join()
    {
        var task = NewTask();

        var pointer = DelegationReportFormatter.BuildBriefPointer(
            task, new DelegationSettings(), spillPath: null, fullLength: 5_203, AgentKind.Grok);

        pointer.ShouldNotContain("\n");
        pointer.ShouldContain($"/api/agent-tasks/{task.Id} and read the \"goal\" field Everything you need is there",
            customMessage: "the URL must not grow the next sentence either");
    }

    /// <summary>
    /// Rendering for a non-joining kind is byte-identical to what shipped — the parameter's default
    /// is what every existing caller passes, and this is what makes "keep non-Grok delivery
    /// unchanged" a fact rather than a claim.
    /// </summary>
    [Test]
    public void A_non_joining_pointer_is_byte_identical_to_the_shipped_rendering()
    {
        var task = NewTask();
        var settings = new DelegationSettings();
        var spill = Path.Combine("C:", "src", "antiphon", ".antiphon", "task-7f3a2b91-brief.md");

        DelegationReportFormatter.BuildBriefPointer(task, settings, spill, 5_203, AgentKind.ClaudeCode)
            .ShouldBe(DelegationReportFormatter.BuildBriefPointer(task, settings, spill, 5_203));
        DelegationReportFormatter.BuildRefinementPointer(task, settings, spill, 4_100, AgentKind.ClaudeCode)
            .ShouldBe(DelegationReportFormatter.BuildRefinementPointer(task, settings, spill, 4_100));
    }

    /// <summary>
    /// Flattening is for POINTERS only, never for a body: a brief travels as a file precisely
    /// because collapsing its structure is the damage being avoided. This pins that the spilled file
    /// keeps every line the caller wrote.
    /// </summary>
    [Test]
    public void The_spilled_brief_keeps_the_structure_the_pointer_gave_up()
    {
        var settings = new DelegationSettings();
        var task = NewTask("HEAD-MARKER\nrun: dotnet run --project tests/Antiphon.Tests\nTAIL-MARKER");

        AgentTaskDispatcher.FitBriefForTyping(
            task, settings, settings.CeilingsFor(PtyBackend.ModernConPty, AnyReason), null, AgentKind.Grok);

        var spill = Path.Combine(
            task.WorkingDirectory, ".antiphon",
            $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
        var spilled = File.ReadAllText(spill);

        spilled.ShouldContain("HEAD-MARKER\nrun: dotnet run --project tests/Antiphon.Tests\nTAIL-MARKER",
            customMessage: "the file is the whole point — its newlines are still there");
        AsGrokWouldReceive(spilled).ShouldNotBe(spilled,
            customMessage: "and typing this body is exactly what would have destroyed it");
    }
}
