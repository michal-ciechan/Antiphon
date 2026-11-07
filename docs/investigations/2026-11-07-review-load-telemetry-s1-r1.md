# Host load telemetry S1 — Review round 1

Found four source defects, a measured native CPU-budget failure and a new DTO contract failure; ordinary verification is incomplete. Do not land this candidate. Review is read-only; no product, test, migration or policy repair was made. All PC-1..102 and variants remain pending for method-scoped SourceLanding Mutation.

Reviewed source: `0cc57cca7dcb6bc506bfecbde9f22638fe92e308`, branch `origin/feat/card-task-7e3e6bcc`. Original Code landing owner: `924f8ae2-e285-400d-8efe-fcd1a16a9ced`; continuation Code: `7e3e6bcc-cca0-48a8-8e7b-e40c1cd469d5`. Review task: `8da334c5-0568-4f17-89ad-613169da7f35`. Full candidate range: `fbb2be214a849549cfa72f2e9a17646cc848cfeb..0cc57cca7dcb6bc506bfecbde9f22638fe92e308` (32 commits). Master checked: `5d5973be2cd867bf592cd0363240e728f64d3b28`.

The brief's appended Final verification profile and stage contract govern. The prose's proposed Interim/master-only landing does not satisfy that profile. The mutation request conflicts with the read-only Review contract and was not executed. Actual verification scope and all missing execution are recorded below, without converting historical receipts or native skips into current-source passes.

## Blocking findings

### B-1 — Retention cannot keep up with one host (introduced, storage)

**Where:** `server/Infrastructure/HostLoad/HostLoadDerivedService.cs:69`, `:76`, `:99`; `server/Infrastructure/Data/HostLoadModel.cs:54`.

**Failure:** A continuously sampled host inserts 720 samples/hour (17,280/day), but the worker deletes at most 500 expired raw rows and 500 identity rows once per hour, globally. After the retention horizon, each of these tables grows by at least 5,280 rows/day even with only one host. Restarts reset the hourly timer. `HostLoadObservedEvents` is also inserted indefinitely and has no retention or cascading ownership.

**Why:** Bounded individual batches are insufficient when total reclamation throughput is below production. Seven-day raw and 90-day identity retention are not enforced. The existing retention test uses fewer than 500 rows and manually invokes retention, so it cannot expose steady-state growth or scheduling.

**Fix:** Keep each transaction bounded but persist a reclamation cursor and continue bounded batches on subsequent ticks until caught up, with a budget that exceeds configured production. Add appropriate age/frontier indexes and safe observed-event lifecycle cleanup without allowing event replay to recreate retired windows. Test multiple hours of real worker cadence, more than one batch, restart and an outstanding derived frontier.

### B-2 — Real warm/standing dispatches never acquire task windows (introduced, attribution and missing producer coverage)

**Where:** `server/Infrastructure/HostLoad/HostLoadTaskWindowObserver.cs:36` and `:64`; real producers `server/Application/Services/AgentTaskDispatcher.cs:8777` and `:9036`; fixture `tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs:260`.

**Failure:** Warm and standing dispatches use an already-running session and set a new task `DispatchedAt`. The observer requires `session.StartedAt >= dispatchedAt`, rejects these ordinary dispatches, and nevertheless permanently marks the event observed. Their start/during/end history is absent and the launch is absent from startup-debt inputs. A retry does not repair the consumed event. The added operator documentation acknowledges the omission, but the frozen S1 contract requires authoritative per-task attribution and producer-to-recipient coverage.

**Why:** The fixtures directly add new sessions and task events, always setting session start ten seconds after dispatch. They do not call the actual launch/terminal producers; only retry calls `AgentTaskService`. Therefore V-31..35 and the derived H3 cuts do not establish the promised real launch adapter wiring. On a backwards clock jump, `CloseAsync` also leaves a terminal window open when the terminal timestamp precedes start, then consumes the event.

**Fix:** Persist an authoritative task-attempt start/acceptance fact for fresh, reused and standing sessions, and reconcile that fact independently of process birth time. Preserve honest unresolved state for unavailable facts and handle terminal ordering/clock discontinuity without leaving completed tasks permanently open. Add an ordinary producer-to-recipient test through real dispatch and terminal services, observer restart, and the public load API. If delivered input is used as the acceptance fact, require its matching complete UserPrompt transcript; a queued/Sent/ack flag is insufficient.

