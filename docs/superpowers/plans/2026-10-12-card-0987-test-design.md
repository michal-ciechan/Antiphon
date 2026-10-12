# CARD-0987 verification design

TestDesign task: `88ffed25-4b28-4409-8a0d-598d8ba040a9`, 2026-10-12.
Frozen source: `7df4121900be5c7e7e212caaf60e4e03508c46de`.
Fix design: [remaining-slices plan](2026-10-12-card-0987-remaining-slices-plan.md).
This is an appended verification companion; it does not replace the fix design or authorize activation.
The CARD-0826 parent V/R identifiers remain stable. No production/test source was edited;
no build, test listing, test execution, mutation, deployment or deletion ran here.
Authenticated card/task reads were **bound**: ANTIPHON_TASK_TOKEN was present and used without printing it.
Source census is not executed/discovered coverage. A read-only Node census counted declarations/Arguments; the checkpoint importer was not executed. New payloads and PC recipes remain **binding required after Code**.

**Amendment 2026-10-12 (Plan task `04839ba2-4569-48eb-9870-445321244f5e`, from `c7e8ff5bbbad2956ec4a7d5fe37f73cd0b123930`).**
Findings 6 and 7 are resolved in the fix design (D-10..D-12): S1 is split into S1a (tool seam, the 14 tool-only ports)
and S1b (the unmoved sweep over the port, the 2 sweep-backed ports); S6 is split into S6a (maintenance closure) and S6b
(delivery verification after S5, S6a and S7). Rows changed by that amendment and nothing else: **CP-17 added**
(After S1a, 14 tool-only executions, own build, inserted before CP-1); **CP-1** After S1 -> S1b; **CP-8** After S6 -> S6a
and moved before CP-7; **CP-7** and **CP-9** After S6 -> S6b; the Checkpoints preamble; the Collision resolution rows for
S1/S2 and S6; the safe-order line; the R-1 guard row (adds CP-17); the Cost sums (137 -> 145, 149 -> 157, 1224 -> 1232,
15 -> 16 builds); and the Handoff audit's last-but-one bullet. The method ledger, the 54/60/30 census, every V/R
identifier, every G/PC binding and the 260-control roster are untouched. `After` tokens with suffixes are literal in
`AfterSelector`; select rows with `--rows CP-n`.

## Freeze findings and dispatch conditions

1. Compatibility source: **45 methods / 54 results** (21/27 sweep, 11/11 scope, 13/16 tool-copy).
   Target compatibility class is absent. The method-by-method port ledger below binds source names and argument rows;
   it does not claim the target exists. CP-4 is 80 = 12 + 14 + 54.
2. Service union: **34 present + 26 missing = 60**. Schema 2 and receipt regressions 2 are additional to the parent's 56.
   Missing methods are individually marked with their destination file below; no missing name is treated as green.
3. Client: **30**, not the plan's 29-or-30 conditional. There are 24 plain `it` declarations and one six-row `it.each`.
   `shows sustained worktree backlog with its responsible owner` already exists; it is not a new case.
   `WorktreeCleanupBacklog` is already kind 56; `HostCleanupBacklog` in S6 prose is not the wire enum name.
4. CP-4, CP-8 and CP-16 now have independent output/build rows. CP-12 is non-TUnit and needs no .NET build.
   CP-2 has two separately committed invocations (pre-move and post-move), each with its own isolated build and receipt.
5. `/api/attention` is intentionally fleet-global and accepts no board filter. V-6 checks the producer tuple in its
   authenticated response. Board isolation and pagination are asserted on the feature's report route and its scoped
   projection. Do not add a fictitious `/api/attention?boardId=...` isolation oracle or change existing feed semantics.
6. **S1 seam gap requiring Plan resolution before Code:** the 16 tool-copy ports include
   `nested_live_executor_vetoes_whole_root` and `unknown_nested_custody_vetoes_whole_root`, which call the
   actual `CheckpointTempRootSweep`. That class uses static File/Directory/FileStream/Stopwatch and is only in S2's
   footprint; S1's proposed tool-only seams cannot execute those two ports virtually. Move the *instance-I/O threading*
   for the still-unmoved sweep into a preparatory S1 scope, or revise the slice boundary/checkpoint grouping while
   retaining the 16/54/80 floors. Do not substitute a fixture implementation, call the physical test, or lower CP-1.
   The scope wrapper also needs its own instance port before CP-2; its current TempDir/DisposeAsync do physical I/O.
7. **S6 depends on S7's real local store/outbox/dispatcher.** Freeze dependencies as S7-before-S6; fake serialized
   success cannot close CP-7/CP-9. This only changes dispatch order, not the fix behavior. S5 can use scripted transport
   for registration/admission, but producer-to-runner delivery uses S7 and final end-to-end delivery closes in S6.
8. Existing bodies are weaker than some parent prose: the cross-family byte test currently uses two checkpoint roots;
   the cancellation scope test creates a canceled token without passing it; the ownership competition is sequential;
   the config test has no removable hard-deny setting; inventory missing-owner tests have no retirement tripwire;
   the event recorder drops payload/group. Code must bind the stated variants/attempt counters/barriers in the relevant
   fixtures before claiming those PC recipes executable. Keep the source-contract assertions, add the missing witness,
   and record the exact production line/argument row at the slice SHA. A present name is not proof of its full prose claim.

OD-1..OD-5 can be honored without operator change: accept 54/80; accept split plus 60-case union;
name CARD-1017 with owner_unavailable until availability exists; keep deploy/image/smoke work offline;
keep all seven expired historical holds as hold_expired_review_required. Unknown capability, identity,
configuration, hold resolution, liveness or qualification withholds destructive authority. Enabled and Execute
stay false in shipped configuration. OD defaults do not answer the S1 verification seam gap.

### Collision resolution

Live reads (2026-10-12) confirmed CARD-1017 InProgress, CARD-1032 InProgress and CARD-1033 Review.
The task detail reads are more specific: CARD-1032 S2 repair `cac7e685` and CARD-1033 S3 Code
`b113f1fa` are Dispatched. A Review card column is not proof that its file owners have landed.

| CARD-0987 slice | Prior owner that must land / exact collision | Safe dispatch gate |
|---|---|---|
| S1a/S1b/S2 | CARD-1199 active repair `71060864`: Checkpoints build/deletion confinement and run-checkpoint driver; its build-output cleanup path (`OutputCleanup.cs`, `CheckpointApp.cs`, `Program.cs`) is S1a's threading target | Its round-10 brief names BuildConfinement.cs and run-checkpoint.ps1; the repaired checkpoint caller graph is an integration dependency. Wait for its reviewed landing and re-read the changed caller graph before threading or extracting. Finding 6 is resolved by the S1a/S1b split (amendment). |
| S3/S4 | CARD-1017 foundation is already source-visible; inventory only names its owner | No authority transfer. If CARD-0692 scratch integration lands, bind to its exact released-scratch entry rather than add another deletion stage. |
| S5 | CARD-1033 **S17**: server/Program.cs and Infrastructure/Agents/HangfireConfiguration.cs; CARD-1017 foundation's composition first | Wait for the composition slice's reviewed landing, not merely current S3 completion. No AppDbContext/migration edit is planned here. |
| S6a / S6b | CARD-1033 **S7 and S11**: Application/Services/AgentTaskLandService.cs (S6a only); CARD-1017 local/publication owner | S6a: wait for both shared-file slices' land and no newer land-service owner; it needs nothing from S5 or S7. S6b: S5, S6a and S7 of CARD-0987 must be in its start tree (amendment). |
| S7 | CARD-1032 runner/mirror slices: SessionRunnerContracts.cs, PhoneHomeContracts.cs, PhoneHomeCommandDispatcher.cs, runner Program.cs, PhoneHomeRunnerClient.cs | S2 repair alone is insufficient: wait for later runner removal/fence/transport owners touching these paths to land. Re-read capability/operation allocations. CARD-1017's remote-owner work is carried by CARD-1032. |
| S8 | Relevant CARD-1199 driver repair; exact deployment/script/bundle owners at dispatch | CARD-1042 Review `defe909e` names four evidence test files + allowlist only: no source collision with this slice. Empty task scope is not absence; read any new owner brief, including CARD-1196 if active. |

After the seam repair and these gates, safe order is **S1a -> S1b -> S2 pre-move -> S2 move -> S3 -> S4 -> S7 -> S5 -> S6a -> S6b -> S8 -> S9 -> S10** (amended).
The S7/S5 relative order is deliberately conservative. Each starts from a source containing its prerequisites;
no rebase/reset of this fast-forward-only TestDesign branch is needed. Read fresh active footprints before each Code dispatch.
No human choice is needed for the OD defaults; the next stage is **Plan**, to make the S1 seam verifiable and adopt the dependency order. Done by the amendment above; the next stage is now Code (S1a, CP-17).

## Verification design

### Inspection

| Bodies read | Boundary -> verification or exclusion |
|---|---|
| Three complete physical compatibility test classes: CheckpointTempRootSweepTests, CheckpointTempScopeTests, CheckpointToolCopyCleanupTests; their local helpers; CheckpointTestScope/CheckpointTestBase; CheckpointFixtures.MarkRun | R-1's 54 source contracts; physical tests excluded; S1 seam gap above |
| Infrastructure HostCleanupFixture, VirtualCleanupFileSystem, RecordingCleanupPlanStore/ClaimStore; complete Policy/Execution/Ownership tests | V-1..V-3; observation versus mutation counters; existing clock advances wall time only |
| Application HostCleanupFixture and complete Orchestration/Report/Maintenance/WorktreeInventory tests; receipt DB interceptor, PausedInventoryProbe, PagedProbe/OwnerProbe | V-5..V-7/V-13; 34 present bodies; no public delivery fixture yet |
| HostCleanupService.IngestAsync/ReadAsync/PublishPendingAsync/PublishOneAsync; HostCleanupAttentionService.ReadAsync; AttentionEndpoints.MapAttentionEndpoints | V-6 and R-4; immutable digest, stale observation, pending invalidation, global-feed versus scoped-report distinction |
| CleanupFamilyRegistry and Linux/WindowsCleanupFileSystem bodies | V-1/V-4/V-9 and V-10/V-11; scratch grammar currently broad; native implementations currently throw |
| PhoneHomeCommandDispatcherTests admission, pre-ack restart/loss and host-stats bodies; Dispatcher/RecordingRuntime helpers; PhoneHomeTestHost Start/Register/ConnectPeer/WaitLive/Dispose | Nearest fixture for new runner files; V-8; reuse actual channel, replace scripted success peer with real dispatcher; do not reuse wall-clock WaitLive as a deadline proof |
| BuildSlotScriptTests, RunnerDrainScriptTests, CapabilityScriptTests and ScriptHarness.RunHarnessCaseAsync/Validate | Nearest fixtures for new Scripts files, V-9; named PASS receipts are assertions, not extra TUnit executions; use owned ScriptHarnessProcess, not naked child launch |
| Entire attentionVisuals.test.ts including item(), 24 plain titles and 6-row table | V-12; 30 presentation results; no transport or UI rendering claim |
| Parent plan, remaining-slices plan, testing/build schema and mutation owner; project/ops/session/orchestration owners; live card and scoped task briefs | CP/PC binding, collisions, scopes and evidence rules; no deployment/activation authority |

### Recipe, variant and reachability contract

Each ledger row below inherits one of these recipes; the row supplies its exact case, oracle, mutation,
source binding and classification. Variant tables use hand-written expected reasons/bytes/identities,
not expected = production decision. Start with all admission predicates positive; flip only the named fact.
Probe errors and unknown values preserve today's retention/withholding. Assertion labels use
`C987.<method>.<variant>.<boundary>` for new assertions and preserve landed `C826.*` labels.
A loop remains one TUnit result; only the three specified Arguments methods expand execution counts.

| Recipe | Fixture and variant | Reachability / classification |
|---|---|---|
| P | Existing Infrastructure HostCleanupFixture; known storage/file/generation; old released root, injected file observations; one age/ownership/cap/hold fact changed | Actual planner/executor/registry; Unit. FileSystem, claims and store record attempts before downstream veto. Positive eligible sibling proves the path ran. No host deletion. |
| C | New Infrastructure/HostCleanupCheckpointFixture.cs, CheckpointVirtualFixture; virtual v1 marker/index/run/state/evidence and injected monotonic clock/process control | Actual tool callers, still-unmoved then moved sweep, scope and assembly-hook composition; Unit. No default CheckpointTestBase, no new global override, no physical sweep. Caller-by-caller I/O invocation witnesses; missing seam is finding 6. |
| A | Existing Application/HostCleanupFixture.cs extended with isolated migrated DB, fresh service providers, mapped feature routes/auth and public AttentionService; known host/store/board/run/digests | Actual scheduler/service/maintenance/inventory; Integration for DB/wire/HTTP, existing pure inventory methods retain Unit. Class mixed scope is explicitly selected. Scheduler registrations recorded without running live schedules. |
| Q | A plus new HostCleanupTestServer.cs and runner command/local-store/outbox; reuse PhoneHomeTestHost registration/channel with a real dispatcher recipient | Integration. Real production enqueue/dequeue/recovery logic; block receiver on named barriers, inject failure before/after each durable boundary, recreate sender/receiver independently. No canned receipt. |
| U | New SessionRunner.Tests/HostCleanupFixture.cs; real dispatcher/command service and production local outbox over virtual persistent byte store; recording runtime; serialized HTTP/phone-home inputs | Unit when entirely virtual, Integration if a local socket is used. No session/provider launch. Local-store restart rebuilds from committed bytes, not a retained object graph. Server recipient evidence belongs to Q. |
| H | New Scripts/HostCleanupScriptFixture.cs; real wrappers executed via owned ScriptHarnessProcess and rejecting offline command shims | Integration + ParallelLimiter<ProcessSpawnLimit>; never inherit live tokens/endpoints/credentials. Synthetic input bodies only. Unexpected command records violation before refusing. No schedule install, SSH, Docker or filesystem remover reaches the host. |
| N | Extend Infrastructure/HostCleanupFixture.cs with HostCleanupNativeFixture and recording ICleanupNativeApi; synthetic handles/device/file IDs, explicit syscall barrier | Unit over real adapter with injected native port. Fake results prove adapter call/flag/order only. Native kernel qualification excluded. CP-15/16 use Windows as commissioned; only powershell.exe 5.1 intrinsically requires Windows. |
| UI | Existing item() DTO factory and real attentionVisuals functions | Vitest; presentation only. Exact target/group/bucket/icon/label oracle; no browser/delivery assertion. |

Deadline tests use TimeProvider.GetTimestamp/GetElapsedTime or an explicit monotonic fake, separate from
calendar-day UTC/DST inputs. Fake timers advance scheduled callbacks. Barrier entry, assertion, release,
and owned-task join are explicit; elapsed wall time is a harness bound, not success or expected red.
Do not pass the current frozen HostCleanupClock into queue polling/timers unchanged.

### Delivery inventory

Durable join key: **BoardId + HostId + StorageId + RunnerStoreId + ProcessBootId + RunId + LocalDate +
ConfigDigest + PlanDigest + ReceiptDigest** as appropriate to the handoff. Candidate claims additionally
bind canonical file identity + owner generation. A replaced host/store/boot/config is a refusal, not a new retry target.

