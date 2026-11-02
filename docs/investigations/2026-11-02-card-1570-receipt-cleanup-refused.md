# CARD-1570: receipt `cleanup_refused` after required output cleanup

Investigate, 2026-11-02, base `e18a1177d9e042ac5986cf8bdebc5b47abb7ca11` (master), Linux (server2).
Read-only, plus one probe: the PowerShell validator run against two synthetic `report.json` files and one stored receipt. No build.

## Verdict

**Confirmed. Still reproducible on master. The card's mechanism is mis-stated.**

Removing the outputs after the run has no effect. A receipt is a frozen file, and neither validator
looks at the disk. The actual cause is a rule: **any run with a red, skipped, timed-out or
slot-refused row is invalid as a whole, including its green rows.** The finalizer writes the
kept-because-red output into the receipt as the cleanup refusal `still-present`. That refusal is
checked before any row selection, so `-Rows`/`--rows` cannot rescue a green row. Without the label,
the same receipt would still fail as `report_failed`. The 2026-11-02 `SOURCE VALID` runs named in
the brief were all-green runs, so they never reach this path. Nothing has fixed it.

## Q1: is it still reproducible, and did a later commit fix it?

Not fixed. No commit in `tools/Antiphon.Checkpoints`, `src/Antiphon.HostCleanup/Checkpoints`,
`scripts/validate-checkpoint-receipt.ps1` or `scripts/run-checkpoint.ps1` since 2026-10-22 touches
this ordering. I checked the full `git log --since=2026-10-22` list: the 1601/1599 read changes, the
1616 admission report, the 1532 stamps and the 1531 budgets. `git log -S still-present` ends at
`9105a7b2a` (2026-10-11). `git blame` puts the final-sweep lines at `9105a7b2a`/`c24e46e79`
(≤ 2026-10-15), before the 2026-10-23 incidents. `b191de000` (CARD-1616, 2026-10-30) changes only
the admission-failure report's `OutputPaths` (`CheckpointApp.cs:867`), not finalization.

The ordering on master, for a run with a red row and `--keep-outputs` not given:

1. `CheckpointApp.cs:403` `ReportWriter.WriteFiles`: the first receipt, with no cleanup refusal yet.
2. `CheckpointApp.cs:440-447` calls `CleanOwnedOutputs`. That method returns `KeptBecauseRed` when
   `exitCode != 0 && !cleanOnRed` (`Cleanup/OutputCleanup.cs:41-42`). Keeping the outputs is
   deliberate, and the code sets `CleanedOutputs = false` and adds no refusal.
3. `CheckpointApp.cs:497-510` is the unconditional final sweep. `remaining` = every `OutputPaths`
   entry that still exists, which is the deliberately kept output. If `CleanupRefusals` is empty, it
   adds **`"still-present"`** (`:503-504`). The exit is already non-zero, so it stays as it is.
4. `CheckpointApp.cs:532` `ReportWriter.WriteFiles` publishes `cleanupRefusals:["still-present"]`,
   and `report.md` prints `refused: still-present` / `outputs: refused/kept …`.
5. Later the delegate removes the outputs by hand. The receipt does not change.
6. `ReportValidator.cs:34-35` returns `cleanup_refused` before the exit check at `:36-37` and before
   row selection at `:43-47`. The script does the same: `validate-checkpoint-receipt.ps1:70`
   (`cleanup_refused`), then `:71` (`report_failed`), then row selection at `:76-81`.

Stored evidence from today (server2 mirrors; outputs now absent from disk in every case):

| Run | Exit | Rows | `cleanupRefusals` |
|---|---|---|---|
| `task-cf298805/.antiphon/checkpoints/20261102-065750-2a89` @ `b7029b0f1859…` | 1 | 9 green + CP-15 red | `["still-present"]` |
| `task-cf298805/.antiphon/checkpoints/20261102-082251-aa4e` @ `4392d6d165e2…` | 1 | CP-4 green, CP-5 red | `["still-present"]` |
| `task-5ef0b4bc/.antiphon/checkpoints/20261102-100552-570c` | 5 | CP-8 skipped (timeout) | `["still-present"]` |
| `task-5ef0b4bc/.antiphon/checkpoints/20261102-100853-f8c0` | 2 | CP-8 slot-refused | `["still-present"]` |