### B-3 — Task response can declare complete history despite missing during samples (introduced, receipt correctness)

**Where:** `server/Infrastructure/HostLoad/HostLoadHistoryService.cs:156`; `server/Infrastructure/HostLoad/HostLoadTaskWindowObserver.cs:50` and `:145`.

**Failure:** With valid samples near start and end but no samples during a multi-minute task, both boundary checks set/preserve `window.Complete=true`. `TaskAsync` returns `complete=true` and empty gaps solely from that flag, while `during.cpuPercent` reports zero valid observations, positive expected count and `complete=false`.

**Why:** The outer delivery/completeness verdict ignores required metric coverage and durable gaps. A consumer relying on that verdict can accept incomplete history. Current boundary/overlap tests do not assert this missing-interior case.

**Fix:** Compute task completeness from the event frontier, boundary state, required metric coverage and durable loss state, with an explicit valid empty interval rule for very short tasks. Add a two-boundary/missing-interior API witness that asserts the complete/gaps outcome as well as metric values.

### B-4 — Runner seat clamp is derived from task count (introduced, shadow recommendation)

**Where:** `server/Infrastructure/HostLoad/HostLoadCapacityReader.cs:16` through `:23`; `server/Application/Services/HostLoadRecommendation.cs:35`.

**Failure:** `runnerRemaining = descriptor.Capacity - activeTaskCount` treats declared capacity as a seat inventory and excludes retained tasks. Live manual/orphan sessions and retained input-waiting sessions may occupy the runner without a counted Dispatched/Working task; warm/standing seats also require explicit eligibility accounting. A runner whose seats are all occupied and unavailable to this request can therefore return positive `recommendedAdditionalTasks` under low CPU and a permissive host budget. Existing `RunnerSlotService` independently counts `OccupiesCapacity` from runner inventory; this reader does not consult it.

**Why:** Runner seats and task budgets are independent constraints in the frozen design. The recommendation arithmetic tests inject hand-written capacities and never drive `HostLoadCapacityReader` against occupied runner inventory. Parking is disabled by default; input waiting provides no automatic release deadline (CARD-1083). Neither low CPU nor elapsed time frees that seat.

**Fix:** Use a fresh, independently observed runner-seat inventory/capacity fact, preserve unknown as zero advice, and keep host/project/stage task clamps separate. Add coverage with full warm/standing/waiting seats and few active task rows. Do not change admission behavior as part of this S1 repair.

## Verification completeness

Primary run `20261107-082808-cc60` completed with **RED, exit 5**, `row-deadline-exceeded`. It ran from `2026-11-07T08:28:12.0310414Z` to `2026-11-07T09:44:13.1963482Z`. Its eleven completed TRX files contain **266 executions: 265 passed, one failed, zero skipped**. They have eleven distinct fresh run identities. The full dispatcher roster has all 45 distinct source methods; H3 has all eleven distinct passing argument cases. These are execution counts, not a count of unique tests across overlapping selections.

| Review row | Executed / passed / failed / skipped | Row wall seconds |
|---|---|---:|
| CP-1 | 18 / 18 / 0 / 0 | 7.406 |
| CP-2 | 33 / 33 / 0 / 0 | 4.425 |
| CP-3 | 3 / 3 / 0 / 0 | 3.769 |
| CP-4 | 28 / 28 / 0 / 0 | 239.120 |
| CP-5 | 40 / 40 / 0 / 0 | 49.912 |
| CP-6 | 6 / 6 / 0 / 0 | 21.041 |
| CP-7 | 7 / 6 / 1 / 0 | 158.886 |
| CP-M1 | 6 / 6 / 0 / 0 | 90.936 |
| CP-M2 | 4 / 4 / 0 / 0 | 92.082 |
| CP-G | 76 / 76 / 0 / 0 | 184.350 |
| CP-PHONE-FULL | 45 / 45 / 0 / 0 | 16.962 |
| CP-UNIT | unavailable: deadline, no TRX | 2701.227 |

The Unit row receives **no completed count**, not a zero-failure pass. Its console contains three assertion failures and platform skips before termination: B-6's DTO guard and `CheckpointReceiptSafetyTests.malformed_state_shape_by_verb_table_matches_documented_exits` (`:203`) plus `receipt_row_and_build_identities_refuse_blank_without_writing(CP-fixture)` (`:171`). The latter two expect source validation to succeed, but receive `CHECKPOINT SOURCE INVALID reason=receipt_counts`. They have not been rerun at the base; their attribution is **unresolved**, not inherited. The preserved console is partial execution evidence, not a completed Unit receipt.

