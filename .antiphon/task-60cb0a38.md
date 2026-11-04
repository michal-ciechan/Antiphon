# CARD-1701 Code report (task 60cb0a38)

Outcome: A-1, A-3 and the M7c witness are implemented test-first. A-6 is documented. A-5 is not changed, because a test pins the default; the recommendation is below. CP-23 and CP-24 are green at the tested SHA.

- Branch `feat/card-task-60cb0a38`, worktree `/work/worktrees/task-60cb0a38` (server2 runner mirror). Base master `1470340559889c2985ea722173181ea25c51b47c`.
- Commits:
  - `3526cc7329f87537f82c8ee2d1e9642ba1a12c75`: red tests.
  - `b2448d0e214705c567bfc9f5cec67d7cc22afdc8`: fix and docs.
  - `f3f92fc6219f9d38534717982232bb8af60ba376`: test correction. **This is the tested SHA.**
  - The report commit sits on top of it and adds only this file.

## Changes

- **A-1** (`server/Application/Services/DesktopLegacyReleasePolicyService.cs`, `ActAsync`). An executing pass now records `passed` only after a release stands (filed now or filed earlier, before `TryRetireAsync`). A filing that the service refuses (`ConflictException`, for example `mutation_excluded`) therefore extends the held state that already exists: 60, 120, 240 ... min, capped at 24 h. When the task's facts change (status, attempt, CompletedAt, path or branch), the existing fingerprint voids that state and the backoff restarts at 60 min. Dry run and preview are unchanged.
- **A-3** (`Classify`). I chose to normalise rather than refuse at queue time. Refusing new spellings would still leave already-queued rows and recorded `WorktreeBaseRef` values exposed. Normalising holds the tree whenever any reading could resolve to the branch, so no dependent can lose its local base branch.
  - Both `WorktreeBaseRef` and `WorktreeBaseRequestedRef` now go through one `NamesBranch`.
  - It cuts a revision suffix at `^`, `~`, `:` or `@{`. A branch name cannot contain these.
  - It then matches the rest with or without one `refs/heads/`, `heads/`, `refs/remotes/origin/`, `remotes/origin/` or `origin/` prefix.
  - Matching ignores case, because a Windows loose-ref store resolves refs case-insensitively. That is the desktop S4a runs on. The case-insensitive match is beyond the card's two named spellings and can only hold more trees.
  - No deletion guard was changed.
- **A-6** (`docs/orchestration-loop.md`, the S4a paragraph): an unknown `WorktreeLegacyRelease` key stops startup (`ValidateOnStart`, `OptionsValidationException`). On hot reload it disables S4a (`settings_invalid`) until the key is fixed. This is already pinned by LS `Non_scalar_values_are_refused("Enabeld")` and `Invalid_reload_fails_closed_then_recovers`. The paragraph also documents the A-1 and A-3 behaviour.
- **A-5: no change.**
  - The default of 25 is pinned by V-1 `Defaults_and_shipped_file_are_on`, which asserts `MaxReleasesPerTick == 25` for both the class default and the shipped `server/appsettings.json`.
  - The test design also uses "<= 25 removals per hour" as the reason Q-4 needs no rc fence.
  - Recommendation for the operator: for the first drain only, set `WorktreeLegacyRelease:MaxReleasesPerTick=100` in settings. The validator allows 1..100. At that cap the first pass over the ~7,000 desktop candidates takes about 70 h instead of about 11.7 days. Revert to 25 afterwards, or change the shipped default through a design amendment that also re-argues Q-4.
- **Tests:**
  - `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs`:
    - new `Refused_filing_backs_off_and_resets_when_the_facts_change`;
    - new `Settled_missing_directory_is_never_re_checked("stat"|"precheck")`;
    - 5 new cases on `Server_predicates_keep_the_tree`: `startref-queued-heads`, `-peeled`, `-remotes`, `-case` and `baseref-heads-peeled`.
  - `DesktopLegacyWorld.cs` gains `CreateTreeAsync`, `CheckCursorAsync` and `Prechecked`.
- No policy input changed (no new class, roster or execution-policy edit), so policyHash and PinnedInputDigest were not re-recorded.

## Red evidence (which mutant killed which new test)

Each run held one build-slot lease and used the native output with `UseAppHost=false`. The filter was `/*/*/C0824DesktopLegacyReleasePolicyTests/(Refused_filing*)|(Settled_missing*)|(Server_predicates*)`, which runs 26 tests.

