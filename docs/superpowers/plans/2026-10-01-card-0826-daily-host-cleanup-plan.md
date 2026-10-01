# CARD-0826: daily host cleanup

Date: 2026-10-01. Stage: TestDesign complete; Code follows. Baseline: `origin/master` at `8331a9cf1cbb1db564791b3acce5e9af2b298b3a` (fetched during this Plan). Assigned branch starts at `56dce415c0e273e7d4f0c23ada86ff5da3e0045d`; it is not rebased. Source observations below were compared with the frozen baseline. The relevant newer changes are checkpoint-slot tests and the checkpoint-tool slot documentation; they do not remove the deletion constraints below.

## Outcome and scope

Add a daily, bounded backstop for **owned disposable working files whose newest file write is strictly older than 24 hours**, on the desktop, every runner, and the server2 host. Main-instance Hangfire coordinates Antiphon runners. Windmill retains the host SSH bridge. A runner performs its own filesystem work; the server never walks a remote container's mount through Docker. No agent is spawned.

The feature inventories all requested families, records a plan before execution, preserves unknown ownership, supports dry-run and status, reports per-host outcomes to board attention, and monitors the runner-tmp budget between daily runs. It does not convert old age, disk pressure, a terminal task, or an absent PID into deletion authority.

**Operator decision, confirmed 2026-10-01: keep the daily job entirely lock-free with respect to repository mutation.** It takes no repository mutation lease and no `landing.lock`, directly or through any component it calls. Worktrees are inventory-only, including in execute mode; deletion stays with the existing CARD-0692/0459/0665/0670 owners on their own schedules. The daily job deletes eligible owned non-worktree scratch under the safety rules below, and reports eligible-but-unremoved worktree bytes with a sustained-backlog alert. This is the accepted scope, with no outstanding D-2 decision.

Not included: branch/ref garbage collection owned by CARD-0669/0692/0824, slot allocation/reset design, session stopping, provider session archival, package-cache eviction, Docker pruning or volume removal, log retention, and automatic adoption of legacy unmarked folders. The seven historical worktrees remain explicitly held pending disposition.

## Evidence and limits

Read in full using `pwsh -NoProfile -File scripts/card.ps1 get CARD-<n> -Board Antiphon`: 0826, 0692, 0824, 0459, 0665, 0670, 0804, 0805, 0669, and the eight flake cards named below. Read the desktop survey through authenticated `GET /api/agent-tasks/b72ef7e3`, selecting its `result` field without printing credentials. The API result is the evidence, not the preliminary figures quoted in CARD-0826.

| Observation | Value and provenance | Limit |
|---|---|---|
| Desktop disk | Survey: C: 1,598.7 GB used, 263.7 GB free; Temp 419.6 GB / 93,778 entries | Historical survey, not a current Windows measurement. |
| Desktop registered worktrees | Survey: 398 across four repositories, 95.9 GB; SAFE 201 / 40.5 GB, KEEP 144 / 31.9 GB, UNKNOWN 53 / 23.8 GB | SAFE is the survey's classification, **not this plan's permission**. Survey used unfetched local origin refs. Age and all ownership/content gates must be re-evaluated. |
| Desktop temp families | 497 land-verify roots (465 >1 day); 2,401 probe roots (2,337 >1 day) | Approximately 225 GB verify and 110 GB probes were extrapolated from samples. They were not recursively measured per candidate or proved owned. |
| Desktop risks | Nine dirty worktrees, 22 branches with no remote ref, 21 ahead of their remote, 53 detached/uncontained trees; locked partial trees present | Do not inherit the survey's statement that young worktrees simply become SAFE tomorrow. |
| Historical server2 incident | CARD-0826: 502 trees / 327 GB and full disk on September 29; broad `/tmp/antiphon-*` sweep removed `/tmp/antiphon-pty-hosts` and broke launches | A matching prefix and old root timestamp are explicitly insufficient. |
| Historical runner-tmp growth | CARD-0826: 44.8 GB after about nine hours on September 30, approximately 5 GB/hour | Estimate from that interval, not a sustainable measured growth rate. |
| Fresh Linux disk | `2026-10-01T07:36:57Z`, `df -B1 /tmp /work`: capacity 756,634,804,224 B, used 329,589,497,856 B, available 396,234,375,168 B | Both paths share the backing filesystem. Do not add their free-space figures together. This is container-visible filesystem free space, not a separate trusted host-SSH measurement. |
| Fresh Linux allocations | Read-only `du -s -B1`: `/tmp` 94,240,129,024 B; `/work/worktrees` 95,323,074,560 B (collection completed by 07:38Z) | Live, non-atomic observations; no candidate ownership or newest-write eligibility inferred. |
| Checkpoint family size | Read-only `du -s -B1 /tmp/c723-*` aggregation: 5,714 roots, approximately 65.26 GB allocated | Includes unmarked/live/ineligible roots; this is not a reclaimable-byte estimate. |
| Mount identity | `/proc/self/mountinfo`: `/tmp` = `antiphon-runner_runner-tmp`; `/work` = `antiphon-runner_work`, both ext4 on the same backing device | Inspect only these mount entries. Never dump Docker environment/config or provider homes. |
| Current runner catalogue | At 07:38Z: desktop Windows available (capacity 2, occupied 0); server2 Linux available (capacity 10, occupied 5); server2-temp unavailable/stale/draining | Read-only GET `/api/session-runners`. No cleanup capability exists yet. Unavailability never means its files are abandoned. |
| Placement preference | GET `/api/runner-defaults`: revision 2, global default server2, no kind overrides | Use server2 for ordinary verification; request Windows only for native Windows rows. Resolve current catalogue again at execution. |

No builds, tests, process stops, deletions, deployments, schedule changes or production configuration writes were performed in Plan. Git fetch, the plan file, commits and branch pushes are the only task-workspace mutations. No direct host SSH was attempted. Live Windmill schedule configuration, the host's own `/tmp`, and the build-slots image were not freshly inspected; schedule names and old-image evidence below come from repository documentation and the card.

Read-only reproduction commands, scoped to the Linux runner, are `date -u`, `df -B1 /tmp /work`, `du -s -B1 /tmp /work/worktrees`, and filtering `/proc/self/mountinfo` to those two mountpoints. Give inventory a deadline and report a timed-out/inaccessible subtree as incomplete. Do not expand these commands into a user-home scan. The desktop numbers can be recovered from the task result; do not rely on its disposable CSV path.

### Ground truth and ownership reconciliation

| Card / component | Baseline code actually does | Reuse versus new responsibility |
|---|---|---|
| CARD-0459 | `WorktreeResidueJob`, `WorktreeResidueSweepService`, `TaskWorktreeRetirementService`; daily Hangfire `antiphon:worktree-residue`, 10:00 Europe/London, Execute defaults false. Preview persists a run **and reconciles reservation rows**. | Preserve its schedule and current Execute setting; this existing owner alone decides its worktree actions. The new daily job reads release/status facts and independently reports inventory; it never invokes this preview, sweeper or retirement path. No duplicate worktree deleter is introduced. |
| CARD-0665 | `WorktreeIgnoredContentClassifier`, `WorktreeIgnoredContentGate`, evidence retention, `GuardedWorktreeRemoval`, no-follow set-aside removal; protected wins, then retained evidence, then disposable, otherwise protected. | Reuse unchanged classes and evidence rules; no broader `.antiphon/**` or `bin-*` permission. Daily discovery does not grant worktree removal. Board is Review although code is present; presence is not activation acceptance. |
| CARD-0670 | Board Backlog. Existing `ReleaseAsync` requires explicit NoFurtherWorkspaceUse, exact revision/SHA, preserved reports, resolved handoffs, and no recovery debt. | No automatic release merely because a non-Code task ended. Pending never-landed policy remains its owner's work; report `release_required`. Pushed branches are preserved. |
| CARD-0692 | Board Review; intended post-land owner for all tasks, mirrors, sessions, branches and task temp. There is no general post-land temp executor in this baseline. | Only non-worktree scratch cleanup shares the candidate executor/claim store with the daily backstop, deduped by storage/root/owner generation. CARD-0692 retains its own worktree, session, branch and release actions. Daily code never calls its worktree removal or session-stop path. |
| CARD-0824 | Board Backlog; slot worktrees not implemented here. | Reserve a `slot` ownership kind: active, idle and quarantined slots are excluded from daily worktree deletion. Slot reset owns its contents; it may use the same scratch executor after ending its prior task generation. Legacy per-task inventory remains useful. |
| CARD-0804/0805 | Both Done, landed `6bf159cf`; code present at the Plan baseline. `CheckpointTempRootSweep` currently lives in the **test assembly**, with assembly hooks; marker/index and public guards are in `tools/Antiphon.Checkpoints/Cleanup`. Sweep has PID/start identity, nested run custody, per-root gate, budgets and partial-resume behavior. | Extract the existing executor into production-consumable shared code, retain its test hook and marker format. Daily entry adds a 24-hour newest-file gate and shared plan/caps; it must not run tests to invoke cleanup. The five-minute startup sweep and per-test disposal retain lifecycle ownership. |
| Checkpoint sweep limits | Ten-minute creation grace, five-minute interval, 512 index entries, 10,000 descendants/root, 16 completed roots, 256 MiB and two seconds; no newest-file age check. | Lifecycle cleanup may remain earlier than 24 hours; **daily** calls must add the stricter age gate. Extract reusable eligibility/deletion operations; do not blindly invoke current `SweepOnce` as the daily job. |
| CARD-0669 | Board Backlog; owns source-recheck refs, land pins and verify retention. | Ref/pin deletion stays there. Daily worker can execute only exact verify-artifact releases issued by that owner; unknown historical roots remain reported. Shared executor prevents a second recursive remover. |
| Runner mirrors | `RunnerWorkspaceService.RemoveAsync` checks confinement, dirty status and optional PublishedSha, then uses `git worktree remove --force` and repository-wide prune. `RemoteWorkspaceService.RemoveMirrorAsync` sends that request best-effort. | Never call either removal API from the daily job. Inventory runner worktrees and stale mirror gitdirs, classify their contents/release evidence, and report the existing owner; the metadata and trees remain untouched. Separately owned non-worktree scratch on `/work` is eligible for the daily scratch executor. |
| Runner housekeeping | `AuditCleanupService` runs every 30 minutes and calls `PtySessionAudit.PruneOldAudits` and `CleanupPtyHostState`. | Leave it alone. Its owned runtime roots are hard denied to daily cleanup, including old pty-host bins/logs/manifests. |
| Weekly build junk | `scripts/cleanup-build-junk.ps1`: Windmill `u/lndcobra/antiphon_build_junk_cleanup`, Monday 09:00 London; broad bin-* recursion, folder mtime, robocopy mirror deletion. | Replace script with a safe compatibility wrapper; fold its filesystem responsibility into the daily job and disable the weekly schedule at cutover. No simultaneous old/new deleters. |
| Weekly Claude cleanup | `scripts/cleanup-claude-sessions.ps1`: Windmill `u/lndcobra/claude_session_cleanup`; provider REST session report/archive, default dry-run, Delete not intended for schedule. | Leave the existing schedule and API archive policy. It is not local scratch cleanup; local provider session directories are denied here. |
| Legacy installer | `scripts/install-cleanup-task.ps1` can still register a Windows Scheduled Task. | Make install refuse with the Hangfire/Windmill migration message, retain explicit uninstall only. Never execute it in Plan. |

## Design decisions

### D-1: one scheduler owner per storage namespace

Main Hangfire owns `antiphon:host-cleanup:<hostId>` once per day at 10:00 Europe/London with a deterministic 0–30 minute host offset. Desktop calls its local runner over existing authenticated transport; remote runners use a capability-gated `hostCleanupV1` phone-home command. Add one main-instance Hangfire monitor every 15 minutes for bounded usage samples/alerts, with no deletion. No runner timer duplicates either schedule. Persist last completed daily date and run identity so main restart performs at most one catch-up, not every missed day. Preserve CARD-0459's independent `antiphon:worktree-residue` job and configuration: it owns worktree retirement; the new job only inventories worktrees and owns eligible scratch deletion.

Windmill owns `u/lndcobra/antiphon_host_cleanup_server2` for server2 **host-only** roots through the existing trusted SSH lane. The host command runs the same packaged cleanup worker with a configured root inventory, sends its receipt to the main API, and reports its own failures through the existing Windmill run status. Never give a runner the host Docker socket. Host plans exclude all Docker volume data, including runner-tmp/work/state/dind/cache; runners own their mounted disposable roots. A second runner/container sharing a backing store must use the same storage identity and claim ledger, not acquire independent deletion authority.

Other registered runners get the same Hangfire contract with per-host roots and dry-run defaults; unsupported/offline runners produce a visible deferred result. Do not infer a host root from an arbitrary path supplied by a task. Physical-host disk free space and namespace usage are separate metrics.

Rejected: new Windows Scheduled Tasks; Windmill for in-process Antiphon scheduling; a timer in each runner; host `docker exec` sweeps of runner volumes; treating `server2` and `server2-temp` as the same namespace. The old build-junk schedule is folded in; Claude API archive remains separate.

### D-2: entirely lock-free daily job; worktrees are inventory-only

Invariant: daily planning/execution never acquires `IRepositoryMutationLease` or `RepositoryMutationLease`, opens/acquires `<git-common>/antiphon/landing.lock`, or calls any component that does so. This applies to the full graph of server, runner, host helper, worktree inventory, scratch executor and error/retry paths. The job never invokes Git fetch/prune/remove/update-ref, reconciles workspace-use rows, enqueues worktree retirement, or spawns an agent. Read-only Git probes set `GIT_OPTIONAL_LOCKS=0`; use `ls-remote` observations plus locally available objects to prove exact pushed/contained commits. Missing objects, stale refs, network failure or an ambiguous detached identity mean Unknown, not a fetch or permission.

`GuardedWorktreeRemoval.AuthorityAsync` checks lease ownership for the repository's **common directory**. `RepositoryMutationLease` implements that authority with an exclusive `FileShare.None` handle on `<common>/antiphon/landing.lock`, shared with land and dispatch. It is a repository-wide lease, not a brief per-worktree lease; each retirement can hold lands/worktree creation and encounter the unfinished-child-journal fence. Enqueueing a leased owner from the daily job would still violate this decision. Leave the guard and the existing owners' independent schedules unchanged.

`Worktrees=InventoryOnly` is a fixed behavior of this implementation, not a deletion mode unlocked by Execute. Inventory every discovered registered worktree, runner mirror and unregistered worktree-like directory, with bounded pages and explicit incomplete/unknown entries. Classify `would-remove`, `keep`, or `unknown` using CARD-0665's Protected/Evidence/Disposable classes and CARD-0670's release rules. Record exact owner/task/session/branch identity, source/pushed/target SHA observations, sizes, newest inspected file write and age, dirty/unpushed/active-task/live-session/branch-reachability reasons, evidence-preservation/release facts, and keep-list expiry. Reuse only pure classifiers and read-only probes; do not call guarded removal, `TryRetireAsync`, release/reconcile operations, or a preview with side effects. If all safety/release conditions are proved, `would-remove` describes eligibility for the existing owner, never an action by this job.

Read existing-owner availability/latest refusal metadata at most once per owner per run; no request to execute or enqueue is sent. An unavailable/refusing owner leaves the classification and files intact, adds `owner_unavailable` or the bounded refusal code to the report, and is not retried in a loop. Do not infer live availability from an old successful receipt. Reports state: **`worktrees eligible but not removed by this job: N, X GB, owner: <existing owner>`**, grouped by CARD-0692 post-land Cleanup and CARD-0459 scheduled sweep, with CARD-0665/0670 authority/release details. Eligible-but-unremoved totals are never counted as reclaimed bytes.

This worktree exception does not disable lock-free scratch cleanup. Eligible temp families, non-worktree scratch on the runner work volume, producer-owned `bin-*`/`obj` outputs, per-run `.antiphon` scratch, exact allow-listed `/tmp/claude-1654/<task>` scratch and owned checkpoint roots are deleted under D-3..D-5. A separately owned disposable scratch child inside a retained worktree can be removed only with its own release, 24-hour age and all safety gates; the worktree, source, `.git` entry, common-directory metadata, registrations and refs remain unchanged. An unregistered directory resembling a worktree never becomes generic scratch merely because Git no longer lists it.

An unpushed HEAD is always kept. A HEAD absent from origin/master but fully contained in its pushed task branch is only a potential non-Code retirement candidate: it still requires CARD-0670 release and retained evidence, and this job never deletes that remote branch. Dirty tracked/untracked source, ignored Protected content, active/dispatched/queued/blocked owner, live/unknown session, pending land/recovery, Mutation/SourceLanding ownership or a slot binding blocks retirement.

Rejected: removing the lease check; substituting age for release; calling the runner's Force/prune path; or indirectly acquiring the repository-wide lease by enqueueing an existing owner. The operator-selected inventory-only worktree behavior is the acceptance contract for this card.

### Decisions for the operator

The current decision is settled: **keep the daily job entirely lock-free**. No answer is needed to implement this plan.

Document, but **do not build**, a default-off extension named **worktree retirement enqueue**. Reserve the future decision/configuration line `WorktreeRetirementEnqueue.Enabled = false`; it is a documented contract, not a functioning setting or dormant call path in this delivery. An operator can later explicitly commission `WorktreeRetirementEnqueue.Enabled = true` after weighing the repository-wide lease cost and effects on lands. That future implementation would submit immutable eligible inventory IDs to the existing owner off-peak (proposed 02:00–04:00 Europe/London), at most ten per day and one per minute, with fresh owner-side revalidation and visible refusal receipts; busy/refusing owners defer without retry loops. Existing inventory records carry the necessary owner/root/generation/reason/size facts now, so this remains a bounded adapter addition rather than a cleanup-policy redesign. Enabling it would deliberately revise today's transitive no-lease invariant and requires its own reviewed footprint/tests; changing a config file alone cannot activate unbuilt behavior. It can later be disabled with the same one-line decision. Do not add its queue, executor, migrations, timer or enablement code to this card.

