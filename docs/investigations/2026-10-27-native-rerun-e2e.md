# Native Windows E2E rerun investigation — 595bc226

Status: investigation complete for all nine historical failure cases, 2026-10-27. Six still fail natively; Agents and Pipeline pass, and the review case passes four times. Five classes were rerun fully; 49 land-delivery cases were not rerun within the 120-minute budget. No product or test changes.

## Provenance and scope

- Assigned branch: `feat/card-task-595bc226`; initial HEAD `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Requested source: fetched `origin/rc/2026-10-27`, `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- The assigned branch is not descended from that rc tip. E2E/client files match; the only runtime source difference is `src/Antiphon.Agents.Pty/ClaudeScreen.cs`. Branch switching, merging and source edits are outside this investigation. Therefore execution uses an exact `git archive` of the requested rc tip at `C:\Antiphon\scratch\595bc226-rc`, with explicit build source revision metadata; the report stays on the assigned branch.
- Local evidence: `C:\Antiphon\worktrees\card-task-595bc226\.antiphon\native-595bc226` (gitignored). Historical evidence is read-only under `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Final historical digest fetched from `origin/feat/card-task-7e719c57` at `c7ad9e901c6be169fb5a0f43be8e210211e9081b`, `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md`.
- Initial host check: Windows, SDK `10.0.300`, 8,274 MiB available / 32,709 MiB total; no native qualification lock. A visible `Antiphon.Tests` process belongs to active task fb80be16, not this task or abandoned qualification; left untouched.
- Byte-for-byte verification at `2026-10-27T08:54:05Z` checked all 9,063 archived files with zero differences (`source-verification.json`).
- Every driver uses `scripts/build-slot.ps1`; E2E fixture owns random-loopback server/runner and test Postgres. No live service restart, publish, tag or promotion is authorized or performed.

## Native results

| Class | Historical result | Native rc initial result | Cause / classification | Proposed fix |
|---|---|---|---|---|
| AgentE2ETests | 1 pass / 1 fail | 2 pass / 0 fail | Fixed by rc/2026-10-27 | None |
| PipelineGlanceE2ETests | 0 pass / 1 fail | 1 pass / 0 fail | Fixed by rc/2026-10-27 | None |
| BoardE2ETests | 9 pass / 1 fail | 9 pass / 1 fail | Confirmed fixture navigation defect; Diff tab never selected | See Not done, noted |
| WorkflowDeleteTests | 4 pass / 2 fail | 4 pass / 2 fail | Confirmed fixture routing assumptions predate Home/Workflows split | See Not done, noted |
| AgentTaskLandDeliveryE2ETests | 50 pass / 2 fail | Partial: 2 pass / 1 fail; 49 not rerun | Residue fixture confirmed; historical review receipt race reconstructed, 4/4 current runs pass | See Not done, noted |
| DispatchBaseWarningDeliveryE2ETests | 11 pass / 2 fail | 11 pass / 2 fail | Confirmed stale warning expectation plus product admission delay | See Not done, noted |

The single native build passed (39 warnings, zero errors, 220.67 seconds MSBuild / 222 seconds leased). `npm ci` and `npm run build` passed (106 seconds leased, 75 seconds slot wait). Source archive SHA-256: `6AA5990C26B3E5515C647BFE34BF8EBD6355C8D1A3F437C0D6525D6B0D0AC526`.

Agent class: 79.9 seconds outer wall; Pipeline class: 60.4 seconds. Fresh `results.trx`, `console.log`, and `run.json` live under `<evidence>/<Class>-initial/`. Failure screenshots and DOM logs are copied under `<evidence>/ui-evidence/`; originals remain in the export's test output.

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

Classification: fixture contract drift, not a demonstrated Windows or product defect. This mechanism is deterministic; no flake claim. A pushed repair exists on `origin/feat/card-task-380013d3` at `03d62fedb18a36c573b8b508ad2fe21f26985f48`, but its Board change is absent from the pinned rc source. The report does not apply or verify that repair. The manual xterm case passed in this native run; that single pass does not establish race freedom.

## Confirmed: Workflow delete tests still assume the old dashboard route

Fresh native `WorkflowDeleteTests-initial/results.trx` reports 4 passed / 2 failed; 179.793 seconds host duration, 260.6 seconds outer wall including 75 seconds admission. The exact error messages and full stacks for both cases are in `WorkflowDeleteTests-initial/failures.log`.

```text
Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard
TimeoutException: Timeout 10000ms exceeded.
waiting for navigation to "**/" until "Load"
  navigated to "http://127.0.0.1:62178/workflows"

