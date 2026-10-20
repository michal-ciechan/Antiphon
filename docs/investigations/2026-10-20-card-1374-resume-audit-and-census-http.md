# CARD-1374: Resume audit and census HTTP

Root cause confirmed by reconstruction from stored transcript evidence and the deployed scripts: a single failed host task-detail GET ends the census immediately, after two full audits have completed. A resume with the original work volume still present runs both audits again. The journal retains the earlier audit text, but does not make that text reusable proof of unchanged contents.

## Scope and provenance

- Investigated on 2026-10-20, task `90a85c4b-db91-4e77-9597-53d4b19ef26b`, branch `feat/card-task-90a85c4b`, base `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`.
- Read CARD-1374 on board Antiphon using `pwsh -NoProfile -File scripts/card.ps1 get CARD-1374 -Board Antiphon`. Card row: `7b4aa8ad-b4e4-4536-8bab-8a8218340653`; revision count 0; token `24fc6132-cee7-4d07-b9d9-d45e5bd8ea4d` at the read.
- Incident deployment SHA: `81cfb267a0af5c0ddb65e8b238af355067bee9cc`. `git diff <incident-sha> HEAD -- scripts/deploy-server2.ps1 scripts/c590-remote.sh` was empty. Both commits have wrapper blob `46f4581f035395b85a0f6ffa1b819335531f191a` and host-script blob `3d1791a33bee1e293429c1c4467cffe2c1a6ae97`. All source line references below use these identical files.
- Operation: `c1008c7791e0671184d028e8036d05ce5b568`; failed resume run: `c727b92e5fd75552`. Recorded host journal: `/home/mc/antiphon-server2/recycle/c1008c7791e0671184d028e8036d05ce5b568.json`; recorded host receipts: `/work/test-evidence/c727b92e5fd75552/deploy-parent/`; desktop copies: `C:\src\Antiphon\.antiphon\rolling-server2\c727b92e5fd75552`.
- Evidence retrieval used the server-normalized transcript, `GET /api/sessions/daec6a72-771d-4a66-9634-fbe474d87464/transcript?since=<sequence>`. Relevant rows are identified below. The selected five rows and a projected task-detail observation are retained locally in ignored `.antiphon/investigation-1374/evidence.json`, SHA-256 `6e9293e7eb8c0a6c106b3dceb982168b84bd3c7f51c9a464763806e6d73ce278`. Essential evidence is also transcribed here because ignored files do not travel with this commit.

This investigation ran no tests, builds, audit helpers or deployment phases. It did not access or alter the currently running resume, host journals, volumes or task state. The historical host observations below are from the stored delegate report, not fresh measurements of the replacement attempt.

## Stored observations

Transcript row **10295**, `2026-10-20T01:26:35.136Z`, is the delivered full report of Debug task `98c512c8`, with these measurements:

| Observation | Recorded result |
|---|---|
| Desktop invocation | Deploy PID 125696, verifier 408492, SSH 108196; started 2026-10-19 22:33:39–51 UTC |
| Initial censuses | Desktop receipt at 22:33:50: 39 successful reads; host receipt at 22:34:26: 38 successful reads; zero old-runner-bound tasks or pending lands |
| Second audit helper | `c98497e4d936525f8cc3b45af80b54e93a7646e14a6d372fc94982c58635b0fc`, started 2026-10-20 **00:24:18 UTC**, UID 1654, read-only `/work` and `/runner-tmp` mounts |
| Progress | 518 repository entries at 01:24:23, 527 at 01:25:30; saved audit had **883 repositories**; `dirty-519.actual` modified at 01:25:29.619 |
| Expensive operations | Parallel `git status --porcelain --untracked-files=all` and `git hash-object --no-filters --stdin-paths`; workers observed waiting in filesystem reads, with continued progress |
| Journal at that observation | `phase=containersRemoved`, `ownedRemoved=true`, no `auditFailure`, mtime 00:24:17.263; all three original target volumes present and `pending`; standing runner container absent |
| Audit ordering | First audit had passed and matched the saved audit before helper two started |

