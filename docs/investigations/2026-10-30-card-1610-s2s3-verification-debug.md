# CARD-1610 S2/S3 verification Debug

Commission: task 40e35f61, Linux/server2, 2026-10-30. Initial source
be2e50a02ec2a01990b0aaa796558d6b5a16b376. S2 and S3 must ship together.

## Timing assessment before execution

The preceding report's completed CP-27/28 hosts took 232.928/95.637 seconds
(341/382 executions); rows including admission took 278.712/141.528 seconds.
The last producer took 516.457 seconds wall, 426.436 seconds recorded build,
30 seconds admission. Those samples justify first rerunning the repaired
CP-17/28/52 and required CP-16 with one isolated producer, then continuing the
remaining exact rows from that same unchanged output. No repeated broad sweep
solely to obtain statistics is commissioned. This Debug record precedes the
potentially >30-minute aggregate. No desktop or native qualification is used.

Admission is bounded at 25 minutes per acquisition (caller explicitly allows
10–25 minute waits). Rows are serial because inherited integration fixtures share
state; production build occurs once for the selected S2/S3 group. Initial row
bounds retain max(2 minutes, 2x plan estimate), with CP-27/28 reduced to 10 minutes
from the provisional 50 on the above measured host samples. Smaller samples remain
provisional (CARD-1404); build and row custody work and variable load justify the
finite margin. Shared producer's scheduler minimum is 15 minutes. Overall task
budget is 150 minutes; stop before starting work that cannot be joined. Record
actual per-test p95, phases, admission and unattributed remainder after each run.
Unknown component timings remain unknown. Existing 20/90 phase deadlines and test
assertions are not altered for performance. No definition update is authorized.

## Initial failure triage

The historical 45 CP-28 failures belong to DelegationDispatchRecoveryBoundaryTests.
42 current-only cases were repaired by 5039ccb73e5482ef33c89258db00beca36a8afe9:
AbsentLaunchPolicy's strict EF column census needed to classify the new nullable
SourceTaskSupervisionNoticeId and require null for a pristine original brief.
Three C1151_Ceiling_failure_note_has_one_complete_user_prompt cases (eligible,
busy, crash) also failed on S2 tip b588cb5d058d810c1d56e4c6bca0196435f78349;
combined-feature attribution remains unresolved until this Debug reproduces them.
Receipt starvation fix e34fc074266f3e0a168a105880339038857bd976 also needs green.
The preceding custody refusal executed zero tests and remains incomplete evidence.

## Results

Pending. Exact rows: CP-15/16/17/19 and CP-21 through CP-91. S4-only
CP-13/14/18/20 and S1-only runner/native rows are excluded. Historical red
provenance is retained, never rewritten or rebound to this tip.

## S2 baseline reproduction (before repair)

Run 20261030-160625-cf79 on detached S2 tip
b588cb5d058d810c1d56e4c6bca0196435f78349: the exact C1151 method executed
3, passed 0, failed 3, skipped 0; clean source and verified build. All modes
(eligible/busy/crash) failed `await host.OverdueAsync(): expected 1, actual 0`.
The fixture supplies no `CountingRunner` session and no transcript observation.
S2 correctly defers an unknown ceiling observation, including this idle fixture.
This is an S2 integration-fixture incompatibility; it is not proved pre-existing
on master. Preserve the caller-note protection: provide the same seeded idle
session's actual accepted generation and complete two-entry transcript, then
retain every original failure, complete UserPrompt, dedup, busy/crash, decoy,
zero-stop, zero-input assertion. No production weakening is indicated.

Baseline build 326.992s; row wall 75.213s/admission 0s/execution 75.128s;
host startup 58.887s/tests wall 9.770s/teardown 5.413s/host 74.070s.
Run wall 425.106s, charged execution 425.093s, admission-only 0.013s.
Per-component DB/container/IO timing not yet extracted; unknown stays unknown.
Row deadline 4 minutes; source/build receipt and original report/TRX copied under
`.antiphon/c1610-debug/base-evidence/20261030-160625-cf79` before scratch cleanup.
Tool-bootstrap build (unlisted, required to execute the checkpoint CLI): 4.70s
compiler, 0 errors/5 existing warnings; admission 30s, slot held 6s. Import reused
that tool under a separate granted slot (0s admission, held 3s), accepted 92 rows.
The initial `--output` spelling was ignored by the importer; its default
`.antiphon/checkpoints.yaml` is the retained authoritative import. A scratch-cwd
preparation attempt and wrong-root wait refused before any build/test; no credit.

CHECKPOINT CP-28-s2-c1151 commit=b588cb5d058d810c1d56e4c6bca0196435f78349 build=ok filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1151_Ceiling_failure_note_has_one_complete_user_prompt executed=3 passed=0 failed=3 skipped=0 trx=/tmp/c1610-debug-s2-b588cb5d/.antiphon/checkpoints/20261030-160625-cf79/rows/CP-28-s2-c1151/run.trx slot=granted waited=0s dirty=0 source=b588cb5d058d810c1d56e4c6bca0196435f78349 sourceState=clean buildSource=verified

## Verification amendment and protective intent

C1151's fixture now supplies its actual seeded idle transcript and accepted
Running generation through the real runner-client seams. Every original assertion
is unchanged. CP-28 still selects the full 382-case class.

CP-92 is an explicit six-case plan amendment closing the prior report's untested
retention safeguards. Each case asserts pending debt retains its evidence, an
unrelated eligible control is pruned, and the same evidence becomes prunable after
Told. Removing the corresponding production exclusion would fail the retained
assertion; unconditional retention would fail the control/release assertions.
It adds coverage, not a production change. All shared-setup uses the assembly DB
fixture and per-case isolated schemas. No new process spawn or Slow exemption.

The selected roster runs CP-17/28/52/16 first so repaired rows cannot be hidden
behind lower-priority regressions, then every remaining S2/S3 row. Ordering is a
recorded scheduling deviation; exact original filters and floors remain intact.
The two historical broad classes form the aggregate CP-27 + CP-28 (723 cases).
Do not run another duplicate aggregate: those same complete rows satisfy it.

First combined producer 20261030-161544-a4d6 at d5bda599b1edc88e52fa5d43e4c6114747417422
failed compilation: three CS0246 errors from the fixture's missing
Antiphon.Server.Application.Dtos import. This is introduced by this Debug task,
not inherited. No tests executed and no red/pass witness credit is claimed.
Compiler wall 168.15s; producer recorded 216.070s, execution 216.378s,
wall 227.773s, admission 0s; source clean/build unknown. All 76 rows were
build-failed. Correct only the import and rerun the same roster, with a fresh
isolated output; retain this failed receipt and cost.

## First verified producer and remaining C1151 root cause

Run 20261030-162011-5604 at bbd0a546ec10e4722a4ee386d673d744a7a274e1:
producer compiled with zero errors (compiler 370.91s), clean source and verified
build. Build admission 285s. CP-17 executed/passed 22, failed/skipped 0: the
starvation repair is verified. Row wall 199.832s/admission 105s; startup
50.093s/tests wall 40.156s/teardown 3.788s/host 94.036s.

CP-28 executed 382, passed 379, failed 3, skipped 0. Exact historical identity
comparison proves ALL 42 census failures now pass. The three C1151 modes still
fail the original count assertion, not a relaxed replacement assertion. Runner
and snapshot evidence alone did not complete the fixture: OpenSweep lacks
SessionStateStore, so PersistTranscriptAsync records `Transcript ingestion
requires SessionStateStore` and CatchUpForCeilingAsync defers. Enable the real
SessionStateLoader/Store and action coordinator only for this fixture, preserving
the other statement-budget harnesses and every original C1151 assertion. No
production change is indicated. CP-28 wall 199.500s/admission 75s; startup
34.039s/tests wall 78.174s/teardown 11.040s/host 123.253s.

CP-52 executed/passed 247, failed 0, skipped 2; min 249 is UNCHANGED, so this is
roster-miss, not green. The two platform skips are:
- AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash:
  `Canonical pin cwd is a Windows drive-rooted path`.
- AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone:
  `Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows`.
No native test is authorized in this Linux task. Do not repeat unchanged Linux
execution or lower the floor: the caller must commission the missing native
coverage separately. All AbsentLaunchPolicyTests and their strict census guard
passed in this row. CP-52 wall 112.027s/admission 105s; startup 4.877s/tests
wall 0.484s/teardown 0.752s/host 6.113s.

Stopped the scheduler before editing source to repair C1151. CP-16 was waiting
for a slot; it executed zero tests. The supported stop returned 0, subsequent
wait returned stopped/6, and executor PID 14421 was absent before edits. This
stopped run has no completed top-level report.json and is not a completed gate;
its state, TRX and original per-row source-bound CHECKPOINT lines are retained.
The next run reruns full CP-28 and all unexecuted rows from one fresh producer.
Unchanged CP-17 is not repeated; CP-52's explicit native gap remains owed.

Real land graph -Check PASSED at bbd0a546ec10e4722a4ee386d673d744a7a274e1,
using its verified producer's staged Antiphon.LandGraphAudit.dll, both evaluation
passes and canary approved, exit 0. Digest
7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68;
canary /work/worktrees/antiphon-landgraph-canary-74a71fbe2eb04c46bb0cca94caf4213f.
Outer slot waited 90s, held 39s. No -Update or definition-file mutation occurred.
Evidence-history guard over be2e50a..bbd0a546e: four commits, zero violations.

## Completed broad witnesses and newly exposed reds (before repair)

Run 20261030-164314-bf74 at d36e79f432307ff26ab687d06bad5e9a5f92cff1
verified full CP-28 **382/382**, including all 45 historical failure identities
(exact test-name comparison: 45 Passed). CP-27 also passed **341/341**, so the
broad-class aggregate is **723/723**, zero failures/skips. CP-16 met its unchanged
44 floor, CP-15 passed 12, CP-19 passed 22. The remainder's per-row state/TRX and
original CHECKPOINT lines are retained; final tables follow after repairs.

New red witnesses at that same source, before any repair:

| Row/test | Observed failure | Cause and required repair |
|---|---|---|
| CP-22 a_queued_only_prompt_is_no_boot_and_keeps_the_ceiling_failure(working-inherited-mid-turn) | expected Failed, actual Working | Obsolete pre-S2 expectation. Preserve no boot/failure/stop, assert the typed counted Working hold and one Held event. Idle arm still proves exact ceiling failure. |
| same method(terminal-session) | expected Failed, actual Dispatched | Live-ceiling sweep correctly defers a terminal session to dead-session ownership. Assert unchanged terminal-task status, no hold/completion/failure, no boot event and no stop. Existing complete dispatch-recovery class independently covers dead-session reconciliation. |
| CP-23 a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it | missing Closing on the deadline | Production copy collapsed approaching and breached ceilings. Keep the existing temporal assertion and give approaching decisions their own prefix. |
| CP-23 a_breached_deadline_says_the_sweep_is_about_to_fail_it | missing Past its deadline | Restore a distinct breached-decision prefix without promising failure for Working ceilings. Keep assertion and phase copy. |
| CP-26 C1151_Boot_branch_statement_counts(working-first-detection), (operator-escalation), (event-save-fault), (non-working-listed), (absent-proof-refused) | exact SQL budget 29 observed 30 | S2 adds an unnecessary generation SELECT after BootStallFacts already carries SessionStartedAt. Reuse that accepted observation for boot paths, retain non-boot reads and writer generation fences. Do not increase budgets. |
| same method(identity-changed-before-event) | exact SQL budget 23 observed 24 | Same redundant query; retain original exact budget and identity interleaving assertions. |
| CP-26 C1151_Only_an_accepted_prompt_is_protected(queued-only-past-ceiling) | expected overdue count 1, actual 0 | Fixture has a matching accepted runner generation but supplies no complete ceiling observation. Use the established seeded-observation seam for this arm only; keep every original idle-failure/no-boot/no-stop assertion. |

CP-22 executed 36 / passed 34 / failed 2 / skipped 0. CP-23: 167 / 165 / 2 / 0.
CP-26: 59 / 52 / 7 / 0. All are introduced S2 behavior/cost integration issues;
none is blamed on master or waived as inherited red. No assertion budget, timer,
retry or selection is relaxed. Their committed pre-fix tests are the red witness.

CP-46 passed 215, failed 0, skipped 1, min 216 unchanged (roster-miss):
AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine
reports `The npm shim layout is Windows-specific`. Retain this native-placement
gap alongside CP-52's two skips; no unchanged Linux rerun is warranted.

Stopped the broad scheduler using its supported stop after CP-48: 27 green rows,
three red rows and CP-46 roster-miss; CP-49 was waiting for a slot (195 seconds,
zero tests), 42 later rows queued. Wait returned stopped/6 and executor PID 25017
was absent before source edits. This prevents leaving the known product defects
unrepaired inside the 150-minute task budget. No stopped run is a completed gate.
The next single producer runs the affected rows first, then the exact unexecuted
roster. Previously passing unrelated rows are not repeated. Historical outcomes
and costs remain intact, including the earlier stopped CP-17 green observation.

## Batched repair after the red witnesses

Production repair reuses `suspected.Boot.SessionStartedAt`, already read by the
boot policy, and retains the existing pre-pull SELECT for every non-boot path.
The boot writer still rechecks ownership/generation under its own fence. This
removes the redundant statement without raising any exact SQL budget or changing
phase clocks, ceiling hold admission, CARD-1604 or stop authority.

Attention now distinguishes `Closing on the deadline: a role ceiling decision
is approaching` from `Past its deadline: a role ceiling decision is due`.
It continues to say Working remains counted and unknown observations defer.
The two original headline assertions remain intact; phase-deadline wording is
unchanged. Queued-only test shapes retain all three cases and the original
idle-failure assertions; the Working and terminal shapes gain explicit assertions
for their S2 owners, including no failure/completion/stop/boot event.
The separate BootStallWorld queued-only arm now explicitly supplies its seeded
complete observation and retains all original failure/no-boot/no-stop assertions.

The next producer prioritizes CP-26/22/23/16/17/28/92, then every unexecuted row
from CP-49 onward. CP-17 receives a current-source completed-row receipt because
the earlier 22-pass result belonged to a stopped run. Its earlier outcome and
cost are retained. This is the required targeted verification after production
and fixture repairs, not an arbitrary full rerun. The new build is necessary
because source changed; previous outputs remain untouched until all runs end.

## Final verification result

All 45 historical failures and 11 newly exposed regressions are fixed and verified; the closed S2/S3 gate remains INCOMPLETE because of platform skips, checkpoint timeouts and unexecuted rows. S2 MUST NOT LAND ALONE.

Final tested implementation: 192f642b2b0a5d23f9f9e33112adb8c43efe579d. Task base: be2e50a02ec2a01990b0aaa796558d6b5a16b376. Combined S2+S3 review base: 698bf1b73e8abf449d5569ea74ac8c82e80f92a1. Assigned branch: feat/card-task-40e35f61. All meaningful source slices were pushed before verification. The settlement report commit is documentation only and is not a new implementation test run.

The final seven priority rows (CP-26/22/23/16/17/28/92) passed 716/716, zero failed/skipped, on the final implementation. That includes all 45 original failing identities and every new regression assertion. CP-27+CP-28 broad aggregate passed 723/723 at d36e79f432307ff26ab687d06bad5e9a5f92cff1; full CP-28 passed again on the final implementation. Exact source identity is retained per row below.

The latest available per-row observations contain 5,177 executed/passed, zero known assertion failures and 28 platform-skipped cases. These span the explicitly named source tips; they are not a single passing qualification run. Of 76 required S2/S3 rows (including added CP-92), 44 have green observations, seven miss their floors through Windows-only skips, CP-67 timed out, CP-68 was interrupted by the execution budget, and CP-69..CP-91 were not run. Interrupted rows have unknown counts, not zero-pass or red-assertion credit.

## Repairs and protective intent

- d36e79f432307ff26ab687d06bad5e9a5f92cff1 completes the C1151 fixture with its actual accepted runner generation, complete seeded transcript and real SessionStateLoader/Store/action coordinator graph. All original full UserPrompt, busy/crash, dedup, decoy, unchanged-outcome and zero-stop/input assertions remain. Its three original failures reproduced 3/3 on S2 tip b588cb5d058d810c1d56e4c6bca0196435f78349; no pre-existing-master claim is made.
- The inherited production census repair 5039ccb73e5482ef33c89258db00beca36a8afe9 is now verified: SourceTaskSupervisionNoticeId is classified and must be null for a pristine original brief. All 42 historical census failures pass.
- 192f642b2b0a5d23f9f9e33112adb8c43efe579d reuses the already observed boot generation; non-boot generation reads and boot writer ownership fences remain. All six original exact SQL budgets (29/23) pass without changes to their assertions.
- The same commit restores distinct approaching/breached ceiling headlines while saying Working stays counted; both original temporal headline assertions remain unchanged. Phase-deadline copy is unchanged.
- Queued-only test cases retain the original idle-ceiling failure and no-boot/no-stop checks. Working now additionally asserts a persisted active hold, one Held event, and no failure/completion. Terminal-session asserts unchanged Dispatched status, no hold/failure/completion and an unchanged stopped session, leaving dead-session ownership intact. No case was removed.
- CP-92 adds six isolated-schema retention checks. Pending supervision debt retains source/recipient sessions and transcripts, the linked queue row and the whole task tree; unrelated eligible controls are pruned; the same protected evidence becomes eligible after Told. These checks passed 6/6. No process-spawn exemption or Slow allowlist change was added.

Files changed include /work/worktrees/task-40e35f61/server/Application/Services/AgentTaskDispatcher.cs, /work/worktrees/task-40e35f61/server/Application/Services/AttentionService.cs, five affected test/harness files, the plan's explicit CP-92 amendment and /work/worktrees/task-40e35f61/docs/investigations/2026-10-30-card-1610-s2s3-verification-debug.md. No migration, project/build definition, configuration, daemon script or client source was changed by this Debug task.

## Invariant review

- No new stop/kill/release authority: hold/typed decisions/notices/late forwarding contain no new session-stop calls. CP-16 passes all roles, phase failures, normal settlement, explicit Cancel/Retry, restart identity and successor fencing. Automatic Working-session stop remains only the existing qualified CARD-0079 path; full CARD-0079 CP-85..91 execution is still owed.
- Held tasks remain Working. The unchanged stage/absolute/local/pipeline/Check selectors are exercised by CP-16's four real occupancy arms and CP-34 (8), CP-35 (25), CP-36 (69), all passed at their recorded sources. No selector was broadened to Blocked.
- Defaults remain 20-minute model wait and 90-minute local tool. CP-16's real phase precedence/continued-supervision cases pass on final source; CP-22's complete 36-case regression also passes there.
- CARD-1604 heartbeat formula is unchanged: later transcript/token_count timestamp, capped by task age, only for model wait. CP-25's 39 policy cases and CP-22's fresh/stale/silent heartbeat cases pass. No transcript row is minted from token_count.
- S1 zero-row retention and FailedTaskSessionOwnership source are unchanged. CP-19 visibility (22) and CP-21 pool (48) pass at d36e79f; the final production delta does not touch those paths. No historical receipt is relabeled as final-source execution.

## Performance, provenance and incomplete work

The pre-run Debug assessment and complete per-row table live in the tracked investigation. All drivers used granted host slots, TEMP=/tmp and UseAppHost=false, with serial rows, one isolated producer per necessary source group, 25-minute acquisition bounds and finite execution/wall bounds. No unleased fallback, NoSlot, native/desktop run, provider login, live qualification d47848cc, deployment, tag, target merge/rebase or card write occurred. Existing caller authority covered these server2 actions; no extra approval was requested.

The first combined producer failed with three CS0246 errors from my missing DTO using; zero tests executed. bbd0a546ec10e4722a4ee386d673d744a7a274e1 fixed that import. Earlier source bbd verified receipts/fairness, then still exposed the three C1151 fixture failures; these were fixed and verified in d36. Both earlier stopped runs remain incomplete gates with original state/TRX/lines retained. Their costs and outcomes were not overwritten.

Final run 20261030-173840-e5d5 ended done/exit 5 at 18:18:02 UTC: execution 1802.157s against 1800s, wall 2357.436s, admission-only 555.279s, independent wall cap 2400s. Final producing build: compiler 331.39s / zero errors; recorded build 387.031s, wall 422.728s, admission 0s, build ceiling 24 minutes (scheduler coupling to the largest dependent row). Build cost exceeds 125% of the first CP-26 four-minute estimate; this advisory remains open. It is charged once. Unknown detailed DB/container/IO phases remain unknown. No timeout or retry was widened to make red green.

CP-67: row-deadline-exceeded at six minutes, wall 364.183s / execution 364.030s / admission 0s; no TRX, per-test journal or useful progress after the TUnit banner. Startup versus body versus teardown is unknown; the last method and root cause are unresolved. No inheritance or product-defect claim is made. Before repeating this 216-floor selection, commission instrumented Debug and disjoint method partitions with unchanged coverage; do not blindly repeat or widen it. CP-68 then reached the global execution budget, also without trustworthy counts. Their exact filters and all other unexecuted filters are below.

The receipt validator correctly rejected the final partial report with report_failed, exit 2. This is not Clean/Full, ReviewedSourceClean is not certified, and no land is authorized. All owned executors/observers were joined; PID 17840 was absent before report edits. A final command-line inventory found no dotnet child from this worktree.

The duration tripwire analyzed 51 retained TRX files and reported advisories in 20 rows: CP-28, CP-16, CP-19, CP-21, CP-22, CP-25, CP-26, CP-27, CP-38, CP-39, CP-41, CP-42, CP-43, CP-44, CP-45, CP-46, CP-48, CP-57, CP-65, CP-92. The new retention source-transcript arm measured 5.106s; the existing successor race measured 10.643s. No allowlist was changed. Full logs, all slow-case observations and static direct ProjectReference inventory (50 projects, imported/dynamic references not independently qualified by that inventory) remain ignored under /work/worktrees/task-40e35f61/.antiphon/c1610-debug. These are ordinary Linux backend supervision/database/queue Unit and Integration observations, not scoped rc qualification.

