# Review f5262ff0 — subject e150a772 (C508 E2E expectation change)

Reviewed `e5f9fef33be102bf808af375892f8c39220f0ec5` (one commit over rc base `91b45006b`).
Scope: Final review, **Interim** ordinary scope (brief-named classes only; no full Unit lane;
the two changed E2E methods are Windows-gated and were compiled, not run).

## Verdict on the diagnosis: correct, not Windows-only

- The refusal is CARD-1428 D-3 (`c00e3d1da`): `AgentTaskDispatcher.cs:5571-5598` revalidates the
  frozen sibling observation under the lease; `CardSiblingInspectionIntent.Fallback` is the
  `Project.BaseBranch` string read by `CardSiblingInspectionService.ConfiguredDefaultAsync`
  (`:72-79`). The lease hook edits that DB value, the intent differs, and the claim returns
  `SiblingObservationStale`. It is a DB string comparison with no git ref spelling, CRLF, or path
  casing involved, so it is not specific to any platform.
- The Linux unit twin `AgentTaskDispatchBaseGuardTests.C508_GuardRefMismatchWarnedOnce`
  (`:1357`; the E2E hook's own doc comment names it as its match) was amended by CARD-1349 S2 to
  expect: still Queued after the first tick, Dispatched from `project-default` on the second, and
  zero `base-observation-stale` intents. That is the same outcome the E2E now asserts.
- `Count == 1` cannot be reached in this scenario by design. The new predicate cannot pass on a
  broken dispatch: it requires `Dispatched` + `project-default` + `DefaultBranch` + no mismatch
  key. It fails fast on Blocked/Failed, asserts the hook fired, and is capped at 600 s
  (`ProgressAwareWait`).

## Defects

**D-1 Weakened test: the delivery-named methods now assert absence.**
- Where: `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:26-35` and `:38-59`. Both are
  pinned in `scripts/fixtures/nightly/c1584-e2e-source-roster.txt:97-98`.
- Failure: a regression that drops every `base-observation-stale` prompt between intent and the
  caller (for example, a materializer or delivery branch that skips `MismatchKey`) leaves both
  tests green. PC-50 (`docs/superpowers/plans/2026-09-14-card-0508-worktree-base-selection-plan.md:2430`,
  `:2701`) names `C508_MismatchWarningReachesCaller` as its killing test and can no longer go red.
- Why: the bodies now assert `intents.ShouldBeEmpty()`, zero notes, and zero prompts. This
  duplicates the Linux unit case `C508_GuardRefMismatchWarnedOnce(0, True)` at E2E cost, while the
  roster still advertises both methods as delivery witnesses.
- Fix: see D-2. Retarget these two methods (names and roster unchanged) to a scenario that
  really produces a mismatch. Move the re-observation check into a new, honestly named sibling,
  e.g. `C508_LeaseDefaultEditReobservedWithoutWarning` (add it to the roster), or drop it as
  already covered by the unit case.

**D-2 Lost producer-to-recipient witness for a real stale-base warning.**
- Where: the only reachable mismatch producer is the vanished-default path
  (`AgentTaskDispatcher.cs:6390-6396`). Its only test,
  `DispatchBaseNotificationTests.Coverage.cs:404` `C508_VanishedDefaultStillCapturesWarning`,
  stops at the intent and notification rows. No test takes a real mismatch intent to a
  transcript-confirmed caller UserPrompt, either idle or busy.
- Failure: the default branch is deleted under the claim lease, so the cut falls back to
  `HEAD`/`RepoHead` and two intents are written. If the mismatch prompt is then never delivered,
  or delivered twice, no test fails. The C540 tests prove delivery only for sibling-keyed
  DispatchBase notes. The pipeline is key-agnostic today, so general delivery is still witnessed,
  but the mismatch key itself is not.
- Fix:
  - Add a lease hook variant in `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`, e.g.
    `delete-default-on-lease.json` beside `MoveDefaultOnLease`, that runs
    `git branch -D trunk` in the fixture repository.
  - In `StartMismatchDispatchAsync`, seed `Project.BaseBranch = "trunk"` with a real `trunk`
    branch.
  - In `WaitForMismatchIntentAsync`, expect: Dispatched, `WorktreeBaseRef == "HEAD"`,
    `WorktreeBaseSource == RepoHead`, and exactly two intents. These are the mismatch intent
    (detail contains `trunk` and `HEAD`, `ParentSessionId == CallerId`) and
    `default-unresolved`.
  - Keep `AssertDispatchReceiptsAsync(intents)`, which proves each complete prompt exactly once.
    The busy variant keeps Pending rows with 0 attempts and no prompt until
    `ReleaseBusyAsync`, as the pre-change version did.
  - Re-point PC-50 at it.

## Advisories (no Review rerun needed for these alone)

- A-1: CARD-0508 plan rows V-16 / PC-50 (`:1394`, `:1501`, `:2190`, `:2430`, `:2701`) still
  describe one mismatch prompt. Amend them in a separate Docs task, or in the D-1/D-2 Code task
  (watch for plan add/add on master).
- A-2: The diagnostics are safe. They contain only task-owned DB rows (status, FailureReason,
  intent keys/details, last six events), flattened and truncated to 180 chars per field.
- A-3: `DispatchWaitDiagnostic` is linked into both csproj files exactly like
  `ProgressAwareWait.cs`, so the sharing is consistent. Its two new tests pin exact strings and
  can go red. `ProgressAwareWait` itself is unchanged.
- A-4: The `Directory.Exists(native)` guard does not weaken C540. A missing directory gives
  zero native prompts, which still fails the `== original.Count` (2) check.
- A-5: The success predicate accepts both "refused then re-inspected" and "hook fired before the
  first inspection". If the refusal path itself must be pinned, also assert the
  SiblingInspectionPending hold event.

## Runs (server2/Linux, `e5f9fef33`, build slot, `UseAppHost=false`, `OutputPath=bin-f5262ff0/`)

- Build Antiphon.Tests: 0 errors (slot lease 337cead2). Build Antiphon.E2E (compile check of
  DispatchBaseWarningDeliveryE2ETests): 0 errors (lease 75bb9609).
- `dotnet exec --no-build` (lease 014fa743):
  - Run 1, classes ProgressAwareWaitTests (8, including both `Dispatch_timeout_*`),
    LandDeliveryWaitRoutingTests (4), RcRosterRepairTests (3),
    WindowsUnitPlatformAdmissionTests (5), C1514LandingSplitBudgetTests (5: C1404, C1577,
    C1584, C1410, G2865): total 25, passed 24, failed 0, skipped 1. The skip is
    `C1210_Windows_linux_only_outcomes_are_exact`, which needs Windows placement.
    `C1210_Roster_closure_matches_source_gates` passed.
  - Run 2, `C508_GuardRefMismatchWarnedOnce` (0,T)(1,T)(2,T)(1,F),
    `C508_VanishedDefaultStillCapturesWarning`, and
    `Stale_claim_preserves_capacity_grant_for_the_fresh_inspection`: 6/6 passed.
- Not run: the two C508 E2E methods (Windows-gated at `LandDeliveryFixture.InitializeAsync`),
  the full Unit lane, and E2E class-wide. A native Windows run remains pending.
- `check-evidence-diff.ps1` over 91b45006..e5f9fef33: commits=1, violations=0.
- Merge-tree:
  - Onto rc tip `01c32a59`: clean.
  - Onto master `f8bddb6e`: a whole-branch merge-tree has an add/add conflict on
    `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, inherited from the rc line.
    Picking the single commit onto master (`--merge-base 91b45006`) is clean (tree f4016226b).
- Cleanup: all 35 `bin-f5262ff0` directories were removed by literal path. The worktree is clean.