CARD-1374 records completion/refusal at approximately 02:15 UTC: about 221 minutes from 22:34, split into approximately 110 minutes per audit. These are rounded incident timings, not new per-stage profiling. The exact completion instant is not in the retrieved log excerpt.

Transcript row **10488**, `2026-10-20T02:16:09.137Z`, reads the tail of desktop `resume2.log`. Its result, row **10489**, `02:16:09.809Z`, contains:

```text
DIAGNOSIS=RecycleTaskCensusUnknown cause=Http status=502 path=/api/agent-tasks/783030cd-f4e7-44dd-8b98-230b1d506ff9 elapsedMs=3100
Rolling deploy stopped: HostCaseFailed deploy-parent exit=2
exit=2
```

Row **10490**, `02:16:14.042Z`, requests the same task route three times through desktop `http://localhost:17202`. Row **10491**, `02:16:14.994Z`, records:

```text
200 0.019541
200 0.018893
200 0.017644
Custom Blocked None Release-faster: v6 issues section
```

A fresh read at `2026-10-20T02:21:58.7373039Z` confirmed task `783030cd-f4e7-44dd-8b98-230b1d506ff9`: role `Custom`, status `Blocked`, `cardId=null`, project `abf9c104-0740-4b54-93a1-086cd9bde9ac` (slides), runner `desktop`, null `landRequestedAt`, `landStartedAt` and `landRequest`. This establishes why an unrelated desktop task was in the open-task closure; it does not retrospectively prove its fields at the exact failed GET.

The three successful reads used the desktop route, while the failing host read uses `C604_SERVER_ORIGIN`. They show rapid availability of the application route, not an end-to-end retry of the identical host network path. The component that generated the 502 remains unidentified.

## Why the resume audits twice

The wrapper admits an offline, explicitly resumed main to host journal validation (`scripts/deploy-server2.ps1:1126`), performs its census at `:1141`, and sends the resume context to `deploy-parent` at `:1142`–`:1144`. It does not itself run either Git audit. `case_deploy_parent` calls `c1008_recycle` at `scripts/c590-remote.sh:7869`–`:7870`.

The host control flow is:

| Step | Source evidence |
|---|---|
| Load and validate the saved source SHA, operation, project, context and Compose digest | `scripts/c590-remote.sh:7693`–`:7700` |
| Fresh status/task proofs, first before locks and then under locks | `:7702`, `:7714`–`:7715` |
| Validate original volume generations, preserved volumes, owned containers and references | `:7739`–`:7787` |
| **Audit one**, followed by comparison with saved `.audit` on resume | `:7791`–`:7793` |
| Stop/remove owned containers; write `stopped`, then `containersRemoved` | `:7803`–`:7828` |
| **Audit two**, requiring equality with audit one | `:7829`–`:7830` |
| Fresh status/task proof, then no-reference proof | `:7831`–`:7832` |
| Set `removing`; repeat status/task and reference proofs before each target volume removal | `:7834`, `:7838`–`:7845` |

There is no `containersRemoved` branch that skips either audit. On an already absent-container resume, the two container loops have no owned IDs to process, but phase assignments and both audit calls still run. The special earlier return at `:7724`–`:7737` applies only to `recreating`/`verified` with the required recreated-generation evidence.

`c1008_audit_checked` reuses `.audit` only when resuming and the work volume is **absent**, with saved outcome `removed`, or `pending` under the saved `removing` intent (`:7444`–`:7458`). With the work volume intact, it always invokes `c1008_audit` at `:7460`. Each invocation creates a new UID-1654 helper, mounts the work volume and existing runner-tmp volume read-only, waits for it, removes the helper and sorts its output (`:7391`–`:7439`). Each receives its own 10,800-second default budget (`:6182`–`:6189`, `:7272`–`:7288`).

**Invariant assessment:** the second audit is explicitly documented, not an accidental second wrapper invocation. `docs/docker-stack.md:328`–`:334` requires the initial inventory before stopping/removing the container and successful publication proof; `:336` explicitly describes both audits, before stop and after container removal. `:360`–`:362` says resume rechecks the publication audit and volume generations. `:312`–`:319` and `:345`–`:346` require fresh removal preconditions and a continuing no-new-land interval.

