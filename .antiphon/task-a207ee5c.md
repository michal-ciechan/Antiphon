# Review a207ee5c: CARD-0747 aged land-note expiry, Held supersession, sweep (round 1, Final)

**Verdict: BLOCKED. One blocker (D-1).** Subject `feat/card-task-3593eb2f`, reviewed tip
`117987081300698d8522099c24cb951ccdf400ea`, range `3db1a43b0..117987081` (code is final at 6dfcb374;
117987081 adds only the Debug report). Code owner: task 3593eb2f (Grok Code author).

## Rows run at 117987081 (server2, Linux, build-slot leased, native output, UseAppHost=false)

No `### Checkpoints` table exists, because this is a Debug branch with no plan. So I ran direct build-slot rows, which the reason above covers.

| Row | Selection | Result |
|---|---|---|
| Build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | exit 0, 3m16s, lease ae92f02c |
| Touched | C0747LandReceiptReplayTests 16, AgentTaskLandQueuedReceiptTests 22, AgentTaskLandReceiptTests 42 | **80/80**, 0 skipped, 58 s |
| Guards + neighbours | TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, AgentTaskLandMonitoring 26, AttentionFeedNoise 14, SessionMessageQueueService 26, AgentTaskCheckSweep 37 | **155/155**, 0 skipped, 2m30s |
| check-evidence-diff | 3db1a43b0..117987081 | 7 commits, 0 violations |
| merge-tree | origin/master b02f110fb (master moved past 3db1a43b) | clean, tree 464c95886 |

I did not re-run the mutants independently. The author reports 15 mutant runs, all killed. The PCs stay pending for SourceLanding Mutation.

## Checked and correct

- **Never closes typed-uncertain work.** A row typed once that is still Pending (`DeliveryAttempts > 0`) returns `TypedPending` before any write, and its late receipt still confirms (`typed-pending-control` arm; sweep cohort `typed`).
- **Sent row with a stored receipt.** It returns `ReceiptPresent` and the ordinary path confirms it (C641_Historical_1cef_shape is in the 22 queued-receipt tests).
- **Younger notes.** A note younger than the cutoff returns `NotAged` as its first check. The reconcile setting is 7 days and 0 disables it. The sweep minimum is 7, and 6 is a 422.
- **No lost non-terminal note.** Terminality (the request is not pending, or a request-less task is Succeeded/Failed/Canceled) is checked before any cancel. ProviderInputWait and LegacyCheckNote are never terminal. The cancel runs under the queue lock with the fresh predicate `Pending && attempts==0`. If it races with typing, the result is `Changed` and nothing is written. The hosted scan bounds the lock wait to 1 s and records the session busy (CARD-1705 shape).
- **Performance.** `DueNoteIds`/`DueNoteIdsAfter` already exclude `LegacyUnverified` and `NotRequired`, so a settled note leaves the 5 s loop. The age check is the first, cheap test in the reconcile.
- **Writes and schema.** The change only settles or marks: no delete, no raw SQL, no migration, and existing states are reused. A second apply is idempotent; the test pins `Scanned=2, Settled=0`.
- **Held supersession.** It runs only for unlinked notes, or for a linked row that was never typed (canceled under lock). A typed Held row keeps its receipt scan (`sent-control`).
- **Carried completion.** `FollowsCarriedCompletion` requires the row to be keyed to this note, the digest to match, the row to be Sent and typed, and a generation to be recorded. A prompt that appears only on the parent does not confirm.

## Defects

### D-1 (blocker): the apply route is not operator-authenticated
- **Where:** `server/Api/Endpoints/AgentTaskEndpoints.cs:143-160` (`POST /api/agent-tasks/land-notifications/expire`).
- **Failure:** `apply: true` refuses only a child task token. A caller with no token is accepted: any local agent shell, or the public vhost, which reaches Kestrel as loopback through Caddy (docs/ops-http.md, Runner seats row). That caller can settle and cancel notes. With `Supervision:DeliveryVerification:LandNoteReceiptExpiryDays=0` (the documented kill switch), the route still applies the 7-day rule, so an unauthenticated POST bypasses the operator's off switch. No test covers the route, so the child-token refusal is also unpinned.
- **Why:** The brief requires an operator-authenticated endpoint. The repo already has the operator credential for operator mutations: `OperatorCredential.Require` with `X-Antiphon-Operator-Token` is used by runner drain/retire/seat release, operator shutdown, instructions refresh and runner retention execute.
- **Fix:** On `apply`, call `OperatorCredential.Require(http, IOptions<PhoneHomeRunnerSettings>.Value, "...")` (403 `operator_token_required`). The dry run can stay open. Add an endpoint-level witness: no token or a child token gives 403 with nothing written, the operator token applies, and a bare POST is a dry run. Update the ops-http row to name the header.

## Advisories (go to cards, never hold the land)

- **A-1 ReceiptPresent can be permanent and costly.** `HasStoredReceiptAsync` accepts a prompt on the *parent*, but the ordinary path for a profiled carried completion does not. The `profiled-receipt-on-parent-only` shape, once aged, therefore returns `ReceiptPresent` on every pass: it never expires, never confirms, and runs uncached transcript scans every 5 s. Align the two rules, or let the ordinary verdict decide.
- **A-2 Producer-to-recipient gap.** The carried completion is pinned from a seeded carried row (`MarkSentAsync(destination: successor)`, with a Stopped successor started before the parent). No test drives `AgentControlService` AcceptAsync carry → queue typing on a Running successor → reconcile.
- **A-3 Held settle bookkeeping.** `SettleHeldSupersededAsync` does not rotate `ConcurrencyToken`, although `ExpireCoreAsync` does.
- **A-4 Report's own follow-ups.** `TerminalRunnerSeatReleaseService.HasPendingDeliveryAsync` and dispatcher DeliveryFailure recovery still treat `LegacyUnverified` as open. The adopted-session withhold backlog is also unfiled.

## Plan to apply (judged)

The plan is correct but incomplete as written. After D-1 is fixed and re-reviewed, the steps are:
1. Land on master.
2. Port to the current `rc/YYYY-MM-DD` line. Product hosts track rc, not master, and a master land is not activation.
3. Restart the production server (server2 after CARD-1744: `scripts/restart-linux-server.ps1`).
4. Confirm `GET /api/version` reports the SHA.
5. Only then run `{"apply":false}` with the operator token and record the counts.

Once the fix is activated at the default of 7 days, the hosted reconcile settles the same cohort within minutes on its own. The sweep apply is then mostly a count and confirmation step. The 663 younger b2f1a869 notes continue to drain by rule.
