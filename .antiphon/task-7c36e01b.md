# Review 7c36e01b — CARD-0824 S1c slot role allow-list and sticky decision (round 1 of 3)

Outcome: CLEAN. No numbered defects. Tested SHA `69d15cd6fde113a5d1493ccd8256cafe88ca3ecf` (subject branch
`feat/card-task-6db87da1`, tip `13d5f15e0e307ef6fd308db3c3e0ab348e3b9b7a` adds only the Code report). Base `38e67ae15`.
Landing owner: Code task 6db87da1.

## Safety verdicts

1. **Gate.**
   - `*` admits every role except Mutation (`Admits`: source_landing → roles_none → mutation → all/listed).
   - `none` admits nothing. The gate is consulted only in `DecideSlot`, so the release service, the reconciler and the
     hosted loop never read it. V-12 (both passes) and V-13 (removed role released) witness this.
   - Names are matched exactly (ordinal against `Enum.GetNames`). The grammar refuses whitespace, empty entries,
     unknown names, numbers, `Mutation`, and `*` or `none` mixed with names.
   - A bad value at startup fails the host (`ValidateOnStart`, T-D-1 by design). A bad hot reload makes
     `CurrentValue` throw `OptionsValidationException`. The gate catches that and returns `roles_invalid`: no lease,
     one warning per spell, never a throw.
   - With `roles_invalid` the task gets the legacy mirror. The V-11 `invalid` case shows the exact legacy request, the
     `task-<id8>` tree, the row path, no `workspace_slot_unavailable` and no Warning.
   - The gate is the only `IOptionsMonitor<DelegationSettings>` consumer, so the new shape failure cannot break
     another reader.
   - No null or absent path widens the gate:
     - absent → `*` (the documented default);
     - JSON `null`, `""` or an empty env var → refused (shape check or grammar);
     - `[]`, `["Plan"]`, or a layered array over a string → refused by the shape check (children present);
     - with no monitor, the gate falls back to `*`, and only in containers where the flag is off by default.
   - The pool flag gates first in both `DecideSlot` and `ResolveSlotDecisionAsync`, so `*` as the default is
     harmless with the flag off (V-17 `flag-off:*`, V-18).
2. **Sticky decision.**
   - A preparation is (task, attempt, runner, store), and every resend reuses its `TaskWorktreeMirrorBinding`
     row (`BeginMirrorPreparationAsync`).
   - The decision event is saved before `MirrorAsync`, and a crash after the save makes the retry reuse it (V-16).
   - A lost reply followed by `none` resends in the slot shape and gets the same Leased slot back, with no
     `task-<id8>` beside it (V-15 `slot-then-none`). The reverse flip is covered by `legacy-then-listed`.
   - A 409 refusal clears the decision and the next send decides again (`slot-refused-then-none`).
   - The query filters by task id and by `preparation=<D>`, so different tasks never share a decision.
   - The preparer is single-flight per task (`_inFlight`).
   - Type 41 does not collide (the enum ends at 40). `AgentTaskEvents.Type` is a plain `integer` in the snapshot
     with no CHECK constraint, so no migration is needed.
3. **SourceLanding is refused in both places.**
   - `WorkspaceSlotRoleSet.Admits` checks it first, whatever the value (V-9).
   - The dispatcher guard `AgentTaskDispatcher.cs:6149` is unchanged. R-2 `DefaultRunnerPinTests` is green.
4. **Deviations 1-6.** None weakens a witness.
   - V-5 still goes red without the shape validator: `["Plan"]` would bind to `*` and not throw, and `[]` would
     carry no "single string" failure.
   - V-11 "never holds" still catches a hold (`workspace_slot_unavailable`) and a rethrowing gate (Warning). The
     legacy tree and row path show the task is not blocked.
   - Review in place of Code in V-11, the `mutation` reason, the 80 s budget (2× the measured 38.6 s) and the
     V-12/V-14 sequencing keep their oracles.

## Runs (server2, Linux)

- **Checkpoint run `20261103-224759-baf5`** (tool; one build, 322 s; 15 s slot wait per row):
  - `CHECKPOINT CP-21 ... executed=50 passed=50 failed=0 skipped=0 ... sourceState=clean buildSource=verified`
  - `CHECKPOINT CP-22 ... executed=79 passed=67 failed=12 skipped=0 ... sourceState=clean buildSource=verified`
  - Verdict RED; `validate` gives `report_failed`.
  - CP-22's failures are the same 12 as Code's: 11 `C0824WorkspaceSlotServerTests` cases hit their class 35 s
    timeout, and `RemoteWorkspacePreparerTests.Cancel_of_a_queued...` saw no request in 10 s.
  - Load was 36. All 79 tests ran at once, and the passing tests in the sibling classes took 29-42 s.
- **Failures rerun alone** (unlisted, build-slot, kept output, the same 12 tests):
  - first: 11/12 red at load 35, all 35 s timeouts;
  - second: 12/12 green at load ~33 (39 s run wall).
