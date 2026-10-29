# Review cd186dd7: C540 relaxed sibling wait (subject 3308416c @ 8cb30a5d4)

Verdict: no defects. The relaxed wait does not hide a duplicated, lost or uncollapsed warning.
Every regression class C540/C508 exist to catch still goes red, either in the predicate or in the
unchanged post-wait and receipt assertions. Scope was Interim: narrow named classes on Linux, and
the native Windows E2E rerun is still pending.

Reviewed: 5e9514d02..8cb30a5d470797def87ad2207b757c157e5e5885 (red 6cc4227c6, fix 8cb30a5d4), 5 test-only files.

## Runs (server2/Linux, one slot build UseAppHost=false OutputPath=bin-rvcd/, then dotnet exec through build-slot.ps1)
- Named classes: 123 total, 122 passed, 0 failed, 1 skipped. Per TRX: DispatchCrashWarningTests 21,
  DispatchSiblingInspectionTests 33 (includes the new C540_LoadedHostInspectionTimeoutSettlesTheReducedSiblingWait),
  LandDeliveryWaitRoutingTests 4, DispatchBaseNotificationTests 57, RcRosterRepairTests 3,
  WindowsUnitPlatformAdmissionTests 4 passed (C1210 guard/roster-closure/audit green) + 1 skipped
  (C1210_Windows_linux_only_outcomes_are_exact needs Windows placement, by design).
- Census C1514LandingSplitBudgetTests C1404/C1577/C1584/C1410/G2865: 5/5 passed.
- Unlisted run, with its reason: AgentTaskDispatchBaseGuardTests/C540* 28/28 passed. It proves the
  backstop described in A-3.
- Antiphon.E2E compiled with 0 errors. The E2E classes are Windows-only; a native rerun is PENDING.
- check-evidence-diff 5e9514d02..HEAD: commits=2 violations=0.
- Red witness: at 6cc4227c6 the predicate is the exact-count form, so the 3-intent set returns false.
  Both new positive witnesses are red there by construction.

## CHECK 1: counter-examples against ReducedSiblingDispatchReady (tests/Shared/DispatchWaitDiagnostic.cs:73-94)
Mutants were analysed against the unit inputs:
- Drop the `0 omitted` check (:94): killed by A_non_timeout_or_omitting... case 1.
- Drop the inspection_timeout check: killed by cases 2 and 3.
- Final `All` -> true: killed by A_missing_expected_sibling_does_not_settle.
- Drop the expected-key `continue`: killed by An_exact_reduced_sibling_pair...
- Survivors are each redundant or backstopped (A-1). In detail:
  - Drop the duplicate check (:82): a duplicated sibling key is still rejected post-wait by
    `intents.Single` (LandDeliveryFixture.Dispatch.cs:137). A duplicated incomplete key is still
    rejected by `budgetWarning`.
  - Drop `budgetWarning ||` (:84): a second incomplete warning carries the same key, so the
    duplicate check rejects it.
  - Drop `key != SiblingInspectionIncompleteKey`: a foreign key is still rejected by the
    detail-shape check, because only the incomplete key carries "inspection_timeout; ... 0 omitted".
  - Drop the `Count == 0` guard: the E2E always passes two expected keys.
- Open-intent-id / crash-cut projection: IsUnmaterializedProjection is unchanged, and its 14 cases are green.
- Lost warning or dropped pair: a missing expected key never settles, so the wait times out red.
- Uncollapsed pair: the extra `sibling:<a>` key never settles. If a timed-out prefix happened to equal
  {containing, divergent}, the post-wait `also covers 2` assertion (:139) goes red.

## CHECK 2: delivery
AssertDispatchReceiptsAsync is unchanged. It requires one transcript UserPrompt and one native
user_message_chunk per committed intent body, using PromptSubmissionMatch.IsCompleteIn. It checks
one queue row per notification and that the current intents equal the original ones (:370).
The incomplete warning's Body carries the `[dispatch-base` marker, so it is counted and must also
arrive exactly once. The original pair's branch, tip and Covered assertions are unchanged (:137-141).
The busy-caller and pre-enqueue counts now follow intents.Count, and those counts are still pinned
exactly by the predicate. The enqueue-errors cut (only 2 enqueues fail) is unaffected by a third intent.

## CHECK 3: late write
None. All drafts, including sibling-inspection-incomplete, are built in one list
(AgentTaskDispatcher.cs:6367-6400). They are captured by DispatchBaseWarningIntentService.CaptureAsync
and committed in the single claim SaveChanges alongside the Dispatched event (AgentTaskDispatcher.cs:6170-6198).
A poll therefore sees either none or all of a dispatch event's intents. A later dispatch event for
the same task would add duplicate keys, which the predicate rejects; if that write landed after
the wait returned, AssertDispatchReceiptsAsync :370 catches it.

## CHECK 4: legitimacy
A budget timeout on a loaded host is a designed degraded state, not a product bug:
- WorktreeBaseInspection holds one monotonic 5 s deadline (GitSettings.WorktreeBaseInspectionTimeoutSeconds, unchanged).
- CardSiblingInspectionService catches enrichment timeouts (:236 ancestry, :253 description). It keeps
  and reduces the warnings, and returns the reason. Its contract (:200) is "Warning output can be
  incomplete; it cannot silently confer a source-selection verdict."
- BuildDispatchWarningDrafts emits sibling-inspection-incomplete whenever Reason != null (AgentTaskDispatcher.cs:6379-6381).

## CHECK 5: names, roster and census
No test was renamed or removed. The roster, census and RcRoster checks are green. The new predicate
writes no diagnostics, and the timeout diagnostic is unchanged. Nothing is leaked.

## Advisories (no Review rerun needed)
- A-1: Add unit cases that pin the surviving branches: a duplicate sibling key, a duplicate incomplete
  key, and a foreign key carrying a timeout-shaped detail.
- A-2: `0 omitted` does not prove every candidate was inspected. A timeout inside the candidate loop
  exits through the outer catch (CardSiblingInspectionService.cs:256) with unreduced partial
  warnings and still reports 0 omitted. The post-wait Covered assertion keeps this loud rather than
  hidden. The doc comment at DispatchWaitDiagnostic.cs:66 overstates the check.
- A-3: A systematic slowdown that always trips the budget (for example an enrichment performance
  regression) no longer fails C540 E2E. The Linux AgentTaskDispatchBaseGuardTests C540_* exact-count
  RunAsync helper (:562) still pins "no extra intent" (28/28 green).
- A-4: The new witness hand-copies the dispatcher's incomplete-detail format
  (DispatchSiblingInspectionTests.cs:496), so it is partly self-comparing. If the format drifts, the
  E2E fails safe: it times out rather than settling falsely.
- A-5: The wait label "two committed reduced dispatch intents" (LandDeliveryFixture.Dispatch.cs:128) is now stale wording.
- A-6 LANDING: The fix does not reach master or rc as a two-commit pick. Pick 1 (6cc4227c6) applies
  cleanly. Pick 2 (8cb30a5d4) conflicts in tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs
  on both origin/master and origin/rc/2026-10-27. Its prerequisite is 6d3c8b974, which links
  DispatchWaitDiagnostic.cs into Antiphon.E2E.csproj and adds the DispatchTimeoutAsync try/catch, and
  it is on neither line. The base 5e9514d02 is also on neither line, and a whole-branch merge-tree
  against master conflicts in 19+ files. To land, carry 6d3c8b974 first, or adapt the 4-line return
  hunk and add the csproj link and the `using Antiphon.TestSupport` on each line.
