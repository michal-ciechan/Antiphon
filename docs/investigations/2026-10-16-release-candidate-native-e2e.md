# Release candidate native E2E verification - 2026-10-16

**FAIL / INCOMPLETE.** The two-hour Windows window executed **42 primary cases: 24 passed, 18 failed, 0 skipped**. All three requested classes were exercised, but **27 of 52 landing cases remain unrun**. The window was bounded to preserve required failure comparisons and cleanup; driver slot waits and memory admission consumed part of it. This pass does not complete native qualification or clear the earlier Windows unit/browser failures.

16 failing cases reproduce at the morning baseline; 2 failing cases still lack a baseline verdict. **5 required one-case comparisons remain unrun.** No regression introduced between baseline and candidate was demonstrated in completed comparisons; unexecuted cases and pending comparisons remain uncertified.

## Source and execution identity

- Candidate build source: **067b47e2dc903247f1a7c8aa539032cc4266e4ad**. Test/server/runner DLL informational versions all name that full SHA. The report-only preliminary commit was made after the build and before long test execution; its checkout HEAD was `af46ae31eb1f2917e4f6ff2da72fe66ba2da08df`. Product and test source were unchanged throughout execution.
- Morning baseline: **cc8798c1fa4f3c2faa834283aef080d8e5fd28f0**, built and tested in a separate detached Windows worktree with its own fresh client bundle and output. The three baseline DLL informational versions name that full SHA.
- Native Windows 10.0.19045, .NET 9.0.21, TUnit 1.44. Each candidate/baseline prerequisite and test driver went through `scripts/build-slot.ps1`. At most the host-granted leases ran concurrently; each test process owned isolated random-port runners/Postgres fixtures. `PhoneHome__*` was cleared, then `PhoneHome__Enabled=false` and `PhoneHome__ServerOrigin=http://127.0.0.1:1` were set. Policy headed/live opt-ins, `ANTIPHON_PTY_BACKEND` and `ANTIPHON_*PARALLEL*` were cleared for the test helpers.
- This task did not restart or stop the production AppHost/runner, and no fixture used production runner/server ports. Production 17204 supplied build-slot coordination only. No product/test code, assertion, retry count or test deadline was changed.

Persistent raw evidence root:

`C:\src\Antiphon\.antiphon\investigations\rc-native-30129456`

## Primary class results

Wall is the sum of sequential driver walls for that class, including lease waits. Test time comes from the fresh TRX run start/finish. Comparison drivers overlapped primary runs, so driver walls must not be added to infer elapsed task time. Zero-test attempts are accounted separately below.

| Class (`Antiphon.E2E`) | Verdict | Planned | Executed | Passed | Failed | Skipped | Unrun | Driver wall | TRX time |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `DispatchBaseWarningDeliveryE2ETests` | **FAIL** | 12 | 12 | 2 | 10 | 0 | 0 | 1,313.242s | 1,308.174s |
| `WorktreeRetirementDeliveryE2ETests` | **PASS** | 5 | 5 | 5 | 0 | 0 | 0 | 237.226s | 202.958s |
| `AgentTaskLandDeliveryE2ETests` | **FAIL / INCOMPLETE** | 52 | 25 | 17 | 8 | 0 | 27 | 3,209.665s | 2,660.237s |

The landing partitions selected exactly **1, 1, 7, 10 and 6** cases. Their 25 results have no duplicate primary case identities. Metadata-only discovery independently found 52 landing, 12 dispatch and 5 worktree cases. The 24-method source census expands to 52 landing cases; `land-coverage.json` records expected/executed rows per method. No unrun case is counted as passed or TRX-skipped.

candidate-alone comparisons: **15 executed, 15 failed**, 4,069.124s summed driver wall; each completed selection has one case.

baseline-alone comparisons: **16 executed, 16 failed**, 3,803.844s summed driver wall; each completed selection has one case.

## Every observed failing case

D = `DispatchBaseWarningDeliveryE2ETests`; L = `AgentTaskLandDeliveryE2ETests`. C/B shows candidate-alone / baseline-alone execution. Every completed comparison selected exactly one case; parameter rows use discovered `--filter-uid` selectors without a simultaneous tree filter, with independent name/count audits. Full errors, stacks and exact selectors are in `failure-triage.json`, `comparison-cases.json` and the individual TRX/logs.

Introducing commits are **not established** for inherited failures: execution bounds them before/at the morning baseline, but no historical bisection was commissioned within this window. Pending rows have no introducing-commit attribution. None is falsely assigned to a candidate-day commit.