| Path | Producer -> destination; persistence boundary | Recovery and observable recipient / tests / positive controls |
|---|---|---|
| DQ-1 daily request | HostCleanupScheduler/Job -> real PhoneHomeLiveConnection outbound channel -> dispatcher/HostCleanupCommandService; server run committed before send, runner claim and complete plan before first delete | Busy dispatcher and already-ready variants in Lost_reply_returns_existing_receipt and Restart_catchup_runs_once_per_local_day; failed enqueue, crash before send, loss after acceptance and reconnect reuse RunId. Recipient plan/typed immutable outcome and exactly one virtual deletion, then DQ-2, prove receipt. PC-16/37/38/68 plus dedicated enqueue/busy controls below. |
| DQ-2 local outcome upload | Runner/host worker -> production HostCleanupLocalStore/outbox -> authenticated HostCleanupEndpoints receipt route -> HostCleanupService; plan/claim, delete outcome, durable receipt, outbox obligation, ingestion transaction in that order | Ready/busy V-6 tests drive producer through real queue to report GET and global attention GET matching the join key. Crash after receipt commit/before enqueue, enqueue throw, request-send failure, rollback before ingestion commit, commit before ACK loss, receipt-write failure after delete each have explicit variants. Fresh services recover committed bytes; uncertain pre-receipt claim withholds re-delete. PC-83/84/101 plus ready/busy/enqueue controls below. |
| DQ-3 ingestion notification | HostCleanupService -> persisted InvalidationPending -> real recovery drain -> IEventBus subscriber -> authenticated attention GET via AttentionService | Crash before publish, publish throw, success then pending-clear failure; retry invalidation may repeat but report identity stays one. Recipient GET must contain exact producer RunId/BoardId and subscriber payload must match. PC-85 plus feed-composition and clear-failure controls below. |
| DQ-4 calendar projection | Complete immutable daily reports -> stored per-storage/date observations -> backlog/pressure/hold projection -> public feed | Fresh DB context on restart, out-of-order upload and re-upload preserve original observation; day gap/incomplete/stale sample do not fabricate recovery. V-6 day/window/recovery tests, PC-74..80/122. |
| DQ-5 offline host bridge | Actual host-cleanup-server2 wrapper -> recorded SSH command invoking shared worker -> same durable receipt protocol DQ-2 | H proves literal host configuration, no socket/volume authority and exit/redaction/cutover order; Q injects host-origin receipt through same ingress/outbox protocol. It does not prove real SSH/Windmill delivery. PC for Windmill bridge/smoke/status below. |

**Evidence ORDER:** admission facts -> committed plan -> candidate reservation/claim -> final identity/epoch
recheck -> recorded virtual syscall -> committed immutable receipt -> durable upload obligation -> authenticated
ingestion commit -> pending notification -> observed matching invalidation -> fresh report/attention GET.
Assert indices at named barriers; a request, insert, event, Sent flag or ACK is not the final verdict.
For busy cases assert no recipient row before release, then drain deterministically and assert exact tuple and contents.
For ready cases do not require a later unrelated wakeup. Exercise enqueue exception separately from process restart.
Retire neither upload nor notification debt just because transport returned 200; bind returned identity/digest.

Substitutes: virtual files model commit/restart but not fsync/power loss; loopback byte transport replaces WAN outages,
not production queues; recording recurring-job manager proves registration but not Hangfire timers;
recording IEventBus subscriber proves publication/payload but not browser repaint; authenticated fresh HTTP GET proves
server recipient content but not a user reading it; offline SSH shim proves argv, not remote execution.
A fixture calling IngestAsync then standalone AttentionAsync directly cannot close Q.
No session input is introduced. If Code adds one, this design must be extended with matching **complete UserPrompt**
transcript evidence, same session/generation and attempt floor; ACK, partial/header-only transcript or queue rows cannot substitute.

### Proves it works now

These are required future V/R runs, **not a claim of present success**. Exact names below have been source-bound;
PRESENT means the body exists, not that its complete intended contract was executed or mutation-qualified.
MISSING is a current-tree implementation defect recorded for the named Code slice. PORT REQUIRED means the source
body exists at the listed physical file but the virtual destination is absent. No spelling-only mismatch was found.

| ID | Behavior / layer | Command selection | Expected results |
|---|---|---|---|
| V-1/V-2 | Policy/execution virtual facts | CP-13 | 22 + 20 = 42 |
| V-3 | Ownership/claim virtual facts | CP-4 | 12 within 80 |
| V-4 | Actual checkpoint adapter/caller composition | CP-3/4 | 14 within 80 |
| V-5 | Scheduling/auth/qualification + schema | CP-6/9 | 16 within 60 |
| V-6 | Receipt immutability, producer-to-HTTP recipient, recovery | CP-7/9 | 22 within 60 |
| V-7 | Maintenance service and actual land caller at syscall barrier | CP-8/9 | 10 within 60 |
| V-8 | Actual runner dispatcher/command/local queue | CP-10 | 12 |
| V-9 | Offline real worker/wrapper entry points | CP-11 | 12 |
| V-10 | Linux adapter with virtual native port | CP-14 | 6 |
| V-11 | Windows adapter and actual PowerShell 5.1 parser/wrapper | CP-15/16 | 6 + 1 |
| V-12 | Attention DTO presentation | CP-12 | 30 Vitest |
| V-13 | Mixed daily graph, inventory and read-only Git | CP-5/9 | 12 within 60 |

#### Method ledger

For each class, the file line is the full destination path. Recipe supplies setup/variant/reachability/classification;
the case name and expected outcome are below. The PC column is the case -> compiling mutation -> exact red assertion
join into Positive controls; no test is accepted without that join. Arguments are literal source rows, not new counts.


**HostCleanupPolicyTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupPolicyTests.cs`; recipe P; 22 present / 22 required methods; 22 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Unknown_family_is_reported_without_delete` | PRESENT; 1 result | Unknown registered-looking name remains family_unknown. | PC-1 |
| `Runtime_deny_overrides_owned_family` | PRESENT; 1 result | Every D-3 hard deny, including configured aliases, wins over an allow match; no delete intent. | PC-2 |
| `Parent_containing_runtime_state_is_kept` | PRESENT; 1 result | A disposable-looking parent containing a denied child is protected. | PC-3 |
| `Child_of_protected_root_is_kept` | PRESENT; 1 result | A matched descendant of a deny root is protected. | PC-23 |
| `Traversal_cannot_escape_configured_root` | PRESENT; 1 result | Reject ../, rooted override and sibling-prefix paths before observation/deletion. | PC-24 |
| `Link_at_any_depth_is_kept` | PRESENT; 1 result | Root, ancestor and descendant link fixtures are Unknown, never followed. | PC-25 |
| `Foreign_mount_is_kept` | PRESENT; 1 result | An admitted path on a different mount/storage identity is refused. | PC-26 |
| `New_nested_file_keeps_old_directory` | PRESENT; 1 result | 48-hour folder plus 1-hour hidden nested file is recent_write. | PC-4 |
| `Exactly_twenty_four_hours_is_kept` | PRESENT; 1 result | newestWrite == cutoff is ineligible; cutoff minus one tick is eligible. | PC-5 |
| `Future_timestamp_is_kept` | PRESENT; 1 result | A future nested write cannot become old by duration absolute-value arithmetic. | PC-27 |
| `Empty_root_requires_old_valid_marker` | PRESENT; 1 result | Old bound marker admits empty root; absent/fresh marker does not. | PC-28 |
| `Unreadable_entry_is_not_empty` | PRESENT; 1 result | Access-denied subtree yields incomplete/Unknown, no estimate of zero. | PC-29 |
| `Traversal_budget_exhaustion_is_unknown` | PRESENT; 1 result | Candidate/depth/time bounds stop traversal and retain custody. | PC-30 |
| `Newest_write_includes_hidden_files_and_marker` | PRESENT; 1 result | Hidden evidence and original marker timestamp participate in maximum. | PC-31 |
| `Configuration_cannot_relax_hard_guards` | PRESENT; 1 result | Negative/zero caps, missing roots, removed denies and blanket globs are rejected. | PC-32, PC-244 |
| `Unregistered_msbuild_testcontainers_and_tmp_are_unknown` | PRESENT; 1 result | Name alone never owns global MSBuildTemp, Testcontainers or standalone tmp. | PC-124 |
| `Private_cache_requires_exact_package_and_scratch_ownership` | PRESENT; 1 result | Keep shared cache/locks; allow only released exact private pair. | PC-33 |
| `Retained_evidence_is_never_scratch` | PRESENT; 1 result | Reports and deliverables remain protected even under bin-*. | PC-34 |
| `Per_host_roots_are_explicit_and_nonoverlapping` | PRESENT; 1 result | Desktop, runner and host roots bind storage ID; missing roots never default to /tmp. | PC-35 |
| `All_registered_owned_families_can_be_eligible` | PRESENT; 1 result | Each of seven D-3 family adapters yields eligible with all authority supplied. | PC-125 |
| `Older_owned_root_is_eligible` | PRESENT; 1 result | Positive old released scratch candidate reaches executor plan. | PC-126 |
| `Daily_plan_uses_injected_observations_only` | PRESENT; 1 result | No direct OS I/O or system clock in the planner; reordered facts give same plan. | PC-127 |

**HostCleanupExecutionTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupExecutionTests.cs`; recipe P; 20 present / 20 required methods; 20 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Dry_run_has_zero_mutating_calls` | PRESENT; 1 result | All filesystem, claim, cursor, budget and store mutation counters stay zero. | PC-9 |
| `Explicit_plan_file_is_the_only_preview_write` | PRESENT; 1 result | One caller-requested output outside candidates; inside-candidate output refused. | PC-36 |
| `Plan_is_durable_before_first_delete` | PRESENT; 1 result | Committed complete plan event strictly precedes every destructive intent. | PC-37 |
| `Plan_persist_failure_prevents_delete` | PRESENT; 1 result | Failed flush/commit gives zero attempts and preserved virtual contents. | PC-38 |
| `Failed_attempt_still_consumes_count_cap` | PRESENT; 1 result | Ten attempts including partial failures; eleventh is count_cap. | PC-10 |
| `Byte_cap_is_shared_across_families` | PRESENT; 1 result | Two 6-GiB roots under 10 GiB allow one; reservation survives retry. | PC-11 |
| `Oversized_root_is_not_truncated` | PRESENT; 1 result | One 11-GiB root under 10 GiB remains byte-identical; no truncate intent. | PC-39 |
| `Unknown_or_overflowed_size_is_kept` | PRESENT; 1 result | Unknown allocated bytes or checked overflow refuses reservation. | PC-40 |
| `Hardlinks_reserve_conservatively` | PRESENT; 1 result | Reserve max(logical,allocated) without optimistic deduplication. | PC-41 |
| `Changed_file_identity_refuses_execution` | PRESENT; 1 result | Replacing a scanned root preserves replacement and reports identity_changed. | PC-14 |
| `Revalidation_catches_new_write` | PRESENT; 1 result | Write injected after plan causes recent_write and zero delete intents. | PC-42 |
| `Revalidation_catches_new_deny_or_hold` | PRESENT; 1 result | Deny/hold revision injected after plan preserves candidate. | PC-43, PC-213 |
| `Revalidation_catches_live_owner_or_revoked_release` | PRESENT; 1 result | New live status or revoked release before first syscall refuses. | PC-44, PC-214 |
| `Active_hold_preserves_owned_scratch` | PRESENT; 1 result | Exact active hold also protects independently marked descendants. | PC-13 |
| `Expired_hold_requires_visible_disposition` | PRESENT; 1 result | All seven historical prefixes retain unresolved/expired hold evidence and zero deletes. | PC-12 |
| `Partial_failure_keeps_custody_and_budget` | PRESENT; 1 result | Partial outcome retains marker, attempt and reserved bytes across resume. | PC-45, PC-250 |
| `Duplicate_request_returns_same_receipt` | PRESENT; 1 result | Same run/candidate identity repeats immutable receipt and no second delete. | PC-128 |
| `Concurrent_parent_removal_is_zero_reclaimed` | PRESENT; 1 result | Independent worktree removal returns AlreadyAbsent with zero attributed bytes. | PC-46 |
| `Cursor_is_stable_without_starving_old_roots` | PRESENT; 1 result | A bounded resumed scan reaches old roots despite newer arrivals. | PC-129 |
| `Eligible_scratch_is_removed_and_measured` | PRESENT; 1 result | Only eligible virtual bytes disappear; measured receipt differs from reservation. | PC-130 |

**HostCleanupOwnershipTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupOwnershipTests.cs`; recipe P; 12 present / 12 required methods; 12 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Unknown_owner_prevents_delete` | PRESENT; 1 result | Unmarked/unowned root retained, never retrospectively adopted. | PC-47 |
| `Live_owner_prevents_delete` | PRESENT; 1 result | AliveSame retains sentinel with owner_live. | PC-6 |
| `Reused_pid_prevents_delete` | PRESENT; 1 result | Same PID/different start generation retains root. | PC-48 |
| `Unknown_liveness_prevents_delete` | PRESENT; 1 result | Probe failure is Unknown, not Dead. | PC-49 |
| `Foreign_host_boot_or_namespace_prevents_delete` | PRESENT; 1 result | Each mismatched identity component refuses local-dead inference. | PC-50, PC-215 |
| `Dead_parent_with_live_executor_is_kept` | PRESENT; 1 result | Nested live executor veto survives dead starter. | PC-7 |
| `Missing_nested_custody_is_kept` | PRESENT; 1 result | Run-shaped child missing valid custody blocks whole root. | PC-51 |
| `Evidence_receipt_is_required_for_release` | PRESENT; 1 result | Settled task without preserved/delivered evidence is retained. | PC-52 |
| `Owner_generation_change_refuses_claim` | PRESENT; 1 result | Different owner generation cannot reuse old path authority. | PC-53 |
| `Competing_lifecycle_and_daily_calls_share_claim` | PRESENT; 1 result | Barrier-controlled daily/self-clean/post-land calls yield exactly one mutation sequence. | PC-54 |
| `Slot_binding_blocks_daily_ownership` | PRESENT; 1 result | Active/idle/quarantined slot stays outside generic scratch authority. | PC-55 |
| `Released_dead_owner_allows_scratch` | PRESENT; 1 result | Full dead/released/evidence-preserved facts permit candidate. | PC-131, PC-258 |

**HostCleanupCheckpointAdapterTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointAdapterTests.cs`; recipe C; 0 present / 14 required methods; 14 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `V1_markers_and_nested_executor_records_round_trip` | MISSING / BINDING REQUIRED; 1 result | Existing case/camel-case formats, binding, state and version remain compatible. | PC-132 |
| `Daily_age_is_stricter_than_lifecycle_grace` | MISSING / BINDING REQUIRED; 1 result | One-hour root passes lifecycle grace but daily retains it; 25-hour root qualifies. | PC-56 |
| `Daily_uses_shared_budget_without_lifecycle_truncation` | MISSING / BINDING REQUIRED; 1 result | Daily uses whole-candidate reservation; original lifecycle partial truncation stays isolated. | PC-133 |
| `Original_root_gate_serializes_all_entry_points` | MISSING / BINDING REQUIRED; 1 result | Scope/lifecycle/daily share root gate; busy attempt records retained. | PC-57 |
| `Preview_does_not_run_startup_sweep` | MISSING / BINDING REQUIRED; 1 result | No index migration, marker update, gate creation or cursor write during preview. | PC-134 |
| `Released_task_scratch_and_nested_checkpoint_deduplicate` | MISSING / BINDING REQUIRED; 1 result | Parent and nested root cannot both reserve/reclaim the same bytes. | PC-135 |
| `Current_executor_image_is_retained_after_extraction` | MISSING / BINDING REQUIRED; 1 result | Actual shared image guard keeps current executable; a sibling is eligible. | PC-59 |
| `Unsupported_marker_version_is_retained` | MISSING / BINDING REQUIRED; 1 result | Unknown versions/invalid path/run IDs fail closed. | PC-60 |
| `Lifetime_hook_and_linked_host_use_shared_core` | MISSING / BINDING REQUIRED; 1 result | Resolve real hook/scope wrapper composition with fake dependencies; same shared implementation type. | PC-136 |
| `Shared_library_has_no_test_or_tool_dependency` | MISSING / BINDING REQUIRED; 1 result | Assembly references exclude TUnit, test assembly, checkpoint executable and kill APIs. | PC-58 |
| `Evidence_wrapper_and_direct_cleanup_share_image_guard` | MISSING / BINDING REQUIRED; 1 result | Both actual public entry points use shared guard and identical outcomes. | PC-137 |
| `Start_rollback_retains_unknown_launch_custody` | MISSING / BINDING REQUIRED; 1 result | Actual starter errors with launch-attempted/Unknown keep tool/root and records. | PC-61 |
| `Executor_ack_is_persisted_before_recovery_can_remove` | MISSING / BINDING REQUIRED; 1 result | Lost/failed ack keeps Unknown; confirmed dead ack allows virtual recovery. | PC-62 |
| `Every_cleanup_caller_accepts_virtual_io` | MISSING / BINDING REQUIRED; 1 result | Actual wait/start/stop/evidence/age-clean/scope/hook paths never hit native delete; counters prove each exercised. | PC-138 |

**HostCleanupOrchestrationTests** — `tests/Antiphon.Tests/Application/HostCleanupOrchestrationTests.cs`; recipe A; 2 present / 16 required methods; 16 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Main_only_registers_daily_jobs_and_monitor` | MISSING / BINDING REQUIRED; 1 result | Recording scheduler sees stable daily per-host IDs/offset and one 15-minute monitor; nonmain zero. | PC-64, PC-241 |
| `Storage_aliases_share_one_job_and_claim_namespace` | MISSING / BINDING REQUIRED; 1 result | Two runner aliases of one store cannot schedule competing ownership. | PC-63 |
| `Retirement_and_claude_schedules_are_unchanged` | MISSING / BINDING REQUIRED; 1 result | CARD-0459 ID/cron/Execute and Claude archival remain byte-identical. | PC-65 |
| `Weekly_cutover_requires_confirmed_disable` | MISSING / BINDING REQUIRED; 1 result | Old build-junk enabled/unknown refuses new execute admission; no unsafe rollback enable. | PC-66 |
| `Unsupported_runner_is_deferred` | MISSING / BINDING REQUIRED; 1 result | Missing capability produces visible Unsupported and zero commands. | PC-139 |
| `Offline_runner_is_deferred` | MISSING / BINDING REQUIRED; 1 result | Unavailable transport produces Deferred, not empty-success inventory. | PC-140 |
| `Host_bridge_excludes_runner_volumes` | MISSING / BINDING REQUIRED; 1 result | Host registry excludes all Docker volume identities; runner owns its configured mounts. | PC-67 |
| `Restart_catchup_runs_once_per_local_day` | MISSING / BINDING REQUIRED; 1 result | Restart/fall-back clock/retry chooses one current daily run, never all missed days. | PC-68, PC-234 |
| `Task_token_cannot_control_another_owner_or_host` | MISSING / BINDING REQUIRED; 1 result | Preview/status/register/run/hold/maintenance/ingestion authorization remains scoped. | PC-69 |
| `Ambiguous_historical_prefix_remains_held` | MISSING / BINDING REQUIRED; 1 result | Resolve each of seven prefixes uniquely; ambiguous/missing remains held including descendants. | PC-70 |
| `Each_namespace_requires_its_own_dry_run` | MISSING / BINDING REQUIRED; 1 result | A qualified desktop cannot enable runner/host; offline namespace stays pending. | PC-71 |
| `Activation_binds_storage_and_config_digest` | MISSING / BINDING REQUIRED; 1 result | Changed roots/store/config invalidate old dry-run receipt. | PC-72, PC-216 |
| `Dry_run_success_does_not_enable_execute` | MISSING / BINDING REQUIRED; 1 result | Only explicit operator change after matching qualification permits scratch execute. | PC-73 |
| `Scheduled_preview_persists_report_after_pure_scan` | MISSING / BINDING REQUIRED; 1 result | Scan writes zero; explicit wrapper saves returned document and publishes report. | PC-141 |
| `Cleanup_model_has_namespace_uniqueness_and_candidate_custody` | PRESENT; 1 result | Unique StorageId/LocalDate daily index; candidate FK Restrict; four feature tables present. | PC-175 |
| `AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | PRESENT; 1 result | Migration preserves seeded task/event/InputBody; HasPendingModelChanges is false. | PC-176 |

**HostCleanupReportTests** — `tests/Antiphon.Tests/Application/HostCleanupReportTests.cs`; recipe A + Q; 17 present / 22 required methods; 22 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Backlog_opens_on_third_complete_local_day` | PRESENT; 1 result | At 20 GiB: days one/two closed; day three opens one owner-attributed episode. | PC-74 |
| `Backlog_counts_one_observation_per_local_day` | PRESENT; 1 result | Repeated runs and DST repeated hour cannot advance streak. | PC-75 |
| `Missing_day_breaks_backlog_streak` | PRESENT; 1 result | Day gap restarts qualification and raises freshness condition. | PC-76 |
| `Incomplete_inventory_cannot_open_or_clear_backlog` | PRESENT; 1 result | Unknown/offline/incomplete day cannot count or clear an existing episode. | PC-77 |
| `Fresh_below_threshold_inventory_clears_backlog` | PRESENT; 1 result | 19 GiB fresh complete sample clears; exact threshold remains open. | PC-142 |
| `Backlog_window_and_threshold_are_configurable` | PRESENT; 1 result | Injected 2-day/7-GiB configuration changes exact boundary. | PC-143 |
| `Reclaimed_bytes_exclude_worktree_backlog` | PRESENT; 1 result | Eligible worktree bytes separate from measured scratch removal and fresh post-run sample. | PC-78 |
| `Partial_receipt_is_not_complete_success` | PRESENT; 1 result | Incomplete traversal/deletion retains explicit outcomes and custody. | PC-144 |
| `Null_sample_never_becomes_zero_usage` | PRESENT; 1 result | Missing usage/free bytes retain Unknown and sample age. | PC-79 |
| `Budget_pressure_has_one_episode_and_fresh_recovery` | PRESENT; 1 result | 60% warning, 90% critical, one episode; stale good sample cannot clear. | PC-80 |
| `Post_run_low_space_never_widens_deletion` | MISSING / BINDING REQUIRED; 1 result | max(50 GiB,10%) threshold alerts without extra attempts or reduced age. | PC-81 |
| `Receipt_replay_is_idempotent` | PRESENT; 1 result | Same run/digest once; same persisted rows and attention identity after reconnect. | PC-145 |
| `Changed_receipt_digest_is_refused` | PRESENT; 1 result | Same run/different digest conflicts and preserves original receipt. | PC-82 |
| `Receipt_to_attention_reaches_busy_recipient` | MISSING / BINDING REQUIRED; 1 result | Real local outbox pauses delivery, then drains to real ingestion and attention GET containing run ID. | PC-146, PC-232 |
| `Receipt_to_attention_reaches_ready_recipient` | MISSING / BINDING REQUIRED; 1 result | Real local outbox immediately delivers, attention GET and invalidation use same run/board. | PC-147, PC-236, PC-251 |
| `Crash_before_enqueue_recovers_pending_receipt` | MISSING / BINDING REQUIRED; 1 result | Restart store/service between receipt commit and outbox enqueue eventually delivers once. | PC-83, PC-231, PC-259 |
| `Crash_after_ingestion_before_ack_queries_status` | MISSING / BINDING REQUIRED; 1 result | Lost acknowledgement queries run status; no repeat execution; one consumer row. | PC-84, PC-252 |
| `Crash_before_invalidation_recovers_attention_delivery` | PRESENT; 1 result | Committed report survives event failure; recovery publishes invalidation and fresh GET proves item. | PC-85, PC-233 |
| `Attention_is_board_scoped_paginated_and_redacted` | PRESENT; 1 result | Two boards, multi-page report, no foreign root payload/credentials/raw stderr in attention. | PC-86, PC-217, PC-218 |
| `Missed_host_and_expired_hold_stay_visible` | PRESENT; 1 result | No fresh complete run and expired unresolved hold remain actionable across restart. | PC-148 |
| `Delayed_complete_inventory_cannot_clear_open_backlog` | PRESENT; 1 result | C826.delayed-recovery-cannot-clear-backlog remains true; stored Complete == false. | PC-122 |
| `Receipt_candidates_are_snapshotted_before_database_await` | PRESENT; 1 result | Persisted CanonicalPath == /virtual/original and replay of original snapshot returns same RunId. | PC-121 |

**HostCleanupMaintenanceTests** — `tests/Antiphon.Tests/Application/HostCleanupMaintenanceTests.cs`; recipe A; 9 present / 10 required methods; 10 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Land_intent_prevents_cleanup_admission` | PRESENT; 1 result | Committed land epoch before admission gives Deferred and zero delete intents. | PC-15 |
| `Restart_intent_prevents_cleanup_admission` | PRESENT; 1 result | Restart/deploy intent before admission gives Deferred. | PC-87 |
| `Land_waits_only_for_bounded_inflight_delete` | MISSING / BINDING REQUIRED; 1 result | At syscall barrier land lease acquisition count 0; release lets land proceed before next candidate. | PC-88 |
| `Cleanup_yields_to_pending_maintenance` | PRESENT; 1 result | Pending intent prevents second candidate even while inventory remains. | PC-89 |
| `Stale_epoch_refuses_delete` | PRESENT; 1 result | Changed epoch between plan/reservation and syscall retains root. | PC-90 |
| `Lost_connection_does_not_expire_live_claim` | PRESENT; 1 result | Clock past TTL cannot authorize another worker without positive custody reconciliation. | PC-91 |
| `Restart_requires_positive_custody_reconciliation` | PRESENT; 1 result | Unknown previous worker disables destructive work until confirmed stopped. | PC-92 |
| `Independent_clients_observe_the_same_host_gate` | PRESENT; 1 result | Two service providers/DB contexts and worker-client identities see one epoch and claim. | PC-149 |
| `Gate_is_released_on_success_and_failure` | PRESENT; 1 result | Bounded operation releases only with confirmed outcome, exceptions leave visible uncertainty. | PC-150, PC-253, PC-254, PC-255 |
| `Inventory_never_holds_land_admission` | PRESENT; 1 result | Paused inventory does not own destructive gate; land proceeds without waiting for scan. | PC-151 |

**HostCleanupWorktreeInventoryTests** — `tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs`; recipe A; 6 present / 12 required methods; 12 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Full_run_never_acquires_repository_lease` | MISSING / BINDING REQUIRED; 1 result | Preview/execute/retry/recovery/refusal fixture graph: both lease overload attempts 0 while eligible virtual scratch removed. | PC-19, PC-212 |
| `Daily_graph_has_no_direct_lease_or_lock_escape` | MISSING / BINDING REQUIRED; 1 result | Reachable daily components have no concrete lease construction, landing.lock open, raw destructive I/O or retirement invocation. | PC-93 |
| `Would_remove_worktree_is_preserved_in_execute_mode` | MISSING / BINDING REQUIRED; 1 result | Tree/source/gitdir/refs/registration snapshots identical; count/bytes/owner present; worktree executor dispatch attempts and reclaimed worktree bytes both 0. | PC-22 |
| `Would_remove_worktree_is_preserved_in_dry_run` | MISSING / BINDING REQUIRED; 1 result | Same inventory with all mutation counters zero. | PC-152 |
| `Unavailable_owner_is_reported_without_retry_or_delete` | PRESENT; 1 result | One owner-status read; owner_unavailable; no retirement/remove/enqueue; preserve sentinels. | PC-20 |
| `Refusing_owner_is_reported_without_retry_or_delete` | PRESENT; 1 result | One read, exact bounded refusal code, zero submission/removal; immediate call-count assertion. | PC-21 |
| `Unpushed_commit_is_never_eligible` | PRESENT; 1 result | Old clean terminal candidate with ahead tip is keep/unpushed. | PC-8 |
| `Dirty_or_active_worktree_is_kept` | PRESENT; 1 result | Dirty source/untracked source, queued/dispatched/blocked task, live/unknown session, pending land/recovery each veto. | PC-94, PC-219, PC-220, PC-221, PC-248 |
| `Classifier_preserves_guarded_classes_and_release_requirements` | PRESENT; 1 result | Real CARD-0665 classifier; Protected wins over Evidence/Disposable; pushed nonmaster needs exact release/evidence. | PC-95, PC-222, PC-223, PC-249 |
| `Registered_and_unregistered_candidates_are_fully_accounted` | PRESENT; 1 result | Paged registered/mirror/orphan/detached inventory complete or explicit Unknown; slots never scratch. | PC-153 |
| `Read_only_git_never_fetches_or_mutates_refs` | MISSING / BINDING REQUIRED; 1 result | GIT_OPTIONAL_LOCKS=0; missing objects/network/stale observation is Unknown, forbidden Git calls 0. | PC-96 |
| `Only_owned_scratch_child_is_deleted` | MISSING / BINDING REQUIRED; 1 result | Old released child removed virtually; held/unreleased children and all worktree metadata preserved. | PC-154 |

