# CARD-1353: lands proceed throughout a server2 rolling rollout

Date: 2026-10-19. Plan task: `cdfb7e42-e1fb-4ada-a80e-8aeb273f7275` (Frontier, Worktree).
Assigned branch: `feat/card-task-cdfb7e42`. Start ref: `da80c493c9688c516d0195ec507903041a57232d`.
Fetched master: `a0f3c4f3e728655933da1782981a88fb2b7b3a56` (merge-base with the start ref is `81cfb267a`).
Input: [investigation](../../investigations/2026-10-19-card-1353-land-during-rollout.md).
Line numbers below are for the start ref; function names are the durable anchors.

## Outcome and scope

A land is desktop-server git work on the task's desktop worktree; it never runs on, binds to, or
reads a session runner. The rollout broke on 2026-10-17 because the host recycle helper re-ran the
task census after it had stopped and removed the standing container, and that census refuses
`RecycleLandInFlight` for any row in the project's `landPending` closure with no runner condition.
This plan narrows the land rule in both censuses to rows whose `runnerId` is the runner being
recycled, removes off-runner lands from the compared census snapshot so a land requested during the
audits cannot end `Unstable`, turns the remaining bound-land refusal into a bounded wait at every
census call site (wrapper and host, including the two post-removal proofs), reports both counts
from `check-census`, and drops the "no lands during the rollout" practice from the docs.

Master-only. No rc follow-up unless the operator asks. A real rollout is in `drain-old` right now:
Code runs no `deploy-server2.ps1` phase and touches no server2 container; every checkpoint is an
offline fixture run.

Not in scope: the server-side `recycle_land_hold` 409 counter-proposal from
`2026-10-17-server2-recycle-recovery.md:285-289` (it would institutionalise the freeze this card
rejects), any change to `AgentTaskLandService`, the publication audit, or the drain phases.

## Ground truth

