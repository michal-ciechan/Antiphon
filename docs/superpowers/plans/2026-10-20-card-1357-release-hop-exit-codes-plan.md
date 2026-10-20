# CARD-1357: preserve release-hop exit codes and restore RC fixture coverage

Date: 2026-10-20. Inspected base: `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`.
Next: Code; verification design is included here. Plan performed source inspection only,
with no builds or tests. The card's exit-7-to-1 probe is prior Review evidence, not a new run.

## Ground truth and root causes

Line references below are at the inspected base.

| Card assumption | What the code does | Consequence |
|---|---|---|
| The selection hop returns the child's exit code. | `scripts/lib/release-selection.ps1:398` ends its encoded command with `& $r.script @p`. Lines 401-403 return the outer pwsh process's exit code. | Without an explicit `exit $LASTEXITCODE`, pwsh command-mode termination reduces the script's nonzero result to 1. The parent at `scripts/lib/nightly-run-impl.ps1:476` then faithfully forwards that already-collapsed value. |
| The RC run fixture reaches the self-reexec and tests hops. | `scripts/test-release-gate.ps1:995` uses `New-C487OwnedClone`; `scripts/lib/c487-harness.ps1:42` creates `.git/config` and `.git/HEAD`, not Git objects. Its Git seam returns the invented `a...a` SHA at lines 63 and 83-87. | `scripts/lib/nightly-run-impl.ps1:403` now binds selection before the hop. `scripts/lib/release-selection.ps1:196` requires a real commit; lines 46-61 then read pinned policy, catalog, selector and runtime blobs. The synthetic checkout refuses with phase `selection` at `nightly-run-impl.ps1:419`, before line 476. |
| A passing pin test or exec trace establishes forwarding. | `scripts/test-release-gate.ps1:1079` checks only the derived candidate ID; lines 1083-1095 inspect source text. The isolation test at line 1047 accepts any non-null result. Also, `scripts/lib/nightly-run-impl.ps1:77` intercepts selection-bearing calls before the exec trace and `StartProcess` seam at lines 80-82. | Early refusal can satisfy today's assertions; even a repaired fixture cannot use that old trace as proof of the hop. Read child-produced evidence. |
| The selection worker's 60-second cap is the exit-code defect. | `scripts/lib/release-selection.ps1:288` bounds the selection worker; line 322 applies the same bound to the 5.1 bridge. The workload hop at lines 401-403 is separate. | Timeout sizing needs its own measurement; increasing it does not repair either defect above. |

## Decisions

- **D-1:** Append `; exit $LASTEXITCODE` immediately after `& $r.script @p` in
  the encoded command. Keep its existing error preference, argument transport and cleanup.
  Reject caller-side remapping and special cases for 4/7: they cannot recover a lost code.
- **D-2:** Make `New-C599RunFx -Rc` opt into a real, private selection-capable
  checkout, and use it for the positive parameter-hop and master-isolation cases.
  Keep the default synthetic fixture for unrelated refusal/lock tests. Reuse the existing
  real-Git fixture pattern in `scripts/test-change-aware-release.ps1:32` and C487 workload
  stubs; no shared harness rewrite. Reject bypassing selection or restoring interception
  by the old `StartProcess` seam: either would leave the production hop untested.
- **D-3:** Require phase `hop` and a fresh child `summary.json` with the pinned identity.
  Keep existing refusal and master-file assertions. Source-text checks may remain as
  wrapper-contract checks, but cannot substitute for child execution.
- **D-4:** This repair has exactly one red witness run and one narrow green run;
  no suite/profile/lane runs. The checkpoint execution lane is **portable PowerShell 7
  script fixtures**. `GET /api/runner-defaults` (revision 2) and `GET /api/session-runners`
  were read; the catalogue had an eligible Linux runner. Resolve placement afresh at
  dispatch; omit `-Runner` and `-Platform`. There is no Windows-only acceptance here.
- **D-5:** Keep the worker's 60-second bound unchanged in this repair. Before S6-B/C
  binds a real baseline, measure worker elapsed time, Git-read count/time and archive/TRX
  volume on representative baseline histories, including a cold-cache sample. Record
  whether valid inputs approach/exceed 60 seconds before choosing a timeout change.
  This is follow-up qualification, not permission to widen a timeout to pass these tests.

## Implementation slices

### S1: commit the failing witness

Files: `scripts/test-release-gate.ps1` and
`tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs`.

Add `Test-C599_SelectionHopExitCodes` and its single TUnit method
`C599_SelectionHopExitCodes`. Use the existing ScriptHarness/C487 scratch-script pattern
to invoke the real `Invoke-ReleaseSelectionHop` with a harmless temporary child that
writes a received-value marker and explicitly exits 0, 4 or 7. Keep all inputs and output
under its private fixture root. Assert each exact code and that its child ran; carry one
space-containing argument through the existing JSON transport. Use three named harness
rows inside one TUnit execution. No production function substitution or source-string
assertion counts as this witness. Commit/push with production unchanged, then run CP-1.