Delete_workflow_button_shows_confirm_and_removes_card
TimeoutException: Timeout 10000ms exceeded.
Call log:
  - waiting for Locator("[role='article']").First to be visible
```

`tests/Antiphon.E2E/WorkflowDeleteTests.cs:294` waits for `/` after deleting, but `client/src/features/workflow/WorkflowDetailPage.tsx:471` explicitly navigates to `/workflows`. The failure screenshot `ui-evidence/Screenshots/Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard/FAIL_final_073148.png` shows the Workflows page with **No workflows yet**. This supports a successful UI delete/navigation with an obsolete URL expectation; the native API delete case also passed.

The other failing test navigates to the bare app root (`WorkflowDeleteTests.cs:335`) then waits for workflow cards (`:345`). `client/src/App.tsx:91` renders Home at the index; the workflow dashboard is registered under `workflows` (`:100`). This explains why the first card never appears. That test's `finally` only disposes its browser context (`WorkflowDeleteTests.cs:409`); it does not call the screenshot/DOM completion helper. Its native error and per-test server/notes logs are retained; no failure screenshot for this second method is claimed.

Classification: fixture navigation drift, not an established Windows, SDK, ACL or product defect. The same two failures reproduce without source changes. No flake claim or timeout change.

## Confirmed: the residue fixture now selects retained evidence

`AgentTaskLandDeliveryE2ETests-residue-initial/results.trx`: two selected arguments, one pass (`preoperation-refusal`), one failure (`residue-cleanup`), 135.015 seconds test-host duration / 245.7 seconds outer wall. Error:

```text
ShouldAssertException: terminal.Type
    should be
AgentTaskEventType.LandedWithResidue
    but was
