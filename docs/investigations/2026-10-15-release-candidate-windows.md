# Release candidate Windows verification — 2026-10-15

**FAIL / INCOMPLETE.** The frozen Windows unit roster failed once; its one failure passed both isolation runs. The completed browser/API E2E partition failed with ten methods; nine fail at the morning baseline and one passes both isolation runs. No regression introduced today was demonstrated in the completed selections. **The full requested E2E set is not qualified:** three native delivery classes remain incomplete and require another Windows execution window. No product or test code was fixed.

## Source and execution identity

- Candidate: `release/rc-20261015T215926Z`, **067b47e2dc903247f1a7c8aa539032cc4266e4ad**. `git ls-remote` matched the assigned worktree HEAD before execution. The candidate ref was not moved.
- Morning baseline: **cc8798c1fa4f3c2faa834283aef080d8e5fd28f0**, tested in a separate detached worktree.
- Roster artifact: **d5e50c1d121bbaf0c8b88abe710a6d452e44a383**, `docs/investigations/2026-10-15-card-1287-windows-unit-{roster,filter}.txt`. The retained literal filter matches that commit. All **361** intended unit class identities executed, with no missing or extra classes.
- Candidate and baseline test assembly informational versions matched their respective full source SHAs. All four assembly SHA-256 hashes were unchanged after execution; see `build-identities*.json` and `baseline-e2e-build-identity.json` in the evidence root.
- Native Windows 10.0.19045, .NET 9.0.21, TUnit 1.44. The inherited parallel-test override was unset. Every driver used `scripts/build-slot.ps1`: **34 granted driver leases, zero unleased/skipped/unlimited drivers**. No production AppHost/server/runner was restarted or stopped. E2E fixtures used their isolated random-port runners. This pass used production 17204 for build-slot coordination.

Persistent evidence root:

`C:\src\Antiphon\.antiphon\investigations\rc-windows-1c3a9c75`

## Results per piece

Wall times include slot waits unless stated otherwise. Each completed test run has a fresh `run.trx`; the interrupted attempt has no final TRX. HTML reports and console logs are retained too.

| Piece | Outcome | Total | Passed | Failed | Skipped | Wall | Evidence under root |
|---|---|---:|---:|---:|---:|---:|---|
| Candidate isolated unit build | PASS, 0 errors / 1,057 warnings | — | — | — | — | 130.779s | `unit-build.log` |
| Frozen Windows roster, exactly one run | **FAIL** | 4,526 | 4,525 | 1 | 0 | **383.852s**; test duration **300.521s** | `unit/run.trx`, `unit.log` |
| Unit failure alone, candidate | PASS | 1 | 1 | 0 | 0 | 12.461s | `unit-isolate/run.trx` |
| Unit failure alone, morning baseline | PASS | 1 | 1 | 0 | 0 | 152.777s, mostly slot wait | `baseline-unit-isolate/run.trx` |
| Candidate client dependency installation and bundle rebuild | PASS | — | — | — | — | bundle build 96.921s including wait | `npm-ci.log`, `client-build.log` |
| Candidate isolated E2E build / matching Chromium installation | PASS | — | — | — | — | E2E build 168.195s including wait | `e2e-build.log`, `playwright-install.log` |
| Initial complete-selection E2E attempt | **INTERRUPTED / INCOMPLETE**, exit -1; no final TRX | — | 39 console-reported completions | 0 reported | — | 570.817s | `e2e.log`, `initial-e2e-completed-lines.txt` |
| Browser/API E2E partition, 22 classes | **FAIL** | **98** | **85** | **10** | **3** | **1,688.484s**; test duration **1,651.139s** | `e2e-browser/run.trx`, `e2e-browser.log` |
| Each E2E failure alone, candidate | **FAIL**, ten separate exact-method runs | 10 | 1 | 9 | 0 | 452.944s summed driver walls | `candidate-isolate-<Class>-<Method>/run.trx` |
| Each E2E failure alone, morning baseline | **FAIL**, ten separate exact-method runs | 10 | 1 | 9 | 0 | 438.579s summed driver walls | `baseline-isolate-<Class>-<Method>/run.trx` |
| Remaining native delivery E2E classes | **INCOMPLETE** | — | — | — | — | Separate continuation required | Selection below |
| Child/output/baseline cleanup | PASS | — | — | — | — | — | `process-cleanup.json`, `build-output-cleanup.json`, `baseline-cleanup.json` |

The baseline E2E build and its own client dependency installation and bundle rebuild passed before baseline browser execution. Isolation reruns did not rebuild or repair the candidate. Every failed method was run alone once at each SHA; each selected exactly one executed case. No assertion, test deadline or retry setting was changed. The two original aggregate failures remain red despite subsequent isolation outcomes.

## Every observed failing method

All E2E classes below have namespace `Antiphon.E2E`. `C/B` gives candidate-alone / baseline-alone result; each is one executed case. Introducing commits were not bisected for inherited failures: baseline execution already establishes that they predate today's candidate. **There are no demonstrated regression-from-today rows and therefore no introducing-today commit/card to assign.** Smallest-fix entries are proposed follow-up work, not implemented repairs.

