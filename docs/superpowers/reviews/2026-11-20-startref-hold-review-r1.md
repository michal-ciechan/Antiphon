# CARD-1514 immutable StartRef sibling-land hold: Review round 1

Review task `c0fa313a-6f7c-4a9e-ace7-14f10c30ab92` (Claude Opus; author Grok). Subject: Code task
`dc59c094-9321-47b5-802b-a5e8803629aa`, branch `feat/card-task-dc59c094`, tip
`5ee5e5e7c5f8a91ed5c350187b99914d7575c7c4`. Range `e54065fe59f798fb5dc395a222679545ebe1c9e0..5ee5e5e7c5f8a91ed5c350187b99914d7575c7c4`
(red f16d1c917, fix bffadcd8d, dedupe 5ee5e5e7c). Round 1, Final profile, full ordinary scope.

## Verdict: CLEAN

No blockers and no regressions. Three advisories are below. None of them holds the land.

## Evidence (server2, Linux; Windows UNOBSERVED)

One checkpoint-tool run `20261120-111352-d889` over `.antiphon/review-c0fa313a/plan.md`. Rows CP-1..CP-4
are copied verbatim from the plan; CP-G is the brief's guard batch. It used one cold build
(`bin-c0fa313a-r1/`, 331 s), `slot=granted` on every row, `sourceState=clean buildSource=verified`,
and `unlisted: none`. `validate --expected-source-sha 5ee5e5e7c...` printed
`CHECKPOINT SOURCE VALID rows=5`. The run started at 1-minute load 7.75. An earlier start
(`20261120-110651-10b3`) was stopped before any build or row because load rose to 43; its rows show
`executed=n/a slot=skipped`.

| CP | Filter | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| CP-1 | `(AgentTaskDispatchBaseGuardTests*)\|(DispatchSiblingInspectionTests*)/C1514_Immutable*` | 2 | 2 | 0 | 0 |
| CP-2 | `AgentTaskDispatchBaseGuardTests/*` | 103 | 103 | 0 | 0 |
| CP-3 | `(DispatchSiblingInspectionTests*)\|(DispatcherSiblingInspectionSchedulingTests*)/*` | 83 | 83 | 0 | 0 |
| CP-4 | `AgentTaskWorktreeBaseSyncDebtTests/*` | 10 | 10 | 0 | 0 |
| CP-G | `(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/*` | 53 | 53 | 0 | 0 |

The counts match the plan's Expect column. Other checks:

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef e54065fe5 -HeadRef HEAD`: commits=3,
  entries=0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` against master `ba2ca8859d8ff4b80303e58458b677cd6dfc3182`:
  clean, tree `5f54f8e546aad97abbea85010724cf45f75d1691`.
- Red witnesses: I did not re-run them at f16d1c917, because the brief allows one build. I traced
  each one against the base code instead. Base `InspectAsync` returned a `pending_land` hold for any
  uncontained pending sibling, whatever the StartRef was. In V-1 the sibling tip is above master and
  is not an ancestor of the StartRef, so no reviewed exemption applies and the task stays Queued.
  In V-2 the overflow catch returned the first pending row as a hold. In V-3 the peek-null tick
  wrote the pending sentence a second time. All three are red for the reasons the plan states.
- Can the new code still fail? Reverting `IsCommitId` to false turns CP-1 red. Widening it to
  "any non-null StartRef" turns the existing `C1349_PendingLandSurvivesLimits` cases red: its
  `unrelated StartRef` case uses `master` and its `unresolved StartRef` case uses `missing-ref`,
  and both must still hold. Reverting the peek-null text turns V-3 and
  `T0442_V20_pending_land_precedes_source_selection_in_every_mode` red.

## Questions

