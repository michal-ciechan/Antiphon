# CARD-1892 g90 Final Review, round 2

Reviewed source: `9c3dc075d2085e41d21e26bc9b6bc3afc36bad03` on `feat/card-task-ac7f437c` (Code task `ac7f437c-c7df-4e32-ba07-a57dce8c6eac`). Range `f701b2ba967d503427d7064fe4fffa17b8c1738d..9c3dc075d2085e41d21e26bc9b6bc3afc36bad03` (21 commits). Round 1 was Review `3afa3cfa`. This round is blockers and regressions only. Round 1 blocker D-1 was a standing-session switch that could stay on HTTP 409 `standing_resume_delivery_pending` with no operator exit when a Sent row can never late-confirm.

Verdict: no blocker. The operator exit is `POST /api/sessions/{id}/messages/{messageId}/abandon`. It requires the operator token, records one audit in the same transaction as the status change, reuses `Canceled`, sends no input, and leaves confirmed and Pending rows refused. Recovery selectors that can type still require `Sent` or `Pending`.

`reviewedSourceClean` is false. The rows below are leased `dotnet run` receipts through `scripts/build-slot.ps1`, not checkpoint-tool receipts. Native output, `UseAppHost=false`, `TEMP=/tmp`. Positive controls PC-1 through PC-10 and the PC-11 variants stay pending. Native Windows placement stays pending.

## Abandon behavior

`SessionMessageQueueService.AbandonAsync` trims the reason and refuses an empty, whitespace, or over-1000-character reason with `ValidationException("reason")` before it takes the session lock. A missing operator name is `ValidationException("operator")`. It then takes `GetLock(sessionId)`, begins a transaction, and `SELECT ... FOR UPDATE` on the session row. One `SaveChanges` writes `Canceled`, `CanceledAt`, and a single `AuditRecord` whose id is the message id and whose summary starts `operator-abandon:{messageId}; session={sessionId};`. Commit publishes both. An uncommitted dispose rolls both back. A second call that still sees `Canceled`, a null verdict, and that audit returns the original `CanceledAt` and does not add another audit.

The route uses `OperatorCredential.Require`, the same header check as the other operator-only routes (`X-Antiphon-Operator-Token`). A missing or wrong token is 403 `operator_token_required`. There is no separate role gate. A message that belongs to another session, or a missing session or message, is `NotFoundException`. Delivered, LateConfirmed, and Pending rows are 409 `queue_abandon_not_unconfirmed`. Ordinary DELETE remains the Pending cancel. The method does not call a send, Enter, or retype path. The response warning says the message may already have been delivered and that abandonment does not recall it.

The switch predicate is unchanged from round 1: while switching, a source-session row that is `Sent`, has a null verdict, and has `DeliveryAttempts > 0` returns 409 `standing_resume_delivery_pending`. The text names the blocking ids, `GET /api/sessions/{sourceId}/messages`, the abandon POST with the operator token and a JSON reason, and ordinary DELETE for Pending ids. After the row is `Canceled`, that query no longer matches. `switching` is `sourceId != chosenSessionId`. A fresh start nulls the previous session, so a persistent source still switches. Select-all is a different-session resume and uses the same block. Same-session resume is not switching.

Claim and recovery queries that can type require `Sent` or `Pending`: composerless unknown attempts, the fresh and interrupted Sent selectors, and the Pending flush. `GrokRulesRefreshService.Judge` returns immediately when the row is `Canceled`, so a later transcript prompt does not set LateConfirmed and does not create a replacement rules row. `LateConfirmAttemptedMessagesAsync` is only given rows those Sent selectors already loaded.

## Evidence at the reviewed SHA

