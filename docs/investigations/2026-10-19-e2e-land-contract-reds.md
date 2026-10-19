# Windows E2E land contract reds - 2026-10-19

**Confirmed: three fixture mismatches explain the four reported signatures.**
One representative per signature reproduced at the requested master source:
**4 executed, 0 passed, 4 failed, 0 skipped; no five-minute cap fired.** The
reviewed-source-clean gate explains seven of the historical 19 rows, not all 19.
The remaining signatures concern completion recovery observation and automatic
worktree-base selection. No land delivery product defect or environmental cause
was demonstrated by these reproductions. No product or test files were changed.

## Verdict per signature

Historical row counts come from `1e5c17ce`'s
`docs/investigations/2026-10-19-rc-windows-new-reds.md`; they are not a new full-suite
run. Each row below selected exactly one case once.

| Historical signature | Representative at master | Result / test duration | Confirmed cause |
|---|---|---|---|
| Six C488 land POST failures | `C488_ReviewToLandReceiptMatrix(idle, raw)` | Failed, 177.950s; HTTP 409 `review_evidence_source_not_clean` | Fixture-generated Review omits the mandatory clean-source declaration. |
| One C488 stale-review assertion | `C488_ApprovalOutcomeReceiptMatrix(stale-A)` | Failed, 178.251s; expected `review_evidence_sha_mismatch` | Same omission; clean-source validation precedes SHA matching. |
| Two missing completion-scan assertions | `C488_ApprovalLostFlushRecovers` | Failed, 81.066s; no `completion-scan-*.observation.json` | Delivery succeeded through current recovery; the fixture suppresses an older wakeup and requires an obsolete path observation. |
| Ten C540 quiet-deadline failures | `C540_PreEnqueueCrashRecoversWarnings` | Failed, 119.380s; `before-enqueue (quiet 60s after 66.4s; progress 46)` | The fixture's divergent same-card branches block automatic base selection before warning intents are committed. |

These are category **(a), fixture/contract drift**, although only the first two
signatures concern the reviewed-source-clean contract. The evidence does not
support classifying these failures as category (b), broken land delivery, or (c),
environment. The tests remain red; this report is not a qualification pass.

## Source, execution, and retained evidence

- Built product/test source: `a0f3c4f3e728655933da1782981a88fb2b7b3a56`.
  `Antiphon.E2E.dll`, `Antiphon.Server.dll`, and `Antiphon.SessionRunner.dll`
  each report `1.0.0+a0f3c4f3e728655933da1782981a88fb2b7b3a56`.
- The preliminary report-only commit, made and pushed before long tests, is
  `216e3ac87f3b746c0e52bba88cdda7e787123abf`. That was checkout HEAD during
  execution; the already-built product/test source was unchanged. DLL hashes and
  versions are retained in `build-identity.json`.
- Windows, .NET 9.0.21, TUnit 1.44. One isolated E2E build passed with 36 warnings
  and 0 errors. `npm ci` and a fresh frontend build supplied the fixture's required
  `client/dist`. Metadata discovery found 171 cases; discovery is not execution.
- Build, frontend build, discovery, and all four case drivers used
  `scripts/build-slot.ps1`. Every driver obtained a lease. Discovery waited 64s
  for the memory floor; the four case leases were immediate. This wait did not
  produce any test failure. Each test process had a 300-second wall cap after
  slot acquisition. Driver walls were 180.510s, 180.835s, 84.291s, and 122.147s.
- Safe test environment policy was applied; inherited phone-home settings were
  cleared, phone-home disabled, and provider/parallel opt-ins cleared. The fixtures
  owned random-port servers, native FakeGrok, isolated runners, and PostgreSQL
  containers. Production 17204 was used only by the required build-slot broker.
  No AppHost or production runner restart, session stop, or configuration change
  was performed. All commands completed. No owned output process remained in
  the post-test process census; all 15 task-created `bin-c463014fb` directories
  were removed. `cleanup.json` and `output-inventory.json` retain that evidence.

Persistent, gitignored evidence root:

`C:\src\Antiphon\.antiphon\investigations\e2e-land-contract-463014fb`

Each case directory contains fresh `run.trx`, stdout/stderr, `run.json` (selector,
source, process ID, wall time, exit, cap result), and retained fixture evidence.
`results.json` consolidates the exact four counts; `sha256.json` hashes the retained
files. Driver logs preserve the `BUILD SLOT granted` / `released` lines. Original
fixture roots are beneath this worktree's `.antiphon/acceptance/card-0467/`:

| Case directory | Original fixture root leaf |
|---|---|
| `review-idle-raw` | `e048399db32e4a0fb9ba701dd6193636` |
| `stale-review` | `461c9434fcdb4ef99e66e30dfbb1c2f4` |
| `lost-flush` | `5755aea0c77444b386cdf492192b0d0a` |
| `dispatch-preenqueue` | `c401bcbbaf7f49c9b34378a85c7c7bee` |

## C488: omitted source-clean assertion and validation order