1. **Why the hold existed, and is a full-SHA StartRef immune?** The hold came from CARD-0442
   (ab9094127) and was bounded by CARD-1349 (a4207bf70). It protects a task whose base is chosen
   from a moving ref (the CARD-0442 automatic tip, or a symbolic merge target) while a sibling land
   moves that ref. When `WorktreeBaseRequestedRef` is set, `AgentTaskWorktreeBaseResolver` returns
   `Target()` and skips CARD-0442 selection, and `WorktreeBaseResolver.Resolve` cuts from the
   requested ref. A full SHA resolves to itself, and `frozen[baseRef] = target` pins it, so the
   land cannot change the cut. Holding never moved the base; it only delayed dispatch.
   - If the Goal says to build on the sibling's land but the StartRef is a fixed SHA, the hold
     would not help: the cut is still that SHA. The ordinary sibling warning now names the
     uncontained sibling, which is the correct signal.
   - A SHA that is not an ancestor of the target, or not yet on origin, is handled by
     StartRefAvailability at creation and by resolution. Neither changed.
   - Where the base comes from: only two writers set `WorktreeBaseRequestedRef`
     (`AgentTaskService.cs:349` and `:1591`). They are a caller StartRef stored verbatim, a
     CARD-0644 continuation tip SHA, and a ref-only parent's `FrozenSourceSha`. All three are fixed
     cuts. A defaulted or automatic base leaves the field null, so it never skips the hold.
2. **The hex test.** `GitObjectId.TryNormalize` requires exactly 40 or 64 hex digits after Trim
   and lowercasing, and rejects CR, LF and tab.
   - Abbreviated SHAs and `refs/...` values fail the test and still hold.
   - Leading or trailing whitespace is already refused at creation (`RequireValidStartRef`).
   - Uppercase is accepted. Git parses uppercase full hex as an object id, so this is correct.
   - A branch literally named with 40 hex characters is safe: git's `get_oid_basic` prefers the
     object id (A-3 covers the 64-hex case).
3. **Warnings and dedupe.**
   - An immutable pending sibling adds an ordinary `Describe(row, sha, state)` warning, which still
     names the branch, the tip and the `Land` state.
   - Past the cap, `Result(null, "candidate_limit")` dispatches with `sibling-inspection-incomplete`.
     V-2 asserts this.
   - The tip and enrichment loops now skip a null `FullTipSha`, which only the new
     unknown-commit path produces, and `SiblingWarningReducer` already handles null tips.
   - The peek-null site reuses the previous `held: ... is landing and is not yet in ...` sentence
     only when that was the last Held text for the task. The capacity, stale-observation,
     integration-unknown and `HoldKind.SiblingInspectionPending` texts are unchanged.
     `DispatchHoldDetails.Classify` still maps the reused sentence to `Landing`.
4. **Red witnesses and the unrelated-StartRef guard.** See Evidence: red at base by trace, green
   at the tip. `C1349_PendingLandSurvivesLimits` (all modes, including `unrelated StartRef` and
   `unresolved StartRef`) passed in CP-3.
5. **Docs.** The two `docs/orchestration-loop.md` edits (lines ~300 and ~533) match the code. The
   Fast-track lane section on master (`#fast-track-lane-operator-2026-11-20`) says nothing about
   sibling holds or StartRef, so there is no inconsistency, and merge-tree is clean.
6. **Diff scope and concurrency.** The diff touches only `CardSiblingInspectionService.InspectAsync`
   (plus `IsCommitId`), the one peek-null site in `AgentTaskDispatcher`, the docs, the plan and the
   tests. The `git_inspection_error` path still holds, which is conservative and stated in the plan.
   A land that finishes during dispatch moves only the frozen landing ref, so the existing
   stale-observation path re-inspects. The SHA base is frozen once and cannot move.

## Advisories (file as cards; they do not hold the land)

- **A-1 (test coverage).** No direct witness covers an abbreviated (7-12 hex) or uppercase
  StartRef. A mutant that replaced `IsCommitId` with a hex-only check of any length would survive.
  Add one `C1349_PendingLandSurvivesLimits` case with an abbreviated SHA.
- **A-2 (docs).** The doc text says "a StartRef that is a 40- or 64-character commit id". The skip
  also applies to a CARD-0644 continuation tip and a ref-only parent `FrozenSourceSha`, because
  both use the same field. The behaviour is correct (both are fixed cuts), but the doc could say so.
- **A-3 (edge case).** In a SHA-1 repository git resolves a 64-hex string as a ref name. A branch
  literally named with 64 hex characters would therefore be a mutable base that still skips the
  hold. This is contrived, and `TryNormalize` could check the length against the repository's
  object format.

Activation needs an AppHost restart on a build that contains this change. PCs stay pending for
method-scoped SourceLanding Mutation (mutation waived by operator 2026-11-12).