### D-3: explicit disposable families, deny first

Registry entries bind a family id, exact configured root/depth, name grammar, ownership adapter, evidence policy and executor. A name match discovers a candidate; it never authorizes deletion. New families ship report-only until their creator and custody rules are added with tests.

| Family | Admitted shape and additional authority |
|---|---|
| Checkpoint fixture | Direct temp child `c723-<32 lowercase hex>`; valid CARD-0804 root marker/index, original per-root gate and all nested executor custody. |
| Probe/test sandbox | Individually registered grammars for c527/c487/c590/c408/c490 scratch and numeric `c<N>-probe-<run>` roots. Exact task/run marker is mandatory; no generic `c*` glob. Unknown creators remain inventory-only. |
| Land verification | `antiphon-land-verify-<id>` plus an exact terminal-operation release and retained-evidence receipt from CARD-0669/land owner. Never glob-delete historical roots. |
| Task scratch | Registered per-run `.antiphon` subdirectories and exact `/tmp/claude-1654/<task>` (or actual platform scratch mapping) from a creator record. Do not assume the final component is the task ID; resolve its full owner. Never delete `/tmp/claude-1654` itself. |
| Runner work-volume scratch | Exact registered non-worktree scratch roots on `/work`, with owner/run release. Worktrees, mirrors and worktree-like orphan directories stay in the inventory lane; Git admin/common directories are denied. |
| Build outputs and private caches | Exact producer-owned `bin-*` and `obj` output paths, private NuGet package+scratch pairs, and test TMPDIR children recorded before use. Shared/live bin/obj, shared NuGet/npm caches and their locks are excluded. A retained worktree's independently released scratch child is eligible; its source and Git metadata are not. |
| MSBuild/test-host temp | Only a registered per-run sandbox; shared `MSBuildTemp*`, Testcontainers state and standalone `.tmp` files without custody remain Unknown. |

Hard deny roots include `/tmp/antiphon-pty-hosts` and all descendants, desktop configured pty-host runtime roots, runner state/custody/ledgers/manifests, provider `.claude`/`.codex`/`.grok` homes and sessions, Hangfire/Postgres stores, build-slot leases, Git common/admin dirs and `.git/antiphon` journals/custody, shared package caches/scratch locks, retained reports/deliverables, Docker daemon data and every mounted foreign filesystem. Deny ancestors **and descendants**: a proposed parent containing protected runtime state is not disposable. Resolve configured runtime paths as well as canonical defaults; aliases and mount identities cannot bypass denial. Protect `.git/antiphon/review` under this job; its exact review owner must export/release artifacts first, not make that administrative tree generally disposable.

Reject links/junctions/reparse points at any ancestor or descendant, mount crossings, case-confused escapes, invalid identities, unreadable entries and incomplete traversal. Windows and Linux use separate native file-identity/no-follow adapters. Configuration may narrow admitted families or add protected paths; it cannot remove hard denies or disable custody/content gates. Do not log payloads, command lines, environment, credentials or provider-session contents.

Rejected: `antiphon-*`; treating all gitignored files as disposable; broad `/tmp` recursion as deletion authority; assuming an old private NuGet cache is unused; inheriting the unsafe weekly script's name-only policy.

### D-4: custody, age and races

Use injected `TimeProvider`, filesystem, process-identity/liveness probe, Git probe, owner/run status probe and host-maintenance probe. Planning is pure over collected facts. Filesystem execution also goes through the injected seam; there is no static System.IO escape available to unit tests.

For a nonempty candidate, record the maximum last-write UTC of **every regular file**, including hidden files and existing ownership records, plus the root's creation/ownership timestamp as a conservative floor. Require `newestWrite < now - 24h`, strictly; equality, future timestamps, unreadable entries and traversal budget exhaustion are kept. Empty roots require a valid ownership record older than 24 hours. Folder mtime is diagnostic only and never substitutes for a nested-file scan. A marker updated by cleanup itself cannot authorize deletion: classify before writing, preserve the original observation, and require the age check again for any later resumed run.

Process identity includes host boot identity, PID namespace/store identity, PID and process start time. Dead starter alone does not prove its executor/descendants died. Reuse checkpoint nested custody and terminal-run checks; require owner settlement and report/evidence preservation where applicable. Live, reused/ambiguous PID, inaccessible liveness or missing nested custody retains the candidate. Never kill to make cleanup eligible.

Use one scratch-candidate claim keyed by storage identity + canonical file identity + owner generation. Post-land scratch cleanup, checkpoint lifecycle, self-clean and daily scratch entry points converge on the same family executor and exclusion mechanism; whole-worktree retirement stays with its separate owner. Duplicate scratch requests return the same receipt; a changed path identity or owner generation refuses. Revalidate deny/keep-list, file identity, content inventory, latest write, liveness, owner release, maintenance epoch and remaining budget immediately before destructive work. Native deletion is handle-relative/no-follow, bounded, and never recurses through a newly substituted link. A failed or partial delete retains its custody and receipt; it is not success and cannot acquire a fresh budget on a retry of the same run. If independent worktree retirement removes a parent or candidate, report AlreadyAbsent/changed with zero bytes attributed to this job; never recreate it, reconcile its Git metadata or retry through the retirement owner. Refresh the worktree post-run byte sample so concurrent owner cleanup cannot be double-counted as daily reclamation.

Restart/land exclusion cannot be promised by a one-time status poll. Add a small **host maintenance activity gate**, distinct from the repository lock: cleanup requests are try-only and yield immediately to a pending restart/land; startup/restart/deploy/land entry points publish a maintenance epoch before doing work and wait for the current bounded cleanup operation to drain. Recovery defaults to deletion disabled after uncertain process death; a TTL does not prove old work stopped. No repository lock is held while waiting. Gate contention defers cleanup and never delays a land behind a whole inventory. Gate ordering and cross-process host participation are mandatory integration tests. Existing restart/launch locks and child journals remain additional read-only vetoes; this job never clears them.

Rejected: raw PID lookup, liveness from a terminal task row, one-shot preflight, deletion through symlinks, a repository-wide housekeeping lock, or concurrent independent family sweepers.

### D-5: dry-run, plans, limits and operator holds

`host-cleanup.ps1 -Host <id> -DryRun` returns a plan on stdout; `-Status` reads prior receipts. Dry-run performs **zero writes**: no marker/index/cursor updates, reservation reconciliation, claims, budget spend, evidence copies, report persistence or Hangfire mutation. An explicit `-PlanFile <outside-candidate-path>` lets the caller save returned JSON; that is the only requested output write and is tested separately. Scheduled dry-run runs may persist that returned document to the protected receipt store and publish attention; the scan itself remains pure. Execute always durably writes its complete plan outside candidate roots before the first deletion. Failure to persist means zero deletes.

Default daily deletion caps per storage namespace: **10 scratch-candidate attempts and 10 GiB**, total across disposable families. Worktree inventory uses scan limits but never consumes or reserves a deletion allowance. Count scratch attempts including partial failures; no cap reset on retry/reconnect. Reserve a candidate's conservative upper-bound bytes before deleting, using `max(logical, allocated)` and rejecting uncertainty/overflow. Candidates larger than the remaining allowance are kept, never truncated to fit. Track file IDs/hard links conservatively; reclaimed bytes are measured separately, not inferred from reservations. Bound scan to 10,000 candidates, 100,000 descendants per candidate and a five-minute host pass; persist pagination only outside dry-run and mark incomplete candidates Unknown. Stable ordering plus a cursor prevents small items permanently starving old entries. Limits are settings, validated positive and never relaxed automatically for pressure.

Keep-list records carry storage identity, canonical exact path, full owner identity where known, reason, creator, created/expiry UTC and revision. Seed the seven server2 task prefixes (`19e7f181`, `28fc3bab`, `7647cb6a`, `7ebf8aa2`, `89719e41`, `c03af147`, `e7c20aaa`) as explicit unresolved holds. Activation resolves each prefix uniquely against task records and actual paths; ambiguity classifies the worktree as Unknown/held and preserves its scratch descendants too. Initial review expiry is **2026-10-08T00:00:00Z**, not deletion approval. Expired holds remain visible as `hold_expired_review_required` until an explicit disposition removes/renews them; every ordinary safety gate still applies. This avoids silently discarding the historical request to decide their fate. New holds follow the same expiry/attention behavior.

Rejected: dry-run which writes reconciliation state; raw recursive delete; “free space low” overrides; per-family caps that multiply the advertised total; expiration as automatic permission to delete dirty or unresolved work.

### D-6: shared ownership helper and post-land integration

Add `task-scratch.ps1 register|status|release|clean` backed by the same worker. Registration occurs **before** a task uses a new scratch root, with full task/run/process identity and exact paths; no retrospective claim over arbitrary content. Code and Review bundles require an end-of-task inventory and `release/clean` receipt for their own scratch, including private caches and scratchpads, after child processes join and evidence is preserved. Self-clean is lifecycle cleanup and may precede 24 hours; it can only affect that exact released run, never an active checkpoint or the task's whole worktree. Daily cleanup still uses the strict age gate.

CARD-0692 calls the same released-scratch executor for all settled card tasks, including the exact mapped `/tmp/claude-1654/<task>` subtree. Missing report delivery/preservation or missing producer record retains it. If CARD-0692 lands first, integrate its actual entry point after inspecting that source; no second Cleanup stage, agent role or session stop is added here. CARD-0824 slot reset consumes the same records when implemented. Private cache cleanup never touches the shared CARD-0849 cache volumes.

Rejected: asking agents to `rm -rf` guessed paths; a second cleanup stage; allowing a Code task to mark another task's scratch disposable; declaring all old provider scratch abandoned.

### D-7: reports, budgets and alerts

Persist a host run, immutable planned candidates, outcomes, keep reasons, source/config digest, exact host/runner store/boot/storage identities, timestamps, scan completeness, bytes planned/attempted/reclaimed, before/after free bytes, and budget/cursor status. Host report upload is idempotent by run ID + digest; reject a mismatched replay. Large reports are paginated; attention contains bounded metadata and a link, never secret contents or arbitrary stderr. Associate configured board IDs explicitly; do not use a fleet-wide task list as a board-specific authority.

Add durable attention projections for the latest daily summary, stale/missed host report, expired hold and disk pressure. A persisted receipt is the producer evidence; a successful attention GET containing its run ID plus the board-scoped item and event invalidation is the delivery acceptance. Runner uploads queue in protected local state until acknowledged; loss of the reply queries status before retry, never repeats deletion. Keep reports for 30 days in the protected store; do not make report retention another directory sweeper in this card.

Each report includes worktree counts and bytes per `would-remove`/`keep`/`unknown` disposition and CARD-0665 content class, newest-write age, owner status/refusal reason, holds and before/after disk free. Include the D-2 eligible-but-unremoved summary even in execute mode, with zero worktree removals and zero worktree-reclaimed bytes. Show remaining scratch bytes separately; do not double-count an independently removed scratch child in worktree backlog measurements. A new `WorktreeCleanupBacklog` attention episode opens when the post-run eligible-but-unremoved worktree total is at least **20 GiB for N=3 consecutive complete daily reports** on that host (configurable bytes/days; at most one qualifying observation per local calendar day). Incomplete, missed or unavailable inventory cannot satisfy a missing day or clear an open alert; a missing day breaks the qualification streak and raises scan freshness separately. Clear only on a fresh complete inventory below threshold. Show N, observed days, GB and existing responsible owner, including unavailable/refusing status. This alert never enqueues retirement, retries an owner or widens scratch caps.

Initial configurable `runner-tmp` budget: **100 GiB**, warning at **60% (60 GiB)**, critical at 90%. Disk low threshold: free bytes below **max(50 GiB, 10% capacity)** after a run. The fresh approximately 87.77 GiB tmp measurement would already exceed the warning threshold, supporting immediate visibility. Fifteen-minute observations are bounded/cached and show sample age/incomplete, not false zero. One unresolved attention episode per host/storage/condition; resolve only on a fresh good sample. Pressure alerts never increase caps or shorten retention.

Extend CARD-0849 rolling-deploy smoke receipts with exact `/tmp` volume identity and allocated bytes, host filesystem capacity/free bytes, observation time and collection completeness. Host free must come from the trusted host lane, not be relabelled container evidence. On the next already-authorized redeploy recreate the build-slots service with the selected current image and verify its immutable image/source SHA and broker response; the card's `session-testing:a8b4e9e5a071` is historical, so inspect before acting. This Plan performs no redeploy.

Rejected: counts inferred from attempted deletions, treating offline as zero usage, an alert sink outside board attention, unbounded `/tmp` walks every minute, or deletion as the response to low space.

## Exact implementation footprint and slices

This dispatch changes **only this plan**. The Code footprint implements lock-free scratch deletion plus worktree inventory/classification/reporting. It contains no worktree executor, retirement enqueue adapter or repository-lease acquisition. D-2's documented extension is outside this delivery and requires separate explicit commissioning. No area-map change is justified.

New files are prefixed **new**; renamed files are identified explicitly. Braced lists below enumerate files, not permission for unspecified files. The migration's generated timestamp is the sole filename component intentionally determined by the EF CLI.

| Slice | Exact paths and changes | Test-first work |
|---|---|---|
| S1: shared policy and filesystem seams | **new** `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, `CleanupContracts.cs`, `CleanupPlanner.cs`, `CleanupExecutor.cs`, `CleanupFamilyRegistry.cs`, `CleanupOwnership.cs`, `ICleanupFileSystem.cs`, `CleanupFileSystem.cs`, `LinuxCleanupFileSystem.cs`, `WindowsCleanupFileSystem.cs`, `CleanupBudget.cs`, `CleanupKeepList.cs` (all under that project); `Antiphon.sln`; `tests/Antiphon.Tests/Antiphon.Tests.csproj` project reference | **new** `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs`, `HostCleanupExecutionTests.cs`, `HostCleanupOwnershipTests.cs`, `HostCleanupLinuxTests.cs`, `HostCleanupWindowsTests.cs`, and `HostCleanupFixture.cs`. Write policy/budget/ownership assertions against injected facts before production decisions. The fixture is a virtual filesystem; real host paths are refusal inputs and never reach OS deletion. |
| S2: checkpoint reuse and task scratch | Move `tools/Antiphon.Checkpoints/Cleanup/{ProcessIdentity,RunOwnership,TestRootGuard,ToolCopyCleanup}.cs` to `src/Antiphon.HostCleanup/Checkpoints/` retaining existing public names/namespaces; modify `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` to reference shared library. **new** `src/Antiphon.HostCleanup/Checkpoints/CheckpointTempRootSweep.cs`, extracted from existing test implementation, with injected I/O and family execution adapter; leave only assembly hook/roster glue in `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs`; adjust `tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs` and `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj`. **new** `src/Antiphon.HostCleanup/ScratchRegistry.cs`, `tools/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, `tools/Antiphon.HostCleanup/Program.cs`, `scripts/task-scratch.ps1` | **new** `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointAdapterTests.cs`; port the 40 source-derived sweep/scope/tool-copy contracts to HostCleanupCheckpointCompatibilityTests over virtual I/O before the extraction; do not execute the old physical fixtures in this card's rows. Preserve `ownership.json` and `.checkpoint-test-root.json` v1 compatibility; no test hook in production. `ProcessControl.cs` stays in Checkpoints; the cleanup library has no kill API. Update library internals to avoid mutable static state; maintain pure compatibility facades where existing callers require them. |
| S3: worktree inventory/classification/reporting, coordination and attention | **new** `server/Application/Settings/HostCleanupSettings.cs`, `HostCleanupSettingsValidator.cs`; **new** `server/Application/Dtos/HostCleanupDtos.cs`; **new** `server/Application/Interfaces/IHostCleanupTransport.cs`, `IHostMaintenanceStore.cs`, `IHostCleanupWorktreeProbe.cs`; **new** `server/Application/Services/HostCleanupService.cs`, `HostCleanupWorktreeInventory.cs`, `HostCleanupAttentionService.cs`, `HostMaintenanceService.cs`; **new** `server/Infrastructure/Git/HostCleanupWorktreeProbe.cs` (read-only); **new** `server/Infrastructure/Agents/HostCleanupJob.cs`, `HostCleanupMonitorJob.cs`; **new** `server/Infrastructure/Data/HostMaintenanceStore.cs`; **new** `server/Domain/Entities/HostCleanupRun.cs`, `HostCleanupCandidate.cs`, `HostCleanupHold.cs`, `HostMaintenanceActivity.cs`; **new** `server/Api/Endpoints/HostCleanupEndpoints.cs`; modify `server/Infrastructure/Data/AppDbContext.cs`, `server/Program.cs`, `server/appsettings.json`, `server/Infrastructure/Agents/HangfireConfiguration.cs`, `server/Application/Services/AttentionService.cs`, `server/Application/Dtos/AttentionDtos.cs`, and `server/Application/Services/AgentTaskLandService.cs` (maintenance entry/exit only, no cleanup invocation) | **new** `tests/Antiphon.Tests/Application/HostCleanupOrchestrationTests.cs`, `HostCleanupWorktreeInventoryTests.cs`, `HostCleanupReportTests.cs`, `HostCleanupMaintenanceTests.cs`. Run complete mixed worktree/scratch inventories with a recording/throwing repository-lease double and assert zero acquisitions. Cover unavailable/refusing owners, execute-mode preservation and sustained-backlog attention. Use isolated PostgreSQL and stub transport, not the production runner. Migration `AddHostCleanup` creates run/candidate/hold/maintenance tables and uniqueness constraints, not a retirement queue: CLI-generated `server/Migrations/<timestamp>_AddHostCleanup.cs`, matching `.Designer.cs`, and `AppDbContextModelSnapshot.cs`. No manual migration editing. |
| S4: runner transport and packaging | **new** `src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs`, `src/Antiphon.SessionRunner/HostCleanupCommandService.cs`, `HostCleanupLocalStore.cs`, `HostCleanupWorktreeInventory.cs` (read-only Git/filesystem facts); modify `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`, `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `src/Antiphon.SessionRunner/Program.cs`, `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`; **new** `server/Infrastructure/Agents/SessionRunner/HostCleanupTransport.cs`; modify `docker/session-runner-grok/Dockerfile`, `docker-compose.server2-runner.yml` for worker publication/settings, no new privileged mount | **new** `tests/Antiphon.SessionRunner.Tests/HostCleanupCommandTests.cs`. Capability absence, arbitrary path input, wrong storage identity, disconnect and idempotent replay must fail closed. Worktree facts are returned for classification; WorkspaceRemove/retirement commands are never issued. Host worker is a packaged .NET command, never an agent and never a test assembly. Desktop routes and phone-home routes share the same command service. |
| S5: operations and agent guidance | **new** `scripts/host-cleanup.ps1`, `scripts/host-cleanup-server2.sh`, `scripts/fixtures/c826-cleanup-shim.ps1`, `scripts/windmill/antiphon-host-cleanup-server2.json`, `scripts/windmill/antiphon-host-cleanup-server2.schedule.json`; modify `scripts/cleanup-build-junk.ps1`, `scripts/install-cleanup-task.ps1`, `scripts/restart-apphost.ps1`, `scripts/restart-session-runner.ps1`, `scripts/deploy-server2.ps1`, `scripts/verify-card0849-caches.ps1`, `scripts/windmill/README.md`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md`; **new** `docs/host-cleanup.md`; update only relevant sections of `docs/bootstrap.md`, `docs/logs.md`, `docs/testing-and-build.md`, `docs/ops-http.md`; modify `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`, `attentionVisuals.test.ts` | **new** `tests/Antiphon.Tests/Infrastructure/HostCleanupScriptTests.cs` (offline process/SSH/Windmill/deploy shims); client tests for summary/pressure/expired-hold display. Scripts retain ASCII and PowerShell 5.1 compatibility where daemon fallback applies. No live schedule or deploy in Code. |