### S2: fix the command and repair the positive RC fixtures

Files: `scripts/lib/release-selection.ps1`, `scripts/test-release-gate.ps1`, and
`tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs`.

1. Apply D-1's one-line production fix; keep the bridge ASCII and 5.1-parseable.
2. Implement D-2's opt-in checkout locally in the release-gate test harness. Follow
   `New-Fx`'s committed seed/local-origin pattern: real candidate commit, owned-clone
   markers, copied nightly wrappers/libs, selector script, both policy JSON files,
   `global.json`, and the fixture project/source inputs its existing workload stub needs.
   Keep policy/catalog hashes valid. Use the real `rev-parse HEAD` as `Fx.Sha` and
   `ExpectedSha`; never reuse `$ShaA` as this checkout's identity. All Git remotes are
   private filesystem fixtures; clear the synthetic Git seam for this path.
3. Reuse `Write-C487NativePassSeams` for workload I/O and its existing prerequisite
   stubs, as the change-aware fixture does. Pass private clone/state/release/coordination
   roots and `NoReport`. Force the copied wrapper to differ so self-reexec occurs.
   Leave baseline absent and selection Full: real selection binding still runs.
4. Strengthen `C599_ParameterHops`: require outer phase `hop`, no selection refusal,
   and exactly one fresh child summary. Assert its `profile=rc`, `trigger=rc`,
   `sha=expectedSha=Fx.Sha`, expected `gitRef`, and selection candidate SHA/ref/ID,
   run ID and Full mode. The summary fields come from
   `scripts/lib/nightly-tests-impl.ps1:698`; inspect the bound selection artifact too.
5. Strengthen `C599_MasterStateIsolation` to prove the same child execution before
   accepting the four byte-identical master files. Preserve the master-state-root refusal.
   Update both TUnit wrappers' exact harness-row counts and required labels; a non-null
   result or pre-hop refusal must fail the new reachability assertion.

Commit/push S2, then run CP-2. No publisher/promoter invocation and no creation, push,
move or deletion of any Git tag, including inside fixtures. No release qualification,
real native workload, or external report delivery is required or permitted by this plan.

## Verification design

### Inspection and assertions

Inspected the hop, selection-worker admission, both nightly-run call paths, C487 clone
and workload seams, C599 wrappers, and the existing C1313 real-Git fixture.

- **V-1:** `C599_SelectionHopExitCodes` exercises the actual encoded child process.
  Before the production fix, the 7/4 cases must report 1; after it, exact 0/4/7 survive.
  A launch, build, fixture or zero-test failure is not the intended red witness.
- **V-2:** `C599_ParameterHops` proves real selection admission and both hops through
  the child's consumed RC identity. An early selection refusal fails even if candidate
  ID derivation and static wrapper declarations still pass.
- **R-1:** Retain the existing parameter-refusal assertions in that same method.
- **R-2:** `C599_MasterStateIsolation` proves the real RC fixture leaves all four
  master state files untouched and still rejects the default master state root.

Guard inventory: V-1 guards the missing explicit exit; V-2/R-2 guard a false-positive
fixture that stops before the hop. CP-1 supplies the requested red-first evidence;
no additional mutation battery is commissioned. Publication, real baseline scale,
Windows PowerShell 5.1 execution and full release credit are outside this selection.

### Execution and cost

Code budget: **45-60 minutes** (25-40 authoring/inspection plus 20 checkpoint minutes).
Run the checkpoint tool once per committed slice group: `run --plan <this-file> --rows CP-1`
at S1, then `--rows CP-2` at S2, each with `--expected-source-sha <that-commit>`.
Launch the dotnet tool through `scripts/build-slot.ps1`; await `wait` until exit is not 75.
Preserve the expected CP-1 red receipt without relabeling it green. CP-2 is the final
ordinary scope, with a fresh isolated build at S2. Retain SHA-bound CHECKPOINT lines,
TRX counts and named harness assertions in the report. Validate CP-2's receipt, run
`scripts/check-evidence-diff.ps1` over base..HEAD, and remove owned alternate outputs.

### Checkpoints

Both rows use the portable PowerShell 7 script-fixture lane; the green filter names
three methods, not a category, whole class, or release lane. The method suffix
wildcards accommodate the pinned TUnit hint extractor; require exactly the three
named methods in the TRX, with no additional matches.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1357-red/` | portable-hop-red | `/*/*/ReleaseGateRunTests/C599_SelectionHopExitCodes` | V-1 | exactly 1 executed, 1 failed, 0 skipped; exact non-1 exit assertion fails | 1 | 10 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1357-green/` | portable-hop-green | `/*/*/ReleaseGateRunTests/(C599_SelectionHopExitCodes*)\|(C599_ParameterHops*)\|(C599_MasterStateIsolation*)` | V-1, V-2, R-1, R-2 | exactly 3 executed, 0 failed/skipped; all named harness rows pass | 3 | 10 | true |