`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs:41` constructs the
synthetic Review report with subject, SHA, and `ordinaryScopeCompleted: Full`,
but no `reviewedSourceClean`. The omission is shared by raw, Apply, and spill
forms. `server/Application/Services/ReviewEvidence.cs:83` converts an absent
declaration to null; `ReviewEvidenceBindingService.cs:63` carries that nullable
value into the first-settlement candidate; `AgentTaskReplyService.cs:5173`
persists it on the outcome. Full scope does not imply clean source.

`server/Application/Services/LandApproval.cs:77` requires
`row.ReviewedSourceClean == true`; `:78` throws the observed refusal. The
subject/SHA comparisons are later, at `:107` and `:109`. Consequently the stale-A
fixture cannot reach its intended SHA-mismatch assertion at
`LandDeliveryFixture.Review.cs:406` with this report.

This is a deliberate contract: `docs/orchestration-loop.md:2282` states that
missing/false clean-source assertions refuse evidence-backed admission and that
old null outcomes need fresh Review. Git blame attributes the gate to
`31411ea310024568d45c3130b9678fd4da63d68f` (CARD-0835). The report does not infer
intent solely from that commit's WIP subject; the current owner policy explicitly
requires the behavior.

Stored evidence:

- `review-idle-raw/run.trx` contains the complete HTTP 409 body and exact code.
  Its `fixture/delivery-evidence.json` records caller
  `68de1b84-1206-44b2-b82c-4d84bec1b40e`, UserPrompt sequence **4**, row
  `f438fb90-ab5a-4016-b7fe-a030df97ce75`. That prompt contains Review evidence ID
  `94ef94b4-5c35-4d8c-b802-6471be9b4108`, subject
  `235bef75-f62d-442e-a3e6-2f713bb9f726`, and the delivered report missing the
  declaration. The owner has **0 land requests, 0 operations, 0 notifications**.
  The fixture had already asserted a complete correlated Review UserPrompt and
  Clean Final/Full outcome before calling land (`LandDeliveryFixture.Review.cs:117`,
  `:135`, `:367`).
- `stale-review/run.trx` contains the expected SHA-mismatch assertion failure.
  The corresponding original server log, `server-logs/antiphon-20261019.log:5654`,
  records HTTP 409 for owner `2a813fb9-9fe3-4042-9c01-c5ef5f5841bb`; `:5655`
  states **"Review evidence does not assert a verified clean source."** The next
  frame is `LandApproval.LoadUsableEvidenceAsync`, line 78. Those exact numbered
  lines are retained in `stale-review/fixture/server-refusal.txt`, resolving the
  earlier report's uncertainty caused by Shouldly's truncated response text.

The StageOutcomes row itself was not exported; its missing nullable assertion is
reconstructed from the retained synthetic report, parser/binding code, and the
specific gate exception. Review delivery was observed; land execution was not
admitted in these cases.

## Lost flush: successful delivery, obsolete scan expectation

`AgentTaskLandDeliveryE2ETests.cs:375` directly calls the C467 V28 method at
`:149`. It requests land without Review evidence, receives the outcome, verifies
remote publication and one complete prompt, and only then requires a
`completion-scan` file at `:154`. The fixture's optional evidence parameter is
omitted (`LandDeliveryFixture.cs:164`), so the clean-source gate cannot explain
this failure.

`LandDeliveryOptions.cs:121` suppresses `DropWakeup("completion", ...)` for
`lost-flush`; `AgentTaskLandNotificationService.cs:161` consults that seam after
queue insertion. However, `SessionMessageQueueService.cs:887` independently
schedules recovery for a committed delegation row, including these land outcome
rows, at `:890`. `CompletionNoteWork.cs:56` wakes that recovery queue, and
`CompletionNoteWorkHostedService.cs:30` drains due sessions into flushes without
emitting a `completion-scan` observation. That observation belongs to the durable
sweep at `:78` or missing-completion reconstruction at `:121`. The sweep's backstop
is 15 minutes (`:39`). This event-driven path was introduced by
`a16d82d72d083ba3e62960b8f9f3e7b036af07e9`; the old test assumption survived it.

The retained `lost-flush/fixture/delivery-evidence.json` proves:

- Request `e47a711c-52d7-479c-a35d-0d5de7d53649` produced operation
  `313cc7ea-fa12-4a8c-91c1-3a155d916046` and notification
  `f33343c7-af11-4d4e-855b-7641e2b8c265`.
- Notification state **Confirmed**, queue
  `eef4aead-0a34-4577-80e0-e487743db976`, **one** delivery attempt,
  verdict **Delivered**. Caller `8393f77b-122f-486e-86a3-eda34f5afa11` has
  matching UserPrompt sequence **4**, row `7a0c5b5d-69f7-43f2-bad5-b24651667c80`.
- The prompt states `publication=Landed; cleanup=Complete`, with matching
  expected/verified/remote SHA `7319cd5e1a6d0453bb4b26e0e2ab9ee86a07f097`.
  The fixture's remote and single-prompt assertions passed before the scan-file
  failure. `observation-counts.json` records **0 completion scans, 10 notification
  scans**.

Thus the failure is a test observation/coverage mismatch, not a lost delivered
outcome. This run does not qualify recovery after losing every current wakeup:
the existing cut leaves the direct recovery signal active.