AgentTaskEventType.Landed
```

`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:329` creates `.antiphon/valuable.txt` containing `owned user residue`; `AgentTaskLandDeliveryE2ETests.cs:213` expects `LandedWithResidue`. Current defaults retain `**/.antiphon/**` (`server/Application/Settings/WorktreeCleanupSettings.cs:26`). The classifier's catch-all adds it to Evidence (`server/Application/Services/WorktreeIgnoredContentClassifier.cs:55`), and guarded removal copies evidence before reinspection/deletion (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:122`). The product is executing the newer evidence-retention contract.

Native proof is stored in `residue-proof.json`, extracted without changing the original snapshot at `C:\Antiphon\scratch\595bc226-rc\.antiphon\acceptance\card-0467\444e140c28c042c4a7591b1f64cb23c0\delivery-evidence.json`:

- Task `b8c05366-2b90-4df7-b78e-d47c178a867e`, operation `4631785c-1f26-4140-be2a-a2b3b3490008`, terminal event `8b47caa3-42da-40cd-97b6-9d40290a465b` has type 21 (`Landed`).
- Notification `cf979bbe-b711-4acf-b2c0-7e87cecb2cd2` points at that event, is Confirmed, and names confirming prompt sequence 4. That stored `UserPrompt` reports `outcome=Landed`, `cleanup=Complete`, and `retained=1 files`.
- The native retained path is `trees/.retained/b8c053662b904df7b78ed47c178a867e/4b4184b373654d79afdbcdd552159b26/.antiphon/valuable.txt` under the fixture root. Its 18 bytes hash to `D3C4C32B9814B7F134582E5F3CB34F5AB69F9B08E99E5B904F8956275229038E`, exactly the UTF-8 hash of the fixture payload.

Classification: stale fixture classification/expectation; no lost file, Windows ACL failure, or failed native publication demonstrated. The failure stops before the fixture's cleanup-retry assertions, so that later path is not covered by this run.

## Confirmed historical review fixture race; four current native passes

`AgentTaskLandDeliveryE2ETests-review-initial/results.trx` reports one pass for `C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict)`, 233.965 seconds host duration / 540.5 seconds outer wall (300 seconds admission). This single pass does not establish a fix.

The historical `e2e-all-split-34.trx` failed at `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs:269` because `note.ConfirmedAt` was `2026-10-27T03:38:38.5361780Z` rather than null. The test waits for the queue-before-verdict barrier (`:207`, `:211`), then assumes the notification remains unconfirmed (`:269`). The hook holds the queue's verdict-save path (`server/Application/Services/SessionMessageQueueService.cs:2804`; fixture `LandDeliveryOptions.cs:176`), while the notification service independently scans attempted rows for complete transcript evidence (`AgentTaskLandNotificationService.cs:254`, `:300`, `:315`) and sets `ConfirmedAt` (`:355`). These are different boundaries. A complete stored UserPrompt can therefore discharge the note before the queued-message verdict is saved.

Fresh passing-run root: `C:\Antiphon\scratch\595bc226-rc\.antiphon\acceptance\card-0467\a316a3aafc40484794276e69730e8c5c`. Its barrier names Review task `03d3283d-0ab4-486b-a511-d62e39de9933`, queued row `199d4d46-25b9-4e1f-8f8a-140382453736` and child PID 378348. Its final snapshot contains caller UserPrompt sequence 4, header `[task 03d3283d done]`, timestamp `2026-10-27T07:45:49Z`. The three additional repetitions below all pass. This does not establish that the historical timing-dependent assertion was repaired or quantify its failure frequency.

`git diff 105792f7d22f origin/rc/2026-10-27 -- tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs server/Application/Services/AgentTaskLandNotificationService.cs server/Application/Services/SessionMessageQueueService.cs` is empty. The cited assertion, cut and receipt-save mechanism are unchanged from the historical failure.

Three additional isolated invocations of the exact review method completed under one gate lease, with fresh fixture/server/runner state per run. The wrapper waited for the C508 diagnostic to exit, so no two E2E hosts from this task overlapped. `review-repeat-driver.log` records 571 seconds of slot wait and 669 seconds held for the serial batch.

| Attempt | Native result | Driver wall seconds | Fixture root suffix |
|---|---|---:|---|
| review-repeat-1 | 1 pass / 0 fail | 225.9 | `eabae6dbcd2b432485c0ba2bd3d784e9` |
| review-repeat-2 | 1 pass / 0 fail | 224.3 | `515563ae5ee04d11ae39cb72c2e08c04` |
| review-repeat-3 | 1 pass / 0 fail | 214.3 | `99f76ba4988549caa81451c042906947` |

`review-transcript-proof.json` binds all four native roots to their queue-before-verdict barrier, snapshot SHA-256, Review task ID, and exactly one caller UserPrompt at sequence 4 containing that task's completion header and `review-evidence=`. The original historical assertion records a non-null confirmation timestamp, while the only current receipt writer shown above is independent of the queue barrier. This confirms the fixture's unsynchronized assumption by reconstruction; the precise historical worker interleaving and original full transcript were not recovered. The race itself was **not reproduced on the current rc** in these four runs, and no current fix is claimed.

## Dispatch warnings: current contract mismatch and admission delay

Both C508 cases have reproduced `TimeoutException: one committed mismatch dispatch intent` at `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs:130`. The whole class completed: 11 passed / 2 failed, 1,463.087 seconds host duration / 1,576.9 seconds outer wall including 105 seconds slot wait. The exact errors and stacks are in `DispatchBaseWarningDeliveryE2ETests-initial/failures.log`. The snapshots in `dispatch-mismatch-proof.json` bind these roots and tasks:

| Method | Fixture root suffix | Task | Intents / notifications |
|---|---|---|---|
| C508_MismatchWarningReachesCaller | `e40a314acb9b4824880bfc4872c81ca9` | `f94da36e-bc79-4d02-9755-ad2b193cec31` | 0 / 0 |
| C508_MismatchWarningWaitsForCaller | `ed885bddff604ca884aa5e5c14215803` | `e0b2da83-d81f-4b5c-9715-3e057b0ce349` | 0 / 0 |

The fixture still changes the project default from `master` to `project-default` on the lease (`LandDeliveryOptions.cs:95`), then waits for one mismatch intent. Claim-time revalidation now compares the configured fallback with the frozen inspection intent (`server/Application/Services/AgentTaskDispatcher.cs:5566`) and rolls back stale observations (`:5587`). The tick invalidates/requeues that inspection (`:982`). The current unit fixture for the very same C508 scenario explicitly expects the refreshed base and **zero** mismatch intents (`tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs:1395`, `:1417`). The E2E expectation no longer matches the current contract.

An additional native observation prevents oversimplifying the timeout as a completed successful dispatch: the first case's read-only task-detail observation at `2026-10-27T07:51:21Z` still showed Queued, no base ref, and `WorktreeBaseSource=Unset` (`dispatch-first-observation.json`). Its lease-change witness fired at `07:50:42.5058816Z`; the task event at `07:50:52` said `waiting for Grok capacity (any alias); capacity wait b7c8a8f4 not yet granted.` The second case shows the same sequence. A subsequent focused DB witness confirms the admission state below; no permanent capacity deadlock is claimed.

Confirmed admission sequence (reconstructed source plus two native DB reads): `AgentTaskDispatcher.cs:960` redeems an unfinished capacity wait before `DispatchOneAsync`. `CapacityRecoveryService.cs:287` marks it Admitted, clears the provider grant (`:295`), and commits (`:300`). A subsequent stale-inspection rollback is in the separate claim transaction. On the following tick, Admitted is still an unfinished wait (`AgentTaskDispatcher.cs:1747`), but redemption requires the now-cleared grant (`:1771`). Stalled admission recovery exists (`CapacityRecoveryService.cs:1161`, `:1198`): its default threshold is two 60-second admission intervals (`CapacityRecoveryPolicy.cs:89`; `SupervisionSettings.cs:72`; `server/appsettings.json:263`). That delays eligibility for recovery beyond this fixture's 120-second observation window, which begins before the admission. The rows below provide the Queued task, Admitted wait, null launch receipt, cleared provider grant and changed project default in the same owned test DB. Permanent deadlock or eventual recovery is not established.
The diagnostic rerun uses fixture root `b15312266469483abc65742e20e727f2`. `capacity-observation-1/ownership.json` binds native test PID **344680** (created 08:38:42Z) to container **cd6d0b81874a**, mapped Postgres port **50874**, by the owned executable path and its established TCP connection. Both observations execute `BEGIN READ ONLY`; original psql output and parsed JSON are retained. The earlier `dispatch-control-db*` files are a task-absent preflight and are not proof.

`capacity-observation-1/rows.json` at **08:40:09.729833Z**, and `capacity-observation-2/rows.json` at **08:40:42Z**, agree:

| Row | Stored native state |
|---|---|
| Task `ec51c85e-a0d0-43bd-83b7-8c9837d80153` | Status 0 (Queued), CapacityWaitId `424042bd-08b3-4757-88ed-c4e9e6b64b6d`; session, worktree and base ref null |
| Capacity wait `424042bd-08b3-4757-88ed-c4e9e6b64b6d` | State 3 (Admitted), AdmissionCount 1, OutcomeReason Dispatch, NeedsRevalidationGrant false, LaunchSessionId null; UpdatedAt `08:39:56.910534Z` |
| Provider Kind 4 (Grok) | GrantedWaitId and GrantedActionKey null; LastActionKey `424042bd08b3475788edc4e9e6b64b6d:0`; LastActionAt `08:39:56.910534Z`; NextAdmissionAt `08:40:56.910534Z` |
| Project `a18dccf8-f5de-4029-b685-0ef95054028e` | BaseBranch `project-default`; fixture move fired at `08:39:57.083965Z`, after admission |
| Task event `66ed92d4-090e-480f-b186-74f17bdb8afd` | `08:39:57.339583Z`: Held, same-card base inspection pending |
| Task event `efb075f7-5d98-4647-be49-87ffefb8a31b` | `08:40:06.968033Z`: capacity wait 424042bd not yet granted |
| Warning intents | Zero |

Classification: **fixture contract drift** explains the impossible expected mismatch notification; **product control-flow integration delay** explains why the real E2E task remains queued instead of dispatching on the next fresh inspection. This is not evidence of an unavailable Grok provider or Windows transport failure. Default stalled-admission recovery cannot become eligible until approximately **08:41:56.910534Z**, plus its scan scheduling; this run does not prove its eventual outcome.

## Coverage and remaining uncertainties

Initial selection: **35 cases, 29 passed / 6 failed**. Additional diagnostics: **three review passes and one repeated C508 failure**. Total: **39 executions, 32 passed / 7 failed**, with all nine historically failing cases included. The focused C508 rerun reproduced its original error (176.197 seconds host, 1,579.0 seconds outer wall, including 1,397 seconds slot wait) and produced both DB observations above.

- The 49 other land-delivery cases were not rerun; their historical 50-pass class result is not fresh coverage. The failed residue argument did not reach its later cleanup-retry assertions.
- The review race is reconstructed from stored historical assertion evidence and unchanged receipt/barrier code; the four current passes do not reproduce the adverse scheduling order or prove a fix.
- Admission state and the subsequent queued tick are confirmed. Recovery after the 120-second default stale-admission threshold, permanent deadlock, and exhaustion behavior were not observed.
- The second Workflow failure has no screenshot because its completion hook does not capture one. Its error and routing mechanism are still evidenced.
- Results bind the pinned rc SHA above, not a moving later rc tip. No independent AV/ACL/path forensic probe was needed or performed; those are not demonstrated causes of these failures.

## Not done, noted

Fix ideas only: select the Board Diff tab; align Workflow routes; use actual protected residue in the residue fixture; align C508 with reinspection semantics; reconcile the review assertion with independent transcript confirmation; address capacity admission consumed before a stale claim rollback. No fix was designed or implemented.

## Execution contract and performance limits

Both delivery classes use the real Program, isolated runner and native FakeGrok (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:23`). Initialization asserts Windows, locates ConPTY, and requires `fakegrok.exe` (`:59`–`:61`). It also rejects production runner port 17204 (`:89`) and records loaded runner identity/hash (`:140`–`:146`). These checks passed in the native executions; the reported failures occur later. No SDK, ACL, CRLF, path-length or ConPTY initialization failure was observed.

Execution used this build once from the pinned export, followed by the frontend rebuild under its own gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-e2e-595bc226-build -- dotnet build tests/Antiphon.E2E --property:OutputPath=bin-595bc226/ --property:SourceRevisionId=d1d090f766451797a3bd9b30c9d2966f2f8c034c --nologo
```

Each ordinary class invocation had this shape (no source changes or rebuild between invocations):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label <label> -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-595bc226/ -- --treenode-filter "/*/*/<Class>/*" --report-trx --report-trx-filename results.trx --results-directory <unique-evidence-directory>
```

The land selection substitutes exact methods `C467_V29_RealOutcomeProducerMatrix_Batch2` and `C488_ReviewDeliveryCrashMatrix_Batch5` for the final `*`. Additional diagnostic selection: `DispatchBaseWarningDeliveryE2ETests/C508_MismatchWarningReachesCaller`. All exact filters, UTC times and child exit codes are saved in per-run `run.json`; TRX files preserve every selected case and failure. `native-result-inventory.json` binds run results to SHA-256 hashes. These are native runs of the requested rc, not tests of the documentation branch's executable source.

Host durations and slot waits are measured independently. The largest completed initial selection is the dispatch class at 24m23s host time; the original land class cost exceeds the entire task budget. Retained server logs contain LIVE_TIMING migration/setup/startup measurements; `phase-excerpts.txt` preserves original file:line entries and `phase-summary.json` summarizes deduplicated samples. For example the initial review fixture reports 31.824 seconds migration within 33.946 seconds setup (`a316a3aafc40484794276e69730e8c5c/server-logs/antiphon-20261027.log:240`, `:245`). Phase durations overlap and must not be added. Separate teardown/body phase timing remains unknown; TRX captures the overall case duration. These shared-host samples do not establish a stable production p95. Shared-host contention is visible in the BUILD SLOT wait lines and is not treated as a product failure. No timeouts/assertions were widened, no failure was repaired, and no all-green claim is made.
Observed phase summary (63 deduplicated host/fixture samples, including restarts; these are not 63 independent test cases):

| Phase | Median seconds | Empirical p95 seconds | Maximum seconds |
|---|---:|---:|---:|
| Database migration | 7.135 | 29.593 | 32.169 |
| Fixture setup, including migration | 8.350 | 31.739 | 34.771 |
| Host start | 2.099 | 3.302 | 3.528 |

## Final integrity and cleanup

- Final archive comparison at **08:54:05Z**: all **9,063** source files match the pinned rc archive, zero differences. No tracked screenshot baseline changed.
- All foreground test/observer processes were awaited. Before deleting outputs, process inventory found zero executables running from the owned source export. All **16** `bin-595bc226` directories were resolved and checked beneath that export, then deleted; zero remain (`cleanup-receipt.json`, completed **08:55:27Z**).
- TRX records identify **50** native Testcontainers containers created by these runs; Docker inventory finds zero remaining (`container-cleanup-receipt.json`). Foreign task processes/containers and the live AppHost/runner were left alone.
- Source export, original snapshots, exact errors/stacks, TRX, screenshots, phase excerpts and selected DB/transcript evidence remain at the local paths above. `evidence-hashes.json` and `native-result-inventory.json` bind the retained result files. Generated evidence is gitignored; the only tracked deliverable is this report.
- The caller's standing authority covered execution and evidence collection; no further approval was needed. No product/test fix, deployment, tag, promotion or live restart was performed.

Confirmed mechanisms are ready for Plan. Remaining uncertainty is explicit above; no source repair or green verification of a repair is claimed.

--- next stage ---
next: plan
handoff: Plan the confirmed Board/Workflow/residue/C508 fixture contract updates, the reconstructed review receipt race, and the product admission delay after stale claim rollback; retain the 49-case coverage gap and unobserved eventual recovery as explicit limits.
artifact: docs/investigations/2026-10-27-native-rerun-e2e.md