The primary budget receipt records wall **4560.855 s**, execution **4560.843 s**, admission-only **0.013 s**. Every row was slot-granted with zero row wait; max concurrent builds was one. There were ten green rows and two red rows (native overhead plus Unit deadline). The executor and Unit host exited, and the tool removed its owned shadow copy. `cleanupRefusals` is empty in the report. The SHA receipt validator exits **2**, `CHECKPOINT SOURCE INVALID reason=report_failed`; no clean certificate is claimed. Tracked source stayed clean and the receipt's build provenance says verified, but a failed report cannot be relabeled clean.

### Incomplete verification (unresolved gate)

**Where:** CP-UNIT above; frozen CP-8 / V-50/V-52 / native Windows R-1/R-6; required Windows startup/manual acceptance; the timing audit below.

**Failure:** Whole Unit did not complete, its logged failures remain unresolved, native Linux overhead failed, and required Windows/manual execution is absent. All PCs remain pending for the separate Mutation stage and are not counted as ordinary evidence.

**Why:** This is only Interim completed scope under the commissioned Final profile. Neither old author receipts, native skips, a declaration lint, nor nightly green closes these gaps.

**Fix:** Have the caller commission performance Debug before another broad Unit run, resolve the exact failing assertions and complete the missing ordinary/native/manual selection through the pipeline. Preserve this failed source and cost record. Do not rerun passing rows solely to relabel evidence or make advisory text changes.

**Additional missing row — CP-10:** Supplemental run `20261107-094434-3b28` ended **RED, exit 2**, before any test execution. Its build obtained a slot after **75 seconds** but was refused as `tampered`: generated native `obj` file lists and compiler caches changed outside the isolated output. Eight exact paths are retained in the build log under `Antiphon.Agents.Pty`, `Antiphon.PtyHost.Protocol`, `Antiphon.PtyHost` and `Antiphon.SessionRunner.Contracts`. Git tracked source and its fingerprint remained unchanged, but `buildSource=unknown`. CP-10 has **zero executed tests, no TRX**, and stays pending. The validator rejects this report as `report_failed`. This is an unresolved build-gate/provenance failure, not an executed test failure and not a claimed product regression. The smallest next step is to investigate the native-output guard interaction and run this exact seven-test class through the gate once provenance is trustworthy; do not run unleased or bypass the refusal. This Review did not retry it or alter the gate.

The supplement's build execution was **172.112 s** and build wall **301.383 s**; total run wall **302.756 s**, execution **227.781 s**, admission-only **74.975 s**. Its executor exited and the tool removed its owned shadow copy. Combined completed TRX counts remain **266 executions: 265 passed, one failed**, with no added count for Unit or CP-10.

### Author-to-plan and ordinary selection mapping

| Plan/checkpoint | Author's latest claimed ordinary result | Review selection |
|---|---|---|
| CP-1 | 18/18 at `a00b224dd70590925cc9b36e9aca3b084a9eee4b` | Exact four-class sampler/authority/spool/wire union |
| CP-2 | 33/33 at `3f39b37fe48c74ff9a7e399574de0dac9d2191c0` | Exact legacy runner and guard union |
| CP-3 | 3/3 at `79f14fa0f529320d9d2c2f82fb7d7cd43656a03a` | Exact three `Host_stats_*` methods |
| CP-4 | 28/28 at `87cf5fd6ae717c6f9b5b4f32a3a7d23aa3444f37` | Full history/window/API classes, all 11 H3 arguments |
| CP-5 | 40/40 at `87cf5fd6ae717c6f9b5b4f32a3a7d23aa3444f37` | Exact eight-class surface union |
| CP-6 | 6/6 at reviewed `0cc57cca7dcb6bc506bfecbde9f22638fe92e308` | Exact six guards |
| CP-7 / CP-8 | Deferred by Code | CP-7 executed here; CP-8 remains pending on native Windows |
| CP-M1 / CP-M2 | 6/6 at `87cf5fd6ae717c6f9b5b4f32a3a7d23aa3444f37`; 4/4 at reviewed tip | Both full migration classes |
| Original CP-9 / completion CP-G | Original 75 guards; completion 76/76 at reviewed tip | CP-G is the current 76-case union including the lane guard; covers original CP-9 |
| Original CP-10 | 7/7 at `d25e9aba811b827be1ba725f557ca694ef16ee35` | Full affected `BuildSlotEndpointTests` supplemental checkpoint |
| Whole Unit | Explicitly deferred, no claimed pass | `/*/*/*/*[Category=Unit]` on `Antiphon.Tests` |
| Additional affected class | Code only selected the three host-stat methods | Full `PhoneHomeCommandDispatcherTests`, as Final requires full affected classes |