Every all-green run in the same mirrors has `exitCode 0, cleanupRefusals [], cleanedOutputs true`.
Examples are 34aa, 49d8, 5f17 and d8ca.

The probe ran the script validator, using the stored 2a89 receipt for a green row:
`-Rows CP-1 -ExpectedSourceSha b7029b0f…` → `CHECKPOINT SOURCE INVALID reason=cleanup_refused`, exit 2.
It also ran on synthetic receipts with the same shape (CP-2 green, CP-4 red, exitCode 1):

| `cleanupRefusals` | no `-Rows` | `-Rows CP-2` |
|---|---|---|
| `["still-present"]` | `cleanup_refused` (2) | `cleanup_refused` (2) |
| `[]` | `report_failed` (2) | `report_failed` (2) |

**Mapping to the 2026-10-23 tasks:**

- **A2 `3fa840bf`** (Failed, Codex). This is the only true case of green behaviour without a usable
  receipt. At `e3e62981` CP-2, CP-3 and CP-5 were green, but CP-4 had 21 failures, so the run was red
  and all four rows were invalid. CP-4 was repaired at `68be5426` and rerun alone. That run was green
  and valid. The CP-2, CP-3 and CP-5 receipts stayed red-run receipts at an *older* SHA. The evidence
  is the task result, plus the plan and investigation docs on `origin/feat/card-task-3fa840bf`
  (`docs/investigations/2026-10-23-card-1548-1559-fixture-repair.md`).
- **A1 `c099accb`** (Succeeded, `verdict: done`). R-1 ran 130 tests with 4 base-confirmed inherited
  failures, so the red receipt was refused by design. The operator accepted it.
- **A3 `a5f9c44c`** (Failed): there were 7 real BootStall failures.
- **W1 `86740437`** (Failed): it passed 14 of 15, and `C1213_QueuedBranchRecovery` was red.

In A3 and W1 the test failures were real; `cleanup_refused` only hid their `report_failed`.

## Q2: smallest fix and witness design (not implemented)

Two slices. S1 is a pure labelling fix. S2 is the policy change that actually gives a green row a
usable receipt. S2 has the same shape as the classification already sketched in CARD-1368 D-4
(`docs/superpowers/plans/2026-10-19-card-1368-testresults-cleanup-plan.md:36,47`).

- **S1, truthful label.** In the final sweep (`CheckpointApp.cs:497-510`), do not add
  `still-present` for paths that the cleanup itself kept because the run was red (the
  `clean.KeptBecauseRed` branch). Keep `CleanedOutputs=false` and print a distinct
  `outputs: kept (red) …` line. A green run, or a red run with `--clean-on-red`, that leaves output
  behind still adds `still-present`. A red run then fails validation as `report_failed`, not
  `cleanup_refused`, which is the truthful reason.
  - *Witness W-1 (`CheckpointSourceExecutionTests`):* run `ExecuteAsync` with the FakeDriver, a
    two-row manifest on one `bin-c1570`, CP-1 green, CP-2 red (TRX with one failed case), and
    `KeepOutputs=false`, `CleanOnRed=false`. Assert exit 1, `CleanupRefusals` empty,
    `CleanedOutputs` false, the output directory still present, and both validators returning
    `report_failed`. **Red at base:** `CleanupRefusals == ["still-present"]` and both say
    `cleanup_refused`.
  - *Guard W-2:* in a green run, output that the run created but that survives cleanup is still
    `still-present` / exit 2. The existing `cleanup_refusals_disqualify_green_rows_in_both_validators`
    stays green.