S3 API shape: GET `/api/host-cleanup/hosts`, GET `/api/host-cleanup/runs/{id}` (paged candidates), read-only preview accepting only a configured host ID, operator-authenticated run/hold/maintenance actions, and authenticated host receipt ingestion with bounded payloads. An untrusted task token cannot enumerate another task's roots, register another owner, execute fleet cleanup, release holds or authorize maintenance. `-DryRun` maps to pure preview; daily scheduling wraps preview with the explicitly permitted report persistence. Execute admits only disposable scratch; worktree inventory cannot be passed to that executor. Client additions use existing attention transport/invalidation, not a new screen or raw fetch. No retirement enqueue API/configuration toggle is implemented. CARD-0459's registration/settings remain untouched by the new scheduler registration.

Execution order groups S1/S2 fixtures and compiling seams before policy/extraction, then S3/S4 service and transport fixtures before their implementation, then S5 offline script/client fixtures before wrappers. Keep the S2 compatibility run before moving shared code. Commit/push each substantial slice. New tests assert production decisions, virtual filesystem contents and actual caller behavior. Run the affected exact CP selection against compiling neutral seams to obtain named assertion failures; record this as the **test-first red round** of the same closed row, then rerun after implementation. Compilation failures do not count as red. The final green rows cover all five slices; later code changes require rerunning affected rows, with every red/fix/pre-move run reported separately. The detailed frozen checkpoint section owns group timing and the scoped Windows preparatory run.

**Source-area scope (future Code):** `src/Antiphon.HostCleanup/**,tools/Antiphon.HostCleanup/**,tools/Antiphon.Checkpoints/Cleanup/**,tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj,tests/Antiphon.Tests/Infrastructure/HostCleanup*,tests/Antiphon.Tests/Application/HostCleanup*,tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs,tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs,tests/Antiphon.Tests/Antiphon.Tests.csproj,tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj,tests/Antiphon.SessionRunner.Tests/HostCleanup*,server/Application/Settings/HostCleanup*,server/Application/Dtos/HostCleanup*,server/Application/Dtos/AttentionDtos.cs,server/Application/Interfaces/IHostCleanupTransport.cs,server/Application/Interfaces/IHostMaintenanceStore.cs,server/Application/Services/HostCleanup*,server/Application/Services/HostMaintenanceService.cs,server/Application/Services/AttentionService.cs,server/Application/Services/AgentTaskLandService.cs,server/Infrastructure/Agents/HostCleanup*,server/Infrastructure/Agents/HangfireConfiguration.cs,server/Infrastructure/Agents/SessionRunner/HostCleanupTransport.cs,server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs,server/Infrastructure/Data/HostMaintenanceStore.cs,server/Infrastructure/Data/AppDbContext.cs,server/Domain/Entities/HostCleanup*,server/Domain/Entities/HostMaintenanceActivity.cs,server/Api/Endpoints/HostCleanupEndpoints.cs,server/Migrations/**,server/Program.cs,server/appsettings.json,src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs,src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs,src/Antiphon.SessionRunner/HostCleanup*,src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs,src/Antiphon.SessionRunner/Program.cs,src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj,docker/session-runner-grok/Dockerfile,docker-compose.server2-runner.yml,scripts/host-cleanup*,scripts/task-scratch.ps1,scripts/fixtures/c826-cleanup-shim.ps1,scripts/windmill/antiphon-host-cleanup-server2*,scripts/windmill/README.md,scripts/cleanup-build-junk.ps1,scripts/install-cleanup-task.ps1,scripts/restart-apphost.ps1,scripts/restart-session-runner.ps1,scripts/deploy-server2.ps1,scripts/verify-card0849-caches.ps1,server/Bundles/stage-code.md,server/Bundles/stage-review.md,client/src/api/attention.ts,client/src/features/attention/attentionVisuals*,docs/host-cleanup.md,docs/bootstrap.md,docs/logs.md,docs/testing-and-build.md,docs/ops-http.md,Antiphon.sln`.

**Additional exact Code scope for S3's read-only worktree probe:** `server/Application/Interfaces/IHostCleanupWorktreeProbe.cs,server/Infrastructure/Git/HostCleanupWorktreeProbe.cs`. The complete Code scope is the union of these two scope lines and Verification design's exact extraction/fixture/startup additions; the new service, runner inventory and tests are already covered by the respective `HostCleanup*` prefixes above. There is no edit to `GuardedWorktreeRemoval.cs`, `RepositoryMutationLease.cs`, `TaskWorktreeRetirementService.cs`, `WorktreeResidueSweepService.cs`, `WorktreeResidueJob.cs` or `RunnerWorkspaceService.cs`, and no worktree-executor scope.

The Plan and TestDesign dispatch scope is just `docs/superpowers/plans/2026-10-01-card-0826-daily-host-cleanup-plan.md`. These globs have literal prefixes; do not replace them with leading filename wildcards or a broad `delegation` area.

| In-flight task | Actual overlap with proposed Code | Ordering |
|---|---|---|
| CARD-0835 | `tools/Antiphon.Checkpoints` project/shared cleanup area, `server/Application/Services/AgentTaskLandService.cs`, generated EF model snapshot/migration area, `docs/testing-and-build.md`. No planned edit to `scripts/run-checkpoint.ps1`, `scripts/delegate.ps1`, or Review services. | Serialize S2/S3 touching these areas after its land; re-read the landed source and rederive compatibility counts. Do not dispatch overlapping Code merely because worktrees are isolated. |
| CARD-0801 | Same `tests/Antiphon.SessionRunner.Tests` project, but only new HostCleanup tests. No `HerdrTransport.cs`, fake Herdr fixture or existing parity-test edit. | No exact source-file collision; avoid concurrent builds in one checkout. Do not add this feature's cases to Herdr tests. |
| CARD-0778 | No Grok readiness, adapter or fixture file in the footprint. | No exact collision. Dockerfile is packaging only, not a provider readiness change. |
| CARD-0880 policy | Its supplied orchestrator-policy files do not overlap stage-code/stage-review guidance. | No required dependency unless its scope expands; preserve policy text. See the frozen collision table. |
| CARD-0849 | `scripts/deploy-server2.ps1`, `scripts/verify-card0849-caches.ps1`, Compose/Docker packaging if its branch changes those again | Preserve existing rollout gates and cache scope; report disk metrics by extending existing receipts. |

## Verification design

### Inspection

TestDesign frozen at source `1dacda5e36f52a288c3b9b91f379b0e274c49038`. `pwsh -NoProfile -File scripts/card.ps1 get CARD-0826 -Board Antiphon` confirmed **Daily per-host cleanup of worktrees, work volume and temp older than 24 hours**. This stage changes only this plan. Linux/Windows builds and tests executed: **0/0**; all counts below are source census or explicitly proposed executions, never runtime results.

| Bodies read | Boundary and coverage |
|---|---|
| All 37 methods in `CheckpointTempRootSweepTests`, `CheckpointTempScopeTests`, `CheckpointToolCopyCleanupTests`; `CheckpointTestScope`, `CheckpointTempRootSweep`, `CheckpointTestSupport` helpers; lifecycle-host csproj links | S2 extraction, real callers, original lifecycle ownership and budgets -> V-4, R-1 and 40 virtual compatibility executions below. |
| `tools/Antiphon.Checkpoints/Cleanup/{ProcessIdentity,RunOwnership,TestRootGuard,ToolCopyCleanup,OutputCleanup}.cs`; `Evidence/EvidenceFolder.cs`, `Commands/WaitCommand.cs`, `State/RunStateStore.cs`; `CheckpointApp` create/start/execute cleanup and `Program` wait/stop/clean call sites | Full production dependency closure and entry points -> V-4/R-1. `ProcessControl` remains outside shared cleanup; no kill capability is added. |
| `IRepositoryMutationLease`, `RepositoryMutationLease` (both acquisition overloads, concrete exclusive `landing.lock` open), `WorktreeIgnoredContentClassifier`, land acquisition entry | Transitive no-lease, worktree preservation, maintenance ordering -> V-7/V-13/R-2. No remover or lease authority is weakened. |
| `WorktreeResidueRegistrationTests.C459_ScheduledWorkerExecutesBothLanes`, `AttentionServiceTests.Outbound_delivery_states_have_truthful_attention`, `TestDbFixture`, `ProductionRunnerGuard` | Nearest scheduling/attention/store fixtures -> V-5/V-6. Use isolated cloned database; do not inherit their real scheduler registration or broad attention class filter. |
| `PhoneHomeCommandDispatcher` constructor/admission and `PhoneHomeCommandDispatcherTests` admission/capacity/retire cases | Real dispatcher/serialized transport with fake runtime -> V-8 and delivery tests. No Herdr or provider launch fixture. |
| `cleanup-build-junk.ps1`, `install-cleanup-task.ps1`, restart entry points, `dev-aspire.ps1` startup, `session-runner-restart-health.ps1` platform seam, `BuildSlotScriptTests` harness, `RunnerDrainScriptTests` ASCII assertion | Weekly wrapper, no new installer, offline maintenance scripts, ASCII/5.1 -> V-9/V-11/R-3. |
| All `attentionVisuals.test.ts` bodies; `test-client.ps1`; `PlanTableImporter`, `run-checkpoint.ps1`, stage TestDesign bundle and testing owner | Client census, positional filter, escaped class unions, manifest schema -> V-12 and CP table. |

### Extraction corrections and call-site closure

The weekly script has **no safe reusable cleanup policy**: it selects `bin-*` by folder mtime then invokes robocopy and Remove-Item. The two weekly entry points found in executable/config source are `scripts/cleanup-build-junk.ps1` itself and `scripts/install-cleanup-task.ps1`; the documented external Windmill schedule calls the former over SSH. There is no checked-in definition proving that live schedule's current settings. Replace the old script with a compatibility wrapper calling the same HostCleanup worker used by daily and task-scratch; never transplant its name-only authority. V-9 exercises the actual script with recording commands, V-5 freezes schedule cutover, and activation requires inspecting the real external schedule.

The four-file checkpoint move is insufficient on its own. `ToolCopyCleanup.cs` also defines `CleanupReceipt`, `ExecutorOwnership`, `ExecutorOwnershipStore`, and `ContainedCleanup`: move those with it, preserving public namespaces/formats. Extract only `EvidenceFolder.IsExecutorImage` into new `src/Antiphon.HostCleanup/Checkpoints/ExecutorImageGuard.cs`; leave a forwarding method in EvidenceFolder. Do **not** move EvidenceFolder's report/manifest/rerun dependencies into the library. Replace test-only `CheckpointTestScope.MarkerName` references in the shared sweep with `TestRootGuard.MarkerName`; shared sweep options/receipt move with the core, while TUnit hooks/roster remain in the test wrapper. LifecycleHost retains its linked wrapper and gains a direct library reference. No new assembly dependency may point from HostCleanup back to the tool or tests.

Introduce instance filesystem/clock/process/native-call dependencies before moving logic. Static compatibility facades may forward to instances, but have no mutable global test override. Add optional instance dependencies to actual callers; preserve existing public call signatures through forwarding overloads. Default production adapters remain native; all selected tests supply virtual instances. No test bypasses the production decision by implementing a second planner in a fake.

| Actual call site | Compatibility detecting methods (class prefix `HostCleanupCheckpointCompatibilityTests` unless stated) |
|---|---|
| `CheckpointApp.CreateRun` -> tool sweep, root registration, ownership write, failed preparation rollback | `next_start_uses_the_shared_veto`; V-4 `Every_cleanup_caller_accepts_virtual_io`, `Start_rollback_retains_unknown_launch_custody`. |
| `CheckpointApp.StartAsync` -> preparation/launch custody and rollback; executor entry -> executor acknowledgement | V-4 `Start_rollback_retains_unknown_launch_custody`, `Executor_ack_is_persisted_before_recovery_can_remove`. NotStarted/pre-launch faults may clean their own virtual allocation; launch-attempted/Unknown must retain it. |
| `WaitCommand.WaitAsync`; `Program` run/wait front doors | `wait_uses_custody_not_progress_pid`, four `completed_wait_removes_only_tool` cases; V-4 `Every_cleanup_caller_accepts_virtual_io` invokes both front doors with fake delay/clock. |
| `Program.Stop` -> identity probe, process control, tool removal | `stop_validates_generation_before_signaling`, `stop_waits_for_confirmed_exit`. Fake control only; daily has no stop route. |
| `EvidenceFolder.Write/TryRemoveToolCopy/SweepFinishedToolCopies` and image guard | `evidence_write_has_no_raw_delete_bypass`, `automatic_recovery_preserves_all_evidence`; V-4 `Evidence_wrapper_and_direct_cleanup_share_image_guard` covers all three wrappers including empty results root. |
| `OutputCleanup.RemoveOlderRuns` and `Program.Clean`; `CleanOwnedOutputs` from Program and CheckpointApp completion | `age_clean_cannot_bypass_live_custody`; V-4 `Every_cleanup_caller_accepts_virtual_io` drives real age and exact-owned-output callers, proving red-output retention, dry-run and non-owned output preservation. These stay lifecycle operations and are never a daily shortcut. |
| `CheckpointTempRootSweep` -> root gate, nested custody, bounded deletion/cursor/index; assembly startup hook | Fifteen named sweep ports below plus V-4 `Preview_does_not_run_startup_sweep`, `Lifetime_hook_and_linked_host_use_shared_core`. |
| `CheckpointTestScope.TempDir/DisposeAsync` -> marker/index before return, scope gate, joined work/children, sweep/register/unregister | Nine scope ports below; dispose's process-control behavior stays in test lifecycle and is not extracted into production cleanup. |
| Recovery/usage/ownership test consumers and LifecycleHost compile links | Public type/namespace compatibility is compiled by CP-2, including existing `CheckpointRecoveryLinuxTests`, `CheckpointRecoveryWindowsTests`, `CheckpointRunOwnershipTests`, `CheckpointTempUsageTests`. Their destructive bodies are excluded from this card's row filters. No claim that compilation executed those bodies. |

**Before extraction:** author the virtual ports against the current implementation with injected dependencies, commit and run CP-2: existing behavior must be green, and new daily adapter requirements must show named assertion-red where absent. Then move implementation, preserve facade calls and rerun the identical row. A move with only an unchanged source-count claim is insufficient.

### Fixtures and deterministic boundaries

All filesystem candidate fixtures are **virtual**, including fixtures spelling real protected paths. No selected test calls File.Delete, Directory.Delete, robocopy, native unlink/delete disposition, process kill, schedule registration, SSH or Docker against the host. Creating output logs/TRX and using the isolated PostgreSQL fixture are test infrastructure; they grant no host filesystem deletion authority. No cleanup test inherits `CheckpointTestBase` or instantiates its default real-temp scope. Set the CP environment `C804_ORPHAN_SWEEP_ROOT=c826-disabled` before loading Antiphon.Tests: the inspected assembly hook treats any non-null value as a skip, and no selected class launches LifecycleHost's orphan mode. Never set it to a live temp path or execute that mode.