Every current receipt must name the reviewed tip, regardless of these older claimed SHAs. The first six portable rows map to V-1..48 and portable R-1..6, subject to the test-adequacy findings above. CP-7 covers V-49/V-51 and native Linux R-1/R-6; CP-8 is the missing V-50/V-52 and Windows native scope. Required Windows startup/manual acceptance is unexecuted. Nightly results and OS-witness declarations do not discharge it.

The primary Review manifest combines the complete ordinary Linux selection and Unit in one checkpoint-tool run, as the stage contract requires. It reuses one verified cold runner build and one verified cold server build at this single SHA; it does not repeat a green checkpoint row. This supersedes the Code packet's three-row grouping for Review. The complete Unit lane and full dispatcher class deliberately overlap some named selections because the Final profile requires both. CP-10 was recovered from the original author's report after the primary manifest had started; its separate, exact supplemental run is listed for that reason, with no repeated class. The only other build is the slot-gated native-output checkpoint-tool bootstrap, required because the tool was not built in this checkout. No product/test driver was launched outside the slot gate.

The primary manifest gives each acquisition a ten-minute bound, serial rows, the frozen portable row caps, a nine-minute Linux row cap, and a provisional 45-minute Unit cap under CARD-1720/1728. Its finite execution/hard-wall budgets are 160/300 minutes. The runner build used the 28-minute build floor; the server build inherited 45 minutes from the Unit row. Observed cold builds were 156.658 and 779.188 seconds, both with zero slot wait. The checkpoint-tool bootstrap compiled in 27.06 seconds (28-second slot wall), zero errors, nine warnings. An initial manifest attempt rejected a floating-point estimate before any build/test ran; it was corrected to an integer. These costs and refusals are not test executions.

These B-1..4 findings are source-trace conclusions with concrete scenarios, not newly executed reproductions. The corresponding missing assertions must be added by Code; this read-only Review did not repair tests or run mutations. Existing selected tests have outcome assertions against persisted sample identities and independently calculated expected values, but that does not establish sensitivity for these missing scenarios. All 102 PCs and their variants remain unexecuted.

### B-5 — Linux sampler overhead exceeds its acceptance budget (observed; cause unresolved)

**Where:** `tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs:33`, `Linux_SamplerOverheadStaysWithinBudget`; CP-7 at the reviewed SHA.

**Failure:** The first measured off/on pair reports `on.CpuPoints - off.CpuPoints = 1.9332088172767952`, against the strict `<1` percentage point of one core limit. CP-7 executed seven tests: six passed, this one failed, zero skipped. The row took 158.886 seconds. The assertion stops the experiment before the second pair and before the subsequent cadence/tick/spool assertions; these are not passing measurements.

**Why:** V-51/R-6's native cost acceptance is not met. The result is an actual outcome assertion, not a build/discovery/fixture failure and not PC evidence. This review does not attribute the cause to either the product or the benchmark without investigation; no inherited-red claim is made and no unchanged green or failed experiment is rerun to obtain a favorable number.

**Fix:** Commission the required performance Debug work on this exact source and owned full-runner experiment. Identify the CPU cost or a demonstrated measurement defect, repair it with appropriate evidence, and rerun the exact native row after that change. Preserve the threshold and original failure/cost; do not widen a timeout, relax the limit or add a retry.

### B-6 — New aggregation DTO breaks the existing public read-contract guard (observed compatibility failure)

**Where:** `server/Application/Dtos/HostLoadDtos.cs:8`; existing `tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs:432` / assertion at `:476`.

**Failure:** The Unit console logs `Public_read_contract_serialization_does_not_expose_secret_value_properties` failed because it finds `Antiphon.Server.Application.Dtos.HostLoadMetricPoint.Value (value)`. There is no final Unit TRX after the row deadline, so this is a logged assertion failure, not a completed Unit count. The exported DTO is added by this candidate; the guard is unchanged across the reviewed range. The numeric observation is not evidence of an actual secret leak, but the new public contract violates the existing contract rule and prevents a green Unit receipt.