| Card assumption | What the code does at `da80c493c` | Consequence |
|---|---|---|
| A land needs the standing runner or its workspace volume | `delegate.ps1 -Land` POSTs `/land/v2`; `AgentTaskLandHostedService` drains the queue in the server process; `AgentTaskLandingProtocol.RunAsync` works on `task.WorktreePath`/`task.RepoPath` (desktop paths). No land file reads `RunnerId`. | The land rule protects nothing for off-runner rows. Narrow it by `runnerId` (D-1). |
| `drain-old` refuses lands | `deploy-server2.ps1:1098-1117` posts the drain and waits on `sessions`, `runnerSessions`, `queuedTasks`. No census, no land check. | Nothing to change in `drain-old`; the docs must stop telling operators to freeze lands before it. |
| The wrapper census is what refused on 2026-10-17 | The wrapper census (`:1141`) passed at 12:23. The host `c1008_status_proof` (`c590-remote.sh:6105`) ran again at `:7831` after `docker stop`/`docker rm` and refused `RecycleLandInFlight 4ce63b7a` (`runnerId=server2-temp`) at `:5964`, about 15:19; the land finished 15:23. | The post-removal proofs must wait, not refuse, and only for bound lands (D-1, D-3). |
| Only the refusal sites encode the rule | Four refusal sites (`deploy-server2.ps1:728`, `:749`; `c590-remote.sh:5964`, `:5996`) plus the compared snapshot: the two-pass compare (`:656-661` wrapper, `:6018-6063` host) and `c1008_tasks_match_record` (`:6098-6101`) treat any land key appearing after the record as `Unstable`. `c994_status_proof` (`:8223-8227`) byte-compares the whole snapshot with its record. | Had `RecycleLandInFlight` not fired first, the same land would have ended `RecycleTaskCensusUnknown cause=Unstable` at the post-removal proof. The compared snapshot must drop off-runner lands (D-2). |
| A land's `--force-with-lease` republish can make the mirror's tip unpublished and trip the audit | Schema 3 never moves the source branch: `AgentTaskLandingProtocol.cs:16-18` and `ExpectedBranch` (`:568-571`) pin the source at `SourceLocalSha`; the only push in the protocol is the target push (`:300`, `PushOwnedAsync`). `PushSourceOwnedAsync` (`LandingGit.cs:1004-1017`) has one caller, `AgentTaskLandSourceResolver.cs:530`, reached only when origin's task branch is not at the reviewed SHA (`ownerObserved.Sha != expected`, `:515`); it moves the branch back to the reviewed SHA. | Safe without a guard (section "Publication audit"). |
| Runner id on a row is a proxy for "mirror on the recycled volume" | A task's mirror lives on the runner it was dispatched to; `AgentTaskDispatcher.cs:1072-1090` moves `RunnerId` and removes the old mirror only for a Queued task on drain; retirement (`TaskWorktreeRetirementService.cs:377-389`) removes it at card done. A Succeeded task keeps the `runnerId` it ran on. | `runnerId == <recycled runner>` is the right predicate; null or desktop rows have no mirror on any runner volume. |
| CARD-1317/1319/1324/1326 cover this | All four are ancestors of the start ref and leave the land rule untouched; CARD-1326 pins that a land-timestamp or `landPending` appearance still refuses (`RollingVolumeRecycleScriptTests.cs:1259-1303`, `docs/docker-stack.md:248-257`). | Those vectors are rewritten by runner (S1); the pinned sentence is replaced (S4). |
| Only C1087/C1008/C1326 pin the rule | `C994TaskVectors.cs` also pins it: `requested`, `started`, `land-pending-succeeded` and `land-Queued/Held/Running/NeedsResolution` use `runner="other"` and expect refusal, and `RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete` runs that matrix through the host `c1008_tasks`. | The matrix flips for off-runner rows and gains bound variants (S1). |
| Pending lands are rare during a rollout | `redeploy-old` holds the lock through two audits of up to 3 h each (about 86 min on 2026-10-17); the orchestrator lands continuously under CARD-1311. | A land request between the post-lock proof and the post-removal proof is the normal case, not a race. |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Both censuses refuse `RecycleLandInFlight` only for a row whose `runnerId` string-equals the recycled runner (`$RunnerId` / `$C1008_RUNNER`), exactly the `RecycleBoundTasks` predicate. Off-runner rows with a land timestamp or a live `landRequest` state are read, counted and recorded, never refused. | Drop the land rule entirely (section 1 of the investigation says every land is off-volume). Rejected as the hedge: a bound Succeeded task's mirror can still sit on the volume being removed, and the publication audit between the proofs compares a moving origin; a short hold costs minutes. |
| D-2 | The compared census snapshot keeps, for the `land` closure, only bound rows: `land = { tasks: <rows with runnerId == runner> }` with no `scopes` entry. The closure is still walked in full, `excluded.byProject` counts still reconcile per pass, the receipt still records every read and row, and `check-census` still counts every pending land. The `open` closure snapshot is unchanged. | Keep the full land closure in the compare and add a forgiveness rule for off-runner appearances. Rejected: the forgiveness rules work on the open closure's shape, the `land.scopes` id strings and nested `excluded` counts would still differ, and `c1008_tasks_match_record` and the `c994_status_proof` byte compare would each need their own carve-out. Filtering at snapshot build time fixes all three with one predicate. |
| D-3 | A bound land makes the census **wait**, bounded, at every call site instead of refusing: wrapper `Wait-RecycleTaskCensus` (new) around the three `Assert-RecycleTaskCensus` call sites (`:890`, `:1141`, `:1197`), host `c1008_tasks_wait` (new) around `c1008_tasks` in `c1008_status_proof` (`:6171`) and `c994_status_proof` (`:8223`). Only `RecycleLandInFlight` is retried; every other refusal is immediate. On expiry the refusal keeps its first token: `RecycleLandInFlight <id> runner=<runner> waited=<n>polls`. | Move the post-removal proofs before removal. Rejected: they also carry the routing, counter and reference checks volume removal needs (`c1008_references`), and the wrapper already checks before any host work. Poll `Unstable` too: rejected, a changed row is a different hazard class and the two passes are seconds apart. |
| D-4 | Bound defaults: wrapper `-LandWaitMinutes` (`[ValidateRange(0, 1440)]`, default 30; 0 means refuse on the first read, today's behaviour) with the deadline from `C727_TEST_LAND_WAIT_MS` when set and the poll from `C727_TEST_POLL_MS` (default 30 s). Host `C1008_LAND_WAIT_SECONDS` (default 1800) and `C1008_LAND_POLL_SECONDS` (default 30), validated with the audit-cap digit pattern (`^[1-9][0-9]{0,6}$`, `c1008_resolve_audit_cap` at `:6186`); an invalid value refuses `RecycleContextInvalid` before any proof read. Polls = ceil(wait / poll); the loop counts polls and sleeps `poll` seconds between them, so a test can stub `sleep`. | Export the host knobs from the desktop through `c590-real.ps1` like `C1008_AUDIT_TIMEOUT_SECONDS` (`:494-496`). Deferred: it touches the ssh export list and its contract tests for a knob whose default already exceeds the longest observed land by 5x. File a card if an operator wants it. |
| D-5 | The detail-state refusal (`deploy-server2.ps1:749`, `c590-remote.sh:5996`) takes the same predicate and is kept for shape. It is unreachable for bound rows, which already refuse `RecycleBoundTasks` on the open list (`:731`, `:5968`); for off-runner rows an in-flight state (`Queued`, `Held`, `Running`, `NeedsResolution`) is recorded as `{state, terminalEventId: null}` and counted, and the terminal-proof check (`Completed`/`Superseded`/`Canceled` with a GUID `terminalEventId`) still refuses `RecycleLandUnknown` for anything else. | Delete the site. Rejected: `check-census` (`-Report`) relies on the same branch for its count, and keeping all four sites symmetric is the simplest story for Review. |
| D-6 | Forgiveness rules (`Test-RecycleForgivenDrift` `:611-638`; host pass-1 `:6018-6063`) are left textually as they are. Because `live.land.tasks` is now bound-only (D-2), the CARD-1326 "appears in the `landPending` closure" edge now applies only to bound rows; the "appearing open row carries a land timestamp" edge stays for every row. | Relax forgiveness for off-runner rows. Rejected as scope creep: an open row appearing with a land timestamp is a recovery land on an open owner (`LandApproval.RecoveryStatusEligible` admits Blocked), rare, and `Unstable` there is still correct. |
| D-7 | `check-census` prints `RECYCLE_CENSUS runner=<id> open=<n> boundOpen=<n> landPending=<n> boundLandPending=<n> scopes=<n> elapsedMs=<n>`; `landPending` keeps counting every pending land. | Reuse `landPending` for the bound count. Rejected: the operator loses the fleet-wide view the read-only phase exists for. |
| D-8 | Docs drop the freeze: `docs/docker-stack.md:22` (verify no land is pending), `:345` (no-new-land interval), and `docs/orchestration-loop.md:29-31` keep the land wait only for the AppHost pull, which does fast-forward the canonical checkout. The census paragraph (`:236-258`) states the runner-bound rule, the bound-only compare and the wait. | Leave the docs and rely on the orchestrator instructions (which carry no land sentence). Rejected: `docs/docker-stack.md:22` is the sentence operators follow. |
| D-9 | Publication audit: no guard. Section "Publication audit" shows the ordinary land never moves the source branch and the one case that does is a correct refusal. | Add a census rule that refuses a land whose request started after the first audit. Rejected: it would reintroduce the freeze for a hazard the audit already reports correctly. |
| D-10 | CARD-1008 plan rows PC-9, PC-49 and PC-51 (`2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md:468,823,991`) get an in-place "Superseded 2026-10-19 by CARD-1353 D-1" note, the pattern `Card_0590_D6_is_annotated_superseded` pins for CARD-0590. | Leave them; a reader would re-derive the old rule from them. |
| D-11 | Red-first witnesses are mandated runs outside the checkpoint table (rule 2 of the manifest: unlisted with a stated reason), one build of the test assembly at the tests-only commit into `bin-c1353-red/` and the five row filters against it. Each must fail on the named assertion; zero tests or a build error is not red. | Model the red runs as table rows: a row's `Expect` is 0 failed, so a red row is a failed row, not a witness. |

## Publication audit versus a land (the investigation's open uncertainty)

The investigation called `LandingGit.cs:1015` a "source republish `--force-with-lease`". The code
says otherwise:

- `AgentTaskLandingProtocol` (schema 3, `:14-18`): "the source is the task branch ref plus the
  reviewed SHA; the rebase and the verification build run in the one detached land worktree; the
  push comes first". `ExpectedBranch` (`:568-571`): "schema 3 never moves it". The only push in the
  protocol is `PushOwnedAsync(op.RepositoryPath, Destination(op), op.VerifiedSourceSha!, ...)` at
  `:300`, to the **target**. Cleanup deletes the desktop's local source ref only
  (`GuardedWorktreeRemoval.cs:286-306`, `update-ref -d`).
- `PushSourceOwnedAsync` (`LandingGit.cs:1004-1017`) is called once, from
  `AgentTaskLandSourceResolver.cs:530`, inside `if (ownerObserved.Sha != expected)` (`:515`), where
  `expected` is `request.ExpectedSourceSha`, the reviewed SHA (`:53`). It pushes the reviewed SHA to
  the source branch with a lease on the observed remote SHA, then confirms it (`:542-546`). It runs
  only when origin's task branch is **not** at the reviewed SHA.

Consequences for the audit (`c1008_audit_checked` `:7442`; rule "a local tip that no origin head
contains refuses `RecycleUnpublishedWork`"):

1. Ordinary land: the runner mirror's branch tip is the SHA the delegate pushed, which is the SHA
   Review reviewed and the land's `ExpectedSourceSha`. Origin's task branch stays at that SHA
   before, during and after the land; master gains rebased copies. The mirror tip stays reachable
   from an origin head. Both audits of a recycle see the same local facts ("a moving origin leaves
   the before/after comparison equal", `docs/docker-stack.md:336`). Safe.
2. Adoption land (`ownerObserved.Sha != expected`): origin's task branch had moved past the reviewed
   SHA and the land rewinds it. Commits past the reviewed SHA become unreachable on origin; a mirror
   holding them is genuinely unpublished work and the audit refuses `RecycleUnpublishedWork`. That
   is the audit doing its job, it is independent of when the land runs, and the recovery path is the
   documented one. No guard is added (D-9); the docs name it as the one land shape that can refuse.
3. Branch deletion on origin (`GitService.DeleteBranchAsync` from `WorkflowEngine.cs:851`, card
   done) is the CARD-1105 replay 3b open decision and is not changed here.

## Design

### Census predicate and snapshot (D-1, D-2, D-5)

Wrapper, `Assert-RecycleTaskCensus`:

```powershell
$bound = ([string]$task.runnerId -ceq $RunnerId)
if ($null -ne $task.landRequestedAt -or $null -ne $task.landStartedAt) {
    [void]$pendingLands.Add($id)
    if ($bound) { [void]$boundLands.Add($id); if (-not $Report) { throw "RecycleLandInFlight $id" } }
}
# ... detail path, open rows only:
if ($null -ne $detail.landRequest) {
    $state = [string]$detail.landRequest.state
    if ($state -cin @('Queued','Held','Running','NeedsResolution')) {
        [void]$pendingLands.Add($id)
        if ($bound) { [void]$boundLands.Add($id); if (-not $Report) { throw "RecycleLandInFlight $id" } }
    } elseif ($state -cnotin @('Completed','Superseded','Canceled') -or
        [string]$detail.landRequest.terminalEventId -cnotmatch '^[0-9a-f]{8}-...$') { throw 'RecycleLandUnknown' }
}
# snapshot: land closure keeps bound rows only
if ($kind -eq 'land') {
    foreach ($key in @($rows.Keys | Sort-Object)) { $row = $rows[$key] | ConvertFrom-Json
        if ([string]$row.runnerId -ceq $RunnerId) { $taskSnapshot[$key] = $row } }
    $closures[$kind] = [ordered]@{ tasks=$taskSnapshot }
}
```

`$rows` stays complete for the per-scope `excluded.byProject` reconciliation; `$script:recycleReceiptTasks`
stays complete; `-Report` returns `landPending=$pendingLands.Count` and `boundLandPending=$boundLands.Count`.

Host, `c1008_tasks_collect`: the two `RecycleLandInFlight` sites gain
`and .runnerId==$runner` (`--arg runner "$C1008_RUNNER"`); the proof validity jq adds the in-flight
states as valid (`terminalEventId` may be null for them); the `land` kind snapshot becomes
`{tasks: ($rows | with_entries(select(.value.runnerId==$runner)))}`. `C1008_CENSUS_TASKS` (receipt)
is unchanged. `c1008_tasks_match_record` needs no edit: its `land.tasks` rules now see bound rows only.
`c994_status_proof`'s byte compare likewise.

### Bounded wait (D-3, D-4)

Wrapper:

```powershell
function Wait-RecycleTaskCensus {
    param([string]$RunnerId, [string]$ProjectId)
    $deadline = [datetime]::UtcNow.AddMinutes($LandWaitMinutes); $pollMs = 30000; $polls = 0
    if ($env:C727_TEST_LAND_WAIT_MS) { $deadline = [datetime]::UtcNow.AddMilliseconds([int]$env:C727_TEST_LAND_WAIT_MS) }
    if ($env:C727_TEST_POLL_MS) { $pollMs = [int]$env:C727_TEST_POLL_MS }
    do {
        try {
            $snapshot = Assert-RecycleTaskCensus -RunnerId $RunnerId -ProjectId $ProjectId
            if ($null -ne $script:recycleReceipt) {
                $script:recycleReceipt['landWait'] = [ordered]@{ polls=$polls; outcome=$(if ($polls) {'cleared'} else {'none'}) }
                Write-RecycleReceipt }
            return $snapshot
        } catch {
            if ($_.Exception.Message -cnotmatch '^RecycleLandInFlight ([0-9a-f-]{36})$') { throw }
            $landId = $Matches[1]
            if ([datetime]::UtcNow -ge $deadline) {
                if ($null -ne $script:recycleReceipt) {
                    $script:recycleReceipt['landWait'] = [ordered]@{ polls=$polls; outcome='expired' }; Write-RecycleReceipt }
                throw "RecycleLandInFlight $landId runner=$RunnerId waited=${polls}polls"
            }
            # Console, not the pipeline: every call site voids the function's output.
            [Console]::Out.WriteLine("RECYCLE_LAND_WAIT runner=$RunnerId id=$landId polls=$polls")
            Start-Sleep -Milliseconds $pollMs; $polls++
        }
    } while ($true)
}
```

The three call sites (`Invoke-TempContainerCleanup` `:890`, `redeploy-old` `:1141`, `retire-temp`
`:1197`) call `Wait-RecycleTaskCensus`. `check-census` keeps calling `Assert-RecycleTaskCensus -Report`.
`Assert-RecycleTaskCensus` already rewrites the same `census-<phase>-<runner>.json` on each call
(`Start-RecycleReceipt` at `:1043` runs once per phase), so the final receipt is the one with the
`landWait` field. A refusing census consumes one `landPending` read: the throw happens on the row in
pass 0. A clean census reads it twice.

Host:

```bash
c1008_tasks_wait() {
    local code=0 polls=0 max wait="${C1008_LAND_WAIT_SECONDS:-1800}" poll="${C1008_LAND_POLL_SECONDS:-30}"
    [[ "$wait" =~ ^[1-9][0-9]{0,6}$ ]] && [[ "$poll" =~ ^[1-9][0-9]{0,6}$ ]] || { C1008_TASK_ERROR=RecycleContextInvalid; return 2; }
    max=$(( (wait + poll - 1) / poll ))
    while :; do
        C1008_LAND_WAIT_POLLS="$polls"      # c1008_tasks writes it into the census receipt
        code=0; c1008_tasks >/dev/null || code=$?
        [ "$code" = 3 ] || break
        if [ "$polls" -ge "$max" ]; then C1008_TASK_ERROR="$C1008_TASK_ERROR runner=$C1008_RUNNER waited=${polls}polls"; break; fi
        printf 'C1008_LAND_WAIT runner=%s reason=%s polls=%s\n' "$C1008_RUNNER" "$C1008_TASK_ERROR" "$polls"
        sleep "$poll"; polls=$((polls + 1))
    done
    return "$code"
}
```

`c1008_status_proof` (`:6171`) and `c994_status_proof` (`:8223`) replace `c1008_tasks >/dev/null || code=$?`
with `c1008_tasks_wait || code=$?`. `c1008_tasks` adds `landWaitPolls:(${C1008_LAND_WAIT_POLLS:-0})` to
the receipt it writes. A refusal after the wait goes through the existing `c1008_refuse "$C1008_TASK_ERROR"`,
so the journal `diagnosis` carries the suffixed string and the first token is unchanged.

### Docs (D-8, D-10)

`docs/docker-stack.md`: line 22 becomes "Before starting, verify the checkout has no half-reset
worktree and ... Lands keep running throughout the rollout (CARD-1353); the only land a phase waits
for is one bound to the runner being recycled." Line 33 gains `boundLandPending=<n>`. Lines 236-258
are rewritten: the `landPending=true` closure refuses `RecycleLandInFlight <id>` only for a row bound
to the target runner, after a bounded wait (`-LandWaitMinutes`, default 30; host
`C1008_LAND_WAIT_SECONDS`, default 1800, `C1008_LAND_POLL_SECONDS`, default 30; wait lines
`RECYCLE_LAND_WAIT` and `C1008_LAND_WAIT`); off-runner pending lands are counted and recorded but are
not part of the compared snapshot, so a land requested for a task on the other runner or the desktop
during the audits cannot end `cause=Unstable`; the CARD-1326 sentence "Mutating phases and the host
census refuse `RecycleLandInFlight` on the row itself" becomes "... on a row bound to the recycled
runner". Line 313: "no land in flight **for a task bound to that runner**". Line 345: the no-new-land
sentence is deleted. One new sentence in the audit paragraph names the adoption-land shape
(Publication audit, item 2).

`docs/orchestration-loop.md:29-31`: the land wait applies to the AppHost pull and restart ("Wait for
any land in progress before pulling; never pull while a land runs" stays). Add: "The server2 rolling
phases need no land freeze: lands are desktop work and the census waits only for a land bound to the
runner being recycled (CARD-1353)."

## Slices

Commit order: S1 (every test change, red) first, then S2, S3, S4 in any order. The red witness build
(D-11) runs at the S1 commit; the checkpoint table runs after all four.

### S1: tests, committed red

Files: `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`,
`tests/Antiphon.Tests/Scripts/C994TaskVectors.cs`,
`tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`.

Host census tests:

- `C1326HostCensusScript`: `row_of` takes a runner argument. Probes: `appears-with-land-timestamp`
  (desktop) now expects `unstable` (the appearing open row carries a land timestamp, not forgiven,
  D-6; it is no longer `RecycleLandInFlight` because the row is off-runner). `appears-in-land-closure`
  splits into `appears-in-land-closure-bound` (runner `server2`, expect `land-in-flight`, error
  exactly `RecycleLandInFlight <id>`) and `appears-in-land-closure-other` (runner `desktop`, expect
  `pass`; the snapshot's `land.tasks` does not contain the id). The probe gains `unstable` and `pass`
  branches like `C1319HostCensusScript`.
- New `C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands`: `LinuxShell` with
  `C1317ShellFunction` extracts of `c1008_tasks_collect`, `c1008_tasks`, `c1008_tasks_wait` and
  `c1008_tasks_match_record`; `sleep() { :; }`; a `c1008_http` stub serving per-read sequences for the
  `landPending` path (a refusing census consumes one land read, a clean census two). Probes, each
  printing `PASS host-<name>`:
  `off-runner-pending` (Succeeded, `runnerId=server2-temp`, `C1008_RUNNER=server2`, landRequestedAt
  set; `c1008_tasks` returns 0; snapshot `.land.tasks` is empty; receipt `tasks` has the row),
  `bound-pending-refuses` (`runnerId=server2`; return 3, `RecycleLandInFlight <id>`),
  `off-runner-in-flight-detail` (Blocked, `runnerId=other`, detail `landRequest.state=Running`,
  `terminalEventId=null`; return 0; `.open.land[id] == {state:"Running",terminalEventId:null}`),
  `match-record-ignores-off-runner-land` (`C1008_RECORD` with empty `land.tasks`, phase
  `containersRemoved`; live snapshot from a census where an off-runner land is present; returns 0; the
  same with a bound land returns 1),
  `wait-clears` (`C1008_LAND_WAIT_SECONDS=3`, `C1008_LAND_POLL_SECONDS=1`; land reads: pending, pending,
  empty, empty; return 0; receipt `landWaitPolls == 2`; two `C1008_LAND_WAIT` lines),
  `wait-expires` (same env, land read always pending; return 3; error
  `RecycleLandInFlight <id> runner=server2 waited=3polls`; exactly 3 `C1008_LAND_WAIT` lines),
  `wait-invalid-env` (`C1008_LAND_WAIT_SECONDS=0`; return 2, `RecycleContextInvalid`, zero HTTP reads).
- `C994TaskVectors`: `requested`, `started`, `land-pending-succeeded`, `land-Queued/Held/Running/
  NeedsResolution` become `accepted=true` (runner `other`); add `bound-requested`, `bound-started`
  (Succeeded, `server2-temp`, `accepted=false`) and `bound-land-pending-succeeded` (`accepted=false`).
  `land-Unknown` stays false. The matrix is executed by
  `RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete`, whose own Succeeded
  loop (`:125`, `runnerId=server2-temp`) is unchanged.

Wrapper census tests:

- `C1087_Census_filters_open_work_and_pending_lands`: vectors `pending-requested`/`pending-started`
  (row `runnerId=other`) now expect exit 0 with the `retire-temp-runner` case in the trace and
  `C1087Receipt(...)["tasks"]` containing the row; new vectors `bound-pending-requested`/
  `bound-pending-started` (`runnerId=server2-temp`) keep `RecycleLandInFlight <id>`, exit 2, no case.
  `closure`: the nested land row gets `runnerId=server2-temp` so the nested walk still refuses.
- `C1087_Check_census_phase_is_read_only`: add a third row (Succeeded, `landStartedAt` set,
  `runnerId=server2-temp`). Pins become
  `RECYCLE_CENSUS runner=server2 open=1 boundOpen=1 landPending=2 boundLandPending=0 scopes=1 elapsedMs=`
  and `RECYCLE_CENSUS runner=server2-temp open=1 boundOpen=0 landPending=2 boundLandPending=1 scopes=1 elapsedMs=`.
- `C1008_Busy_routed_and_land_in_flight_refuse`: the states loop (`:445-460`, `runnerId=other`) splits:
  `Unknown`, `omitted` still exit 2 with no case; `Queued`, `Held`, `Running`, `NeedsResolution`,
  `started` exit 0 with the case; add `bound-started` (`runnerId=server2-temp`, Succeeded,
  `landStartedAt`) exit 2 `RecycleLandInFlight`. The Succeeded row in the `:410-418` loop already has
  `runnerId=server2-temp` and stays a refusal.
- `C1326WrapperProbe`: `appears-with-land-timestamp` stays `unstable`; `appears-in-land-closure`
  splits into `-bound` (land row `runnerId=server2`, `check-census` exits 2 `Unstable`) and `-other`
  (`desktop`, exits 0, `Census check complete:` present, both `RECYCLE_CENSUS` lines present).
- New `C1353_Wrapper_waits_for_a_bound_land_then_proceeds`. `C1008WrapperFixture` gains
  `internal Dictionary<string, string> Environment { get; } = new();` merged into `Run`'s
  `psi.Environment`. Clears arm: `MainDrained()`, `allowClear=true`, phase `redeploy-old`,
  `Environment["C727_TEST_LAND_WAIT_MS"]="60000"`, `taskQueries[land path] = [pending, empty]` for a
  Succeeded row bound to `server2` (one refusing census, then a clean one; the stub keeps serving the
  last element). Asserts exit 0, `deploy-parent` case in the trace, one `RECYCLE_LAND_WAIT runner=server2`
  line, at least three land reads in the trace, receipt `landWait.outcome == "cleared"` and `polls == 1`.
  Expiry arm: `Environment["C727_TEST_LAND_WAIT_MS"]="1"`, land reads always pending: exit 2, output
  contains `RecycleLandInFlight <id> runner=server2 waited=`, no case, receipt `landWait.outcome == "expired"`.
  Zero arm: `-LandWaitMinutes 0` without the env: exit 2 after exactly one land read. The fixture's
  `Run` deadline is 30 s; one wait poll keeps the clears arm well inside it.

Doc pins: new `DockerStackDocumentationTests.Lands_proceed_during_rollout_is_documented`:
`docs/docker-stack.md` contains `boundLandPending`, `bound to the runner being recycled`,
`-LandWaitMinutes`, `C1008_LAND_WAIT_SECONDS`, `RECYCLE_LAND_WAIT`, and `cannot end` together with
`cause=Unstable` inside the census paragraph; does not contain `verify no land is pending`,
`Keep the no-new-land interval`, or `host census refuse `RecycleLandInFlight` on the row itself`.
`docs/orchestration-loop.md` contains `need no land freeze` and still contains
`never pull while a land runs`. The CARD-1008 plan contains `Superseded 2026-10-19 by CARD-1353 D-1`
three times.

### S2: host census (`scripts/c590-remote.sh`)

`c1008_tasks_collect` (predicate at `:5964` and `:5996`, proof validity for in-flight states, land
snapshot bound-only), new `c1008_tasks_wait`, `c1008_tasks` receipt field, call sites `:6171` and
`:8223`. No other function changes; `c1008_tasks_match_record` and the forgiveness blocks stay as
they are.

### S3: wrapper census (`scripts/deploy-server2.ps1`)

`param` adds `[ValidateRange(0, 1440)][int]$LandWaitMinutes = 30`; `Assert-RecycleTaskCensus`
(predicate at `:728` and `:749`, `$boundLands`, in-flight proof, land snapshot, `-Report` object gains
`boundLandPending`); new `Wait-RecycleTaskCensus`; call sites `:890`, `:1141`, `:1197`; `check-census`
output line (`:1221`).

### S4: docs

`docs/docker-stack.md`, `docs/orchestration-loop.md`,
`docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md` (PC-9,
PC-49, PC-51 notes), as in Design.

## Verification design

Linux lane (`-Platform Linux`, no `-Runner`): the host probes run bash and `C1008HostFixture`
requires native Linux. Placement read 2026-10-19: `GET /api/runner-defaults` revision 2,
`defaultRunnerId` null, no kind defaults; `GET /api/session-runners`: `desktop` windows accepting,
`server2` linux draining (not accepting), `server2-temp` linux accepting. No whole-lane or Unit run.
Each behaviour has one red-first witness (the rewritten or new method run against the unchanged
scripts at the S1 commit, red for the stated reason) and one narrow green filter (the same method in
the table after S2-S4).

| ID | Behaviour | Witness (method) | Red at S1 because |
|---|---|---|---|
| V-1 | Wrapper: an off-runner pending land passes; a bound one refuses | `C1087_Census_filters_open_work_and_pending_lands` (`pending-requested`, `pending-started`, `bound-*`) | base refuses `pending-requested` with `RecycleLandInFlight` |
| V-2 | Wrapper: `check-census` reports `boundLandPending` and keeps `landPending` | `C1087_Check_census_phase_is_read_only` | base output lacks `boundLandPending=` |
| V-3 | Wrapper: an off-runner in-flight detail state is recorded, not refused | `C1008_Busy_routed_and_land_in_flight_refuse` (states loop) | base exits 2 for `Running` on `runnerId=other` |
| V-4 | Wrapper: an off-runner land appearing in the land closure is not `Unstable`; a bound one is | `C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` (`appears-in-land-closure-other`) | base ends `cause=Unstable` |
| V-5 | Wrapper: a bound land is waited for, bounded, before host work; expiry keeps the first token | `C1353_Wrapper_waits_for_a_bound_land_then_proceeds` | base refuses at the first land read (one read, no `RECYCLE_LAND_WAIT`) |
| V-6 | Host: off-runner lands pass `c1008_tasks`, bound lands refuse, in-flight off-runner proof shape | `C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands` (`off-runner-pending`, `bound-pending-refuses`, `off-runner-in-flight-detail`) | base returns 3 for `off-runner-pending` |
| V-7 | Host: `c1008_tasks_match_record` ignores an off-runner land absent from the record | same method (`match-record-ignores-off-runner-land`) | base returns 1 (live land key missing from saved) |
| V-8 | Host: `c1008_tasks_wait` clears, expires with `waited=`, refuses invalid env | same method (`wait-clears`, `wait-expires`, `wait-invalid-env`) | `c1008_tasks_wait` is missing; `C1317ShellFunction` fails |
| V-9 | Host: C1326 edges by runner | `C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` (host probes) | `appears-in-land-closure-other` returns 3 on base |
| V-10 | Host matrix: off-runner land vectors accepted, bound variants refused | `RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete` | `requested` vector is refused on base while the matrix says accepted |
| V-11 | Docs drop the freeze and state the runner-bound rule | `DockerStackDocumentationTests.Lands_proceed_during_rollout_is_documented` | pins absent on base |
| R-1 | Bound open work still refuses `RecycleBoundTasks`; nested closures still walked | `C1087_Census_filters_open_work_and_pending_lands` (status loop, `closure`), `C1008_Busy_routed_and_land_in_flight_refuse` (`active-excluded`, `unscoped`, `withheld`) | regression guard |
| R-2 | Existing doc pins | `DockerStackDocumentationTests.Recycle_census_preflight_is_documented`, `Main_volume_recycling_is_scripted_only` | regression guard |

Red witness (D-11): at the S1 commit, one build `tests/Antiphon.Tests -> bin-c1353-red/` and the five
checkpoint filters below against it, through `scripts/build-slot.ps1` or `run-checkpoint.ps1` with a
separate results root. Report each as
`RED-WITNESS CP-n commit=<S1 sha> filter=<filter> failed=<n> assertion=<first failing assertion text>`
with the reason "CARD-1353 operator policy red-first witness". V-1 to V-11 must each appear red by
name; a filter that fails only to build or runs zero tests is not a witness. Delete `bin-c1353-red/`
with the other `bin-*` directories before finishing.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1353/` | wrapper-census | `/*/*/RollingVolumeRecycleScriptTests/(C1087_Census_filters_open_work_and_pending_lands*)\|(C1087_Check_census_phase_is_read_only*)` | V-1, V-2, R-1 | both methods, 0 failed/skipped | 2 | 9 |
| CP-2 | all | CP-1 | wrapper-gates | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1353_Wrapper_waits_for_a_bound_land_then_proceeds*)` | V-3, V-5, R-1 | both methods, 0 failed/skipped | 2 | 5 |
| CP-3 | all | CP-1 | census-drift | `/*/*/RollingVolumeRecycleScriptTests/(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance*)\|(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)` | V-4, V-6, V-7, V-8, V-9 | both methods, 0 failed/skipped; output contains every `PASS host-<name>` named in S1 | 2 | 3 |
| CP-4 | all | CP-1 | host-matrix | `/*/*/RetiredTempContainerHostTests/C994_Host_task_and_land_census_is_complete` | V-10 | exact method, 0 failed/skipped | 1 | 3 |
| CP-5 | all | CP-1 | doc-pins | `/*/*/DockerStackDocumentationTests/(Lands_proceed_during_rollout_is_documented*)\|(Recycle_census_preflight_is_documented*)\|(Main_volume_recycling_is_scripted_only*)` | V-11, R-2 | three methods, 0 failed/skipped | 3 | 1 |

Every row shares `After: all`, so one build serves the table. Check in each fresh TRX that the
method-segment OR selected exactly the named methods (CARD-0403: a bare `|` between full paths
silently widens or empties the run).

### Cost

Ordinary Code floor: 9 + 5 + 3 + 3 + 1 = 21 minutes of checkpoints, plus the red witness build and
runs (about 10 minutes) and authoring (about 45 minutes). `-ExpectAbout` 75 minutes, inside the 60 to
90 minute budget. Brief line:
`checkpoints: docs/superpowers/plans/2026-10-19-card-1353-lands-during-rollout-plan.md@<plan commit sha> section "### Checkpoints"`.

## Risks and residuals

- **Land requested after the post-lock proof for a bound task.** The census cannot see a land that
  does not exist yet; such a land still finishes in minutes and the post-removal proof now waits for
  it (D-3). If it outlasts the bound, the refusal is the 2026-10-17 shape with volumes preserved; the
  operator resumes with `-ResumeRecycle`. Same residual as today, narrower.
- **Recovery land on an open off-runner owner during the census.** Its `open.land` proof can change
  between pass 0 and pass 1 or between the post-lock and post-removal proofs and end `Unstable` (D-6).
  Rare (recovery mode plus a Blocked owner plus exact timing); documented, not fixed here.
- **Journal written by the previous script version.** A resume compares `.tasks` from the saved
  journal; the land snapshot shape changed (no `scopes`). Cross-version resume is already refused by
  `sourceSha` equality in the journal (`RecycleResumeMismatch`), so no migration is needed. Finish the
  in-progress rollout before activating this change on the desktop.
- **Shared `tests/Antiphon.Tests` surface.** Three test files change; Code must not widen beyond the
  named methods. No `LandVerifyFilter` or production verifier change.
- **No session waits for input.** The waits added here are script polls inside a phase, not sessions;
  the CARD-1083 parking checklist does not apply.
- **Server2 is mid-rollout.** Code dispatch lands on `server2-temp` or the desktop; the plan needs
  Linux for the bash probes, so `-Platform Linux`.