| Fixture, exact owner file | Dependencies/fakes and decisive evidence |
|---|---|
| `HostCleanupFixture`, `tests/Antiphon.Tests/Infrastructure/HostCleanupFixture.cs` | `VirtualCleanupFileSystem` models paths, contents, file IDs, mounts, hard links, case rules and crash-persisted bytes. `RecordingCleanupNativeApi` records opens/unlinks/truncates against virtual handles; no OS deletion fallback. `FakeTimeProvider` drives wall and monotonic clocks/timers. `ScriptedProcessIdentityProbe`, `ScriptedOwnerProbe`, `RecordingReadOnlyGitProbe`, `FakeDiskProbe`, `RecordingCleanupStore`, `RecordingScratchClaimStore`, `CleanupBarriers`, `ProtocolStepGuard` expose observations, ordering and counts. |
| `CheckpointVirtualFixture`, new `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointFixture.cs` | Same virtual I/O plus `RecordingProcessControl`, `FakeCheckpointDelay`, virtual v1 marker/index/run/state files and current-image identity. Drives actual shared core and tool/scope/hook callers. Original 40 tests' contracts are ported, not invoked through their physical fixtures. |
| `HostCleanupServerFixture`, new `tests/Antiphon.Tests/Application/HostCleanupFixture.cs` | `TestDbFixture.CreateIsolatedSchemaAsync` returns its own migrated cloned database; fresh service providers simulate restart. Real services/routes, local receipt outbox and serialization; `RecordingRecurringJobManager` implements scheduling calls without Hangfire storage/server; `RecordingWindmillScheduleClient`, `ScriptedHostCleanupTransport`, `RecordingEventBus`, `ThrowingRepositoryMutationLease`, `ForbiddenRetirementProbe`, `ScriptedExistingOwnerStatusProbe`. `HostCleanupTestServer` maps only feature/attention/auth routes on in-process TestServer; no real Program startup. Every query/assertion scoped to test-created board/host/run IDs. |
| `HostCleanupMaintenanceFixture`, same Application fixture file | Two independent service providers/DB contexts and two worker identities use the real HostMaintenanceStore against that clone. `MaintenanceBarriers` pause after epoch commit, claim commit, before native delete and before land lease. Simulates cross-process participants without launching a server or holding a real repository lease. |
| `HostCleanupRunnerFixture`, new `tests/Antiphon.SessionRunner.Tests/HostCleanupFixture.cs` | Real `PhoneHomeCommandDispatcher`, command service and local outbox over `VirtualRunnerDurableStore`; `RecordingPhoneHomeRuntime`, `LoopbackHostCleanupWire` serialize frames/HTTP messages and can drop each reply. All launch/kill/WorkspaceRemove calls record forbidden attempts. Server fixture uses the same wire protocol with its own local fake. |
| `HostCleanupScriptFixture`, new `tests/Antiphon.Tests/Infrastructure/HostCleanupScriptFixture.cs`; `scripts/fixtures/c826-cleanup-shim.ps1` | Actual script entry points in isolated pwsh child/runspace; `RecordingCleanupCommands` intercepts worker, HTTP, SSH, Docker, filesystem deletion, scheduler install/uninstall and restart commands. No credential lookup or external network. A unexpected invocation produces a recorded violation, never falls through to the machine. Uses assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Logs are written outside virtual candidates and retained for the normal artifact owner. |
| `HostCleanupNativeFixture`, same Infrastructure HostCleanupFixture file | Real Linux/Windows adapter code, virtual native syscall implementation and synthetic /proc/Windows identity probes. Linux CP-6 and Windows CP-7 select the actual OS adapter. CP-7 additionally invokes installed Windows PowerShell 5.1 with offline cmdlet shims. No native deletion in either row. |
| Client `item()` in `attentionVisuals.test.ts` | Existing DTO factory, four new exact test titles below. No browser or real API. |

`ThrowingRepositoryMutationLease` overrides **both** TryAcquireAsync overloads, increments counters before throwing, and records requested repository. Assertions inspect counters even if production catches the exception. The full run must actually delete one eligible **virtual** scratch root and inventory eligible/kept/unknown worktrees; skipping the run cannot pass. `Daily_graph_has_no_direct_lease_or_lock_escape` inspects compiled reachable daily types/composition (including error/retry paths) for concrete lease construction, raw `landing.lock` I/O and retirement/remover calls, and the filesystem/wire probes record lock-open/remove/enqueue attempts. It does not scan unrelated legitimate land owners and call their expected lease a daily violation.

`ProtocolStepGuard` completes a violation TaskCompletionSource on a forbidden second owner read or submission, then parks that call until test cancellation. The test awaits **outcome or violation**, immediately asserts the count/forbidden-call list, and cancels/joins the parked operation in finally. This makes PC-21 assertion-red even if an erroneous retry loop catches exceptions. Race tests use named barriers and assert before releasing them; no Task.Delay race, harness deadline or exception thrown by fixture setup is the red verdict. Clock advances run scheduled callbacks explicitly; no frozen clock is passed to a real timer/poll loop.

Each PC fixture satisfies every unrelated precondition and exposes the target boundary's decision or attempt count before a later guard can mask it. For example, the worktree executor records a forbidden worktree dispatch before rejecting its type; maintenance records admission before the final epoch fence; qualification tests assert the per-host qualification verdict before the independent Execute setting. Planned disposition/keep reason is asserted independently of final execution receipts. This is required for a removed guard to fail an assertion even when defense in depth still preserves the virtual file. The no-raw-I/O graph check also runs read-only before any fake-only cleanup invocation, so fixtures never fall through to real filesystem deletion on a miss.

Native syscall ordering, simulated power-loss recovery and concurrent worker identities are deterministic. Actual kernel atomicity across every possible race, disk-free deltas under unrelated load, a live SSH/Windmill configuration, physical reboot/death and sustained real calendar-day operation **cannot be proved by these fake-only rows**. They remain explicit per-OS/per-host operational acceptance in the activation section; do not claim native deletion qualification from CP-6/7. No such destructive qualification is run in TestDesign or Code. A Windows row is limited to seven adapter/PowerShell cases; no desktop stack restart or broad Windows suite is commissioned.

### Delivery inventory

No agent/session message is introduced, so a UserPrompt transcript is not this feature's recipient evidence. The recipient is the configured board attention read model; final receipt means an authenticated attention GET contains the matching run/board plus observed query-invalidation event. Sender ACK, queued frame, outbox insert or persisted report alone is insufficient.

| Path / durable identity | Producer, queue and persistence | Recovery / recipient evidence / detecting test |
|---|---|---|
| Main scheduler -> runner daily command: host + store + local date + run ID + config digest | Real job/service creates run before transport; recording scheduler only registers definitions. Real command dispatcher consumes serialized request; fake wire controls busy/offline/already-ready state. | V-5 catch-up/unsupported/offline; V-8 lost reply and crash-before-plan. Runner complete plan/receipt is joined by run ID before board delivery below; an acceptance frame is not completion. |
| Runner/host receipt -> main ingestion: run ID + digest + store/boot identity | Real local receipt store/outbox, serialized payload and real authenticated ingestion into isolated PostgreSQL. Host bridge uses same receipt contract. No fake queue replaces production enqueue/dequeue/recovery logic. | V-6 busy and ready recipient tests; crash before enqueue, before ingestion commit, after commit/before ACK and before invalidation. After recovery, drain explicitly, GET the report and attention item; execution count remains one. V-8 covers worker crash after filesystem outcome/before receipt commit as uncertain custody, never retry-delete. |
| Main ingestion -> event -> attention GET: board + host/store + run/condition key | Committed report and durable pending-notification state; real AttentionService projection, recording IEventBus subscriber, then TestServer HTTP GET. | V-6 `Crash_before_invalidation_recovers_attention_delivery` observes retry event and consumer result; board A cannot see B's row. Use existing mapped `ScheduleChanged` attention invalidation event unless a dedicated event is explicitly added and tested; no silent new unmapped event. |
| Daily complete reports -> sustained backlog episode: host/store + condition + local observation day | Persist unique observation days and streak/episode state, injected local calendar/clock; restart service with same database. | V-6 day/threshold/missing/incomplete/fresh-recovery cases assert final attention item, observed dates, GiB and named/refusing owner. No retirement submission is part of delivery. |

The wire, event subscriber and virtual durable-file store substitute transport/network/power-loss behavior. They prove production queue decisions and recipient reads, not actual network delivery or filesystem fsync across power failure. API errors injected before ingestion commit also exercise V-6 busy-recipient test; the same durable item remains queued and no attention row is fabricated. No live Hangfire or Windmill schedule is registered.

### Proves it works now

The following is the **frozen proposed roster**, in red-first authoring order within each slice. Every listed method is one `[Test]` execution unless explicitly parameterized below. A method's scenario table/loop is one execution, never an invented Min count. Write fixture plus compiling neutral production seam, commit, show named assertion-red in the exact CP row, then implement and obtain green. Existing compatibility behavior is green before and after the extraction; new safety decisions supply the red. Compilation failure, zero discovered tests, fixture construction error or timeout is not red-first evidence.

#### V-1 — S1, HostCleanupPolicyTests (22 executions)

Fixture: `HostCleanupFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Unknown_family_is_reported_without_delete` | Unknown registered-looking name remains family_unknown. |
| `Runtime_deny_overrides_owned_family` | Every D-3 hard deny, including configured aliases, wins over an allow match; no delete intent. |
| `Parent_containing_runtime_state_is_kept` | A disposable-looking parent containing a denied child is protected. |
| `Child_of_protected_root_is_kept` | A matched descendant of a deny root is protected. |
| `Traversal_cannot_escape_configured_root` | Reject ../, rooted override and sibling-prefix paths before observation/deletion. |
| `Link_at_any_depth_is_kept` | Root, ancestor and descendant link fixtures are Unknown, never followed. |
| `Foreign_mount_is_kept` | An admitted path on a different mount/storage identity is refused. |
| `New_nested_file_keeps_old_directory` | 48-hour folder plus 1-hour hidden nested file is recent_write. |
| `Exactly_twenty_four_hours_is_kept` | newestWrite == cutoff is ineligible; cutoff minus one tick is eligible. |
| `Future_timestamp_is_kept` | A future nested write cannot become old by duration absolute-value arithmetic. |
| `Empty_root_requires_old_valid_marker` | Old bound marker admits empty root; absent/fresh marker does not. |
| `Unreadable_entry_is_not_empty` | Access-denied subtree yields incomplete/Unknown, no estimate of zero. |
| `Traversal_budget_exhaustion_is_unknown` | Candidate/depth/time bounds stop traversal and retain custody. |
| `Newest_write_includes_hidden_files_and_marker` | Hidden evidence and original marker timestamp participate in maximum. |
| `Configuration_cannot_relax_hard_guards` | Negative/zero caps, missing roots, removed denies and blanket globs are rejected. |
| `Unregistered_msbuild_testcontainers_and_tmp_are_unknown` | Name alone never owns global MSBuildTemp, Testcontainers or standalone tmp. |
| `Private_cache_requires_exact_package_and_scratch_ownership` | Keep shared cache/locks; allow only released exact private pair. |
| `Retained_evidence_is_never_scratch` | Reports and deliverables remain protected even under bin-*. |
| `Per_host_roots_are_explicit_and_nonoverlapping` | Desktop, runner and host roots bind storage ID; missing roots never default to /tmp. |
| `All_registered_owned_families_can_be_eligible` | Each of seven D-3 family adapters yields eligible with all authority supplied. |
| `Older_owned_root_is_eligible` | Positive old released scratch candidate reaches executor plan. |
| `Daily_plan_uses_injected_observations_only` | No direct OS I/O or system clock in the planner; reordered facts give same plan. |

#### V-2 — S1, HostCleanupExecutionTests (20 executions)

Fixture: `HostCleanupFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Dry_run_has_zero_mutating_calls` | All filesystem, claim, cursor, budget and store mutation counters stay zero. |
| `Explicit_plan_file_is_the_only_preview_write` | One caller-requested output outside candidates; inside-candidate output refused. |
| `Plan_is_durable_before_first_delete` | Committed complete plan event strictly precedes every destructive intent. |
| `Plan_persist_failure_prevents_delete` | Failed flush/commit gives zero attempts and preserved virtual contents. |
| `Failed_attempt_still_consumes_count_cap` | Ten attempts including partial failures; eleventh is count_cap. |
| `Byte_cap_is_shared_across_families` | Two 6-GiB roots under 10 GiB allow one; reservation survives retry. |
| `Oversized_root_is_not_truncated` | One 11-GiB root under 10 GiB remains byte-identical; no truncate intent. |
| `Unknown_or_overflowed_size_is_kept` | Unknown allocated bytes or checked overflow refuses reservation. |
| `Hardlinks_reserve_conservatively` | Reserve max(logical,allocated) without optimistic deduplication. |
| `Changed_file_identity_refuses_execution` | Replacing a scanned root preserves replacement and reports identity_changed. |
| `Revalidation_catches_new_write` | Write injected after plan causes recent_write and zero delete intents. |
| `Revalidation_catches_new_deny_or_hold` | Deny/hold revision injected after plan preserves candidate. |
| `Revalidation_catches_live_owner_or_revoked_release` | New live status or revoked release before first syscall refuses. |
| `Active_hold_preserves_owned_scratch` | Exact active hold also protects independently marked descendants. |
| `Expired_hold_requires_visible_disposition` | All seven historical prefixes retain unresolved/expired hold evidence and zero deletes. |
| `Partial_failure_keeps_custody_and_budget` | Partial outcome retains marker, attempt and reserved bytes across resume. |
| `Duplicate_request_returns_same_receipt` | Same run/candidate identity repeats immutable receipt and no second delete. |
| `Concurrent_parent_removal_is_zero_reclaimed` | Independent worktree removal returns AlreadyAbsent with zero attributed bytes. |
| `Cursor_is_stable_without_starving_old_roots` | A bounded resumed scan reaches old roots despite newer arrivals. |
| `Eligible_scratch_is_removed_and_measured` | Only eligible virtual bytes disappear; measured receipt differs from reservation. |

#### V-3 — S1, HostCleanupOwnershipTests (12 executions)

Fixture: `HostCleanupFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Unknown_owner_prevents_delete` | Unmarked/unowned root retained, never retrospectively adopted. |
| `Live_owner_prevents_delete` | AliveSame retains sentinel with owner_live. |
| `Reused_pid_prevents_delete` | Same PID/different start generation retains root. |
| `Unknown_liveness_prevents_delete` | Probe failure is Unknown, not Dead. |
| `Foreign_host_boot_or_namespace_prevents_delete` | Each mismatched identity component refuses local-dead inference. |
| `Dead_parent_with_live_executor_is_kept` | Nested live executor veto survives dead starter. |
| `Missing_nested_custody_is_kept` | Run-shaped child missing valid custody blocks whole root. |
| `Evidence_receipt_is_required_for_release` | Settled task without preserved/delivered evidence is retained. |
| `Owner_generation_change_refuses_claim` | Different owner generation cannot reuse old path authority. |
| `Competing_lifecycle_and_daily_calls_share_claim` | Barrier-controlled daily/self-clean/post-land calls yield exactly one mutation sequence. |
| `Slot_binding_blocks_daily_ownership` | Active/idle/quarantined slot stays outside generic scratch authority. |
| `Released_dead_owner_allows_scratch` | Full dead/released/evidence-preserved facts permit candidate. |

#### V-4 — S2, HostCleanupCheckpointAdapterTests (14 executions)