**Why:** A calculation input was placed among exported public read DTOs using a property name the existing read-contract guard forbids. The narrow S1 checkpoint selection never exercised this existing guard.

**Fix:** Keep the internal aggregation input separate from the public read DTO contract, or give its property a contract-compatible name, without broadening or disabling the secret-protection guard. Run the exact guard and affected aggregation checks after repair. Do not describe the current candidate's Unit lane as green.

### Timing and incomplete evidence

The fresh duration tripwire exits 1 for CP-4 (12 unlisted executions at least five seconds) and CP-5 (three). CP-4's entries are `CurrentAndTaskReadsRespectExistingScope` (5.471 s), ready/busy delivery (9.439/8.042 s), and nine ingest/derive H3 cases (11.241–18.400 s); the two append cases are below five seconds. CP-5's entries are existing `HostStatsEndpointTests.Hosts_stats_answers_the_cache_projection_with_counters` (5.165 s) and new script `TokenStaysInHeaders`/`FailuresNeverPrintPositiveCapacity` (9.185/9.832 s). The native CP-7 duration audit passes its allowlist; that does not overturn its failed CPU assertion. Classification guards only validate declared classifications and therefore do not cure these measured omissions. Code must address measured cost/classification with the policy-required rationale; no blanket allowlisting or inherited-failure attribution is supplied here.

The same read-only duration audit exits 1 for CP-G with 13 unlisted guard executions at least five seconds, including `C487_G069(ordinary)` at 71.967 seconds. CP-M1 and CP-M2 pass the duration registry check. These audit runs read existing TRX files; they do not rerun any test. Logs retain all exact cases and timings. Attribution of the guard/legacy timing observations remains unresolved, rather than being described as an introduced product regression.

This run retains TRX per-test durations and checkpoint startup/body/teardown plus build/slot timings. `ANTIPHON_TEST_PHASE_ROOT` was not enabled: finer Docker/readiness/clone/migration/external-call attribution is unknown, not zero. Native ambient host load during the pair was not independently recorded. These R4/R6 evidence limits remain explicit. The required long-run performance Debug handoff cannot be delegated from this delegate; the caller owns dispatch with the exact run, source, selection, timings and host facts.

## Other audit conclusions and advisories

- **Admission:** No new telemetry consumer was found in grant/hold/refusal/queue decisions. `scripts/build-slot.ps1`, `scripts/delegate.ps1`, `AgentTaskDispatcher.cs` and `BuildSlotBroker.cs` are byte-unchanged across the candidate. The broker change adds only GET `/build-slots/host-load`; pipeline/runner changes project nullable shadow advice. This does not cure the advice defect B-4.
- **Sampler:** Default cadence remains five seconds; TryWrite decouples the sampler from disk/authority I/O; append queue is capped at 48 by default, authority reads are cached for five seconds with three-second/64-KiB bounds, and spool retention is 24 hours/256 MiB with signed acknowledgement cursors. Native overhead evidence must be read separately from arithmetic/seam tests. Linux null probe results skip the tick; no explicit spool gap is emitted for that case, so explicit probe-failure gap coverage remains an acceptance concern.
- **Recovery:** D1/D2/D3/D5 tests exercise actual capture/spool, direct HTTP or production phone-home dispatcher, bounded ingest channel, fresh PostgreSQL reads, public API and joined script stdout. Ready and held-recipient tests assert sample identities/values after release. Eleven H3 argument cases cover append, enqueue, commit and derived frontiers. These telemetry recipients are storage/API/stdout, not session input. Their receipts do not prove D4's missing actual task-start producer.
- **Aggregation:** Exact p95 is the one-based ceiling of 0.95*n; null empty metrics and one-sample behavior are asserted. Histograms retain count/sum/extrema and declare approximation/bin width. Raw-backed exact statistics are separate from histogram quantiles. Per-host raw row estimate is 120,960 at seven days; identity rows are 1,555,200 at 90 days before B-1's excess growth. Hour rollups are up to `(16 + 2*volumeCount)*24` metric rows per host/day; null metrics reduce this count.
- **Migrations:** The two IDs follow `20261106032116_AddRefOnlyParkWorkspaceSnapshot` linearly. Existing event-table additions are nullable; membership columns are new-table additions with defaults; there is no rename/type change or destructive Down. Both Down paths throw. Runtime `HasPendingModelChanges` is asserted by the migration classes. This is additive compatibility by inspection and SQL witnesses, not a claimed old-binary/native deployment run.
- **A-1 (operational evidence):** `20261107034430_HostLoadTelemetryS1.cs:184` creates a normal, non-concurrent index on the existing event table inside the migration transaction. Nullable additions avoid a data backfill, but lock/scan duration on a populated event table is unmeasured. Retain an explicit startup/rollout measurement obligation; the private migration tests do not measure a production-sized event table.
- **API/script:** Host reads follow sibling read authorization; task reads use existing task resolution/polling scope. Queries enforce scalar parsing, range caps and raw/hour pagination; raw snapshot-membership test excludes a transaction committed after page one. The script uses bounded GETs, header-only task credentials, one JSON stdout object, separate error stderr and live global placement lookup. Source is ASCII. It explicitly requires PowerShell 7 and is not Windows PowerShell 5.1-compatible; native Windows script/startup work remains pending if 5.1 compatibility is required by the commissioning prose. Volume paths are deliberately included by the frozen DTO contract, so a literal no-path-output requirement needs a contract correction; no command lines or task goals are sampled.
- **Performance:** Per-session shared DB/container/world setup is present; historical migration tests justify fresh databases and record phases. Existing row budgets and provisional CARD-1514 references are retained. Full Unit/performance work over 30 minutes requires the caller's Debug follow-up under CARD-1720/1728; this delegate cannot sub-delegate. No claim is made that an exit code, discovery list, timeout or build supplies executed tests.
- **Activation:** Both server and session runner must restart after reviewed rc integration; the separate shared build-slot broker must also pick up its new observation route if that authority is used. No restart, deployment, activation, desktop execution, land or policy write was performed here. No changed input-wait/release implementation was found.

