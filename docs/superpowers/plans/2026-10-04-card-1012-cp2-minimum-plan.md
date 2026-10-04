# CARD-1012: raise the CARD-0927 image-contract execution floor

Status: Plan complete; separate TestDesign freeze next. No implementation or test
execution was performed in this Plan dispatch.

Card: Antiphon / CARD-1012, read with `scripts/card.ps1 get` and `history` on
2026-10-04. It has zero revisions and no terminal verdict. Inspection base and
freshly fetched `origin/master`: `2d3c582416c5610d72e53df3e790bc114d87ad82`.

## Outcome and acceptance

Change only the `Min` cell of CP-2 in
`docs/superpowers/plans/2026-10-03-card-0927-pinned-jq-plan.md`, from `100` to
`163`. Preserve that row's filter, build, group, slice binding, expectation,
coverage, time estimate and serial setting, and every other row and paragraph.
The existing driver must receive `minExecuted: 163`; ordinary image-contract
verification must execute the four intended classes with zero failures/skips.

This is an unsatisfied manifest correction. The target row still says `100` on
both the inspection base and fetched master. CARD-1005, CARD-1006 and CARD-1015
are Done, but none supersedes this change.

## Ground truth

| Card assumption | Repository / board fact | Consequence |
|---|---|---|
| CARD-0927 CP-2 still has `Min 100`. | Target plan's `### Checkpoints` has exactly that value; its last edit was the CP-3 correction at `a70cb6668`. | One cell needs changing. CP-3 stays 84. |
| The reviewed image-contract roster is 163. | Card cites Final review at `7bd6dd8613bc855ad3f63eff5daf888e003aaeb7`. All four test files are byte-identical between that source and this base. Source attributes expand to Docker 112, Codex 11, Grok 26 and jq 14. This Plan did not rerun them. | Use 163 as the minimum; retain the historical review's SHA rather than claiming fresh runtime evidence. |
| A missing jq class can pass the old floor. | 149 remaining cases exceed 100, but `PlanTableImporter.RosterTokens` also derives class expectations. `RowRunner` checks class-name presence independently, including skipped names. Complete class disappearance is already caught by that check; partial losses within present classes can still clear 100. | Correct the count gap without replacing roster checks or overstating what the old driver permits. |
| The count is manifest data, not a production defect. | `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs` maps `Min` to `MinExecuted`; `Execution/RunScheduler.cs` forwards it; `Execution/RowRunner.cs` returns exit 3 below it. `scripts/run-checkpoint.ps1` enforces the same floor. | No driver/parser, runtime, image, or test-source repair is needed. |
| Recent checkpoint work might have solved it. | CARD-1005 added an opt-in selected-class census; CARD-1006 changed Grok sign-in wording/qualification; CARD-1015 changed evidence Git policy. Target CP-2 remains 100. `scripts/lib/checkpoint-usage.ps1` still has literal `selected = 377`. | Keep the census opt-in, literal 377 and shared tooling untouched. |

Source roster, all in `tests/Antiphon.Tests/Infrastructure/`:

| File | Source methods | Expanded cases | Expansion |
|---|---:|---:|---|
| `DockerStackContractTests.cs` | 112 | 112 | Single-result methods. |
| `CodexRunnerImageContractTests.cs` | 11 | 11 | Single-result methods. |
| `GrokRunnerImageContractTests.cs` | 2 | 26 | One single-result method plus 25 `Arguments` rows. |
| `JqRunnerImageContractTests.cs` | 3 | 14 | Two single-result methods plus 12 `Arguments` rows. |
| Total | 128 | 163 | Execution floor counts results, not methods or internal assertions. |

## Decisions

- **D-1 — Set 163 exactly.** This is the explicit card request and the supported
  current roster. Reject retaining 100 or choosing another margin: either leaves
  the recorded gap. A minimum is not an exact-roster census; future additions
  remain allowed, and their counts must be reported.