**HostCleanupCommandTests** — `tests/Antiphon.SessionRunner.Tests/HostCleanupCommandTests.cs`; recipe U; 0 present / 12 required methods; 12 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Missing_capability_refuses_cleanup` | MISSING / BINDING REQUIRED; 1 result | Dispatch fails Unsupported before any root observation. | PC-97 |
| `Request_cannot_supply_arbitrary_roots` | MISSING / BINDING REQUIRED; 1 result | Only configured host ID; path injection rejected. | PC-98, PC-256 |
| `Foreign_host_or_store_is_refused` | MISSING / BINDING REQUIRED; 1 result | Authenticated command bound to wrong host/store does nothing. | PC-99, PC-224 |
| `Changed_owner_generation_is_refused` | MISSING / BINDING REQUIRED; 1 result | Expected generation mismatch cannot resume/delete. | PC-155 |
| `Dry_run_dispatch_has_zero_writes` | MISSING / BINDING REQUIRED; 1 result | Both HTTP and phone-home paths return same pure inventory. | PC-156 |
| `Lost_reply_returns_existing_receipt` | MISSING / BINDING REQUIRED; 1 result | Actual dispatcher/client response loss returns same run/receipt; one delete sequence. | PC-16, PC-235 |
| `Restart_replays_report_without_redeleting` | MISSING / BINDING REQUIRED; 1 result | Recreate runner service and drain real outbox over virtual durable store; no second execution. | PC-157 |
| `Cleanup_never_launches_or_stops_a_session` | MISSING / BINDING REQUIRED; 1 result | Recording runtime start/kill/release/input calls all zero on success/failure/retry. | PC-100 |
| `Desktop_and_phone_home_share_command_service` | MISSING / BINDING REQUIRED; 1 result | Serialized equivalent requests use same service and identical outcomes. | PC-158 |
| `Busy_upload_queue_retains_order_and_identity` | MISSING / BINDING REQUIRED; 1 result | Busy recipient keeps durable outbox entry; ready replay preserves run/digest. | PC-159 |
| `Receipt_commit_failure_retains_claim_without_reexecution` | MISSING / BINDING REQUIRED; 1 result | Crash before receipt commit leaves uncertain claim; reconciliation cannot assume success. | PC-101 |
| `Command_crash_before_plan_commit_deletes_nothing` | MISSING / BINDING REQUIRED; 1 result | Worker restart before durable plan recovers a retained candidate without mutation. | PC-160 |

**HostCleanupScriptTests** — `tests/Antiphon.Tests/Scripts/HostCleanupScriptTests.cs`; recipe H; 0 present / 12 required methods; 12 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Daemon_and_autostart_scripts_are_ascii` | MISSING / BINDING REQUIRED; 1 result | Every byte of scoped daemon/autostart/wrapper scripts <128; enumerate exact paths. | PC-102 |
| `Scratch_registration_refuses_foreign_owner_and_late_adoption` | MISSING / BINDING REQUIRED; 1 result | Foreign owner or used unregistered root denied; no native call. | PC-103, PC-123, PC-225, PC-257 |
| `Scratch_release_requires_joined_children_and_preserved_evidence` | MISSING / BINDING REQUIRED; 1 result | Pending children/evidence debt prevent clean; exact owner only. | PC-104, PC-226, PC-260 |
| `Legacy_build_junk_wrapper_uses_shared_worker_only` | MISSING / BINDING REQUIRED; 1 result | Actual wrapper forwards host/dry-run and returns receipt; robocopy/Remove-Item/native-delete calls 0. | PC-105 |
| `Installer_refuses_registration_but_allows_explicit_uninstall` | MISSING / BINDING REQUIRED; 1 result | Default exits refusal with Hangfire/Windmill message; scheduled-task creation counters 0. | PC-106 |
| `Windmill_bridge_uses_host_roots_and_no_docker_socket` | MISSING / BINDING REQUIRED; 1 result | Offline SSH command passes only host config ID; volumes excluded; no schedule import. | PC-161 |
| `Smoke_reports_separate_tmp_and_host_disk_identity` | MISSING / BINDING REQUIRED; 1 result | Exact sample identities, age/completeness and image SHA; container free never relabelled host. | PC-162 |
| `Status_or_smoke_never_redeploys_or_registers_schedule` | MISSING / BINDING REQUIRED; 1 result | Status/dry-run/smoke leave deploy/scheduler counters zero. | PC-163 |
| `All_restart_entry_points_publish_and_drain_maintenance` | MISSING / BINDING REQUIRED; 1 result | Actual AppHost/runner/deploy wrappers publish epoch before stop/restart; failure does not clear uncertainty. | PC-107, PC-227 |
| `Helper_preserves_literal_paths_and_exit_codes` | MISSING / BINDING REQUIRED; 1 result | Spaces, brackets, apostrophes and backslash Windows roots bind as one exact argument. | PC-164 |
| `Weekly_schedule_cutover_is_single_owner_and_reversible_to_dry_run` | MISSING / BINDING REQUIRED; 1 result | Offline cutover receipt proves old disabled before new enable; failure leaves new dry-run. | PC-165 |
| `Protected_payloads_never_reach_script_output` | MISSING / BINDING REQUIRED; 1 result | Synthetic secret sentinel in error payload is absent from stdout/stderr/receipt summary. | PC-108 |

**HostCleanupLinuxTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupLinuxTests.cs`; recipe N; 0 present / 6 required methods; 6 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Swapped_symlink_preserves_outside_sentinel` | MISSING / BINDING REQUIRED; 1 result | Real Linux adapter with recording openat/unlinkat seam rejects substituted link; virtual outside bytes unchanged. | PC-17 |
| `Mount_crossing_refuses_native_delete` | MISSING / BINDING REQUIRED; 1 result | Device/mount identity change prevents unlink call. | PC-166 |
| `Ancestor_handles_stay_bound_after_rename` | MISSING / BINDING REQUIRED; 1 result | Recorded deletion uses held parent handle/file ID, not rebuilt absolute path. | PC-167 |
| `Permission_failure_retains_marker_and_reports_partial` | MISSING / BINDING REQUIRED; 1 result | Injected EACCES yields Partial and retained custody. | PC-168 |
| `Proc_identity_namespace_mismatch_is_unknown` | MISSING / BINDING REQUIRED; 1 result | Synthetic /proc boot/start/ns data cannot prove foreign PID dead. | PC-169 |
| `Long_path_and_hardlink_budget_use_native_identity` | MISSING / BINDING REQUIRED; 1 result | Recorded relative segments and conservative allocated bytes preserve confinement. | PC-170 |

**HostCleanupWindowsTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupWindowsTests.cs`; recipe N; 0 present / 7 required methods; 7 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `Swapped_junction_preserves_outside_sentinel` | MISSING / BINDING REQUIRED; 1 result | Real Windows adapter requests reparse-safe opens; injected junction swap cannot delete virtual outside bytes. | PC-18 |
| `Case_and_path_aliases_cannot_escape_root` | MISSING / BINDING REQUIRED; 1 result | Drive-relative, UNC/device, alternate stream, trailing dot/space and sibling-prefix escapes refused. | PC-117 |
| `Held_file_returns_partial_and_retains_marker` | MISSING / BINDING REQUIRED; 1 result | Injected sharing violation yields Partial with immutable custody/budget. | PC-171 |
| `File_id_substitution_refuses_delete` | MISSING / BINDING REQUIRED; 1 result | Same path/different volume/file ID refuses delete disposition. | PC-118, PC-247 |
| `Long_path_uses_bound_handles` | MISSING / BINDING REQUIRED; 1 result | Long path remains bounded; correct handle-relative flags recorded. | PC-172 |
| `Windows_boot_and_start_uncertainty_is_retained` | MISSING / BINDING REQUIRED; 1 result | Boot tolerance never masks start mismatch; inaccessible identity is Unknown. | PC-173 |
| `Windows_powershell_51_parses_and_runs_wrappers_offline` | MISSING / BINDING REQUIRED; 1 result | Only changed Windows wrappers, real powershell.exe 5.1 plus recording cmdlets; zero native delete/restart/register. | PC-174 |

**HostCleanupCheckpointCompatibilityTests** — `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs`; recipe C; 0 present / 45 required methods; 54 required executions.

| Exact case | Binding / variant count | Oracle | Case -> mutation -> red |
|---|---|---|---|
| `live_roots_are_skipped_without_taking_the_root_lock` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Callback registration failure is null; completed roots zero. | PC-177 |
| `uncertain_roots_are_skipped_without_taking_the_root_lock` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; `ProcessVerdict.Unknown`; `ProcessVerdict.ReusedPid`; 2 results | Both argument rows register without failure and retain payload. | PC-178 |
| `young_roots_are_skipped_without_taking_the_root_lock` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Lock file does not exist; skips[grace] == 1. | PC-179 |
| `root_lock_is_still_required_for_dead_candidates` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 0; skips[root-busy] == 1; payload retained. | PC-180 |
| `eligibility_is_rechecked_after_the_root_lock` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; `"live"`; `"unknown"`; `"reused"`; `"grace"`; `"marker"`; `"inventory"`; 6 results | live arm: CompletedRoots == 0 at locked-owner-retained; payload/marker/index unchanged. | PC-119, PC-228, PC-229, PC-230, PC-242, PC-243 |
| `two_hundred_concurrent_allocations_ignore_the_sweep_gate` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | 200 allocations finish while virtual coordinator held; not waiting on release. | PC-181 |
| `grace_is_additional_to_dead_ownership` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | At grace minus one tick CompletedRoots == 0; at grace == 1. | PC-182 |
| `sweep_interval_is_shared_across_hosts` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Second instance Examined == 0; interval present and coordinator-busy absent. | PC-183 |
| `cursor_write_failure_does_not_fail_the_sweep` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Sweep returns normally with CompletedRoots == 1. | PC-184 |
| `sweep_removes_only_stale_index_temp_files` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Stale file absent and fresh file present. | PC-185 |
| `root_delete_count_is_bounded` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 2; third remains. | PC-111 |
| `actual_deleted_bytes_are_bounded` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | ReclaimedBytes <= 8192; remaining payload length == 8192. | PC-186 |
| `direct_entry_scan_is_bounded` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | First Examined == 2; one root remains for second sweep. | PC-187 |
| `descendant_scan_is_bounded` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 0 and inventory-incomplete-or-linked skip == 1. | PC-188 |
| `monotonic_deadline_stops_new_operations` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Zero-duration budget has Examined == 0, probe calls == 0, BudgetExhausted true. | PC-189 |
| `resumed_deletion_rechecks_every_veto` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Second pass CompletedRoots == 0 and payload length unchanged. | PC-190 |
| `marker_survives_partial_deletion` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | deleting marker exists after partial pass. | PC-110 |
| `cursor_prevents_new_arrival_starvation` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Five original roots absent after explicit three passes. | PC-191 |
| `absent_root_pointer_is_dropped_by_the_sweep` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | IndexExists == false at absent-pointer-dropped; CompletedRoots == 0. | PC-120 |
| `disposed_roots_do_not_consume_the_next_sweep_budget` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | With 700 absent entries plus orphan: Examined == 1, CompletedRoots == 1, orphan absent. | PC-192 |
| `eligible_backlog_drains_without_touching_exclusions` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Four eligible roots absent after four bounded passes; live root retained. | PC-193 |
| `unfinished_registered_work_retains_roots` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | unfinished-work-roots-retained payload, marker and index remain on both roots. | PC-194 |
| `completed_faulted_work_reports_failure_and_deletes_roots` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | registered-fault-reported contains synthetic-registered-fault while both roots/index entries are removed. | PC-195 |
| `allocation_requires_an_owner` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Expected InvalidOperationException is observed. | PC-196 |
| `root_is_registered_before_return` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | callback observes marker and indexed root before return. | PC-113, PC-245, PC-246 |
| `sealed_scope_awaits_registered_work` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | before explicit release, disposal incomplete and root exists. | PC-112 |
| `live_child_prevents_scope_deletion` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | identity-unknown failure receipt; virtual root remains. | PC-114 |
| `canceled_test_gets_a_fresh_cleanup_token` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Virtual root removed despite canceled test token; no remaining cleanup debt. | PC-197 |
| `teardown_preserves_failure_and_attempts_other_roots` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | both failure reasons and second root removed. | PC-115 |
| `marker_failure_rolls_back_empty_root` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Marker-write IOException observed and root absent. | PC-198 |
| `parallel_scopes_have_distinct_attempt_ownership` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | AttemptId and RootId differ. | PC-199 |
| `owned_disposal_does_not_require_dead_test_host` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Live test owner positive fixture; root absent after disposal. | PC-200 |
| `nested_live_executor_vetoes_whole_root` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | nested-live-retained: CompletedRoots == 0 and sentinel unchanged. | PC-201 |
| `unknown_nested_custody_vetoes_whole_root` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | nested-missing-retained: CompletedRoots == 0 and sentinel unchanged. | PC-202 |
| `wait_uses_custody_not_progress_pid` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | StillRunning and tool/report retained. | PC-203 |
| `next_start_uses_the_shared_veto` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | Dead tool removed; unknown tool and unrelated results retained. | PC-204 |
| `stop_validates_generation_before_signaling` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | CountingControl.Calls == 0; tool retained. | PC-116 |
| `stop_waits_for_confirmed_exit` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | StillRunning, phase running and tool present. | PC-205 |
| `age_clean_cannot_bypass_live_custody` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | Removed list contains only dead; live/reused/unknown remain. | PC-206 |
| `evidence_write_has_no_raw_delete_bypass` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | Tool directory still present without custody. | PC-207 |
| `automatic_recovery_preserves_all_evidence` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | all evidence hashes remain identical. | PC-109 |
| `sweep_never_signals_a_process` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | Control calls == 0; live tool remains. | PC-208 |
| `failed_delete_receipt_is_truthful_and_retryable` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | failed-delete-truthful outcome == Failed then retry == Removed. | PC-209 |
| `completed_wait_removes_only_tool` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; `0, false`; `0, true`; `1, false`; `1, true`; 4 results | All four rows preserve report hash, return exact exitCode and remove only tool. | PC-210 |
| `already_absent_is_idempotent` | PORT REQUIRED; source `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs`; 1 result | Second outcome == AlreadyAbsent. | PC-211 |

#### Client roster (UI recipe)

File: `client/src/features/attention/attentionVisuals.test.ts`. All 25 declarations are PRESENT (30 results).
The four cleanup cases below bind real function outputs. The expired-hold literal expiry/actions assertions
are DTO setup checks, not an expiry/auto-clear guard; that safety proof stays in V-6.

| Exact title | Cases / hand-written oracle | Compiling production mutation -> red |
|---|---|---|
| `draws TaskInputUnreadable as a warning with a task target` | 1; Input unreadable/warning and exact task target | Change TaskInputUnreadable label -> label equality red. |
| `keeps unreadable input in the review home bucket` | 1; review | Map unreadable kind hidden -> review equality red. |
| `keeps runner outages in the broken bucket with danger styling` | 1; Runner unavailable/danger/broken | Map RunnerUnavailable to review -> broken equality red. |
| `links a host cleanup summary to its run` | 1; run-c826 target, nonempty label, suspect | PC-237. |
| `shows cleanup disk pressure at the reported severity` | 1; Warning review, Critical blocked, danger | PC-238. |
| `keeps an expired cleanup hold visible for review` | 1; review | PC-239. |
| `shows sustained worktree backlog with its responsible owner` | 1; owner=CARD-0692, run-backlog, refusal=owner_unavailable and backlog label | PC-240. |
| `maps every kind to a label, a colour, an icon and a hint` | 1; declared kinds have nonempty label/hint and function icon | Empty HostCleanupSummary label -> nonempty label red. |
| `shows the planned recovery hint for a fenced repository` | 1; exact recovery hint literal | Change RepositoryChildJournalStale.hint -> exact string red. |
| `keeps kinds off the violet tier axis` | 1; no visual has violet color | Set HostCleanupSummary.color to violet -> not-violet red. |
| `lands every kind in a declared group` | 1; group belongs to ATTENTION_GROUPS | Return bogus group for one declared kind with a typed cast -> membership red. |
| `sorts by severity, except that a settled failure is history rather than suspicion` | 1; now/broken/suspect/failures per literal matrix | Map RecentFailure to suspect -> failures equality red. |
| `collapses only the failures group by default` | 1; collapsed list is [failures] | Set suspect group collapsed true -> exact array red. |
| `draws CommitRecoveryPending as a held commit recovery` | 1; held label/danger/task target/key | Drop task from target -> exact target red. |
| `draws DispatchWarningUnconfirmed as a missing dispatch receipt` | 1; missing-dispatch label/danger/task target/key | Drop kind from keyOf -> key substring red. |
| `sends a card row to its board, a task row to the drawer, and an agent row to incidents` | 1; exact board/task/agent targets; unclaimed ParkedMessage null | Return session URL for ParkedMessage -> null red. |
| `ages a row from when the condition began, and says nothing when that is unknown` | 1; 10800, null, skew 0 | Remove nonnegative age clamp -> skew equality red. |
| `keys a row by its subject so a poll does not remount it` | 1; headline changes same key, task changes different key | Include headline in key -> same-key equality red. |
| `separates two incident groups on the same agent` | 1; distinct sinceUtc yields distinct key | Omit sinceUtc from incident key -> inequality red. |
| `maps every visual kind onto exactly one home bucket` | 1; declared bucket membership | Return bogus bucket with typed cast -> membership red. |
| `puts known kinds in the home buckets and hides RecentFailure` | 1; literal kind/severity matrix, RecentFailure hidden | Map RecentFailure review -> hidden equality red. |
| `counts the three home buckets and ignores RecentFailure` | 1; blocked 1, broken 2, review 1 | Count RecentFailure as review -> object equality red. |
| `draws CapacityRecoveryExhausted and includes it in every-kind coverage` | 1; enum census, Capacity retries paused, broken/task/agent target | Drop agent fallback target -> exact target red. |
| `CARD-1079 seat kinds are drawable and grouped by severity` | 1; literal three kinds/icons/labels/action hints and severity buckets | Change SeatIdle label -> literal label red. |
| `draws %s in the %s severity bucket` | 6: RunnerUnavailable/Error/broken; RepositoryChildJournalStale/Error/broken; PoolDelegateUnreleased/Error/broken; SessionStopStuck/Error/broken; SessionUnowned/Warning/review; ZombieCensusReport/Warning/review | For each row, map its kind to hidden -> exact expected bucket equality red. Six independent variants; one declaration. |

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-1 | Extraction loses source custody/lock/marker/evidence/teardown semantics | 54 ports before/after move, CP-17/1/2/4; source-specific oracles above. Adapter shared-caller entry counters and current-image retention close remaining bypasses. |
| R-2 | Daily graph gains repository/worktree authority | Full_run_never_acquires_repository_lease: tagged/untagged counters 0 with one eligible virtual scratch removal; Daily_graph_has_no_direct_lease_or_lock_escape: forbidden reachable calls empty; CP-5/9/13. |
| R-3 | Extra scheduler/deleter or unsafe maintenance/cutover | Registration snapshots unchanged; wrapper remover counters 0; maintenance intent/commit/drain strictly precede stop/lease; CP-4/6/8/11/16. |
| R-4 | Delivery stops at ACK/queue, replay deletes again or stale report clears attention | Complete producer join tuple in authenticated report/attention GET, execute count 1 across each crash; PC-121/122 immutable/stale oracles; CP-7/9/10/12. |
| R-5 | Adapter trusts replaced path/identity | Required native flags/held handles/device/file ID equality and zero forbidden native calls; CP-14/15. Kernel behavior remains operational qualification. |

### Guard inventory

Parent guard/PC IDs 1..118 retained; 119..123 bind the proposed additions. New IDs split independently
bypassable guards and supply controls for previously unpaired safety assertions. In inherited compound rows,
the narrowed guard above is the named primary flip; split rows below cover the independent facts.
G-19 is now the tagged overload only; its formerly second prescribed variant has a distinct guard/PC.
A method can detect several independent guards; each G has exactly one distinct PC.

| Guard | Plan reference + guarded boundary | Positive control |
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
| G-19 | D-2: tagged repository-lease attempt tripwire | PC-19 |
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
| G-32 | D-5: nonpositive attempt-cap configuration rejected | PC-32 |
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
| G-43 | D-4: fresh hold revision | PC-43 |
| G-44 | D-4: fresh live-owner veto | PC-44 |
| G-45 | D-4/D-5: Partial cleanup retains custody/budget | PC-45 |
| G-46 | D-4: No credit for other owner's removal | PC-46 |
| G-47 | D-4: Unknown ownership veto | PC-47 |
| G-48 | D-4: PID generation veto | PC-48 |
| G-49 | D-4: Unknown liveness veto | PC-49 |
| G-50 | D-4: boot identity | PC-50 |
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
| G-72 | Activation: config digest binding | PC-72 |
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
| G-86 | D-7: report board scope | PC-86 |
| G-87 | D-4: Restart/deploy intent exclusion | PC-87 |
| G-88 | D-4: Maintenance drains before land lease | PC-88 |
| G-89 | D-4: Pending maintenance yields after current operation | PC-89 |
| G-90 | D-4: Epoch checked at deletion | PC-90 |
| G-91 | D-4: No TTL release of unknown worker | PC-91 |
| G-92 | D-4: Restart custody reconciliation | PC-92 |
| G-93 | D-2: No direct concrete-lease/lock bypass | PC-93 |
| G-94 | D-2: dirty worktree veto | PC-94 |
| G-95 | D-2: exact release prerequisite | PC-95 |
| G-96 | D-2: Read-only Git commands | PC-96 |
| G-97 | D-1/S4: Capability refusal | PC-97 |
| G-98 | D-3/S4: No request-selected roots | PC-98 |
| G-99 | S7: authenticated store identity | PC-99 |
| G-100 | D-2/S4: No process lifecycle side effects | PC-100 |
| G-101 | D-7 delivery: Uncertain receipt retains execution claim | PC-101 |
| G-102 | AGENTS ASCII: Windows fallback ASCII | PC-102 |
| G-103 | S8: exact scratch owner | PC-103 |
| G-104 | S8: child join prerequisite | PC-104 |
| G-105 | D-1/S5: Weekly compatibility wrapper has no remover | PC-105 |
| G-106 | D-1/S5: No local schedule install | PC-106 |
| G-107 | S8: intent commit before restart | PC-107 |
| G-108 | D-7: Output redaction | PC-108 |
| G-109 | S2: Tool evidence never swept as payload | PC-109 |
| G-110 | S2: Lifecycle partial keeps marker | PC-110 |
| G-111 | S2: Lifecycle limits retained | PC-111 |
| G-112 | S2: Scope awaits owned work | PC-112 |
| G-113 | S2: root recorded in owning scope before caller observation | PC-113 |
| G-114 | S2: Scope never deletes with uncertain child | PC-114 |
| G-115 | S2: Lifecycle disposal preserves original failure | PC-115 |
| G-116 | S2: Tool stop validates generation | PC-116 |
| G-117 | D-3/native: Windows path aliases denied | PC-117 |
| G-118 | D-4/native: Windows file identity revalidation | PC-118 |
| G-119 | S2: post-lock owner reobservation (live arm) | PC-119 |
| G-120 | S2: absent root index cleanup | PC-120 |
| G-121 | S6: immutable receipt before first database await | PC-121 |
| G-122 | S6: stale complete observation cannot clear backlog | PC-122 |
| G-123 | S3/S8: scratch exact two-level creator path grammar | PC-123 |
| G-124 | V-1: Unregistered msbuild testcontainers and tmp are unknown | PC-124 |
| G-125 | V-1: All registered owned families can be eligible | PC-125 |
| G-126 | V-1: Older owned root is eligible | PC-126 |
| G-127 | V-1: Daily plan uses injected observations only | PC-127 |
| G-128 | V-2: Duplicate request returns same receipt | PC-128 |
| G-129 | V-2: Cursor is stable without starving old roots | PC-129 |
| G-130 | V-2: Eligible scratch is removed and measured | PC-130 |
| G-131 | V-3: Released dead owner allows scratch | PC-131 |
| G-132 | V-4: V1 markers and nested executor records round trip | PC-132 |
| G-133 | V-4: Daily uses shared budget without lifecycle truncation | PC-133 |
| G-134 | V-4: Preview does not run startup sweep | PC-134 |
| G-135 | V-4: Released task scratch and nested checkpoint deduplicate | PC-135 |
| G-136 | V-4: Lifetime hook and linked host use shared core | PC-136 |
| G-137 | V-4: Evidence wrapper and direct cleanup share image guard | PC-137 |
| G-138 | V-4: Every cleanup caller accepts virtual io | PC-138 |
| G-139 | V-5: Unsupported runner is deferred | PC-139 |
| G-140 | V-5: Offline runner is deferred | PC-140 |
| G-141 | V-5: Scheduled preview persists report after pure scan | PC-141 |
| G-142 | V-6: Fresh below threshold inventory clears backlog | PC-142 |
| G-143 | V-6: Backlog window and threshold are configurable | PC-143 |
| G-144 | V-6: Partial receipt is not complete success | PC-144 |
| G-145 | V-6: Receipt replay is idempotent | PC-145 |
| G-146 | V-6: Receipt to attention reaches busy recipient | PC-146 |
| G-147 | V-6: Receipt to attention reaches ready recipient | PC-147 |
| G-148 | V-6: Missed host and expired hold stay visible | PC-148 |
| G-149 | V-7: Independent clients observe the same host gate | PC-149 |
| G-150 | V-7: Gate is released on success and failure | PC-150 |
| G-151 | V-7: Inventory never holds land admission | PC-151 |
| G-152 | V-13: Would remove worktree is preserved in dry run | PC-152 |
| G-153 | V-13: Registered and unregistered candidates are fully accounted | PC-153 |
| G-154 | V-13: Only owned scratch child is deleted | PC-154 |
| G-155 | V-8: Changed owner generation is refused | PC-155 |
| G-156 | V-8: Dry run dispatch has zero writes | PC-156 |
| G-157 | V-8: Restart replays report without redeleting | PC-157 |
| G-158 | V-8: Desktop and phone home share command service | PC-158 |
| G-159 | V-8: Busy upload queue retains order and identity | PC-159 |
| G-160 | V-8: Command crash before plan commit deletes nothing | PC-160 |
| G-161 | V-9: Windmill bridge uses host roots and no docker socket | PC-161 |
| G-162 | V-9: Smoke reports separate tmp and host disk identity | PC-162 |
| G-163 | V-9: Status or smoke never redeploys or registers schedule | PC-163 |
| G-164 | V-9: Helper preserves literal paths and exit codes | PC-164 |
| G-165 | V-9: Weekly schedule cutover is single owner and reversible to dry run | PC-165 |
| G-166 | V-10: Mount crossing refuses native delete | PC-166 |
| G-167 | V-10: Ancestor handles stay bound after rename | PC-167 |
| G-168 | V-10: Permission failure retains marker and reports partial | PC-168 |
| G-169 | V-10: Proc identity namespace mismatch is unknown | PC-169 |
| G-170 | V-10: Long path and hardlink budget use native identity | PC-170 |
| G-171 | V-11: Held file returns partial and retains marker | PC-171 |
| G-172 | V-11: Long path uses bound handles | PC-172 |
| G-173 | V-11: Windows boot and start uncertainty is retained | PC-173 |
| G-174 | V-11: Windows powershell 51 parses and runs wrappers offline | PC-174 |
| G-175 | V-5: Cleanup model has namespace uniqueness and candidate custody | PC-175 |
| G-176 | V-5: AddHostCleanup upgrades current master without changing existing rows | PC-176 |
| G-177 | R-1: live roots are skipped without taking the root lock | PC-177 |
| G-178 | R-1: uncertain roots are skipped without taking the root lock | PC-178 |
| G-179 | R-1: young roots are skipped without taking the root lock | PC-179 |
| G-180 | R-1: root lock is still required for dead candidates | PC-180 |
| G-181 | R-1: two hundred concurrent allocations ignore the sweep gate | PC-181 |
| G-182 | R-1: grace is additional to dead ownership | PC-182 |
| G-183 | R-1: sweep interval is shared across hosts | PC-183 |
| G-184 | R-1: cursor write failure does not fail the sweep | PC-184 |
| G-185 | R-1: sweep removes only stale index temp files | PC-185 |
| G-186 | R-1: actual deleted bytes are bounded | PC-186 |
| G-187 | R-1: direct entry scan is bounded | PC-187 |
| G-188 | R-1: descendant scan is bounded | PC-188 |
| G-189 | R-1: monotonic deadline stops new operations | PC-189 |
| G-190 | R-1: resumed deletion rechecks every veto | PC-190 |
| G-191 | R-1: cursor prevents new arrival starvation | PC-191 |
| G-192 | R-1: disposed roots do not consume the next sweep budget | PC-192 |
| G-193 | R-1: eligible backlog drains without touching exclusions | PC-193 |
| G-194 | R-1: unfinished registered work retains roots | PC-194 |
| G-195 | R-1: completed faulted work reports failure and deletes roots | PC-195 |
| G-196 | R-1: allocation requires an owner | PC-196 |
| G-197 | R-1: canceled test gets a fresh cleanup token | PC-197 |
| G-198 | R-1: marker failure rolls back empty root | PC-198 |
| G-199 | R-1: parallel scopes have distinct attempt ownership | PC-199 |
| G-200 | R-1: owned disposal does not require dead test host | PC-200 |
| G-201 | R-1: nested live executor vetoes whole root | PC-201 |
| G-202 | R-1: unknown nested custody vetoes whole root | PC-202 |
| G-203 | R-1: wait uses custody not progress pid | PC-203 |
| G-204 | R-1: next start uses the shared veto | PC-204 |
| G-205 | R-1: stop waits for confirmed exit | PC-205 |
| G-206 | R-1: age clean cannot bypass live custody | PC-206 |
| G-207 | R-1: evidence write has no raw delete bypass | PC-207 |
| G-208 | R-1: sweep never signals a process | PC-208 |
| G-209 | R-1: failed delete receipt is truthful and retryable | PC-209 |
| G-210 | R-1: completed wait removes only tool | PC-210 |
| G-211 | R-1: already absent is idempotent | PC-211 |
| G-212 | G-19 untagged repository lease tripwire | PC-212 |
| G-213 | G-43 fresh deny revision | PC-213 |
| G-214 | G-44 revoked release | PC-214 |
| G-215 | G-50 PID namespace identity | PC-215 |
| G-216 | G-72 activation storage binding | PC-216 |
| G-217 | G-86 report page bound | PC-217 |
| G-218 | G-86 attention redaction | PC-218 |
| G-219 | G-94 active task veto | PC-219 |
| G-220 | G-94 live session veto | PC-220 |
| G-221 | G-94 pending land/recovery veto | PC-221 |
| G-222 | G-95 retained content classifier | PC-222 |
| G-223 | G-95 evidence prerequisite | PC-223 |
| G-224 | G-99 host identity | PC-224 |
| G-225 | G-103 late adoption | PC-225 |
| G-226 | G-104 evidence debt | PC-226 |
| G-227 | G-107 drain ordering | PC-227 |
| G-228 | G-119 post-lock grace | PC-228 |
| G-229 | G-119 post-lock marker | PC-229 |
| G-230 | G-119 post-lock inventory | PC-230 |
| G-231 | D-7 enqueue failure recovery | PC-231 |
| G-232 | D-7 ingestion commit failure | PC-232 |
| G-233 | D-7 publish-success pending-clear failure | PC-233 |
| G-234 | D-7 command enqueue failure | PC-234 |
| G-235 | D-7 command busy recipient retention | PC-235 |
| G-236 | D-7 complete feed integration | PC-236 |
| G-237 | V-12: cleanup run target | PC-237 |
| G-238 | V-12: disk-pressure severity presentation | PC-238 |
| G-239 | V-12: expired hold review visibility | PC-239 |
| G-240 | V-12: backlog owner target | PC-240 |
| G-241 | D-1: disabled feature registers no daily jobs | PC-241 |
| G-242 | D-4: Unknown post-lock owner verdict | PC-242 |
| G-243 | D-4: reused post-lock owner verdict | PC-243 |
| G-244 | D-3: descendant registry deny cannot be relaxed | PC-244 |
| G-245 | S2: scope marker before return | PC-245 |
| G-246 | S2: scope index before return | PC-246 |
| G-247 | D-3: native Windows volume identity | PC-247 |
| G-248 | D-2: untracked worktree source | PC-248 |
| G-249 | D-2: unknown pushed-branch containment | PC-249 |
| G-250 | D-4: partial reservation survives restart | PC-250 |
| G-251 | D-7: report persistence before notification | PC-251 |
| G-252 | D-7: status identity after lost ACK | PC-252 |
| G-253 | D-4: maintenance operation identity | PC-253 |
| G-254 | D-4: maintenance intent identity | PC-254 |
| G-255 | D-4: unconfirmed maintenance finish | PC-255 |
| G-256 | S7: protected outbox state location | PC-256 |
| G-257 | S3/S8: scratch generation binding | PC-257 |
| G-258 | D-4: owner release is required at planning | PC-258 |
| G-259 | D-7: monitor retention protects pending receipts | PC-259 |
| G-260 | D-7: exact output protection | PC-260 |

### Positive controls

**Status: pending, post-land Mutation paused by the brief. Zero PCs executed here.** These are recipes, not
measured evidence. When separately commissioned after Code ordinary V/R, independent Review and confirmed land,
run **all 260 controls**; no sampling. Baseline green -> apply compiling defect -> named assertion red ->
restore exact source and refresh timestamp/rebuild -> same method green. Review judges the design before land.
The pending status is not an exemption or a claim that the execution gate is complete.

For each TUnit PC the exact filter is `/*/*/<Class>/<ExactMethod>*`, substituting the two literal components
in its table row (class exact, trailing method wildcard for generated/Arguments names). Inspect the TRX argument
row; the six-row recheck method still runs all six when one arm is mutated. Min is the method's Arguments count
(6, 2 or 4 for the three parameterized ports), otherwise 1. Mutation is method-scoped, not CP/class-scoped.
UI PCs use `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts -t '<exact title>'`.
Every phase takes the host build slot; an external copied checkpoint driver/source-bound pack owns isolated
outputs. SourceLanding writes receipts/restoration externally and does not commit/push snapshot changes.
Missing production seams/methods/payloads stay **binding required after Code**: bind exact compiling line,
fixture case, phase paths, SHA and decisive assertion in the Code handoff. A name-existence check cannot bind
a mutation. Zero selected results, build error, fixture exception or timeout is **not** red.

| PC | Break the corresponding G by this compiling defect | Exact detecting method/title | Expected red assertion |
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
| PC-19 | Insert tagged TryAcquireAsync in reachable daily inventory; untagged has its own PC below. | `HostCleanupWorktreeInventoryTests.Full_run_never_acquires_repository_lease` | both acquisition counters == 0 after completed mixed run; scratch removal count == 1. |
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
| PC-32 | Remove MaxAttempts <= 0 rejection from CleanupLimits.Validate. | `HostCleanupPolicyTests.Configuration_cannot_relax_hard_guards` | C826.Configuration_cannot_relax_hard_guards:limits observes ArgumentOutOfRangeException. |
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
| PC-43 | Reuse old hold revision after new hold is added. | `HostCleanupExecutionTests.Revalidation_catches_new_deny_or_hold` | new protection reason; unchanged candidate. |
| PC-44 | Reuse old dead-owner observation after owner becomes live. | `HostCleanupExecutionTests.Revalidation_catches_live_owner_or_revoked_release` | fresh veto reason; zero delete intents. |
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
| PC-99 | Skip store comparison only, retaining host comparison. | `HostCleanupCommandTests.Foreign_host_or_store_is_refused` | refusal; mutation log empty. |
| PC-100 | Call runtime KillAll on cleanup error. | `HostCleanupCommandTests.Cleanup_never_launches_or_stops_a_session` | start/kill/release/input counters == 0. |
| PC-101 | Release claim after receipt-write failure. | `HostCleanupCommandTests.Receipt_commit_failure_retains_claim_without_reexecution` | claim remains uncertain; resume does not execute again. |
| PC-102 | Insert U+2014 in a scoped script comment. | `HostCleanupScriptTests.Daemon_and_autostart_scripts_are_ascii` | offending path/offset byte must be <128. |
| PC-103 | Allow registration by a foreign full owner identity on a fresh unused path. | `HostCleanupScriptTests.Scratch_registration_refuses_foreign_owner_and_late_adoption` | refusal; worker mutation count == 0. |
| PC-104 | Release while child pending. | `HostCleanupScriptTests.Scratch_release_requires_joined_children_and_preserved_evidence` | release refused; scratch remains. |
| PC-105 | Restore robocopy /MIR branch. | `HostCleanupScriptTests.Legacy_build_junk_wrapper_uses_shared_worker_only` | robocopy/Remove-Item/native-delete counters == 0; shared worker called. |
| PC-106 | Restore Register-ScheduledTask branch. | `HostCleanupScriptTests.Installer_refuses_registration_but_allows_explicit_uninstall` | creation calls == 0; default refusal; explicit uninstall only. |
| PC-107 | Skip epoch publication in runner restart wrapper. | `HostCleanupScriptTests.All_restart_entry_points_publish_and_drain_maintenance` | first stop/restart index follows committed intent and drain for each entry. |
| PC-108 | Copy raw exception body into script output. | `HostCleanupScriptTests.Protected_payloads_never_reach_script_output` | secret sentinel absent from stdout/stderr/summary. |
| PC-109 | Delete report alongside tool in recovery. | `HostCleanupCheckpointCompatibilityTests.automatic_recovery_preserves_all_evidence` | all evidence hashes remain identical. |
| PC-110 | Remove marker before payload finishes. | `HostCleanupCheckpointCompatibilityTests.marker_survives_partial_deletion` | deleting marker exists after partial pass. |
| PC-111 | Ignore MaxRoots. | `HostCleanupCheckpointCompatibilityTests.root_delete_count_is_bounded` | CompletedRoots == 2; third remains. |
| PC-112 | Delete roots before registered task completes. | `HostCleanupCheckpointCompatibilityTests.sealed_scope_awaits_registered_work` | before explicit release, disposal incomplete and root exists. |
| PC-113 | Move _roots.Add(path) after beforeReturn callback while keeping marker/index writes unchanged. | `HostCleanupCheckpointCompatibilityTests.root_is_registered_before_return` | Callback roots.Contains(path) == true; marker/index have separate controls. |
| PC-114 | Treat uncertain registered child as safe. | `HostCleanupCheckpointCompatibilityTests.live_child_prevents_scope_deletion` | identity-unknown failure receipt; virtual root remains. |
| PC-115 | Discard original error after second cleanup fault. | `HostCleanupCheckpointCompatibilityTests.teardown_preserves_failure_and_attempts_other_roots` | both failure reasons and second root removed. |
| PC-116 | Signal before checking expected start token. | `HostCleanupCheckpointCompatibilityTests.stop_validates_generation_before_signaling` | CountingControl.Calls == 0; tool retained. |
| PC-117 | Normalize drive-relative path as trusted absolute. | `HostCleanupWindowsTests.Case_and_path_aliases_cannot_escape_root` | path_escape for alias fixtures; native deletion count == 0. |
| PC-118 | Skip file-ID equality. | `HostCleanupWindowsTests.File_id_substitution_refuses_delete` | identity_changed; delete-disposition count == 0. |
| PC-119 | Reuse preflight Dead observation for the locked owner check. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | live arm: CompletedRoots == 0 at locked-owner-retained; payload/marker/index unchanged. |
| PC-120 | Skip Unregister on confirmed absent root. | `HostCleanupCheckpointCompatibilityTests.absent_root_pointer_is_dropped_by_the_sweep` | IndexExists == false at absent-pointer-dropped; CompletedRoots == 0. |
| PC-121 | In HostCleanupService.IngestAsync remove Candidates.ToArray() snapshot assignment. | `HostCleanupReportTests.Receipt_candidates_are_snapshotted_before_database_await` | C826.immutable-receipt-across-await: persisted path equals /virtual/original. |
| PC-122 | Remove receivedAt - FinishedAt <= one-day completeness condition in IngestAsync. | `HostCleanupReportTests.Delayed_complete_inventory_cannot_clear_open_backlog` | C826.delayed-recovery-cannot-clear-backlog: backlog remains true after day-8 ingest of day-4 low sample. |
| PC-123 | In ScratchRegistry admission compare only Leaf(path), ignoring worktree-directory/session parents. | `HostCleanupScriptTests.Scratch_registration_refuses_foreign_owner_and_late_adoption` | Same scratchpad leaf under foreign session is refused; registry writes and worker mutations == 0. |
| PC-124 | Admit MSBuildTemp123 by its basename. | `HostCleanupPolicyTests.Unregistered_msbuild_testcontainers_and_tmp_are_unknown` | All three cases remain Unknown. |
| PC-125 | Return false for registered Probe family. | `HostCleanupPolicyTests.All_registered_owned_families_can_be_eligible` | The fully owned Probe row remains Eligible. |
| PC-126 | Reject the eligible branch as recent_write. | `HostCleanupPolicyTests.Older_owned_root_is_eligible` | Disposition == Eligible and ReservedBytes == 10. |
| PC-127 | Return decisions in input enumeration order. | `HostCleanupPolicyTests.Daily_plan_uses_injected_observations_only` | Reversed inputs yield the same ordered path/reason array. |
| PC-128 | Ignore ExistingAsync receipt and generate a new receipt on replay. | `HostCleanupExecutionTests.Duplicate_request_returns_same_receipt` | Same ReceiptId and DeleteCalls == 1. |
| PC-129 | Ignore incoming NextCursor. | `HostCleanupExecutionTests.Cursor_is_stable_without_starving_old_roots` | Second plan starts at b, not a. |
| PC-130 | Credit reserved bytes instead of DeleteNoFollow measured bytes. | `HostCleanupExecutionTests.Eligible_scratch_is_removed_and_measured` | Measured ReclaimedBytes == 6 for reservation 10. |
| PC-131 | Treat confirmed dead released owner as Unknown. | `HostCleanupOwnershipTests.Released_dead_owner_allows_scratch` | Disposition == Eligible. |
| PC-132 | Change serialized marker version field from v1 to v2. | `HostCleanupCheckpointAdapterTests.V1_markers_and_nested_executor_records_round_trip` | Hand-written v1 round-trip fields and binding equal the original fixture. |
| PC-133 | Route daily oversized root through lifecycle truncation branch. | `HostCleanupCheckpointAdapterTests.Daily_uses_shared_budget_without_lifecycle_truncation` | Daily truncate calls == 0 and original bytes unchanged. |
| PC-134 | Call startup sweep from preview. | `HostCleanupCheckpointAdapterTests.Preview_does_not_run_startup_sweep` | Index/marker/gate/cursor writes == 0. |
| PC-135 | Key nested checkpoint separately from its admitted parent. | `HostCleanupCheckpointAdapterTests.Released_task_scratch_and_nested_checkpoint_deduplicate` | One claim/reservation; measured bytes credited once. |
| PC-136 | Wire linked host to an old local implementation type. | `HostCleanupCheckpointAdapterTests.Lifetime_hook_and_linked_host_use_shared_core` | Hook and linked-host implementation type equals the shared core type. |
| PC-137 | Bypass image guard only in EvidenceFolder.Write. | `HostCleanupCheckpointAdapterTests.Evidence_wrapper_and_direct_cleanup_share_image_guard` | Both callers retain current image; direct and wrapper outcomes match hand-written expected results. |
| PC-138 | Construct native I/O in WaitCommand instead of supplied instance. | `HostCleanupCheckpointAdapterTests.Every_cleanup_caller_accepts_virtual_io` | Wait caller injected I/O invocation count > 0; native fallback violations == 0 before invocation. |
| PC-139 | Treat absent capability as supported in scheduler. | `HostCleanupOrchestrationTests.Unsupported_runner_is_deferred` | Unsupported outcome; transport command count == 0. |
| PC-140 | Convert unavailable transport to empty successful inventory. | `HostCleanupOrchestrationTests.Offline_runner_is_deferred` | Deferred outcome; Complete == false. |
| PC-141 | Skip explicit report persistence after pure scan. | `HostCleanupOrchestrationTests.Scheduled_preview_persists_report_after_pure_scan` | Scan writes == 0; after wrapper completion report GET contains RunId. |
| PC-142 | Keep open after a fresh 19-GiB report. | `HostCleanupReportTests.Fresh_below_threshold_inventory_clears_backlog` | Backlog false at C826.fresh-below-threshold-clears. |
| PC-143 | Use constant default window instead of configured two days. | `HostCleanupReportTests.Backlog_window_and_threshold_are_configurable` | Backlog true at day 2 with 7 GiB. |
| PC-144 | Count partial candidate as complete. | `HostCleanupReportTests.Partial_receipt_is_not_complete_success` | C826.partial-not-complete: Complete == false. |
| PC-145 | Publish duplicate invalidation despite completed same-digest replay. | `HostCleanupReportTests.Receipt_replay_is_idempotent` | C826.one-invalidation: event count == 1; one run and candidate. |
| PC-146 | Drop local outbox item on retryable ingestion failure. | `HostCleanupReportTests.Receipt_to_attention_reaches_busy_recipient` | After one explicit recovery drain authenticated GET contains matching RunId/BoardId/digest; before drain no fabricated receipt. |
| PC-147 | Skip outbox drain when receiver is already ready. | `HostCleanupReportTests.Receipt_to_attention_reaches_ready_recipient` | After ordinary producer completion and drain GET contains matching RunId/BoardId. |
| PC-148 | Filter expired unresolved holds from projection. | `HostCleanupReportTests.Missed_host_and_expired_hold_stay_visible` | C826.expired-hold-visible has exact expiry; DisposedAt null. |
| PC-149 | Remove Revision compare-and-swap predicate in HostMaintenanceStore. | `HostCleanupMaintenanceTests.Independent_clients_observe_the_same_host_gate` | Stale TryReplaceAsync returns false; no operation installed. |
| PC-150 | Clear OperationId on unconfirmed CompleteCleanupAsync. | `HostCleanupMaintenanceTests.Gate_is_released_on_success_and_failure` | C826.unknown-outcome-retains-operation: exact OperationId retained. |
| PC-151 | Make read-only inventory acquire cleanup admission. | `HostCleanupMaintenanceTests.Inventory_never_holds_land_admission` | At paused inventory barrier land result is drained with null OperationId. |
| PC-152 | Write an inventory cursor inside the observed worktree during preview. | `HostCleanupWorktreeInventoryTests.Would_remove_worktree_is_preserved_in_dry_run` | Full worktree/ref/registration snapshot unchanged and mutation counters == 0. |
| PC-153 | Stop after first probe page. | `HostCleanupWorktreeInventoryTests.Registered_and_unregistered_candidates_are_fully_accounted` | Count == 2 and probe calls == 2. |
| PC-154 | Admit an unreleased child alongside released one. | `HostCleanupWorktreeInventoryTests.Only_owned_scratch_child_is_deleted` | Only released child removed; unreleased/held children and worktree metadata byte-identical. |
| PC-155 | Ignore expected owner generation in command admission. | `HostCleanupCommandTests.Changed_owner_generation_is_refused` | Refused decision before execution; replacement generation sentinel unchanged. |
| PC-156 | Persist a preview claim from command service. | `HostCleanupCommandTests.Dry_run_dispatch_has_zero_writes` | HTTP and phone-home preview mutation totals == 0. |
| PC-157 | Discard durable completion and invoke executor after recreating command service. | `HostCleanupCommandTests.Restart_replays_report_without_redeleting` | Execution count == 1 across restart; same receipt delivered. |
| PC-158 | Return synthetic HTTP success without calling shared command service. | `HostCleanupCommandTests.Desktop_and_phone_home_share_command_service` | Service-entry counter == 1 per transport and matching real receipt body. |
| PC-159 | Dequeue second committed item before first pending item. | `HostCleanupCommandTests.Busy_upload_queue_retains_order_and_identity` | Received RunId/digest sequence equals hand-written first,second order. |
| PC-160 | Call delete before committing plan in command service. | `HostCleanupCommandTests.Command_crash_before_plan_commit_deletes_nothing` | At pre-commit crash boundary deletion attempts == 0; candidate retained. |
| PC-161 | Add docker.sock or runner volume to bridge invocation. | `HostCleanupScriptTests.Windmill_bridge_uses_host_roots_and_no_docker_socket` | Recorded SSH argv contains only configured host ID; forbidden mount/socket list empty. |
| PC-162 | Assign container sample storage ID to host disk sample. | `HostCleanupScriptTests.Smoke_reports_separate_tmp_and_host_disk_identity` | Exact independent tmp-volume and host-device IDs match fixture; no relabel. |
| PC-163 | Invoke deploy from status branch. | `HostCleanupScriptTests.Status_or_smoke_never_redeploys_or_registers_schedule` | Deploy/restart/scheduler command counters == 0. |
| PC-164 | Join worker argument array into command text. | `HostCleanupScriptTests.Helper_preserves_literal_paths_and_exit_codes` | Captured special-character path is one exact argument and exit 23 is preserved. |
| PC-165 | Enable new execute before confirmed old-disable receipt. | `HostCleanupScriptTests.Weekly_schedule_cutover_is_single_owner_and_reversible_to_dry_run` | Ordered log old-disable-confirmed precedes new-enable; failure leaves dry-run. |
| PC-166 | Skip native device comparison. | `HostCleanupLinuxTests.Mount_crossing_refuses_native_delete` | No unlink; mount_crossing reason. |
| PC-167 | Reopen absolute pathname after ancestor rename. | `HostCleanupLinuxTests.Ancestor_handles_stay_bound_after_rename` | Delete uses recorded original parent handle/file ID; no path reopen. |
| PC-168 | Convert native EACCES to success and remove marker. | `HostCleanupLinuxTests.Permission_failure_retains_marker_and_reports_partial` | Partial outcome; marker retained. |
| PC-169 | Ignore PID namespace mismatch in native identity observation. | `HostCleanupLinuxTests.Proc_identity_namespace_mismatch_is_unknown` | Foreign namespace yields Unknown; no delete admission. |
| PC-170 | Use logical-only bytes for native hardlink reservation. | `HostCleanupLinuxTests.Long_path_and_hardlink_budget_use_native_identity` | Reserved bytes equal conservative allocated maximum; relative handle segments remain bounded. |
| PC-171 | Convert sharing violation to Removed. | `HostCleanupWindowsTests.Held_file_returns_partial_and_retains_marker` | Partial receipt; marker and reservation retained. |
| PC-172 | Use path-based deletion for long Windows path. | `HostCleanupWindowsTests.Long_path_uses_bound_handles` | Recorded disposition is on the bound handle; no path-delete attempt. |
| PC-173 | Apply boot tolerance to start token mismatch. | `HostCleanupWindowsTests.Windows_boot_and_start_uncertainty_is_retained` | Start mismatch stays Unknown/Reused; no delete disposition. |
| PC-174 | Insert PowerShell-7-only null-coalescing operator in a changed wrapper. | `HostCleanupWindowsTests.Windows_powershell_51_parses_and_runs_wrappers_offline` | Named AST parse error inventory is empty under real powershell.exe 5.1; parse failure is asserted by test, not fixture crash. |
| PC-175 | Remove unique flag from daily StorageId/LocalDate index. | `HostCleanupOrchestrationTests.Cleanup_model_has_namespace_uniqueness_and_candidate_custody` | C826.daily-namespace-uniqueness predicate true. |
| PC-176 | Add an UPDATE clearing existing InputBody in the migration Up body. | `HostCleanupOrchestrationTests.AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | C826.upgrade-preserves-input-body equals existing input body. |
| PC-177 | Take root lock before live preflight observation. | `HostCleanupCheckpointCompatibilityTests.live_roots_are_skipped_without_taking_the_root_lock` | Callback registration failure is null; completed roots zero. |
| PC-178 | Take root lock before Unknown/Reused preflight. | `HostCleanupCheckpointCompatibilityTests.uncertain_roots_are_skipped_without_taking_the_root_lock` | Both argument rows register without failure and retain payload. |
| PC-179 | Open root gate before grace check. | `HostCleanupCheckpointCompatibilityTests.young_roots_are_skipped_without_taking_the_root_lock` | Lock file does not exist; skips[grace] == 1. |
| PC-180 | Continue mutation after root-lock acquisition fails. | `HostCleanupCheckpointCompatibilityTests.root_lock_is_still_required_for_dead_candidates` | CompletedRoots == 0; skips[root-busy] == 1; payload retained. |
| PC-181 | Make registration require coordinator gate. | `HostCleanupCheckpointCompatibilityTests.two_hundred_concurrent_allocations_ignore_the_sweep_gate` | 200 allocations finish while virtual coordinator held; not waiting on release. |
| PC-182 | Remove creation-grace comparison. | `HostCleanupCheckpointCompatibilityTests.grace_is_additional_to_dead_ownership` | At grace minus one tick CompletedRoots == 0; at grace == 1. |
| PC-183 | Ignore persisted LastSweep on new sweep instance. | `HostCleanupCheckpointCompatibilityTests.sweep_interval_is_shared_across_hosts` | Second instance Examined == 0; interval present and coordinator-busy absent. |
| PC-184 | Let cursor replacement IOException escape. | `HostCleanupCheckpointCompatibilityTests.cursor_write_failure_does_not_fail_the_sweep` | Sweep returns normally with CompletedRoots == 1. |
| PC-185 | Remove index-temp age predicate. | `HostCleanupCheckpointCompatibilityTests.sweep_removes_only_stale_index_temp_files` | Stale file absent and fresh file present. |
| PC-186 | Ignore remaining lifecycle byte allowance. | `HostCleanupCheckpointCompatibilityTests.actual_deleted_bytes_are_bounded` | ReclaimedBytes <= 8192; remaining payload length == 8192. |
| PC-187 | Ignore MaxDirectEntries. | `HostCleanupCheckpointCompatibilityTests.direct_entry_scan_is_bounded` | First Examined == 2; one root remains for second sweep. |
| PC-188 | Treat max-descendant truncation as complete inventory. | `HostCleanupCheckpointCompatibilityTests.descendant_scan_is_bounded` | CompletedRoots == 0 and inventory-incomplete-or-linked skip == 1. |
| PC-189 | Skip duration check before first observation. | `HostCleanupCheckpointCompatibilityTests.monotonic_deadline_stops_new_operations` | Zero-duration budget has Examined == 0, probe calls == 0, BudgetExhausted true. |
| PC-190 | Reuse original dead owner on resumed partial cleanup. | `HostCleanupCheckpointCompatibilityTests.resumed_deletion_rechecks_every_veto` | Second pass CompletedRoots == 0 and payload length unchanged. |
| PC-191 | Reset LastRootId after each bounded sweep. | `HostCleanupCheckpointCompatibilityTests.cursor_prevents_new_arrival_starvation` | Five original roots absent after explicit three passes. |
| PC-192 | Charge removed legacy pointer against MaxDirectEntries. | `HostCleanupCheckpointCompatibilityTests.disposed_roots_do_not_consume_the_next_sweep_budget` | With 700 absent entries plus orphan: Examined == 1, CompletedRoots == 1, orphan absent. |
| PC-193 | Stop admitting eligible roots after first capped pass. | `HostCleanupCheckpointCompatibilityTests.eligible_backlog_drains_without_touching_exclusions` | Four eligible roots absent after four bounded passes; live root retained. |
| PC-194 | Skip unfinishedWork veto after join deadline. | `HostCleanupCheckpointCompatibilityTests.unfinished_registered_work_retains_roots` | unfinished-work-roots-retained payload, marker and index remain on both roots. |
| PC-195 | Ignore completed registered-task exception. | `HostCleanupCheckpointCompatibilityTests.completed_faulted_work_reports_failure_and_deletes_roots` | registered-fault-reported contains synthetic-registered-fault while both roots/index entries are removed. |
| PC-196 | Allocate a scope on demand in unopened TempDir caller. | `HostCleanupCheckpointCompatibilityTests.allocation_requires_an_owner` | Expected InvalidOperationException is observed. |
| PC-197 | Use an already canceled token for cleanup. | `HostCleanupCheckpointCompatibilityTests.canceled_test_gets_a_fresh_cleanup_token` | Virtual root removed despite canceled test token; no remaining cleanup debt. |
| PC-198 | Skip empty-root rollback after marker write failure. | `HostCleanupCheckpointCompatibilityTests.marker_failure_rolls_back_empty_root` | Marker-write IOException observed and root absent. |
| PC-199 | Use one constant attempt ID for two scope instances. | `HostCleanupCheckpointCompatibilityTests.parallel_scopes_have_distinct_attempt_ownership` | AttemptId and RootId differ. |
| PC-200 | Require test-host death before scope-owned disposal. | `HostCleanupCheckpointCompatibilityTests.owned_disposal_does_not_require_dead_test_host` | Live test owner positive fixture; root absent after disposal. |
| PC-201 | Ignore nested live executor in sweep. | `HostCleanupCheckpointCompatibilityTests.nested_live_executor_vetoes_whole_root` | nested-live-retained: CompletedRoots == 0 and sentinel unchanged. |
| PC-202 | Accept missing custody on nested tool directory. | `HostCleanupCheckpointCompatibilityTests.unknown_nested_custody_vetoes_whole_root` | nested-missing-retained: CompletedRoots == 0 and sentinel unchanged. |
| PC-203 | Trust done phase/dead progress PID over unknown launch custody. | `HostCleanupCheckpointCompatibilityTests.wait_uses_custody_not_progress_pid` | StillRunning and tool/report retained. |
| PC-204 | Skip custody veto in next-start recovery. | `HostCleanupCheckpointCompatibilityTests.next_start_uses_the_shared_veto` | Dead tool removed; unknown tool and unrelated results retained. |
| PC-205 | Set phase done after stop request returns false. | `HostCleanupCheckpointCompatibilityTests.stop_waits_for_confirmed_exit` | StillRunning, phase running and tool present. |
| PC-206 | Use age alone to remove runs. | `HostCleanupCheckpointCompatibilityTests.age_clean_cannot_bypass_live_custody` | Removed list contains only dead; live/reused/unknown remain. |
| PC-207 | Remove tool directly from EvidenceFolder.Write. | `HostCleanupCheckpointCompatibilityTests.evidence_write_has_no_raw_delete_bypass` | Tool directory still present without custody. |
| PC-208 | Call injected process control from sweep on live owner. | `HostCleanupCheckpointCompatibilityTests.sweep_never_signals_a_process` | Control calls == 0; live tool remains. |
| PC-209 | Return Removed after delete IOException. | `HostCleanupCheckpointCompatibilityTests.failed_delete_receipt_is_truthful_and_retryable` | failed-delete-truthful outcome == Failed then retry == Removed. |
| PC-210 | Remove run directory instead of tool on completed wait. | `HostCleanupCheckpointCompatibilityTests.completed_wait_removes_only_tool` | All four rows preserve report hash, return exact exitCode and remove only tool. |
| PC-211 | Return Removed for missing tool. | `HostCleanupCheckpointCompatibilityTests.already_absent_is_idempotent` | Second outcome == AlreadyAbsent. |
| PC-212 | Insert untagged TryAcquireAsync on the reachable daily path; leave tagged path unchanged. | `HostCleanupWorktreeInventoryTests.Full_run_never_acquires_repository_lease` | Untagged acquisition counter == 0 even if exception is swallowed. |
| PC-213 | Reuse pre-plan deny registry after new deny is added. | `HostCleanupExecutionTests.Revalidation_catches_new_deny_or_hold` | Deny-only variant returns protected_runtime before claim; payload unchanged. |
| PC-214 | Reuse old release flag after revocation; keep live-owner check. | `HostCleanupExecutionTests.Revalidation_catches_live_owner_or_revoked_release` | Release-only variant returns release_required with no delete. |
| PC-215 | Remove PID namespace comparison only. | `HostCleanupOwnershipTests.Foreign_host_boot_or_namespace_prevents_delete` | Namespace-only variant returns owner_identity_foreign. |
| PC-216 | Ignore changed store identity but retain digest comparison. | `HostCleanupOrchestrationTests.Activation_binds_storage_and_config_digest` | Same config with replacement store is unqualified. |
| PC-217 | Ignore take in candidate query. | `HostCleanupReportTests.Attention_is_board_scoped_paginated_and_redacted` | Requested take 1 returns exactly one candidate and NextOffset 1. |
| PC-218 | Append CanonicalPath to attention Evidence. | `HostCleanupReportTests.Attention_is_board_scoped_paginated_and_redacted` | SECRET-SENTINEL absent in authenticated attention response. |
| PC-219 | Ignore IsTaskActive while retaining dirty checks. | `HostCleanupWorktreeInventoryTests.Dirty_or_active_worktree_is_kept` | Task-only row reason task_active. |
| PC-220 | Ignore HasLiveSession. | `HostCleanupWorktreeInventoryTests.Dirty_or_active_worktree_is_kept` | Session-only row reason session_live. |
| PC-221 | Ignore HasPendingLandOrRecovery. | `HostCleanupWorktreeInventoryTests.Dirty_or_active_worktree_is_kept` | Pending-only row reason land_or_recovery_pending. |
| PC-222 | Ignore Protected content class. | `HostCleanupWorktreeInventoryTests.Classifier_preserves_guarded_classes_and_release_requirements` | Protected-content row reason protected_content. |
| PC-223 | Ignore EvidencePreserved. | `HostCleanupWorktreeInventoryTests.Classifier_preserves_guarded_classes_and_release_requirements` | Evidence-only row reason evidence_required. |
| PC-224 | Ignore host comparison only, retaining store check. | `HostCleanupCommandTests.Foreign_host_or_store_is_refused` | Foreign host with matching store is refused before observations. |
| PC-225 | Admit used unregistered path for otherwise exact owner. | `HostCleanupScriptTests.Scratch_registration_refuses_foreign_owner_and_late_adoption` | Late-adoption row refused; registration writes zero. |
| PC-226 | Release with missing evidence receipt but children joined. | `HostCleanupScriptTests.Scratch_release_requires_joined_children_and_preserved_evidence` | Evidence-debt row release refused and scratch retained. |
| PC-227 | Perform stop after intent commit but before drain confirmation. | `HostCleanupScriptTests.All_restart_entry_points_publish_and_drain_maintenance` | Stop index is strictly after drain-confirmed in each entry-point trace. |
| PC-228 | Skip locked grace recheck only. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | grace row locked-grace-retained: CompletedRoots == 0. |
| PC-229 | Skip reread marker identity comparison only. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | marker row locked-marker-retained: CompletedRoots == 0. |
| PC-230 | Reuse preflight descendant inventory after root lock. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | inventory row locked-inventory-retained: CompletedRoots == 0. |
| PC-231 | Mark receipt queued before production enqueue succeeds. | `HostCleanupReportTests.Crash_before_enqueue_recovers_pending_receipt` | enqueue-throws variant: recreated service delivers same RunId to GET after next drain. |
| PC-232 | Set outbox delivered when ingestion transaction rolls back. | `HostCleanupReportTests.Receipt_to_attention_reaches_busy_recipient` | pre-ingestion-commit failure: same pending item eventually reaches GET; no fabricated receipt before successful commit. |
| PC-233 | Discard immutable report on pending-clear failure. | `HostCleanupReportTests.Crash_before_invalidation_recovers_attention_delivery` | After restart repeat invalidation allowed; GET still contains one matching run. |
| PC-234 | Discard persisted daily run after outbound enqueue fails. | `HostCleanupOrchestrationTests.Restart_catchup_runs_once_per_local_day` | Restart sends same RunId once to dispatcher; one eventual matching report/attention tuple. |
| PC-235 | Drop command when recipient is held busy before dispatcher admission. | `HostCleanupCommandTests.Lost_reply_returns_existing_receipt` | After barrier release recipient executes once and eventual matching receipt is readable. |
| PC-236 | Omit HostCleanupAttentionService from AttentionService.GetAsync composition. | `HostCleanupReportTests.Receipt_to_attention_reaches_ready_recipient` | Authenticated global attention GET contains producer RunId and BoardId. |
| PC-237 | Drop hostCleanupRunId from targetOf cleanup branch. | `attentionVisuals.test.ts :: links a host cleanup summary to its run` | Target contains run-c826. |
| PC-238 | Route Critical cleanup pressure to review instead of blocked. | `attentionVisuals.test.ts :: shows cleanup disk pressure at the reported severity` | homeBucketOf(critical) == blocked. |
| PC-239 | Return hidden from homeBucketOf for HostCleanupHoldExpired. | `attentionVisuals.test.ts :: keeps an expired cleanup hold visible for review` | homeBucketOf(row) == review. |
| PC-240 | Omit owner query parameter from targetOf WorktreeCleanupBacklog. | `attentionVisuals.test.ts :: shows sustained worktree backlog with its responsible owner` | Target contains owner=CARD-0692. |
| PC-241 | Ignore HostCleanup.Enabled while Hangfire worker remains enabled. | `HostCleanupOrchestrationTests.Main_only_registers_daily_jobs_and_monitor` | Disabled-feature row registration count == 0; enabled worker+feature positive row registers expected jobs. |
| PC-242 | Admit locked Unknown while retaining AliveSame veto. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | unknown argument retains root and scripted-Unknown skip. |
| PC-243 | Admit locked ReusedPid while retaining other vetoes. | `HostCleanupCheckpointCompatibilityTests.eligibility_is_rechecked_after_the_root_lock` | reused argument retains root and scripted-ReusedPid skip. |
| PC-244 | Allow configured roots to override IsDenied hard runtime component. | `HostCleanupPolicyTests.Configuration_cannot_relax_hard_guards` | Otherwise-positive owned runtime candidate is refused even with explicit family registration; add this currently missing oracle. |
| PC-245 | Invoke beforeReturn callback before writing marker. | `HostCleanupCheckpointCompatibilityTests.root_is_registered_before_return` | Callback sees a valid marker before first observation; add callback marker read to port. |
| PC-246 | Move index registration after beforeReturn callback. | `HostCleanupCheckpointCompatibilityTests.root_is_registered_before_return` | Callback sees exact indexed root before first observation; add callback index read to port. |
| PC-247 | Ignore volume identity while checking file ID. | `HostCleanupWindowsTests.File_id_substitution_refuses_delete` | Same file ID on replacement volume returns identity_changed; disposition calls zero. |
| PC-248 | Ignore HasUntrackedSource but retain IsDirty. | `HostCleanupWorktreeInventoryTests.Dirty_or_active_worktree_is_kept` | Untracked-only fixture reason dirty_source. |
| PC-249 | Coalesce null IsContainedInPushedBranch to true. | `HostCleanupWorktreeInventoryTests.Classifier_preserves_guarded_classes_and_release_requirements` | Null-only fixture disposition Unknown. |
| PC-250 | Clear reserved byte debit after partial receipt while retaining claim. | `HostCleanupExecutionTests.Partial_failure_keeps_custody_and_budget` | Fresh executor/store instance still has original reservation; second pass cannot spend it again. |
| PC-251 | Publish invalidation before receipt SaveChangesAsync completes. | `HostCleanupReportTests.Receipt_to_attention_reaches_ready_recipient` | At DB barrier subscriber count zero; ordered receipt-commit precedes event. |
| PC-252 | Accept status for a different ReceiptDigest as confirmation. | `HostCleanupReportTests.Crash_after_ingestion_before_ack_queries_status` | Wrong-digest status keeps pending upload, then exact status/GET matches original digest. |
| PC-253 | Accept CompleteCleanupAsync for wrong operation ID. | `HostCleanupMaintenanceTests.Gate_is_released_on_success_and_failure` | Wrong-ID completion Accepted false and original operation retained. |
| PC-254 | Accept FinishAsync for wrong intent ID. | `HostCleanupMaintenanceTests.Gate_is_released_on_success_and_failure` | Wrong-intent variant cannot clear committed intent; exact-ID successful reconciliation can. |
| PC-255 | Clear maintenance intent despite confirmed=false. | `HostCleanupMaintenanceTests.Gate_is_released_on_success_and_failure` | Finish false returns refusal and retains intent. |
| PC-256 | Use request-supplied directory as receipt store root. | `HostCleanupCommandTests.Request_cannot_supply_arbitrary_roots` | Store-root injection refused; writes only under configured protected virtual state. |
| PC-257 | Match task ID but ignore creator generation in registry. | `HostCleanupScriptTests.Scratch_registration_refuses_foreign_owner_and_late_adoption` | Same task/new generation cannot adopt prior scratch; zero writes. |
| PC-258 | Admit unreleased owner when other ownership facts are positive. | `HostCleanupOwnershipTests.Released_dead_owner_allows_scratch` | Add unreleased one-flip row: not Eligible; released positive stays Eligible. |
| PC-259 | Prune a pending receipt at retention age before upload. | `HostCleanupReportTests.Crash_before_enqueue_recovers_pending_receipt` | Expired-age pending receipt remains recoverable and reaches matching GET. |
| PC-260 | Accept different evidence digest as preservation. | `HostCleanupScriptTests.Scratch_release_requires_joined_children_and_preserved_evidence` | Digest-mismatch variant refuses release despite joined children. |