| Method | Class | Error and isolation observation | C/B | Classification / introducing commit | Smallest follow-up fix |
|---|---|---|---|---|---|
| `C665_LockedFileMidDeleteResumesOnLaterPass` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` | `File.Exists(Path.Combine(aside, "keep.txt"))` expected false, got true; "deletion had started before the lock stopped it", line 457. Both isolated executions pass. | PASS / PASS | **flake**; introduction not established. Test and inspected deletion implementation files have no baseline-to-candidate diff. | Capture the first removal's refusal detail and make the mid-delete interruption deterministic while retaining resume/deletion assertions. Underlying transient mechanism remains unresolved; do not widen a timeout. |
| `Board_manual_xterm_command_marks_session_as_streaming_turn` | `BoardE2ETests` | Expected `StreamingTurn`; row showed `Succeeded`. Both isolated executions pass. | PASS / PASS | **flake**; introduction not established. | Synchronize command lifetime and observation with an explicit handshake so the four-second command cannot finish before the phase assertion. Keep the phase assertion. |
| `Board_user_can_review_real_worktree_diff_and_send_inline_comment` | `BoardE2ETests` | Aggregate: runner setup missed 15s readiness. Both isolated runs reach the body and fail waiting 15s for dialog text `Diff review` (line 502). | FAIL / FAIL | **pre-existing**, with an **environment** setup failure in the aggregate; before morning baseline, not bisected. | Repair the scenario/selector for the current worktree review UI; preserve proof of real diff contents and persisted inline comments. Do not simply remove the missing-label assertion without replacing its witness. |
| `Agent_files_review_threads_and_content_contracts` | `ContractSnapshotTests` | Aggregate: runner readiness failure. Both isolated runs fail `Normalize(existing)` for `agent-files.json`: actual file list starts with generated `CLAUDE.md`, stored fixture starts with `notes/report.md`. | FAIL / FAIL | **pre-existing**, initially masked by **environment** setup failure; before baseline. | Resolve intended generated-instruction-file visibility in the fixture, then review/update `agent-files.json` and matching stories. Do not blindly bless the response. |
| `Backlog_quadrant_contract` | `ContractSnapshotTests` | Aggregate: runner readiness failure. Both isolated runs fail `cards-backlog.json`: actual card adds `cardFileVisibility`, `hasPrivateNotes`, verification policies and platform metadata absent from the fixture. | FAIL / FAIL | **pre-existing**, initially masked by **environment** setup failure; before baseline. | Review the accepted card shape and refresh the contract fixture/story seed together. |
| `Delegated_task_board_and_drawer_contracts` | `ContractSnapshotTests` | `Normalize(existing)` mismatch for `agent-tasks.json`; actual includes required platform, runner and selection metadata after `scopeSource`, absent from the fixture. Repeats at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. | Review/update the affected delegated-task fixture and story seed for the accepted DTO fields. |
| `Home_tasks_rail_contract` | `ContractSnapshotTests` | `Normalize(existing)` mismatch for `home-tasks.json`; actual adds `requiredPlatform`, `runnerId`, `observedPlatform` after `completedAt`. Repeats at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. | Review/update the home-task fixture and story seed for the accepted platform fields. |
| `Pipeline_status_contract` | `ContractSnapshotTests` | `Normalize(existing)` mismatch for `pipeline.json`; first difference is Plan `recommendedInFlight`: actual **3**, fixture **1**. Repeats at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. | Bind concurrency inputs explicitly in the owned fixture, then review/update the pipeline JSON and stories against those effective settings. |
| `C557_combined_glance_on_phone` | `PipelineGlanceE2ETests` | Candidate link `box.X + box.Width` is **565**, expected at most **391** for a 390px viewport, line 74. Same numbers at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. Existing scenario owner CARD-0557; no today introduction. | Make pipeline panel/row ancestors shrink to the mobile viewport; inspect width/min-width constraints and retain the 390px bounding-box and no-horizontal-scroll assertions. |
| `Delete_workflow_button_shows_confirm_and_removes_card` | `WorkflowDeleteTests` | 10,000ms timeout waiting for `Locator("[role='article']").First` on the home page, line 345. Same failure at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. | Point the deletion scenario at the current workflow-list page and its actual card/row affordance; retain confirmation and removal/persistence checks. |
| `Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard` | `WorkflowDeleteTests` | 10,000ms navigation timeout: expected `**/`, observed `/workflows`, line 294. Same failure at both SHAs. | FAIL / FAIL | **pre-existing**; before baseline. | Align the destination assertion with the accepted workflow-list route while retaining the deletion assertion and existing timeout. |

Thus the **eleven unique observed failing methods** comprise **two observed flakes and nine inherited E2E failures**. Three aggregate startup errors are retained as environmental observations, not erased by the different body errors exposed in isolation. For snapshot methods, the first mismatched snapshot stops that method; downstream assertions inside the same method are not certified.

The original environmental refusals named distinct isolated random-port runners that did not become healthy within **15 seconds**. During these failures a host sample recorded **98% CPU** and **4,787 MiB free physical memory** out of 32,709 MiB (`host-load-0033.json`), below the broker's 6 GiB admission floor. This supports host-load involvement; it is not an execution bisection or proof that every error has that cause. Readiness deadlines were not extended.

## E2E scope and the remaining gate

The candidate RC policy explicitly excludes `DelegationPipelineE2ETests`, `DelegationSequencingE2ETests` and `OutputDistillationApplyCanaryTests`. Its safe environment was used, including clearing inherited headed/distiller opt-ins. The completed partition matched all 22 intended class identities with no missing or extra classes.

Three `ChannelOutboundIsolatedTests` cases were discovered and **skipped**, not passed:

- `FakeGrok_outbound_mode_reads_frozen_request_and_invokes_a_tool`
- `Four_sources_convert_only_for_the_selected_conversation`
- `Static_logger_reaches_owned_host_file_after_restart`

Their required isolated opt-ins were absent. No live provider or headed canary was enabled.

The first all-class E2E attempt was deliberately stopped after its native delivery cases consumed approximately four minutes per case. It reported 39 passes, including `C494_ReviewReceiptRejectsFalseEvidence(QueuedUserPrompt)` and `(partial-middle)`, but produced **no final TRX**. Those console observations are not a class or full-suite certificate and must not be added to the 98-case partition counts. The completed partition was then run to collect browser/API/CLI failures within the brief's **90-minute budget**.

These three native classes still need a complete execution at the pinned candidate and all resulting failures need isolation/baseline attribution:

- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests`