## Provenance and reporting

The named author report files were absent from this assigned checkout. Their complete copies were found in the authors' retained worktrees and read there. API task detail confirmed the original owner and this Review's Final profile; live runner defaults/catalogue were read without embedding fleet placement into code. Historical author counts remain attached to their original SHAs, never retagged as this review's source.

`git merge-tree --write-tree origin/master HEAD` was conflict-free, tree `c2bc3597dd083db9cd77c0474bdb7d583d023499`. `check-evidence-diff.ps1` initially refused current master as non-ancestor; the complete candidate merge-base range then passed: 32 commits, zero evidence entries/violations. No target merge was performed.

Both checkpoint runs are terminal and their executor processes and the Unit host have exited. The original reviewed source remains `0cc57cca7dcb6bc506bfecbde9f22638fe92e308`; the requested report-only commit is separate and is identified by the final task progress marker. Master was fetched again after the primary run and remains `5d5973be2cd867bf592cd0363240e728f64d3b28`; merge-tree again returned the same conflict-free tree.

Full unedited reports, TRX files, executed rosters, fresh run IDs, source/build stamps, logs, resolved manifests, duration audits and SHA-256 copy inventories are retained outside the worktree at `/work/repos/antiphon/.git/review-evidence/8da334c5-0568-4f17-89ad-613169da7f35/`, in each original run-ID subdirectory. Original absolute runtime paths in the unedited receipts map to that archive using the same suffix below the run ID. The archive preserves the failed reports; it does not manufacture a clean certificate. Both receipt-validation logs record exit 2 / `report_failed`. The primary's exact essential receipt lines follow below.

## Unedited current-source checkpoint evidence