Fixture: `CheckpointVirtualFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `V1_markers_and_nested_executor_records_round_trip` | Existing case/camel-case formats, binding, state and version remain compatible. |
| `Daily_age_is_stricter_than_lifecycle_grace` | One-hour root passes lifecycle grace but daily retains it; 25-hour root qualifies. |
| `Daily_uses_shared_budget_without_lifecycle_truncation` | Daily uses whole-candidate reservation; original lifecycle partial truncation stays isolated. |
| `Original_root_gate_serializes_all_entry_points` | Scope/lifecycle/daily share root gate; busy attempt records retained. |
| `Preview_does_not_run_startup_sweep` | No index migration, marker update, gate creation or cursor write during preview. |
| `Released_task_scratch_and_nested_checkpoint_deduplicate` | Parent and nested root cannot both reserve/reclaim the same bytes. |
| `Current_executor_image_is_retained_after_extraction` | Actual shared image guard keeps current executable; a sibling is eligible. |
| `Unsupported_marker_version_is_retained` | Unknown versions/invalid path/run IDs fail closed. |
| `Lifetime_hook_and_linked_host_use_shared_core` | Resolve real hook/scope wrapper composition with fake dependencies; same shared implementation type. |
| `Shared_library_has_no_test_or_tool_dependency` | Assembly references exclude TUnit, test assembly, checkpoint executable and kill APIs. |
| `Evidence_wrapper_and_direct_cleanup_share_image_guard` | Both actual public entry points use shared guard and identical outcomes. |
| `Start_rollback_retains_unknown_launch_custody` | Actual starter errors with launch-attempted/Unknown keep tool/root and records. |
| `Executor_ack_is_persisted_before_recovery_can_remove` | Lost/failed ack keeps Unknown; confirmed dead ack allows virtual recovery. |
| `Every_cleanup_caller_accepts_virtual_io` | Actual wait/start/stop/evidence/age-clean/scope/hook paths never hit native delete; counters prove each exercised. |

#### V-5 — S3, HostCleanupOrchestrationTests (14 executions)

Fixture: `HostCleanupServerFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Main_only_registers_daily_jobs_and_monitor` | Recording scheduler sees stable daily per-host IDs/offset and one 15-minute monitor; nonmain zero. |
| `Storage_aliases_share_one_job_and_claim_namespace` | Two runner aliases of one store cannot schedule competing ownership. |
| `Retirement_and_claude_schedules_are_unchanged` | CARD-0459 ID/cron/Execute and Claude archival remain byte-identical. |
| `Weekly_cutover_requires_confirmed_disable` | Old build-junk enabled/unknown refuses new execute admission; no unsafe rollback enable. |
| `Unsupported_runner_is_deferred` | Missing capability produces visible Unsupported and zero commands. |
| `Offline_runner_is_deferred` | Unavailable transport produces Deferred, not empty-success inventory. |
| `Host_bridge_excludes_runner_volumes` | Host registry excludes all Docker volume identities; runner owns its configured mounts. |
| `Restart_catchup_runs_once_per_local_day` | Restart/fall-back clock/retry chooses one current daily run, never all missed days. |
| `Task_token_cannot_control_another_owner_or_host` | Preview/status/register/run/hold/maintenance/ingestion authorization remains scoped. |
| `Ambiguous_historical_prefix_remains_held` | Resolve each of seven prefixes uniquely; ambiguous/missing remains held including descendants. |
| `Each_namespace_requires_its_own_dry_run` | A qualified desktop cannot enable runner/host; offline namespace stays pending. |
| `Activation_binds_storage_and_config_digest` | Changed roots/store/config invalidate old dry-run receipt. |
| `Dry_run_success_does_not_enable_execute` | Only explicit operator change after matching qualification permits scratch execute. |
| `Scheduled_preview_persists_report_after_pure_scan` | Scan writes zero; explicit wrapper saves returned document and publishes report. |

#### V-6 — S3, HostCleanupReportTests (20 executions)

Fixture: `HostCleanupServerFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Backlog_opens_on_third_complete_local_day` | At 20 GiB: days one/two closed; day three opens one owner-attributed episode. |
| `Backlog_counts_one_observation_per_local_day` | Repeated runs and DST repeated hour cannot advance streak. |
| `Missing_day_breaks_backlog_streak` | Day gap restarts qualification and raises freshness condition. |
| `Incomplete_inventory_cannot_open_or_clear_backlog` | Unknown/offline/incomplete day cannot count or clear an existing episode. |
| `Fresh_below_threshold_inventory_clears_backlog` | 19 GiB fresh complete sample clears; exact threshold remains open. |
| `Backlog_window_and_threshold_are_configurable` | Injected 2-day/7-GiB configuration changes exact boundary. |
| `Reclaimed_bytes_exclude_worktree_backlog` | Eligible worktree bytes separate from measured scratch removal and fresh post-run sample. |
| `Partial_receipt_is_not_complete_success` | Incomplete traversal/deletion retains explicit outcomes and custody. |
| `Null_sample_never_becomes_zero_usage` | Missing usage/free bytes retain Unknown and sample age. |
| `Budget_pressure_has_one_episode_and_fresh_recovery` | 60% warning, 90% critical, one episode; stale good sample cannot clear. |
| `Post_run_low_space_never_widens_deletion` | max(50 GiB,10%) threshold alerts without extra attempts or reduced age. |
| `Receipt_replay_is_idempotent` | Same run/digest once; same persisted rows and attention identity after reconnect. |
| `Changed_receipt_digest_is_refused` | Same run/different digest conflicts and preserves original receipt. |
| `Receipt_to_attention_reaches_busy_recipient` | Real local outbox pauses delivery, then drains to real ingestion and attention GET containing run ID. |
| `Receipt_to_attention_reaches_ready_recipient` | Real local outbox immediately delivers, attention GET and invalidation use same run/board. |
| `Crash_before_enqueue_recovers_pending_receipt` | Restart store/service between receipt commit and outbox enqueue eventually delivers once. |
| `Crash_after_ingestion_before_ack_queries_status` | Lost acknowledgement queries run status; no repeat execution; one consumer row. |
| `Crash_before_invalidation_recovers_attention_delivery` | Committed report survives event failure; recovery publishes invalidation and fresh GET proves item. |
| `Attention_is_board_scoped_paginated_and_redacted` | Two boards, multi-page report, no foreign root payload/credentials/raw stderr in attention. |
| `Missed_host_and_expired_hold_stay_visible` | No fresh complete run and expired unresolved hold remain actionable across restart. |

#### V-7 — S3, HostCleanupMaintenanceTests (10 executions)

Fixture: `HostCleanupMaintenanceFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Land_intent_prevents_cleanup_admission` | Committed land epoch before admission gives Deferred and zero delete intents. |
| `Restart_intent_prevents_cleanup_admission` | Restart/deploy intent before admission gives Deferred. |
| `Land_waits_only_for_bounded_inflight_delete` | At syscall barrier land lease acquisition count 0; release lets land proceed before next candidate. |
| `Cleanup_yields_to_pending_maintenance` | Pending intent prevents second candidate even while inventory remains. |
| `Stale_epoch_refuses_delete` | Changed epoch between plan/reservation and syscall retains root. |
| `Lost_connection_does_not_expire_live_claim` | Clock past TTL cannot authorize another worker without positive custody reconciliation. |
| `Restart_requires_positive_custody_reconciliation` | Unknown previous worker disables destructive work until confirmed stopped. |
| `Independent_clients_observe_the_same_host_gate` | Two service providers/DB contexts and worker-client identities see one epoch and claim. |
| `Gate_is_released_on_success_and_failure` | Bounded operation releases only with confirmed outcome, exceptions leave visible uncertainty. |
| `Inventory_never_holds_land_admission` | Paused inventory does not own destructive gate; land proceeds without waiting for scan. |

#### V-13 — S3, HostCleanupWorktreeInventoryTests (12 executions)

Fixture: `HostCleanupServerFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Full_run_never_acquires_repository_lease` | Preview/execute/retry/recovery/refusal fixture graph: both lease overload attempts 0 while eligible virtual scratch removed. |
| `Daily_graph_has_no_direct_lease_or_lock_escape` | Reachable daily components have no concrete lease construction, landing.lock open, raw destructive I/O or retirement invocation. |
| `Would_remove_worktree_is_preserved_in_execute_mode` | Tree/source/gitdir/refs/registration snapshots identical; count/bytes/owner present; worktree executor dispatch attempts and reclaimed worktree bytes both 0. |
| `Would_remove_worktree_is_preserved_in_dry_run` | Same inventory with all mutation counters zero. |
| `Unavailable_owner_is_reported_without_retry_or_delete` | One owner-status read; owner_unavailable; no retirement/remove/enqueue; preserve sentinels. |
| `Refusing_owner_is_reported_without_retry_or_delete` | One read, exact bounded refusal code, zero submission/removal; immediate call-count assertion. |
| `Unpushed_commit_is_never_eligible` | Old clean terminal candidate with ahead tip is keep/unpushed. |
| `Dirty_or_active_worktree_is_kept` | Dirty source/untracked source, queued/dispatched/blocked task, live/unknown session, pending land/recovery each veto. |
| `Classifier_preserves_guarded_classes_and_release_requirements` | Real CARD-0665 classifier; Protected wins over Evidence/Disposable; pushed nonmaster needs exact release/evidence. |
| `Registered_and_unregistered_candidates_are_fully_accounted` | Paged registered/mirror/orphan/detached inventory complete or explicit Unknown; slots never scratch. |
| `Read_only_git_never_fetches_or_mutates_refs` | GIT_OPTIONAL_LOCKS=0; missing objects/network/stale observation is Unknown, forbidden Git calls 0. |
| `Only_owned_scratch_child_is_deleted` | Old released child removed virtually; held/unreleased children and all worktree metadata preserved. |

#### V-8 — S4, HostCleanupCommandTests (12 executions)

Fixture: `HostCleanupRunnerFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Missing_capability_refuses_cleanup` | Dispatch fails Unsupported before any root observation. |
| `Request_cannot_supply_arbitrary_roots` | Only configured host ID; path injection rejected. |
| `Foreign_host_or_store_is_refused` | Authenticated command bound to wrong host/store does nothing. |
| `Changed_owner_generation_is_refused` | Expected generation mismatch cannot resume/delete. |
| `Dry_run_dispatch_has_zero_writes` | Both HTTP and phone-home paths return same pure inventory. |
| `Lost_reply_returns_existing_receipt` | Actual dispatcher/client response loss returns same run/receipt; one delete sequence. |
| `Restart_replays_report_without_redeleting` | Recreate runner service and drain real outbox over virtual durable store; no second execution. |
| `Cleanup_never_launches_or_stops_a_session` | Recording runtime start/kill/release/input calls all zero on success/failure/retry. |
| `Desktop_and_phone_home_share_command_service` | Serialized equivalent requests use same service and identical outcomes. |
| `Busy_upload_queue_retains_order_and_identity` | Busy recipient keeps durable outbox entry; ready replay preserves run/digest. |
| `Receipt_commit_failure_retains_claim_without_reexecution` | Crash before receipt commit leaves uncertain claim; reconciliation cannot assume success. |
| `Command_crash_before_plan_commit_deletes_nothing` | Worker restart before durable plan recovers a retained candidate without mutation. |

#### V-9 — S5, HostCleanupScriptTests (12 executions)

Fixture: `HostCleanupScriptFixture`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Daemon_and_autostart_scripts_are_ascii` | Every byte of scoped daemon/autostart/wrapper scripts <128; enumerate exact paths. |
| `Scratch_registration_refuses_foreign_owner_and_late_adoption` | Foreign owner or used unregistered root denied; no native call. |
| `Scratch_release_requires_joined_children_and_preserved_evidence` | Pending children/evidence debt prevent clean; exact owner only. |
| `Legacy_build_junk_wrapper_uses_shared_worker_only` | Actual wrapper forwards host/dry-run and returns receipt; robocopy/Remove-Item/native-delete calls 0. |
| `Installer_refuses_registration_but_allows_explicit_uninstall` | Default exits refusal with Hangfire/Windmill message; scheduled-task creation counters 0. |
| `Windmill_bridge_uses_host_roots_and_no_docker_socket` | Offline SSH command passes only host config ID; volumes excluded; no schedule import. |
| `Smoke_reports_separate_tmp_and_host_disk_identity` | Exact sample identities, age/completeness and image SHA; container free never relabelled host. |
| `Status_or_smoke_never_redeploys_or_registers_schedule` | Status/dry-run/smoke leave deploy/scheduler counters zero. |
| `All_restart_entry_points_publish_and_drain_maintenance` | Actual AppHost/runner/deploy wrappers publish epoch before stop/restart; failure does not clear uncertainty. |
| `Helper_preserves_literal_paths_and_exit_codes` | Spaces, brackets, apostrophes and backslash Windows roots bind as one exact argument. |
| `Weekly_schedule_cutover_is_single_owner_and_reversible_to_dry_run` | Offline cutover receipt proves old disabled before new enable; failure leaves new dry-run. |
| `Protected_payloads_never_reach_script_output` | Synthetic secret sentinel in error payload is absent from stdout/stderr/receipt summary. |

#### V-10 — S1, HostCleanupLinuxTests (6 executions)