- **D-2 — One implementation cell; no new tests.** Existing import and execution
  logic already enforce numeric floors. Reject a new parser feature, a test that
  merely pins this Markdown constant, or changes to test assertions/timeouts.
  Those enlarge a reversible documentation fix without improving its evidence.
- **D-3 — Preserve the original manifest.** Reject changing CP-2's filter or
  rewriting historical receipts, CP-3/CP-4 floors, or CARD-0927's narrative.
  Verify the actual edited manifest as well as the selected classes.
- **D-4 — Keep TestDesign separate.** The brief did not fold that stage into Plan.
  The verification design below is the proposed closed list to freeze before
  Code; no product decision or human approval is outstanding.
- **D-5 — Use the live default runner and bounded regression scope.** No host
  pin or admission/compatibility rule is part of this change. Preserve
  allow-by-default compatibility gating and regression-only review verdicts.
  Include the default Unit lane once, reusing the image-contract build; its
  unrelated inherited failures/skips must be disclosed and evidenced, not repaired
  or silently waived under this card.

## Slice and scope

| Slice | Files changed | Work | Tests / completion evidence |
|---|---|---|---|
| S1 | `docs/superpowers/plans/2026-10-03-card-0927-pinned-jq-plan.md` | Edit only CP-2 `Min`: 100 to 163. Commit and push before execution. | V-1 exact diff and real importer check; V-2 four existing infrastructure classes; R-1 default Unit lane. No test files change. |

This Plan adds only this artifact. TestDesign may amend this artifact to freeze
verification. The eventual Code footprint is the single target-plan cell;
generated receipts/TRX/logs remain ignored under CARD-1015. Any optional small
Markdown evidence must be committed individually, before final source
qualification. Never force-add checkpoint directories or `.antiphon/reports`.

## Platform and collisions

Read `GET /api/runner-defaults` and `GET /api/session-runners` at 2026-10-04
00:05 UTC. Defaults revision 2 selects an available, eligible Linux runner;
an eligible Windows runner also exists and a spare entry is unavailable/draining.
These are observations, not placement pins. Re-read at dispatch. Omit `-Runner`
and `-Platform`: CARD-1012 remains Any. Use `-Platform Any` only to clear a prior
pin. The named checkpoint lane is **image contracts / Unit**, preferably on the
default Linux environment with bash. On Windows, the existing shell helper needs
WSL; a missing shell is not an acceptable image-contract pass.

| Concurrent work named in brief | Observed overlap and ordering |
|---|---|
| CARD-0959 Code `bd02f8d9` | Board-scoped task read still Dispatched; inert Codex observation work. No direct target-file overlap. Do not restore version-floor admission refusals from its old description. |
| CARD-1011 Code `d422c5a9` | Still Dispatched; prompt/routing and Grok qualification docs can affect existing source-contract assertions and Unit tests. No target-plan overlap. Verify against the actual Code base; classify unrelated red at its unchanged base. |
| CARD-1013 Code `2c35a27d` | Still Dispatched; owns coverage tooling and `.gitattributes`. Shared tooling dependency, no required edit overlap. Do not modify/import its source changes or update its census. If its landing changes checkout bytes or test selection, recheck this plan's importer/roster assumptions at the new source. |
| CARD-1020 TestDesign freeze | Still Dispatched; native process cleanup. This card introduces no native host run or process-lifetime change. Leave its files/freeze untouched. |
| CARD-1017 TestDesign freeze | Card now Review; absent from the active board-scoped task list. Whole-worktree cleanup is separate; preserve current evidence rules and do not add cleanup authority here. |
| CARD-1022 and CARD-1023 plans | Cards now Review; absent from that active task list. Backend removal and compatibility matrix remain separate. No backend, routing, catalogue, or compatibility edits. |

No direct file collision requires serializing the one-cell edit behind these
cards. Recheck occupancy and declared footprints before Code; any newly observed
same-file owner must settle first. Keep this task branch fast-forward-only; do
not rebase it to integrate concurrent work. Landing owns target integration.

## Activation order

1. Commit/push this plan, then complete the separate TestDesign freeze.
2. Code applies S1, commits/pushes it and runs the frozen checkpoints at that SHA.
   Review independently checks the single-cell diff and ordinary evidence.