| Source under test | Red (expected) | Green |
|---|---|---|
| Unfixed code at 3526cc73 | `Refused_filing...`: deltas `[60,60,60,60,60,60,60]`, expected `[60,120,240,480,960,1440,1440]`. All 5 new spelling cases: Outcome `Removed`, expected `Held` | the other 20 |
| b2448d0e (fix) plus batched mutants. M7c-a: drop `RecordCheck(... "directory_absent", settled: true ...)` in `RunTickAsync`. M7c-b: the pre-check `directory_absent` hold has `settled: false`. MC: `NamesBranch` compares Ordinal | `Settled(stat)`: killed by M7c-a ("no git command ran in the refused tree"). `Settled(precheck)`: killed by M7c-b (same assertion). `startref-queued-case`: killed by MC (Outcome). 3/26 failed | 23 |

The A-1 witness passed on the fix. The run before the test correction failed only on its own post-loop clock bug, which f3f92fc6 corrects; the same assertion is green at the tested SHA. The mutants were restored with `git checkout --`, and the tree was clean before the checkpoint run.

## Checkpoints (run 20261104-090829-a181, plan docs/superpowers/plans/2026-11-03-card-0824-s4a-test-design.md, --after S4a)

```
CHECKPOINT CP-23 commit=f3f92fc6219f9d38534717982232bb8af60ba376 build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=128 passed=128 failed=0 skipped=0 trx=/work/worktrees/task-60cb0a38/.antiphon/checkpoints/20261104-090829-a181/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=f3f92fc6219f9d38534717982232bb8af60ba376 sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=f3f92fc6219f9d38534717982232bb8af60ba376 build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-60cb0a38/.antiphon/checkpoints/20261104-090829-a181/rows/CP-24/run.trx slot=granted waited=30s dirty=0 source=f3f92fc6219f9d38534717982232bb8af60ba376 sourceState=clean buildSource=verified
```

- `validate --expected-source-sha f3f92fc6...`: `CHECKPOINT SOURCE VALID rows=2`. Verdict GREEN, exit 0. Wall 9m49s; build 293 s.
- The fresh CP-23 TRX shows every new test Passed: `Refused_filing...` 1, `Settled_missing...` 2 and the 5 new spelling cases.
- CP-25 is Windows-native and was not run. The brief says so, and it is pending until the live rc run ends.
- Unlisted runs: four direct build-slot runs, made for the red and mutant evidence above.
  - c1701-red-build: waited 45 s, held 222 s.
  - c1701-red-run: waited 0 s, held 49 s.
  - c1701-mutant-build: waited 0 s, held 224 s.
  - c1701-mutant-run: waited 0 s, held 55 s.
  - c1701-mutant-build2 and c1701-mutant-run2 (after the test correction): held 133 s and 42 s.
- **Unit lane: not run.** The changed code is reached only by the Integration classes in CP-23. No Unit-category test references `DesktopLegacyReleasePolicyService`. The brief limits this task to CP-23 and CP-24 within a 120 min budget, and the server2 Unit lane takes about 3 h. Listed as deferred, not passed.
- `scripts/check-evidence-diff.ps1 -BaseRef 1470340559 -HeadRef HEAD`: commits=3 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master dd30b167a): clean.

## Pending for Mutation (method-scoped)

New PCs proposed for this card:

- **PC-69, A-1.** Mutant: re-record `passed` before the filing (the unfixed order). Witness: LP/`Refused_filing_backs_off_and_resets_when_the_facts_change` at the deltas assertion.
- **PC-70, A-3.** Mutant: drop the `heads/` prefix, or drop the suffix cut. Witness: LP/`Server_predicates_keep_the_tree("startref-queued-heads"|"startref-queued-peeled"|"baseref-heads-peeled")`.
- **PC-71, A-3 case.** Mutant: MC. Witness: LP/`Server_predicates_keep_the_tree("startref-queued-case")`.
- **PC-72, M7c-a.** Witness: LP/`Settled_missing_directory_is_never_re_checked("stat")`.
- **PC-73, M7c-b.** Witness: LP/`Settled_missing_directory_is_never_re_checked("precheck")`.

Still pending: PC-1..PC-68 and M-1..M-8 from the S4a design, with CP-25 and PC-66..PC-68 on the desktop lane after the rc run.
