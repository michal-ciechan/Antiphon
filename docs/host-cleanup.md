# CARD-0826 host cleanup: implementation status

This code slice is **not operational**. No daily schedule, runner command, host SSH job, native
deletion adapter, persistent scratch claim store, participating maintenance gate, authenticated API receipt ingestion, or per-host
qualification is installed. `HostCleanup:Execute` is not configured. The new native filesystem
adapters throw before observation or deletion. Existing worktree cleanup owners and their settings
were not changed. No live host cleanup or provider launch was performed.

The branch contains a virtual-facts policy/executor seam, a read-only worktree
classifier, attention DTO/client presentation, ASCII-only `dev-aspire.ps1`, and the additive
`20261002145938_AddHostCleanup` migration. Standalone services persist metadata-only receipts,
reject changed replays, retain pending invalidation after event failure, page board-scoped reports,
and project backlog, pressure and expired holds. A receipt older than one day is retained as an
incomplete observation; it cannot qualify or clear backlog. The receipt candidate list is snapshotted
before any database await, so its hash and stored rows cannot diverge through caller mutation.
The standalone maintenance store/service publishes a durable storage-scoped epoch and uses
compare-and-swap for one bounded operation. Pending intent blocks admission; uncertain custody
requires a positive injected probe, with no TTL expiry. These services are not registered in the HTTP,
scheduler or runner graph. Worktree inventory rows never confer deletion
authority. The classifier has no repository mutation lease, Git mutation, or retirement dependency.
These facts do **not** establish the plan's full transitive no-lease invariant; the scheduled call
graph does not exist yet.

## Replay and continuation (task 863ee584)