- **S2, row-scoped qualification of a mixed run.** When rows are selected explicitly, both
  `ReportValidator.Validate` and the script accept a non-green run under four conditions: every
  selected row is green and source-eligible (the checks that already exist), `cleanupRefusals` is
  empty (with S1 applied, kept red output is not a refusal), the run's terminal source is eligible,
  and the run exit came from failures in unselected rows only. That last condition means exit 1 or
  3, never 2, 4, 5, 6 or 7. A whole-run validation with no row selection stays `report_failed`.
  - *Witness W-3:* the W-1 report with selection CP-1 → `null`/exit 0 in both validators; selection
    CP-2 → `row_failed`; no selection → `report_failed`. The same report with
    `cleanupRefusals=["output-ledger-mismatch: x"]` and selection CP-1 → `cleanup_refused`. A report
    with exit 5 (timeout) and selection CP-1 → `report_failed`. **Red at base:** selection CP-1 →
    `cleanup_refused`.
  - The owner doc needs one paragraph in `docs/testing-and-build.md` (Checkpoint runner tool: exit
    codes and validation). The stage-code bundle needs one clause in `CHECKPOINTS:` saying that a
    green row in a mixed run qualifies only through `--rows`/`-Rows`, at that run's SHA.
- **Limit (A2):** S2 would have given A2 valid CP-2, CP-3 and CP-5 receipts, but at `e3e62981`, not
  at the tip `68be5426`. Whether a receipt at an ancestor SHA covers the tip, when the delta is
  test-only in another class, is a policy question (stage-code `SOURCE:`). S2 does not answer it.

## Q3: green behaviour, unusable receipt → Succeeded-with-disclosure?

**Not implemented anywhere. Settlement has no notion of a receipt.** The status comes only from the
delegate's own closing token: `AgentTaskReplyService.ClassifyReportAsync`. `done` →
`Succeeded` at `:4027-4043`. `failed` → `Failed` with the first report line as `FailureReason` at
`:4047-4051`, and the retained-report path does the same at `:4022-4023`. A2's event row says
exactly this: `Delegate reported 2,071 characters … (verdict: failed)`. A1 settled as Succeeded
because its delegate wrote `done`. No server code reads `report.json` or the `cleanup_refused` text.

So the failure was in the delegate's choice of verdict. The server did not mark the task Failed. The
minimal change is instruction text, not settlement code:

- **`server/Bundles/stage-code.md`** (2,404 bytes; respect the bundle pins and caps) needs one
  clause: "When every required V-n/R-n behaviour is green on the tested SHA and the only gap is
  receipt validity (another row's red, a stopped run, or an ancestor SHA), end `done` with a
  `RECEIPT GAP:` line naming the rows and the reason, and set `next: code` for the receipt rerun.
  Never end `failed` for a receipt-only gap."
- **The owner doc is `docs/orchestration-loop.md`**, under Standing operating rules / the
  stage-report contract. It needs a mirror sentence telling the orchestrator to dispatch the receipt
  rerun from `next=code` on the Succeeded owner.
- One alternative was considered and not recommended: a server-side `Succeeded`+disclosure status.
  It would need a new report token or field, a classifier change and DTO/UI changes, while the
  existing `done` + `next: code` already keeps the owner landable and answerable.
- Already mitigated: a Failed Code owner can still land through reviewed recovery,
  `POST …/land/v2 { recoverReviewedSource: true }` (CARD-0753, `06d030c6e`; `docs/ops-http.md:195`),
  as long as Review evidence exists.

## Disposition

Keep CARD-1570. Narrow the title to "a red run's green rows can never qualify; kept red output is
mislabelled `still-present`". Dispatch **Plan** for S1, S2 and the Q3 bundle clause. Plan is needed
because Code requires a `### Checkpoints` table, and because S2 changes a validation policy that
CARD-1368 D-4 had deferred.

## Remaining uncertainties

- S2's exact admissible exit set (1 and 3) and the rule for `--baseline` INHERITED rows are design
  choices for Plan.
- I did not re-execute the tool on a live red run. The red-run receipt shape comes from four stored
  2026-11-02 receipts and the code path. The validator outcome was reproduced by the probe.

## Not done, noted

- Fix idea, one line: S1, skip `still-present` for kept-because-red paths at `CheckpointApp.cs:503`;
  S2, row-scoped validation of a mixed run when rows are selected and `cleanupRefusals` is empty.
