using Antiphon.Server.Application.Services;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1332. The orchestrator bundle carries short mandatory rules plus owner pointers, and
/// <c>docs/orchestration-loop.md</c> keeps the complete procedures at stable anchors. Every expected
/// sentence here is an independent literal: nothing is read from the plan, the bundle or another
/// test's phrase array, so a relocation that drops a rule fails the assertion that names it.
/// </summary>
[Category("Unit")]
public sealed class OrchestratorBundleOwnershipTests
{
    // ---- V-1: the owner keeps every relocated procedure at a unique, usable anchor ------------

    [Test]
    [Arguments("child")]
    [Arguments("pipeline")]
    [Arguments("verification")]
    [Arguments("placement")]
    public void C1332_OwnersContainRelocatedProcedures(string family)
    {
        var owner = Owner();
        switch (family)
        {
            case "child":
                AssertChildOwner(owner);
                break;
            case "pipeline":
                AssertPipelineOwner(owner);
                break;
            case "verification":
                AssertVerificationOwner(owner);
                break;
            case "placement":
                AssertPlacementOwner(owner);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "unknown relocation family");
        }
    }

    private static void AssertChildOwner(string owner)
    {
        // Window 1: the -StartRef dispatch recipe, from its new anchor to the branch-name section.
        var siblingAt = Anchor(owner, "delegate-start-ref",
            "**Continuing a sibling's work (CARD-0613).**", "c1332-owner-anchor-delegate-start-ref");
        var startRef = Window(owner, siblingAt, "#### Delegate branch names", "c1332-owner-startref");
        startRef.ShouldContain("It requires `-Worktree`", Case.Sensitive, "c1332-owner-startref-worktree");
        var exclusion = Sentence(startRef, "is refused alongside", "c1332-owner-startref-exclusions");
        foreach (var flag in new[] { "-Shared", "-ReadOnly", "-OnAgent", "-Agent", "-RepairSource", "-SourceLanding" })
            exclusion.ShouldContain($"`{flag}`", Case.Sensitive, $"c1332-owner-startref-excluded-{flag}");
        startRef.ShouldContain("reported as its `WorktreeBranch`", Case.Sensitive, "c1332-owner-recorded-branch");
        startRef.ShouldContain("sets no merge target", Case.Sensitive, "c1332-owner-base-only");
        startRef.ShouldContain(
            "confirm the running build has it (`GET /api/version`) and that the task detail shows both the requested ref and the recorded base",
            Case.Sensitive, "c1332-owner-support-observation");

        // Window 2: failed-owner recovery, from its existing anchor to the sibling topic.
        var recoveryAt = Anchor(owner, "repairsource-failed-owner-recovery",
            "**RepairSource succeeded; owner Failed.**", "c1332-owner-anchor-repairsource-failed-owner-recovery");
        var recovery = Window(owner, recoveryAt, "**Continuing a sibling's work", "c1332-owner-recovery");
        recovery.ShouldContain("whose `subjectTaskId` is the original Code/Worktree owner", Case.Sensitive, "c1332-owner-review-subject");
        var land = recovery.IndexOf("scripts/delegate.ps1 -Land <owner-guid>", StringComparison.Ordinal);
        land.ShouldBeGreaterThanOrEqualTo(0, "c1332-owner-recovery-command");
        var status = recovery.IndexOf("scripts/delegate.ps1 -Status <owner-guid>", land, StringComparison.Ordinal);
        status.ShouldBeGreaterThan(land, "c1332-owner-recovery-status");
        var landCommand = recovery[land..status];
        foreach (var (flag, literal) in new[]
                 {
                     ("-ExpectedSourceSha", "-ExpectedSourceSha <full-sha>"),
                     ("-ReviewEvidenceId", "-ReviewEvidenceId <review-evidence-id>"),
                     ("-RecoverReviewedSource", "-RecoverReviewedSource"),
                 })
            landCommand.ShouldContain(literal, Case.Sensitive, $"c1332-owner-recovery-command-{flag}");
        recovery.ShouldContain("a queued request is not publication", Case.Sensitive, "c1332-owner-publication");
        recovery.ShouldContain(
            "Failed status, failure code, reason and completion time remain historical",
            Case.Sensitive, "c1332-owner-failed-history");

        // Window 3: adopting a separate -StartRef repair, to its next peer heading.
        const string adoptionHeading = "#### Landing a `-StartRef` repair (CARD-0675)";
        var adoptionAt = owner.IndexOf(adoptionHeading, StringComparison.Ordinal);
        adoptionAt.ShouldBeGreaterThanOrEqualTo(0, "c1332-owner-adoption");
        var adoption = Window(owner, adoptionAt + adoptionHeading.Length, NextHeading(owner, adoptionAt + adoptionHeading.Length), "c1332-owner-adoption");
        adoption.ShouldContain("with the **repair task** named as `subjectTaskId`", Case.Sensitive, "c1332-owner-adoption-subject");
        adoption.ShouldContain(
            "-Land <owner> -FromTask <repair> -ExpectedSourceSha <S> -ReviewEvidenceId <id>",
            Case.Sensitive, "c1332-owner-adoption-command");
        adoption.ShouldContain(
            "`-FromTask` is refused `adopt_source_invalid` while `RepairSourceTaskId` is set",
            Case.Sensitive, "c1332-owner-no-repairsource-adoption");
    }

    private static void AssertPipelineOwner(string owner)
    {
        var at = Anchor(owner, "standing-pipeline-policy",
            "### Standing pipeline policy: four per stage, six on server2, read limits first (CARD-0533)",
            "c1332-owner-anchor-standing-pipeline-policy");
        var policy = Window(owner, at, "### Keep-six in-flight reminder", "c1332-owner-pipeline");

        // Rule 1's route explanation names every field the dispatch read interprets.
        var route = Slice(policy, "read the effective-settings route", "Use its limits", "c1332-owner-pipeline-route");
        route.ShouldContain("`GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true`", Case.Sensitive, "c1332-owner-pipeline-scoped-route");
        foreach (var field in new[] { "concurrencyScopes", "hosts", "runners", "runnerDefaults", "parallelRemaining", "queuedRemaining" })
            route.ShouldContain($"`{field}`", Case.Sensitive, $"c1332-owner-pipeline-field-{field}");

        policy.ShouldContain("Each pipeline stage at up to four; at most six tasks on server2 across all stages.", Case.Sensitive, "c1332-owner-pipeline-defaults");
        policy.ShouldContain("Antiphon's enforced limits are the ceiling, so use the lower effective stage cap", Case.Sensitive, "c1332-owner-pipeline-lower-cap");
        policy.ShouldContain("each task in its own `-Worktree`, never more tasks in one stage than its cap", Case.Sensitive, "c1332-owner-pipeline-worktrees");
        policy.ShouldContain("Prefer server2 (`-Runner server2`) while it is eligible", Case.Sensitive, "c1332-owner-pipeline-host-preference");
        policy.ShouldContain("is an operator-only setting; the orchestrator does not write it on its own initiative", Case.Sensitive, "c1332-owner-pipeline-host-authority");
        policy.ShouldContain("Count Code rows that are in flight, queued or `ready`", Case.Sensitive, "c1332-owner-pipeline-code-depth");
        policy.ShouldContain("At the cap, start no new Plan toward Code", Case.Sensitive, "c1332-owner-pipeline-no-plan-at-cap");
        policy.ShouldContain("defer, even with a free Code slot", Case.Sensitive, "c1332-owner-pipeline-overlap");
        policy.ShouldContain("the flag is legal only when `population` is `open` and `canOverride` is true", Case.Sensitive, "c1332-owner-pipeline-override-predicates");
        policy.ShouldContain("`axis: absolute` with no occupant in that role: re-send with `-IgnoreConcurrencyLimit`", Case.Sensitive, "c1332-owner-pipeline-override-axis");
        policy.ShouldContain("`axis: role`, or any listed occupant in the role you are dispatching: defer", Case.Sensitive, "c1332-owner-pipeline-override-stage");
    }

    private static void AssertVerificationOwner(string owner)
    {
        var at = Anchor(owner, "verification-rounds",
            "## Interim and Final verification rounds (CARD-0544, dormant)", "c1332-owner-anchor-verification-rounds");
        var rounds = Window(owner, at, NextHeading(owner, at + 1), "c1332-owner-verification");

        rounds.ShouldContain("`Final` is the default and the only round that can approve a land", Case.Sensitive, "c1332-owner-final-default");
        rounds.ShouldContain("The first Code and the first Review of a card always run `Final`", Case.Sensitive, "c1332-owner-first-final");
        rounds.ShouldContain("`codeVerificationPolicy`/`reviewVerificationPolicy = AllowInterim`", Case.Sensitive, "c1332-owner-interim-AllowInterim");
        rounds.ShouldContain("`InterimVerification:Enabled`", Case.Sensitive, "c1332-owner-interim-InterimVerification:Enabled");
        rounds.ShouldContain("the nightly backstop readiness receipt/monitor are current", Case.Sensitive, "c1332-owner-interim-nightly-readiness");
        rounds.ShouldContain("`-VerificationSubject` (the original landing owner)", Case.Sensitive, "c1332-owner-interim-VerificationSubject");
        rounds.ShouldContain("`-VerificationBaselineOutcome` (a full-scope Review outcome)", Case.Sensitive, "c1332-owner-interim-VerificationBaselineOutcome");
        rounds.ShouldContain("`-VerificationSelectionFile` (the committed selection as JSON)", Case.Sensitive, "c1332-owner-interim-VerificationSelectionFile");
        rounds.ShouldContain("is never resubmitted silently as a different round", Case.Sensitive, "c1332-owner-no-fallback");
        rounds.ShouldContain("routes to a fresh Final Review of the unchanged candidate, never to land", Case.Sensitive, "c1332-owner-final-before-land");
        rounds.ShouldContain("a latched owner lands only with a Clean Final/Full Review", Case.Sensitive, "c1332-owner-latched-land");
        rounds.ShouldContain("ships disabled", Case.Sensitive, "c1332-owner-interim-disabled");
    }

    private static void AssertPlacementOwner(string owner)
    {
        const string heading = "**Runner defaults are not model pins.**";
        var at = Anchor(owner, "runner-placement", heading, "c1332-owner-anchor-runner-placement");
        var placement = Window(owner, at, NextBoldTopic(owner, at + heading.Length), "c1332-owner-placement");

        placement.ShouldContain("Read `GET /api/runner-defaults` and `GET /api/session-runners`", Case.Sensitive, "c1332-owner-placement-routes");
        placement.ShouldContain("Normally omit `-Runner`; the runtime default places the task.", Case.Sensitive, "c1332-owner-runtime-placement");
        placement.ShouldContain("a follow-up inherits its predecessor's platform", Case.Sensitive, "c1332-owner-inherit-predecessor");
        placement.ShouldContain("a stage inherits the card's platform", Case.Sensitive, "c1332-owner-inherit-card");
        placement.ShouldContain("else the task is unpinned (`Any`) and the runtime default places it", Case.Sensitive, "c1332-owner-inherit-any");
        placement.ShouldContain("To unpin a stage on a pinned card pass `-Platform Any` explicitly.", Case.Sensitive, "c1332-owner-any-unpins");
        placement.ShouldContain("Pass a specific platform only when that piece of work requires it", Case.Sensitive, "c1332-owner-os-requirement");
        placement.ShouldContain("an OS-only test or probe", Case.Sensitive, "c1332-owner-os-probe");
        placement.ShouldContain("Habit, a stage name, \"Review always on X\", or preferring the current host are not requirements.", Case.Sensitive, "c1332-owner-no-habit-pins");
        placement.ShouldContain("scope a platform-pinned task to just the OS-specific part with its own filter and budget; leave the rest unpinned", Case.Sensitive, "c1332-owner-os-scope");
        placement.ShouldContain("A pipeline tick never writes defaults.", Case.Sensitive, "c1332-owner-no-tick-write");
        var settings = Sentence(placement, "settings change is", "c1332-owner-settings");
        settings.ShouldContain("A user-requested settings change", Case.Sensitive, "c1332-owner-settings-authority");
        settings.ShouldContain("the current revision", Case.Sensitive, "c1332-owner-settings-revision");
        settings.ShouldContain("a reason", Case.Sensitive, "c1332-owner-settings-reason");
        settings.ShouldContain("Human provenance", Case.Sensitive, "c1332-owner-settings-provenance");
    }

    // ---- V-2: the embedded bundle keeps the operative rules and real owner pointers -----------

    [Test]
    [Arguments("child")]
    [Arguments("pipeline")]
    [Arguments("verification")]
    [Arguments("placement")]
    public void C1332_BundleKeepsRulesAndOwnerPointers(string family)
    {
        // The EMBEDDED resource, not the disk file: this is what a launch composes.
        var bundle = Collapse(InstructionBundles.TextOf(InstructionBundles.Orchestrator));
        var owner = Owner();
        switch (family)
        {
            case "child":
                AssertChildBundle(Slice(bundle,
                    "Message a child's session directly only to steer already-dispatched work.",
                    "When you are working a board", "c1332-bundle-child"), owner);
                break;
            case "pipeline":
                AssertPipelineBundle(Slice(bundle, "When you are working a board", "Live operating settings", "c1332-bundle-pipeline"), owner);
                break;
            case "verification":
                AssertVerificationBundle(Slice(bundle, "Verification", "Platform:", "c1332-bundle-verification"), owner);
                break;
            case "placement":
                AssertPlacementBundle(bundle[Index(bundle, "Platform:", 0, "c1332-bundle-placement")..], owner);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "unknown relocation family");
        }
    }

    private static void AssertChildBundle(string child, string owner)
    {
        AssertPointers(child, owner, "delegate-start-ref", "repairsource-failed-owner-recovery");
        child.ShouldContain("use -Worktree -StartRef <full-sha>", Case.Sensitive, "c1332-bundle-startref-worktree");
        child.ShouldContain("never git checkout -B", Case.Sensitive, "c1332-bundle-no-checkout");
        child.ShouldContain("Use its reported WorktreeBranch.", Case.Sensitive, "c1332-bundle-recorded-branch");
        child.ShouldContain("A base grants no land authority.", Case.Sensitive, "c1332-bundle-base-only");
        child.ShouldContain("for exclusions and -FromTask adoption", Case.Sensitive, "c1332-bundle-adoption-pointer");
        child.ShouldContain("require owner-bound Clean Final/Full Review", Case.Sensitive, "c1332-bundle-owner-review");
        child.ShouldContain("-RecoverReviewedSource", Case.Sensitive, "c1332-bundle-recovery");
        child.ShouldContain("A -RepairSource task cannot be used with -FromTask.", Case.Sensitive, "c1332-bundle-no-repairsource-adoption");
    }

    private static void AssertPipelineBundle(string pipeline, string owner)
    {
        AssertPointers(pipeline, owner, "standing-pipeline-policy");
        pipeline.ShouldContain(
            "read the effective-settings route (CARD-0881): GET /api/agent-tasks/pipeline?projectId=<id> (or ?unscoped=true)",
            Case.Sensitive, "c1332-bundle-effective-route");
        pipeline.ShouldContain("never exceed the lower effective cap", Case.Sensitive, "c1332-bundle-lower-cap");
        pipeline.ShouldContain("Run stages in parallel, each in its own -Worktree.", Case.Sensitive, "c1332-bundle-stage-worktrees");
        pipeline.ShouldContain("Host budget writes need an operator request.", Case.Sensitive, "c1332-bundle-host-authority");
        var depth = Sentence(pipeline, "Keep Code at its depth cap", "c1332-bundle-code-depth");
        foreach (var state in new[] { "in-flight", "queued", "ready" })
            depth.ShouldContain(state, Case.Sensitive, $"c1332-bundle-code-depth-{state}");
        pipeline.ShouldContain("Below cap, pull the lowest-rank unstarted Backlog card", Case.Sensitive, "c1332-bundle-backlog-pull");
        pipeline.ShouldContain("at cap, start no new Plan toward Code", Case.Sensitive, "c1332-bundle-no-plan-at-cap");
        pipeline.ShouldContain(
            "Defer Code touching an in-flight Code task's same source area until it lands, even with a free slot.",
            Case.Sensitive, "c1332-bundle-overlap");
        var @override = Sentence(pipeline, "permits -IgnoreConcurrencyLimit", "c1332-bundle-override");
        @override.ShouldContain("population=open", Case.Sensitive, "c1332-bundle-override-population");
        @override.ShouldContain("canOverride=true", Case.Sensitive, "c1332-bundle-override-allowed");
        @override.ShouldContain("axis=absolute", Case.Sensitive, "c1332-bundle-override-axis");
        @override.ShouldContain("and no same-stage occupant", Case.Sensitive, "c1332-bundle-override-stage");
        @override.ShouldContain("otherwise defer", Case.Sensitive, "c1332-bundle-override-defer");
        pipeline.ShouldContain("Other projects' work never counts against yours.", Case.Sensitive, "c1332-bundle-project-isolation");
    }

    private static void AssertVerificationBundle(string verification, string owner)
    {
        AssertPointers(verification, owner, "verification-rounds");
        verification.ShouldContain("first Code/Review is Final/Full", Case.Sensitive, "c1332-bundle-first-final");
        verification.ShouldContain("Interim requires explicit admission", Case.Sensitive, "c1332-bundle-interim-admission");
        verification.ShouldContain("never silently change rounds", Case.Sensitive, "c1332-bundle-no-fallback");
        verification.ShouldContain("A clean Interim goes to Final Review, never land.", Case.Sensitive, "c1332-bundle-final-before-land");
    }

    private static void AssertPlacementBundle(string placement, string owner)
    {
        AssertPointers(placement, owner, "runner-placement");
        placement.ShouldContain("read GET /api/runner-defaults and GET /api/session-runners", Case.Sensitive, "c1332-bundle-placement-routes");
        placement.ShouldContain("Do not embed a fleet location.", Case.Sensitive, "c1332-bundle-no-fleet-location");
        placement.ShouldContain("Normally omit -Runner and -Platform", Case.Sensitive, "c1332-bundle-runtime-placement");
        placement.ShouldContain("Use -Platform Any to unpin.", Case.Sensitive, "c1332-bundle-any-unpins");
        placement.ShouldContain(
            "Pin only an OS-required piece with its own filter and budget; leave the rest unpinned.",
            Case.Sensitive, "c1332-bundle-os-scope");
        placement.ShouldContain("Never write defaults on a pipeline tick.", Case.Sensitive, "c1332-bundle-no-tick-write");
    }

    /// <summary>
    /// The paragraph carries each canonical owner URI as an exact fragment (a fragment with a suffix
    /// is a different, unresolvable pointer), and every pointer it carries resolves to exactly one
    /// <c>&lt;a id&gt;</c> anchor in the real owner file.
    /// </summary>
    private static void AssertPointers(string paragraph, string owner, params string[] fragments)
    {
        var carried = Regex.Matches(paragraph, @"docs/orchestration-loop\.md#([A-Za-z0-9-]+)")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        foreach (var fragment in fragments)
        {
            carried.ShouldContain(fragment, $"c1332-pointer-{fragment}");
            Count(owner, $"<a id=\"{fragment}\"></a>").ShouldBe(1, $"c1332-pointer-{fragment}-target");
        }
        foreach (var fragment in carried)
            Count(owner, $"<a id=\"{fragment}\"></a>").ShouldBe(1, $"c1332-pointer-resolves-{fragment}");
    }

    // ---- V-3: one fixed allocation for the normalised orchestrator body, LF or CRLF checkout ---

    /// <summary>The orchestrator body's allocation in UTF-16 units after the catalog's LF + Trim normalisation (CARD-1332 D-3).</summary>
    private const int OrchestratorBodyAllocation = 11_500;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void C1332_OrchestratorBodyFitsItsAllocation(bool crlfCheckout)
    {
        // Boundary probe first, so a measurement defect reaches its own assertion even when the real
        // bundle's headroom is small: 11,497 ASCII x, one internal LF and one supplementary scalar (two
        // UTF-16 units, four UTF-8 bytes) is exactly the allocation. Trim-only whitespace surrounds it;
        // the CRLF case renders the internal LF as CRLF, the way a Windows checkout would.
        var expectedProbe = new string('x', 6_000) + "\n" + new string('x', 5_497) + "\U0001F600";
        var probeSource = "\n\n  " + expectedProbe + "  \n\n";
        if (crlfCheckout)
            probeSource = probeSource.ReplaceLineEndings("\r\n");
        var (probeText, probeUnits) = Measure(probeSource);
        probeText.ShouldBe(expectedProbe, "c1332-body-normalized");
        probeUnits.ShouldBe(OrchestratorBodyAllocation, "c1332-body-utf16");

        // The actual source, as checked out here and as a CRLF checkout would hold it.
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server", "Bundles", "orchestrator.md"));
        if (crlfCheckout)
            source = source.ReplaceLineEndings("\r\n");
        var (_, units) = Measure(source);
        Console.WriteLine(
            $"C1332 orchestrator body crlf={crlfCheckout} measured={units} limit={OrchestratorBodyAllocation} headroom={OrchestratorBodyAllocation - units}");
        units.ShouldBeLessThanOrEqualTo(OrchestratorBodyAllocation,
            $"c1332-body-allocation: actual {units} > {OrchestratorBodyAllocation}");
    }

    /// <summary>
    /// The catalog's normalisation (InstructionBundles.ReadNormalised): line endings to LF, outer blank
    /// space trimmed, and the size counted in UTF-16 units because the argv ceiling is CreateProcessW's.
    /// </summary>
    private static (string Normalised, int Units) Measure(string source)
    {
        var normalised = source.ReplaceLineEndings("\n").Trim();
        return (normalised, normalised.Length);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static string Owner() =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "orchestration-loop.md"));

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>
    /// The anchor appears exactly once and nothing but line breaks sits between it and the heading
    /// it introduces. Returns the heading's index so the caller can bound the section from there.
    /// </summary>
    private static int Anchor(string owner, string id, string heading, string label)
    {
        var anchor = $"<a id=\"{id}\"></a>";
        Count(owner, anchor).ShouldBe(1, label);
        var at = owner.IndexOf(anchor, StringComparison.Ordinal);
        var headingAt = owner.IndexOf(heading, at, StringComparison.Ordinal);
        headingAt.ShouldBeGreaterThan(at, label + "-heading");
        owner[(at + anchor.Length)..headingAt].Trim().ShouldBe(string.Empty, label + "-introduces");
        return headingAt;
    }

    /// <summary>Collapsed text from <paramref name="start"/> to the first <paramref name="endMarker"/> after it.</summary>
    private static string Window(string text, int start, string endMarker, string label) =>
        Window(text, start, Index(text, endMarker, start, label + "-end"), label);

    private static string Window(string text, int start, int end, string label)
    {
        end.ShouldBeGreaterThan(start, label + "-end");
        return Collapse(text[start..end]);
    }

    /// <summary>Collapsed text between two markers, both of which must exist in order.</summary>
    private static string Slice(string text, string startMarker, string endMarker, string label)
    {
        var start = Index(text, startMarker, 0, label + "-start");
        var end = Index(text, endMarker, start + startMarker.Length, label + "-end");
        return text[start..end];
    }

    /// <summary>The sentence holding <paramref name="marker"/>: from the preceding full stop to the next one.</summary>
    private static string Sentence(string collapsed, string marker, string label)
    {
        var at = Index(collapsed, marker, 0, label);
        var start = collapsed.LastIndexOf(". ", at, StringComparison.Ordinal);
        var end = collapsed.IndexOf('.', at + marker.Length);
        end.ShouldBeGreaterThan(at, label + "-sentence-end");
        return collapsed[(start < 0 ? 0 : start + 2)..(end + 1)];
    }

    private static int Index(string text, string marker, int from, string label)
    {
        var at = text.IndexOf(marker, from, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, label);
        return at;
    }

    /// <summary>The next Markdown heading of any level after <paramref name="from"/>.</summary>
    private static int NextHeading(string text, int from)
    {
        var candidates = new[] { "\n## ", "\n### ", "\n#### " }
            .Select(marker => text.IndexOf(marker, from, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .ToArray();
        candidates.ShouldNotBeEmpty("c1332-owner-next-heading");
        return candidates.Min();
    }

    /// <summary>The next bold topic paragraph (a line opening with <c>**</c>) after <paramref name="from"/>.</summary>
    private static int NextBoldTopic(string text, int from) => Index(text, "\n**", from, "c1332-owner-next-topic");

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