Baseline: `808677658cc418dc439d906de4526aaea32e231a`. All ten prior branch commits were
cherry-picked in order. CARD-0888's client tests and `TaskInputUnreadable = 52` were preserved;
cleanup kinds use 53, 54, 55 and 56. The full enum distinctness test and all three wire-name
tests pass. Client roster is 29 (the frozen 26 plus CARD-0888's three tests).

CP-1 passed 42/42 at `a9cc771591e7c8cfbb776cdcc9503043d05d9ea9`. The S3 subset passed 23/23
at `4ecee171fc0c6c76af089a6a5cd850756c011768`; CP-3 exited 3 because its unchanged Min is 56.
Schema tests first failed at named assertions on the baseline. Fifteen report tests then failed
against neutral compiling services before implementation. The generated Designer model body is
identical to the snapshot; upgrading from `AddAgentTaskEventInputBody` preserves existing task
and event/InputBody rows. This is not acceptance of missing CP-3 cases or operational delivery.

The next maintenance/immutability red round at `291800e69ebfa51a726b128765f68c0667b8f446`
executed 33: 23 passed and ten named assertions failed. After implementation at
`e66ade5474470b1f21bad0e315062cdc3789aa02`, all 33 passed (CP-3 still exited 3 on Min=56).
The delayed-receipt regression at `cf2c147110aefe83a0a834f49bf8e2fb252bf3d1` executed 34:
33 passed and the named `C826.delayed-recovery-cannot-clear-backlog` assertion failed.
Its production fix and final ordinary verification are recorded in the task report.

Current CP-3 roster: zero of 14 planned orchestration cases plus two schema tests; 15 of 20
planned report cases plus two receipt regression tests; nine of ten maintenance cases;
six of twelve worktree-inventory cases. Total 34 = 30 planned + four supplemental, leaving
26 planned cases missing. The frozen floor remains 56; a completed expanded roster would be 60.
The maintenance tests prove persisted decisions and CAS, including a paused inventory scan,
not a live native syscall, repository lease, restart, deploy or land caller. In particular
`Land_waits_only_for_bounded_inflight_delete` remains unimplemented.

S2's 40 virtual compatibility ports and pre-move run are still pending; no shared checkpoint
files have moved. CARD-0885's repeat tool changes remain intact. S4 transport/store/packaging,
S5 worker/helper/script/cutover integration, native adapter qualification, HTTP authorization,
daily scheduling/catch-up, historical hold resolution and per-host activation remain pending.
Receipt persistence does not prove durable plan-before-delete, shared scratch claims, caller
maintenance participation, local outbox recovery or receipt-to-HTTP-attention delivery. Report
retention and recurring invalidation recovery are not scheduled. Do not activate cleanup.

Replaying prior-branch work onto a newer master is done by cherry-pick in a NEW task;
never rebase or merge master into a fast-forward-only task branch.

## Historical verification from task 3a378666

| Evidence | Result | Limit |
|---|---|---|
| CP-1 at `f70183cb258e9d94e017143005db6e216fff2a1b` | 42 executed, 42 passed, 0 failed/skipped | Policy and execution virtual-facts roster; later source changes require a final-SHA rerun, reported by the task. |
| CP-8 client filter at `5d1bf4d2d973beb5151acbc09bffe436482803f3` | 26 Vitest cases passed | DTO/presentation only; server producer and recipient route are pending. |
| Partial V-13 diagnostic at `f0be40f58` | 6 executed, 6 passed | Six pure inventory cases; CP-3's 56-case full row is pending. |
| Scratch red proof | All 60 authored TUnit methods and four new client cases reached distinct `C826.*` named assertion labels | The remaining frozen proposed tests are not authored. Each scratch mutation was restored with `git diff` empty. The per-test label inventory follows. |

The plan's frozen roster, time box, and per-host activation checks remain the acceptance contract.
No operator should set up or run this partial worker against a live host.

## Keyword requirement trace

The table below indexes every **plan line** containing `verify`, `assert`, `must`, `exact`,
`never`, or `always` (case-insensitive). A line can hold multiple sentences; **No** covers every
unproved clause on that line. A partial assertion is listed as a lead, never treated as complete
proof. This deliberately conservative trace records the Code time-box departure rather than
weakening the plan. Full implementation and a sentence-level Yes/No audit remain required before
Review or activation.

| Plan line | Requirement lead (abridged) | Test and assertion lead (file:line) | All clauses enforced? |
|---:|---|---|:---:|
| 7 | Add a daily, bounded backstop for **owned disposable working files whose newest file write is strictly older than 24 h | PENDING: assertion absent | No |
| 22 | Desktop registered worktrees / Survey: 398 across four repositories, 95.9 GB; SAFE 201 / 40.5 GB, KEEP 144 / 31.9 GB,  | PENDING: assertion absent | No |
| 23 | Desktop temp families / 497 land-verify roots (465 >1 day); 2,401 probe roots (2,337 >1 day) / Approximately 225 GB ve | PENDING: assertion absent | No |
| 30 | Mount identity / '/proc/self/mountinfo': '/tmp' = 'antiphon-runner_runner-tmp'; '/work' = 'antiphon-runner_work', both | PENDING: assertion absent | No |
| 31 | Current runner catalogue / At 07:38Z: desktop Windows available (capacity 2, occupied 0); server2 Linux available (cap | PENDING: assertion absent | No |
| 42 | CARD-0459 / 'WorktreeResidueJob', 'WorktreeResidueSweepService', 'TaskWorktreeRetirementService'; daily Hangfire 'anti | PENDING: assertion absent | No |
| 44 | CARD-0670 / Board Backlog. Existing 'ReleaseAsync' requires explicit NoFurtherWorkspaceUse, exact revision/SHA, preser | PENDING: assertion absent | No |
| 45 | CARD-0692 / Board Review; intended post-land owner for all tasks, mirrors, sessions, branches and task temp. There is  | PENDING: assertion absent | No |
| 47 | CARD-0804/0805 / Both Done, landed '6bf159cf'; code present at the Plan baseline. 'CheckpointTempRootSweep' currently  | PENDING: assertion absent | No |
| 48 | Checkpoint sweep limits / Ten-minute creation grace, five-minute interval, 512 index entries, 10,000 descendants/root, | PENDING: assertion absent | No |
| 49 | CARD-0669 / Board Backlog; owns source-recheck refs, land pins and verify retention. / Ref/pin deletion stays there. D | PENDING: assertion absent | No |
| 50 | Runner mirrors / 'RunnerWorkspaceService.RemoveAsync' checks confinement, dirty status and optional PublishedSha, then | PENDING: assertion absent | No |
| 54 | Legacy installer / 'scripts/install-cleanup-task.ps1' can still register a Windows Scheduled Task. / Make install refu | PENDING: assertion absent | No |
| 62 | Windmill owns 'u/lndcobra/antiphon_host_cleanup_server2' for server2 **host-only** roots through the existing trusted  | PENDING: assertion absent | No |
| 70 | Invariant: daily planning/execution never acquires 'IRepositoryMutationLease' or 'RepositoryMutationLease', opens/acqu | PENDING: assertion absent | No |
| 74 | 'Worktrees=InventoryOnly' is a fixed behavior of this implementation, not a deletion mode unlocked by Execute. Invento | PENDING: assertion absent | No |
| 76 | Read existing-owner availability/latest refusal metadata at most once per owner per run; no request to execute or enqu | PENDING: assertion absent | No |
| 78 | This worktree exception does not disable lock-free scratch cleanup. Eligible temp families, non-worktree scratch on th | PENDING: assertion absent | No |
| 80 | An unpushed HEAD is always kept. A HEAD absent from origin/master but fully contained in its pushed task branch is onl | PENDING: assertion absent | No |
| 92 | Registry entries bind a family id, exact configured root/depth, name grammar, ownership adapter, evidence policy and e | PENDING: assertion absent | No |
| 97 | Probe/test sandbox / Individually registered grammars for c527/c487/c590/c408/c490 scratch and numeric 'c<N>-probe-<ru | PENDING: assertion absent | No |
| 98 | Land verification / 'antiphon-land-verify-<id>' plus an exact terminal-operation release and retained-evidence receipt | PENDING: assertion absent | No |
| 99 | Task scratch / Registered per-run '.antiphon' subdirectories and exact '/tmp/claude-1654/<task>' (or actual platform s | PENDING: assertion absent | No |
| 100 | Runner work-volume scratch / Exact registered non-worktree scratch roots on '/work', with owner/run release. Worktrees | PENDING: assertion absent | No |
| 101 | Build outputs and private caches / Exact producer-owned 'bin-*' and 'obj' output paths, private NuGet package+scratch  | PENDING: assertion absent | No |
| 104 | Hard deny roots include '/tmp/antiphon-pty-hosts' and all descendants, desktop configured pty-host runtime roots, runn | PENDING: assertion absent | No |
| 114 | For a nonempty candidate, record the maximum last-write UTC of **every regular file**, including hidden files and exis | PENDING: assertion absent | No |
| 116 | Process identity includes host boot identity, PID namespace/store identity, PID and process start time. Dead starter a | PENDING: assertion absent | No |
| 118 | Use one scratch-candidate claim keyed by storage identity + canonical file identity + owner generation. Post-land scra | PENDING: assertion absent | No |
| 120 | Restart/land exclusion cannot be promised by a one-time status poll. Add a small **host maintenance activity gate**, d | PENDING: assertion absent | No |
| 126 | 'host-cleanup.ps1 -Host <id> -DryRun' returns a plan on stdout; '-Status' reads prior receipts. Dry-run performs **zer | PENDING: assertion absent | No |
| 128 | Default daily deletion caps per storage namespace: **10 scratch-candidate attempts and 10 GiB**, total across disposab | PENDING: assertion absent | No |
| 130 | Keep-list records carry storage identity, canonical exact path, full owner identity where known, reason, creator, crea | PENDING: assertion absent | No |
| 136 | Add 'task-scratch.ps1 register/status/release/clean' backed by the same worker. Registration occurs **before** a task  | PENDING: assertion absent | No |
| 138 | CARD-0692 calls the same released-scratch executor for all settled card tasks, including the exact mapped '/tmp/claude | PENDING: assertion absent | No |
| 144 | Persist a host run, immutable planned candidates, outcomes, keep reasons, source/config digest, exact host/runner stor | PENDING: assertion absent | No |
| 146 | Add durable attention projections for the latest daily summary, stale/missed host report, expired hold and disk pressu | PENDING: assertion absent | No |
| 148 | Each report includes worktree counts and bytes per 'would-remove'/'keep'/'unknown' disposition and CARD-0665 content c | PENDING: assertion absent | No |
| 150 | Initial configurable 'runner-tmp' budget: **100 GiB**, warning at **60% (60 GiB)**, critical at 90%. Disk low threshol | PENDING: assertion absent | No |
| 152 | Extend CARD-0849 rolling-deploy smoke receipts with exact '/tmp' volume identity and allocated bytes, host filesystem  | PENDING: assertion absent | No |
| 156 | ## Exact implementation footprint and slices | PENDING: assertion absent | No |
| 162 | Slice / Exact paths and changes / Test-first work / | PENDING: assertion absent | No |
| 164 | S1: shared policy and filesystem seams / **new** 'src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj', 'CleanupContr | PENDING: assertion absent | No |
| 166 | S3: worktree inventory/classification/reporting, coordination and attention / **new** 'server/Application/Settings/Hos | PENDING: assertion absent | No |
| 167 | S4: runner transport and packaging / **new** 'src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs', 'src/Anti | PENDING: assertion absent | No |
| 168 | S5: operations and agent guidance / **new** 'scripts/host-cleanup.ps1', 'scripts/host-cleanup-server2.sh', 'scripts/fi | PENDING: assertion absent | No |
| 172 | Execution order groups S1/S2 fixtures and compiling seams before policy/extraction, then S3/S4 service and transport f | PENDING: assertion absent | No |
| 174 | **Source-area scope (future Code):** 'src/Antiphon.HostCleanup/**,tools/Antiphon.HostCleanup/**,tools/Antiphon.Checkpo | PENDING: assertion absent | No |
| 176 | **Additional exact Code scope for S3's read-only worktree probe:** 'server/Application/Interfaces/IHostCleanupWorktree | PENDING: assertion absent | No |
| 183 | CARD-0801 / Same 'tests/Antiphon.SessionRunner.Tests' project, but only new HostCleanup tests. No 'HerdrTransport.cs', | PENDING: assertion absent | No |
| 184 | CARD-0778 / No Grok readiness, adapter or fixture file in the footprint. / No exact collision. Dockerfile is packaging | PENDING: assertion absent | No |
| 186 | CARD-0849 / 'scripts/deploy-server2.ps1', 'scripts/verify-card0849-caches.ps1', Compose/Docker packaging if its branch | PENDING: assertion absent | No |
| 192 | TestDesign frozen at source '1dacda5e36f52a288c3b9b91f379b0e274c49038'. 'pwsh -NoProfile -File scripts/card.ps1 get CA | PENDING: assertion absent | No |
| 208 | The weekly script has **no safe reusable cleanup policy**: it selects 'bin-*' by folder mtime then invokes robocopy an | PENDING: assertion absent | No |
| 217 | 'CheckpointApp.StartAsync' -> preparation/launch custody and rollback; executor entry -> executor acknowledgement / V- | PENDING: assertion absent | No |
| 221 | 'OutputCleanup.RemoveOlderRuns' and 'Program.Clean'; 'CleanOwnedOutputs' from Program and CheckpointApp completion / ' | PENDING: assertion absent | No |
| 226 | **Before extraction:** author the virtual ports against the current implementation with injected dependencies, commit  | PENDING: assertion absent | No |
| 230 | All filesystem candidate fixtures are **virtual**, including fixtures spelling real protected paths. No selected test  | PENDING: assertion absent | No |
| 232 | Fixture, exact owner file / Dependencies/fakes and decisive evidence / | PENDING: assertion absent | No |
| 239 | 'HostCleanupScriptFixture', new 'tests/Antiphon.Tests/Infrastructure/HostCleanupScriptFixture.cs'; 'scripts/fixtures/c | PENDING: assertion absent | No |
| 241 | Client 'item()' in 'attentionVisuals.test.ts' / Existing DTO factory, four new exact test titles below. No browser or  | PENDING: assertion absent | No |
| 243 | 'ThrowingRepositoryMutationLease' overrides **both** TryAcquireAsync overloads, increments counters before throwing, a | PENDING: assertion absent | No |
| 245 | 'ProtocolStepGuard' completes a violation TaskCompletionSource on a forbidden second owner read or submission, then pa | PENDING: assertion absent | No |
| 247 | Each PC fixture satisfies every unrelated precondition and exposes the target boundary's decision or attempt count bef | PENDING: assertion absent | No |
| 258 | Runner/host receipt -> main ingestion: run ID + digest + store/boot identity / Real local receipt store/outbox, serial | PENDING: assertion absent | No |
| 260 | Daily complete reports -> sustained backlog episode: host/store + condition + local observation day / Persist unique o | PENDING: assertion absent | No |
| 266 | The following is the **frozen proposed roster**, in red-first authoring order within each slice. Every listed method i | PENDING: assertion absent | No |
| 270 | Fixture: 'HostCleanupFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 279 | 'Link_at_any_depth_is_kept' / Root, ancestor and descendant link fixtures are Unknown, never followed. / | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:76 C826.Link_at_any_depth_is_kept (partial) | No |
| 289 | 'Unregistered_msbuild_testcontainers_and_tmp_are_unknown' / Name alone never owns global MSBuildTemp, Testcontainers o | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:187 C826.Unregistered_msbuild_testcontainers_and_tmp_are_unknown (partial) | No |
| 290 | 'Private_cache_requires_exact_package_and_scratch_ownership' / Keep shared cache/locks; allow only released exact priv | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:198 C826.Private_cache_requires_exact_package_and_scratch_ownership:unowned (partial) | No |
| 292 | 'Per_host_roots_are_explicit_and_nonoverlapping' / Desktop, runner and host roots bind storage ID; missing roots never | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:218 C826.Per_host_roots_are_explicit_and_nonoverlapping (partial) | No |
| 299 | Fixture: 'HostCleanupFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 316 | 'Active_hold_preserves_owned_scratch' / Exact active hold also protects independently marked descendants. / | tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:197 C826.Active_hold_preserves_owned_scratch (partial) | No |
| 326 | Fixture: 'HostCleanupFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 330 | 'Unknown_owner_prevents_delete' / Unmarked/unowned root retained, never retrospectively adopted. / | tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:16 C826.Unknown_owner_prevents_delete (partial) | No |
| 345 | Fixture: 'CheckpointVirtualFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 362 | 'Every_cleanup_caller_accepts_virtual_io' / Actual wait/start/stop/evidence/age-clean/scope/hook paths never hit nativ | PENDING: assertion absent | No |
| 366 | Fixture: 'HostCleanupServerFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 377 | 'Restart_catchup_runs_once_per_local_day' / Restart/fall-back clock/retry chooses one current daily run, never all mis | PENDING: assertion absent | No |
| 387 | Fixture: 'HostCleanupServerFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 395 | 'Fresh_below_threshold_inventory_clears_backlog' / 19 GiB fresh complete sample clears; exact threshold remains open.  | PENDING: assertion absent | No |
| 396 | 'Backlog_window_and_threshold_are_configurable' / Injected 2-day/7-GiB configuration changes exact boundary. / | PENDING: assertion absent | No |
| 414 | Fixture: 'HostCleanupMaintenanceFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 431 | Fixture: 'HostCleanupServerFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 440 | 'Refusing_owner_is_reported_without_retry_or_delete' / One read, exact bounded refusal code, zero submission/removal;  | tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:106 C826.Refusing_owner_is_reported_without_retry_or_delete:one-read (partial) | No |
| 443 | 'Classifier_preserves_guarded_classes_and_release_requirements' / Real CARD-0665 classifier; Protected wins over Evide | tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:61 C826.Classifier_preserves_guarded_classes_and_release_requirements:protected (partial) | No |
| 444 | 'Registered_and_unregistered_candidates_are_fully_accounted' / Paged registered/mirror/orphan/detached inventory compl | tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:80 C826.Registered_and_unregistered_candidates_are_fully_accounted:complete (partial) | No |
| 450 | Fixture: 'HostCleanupRunnerFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 469 | Fixture: 'HostCleanupScriptFixture'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 473 | 'Daemon_and_autostart_scripts_are_ascii' / Every byte of scoped daemon/autostart/wrapper scripts <128; enumerate exact | PENDING: assertion absent | No |
| 475 | 'Scratch_release_requires_joined_children_and_preserved_evidence' / Pending children/evidence debt prevent clean; exac | PENDING: assertion absent | No |
| 479 | 'Smoke_reports_separate_tmp_and_host_disk_identity' / Exact sample identities, age/completeness and image SHA; contain | PENDING: assertion absent | No |
| 482 | 'Helper_preserves_literal_paths_and_exit_codes' / Spaces, brackets, apostrophes and backslash Windows roots bind as on | PENDING: assertion absent | No |
| 488 | Fixture: 'HostCleanupNativeFixture(Linux)'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 501 | Fixture: 'HostCleanupNativeFixture(Windows)'. Exact method names, in authoring order: | PENDING: assertion absent | No |
| 510 | 'Windows_boot_and_start_uncertainty_is_retained' / Boot tolerance never masks start mismatch; inaccessible identity is | PENDING: assertion absent | No |
| 515 | New file 'tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs', fixture 'CheckpointVirtualF | PENDING: assertion absent | No |
| 517 | Source class / exact method in new class / Executions / | PENDING: assertion absent | No |
| 557 | 'completed_wait_removes_only_tool' keeps exactly '[Arguments(0,false)]', '[Arguments(0,true)]', '[Arguments(1,false)]' | PENDING: assertion absent | No |
| 561 | Source census: 16 plain 'it' cases plus one 'it.each' with six rows = 22. Preserve all 22 and add these four exact tit | PENDING: assertion absent | No |
| 565 | 'links a host cleanup summary to its run' / Target contains the exact run ID; label and group are nonempty. / | client/src/features/attention/attentionVisuals.test.ts:100 C826.links a host cleanup summary to its run (partial) | No |
| 582 | Each row below is one independently reviewed guard with one distinct prescribed positive control. A matrix test can de | PENDING: assertion absent | No |
| 607 | G-22 / D-2: Execute never deletes worktrees / PC-22 / | PENDING: assertion absent | No |
| 658 | G-73 / Activation 6: Dry-run never auto-enables / PC-73 / | PENDING: assertion absent | No |
| 688 | G-103 / D-6: Register before use and exact owner / PC-103 / | PENDING: assertion absent | No |
| 694 | G-109 / S2: Tool evidence never swept as payload / PC-109 / | PENDING: assertion absent | No |
| 699 | G-114 / S2: Scope never deletes with uncertain child / PC-114 / | PENDING: assertion absent | No |
| 707 | SourceLanding Mutation only, after ordinary Code, independent Review and confirmed publication. Every defect below mus | PENDING: assertion absent | No |
| 723 | PC-13 / Ignore matching hold. / 'HostCleanupExecutionTests.Active_hold_preserves_owned_scratch' / exact hold ID and de | tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:197 C826.Active_hold_preserves_owned_scratch (partial) | No |
| 734 | PC-24 / Accept ../ or sibling-prefix escape. / 'HostCleanupPolicyTests.Traversal_cannot_escape_configured_root' / outs | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:63 C826.Traversal_cannot_escape_configured_root (partial) | No |
| 743 | PC-33 / Treat registered parent as ownership of shared cache. / 'HostCleanupPolicyTests.Private_cache_requires_exact_p | tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:198 C826.Private_cache_requires_exact_package_and_scratch_ownership:unowned (partial) | No |
| 749 | PC-39 / Truncate oversized root to fit budget. / 'HostCleanupExecutionTests.Oversized_root_is_not_truncated' / truncat | tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:99 C826.Oversized_root_is_not_truncated (partial) | No |
| 805 | PC-95 / Treat pushed nonmaster tip as sufficient release. / 'HostCleanupWorktreeInventoryTests.Classifier_preserves_gu | tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:61 C826.Classifier_preserves_guarded_classes_and_release_requirements:protected (partial) | No |
| 808 | PC-98 / Use request path in worker registry. / 'HostCleanupCommandTests.Request_cannot_supply_arbitrary_roots' / valid | PENDING: assertion absent | No |
| 812 | PC-102 / Insert U+2014 in a scoped script comment. / 'HostCleanupScriptTests.Daemon_and_autostart_scripts_are_ascii' / | PENDING: assertion absent | No |
| 830 | PC-19's two variants separately insert the tagged and untagged overload call; both must fail the recorded-count assert | PENDING: assertion absent | No |
| 837 | - Scripted maintenance clients prove the host-gate protocol and real database consistency; they do not prove that ever | PENDING: assertion absent | No |
| 841 | The two implementation scope lines above remain the base. The following are **exact additions/corrections**, needed fo | PENDING: assertion absent | No |
| 847 | - For the ASCII test, read exact files 'dev-aspire.ps1', 'scripts/host-cleanup.ps1', 'scripts/task-scratch.ps1', 'scri | PENDING: assertion absent | No |
| 849 | Static byte census found **351 non-ASCII bytes in 'dev-aspire.ps1'** at the frozen source; the other nine existing scr | PENDING: assertion absent | No |
| 851 | Other work / Exact collision decision / Required landing/dispatch order / | PENDING: assertion absent | No |
| 853 | CARD-0835 Code / **Collides**: checkpoint tool project, 'CheckpointApp.cs'/cleanup dependency injection area, EF snaps | PENDING: assertion absent | No |
| 855 | CARD-0778 / No edit to 'tests/Antiphon.Tests/Agents/Grok' or its helpers; runner image is packaging only. / No exact c | PENDING: assertion absent | No |
| 856 | CARD-0801 / New 'HostCleanup*' runner tests only; no Herdr source, parity tests, FakeHerdr or shared Herdr fixture edi | PENDING: assertion absent | No |
| 857 | Held Code-ready CARD-0788, CARD-0505, CARD-0822 / **Collide if concurrently commissioned** through generated EF snapsh | PENDING: assertion absent | No |
| 858 | CARD-0692 / Shared released-scratch integration seam, if its cleanup implementation lands before S2/S3. No existing ge | PENDING: assertion absent | No |
| 859 | CARD-0849 / Existing deploy/smoke scripts and possibly Compose/Dockerfile. / Serialize those exact paths if active; pr | PENDING: assertion absent | No |
| 865 | All figures are **estimates**, not measured wall time. Ordinary final V/R floor = **77 minutes**: CP-1 10 + CP-2 15 +  | PENDING: assertion absent | No |
| 867 | Mutation floor = **119 variants x 4 minutes = 476 minutes** for baseline-green/break/red/restore/rebuild/green, plus * | PENDING: assertion absent | No |
| 869 | Source-driven growth from 22 to 118 guards is deliberate: the original roster explicitly omitted independently importa | PENDING: assertion absent | No |
| 873 | Closed ordinary list. Run each green row once after its committed slice group; rows already green need rerun only if l | PENDING: assertion absent | No |
| 890 | The frozen Verification design supersedes the Plan's preliminary method counts, native destructive fixtures, four-file | PENDING: assertion absent | No |
| 892 | No builds or tests were run here. The assigned TestDesign brief authorizes static census/manifest checks only; any dia | PENDING: assertion absent | No |
| 894 | Direct-row form, when the caller commissions that fallback, with environment set before the test host and the exact fi | PENDING: assertion absent | No |
| 902 | The script owns the build slot and fresh result directory. Use its '-OutputPath' flag with a forward slash; TUnit uses | PENDING: assertion absent | No |
| 904 | Report each CP-n source SHA, OS, build result, exact filter, actual executed/passed/failed/skipped counts, fresh TRX/J | PENDING: assertion absent | No |
| 909 | 2. Deploy with 'HostCleanup:Execute=false' on **every** namespace, conservative scratch caps and the stated volume/fre | PENDING: assertion absent | No |
| 910 | 3. Before turning on the new daily schedule, export/record old schedule settings, disable the overlapping Windmill bui | PENDING: assertion absent | No |
| 912 | 5. In private test-created directories on each actual OS, qualify a released old scratch file, a fresh nested file, a  | PENDING: assertion absent | No |
| 913 | 6. Enable **scratch deletion only** separately per qualified namespace, initially at **one candidate / one GiB**. Exec | PENDING: assertion absent | No |
| 914 | 7. Observe N=3 complete daily reports plus at least two 15-minute usage samples. Require summary and pressure/recovery | PENDING: assertion absent | No |
| 916 | Rollback first sets Execute=false for all namespaces, withdraws pending scratch execution authorizations, drains or po | PENDING: assertion absent | No |
| 922 | 'git diff --check' and the static manifest/roster cross-check passed: eleven columns per checkpoint, required header o | PENDING: assertion absent | No |
| 924 | TestDesign handoff: **complete; next: code**. Land CARD-0835 first, serialize the generated EF snapshot with any admit | PENDING: assertion absent | No |

There are 145 keyword-bearing plan lines in this conservative inventory. The `No` entries remain required. The reason they are pending is the 150-minute Code time box, which cannot discharge the plan's estimated 16–24 hours of implementation and its remote/Windows qualification.

## Authored new-control red proof

The label below is the first failing named assertion recorded for that test under a scratch fault. Each method/title exactly matches the frozen plan roster. These red logs are diagnostic, not committed-source checkpoint receipts. Restoring each scratch fault left `git diff` empty.

| Planned method or client title | Failing assertion label | Assertion source | Verdict |
|---|---|---|---|
| `Unknown_family_is_reported_without_delete` | `C826.Unknown_family_is_reported_without_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:15` | named red; matches plan |
| `Runtime_deny_overrides_owned_family` | `C826.Runtime_deny_overrides_owned_family:.git` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:29` | named red; matches plan |
| `Parent_containing_runtime_state_is_kept` | `C826.Parent_containing_runtime_state_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:42` | named red; matches plan |
| `Child_of_protected_root_is_kept` | `C826.Child_of_protected_root_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:51` | named red; matches plan |
| `Traversal_cannot_escape_configured_root` | `C826.Traversal_cannot_escape_configured_root` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:63` | named red; matches plan |
| `Link_at_any_depth_is_kept` | `C826.Link_at_any_depth_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:76` | named red; matches plan |
| `Foreign_mount_is_kept` | `C826.Foreign_mount_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:86` | named red; matches plan |
| `New_nested_file_keeps_old_directory` | `C826.New_nested_file_keeps_old_directory` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:97` | named red; matches plan |
| `Exactly_twenty_four_hours_is_kept` | `C826.Exactly_twenty_four_hours_is_kept:equal` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:108` | named red; matches plan |
| `Future_timestamp_is_kept` | `C826.Future_timestamp_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:122` | named red; matches plan |
| `Empty_root_requires_old_valid_marker` | `C826.Empty_root_requires_old_valid_marker:fresh` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:131` | named red; matches plan |
| `Unreadable_entry_is_not_empty` | `C826.Unreadable_entry_is_not_empty` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:144` | named red; matches plan |
| `Traversal_budget_exhaustion_is_unknown` | `C826.Traversal_budget_exhaustion_is_unknown` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:153` | named red; matches plan |
| `Newest_write_includes_hidden_files_and_marker` | `C826.Newest_write_includes_hidden_files_and_marker` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:165` | named red; matches plan |
| `Configuration_cannot_relax_hard_guards` | `C826.Configuration_cannot_relax_hard_guards:limits` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:173` | named red; matches plan |
| `Unregistered_msbuild_testcontainers_and_tmp_are_unknown` | `C826.Unregistered_msbuild_testcontainers_and_tmp_are_unknown` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:187` | named red; matches plan |
| `Private_cache_requires_exact_package_and_scratch_ownership` | `C826.Private_cache_requires_exact_package_and_scratch_ownership:unowned` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:198` | named red; matches plan |
| `Retained_evidence_is_never_scratch` | `C826.Retained_evidence_is_never_scratch` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:209` | named red; matches plan |
| `Per_host_roots_are_explicit_and_nonoverlapping` | `C826.Per_host_roots_are_explicit_and_nonoverlapping` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:218` | named red; matches plan |
| `All_registered_owned_families_can_be_eligible` | `C826.All_registered_owned_families_can_be_eligible:Checkpoint` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:242` | named red; matches plan |
| `Older_owned_root_is_eligible` | `C826.Older_owned_root_is_eligible` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:252` | named red; matches plan |
| `Daily_plan_uses_injected_observations_only` | `C826.Daily_plan_uses_injected_observations_only` | `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs:265` | named red; matches plan |
| `Dry_run_has_zero_mutating_calls` | `C826.Dry_run_has_zero_mutating_calls` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:27` | named red; matches plan |
| `Explicit_plan_file_is_the_only_preview_write` | `C826.Explicit_plan_file_is_the_only_preview_write:inside` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:37` | named red; matches plan |
| `Plan_is_durable_before_first_delete` | `C826.Plan_is_durable_before_first_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:53` | named red; matches plan |
| `Plan_persist_failure_prevents_delete` | `C826.Plan_persist_failure_prevents_delete:failed-commit` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:64` | named red; matches plan |
| `Failed_attempt_still_consumes_count_cap` | `C826.Failed_attempt_still_consumes_count_cap:attempts` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:77` | named red; matches plan |
| `Byte_cap_is_shared_across_families` | `C826.Byte_cap_is_shared_across_families:one-delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:90` | named red; matches plan |
| `Oversized_root_is_not_truncated` | `C826.Oversized_root_is_not_truncated` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:101` | named red; matches plan |
| `Unknown_or_overflowed_size_is_kept` | `C826.Unknown_or_overflowed_size_is_kept:unknown` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:115` | named red; matches plan |
| `Hardlinks_reserve_conservatively` | `C826.Hardlinks_reserve_conservatively` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:134` | named red; matches plan |
| `Changed_file_identity_refuses_execution` | `C826.Changed_file_identity_refuses_execution` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:147` | named red; matches plan |
| `Revalidation_catches_new_write` | `C826.Revalidation_catches_new_write` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:160` | named red; matches plan |
| `Revalidation_catches_new_deny_or_hold` | `C826.Revalidation_catches_new_deny_or_hold` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:173` | named red; matches plan |
| `Revalidation_catches_live_owner_or_revoked_release` | `C826.Revalidation_catches_live_owner_or_revoked_release` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:186` | named red; matches plan |
| `Active_hold_preserves_owned_scratch` | `C826.Active_hold_preserves_owned_scratch` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:199` | named red; matches plan |
| `Expired_hold_requires_visible_disposition` | `C826.Expired_hold_requires_visible_disposition:reason` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:213` | named red; matches plan |
| `Partial_failure_keeps_custody_and_budget` | `C826.Partial_failure_keeps_custody_and_budget:receipt` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:229` | named red; matches plan |
| `Duplicate_request_returns_same_receipt` | `C826.Duplicate_request_returns_same_receipt` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:245` | named red; matches plan |
| `Concurrent_parent_removal_is_zero_reclaimed` | `C826.Concurrent_parent_removal_is_zero_reclaimed:outcome` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:257` | named red; matches plan |
| `Cursor_is_stable_without_starving_old_roots` | `C826.Cursor_is_stable_without_starving_old_roots` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:272` | named red; matches plan |
| `Eligible_scratch_is_removed_and_measured` | `C826.Eligible_scratch_is_removed_and_measured:measured` | `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs:283` | named red; matches plan |
| `Unknown_owner_prevents_delete` | `C826.Unknown_owner_prevents_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:16` | named red; matches plan |
| `Live_owner_prevents_delete` | `C826.Live_owner_prevents_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:26` | named red; matches plan |
| `Reused_pid_prevents_delete` | `C826.Reused_pid_prevents_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:35` | named red; matches plan |
| `Unknown_liveness_prevents_delete` | `C826.Unknown_liveness_prevents_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:44` | named red; matches plan |
| `Foreign_host_boot_or_namespace_prevents_delete` | `C826.Foreign_host_boot_or_namespace_prevents_delete` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:57` | named red; matches plan |
| `Dead_parent_with_live_executor_is_kept` | `C826.Dead_parent_with_live_executor_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:67` | named red; matches plan |
| `Missing_nested_custody_is_kept` | `C826.Missing_nested_custody_is_kept` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:79` | named red; matches plan |
| `Evidence_receipt_is_required_for_release` | `C826.Evidence_receipt_is_required_for_release` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:88` | named red; matches plan |
| `Owner_generation_change_refuses_claim` | `C826.Owner_generation_change_refuses_claim` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:101` | named red; matches plan |
| `Competing_lifecycle_and_daily_calls_share_claim` | `C826.Competing_lifecycle_and_daily_calls_share_claim:receipt` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:116` | named red; matches plan |
| `Slot_binding_blocks_daily_ownership` | `C826.Slot_binding_blocks_daily_ownership:active` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:129` | named red; matches plan |
| `Released_dead_owner_allows_scratch` | `C826.Released_dead_owner_allows_scratch` | `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs:138` | named red; matches plan |
| `Unpushed_commit_is_never_eligible` | `C826.Unpushed_commit_is_never_eligible:keep` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:33` | named red; matches plan |
| `Dirty_or_active_worktree_is_kept` | `C826.Dirty_or_active_worktree_is_kept` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:50` | named red; matches plan |
| `Classifier_preserves_guarded_classes_and_release_requirements` | `C826.Classifier_preserves_guarded_classes_and_release_requirements:protected` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:61` | named red; matches plan |
| `Registered_and_unregistered_candidates_are_fully_accounted` | `C826.Registered_and_unregistered_candidates_are_fully_accounted:orphan` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:80` | named red; matches plan |
| `Unavailable_owner_is_reported_without_retry_or_delete` | `C826.Unavailable_owner_is_reported_without_retry_or_delete:one-read` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:95` | named red; matches plan |
| `Refusing_owner_is_reported_without_retry_or_delete` | `C826.Refusing_owner_is_reported_without_retry_or_delete:one-read` | `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs:106` | named red; matches plan |
| `links a host cleanup summary to its run` | `C826.links a host cleanup summary to its run` | `client/src/features/attention/attentionVisuals.test.ts:100` | named red; matches plan |
| `shows cleanup disk pressure at the reported severity` | `C826.shows cleanup disk pressure at the reported severity:warning` | `client/src/features/attention/attentionVisuals.test.ts:108` | named red; matches plan |
| `keeps an expired cleanup hold visible for review` | `C826.keeps an expired cleanup hold visible for review` | `client/src/features/attention/attentionVisuals.test.ts:116` | named red; matches plan |
| `shows sustained worktree backlog with its responsible owner` | `C826.shows sustained worktree backlog with its responsible owner` | `client/src/features/attention/attentionVisuals.test.ts:125` | named red; matches plan |