The real land graph -Check passed on final tested implementation using its gated producer's staged audit DLL: both evaluation passes and canary approved, exit 0; digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68. Audit slot waited 105s / held 28s; canary /work/worktrees/antiphon-landgraph-canary-f830090713cc4d0e8049f36c94a3fa3b. No -Update ran. Fresh origin/master 31f990317038635c2f1e652b6a950905fcf07a31 merged conflict-free via merge-tree against implementation 192f642b2: tree f16c429a6c7ff751ee28a4ff59768623407c14df. No branch merge occurred.

Inherited schema changes are additive: S2 adds nullable hold fields plus a false-default nonnullable Boolean and a new notice table; S3 adds nullable SourceTaskSupervisionNoticeId plus its filtered unique index. They are not all nullable. A master/rc port must carry both S2/S3 implementations, both CLI-generated migrations/designers and the matching model snapshot together, apply migrations before activating that source, and receive ordinary joint Review plus separately reviewed rc ports. S4 remains ordered behind CARD-1615/CARD-1609. No production database was migrated here.

After every run/audit/observer finished, all seven owned compiler-output roots (six in the assigned checkout plus the S2 scratch output) were removed with checked file-by-file deletion and empty-directory removal; canonical roots and links were checked first. The initial rm -rf command was automatically rejected before execution; the safer deletion completed. The own detached S2 worktree was removed after its unedited receipt copy was verified. No evidence directory was deleted or staged. Unknown pre-existing bin/obj content was preserved.

## Next action

Code must close the exact missing selections below: mixed-platform rows CP-46/52/54/55/56/62/64 need explicit placement preserving all 28 skipped cases and original floors; native work needs a separately authorized native task. Do not rerun unchanged Linux skips or infer a combined pass without accepted source-qualified evidence. CP-67 needs instrumented Debug before another execution; CP-68 and CP-69..91 remain exact missing execution. Retain the incomplete gate. Opus Review of S2+S3 together follows only after all required rows are green; S2 must never land alone.

No new card was filed. Proposal for caller triage: repair the mixed-platform checkpoint placement (without dropping coverage) and commission the exact native methods listed below; use existing CARD-1404/1514/1531 for measured budgets and missing phase/timeout diagnosis rather than inventing a pass.


## Producer and run history

Stopped-run budgets are the last recorded partial snapshot, not finalized totals.

| Run | Source | Completion | Producer state / build s / wall s / admission s / deadline min | Recorded execution / wall / admission-only s |
|---|---|---|---|---|
| 20261030-161544-a4d6 | d5bda599b1edc88e52fa5d43e4c6114747417422 | completed report | failed / 216.0698921 / 227.7731054 / 0 / 24 | 230.1357954 / 230.1357986 / 0 |
| 20261030-162011-5604 | bbd0a546ec10e4722a4ee386d673d744a7a274e1 | stopped; incomplete | ok / 445.0428271 / 753.6895676 / 285 / 24 | 696.0182702 / 1266.4514813 / 570.4332081 |
| 20261030-164314-bf74 | d36e79f432307ff26ab687d06bad5e9a5f92cff1 | stopped; incomplete | ok / 262.9238403 / 346.3431858 / 60 / 24 | 2294.1213582 / 2969.4890847 / 675.3677097 |
| 20261030-173840-e5d5 | 192f642b2b0a5d23f9f9e33112adb8c43efe579d | completed report | ok / 387.0311222 / 422.7281725 / 0 / 24 | 1802.156965 / 2357.4358235 / 555.2788429 |

Latest recorded observation per required S2/S3 row; the source SHA is part of each result. Stopped or budget-limited runs are incomplete gates. Row state skipped means not run because the execution budget ended; it is distinct from a TUnit platform skip. Interrupted rows without TRX retain unknown counts. Counts below never rebind historical evidence to a newer source. Detailed database/container/readiness/migration/schema/IO/HTTP/CLI/drain subphases were unavailable and remain unknown. Execution remainder is execution minus host wall, where both were recorded; per-test p95 is the mixed-case nearest-rank duration quantile, not a repeated-sample p95 for each individual test. Each exact slow case has one current observation (provisional; CARD-1404/1514/1531).

| Row | Source | State | Executed / passed / failed / skipped | Wall s | Admission s | Execution s | Startup / body wall / teardown s | Host s | Execution remainder s | Case p95 / max s (n) | Deadline min |
|---|---|---|---|---:|---:|---:|---|---:|---:|---|---:|
| CP-17 | 192f642b2 | green | 22 / 22 / 0 / 0 | 133.003 | 45.000 | 87.703 | 31.668 / 49.089 / 6.108 | 86.865 | 0.838 | 2.702 / 18.531 (22) | 10 |
| CP-28 | 192f642b2 | green | 382 / 382 / 0 / 0 | 95.556 | 0.000 | 95.470 | 25.840 / 65.141 / 3.638 | 94.620 | 0.849 | 1.942 / 10.851 (382) | 10 |
| CP-52 | bbd0a546e | roster-miss | 247 / 247 / 0 / 2 | 112.128 | 105.000 | 7.023 | 4.877 / 0.484 / 0.752 | 6.113 | 0.910 | 0.200 / 0.403 (247) | 8 |
| CP-16 | 192f642b2 | green | 44 / 44 / 0 / 0 | 176.705 | 75.000 | 101.613 | 35.732 / 32.454 / 4.117 | 72.302 | 29.310 | 1.143 / 10.643 (44) | 6 |
| CP-15 | d36e79f43 | green | 12 / 12 / 0 / 0 | 6.343 | 0.000 | 6.299 | 4.679 / 0.032 / 0.774 | 5.485 | 0.814 | 0.032 / 0.032 (12) | 10 |
| CP-19 | d36e79f43 | green | 22 / 22 / 0 / 0 | 41.439 | 0.000 | 41.330 | 20.271 / 16.570 / 3.446 | 40.286 | 1.043 | 0.913 / 8.294 (22) | 4 |
| CP-21 | d36e79f43 | green | 48 / 48 / 0 / 0 | 78.574 | 0.000 | 78.506 | 24.869 / 49.073 / 3.651 | 77.593 | 0.913 | 2.111 / 13.114 (48) | 8 |
| CP-22 | 192f642b2 | green | 36 / 36 / 0 / 0 | 187.876 | 150.000 | 37.739 | 18.714 / 15.200 / 3.053 | 36.968 | 0.771 | 0.860 / 6.693 (36) | 8 |
| CP-23 | 192f642b2 | green | 167 / 167 / 0 / 0 | 116.204 | 45.000 | 71.098 | 20.347 / 46.490 / 3.670 | 70.506 | 0.592 | 21.136 / 25.080 (167) | 24 |
| CP-24 | d36e79f43 | green | 10 / 10 / 0 / 0 | 59.981 | 15.000 | 44.745 | 33.777 / 4.165 / 5.100 | 43.043 | 1.703 | 4.165 / 4.165 (10) | 4 |
| CP-25 | d36e79f43 | green | 39 / 39 / 0 / 0 | 98.299 | 45.000 | 52.892 | 31.509 / 12.687 / 4.830 | 49.026 | 3.866 | 12.657 / 12.686 (39) | 6 |
| CP-26 | 192f642b2 | green | 59 / 59 / 0 / 0 | 92.517 | 15.000 | 77.423 | 58.147 / 14.351 / 4.378 | 76.877 | 0.546 | 5.015 / 5.033 (59) | 8 |
| CP-27 | d36e79f43 | green | 341 / 341 / 0 / 0 | 257.181 | 0.000 | 256.686 | 39.361 / 193.366 / 4.104 | 236.831 | 19.855 | 1.393 / 8.871 (341) | 10 |
| CP-29 | d36e79f43 | green | 17 / 17 / 0 / 0 | 62.850 | 15.000 | 47.692 | 21.674 / 18.690 / 6.293 | 46.657 | 1.035 | 18.690 / 18.690 (17) | 6 |
| CP-30 | d36e79f43 | green | 28 / 28 / 0 / 0 | 69.401 | 0.000 | 69.284 | 31.276 / 32.199 / 2.477 | 65.952 | 3.332 | 11.390 / 12.728 (28) | 6 |
| CP-31 | d36e79f43 | green | 35 / 35 / 0 / 0 | 197.813 | 0.000 | 197.760 | 32.083 / 159.748 / 4.479 | 196.309 | 1.451 | 23.812 / 27.195 (35) | 6 |
| CP-32 | d36e79f43 | green | 12 / 12 / 0 / 0 | 98.834 | 0.000 | 98.146 | 32.323 / 39.746 / 2.786 | 74.855 | 23.291 | 19.593 / 19.593 (12) | 4 |
| CP-33 | d36e79f43 | green | 4 / 4 / 0 / 0 | 49.315 | 0.000 | 49.243 | 27.354 / 17.152 / 3.422 | 47.928 | 1.315 | 10.791 / 10.791 (4) | 2 |
| CP-34 | d36e79f43 | green | 8 / 8 / 0 / 0 | 45.088 | 0.000 | 44.978 | 20.319 / 16.427 / 4.431 | 41.177 | 3.801 | 4.978 / 4.978 (8) | 4 |
| CP-35 | d36e79f43 | green | 25 / 25 / 0 / 0 | 61.353 | 30.000 | 31.237 | 19.580 / 4.769 / 3.989 | 28.339 | 2.898 | 4.512 / 4.769 (25) | 6 |
| CP-36 | d36e79f43 | green | 69 / 69 / 0 / 0 | 41.088 | 0.000 | 41.007 | 21.697 / 16.541 / 1.984 | 40.222 | 0.785 | 3.818 / 3.858 (69) | 8 |
| CP-37 | d36e79f43 | green | 1 / 1 / 0 / 0 | 36.225 | 0.000 | 36.110 | 26.412 / 4.928 / 3.148 | 34.488 | 1.623 | 4.928 / 4.928 (1) | 2 |
| CP-38 | d36e79f43 | green | 13 / 13 / 0 / 0 | 53.888 | 0.000 | 53.806 | 33.097 / 14.268 / 4.327 | 51.692 | 2.114 | 10.063 / 10.063 (13) | 4 |
| CP-39 | d36e79f43 | green | 1 / 1 / 0 / 0 | 46.626 | 0.000 | 45.987 | 23.822 / 17.549 / 3.390 | 44.761 | 1.226 | 17.549 / 17.549 (1) | 4 |
| CP-40 | d36e79f43 | green | 40 / 40 / 0 / 0 | 142.716 | 75.000 | 67.241 | 22.251 / 42.160 / 1.878 | 66.290 | 0.951 | 1.830 / 20.397 (40) | 10 |
| CP-41 | d36e79f43 | green | 7 / 7 / 0 / 0 | 33.811 | 0.000 | 33.718 | 22.076 / 9.277 / 1.379 | 32.732 | 0.986 | 5.451 / 5.451 (7) | 4 |
| CP-42 | d36e79f43 | green | 12 / 12 / 0 / 0 | 102.383 | 0.000 | 102.315 | 20.407 / 78.018 / 2.191 | 100.617 | 1.698 | 8.665 / 8.665 (12) | 6 |
| CP-43 | d36e79f43 | green | 10 / 10 / 0 / 0 | 43.757 | 0.000 | 43.681 | 32.750 / 5.806 / 3.775 | 42.332 | 1.350 | 5.806 / 5.806 (10) | 4 |
| CP-44 | d36e79f43 | green | 1 / 1 / 0 / 0 | 88.540 | 30.000 | 58.310 | 5.764 / 44.277 / 5.411 | 55.452 | 2.857 | 44.277 / 44.277 (1) | 2 |
| CP-45 | d36e79f43 | green | 46 / 46 / 0 / 0 | 122.142 | 105.000 | 16.968 | 7.346 / 6.968 / 0.927 | 15.241 | 1.727 | 0.532 / 6.967 (46) | 4 |
| CP-46 | d36e79f43 | roster-miss | 215 / 215 / 0 / 1 | 98.876 | 75.000 | 23.385 | 11.574 / 8.961 / 0.601 | 21.136 | 2.249 | 2.152 / 7.356 (215) | 6 |
| CP-47 | d36e79f43 | green | 29 / 29 / 0 / 0 | 101.136 | 90.000 | 10.991 | 4.708 / 3.939 / 0.866 | 9.513 | 1.479 | 3.824 / 3.937 (29) | 4 |
| CP-48 | d36e79f43 | green | 22 / 22 / 0 / 0 | 28.356 | 15.000 | 13.165 | 5.081 / 5.304 / 1.026 | 11.411 | 1.754 | 5.131 / 5.303 (22) | 4 |
| CP-49 | 192f642b2 | green | 107 / 107 / 0 / 0 | 158.013 | 150.000 | 7.660 | 3.266 / 3.118 / 0.505 | 6.889 | 0.771 | 1.639 / 3.118 (107) | 4 |
| CP-50 | 192f642b2 | green | 158 / 158 / 0 / 0 | 7.955 | 0.000 | 7.885 | 4.260 / 2.507 / 0.473 | 7.240 | 0.645 | 1.938 / 2.275 (158) | 4 |
| CP-51 | 192f642b2 | green | 89 / 89 / 0 / 0 | 4.616 | 0.000 | 4.558 | 3.232 / 0.080 / 0.509 | 3.821 | 0.737 | 0.037 / 0.052 (89) | 4 |
| CP-53 | 192f642b2 | green | 173 / 173 / 0 / 0 | 7.296 | 0.000 | 7.221 | 5.562 / 0.319 / 0.726 | 6.606 | 0.615 | 0.071 / 0.169 (173) | 6 |
| CP-54 | 192f642b2 | roster-miss | 249 / 249 / 0 / 1 | 38.762 | 0.000 | 38.673 | 21.961 / 10.656 / 5.216 | 37.833 | 0.840 | 0.127 / 4.434 (249) | 8 |
| CP-55 | 192f642b2 | roster-miss | 244 / 244 / 0 / 1 | 7.654 | 0.000 | 7.575 | 5.701 / 0.255 / 0.654 | 6.610 | 0.966 | 0.116 / 0.142 (244) | 8 |
| CP-56 | 192f642b2 | roster-miss | 224 / 224 / 0 / 17 | 6.640 | 0.000 | 6.567 | 4.739 / 0.522 / 0.546 | 5.807 | 0.760 | 0.238 / 0.303 (224) | 8 |
| CP-57 | 192f642b2 | green | 214 / 214 / 0 / 0 | 12.309 | 0.000 | 12.218 | 5.163 / 5.022 / 1.274 | 11.460 | 0.759 | 0.176 / 5.022 (214) | 6 |
| CP-58 | 192f642b2 | green | 184 / 184 / 0 / 0 | 6.997 | 0.000 | 6.899 | 4.878 / 0.406 / 0.792 | 6.075 | 0.824 | 0.111 / 0.257 (184) | 6 |
| CP-59 | 192f642b2 | green | 184 / 184 / 0 / 0 | 9.602 | 0.000 | 9.501 | 6.012 / 1.385 / 1.216 | 8.613 | 0.888 | 0.136 / 1.319 (184) | 6 |
| CP-60 | 192f642b2 | green | 35 / 35 / 0 / 0 | 10.111 | 0.000 | 9.986 | 7.247 / 0.756 / 0.846 | 8.848 | 1.138 | 0.406 / 0.449 (35) | 4 |
| CP-61 | 192f642b2 | green | 247 / 247 / 0 / 0 | 13.453 | 0.000 | 13.269 | 8.192 / 2.711 / 1.084 | 11.987 | 1.282 | 0.889 / 2.029 (247) | 8 |
| CP-62 | 192f642b2 | roster-miss | 238 / 238 / 0 / 4 | 13.936 | 0.000 | 13.796 | 10.291 / 0.601 / 1.416 | 12.307 | 1.489 | 0.290 / 0.360 (238) | 8 |
| CP-63 | 192f642b2 | green | 241 / 241 / 0 / 0 | 14.949 | 0.000 | 14.816 | 10.537 / 2.679 / 0.610 | 13.826 | 0.990 | 0.386 / 2.210 (241) | 8 |
| CP-64 | 192f642b2 | roster-miss | 239 / 239 / 0 / 2 | 30.474 | 0.000 | 30.346 | 23.438 / 2.226 / 3.703 | 29.367 | 0.979 | 0.128 / 2.035 (239) | 8 |
| CP-65 | 192f642b2 | green | 232 / 232 / 0 / 0 | 23.396 | 0.000 | 23.334 | 7.618 / 13.518 / 1.284 | 22.420 | 0.914 | 0.348 / 12.965 (232) | 6 |
| CP-66 | 192f642b2 | green | 89 / 89 / 0 / 0 | 13.781 | 0.000 | 13.386 | 8.369 / 2.627 / 0.817 | 11.813 | 1.573 | 1.726 / 1.734 (89) | 4 |
| CP-67 | 192f642b2 | timeout | unknown / unknown / unknown / unknown | 364.140 | 0.000 | 363.987 | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 6 |
| CP-68 | unknown | execution-budget-exceeded | unknown / unknown / unknown / unknown | 253.252 | 0.000 | 252.868 | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 6 |
| CP-69 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 6 |
| CP-70 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-71 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-72 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 8 |
| CP-73 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 8 |
| CP-74 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 8 |
| CP-75 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-76 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-77 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-78 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-79 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 24 |
| CP-80 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 8 |
| CP-81 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 8 |
| CP-82 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-83 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-84 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 2 |
| CP-85 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 4 |
| CP-86 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 6 |
| CP-87 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 2 |
| CP-88 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 2 |
| CP-89 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 10 |
| CP-90 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 6 |
| CP-91 | unknown | skipped | 0 / 0 / 0 / 0 | 0.000 | 0.000 | unknown | unknown / unknown / unknown | unknown | unknown | unknown / unknown (0) | 2 |
| CP-92 | 192f642b2 | green | 6 / 6 / 0 / 0 | 125.676 | 75.000 | 50.552 | 34.143 / 7.003 / 7.740 | 48.887 | 1.666 | 5.106 / 5.106 (6) | 4 |

Every row deadline exceeds twice its observed case-duration p95 in this campaign; missing/one-sample histories remain provisional. Row-wall estimates and admission are separate. Build cost is charged once to its actual producer, never to every reused row.

## Original 45 failure triage

| Historical failing case | Attribution / cause | Fix | Latest CP-28 result |
|---|---|---|---|
| C1153_Real_client_certificate_holds_original_input(local-http) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Watchdog_first_holds_absent_launch | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1161_Dead_session_grace_is_monotonic(rollback-hold, hold, 300, 60, 2, Blocked) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_C1150_Statement_budgets(due-absent-hold, 19) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1161_Dead_session_grace_is_monotonic(aligned-hold, hold, 300, 300, 2, Blocked) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(wrong-generation) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(wrong-session) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(sidecar-entry) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(incomplete) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Caller_note_has_one_complete_user_prompt(busy) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Caller_note_has_one_complete_user_prompt(crash) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(null) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Caller_note_has_one_complete_user_prompt(eligible) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Failed_hold_does_not_persist_on_a_later_save | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Unknown_native_evidence_is_not_unattempted(unreadable) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(crash) | Reproduced on S2 tip; missing accepted/completed runner observation and transcript state in fixture. Not proved pre-existing on master. | d36e79f432307ff26ab687d06bad5e9a5f92cff1 (plus precursor fixture/import commits); all original protective assertions retained. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(busy) | Reproduced on S2 tip; missing accepted/completed runner observation and transcript state in fixture. Not proved pre-existing on master. | d36e79f432307ff26ab687d06bad5e9a5f92cff1 (plus precursor fixture/import commits); all original protective assertions retained. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1151_Ceiling_failure_note_has_one_complete_user_prompt(eligible) | Reproduced on S2 tip; missing accepted/completed runner observation and transcript state in fixture. Not proved pre-existing on master. | d36e79f432307ff26ab687d06bad5e9a5f92cff1 (plus precursor fixture/import commits); all original protective assertions retained. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(ordinary-expiry) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(fresh) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(exact-five-seconds) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(wall-rollback-fresh) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(forward-jump-fresh) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(negative-age) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(review-rollback) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_age_is_monotonic(rollback-before-request) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(empty-transcript) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(incomplete) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(mismatched-generation) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(stale-nonce) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(timeout) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Final_certificate_is_revalidated_under_lock(expired-proof) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Final_certificate_is_revalidated_under_lock(store-or-epoch-change) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(evidence-unreachable) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(old-runner) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_bad_evidence_keeps_failure(plain404) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Real_client_certificate_holds_original_input(remote-phone-home) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Changed_or_working_attempt_is_untouched(attempt-rebind) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Final_certificate_is_revalidated_under_lock(changed-generation) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1591_Withheld_brief_still_holds_absent_launch | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Due_hold_statement_ceiling(parent, 48) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1149_Absent_launch_is_blocked_with_original_input | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Certificate_failure_does_not_leak_a_hold_on_later_save | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |
| C1153_Due_hold_statement_ceiling(no-parent, 40) | Introduced S3 strict EF-column census rejected otherwise pristine original briefs. | 5039ccb73e5482ef33c89258db00beca36a8afe9; new purpose column classified and required null for pristine brief. | Passed at 192f642b2b0a5d23f9f9e33112adb8c43efe579d |

## Platform skips and required placement

These are coverage gaps, not product assertion failures. No native execution was authorized here and no floor was lowered. Commission exact native methods (all parameter cases) and amend placement without dropping coverage; retain an incomplete gate until source-qualified evidence is accepted.

| Row | Exact method | Case count | Skip reason |
|---|---|---:|---|
| CP-52 | Antiphon.Tests.Application.AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash | 1 | Skipped: Canonical pin cwd is a Windows drive-rooted path |
| CP-52 | Antiphon.Tests.Application.AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone | 1 | Skipped: Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows |
| CP-46 | Antiphon.Tests.Agents.AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine | 1 | Skipped: The npm shim layout is Windows-specific |
| CP-54 | Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard | 1 | Skipped: CommandLineToArgvW is Windows-only |
| CP-55 | Antiphon.Tests.Application.DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms | 1 | Skipped: Drive-letter report paths resolve only against a Windows repo root |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.prefix_returns_matching_child_directories | 1 | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.trailing_slash_lists_children_of_that_directory | 1 | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.caches_within_ttl_and_refreshes_after | 1 | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.existing_path_reports_exists_true | 1 | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.partial_leaf_matches_substring_within_child_name | 1 | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls | 12 | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing | 1 | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_modern_backend_confirms_the_raised_ceilings | 1 | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.Unknown_runner_backend_downgrades_modern_server | 1 | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_inbox_conhost_downgrades_a_modern_server | 1 | Skipped: Windows delivery profile proof |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind | 1 | Skipped: ConPTY only on Windows |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence | 1 | Skipped: ConPTY only on Windows |