For a fresh recycle, the post-removal audit checks the interval during which the running container could still change protected work, including shutdown. In the observed **already-offline** resume, both scans inspect the same original volumes with no intervening runner stop or removal to perform. The second **content** pass adds no new information *if* protected bytes and repository metadata stayed unchanged throughout that interval. The existing flow does not establish continuous immutability, however, and each full audit also refreshes origin publication observations. Therefore unconditional removal of the second audit is not justified by this evidence. The existing runbook has no offline-resume exemption; a faster equivalent would need to satisfy its safety purpose explicitly.

## Every census HTTP read and its failure handling

The table separates task-census reads from runner-status reads that admit the census/removal. The Docker container census uses `docker ps -aq --no-trunc` and `docker inspect`, not application HTTP (`scripts/c590-remote.sh:5786` onward).

| Lane and read | Call sites | Transport / HTTP failure today |
|---|---|---|
| Wrapper project resolution: `/api/projects/{id}` or `/api/projects?includeArchived=true` | `scripts/deploy-server2.ps1:495`–`:505`; resolution is cached within the phase | Shared `Invoke-RecycleRead`, one GET. Explicit project 404 is translated to `RecycleProjectUnresolved cause=NotFound` at `:498`–`:499`. Other failures propagate. |
| Wrapper open-task list: `/api/agent-tasks?projectId=<scope>&unscoped=include&includeChecks=true&status=Queued,Dispatched,Working,Blocked` | `:668`–`:670` | Shared `Invoke-RecycleRead`; no retry. |
| Wrapper pending-land list: same base query, `&landPending=true` | `:655`, `:668`–`:670` | Same helper and immediate failure. |
| Wrapper task detail: `/api/agent-tasks/{id}` for each open-list row reaching the detail check | `:730`–`:735` | Same helper and immediate failure. Land-list rows do not request detail. |
| Host open-task and pending-land lists: same queries | `scripts/c590-remote.sh:5882`, `:5892`–`:5895` | `c1008_http`; one curl, no retry; failure returns 2 immediately. |
| Host task detail: `/api/agent-tasks/{id}` for each open-list row reaching detail | `:5963`–`:5972` | Same helper; `c1008_http ... || return 2`. |
| Wrapper runner status: `/api/session-runners/{runner}/status` | `scripts/deploy-server2.ps1:330`–`:332`; redeploy admission at `:1119`, `:1138`; retirement at `:1180`, `:1189`, `:1194` | `Invoke-RunnerRequestCore` at `:289`–`:301`: transport throws `RunnerApiUnavailable`; non-2xx throws `RunnerApiFailed ... HTTP <status>`, except GET 404 returns null and callers refuse missing status. No explicit timeout argument or retry in this helper. |
| Host target/counterpart status: same runner-status routes | `scripts/c590-remote.sh:6107`, `:6130`, `:6167`; temp-container cleanup also `:8207`, `:8219` | `c849_status_body` at `:1577`–`:1583`: single curl with 15-second cap; curl failure or anything other than 200 returns 1; proof caller refuses `RunnerStatusMissing`. No retry. |

Wrapper `Invoke-RecycleRead` uses one `Invoke-WebRequest` with `-TimeoutSec 60 -SkipHttpErrorCheck` (`:435`). Every non-2xx response, including 502/503/504 and 4xx, throws `cause=Http status=...` (`:436`–`:438`, `:458`). Cancellation/timeout exceptions become `Timeout`; other request exceptions become `Transport` (`:448`–`:454`). Empty success and JSON parse failure refuse as `Empty`/`Malformed` (`:441`–`:443`). It writes elapsed/outcome facts in `finally` (`:459`–`:461`). There is no attempt loop or backoff.

Host `c1008_http` uses one `curl -sS --max-time 60` (`:5828`). Curl exit 28 becomes `Timeout`, other nonzero exits `Transport`; non-2xx becomes `Http`; empty or invalid JSON becomes `Empty`/`Malformed` (`:5831`–`:5841`). It appends an observation including status and elapsed milliseconds, sets `C1008_TASK_ERROR`, and returns 2 (`:5849`–`:5857`). Request failures never reach the later body validation or second comparison pass.