| Row | Result |
|---|---|
| CP-1 `SessionQueueAbandonTests` | 17 passed, 0 failed |
| CP-2 `StandingSessionSwitchConcurrencyTests` | 31 passed, 0 failed |
| CP-3 standing selection, ownership, queue switch, recovery HTTP | 54 passed, 0 failed |
| CP-4 agent control and standing-resume readiness | 39 passed, 0 failed |
| CP-5 continuity recovery and restart escalation | 63 passed, 0 failed |
| CP-6, CP-8, CP-9, selector, inside one named batch | 144 passed, 0 failed, 10m 58s. Seven classes were batched, so this row exceeded the 10-minute guidance. It is under the 30-minute Debug threshold. Compose below split them. |
| CP-7 `GrokRulesReadyOrderingTests` | 5 passed, 0 failed. Compaction recovery contributed 11 passed results inside the named batch. Together they meet the floor of 16. |
| CP-10 queue service and delivered-spill recovery | 31 passed, 0 failed |
| CP-11 interrupted attempt and Codex pointer | 52 passed, 0 failed |
| CP-12 attention | 218 passed, 0 failed |
| CP-13 five policy guards | 53 passed, 0 failed |
| CP-14 contract guards | 61 total, 58 passed, 2 failed, 1 skipped. Classified below. |
| CP-15 `ContractSnapshotTests` | 5 passed, 0 failed |
| CP-17 and CP-18 Grok rules | 80 passed, 0 failed |
| CP-20 client typecheck, lint, Vitest | typecheck 0, lint 0, 37 passed (`SessionMessageQueue` 5, `AttentionPanel` 32) |
| CP-21 `npm --prefix client run build` | exit 0 |

`scripts/check-evidence-diff.ps1` over the 21-commit range: `commits=21 entries=0 violations=0`. `git merge-tree --write-tree origin/master HEAD` against `8b5afc6ba560e612a75595a83954589e5453d9cf` exited 0, tree `4e7e94e0d09e75a4743eac5c902f8874291c5449`. No migration files are in the diff, and the impacted-class kinds do not include migrations, so the downgrade selector count is 0.

OS-branch witness checklist: changed product paths are portable. The only `OperatingSystem.IsWindows()` hits are a pre-existing path comparison in `AgentControlService` and sample source inside the selector fixture. `scripts/check-os-witnesses.ps1` exited 0. `witnesses` is empty. `restartImpact.required` is false. No startup or restart script changed.

Nightly pins: `tests/test-execution-policy.json` is byte-identical to `origin/master` (sha256 `4a06a1f04978ecb92ccad03091b9c7a1a43a63a775ee7dddee73739595d51531`). `scripts/test-live-lane-run.ps1 -PrintInputDigest` reproduced `67e50252fffbb65df7cf45d9187bb8a9cdaaec9ef12333103b883c60f313f3b8`, equal to the stored `PinnedInputDigest`. The branch does edit `tests/impacted-classes.json`. That edit merged cleanly, and the selector class passed on the composed tree.

## Compose on current master

Scratch tree `/tmp/review-18072e35-compose2`, detached `f15599e225de2024489e6371f1cad30fab810a31`, is a clean cherry-pick of the 21 commits onto `origin/master` `8b5afc6b`. `scripts/regenerate-derived-impact-index.ps1` reported `derived-impact-index unchanged` (windowsOnly 31, linuxOnly 1). Each row was its own build-slot lease, `--no-build`, after one `Antiphon.Tests` build in that tree.

| Row | Result |
|---|---|
| Derived-index driver, then `regenerate_script_reports_changed_then_unchanged` | driver build exit 0; 1 passed |
| `SessionQueueAbandonTests` | 17 passed, 52s |
| `StandingSessionSwitchConcurrencyTests` | 31 passed, 45s |
| Agent control and standing-resume readiness | 39 passed, 46s |
| Phone-home outage mention and Grok rules compaction recovery | 19 passed, 1m 04s |
| Land-refusal witnesses and mid-turn refine | 63 passed, 1m 27s |
| `SessionMessageQueuePhoneHomeDropTests` | 5 passed, 49s |
| `CodexCliObservationGapTests` | 7 passed, 5m 57s |
| `ImpactedClassesSelectorTests` | 50 passed, 5m 16s |
| Queue service, spill recovery, interrupted attempt, Codex pointer | 83 passed, 2m 05s |
| Five policy guards | 53 passed, 1m 27s |
| Mechanical PC, platform guidance, layering, lane category | 27 passed, 18s |
| `WindowsUnitPlatformAdmissionTests` plus the other derived-index tests | 32 passed, 1 failed, 1 skipped, then the failed method rerun passed |

