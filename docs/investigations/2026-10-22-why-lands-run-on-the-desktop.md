# CARD-1462: why lands run on the desktop, not server2 (2026-10-22)

Investigate task ca843435 (runner mirror, branch `feat/card-task-ca843435`, base `3fa8d7bd4`).
Read-only. Code references are to this branch's tip (master lineage). Database figures come from
read-only queries against the desktop dev Postgres (`antiphon`, port 17280, `BEGIN READ ONLY`,
2026-10-22 ~09:00-09:40 UTC). Prior evidence: [server2 remote-prep latency](2026-10-21-server2-remote-prep-latency.md)
(CARD-1427..1431), [desktop repository maintenance](2026-10-21-card-1432-desktop-repo-maintenance.md),
[stale landings and sync debt](2026-10-22-card-1446-stale-landings-and-sync-debt.md), and card CARD-1461.
Nothing was restarted, written, or reconfigured. No land, process or repository was touched.

Operator premise applied throughout (2026-10-22): AppHost and server2 always run the rc tip, never
master; a master land is not an activation; the AppHost restart is a separate step.

## Verdict

A land runs on the desktop because it is a method of the one Antiphon **server** process, and that
process exists only on the desktop (server2 runs a session runner, a build-slot broker and a seed
container, nothing else: `docker-compose.server2-runner.yml:9,35,166`). Every land step then
binds to that host's filesystem: the project's one repository path (`C:\src\Antiphon`), two OS
file locks under its `.git/antiphon/`, a pid-based child journal, a land worktree under
`C:\Antiphon\worktrees\land\`, a `dotnet build` leased from the **local** session runner, and the
cleanup of the task's desktop worktree. None of those is Windows-specific; all of them are
**server-local**. The desktop is where the server is, so the desktop is where lands are.

What still ties a land to the desktop under the premise is not activation. It is two things:
(1) the desktop worktree is canonical for every Worktree task, server2 tasks included, so a land
must clean it up and dispatch must cut it (`RemoteWorkspaceService.cs:20-27`); and (2) dispatch
cuts every new worktree from the desktop's **local** `master` without fetching
(`WorktreeBaseResolver.cs:21`, `WorktreeManager.cs:130,758-761`), so somebody on the desktop must
keep local `master` current. Today the land does that with one `update-ref` (since 2026-10-16 the
main checkout is no longer on `master`, so the land never touches its working tree: 171 of 171
master lands since then advanced local `master` by ref only, `TargetCheckoutPath` null).

The measured cost is real and mostly not git: a land occupies the single global land lane for a
median 5.5 min (rebase 3 s, build 3-4 min, push 40 s, cleanup 60 s), the lane is serial, and a
burst of 37 requests on 2026-10-20 07:00Z queued for a median 2.3 h and a maximum 3.7 h. The
desktop build-slot memory floor made two 2026-10-22 verifications take 22-24 min instead of 4,
and lands hold the repository mutation lease for 165-543 s each, which starved server2 cuts
(41 tasks on 2026-10-20, median 5 min, worst 3.6 h behind land-held leases).

## 1. Where a land executes today

`delegate.ps1 -Land` POSTs `/api/agent-tasks/{id}/land/v2` at `ANTIPHON_API`
(`scripts/delegate.ps1:379,845`). The server's land queue is one global single reader
(`docs/orchestration-loop.md:1680-1681`). The request is run by `AgentTaskLandService.RunAsync`:

| Step | Code | Host resource | Under which lock |
|---|---|---|---|
| Repository | `task.RepoPath` = the project's `LocalRepositoryPath` (`AgentTaskService.cs:1459`, `Project.cs:16`); every landing in the last 10 days has `RepositoryPath = C:\src\Antiphon`, target `refs/heads/master`, remote `origin` (DB, 2026-10-14..22) | desktop checkout | |
| Yield to dispatch, then two leases | `AgentTaskLandService.cs:475-485`: `YieldToDispatchAsync` (max `LandYieldToDispatchMaxSeconds` 90, `DelegationSettings.cs:727`), then `land-execution.lock` (L) and `landing.lock` (R) | `C:\src\Antiphon\.git\antiphon\{land-execution,landing}.lock`, opened `FileShare.None` (`LandExecutionLease.cs:11-20`, `RepositoryMutationLease.cs:47-56`) | L + R |
| Admission fence | `RepositoryChildJournal.FirstFencingAsync` reads `<common>/antiphon/children/*.json`, each holding a pid and process start ticks of **this host** (`RepositoryChildJournal.cs:21-31`, `RepositoryMutationLease.cs:60-62`) | same host's process table | R |
| Source resolution | `AgentTaskLandSourceResolver.cs:115-154`: fetch the branch from origin into `refs/antiphon/land/<task>/<req>/source-observed`; a desktop branch that is Behind origin lands from the observed remote SHA, no worktree fast-forward (`:148-154`) | desktop repo, origin round trips | R |
| Operation | `LandOperationFactory.cs:19-66`: local branch read, destination, `ObserveAsync` (remote CAS baseline), `target_local_ahead` check of local `master` vs origin (`:46-67`) | desktop repo | R |
| Rebase | land worktree `C:\Antiphon\worktrees\land\<sha256-of-common-dir>` (`LandWorkspace.cs:28-34`, root `Git:WorktreeBasePath` `server/appsettings.json:12`); `git rebase <targetBefore>` (`AgentTaskLandingProtocol.cs:254-255`) | desktop filesystem | R |
| Verification decision | `LandVerificationPolicy.cs:12-16`: skip only for a docs-only diff onto `master`; a `-Verify` filter means tests; any other target means build | | R |
| Verification build | R is released, L and a verification-window marker stay (`AgentTaskLandService.cs:677-693`); `dotnet build --artifacts-path <tmp> --configuration Debug -maxcpucount:N`, tests only with a filter (`AgentTaskLandService.cs:1846-1854`); the slot comes from the **local** runner (`RoutingSessionRunnerClient.cs:38-40`: "the build budget belongs to the host this server builds on, i.e. the local runner"; `SessionRunnerHttpClient.cs:424-428` `POST build-slots`), waits up to `BuildSlotWaitMinutes` 30 (`LandingSettings.cs:17`) | desktop runner `:17204/build-slots`, desktop CPU/RAM (MaxConcurrent 2, floor 6144 MB, `BuildSlotSettings.cs:22`, `docs/testing-and-build.md` Build slots table) | L + window |
| Window semantics | `RepositoryVerificationWindows.cs:48-86`: while the marker's pid is alive only a compatible preflight (an ordinary dispatch cut of an unrelated branch) may take R; settlement syncs, other lands and untagged writers are refused | | |
| Publication | re-observe origin (CAS), `git push origin <verified>:refs/heads/master` from the desktop repository (`AgentTaskLandingProtocol.cs:333-350`, `LandingGit.cs:1058-1065`) | desktop repo, origin | R (reacquired) |
| Canonical advance | `AgentTaskLandingProtocol.cs:478-513` (`AdvanceCanonicalAsync` at `:457`): if the main checkout's HEAD is on the target, `merge --ff-only` in it, refused when dirty/locked/sequencer (`:524-533`); otherwise `update-ref --no-deref refs/heads/master <verified> <current>` (`:504-505`). Any failure is a residue reason, never a refusal (`:457-476`) | desktop local `master` | R |
| Cleanup | `AgentTaskLandingProtocol.cs:406-454` → `GuardedWorktreeRemoval`: remove the task's desktop worktree `C:\Antiphon\worktrees\card-task-<id>`, its registration and branch | desktop filesystem | R |
| Ref retention | `AgentTaskLandService.cs:528-547` deletes the task's `refs/antiphon/{land,progress}` while R is still held (CARD-1432 S2) | desktop repo | R |

Evidence that the canonical advance is now ref-only: landings grouped by day, target, checkout and
outcome (DB, `Phase in (9,10)`):

| Days | Lands | `TargetCheckoutPath` | Outcome |
|---|---|---|---|
| 2026-10-14..16 | 97 | `C:\src\Antiphon` | `canonical_checkout_dirty` on every one: the main checkout was on `master` and dirty, local `master` never advanced |
| 2026-10-16..22 | 171 | null | advanced (or already) by `update-ref`: the main checkout's HEAD is not `refs/heads/master` (it tracks the rc line, `docs/release-gates.md:34,368-369`) |

### 1.1 How a server2 delegate's branch reaches the desktop land

1. Dispatch (desktop server tick) takes R, cuts the desktop worktree from local `master`
   (`WorktreeManager.cs:130`; "Dispatch does not fetch", `:758-761`), captures the progress
   baseline, releases R (2026-10-21 investigation §1).
2. `RemoteWorkspaceService.PushBranchAsync` pushes the branch to origin from the desktop worktree
   (`:102-128`); `MirrorAsync` asks the runner for a mirror (`:179`, capability
   `workspaceRepositoryV1`). The runner's repository is a blobless anonymous HTTPS clone at
   `/work/repos/antiphon`; only pushes use the SSH deploy key (`docs/docker-stack.md:991`), and the
   push credential policy is repository-level, not ref-level (`PushCredentialPolicyFile.cs:8-17`).
3. The session commits and pushes its own branch (`git push origin HEAD:refs/heads/<branch>`).
4. Settlement fast-forwards the desktop worktree to the pushed tip under R
   (`RemoteWorkspaceService.cs:650-706`); a busy R becomes `runner_sync_lease_busy` sync debt
   (CARD-1082), replayed later. 116 debts are `Ready` (paid), 1 is `Pending` after 2,144 attempts
   (DB; CARD-1446 C2).
5. The land reads the desktop branch, observes origin, and lands from origin's SHA when the
   desktop copy is Behind (step "Source resolution" above). So the land never depends on the
   settlement sync having run; it depends on the desktop **repository** (objects, refs, locks).

### 1.2 Hard requirement versus accident of history

| Binding | Hard today? | Why |
|---|---|---|
| The server process lives on the desktop | Hard | server2 has no Antiphon server (`docker-compose.server2-runner.yml`); the land is `AgentTaskLandService`, in-process, one global reader. Moving it means a runner capability or a second server. |
| One repository path per project (`C:\src\Antiphon`) | Hard | `Project.LocalRepositoryPath` → `AgentTask.RepoPath`; `LandOperationFactory` and every `LandingGit` call take that path. |
| `landing.lock`, `land-execution.lock`, verification marker, child journal | Hard, host-local | OS file locks (`FileShare.None`) and pid+start-ticks liveness; they fence only writers on the same host. A second host writing the same repository would not be fenced at all. |
| Desktop worktree is canonical for every Worktree task | Hard, by design (CARD-0604 D-15) | `RemoteWorkspaceService.cs:20-27`; land cleanup, residue, retirement and progress all read it. A server2 land would still have to remove it (or leave `LandedWithResidue`). |
| Dispatch cuts from local `master`, no fetch | Hard today | `WorktreeBaseResolver.cs:21`, `WorktreeManager.cs:130,758`; this is the only live consumer of the land's canonical advance besides the next land's `target_local_ahead` check. |
| Build slot from the local runner | Hard by one line | `RoutingSessionRunnerClient.cs:38-40`; the gate interface (`IBuildSlotGate`) is runner-agnostic. |
| Windows path semantics | Not required | The only `IsWindows()` branches in the land path are case-insensitive path comparers (`AgentTaskLandingProtocol.cs:933`, `LandingGit.cs:1130`, `RepositoryMutationLease.cs:13-15`, `LandWorkspace.cs:31`). No csproj is Windows-only; the landing protocol tests run on Linux (server2 checkpoint rows). |
| "Pin master, pull before restart" (`AGENTS.md:55`, `docs/bootstrap.md:623`, CARD-0358) | Historical | Written 2026-09-04 when AppHost built `master` from the main checkout. Since 2026-10-16 the main checkout is on the rc line and lands advance `master` by `update-ref`; the pull rule now matters only when moving to a new rc tip. The land's own residue text still says "Fix it there with `git pull --rebase`, then restart with scripts/restart-apphost.ps1" (`AgentTaskLandService.cs:852-855`). |
| "Land confirms publication, not activation" (`docs/orchestration-loop.md:360-384`) | Still true, now trivially | Under the premise a master land never activates; only an rc-tip restart does. |
| Verification on Windows | Not exercised | In the last 10 days 0 of 338 landings carried a `VerificationFilter`; verification is `dotnet build` (Debug, whole solution) or `Skipped(non-code)` (DB). The Windows-specific coverage is the RC qualification (`docs/release-gates.md:100-115`), not the land. |
| rc-line publication | Already off the land service | An rc land is a `Merge`-role Worktree task that pushes `HEAD:refs/heads/rc/...` itself (`docs/release-gates.md:42-57`). No landing row in the last 10 days targets an rc ref. |

## 2. What a server2 land would require (under the premise)

- **Who owns `master`.** Origin already does: publication is a CAS push against the observed
  remote tip (`remote_changed_before_push`, `AgentTaskLandingProtocol.cs:339,357`). The
  desktop's local `master` is a cache. If lands ran elsewhere, the desktop would need
  `git fetch origin master:master` (safe while the main checkout is not on `master`) before each
  dispatch cut, or dispatch would have to cut from `origin/master` after a fetch. Nothing else
  reads local `master`.
- **Advancing the desktop checkout.** Not needed for master under the premise. For an rc-tip
  restart the operator (or the restart script) still has to fetch/pull the rc branch; that is
  unchanged by where lands run.
- **Lease semantics across hosts.** `landing.lock` cannot be shared. A server2 land would take the
  runner's own mirror lock for its rebase/push and would still need R on the desktop twice:
  to advance local `master` (or the fetch above) and to remove the desktop worktree. The land
  queue's "one global reader" would also have to span hosts or be partitioned per host.
- **Verification on Linux.** The server2 runner already builds this solution for checkpoint rows
  (`UseAppHost=false` off Windows) with MaxConcurrent 4, MaxCpuCount 6, floor 16,384 MB
  (`docs/testing-and-build.md` Build slots table). A land-verify there would compete with
  delegates' checkpoints for those four slots. Nothing in the land's current verification is
  Windows-only (0 test-bearing lands in 10 days); Windows coverage stays with the RC qualification.
- **Post-land activation check.** Unchanged: compare `GET /api/version` with the rc tip when a
  restart is due (`docs/orchestration-loop.md:375-379`). A master land changes nothing running.
- **Cleanup and receipts.** The protocol's durable rows (`AgentTaskLandings`, cleanup journal,
  `refs/antiphon/land/*` pins, verification artifacts under the desktop temp) are all written by
  the server against the desktop repository. A server2 lane needs equivalents on the runner or a
  split protocol (prepare/verify/push remotely, record/cleanup locally).

## 3. Measured cost of landing on the desktop

### 3.1 Per land (DB, schema-3 landings, 2026-10-12..22)

| Day | Lands | Skipped (docs) | Rebase s | Verify avg s | Verify max s | Push s | Canonical s | Cleanup s | Op total s |
|---|---|---|---|---|---|---|---|---|---|
| 10-12 | 23 | 4 | 2 | 174 | 207 | 30 | n/a (refused dirty) | 46 | 257 |
| 10-14 | 51 | 5 | 3 | 175 | 246 | 33 | n/a | 47 | 264 |
| 10-16 | 12 | 1 | 5 | 201 | 318 | 47 | 1 | 72 | 366 |
| 10-20 | 71 | 0 | 3 | 169 | 304 | 44 | 1 | 63 | 334 |
| 10-21 | 54 | 14 | 3 | 242 | 1,225 | 42 | 1 | 77 | 349 |
| 10-22 (to 09:30Z) | 26 | 10 | 3 | 386 | 1,441 | 12 | 1 | 36 | 312 |

Git processes per land: 232-250 (CARD-1432 §1, `Land git profile` log line,
`AgentTaskLandService.cs:509-521`), about 70 of them per-boundary rechecks and 45-50 cleanup.

### 3.2 The serial lane (DB, land requests, terminal event joined)

| Day | Requests | Median queue s | Max queue s | Median exec s | Lane minutes used | Over 15 min end to end |
|---|---|---|---|---|---|---|
| 10-13 | 37 | 1 | 88 | 320 | 425 | 2 |
| 10-14 | 57 | 1 | 1,196 | 317 | 270 | 1 |
| 10-18 | 9 | 10 | 1,247 | 336 | 48 | 3 |
| 10-20 | 73 | 2,323 | 13,219 | 334 | 568 | 44 |
| 10-21 | 54 | 2 | 2,115 | 349 | 331 | 8 |
| 10-22 | 26 | 1 | 997 | 237 | 139 | 3 |

The 2026-10-20 burst by hour: 06:00Z 6 requests (median queue 4,990 s), 07:00Z 37 requests
(median 8,167 s, max 13,219 s, 266 lane minutes), then single digits per hour with queue
medians of 1-57 s. The lane is serial by construction (one reader, one L per repository), so a
burst of N requests costs about N × 5.5 min of wall clock for the last one.

### 3.3 What lands do to everyone else on the desktop repository

- Dispatch cuts held behind a land-held R (`Held: repository mutation lease is occupied by task
  <id> purpose=land`): 2026-10-20 41 tasks, median span 305 s, max 13,130 s; 2026-10-21 53 tasks,
  median 167 s, max 1,420 s; 2026-10-22 12 tasks, max 399 s (DB, events without a land request).
  Seven back-to-back lands on 2026-10-21 21:04-21:33Z each held R 165-543 s and made six server2
  tasks wait 14-16 min for their cut (2026-10-21 investigation §2). CARD-1427/1428 (landed
  `be14b8b0`) give waiting cuts a turn; CARD-1429/1430 (cleanup and baseline outside R) are Backlog.
- Settlement syncs refused by a busy R become debt (`runner_sync_lease_busy`); all 116 such debts
  were later paid; the Pending one is an inspection failure, not lease contention (CARD-1446).
- The desktop repository itself: 23k loose refs, 72k loose objects, 118 packs, `for-each-ref`
  10.4 s until the 2026-10-21/22 cleanup (CARD-1432); every one of the ~230 processes per land
  paid for it, and the server2 mirror took 29 ms for the same command.

### 3.4 Build-slot starvation (CARD-1461), reconstructed from the landing rows

| Land | Verify started | Verified | Verify wall | Next land queued | Next land started | Queue wait |
|---|---|---|---|---|---|---|
| 3b2c2936 (CARD-1427/1428) | 05:10:42Z | 05:34:43Z | 24 min 1 s | fa175d9b 05:19:07Z | 05:35:43Z | 16 min 36 s |
| bde3f3ad | 06:35:22Z | 06:57:55Z | 22 min 33 s | 0f389926 06:54:08Z | 06:58:54Z | 4 min 46 s |
| bd490132 (normal, same morning) | 03:56:23Z | 04:00:14Z | 3 min 51 s | | | |

During that wait the land holds L (so no other land can start, `land_execution_busy`,
`AgentTaskLandService.cs:478-483`) and the verification marker refuses R to every acquirer
except compatible dispatch preflights (`RepositoryVerificationWindows.cs:74-86`). So a waiting
land-verify does **not** hold R, but it does block other lands and settlement syncs. The floor
refusal itself is the desktop runner's `build_slot_memory_floor` (`BuildSlotBroker.cs:102-106`,
floor 6,144 MB); the server records the `BUILD SLOT waiting` lines only in its log
(`LandingVerifier.cs:52-55`), which is not reachable from this mirror (uncertainty 1).

## 4. Options and trade-offs

| Option | What changes | Removes | Keeps | Rule changes |
|---|---|---|---|---|
| **A. Keep lands on the desktop, cut their cost** (CARD-1429/1430 Backlog, CARD-1432 R4/R5 G1-G8, CARD-1431) | Cleanup and push outside R; baseline outside R; ~235 → ~120-140 git processes per land | Roughly half the R hold per land (push 40 s + cleanup 60 s of 165-543 s), most of the cut starvation | The serial lane, the desktop build and its memory floor, every host-local lock | None |
| **B. Verify on server2, land on the desktop** | A runner capability that builds a given SHA (the prepared commit must be reachable: push the `prepared` pin to a temporary origin ref, or hand the runner the rebased patch); `IBuildSlotGate` selects the runner; the verification window stays on the desktop | The 3-4 min desktop build and CARD-1461 starvation from the lane; desktop CPU/RAM pressure during the Windows qualification | Serial lane, R/L on the desktop, cleanup on the desktop; adds one origin round trip (~15 s) and a cross-host wait | None beyond documenting where the build ran in the receipt |
| **C. Server2 land lane** (rebase, verify, CAS push on server2; desktop does `fetch master:master` and worktree cleanup afterwards) | New runner land capability or a second server; protocol split; durable rows written from runner facts; desktop local `master` advanced by fetch under R | Nearly all land work from the desktop lane and the desktop build | Two short desktop R holds per land (fetch, cleanup); the desktop worktree as canonical (unless CARD-0604 D-15 is reversed); a cross-host queue | `docs/orchestration-loop.md:32-33` "lands are desktop work" and the CARD-1353 census rule; `AGENTS.md:55` and `docs/bootstrap.md:623` pull rule; the residue text in `AgentTaskLandService.cs:852-855`; Post-land activation check text (keep, drop the master wording) |
| **D. Merge-style delegate lands against origin** (push first, desktop fetches) | No server code: a Worktree task started at the master tip rebases/merges and pushes, as rc lands already do (`docs/release-gates.md:42-57`) | Everything from the desktop except the fetch | Loses the protocol: approval recheck, CAS receipts, verification evidence, cleanup, `AgentTaskLandings` rows, the attention/outcome events; every master land becomes an unrecorded push | Same as C, plus the "no out-of-band push" rules would have to be inverted |
| **E. Parallel desktop lanes** | More than one reader per repository | Little: the rebase chain serialises on `master` anyway and R serialises the git | | None |

Under the premise, options A and B change no operating rule. Option C is the only one that
moves the lane off the desktop, and it still needs the desktop twice per land until the desktop
worktree stops being canonical for server2 tasks.

## 5. Recommendation and slice list

Recommendation: **A then B**, re-measure, and defer C.

1. **S1 (A, Code)** CARD-1429: push and cleanup outside R, keeping the ordering guarantees
   (cleanup already runs after publication is confirmed, `AgentTaskLandingProtocol.cs:406-414`).
   Expected: R hold per land drops by ~100 s (push + cleanup columns above).
2. **S2 (A, Code)** CARD-1430: release R after `worktree add`, capture the baseline outside it.
3. **S3 (A, Code)** CARD-1432 R4 then R5: per-subcommand histogram, then G1-G6 (one `for-each-ref`
   per recheck, `update-ref --stdin` pins, in-process ref-name validation, one `remote get-url`
   per leased land, fetch-only observation, merged config/ls-files reads).
4. **S4 (B, Plan/Code)** runner-hosted land verification: push the prepared SHA to a temporary
   origin ref under the existing `refs/antiphon/land/...` prefix, add a runner capability that
   fetches it and runs the same `dotnet build` (and filtered tests) through the runner's broker,
   return the receipt; `SessionRunnerBuildSlotGate` and `LandingVerifier` select the runner by
   a `Landing:VerificationRunner` setting (operator-owned). Keep the desktop path as fallback
   when the runner is ineligible. Record `verifiedOn=<runnerId>` in the landing receipt.
5. **S5 (docs)** Rewrite the rules the premise makes stale, independent of A/B: `AGENTS.md:55`
   and `docs/bootstrap.md:623` (local `master` is advanced by the land's `update-ref`; `git pull`
   is for moving the main checkout to a new rc tip); `docs/orchestration-loop.md:32-33`
   (lands are server work, not desktop work by nature); the `CanonicalWarning` text
   (`AgentTaskLandService.cs:852-855`) to stop prescribing a restart after a master land.
6. **S6 (measure)** After S1-S4, re-run the 3.1-3.4 queries over a week; decide C only if the
   lane is still the bottleneck. C's precondition is a decision on the desktop worktree's
   canonical status for server2 tasks (CARD-0604 D-15).

## 6. What the operator must decide

1. **Local `master` custody.** Keep the land's `update-ref` of the desktop's local `master` (needed
   for dispatch cuts and the `target_local_ahead` admission check), or make dispatch fetch and cut
   from `origin/master`. The second choice is what makes a server2 land lane possible.
2. **Verification platform.** Whether a Linux build on server2 is acceptable as the land's
   verification, with Windows coverage left to the RC qualification. Today's lands verify with a
   Windows build and no tests.
3. **Desktop worktree canonical for server2 tasks.** If it stays canonical, every land keeps a
   desktop cleanup step and lands cannot leave the desktop entirely. Reversing CARD-0604 D-15 is a
   separate design.
4. **Build-slot policy for land-verify** (CARD-1461, operator-owned values): a reserved
   verification lease, a lower floor for `land-verify` labels, or moving the build (option B).
5. **Credentials for a server2 publisher.** Whether the server2 deploy key may push
   `refs/heads/master` (GitHub branch protection and the key's scope are not visible from here).

## Uncertainties

1. The `BUILD SLOT waiting ... memory floor` lines for the 2026-10-22 lands are in the desktop
   server log only; the 22-24 min verification walls above are reconstructed from the landing
   rows, and the per-land git profile lines are from CARD-1432, not re-measured here.
2. Whether `C:\src\Antiphon`'s HEAD is on the rc branch or detached is inferred from
   `TargetCheckoutPath` being null on every master land since 2026-10-16
   (`ScanTarget`, `AgentTaskLandingProtocol.cs:541-552`); the checkout itself was not inspected.
3. GitHub-side branch protection on `master` and the deploy key's write scope were not probed.
4. The server2 runner's spare build-slot capacity for an extra land-verify stream was not
   measured; the table values are configuration.

## Not done, noted

- No fix designed or applied; no process, setting, repository or database changed.
- Fix candidates are the slices in section 5; CARD-1429/1430/1431/1432 R4-R5 already exist as cards.