Their full set cannot fit in the remaining authorized window at the observed pace. A separate Windows window with sufficient native-test time and host headroom is required. This report neither qualifies the candidate nor establishes absence of regressions in those uncompleted classes.

The known `CheckpointToolProjectContractTests` and `HostCleanupCheckpointCustodyTests` issues named in the brief did not appear: both classes are already excluded from the frozen 361-class roster. This run supplies no new execution proof for either excluded class or its master-only fixes.

## Evidence and reproduction

The external root retains full aggregate and individual TRX/errors/stacks, literal filters/rosters, timing, build logs and identities, `e2e-browser-cases.json`, `e2e-triage-summary.json`, `e2e-triage-details.json`, selection and lease audits, browser/server/runner logs, HTML/screenshots and the interrupted native-fixture evidence. Baseline evidence remains there after removal of the temporary checkout.

Reproduce in a fresh worktree at the exact candidate SHA, with fresh results directories. Client dependencies and `client/dist` must be rebuilt there before browser execution. All drivers remain gated. The retained `unit-filter.txt` and `e2e-browser-filter.txt` contain the literal executed selections.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-windows-unit-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rc-windows/ --nologo
$filter = (Get-Content 'C:\src\Antiphon\.antiphon\investigations\rc-windows-1c3a9c75\unit-filter.txt' -Raw).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-windows-unit -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc-windows/ -- --treenode-filter $filter --report-trx --report-trx-filename run.trx --results-directory '<fresh-unit-results>'

# After the owned client bundle, isolated E2E build and Chromium prerequisites:
$native = '/*/*/(AgentTaskLandDeliveryE2ETests*)|(DispatchBaseWarningDeliveryE2ETests*)|(WorktreeRetirementDeliveryE2ETests*)/*'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-windows-native -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-rc-windows-e2e/ -- --treenode-filter $native --report-trx --report-trx-filename run.trx --results-directory '<fresh-native-results>'
```

For individual failures use `/*/*/<Class>/<ExactMethod>` with the appropriate built project/output and another fresh results directory. Baseline comparisons must use a separate checkout of `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0` with its own rebuilt bundle/output. The retained `rerun-e2e-failures.ps1` records this pass's exact driver arguments, bounded slot waits and cutoff; it is historical execution evidence, not a reusable unattended policy.

## Restoration and handoff

No product/test repair was made. The one tracked screenshot overwritten by the E2E suite was preserved externally, then restored to the candidate. Both source checkouts were clean before this note was added. All four assembly hashes matched their recorded build identities before cleanup. Final task-child census was **zero**; the interrupted attempt's owned children had also been stopped before the replacement run. Production services were untouched.

Cleanup removed **73 task-owned alternate-output directories / 7,993 files**, with **zero** remaining inventoried output directories. The temporary baseline worktree was identity-checked at the morning SHA and removed through ordinary `git worktree remove`, without force. Generated evidence remains outside this worktree and is not committed.

The caller can commission separate repairs for the inherited contract/UI/test failures. A fresh complete native E2E continuation at the unchanged candidate is still needed; fixing unrelated master code does not qualify this immutable candidate. Keep release qualification incomplete until the required native execution and any new failure attribution are complete.

--- next stage ---
next: decide
handoff: Allocate a further Windows native E2E window for the three named classes at 067b47e2dc903247f1a7c8aa539032cc4266e4ad; retain the failed unit/browser results and commission inherited-failure repairs separately.
artifact: docs/investigations/2026-10-15-release-candidate-windows.md