### Out of scope

- Whole-worktree/mirror removal and CARD-1017/1032/1033 authority, repository leases, ref mutation, provider archives,
  Docker pruning, physical cleanup and schedule activation. Named tripwires assert their absence on daily paths.
- Live network/SSH/Windmill/host-kernel qualification; native-call fakes cannot prove kernel atomicity, fsync,
  real crash recovery, host free-space deltas or Windows sharing semantics. Parent per-host qualification still applies.
- New agent/session message delivery; if introduced, add complete UserPrompt receipt tests before acceptance.
- Broad Unit, assembly, namespace, provider, physical checkpoint or PTY suites. No existing floor is reduced.
- Direct assertions over input DTO literals prove no producer behavior; do not promote them to recipient evidence.

### Checkpoints

Closed future ordinary list; each TUnit row has its own isolated build and exactly one filter.
The physical table order follows dispatch prerequisites; original CP IDs remain stable (CP-17 precedes CP-1, CP-10 precedes CP-6, CP-8 precedes CP-7).
Adopted by the seam-repair Plan amendment (task 04839ba2): CP-17 (S1a, the 14 tool-only ports) precedes CP-1 (S1b, all 16); finding 6 no longer blocks CP-1.
CP-2 is the single declared preparatory exception to once-per-row: run at pre-move SHA and post-move SHA,
each with a fresh build/output inventory and evidence directory; preserve both receipts.
All remaining rows run once per committed slice; failures trigger only the same corrected row and are counted.
CP-9 at the composed S6b tip includes S5, S6a and S7 and reruns the full 60-case service union.
CP-15/CP-16 are **Windows rows**, dispatched separately with -Platform Windows. Other rows use the Linux
default lane without a Runner/Platform pin; adapter fakes otherwise need no OS claim.