Fixture: `HostCleanupNativeFixture(Linux)`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Swapped_symlink_preserves_outside_sentinel` | Real Linux adapter with recording openat/unlinkat seam rejects substituted link; virtual outside bytes unchanged. |
| `Mount_crossing_refuses_native_delete` | Device/mount identity change prevents unlink call. |
| `Ancestor_handles_stay_bound_after_rename` | Recorded deletion uses held parent handle/file ID, not rebuilt absolute path. |
| `Permission_failure_retains_marker_and_reports_partial` | Injected EACCES yields Partial and retained custody. |
| `Proc_identity_namespace_mismatch_is_unknown` | Synthetic /proc boot/start/ns data cannot prove foreign PID dead. |
| `Long_path_and_hardlink_budget_use_native_identity` | Recorded relative segments and conservative allocated bytes preserve confinement. |

#### V-11 — S1/S5, HostCleanupWindowsTests (7 executions)

Fixture: `HostCleanupNativeFixture(Windows)`. Exact method names, in authoring order:

| Method | Decisive expected outcome |
|---|---|
| `Swapped_junction_preserves_outside_sentinel` | Real Windows adapter requests reparse-safe opens; injected junction swap cannot delete virtual outside bytes. |
| `Case_and_path_aliases_cannot_escape_root` | Drive-relative, UNC/device, alternate stream, trailing dot/space and sibling-prefix escapes refused. |
| `Held_file_returns_partial_and_retains_marker` | Injected sharing violation yields Partial with immutable custody/budget. |
| `File_id_substitution_refuses_delete` | Same path/different volume/file ID refuses delete disposition. |
| `Long_path_uses_bound_handles` | Long path remains bounded; correct handle-relative flags recorded. |
| `Windows_boot_and_start_uncertainty_is_retained` | Boot tolerance never masks start mismatch; inaccessible identity is Unknown. |
| `Windows_powershell_51_parses_and_runs_wrappers_offline` | Only changed Windows wrappers, real powershell.exe 5.1 plus recording cmdlets; zero native delete/restart/register. |

#### R-1 compatibility — S2, HostCleanupCheckpointCompatibilityTests (40 executions)

New file `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs`, fixture `CheckpointVirtualFixture`. Preserve the named source contract and its assertions while replacing physical I/O/processes with injected instances. The old physical classes remain unchanged and are not selected. Before moving code, run these ports against the pre-move implementations; tests must invoke actual public callers listed above. Count census at the frozen source is 15 + 9 + 16 = 40 executions (37 methods, no OS/Explicit skips). New class has exactly the following 37 methods/40 executions:

| Source class / exact method in new class | Executions |
|---|---:|
| CheckpointTempRootSweepTests / `two_hundred_concurrent_allocations_ignore_the_sweep_gate` | 1 |
| CheckpointTempRootSweepTests / `grace_is_additional_to_dead_ownership` | 1 |
| CheckpointTempRootSweepTests / `sweep_interval_is_shared_across_hosts` | 1 |
| CheckpointTempRootSweepTests / `cursor_write_failure_does_not_fail_the_sweep` | 1 |
| CheckpointTempRootSweepTests / `sweep_removes_only_stale_index_temp_files` | 1 |
| CheckpointTempRootSweepTests / `root_delete_count_is_bounded` | 1 |
| CheckpointTempRootSweepTests / `actual_deleted_bytes_are_bounded` | 1 |
| CheckpointTempRootSweepTests / `direct_entry_scan_is_bounded` | 1 |
| CheckpointTempRootSweepTests / `descendant_scan_is_bounded` | 1 |
| CheckpointTempRootSweepTests / `monotonic_deadline_stops_new_operations` | 1 |
| CheckpointTempRootSweepTests / `resumed_deletion_rechecks_every_veto` | 1 |
| CheckpointTempRootSweepTests / `marker_survives_partial_deletion` | 1 |
| CheckpointTempRootSweepTests / `cursor_prevents_new_arrival_starvation` | 1 |
| CheckpointTempRootSweepTests / `disposed_roots_do_not_consume_the_next_sweep_budget` | 1 |
| CheckpointTempRootSweepTests / `eligible_backlog_drains_without_touching_exclusions` | 1 |
| CheckpointTempScopeTests / `allocation_requires_an_owner` | 1 |
| CheckpointTempScopeTests / `root_is_registered_before_return` | 1 |
| CheckpointTempScopeTests / `sealed_scope_awaits_registered_work` | 1 |
| CheckpointTempScopeTests / `live_child_prevents_scope_deletion` | 1 |
| CheckpointTempScopeTests / `canceled_test_gets_a_fresh_cleanup_token` | 1 |
| CheckpointTempScopeTests / `teardown_preserves_failure_and_attempts_other_roots` | 1 |
| CheckpointTempScopeTests / `marker_failure_rolls_back_empty_root` | 1 |
| CheckpointTempScopeTests / `parallel_scopes_have_distinct_attempt_ownership` | 1 |
| CheckpointTempScopeTests / `owned_disposal_does_not_require_dead_test_host` | 1 |
| CheckpointToolCopyCleanupTests / `nested_live_executor_vetoes_whole_root` | 1 |
| CheckpointToolCopyCleanupTests / `unknown_nested_custody_vetoes_whole_root` | 1 |
| CheckpointToolCopyCleanupTests / `wait_uses_custody_not_progress_pid` | 1 |
| CheckpointToolCopyCleanupTests / `next_start_uses_the_shared_veto` | 1 |
| CheckpointToolCopyCleanupTests / `stop_validates_generation_before_signaling` | 1 |
| CheckpointToolCopyCleanupTests / `stop_waits_for_confirmed_exit` | 1 |
| CheckpointToolCopyCleanupTests / `age_clean_cannot_bypass_live_custody` | 1 |
| CheckpointToolCopyCleanupTests / `evidence_write_has_no_raw_delete_bypass` | 1 |
| CheckpointToolCopyCleanupTests / `automatic_recovery_preserves_all_evidence` | 1 |
| CheckpointToolCopyCleanupTests / `sweep_never_signals_a_process` | 1 |
| CheckpointToolCopyCleanupTests / `failed_delete_receipt_is_truthful_and_retryable` | 1 |
| CheckpointToolCopyCleanupTests / `completed_wait_removes_only_tool` | 4 |
| CheckpointToolCopyCleanupTests / `already_absent_is_idempotent` | 1 |

`completed_wait_removes_only_tool` keeps exactly `[Arguments(0,false)]`, `[Arguments(0,true)]`, `[Arguments(1,false)]`, `[Arguments(1,true)]`. All other methods are single tests. Existing lifecycle partial truncation in `actual_deleted_bytes_are_bounded` is preserved **only in lifecycle mode**; V-2/V-4 prove the daily mode never truncates an oversized root. The allocation/gate case uses deterministic virtual gate observations instead of its old 20-second timing contest. No existing physical tests are relabelled as virtual evidence.

#### V-12 — S5, attentionVisuals.test.ts (26 Vitest executions)

Source census: 16 plain `it` cases plus one `it.each` with six rows = 22. Preserve all 22 and add these four exact titles, one execution each:

| Title | Assertion |
|---|---|
| `links a host cleanup summary to its run` | Target contains the exact run ID; label and group are nonempty. |
| `shows cleanup disk pressure at the reported severity` | Warning/critical DTOs map to their expected bucket and severity. |
| `keeps an expired cleanup hold visible for review` | Review bucket and expiry evidence remain, with no auto-clear action. |
| `shows sustained worktree backlog with its responsible owner` | Target/run and owner/refusal evidence are retained with a backlog label. |

### Guards the regression

| ID | Regression and named detecting boundary |
|---|---|
| R-1 | Shared checkpoint extraction loses custody/marker/evidence/current-image semantics or leaves a direct deletion bypass: 40 compatibility ports plus V-4's actual caller tests. |
| R-2 | Daily job obtains repository authority, mutates worktrees/Git or relaxes safety under Execute: V-13 mixed full runs, graph boundary, V-1/V-2/V-3 and V-7. |
| R-3 | A second scheduler/deleter appears, old worktree owners change, or unsafe weekly deletion survives: V-5 schedule snapshots plus V-9 actual offline wrappers; V-3 shared candidate claim. |
| R-4 | Reports stop at enqueue/ACK, replay triggers deletion, offline means zero usage, or backlog is hidden: V-6 recipient GET/recovery/clock assertions, V-8 actual dispatcher/outbox and V-12 visuals. |
| R-5 | Native adapter opens/deletes through path substitution or bypasses host root identity: V-10/V-11 recorded syscall assertions; kernel qualification remains operational, not claimed by fakes. |

### Guard inventory

Each row below is one independently reviewed guard with one distinct prescribed positive control. A matrix test can detect several guards, but a PC ID is never shared by two guard rows. PC-19 has two explicitly enumerated overload variants; all other PCs prescribe one defect. Extra discovered independently bypassable guards require explicit roster revision, not an exemption.

| Guard | Plan boundary / invariant | Positive control |
|---|---|---|
| G-1 | D-3: Explicit family admission | PC-1 |
| G-2 | D-3: Hard deny priority | PC-2 |
| G-3 | D-3: Protected descendant veto | PC-3 |
| G-4 | D-4: Recursive newest-write age | PC-4 |
| G-5 | D-4: Strict 24-hour boundary | PC-5 |
| G-6 | D-4: Live owner veto | PC-6 |
| G-7 | D-4: Nested executor custody | PC-7 |
| G-8 | D-2: Unpushed HEAD veto | PC-8 |
| G-9 | D-5: Pure manual preview | PC-9 |
| G-10 | D-5: Attempt-count cap | PC-10 |
| G-11 | D-5: Shared byte cap | PC-11 |
| G-12 | D-5: Expiry is review, not permission | PC-12 |
| G-13 | D-5: Active hold veto | PC-13 |
| G-14 | D-4: Identity recheck | PC-14 |
| G-15 | D-4: Land intent exclusion | PC-15 |
| G-16 | D-4/D-7: Response-loss deduplication | PC-16 |
| G-17 | D-3/D-4: Linux no-follow deletion | PC-17 |
| G-18 | D-3/D-4: Windows no-follow deletion | PC-18 |
| G-19 | D-2: Zero repository-lease attempts | PC-19 |
| G-20 | D-2: Unavailable retirement owner is inventory-only | PC-20 |
| G-21 | D-2: Owner refusal is not retried | PC-21 |
| G-22 | D-2: Execute never deletes worktrees | PC-22 |
| G-23 | D-3: Protected ancestor veto | PC-23 |
| G-24 | D-3: Lexical per-root confinement | PC-24 |
| G-25 | D-3: Links denied during inventory | PC-25 |
| G-26 | D-3: Mount identity confinement | PC-26 |
| G-27 | D-4: Future timestamps fail closed | PC-27 |
| G-28 | D-4: Empty-root ownership age | PC-28 |
| G-29 | D-3/D-4: Unreadable scan is Unknown | PC-29 |
| G-30 | D-5: Scan budget is not full inventory | PC-30 |
| G-31 | D-4: Hidden writes count | PC-31 |
| G-32 | D-3/D-5: Configuration cannot bypass safety | PC-32 |
| G-33 | D-3: Shared package cache protected | PC-33 |
| G-34 | D-3: Retained evidence protected | PC-34 |
| G-35 | D-3: Explicit per-host roots | PC-35 |
| G-36 | D-5: Requested plan output outside candidates | PC-36 |
| G-37 | D-5: Durable plan precedes mutation | PC-37 |
| G-38 | D-5: Plan persistence failure veto | PC-38 |
| G-39 | D-5: Whole-candidate reservation | PC-39 |
| G-40 | D-5: Unknown/overflow size veto | PC-40 |
| G-41 | D-5: Conservative hard-link accounting | PC-41 |
| G-42 | D-4: Fresh write revalidation | PC-42 |
| G-43 | D-4: Deny and hold revision revalidation | PC-43 |
| G-44 | D-4: Release/liveness revalidation | PC-44 |
| G-45 | D-4/D-5: Partial cleanup retains custody/budget | PC-45 |
| G-46 | D-4: No credit for other owner's removal | PC-46 |
| G-47 | D-4: Unknown ownership veto | PC-47 |
| G-48 | D-4: PID generation veto | PC-48 |
| G-49 | D-4: Unknown liveness veto | PC-49 |
| G-50 | D-4: Host/boot/PID namespace binding | PC-50 |
| G-51 | D-4: Nested missing custody veto | PC-51 |
| G-52 | D-4/D-6: Evidence preserved before release | PC-52 |
| G-53 | D-4: Owner generation binding | PC-53 |
| G-54 | D-4: Single scratch executor | PC-54 |
| G-55 | D-2/D-6: Slot boundary | PC-55 |
| G-56 | D-4/S2: Daily gate distinct from lifecycle | PC-56 |
| G-57 | S2: Checkpoint root rendezvous preserved | PC-57 |
| G-58 | S2: Production extraction dependency closure | PC-58 |
| G-59 | S2: Current executable image protected | PC-59 |
| G-60 | S2: Version/path-bound marker | PC-60 |
| G-61 | S2: Unknown launch custody survives rollback | PC-61 |
| G-62 | S2: Executor acknowledgement boundary | PC-62 |
| G-63 | D-1: One scheduler per store | PC-63 |
| G-64 | D-1: Main scheduler ownership | PC-64 |
| G-65 | D-1: Existing retirement/Claude schedule preservation | PC-65 |
| G-66 | D-1: No simultaneous weekly deleter | PC-66 |
| G-67 | D-1: Host versus volume separation | PC-67 |
| G-68 | D-1: One daily catch-up | PC-68 |
| G-69 | D-5/D-7: Scoped operator/task authorization | PC-69 |
| G-70 | D-5: Historical holds resolve uniquely | PC-70 |
| G-71 | Activation 4: Each host dry-run prerequisite | PC-71 |
| G-72 | Activation 4: Activation binds current config/store | PC-72 |
| G-73 | Activation 6: Dry-run never auto-enables | PC-73 |
| G-74 | D-7: Sustained backlog threshold/window | PC-74 |
| G-75 | D-7: One backlog observation per day | PC-75 |
| G-76 | D-7: Backlog continuity | PC-76 |
| G-77 | D-7: Incomplete is not recovery | PC-77 |
| G-78 | D-7: Measured bytes truthful | PC-78 |
| G-79 | D-7: Unknown disk sample not zero | PC-79 |
| G-80 | D-7: Fresh-only pressure recovery | PC-80 |
| G-81 | D-7: Pressure cannot authorize extra deletion | PC-81 |
| G-82 | D-7: Receipt integrity | PC-82 |
| G-83 | D-7 delivery: Receipt commit to outbox recovery | PC-83 |
| G-84 | D-7 delivery: Ingestion to acknowledgement recovery | PC-84 |
| G-85 | D-7 delivery: Projection to invalidation recovery | PC-85 |
| G-86 | D-7: Board scope and bounded redaction | PC-86 |
| G-87 | D-4: Restart/deploy intent exclusion | PC-87 |
| G-88 | D-4: Maintenance drains before land lease | PC-88 |
| G-89 | D-4: Pending maintenance yields after current operation | PC-89 |
| G-90 | D-4: Epoch checked at deletion | PC-90 |
| G-91 | D-4: No TTL release of unknown worker | PC-91 |
| G-92 | D-4: Restart custody reconciliation | PC-92 |
| G-93 | D-2: No direct concrete-lease/lock bypass | PC-93 |
| G-94 | D-2: Dirty/active worktree safety | PC-94 |
| G-95 | D-2: Release and content classification | PC-95 |
| G-96 | D-2: Read-only Git commands | PC-96 |
| G-97 | D-1/S4: Capability refusal | PC-97 |
| G-98 | D-3/S4: No request-selected roots | PC-98 |
| G-99 | D-4/S4: Authenticated store identity | PC-99 |
| G-100 | D-2/S4: No process lifecycle side effects | PC-100 |
| G-101 | D-7 delivery: Uncertain receipt retains execution claim | PC-101 |
| G-102 | AGENTS ASCII: Windows fallback ASCII | PC-102 |
| G-103 | D-6: Register before use and exact owner | PC-103 |
| G-104 | D-6: Self-clean joins work and preserves evidence | PC-104 |
| G-105 | D-1/S5: Weekly compatibility wrapper has no remover | PC-105 |
| G-106 | D-1/S5: No local schedule install | PC-106 |
| G-107 | D-4/S5: Every maintenance entry point participates | PC-107 |
| G-108 | D-7: Output redaction | PC-108 |
| G-109 | S2: Tool evidence never swept as payload | PC-109 |
| G-110 | S2: Lifecycle partial keeps marker | PC-110 |
| G-111 | S2: Lifecycle limits retained | PC-111 |
| G-112 | S2: Scope awaits owned work | PC-112 |
| G-113 | S2: Current scope owner bound before use | PC-113 |
| G-114 | S2: Scope never deletes with uncertain child | PC-114 |
| G-115 | S2: Lifecycle disposal preserves original failure | PC-115 |
| G-116 | S2: Tool stop validates generation | PC-116 |
| G-117 | D-3/native: Windows path aliases denied | PC-117 |
| G-118 | D-4/native: Windows file identity revalidation | PC-118 |

### Positive controls

SourceLanding Mutation only, after ordinary Code, independent Review and confirmed publication. Every defect below must compile; mutate production policy/dispatch/adapter behavior (PC-102 deliberately mutates a script byte), never the detecting assertion or fixture. Run green, break, show the exact named assertion-red, restore source and freshness/rebuild, then show green. No harness timeout, missing platform, build failure, fixture exception or zero-test result counts. Frozen design audit: **guards=118, mapped=118, missing=0, duplicate PC maps=0; 119 prescribed variants**. This is executability by source/seam design, not a claim of executed mutants.

| PC | Compiling defect | Named detecting test | Required failing assertion |
|---|---|---|---|
| PC-1 | Unknown family falls through as disposable. | `HostCleanupPolicyTests.Unknown_family_is_reported_without_delete` | Reason == family_unknown; delete intents == 0. |
| PC-2 | Allow match returns before hard-deny check. | `HostCleanupPolicyTests.Runtime_deny_overrides_owned_family` | protected_runtime for each deny fixture; sentinel hashes unchanged. |
| PC-3 | Remove parent-contains-denied-child check. | `HostCleanupPolicyTests.Parent_containing_runtime_state_is_kept` | parent retained; nested runtime sentinel unchanged. |
| PC-4 | Read root mtime instead of newest regular file. | `HostCleanupPolicyTests.New_nested_file_keeps_old_directory` | Reason == recent_write; eligible count == 0. |
| PC-5 | Change < cutoff to <= cutoff. | `HostCleanupPolicyTests.Exactly_twenty_four_hours_is_kept` | equal cutoff kept; minus-one-tick eligible. |
| PC-6 | Map AliveSame to Dead. | `HostCleanupOwnershipTests.Live_owner_prevents_delete` | Reason == owner_live; delete intents == 0. |
| PC-7 | Skip nested executor observation after dead starter. | `HostCleanupOwnershipTests.Dead_parent_with_live_executor_is_kept` | executor_live and sentinel unchanged. |
| PC-8 | Treat ahead local tip as pushed. | `HostCleanupWorktreeInventoryTests.Unpushed_commit_is_never_eligible` | Disposition == keep; Reason == unpushed. |
| PC-9 | Persist preview cursor. | `HostCleanupExecutionTests.Dry_run_has_zero_mutating_calls` | sum of all mutation counters == 0. |
| PC-10 | Do not charge failed attempt. | `HostCleanupExecutionTests.Failed_attempt_still_consumes_count_cap` | attempts == 10; eleventh reason == count_cap. |
| PC-11 | Reset allowance per family. | `HostCleanupExecutionTests.Byte_cap_is_shared_across_families` | one 6-GiB candidate attempted; remaining 4 GiB across retry. |
| PC-12 | Drop expired holds. | `HostCleanupExecutionTests.Expired_hold_requires_visible_disposition` | seven holds remain; hold_expired_review_required; deletes == 0. |
| PC-13 | Ignore matching hold. | `HostCleanupExecutionTests.Active_hold_preserves_owned_scratch` | exact hold ID and descendant retained. |
| PC-14 | Use scan identity without re-reading before syscall. | `HostCleanupExecutionTests.Changed_file_identity_refuses_execution` | identity_changed; replacement hash unchanged. |
| PC-15 | Admit cleanup with published land epoch. | `HostCleanupMaintenanceTests.Land_intent_prevents_cleanup_admission` | admission == Deferred; cleanup-entry attempts == 0 while intent active. |
| PC-16 | Allocate a new candidate claim on duplicate command. | `HostCleanupCommandTests.Lost_reply_returns_existing_receipt` | one mutation sequence; same receipt ID. |
| PC-17 | Follow substituted symlink at native seam. | `HostCleanupLinuxTests.Swapped_symlink_preserves_outside_sentinel` | outside virtual sentinel unchanged; unlink-through-link count == 0. |
| PC-18 | Omit reparse-safe open flag. | `HostCleanupWindowsTests.Swapped_junction_preserves_outside_sentinel` | required flag present; outside virtual sentinel unchanged. |
| PC-19 | Insert tagged TryAcquireAsync in daily inventory; untagged overload is a separately replayed variant. | `HostCleanupWorktreeInventoryTests.Full_run_never_acquires_repository_lease` | both acquisition counters == 0 after completed mixed run; scratch removal count == 1. |
| PC-20 | Fallback to remove worktree when owner unavailable. | `HostCleanupWorktreeInventoryTests.Unavailable_owner_is_reported_without_retry_or_delete` | owner_unavailable; remove/enqueue == 0; sentinels unchanged. |
| PC-21 | Read owner a second time after refusal. | `HostCleanupWorktreeInventoryTests.Refusing_owner_is_reported_without_retry_or_delete` | owner probe calls == 1; submissions == 0. |
| PC-22 | Route would-remove worktree into scratch executor. | `HostCleanupWorktreeInventoryTests.Would_remove_worktree_is_preserved_in_execute_mode` | worktree executor dispatch attempts == 0 before downstream rejection; metadata hashes unchanged; worktree delete/reclaimed == 0. |
| PC-23 | Skip protected-ancestor test. | `HostCleanupPolicyTests.Child_of_protected_root_is_kept` | protected ancestor reason; deletes == 0. |
| PC-24 | Accept ../ or sibling-prefix escape. | `HostCleanupPolicyTests.Traversal_cannot_escape_configured_root` | outside root never observed/deleted; reason == path_escape. |
| PC-25 | Ignore descendant link attribute. | `HostCleanupPolicyTests.Link_at_any_depth_is_kept` | linked reason and zero followed-link observations. |
| PC-26 | Accept foreign mount with same lexical prefix. | `HostCleanupPolicyTests.Foreign_mount_is_kept` | mount_crossing; delete intents == 0. |
| PC-27 | Use absolute age duration. | `HostCleanupPolicyTests.Future_timestamp_is_kept` | future write retained. |
| PC-28 | Treat empty unmarked root as old. | `HostCleanupPolicyTests.Empty_root_requires_old_valid_marker` | unmarked/fresh roots retained; old valid marker qualifies. |
| PC-29 | Convert access-denied to empty enumeration. | `HostCleanupPolicyTests.Unreadable_entry_is_not_empty` | Complete == false; Unknown; no deletion. |
| PC-30 | Mark budget-exhausted traversal complete. | `HostCleanupPolicyTests.Traversal_budget_exhaustion_is_unknown` | Unknown/incomplete; descendants visited bounded. |
| PC-31 | Exclude hidden files/marker from newest-write maximum. | `HostCleanupPolicyTests.Newest_write_includes_hidden_files_and_marker` | new hidden/marker timestamp retains root. |
| PC-32 | Allow config to remove a runtime deny. | `HostCleanupPolicyTests.Configuration_cannot_relax_hard_guards` | configuration validation refuses unsafe registry. |
| PC-33 | Treat registered parent as ownership of shared cache. | `HostCleanupPolicyTests.Private_cache_requires_exact_package_and_scratch_ownership` | shared-cache/locks unchanged; only exact private pair eligible. |
| PC-34 | Treat deliverable under bin-* as disposable. | `HostCleanupPolicyTests.Retained_evidence_is_never_scratch` | protected evidence hash unchanged. |
| PC-35 | Default missing root to OS temp. | `HostCleanupPolicyTests.Per_host_roots_are_explicit_and_nonoverlapping` | missing/overlapping registry rejected before scan. |
| PC-36 | Allow PlanFile inside a candidate. | `HostCleanupExecutionTests.Explicit_plan_file_is_the_only_preview_write` | inside output rejected; zero writes. |
| PC-37 | Start executor before plan commit completes. | `HostCleanupExecutionTests.Plan_is_durable_before_first_delete` | ordered event log: plan-commit before first delete. |
| PC-38 | Catch commit failure and continue deleting. | `HostCleanupExecutionTests.Plan_persist_failure_prevents_delete` | attempts == 0; candidate contents unchanged. |
| PC-39 | Truncate oversized root to fit budget. | `HostCleanupExecutionTests.Oversized_root_is_not_truncated` | truncate count == 0; exact original payload size/hash. |
| PC-40 | Use zero on allocation-size failure/overflow. | `HostCleanupExecutionTests.Unknown_or_overflowed_size_is_kept` | no reservation or delete for unknown/overflow candidate. |
| PC-41 | Reserve smaller logical total after optimistic deduplication. | `HostCleanupExecutionTests.Hardlinks_reserve_conservatively` | reserved == conservative max; over-budget candidate retained. |
| PC-42 | Skip newest-write recheck. | `HostCleanupExecutionTests.Revalidation_catches_new_write` | recent_write; zero delete intents. |
| PC-43 | Reuse old hold/deny revision. | `HostCleanupExecutionTests.Revalidation_catches_new_deny_or_hold` | new protection reason; unchanged candidate. |
| PC-44 | Reuse old release/live observation. | `HostCleanupExecutionTests.Revalidation_catches_live_owner_or_revoked_release` | fresh veto reason; zero delete intents. |
| PC-45 | Clear claim/marker after first failed unlink. | `HostCleanupExecutionTests.Partial_failure_keeps_custody_and_budget` | Partial, marker present, previous attempt/reservation unchanged after resume. |
| PC-46 | Credit absent worktree parent bytes to daily cleanup. | `HostCleanupExecutionTests.Concurrent_parent_removal_is_zero_reclaimed` | AlreadyAbsent and reclaimed == 0; no recreation. |
| PC-47 | Create owner record retrospectively for unknown old root. | `HostCleanupOwnershipTests.Unknown_owner_prevents_delete` | owner_unknown; no owner writes or deletion. |
| PC-48 | Ignore process-start mismatch. | `HostCleanupOwnershipTests.Reused_pid_prevents_delete` | reused identity retained; signals == 0. |
| PC-49 | Treat probe error as death. | `HostCleanupOwnershipTests.Unknown_liveness_prevents_delete` | Unknown retained; delete intents == 0. |
| PC-50 | Remove boot identity comparison. | `HostCleanupOwnershipTests.Foreign_host_boot_or_namespace_prevents_delete` | foreign identity retained; local-dead verdict absent. |
| PC-51 | Treat run-shaped directory without ownership.json as dead. | `HostCleanupOwnershipTests.Missing_nested_custody_is_kept` | nested_custody_missing; root unchanged. |
| PC-52 | Authorize cleanup with missing receipt. | `HostCleanupOwnershipTests.Evidence_receipt_is_required_for_release` | evidence_required; delete intents == 0. |
| PC-53 | Key claim by path alone. | `HostCleanupOwnershipTests.Owner_generation_change_refuses_claim` | generation_changed; new generation sentinel unchanged. |
| PC-54 | Give daily and lifecycle different claim keys. | `HostCleanupOwnershipTests.Competing_lifecycle_and_daily_calls_share_claim` | one claim, one destructive sequence, identical completion receipt. |
| PC-55 | Treat idle slot contents as generic scratch. | `HostCleanupOwnershipTests.Slot_binding_blocks_daily_ownership` | slot_owned; all three slot-state fixtures retained. |
| PC-56 | Daily calls SweepOnce without 24-hour age. | `HostCleanupCheckpointAdapterTests.Daily_age_is_stricter_than_lifecycle_grace` | one-hour root kept daily; lifecycle still permitted. |
| PC-57 | Daily uses a different root gate name. | `HostCleanupCheckpointAdapterTests.Original_root_gate_serializes_all_entry_points` | busy root retained; no concurrent delete intents. |
| PC-58 | Add a TUnit package reference and an otherwise harmless typeof(TUnit.Core.TestAttribute) use to the shared library; this compiles without a tool/library reference cycle. | `HostCleanupCheckpointAdapterTests.Shared_library_has_no_test_or_tool_dependency` | forbidden assembly references empty. |
| PC-59 | Return false from shared image guard. | `HostCleanupCheckpointAdapterTests.Current_executor_image_is_retained_after_extraction` | current image retained; sibling positive control removed virtually. |
| PC-60 | Accept unsupported marker version. | `HostCleanupCheckpointAdapterTests.Unsupported_marker_version_is_retained` | invalid marker retained, no mutation. |
| PC-61 | Delete start root on Unknown launch result. | `HostCleanupCheckpointAdapterTests.Start_rollback_retains_unknown_launch_custody` | tool and ownership records still present. |
| PC-62 | Infer dead executor from phase=done without ack. | `HostCleanupCheckpointAdapterTests.Executor_ack_is_persisted_before_recovery_can_remove` | Unknown before ack; no removal until proven dead identity. |
| PC-63 | Include alias host ID in deletion namespace key. | `HostCleanupOrchestrationTests.Storage_aliases_share_one_job_and_claim_namespace` | one job/store and one shared claim. |
| PC-64 | Register on secondary instance. | `HostCleanupOrchestrationTests.Main_only_registers_daily_jobs_and_monitor` | secondary registration count == 0. |
| PC-65 | Overwrite residue Execute or cron during daily registration. | `HostCleanupOrchestrationTests.Retirement_and_claude_schedules_are_unchanged` | old job/config snapshots byte-identical. |
| PC-66 | Admit execute while weekly disable is unconfirmed. | `HostCleanupOrchestrationTests.Weekly_cutover_requires_confirmed_disable` | execute refused until confirmed disabled. |
| PC-67 | Include runner-tmp volume in host-only registry. | `HostCleanupOrchestrationTests.Host_bridge_excludes_runner_volumes` | host plan contains no runner volume identities. |
| PC-68 | Schedule every missed day on restart. | `HostCleanupOrchestrationTests.Restart_catchup_runs_once_per_local_day` | one catch-up creation intent and dispatch per host; one current-day run. |
| PC-69 | Authorize foreign-host execute with task token. | `HostCleanupOrchestrationTests.Task_token_cannot_control_another_owner_or_host` | 403/scoped refusal; foreign rows/files unchanged. |
| PC-70 | Select first task matching ambiguous prefix. | `HostCleanupOrchestrationTests.Ambiguous_historical_prefix_remains_held` | unresolved hold plus descendants retained for each prefix. |
| PC-71 | Use any host's successful preview as qualification. | `HostCleanupOrchestrationTests.Each_namespace_requires_its_own_dry_run` | unqualified namespace remains Execute=false/refused. |
| PC-72 | Ignore changed root-config digest. | `HostCleanupOrchestrationTests.Activation_binds_storage_and_config_digest` | old qualification refused for changed digest/store. |
| PC-73 | Set Execute on preview success. | `HostCleanupOrchestrationTests.Dry_run_success_does_not_enable_execute` | Execute remains false until explicit operator action. |
| PC-74 | Open backlog after first qualifying day. | `HostCleanupReportTests.Backlog_opens_on_third_complete_local_day` | days 1/2 no episode; day 3 exactly one at 20 GiB with owner/refusal. |
| PC-75 | Count multiple reports on same local day. | `HostCleanupReportTests.Backlog_counts_one_observation_per_local_day` | streak == 1 for repeated/DST same-day reports. |
| PC-76 | Ignore missing calendar day. | `HostCleanupReportTests.Missing_day_breaks_backlog_streak` | streak reset; freshness condition present. |
| PC-77 | Clear backlog on incomplete zero-byte sample. | `HostCleanupReportTests.Incomplete_inventory_cannot_open_or_clear_backlog` | open episode remains; incomplete cannot qualify closed episode. |
| PC-78 | Add eligible worktree bytes to reclaimed scratch. | `HostCleanupReportTests.Reclaimed_bytes_exclude_worktree_backlog` | reclaimed equals removed virtual scratch only; worktree reclaimed == 0. |
| PC-79 | Coalesce missing measurement to zero. | `HostCleanupReportTests.Null_sample_never_becomes_zero_usage` | sample value remains null/Unknown with original age. |
| PC-80 | Clear episode on stale good sample. | `HostCleanupReportTests.Budget_pressure_has_one_episode_and_fresh_recovery` | episode stays open for stale; fresh good clears once. |
| PC-81 | Double cap when free bytes low. | `HostCleanupReportTests.Post_run_low_space_never_widens_deletion` | same cap/age/attempt log before and after pressure. |
| PC-82 | Accept same run with changed digest. | `HostCleanupReportTests.Changed_receipt_digest_is_refused` | conflict; original digest/candidates/attention unchanged. |
| PC-83 | Omit recovery enqueue of committed pending receipt. | `HostCleanupReportTests.Crash_before_enqueue_recovers_pending_receipt` | after deterministic recovery tick attention GET contains run ID. |
| PC-84 | Repeat execution when upload ack lost. | `HostCleanupReportTests.Crash_after_ingestion_before_ack_queries_status` | one execute; status query observed; one recipient row. |
| PC-85 | Drop pending invalidation after publish failure. | `HostCleanupReportTests.Crash_before_invalidation_recovers_attention_delivery` | subscriber sees retry event; subsequent GET matches run/board. |
| PC-86 | Skip board predicate in report projection. | `HostCleanupReportTests.Attention_is_board_scoped_paginated_and_redacted` | only requested board IDs; bounded pages and no secret sentinel. |
| PC-87 | Ignore restart intent at cleanup admission. | `HostCleanupMaintenanceTests.Restart_intent_prevents_cleanup_admission` | admission == Deferred; cleanup-entry attempts == 0. |
| PC-88 | Acquire land lease before bounded cleanup drains. | `HostCleanupMaintenanceTests.Land_waits_only_for_bounded_inflight_delete` | at explicit syscall barrier land lease calls == 0. |
| PC-89 | Continue next candidate despite pending intent. | `HostCleanupMaintenanceTests.Cleanup_yields_to_pending_maintenance` | exactly one candidate started; next Deferred. |
| PC-90 | Ignore changed epoch. | `HostCleanupMaintenanceTests.Stale_epoch_refuses_delete` | epoch_changed; no delete intents. |
| PC-91 | Expire claim solely from clock elapsed. | `HostCleanupMaintenanceTests.Lost_connection_does_not_expire_live_claim` | claim retained; second worker delete count == 0. |
| PC-92 | Treat unknown prior worker as absent. | `HostCleanupMaintenanceTests.Restart_requires_positive_custody_reconciliation` | destructive admission remains disabled until positive reconciliation. |
| PC-93 | Insert new RepositoryMutationLease(null!) in a reachable daily server method; constructor alone compiles and is detected before invocation. | `HostCleanupWorktreeInventoryTests.Daily_graph_has_no_direct_lease_or_lock_escape` | forbidden reachable calls empty before invocation. |
| PC-94 | Ignore dirty content in inventory classifier. | `HostCleanupWorktreeInventoryTests.Dirty_or_active_worktree_is_kept` | dirty-source fixture keep; clean released positive fixture would-remove. |
| PC-95 | Treat pushed nonmaster tip as sufficient release. | `HostCleanupWorktreeInventoryTests.Classifier_preserves_guarded_classes_and_release_requirements` | pushed-unreleased keep; exact released/evidence-preserved positive would-remove. |
| PC-96 | Fetch missing object during inventory. | `HostCleanupWorktreeInventoryTests.Read_only_git_never_fetches_or_mutates_refs` | fetch/prune/remove/update-ref command log empty; missing object Unknown. |
| PC-97 | Dispatch unsupported hostCleanup operation. | `HostCleanupCommandTests.Missing_capability_refuses_cleanup` | Unsupported; root observations == 0. |
| PC-98 | Use request path in worker registry. | `HostCleanupCommandTests.Request_cannot_supply_arbitrary_roots` | validation refusal; outside roots never touched. |
| PC-99 | Skip host/store binding. | `HostCleanupCommandTests.Foreign_host_or_store_is_refused` | refusal; mutation log empty. |
| PC-100 | Call runtime KillAll on cleanup error. | `HostCleanupCommandTests.Cleanup_never_launches_or_stops_a_session` | start/kill/release/input counters == 0. |
| PC-101 | Release claim after receipt-write failure. | `HostCleanupCommandTests.Receipt_commit_failure_retains_claim_without_reexecution` | claim remains uncertain; resume does not execute again. |
| PC-102 | Insert U+2014 in a scoped script comment. | `HostCleanupScriptTests.Daemon_and_autostart_scripts_are_ascii` | offending path/offset byte must be <128. |
| PC-103 | Permit retroactive/foreign scratch registration. | `HostCleanupScriptTests.Scratch_registration_refuses_foreign_owner_and_late_adoption` | refusal; worker mutation count == 0. |
| PC-104 | Release while child pending. | `HostCleanupScriptTests.Scratch_release_requires_joined_children_and_preserved_evidence` | release refused; scratch remains. |
| PC-105 | Restore robocopy /MIR branch. | `HostCleanupScriptTests.Legacy_build_junk_wrapper_uses_shared_worker_only` | robocopy/Remove-Item/native-delete counters == 0; shared worker called. |
| PC-106 | Restore Register-ScheduledTask branch. | `HostCleanupScriptTests.Installer_refuses_registration_but_allows_explicit_uninstall` | creation calls == 0; default refusal; explicit uninstall only. |
| PC-107 | Skip epoch publication in runner restart wrapper. | `HostCleanupScriptTests.All_restart_entry_points_publish_and_drain_maintenance` | first stop/restart index follows committed intent and drain for each entry. |
| PC-108 | Copy raw exception body into script output. | `HostCleanupScriptTests.Protected_payloads_never_reach_script_output` | secret sentinel absent from stdout/stderr/summary. |
| PC-109 | Delete report alongside tool in recovery. | `HostCleanupCheckpointCompatibilityTests.automatic_recovery_preserves_all_evidence` | all evidence hashes remain identical. |
| PC-110 | Remove marker before payload finishes. | `HostCleanupCheckpointCompatibilityTests.marker_survives_partial_deletion` | deleting marker exists after partial pass. |
| PC-111 | Ignore MaxRoots. | `HostCleanupCheckpointCompatibilityTests.root_delete_count_is_bounded` | CompletedRoots == 2; third remains. |
| PC-112 | Delete roots before registered task completes. | `HostCleanupCheckpointCompatibilityTests.sealed_scope_awaits_registered_work` | before explicit release, disposal incomplete and root exists. |
| PC-113 | Return new temp root before marker/index registration. | `HostCleanupCheckpointCompatibilityTests.root_is_registered_before_return` | callback observes marker and indexed root before return. |
| PC-114 | Treat uncertain registered child as safe. | `HostCleanupCheckpointCompatibilityTests.live_child_prevents_scope_deletion` | identity-unknown failure receipt; virtual root remains. |
| PC-115 | Discard original error after second cleanup fault. | `HostCleanupCheckpointCompatibilityTests.teardown_preserves_failure_and_attempts_other_roots` | both failure reasons and second root removed. |
| PC-116 | Signal before checking expected start token. | `HostCleanupCheckpointCompatibilityTests.stop_validates_generation_before_signaling` | CountingControl.Calls == 0; tool retained. |
| PC-117 | Normalize drive-relative path as trusted absolute. | `HostCleanupWindowsTests.Case_and_path_aliases_cannot_escape_root` | path_escape for alias fixtures; native deletion count == 0. |
| PC-118 | Skip file-ID equality. | `HostCleanupWindowsTests.File_id_substitution_refuses_delete` | identity_changed; delete-disposition count == 0. |

PC-19's two variants separately insert the tagged and untagged overload call; both must fail the recorded-count assertion even if the injected exception is caught. PC-18/117/118 are Windows adapter mutations; remaining prescribed variants run on Linux. PC-102's ASCII byte mutation is portable and is detected on Linux, while Windows PowerShell 5.1 behavior is ordinary CP-7 evidence. Prescribed mutations never alter real runtime roots, schedules, providers or host processes.

### Out of scope

- Whole-worktree deletion, release/reconciliation side effects, remote branch removal, slot reset, provider archives, Docker pruning and the documented future retirement-enqueue extension. V-13 asserts the relevant calls are absent. Existing CARD-0692/0459/0665/0670 owners retain their independent authority and schedules.
- Real cleanup of developer/CI filesystem paths, real kernel deletion races, live schedule imports, native process death, provider launches and redeploy. These need the separate operational qualification already specified, on operator-authorized disposable fixtures. They are not converted into skipped tests that satisfy Min.
- No unfiltered Unit/full namespace/Herdr/Grok suite. Existing CARD-0791/0794, CARD-0818/0820/0828/0848 and CARD-0878/0879 flakes do not authorize a broad run or hide a failure. The old physical checkpoint cases can still carry CARD-0820 behavior; virtual ports remove timing/OS deletion from this card's acceptance rather than reporting those cases passed.
- Scripted maintenance clients prove the host-gate protocol and real database consistency; they do not prove that every arbitrary operator kill, third-party restart or uninstrumented service manager participates. Those routes cannot be made safe by a timeout/TTL. Startup must keep deletion disabled until custody/gate reconciliation, and activation must inventory participating entry points before Execute is enabled.

### Source-area footprint and collision order

The two implementation scope lines above remain the base. The following are **exact additions/corrections**, needed for the frozen fixtures and extraction, and part of Code's declared scope:

- New `src/Antiphon.HostCleanup/Checkpoints/ExecutorImageGuard.cs`, `src/Antiphon.HostCleanup/ICleanupNativeApi.cs`, `src/Antiphon.HostCleanup/LinuxCleanupNativeApi.cs`, `src/Antiphon.HostCleanup/WindowsCleanupNativeApi.cs` (already within the library prefix).
- Modify `tools/Antiphon.Checkpoints/{CheckpointApp,Program}.cs`, `tools/Antiphon.Checkpoints/Commands/WaitCommand.cs`, `tools/Antiphon.Checkpoints/Evidence/EvidenceFolder.cs`, `tools/Antiphon.Checkpoints/State/RunStateStore.cs` to thread instance cleanup I/O/clock dependencies through the enumerated callers; `Cleanup/OutputCleanup.cs` is already in the cleanup prefix. No unrelated owner/capability/checkpoint scheduler redesign. A production-facing dependency cannot reference a test type.
- New `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs`, `HostCleanupCheckpointFixture.cs`, `HostCleanupScriptFixture.cs`; new `tests/Antiphon.Tests/Application/HostCleanupFixture.cs`; new `tests/Antiphon.SessionRunner.Tests/HostCleanupFixture.cs` (all within existing HostCleanup prefixes). The old 40 physical tests are read-only source contracts, not edited or executed by these rows.
- Add startup maintenance participation in `dev-aspire.ps1`; add/inject maintenance operations in `scripts/session-runner-restart-health.ps1` used by the existing runner restart entry point; new `scripts/lib/host-cleanup-maintenance.ps1` for shared offline-testable publish/drain calls. No installer or new scheduled task is introduced. These extend the already named AppHost/runner/deploy wrappers; unknown startup state keeps deletion off.
- For the ASCII test, read exact files `dev-aspire.ps1`, `scripts/host-cleanup.ps1`, `scripts/task-scratch.ps1`, `scripts/cleanup-build-junk.ps1`, `scripts/install-cleanup-task.ps1`, `scripts/restart-apphost.ps1`, `scripts/restart-session-runner.ps1`, `scripts/session-runner-restart-health.ps1`, `scripts/deploy-server2.ps1`, `scripts/lib/host-cleanup-maintenance.ps1`, `scripts/fixtures/c826-cleanup-shim.ps1`, `scripts/autostart-apphost.ps1`, `scripts/autostart-session-runner.ps1`, `scripts/run-daemon.ps1`. The last three remain read-only; they route startup to the server/runner startup reconciliation or dev-aspire. A pre-existing byte violation is reported rather than silently broadening their edit scope.

Static byte census found **351 non-ASCII bytes in `dev-aspire.ps1`** at the frozen source; the other nine existing scripts in that list have zero. Because dev-aspire is already an explicitly touched startup entry point in this footprint, S5 must normalize its comments/messages to ASCII without changing behavior. This is a known pre-implementation red for the named ASCII assertion, not a passed TUnit result. The three read-only daemon/autostart files need no normalization at this baseline. New scripts must begin ASCII-only.

| Other work | Exact collision decision | Required landing/dispatch order |
|---|---|---|
| CARD-0835 Code | **Collides**: checkpoint tool project, `CheckpointApp.cs`/cleanup dependency injection area, EF snapshot/migrations, `AgentTaskLandService.cs` and testing documentation. CARD-0826 does not edit `AgentTaskService.cs`, `AgentTaskDtos.cs` or `scripts/delegate.ps1`. | CARD-0835 must land first. Re-read its landed caller graph/schema; preserve this 40-contract census, record any source roster delta explicitly before Code. Do not run overlapping Code worktrees concurrently. |
| CARD-0880 policy | **No file collision** with its supplied footprint: `server/Bundles/orchestrator.md`, `AGENTS.md`, `docs/orchestration-loop.md`, orchestrator skill, `StandingPipelinePolicyDocumentationTests`. CARD-0826 edits only stage-code/stage-review guidance. | No mandatory land dependency from that footprint; preserve policy text and recheck if CARD-0880 expands into these two bundles. This corrects the Plan's generic “Stage-cap policy task” collision claim. |
| CARD-0778 | No edit to `tests/Antiphon.Tests/Agents/Grok` or its helpers; runner image is packaging only. | No exact collision from supplied test area. Any changed Dockerfile is separately serialized by actual path. |
| CARD-0801 | New `HostCleanup*` runner tests only; no Herdr source, parity tests, FakeHerdr or shared Herdr fixture edits. | No exact file collision; build slots still govern shared host load. |
| Held Code-ready CARD-0788, CARD-0505, CARD-0822 | **Collide if concurrently commissioned** through generated EF snapshot/migration area. Their AgentTaskService/delegate.ps1 edits are outside this card, but that does not remove the EF collision. | Serialize EF owners. CARD-0835 lands first; any of these three admitted ahead of CARD-0826 must land before its S3 migration. Otherwise keep them held until CARD-0826's schema slice lands. Orchestrator chooses the queue order, never parallel migration authorship. |
| CARD-0692 | Shared released-scratch integration seam, if its cleanup implementation lands before S2/S3. No existing generic post-land temp executor at frozen baseline. | Inspect its actual released-scratch entry point after land; integrate only that endpoint into the shared claim/executor. Do not call its worktree/session branch. A new exact file outside scope requires a plan footprint update before Code touches it. |
| CARD-0849 | Existing deploy/smoke scripts and possibly Compose/Dockerfile. | Serialize those exact paths if active; preserve existing rollout/cache gates. |

No new EF/AgentTaskService/delegate/Herdr/Grok/policy edits are implied beyond the explicit list. TestDesign's own footprint is just this Markdown file.

### Cost

All figures are **estimates**, not measured wall time. Ordinary final V/R floor = **77 minutes**: CP-1 10 + CP-2 15 + CP-3 15 + CP-4 10 + CP-5 10 + CP-6 6 + CP-7 9 + CP-8 2. Linux is 68 minutes/194 TUnit executions plus 26 Vitest executions; Windows is 9 minutes/7 executions. Seven isolated TUnit builds are included in those row estimates; CP-8 needs no .NET build. Red-first runs reuse the exact rows and can add up to 77 minutes; the extra pre-move CP-2 compatibility run adds 15 minutes and is explicitly declared, not hidden.

Mutation floor = **119 variants x 4 minutes = 476 minutes** for baseline-green/break/red/restore/rebuild/green, plus **30 minutes** discovery/custody/restoration/reporting = **506 minutes**. Three Windows variants (PC-18/117/118) contribute 12 of those 476 minutes; no Windows full-suite tax. Exact mutation filters are `/*/*/<Class>*/<method>` using the names in the PC table (argument-expanded method includes all four existing cases). Ordinary + Mutation floor = **583 minutes**, excluding red-first/pre-move runs, authoring and operational qualification. With both declared preparatory rounds, the verification estimate is **675 minutes**. Additional one-time EF generation/launcher bootstrap, if required, is 5–10 minutes with a stated build-slot command/reason.

Source-driven growth from 22 to 118 guards is deliberate: the original roster explicitly omitted independently important custody, auth, replay, recovery, activation and backlog controls. Reusing the 40 contracts avoids inventing different checkpoint semantics; targeted rows exclude broad integration/provider suites. No measured speedup is claimed. Authoring estimate rises to **16–24 hours** because injected I/O must reach the actual existing callers; do not budget this as a shell-script change. Operational per-host dry-run/private-fixture qualification remains approximately 15 minutes per namespace plus normal deployment/drain and N=3 complete daily reports, and may defer on unavailable custody without granting deletion authority.

### Checkpoints

Closed ordinary list. Run each green row once after its committed slice group; rows already green need rerun only if later edits change their covered code. Test-first reds use the **same exact filter**, with all row tests/scaffolds committed; explicitly record red-round/pre-move compatibility reruns and counts separately. Before moving shared code, CP-2's compatibility subset must be green within its full row; new safety assertions can be the expected reds. CP-7's final scoped Windows run follows S5; its S1 adapter red is a declared preparatory run of the same seven-case row with wrapper scaffolds present. Commit before **every** row invocation.

Each TUnit row has one isolated build and one literal class filter; class names end in `*`. CP-8 is the repository schema's non-TUnit exception, one positional Vitest command and Build/Min=n/a. No namespace/full-suite acceptance, no hidden skips or test-list count. Portable roster on Windows equals the Linux count for CP-1..5; Windows copies are not commissioned. CP-6 has Linux 6 / Windows 0 selected; CP-7 has Windows 7 / Linux 0 selected. The Environment column suppresses the existing automatic host-temp sweep and serializes tests. A platform row on the wrong OS is not a passing row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c826-policy/` | cleanup-policy | `/*/*/(HostCleanupPolicyTests*)\|(HostCleanupExecutionTests*)/*` | V-1, V-2, R-2 | Linux 42 executed; 0 failed/skipped; other OS portable roster 42, not commissioned | 42 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c826-custody/` | cleanup-custody | `/*/*/(HostCleanupOwnershipTests*)\|(HostCleanupCheckpointAdapterTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-3, V-4, R-1, R-3 | Linux 66 executed; 0 failed/skipped; other OS portable roster 66, not commissioned | 66 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3-S4 | `tests/Antiphon.Tests -> bin-c826-service/` | cleanup-service | `/*/*/(HostCleanupOrchestrationTests*)\|(HostCleanupReportTests*)\|(HostCleanupMaintenanceTests*)\|(HostCleanupWorktreeInventoryTests*)/*` | V-5, V-6, V-7, V-13, R-2, R-3, R-4 | Linux 56 executed; 0 failed/skipped; other OS portable roster 56, not commissioned | 56 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c826-runner/` | cleanup-runner | `/*/*/(HostCleanupCommandTests*)/*` | V-8, R-4 | Linux 12 executed; 0 failed/skipped; other OS portable roster 12, not commissioned | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c826-scripts/` | cleanup-scripts | `/*/*/(HostCleanupScriptTests*)/*` | V-9, R-3 | Linux 12 executed; 0 failed/skipped; other OS portable roster 12, not commissioned | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1-S2 | `tests/Antiphon.Tests -> bin-c826-linux/` | cleanup-linux | `/*/*/(HostCleanupLinuxTests*)/*` | V-10, R-5 | Linux 6 executed; 0 failed/skipped; Windows 0, do not select | 6 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S5 | `tests/Antiphon.Tests -> bin-c826-windows/` | cleanup-windows | `/*/*/(HostCleanupWindowsTests*)/*` | V-11, R-5 | Windows 7 executed; 0 failed/skipped; Linux 0, do not select | 7 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S5 | n/a | cleanup-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-12, R-4 | Linux 26 Vitest executed = 22 existing + 4 new; 0 failed/skipped; 0 TUnit; Windows not commissioned | n/a | 2 | true | n/a |

## Execution, activation and rollback

The frozen Verification design supersedes the Plan's preliminary method counts, native destructive fixtures, four-file-only dependency assumption and generic stage-policy collision. It preserves D-1..D-7 cleanup authority. The Code footprint is the original explicit scope **plus** the exact seam/fixture/startup additions in Verification design. No production code or activation is delivered by TestDesign.

No builds or tests were run here. The assigned TestDesign brief authorizes static census/manifest checks only; any diagnostic row in this task would use self-leasing `scripts/run-checkpoint.ps1` directly, never an owner-unverified checkpoint tool run (CARD-0853). For Code, follow the checkpoint tool's committed-slice workflow only after its task ownership/token is verified, one run then wait until exit is not 75. Use waits of at most 50 seconds with progress updates; never start a second run to replace a running one. Do not print tokens.

Direct-row form, when the caller commissions that fallback, with environment set before the test host and the exact filter copied from CP-n:

```powershell
$env:C804_ORPHAN_SWEEP_ROOT = 'c826-disabled'
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c826-policy/ -Filter '/*/*/(HostCleanupPolicyTests*)|(HostCleanupExecutionTests*)/*' -MinExecuted 42 -Expect HostCleanupPolicyTests,HostCleanupExecutionTests -ResultsRoot .antiphon/c826-checkpoints
```

The script owns the build slot and fresh result directory. Use its `-OutputPath` flag with a forward slash; TUnit uses `dotnet run`, never `dotnet test`. Standalone EF generation and any required checkpoint-launcher bootstrap use `scripts/build-slot.ps1` with a stated reason; no permission to run unleased or ignore exit 4. CP-8, if run directly instead of the checkpoint tool, uses `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c826-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts`. No additional client build or browser suite is in this acceptance list.

Report each CP-n source SHA, OS, build result, exact filter, actual executed/passed/failed/skipped counts, fresh TRX/JSON path and reruns. Compare the executed Class.Method inventory with the frozen roster, not merely Min. An inherited failure needs exact source SHA/signature evidence; no blanket flake exemption. No Linux result substitutes for CP-7. The current runner mirror cannot certify the inaccessible desktop host.

Activation is commissioned only after ordinary green checkpoints, independent Review and confirmed publication:

1. Apply the confirmed D-2 decision: worktrees/registrations/refs are inventory-only, and the daily call graph has no repository lease or retirement enqueue path. Inspect the current CARD-0692/0459/0824/0669 implementations and live schedule ownership before integration; preserve the existing worktree owners' settings and independent schedules. Refresh the host inventory and resolve the seven hold identities without changing their disposition.
2. Deploy with `HostCleanup:Execute=false` on **every** namespace, conservative scratch caps and the stated volume/free-space/backlog budgets. `Worktrees=InventoryOnly` remains fixed; no retirement-enqueue implementation is shipped. Use the canonical main checkout/runbook and normal rolling-deploy gates; verify `/api/version`, runner capability/store identity and loaded SHA. Recreate build-slots on the selected current image during that authorized redeploy, and record immutable image ID plus broker availability. No stale image is silently accepted.
3. Before turning on the new daily schedule, export/record old schedule settings, disable the overlapping Windmill build-junk schedule, and confirm it is disabled. Preserve `antiphon:worktree-residue` and its current Execute setting, CARD-0692's post-land ownership, and the independent Claude archival schedule. The new daily run must neither trigger nor reconfigure worktree retirement. Do not automatically re-enable the unsafe old build-junk script if a new schedule fails. Import the host-only Windmill definition using the established credentials/SSH relay without printing secrets.
4. Run and preserve **one dry-run-only deployment cycle per desktop, runner and host namespace**. Confirm required roots are present, root/storage identity is unique, pty-host/custody/session/volume denies hold, unowned/legacy files are retained, holds are visible, bytes and free space are measured, and each board attention item links the correct run. Confirm worktree would-remove/keep/unknown counts and eligible-but-unremoved GB, named existing owner and any availability/refusal reason. An offline namespace remains dry-run pending; another host's report does not qualify it.
5. In private test-created directories on each actual OS, qualify a released old scratch file, a fresh nested file, a live-owner root, an unowned root, a protected-runtime lookalike and a would-remove worktree. Exactly the eligible scratch fixture may disappear after explicit execute; the worktree and all other protected fixtures remain, with zero worktree/Git-metadata writes. Verify no repository lock/agent spawn, no lost custody and a complete receipt. These are operational acceptance fixtures, not tests run against real retained host state. Record them separately from TUnit counts.
6. Enable **scratch deletion only** separately per qualified namespace, initially at **one candidate / one GiB**. Execute writes its plan before deletion. Compare outcomes, preserved worktree/runtime sentinels, disk sample and board attention, then restore the configured 10/10-GiB limits only after that receipt is reviewed. Enablement is an explicit operator setting action; a successful dry-run is not auto-approval. Unknown legacy roots may require a separately reviewed adoption/sweep; they are not retroactively marked by this feature. Changing Execute never enables worktree removal or retirement enqueue.
7. Observe N=3 complete daily reports plus at least two 15-minute usage samples. Require summary and pressure/recovery behavior for each host and no repeated deletion after upload retry. If eligible-but-unremoved worktrees stay at/above 20 GiB, require the owner-attributed backlog alert; if below, require the complete measured history rather than a fabricated alert. Offline/incomplete history stays visible. The host's SSH bridge and each runner must both report; warnings do not authorize retirement, retry loops or extra deletion. Acceptance is owned scratch cleanup plus visible inventory-only worktrees under the confirmed lock-free policy.

Rollback first sets Execute=false for all namespaces, withdraws pending scratch execution authorizations, drains or positively confirms completion of any in-flight scratch deletion, and keeps status/attention/monitoring where possible. Worktrees remain inventory-only throughout; there is no retirement enqueue queue or switch to drain. An unknown worker retains its claim; never expire it into a competing deleter. Disable only the new schedules if necessary; do not disable, enable or otherwise alter the existing CARD-0459/0692 worktree owners. Preserve runs/holds/ownership markers, and redeploy the previous compatible binary only after verifying marker/DB compatibility. Keep the additive database tables; no automatic down migration or restore of unsafe weekly build-junk deletion. Already deleted disposable bytes cannot be restored by rollback; regenerated build files and externally preserved deliverables are the recovery paths. Do not recreate a held worktree, provider state, or gitdir from a guessed path.

### TestDesign validation and handoff

Static validation: source-derived checkpoint compatibility census is 15 + 9 + 16 = 40; client census is 16 + 6 = 22. The frozen proposed roster adds 161 new ordinary single-case methods and 37 virtual compatibility methods (40 executions): 201 TUnit executions total, commissioned as Linux 194 and Windows 7, plus 26 Vitest. There are eight schema-shaped CP rows, 118 distinct guard-to-PC mappings and 119 prescribed variants. No build, test, schedule registration, native deletion or provider launch was performed; Linux 0 / Windows 0 executed in this stage.

`git diff --check` and the static manifest/roster cross-check passed: eleven columns per checkpoint, required header order, seven unique isolated outputs, exact class unions and Min totals, all 198 proposed TUnit methods accounted for (three extra argument expansions), all thirteen V IDs and five R IDs covered, every PC method in its row, and one-to-one guard IDs. This is static validation against the inspected importer schema, not a claim of running the .NET checkpoint tool. Only this plan is modified.

TestDesign handoff: **complete; next: code**. Land CARD-0835 first, serialize the generated EF snapshot with any admitted CARD-0788/0505/0822 work, and retain the fixed no-repository-lease/inventory-only worktree boundary. Author the virtual fixtures and caller compatibility tests before extraction, commit before row runs, and use the exact closed roster. Native kernel and live per-host activation claims remain operational acceptance; this plan does not silently count them as covered by fake syscalls. Only this plan changed.