The one compose failure was `regenerate_script_reports_changed_then_unchanged` before the driver existed in that scratch tree (`-NoBuild` looks for `scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll`). The same miss happened at the reviewed SHA and passed after the driver build. It is not a product defect. `C1210_Roster_closure_matches_source_gates` passed in that compose run (55s). `C1210_Windows_linux_only_outcomes_are_exact` skipped, which is the documented Windows-only child audit.

An earlier compose invocation exited 134 because the treenode filter put a `/` inside parentheses. That was a bad filter. The counts above are from the valid rerun.

## Inherited C1210

At the reviewed SHA, `WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates` failed: `c1210-unrostered-gate: Antiphon.Tests.Scripts.VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent`. The branch range does not edit `WindowsUnitSkipRoster.cs`. The roster line is present on `origin/master` and absent on this base. The same method, run alone at bare `origin/master` `8b5afc6b`, passed 1/1. It also passed on the composed tree. This is the Merge 229 repair inherited from the older base. It is not a CARD-1892 blocker. The other CP-14 failure was the missing derived-index driver, rerun 1/1 after that build.

## Mutants judged from the tests

These were not applied as source edits. Each named mutation fails an assertion that passed:

- Making abandon call the input path fails `Inputs.ShouldBeEmpty` in `SessionQueueAbandonTests` (lines 66, 155, 173, 218, and 280).
- Dropping the audit fails the single-audit assertion at line 92.
- Allowing a confirmed row fails the 409 cases.
- Dropping the `Canceled` return in `GrokRulesRefreshService.Judge`, or letting a claim query select `Canceled`, fails the Grok resurrection witness or the flush `Inputs` assertions.
- Narrowing the switch guard back to Pending-only fails `Operator_abandon_of_unstartable_composerless_input_unblocks_history_switch` before the abandon, because the switch still throws `ConflictException`.

## Advisories

These do not hold the land.

- The whitespace HTTP test checks status 422. It does not assert the `reason` field name. The over-1000-character reason uses the same `ValidationException("reason")` and has no separate HTTP test. A missing session id is `NotFoundException`; the HTTP test covers the wrong-session case.
- There is no injected fault between the status write and the audit. Both are one `SaveChanges` in one transaction.
- The per-session queue listing and the fleet attention projection of current unresolved rows have no page cap. The queue returns the full body, as the Pending listing already does. Attention evidence stays at 400 characters. Unconfirmed rows get Open and Abandon, not Send now.
- Abandoning the current Grok rules row leaves that generation `Pending` and closed, so ordinary input on that same session stays held. The switch is unblocked because the guard looks at `Sent`, not at rules state. Judge does not create a replacement prompt.
- The new switch test drives `ResumeSessionId`. Fresh and Select-all share the `switching` predicate rather than a separate abandon test.
- `GraceConfirmAsync` and `HandleDeliveryFailureAsync` can stamp a verdict by id if called without the queue lock. Production flush and SendNow hold `GetLock` for the delivery. This is the existing single-writer assumption.
- DELETE of a Sent row still returns the unchanged queue. The UI does not offer Remove for a Sent row.
- CARD-1926 stays carded. This range still has no `C0824WorkspaceSlotCustodyTests` repair.
- The reviewed-SHA named batch of seven classes took 10m 58s. Compose ran those classes as separate rows, each under 10 minutes. Host load is CARD-1906. No new performance card.

## Pending

PC-1 through PC-10 and the PC-11 variants remain pending for method-scoped mutation. Native Windows proof remains pending, including the Windows-filtered Grok delegate, queue pty, and `SessionQueueReceiptPlumbingTests` rows. `C1210_Windows_linux_only_outcomes_are_exact` remains a Windows placement skip.