Use checkpoint-tool run --plan <this-path> --rows <IDs> --expected-source-sha <committed-SHA>, then owned wait
to non-75, through the host build-slot gate as documented. The run-checkpoint script takes its own slot.
Do not run a build/test driver outside the gate or use a build ID named tests. Include UseAppHost=false on
Linux (driver default), forward-slash OutputPath, fresh result paths and expected-source SHA. Preserve exact
CHECKPOINT lines, counts, source/build provenance and validate receipts. Code must compare executed names and
argument rows with this ledger: a count-only floor can pass the wrong set. List-tests output is not coverage.
Remove only producer-owned isolated outputs after joined runs; logs/TRX/JSON stay ignored.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-17 | S1a | `tests/Antiphon.Tests -> bin-c987-compat-toolonly/` | linux-compat-toolonly | `/*/*/HostCleanupCheckpointCompatibilityTests*/(wait_uses_custody_not_progress_pid*)\|(next_start_uses_the_shared_veto*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a (tool-only 14) | all listed, 0 failed/skipped; 14 executions (11 methods; `completed_wait_removes_only_tool` is 4 rows) | 14 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-1 | S1b | `tests/Antiphon.Tests -> bin-c987-compat-tool/`| linux-compat-toolcopy | `/*/*/HostCleanupCheckpointCompatibilityTests*/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)\|(wait_uses_custody_not_progress_pid*)\|(next_start_uses_the_shared_veto*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a | all listed, 0 failed/skipped; 16 executions | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c987-compat-sweep/`| linux-compat-sweep-scope | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | all listed, 0 failed/skipped; 54 executions (run once pre-move, once post-move, reported separately) | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c987-adapter/`| linux-checkpoint-adapter | `/*/*/HostCleanupCheckpointAdapterTests*/*` | V-4 | all 14 listed executions, 0 failed/skipped | 14 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c987-custody-union/` | linux-cleanup-custody-union | `/*/*/(HostCleanupOwnershipTests*)\|(HostCleanupCheckpointAdapterTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-3, V-4, R-1, R-3 | all listed, 0 failed/skipped; 12 + 14 + 54 | 80 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c987-inventory/`| linux-worktree-inventory | `/*/*/HostCleanupWorktreeInventoryTests*/*` | V-13, R-2 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S7 | `tests/Antiphon.SessionRunner.Tests -> bin-c987-runner/`| linux-cleanup-runner | `/*/*/HostCleanupCommandTests*/*` | V-8, R-4 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c987-orchestration/`| linux-orchestration | `/*/*/HostCleanupOrchestrationTests*/*` | V-5, R-3 | all listed, 0 failed/skipped; 14 + 2 schema | 16 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S6a | `tests/Antiphon.Tests -> bin-c987-maintenance/` | linux-maintenance-gate | `/*/*/HostCleanupMaintenanceTests*/*` | V-7, R-2 | all listed, 0 failed | 10 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S6b | `tests/Antiphon.Tests -> bin-c987-delivery/`| linux-report-delivery | `/*/*/HostCleanupReportTests*/*` | V-6, R-4 | all listed, 0 failed/skipped; 20 + 2 landed | 22 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S6b | `tests/Antiphon.Tests -> bin-c987-service/`| linux-cleanup-service-union | `/*/*/(HostCleanupOrchestrationTests*)\|(HostCleanupReportTests*)\|(HostCleanupMaintenanceTests*)\|(HostCleanupWorktreeInventoryTests*)/*` | V-5, V-6, V-7, V-13, R-2, R-3, R-4 | all listed, 0 failed/skipped; 16 + 22 + 10 + 12 | 60 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S8 | `tests/Antiphon.Tests -> bin-c987-scripts/`| linux-cleanup-scripts | `/*/*/HostCleanupScriptTests*/*` | V-9, R-3 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S8 | n/a| linux-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-12, R-4 | 30 = 24 plain + 6 Arguments; 0 failed/skipped | n/a | 2 | true | n/a |
| CP-13 | S8 | `tests/Antiphon.Tests -> bin-c987-policy/`| linux-cleanup-policy-rerun | `/*/*/(HostCleanupPolicyTests*)\|(HostCleanupExecutionTests*)/*` | V-1, V-2, R-2 (regression after S3 registry change) | all 42 listed executions, 0 failed/skipped | 42 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S9 | `tests/Antiphon.Tests -> bin-c987-linux/`| linux-native-adapter | `/*/*/HostCleanupLinuxTests*/*` | V-10, R-5 | all listed, 0 failed/skipped; Linux only | 6 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S10 | `tests/Antiphon.Tests -> bin-c987-windows/`| windows-native-adapter | `/*/*/HostCleanupWindowsTests*/(Swapped_junction_preserves_outside_sentinel*)\|(Case_and_path_aliases_cannot_escape_root*)\|(Held_file_returns_partial_and_retains_marker*)\|(File_id_substitution_refuses_delete*)\|(Long_path_uses_bound_handles*)\|(Windows_boot_and_start_uncertainty_is_retained*)` | V-11, R-5 | all listed, 0 failed/skipped; Windows only | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S10 | `tests/Antiphon.Tests -> bin-c987-ps51/` | windows-ps51-wrappers | `/*/*/HostCleanupWindowsTests*/Windows_powershell_51_parses_and_runs_wrappers_offline*` | V-11, R-3 | 1 result; real powershell.exe 5.1 with recording cmdlets; zero native delete/restart/register | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

All numbers are **estimates**, not measured in this task. Each row's minutes includes its isolated build.
Ordinary V/R floor (Code) = sum CP EstimatedMinutes = **145 minutes** (amended; was 137 before CP-17): Linux CP-17 and CP-1..14 **131**,
Windows CP-15/16 **14**. The required pre-move extra CP-2 invocation adds **12** minutes,
so mandatory ordinary verification with extraction preparation is **157** minutes.
One-time setup/tool bootstrap allowance **5** minutes, gated and explicitly reported if needed.
For accounting, the 145-minute row sum budgets 16 TUnit builds at approximately 2 minutes each
(**32 build minutes**) and **113 filter minutes**, including the CP-12 client command.
Do not add those 30 minutes a second time. The separate pre-move build is already in its 12-minute allowance.

PC floor (Mutation, paused) = **260 controls x 4 minutes = 1040**, plus **30** setup/binding/
restoration-report minutes = **1070 minutes**. Each 4-minute method cycle estimates baseline/build 1.2,
apply/red rebuild and assertion 1.3, restoration/rebuild/green 1.3, inspection 0.2.
Each named PC filter above owns 4 minutes; argument-expanded methods use the same method filter and
retain their actual count. UI cycles are included conservatively at the same allowance.
Complete commissioned verification estimate = setup 5 + ordinary 145 + pre-move 12 + Mutation 1070
= **1232 minutes**, excluding implementation and operational qualification.
No savings are claimed for independent build-per-row requirements (**0 minutes**); narrow method filters
avoid a whole-class Mutation multiplier, but no comparable measured baseline exists to quantify that saving.
Code red-first authoring may rerun its own row; report actual additional time separately, not as executed PCs.

### Handoff audit

- guards=260, mapped=260, missing=0, duplicate PC maps=0; required PC variants=260.
- All required PCs have a case, compiling-defect recipe and decisive oracle; **executed=0, pending=260**
  under the explicit post-land Mutation pause. The eventual all-PCs-run gate is not satisfied or waived.
- Current-tree ledger: 210 required TUnit methods / 219 unique required executions; 88 methods present,
  77 missing non-port methods and 45 compatibility ports missing at destination (their 54 source executions bound).
  Client 30 present. No ordinary/PC evidence was manufactured from this read-only census.
- Findings 6 and 7 require a Plan amendment before a Code handoff. Proposed first Code slice remains S1
  after that amendment and CARD-1199 land. Subsequent shared-file gates are in Collision resolution.
  Amended by Plan task 04839ba2: the first Code slice is S1a (CP-17) after CARD-1199's reviewed landing, then S1b (CP-1).
- PC payload binding remains required after Code at the exact slice SHA, including existing tests whose
  fixture does not yet reach the asserted boundary. A later Review cannot mark those bindings complete by name alone.