~~~text
CHECKPOINT CP-1 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=ok filter=/*/*/(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=7.355s wall=7.407s
PHASES CP-1 slotWait=0s build=156.6578906s startup=2.7607189s testsWall=3.1504084s teardown=0.5865954s hostWall=6.4976919s
CHECKPOINT CP-2 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(HostStatsProbeParseTests*)|(HostStatsEndpointTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=4.375s wall=4.425s
PHASES CP-2 slotWait=0s build=0s startup=2.4449215s testsWall=0.7337001s teardown=0.5654015s hostWall=3.7440294s
CHECKPOINT CP-3 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/PhoneHomeCommandDispatcherTests/Host_stats_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=3.728s wall=3.769s
PHASES CP-3 slotWait=0s build=0s startup=2.5881048s testsWall=0.1425827s teardown=0.4077905s hostWall=3.138478s
CHECKPOINT CP-4 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=ok filter=/*/*/(C1514HostLoadHistoryTests*)|(C1514TaskLoadWindowTests*)|(C1514HostLoadApiTests*)/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=239.074s wall=239.120s
PHASES CP-4 slotWait=0s build=779.187655s startup=41.8243856s testsWall=193.6188685s teardown=2.8897165s hostWall=238.3329707s
CHECKPOINT CP-5 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/(C1514HostLoadAggregateTests*)|(C1514HostLoadRecommendationTests*)|(C1514HostLoadScriptTests*)|(HostStatsCacheTests*)|(HostStatsPollServiceTests*)|(HostStatsAntiphonCountersTests*)|(HostStatsEndpointTests*)|(SessionRunnerHttpClientHostStatsTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=49.860s wall=49.912s
PHASES CP-5 slotWait=0s build=0s startup=18.3391865s testsWall=27.9113017s teardown=2.7322336s hostWall=48.9827218s
CHECKPOINT CP-6 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)|(every_test_class_is_tagged_unit_xor_integration*)|(WrapperOnlyPaidManifest*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=20.989s wall=21.041s
PHASES CP-6 slotWait=0s build=0s startup=8.0211691s testsWall=11.5186018s teardown=0.6399371s hostWall=20.1797078s
CHECKPOINT CP-7 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*) executed=7 passed=6 failed=1 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=158.833s wall=158.886s
PHASES CP-7 slotWait=0s build=0s startup=2.5666923s testsWall=154.2155101s teardown=0.8504134s hostWall=157.6326158s
CHECKPOINT CP-M1 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/C1514HostLoadMigrationTests*/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-M1/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-M1 waited=0s executed=90.871s wall=90.936s
PHASES CP-M1 slotWait=0s build=0s startup=67.2195326s testsWall=21.0174945s teardown=1.7761026s hostWall=90.0131292s
CHECKPOINT CP-M2 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/C0824RefOnlySnapshotMigrationTests*/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-M2/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-M2 waited=0s executed=92.032s wall=92.082s
PHASES CP-M2 slotWait=0s build=0s startup=55.9306556s testsWall=32.4239232s teardown=2.9249304s hostWall=91.2795095s
CHECKPOINT CP-G commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=76 passed=76 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-G/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-G waited=0s executed=184.291s wall=184.350s
PHASES CP-G slotWait=0s build=0s startup=16.6754447s testsWall=166.2461618s teardown=0.5389029s hostWall=183.4605091s
CHECKPOINT CP-PHONE-FULL commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/PhoneHomeCommandDispatcherTests*/* executed=45 passed=45 failed=0 skipped=0 trx=/work/worktrees/task-8da334c5/.antiphon/checkpoints/20261107-082808-cc60/rows/CP-PHONE-FULL/run.trx slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-PHONE-FULL waited=0s executed=11.585s wall=16.962s
PHASES CP-PHONE-FULL slotWait=0s build=0s startup=2.671512s testsWall=7.5753675s teardown=0.6374068s hostWall=10.8842862s
CHECKPOINT CP-UNIT commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=45m slot=granted waited=0s dirty=0 source=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-UNIT waited=0s executed=2701.175s wall=2701.227s reason=row-deadline-exceeded
BUDGET wall=4560.855s execution=4560.843s admissionOnly=0.013s
verdict: RED exit=5 reason=row-deadline-exceeded
~~~

~~~text
CHECKPOINT CP-10 commit=0cc57cca7dcb6bc506bfecbde9f22638fe92e308 build=failed filter=/*/*/BuildSlotEndpointTests*/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=75s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-10 waited=75s executed=0.000s wall=0.000s
BUDGET wall=302.756s execution=227.781s admissionOnly=74.975s
verdict: RED exit=2
~~~

## Cleanup and deliverable

After both runs and all command clients exited, 13 exact generated output roots were removed: the three isolated build roots, native bootstrap/helper bin/obj roots, and the two task checkpoint-result roots. Every target was checked as a nonempty, non-link, resolved path beneath the assigned worktree with no tracked entries. The cleanup script uses literal absolute rm arguments, no glob, variable target, force flag, loop deletion or Docker operation. The eight flagged generated native-cache files were archived before deletion. No tracked source was changed or restored. Full archive hashes were verified after cleanup. Only this requested Markdown report is committed on the assigned Review branch; the original Code branch is unchanged. Cleanup commands and results remain in the external evidence root.
