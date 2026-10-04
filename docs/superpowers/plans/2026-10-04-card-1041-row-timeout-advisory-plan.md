# CARD-1041: describe the checkpoint row timeout advisory accurately

Date: 2026-10-04. Plan source: `c18148f03f82c8fa5ad4e541a594630404a92135`.
Card: Antiphon / CARD-1041, read through `scripts/card.ps1 get`.
Complexity: **easy**; verification design is folded into this plan. Next: **Code**.

Correct the documentation and importer diagnostic to describe 45 minutes as an
advisory threshold. Preserve the existing timeout calculation and admission rules.
An estimate of 16 continues to import successfully with a 48-minute row timeout.
The operator can then see why splitting the row is suggested without being told
that execution is capped at 45 minutes.

## Ground truth

These are source observations, not results from running tests during Plan.

| Card assumption or question | What the current code does | Consequence |
|---|---|---|
| The documentation describes a 45-minute ceiling. | `docs/testing-and-build.md:326` calls it a row ceiling; `PlanTableImporter.cs:19,153-155` names `RowTimeoutCeilingMinutes` and emits a ceiling warning. | The mismatch is real in both documentation and user-visible diagnostics. |
| Estimate 16 might be clamped to 45. | `Execution/RowTimeout.cs:5-12` returns `max(15, 3 * estimate)` when there is no positive explicit timeout. The importer assigns that value before testing `3L * estimate > 45`. | Estimate 15 gives 45 without this warning; 16 gives 48 with a warning; 20 gives 60 with a warning. Import remains successful. |
| Perhaps the runtime enforces a later cap. | `Execution/RunScheduler.cs:422-425` takes the row timeout or derived fallback, then the CLI row override; it contains no 45-minute cap. `RowTimeout.RunWithDeadlineAsync` uses the supplied deadline. | Changing only the importer to reject or clamp would create a policy discrepancy with YAML and CLI paths. |
| A validator might supply the limit. | `ManifestValidator.cs:56-59` checks positive, non-overflowing estimates, including the Windows estimate. It has no 45-minute timeout rule. | Preserve numeric validation; do not present an advisory as an enforced maximum. |
| Explicit timeouts may exceed 45. | `DeriveRowMinutes` returns a positive explicit timeout unchanged; without an estimate it returns `max(15, manifestDefault)`. The scheduler also accepts row and CLI overrides without a 45-minute clamp. | Document the absence of this particular cap, without promising arbitrary timer durations are supported. |
| Above-45 behavior could be accidental. | `CheckpointImportTests.warns_when_estimate_exceeds_row_timeout` explicitly expects estimate 20 to produce timeout 60. `CheckpointTaskOwnershipTests.windows_estimate_selects_row_and_total_deadlines` expects the Windows estimate 27 to produce timeout 81. | Existing tests support preserving behavior. A hard cap would be a separate behavior change. |
| A long row timeout guarantees that much run time. | `DeriveTotalMinutes` computes `max(30, 2 * sum(estimates) + 10)` absent an override. The Windows-estimate test expects total 64 while its row timeout is 81. | The independent total-run deadline can stop a row earlier. The documentation must distinguish the two budgets. |
| This explains CARD-1021 failures. | The card states that no causal connection is proved. This inspection establishes a wording mismatch only. | Do not claim to diagnose or repair CARD-1021 or broaden into related CARD-1040. |

Paths without a prefix in this table are beneath `tools/Antiphon.Checkpoints/`;
named tests are beneath `tests/Antiphon.Tests/Checkpoints/`.

## Decisions

- **D-1 — Correct the contract wording; keep runtime behavior.** The brief
  authorizes this choice, and existing tests deliberately admit 60- and 81-minute
  timeouts. Reject a clamp: it would silently shorten supported runs. Reject
  admission rejection: it would refuse existing manifests and require consistent
  policy across importer, YAML, CLI, scheduler and validation. Neither behavior
  change has evidence or operator authorization in this card.
- **D-2 — Correct both the owner documentation and the diagnostic.** Rename the
  private constant to `RowTimeoutAdvisoryMinutes`, keeping its value 45 and the
  strict `>` comparison. Say that the timeout is not capped and suggest splitting
  the row. Reject a documentation-only edit because the CLI would still assert
  the false ceiling; reject deleting the warning because it is useful planning
  feedback. No schema, exit-code, deadline or configuration changes.
- **D-3 — One implementation slice, one changed behavior, one positive control.**
  Strengthen and rename the existing importer warning test instead of adding
  tests or argument-expanded cases. Keep its single TUnit result, so the
  checkpoint namespace census does not change. Reuse two existing exact methods
  for timeout arithmetic and platform-estimate compatibility. Reject a whole
  Unit/namespace/class run and a real 45-minute sleep: neither adds useful
  evidence for this diagnostic correction.
