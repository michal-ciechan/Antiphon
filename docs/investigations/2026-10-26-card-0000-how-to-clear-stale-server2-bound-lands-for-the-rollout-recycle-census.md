# How to clear stale server2-bound lands for the rollout recycle census

Confirmed: server2-bound pending land timestamps block recycling regardless of task completion or pushed source; the existing recovery routes cannot abandon all three unwanted requests while preserving their owners' historical states and branch refs.

Investigation task `7251a849-a737-4df8-8ae0-7422d02ae6bf`, 2026-10-26 UTC. This task has no card binding (`summary.cardId=null`); `card-0000` in this filename denotes an unbound investigation, not a filed card. Source inspected: `7b7aea79b26716654923d79600f0e05cc6c60c29`. Live `/api/version` returned `bedea1c03cd788d83b2ff54c40bfb603927bd69c`, capabilities `land-v2, operator-shutdown-v1`. The seven implementation/script files compared below are identical between that running SHA and the inspected SHA: `AgentTaskLandService.cs`, `LandApproval.cs`, `AgentTaskLandSourceResolver.cs`, `AgentTaskService.cs`, `AgentTaskReplyService.cs`, `deploy-server2.ps1`, `delegate.ps1` (`git diff <running-sha> HEAD --stat -- <paths>` produced no differences).

**Observed rows.** Authenticated GETs at 17:31:06–17:31:07Z returned:

| Owner task | Current role/status | Card | Current request/state | Source SHA |
|---|---|---|---|---|
| `f488ce0b-34f4-4de0-b97b-b0266812dd14` | Debug / Blocked | null | `bea18e0e-d1aa-4e98-918f-f223252d2610` / NeedsResolution | `375fa98579bc104cfcdc99182577c9bbbd6e73e2` |
| `c634b0a5-24cd-4e24-a26e-d44bbadef13f` | Debug / Blocked | CARD-1589, `998befc1-259b-434a-9684-1297c5d4efb3` | `e4c2cd75-29ac-4b23-80c8-813496aedcb1` / NeedsResolution | `6783c9427ebd1c84ab4739ba48f9c867fcd031cd` |
| `f4fd378a-75cf-4e60-b57a-db7bdf0ee62e` | Code / Succeeded | CARD-1349, `8c597e79-2dd6-410c-b795-0b53288551d1` | `209ecdb7-fb57-4d1f-9720-103f42a5b0d9` / NeedsResolution | `ff3ae85d1ab467f0ca557a3a925d9ac7ee86f4ba` |
| `3e5b2540-e6aa-4d99-aa18-60dce284f0a7` | Merge / Succeeded | null | `b8858374-b0ba-407c-9757-c8e0ee43b1ef` / Held | `b6db2de3dd0c0bad2ba30823d01882ca6fc9388e` |

All four have `runnerId=server2`, project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, and non-null `landRequestedAt`. The three NeedsResolution operations are `phase=Refused`, `publication=Unconfirmed`, `reason=rebase_conflict`, with null `terminalEventId` on their requests. This contradicts the brief's assumption that every stale owner is currently Succeeded: the first two became Blocked. Their stored Conflicted events identify the CARD-1574 plan and `ResumeReceiptReadinessTests.cs`. The current f4fd378a Conflicted event names `tests/Antiphon.Tests/slow-tests-allowlist.txt`; the brief's ten-conflict count was not independently reproduced.

The pending-list GET with `projectId=<Antiphon>&unscoped=include&includeChecks=true&landPending=true` returned exactly these four rows. Its stored envelope is retained in ignored `.antiphon/investigate-7251a849/pending.json`. `git ls-remote origin` independently confirmed all four branch tips in the table, plus `feat/card-task-c92632b2` at the same `ff3ae85d…` tip as f4fd378a. Pushing a branch does not clear these request rows.

**Why the census refuses.**