| Class | Case | Error | C / B | Classification | Introducing commit |
|---|---|---|---|---|---|
| D | `C540_CollapsedWarningsReachIdleCaller` | TimeoutException: two committed reduced dispatch intents (quiet 60s after 67.5s; progress 45) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_CollapsedWarningsWaitForBusyCaller` | TimeoutException: two committed reduced dispatch intents (quiet 60s after 72.0s; progress 45) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_ClaimCrashRecoversCollapsedWarnings` | TimeoutException: dispatch-warning-claim-committed (quiet 60s after 67.0s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_ProjectionCrashRecoversOriginalPairs` | TimeoutException: dispatch-warning-before-commit (quiet 60s after 97.1s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_PreEnqueueCrashRecoversWarnings` | TimeoutException: before-enqueue (quiet 60s after 66.7s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_EnqueueFailureRetriesWarnings` | TimeoutException: two committed reduced dispatch intents (quiet 60s after 67.2s; progress 45) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_QueueInsertCrashReusesRows` | Primary: `native caller ready` timeout. Both isolated runs reach `queue-inserted` and time out. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_PreTypingCrashRecoversWarnings` | TimeoutException: queue-before-typing (quiet 60s after 97.1s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_PostPromptCrashDoesNotRetype` | TimeoutException: queue-before-verdict (quiet 60s after 67.7s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| D | `C540_ReceiptCrashDoesNotRetype` | TimeoutException: receipt-before-save (quiet 60s after 68.1s; progress 46) | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C467_V28_LostFlushWakeupRecoversOnIdleCaller` | Expected nonempty `completion-scan-*.observation.json`; no files found. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ApprovalOutcomeReceiptMatrix(stale-A)` | Expected `review_evidence_sha_mismatch` in the rejected Land response; assertion did not find it. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ReviewToLandReceiptMatrix(idle, raw)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ReviewToLandReceiptMatrix(idle, Apply)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ReviewToLandReceiptMatrix(idle, spill)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | FAILED / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ReviewToLandReceiptMatrix(busy, raw)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | NOT RUN / FAILED | **Pre-existing** | Before/at baseline; not bisected |
| L | `C488_ReviewToLandReceiptMatrix(busy, Apply)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | NOT RUN / NOT RUN | **Comparison incomplete** | Not established |
| L | `C488_ReviewToLandReceiptMatrix(busy, spill)` | Land script exit 1; HTTP 409 `review_evidence_source_not_clean`. | NOT RUN / NOT RUN | **Comparison incomplete** | Not established |

The original `C540_QueueInsertCrashReusesRows` setup timeout is retained as an environmental observation; its body failure was exposed independently at both SHAs. Other inherited attributions rely on the case-specific baseline assertion/error in the retained receipts, not on an assumption that an entire failure family is inherited.

## Required continuation

The following landing methods have **no execution evidence in this pass**. They were not started after the active six-row matrix because the remaining budget was reserved for required one-row failure attribution and cleanup. Prior interrupted console observations do not certify them.

| Missing method | Cases |
|---|---:|
| `C488_ReviewDeliveryCrashMatrix` | 9 |
| `C488_ApprovalDeliveryCrashMatrix` | 1 |
| `C488_ApprovalEnqueueFailureRecovers` | 1 |
| `C488_ApprovalLostFlushRecovers` | 1 |
| `C488_ApprovalBusyCallerDoesNotBlock` | 1 |
| `C488_ReviewEvidenceCrashRecovers` | 1 |
| `C494_ReviewReceiptRejectsFalseEvidence` | 8 |
| `C488_ApprovalQueueInsertCrashReusesRow` | 1 |
| `C488_ApprovalReceiptSaveFailureNeverRetypes` | 1 |
| `C488_ApprovalPollingCannotConfirm` | 1 |
| `C498_FailureOutcomeReachesCaller` | 2 |

Commission a further Windows window at the unchanged candidate for these **27 cases**, plus each pending comparison in `pending-comparisons.json`. Use fresh results directories and the prerequisites from the prior Windows report: owned `npm ci`/bundle rebuild, isolated E2E build and matching Chromium. All drivers stay gated; fixtures own random runners, and phone-home remains disabled. `missing-land-filter.txt` contains the literal documented wildcard-OR selection for only the missing methods. Historical helper scripts retain this attempt's expired budget cutoffs and are not a reusable unattended policy.

```powershell
$missing = (Get-Content 'C:\src\Antiphon\.antiphon\investigations\rc-native-30129456\missing-land-filter.txt' -Raw).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-native-continuation -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-rcnative-followup/ -- --treenode-filter $missing --report-trx --report-trx-filename run.trx --results-directory '<fresh-results>'
```

Build `bin-rcnative-followup/` at the exact candidate before that command. For each pending parameter-row comparison use its retained UID with `--filter-uid`, and verify exactly one executed case with the expected display name. Do not repeat completed selections just to fill a new aggregate report.

## Prerequisites, execution errors and custody

Candidate and baseline fresh dependency installs, client bundles, isolated E2E builds and Chromium prerequisites passed. Each E2E build had zero errors and 36 warnings. Help and metadata-only UID discovery were additional gated commands needed to select failing parameter rows; discovery is not execution proof.
Two initial method-OR attempts omitted the discovery wildcards and ran **zero tests** (native exit 8). They cost **387.557s** summed driver wall including waits, are retained unchanged, and contribute no coverage or red proof. The documented `Method*` form then executed the intended 7-case partition. This was an execution mistake, not a product regression.

Lease audit: **50 granted drivers, 0 unleased/unlimited/skipped drivers, 0 slot timeouts**. Slot and memory-floor waits remain in the raw logs; one host sample recorded 81% CPU and 6,717 MiB free of 32,709 MiB. No host budget or memory floor was overridden.

Cleanup: **0 remaining owned native child processes**; 0 residual process trees killed after identity checks. All **6** recorded test/server/runner assembly hashes were unchanged after execution. **30 alternate-output directories / 2356 files** were removed from the two verified task-owned checkout paths. The baseline was clean before ordinary `git worktree remove`. Git removed its registration but refused filesystem deletion on a long filename; the remaining exact owned path was checked for links, read-only file bits were cleared, and verified files/empty directories were deleted individually. No Git force option was used.

Raw candidate/baseline fixture trees, server/runner/native-input evidence and TestOutput diagnostics were copied to the external root before baseline/output removal. Reports, literal selections, UID/name audits, class/method coverage, timing, build identities, lease audit and restoration records remain there. Generated payloads are not committed. The only tracked deliverable is this report; the candidate code and release ref were not changed.

--- next stage ---
next: decide
handoff: Keep RC qualification incomplete. Commission the exact 27 missing landing cases and any pending one-case comparisons at the pinned SHAs; commission inherited-failure repairs separately.
artifact: docs/investigations/2026-10-16-release-candidate-native-e2e.md