## Exact remaining rows

| Row | State | Original floor | Exact filter |
|---|---|---:|---|
| CP-52 | roster-miss | 249 | /*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)\|(AgentContextContractTests*)\|(AgentExecutableResolverTests*)\|(AgentPinPathTests*)\|(AgentPresetsTests*)\|(AgentSessionRuntimeBoundedReadTests*)\|(AgentTaskDecisionQuestionTests*)\|(AgentTaskDispatcherPredicateTests*)\|(AgentTaskLandingStateTests*)\|(AgentTaskLandQueueVisibilityTests*)\|(AgentTaskLandVerificationEvidenceTests*)\|(AgentTaskLivenessTests*)\|(AlarmSettingsValidatorTests*)/* |
| CP-46 | roster-miss | 216 | /*/Antiphon.Tests.Agents/(AgentLaunchSpecTests*)\|(AgentProtocolAdapterFactoryTests*)\|(AgentRegistrySettingsTests*)\|(AgentRegistryTests*)\|(AgentSessionSettingsTests*)\|(CodexAdapterRawResponseTests*)\|(CodexResponseAnalyzerTests*)\|(CodexWorkingIndicatorTests*)\|(FakeLlmApiSelfTests*)\|(GrokAdapterTests*)\|(GrokCredentialStoreTests*)\|(GrokFreshWorktreeQualificationTests*)\|(GrokLinuxBlockingPromptTests*)\|(GrokLinuxStartupReadinessTests*)\|(GrokModelListParserTests*)\|(GrokSignInPromptDetectorTests*)\|(GrokStartupCaptureStoreTests*)\|(GrokStartupReadinessTests*)\|(GrokStartupSizeTests*)\|(GrokTrustPromptDetectorTests*)\|(HerdrEnvelopeBodyTests*)\|(OpenCodeAdapterTests*)\|(RunnerClaudeAdapterEffortPromptTests*)\|(RunnerClaudeAdapterTrustPromptTests*)\|(RunnerClaudeAdapterVerifiedPromptTests*)/* |
| CP-54 | roster-miss | 250 | /*/Antiphon.Tests.Application/(ChannelOutboundContractTests*)\|(ChannelOutboundEvidenceAccountingTests*)\|(ChannelOutboundStorageTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelReplyAttachmentReaderTests*)\|(CheckCompactionContinuationSettingsTests*)\|(CheckInterpretationValidatorTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckScheduleBackoffTests*)\|(CheckScheduleElapsedTimesTests*)\|(CheckScheduleRoundingTests*)\|(CheckSpecialistLaunchPolicyTests*)\|(ClaudeEffortByRoleSettingsTests*)\|(ClaudeLaunchArgsTests*)\|(ClaudeRemoteControlLaunchArgsTests*)\|(CodexLaunchArgsTests*)\|(CodexPhoneHomeProjectionTests*)\|(ColumnTextTests*)\|(CommitOnSettleDocumentationTests*)\|(CommitOnSettlePolicyResolverTests*)\|(CommitRecoveryObligationsTests*)\|(CompactionContinuationPolicyTests*)\|(CompletionHeaderOverlapTests*)/* |
| CP-55 | roster-miss | 245 | /*/Antiphon.Tests.Application/(CompletionNoteWorkQueueTests*)\|(ComplexityEffectiveComposeTests*)\|(ComplexityRoutingComposeTests*)\|(ContextCompactionSettingsTests*)\|(D2AdvisoryDocumentationTests*)\|(DelegateBindRefusalRecoveryTests*)\|(DelegateBranchNameTests*)\|(DelegationAllowedRootsFileTests*)\|(DelegationCapabilityContractTests*)\|(DelegationCostTests*)\|(DelegationDenyHookPolicyTests*)\|(DelegationKindPricingTests*)\|(DelegationLeaseSettingsTests*)\|(DelegationPoolDefaultsTests*)\|(DelegationQuestionDetectionTests*)\|(DelegationReportFormatterTests*)\|(DelegationScopeLeaseTests*)\|(DelegationWorkspaceBoundaryTests*)\|(DeliveryBackendCeilingsTests*)/* |
| CP-56 | roster-miss | 241 | /*/Antiphon.Tests.Application/(DesktopCapabilitiesSnapshotTests*)\|(DiagnosisTests*)\|(DiagnosticsRedactorTests*)\|(DialogDeliveryAdmissionTests*)\|(DirectoryBrowseServiceTests*)\|(DirectoryMatcherTests*)\|(DispatchBaseWarningRosterTests*)\|(DispatchConcurrencyPolicyTests*)\|(DispatchHoldLedgerTests*)\|(ExpectationDirectiveTests*)\|(ExpectationPipelineTests*)\|(GitIndexLockTests*)\|(GrokComposerScreenTests*)\|(GrokDeliveryShapeTests*)\|(GrokLaunchArgsTests*)\|(GrokNativeSessionResumeTests*)\|(GrokQuestionPopupTests*)\|(GrokRulesLiveEvidenceTests*)\|(GrokRulesTransportCompatibilityTests*)\|(HerdrAgentKindMapTests*)\|(HerdrLaunchContextTitleTests*)\|(HerdrStatusDisagreementTests*)/* |
| CP-62 | roster-miss | 242 | /*/Antiphon.Tests.Application/(PolicyRefreshDeltaTests*)\|(PostLandMutationContractTests*)\|(PostLandMutationPublicationTests*)\|(PostLandMutationReceiptPolicyTests*)\|(PostLandRetrospectiveContractTests*)\|(ProviderCapacityNoticeTests*)\|(ProviderContractCatalogTests*)\|(ProviderEndpointDocumentationTests*)\|(ProviderQuotaRefusalTests*)\|(PtyDeliveryCeilingsTests*)\|(PtyInlineCeilingTests*)\|(PublishedRoutingDocumentationTests*)\|(ReceiptScanDestinationTests*)\|(ReleaseBranchModelDocumentationTests*)\|(RemoteControlMenuScreenTests*)\|(RemoteControlPolicyTests*)\|(RemoteVerificationWorkspaceTests*)/* |
| CP-64 | roster-miss | 241 | /*/Antiphon.Tests.Application/(SessionContextUsageTests*)\|(SessionDeliveryProfileTests*)\|(SessionReAdoptionStateTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerSettingsTests*)\|(SettlementSyncDebtAttentionTests*)\|(SettlementSyncDebtPolicyTests*)\|(SharedWriterLeaseProjectionTests*)\|(SiblingInspectionResultsTests*)\|(SiblingWarningReducerTests*)\|(SlashCommandCatalogServiceTests*)\|(SlashCommandParserTests*)\|(SourceLandingAdmissionTests*)\|(SpecialistAttemptEvidenceTests*)\|(SpecialistFailurePolicyTests*)\|(SpecialistQualificationContractTests*)\|(SpecialistRoleContractTests*)\|(StandingBootDocumentationTests*)\|(StandingBootWatchPolicyTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(StandingSpecialistHealthPolicyTests*)\|(SubagentNotificationTests*)\|(SubscriptionQuotaGateTests*)\|(SubscriptionUsageParserTests*)/* |
| CP-67 | timeout | 216 | /*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)\|(BuildSlotClientTests*)\|(C1307CliCancellationTests*)\|(CheckpointAdmissionBudgetTests*)\|(CheckpointAdmissionContractTests*)\|(CheckpointAppTests*)\|(CheckpointBoundedReaderTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointConfinementLedgerTests*)\|(CheckpointEofStallTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointFixtureProcessTests*)\|(CheckpointFreshOutputSealTests*)\|(CheckpointImportTests*)\|(CheckpointLargeStampScriptTests*)\|(CheckpointLargeStampTests*)\|(CheckpointLaunchCleanupTests*)\|(CheckpointLineTests*)\|(CheckpointManifestTests*)\|(CheckpointNamespaceCensusUsageTests*)\|(CheckpointOrFilterTests*)\|(CheckpointOutputConfinementTests*)\|(CheckpointOwnedOutputTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointReceiptSafetyTests*)/* |
| CP-68 | execution-budget-exceeded | 215 | /*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)\|(CheckpointRepeatConfigurationTests*)\|(CheckpointRepeatDocumentationTests*)\|(CheckpointRepeatHostTests*)\|(CheckpointRunOwnershipTests*)\|(CheckpointScriptReaderDeadlineTests*)\|(CheckpointShapeConflictTests*)\|(CheckpointSlotContractTests*)\|(CheckpointStampSafetyTests*)\|(CheckpointTaskOwnershipTests*)\|(CheckpointTempRootSweepTests*)\|(CheckpointTempScopeTests*)\|(CheckpointTimingHarnessTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointToolSourceTests*)\|(CheckpointTrackedBaselineTests*)\|(CheckpointWindowsReadModeTests*)\|(DetachedLauncherTests*)\|(EvidenceFolderTests*)\|(ExitCodeTests*)/* |
| CP-69 | skipped | 196 | /*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)\|(PlanCoverageAssertionTests*)\|(PlanCoverageBindingTests*)\|(PlanCoverageCensusTests*)\|(PlanCoverageCommandTests*)\|(PlanCoverageGoldenTests*)\|(PlanCoverageHandleTests*)\|(PlanCoverageParserTests*)\|(PlanCoveragePcTests*)\|(ProcessDriverTests*)\|(ReapedProcStatIdentityTests*)\|(ReportMergerTests*)\|(ReportWriterTests*)\|(RerunFilterHostTests*)\|(RerunPolicyTests*)\|(RowRunnerTests*)\|(RunSchedulerTests*)\|(RunStateStoreTests*)\|(SafeDeleteProcessBoundaryTests*)\|(ShadowCopyTests*)\|(TestPhaseCustodyTests*)\|(TestSetTimingTests*)\|(TimeoutTests*)\|(TimingJournalTests*)\|(TrxReportTests*)/* |
| CP-70 | skipped | 12 | /*/Antiphon.Tests.Checkpoints/WaitCommandTests/* |
| CP-71 | skipped | 61 | /*/Antiphon.Tests.Domain.StateMachine/(CardStateMachineTests*)\|(RunAttemptStateMachineTests*)\|(WorkflowStateMachineTests*)/* |
| CP-72 | skipped | 246 | /*/Antiphon.Tests.Infrastructure/(AppDbContextModelTests*)\|(AppHostBrokerSourceGuardTests*)\|(C1307CancellationTests*)\|(C1307ToolRemovalTests*)\|(CheckpointToolProjectContractTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(CustodyHelperContractTests*)\|(DindRunnerContractTests*)\|(DirectoryBuildRspTests*)\|(DockerDeliveryBarrierTests*)\|(DockerDeliveryObservationTests*)\|(DockerStackContractTests*)/* |
| CP-73 | skipped | 247 | /*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)\|(DotnetToolManifestContractTests*)\|(E2ECopyOwnedPtyHostAppHostContractTests*)\|(EvidencePolicyWorkflowTests*)\|(FileSystemEvidenceRetentionTests*)\|(FileUseWindowsTests*)\|(GithubCredentialHelperTests*)\|(GitMinimumVersionGuardTests*)\|(GrokRunnerImageContractTests*)\|(GrokSha256PinTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(HostCleanupCheckpointCompatibilityTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointExtractionTests*)\|(HostCleanupCheckpointPathTests*)\|(HostCleanupCheckpointReadDeadlineTests*)\|(HostCleanupCheckpointReceiptTests*)\|(HostCleanupCheckpointSweepRaceTests*)\|(HostCleanupExecutionTests*)\|(HostCleanupOwnershipTests*)\|(HostCleanupPolicyTests*)\|(JqRunnerImageContractTests*)/* |
| CP-74 | skipped | 250 | /*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)\|(LandingIdentityControlTests*)\|(LandingRemovalPolicyControlTests*)\|(LogRetentionTests*)\|(OperatorDashboardSessionsTests*)\|(OperatorTokenFileTests*)\|(OrchestratorInstructionsMigrationShapeTests*)\|(PackageVersionPinContractTests*)\|(PostgresPoolConfigurationTests*)\|(RunnerPushCredentialDocsTests*)\|(SessionRunnerGrokLineEndingContractTests*)\|(ShellFixtureLineEndingContractTests*)\|(WorktreeDeleteAccessProbeTests*)\|(WorktreeLockDiagnosticsTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerTests*)/* |
| CP-75 | skipped | 5 | /*/Antiphon.Tests.Infrastructure/WorktreeRemovalDefaultTests/* |
| CP-76 | skipped | 46 | /*/Antiphon.Tests.Infrastructure.Resilience/(HttpReadResilienceTests*)\|(HttpResilienceRegistrationTests*)\|(ResilienceBudgetTests*)\|(ResilienceCircuitTests*)\|(ResilienceOptionsAndTelemetryTests*)\|(ResiliencePredicateTests*)/* |
| CP-77 | skipped | 4 | /*/Antiphon.Tests.Migrations/(CommitOnSettleMigrationTests*)\|(PipelineDefinitionMigrationShapeTests*)/* |
| CP-78 | skipped | 67 | /*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)\|(C1364BoundLandWaitProofTests*)\|(DelegateScriptInterruptSwitchTests*)\|(DelegateScriptRunnerSwitchTests*)\|(HostJqPrerequisiteScriptTests*)\|(ProcessSpawnTimeoutPolicyTests*)\|(QualificationCheckoutSourceTests*)\|(RefreshGithubTokenRemoteCommandTests*)\|(RefreshGithubTokenScriptTests*)/* |
| CP-79 | skipped | 499 | /*/Antiphon.Tests.Scripts/RemoteScriptContractTests/* |
| CP-80 | skipped | 168 | /*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)\|(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)\|(ScriptHarnessProcessContractTests*)\|(SeedSmokeTargetingPackContractTests*)\|(SelectorLineEndingAttributeTests*)\|(TestTimingReportTests*)\|(VerifyDevStackSmokeTests*)\|(WindowsUnitPlatformAdmissionTests*)/* |
| CP-81 | skipped | 250 | /*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)\|(CheckCompactionFixtureTests*)\|(ControlledLandingGitTests*)\|(DelegationHarnessCensusTests*)\|(DelegationTestServicesTests*)\|(DirectoryLinkFixtureTests*)\|(EntityScalarSnapshotConsumerGuardTests*)\|(EntityScalarSnapshotTests*)\|(LiveFixtureTextTests*)\|(LoadTimeoutBoundWitnessTests*)\|(LoadTimeoutWaitTests*)\|(PairedOverheadStatisticsTests*)\|(ScaledTimeProviderTests*)\|(SlowTestTripwireTests*)\|(TestAppHostPathTests*)\|(TestClassificationPolicyTests*)\|(TestDbFixtureLifecycleTests*)\|(TestDbFixtureSchemaCacheLifecycleTests*)\|(TestLaneCategoryGuardTests*)\|(TestPhaseSummaryTests*)/* |
| CP-82 | skipped | 37 | /*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)\|(TestSchemaCacheTests*)\|(TestTimingLifecycleTests*)\|(TestTimingRecorderTests*)\|(WindowsDeliveryFixtureWitnessTests*)/* |
| CP-83 | skipped | 1 | /*/Antiphon.TestSupport/TestClassificationGuardTests/* |
| CP-84 | skipped | 8 | /*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled |
| CP-85 | skipped | 2 | /*/*/CheckCompactionContinuationTests/* |
| CP-86 | skipped | 9 | /*/*/CheckCompactionCrashTests/* |
| CP-87 | skipped | 1 | /*/*/CheckCompactionAttentionTests/* |
| CP-88 | skipped | 2 | /*/*/CheckCompactionWorkerLifetimeTests/* |
| CP-89 | skipped | 27 | /*/*/CheckCompactionReceiptTests/* |
| CP-90 | skipped | 11 | /*/*/CheckCompactionAutomaticRestartTests/* |
| CP-91 | skipped | 2 | /*/*/CheckCompactionRecoveryPersistenceTests/* |

## Slow-test evidence

The registry was unchanged during this task. The table gives the slowest 25 exact case observations; n=1 per case, provisional p95 equals the observation, and the 2x column is a recommendation for later measured-budget refinement, not a timer change. No test timeout, retry, assertion or allowlist was relaxed. All other >=5s cases remain in ignored slow-case evidence.

| Row | Case | Duration / provisional p95 s | 2x s |
|---|---|---:|---:|
| CP-44 | the_real_container_gives_the_dispatcher_a_live_transcript_pull | 44.277 | 88.555 |
| CP-31 | C1259_review_recovery_uses_authorized_queue_identity_once(enqueue-failure) | 27.195 | 54.390 |
| CP-23 | a_live_session_no_agent_names_is_unowned(False) | 25.080 | 50.160 |
| CP-31 | C1259_compaction_refinement_and_status_cannot_block_ingestion(manual-compaction) | 23.812 | 47.625 |
| CP-23 | a_task_dispatched_under_stall_minutes_does_not_run_the_workspace_probe | 22.246 | 44.493 |
| CP-23 | A_kind_43_incident_on_a_dead_session_is_not_queued_input_stuck | 22.246 | 44.492 |
| CP-23 | summary_counts_match_a_full_sweep_for_the_same_rows | 22.221 | 44.442 |
| CP-23 | a_task_past_stall_minutes_runs_the_workspace_probe | 22.111 | 44.221 |
| CP-23 | a_misfire_is_a_warning_row_until_the_next_good_fire | 21.785 | 43.570 |
| CP-23 | ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle | 21.755 | 43.511 |
| CP-23 | a_task_holding_a_commit_recovery_obligation_older_than_two_minutes_is_an_error_row_with_the_recipe | 21.596 | 43.192 |
| CP-23 | the_summary_never_runs_the_workspace_probe | 21.136 | 42.271 |
| CP-23 | An_open_kind_43_incident_on_a_live_session_is_queued_input_stuck | 21.027 | 42.054 |
| CP-23 | a_sole_agent_on_a_same_named_empty_board_idle_three_days_is_a_leftover | 21.002 | 42.004 |
| CP-23 | C1160_Legacy_boot_row_states_silence_and_no_delivery_verdict(with-user-prompt) | 21.002 | 42.004 |
| CP-23 | a_running_idle_agent_with_a_queued_task_is_not_outlived | 21.000 | 41.999 |
| CP-23 | A_closed_enqueue_episode_is_not_queued_input_stuck | 20.998 | 41.996 |
| CP-23 | a_running_agent_mid_turn_is_not_outlived | 20.895 | 41.790 |
| CP-23 | A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed | 20.869 | 41.738 |
| CP-23 | a_running_agent_with_zero_transcript_rows_is_not_outlived | 20.869 | 41.737 |
| CP-23 | A_boot_reply_incident_on_a_dead_session_is_not_listed | 20.868 | 41.737 |
| CP-23 | a_leftover_shape_touched_one_day_ago_is_not_outlived | 20.868 | 41.737 |
| CP-23 | an_always_on_live_idle_agent_is_not_outlived | 20.866 | 41.732 |
| CP-23 | a_channel_bound_live_idle_agent_is_not_outlived | 20.864 | 41.728 |
| CP-23 | a_stopped_worktree_agent_untouched_for_three_days_is_a_leftover | 20.864 | 41.728 |

## Unedited latest CHECKPOINT provenance

```text
CHECKPOINT CP-17 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/TaskSupervisionReceiptTests/C1610_* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-17/run.trx slot=granted waited=45s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-28 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/* executed=382 passed=382 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=bbd0a546ec10e4722a4ee386d673d744a7a274e1 build=reused filter=/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)|(AgentContextContractTests*)|(AgentExecutableResolverTests*)|(AgentPinPathTests*)|(AgentPresetsTests*)|(AgentSessionRuntimeBoundedReadTests*)|(AgentTaskDecisionQuestionTests*)|(AgentTaskDispatcherPredicateTests*)|(AgentTaskLandingStateTests*)|(AgentTaskLandQueueVisibilityTests*)|(AgentTaskLandVerificationEvidenceTests*)|(AgentTaskLivenessTests*)|(AlarmSettingsValidatorTests*)/* executed=247 passed=247 failed=0 skipped=2 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-162011-5604/rows/CP-52/run.trx slot=granted waited=105s dirty=0 source=bbd0a546ec10e4722a4ee386d673d744a7a274e1 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/(TaskCeilingHoldTests*)|(TaskCeilingControlTests*)/C1610_* executed=44 passed=44 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-16/run.trx slot=granted waited=75s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/TaskCeilingPolicyTests/C1610_* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/TaskSupervisionVisibilityTests/C1610_* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskPoolTests/* executed=48 passed=48 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/AgentTaskOverdueDeadlineTests/* executed=36 passed=36 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-22/run.trx slot=granted waited=150s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/AttentionServiceTests/* executed=167 passed=167 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-23/run.trx slot=granted waited=45s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SeatOccupancyAttentionTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/TaskDeadlinePolicyTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-25/run.trx slot=granted waited=45s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=ok filter=/*/*/BootStallDetectionTests/* executed=59 passed=59 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-26/run.trx slot=granted waited=15s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskReplyIntegrationTests/* executed=341 passed=341 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SessionRuntimeActionRecoveryTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-29/run.trx slot=granted waited=15s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SessionRuntimeActionTests/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SessionRuntimeActionRoutingTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SessionRuntimeActionDeferralTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SessionRuntimeActionTemporaryBlockTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/DispatchConcurrencyAdmissionTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskConcurrencyLimitTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-35/run.trx slot=granted waited=30s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskPipelineStatusTests/* executed=69 passed=69 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/CheckCompactionRecoveryFlowTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/TaskProgressStallSweepTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/CompletionNoteDurableSweepTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/ApiErrorRecoveryServiceTests/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-40/run.trx slot=granted waited=75s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/SpecialistTaskRunnerDeadlineTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/PipelineHandoffPilotTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskDetailBlockedContextTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/*/AgentTaskDispatcherWiringTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-44/run.trx slot=granted waited=30s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/Antiphon.Tests/(CachingChatClientTests*)|(LandDeliveryWaitRoutingTests*)|(ProcessSpawnLimitTests*)|(ProgressAwareWaitTests*)/* executed=46 passed=46 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-45/run.trx slot=granted waited=105s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/Antiphon.Tests.Agents/(AgentLaunchSpecTests*)|(AgentProtocolAdapterFactoryTests*)|(AgentRegistrySettingsTests*)|(AgentRegistryTests*)|(AgentSessionSettingsTests*)|(CodexAdapterRawResponseTests*)|(CodexResponseAnalyzerTests*)|(CodexWorkingIndicatorTests*)|(FakeLlmApiSelfTests*)|(GrokAdapterTests*)|(GrokCredentialStoreTests*)|(GrokFreshWorktreeQualificationTests*)|(GrokLinuxBlockingPromptTests*)|(GrokLinuxStartupReadinessTests*)|(GrokModelListParserTests*)|(GrokSignInPromptDetectorTests*)|(GrokStartupCaptureStoreTests*)|(GrokStartupReadinessTests*)|(GrokStartupSizeTests*)|(GrokTrustPromptDetectorTests*)|(HerdrEnvelopeBodyTests*)|(OpenCodeAdapterTests*)|(RunnerClaudeAdapterEffortPromptTests*)|(RunnerClaudeAdapterTrustPromptTests*)|(RunnerClaudeAdapterVerifiedPromptTests*)/* executed=215 passed=215 failed=0 skipped=1 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-46/run.trx slot=granted waited=75s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/Antiphon.Tests.Agents/(RunnerCodexAdapterReadyTests*)|(RunnerCodexAdapterSubmitConfirmTests*)|(RunnerCodexAdapterTurnCompleteTests*)/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-47/run.trx slot=granted waited=90s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=d36e79f432307ff26ab687d06bad5e9a5f92cff1 build=reused filter=/*/Antiphon.Tests.Agents/RunnerGrokAdapterReadyTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-164314-bf74/rows/CP-48/run.trx slot=granted waited=15s dirty=0 source=d36e79f432307ff26ab687d06bad5e9a5f92cff1 sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Agents/(RunnerGrokAdapterSignInPromptTests*)|(RunnerGrokAdapterTrustPromptTests*)|(RunnerGrokAdapterTurnCompleteTests*)|(RunnerTerminalSessionGenerationTests*)|(RunnerTerminalSessionInputEncodingTests*)|(TestOwnedPtyHostIdentityTests*)|(TestOwnedPtyHostPolicyTests*)|(TranscriptNormalizerTests*)|(TranscriptTurnBaselineTests*)/* executed=107 passed=107 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-49/run.trx slot=granted waited=150s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.AgentTui/(AgentTuiOperationCoordinatorTests*)|(AgentTuiSecretProtectorTests*)|(AgentTuiSmokeScriptTests*)|(DollarEnvArgTests*)|(RunnerProbePowerShellFixtureTests*)|(RunnerProcessProbeRedactionTests*)/* executed=158 passed=158 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.ApiKeys/(AgentLaunchEnvTests*)|(ApiKeyPlaceholderTests*)|(ApiKeyProtectorTests*)|(ProviderEndpointProjectionTests*)/* executed=89 passed=89 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(ApiErrorClassifierTests*)|(ApiErrorRetryScheduleTests*)|(AreaMapContractTests*)|(AreaMapLoaderTests*)|(AttentionKindWireTests*)|(AttentionSettingsValidatorTests*)|(AwayDigestFormatterTests*)|(BlockedQuestionTests*)|(BlockedTaskParkProjectionTests*)|(BoardServiceNeedsHumanReviewTests*)|(BootStallDocumentationTests*)|(BootStallPolicyTests*)|(BootWedgeContractTests*)|(CapacityRecoveryPolicyTests*)|(CardAliasNormalizationTests*)|(CardFilePolicyTests*)|(CardFilePrivacyDocumentationTests*)|(CardIdentifierAllocatorTests*)|(CardRankingOrderTests*)|(CardRankingTests*)|(CardTaskFileRendererTests*)|(CardWorkflowRunFactoryTests*)|(ChannelContractsTests*)|(ChannelInboundDebouncerTests*)|(ChannelMachineTurnMatchTests*)/* executed=173 passed=173 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(ChannelOutboundContractTests*)|(ChannelOutboundEvidenceAccountingTests*)|(ChannelOutboundStorageTests*)|(ChannelPromptCorrelationUnitTests*)|(ChannelReplyAttachmentReaderTests*)|(CheckCompactionContinuationSettingsTests*)|(CheckInterpretationValidatorTests*)|(CheckpointManifestDocumentationTests*)|(CheckScheduleBackoffTests*)|(CheckScheduleElapsedTimesTests*)|(CheckScheduleRoundingTests*)|(CheckSpecialistLaunchPolicyTests*)|(ClaudeEffortByRoleSettingsTests*)|(ClaudeLaunchArgsTests*)|(ClaudeRemoteControlLaunchArgsTests*)|(CodexLaunchArgsTests*)|(CodexPhoneHomeProjectionTests*)|(ColumnTextTests*)|(CommitOnSettleDocumentationTests*)|(CommitOnSettlePolicyResolverTests*)|(CommitRecoveryObligationsTests*)|(CompactionContinuationPolicyTests*)|(CompletionHeaderOverlapTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(CompletionNoteWorkQueueTests*)|(ComplexityEffectiveComposeTests*)|(ComplexityRoutingComposeTests*)|(ContextCompactionSettingsTests*)|(D2AdvisoryDocumentationTests*)|(DelegateBindRefusalRecoveryTests*)|(DelegateBranchNameTests*)|(DelegationAllowedRootsFileTests*)|(DelegationCapabilityContractTests*)|(DelegationCostTests*)|(DelegationDenyHookPolicyTests*)|(DelegationKindPricingTests*)|(DelegationLeaseSettingsTests*)|(DelegationPoolDefaultsTests*)|(DelegationQuestionDetectionTests*)|(DelegationReportFormatterTests*)|(DelegationScopeLeaseTests*)|(DelegationWorkspaceBoundaryTests*)|(DeliveryBackendCeilingsTests*)/* executed=244 passed=244 failed=0 skipped=1 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(DesktopCapabilitiesSnapshotTests*)|(DiagnosisTests*)|(DiagnosticsRedactorTests*)|(DialogDeliveryAdmissionTests*)|(DirectoryBrowseServiceTests*)|(DirectoryMatcherTests*)|(DispatchBaseWarningRosterTests*)|(DispatchConcurrencyPolicyTests*)|(DispatchHoldLedgerTests*)|(ExpectationDirectiveTests*)|(ExpectationPipelineTests*)|(GitIndexLockTests*)|(GrokComposerScreenTests*)|(GrokDeliveryShapeTests*)|(GrokLaunchArgsTests*)|(GrokNativeSessionResumeTests*)|(GrokQuestionPopupTests*)|(GrokRulesLiveEvidenceTests*)|(GrokRulesTransportCompatibilityTests*)|(HerdrAgentKindMapTests*)|(HerdrLaunchContextTitleTests*)|(HerdrStatusDisagreementTests*)/* executed=224 passed=224 failed=0 skipped=17 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(HerdrSupervisionFailureEvidenceTests*)|(HostCleanupWorktreeInventoryTests*)|(HostStatsAntiphonCountersTests*)|(HostStatsCacheTests*)|(InstructionBundleTests*)|(InstructionFileStampTests*)|(InstructionFragmentTests*)|(InternalDecisionPolicyTests*)|(IssueTrackerConfigParserTests*)|(KeepSixDocumentationTests*)|(KeepSixProjectionTests*)|(LandFailureDiagnosticTests*)|(LandMergeWriterTests*)/* executed=214 passed=214 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-57/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(LandReceiptScanCacheTests*)|(LandVerificationBuildSlotTests*)|(LaunchModelArgumentAppenderTests*)|(LeaseTurnScenarioTests*)|(MatrixDefaultComplexitySettingsTests*)|(MechanicalPcContractTests*)|(MidTurnInterruptDocumentationTests*)|(MidTurnInterruptPolicyTests*)/* executed=184 passed=184 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-59 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(ModelAliasTests*)|(ModelLevelAliasDisplayTests*)|(MutationRoleContractTests*)|(OperatorShutdownCoordinatorTests*)|(OrchestratorBundleOwnershipTests*)|(OrchestratorInstructionsDeltaTests*)|(OrchestratorInstructionsGuidanceTests*)|(OrchestratorInstructionsPathTests*)|(OrchestratorInstructionsRecipientTests*)|(OrchestratorInstructionsRendererTests*)|(OrchestratorInvestigationDetectorTests*)|(OrchestratorSettingsTests*)|(OrchestratorWorkspaceLayoutTests*)/* executed=184 passed=184 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-59/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-60 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/OutboundConversionManifestTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-60/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-61 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(OutputDistillationGateTests*)|(OutputDistillationPolicyTests*)|(OutputDistillationQueueTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeRunnerRetirementIdentityTests*)|(PhoneHomeRunnerSettingsValidatorTests*)|(PhoneHomeSpillTests*)|(PhoneHomeTaskRoutingTests*)|(PipelineDefinitionDocumentationTests*)|(PipelineHandoffParseTests*)|(PipelineHandoffRouteDocumentationTests*)|(PipelineStagesJsonTests*)|(PlanCatalogServiceTests*)/* executed=247 passed=247 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-61/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-62 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(PolicyRefreshDeltaTests*)|(PostLandMutationContractTests*)|(PostLandMutationPublicationTests*)|(PostLandMutationReceiptPolicyTests*)|(PostLandRetrospectiveContractTests*)|(ProviderCapacityNoticeTests*)|(ProviderContractCatalogTests*)|(ProviderEndpointDocumentationTests*)|(ProviderQuotaRefusalTests*)|(PtyDeliveryCeilingsTests*)|(PtyInlineCeilingTests*)|(PublishedRoutingDocumentationTests*)|(ReceiptScanDestinationTests*)|(ReleaseBranchModelDocumentationTests*)|(RemoteControlMenuScreenTests*)|(RemoteControlPolicyTests*)|(RemoteVerificationWorkspaceTests*)/* executed=238 passed=238 failed=0 skipped=4 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-62/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-63 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(RemoteWorktreeMirrorTests*)|(RepairSourceDocumentationTests*)|(RepositoryLeaseWaitersTests*)|(RestartFailureClassificationTests*)|(ReviewEvidenceParserTests*)|(RolePolicyConfigurationValidationTests*)|(RolePolicyReadServiceTests*)|(RolePolicyToolBundleTests*)|(RoutingCandidateParseTests*)|(RunnerAbsenceEvidenceValidatorTests*)|(RunnerBranchContractDocumentationTests*)|(RunnerCapabilitiesFailureTests*)|(RunnerContractMapperTranscriptEventTests*)|(RunnerDefaultGuidanceTests*)|(RunnerReadSourcePinTests*)|(RunnerSlotRulesTests*)|(RunnerWorkspaceParkPathTests*)|(ScheduleRecurrenceTests*)|(ScopeDriftTests*)|(ScopedVerificationInstructionTests*)|(ScopeOverlapPolicyTests*)|(ScopeResolverAreaTests*)|(SeatOccupancyProjectionTests*)/* executed=241 passed=241 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-63/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-64 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(SessionContextUsageTests*)|(SessionDeliveryProfileTests*)|(SessionReAdoptionStateTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(SessionRunnerHttpClientHostStatsTests*)|(SessionRunnerSettingsTests*)|(SettlementSyncDebtAttentionTests*)|(SettlementSyncDebtPolicyTests*)|(SharedWriterLeaseProjectionTests*)|(SiblingInspectionResultsTests*)|(SiblingWarningReducerTests*)|(SlashCommandCatalogServiceTests*)|(SlashCommandParserTests*)|(SourceLandingAdmissionTests*)|(SpecialistAttemptEvidenceTests*)|(SpecialistFailurePolicyTests*)|(SpecialistQualificationContractTests*)|(SpecialistRoleContractTests*)|(StandingBootDocumentationTests*)|(StandingBootWatchPolicyTests*)|(StandingPipelinePolicyDocumentationTests*)|(StandingSpecialistHealthPolicyTests*)|(SubagentNotificationTests*)|(SubscriptionQuotaGateTests*)|(SubscriptionUsageParserTests*)/* executed=239 passed=239 failed=0 skipped=2 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-64/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-65 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(TaskCompletionContinuationTests*)|(TaskCompletionProgressPolicyTests*)|(TaskPlatformGuidanceTests*)|(TaskReportHousekeepingTests*)|(TierReasoningEffortAgreementTests*)|(TrackerSyncSummaryFormatterTests*)|(TranscriptIncidentReplayGuardTests*)|(TranscriptLocalCommandEchoTests*)|(TypedBodySpillTests*)|(UnmarkedWaitingContractTests*)|(UsageLimitWallParserTests*)|(VerificationReceiptPolicyTests*)|(VerificationRoundBriefTests*)|(VerificationRoundInstructionTests*)|(WorkerWorkspaceDefaultMigrationShapeTests*)|(WorkerWorkspaceDefaultStorageTests*)|(WorkflowEngineParsingTests*)|(WorkspaceReservationKeyTests*)|(WorkspaceReservationLivenessTests*)/* executed=232 passed=232 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-65/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-66 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Application/(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(WorktreeIgnoredContentClassifierTests*)|(WorktreeListParsingTests*)/* executed=89 passed=89 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-66/run.trx slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-67 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)|(BuildSlotClientTests*)|(C1307CliCancellationTests*)|(CheckpointAdmissionBudgetTests*)|(CheckpointAdmissionContractTests*)|(CheckpointAppTests*)|(CheckpointBoundedReaderTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointConfinementLedgerTests*)|(CheckpointEofStallTests*)|(CheckpointExecutorLogTests*)|(CheckpointFixtureProcessTests*)|(CheckpointFreshOutputSealTests*)|(CheckpointImportTests*)|(CheckpointLargeStampScriptTests*)|(CheckpointLargeStampTests*)|(CheckpointLaunchCleanupTests*)|(CheckpointLineTests*)|(CheckpointManifestTests*)|(CheckpointNamespaceCensusUsageTests*)|(CheckpointOrFilterTests*)|(CheckpointOutputConfinementTests*)|(CheckpointOwnedOutputTests*)|(CheckpointProcessIdentityTests*)|(CheckpointReceiptSafetyTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=0s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)|(CheckpointRepeatConfigurationTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointRepeatHostTests*)|(CheckpointRunOwnershipTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointShapeConflictTests*)|(CheckpointSlotContractTests*)|(CheckpointStampSafetyTests*)|(CheckpointTaskOwnershipTests*)|(CheckpointTempRootSweepTests*)|(CheckpointTempScopeTests*)|(CheckpointTimingHarnessTests*)|(CheckpointToolCopyCleanupTests*)|(CheckpointToolSourceTests*)|(CheckpointTrackedBaselineTests*)|(CheckpointWindowsReadModeTests*)|(DetachedLauncherTests*)|(EvidenceFolderTests*)|(ExitCodeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-69 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(PlanCoverageAssertionTests*)|(PlanCoverageBindingTests*)|(PlanCoverageCensusTests*)|(PlanCoverageCommandTests*)|(PlanCoverageGoldenTests*)|(PlanCoverageHandleTests*)|(PlanCoverageParserTests*)|(PlanCoveragePcTests*)|(ProcessDriverTests*)|(ReapedProcStatIdentityTests*)|(ReportMergerTests*)|(ReportWriterTests*)|(RerunFilterHostTests*)|(RerunPolicyTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(RunStateStoreTests*)|(SafeDeleteProcessBoundaryTests*)|(ShadowCopyTests*)|(TestPhaseCustodyTests*)|(TestSetTimingTests*)|(TimeoutTests*)|(TimingJournalTests*)|(TrxReportTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-70 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Checkpoints/WaitCommandTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-71 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Domain.StateMachine/(CardStateMachineTests*)|(RunAttemptStateMachineTests*)|(WorkflowStateMachineTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-72 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Infrastructure/(AppDbContextModelTests*)|(AppHostBrokerSourceGuardTests*)|(C1307CancellationTests*)|(C1307ToolRemovalTests*)|(CheckpointToolProjectContractTests*)|(CodexRootExecContractTests*)|(CodexRunnerImageContractTests*)|(CustodyHelperContractTests*)|(DindRunnerContractTests*)|(DirectoryBuildRspTests*)|(DockerDeliveryBarrierTests*)|(DockerDeliveryObservationTests*)|(DockerStackContractTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-73 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)|(DotnetToolManifestContractTests*)|(E2ECopyOwnedPtyHostAppHostContractTests*)|(EvidencePolicyWorkflowTests*)|(FileSystemEvidenceRetentionTests*)|(FileUseWindowsTests*)|(GithubCredentialHelperTests*)|(GitMinimumVersionGuardTests*)|(GrokRunnerImageContractTests*)|(GrokSha256PinTests*)|(HostCleanupCheckpointAuthorityTests*)|(HostCleanupCheckpointCompatibilityTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointExtractionTests*)|(HostCleanupCheckpointPathTests*)|(HostCleanupCheckpointReadDeadlineTests*)|(HostCleanupCheckpointReceiptTests*)|(HostCleanupCheckpointSweepRaceTests*)|(HostCleanupExecutionTests*)|(HostCleanupOwnershipTests*)|(HostCleanupPolicyTests*)|(JqRunnerImageContractTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-74 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)|(LandingIdentityControlTests*)|(LandingRemovalPolicyControlTests*)|(LogRetentionTests*)|(OperatorDashboardSessionsTests*)|(OperatorTokenFileTests*)|(OrchestratorInstructionsMigrationShapeTests*)|(PackageVersionPinContractTests*)|(PostgresPoolConfigurationTests*)|(RunnerPushCredentialDocsTests*)|(SessionRunnerGrokLineEndingContractTests*)|(ShellFixtureLineEndingContractTests*)|(WorktreeDeleteAccessProbeTests*)|(WorktreeLockDiagnosticsTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-75 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Infrastructure/WorktreeRemovalDefaultTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-76 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Infrastructure.Resilience/(HttpReadResilienceTests*)|(HttpResilienceRegistrationTests*)|(ResilienceBudgetTests*)|(ResilienceCircuitTests*)|(ResilienceOptionsAndTelemetryTests*)|(ResiliencePredicateTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-77 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Migrations/(CommitOnSettleMigrationTests*)|(PipelineDefinitionMigrationShapeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-78 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)|(C1364BoundLandWaitProofTests*)|(DelegateScriptInterruptSwitchTests*)|(DelegateScriptRunnerSwitchTests*)|(HostJqPrerequisiteScriptTests*)|(ProcessSpawnTimeoutPolicyTests*)|(QualificationCheckoutSourceTests*)|(RefreshGithubTokenRemoteCommandTests*)|(RefreshGithubTokenScriptTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-79 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-80 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-81 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)|(CheckCompactionFixtureTests*)|(ControlledLandingGitTests*)|(DelegationHarnessCensusTests*)|(DelegationTestServicesTests*)|(DirectoryLinkFixtureTests*)|(EntityScalarSnapshotConsumerGuardTests*)|(EntityScalarSnapshotTests*)|(LiveFixtureTextTests*)|(LoadTimeoutBoundWitnessTests*)|(LoadTimeoutWaitTests*)|(PairedOverheadStatisticsTests*)|(ScaledTimeProviderTests*)|(SlowTestTripwireTests*)|(TestAppHostPathTests*)|(TestClassificationPolicyTests*)|(TestDbFixtureLifecycleTests*)|(TestDbFixtureSchemaCacheLifecycleTests*)|(TestLaneCategoryGuardTests*)|(TestPhaseSummaryTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-82 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)|(TestSchemaCacheTests*)|(TestTimingLifecycleTests*)|(TestTimingRecorderTests*)|(WindowsDeliveryFixtureWitnessTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-83 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.TestSupport/TestClassificationGuardTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-84 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-85 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionContinuationTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-86 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionCrashTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-87 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionAttentionTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-88 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionWorkerLifetimeTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-89 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionReceiptTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-90 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionAutomaticRestartTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-91 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=n/a filter=/*/*/CheckCompactionRecoveryPersistenceTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-92 commit=192f642b2b0a5d23f9f9e33112adb8c43efe579d build=reused filter=/*/*/DataRetentionServiceTests/C1610_Unresolved_supervision_retains_evidence_until_told executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-40e35f61/.antiphon/checkpoints/20261030-173840-e5d5/rows/CP-92/run.trx slot=granted waited=75s dirty=0 source=192f642b2b0a5d23f9f9e33112adb8c43efe579d sourceState=clean buildSource=verified
```

## Unedited duration-tripwire advisory lines

```text
CP-28
SLOW-TEST TRIPWIRE: 7 unlisted tests >= 5s
10.006s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets  C1149_C1150_Statement_budgets(working-live-tick, 19)
10.037s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1150_Local_spill_must_be_present_and_intact  C1150_Local_spill_must_be_present_and_intact(native, missing)
10.621s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1150_Inline_brief_mentioning_spill_paths_is_not_held  C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, received)
10.851s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1150_Late_ensure_after_remote_receipt_and_payload_release  C1150_Late_ensure_after_remote_receipt_and_payload_release(clipped)
5.714s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_certificate_holds_original_input  C1153_Real_client_certificate_holds_original_input(local-http)
5.033s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1153_Certificate_age_is_monotonic  C1153_Certificate_age_is_monotonic(exact-five-seconds)
5.157s  Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1153_Certificate_age_is_monotonic  C1153_Certificate_age_is_monotonic(fresh)
CP-16
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
10.643s  Antiphon.Tests.Application.TaskCeilingControlTests.C1610_Replacement_racing_handoff_is_held  C1610_Replacement_racing_handoff_is_held
CP-19
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
8.294s  Antiphon.Tests.Application.TaskSupervisionVisibilityTests.C1610_Visibility_and_pool_safety  C1610_Visibility_and_pool_safety(failed-fresh)
CP-21
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
13.114s  Antiphon.Tests.Application.AgentTaskPoolTests.a_dispatched_pool_delegate_remains_boardless_after_agent_board_backfill  a_dispatched_pool_delegate_remains_boardless_after_agent_board_backfill
6.165s  Antiphon.Tests.Application.AgentTaskPoolTests.C691_pool_sweeps_preserve_AlwaysOn_and_standing_agents  C691_pool_sweeps_preserve_AlwaysOn_and_standing_agents(True, False)
CP-22
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
6.693s  Antiphon.Tests.Application.AgentTaskOverdueDeadlineTests.a_task_past_its_role_ceiling_is_failed_with_the_clock_and_the_phase_named  a_task_past_its_role_ceiling_is_failed_with_the_clock_and_the_phase_named
CP-25
SLOW-TEST TRIPWIRE: 37 unlisted tests >= 5s
12.417s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_compaction_continuation_prompt_is_not_a_stalled_model_call  a_compaction_continuation_prompt_is_not_a_stalled_model_call
12.417s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_task_nowhere_near_a_limit_costs_one_comparison_and_returns_nothing  a_task_nowhere_near_a_limit_costs_one_comparison_and_returns_nothing
12.424s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_local_command_stdout_tail_is_not_a_stalled_model_call  a_local_command_stdout_tail_is_not_a_stalled_model_call
12.428s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.an_interrupt_marker_is_the_turns_end_not_a_stalled_model_call  an_interrupt_marker_is_the_turns_end_not_a_stalled_model_call
12.430s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.the_last_entry_is_the_newest_by_TIME_not_by_sequence  the_last_entry_is_the_newest_by_TIME_not_by_sequence
12.437s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.an_unarmed_ceiling_and_unarmed_phases_evaluate_nothing_at_all  an_unarmed_ceiling_and_unarmed_phases_evaluate_nothing_at_all
12.522s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_role_with_no_policy_entry_falls_back_to_the_default_ceiling  a_role_with_no_policy_entry_falls_back_to_the_default_ceiling
12.524s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_finished_turn_gets_the_ceiling_and_nothing_tighter  a_finished_turn_gets_the_ceiling_and_nothing_tighter
12.526s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_task_past_the_role_ceiling_is_breached_on_the_wall_clock  a_task_past_the_role_ceiling_is_breached_on_the_wall_clock
11.692s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_running_local_tool_takes_the_much_longer_local_execution_deadline  a_running_local_tool_takes_the_much_longer_local_execution_deadline
11.692s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_token_count_does_not_reset_the_role_ceiling  a_token_count_does_not_reset_the_role_ceiling
12.549s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_token_count_does_not_reset_the_local_tool_clock  a_token_count_does_not_reset_the_local_tool_clock
11.813s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline  all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline(ToolResult)
11.708s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline  all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline(Thinking)
11.711s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline  all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline(AssistantText)
12.553s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_session_without_a_token_count_still_breaches_model_wait_at_twenty_one_minutes  a_session_without_a_token_count_still_breaches_model_wait_at_twenty_one_minutes(Grok)
12.571s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_backfilled_sequence_cannot_manufacture_a_stall  a_backfilled_sequence_cannot_manufacture_a_stall
11.730s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.one_model_row_after_the_prompt_drops_back_to_the_general_arm  one_model_row_after_the_prompt_drops_back_to_the_general_arm(TurnEnd)
12.574s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_session_without_a_token_count_still_breaches_model_wait_at_twenty_one_minutes  a_session_without_a_token_count_still_breaches_model_wait_at_twenty_one_minutes(ClaudeCode)
11.732s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.one_model_row_after_the_prompt_drops_back_to_the_general_arm  one_model_row_after_the_prompt_drops_back_to_the_general_arm(AssistantText)
11.737s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.one_model_row_after_the_prompt_drops_back_to_the_general_arm  one_model_row_after_the_prompt_drops_back_to_the_general_arm(Thinking)
12.586s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.twenty_one_minutes_without_a_transcript_row_or_a_token_count_still_breaches_model_wait  twenty_one_minutes_without_a_transcript_row_or_a_token_count_still_breaches_model_wait
12.457s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline  all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline(UserPrompt)
11.757s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_disarmed_boot_deadline_falls_back_to_the_general_model_wait  a_disarmed_boot_deadline_falls_back_to_the_general_model_wait
11.759s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_compaction_record_after_the_prompt_is_still_a_boot_turn  a_compaction_record_after_the_prompt_is_still_a_boot_turn
12.607s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_token_count_does_not_reset_the_boot_clock  a_token_count_does_not_reset_the_boot_clock
12.610s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(interrupt-only-after-resume)
11.770s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.inherited_rows_before_the_launch_resume_do_not_end_the_boot_turn  inherited_rows_before_the_launch_resume_do_not_end_the_boot_turn
11.771s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.one_model_row_after_the_prompt_drops_back_to_the_general_arm  one_model_row_after_the_prompt_drops_back_to_the_general_arm(ToolCall)
11.774s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_prompt_with_nothing_after_it_takes_the_tighter_boot_deadline  a_prompt_with_nothing_after_it_takes_the_tighter_boot_deadline
12.618s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_warm_pool_session_is_never_charged_for_a_stall_it_inherited  a_warm_pool_session_is_never_charged_for_a_stall_it_inherited
12.620s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(queued-only-working)
12.618s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(accepted-only)
12.624s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(queued-only-idle)
12.643s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(queued-then-accepted)
12.657s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.a_codex_token_count_five_minutes_old_holds_a_twenty_one_minute_assistant_text  a_codex_token_count_five_minutes_old_holds_a_twenty_one_minute_assistant_text
12.686s  Antiphon.Tests.Application.TaskDeadlinePolicyTests.C1151_Boot_facts_need_an_accepted_prompt  C1151_Boot_facts_need_an_accepted_prompt(accepted-then-queued-refinement)
CP-26
SLOW-TEST TRIPWIRE: 3 unlisted tests >= 5s
5.015s  Antiphon.Tests.Application.BootStallDetectionTests.C1151_Listed_or_unknown_session_is_untouched  C1151_Listed_or_unknown_session_is_untouched(working-listed)
5.015s  Antiphon.Tests.Application.BootStallDetectionTests.C1151_Boot_branch_statement_counts  C1151_Boot_branch_statement_counts(non-working-listed, 29, 1)
5.033s  Antiphon.Tests.Application.BootStallDetectionTests.C1151_Only_an_accepted_prompt_is_protected  C1151_Only_an_accepted_prompt_is_protected(accepted-then-queued-refinement)
CP-27
SLOW-TEST TRIPWIRE: 3 unlisted tests >= 5s
8.871s  Antiphon.Tests.Application.AgentTaskReplyIntegrationTests.C527_equals_separator_delivers_exact_parent_receipt  C527_equals_separator_delivers_exact_parent_receipt(False, False)
8.431s  Antiphon.Tests.Application.AgentTaskReplyIntegrationTests.C1499_a_release_the_runner_answers_exited_then_not_found_is_a_normal_release  C1499_a_release_the_runner_answers_exited_then_not_found_is_a_normal_release(killed)
7.444s  Antiphon.Tests.Application.AgentTaskReplyIntegrationTests.Deferred_is_durable_and_releases_runtime  Deferred_is_durable_and_releases_runtime
CP-38
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
10.063s  Antiphon.Tests.Application.TaskProgressStallSweepTests.Dedup_one_episode  Dedup_one_episode
CP-39
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
17.549s  Antiphon.Tests.Application.CompletionNoteDurableSweepTests.C1360_DurableSweepRecoversLostFlushOnIdleCaller  C1360_DurableSweepRecoversLostFlushOnIdleCaller
CP-41
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
5.451s  Antiphon.Tests.Application.SpecialistTaskRunnerDeadlineTests.Queued_hold_uses_the_task_kind_snapshot_like_dispatch  Queued_hold_uses_the_task_kind_snapshot_like_dispatch
CP-42
SLOW-TEST TRIPWIRE: 11 unlisted tests >= 5s
8.665s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_DelayAccounting  C1281_DelayAccounting
8.052s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_WriterReceipt  C1281_WriterReceipt
8.078s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(obligation-insert)
5.408s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(settled-committed)
6.951s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(note-insert)
5.524s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(note-committed)
5.835s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(wakeup-dropped)
7.960s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(render-committed)
6.339s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(spill-written)
6.899s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(attempt-committed)
6.103s  Antiphon.Tests.Application.PipelineHandoffPilotTests.C1281_CompletionRecovery  C1281_CompletionRecovery(prompt-accepted)
CP-43
SLOW-TEST TRIPWIRE: 10 unlisted tests >= 5s
5.763s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.an_unmarked_waiting_block_uses_the_asks_line_not_the_whole_narration  an_unmarked_waiting_block_uses_the_asks_line_not_the_whole_narration
5.761s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_is_null_without_a_session  GetAsync_reports_session_liveness_is_null_without_a_session
5.712s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.a_cost_ceiling_block_cannot_be_answered  a_cost_ceiling_block_cannot_be_answered
5.722s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.a_merge_conflict_points_at_the_child_resolving_it  a_merge_conflict_points_at_the_child_resolving_it
5.744s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.historical_replied_text_is_labelled_not_recorded  historical_replied_text_is_labelled_not_recorded
5.780s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.prior_rounds_are_rebuilt_from_events  prior_rounds_are_rebuilt_from_events
5.786s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_an_ended_session  GetAsync_reports_session_liveness_for_an_ended_session
5.789s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.a_working_task_has_no_blocked_context  a_working_task_has_no_blocked_context
5.789s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.a_question_blocked_task_isolates_the_trailing_question  a_question_blocked_task_isolates_the_trailing_question
5.806s  Antiphon.Tests.Application.AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_a_live_working_session  GetAsync_reports_session_liveness_for_a_live_working_session
CP-44
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
44.277s  Antiphon.Tests.Application.AgentTaskDispatcherWiringTests.the_real_container_gives_the_dispatcher_a_live_transcript_pull  the_real_container_gives_the_dispatcher_a_live_transcript_pull
CP-45
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
6.967s  Antiphon.Tests.ProcessSpawnLimitTests.Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers  Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers
CP-46
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
7.356s  Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution  Disabling_the_probe_does_not_disable_effort_resolution(True)
CP-48
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
5.131s  Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests.Post_trust_blank_or_sign_in_is_not_ready  Post_trust_blank_or_sign_in_is_not_ready
5.303s  Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests.Current_trust_is_answered_once_before_positive_ready  Current_trust_is_answered_once_before_positive_ready
CP-57
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
5.022s  Antiphon.Tests.Application.HerdrSupervisionFailureEvidenceTests.Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them  Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them
CP-65
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
12.965s  Antiphon.Tests.Application.WorkerWorkspaceDefaultStorageTests.C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals  C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals
CP-92
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
5.106s  Antiphon.Tests.Application.DataRetentionServiceTests.C1610_Unresolved_supervision_retains_evidence_until_told  C1610_Unresolved_supervision_retains_evidence_until_told(source-transcript)
```

## Part 2: task 698bf6b9, pre-run assessment

Base 0219ccebf3982b191116dd7d731c8d5b0aaa97c1; Linux/server2. Prior outcomes above
remain historical. This continuation changes plan accounting only at this point.

### All 28 inherited skips classified

Inspected each runtime guard and its Windows production/path prerequisite.
No Linux-capable case is being skipped incorrectly. Windows npm shim/drive roots,
CommandLineToArgvW, drive-rooted report paths, Windows MockFileSystem, the explicitly
Windows Grok argv guard and local modern ConPTY contrast explain every case.
None of these requires weakening an assertion or emulating Windows on Linux.
The portable minima account only for these exact skips; original total coverage
and the native obligation remain. Existing source-qualified observations are retained
in the preceding table; no historical observation is rebound to this source.

| Row | Exact skipped identity | Classification | Evidence / prerequisite |
|---|---|---|---|
| CP-52 | Antiphon.Tests.Application.AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash | Expected Windows-only | Skipped: Canonical pin cwd is a Windows drive-rooted path |
| CP-52 | Antiphon.Tests.Application.AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone | Expected Windows-only | Skipped: Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows |
| CP-46 | Antiphon.Tests.Agents.AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine | Expected Windows-only | Skipped: The npm shim layout is Windows-specific |
| CP-54 | Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard | Expected Windows-only | Skipped: CommandLineToArgvW is Windows-only |
| CP-55 | Antiphon.Tests.Application.DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms | Expected Windows-only | Skipped: Drive-letter report paths resolve only against a Windows repo root |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.prefix_returns_matching_child_directories | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.trailing_slash_lists_children_of_that_directory | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.caches_within_ttl_and_refreshes_after | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.existing_path_reports_exists_true | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.partial_leaf_matches_substring_within_child_name | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(cr) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(lf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(crlf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(nul) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(alias) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(equals) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(missing) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_first) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_second) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(braced_env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env_flag) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_modern_backend_confirms_the_raised_ceilings | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.Unknown_runner_backend_downgrades_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_inbox_conhost_downgrades_a_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind | Expected Windows-only | Skipped: ConPTY only on Windows |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence | Expected Windows-only | Skipped: ConPTY only on Windows |

### CP-67 diagnosis plan, before repeating

The previous six-minute timeout had no per-case journal or TRX. No measured row
p95 or startup/body/teardown split exists, so increasing its deadline would have
no evidential basis. Five disjoint class partitions (216 total cases) retain
six-minute limits and enable existing TestTiming early-start/late-end journaling.
The fixtures use marked isolated temp roots; no database/container migration is
required by this Unit row. TempDir invokes the shared sweep, whose five-minute
interval already avoids replaying the sweep per allocation. Assembly sweep and
process waits remain suspects to measure, not established causes. Cases whose
subject is startup, output seals, teardown or process custody require their own
fixture; no speculative setup sharing is applied before measuring.

The rest of the exact inherited rows CP-68..91 use the same source and one producer,
TEMP=/tmp, UseAppHost=false, serial rows, 25-minute admission bounds. Execution
and independent wall bounds are finite and reported separately. Mixed-case
p50/p95/max are collected once per needed case; repeated-history p95 and detailed
external-call spans remain provisional/unknown where instrumentation is absent.
The real report validator rejects any skip even if ExpectText documents one;
this is a separate verification-tool limitation, never a fabricated clean gate.


## Part 2: task 698bf6b9, completed observations and remaining gates

Verification remains incomplete: 3557 fresh passes, 0 assertion failures, 43 expected Windows skips; CP-79 and CP-80 still need measured follow-up.

All fresh receipts below bind to clean source **b4cea3d0e1d3f9e47af86095696a72a25bcd530a**, fingerprint `d05cfb143e4dc1dface38b1ff7c91577eb92cdc0a04414f385e7b7427a947ab8`, and one verified isolated producer. The tracked tree stayed frozen throughout execution. This is ordinary Debug evidence, not Full, native qualification, ReviewedSourceClean, land approval or activation. No production/test source or definition changed.

### Fresh per-row observations

The latest completed observation replaces only the matching historical row. Raw timeout, roster-miss, cancellation and stopped-run records remain below. Direct-row execution is the monotonic driver wall envelope minus receipt admission; it is **not** scheduler-charged execution. Scheduled rows use the scheduler measurement. `T:` phases are TRX-only startup/body/teardown and exclude pre-TRX dotnet startup; `H:` phases include measured host startup. Detailed DB, CLI, fs, fixture, IO and drain spans are unknown when no phase was emitted. No unknown is represented as zero. Case p50/p95/max are nearest-rank across different cases in one run, not repeated row-history quantiles. The table selects one latest observation per identity; successful repeat histories for CP-69/74/81 are retained separately. Sample sizes remain small and bounds provisional.

| Row | Raw state | Executed / passed / failed / skipped | Wall s | Admission s | Execution s | Startup / body wall / teardown s | Case p50 / p95 / max s (n) | Actual bound min |
|---|---|---|---:|---:|---:|---|---|---:|
| CP-46 | green | 215 / 215 / 0 / 1 | 14.529 | 0.000 | 14.529 | T: 1.009 / 7.735 / 0.214 | 0.009 / 1.064 / 7.312 (215) | 15 |
| CP-52 | green | 247 / 247 / 0 / 2 | 9.426 | 0.000 | 9.426 | T: 1.744 / 0.588 / 0.317 | 0.003 / 0.248 / 0.509 (247) | 15 |
| CP-54 | green | 249 / 249 / 0 / 1 | 41.917 | 0.000 | 41.917 | T: 21.313 / 13.639 / 1.534 | 0.010 / 0.131 / 4.850 (249) | 15 |
| CP-55 | green | 244 / 244 / 0 / 1 | 7.385 | 0.000 | 7.385 | T: 1.819 / 0.301 / 0.338 | 0.003 / 0.166 / 0.206 (244) | 15 |
| CP-56 | green | 224 / 224 / 0 / 17 | 8.626 | 0.000 | 8.626 | T: 1.937 / 0.461 / 0.333 | 0.009 / 0.197 / 0.269 (224) | 15 |
| CP-62 | green | 238 / 238 / 0 / 4 | 8.292 | 0.000 | 8.292 | T: 1.930 / 0.388 / 0.337 | 0.016 / 0.150 / 0.210 (238) | 15 |
| CP-64 | green | 239 / 239 / 0 / 2 | 27.889 | 0.000 | 27.889 | T: 18.296 / 1.981 / 2.786 | 0.012 / 0.131 / 1.728 (239) | 15 |
| CP-67a | green | 38 / 38 / 0 / 0 | 106.792 | 90.000 | 16.506 | H: 8.970 / 2.681 / 0.990 | 0.248 / 2.328 / 2.336 (38) | 6 |
| CP-67b | green | 48 / 48 / 0 / 0 | 605.584 | 180.000 | 425.584 | T: 1.546 / 414.188 / 0.202 | 1.913 / 31.666 / 121.245 (48) | 15 |
| CP-67c | green | 40 / 40 / 0 / 0 | 321.923 | 240.000 | 81.444 | H: 5.532 / 72.547 / 0.693 | 0.090 / 10.951 / 22.775 (40) | 6 |
| CP-67d | green | 28 / 28 / 0 / 0 | 107.887 | 30.000 | 77.635 | H: 8.601 / 66.361 / 0.480 | 1.565 / 20.627 / 23.310 (28) | 6 |
| CP-67e | green | 61 / 61 / 0 / 1 | 544.994 | 225.000 | 319.994 | T: 1.821 / 307.916 / 0.734 | 1.011 / 27.864 / 50.539 (61) | 15 |
| CP-68 | green | 214 / 214 / 0 / 1 | 641.577 | 0.000 | 641.577 | T: 1.175 / 633.573 / 0.166 | 0.143 / 19.809 / 50.089 (214) | 15 |
| CP-69 | green | 188 / 188 / 0 / 8 | 305.804 | 30.000 | 275.804 | T: 0.909 / 268.268 / 0.163 | 0.930 / 24.350 / 267.716 (188) | 15 |
| CP-70 | green | 12 / 12 / 0 / 0 | 69.919 | 60.000 | 9.567 | H: 5.224 / 2.360 / 0.907 | 0.402 / 2.360 / 2.360 (12) | 4 |
| CP-71 | green | 61 / 61 / 0 / 0 | 6.483 | 0.000 | 6.396 | H: 5.023 / 0.069 / 0.523 | 0.005 / 0.016 / 0.069 (61) | 4 |
| CP-72 | green | 246 / 246 / 0 / 0 | 155.958 | 135.000 | 20.851 | H: 3.456 / 15.798 / 0.718 | 0.002 / 1.180 / 15.728 (246) | 8 |
| CP-73 | green | 248 / 248 / 0 / 0 | 109.007 | 90.000 | 18.877 | H: 5.857 / 10.664 / 1.185 | 0.053 / 2.807 / 7.389 (248) | 8 |
| CP-74 | green | 248 / 248 / 0 / 2 | 60.709 | 30.000 | 30.709 | T: 2.126 / 19.741 / 0.223 | 0.051 / 0.365 / 19.086 (248) | 15 |
| CP-75 | green | 5 / 5 / 0 / 0 | 6.522 | 0.000 | 6.424 | H: 4.357 / 0.021 / 0.538 | 0.021 / 0.021 / 0.021 (5) | 4 |
| CP-76 | green | 46 / 46 / 0 / 0 | 17.615 | 0.000 | 17.124 | H: 4.664 / 10.296 / 1.355 | 0.029 / 0.619 / 7.677 (46) | 4 |
| CP-77 | green | 4 / 4 / 0 / 0 | 72.373 | 0.000 | 72.016 | H: 66.967 / 1.604 / 2.550 | 0.021 / 1.604 / 1.604 (4) | 4 |
| CP-78 | green | 65 / 65 / 0 / 2 | 878.178 | 90.000 | 788.178 | T: 1.005 / 779.595 / 0.160 | 2.473 / 108.491 / 142.368 (65) | 15 |
| CP-79 | running | unknown / unknown / unknown / unknown | unknown | 45.000 | unknown | unknown | unknown | 24 |
| CP-80 | timeout | unknown / unknown / unknown / unknown | 979.915 | 75.000 | 904.915 | unknown | unknown | 15 |
| CP-81 | green | 249 / 249 / 0 / 1 | 157.662 | 15.000 | 142.662 | T: 18.979 / 116.654 / 1.380 | 0.034 / 4.935 / 12.154 (249) | 15 |
| CP-82 | green | 37 / 37 / 0 / 0 | 150.181 | 120.000 | 30.181 | T: 0.633 / 22.921 / 0.355 | 0.018 / 6.027 / 9.107 (37) | 15 |
| CP-83 | green | 1 / 1 / 0 / 0 | 38.131 | 30.000 | 8.131 | T: 0.492 / 1.840 / 0.311 | 1.839 / 1.839 / 1.839 (1) | 15 |
| CP-84 | green | 8 / 8 / 0 / 0 | 20.126 | 15.000 | 5.126 | T: 0.500 / 0.010 / 0.186 | 0.010 / 0.010 / 0.010 (8) | 15 |
| CP-85 | green | 2 / 2 / 0 / 0 | 227.186 | 195.000 | 32.186 | T: 16.860 / 5.842 / 3.254 | 5.827 / 5.841 / 5.841 (2) | 15 |
| CP-86 | green | 9 / 9 / 0 / 0 | 302.975 | 135.000 | 167.975 | T: 29.961 / 123.194 / 3.571 | 13.549 / 27.800 / 27.800 (9) | 15 |
| CP-87 | green | 1 / 1 / 0 / 0 | 118.704 | 90.000 | 28.704 | T: 15.223 / 5.076 / 3.353 | 5.077 / 5.077 / 5.077 (1) | 15 |
| CP-88 | green | 2 / 2 / 0 / 0 | 106.872 | 30.000 | 76.872 | T: 25.771 / 36.964 / 4.796 | 15.488 / 21.421 / 21.421 (2) | 15 |
| CP-89 | green | 27 / 27 / 0 / 0 | 132.731 | 45.000 | 87.731 | T: 30.616 / 38.170 / 11.207 | 37.095 / 37.237 / 38.169 (27) | 15 |
| CP-90 | green | 11 / 11 / 0 / 0 | 287.772 | 240.000 | 47.772 | T: 25.904 / 12.536 / 3.188 | 12.151 / 12.532 / 12.532 (11) | 15 |
| CP-91 | green | 2 / 2 / 0 / 0 | 29.926 | 0.000 | 29.926 | T: 15.516 / 3.651 / 3.277 | 3.302 / 3.651 / 3.651 (2) | 15 |

CP-67 is covered by the union of five disjoint class partitions: 38+48+40+28+62 discovered = **216**, with 215 passed and one explicit Windows skip. The original full aggregate filter is retained in the plan history; it was not repeated as a duplicate run. CP-73 actually discovered/passed 248 versus its old floor 247; this is recorded rather than credited as an exact old census. The earlier CP-69 and CP-74 raw roster-miss states reflect exactly 8 and 2 deliberate Windows guards against old unadjusted floors, with no failed tests. Their corrected planned floors do not rewrite the historical receipts.

### Prior source-qualified observations retained

These unchanged observations were executed by earlier tasks, not rerun or rebound to b4cea3d0. Full SHA mappings and complete old histories remain in `.antiphon/task-40e35f61.md`. The following rows plus the fresh table form the S2/S3 ledger; no single aggregate run finished.

| Row | Source prefix | State | Executed / passed / failed / skipped | Wall s | Admission s | Execution s | Startup / body wall / teardown s | Host s | Remainder s | Case p95 / max s (n) | Bound min |
|---|---|---|---|---:|---:|---:|---|---:|---:|---|---:|
| CP-17 | 192f642b2 | green | 22 / 22 / 0 / 0 | 133.003 | 45.000 | 87.703 | 31.668 / 49.089 / 6.108 | 86.865 | 0.838 | 2.702 / 18.531 (22) | 10 |
| CP-28 | 192f642b2 | green | 382 / 382 / 0 / 0 | 95.556 | 0.000 | 95.470 | 25.840 / 65.141 / 3.638 | 94.620 | 0.849 | 1.942 / 10.851 (382) | 10 |
| CP-16 | 192f642b2 | green | 44 / 44 / 0 / 0 | 176.705 | 75.000 | 101.613 | 35.732 / 32.454 / 4.117 | 72.302 | 29.310 | 1.143 / 10.643 (44) | 6 |
| CP-15 | d36e79f43 | green | 12 / 12 / 0 / 0 | 6.343 | 0.000 | 6.299 | 4.679 / 0.032 / 0.774 | 5.485 | 0.814 | 0.032 / 0.032 (12) | 10 |
| CP-19 | d36e79f43 | green | 22 / 22 / 0 / 0 | 41.439 | 0.000 | 41.330 | 20.271 / 16.570 / 3.446 | 40.286 | 1.043 | 0.913 / 8.294 (22) | 4 |
| CP-21 | d36e79f43 | green | 48 / 48 / 0 / 0 | 78.574 | 0.000 | 78.506 | 24.869 / 49.073 / 3.651 | 77.593 | 0.913 | 2.111 / 13.114 (48) | 8 |
| CP-22 | 192f642b2 | green | 36 / 36 / 0 / 0 | 187.876 | 150.000 | 37.739 | 18.714 / 15.200 / 3.053 | 36.968 | 0.771 | 0.860 / 6.693 (36) | 8 |
| CP-23 | 192f642b2 | green | 167 / 167 / 0 / 0 | 116.204 | 45.000 | 71.098 | 20.347 / 46.490 / 3.670 | 70.506 | 0.592 | 21.136 / 25.080 (167) | 24 |
| CP-24 | d36e79f43 | green | 10 / 10 / 0 / 0 | 59.981 | 15.000 | 44.745 | 33.777 / 4.165 / 5.100 | 43.043 | 1.703 | 4.165 / 4.165 (10) | 4 |
| CP-25 | d36e79f43 | green | 39 / 39 / 0 / 0 | 98.299 | 45.000 | 52.892 | 31.509 / 12.687 / 4.830 | 49.026 | 3.866 | 12.657 / 12.686 (39) | 6 |
| CP-26 | 192f642b2 | green | 59 / 59 / 0 / 0 | 92.517 | 15.000 | 77.423 | 58.147 / 14.351 / 4.378 | 76.877 | 0.546 | 5.015 / 5.033 (59) | 8 |
| CP-27 | d36e79f43 | green | 341 / 341 / 0 / 0 | 257.181 | 0.000 | 256.686 | 39.361 / 193.366 / 4.104 | 236.831 | 19.855 | 1.393 / 8.871 (341) | 10 |
| CP-29 | d36e79f43 | green | 17 / 17 / 0 / 0 | 62.850 | 15.000 | 47.692 | 21.674 / 18.690 / 6.293 | 46.657 | 1.035 | 18.690 / 18.690 (17) | 6 |
| CP-30 | d36e79f43 | green | 28 / 28 / 0 / 0 | 69.401 | 0.000 | 69.284 | 31.276 / 32.199 / 2.477 | 65.952 | 3.332 | 11.390 / 12.728 (28) | 6 |
| CP-31 | d36e79f43 | green | 35 / 35 / 0 / 0 | 197.813 | 0.000 | 197.760 | 32.083 / 159.748 / 4.479 | 196.309 | 1.451 | 23.812 / 27.195 (35) | 6 |
| CP-32 | d36e79f43 | green | 12 / 12 / 0 / 0 | 98.834 | 0.000 | 98.146 | 32.323 / 39.746 / 2.786 | 74.855 | 23.291 | 19.593 / 19.593 (12) | 4 |
| CP-33 | d36e79f43 | green | 4 / 4 / 0 / 0 | 49.315 | 0.000 | 49.243 | 27.354 / 17.152 / 3.422 | 47.928 | 1.315 | 10.791 / 10.791 (4) | 2 |
| CP-34 | d36e79f43 | green | 8 / 8 / 0 / 0 | 45.088 | 0.000 | 44.978 | 20.319 / 16.427 / 4.431 | 41.177 | 3.801 | 4.978 / 4.978 (8) | 4 |
| CP-35 | d36e79f43 | green | 25 / 25 / 0 / 0 | 61.353 | 30.000 | 31.237 | 19.580 / 4.769 / 3.989 | 28.339 | 2.898 | 4.512 / 4.769 (25) | 6 |
| CP-36 | d36e79f43 | green | 69 / 69 / 0 / 0 | 41.088 | 0.000 | 41.007 | 21.697 / 16.541 / 1.984 | 40.222 | 0.785 | 3.818 / 3.858 (69) | 8 |
| CP-37 | d36e79f43 | green | 1 / 1 / 0 / 0 | 36.225 | 0.000 | 36.110 | 26.412 / 4.928 / 3.148 | 34.488 | 1.623 | 4.928 / 4.928 (1) | 2 |
| CP-38 | d36e79f43 | green | 13 / 13 / 0 / 0 | 53.888 | 0.000 | 53.806 | 33.097 / 14.268 / 4.327 | 51.692 | 2.114 | 10.063 / 10.063 (13) | 4 |
| CP-39 | d36e79f43 | green | 1 / 1 / 0 / 0 | 46.626 | 0.000 | 45.987 | 23.822 / 17.549 / 3.390 | 44.761 | 1.226 | 17.549 / 17.549 (1) | 4 |
| CP-40 | d36e79f43 | green | 40 / 40 / 0 / 0 | 142.716 | 75.000 | 67.241 | 22.251 / 42.160 / 1.878 | 66.290 | 0.951 | 1.830 / 20.397 (40) | 10 |
| CP-41 | d36e79f43 | green | 7 / 7 / 0 / 0 | 33.811 | 0.000 | 33.718 | 22.076 / 9.277 / 1.379 | 32.732 | 0.986 | 5.451 / 5.451 (7) | 4 |
| CP-42 | d36e79f43 | green | 12 / 12 / 0 / 0 | 102.383 | 0.000 | 102.315 | 20.407 / 78.018 / 2.191 | 100.617 | 1.698 | 8.665 / 8.665 (12) | 6 |
| CP-43 | d36e79f43 | green | 10 / 10 / 0 / 0 | 43.757 | 0.000 | 43.681 | 32.750 / 5.806 / 3.775 | 42.332 | 1.350 | 5.806 / 5.806 (10) | 4 |
| CP-44 | d36e79f43 | green | 1 / 1 / 0 / 0 | 88.540 | 30.000 | 58.310 | 5.764 / 44.277 / 5.411 | 55.452 | 2.857 | 44.277 / 44.277 (1) | 2 |
| CP-45 | d36e79f43 | green | 46 / 46 / 0 / 0 | 122.142 | 105.000 | 16.968 | 7.346 / 6.968 / 0.927 | 15.241 | 1.727 | 0.532 / 6.967 (46) | 4 |
| CP-47 | d36e79f43 | green | 29 / 29 / 0 / 0 | 101.136 | 90.000 | 10.991 | 4.708 / 3.939 / 0.866 | 9.513 | 1.479 | 3.824 / 3.937 (29) | 4 |
| CP-48 | d36e79f43 | green | 22 / 22 / 0 / 0 | 28.356 | 15.000 | 13.165 | 5.081 / 5.304 / 1.026 | 11.411 | 1.754 | 5.131 / 5.303 (22) | 4 |
| CP-49 | 192f642b2 | green | 107 / 107 / 0 / 0 | 158.013 | 150.000 | 7.660 | 3.266 / 3.118 / 0.505 | 6.889 | 0.771 | 1.639 / 3.118 (107) | 4 |
| CP-50 | 192f642b2 | green | 158 / 158 / 0 / 0 | 7.955 | 0.000 | 7.885 | 4.260 / 2.507 / 0.473 | 7.240 | 0.645 | 1.938 / 2.275 (158) | 4 |
| CP-51 | 192f642b2 | green | 89 / 89 / 0 / 0 | 4.616 | 0.000 | 4.558 | 3.232 / 0.080 / 0.509 | 3.821 | 0.737 | 0.037 / 0.052 (89) | 4 |
| CP-53 | 192f642b2 | green | 173 / 173 / 0 / 0 | 7.296 | 0.000 | 7.221 | 5.562 / 0.319 / 0.726 | 6.606 | 0.615 | 0.071 / 0.169 (173) | 6 |
| CP-57 | 192f642b2 | green | 214 / 214 / 0 / 0 | 12.309 | 0.000 | 12.218 | 5.163 / 5.022 / 1.274 | 11.460 | 0.759 | 0.176 / 5.022 (214) | 6 |
| CP-58 | 192f642b2 | green | 184 / 184 / 0 / 0 | 6.997 | 0.000 | 6.899 | 4.878 / 0.406 / 0.792 | 6.075 | 0.824 | 0.111 / 0.257 (184) | 6 |
| CP-59 | 192f642b2 | green | 184 / 184 / 0 / 0 | 9.602 | 0.000 | 9.501 | 6.012 / 1.385 / 1.216 | 8.613 | 0.888 | 0.136 / 1.319 (184) | 6 |
| CP-60 | 192f642b2 | green | 35 / 35 / 0 / 0 | 10.111 | 0.000 | 9.986 | 7.247 / 0.756 / 0.846 | 8.848 | 1.138 | 0.406 / 0.449 (35) | 4 |
| CP-61 | 192f642b2 | green | 247 / 247 / 0 / 0 | 13.453 | 0.000 | 13.269 | 8.192 / 2.711 / 1.084 | 11.987 | 1.282 | 0.889 / 2.029 (247) | 8 |
| CP-63 | 192f642b2 | green | 241 / 241 / 0 / 0 | 14.949 | 0.000 | 14.816 | 10.537 / 2.679 / 0.610 | 13.826 | 0.990 | 0.386 / 2.210 (241) | 8 |
| CP-65 | 192f642b2 | green | 232 / 232 / 0 / 0 | 23.396 | 0.000 | 23.334 | 7.618 / 13.518 / 1.284 | 22.420 | 0.914 | 0.348 / 12.965 (232) | 6 |
| CP-66 | 192f642b2 | green | 89 / 89 / 0 / 0 | 13.781 | 0.000 | 13.386 | 8.369 / 2.627 / 0.817 | 11.813 | 1.573 | 1.726 / 1.734 (89) | 4 |
| CP-92 | 192f642b2 | green | 6 / 6 / 0 / 0 | 125.676 | 75.000 | 50.552 | 34.143 / 7.003 / 7.740 | 48.887 | 1.666 | 5.106 / 5.106 (6) | 4 |

### All fresh platform skips

All are deliberate **Windows-only proofs**, not Linux-capable gaps; no assertion, tag, timer or allowlist was weakened. The initial 28 expanded cases were inspected before execution and are classified individually in the investigation’s pre-run section; the complete fresh list is below. Fresh execution reproduced those exact 28 and found 15 additional deliberate platform guards. Native execution remains pending outside this commission.

| Row | Full class / expanded case | Classification | Runtime reason |
|---|---|---|---|
| CP-46 | Antiphon.Tests.Agents.AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine | Expected Windows-only | Skipped: The npm shim layout is Windows-specific |
| CP-52 | Antiphon.Tests.Application.AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone | Expected Windows-only | Skipped: Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows |
| CP-52 | Antiphon.Tests.Application.AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash | Expected Windows-only | Skipped: Canonical pin cwd is a Windows drive-rooted path |
| CP-54 | Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard | Expected Windows-only | Skipped: CommandLineToArgvW is Windows-only |
| CP-55 | Antiphon.Tests.Application.DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms | Expected Windows-only | Skipped: Drive-letter report paths resolve only against a Windows repo root |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.prefix_returns_matching_child_directories | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.existing_path_reports_exists_true | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.caches_within_ttl_and_refreshes_after | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.partial_leaf_matches_substring_within_child_name | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.trailing_slash_lists_children_of_that_directory | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(cr) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(nul) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(lf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(crlf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_first) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_second) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(missing) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(alias) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(equals) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(braced_env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env_flag) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.Unknown_runner_backend_downgrades_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_inbox_conhost_downgrades_a_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_modern_backend_confirms_the_raised_ceilings | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence | Expected Windows-only | Skipped: ConPTY only on Windows |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind | Expected Windows-only | Skipped: ConPTY only on Windows |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset | Expected Windows-only | Skipped: CARD-1310 confined Windows build leaves UseAppHost unset |
| CP-68 | Antiphon.Tests.Checkpoints.EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open | Expected Windows-only | Skipped: Native file-sharing retry proof requires Windows. |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_sharing_violation_retries_then_adopts | Expected Windows-only | Skipped: CARD-1318 Windows custody hardening |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_copied_old_change_time_is_adopted | Expected Windows-only | Skipped: CARD-1318 Windows copy change time |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write | Expected Windows-only | Skipped: CARD-1310 Windows output custody |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted | Expected Windows-only | Skipped: CARD-1318 Windows custody hardening |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_quick_row_finishes_beside_a_slow_row | Expected Windows-only | Skipped: Concurrent Windows row processes must not inherit each other's stdout pipes. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_chatty_row_drains_interleaved_stdout_and_stderr | Expected Windows-only | Skipped: Windows row pipes must drain a large interleaved stdout and stderr. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild | Expected Windows-only | Skipped: The descendant sweep kills a start /b grandchild on Windows. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_arguments_round_trip_intact | Expected Windows-only | Skipped: Windows row arguments are delivered by ProcessStartInfo.ArgumentList. |
| CP-74 | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded | Expected Windows-only | Skipped: Sharing-mode locks are a Windows file-system behaviour. |
| CP-74 | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass | Expected Windows-only | Skipped: Sharing-mode locks are a Windows file-system behaviour. |
| CP-78 | Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact | Expected Windows-only | Skipped: CARD-1050 and CARD-1110: this audit needs Windows placement |
| CP-78 | Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact | Expected Windows-only | Skipped: CARD-1050 and CARD-1110: this audit needs Windows placement |
| CP-81 | Antiphon.Tests.TestHelpers.TestPhaseSummaryTests.C1514T_PS51AndRerun | Expected Windows-only | Skipped: CARD-1514: actual nightly native custody and PowerShell 5.1 rerun require Windows; Linux cannot qualify this owner. |

`RowRunner` accepts portable minima and preserves skipped identities. `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` additionally requires `row.Skipped == 0`, even for explicitly planned Windows skips. CP-67b validation passed; CP-67e validation returned exit2 `CHECKPOINT SOURCE INVALID reason=row_failed`. Keep this tool gate incomplete and retain native obligations; do not transform a Linux skip into execution credit. Propose a scoped tool-contract follow-up preserving identity/count/source checks; no card was filed.

### CP-67 measured diagnosis and corrected scheduling

The inherited aggregate six-minute timeout had no per-case progress or attribution. Before repeating any selection, the plan was amended/committed with five disjoint partitions, finite six-minute diagnostics and early-start/late-complete journals. CP-67a/c/d finished. CP-67b timed out after 33 completed cases / 34 starts, in the second long bounded-reader probe; CP-67e expired late in its receipt matrix. No assertion failed, and an incomplete journal is not a red test. On the same frozen producer, standalone tool rows with their finite15-minute default completed b/e. The CLI `row` path exposes no per-row deadline switch, so that default is a disclosed scheduling exception; no per-test timeout was changed.

CP-67b’s two slowest `changed_readers_bound_fifo_and_exited_child_pipes` arguments took 121.245s and 121.049s. Its full case window was 414.188s. CP-67e spent 307.916s in its case window; the shape/verb matrix took 50.539s, transitive imported build 39.748s and conditional import 29.703s. These tests deliberately recreate receipts/output/process custody; sharing mutable fixtures would compromise their subject. Existing TempDir sweep is throttled at five minutes, so it is not established as the dominant repeated cost. No speculative fixture cache was introduced. Child/process/fs spans were not emitted by these existing tests: per-case wall is measured, attribution remains unknown.

The completed partition execution envelopes sum to about921.163s before admission. That makes the old 360s aggregate deadline demonstrably too short. A duplicate aggregate is unnecessary: the final plan uses the same exact class union through CP-67a..e and retains the original filter/history. Future initial bounds are a:6/b:16/c:6/d:6/e:12 minutes (2x integer estimates 3/8/3/3/6). b’s 16 minutes exceeds 2x425.584s; e’s 12 exceeds 2x319.994s. These are single-observation provisional bounds, not an invented row-history p95. CP-68’s641.577s supports estimate 11 / bound 22min; CP-69’s304.732s supports 6 /12; CP-78’s788.178s supports 14 /28. The successful standalone15-minute rows are reported at their actual bounds, not retroactively relabeled22/28. Product phase defaults 20/90min and test-specific bounds remain unchanged.

### Incomplete heavy selections and next execution

CP-79 `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/*` (499 identities) did not finish. Its `C1008_Recycle_resume_requires_matching_receipt` alone passed at 398.283s: 16 `C1008HostFixture` constructions each derive 4 Node compose models (64 constructor processes) plus 32 real offline fault/resume script invocations. The test uses 120s bounded child runs; whole class has 600s per-test caps. After about19 minutes only a small fraction of499 cases had started. Supported `stop --run 20261030-184126-5d9d` ended the executor before the remaining global execution budget could produce a whole receipt. It returned0; observer ended6 (`executor died without phase=done`). Main state is `stopped`, CP-79’s stale row state is `running`, no completed TRX or aggregate report exists. A transient TOOL CLEANUP IOException was followed by `Removed ... reason=identity-dead`; executor PID27470 was absent. This is incomplete coverage, never a passing row or assertion-red inheritance claim.

CP-80 retains its exact168-identity filter in the plan. The opt-in journal shows `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` constructing16 fixtures across before-first/late-fault matrices; it shares CP-79’s expensive host model setup. Its terminal outcome and journal counts are below. Before repeating either heavy row, commission measured Debug/Code: isolate immutable compose-model derivation once per process/run, keep per-case mutable state and fresh safety barriers, or split the complete unchanged identity set into bounded method partitions. Capture fixture/CLI/fs/process spans and preserve all 499+168 identities and protective assertions. Do not blindly rerun the full classes or widen test caps. No master-inherited assertion failure was claimed, so no scratch-master execution was fabricated.

### Runtime invariant witnesses and schema

| Invariant | Evidence and scope |
|---|---|
| Detection/hold cannot kill a Working session | No production code changed here. Inspection of the inherited hold, notice, typed-decision and late-forward paths found no added stop/kill/release; explicit Cancel/Retry stays explicit. Fresh CP-90 passed11 narrow CARD-0079 stop/restart witnesses, including human/provider/model/quota/auth holds and unsupported-stop refusal; CP-85..91 passed54 in total. CP-88 kills only its own crash-test worker, not product Working sessions. |
| Held tasks count everywhere | Held remains status Working; unchanged DispatchConcurrencyPolicy.IsOpen/IsParallel, dispatcher local-active and pipeline/Check selectors retain Working. Fresh CP-56 executes the policy classes with224 passes; historical CP-16 passed44 at192f642b2b0a5d23f9f9e33112adb8c43efe579d and CP-34/35/36 passed8/25/69 atd36e79f432307ff26ab687d06bad5e9a5f92cff1. Those are retained historical witnesses, not rerun source stamps. |
| Phase beats ceiling;20/90 unchanged | DelegationSettings defaults remain ModelWaitDeadlineMinutes20 and LocalExecutionDeadlineMinutes90; TaskDeadlinePolicy retains phase-first verdict semantics under S2/S3. Historical CP-16/22/25 hold, overdue and deadline cases remain source qualified in prior table. |
| CARD-1604 token_count unchanged | TaskDeadlinePolicy takes the later transcript/token_count activity only for ModelWait and caps by task age; no new transcript row, boot/local-tool/role-clock change. Source comparison with the combined pre-S2/S3 base confirms equivalent extraction. Existing CP-25 witness is historical. |
| S1 unchanged | Zero-row and uncertain Working retention / FailedTaskSessionOwnership were not changed here. Historical CP-19/21 passed22/48 atd36e79f432307ff26ab687d06bad5e9a5f92cff1. Fresh compaction recovery/receipt/control tests do not replace S1’s specific historical witnesses. |

Both inherited migrations are **additive, not entirely nullable**. `20261030114738_AddTaskCeilingSupervision` adds nullable hold fields, nonnullable Boolean CeilingOperatorExtended default false, and TaskSupervisionNotices with required and optional columns. `20261030143219_AddTaskSupervisionQueuePurpose` adds nullable SourceTaskSupervisionNoticeId plus its filtered unique index. Port S2+S3 together onto master/rc with both migrations, designers and snapshot; migrate before activation after joint ordinary Review and separately reviewed rc ports. No production migration ran. S4 remains behind CARD-1615 and CARD-1609. No tags, deployment or activation is authorized by this record.

### Producer, source and execution provenance

Base 0219ccebf3982b191116dd7d731c8d5b0aaa97c1; initial plan/skip diagnosis commit b4cea3d0e1d3f9e47af86095696a72a25bcd530a. Tool bootstrap used a granted host slot, UseAppHost=false,7.17s compiler/5 existing warnings/0 errors. Plan import admitted98 rows. Producer run20261030-184126-5d9d compiled once into tests/Antiphon.Tests/bin-c1610-p2: compiler325.33s /1419 warnings /0 errors; recorded build 390.337s, wall 438.572s, admission 30s, finite 24min coupled build bound. All subsequent selections reused that source-verified producer. Full inherited filters were compared byte-for-byte with the previous report. No broader assemblies or live/native runner tests were used.

Imported manifest is retained at `.antiphon/c1610-p2/import.yaml`; derived 36-row manifest `.antiphon/c1610-p2/run.yaml`, SHA256 `3d83b7e43d5aba86e6f0b877b53781ccc495b3b3b4e33d73511fb07fb4b281a2`, serial scheduling, UseAppHost=false, TEMP=/tmp,25min admission, finite 60min execution / 110min wall. The last persisted stopped-run budget snapshot (execution 2397.936s, wall 3403.253s, admission 1005.317s) predates most CP-79 work and is **not a final cost**. Standalone row runs continued the exact closed-list selections under their own granted build slots; at most two independently isolated Antiphon.Tests hosts overlapped, no Pty assembly co-scheduled, and all were joined before source edits. TestHelpers fixture lifecycle/cache selections use fake/isolated test schema operations, separate from the compaction integration fixtures. Slot waits were observed, not bypassed.

The actual land graph `-Check` passed at b4cea3d0, using the producer-staged LandGraphAudit DLL under a granted slot: two evaluations and approved canary, no `-Update`. Digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`,68 nodes,195s admission / 32s lease; canary `/work/worktrees/antiphon-landgraph-canary-aa49ced174df4cac828cb46b59b60e64`. Static direct ProjectReference inventory contains 50 projects and computed transitive dependents in ignored project-graph.json. Conditions/imports/dynamic edges are not qualified, and no rc-scoped credit is claimed.

Initial malformed expected-SHA attempt was refused before build/tests and immediately corrected; no red credit. CP-78’s first standalone admission was canceled and joined before tests began when its second Windows skip was discovered; corrected minimum 65 was used on the successful row. The canceled acquisition cost 71.159s and produced no test receipt. Evidence is retained; these are execution corrections, not product failures.

### Latest unedited CHECKPOINT lines

```text
CHECKPOINT CP-46 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Agents/(AgentLaunchSpecTests*)|(AgentProtocolAdapterFactoryTests*)|(AgentRegistrySettingsTests*)|(AgentRegistryTests*)|(AgentSessionSettingsTests*)|(CodexAdapterRawResponseTests*)|(CodexResponseAnalyzerTests*)|(CodexWorkingIndicatorTests*)|(FakeLlmApiSelfTests*)|(GrokAdapterTests*)|(GrokCredentialStoreTests*)|(GrokFreshWorktreeQualificationTests*)|(GrokLinuxBlockingPromptTests*)|(GrokLinuxStartupReadinessTests*)|(GrokModelListParserTests*)|(GrokSignInPromptDetectorTests*)|(GrokStartupCaptureStoreTests*)|(GrokStartupReadinessTests*)|(GrokStartupSizeTests*)|(GrokTrustPromptDetectorTests*)|(HerdrEnvelopeBodyTests*)|(OpenCodeAdapterTests*)|(RunnerClaudeAdapterEffortPromptTests*)|(RunnerClaudeAdapterTrustPromptTests*)|(RunnerClaudeAdapterVerifiedPromptTests*)/* executed=215 passed=215 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-46-20261030-202036-c176/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)|(AgentContextContractTests*)|(AgentExecutableResolverTests*)|(AgentPinPathTests*)|(AgentPresetsTests*)|(AgentSessionRuntimeBoundedReadTests*)|(AgentTaskDecisionQuestionTests*)|(AgentTaskDispatcherPredicateTests*)|(AgentTaskLandingStateTests*)|(AgentTaskLandQueueVisibilityTests*)|(AgentTaskLandVerificationEvidenceTests*)|(AgentTaskLivenessTests*)|(AlarmSettingsValidatorTests*)/* executed=247 passed=247 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-52-20261030-202051-89a5/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(ChannelOutboundContractTests*)|(ChannelOutboundEvidenceAccountingTests*)|(ChannelOutboundStorageTests*)|(ChannelPromptCorrelationUnitTests*)|(ChannelReplyAttachmentReaderTests*)|(CheckCompactionContinuationSettingsTests*)|(CheckInterpretationValidatorTests*)|(CheckpointManifestDocumentationTests*)|(CheckScheduleBackoffTests*)|(CheckScheduleElapsedTimesTests*)|(CheckScheduleRoundingTests*)|(CheckSpecialistLaunchPolicyTests*)|(ClaudeEffortByRoleSettingsTests*)|(ClaudeLaunchArgsTests*)|(ClaudeRemoteControlLaunchArgsTests*)|(CodexLaunchArgsTests*)|(CodexPhoneHomeProjectionTests*)|(ColumnTextTests*)|(CommitOnSettleDocumentationTests*)|(CommitOnSettlePolicyResolverTests*)|(CommitRecoveryObligationsTests*)|(CompactionContinuationPolicyTests*)|(CompletionHeaderOverlapTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-54-20261030-202100-1061/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(CompletionNoteWorkQueueTests*)|(ComplexityEffectiveComposeTests*)|(ComplexityRoutingComposeTests*)|(ContextCompactionSettingsTests*)|(D2AdvisoryDocumentationTests*)|(DelegateBindRefusalRecoveryTests*)|(DelegateBranchNameTests*)|(DelegationAllowedRootsFileTests*)|(DelegationCapabilityContractTests*)|(DelegationCostTests*)|(DelegationDenyHookPolicyTests*)|(DelegationKindPricingTests*)|(DelegationLeaseSettingsTests*)|(DelegationPoolDefaultsTests*)|(DelegationQuestionDetectionTests*)|(DelegationReportFormatterTests*)|(DelegationScopeLeaseTests*)|(DelegationWorkspaceBoundaryTests*)|(DeliveryBackendCeilingsTests*)/* executed=244 passed=244 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-55-20261030-202142-4e11/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(DesktopCapabilitiesSnapshotTests*)|(DiagnosisTests*)|(DiagnosticsRedactorTests*)|(DialogDeliveryAdmissionTests*)|(DirectoryBrowseServiceTests*)|(DirectoryMatcherTests*)|(DispatchBaseWarningRosterTests*)|(DispatchConcurrencyPolicyTests*)|(DispatchHoldLedgerTests*)|(ExpectationDirectiveTests*)|(ExpectationPipelineTests*)|(GitIndexLockTests*)|(GrokComposerScreenTests*)|(GrokDeliveryShapeTests*)|(GrokLaunchArgsTests*)|(GrokNativeSessionResumeTests*)|(GrokQuestionPopupTests*)|(GrokRulesLiveEvidenceTests*)|(GrokRulesTransportCompatibilityTests*)|(HerdrAgentKindMapTests*)|(HerdrLaunchContextTitleTests*)|(HerdrStatusDisagreementTests*)/* executed=224 passed=224 failed=0 skipped=17 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-56-20261030-202149-86ba/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-62 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(PolicyRefreshDeltaTests*)|(PostLandMutationContractTests*)|(PostLandMutationPublicationTests*)|(PostLandMutationReceiptPolicyTests*)|(PostLandRetrospectiveContractTests*)|(ProviderCapacityNoticeTests*)|(ProviderContractCatalogTests*)|(ProviderEndpointDocumentationTests*)|(ProviderQuotaRefusalTests*)|(PtyDeliveryCeilingsTests*)|(PtyInlineCeilingTests*)|(PublishedRoutingDocumentationTests*)|(ReceiptScanDestinationTests*)|(ReleaseBranchModelDocumentationTests*)|(RemoteControlMenuScreenTests*)|(RemoteControlPolicyTests*)|(RemoteVerificationWorkspaceTests*)/* executed=238 passed=238 failed=0 skipped=4 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-62-20261030-202158-f25c/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-64 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(SessionContextUsageTests*)|(SessionDeliveryProfileTests*)|(SessionReAdoptionStateTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(SessionRunnerHttpClientHostStatsTests*)|(SessionRunnerSettingsTests*)|(SettlementSyncDebtAttentionTests*)|(SettlementSyncDebtPolicyTests*)|(SharedWriterLeaseProjectionTests*)|(SiblingInspectionResultsTests*)|(SiblingWarningReducerTests*)|(SlashCommandCatalogServiceTests*)|(SlashCommandParserTests*)|(SourceLandingAdmissionTests*)|(SpecialistAttemptEvidenceTests*)|(SpecialistFailurePolicyTests*)|(SpecialistQualificationContractTests*)|(SpecialistRoleContractTests*)|(StandingBootDocumentationTests*)|(StandingBootWatchPolicyTests*)|(StandingPipelinePolicyDocumentationTests*)|(StandingSpecialistHealthPolicyTests*)|(SubagentNotificationTests*)|(SubscriptionQuotaGateTests*)|(SubscriptionUsageParserTests*)/* executed=239 passed=239 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-64-20261030-202206-7b24/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67a commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=ok filter=/*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)|(BuildSlotClientTests*)|(C1307CliCancellationTests*)|(CheckpointAdmissionBudgetTests*)|(CheckpointAdmissionContractTests*)/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67a/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67b commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)|(CheckpointBoundedReaderTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointConfinementLedgerTests*)|(CheckpointEofStallTests*)/* executed=48 passed=48 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-67b-20261030-192143-89f9/run.trx slot=granted waited=180s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67c commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointExecutorLogTests*)|(CheckpointFixtureProcessTests*)|(CheckpointFreshOutputSealTests*)|(CheckpointImportTests*)|(CheckpointLargeStampScriptTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67c/run.trx slot=granted waited=240s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67d commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointLargeStampTests*)|(CheckpointLaunchCleanupTests*)|(CheckpointLineTests*)|(CheckpointManifestTests*)|(CheckpointNamespaceCensusUsageTests*)/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67d/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67e commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointOrFilterTests*)|(CheckpointOutputConfinementTests*)|(CheckpointOwnedOutputTests*)|(CheckpointProcessIdentityTests*)|(CheckpointReceiptSafetyTests*)/* executed=61 passed=61 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-67e-20261030-193528-0275/run.trx slot=granted waited=225s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)|(CheckpointRepeatConfigurationTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointRepeatHostTests*)|(CheckpointRunOwnershipTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointShapeConflictTests*)|(CheckpointSlotContractTests*)|(CheckpointStampSafetyTests*)|(CheckpointTaskOwnershipTests*)|(CheckpointTempRootSweepTests*)|(CheckpointTempScopeTests*)|(CheckpointTimingHarnessTests*)|(CheckpointToolCopyCleanupTests*)|(CheckpointToolSourceTests*)|(CheckpointTrackedBaselineTests*)|(CheckpointWindowsReadModeTests*)|(DetachedLauncherTests*)|(EvidenceFolderTests*)|(ExitCodeTests*)/* executed=214 passed=214 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-68-20261030-194127-3ea4/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(PlanCoverageAssertionTests*)|(PlanCoverageBindingTests*)|(PlanCoverageCensusTests*)|(PlanCoverageCommandTests*)|(PlanCoverageGoldenTests*)|(PlanCoverageHandleTests*)|(PlanCoverageParserTests*)|(PlanCoveragePcTests*)|(ProcessDriverTests*)|(ReapedProcStatIdentityTests*)|(ReportMergerTests*)|(ReportWriterTests*)|(RerunFilterHostTests*)|(RerunPolicyTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(RunStateStoreTests*)|(SafeDeleteProcessBoundaryTests*)|(ShadowCopyTests*)|(TestPhaseCustodyTests*)|(TestSetTimingTests*)|(TimeoutTests*)|(TimingJournalTests*)|(TrxReportTests*)/* executed=188 passed=188 failed=0 skipped=8 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-69-20261030-202951-28a8/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-70 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/WaitCommandTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-70/run.trx slot=granted waited=60s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Domain.StateMachine/(CardStateMachineTests*)|(RunAttemptStateMachineTests*)|(WorkflowStateMachineTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(AppDbContextModelTests*)|(AppHostBrokerSourceGuardTests*)|(C1307CancellationTests*)|(C1307ToolRemovalTests*)|(CheckpointToolProjectContractTests*)|(CodexRootExecContractTests*)|(CodexRunnerImageContractTests*)|(CustodyHelperContractTests*)|(DindRunnerContractTests*)|(DirectoryBuildRspTests*)|(DockerDeliveryBarrierTests*)|(DockerDeliveryObservationTests*)|(DockerStackContractTests*)/* executed=246 passed=246 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-72/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-73 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)|(DotnetToolManifestContractTests*)|(E2ECopyOwnedPtyHostAppHostContractTests*)|(EvidencePolicyWorkflowTests*)|(FileSystemEvidenceRetentionTests*)|(FileUseWindowsTests*)|(GithubCredentialHelperTests*)|(GitMinimumVersionGuardTests*)|(GrokRunnerImageContractTests*)|(GrokSha256PinTests*)|(HostCleanupCheckpointAuthorityTests*)|(HostCleanupCheckpointCompatibilityTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointExtractionTests*)|(HostCleanupCheckpointPathTests*)|(HostCleanupCheckpointReadDeadlineTests*)|(HostCleanupCheckpointReceiptTests*)|(HostCleanupCheckpointSweepRaceTests*)|(HostCleanupExecutionTests*)|(HostCleanupOwnershipTests*)|(HostCleanupPolicyTests*)|(JqRunnerImageContractTests*)/* executed=248 passed=248 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-73/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)|(LandingIdentityControlTests*)|(LandingRemovalPolicyControlTests*)|(LogRetentionTests*)|(OperatorDashboardSessionsTests*)|(OperatorTokenFileTests*)|(OrchestratorInstructionsMigrationShapeTests*)|(PackageVersionPinContractTests*)|(PostgresPoolConfigurationTests*)|(RunnerPushCredentialDocsTests*)|(SessionRunnerGrokLineEndingContractTests*)|(ShellFixtureLineEndingContractTests*)|(WorktreeDeleteAccessProbeTests*)|(WorktreeLockDiagnosticsTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerTests*)/* executed=248 passed=248 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-74-20261030-203457-3309/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-75 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/WorktreeRemovalDefaultTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-75/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-76 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure.Resilience/(HttpReadResilienceTests*)|(HttpResilienceRegistrationTests*)|(ResilienceBudgetTests*)|(ResilienceCircuitTests*)|(ResilienceOptionsAndTelemetryTests*)|(ResiliencePredicateTests*)/* executed=46 passed=46 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-76/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-77 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Migrations/(CommitOnSettleMigrationTests*)|(PipelineDefinitionMigrationShapeTests*)/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-77/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-78 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)|(C1364BoundLandWaitProofTests*)|(DelegateScriptInterruptSwitchTests*)|(DelegateScriptRunnerSwitchTests*)|(HostJqPrerequisiteScriptTests*)|(ProcessSpawnTimeoutPolicyTests*)|(QualificationCheckoutSourceTests*)|(RefreshGithubTokenRemoteCommandTests*)|(RefreshGithubTokenScriptTests*)/* executed=65 passed=65 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-78-20261030-195603-1559/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-80 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=75s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-81 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)|(CheckCompactionFixtureTests*)|(ControlledLandingGitTests*)|(DelegationHarnessCensusTests*)|(DelegationTestServicesTests*)|(DirectoryLinkFixtureTests*)|(EntityScalarSnapshotConsumerGuardTests*)|(EntityScalarSnapshotTests*)|(LiveFixtureTextTests*)|(LoadTimeoutBoundWitnessTests*)|(LoadTimeoutWaitTests*)|(PairedOverheadStatisticsTests*)|(ScaledTimeProviderTests*)|(SlowTestTripwireTests*)|(TestAppHostPathTests*)|(TestClassificationPolicyTests*)|(TestDbFixtureLifecycleTests*)|(TestDbFixtureSchemaCacheLifecycleTests*)|(TestLaneCategoryGuardTests*)|(TestPhaseSummaryTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-81-20261030-203603-3101/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-82 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)|(TestSchemaCacheTests*)|(TestTimingLifecycleTests*)|(TestTimingRecorderTests*)|(WindowsDeliveryFixtureWitnessTests*)/* executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-82-20261030-203428-ea28/run.trx slot=granted waited=120s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-83 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.TestSupport/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-83-20261030-203529-2260/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-84 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-84-20261030-203551-65b3/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-85 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionContinuationTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-85-20261030-200229-eb4b/run.trx slot=granted waited=195s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-86 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionCrashTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-86-20261030-200516-8516/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-87 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionAttentionTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-87-20261030-200934-3201/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-88 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionWorkerLifetimeTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-88-20261030-201033-447b/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-89 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionReceiptTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-89-20261030-201236-9bff/run.trx slot=granted waited=45s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-90 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionAutomaticRestartTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-90-20261030-201802-7714/run.trx slot=granted waited=240s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-91 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionRecoveryPersistenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-91-20261030-201851-883e/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
```

### Earlier unsuccessful diagnostic receipts retained

```text
CHECKPOINT CP-67b commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)|(CheckpointBoundedReaderTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointConfinementLedgerTests*)|(CheckpointEofStallTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67e commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointOrFilterTests*)|(CheckpointOutputConfinementTests*)|(CheckpointOwnedOutputTests*)|(CheckpointProcessIdentityTests*)|(CheckpointReceiptSafetyTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=105s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)|(CheckpointRepeatConfigurationTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointRepeatHostTests*)|(CheckpointRunOwnershipTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointShapeConflictTests*)|(CheckpointSlotContractTests*)|(CheckpointStampSafetyTests*)|(CheckpointTaskOwnershipTests*)|(CheckpointTempRootSweepTests*)|(CheckpointTempScopeTests*)|(CheckpointTimingHarnessTests*)|(CheckpointToolCopyCleanupTests*)|(CheckpointToolSourceTests*)|(CheckpointTrackedBaselineTests*)|(CheckpointWindowsReadModeTests*)|(DetachedLauncherTests*)|(EvidenceFolderTests*)|(ExitCodeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(PlanCoverageAssertionTests*)|(PlanCoverageBindingTests*)|(PlanCoverageCensusTests*)|(PlanCoverageCommandTests*)|(PlanCoverageGoldenTests*)|(PlanCoverageHandleTests*)|(PlanCoverageParserTests*)|(PlanCoveragePcTests*)|(ProcessDriverTests*)|(ReapedProcStatIdentityTests*)|(ReportMergerTests*)|(ReportWriterTests*)|(RerunFilterHostTests*)|(RerunPolicyTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(RunStateStoreTests*)|(SafeDeleteProcessBoundaryTests*)|(ShadowCopyTests*)|(TestPhaseCustodyTests*)|(TestSetTimingTests*)|(TimeoutTests*)|(TimingJournalTests*)|(TrxReportTests*)/* executed=188 passed=188 failed=0 skipped=8 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-69/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)|(LandingIdentityControlTests*)|(LandingRemovalPolicyControlTests*)|(LogRetentionTests*)|(OperatorDashboardSessionsTests*)|(OperatorTokenFileTests*)|(OrchestratorInstructionsMigrationShapeTests*)|(PackageVersionPinContractTests*)|(PostgresPoolConfigurationTests*)|(RunnerPushCredentialDocsTests*)|(SessionRunnerGrokLineEndingContractTests*)|(ShellFixtureLineEndingContractTests*)|(WorktreeDeleteAccessProbeTests*)|(WorktreeLockDiagnosticsTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerTests*)/* executed=248 passed=248 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-74/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-78 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)|(C1364BoundLandWaitProofTests*)|(DelegateScriptInterruptSwitchTests*)|(DelegateScriptRunnerSwitchTests*)|(HostJqPrerequisiteScriptTests*)|(ProcessSpawnTimeoutPolicyTests*)|(QualificationCheckoutSourceTests*)|(RefreshGithubTokenRemoteCommandTests*)|(RefreshGithubTokenScriptTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=4m slot=granted waited=120s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-80 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=75s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-81 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)|(CheckCompactionFixtureTests*)|(ControlledLandingGitTests*)|(DelegationHarnessCensusTests*)|(DelegationTestServicesTests*)|(DirectoryLinkFixtureTests*)|(EntityScalarSnapshotConsumerGuardTests*)|(EntityScalarSnapshotTests*)|(LiveFixtureTextTests*)|(LoadTimeoutBoundWitnessTests*)|(LoadTimeoutWaitTests*)|(PairedOverheadStatisticsTests*)|(ScaledTimeProviderTests*)|(SlowTestTripwireTests*)|(TestAppHostPathTests*)|(TestClassificationPolicyTests*)|(TestDbFixtureLifecycleTests*)|(TestDbFixtureSchemaCacheLifecycleTests*)|(TestLaneCategoryGuardTests*)|(TestPhaseSummaryTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-81-20261030-202917-58f3/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
```

### Interrupted selection journals (no completed-count credit)

| Selection | Started / completed journal cases | Observed outcomes | Partial lifecycle p50 / p95 / max s | Last started identity |
|---|---|---|---|---|
| CP-79 | 62 / 61 | {"Passed":61} | 1.558 / 57.271 / 398.283 | Antiphon.Tests.Scripts.RemoteScriptContractTests.1.1.C1008_Recycle_receipt_records_disk_and_partial_failure.1.1.0 |
| CP-80-final | 27 / 26 | {"Passed":26} | 14.649 / 116.276 / 131.925 | Antiphon.Tests.Scripts.RetiredTempContainerScriptTests.1.1.C994_Verifier_manifest_is_strict.1.1.0 |

These partial quantiles are censored, not row p95 or valid completed-coverage receipts. CP-80 has a completed **timeout** report (exit5), wall979.915s / receipt admission75s / execution envelope904.915s, bound15min, no TRX. Its report-level verdict is INVALID with reason `source_ineligible`, even though the retained start/end snapshots themselves are clean and build-verified; the timeout is not source-qualified green. CP-79 has no final aggregate report.

### Report validation and duration advisories

- CP-67b: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-67e: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-68: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-78: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-85: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-86: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-87: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-88: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-89: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-90: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-91: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-46: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-52: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-54: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-55: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-56: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-62: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-64: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-69: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-74: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-80: exit2, `CHECKPOINT SOURCE INVALID reason=report_failed`.
- CP-81: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-82: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-83: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-84: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.

The unmodified duration tripwire covered 34 completed TRX selections: 18 selections contain 175 unlisted >=5s entries; 0 invalid inputs. These are performance advisories, not assertion failures. Preserve the incomplete gate; do not add blanket Slow tags, change the registry or rerun Review solely for advisories (D-2). Heavy fixture work belongs in the measured follow-up. All original logs remain ignored.

```text
CP-67b
SLOW-TEST TRIPWIRE: expanded rows=48
SLOW-TEST TRIPWIRE: 16 unlisted tests >= 5s
8.543s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger  tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger
31.666s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(tool-git, held)
121.049s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(tool-eval, held)
121.245s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(script-eval, held)
6.080s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered  ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered
7.351s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot  changed_file_under_an_output_root_is_still_refused_inside_the_slot
15.888s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(script-git, held)
6.526s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.C1381_Project_read_budget_excludes_startup_delay  C1381_Project_read_budget_excludes_startup_delay
5.567s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.scheduler_reports_an_unreadable_ledger_entry_as_unreadable  scheduler_reports_an_unreadable_ledger_entry_as_unreadable
12.403s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.strict_admission_hash_in_the_manifest_refuses_the_build_before_launch  strict_admission_hash_in_the_manifest_refuses_the_build_before_launch
6.351s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode  shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode
5.425s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch  rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch
7.869s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.stat_without_a_change_time_hashes_each_entry_at_admission  stat_without_a_change_time_hashes_each_entry_at_admission
6.578s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted  entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted
5.482s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it  ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it
6.105s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch  portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch
CP-67c
SLOW-TEST TRIPWIRE: expanded rows=40
SLOW-TEST TRIPWIRE: 5 unlisted tests >= 5s
8.739s  Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.C1573_linux_only_cases_skip_when_stat_is_absent  C1573_linux_only_cases_skip_when_stat_is_absent
5.159s  Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse  C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse
13.844s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_script_stamp_matches_full_digest  C1532_script_stamp_matches_full_digest
22.775s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_script_digest_limits_stalls_and_eof  C1532_script_digest_limits_stalls_and_eof
10.951s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_scripts_are_ascii_and_parse_in_pwsh  C1532_scripts_are_ascii_and_parse_in_pwsh
CP-67d
SLOW-TEST TRIPWIRE: expanded rows=28
SLOW-TEST TRIPWIRE: 5 unlisted tests >= 5s
20.627s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_content_and_receipt_limits_are_distinct  C1532_content_and_receipt_limits_are_distinct
6.764s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_tail_edit_with_restored_mtime_is_tampered  C1532_tail_edit_with_restored_mtime_is_tampered
5.992s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_large_ignored_paths_remain_covered  C1532_large_ignored_paths_remain_covered
23.310s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_second_build_and_retry_admit_large_prior_output  C1532_second_build_and_retry_admit_large_prior_output
5.566s  Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases  namespace_census_matches_compiled_checkpoint_cases
CP-67e
SLOW-TEST TRIPWIRE: expanded rows=62
SLOW-TEST TRIPWIRE: 13 unlisted tests >= 5s
13.698s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build  unevaluable_import_is_refused_before_the_build
39.748s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency  transitive_import_build_preserves_the_tracked_dependency
29.703s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency  conditional_import_build_preserves_the_tracked_dependency
27.864s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency  imported_props_build_preserves_the_tracked_dependency
26.416s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency  directory_build_props_build_preserves_the_tracked_dependency
6.073s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.linux_fifo_receipts_refuse_within_four_seconds  linux_fifo_receipts_refuse_within_four_seconds
7.904s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.symlink_and_directory_receipts_refuse_within_four_seconds  symlink_and_directory_receipts_refuse_within_four_seconds
15.874s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals  malformed_collection_and_string_shapes_return_named_refusals
17.066s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing()
22.130s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing( \t
19.313s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing(null)
25.285s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing(CP-fixture)
50.539s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.malformed_state_shape_by_verb_table_matches_documented_exits  malformed_state_shape_by_verb_table_matches_documented_exits
CP-68
SLOW-TEST TRIPWIRE: expanded rows=215
SLOW-TEST TRIPWIRE: 34 unlisted tests >= 5s
5.041s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.windows_estimate_selects_row_and_total_deadlines  windows_estimate_selects_row_and_total_deadlines
5.060s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.settlement_cancels_two_drivers_and_skips_later_rows  settlement_cancels_two_drivers_and_skips_later_rows
5.858s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.unbound_runs_and_normal_starter_exit_remain_allowed  unbound_runs_and_normal_starter_exit_remain_allowed
6.001s  Antiphon.Tests.Checkpoints.CheckpointTempRootSweepTests.disposed_roots_do_not_consume_the_next_sweep_budget  disposed_roots_do_not_consume_the_next_sweep_budget
10.142s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.canceled_process_does_not_wait_forever_for_inherited_output_pipes  canceled_process_does_not_wait_forever_for_inherited_output_pipes
19.824s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(staged-untracked)
19.741s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(file-on-directory)
18.323s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(directory-on-file)
19.475s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(ignored-descendant)
7.367s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-output-symlink)
7.080s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(compiler-collision)
18.776s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(symlink-on-file)
9.927s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-output-directory)
33.856s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(changed-untracked)
19.806s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(regular-user-move)
16.231s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(deleted-untracked)
50.089s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(clean-build)
18.852s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(deleted-tracked)
18.750s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(modified-tracked)
7.062s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-stamp)
19.658s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(untracked-descendant)
22.812s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported  intermediate_output_path_tampering_is_reported
19.767s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported  generated_target_file_tampering_is_reported
25.093s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported  imported_props_reference_tampering_is_reported
5.399s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified  dirty_tracked_tree_is_refused_and_unmodified
20.913s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero  clean_untracked_output_build_exits_zero
38.852s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test  fresh_checkout_run_executes_a_test
6.219s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files  tracked_snapshot_contains_committed_files
19.809s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed  staged_untracked_file_is_kept_and_indexed
37.048s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed  staged_ignored_file_is_kept_and_indexed
23.800s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported  local_output_property_tampering_is_reported
29.795s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported  transitive_reference_tampering_is_reported
13.468s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt  passing_script_requires_writable_source_receipt
16.834s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed  index_added_file_is_kept_and_indexed
CP-72
SLOW-TEST TRIPWIRE: expanded rows=246
SLOW-TEST TRIPWIRE: 4 unlisted tests >= 5s
7.093s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.AgentTasks_has_project_id_status_index  AgentTasks_has_project_id_status_index
7.096s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.Model_sql_fragments_have_no_carriage_returns  Model_sql_fragments_have_no_carriage_returns
9.645s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.Current_model_matches_migration_snapshot  Current_model_matches_migration_snapshot
15.728s  Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Provider_exec_into_the_runner_names_uid_1654  Provider_exec_into_the_runner_names_uid_1654
CP-73
SLOW-TEST TRIPWIRE: expanded rows=248
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
6.223s  Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests.disposed_roots_do_not_consume_the_next_sweep_budget  disposed_roots_do_not_consume_the_next_sweep_budget
7.389s  Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool  Fresh_context_retains_unproven_tool(malformed)
CP-76
SLOW-TEST TRIPWIRE: expanded rows=46
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
7.677s  Antiphon.Tests.Infrastructure.Resilience.ResilienceCircuitTests.Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay  Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay
CP-78
SLOW-TEST TRIPWIRE: expanded rows=67
SLOW-TEST TRIPWIRE: 20 unlisted tests >= 5s
5.693s  Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests.Spawning_classes_carry_a_class_timeout  Spawning_classes_carry_a_class_timeout
7.257s  Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests.Heredoc_fed_docker_exec_keeps_stdin_open  Heredoc_fed_docker_exec_keeps_stdin_open
10.158s  Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests.Spawn_timeouts_do_not_exceed_ten_minutes  Spawn_timeouts_do_not_exceed_ten_minutes
16.490s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_cleanup_reports_waited  C1364_Wrapper_cleanup_reports_waited
7.107s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_publishes_complete_no_clobber  C1025_Provision_publishes_complete_no_clobber
128.626s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited  C1364_Host_recycle_admission_reports_waited
142.368s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited  C1364_Host_recycle_post_removal_reports_waited
15.079s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_redeploy_reports_waited  C1364_Wrapper_redeploy_reports_waited
23.252s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_retire_volumes_reports_waited  C1364_Wrapper_retire_volumes_reports_waited
26.974s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_cleanup_reports_waited  C1364_Host_cleanup_reports_waited
64.145s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry  C1025_Preflight_is_fresh_for_each_entry
7.856s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_is_bounded_and_reaped  C1025_Transport_is_bounded_and_reaped
119.605s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_requires_complete_current_proof  C1025_Receipt_requires_complete_current_proof
18.622s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_persistence_is_required  C1025_Receipt_persistence_is_required
6.170s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Explicit_tool_phases_never_deploy  C1025_Explicit_tool_phases_never_deploy
108.491s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase  C1025_Preflight_precedes_every_phase
7.927s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Final_recycle_guard_remains  C1025_Final_recycle_guard_remains
8.317s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_canonical_source  C1025_Provision_requires_canonical_source
6.926s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Changed_leaf_cannot_emit_success_proof  C1058_Changed_leaf_cannot_emit_success_proof
9.590s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Metadata_syntax_refuses  C1058_Metadata_syntax_refuses
CP-85
SLOW-TEST TRIPWIRE: expanded rows=2
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
5.841s  Antiphon.Tests.Application.CheckCompactionContinuationTests.Successful_unchanged_pull_confirms_but_failed_pull_does_not  Successful_unchanged_pull_confirms_but_failed_pull_does_not
5.827s  Antiphon.Tests.Application.CheckCompactionContinuationTests.Confirmed_episode_survives_restart_and_incident_pruning  Confirmed_episode_survives_restart_and_incident_pruning
CP-87
SLOW-TEST TRIPWIRE: expanded rows=1
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
5.077s  Antiphon.Tests.Application.CheckCompactionAttentionTests.Open_episode_is_one_attention_row_until_it_is_recovered  Open_episode_is_one_attention_row_until_it_is_recovered
CP-89
SLOW-TEST TRIPWIRE: expanded rows=27
SLOW-TEST TRIPWIRE: 27 unlisted tests >= 5s
32.667s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Another_message_for_the_checked_task_is_not_the_receipt  Another_message_for_the_checked_task_is_not_the_receipt
32.654s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Caller_prompt_at_or_before_baseline_does_not_recover  Caller_prompt_at_or_before_baseline_does_not_recover(older)
32.564s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Partial_caller_prompt_does_not_recover  Partial_caller_prompt_does_not_recover
32.686s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Caller_prompt_at_or_before_baseline_does_not_recover  Caller_prompt_at_or_before_baseline_does_not_recover(at-floor)
35.200s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Nonwinning_interpretation_cannot_claim_request_receipt  Nonwinning_interpretation_cannot_claim_request_receipt
35.192s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Prior_generation_request_cannot_recover_current_episode  Prior_generation_request_cannot_recover_current_episode
35.811s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt  Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt(suppressed)
35.847s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt  Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt(wrong-episode)
37.014s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(missing-interpreter-prompt)
37.051s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(not-sent)
37.052s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(submit-retry)
37.095s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_recovers_without_interpretation_parent  Modern_receipt_recovers_without_interpretation_parent(idle)
37.096s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-source)
37.007s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(sent-without-user-prompt)
37.098s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-correlation)
37.013s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_recovers_without_interpretation_parent  Modern_receipt_recovers_without_interpretation_parent(busy)
37.107s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-caller)
37.109s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-message)
37.110s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(receipt-before-sweep)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-purpose)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-baseline)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(queued-before-flush)
37.113s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(empty-result)
37.120s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Historical_legacy_publication_does_not_block_modern_receipt  Historical_legacy_publication_does_not_block_modern_receipt
37.121s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-origin)
37.237s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(missing-interpreter-end)
38.169s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(insert-failure)
CP-90
SLOW-TEST TRIPWIRE: expanded rows=11
SLOW-TEST TRIPWIRE: 11 unlisted tests >= 5s
11.713s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Crossed_stop_generation_cannot_confirm_exit  Crossed_stop_generation_cannot_confirm_exit
11.698s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Crossed_stop_attempt_cannot_confirm_exit  Crossed_stop_attempt_cannot_confirm_exit
11.880s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Unproven_standing_owner_never_restarts  Unproven_standing_owner_never_restarts
11.916s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Quota_hold_vetoes_stop  Quota_hold_vetoes_stop
12.163s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Authentication_refusal_vetoes_stop  Authentication_refusal_vetoes_stop
12.145s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Human_turn_vetoes_automatic_restart  Human_turn_vetoes_automatic_restart
12.151s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Model_hold_vetoes_stop  Model_hold_vetoes_stop
12.214s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Unsupported_stop_never_falls_back_or_resumes  Unsupported_stop_never_falls_back_or_resumes
12.212s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Provider_hold_vetoes_stop  Provider_hold_vetoes_stop
12.304s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Capacity_hold_vetoes_stop  Capacity_hold_vetoes_stop
12.532s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.One_stop_is_reconciled_without_a_second_launch  One_stop_is_reconciled_without_a_second_launch
CP-46
SLOW-TEST TRIPWIRE: expanded rows=216
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
7.312s  Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution  Disabling_the_probe_does_not_disable_effort_resolution(True)
CP-69
SLOW-TEST TRIPWIRE: expanded rows=196
SLOW-TEST TRIPWIRE: 21 unlisted tests >= 5s
6.475s  Antiphon.Tests.Checkpoints.OutputCleanupTests.deletes_only_this_runs_bin_names  deletes_only_this_runs_bin_names
7.255s  Antiphon.Tests.Checkpoints.OutputCleanupTests.never_deletes_bin_obj_workspace_or_foreign_bin_star  never_deletes_bin_obj_workspace_or_foreign_bin_star
7.349s  Antiphon.Tests.Checkpoints.OutputCleanupTests.red_keeps_unless_clean_on_red  red_keeps_unless_clean_on_red
8.099s  Antiphon.Tests.Checkpoints.OutputCleanupTests.wrapper_refuses_a_tracked_file_under_a_csproj_output_directory  wrapper_refuses_a_tracked_file_under_a_csproj_output_directory
8.937s  Antiphon.Tests.Checkpoints.OutputCleanupTests.evidence_under_antiphon_survives_and_dry_run_says_would_delete  evidence_under_antiphon_survives_and_dry_run_says_would_delete
9.001s  Antiphon.Tests.Checkpoints.PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery  census_reports_unmapped_selection_without_dynamic_discovery
5.070s  Antiphon.Tests.Checkpoints.RerunFilterHostTests.parenthesized_class_filter_runs_both_methods_on_a_real_host  parenthesized_class_filter_runs_both_methods_on_a_real_host
13.183s  Antiphon.Tests.Checkpoints.OutputCleanupTests.build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green  build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green
11.057s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5  c866_legacy_reports_v1_v4_v5
15.498s  Antiphon.Tests.Checkpoints.OutputCleanupTests.hanging_git_is_refused_and_killed_within_the_bound  hanging_git_is_refused_and_killed_within_the_bound
13.674s  Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file  readers_never_see_a_partial_file
24.351s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.extra_frozen_method_is_a_checklist_count_finding  extra_frozen_method_is_a_checklist_count_finding
24.350s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible  c835_eight_pc_risks_are_visible
24.610s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.dropped_frozen_method_is_a_checklist_count_finding  dropped_frozen_method_is_a_checklist_count_finding
24.836s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.class_only_frozen_result_count_has_no_findings  class_only_frozen_result_count_has_no_findings
25.256s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.frozen_checklist_preserves_all_72_obligations  frozen_checklist_preserves_all_72_obligations
27.336s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations  c866_fixed_checks_clear_same_obligations
27.169s  Antiphon.Tests.Checkpoints.PlanCoveragePcTests.c983_driver_aliases_bind_on_the_exit_assertion  c983_driver_aliases_bind_on_the_exit_assertion
57.613s  Antiphon.Tests.Checkpoints.OutputCleanupTests.adopted_build_output_over_the_entry_cap_is_unsafe_tree  adopted_build_output_over_the_entry_cap_is_unsafe_tree
152.327s  Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method  historical_row_timeout_checklist_binds_the_advisory_method
267.716s  Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression  historical_plans_bind_without_suppression
CP-74
SLOW-TEST TRIPWIRE: expanded rows=250
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
19.086s  Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests.State_table_and_session_column_match_the_snapshot  State_table_and_session_column_match_the_snapshot
CP-81
SLOW-TEST TRIPWIRE: expanded rows=250
SLOW-TEST TRIPWIRE: 9 unlisted tests >= 5s
5.126s  Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties  Snapshot_covers_exactly_the_reflected_non_navigation_properties(Antiphon.Server.Domain.Entities.AgentSession)
5.124s  Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties  Snapshot_covers_exactly_the_reflected_non_navigation_properties(Antiphon.Server.Domain.Entities.AgentTask)
8.621s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(optin)
8.440s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(ordinary)
10.064s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(no-inherits)
12.154s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(inherits)
8.770s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G066  C487_G066
8.305s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G065  C487_G065
6.347s  Antiphon.Tests.TestHelpers.TestPhaseSummaryTests.C1514T_MergeAndQuality  C1514T_MergeAndQuality(full-match)
CP-82
SLOW-TEST TRIPWIRE: expanded rows=37
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
9.107s  Antiphon.Tests.TestHelpers.TestPhaseSupervisorTests.C1514T_PlannedManifestAndEnvironment  C1514T_PlannedManifestAndEnvironment
6.027s  Antiphon.Tests.TestHelpers.TestPhaseSupervisorTests.C1514T_ActualAndFailurePaths  C1514T_ActualAndFailurePaths
```

### Exact incomplete selections for the next Code/Debug task

**CP-79, inherited floor 499, incomplete**

```text
/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/*
```

**CP-80, inherited floor 168, incomplete**

```text
/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

Do not rerun either unchanged whole filter until the repeated fixture cost is addressed or an exact, count-preserving method partition plan is reviewed. After the repair commit, build once to a new literal isolated output under the host build-slot gate, import the revised plan and derive its finite execution/admission/wall bounds as documented. Execute its exact repaired partitions through `run`, then `wait` until the exit is not75; keep source frozen, retain all receipts and preserve the original failed/interrupted histories. Native Windows witnesses and the planned-skip report-validator contract require separate scoped work; this Linux task grants neither native qualification nor a validator waiver. No master assertion-red claim was made; prove any future such claim by the same failing method on a scratch origin/master.

### Verification closure before publication

Final plan import accepted 97 executable rows under lease 49e7e931-f5f6-4828-af9d-dc27517b58fb (60s admission /1s held). The pre-existing CP-27/28 default import 75min advisories were preserved; this task did not run those selections. All checkpoint table cells/filters match the imported candidate byte-for-byte; subsequent edits correct prose spacing only. The final executable list replaces one CP-67 aggregate with its five disjoint partitions. All original filters are retained, including the aggregate in history.

The final Linux skip count is 43: original 28 plus 15 new (CP-67e:1, CP-68:1, CP-69:8, CP-74:2, CP-78:2, CP-81:1). CP-81’s additional guard is actual Windows PowerShell 5.1/nightly custody at TestPhaseSummaryTests.cs:98; no Linux-native case was incorrectly skipped. Corrected-floor reruns CP-69/74/81 all exited 0; their earlier roster misses and their costs remain retained.

No production/test repairs were necessary or made. No new assertion-red was found, and no inherited-master-red assertion was claimed. The unresolved defects are scheduling/setup cost in CP-79/80 and the strict report-validator planned-skip contract, not unproven product regressions. Caller authority already covered the Debug and measured reruns; no extra approval was requested.

The 216-row per-test lifecycle/phase ledger is in the full task report: `.antiphon/task-698bf6b9.md`. Every native identity joined; named subphases were absent and remain unattributed rather than fabricated.