- **D-4 — Portable execution using live placement.** On 2026-10-04 the required
  `GET /api/runner-defaults` read returned revision 2 with a configured global
  preference and no per-kind overrides. `GET /api/session-runners` returned
  eligible Linux and Windows lanes and an unavailable additional lane. This
  change needs no particular OS or host. Resolve placement again at dispatch;
  omit `-Runner` and `-Platform`, or use `-Platform Any` only to clear a prior
  platform pin. No fleet location belongs in the plan's commands.

These are adopted decisions within the brief's scope, not unresolved defaults
requiring a Decide stage.

## Slices

### S1 — Correct the advisory wording and its regression test

Change exactly these implementation files:

1. `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`: rename the private
   constant as in D-2; keep the derivation and comparison unchanged. Use this
   diagnostic contract, substituting the actual row ID and derived minutes:
   `CP-1: derived row timeout (48 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)`.
2. `docs/testing-and-build.md`, Checkpoint runner tool paragraph: replace the
   ceiling claim with an advisory-threshold description. Explain that imported
   row timeouts are `max(15, 3 * selected EstimatedMinutes)`, including the
   platform-selected estimate; 15 gives 45 without the warning and 16 gives 48
   with a warning. Positive explicit timeouts and the manifest fallback are not
   capped at 45. State that the independent total-run deadline still applies.
   Preserve the existing land-class warning guidance and other runner details.
3. `tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs`: rename
   `warns_when_estimate_exceeds_row_timeout` to
   `warns_when_estimate_exceeds_advisory_threshold`. In that same single `[Test]`
   method import independent one-row tables for estimates 15, 16 and 20. Assert
   successful imports and literal expected timeout values 45, 48 and 60; assert
   no warnings for 15 and exactly one warning with the full D-2 diagnostic for
   each of 16 and 20. Use explicit expected values/text, not the production
   constant or `RowTimeout` to calculate the oracle.

The test body's three examples still count as **one** executed TUnit test. No
new fixtures or helpers are required. `RowTimeout.cs`, `ManifestValidator.cs`,
`RunScheduler.cs` and all existing limits remain unchanged. Do not rewrite
historical plans or generated card documents.

Commit and push S1 before CP-1. Run the closed checkpoint list once against that
commit. If a row fails, diagnose and rerun only the affected row after a committed
fix; confirm any claimed inherited failure with that exact method at the base.
Keep generated evidence ignored. Run `scripts/check-evidence-diff.ps1` over the
full Code task base-to-pushed-tip range and report the result. Review audits the
wording against the source table and the ordinary receipts before land.

## Verification design

### Inspection

| Bodies inspected | Boundary or disposition |
|---|---|
| `RowTimeout.DeriveRowMinutes`, `DeriveTotalMinutes`, `RunWithDeadlineAsync`; scheduler timeout selection | R-1 and R-2; no deadline policy changes. |
| `PlanTableImporter.ImportMarkdown`, estimate validation, platform selection, warning branch; `ManifestValidator.Validate` | V-1 and R-1; advisory threshold versus admission and returned timeout. |
| Existing importer warning test and its inline Markdown table | Extend this exact fixture for 15/16/20; no external setup. |
| `TimeoutTests.row_deadline_is_max_15_or_3x_estimate` | R-2; four existing assertions cover the floor, scaling, absent estimate and positive override. |
| `CheckpointTaskOwnershipTests.windows_estimate_selects_row_and_total_deadlines` | R-3; explicit fake platform/driver, managed temp roots, imported 27/81-minute row values, 64-minute total and 7/11 overrides. Runs without Windows or a real long wait. |
| `CheckpointTestBase` before/after hooks and `TempDir`/`TinyToolDirectory` | Existing managed temporary-file custody is reused; no new helper setup. |

### Delivery inventory

No asynchronous delivery path is introduced or changed. The synchronous import
result and its diagnostic are the observable product surface. No queue,
persistence/recovery protocol or session receipt needs new coverage.

### Proves it works now

- **V-1:** The importer tells the truth about its advisory threshold. The renamed
  importer test imports estimate 16, returns exit 0 and timeout 48, and reports
  exactly the D-2 warning. The same test checks the non-warning boundary at 15
  and preserves the existing above-threshold example at 20. CP-1.
- **V-2:** The owner documentation accurately explains the threshold, selected
  estimate formula, explicit/default paths and independent total deadline.
  Review the S1 diff against the ground-truth table. This is a prose audit, not
  a new source-text test or a build. The old ceiling phrase and old constant
  name must be absent from the changed owner paragraph/importer, while the
  four-land-class warning remains unchanged.

### Guards the regression

- **R-1:** Warning is advisory and strictly above 45: the renamed importer test
  requires no warning at estimate 15, and successful import with 48/60-minute
  values at 16/20. The full warning must say advisory and not capped. CP-1.
- **R-2:** Existing floor, scaling, missing-estimate fallback and positive
  explicit override semantics remain intact. Run only
  `TimeoutTests.row_deadline_is_max_15_or_3x_estimate`; its expected values remain
  15, 30, 15 and 7. CP-2.
