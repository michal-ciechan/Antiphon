# Native Windows E2E rerun investigation — 595bc226

Status: investigation in progress, 2026-10-27. No product or test changes.

## Provenance and scope

- Assigned branch: `feat/card-task-595bc226`; initial HEAD `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Requested source: fetched `origin/rc/2026-10-27`, `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- The assigned branch is not descended from that rc tip. E2E/client files match; the only runtime source difference is `src/Antiphon.Agents.Pty/ClaudeScreen.cs`. Branch switching, merging and source edits are outside this investigation. Therefore execution uses an exact `git archive` of the requested rc tip at `C:\Antiphon\scratch\595bc226-rc`, with explicit build source revision metadata; the report stays on the assigned branch.
- Local evidence: `C:\Antiphon\worktrees\card-task-595bc226\.antiphon\native-595bc226` (gitignored). Historical evidence is read-only under `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Final historical digest fetched from `origin/feat/card-task-7e719c57`, `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md`.
- Initial host check: Windows, SDK `10.0.300`, 8,274 MiB available / 32,709 MiB total; no native qualification lock. A visible `Antiphon.Tests` process belongs to active task fb80be16, not this task or abandoned qualification; left untouched.
- Every driver uses `scripts/build-slot.ps1`; E2E fixture owns random-loopback server/runner and test Postgres. No live service restart, publish, tag or promotion is authorized or performed.

## Native results

| Class | Historical result | Native rc result | Cause / classification | Proposed fix |
|---|---|---|---|---|
| AgentE2ETests | 1 pass / 1 fail | 2 pass / 0 fail | Fixed by rc/2026-10-27 | None |
| PipelineGlanceE2ETests | 0 pass / 1 fail | 1 pass / 0 fail | Fixed by rc/2026-10-27 | None |
| BoardE2ETests | 9 pass / 1 fail | 9 pass / 1 fail | Confirmed fixture navigation defect; Diff tab never selected | See Not done, noted |
| WorkflowDeleteTests | 4 pass / 2 fail | Pending | Pending | Not designed in Investigate |
| AgentTaskLandDeliveryE2ETests | 50 pass / 2 fail | Pending | Pending | Not designed in Investigate |
| DispatchBaseWarningDeliveryE2ETests | 11 pass / 2 fail | Pending | Pending | Not designed in Investigate |

The single native build passed (39 warnings, zero errors, 220.67 seconds MSBuild / 222 seconds leased). `npm ci` and `npm run build` passed (106 seconds leased, 75 seconds slot wait). Source archive SHA-256: `6AA5990C26B3E5515C647BFE34BF8EBD6355C8D1A3F437C0D6525D6B0D0AC526`.

Agent class: 79.9 seconds outer wall; Pipeline class: 60.4 seconds. Fresh `results.trx`, `console.log`, and `run.json` live under `<evidence>/<Class>-initial/`. Screenshots are retained under the export's `tests/Antiphon.E2E/TestOutput/Screenshots` pending collection.

Historical cost is a scope risk: summing the original TRX durations gives 119.1 minutes for the 52 land-delivery cases and 27.8 minutes for 13 dispatch-warning cases, before the other four classes/builds/admission. The brief gives 120 minutes total. This is a Debug follow-up obligation under R1, not permission to hide missing coverage or widen timeouts. No sub-delegation is available to this delegate.

Execution scope adjustment: the two originally failing land-delivery methods are selected separately (three expanded cases, including the passing companion argument). The remaining 49 cases are not native rerun coverage. All other classes use the requested whole-class filter. This prioritizes the caller's failed-test investigation within the 120-minute ceiling.

## Confirmed: Board review opens Sessions, then asserts Diff

Fresh native `BoardE2ETests-initial/results.trx` reports 9 passed / 1 failed, 279.809 seconds test-host duration, 525.6 seconds outer wall including 240 seconds slot wait. Failing method: `Board_user_can_review_real_worktree_diff_and_send_inline_comment`, 23.804 seconds body.

Exact error excerpt (`BoardE2ETests-initial/console.log:18`):

```text
failed Board_user_can_review_real_worktree_diff_and_send_inline_comment (23s 804ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] PlaywrightException: Locator expected to be visible
  Error: element(s) not found
  Call log:
    - Expect "ToBeVisibleAsync" GetByRole(AriaRole.Dialog).GetByText("Diff review") with timeout 15000ms
    - waiting for GetByRole(AriaRole.Dialog).GetByText("Diff review")
```

The source opens the card and immediately asserts the Diff heading (`tests/Antiphon.E2E/BoardE2ETests.cs:499`, `:502`). The product selects `sessions` on desktop and has `keepMounted={false}` (`client/src/features/board/CardModal.tsx:223`); Diff content is a separate panel (`:266`). The current failure screenshot `ui-evidence/Screenshots/Board_user_can_review_real_worktree_diff_and_send_inline_comment/FAIL_final_072636.png` visibly has Sessions selected and Diff available. The corresponding `ui-evidence/Logs/BoardE2ETests/Board_user_can_review_real_worktree_diff_and_send_inline_comment/page.html` and TRX aria snapshot corroborate that state.

Classification: fixture contract drift, not a demonstrated Windows or product defect. This mechanism is deterministic; no flake claim. A pushed repair exists on `origin/feat/card-task-380013d3`, but its Board change is absent from the pinned rc source. The report does not apply or verify that repair. The manual xterm case passed in this native run; that single pass does not establish race freedom.

## Remaining work

Rebuild the native graph and frontend once, run all six classes separately, retain exact failures/TRX/screenshots, and reconstruct surviving failure mechanisms. Any flake claim requires three reruns. Performance and source-revision limitations will remain explicit.

## Not done, noted

Board: the existing sibling change selects Diff before checking its content; no fix was designed or applied here.