3. Land through the normal landing owner. The revised floor becomes effective
   when a caller imports the revised CARD-0927 plan from that commit. A previously
   imported/detached manifest is a snapshot: import again to use 163; do not
   rewrite a completed or running checkpoint's evidence.
4. No AppHost restart, runner rollout, image rebuild/deployment, migration or
   live-session operation is needed. Existing CARD-0927 Mutation obligations
   remain attached to their original owner. This correction adds no production
   mutation target; TestDesign should record zero new PCs explicitly.

## Verification design

Proposed ordinary scope for the separate TestDesign freeze:

- **V-1 — Actual manifest change.** Compare the target file against the Code task
  base: exactly one cell changes, CP-2 100 to 163. Import the edited CARD-0927 plan
  through the existing checkpoint tool and inspect CP-2's `minExecuted = 163`,
  all four roster tokens, and the unchanged filter/build/other rows. This
  read-only import is metadata inspection, not a substitute for test execution.
  Do not execute the original plan's historical red-first CP-1.
- **V-2 — Image-contract execution.** Run CP-1 below; it repeats the target
  CARD-0927 CP-2 filter and floor with a task-owned output path. Fresh results
  must contain the four classes and the source-backed breakdown 112/11/26/14 at
  this base, with at least 163 executed and zero failed/skipped. If concurrent
  source changes grow the roster, explain the extra cases; never lower 163 to
  accommodate a loss. This row verifies existing contracts, not a Docker image
  rollout or provider authentication.
- **R-1 — Default Unit regression lane.** CP-2 below runs the existing Unit
  category once using CP-1's build. Its conservative 2000 floor is the unchanged
  historical Unit minimum, not a claim about today's full count. Report the
  actual roster and counts. Reproduce any failure attributed to inherited work
  on the unchanged Code base, using only the failing method/class. Strict source
  qualification and any inherited-skip disposition follow the owner policy; do
  not label a receipt clean when its validator refuses it.

Run the checkpoint tool against this artifact after S1 is committed, bound with
`--expected-source-sha <full-code-sha>`. Bootstrap the tool only if necessary,
through `scripts/build-slot.ps1`, using a separate `bin-c1012-tool/` output;
record that tooling bootstrap separately from the checkpoint build. Use the
already-built tool for the V-1 `import --plan` inspection and for
`run --plan docs/superpowers/plans/2026-10-04-card-1012-cp2-minimum-plan.md`.
The tool acquires its own row slots; do not nest it inside a slot-owning wrapper.
Await every run, calling `wait` again after exit 75. Exit 4 is not run/blocked,
never permission for an unleased retry. Source stays frozen throughout execution.

Keep unedited CHECKPOINT lines, per-class counts, actual tested SHA and receipt
provenance in the stored report; validate source receipts against that SHA.
Code/Review also run `scripts/check-evidence-diff.ps1` over the full task range.
Use the tool's owned-output cleanup and remove any bootstrap `bin-c1012-tool/`
directories before settlement. No new test source, census flag or literal change
is required; `scripts/lib/checkpoint-usage.ps1` remains 377.

### Cost

One isolated test build shared by two serial rows: estimated ordinary floor
11 minutes (3 + 8), plus editing, metadata inspection and any tool bootstrap or
slot wait. No full assembly, image build, native acceptance, repeated proof or
deliberate mutation run is in this ordinary scope. TestDesign confirms the
checkpoint/import commands and count assumptions before handing off to Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1012-contract/` | image-contracts-unit-lane | `/*/*/(JqRunnerImageContractTests*)\|(DockerStackContractTests*)\|(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)/*` | V-2 | all four full classes; >= 163 executed, zero failed/skipped | 163 | 3 | true |
| CP-2 | S1 | CP-1 | unit-regression-lane | `/*/*/*/*[Category=Unit]` | R-1 | whole Unit lane, zero failed; actual count and inherited platform skips individually accounted | 2000 | 8 | true |