- **R-3:** Platform selection continues to allow above-45 timeouts and keeps the
  total budget separate. Run only
  `CheckpointTaskOwnershipTests.windows_estimate_selects_row_and_total_deadlines`;
  require all its existing assertions, including row 81 versus total 64. CP-3.

### Guard inventory

No safety-critical execution, cancellation, ownership or admission guard is
modified. Safety-critical guards added/changed = 0; unmapped = 0. The single
changed diagnostic behavior has one regression guard:

- **G-1:** D-2/V-1/R-1: a successful above-threshold import must describe an
  advisory and return the unchanged derived timeout. Map to **PC-1**.

Diagnostic guards = 1; mapped = 1; missing = 0; duplicate PC mappings = 0.
R-2/R-3 reuse existing behavior tests; no extra PC battery for unchanged runtime
paths is commissioned by this wording correction.

### Positive controls

- **PC-1:** In a separately commissioned post-land SourceLanding Mutation task,
  replace only the new warning expression in `PlanTableImporter.cs` with its
  former diagnostic, using the renamed constant so the mutation compiles:
  `warnings.Add($"{id}: 3 x EstimatedMinutes ({3L * row.EstimatedMinutes}) exceeds the {RowTimeoutAdvisoryMinutes} minute row-timeout ceiling");`.
  The exact filter is
  `/*/*/CheckpointImportTests/warns_when_estimate_exceeds_advisory_threshold`.
  Require baseline green, mutation red at the estimate-16 full-warning
  assertion, exact source restoration, then restored green. Every phase must
  execute one test; the red phase must have the expected assertion failure,
  not a build/setup failure or zero tests. Do not change the test or deadline
  formula in the mutation. This reinstates the actual reported defect.

Code runs ordinary V/R only. Review assesses that evidence and this executable
PC design. Mutation runs after confirmed land, follows the owner's copied
`run-checkpoint.ps1` method-scoped protocol, and keeps baseline/red/green logs,
fresh TRX, restoration records and separate alternate outputs in its assigned
external evidence custody. The SourceLanding no-commit rule applies. Restore
timestamps and rebuild the restored source before accepting green; await every
phase before editing. One PC for this one changed behavior.

### Out of scope

Hard timeout enforcement, new override validation, arbitrary large-duration
timer support, scheduler/cancellation changes, live runner changes, CARD-1021
causality, CARD-1040, benchmark runs and whole-Unit verification are excluded.
The preserved behavior does not authorize raising unrelated test deadlines.

### Cost and execution

All numbers are estimates. Code budget: **30-45 minutes**, within the requested
30-60: 12 minutes for S1 edits and diff review, 10 ordinary checkpoint minutes
(8 + 1 + 1), 5 for receipt/evidence audit and cleanup, plus 3-18 minutes of
contingency. Slot delays are reported separately; exit 4 is not permission to
bypass the gate or broaden the run.

Ordinary V/R floor: **10 minutes**, one isolated test-project build and three
one-method executions (3 results total). Review uses those receipts; any rerun
needs a stated reason. PC floor: **20 minutes** estimated in the later Mutation
task, comprising 2 minutes of setup/restoration audit plus 6 minutes each for
one-method baseline/red/restored-green builds and runs. Combined planned Code
and Mutation budget: **50-65 minutes** across separate stages. Broader-suite
savings are not claimed as measured; three methods and one build bound ordinary
cost without adding another whole-Unit run.

Lane for **every CP row:** portable .NET, supported Linux or Windows, using live
runner defaults and the host build-slot gate; no pinned host. CP-3 simulates the
Windows estimate and does not require Windows placement.

Use `tools/Antiphon.Checkpoints run --plan` once after S1 with
`--after S1 --expected-source-sha <the committed S1 SHA> --parallel 1`.
If tool bootstrap requires a build, use `scripts/build-slot.ps1`, an isolated
`bin-c1041-tool/` output, then launch the built tool with `dotnet exec`; count
this as tool bootstrap rather than a second test-project checkpoint build.
Await `run`/`wait` until the result is not 75. Never leave a running executor at
settlement. The checkpoint tool owns the row leases. Preserve all three
unedited CHECKPOINT lines, exact tested SHA, executed/passed/failed/skipped
counts and source/build provenance in the task report. Expected ordinary result
is three passed, zero failed/skipped. Remove only task-owned alternate outputs
after execution, including tool bootstrap output if created.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1041/` | advisory-wording | `/*/*/CheckpointImportTests/warns_when_estimate_exceeds_advisory_threshold` | V-1, R-1 | all listed, 0 failed | 1 | 8 |
| CP-2 | S1 | CP-1 | timeout-arithmetic | `/*/*/TimeoutTests/row_deadline_is_max_15_or_3x_estimate` | R-2 | all listed, 0 failed | 1 | 1 |
| CP-3 | S1 | CP-1 | platform-estimate | `/*/*/CheckpointTaskOwnershipTests/windows_estimate_selects_row_and_total_deadlines` | R-3 | all listed, 0 failed | 1 | 1 |