- [AgentTaskService.cs:2713](../../server/Application/Services/AgentTaskService.cs#L2713) implements `landPending=true` as `LandRequestedAt != null || LandStartedAt != null`, not as a task-status or request-state filter. No worktree existence, retirement or remote-publication test participates.
- [deploy-server2.ps1:699](../../scripts/deploy-server2.ps1#L699) builds two complete closures: open tasks (`Queued,Dispatched,Working,Blocked`) and pending lands, without a time window. Lines 733–734 issue the separate filters; excluded projects are traversed from the envelope at line 762. A terminal owner remains in the land closure.
- [deploy-server2.ps1:797](../../scripts/deploy-server2.ps1#L797) binds a land by the **owner task summary's `runnerId` equal to the runner being recycled**, using PowerShell `-eq`. It does not ask which machine executes Git landing. Either pending timestamp then raises `RecycleLandInFlight <owner-task-id>` at line 800. The GUID in that error is the owner task, not the request ID.
- For an open task's detailed request, [deploy-server2.ps1:820](../../scripts/deploy-server2.ps1#L820) explicitly counts `Queued`, `Held`, `Running`, `NeedsResolution`; terminal states are `Completed`, `Superseded`, `Canceled` and require a GUID `terminalEventId` (line 824). Off-runner pending lands are recorded but do not refuse. A timestamp alone can refuse before detail is read.
- [deploy-server2.ps1:891](../../scripts/deploy-server2.ps1#L891) retries only `RecycleLandInFlight`. With `-LandWaitMinutes 0`, it immediately emits `<id> runner=server2 waited=0polls` at line 917. Waiting cannot age out a NeedsResolution row.
- [AgentTaskLandService.cs:757](../../server/Application/Services/AgentTaskLandService.cs#L757) sets an ordinary conflicting owner's status to Blocked and the request to NeedsResolution (line 770), leaving pending timestamps intact. A reviewed recovery conflict preserves historical owner status. [AgentTaskLandService.cs:1286](../../server/Application/Services/AgentTaskLandService.cs#L1286) skips NeedsResolution during automatic requeue, so periodic sweeps cannot resolve the conflict merely through elapsed time.

Thus a Succeeded owner with a pushed branch and an already-removed worktree would still count if its pending flags remained. That worktree condition is a counterfactual here, not an assertion that these desktop worktrees were removed. The normal retirement service itself refuses `recovery_debt`: [TaskWorktreeRetirementService.cs:264](../../server/Application/Services/TaskWorktreeRetirementService.cs#L264), with a pending request detected at line 545.

**Reconstruction and stored transcript evidence.**

An ignored local harness, `.antiphon/investigate-7251a849/reconstruct.ps1`, parsed the actual deployment script and loaded only its census/wait/validation functions through PowerShell's AST. It supplied each stored owner summary through a local fake HTTP reader, isolated the land closure with an empty open closure, and set the wait to zero. It loaded no deployment entrypoint, Docker command, real HTTP transport or database writer. The unedited command output was:

```text
PASS stored f488ce0b RecycleLandInFlight f488ce0b-34f4-4de0-b97b-b0266812dd14 runner=server2 waited=0polls
PASS stored c634b0a5 RecycleLandInFlight c634b0a5-24cd-4e24-a26e-d44bbadef13f runner=server2 waited=0polls
PASS stored f4fd378a RecycleLandInFlight f4fd378a-75cf-4e60-b57a-db7bdf0ee62e runner=server2 waited=0polls
PASS stored 3e5b2540 RecycleLandInFlight 3e5b2540-e6aa-4d99-aa18-60dce284f0a7 runner=server2 waited=0polls
PASS counterfactual Succeeded+pushed+retired RecycleLandInFlight 3e5b2540-e6aa-4d99-aa18-60dce284f0a7 runner=server2 waited=0polls
RECONSTRUCTION passed=5 failed=0 source=7b7aea79b26716654923d79600f0e05cc6c60c29
```

This is five local reconstruction cases, not five application tests or a fresh rollout dry run. The fifth only adds retirement/push metadata and retains the pending timestamps.

The server-normalized transcript `GET /api/sessions/62bfc870-1461-4353-b5a5-6e49afd2e8a9/transcript?since=0` returned 11 entries. Sequence **6**, `AssistantText`, timestamp **2026-10-26T17:28:55.388Z**, records prior Debug f5f12deb's real `check-census` exit **0**, four pending lands/two Blocked tasks, and `redeploy-old -DryRun -LandWaitMinutes 0` exit **2** with the exact f488ce0b refusal reconstructed above. Sequence **4**, 17:25:37.970Z, states that task cancellation rejects the stale Succeeded owner and that the Shared Debug task itself held the port land. The transcript stores that task's reported result; it does not expose a fresh cancellation HTTP receipt. Windows final-observation JSON/logs named in the brief are not accessible from this mirror and were not read directly.

**Existing sanctioned transitions and their limits.**

1. **Resolve and resume an ordinary conflict.** [AgentTaskLandService.cs:105](../../server/Application/Services/AgentTaskLandService.cs#L105) admits a Blocked ordinary conflict owner; lines 155–175 preserve request identity, requeue NeedsResolution, and return Blocked to Succeeded. After genuinely resolving its branch, `delegate.ps1 -Land <owner> -ExpectedSourceSha <compatible-request-sha>` resumes the request. Changed expected SHA/evidence/filter can be refused as `land_request_identity_conflict`. A successfully completed Merge child can supersede its ordinary conflicted parent's request, set the terminal merged event and clear timestamps: [AgentTaskReplyService.cs:1473](../../server/Application/Services/AgentTaskReplyService.cs#L1473), [AgentTaskReplyService.cs:2563](../../server/Application/Services/AgentTaskReplyService.cs#L2563). This means real resolution/publication, not a dismissal command. Recovery conflicts are explicitly excluded.
2. **Reviewed owner recovery or adoption.** [docs/orchestration-loop.md:188](../orchestration-loop.md#L188) documents `-ReviewEvidenceId` with `-RecoverReviewedSource`, or with `-FromTask <source>`. [AgentTaskLandService.cs:100](../../server/Application/Services/AgentTaskLandService.cs#L100) requires a **Code owner** with Succeeded/Blocked/Failed status. A replacement source must be settled Code/Worktree, same non-null card and project/repository, with no `RepairSourceTaskId`, SourceLanding snapshot or pending source land (lines 202–238). [LandApproval.cs:69](../../server/Application/Services/LandApproval.cs#L69) checks Clean Review, `reviewedSourceClean=true`, exact subject/ref/repository/SHA, no superseding evidence; line 124 requires **Final / Full** for recovery. Follow-up evidence binds `FollowUpOfTaskId`; `-StartRef` and the Review's own ID do not establish the reviewed source identity ([docs/orchestration-loop.md:196](../orchestration-loop.md#L196); [StageOutcomeService.cs:199](../../server/Application/Services/StageOutcomeService.cs#L199)).
   Supersession applies only to a pending NeedsResolution request (line 148). A live Merge-helper session blocks it (line 279). The old request gets `Superseded`, `IsPending=false`, and a terminal event, but the owner immediately gets **another pending request** and fresh `LandRequestedAt` ([AgentTaskLandService.cs:293](../../server/Application/Services/AgentTaskLandService.cs#L293)). Therefore supersession alone does not release the census. The replacement must reach a terminal land result that clears timestamps, or it will still count.
3. **Cancel an unfinished owner, then allow reconciliation.** The existing route is `POST /api/agent-tasks/{full-guid}/cancel`: [AgentTaskEndpoints.cs:204](../../server/Api/Endpoints/AgentTaskEndpoints.cs#L204). [AgentTaskService.cs:2942](../../server/Application/Services/AgentTaskService.cs#L2942) rejects only settled statuses; `IsSettled` is Succeeded/Failed/Canceled, **not Blocked** (line 4539). Cancellation stops the delegate and changes the task status, retaining historical events. It does not synchronously clear the land. [AgentTaskLandService.cs:1249](../../server/Application/Services/AgentTaskLandService.cs#L1249) subsequently marks an ineligible pending request Canceled, `IsPending=false`, and clears pending timestamps through line 1268 and `ClearPending` at line 1551. This sweep path does not create a request terminal event; a fresh census excludes the now-terminal owner from open work and excludes its cleared timestamps from the land closure. Do not assert that every Canceled request has a terminal event or reuse an old census snapshot. Existing test source [AgentTaskLandRequestTests.cs:304](../../tests/Antiphon.Tests/Application/AgentTaskLandRequestTests.cs#L304) checks cancellation reconciliation; it was read, not executed here. Card-close/sweep policies use task cancellation, not a separate land-abandon authority.
4. **No additional land-only route/verb/procedure found.** The complete task route map in `server/Api/Endpoints/AgentTaskEndpoints.cs` exposes land/land-v2, retry, cancel, reply/continue/refine, review-evidence recovery, cleanup and worktree retirement; no resolve/abandon/dismiss land endpoint exists. `scripts/delegate.ps1`'s parameter sets expose Land/adoption/recovery, not land abandonment; `scripts/card.ps1`'s verbs concern card operations. [AbandonedLandingReconciler.cs:96](../../server/Application/Services/AbandonedLandingReconciler.cs#L96) explicitly skips any owner with a pending request, so its name does not imply this capability. Re-running a task through `/retry` can eventually make its old land ineligible, but commissions a new attempt, clears completion/session coordinates, and queues execution ([AgentTaskService.cs:2980](../../server/Application/Services/AgentTaskService.cs#L2980), lines 3861–3886); it is not a documented history-preserving land-dismissal procedure. Cleanup retries require confirmed publication, which these three operations lack.

**Concrete implications for these three stale lands.**

There is no complete existing command sequence that only abandons all three, keeps owner statuses and refs intact, and avoids landing the unwanted CARD-1349 patch. A Review cannot confer recovery eligibility on a Debug owner. `3e5b2540` cannot be their reviewed adoption source: it is Merge, has no card binding, and already has a pending land. `f488ce0b` is also unbound, making the required non-null same-card adoption impossible independently of its Debug role. CARD-1589 and CARD-1349 are different cards. **One reviewed replacement cannot supersede all three.**

For the first two, the cheapest existing disposal is task cancellation if changing their current Blocked statuses to Canceled is acceptable. These are instructions for the caller, **not commands executed in this investigation**. Run from an authorized operator/orchestrator shell; authenticate as documented in `docs/ops-http.md:54` without printing credentials:

```powershell
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$h = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $h['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
# First GET each row again and require the same owner/status/request/tip.
Invoke-RestMethod -Headers $h -Uri "$api/api/agent-tasks/f488ce0b"
Invoke-RestMethod -Headers $h -Uri "$api/api/agent-tasks/c634b0a5"
Invoke-RestMethod -Method Post -Headers $h -Uri "$api/api/agent-tasks/f488ce0b-34f4-4de0-b97b-b0266812dd14/cancel"
Invoke-RestMethod -Method Post -Headers $h -Uri "$api/api/agent-tasks/c634b0a5-24cd-4e24-a26e-d44bbadef13f/cancel"
# Poll GET detail until request.state=Canceled and both summary land timestamps are null.
# Then obtain a fresh complete pending-land census; cancellation is asynchronous for the land.
```

The current port-land holder is **a different task now**: `bb750e07-23a1-4183-a027-4b5a117ca3ea`, Blocked Shared Merge, parent=f488ce0b. The other stale helper is `447f7d1f-a889-46f4-a9fc-91ba622cffcc`, Blocked Shared Merge, parent=c634b0a5. These rows came from the scoped open-task GET. That envelope excluded four `slides` project rows; no fleet-wide absence conclusion is drawn. Canceling an owner does not recursively cancel its children. The caller must settle/cancel unwanted helpers separately through the same endpoint after inspecting their current ownership; otherwise they can continue holding `3e5b2540`. bb750e07's stored result says it performed no mutations because its Merge brief conflicted with the Git contract. Do not launch another Shared observer to wait for landing.

For f4fd378a, cancellation is rejected because it is Succeeded. Its **current request is already AdoptReviewedSource**, source=`c92632b2-eae8-4ab1-a0f2-f1bb8b22470e`, review evidence=`97e9cdf1-82a7-48f9-8cfe-1d92a05f42fc`, and both owner/source tips are `ff3ae85d…`. That adoption did not resolve the remaining rebase conflict. Reusing it simply republishes the unwanted patch candidate or conflicts again. No reviewed discard replacement is presently established by this investigation.

If the caller chooses genuine reviewed replacement/publication instead of abandonment, the documented final command shape is:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land f4fd378a-75cf-4e60-b57a-db7bdf0ee62e -FromTask <settled-same-CARD-1349-Code-source-guid> -ExpectedSourceSha <its-current-pushed-full-sha> -ReviewEvidenceId <Clean-Final-Full-source-review-outcome-guid>
# Or, after genuine owner-branch recovery and review of that owner:
pwsh -NoProfile -File scripts/delegate.ps1 -Land f4fd378a-75cf-4e60-b57a-db7bdf0ee62e -ExpectedSourceSha <reviewed-current-owner-full-sha> -ReviewEvidenceId <Clean-Final-Full-owner-review-outcome-guid> -RecoverReviewedSource
```

Those placeholders are required future evidence, not runnable existing replacements. The new request still blocks until completion. A recovery conflict requires a new explicit reviewed request; plain `-Land` is refused at [AgentTaskLandService.cs:166](../../server/Application/Services/AgentTaskLandService.cs#L166).

An adopted replacement **may omit old owner patches**: [AgentTaskLandSourceResolver.cs:403](../../server/Application/Services/AgentTaskLandSourceResolver.cs#L403) records `git cherry`'s uncontained commits without refusing on a false containment result. Existing test source [StartRefRepairAdoptionTests.cs:57](../../tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs#L57) explicitly expects confirmed publication with `RecoveryPatchesContained=false`. This is conditional, not carte blanche to adopt master: the source's recorded worktree base must equal/descend from one of the accepted owner/local/superseded-request tips (resolver lines 368–399). An arbitrary current-master carrier may fail `adopt_source_lineage`. Review, ref identity, live worktree identity, lineage and repository checks still apply. Adoption also moves the owner ref under exact force-with-lease, so it does not meet a requirement to leave that ref unchanged.

**Remaining uncertainties and boundaries.** No land/task/database mutation, cancellation, retirement, deployment or new card filing was performed. No build or application suite was run. Desktop registrations and files were not inspected; normal recovery could refuse an absent/changed owner registration. The exact post-cancellation cleanup/seat outcome needs observation if the caller executes it. There is no new sanctioned empty/discard replacement or corresponding Review outcome to name. The port-land hold is time-varying; its holder must be re-read immediately before action. Activation/deployment after the census clears remains outside this investigation.

**Not done, noted:** A small authenticated abandon operation in `AgentTaskEndpoints.cs`/`AgentTaskLandService.cs`, surfaced by `delegate.ps1`, could close an exact pending NeedsResolution request with a terminal audit event and cleared pending timestamps while preserving task history and branch refs; design and implementation are deferred to Plan.

--- next stage ---
next: plan
handoff: Plan a supported land-only abandonment operation for exact NeedsResolution requests that preserves owner task history and branch refs; account for Debug owners, helper ownership and census terminal proof. Existing task cancellation can clear the two Blocked Debug owners after reconciliation; it cannot cancel the Succeeded CARD-1349 owner.
artifact: docs/investigations/2026-10-26-card-0000-how-to-clear-stale-server2-bound-lands-for-the-rollout-recycle-census.md