Both task collectors traverse excluded project scopes and compare two successful passes: wrapper `:649`–`:695`, `:779`–`:785`; host `:5879`–`:5894`, `:5929`–`:5930`, `:6063`–`:6065`. Those passes establish census stability; they are **not failed-request retries**. Both immediately reject malformed envelope fields; invalid detail identity/land evidence is `RecycleLandUnknown` (wrapper `:735`–`:751`, host `:5973`–`:6001`). The slides project is reached through this scope closure, and desktop tasks remain relevant because lands can affect repository safety.

Census invocation inventory:

- Wrapper: main redeploy `:1141`, temp retirement `:1197`, temp-container cleanup `:890`, and read-only `check-census` once per runner at `:1210`–`:1220`.
- Host: each `c1008_status_proof` calls `c1008_tasks` at `:6171`. Normal main recycle calls the proof twice before audit one (`:7702`, `:7715`), once after audit two (`:7831`), then once before each of three target volume removals (`:7839`). A successful pass through that route therefore makes six complete host task censuses, each internally two-pass. Early-return/refusal paths perform fewer.
- Host temp-container cleanup reuses the same task census at `:8223`; its status proof runs initially (`:8306`), before each owned removal (`:8362`) and at completion (`:8382`). This shares the failure behavior even though it was not this incident's phase.

Runner-ready polling does not rescue these failures: wrapper `Wait-RunnerStatus` calls status outside any retry catch (`:967`–`:972`), and its retirement loop only catches `TempContainerStillRunning` (`:940`–`:945`). Neither retries a census HTTP refusal. After host failure the wrapper throws `HostCaseFailed` (`:991`–`:1000`) and exits 2 (`:1240`–`:1242`).

## Reconstructed refusal and journal consequences

The stored diagnosis exactly matches the host helper's HTTP branch. The chain is `c1008_http` (`:5837`, `:5853`–`:5857`) → open-task detail (`:5971`) → `c1008_tasks` saving its partial receipt (`:5861`–`:5873`) → `c1008_status_proof` refusing (`:6171`–`:6172`) → `c1008_refuse` saving diagnosis/outcome and exiting 2 (`:5489`–`:5499`). No retry or second attempt exists on this path.

Given the card's reported final `containersRemoved` phase, the post-audit proof at `:7831` is the matching failure boundary: phase `containersRemoved` is saved **before** audit two (`:7828`), and `removing` is only saved after that proof (`:7834`). The report of the live second helper plus the subsequent HTTP diagnosis reconstructs successful audit completion; an audit failure or unequal outputs would instead have refused before the HTTP census at `:7829`–`:7831`.

This loses elapsed work, not the saved audit text. New operations save `.audit` after audit one (`:7795`–`:7800`); resumes compare against it but do not replace it. Audit refusal records `.auditFailure` while preserving an existing `.audit` (`:7464`); a census refusal changes diagnosis/outcome, not phase or audit (`:5494`–`:5495`). There is no separately persisted post-removal audit-completion checkpoint at `:7829`–`:7830`. `phase=containersRemoved` alone cannot distinguish a completed second audit from a crash before or during it.

## Can passed work safely be reused, and is there an equally safe faster audit?

**Not from the present journal alone.** Conditional reuse of local-content proof is logically sound only if the exact audited protected state remains unchanged from successful audit through deletion, and the remaining publication and removal invariants still hold. This includes work and registered runner-tmp checkout bytes, indexes, ignore rules, Git metadata, retained refs/reflogs/pseudorefs, repository/worktree discovery and path topology, with no unobserved writer or replacement generation. Origin publication must still be established against sufficiently current real heads; local immutability does not by itself prove continued remote publication (`docs/docker-stack.md:325`, `:333`–`:336`). Fresh task, runner, drain/routing, land and no-reference proofs remain independent requirements (`:312`–`:319`, `:345`–`:346`).

The current checks fall short of that continuous proof:

- Volume identity consists of name, driver, options, creation time, mountpoint and selected labels (`scripts/c590-remote.sh:5512`–`:5520`). Equality at `:7745` proves the recorded generation facts, not unchanged files inside it.
- Rollout/cache locks serialize cooperating deployment operations (`:5437`–`:5448`). A read-only audit mount prevents this helper writing the audited volumes (`:7403`–`:7406`); neither establishes that every host process or another mount was unable to write between attempts. The reference census is a point-in-time observation (`:7475`–`:7495`).
- Successful audit text contains repository-path/common-path digests (`:6313`, `:7221`), retained tip IDs (`:7106`–`:7109`) and repository/partial counts (`:7265`). It is not a per-file content manifest, a volume content digest, or evidence of a mutation-free interval. Content check scratch files are discarded (`:6448`–`:6450`, `:7118`). Equality of audit text after two successful scans does not let a later reader infer cleanliness without the scans.

The proposed example, “skip repositories unchanged since the saved audit,” therefore depends on an **independent, trustworthy definition and proof of unchanged**. Same HEAD, same saved tip list, same volume identity, successful ordinary `git status`, and timestamps alone are insufficient: tracked working bytes and untracked work can change without changing HEAD. The source explicitly says status trusts index flags/stat data and therefore hashes every tracked file/symlink regardless of timestamps (`:6384`–`:6426`); the runbook requires this at `docs/docker-stack.md:338`. New repository/admin entries and registered tmp checkouts also have to be accounted for, not just previously enumerated HEADs (`scripts/c590-remote.sh:7138`–`:7143`, `:6847`–`:6871`).

Several obvious speedups already exist: common Git directories are checked once, checkout content checks are deduplicated within an audit (`:6430`–`:6436`, `:7222`–`:7230`), content workers are capped at `min(nproc, 8)` (`:7123`–`:7125`), and regular file hashing is batched into one `hash-object --stdin-paths` process per worktree (`:6422`–`:6423`). Ignored output is already excluded for indexed checkouts (`:6339`–`:6345`). Stored filesystem-wait observations support substantial read cost; they do not establish that more parallelism would improve elapsed time.

No existing faster, equally safe bypass is demonstrated by this evidence. Avoiding the second local-content pass on a proven unchanged offline volume would avoid about one observed 110-minute scan; that is a conditional cost estimate, not a validated optimization. Reuse across separate invocations needs the same unchanged-state proof across the entire gap, plus valid publication proof. There is no current saved evidence that supplies those guarantees, so manually skipping audits or editing the active journal is not supported.

## Remaining uncertainties

1. The 502 emitter and underlying upstream failure are unknown. The failed attempt's host census receipt plus proxy/server logs around 02:15 UTC would distinguish proxy/connectivity failure from an application failure. The immediate-refusal mechanism does not depend on resolving that infrastructure cause.
2. Exact per-audit completion times and resource totals were not recovered; the approximately 110-minute figures come from the card and the timestamped stored progress report. Raw helper/process accounting would refine them.
3. The completed attempt's final journal/receipt files were not independently re-read from the host, because a new resume now uses the operation. The `containersRemoved` end state is recorded on the card; the last directly reported journal snapshot in the retrieved transcript is from 01:25 UTC. This limits the precision of the post-audit failure-boundary attribution, not the fail-fast HTTP finding.
4. No persistent immutability guarantee or content-cache validation was demonstrated. Proving such a guarantee and its handling of remote publication changes is necessary before calling audit reuse equally safe. No benchmark or fault-injection test was run, as instructed.

## Not done, noted

Plan can evaluate bounded transient retries for the shared wrapper/host census readers and conditional audit-proof reuse under a demonstrated unchanged-state invariant; no fix was designed or implemented here.

--- next stage ---
next: plan
handoff: Confirmed single-attempt HTTP refusal after two audits; plan bounded census-read recovery and assess audit reuse only with proof of unchanged protected volume state and valid publication, preserving fresh removal gates.
artifact: docs/investigations/2026-10-20-card-1374-resume-audit-and-census-http.md