- **Base comparison** (unlisted, inherited-red check): detached `38e67ae15`, build-slot build 223 s, then the same
  12 tests: 12/12 green, 35 s run wall at load ~30.
  - The tests are near their 35 s budget at base as well.
  - Two of the timed-out cases are `C0824_FlagOffMirrorRequestUnchanged`. Flag-off returns from
    `ResolveSlotDecisionAsync` before any read, so S1c adds no work there.
  - Verdict: an inherited budget/load flake, not a regression.
- **Mutant** (unlisted, one build lease plus one run lease):
  - Change: `ResolveSlotDecisionAsync`'s reuse condition became `if (false && ...)`, so a resend re-reads the setting.
  - V-15 went 2/4 red on the expected assertions:
    - `slot-then-none`: `Mirror(requests[1]).Slot`;
    - `legacy-then-listed`: the legacy-shape field set.
  - Restored with `git checkout --`. The tree is clean.
- **check-evidence-diff** `38e67ae15..69d15cd6f`: `commits=3 entries=0 violations=0`. Over `..13d5f15e0`:
  `commits=4 entries=1 violations=0` (the report file).
- **Landability** against `origin/master` `97e77f9f1`: `merge-tree --write-tree` is clean for both `69d15cd6f` and
  `13d5f15e0`.
- **C1323 `LinuxOnlyWindowsExclusionPolicy`** on the merged tree (scratch merge of `97e77f9f1` + `69d15cd6f`):
  6 passed, 0 failed (`C487 HARNESS EXIT CODE: 0`).
  - `tests/test-execution-policy.json`, the live-lane pin and the catalog are untouched in `38e67ae15..69d15cd6f`.
- **Second checkpoint run `20261103-231513-658c`** (tool, cold build, load 26 → 38):
  - CP-21: 50/50.
  - CP-22: 67/79. All 12 failures are `C0824WorkspaceSlotServerTests` 35 s timeouts. Verdict RED.
- **Base/tip/base comparison of the whole CP-22 filter** (unlisted, inherited-red check: a fresh build of base
  `38e67ae15` and the kept tip output, one build-slot lease each, run back to back):
  - base A: 79/79 green, 48 s, at load ~30;
  - tip B: 58/79, 21 failed, 63 s, at load ~18 → 35;
  - base C: 58/79, 21 failed, 61 s, at load ~35 → 46.
  - B and C fail the IDENTICAL 21 `C0824WorkspaceSlotServerTests` cases, all with `TimeoutException`.
  - **CP-22's red is inherited from base. It is a load and budget flake, not an S1c regression.**
- **Receipts.** No validatable clean receipt exists from this Review: both tool runs are RED on CP-22
  (`validate` gives `report_failed`). Code's validated receipts at the same SHA are runs `20261103-223234-bc8e`
  (CP-21) and `20261103-221744-828b` (CP-22).

## Advisories (no Review rerun needed)

- **A-1. The clearing rule is broader than T-D-4, and the deviation list does not say so.**
  - T-D-4 says a definitive refusal is an Error frame with StatusCode 409. The preparer clears on any
    `ConflictException`. The client raises that for 409 and for every status except 404 and 503, which includes
    the runner's 400 and its Internal error frame.
  - This is safe in practice. `RunnerWorkspaceSlotPool.LeaseAsync` sets Leased as its last atomic step (a ledger
    `File.Move`) and then returns, so a non-409 error frame never leaves a Leased record.
  - The worst case is a leftover `task-<id8>` after a failed legacy send, which retention retires.
  - The code comment documents the choice. A Docs touch-up to T-D-4 would align them.
- **A-2. The flag and capability checks come before the recorded-decision read.**
  - A runner that reconnects without `workspaceSlotV1` (a runner rollback) gets the legacy shape even when the
    preparation's recorded decision is slot.
  - This is S1's existing behaviour, the same class as the flag-off stranding the design puts out of scope.
- **A-3. `C0824WorkspaceSlotServerTests` `[Timeout(35_000)]` (CARD-1660) is too tight under server2 load ~35.**
  - It went red in 4 of 5 tip runs at load 33-38, and base went red in the same way (base C above).
  - Every remaining CP-22 run on server2 stays a coin flip until it is re-budgeted.
  - Suggest a Debug card to re-budget it at 2× the loaded p95. This is not S1c's change.
- **A-4. Amendment lines 90 and 344 still describe `string[]`.** The Code report already flags this; it is a
  Docs touch-up.

## Not run

- **The whole Unit lane.** The brief sets an Interim scope of named rows only (CP-21 and CP-22), accepted by the
  operator for master-only work.
- **Windows.** Nothing runs on the desktop.
- **PC-1..PC-28 and M-1..M-8** stay pending for the method-scoped Mutation stage. The one mutant above does not
  discharge any of them.