## C540: automatic continuation blocks the warning fixture

`LandDeliveryFixture.Dispatch.cs:33` builds four same-card sibling branches:
an ancestor, an alias, a containing descendant, and a divergent branch. At `:57`
it inserts the new queued Worktree task without setting
`RequestedWorktreeBaseMode`; the enum's zero/default is **Auto**
(`server/Domain/Enums/AgentTaskEnums.cs:512`).

The current same-card selector gathers eligible unlanded Succeeded branches
(`AgentTaskWorktreeBaseResolver.cs:70`, `:96`), reduces containment, and returns
`worktree_base_ambiguous` when multiple maximal tips remain (`:309`). The dispatcher
blocks and commits that decision at `AgentTaskDispatcher.cs:5744` before returning
at `:5760`. Dispatch warning drafts are later, at `:6036`, so this refusal creates
no warning intents. `DispatchBaseWarningDeliveryE2ETests.cs:113` waits for a
boundary that this fixture never reaches.

CARD-0442 intentionally requires an explicit choice for divergent histories
(`docs/orchestration-loop.md:219`; `docs/session-runtime-invariants.md:1100`). The
dispatch guard was introduced by `ab9094127b309eacaa48340f37d7ee87566eaa96`.

Stored evidence is specific to this run:

- A read-only GET from the fixture's own child server is retained in
  `dispatch-preenqueue/task-detail.json`, with address/PID/time provenance beside
  it. Owner `49893cff-8746-49f3-b1dd-dfd90bae4160` is **Blocked**, mode **Auto**,
  failure `worktree_base_ambiguous`. The detail names competing
  `c540-containing` at `466dd40f94e40a0dbf7c7f6ecc1e014fabd177fc` and
  `c540-divergent` at `4ed630daa2acb7ad903ea0f1e05a1ac6acd3c432`.
- `fixture/delivery-evidence.json` records Blocked event
  `28b1b0b7-d145-4a2b-a20f-bb87563abc13` at
  `2026-10-19T17:09:18.406675Z`, **0 intents, 0 notifications, 0 queue rows,
  0 land requests, 0 operations**. Its sole caller UserPrompt is initialization.
  The event timestamp agrees with the live task-detail response.
- `fixture/dispatch-graph.json` preserves the four sibling identities and tips.
  The `wait-*.json` record reports `before-enqueue`, timeout after **66.3788213s**,
  protocol Git count **1 -> 46**, and a renewed quiet deadline. This is completed
  dispatch rejection followed by the expected quiet timeout, not an environment
  hang and not a land approval refusal.

One early read-only task GET returned 404 before `StartDispatchAsync` inserted
the task. Later reads captured the persisted Blocked row; no fixture state was
changed to obtain this evidence.

## Scope, uncertainties, and continuation

- The other 15 historical argument/case rows were not rerun. Their attribution
  uses the common fixture and, for C467/C488, the direct method alias, together
  with the earlier rc evidence. This is not current-master full-suite coverage.
- Repairs may expose later failures currently masked by admission/base selection.
  No source/test mutation or green rerun was performed in this investigation.
- There is no production incident claim here. The fixture's stored transcript
  and DB snapshots support the stated mechanisms, not general fleet health.
- Scope is **master-only**. No rc branch was changed. C488's seven rows belong to
  the CARD-0488 fixture / CARD-0835 contract follow-up. Lost-flush belongs to the
  CARD-0467/CARD-0488 completion-recovery fixture. C540 belongs to a separate
  CARD-0540 warning-fixture / CARD-0442 base-selection follow-up, not the
  reviewed-source-clean gate. No new card identifier is claimed by this report.

## Not done, noted

Smallest master-only repair directions for Plan: supply the C488 synthetic clean-source declaration (both land-success and stale-SHA signatures); refresh the C467/C488 lost-wakeup cut and observation for current recovery; give C540's intentionally omitted-sibling warning fixture explicit target-base intent (separate CARD-0540/CARD-0442 follow-up).

## Reproduction

The evidence root's `run-case.ps1` is the exact capped helper used. With the
requested source built into `bin-c463014fb/` and a fresh frontend bundle, each
case was invoked separately as follows; full selectors are also in its `run.json`:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c463014fb-<case> -SlotWaitMinutes 5 -- pwsh -NoProfile -File .antiphon/task-463014fb/run-case.ps1 -Case <case>
```

`<case>` is `review-idle-raw`, `stale-review`, `lost-flush`, or
`dispatch-preenqueue`. The first selects only UID
`Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ReviewToLandReceiptMatrix(System.String, System.String).1.1.0`;
the other selectors name their single methods. Each fresh TRX confirms exactly
the intended case. The helper and evidence remain preserved externally after
output cleanup; the compiled outputs must be rebuilt before another run.

--- next stage ---
next: plan
handoff: Plan master-only fixture repairs for C488 missing reviewedSourceClean, C467/C488 obsolete lost-flush observation, and C540 Auto-base ambiguity. Preserve the product contracts and current failure evidence; separate the C540 follow-up from the land-approval rule.
artifact: docs/investigations/2026-10-19-e2e-land-contract-reds.md
