# CARD-1039: activate and prove daily whole-Unit coverage through Windmill

Stage: Plan. Verification design is a **separate TestDesign stage**; this document
is not yet a runnable Code checkpoint manifest. Next: `test-design`.

Source inspected: `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`. Plan task:
`e2c04614-77dd-4b32-92fa-ce4c29c1b624`, branch `feat/card-task-e2c04614`.
The caller lands this plan through normal landing after its branch is pushed;
this delegate does not push to master or alter the canonical checkout.

## Outcome and acceptance

Make the existing Windmill job `u/lndcobra/antiphon_nightly_tests` run daily at
00:30 Europe/London against origin/master, and retain evidence that the complete
eligible Unit inventory ran. Preserve the seven-suite nightly profile and its
independent watchdog qualification requirements. A checked-in schedule, accepted
registration request, manual green, or healthy worker alone is insufficient.

CARD-1021 revision 9 records the operator's decision: further whole-Unit
verification for that card is waived, on condition that daily coverage is
scheduled and timeouts investigated. It does not turn the existing red receipts
green or activate reduced dispatch verification. Do not commission another
CARD-1021 whole-Unit run to satisfy this plan.

Completion requires:

1. Readback of the registered script and enabled nightly schedule, correct content
   and stored revision, cron, timezone, empty args, tag and script target; a second
   apply creates no duplicate or needless revision and preserves enablement.
2. One manual full unattended green followed by a **real subsequent 00:30
   scheduled green**, with the seven suite inventories and a separately auditable
   Unit census. Required failures, skipped required cases, missing expanded rows,
   stale evidence or missing report delivery cannot receive green credit.
3. Measured build, slot wait, discovery, test and cleanup durations; native chunks
   finish within their existing budgets. The run fits the existing London morning
   deadline. Timeouts and stalls have identities and evidence, not assumed causes.
4. CARD-0545 S6 independence, outage/recovery and recipient-readback evidence,
   matching readiness identities, and a named morning triage owner. Record this
   in the existing CARD-0487 qualification artifact family.

Record intermediate outcomes separately: **registered**, **enabled**, **daily
Unit complete**, **nightly green**, and **qualified backstop**. A later non-Unit
failure may leave a complete Unit receipt, but cannot qualify the full nightly.
No change to `InterimVerification:Enabled` or `reducedDispatchPolicy` is in scope.

## Sources and ground truth

Read owners: [testing and build](../../testing-and-build.md#nightly),
[watchdog](../../nightly-watchdog.md), [release gates](../../release-gates.md#registration-operator-run-s5s6),
[orchestration](../../orchestration-loop.md), [HTTP operations](../../ops-http.md),
[project context](../../project-context.md), and the
[CARD-0545 plan/S6](2026-09-17-card-0545-independent-nightly-watchdog-plan.md).
The [CARD-1021 evidence](../../investigations/2026-10-04-card-1021-code-7c7fdeeb.md)
owns its exact tested SHAs, failed names and unedited CHECKPOINT lines.

Read live cards on 2026-10-04: CARD-1039 rev 2; CARD-1021 rev 9; CARD-1040 rev 1;
CARD-0545 rev 5; CARD-0589 rev 13. The Windmill census below is the investigation
record on CARD-1039, not a fresh Windmill census performed by this Plan task.

| Card premise or expectation | What the source or retained evidence actually says | Consequence |
|---|---|---|
| There is an existing daily backstop to activate. | CARD-1039 records a read-only live census at 2026-10-04 15:53:47 UTC: zero matching scripts, schedules or retained jobs. The latest local attempt was a failed September 4 feature-branch run. | Registration and real execution are required; no current backstop credit. |
| The schedule is already enabled. | `scripts/windmill/antiphon-nightly-tests.schedule.json` says `enabled: true`, cron `0 30 0 * * *`, zone `Europe/London`; `register-release-gates.ps1` deliberately creates schedules disabled. | Payload intent is not live state; enable explicitly after manual green and verify by GET. |
| Daily Unit coverage means the nightly is a Unit-only run. | `tests/test-execution-policy.json` requires antiphon, session-runner, pty-host, agents-pty, messaging, client and scripts. `nightly-tests-impl.ps1` launches the full Antiphon test executable when no class chunks exist. | Keep this profile; derive Unit evidence from its actual discovery and execution, without replacing the nightly by a filtered run. |
| A 60-minute invocation budget should cover that executable. | `watchdogs.antiphon` is 3,600,000 ms. No `chunks.antiphon` is configured. The investigation brief reports historical Windows full-assembly durations of 190-270 minutes. | There is a budget/selection mismatch risk, not proof of a Unit timeout. Obtain retained Windows class timings and partition before qualification; never just widen the timeout. |
| CARD-1021's 15 failures establish timeouts. | 4,051 executed, 4,036 passed, 15 failed, 52 skipped; all 15 failed at the base. CARD-1040 records timeout=0, aborted=0, slot wait 0s, build 102.244s, host wall 522.468s, individual failures 0.078-4.694s. | Preserve these red facts; CARD-1040 owns missing-jq/environment repair and actual timeout diagnosis. |
| Native nightly work already participates in CARD-0589. | `scripts/lib/nightly-tests-impl.ps1:383` calls `dotnet build` directly; native execution and discovery also call the owned process helper directly. The testing owner explicitly leaves nightly integration to CARD-0589 Round 2. | Land/reuse the integration prerequisite; one real host slot per driver, no unlisted direct build. |
| Existing chunks are sufficient to prove coverage. | The implementation supports class chunks, membership checks and expanded-UID union checks, but can derive `eligibleClasses` from the chunks themselves. Discovery is normally produced after execution and only when TRX exists. | Admit partitions against independent compiled discovery before execution; maintain final UID union verification. |
| Existing registration tests prove installed-API idempotence. | The script compares a local canonical definition digest to the returned `hash`; the fixture in `test-release-gate.ps1` manufactures a hash from the posted body. Preview sends no token. Any unsuccessful GET currently looks like absence. | Treat the live hash as opaque; distinguish unknown reads from 404 and compare owned content/fields. Fixture success alone is not installed-API evidence. |
| A done CARD-0545 means qualification happened. | CARD-0545 close text explicitly leaves S6 deploy, credentials, reader login, live registration and qualification untouched. | Carry S6 as required operational work; reuse its implementation and exact controls. |
| The declared nightly policy text establishes eligibility. | The nightly profile description mentions OptIn exclusion, but profile-aware execution uses explicit profile exclusions; its exclusions array is empty. | Freeze the compiled census under actual code semantics. Do not silently add exclusions to get green; report any blocking inventory discrepancy to the policy owner. |

Platform reads completed during this Plan using `ANTIPHON_API` and the inherited
task header: `GET /api/runner-defaults` returned revision 2, a global runtime
preference, no kind overrides and no unresolved references; `GET
/api/session-runners` returned eligible Windows and Linux descriptors plus an
unavailable draining descriptor. These observations grant no capacity reservation.
Re-read both before dispatch. Omit `-Runner`; omit `-Platform` for portable work,
or use `-Platform Any` only to remove an inherited pin. Use `-Platform Windows`
only for the native Windows checkpoint/qualification lane below. No fleet
address, SSH identity, execution host or watchdog host is prescribed here.

## Decisions

**D-1 — Reuse the nightly schedule and profile.** Keep 00:30 Europe/London, the
existing job path and seven-suite inventory. This directly meets the operator's
daily-run request and preserves CARD-0487/0545 acceptance. Reject a local
Scheduled Task, a second Unit-only schedule, and changing the nightly to a Unit
filter: each would create another scheduler or weaken existing qualification.
The RC schedule remains disabled unless separately authorized under CARD-0599.

**D-2 — Prove Unit coverage as a subset of the actual run.** Join discovery nodes
with Category=Unit to terminal results by expanded UID, including argument rows,
and bind the census to SHA, policy hash, assembly hash, native run and job. Include
discovered, eligible, excluded-with-reason, executed, passed, failed, skipped,
missing and duplicate counts, plus a sorted UID digest. Required skips are not
passes. Reject a fixed floor of 4,051 or treating the old 52 skips as permission
to skip now; those numbers are historical and platform-specific. Keep full-suite
coverage and report-delivery predicates authoritative for nightly green.

**D-3 — Use measured class chunks, with an independent census.** Reuse the existing
`chunks.antiphon` class mechanism; partition the complete eligible compiled
inventory exactly once. Build once, discover once, then execute chunks serially
from that unchanged output. Disallow unknown/duplicate class assignments and
missing UIDs. Preserve fresh per-chunk TRX and diagnostics and the final union
check. New unassigned cases fail completeness instead of silently disappearing.
Reject deriving the admission inventory from chunk membership. Do not invent a
large static roster or durations in Plan: TestDesign supplies the current roster
and measurement inputs; S3 commits the resulting policy and recomputed hash.

**D-4 — Integrate the existing build-slot library at the nightly driver boundary.**
Prefer `Enter-AntiphonBuildSlot`, `Add-AntiphonMaxCpuCount` and
`Exit-AntiphonBuildSlot` from `scripts/lib/build-slot.ps1`, with one lease held
around each build, discovery or test driver and released in `finally` after its
owned children are joined. This keeps slot waiting outside the child's execution
watchdog. Do not wrap the complete hours-long nightly or take nested leases around
`run-checkpoint.ps1`. Preserve CPU grants, renewal, wait/hold receipts and the
library's existing unreachable/unlimited classifications. Exit 4 means not run;
no unleased retry. An unleased run is diagnostic and does not discharge the
activation gate. CARD-0589 owns broker behaviour; this work owns its nightly
consumer only after the caller checks for an overlapping Round 2 implementation.
The gate library requires PowerShell 7: load it in the existing `pwsh` test-driver
process, retaining the ASCII/PowerShell 5.1 bootstrap contract. A direct unsupported
invocation must fail clearly or use the established hop, never bypass admission.

**D-5 — Retain limits and measure different clocks separately.** The native
Antiphon cap stays 60 minutes per invocation; other suite caps and the 08:00 London
readiness deadline remain unchanged. Partition using observed class timings with
headroom for native startup/teardown, discovery and slot contention. A chunk with
no defensible timing evidence is not admitted to unattended qualification. Record
slot queue time, child elapsed time, latest test activity, PID/process-tree exit
and cleanup, distinguishing timeout, assertion failure, infrastructure refusal
and stalled progress. Reject a broad timeout increase, retry or weaker assertion.
CARD-1041 owns enforcement of the documented checkpoint row ceiling; this plan's
rows must remain bounded without assuming that enforcement is already installed.

**D-6 — Reconcile registration using observable installed state.** The returned
Windmill hash is a revision identifier, retained for parent/concurrency checks;
equality comes from the owned script content and fields, with schedule target,
cron, timezone, tag, args and enabled state verified independently. Only confirmed
404 is absence. Authentication, transport and other failed reads are unknown and
must prevent an Apply from creating objects. Keep unauthenticated preview's
zero-token/zero-write contract, but label failed reads unknown rather than absent.
Do not assume a no-token preview is a deployment census. Preserve disabled create,
single explicit enable and existing drift refusal. Validate these behaviours with
a stateful API fixture whose revision hash is unrelated to our digest, then
read back the real installation during qualification.

**D-7 — Preserve producer and trigger identity.** The wrapper keeps schedule
provenance from Windmill's own schedule context; a manual API invocation remains
manual. SHA/ref/policy/script identities must match through native output,
Windmill result, state files, monitor and qualification receipt. Verify the
wrapper's bootstrap script comes from the intended deployed revision as well as
the isolated clone it launches. No feature branch, `-NoReport`, partial selection
or manually supplied trigger earns scheduled-green credit.

**D-8 — Keep activation and reduced-verification policy separate.** Deployment
profiles, credentials, independent watchdog location, authorized recipient and
reader login remain the operator's CARD-0545 S6 custody. Prepare reviewable code,
definitions and the runbook first. The Plan/Code/Review delegates perform no live
Windmill writes, message sends or reader login. Qualification reuses F-1..F-6 and
their existing IDs; it does not fake a receipt from a send acknowledgement.
Even after this card qualifies the nightly, CARD-0544 activation is a separate
commissioned action.

## Dependencies and stop conditions

| Owner | Required evidence / boundary | Work that may proceed meanwhile |
|---|---|---|
| CARD-1040 / CARD-0927 activation | Correct jq/tool environment and retained exact failing-method comparison. Actual timeout cases carry their own run/host/source/timing evidence. | Offline plan, harness and gate work. Do not repair `c590-remote.sh` or images in CARD-1039. Missing tools or still-required failing/skipped Unit cases block green qualification. |
| CARD-0589 Round 2 | Nightly build, native discovery and test drivers actually use the existing host gate, including wait, CPU cap and release evidence. | Review or implement only the missing consumer slice S1 after checking ownership; no broker redesign. |
| CARD-0545 S6 / CARD-0487 | Independent supervisor, credential references, recipient authorization/login, native manual plus scheduled greens, outage/recovery readback, named triage owner. | All repository changes and offline tests. A done infrastructure card does not waive this dependency. |
| CARD-0599 | Correct registration and schedule provenance, RC/master coordination and profile identity. | S4 hardening and readback preparation. No RC enable or publication. |
| CARD-1041 | Separate documented-vs-enforced row-ceiling discrepancy. | Keep this task's checkpoint rows narrow; do not change the checkpoint timeout policy here. |

If retained Windows evidence is unavailable, commission a bounded native timing
probe of the specific unmeasured class groups on the same clean build. Record
started/finished cases, elapsed/CPU/memory/slot time and process exits. A genuine
stall returns its exact method or class to CARD-1040/its linked timeout card; do
not use another monolithic full assembly run as the diagnostic. If a single
class exceeds its budget, stop partition admission and send that evidence to the
timeout owner; do not add method partitioning or raise limits incidentally here.

## Implementation slices

Code budgets below include authoring and their ordinary targeted checks. Freeze
each slice, commit and push before its checkpoint. These slices share nightly
files and therefore execute in order, not concurrently. Split an over-budget
slice at its named behaviour boundary before dispatch; never silently omit its
verification. Review and land each completed slice under the normal workflow.

| Slice / budget | Files | Concrete change and completion check |
|---|---|---|
| S1: gate consumer, 45-60 min | `scripts/lib/nightly-tests-impl.ps1`, its imports in `scripts/nightly-tests.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyBuildSlotTests.cs` | Apply D-4 at all driver callsites, including discovery, client and script drivers. Controlled broker/child evidence proves acquire-before-start, grant CPU cap, no start on slot timeout, and release after success/failure/timeout. Audit nested script-owned gates; each driver has exactly one owner. Reuse a landed CARD-0589 consumer if equivalent. |
| S2: discovery and partition admission, 45-60 min | `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyChunkAdmissionTests.cs` | Move independent discovery before chunk execution; bind it to the built output. Validate D-3 against it, reuse it in final union accounting, retain serial native execution. Exercise missing class, added argument, overlapping filters and later-chunk execution after an earlier ordinary red. Do not claim a helper-only union test proves the production loop. |
| S3a: measured chunk configuration, 30-45 min | `tests/test-execution-policy.json`, `scripts/test-nightly-tests.ps1`, `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs`; measured roster in the qualification Markdown | Commit class groups derived from the current compiled inventory and retained/measured Windows timings; recompute policyHash through the existing helper. No suite removal, new exclusion, timeout change or manual/RC profile shortcut. Current policy/census/hash contract tests must agree. |
| S3b: Unit and duration receipt, 40-60 min | `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyUnitReceiptTests.cs` | Add D-2 accounting to summary.json and concise human report evidence without changing existing final result identity or green predicates. Preserve independent full-suite verdict. Prove missing/failed/skipped/duplicate Unit rows cannot count as complete-green and non-Unit red stays nightly red. Record timings needed by D-5. |
| S4: registration readback, 40-60 min | `scripts/register-release-gates.ps1`, `scripts/test-release-gate.ps1`, `tests/Antiphon.Tests/Scripts/ReleaseGateRegistrationTests.cs`; `scripts/lib/release-gate.ps1` only if the transport needs a bounded read fix | Implement D-6 against an opaque-hash fixture. Preserve existing preview, explicit enable, schedule provenance and concurrent drift behaviour. Prevent unknown reads from writes; verify stored fields rather than just write acceptance. Existing front door remains the only registration path. |
| S5: qualification runbook, 30 min | `docs/testing-and-build.md`, `scripts/windmill/README.md`, `docs/nightly-watchdog.md` only where ownership needs clarification; `docs/investigations/<date>-card-0487-nightly-qualification.md` | Document gate/partition/receipt semantics, commands below and a pending/completed evidence matrix. Update absent-registration statements only with measured live evidence. Preserve ASCII-only daemon scripts and Windows backslash configuration conventions. Documentation-only slice needs link/diff checks, not another suite. |

S3b's JSON is ignored runtime evidence, not a committed generated payload. The
qualification Markdown records source SHA, compact counts, identities and paths
to retained originals; TRX, logs, JSON exports and checkpoint output remain in
their ignored/external evidence stores. No force-add or relocation workaround.

## Verification handoff to TestDesign

Do not send this plan directly to Code. TestDesign adds the required
`## Verification design`, exact V/R/PC mappings and an importer-valid
`### Checkpoints` closed list with integer execution floors derived from real
argument-expanded methods, separate duration estimates and explicit lane names.
An assertion count inside a PowerShell harness is not a TUnit execution count.
Use one method-scoped positive control per changed behaviour; no old stub
assertions such as unconditional `Assert-C487 -Cond $true` count as proof.

Candidate checkpoint groups (selection contract, **not** a frozen runnable
manifest):

| Checkpoint | Slice | Lane | Files/tests and required oracle |
|---|---|---|---|
| CP-1 | S1 | Portable offline; no platform pin | New `NightlyBuildSlotTests` plus exact affected `BuildSlotScriptTests` methods. Real owned child with a loopback broker or controlled existing slot seam; success, refusal, timeout and cleanup/lease paths. No production runner. |
| CP-2 | S2 | Portable offline; no platform pin | New `NightlyChunkAdmissionTests`; existing `test-nightly-tests.ps1` G037/G038/G051/G144 production-path cases, with non-tautological missing/duplicate/expanded-row and literal-filter assertions. |
| CP-3 | S3a | Native Windows census/timing | Current compiled discovery and exact configured class groups, matching build SHA/digest and independently enumerated expanded UIDs. Resolve class durations before admitting groups; this is not an all-assembly test checkpoint. |
| CP-4 | S3b | Portable offline; no platform pin | New `NightlyUnitReceiptTests`, `ReleaseGatePolicyTests.C599_MetadataParsers`, `C599_EligibilityCensus`, `C599_ExpandedCoverage`. Unit subset, explicit exclusions, required skips, unknown/stale/missing records and full-nightly independence. |
| CP-5 | S4 | Portable offline; no platform pin | `ReleaseGateRegistrationTests.C599_Preview`, `C599_ApplyReadback`, `C599_Concurrency`, `C599_ScheduleProvenance`, plus new exact opaque-hash/failed-read/field-readback cases. Stateful stored objects, not just HTTP call counts. |
| CP-6 | all executable slices | Native Windows integration | Narrow end-to-end nightly fixture from owned child to summary/final JSON: slot waiting, real filter argv, TRX/discovery, child failure/timeout and process exit. Select only the frozen fixture methods and relevant `NightlyScriptsTests` ASCII/clone guard methods. |

TestDesign must inspect existing watchdog/readiness regressions before selecting
only those affected by receipt changes: `NightlyVerificationContractTests`
`C544_CoverageRequired`, `C544_GreenRequired`, `C544_ReportReceiptRequired`, and the
C545 result-line/job-result/readiness cases. Do not rerun all CARD-0545 PCs for
this card or relabel their pending disposition.

Ordinary rows use the checkpoint tool once per committed slice group, then wait
until terminal exit (not 75). The tool owns its row gates; any separate bootstrap
build or test driver uses `scripts/build-slot.ps1` and is listed with its reason.
Rows use isolated `bin-c1039-<slice>/` outputs, forward slash, source SHA validation
and cleanup after all owned children exit. No source edits during runs. Full
native nightly qualification uses its dedicated clone's normal output, as the
existing producer contract requires; never point it at a worktree/daemon bin.

Budget each Code dispatch at 30-60 minutes with narrow filters and one isolated
build per slice group. The real nightly qualification is an explicit operational
run with its measured duration and due-day boundary, not hidden inside a Code
budget or checkpoint estimate. No additional whole-Unit CARD-1021 checkpoint.

## Operational activation and evidence checkpoints

These checkpoints are owned by the explicitly commissioned qualification caller
and the operator for custody/live notices, after the code is reviewed and landed.
They are not automated Code manifest rows. Resolve all profile values from the
existing untracked deployment profiles; do not copy a fleet location into a plan
or choose a new recipient. The commands below use caller-bound profile variables.

| ID / lane | Action | Required retained evidence and stop gate |
|---|---|---|
| Q-0 / native Windows + independent watchdog | Verify deployed bootstrap SHA, isolated clone ownership, SDK/runtime, Docker, jq/tool dependencies, output disk, slot endpoint and worker routing; admit S3a inventory/timings. Complete CARD-0545 S6 profile/reader prerequisites. | Sanitized versions/identities and slot test result; actual host/job/SSH outer limits must cover measured native execution plus queue/cleanup. Unknown credentials, missing tools, unexplained required exclusions or insufficient limits block qualification, not Plan completion. |
| Q-1 / installed Windmill API | Run front-door preview, then authorized Apply; GET each stored script/schedule, reapply and GET again. | Before/after content digest and opaque hash; correct cron/zone/tag/args/target; created-disabled state; no second-apply writes or duplicates. The current front door registers nightly, readiness **and disabled RC**; review that full diff, never enable RC here. Failed/unauthorized reads stop writes. |
| Q-2 / native Windows execution via Windmill | Invoke the nightly path manually with the nightly profile and no suite/ref override; await owned job completion. | Job id/result, native run id, bootstrap and tested SHA/ref, policy/assembly/script hashes, seven-suite inventories, Unit UID census/digest, durations, logs/TRX/discovery and report receipt. Manual provenance must remain manual. Red blocks Q-3; target failures rather than repeating the entire battery during diagnosis. |
| Q-3 / installed Windmill API + readiness | Explicitly enable only nightly and readiness after Q-2 and the watchdog/readiness prerequisite is usable. GET each schedule and the next due time. | Exact 00:30 London nightly and existing half-hour readiness; RC still disabled. Store independent watchdog instance/supervisor/snapshot and correct readiness config identities. Expected pending-scheduled-green readiness is not misreported as qualified. |
| Q-4 / real daily scheduled native lane | Observe the next actual 00:30 firing; await completion and inspect Windmill result, native state and monitor. | Scheduled job context, matching native run and local due date, same complete evidence as Q-2; `last-complete-green.json` advances only on true scheduled master green. No manually invented scheduled trigger. Complete by the existing morning deadline or retain a failed/pending qualification. |
| Q-5 / independent watchdog qualification | Carry CARD-0545 S6 F-1..F-6, production notice and recipient readback forward, using authorized isolated qualification targets. | Stable outage/notification/attempt IDs, complete recipient body match and receipt, correlated recovery, persistence/crash-cut evidence, heartbeat identity and named morning operator. Sender acceptance alone is not received. Never disable shared services to inject failure. |
| Q-6 / artifact and acceptance | Publish the qualification Markdown and then the trusted receipt only after all required rows pass. | Link C/O/L, Review and any SourceLanding companion/disposition, Q-0..Q-5 evidence and excluded inventory reasons. Run the evidence diff guard over the entire committed task range. CARD-0544 remains disabled until separately commissioned. |

Front door, in this order and only in the qualified caller's environment:

```powershell
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile $releaseGatesProfile
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile $releaseGatesProfile -Apply
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile $releaseGatesProfile -Apply
# Q-2 manual green and watchdog/readiness prerequisites precede these enables.
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile $releaseGatesProfile -Apply -EnableSchedule readiness
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile $releaseGatesProfile -Apply -EnableSchedule nightly
```

Use the installed API's existing `jobs/run/p/u/lndcobra/antiphon_nightly_tests`
front door for Q-2; credentials go through the approved file-backed path and
never command text, logs or this artifact. Preserve registration's readback
receipts; success output alone is insufficient. The next scheduled green must
come from the scheduler, not another invocation of this command.

If activation fails, retain the attempted job and evidence; explicitly disable
only a newly enabled affected schedule through the installed schedule API and
verify disabled state. Keep the watchdog reporting the outage, preserve previous
last-green evidence and leave reduced verification inactive. Do not delete jobs,
state, the clone or credentials as rollback. The qualification owner records
which exact states changed and which required checkpoint remains pending.

## Plan-stage validation and handoff

This task performed repository and authenticated read-only Antiphon HTTP/card
inspection. It ran no build, test, Windmill mutation or live notice. The historical
CARD-1021 test counts above are attributed evidence, not results from this task.
The card's premise is confirmed: registration is absent in the investigation and
the checked-in integration has concrete admission gaps. No operator decision is
needed to write this plan; deployment custody is handled at Q-0, after concrete
reviewable changes exist.

Next TestDesign must freeze the checkpoint selections/counts, build paths,
timing inputs and one PC per changed behaviour; verify profile eligibility and
the registration fixture against the inspected production path. Preserve the
CARD-1021 waiver and CARD-1040/1041 scope boundaries. Do not start Code with the
candidate checkpoint table above.


## Verification design

TestDesign task 68138e50, inspected source
ff57123014a952d522f8b896bb38780c73e1095b. This section is appended; the fix design
above is unchanged.

**Disposition: return to Plan; do not dispatch Code from this manifest.** The
offline selections below are concrete, but the full verification design fails
admission at five seams listed below. In particular, a successful report process
currently substitutes for recipient evidence. Calling that path qualified would
violate both the acceptance criteria and the delivery-verification owner.

### Inspection

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| scripts/lib/nightly-tests-impl.ps1: all function bodies; scripts/nightly-tests.ps1; Start-NightlyProcess in nightly-common.ps1 | Actual driver callsites, cleanup, chunk loop, summary -> V-1/V-2/V-3; native custody gap B-2 |
| scripts/lib/nightly-coverage.ps1: all function bodies | Discovery versions/metadata, TRX definitions, UID joins, freshness, class membership, union -> V-2/V-3 and R-2 |
| scripts/test-nightly-tests.ps1: all cases and New-Efx/Invoke-E; scripts/lib/c487-harness.ps1: all helpers | G037/G038 are helper checks; G039/G040/G144 reach the entry via fabricated process results; G051's first assertion is literally true -> replace that claim with V-2 production-loop evidence |
| NightlyScriptsTests.cs; ReleaseGatePolicyTests.cs; ReleaseGateRegistrationTests.cs; Scripts/ScriptHarness.cs | Existing executed-method counts and harness result checking -> R-2/R-4/R-5; nearest fixtures for the proposed classes |
| BuildSlotScriptTests.cs; test-build-slot.ps1 fixture launch/wait/order helpers and C589 lease/CPU/failure/timeout/unreachable/renew bodies; C800 literal-argv body; scripts/lib/build-slot.ps1 | Real library semantics, private endpoint, command recorder, renewer -> V-1/R-1; neither the shim nor wrapper tests prove nightly uses the gate |
| BuildSlotEndToEndTests.cs, budget-one and killed-holder tests plus its child launcher | Real loopback broker example; separate test assembly. Excluded from ordinary reruns because CARD-0589 owns the broker; use its pattern for the nightly consumer, not a claim that these tests cover nightly |
| register-release-gates.ps1: full body; Invoke-ReleaseGateWindmill; test-release-gate.ps1 New-C599Policy, policy/metadata/census/expanded cases, New-C599WindmillFx and all registration cases | Opaque revision, missing/unknown, accepted/stored, drift, enabled/disabled -> V-4/R-4. Fixture stores only a subset of posted fields today |
| NightlyVerificationContractTests.cs; C545 ResultLine/JobResultFetch wrappers; their PowerShell case bodies, Invoke-C545ResultLineRun, New-RunFx and Test-C544FlagRequired | Existing flags and identity propagation -> R-3. ResultLine fabricates reportDelivered=true; it cannot prove report receipt |
| nightly-run-impl.ps1 complete-green/state writer, report phase, returned result record; nightly-report.ps1 Finish-Report and green/red write paths; test-nightly-report.ps1 summary/store helpers and T7 | Exit-zero fallback and green/no-card no-op -> B-3. Reporter initializes ReportDelivered=false and never sets it true; the process caller does not consume that object |
| C545World setup/restart; C545_ReaderFirstRetry and C545_HeldReaderNoResend bodies; CARD-0545 delivery inventory and F-1..F-6 | Separate watchdog ledger/recipient pipeline; carry its live obligation, do not count it as the nightly report's receipt |
| scripts/fixtures/nightly/c487-probe/Probe.cs and Probe.csproj; CARD-0487 discovery report | Nearest native fixture: 9 discovered/8 default executed at historical SHA, with argument/data/inherited/partial/OptIn rows. No Unit category and no native child-custody sentinel -> B-1/B-2 |
| tests/test-execution-policy.json; Get-NightlyPolicyHash, Get-NightlySafeChildEnvironment and profile exclusion/disposition helpers | Seven suites, no antiphon chunks, 3,600,000 ms cap, inactive reduced policy; nightly ProfileAware=false -> V-3 and B-1 |
| CARD-1021 Code evidence; CARD-0474 full-category timing report | Linux Unit facts and incomplete old Windows timings -> B-1. Neither supplies a current compiled Windows inventory or complete class timing map |

Required owners read for these boundaries: testing-and-build (manifest, slots,
nightly, delivery), nightly-watchdog, release-gates registration, project-context
conventions, and orchestration's stage/landing contracts. No session-runtime or
queue implementation is being changed by this documentation task.

**B-1: Windows census and timing are unavailable in this checkout.** The brief
explicitly says the desktop checkout is unreachable. The locally retained
CARD-0474 report is an interrupted, partial Windows run at 0eff2a32: about 87 of
320 remaining classes were reached; fifteen named land classes were not reached.
Its case-duration sums are not current class wall-clock budgets. CARD-1021's
4,051 executed/15 failed/52 skipped is a Linux Unit receipt, not a Windows census.
The old nine-node probe is not the repository inventory. No current Windows
count, roster, UID digest or chunk duration is frozen by this task.

Plan must commission/retrieve a clean Windows compiled discovery and retained
per-class timing receipts, with source SHA, policy hash, assembly hash, exact
selection, expanded UIDs, class wall time, discovery/startup/cleanup and slot wait.
Join those inputs before admitting S3a. For missing timings, commission only the
specific unmeasured classes with bounded filters after discovery; never use a
whole-Unit or full-assembly diagnostic. A class over its existing invocation
budget goes to CARD-1040. No invented count or 4,051 floor is admissible.

The eligibility premise also needs an explicit correction in the Plan stage:
Invoke-AntiphonNightlyTests sets profileAware to (Profile != 'nightly').
Test-NightlyDiscoveryExcluded therefore still excludes OptIn/Explicit in the
nightly lane. Freeze that actual behavior; changing it would be a separate policy
change. The policy description is consistent with this implementation. The source
table above the verification section is not evidence for the contrary claim.

**B-2: native fixture and cleanup evidence need a planned seam.**
Invoke-NightlyOwnedProcess calls taskkill.exe on timeout, ignores the cleanup
wait's result, and returns ChildrenExited=true unconditionally. The StartProcess
fixture bypasses that entire body. Neither a true return value nor G050's literal
true assertion proves child exit before slot release. The existing probe also
cannot be selected by the production loop without its hard-coded native path or
the broad StartProcess replacement. Plan must specify a fixture-owned executable
resolution seam/staging arrangement, actual descendant/output-drain observation,
and a Windows row that runs the production launcher. It must include the
PowerShell 5.1 bootstrap -> PowerShell 7 driver boundary; merely dot-sourcing a
#requires-Version-7 library inside a 5.1 entry is not that proof.

**B-3: report receipt and recovery have no verifiable production contract.**
nightly-run-impl.ps1 lines 544-548 accept reporter exit zero when summary has no
reportDelivered property. nightly-report.ps1 returns zero for green/no-open-card
without delivering anything (T7 explicitly requires zero HTTP writes). There is
no correlated report receipt consumed here and no durable nightly-report
delivery/recovery queue in this path. C545_ResultLine supplies a synthetic true
flag before launching the fake reporter. Its success cannot close this gap.
Plan must name the report recipient, durable identity, persistence/recovery
boundary, and receipt reader before TestDesign can finish PC-54/55. Do not
silently relabel watchdog outage receipt as nightly report receipt.

### Delivery inventory

The changed scheduling/producer path and the proposed richer report traverse the
following boundaries. All receipt claims must join the same due day, job,
native run, source/policy/script identities and, where relevant, the whole body.

| Path | Producer -> destination | Persistence and recovery | Observable recipient evidence |
|---|---|---|---|
| DL-1 scheduled execution | Windmill schedule -> real Windmill queue -> desktop-tag worker -> native bootstrap/clone | Schedule/script revisions and job id persist in Windmill; native state persists run id and due date. Q-1 reconciles writes; Q-2/Q-4 must follow job -> native completion. Worker unavailable/enqueue refusal, death before native launch, and death after native completion before result persistence must remain incomplete until recovered evidence joins | Native execution evidence plus retrieved completed job result with matching native run and due day. Registration, enqueue response, running event or job success boolean alone is insufficient |
| DL-2 Unit/duration evidence | Native children -> production suite loop -> summary.json -> report builder/final result -> readiness reader | Fresh per-invocation artifacts, build identity, summary/state write and final stdout. Missing/partial write or missing result is incomplete; retain attempt evidence and recover through the owning run, never reuse a previous green under a new identity | V-2/V-3 assert persisted summary contents, R-3 asserts final result/consumer flags. These prove local data propagation only, not human receipt |
| DL-3 nightly report | summary/report builder -> Antiphon board card/content/discussion, or current no-op green path | No durable report intent, queue recovery or correlated recipient receipt is implemented in the inspected path; report process exit is the current fallback | B-3: no acceptable producer-to-recipient proof. Plan must select and expose the intended recipient readback. A board write acknowledgement and ReportDelivered flag cannot discharge it |
| DL-4 watchdog failure/recovery/qualification notices, retained | Evaluator -> SQLite Ledger intent/attempt -> Telegram transport -> recipient reader -> receipt importer | CARD-0545 outageId/nid/attempt, whole-body hash, authorized peer; restart reuses ledger, reader-first retry; distinct recovery nid links failure nid | Carry Q-5/F-1..F-6: complete recipient body plus imported receipt, held reader and already eligible reader, each intent/send/accept/reader crash cut. Existing C545World is an offline substitute only |

DL-1's real-queue qualification must include a ready worker and a queued job
whose worker becomes eligible later, then the recovery cuts above. Observe the
recipient native process's complete result and artifacts, not just queue depth.
CARD-0545 F-2 covers a queued worker outage but does not prove every new native
handoff; Plan must assign any missing native recovery checks explicitly.

For DL-3, require a producer-to-recipient test through the chosen **real queue**:
busy recipient, already eligible recipient, persist-before-enqueue, enqueue
failure, commit-before-wake, send-before-ack, receipt-before-final-state cuts.
Each cut must fire and restart over the same durable identity. If the chosen
destination is a session, receipt is the matching complete UserPrompt transcript;
no session is currently selected, so inventing such an assertion here would be
misleading. If the destination remains the board, define an independent stored
whole-report readback including its run identity and the green/no-card case.
These are required design inputs, not permission to send live messages.

Substitutes: C487 StartProcess can prove loop decisions and arguments, not native
process exit, quoting, queueing or timing. Golden TRX/diagnostics can prove parser
behavior, not current compiled discovery. The stateful Windmill fixture can prove
reconciliation/retry logic, not installed API behavior or real scheduling.
C545World uses the real SQLite ledger but fake transport/chat/reader; it proves
offline persistence and matching, not Telegram receipt or independent deployment.
Only Q-0..Q-6 supply the corresponding operational evidence. None of the offline
rows may be credited as daily Unit complete, scheduled green or qualified.

### Proves it works now

These are executable test specifications for the repository slices, not claims
of tests run in this TestDesign task. New methods are deliberately
non-parameterized TUnit methods; internal matrix rows do not increase Min.

- V-1: every nightly driver obeys admission and custody | production entry plus
  real build-slot library and controlled broker/owned children |
  NightlyBuildSlotTests' seven C1039 methods in CP-1 | ordered grant/start/exit/
  drain/release trace, correct CPU argv and propagated failure. Exercise eight
  driver sites independently, busy and memory-floor timeout, granted/unlimited/
  unleased classification, pid/renew leases, exception and child timeout.
  No production runner; fixture child environment explicitly supplies its private
  slot endpoint. Unleased is diagnostic and earns no Q-0 qualification credit.
- V-2: independent discovery admits the partition and final expanded UID union |
  real Invoke-AntiphonNightlyTests loop with controlled I/O |
  NightlyChunkAdmissionTests' eight C1039 methods in CP-3 | discovery once before
  execution, no execution after invalid admission, complete later-chunk evidence
  after ordinary red, and exact missing/duplicate UID refusal.
- V-3: honest Unit and duration receipt | production summary writer and coverage
  helpers, read persisted summary back independently |
  NightlyUnitReceiptTests' seven C1039 methods in CP-4 | exact counts/digest,
  explicit exclusions, unchanged global verdict, separate phase clocks.
- V-4: reconcile the installed-state model | production registration entry and
  stateful stored API fixture | six new C1039 registration methods plus four
  inspected C599 methods in CP-7 | opaque revisions, no writes on unknown initial
  reads, drift refusal, explicit enable readback and idempotent recovery.
- V-5: native integration and S3a measured partition | native Windows production
  launcher, independent compiled census and class timings | **not admitted:
  B-1/B-2**. This obligation has no executable checkpoint row yet.
- V-6: delivered nightly report and restart recovery | producer through real
  queue to complete matching recipient evidence | **not admitted: B-3**.
  Existing report/flag tests cannot satisfy this obligation.

**Fixture construction and boundary combinations.**

V-1 uses a private clone/log/coordination root and saved/restored environment.
The library may be shimmed at HTTP only; the gate decision and nightly consumer
cannot be replaced. Each admitted child writes its actual PID, exact argv,
start/exit markers and output-drain sentinel. Timeout tests distinguish never
started due to slot timeout from started then killed by the child watchdog.
Hold cleanup with a barrier, assert no release, then release it. Finally joins
every fixture child. Do not inherit ScriptHarness's process cleanup as proof:
its timeout currently does not kill/join the child before deleting results.
The native truth of these observations remains B-2, not a fake ChildrenExited bit.

V-2's independent discovery contains A.Rows(1), A.Rows(2), inherited/partial cases
and B.Plain; chunk configuration is a separate input. Test no chunks (one full
invocation), correct A/B partition, missing B, duplicate A, unknown Z, empty
assignment, added argument row, A versus AExtra prefix overlap, and duplicate
UID across two chunks. With A ordinary-red, B must still execute; with A cleanup
unresolved, B must not start. Test discovery nonzero, timeout, empty, missing,
unsupported version and malformed record independently. Hash the built DLL
before discovery and before execution; changing it between the two must refuse.
Keep production TRX-basename/results-directory arguments pinned (G144); do not
reuse the current fixture's fabricated identical TRX id and diagnostic UID as the
only positive example. G037/G038 are useful helper boundaries, but neither proves
entry-loop admission.

V-3 uses five discovered Unit UIDs (three eligible, two explicit exclusions) and
one non-Unit UID. Category is metadata, not a name substring; include mixed
class/method metadata and inherited Unit metadata. Three passed eligible rows
give discovered=5, eligible=3, excluded=2, executed=3, passed=3, failed/skipped/
missing/duplicates=0. Then alter one dimension at a time: missing expanded row,
Failed, Skipped, NotExecuted, InProgress, unknown state, duplicate within chunk,
duplicate across chunks, unknown UID, zero eligible, and non-Unit failure.
Count terminal Failed/Skipped facts separately; neither earns complete-green.
Use unrelated TRX GUIDs and diagnostic expanded UIDs, preserving independent
TRX multiplicity/outcome cross-check. Exclusion reasons come from actual nightly
category semantics; RC declared exclusions keep their existing behavior. Permuting
UID order preserves digest; replacing one UID at the same count changes it.
Independently mismatch SHA, ref, policy, assembly, native run, job and script
identities, plus stale timestamp/wrong run directory. No Cartesian product across
all corruptions is needed: independent single corruptions locate each guard;
missing+red, duplicate+missing and Unit-green+non-Unit-red are required paired
cases because aggregation can mask one with the other.

V-4 expands New-C599WindmillFx to store every owned field and an independent
monotonic opaque revision. Persist fixture state to disk, dispose/reopen it on
restart, and count script revisions as well as object paths. Script fields:
path/content/language/tag/schema/summary/description; parent_hash is concurrency
input, not a desired stored field. Schedule fields:
path/script_path/schedule/timezone/tag/args/is_flow/enabled. Corrupt every field
independently at readback, including a matching content string with wrong tag.
Exercise first/second/third definition unknown reads; distinguish 404 from
401/403/503/transport and malformed 200. Initial unknown census prevents writes
before applying any definition. Preview remains zero-token/zero-write and labels
unknown instead of missing. Reapply matches objects, keeps enabled state and
creates no revision. At script-create, schedule-create and explicit enable,
exercise failure-before-write, commit-with-lost-response, and readback failure,
then restart and read the stored result. Concurrent revision drift must reject
with parent_hash; concurrent schedule drift remains untouched. Ready and
temporarily unavailable API states are reconciliation tests, not queue-delivery
tests.

### Guards the regression

- R-1: build-slot semantics remain intact | CP-2:
  BuildSlotScriptTests.C589_WrapperRunsUnderLease,
  C589_WrapperMaxCpuCountRules, C589_WrapperTimeout,
  C800_WrapperPassesWildcardArgvLiterally. Require the existing named PASS
  inventories, literal argv and exit 4 with no command.
- R-2: pinned parser/profile/census contracts | CP-5: all five
  ReleaseGatePolicyTests methods. Require stale-hash refusal, unchanged nightly/RC
  suite distinction, metadata preservation, expanded missing-row refusal and
  declared exclusion reason/owner checks. This is five TUnit results, not the sum
  of its PowerShell assertions.
- R-3: enriched summary must not relax readiness or change completion identity |
  CP-6: NightlyVerificationContractTests.C544_ScheduledIdentity,
  C544_CoverageRequired, C544_GreenRequired, C544_ReportReceiptRequired,
  C545_ResultLine, C545_JobResultFetch. Decisive assertions are manual/crossed-run
  refusal; each false flag is unready; final JSON equals native state; missing/
  unfetchable result remains unknown. ResultLine remains a propagation test only.
- R-4: preview, disabled create, enable selection, drift and wrapper provenance |
  CP-7: existing ReleaseGateRegistrationTests.C599_Preview,
  C599_ApplyReadback, C599_Concurrency, C599_ScheduleProvenance, after expanding the
  fixture without weakening their named assertions. Source-text provenance
  checks are a regression pin, not proof of a real scheduled job.
- R-5: bootstrap safety | CP-8: NightlyScriptsTests.The_three_scripts_are_ascii_only
  and Shared_tree_WhatIf_exits_3_naming_the_guard. Require ASCII bytes and exit 3
  naming AllowSharedTree before WhatIf output. Windows 5.1 execution remains B-2.

### Guard inventory

Each row is a distinct independently bypassable guard. The eight driver
callsites have separate PCs even though one method exercises them. Unchanged
watchdog internals retain their CARD-0545 guard/PC IDs and are excluded from this
card's mutation battery; their operational acceptance is still Q-5.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | S1/D-4 npm ci admission | PC-1 |
| G-2 | S1/D-4 npm build admission | PC-2 |
| G-3 | S1/D-4 lint admission | PC-3 |
| G-4 | S1/D-4 dotnet build admission | PC-4 |
| G-5 | S1/D-4 discovery admission | PC-5 |
| G-6 | S1/D-4 native execution admission | PC-6 |
| G-7 | S1/D-4 client test admission | PC-7 |
| G-8 | S1/D-4 unattended script admission | PC-8 |
| G-9 | S1/D-4 slot timeout starts nothing | PC-9 |
| G-10 | S1/D-4 apply broker CPU grant | PC-10 |
| G-11 | S1/D-4 cleanup precedes lease release | PC-11 |
| G-12 | S1/D-4 release on exceptional completion | PC-12 |
| G-13 | S1/D-4 preserve renew-mode lifetime | PC-13 |
| G-14 | S1/D-5 queue wait is outside child watchdog | PC-14 |
| G-15 | S1/D-4 one lease owner, no nested driver lease | PC-15 |
| G-16 | S2/D-3 independent discovery before admission | PC-16 |
| G-17 | S2/D-3 missing class is refused | PC-17 |
| G-18 | S2/D-3 duplicate class is refused | PC-18 |
| G-19 | S2/D-3 unknown class is refused | PC-19 |
| G-20 | S2/D-3 invalid discovery cannot admit execution | PC-20 |
| G-21 | S2/D-3 discovery binds unchanged built output | PC-21 |
| G-22 | S2/D-3 literal filter and exact class membership | PC-22 |
| G-23 | S2/D-3 final union requires every expanded UID | PC-23 |
| G-24 | S2/D-3 duplicate UID across chunks cannot disappear | PC-24 |
| G-25 | S2/D-3 ordinary red must retain later chunk evidence | PC-25 |
| G-26 | S2/D-3 serial execution waits for cleanup | PC-26 |
| G-27 | S3b/D-2 Unit comes from preserved compiled metadata | PC-27 |
| G-28 | S3b/D-2 expanded diagnostics, not TRX GUIDs, identify rows | PC-28 |
| G-29 | S3b/D-2 zero/nonterminal set cannot be complete | PC-29 |
| G-30 | S3b/D-2 required skipped row never earns green | PC-30 |
| G-31 | S3b/D-2 failed row never earns green | PC-31 |
| G-32 | S3b/D-2 duplicate terminal rows are invalid | PC-32 |
| G-33 | S3b/D-2 bind receipt identity envelope | PC-33 |
| G-34 | S3b/D-2 sorted UID digest covers the admitted set | PC-34 |
| G-35 | S3b/D-1/D-2 Unit success cannot override full nightly red | PC-35 |
| G-36 | S3b/D-5 duration fields describe separate phases | PC-36 |
| G-37 | S4/D-6 opaque revision is not a content digest | PC-37 |
| G-38 | S4/D-6 failed script read is unknown, never absent | PC-38 |
| G-39 | S4/D-6 failed schedule read is unknown, never absent | PC-39 |
| G-40 | S4/D-6 stored script fields, not accepted POST, establish reconciliation | PC-40 |
| G-41 | S4/D-6 stored schedule fields establish reconciliation | PC-41 |
| G-42 | S4/D-6 enable readback establishes the selected stored state | PC-42 |
| G-43 | S4/D-6 restart reconciles accepted write with lost response | PC-43 |
| G-44 | S4/D-6 concurrent script revision is a fence | PC-44 |
| G-45 | S4/D-6 preview writes nothing | PC-45 |
| G-46 | S4/D-6 preview sends no token | PC-46 |
| G-47 | S4/D-6 create schedules disabled | PC-47 |
| G-48 | S4/D-6 enable only the explicit selector | PC-48 |
| G-49 | D-7 manual does not earn scheduled credit | PC-49 |
| G-50 | D-2 full coverage remains a readiness prerequisite | PC-50 |
| G-51 | D-2 full test success remains a readiness prerequisite | PC-51 |
| G-52 | D-8 false delivery remains a readiness refusal | PC-52 |
| G-53 | S1/D-4 actual descendant exit and drained output, not an asserted ChildrenExited bit | PC-53: not executable; B-2: native process observation/staging seam is absent |
| G-54 | D-8 complete correlated nightly report receipt, not exit zero | PC-54: not executable; B-3: report recipient/receipt reader contract is absent |
| G-55 | D-8 durable nightly-report recovery at each queue handoff | PC-55: not executable; B-3: report intent/queue/recovery contract is absent |
| G-56 | S3a/D-3/D-5 measured complete Windows partition within unchanged caps | PC-56: not executable; B-1: current compiled census and full class timing inputs are unavailable |
| G-57 | S1/D-4 PowerShell 5.1 bootstrap cannot bypass PowerShell 7 admission | PC-57: not executable; B-2: native interpreter handoff fixture and checkpoint are absent |

### Positive controls

PC-1..52 below are concrete mutation recipes for the planned methods. Code must
implement the new methods and their assertions; an empty/constant harness row is
not an implementation. All selectors are method-scoped:
'/*/*/ClassName/ExactMethodName', using the exact Class.Method in the table.
Each recipe changes production PowerShell, not the test expectation or fixture.
A valid red is the named assertion reached with tests executing; parse, build,
fixture failure and zero selection are not red.

Mutation runs break/red/restore/green **after land** against the commissioned
SourceLanding SHA, with separate fresh build/receipts for red and restored green.
Code runs V/R; ordinary Review judges the recipes before land. Execute all named
single-field/response/outcome variants where a method covers a matrix. Never
declare a multi-variant PC complete from one failing arm. Within one PC, independent
single-field bypasses each get their own red/restore/green cycle if the
implementation has separate guard branches; TestDesign must split the G/PC rows
before Code admission once those branches are chosen.

| PC | Break the matching G by this compiling defect | Exact test method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 | bypass the lease only for npm ci | NightlyBuildSlotTests.C1039_AllDriversLeased | npm-ci: child-start follows its own grant |
| PC-2 | bypass the lease only for npm run build | NightlyBuildSlotTests.C1039_AllDriversLeased | npm-build: child-start follows its own grant |
| PC-3 | bypass the lease only for client lint | NightlyBuildSlotTests.C1039_AllDriversLeased | lint: child-start follows its own grant |
| PC-4 | bypass the lease only for dotnet build | NightlyBuildSlotTests.C1039_AllDriversLeased | dotnet-build: child-start follows its own grant |
| PC-5 | bypass the lease only for list-tests | NightlyBuildSlotTests.C1039_AllDriversLeased | discovery: child-start follows its own grant |
| PC-6 | bypass the lease only for native execution | NightlyBuildSlotTests.C1039_AllDriversLeased | native: child-start follows its own grant |
| PC-7 | bypass the lease only for test-client | NightlyBuildSlotTests.C1039_AllDriversLeased | client: child-start follows its own grant |
| PC-8 | bypass the lease only for a script-census child | NightlyBuildSlotTests.C1039_AllDriversLeased | scripts: child-start follows its own grant |
| PC-9 | continue to launch after Outcome=timeout | NightlyBuildSlotTests.C1039_SlotTimeout | busy and memory-floor: zero child starts and exit 4 |
| PC-10 | discard Add-AntiphonMaxCpuCount output | NightlyBuildSlotTests.C1039_CpuGrant | dotnet build argv contains exactly -maxcpucount:3 |
| PC-11 | release the lease before the owned-child join | NightlyBuildSlotTests.C1039_ReleaseAfterCleanup | child-exit and output-drained precede DELETE |
| PC-12 | omit Exit-AntiphonBuildSlot from the failure finally arm | NightlyBuildSlotTests.C1039_ReleaseAfterCleanup | throw-after-start: exactly one DELETE after cleanup |
| PC-13 | dispose the lease renewal job immediately after acquisition | NightlyBuildSlotTests.C1039_RenewLease | two renewals occur while the child is alive |
| PC-14 | subtract slot wait from the child's timeout allowance | NightlyBuildSlotTests.C1039_WaitClock | wait longer than child budget then grant: quick child succeeds |
| PC-15 | wrap an already gated child in a second acquisition | NightlyBuildSlotTests.C1039_NoNestedLease | nested fixture driver: one grant, one release, one child |
| PC-16 | restore eligibleClasses derived from chunks and discovery after execution | NightlyChunkAdmissionTests.C1039_IndependentDiscovery | extra discovered class: refusal before first execution |
| PC-17 | ignore the membership Missing set | NightlyChunkAdmissionTests.C1039_Admission | A+B discovered, A assigned: missing B and zero execution |
| PC-18 | ignore duplicate assignments | NightlyChunkAdmissionTests.C1039_Admission | A assigned twice: duplicate A and zero execution |
| PC-19 | remove the unknown-class rejection | NightlyChunkAdmissionTests.C1039_Admission | A discovered, A+Z assigned: unknown Z and zero execution |
| PC-20 | use configured classes when discovery exits nonzero | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | nonzero discovery with otherwise valid diagnostics: zero execution |
| PC-21 | skip the assembly hash comparison | NightlyChunkAdmissionTests.C1039_BuildBinding | changed DLL after discovery: execution refused |
| PC-22 | drop one OR operand from generated native arguments | NightlyChunkAdmissionTests.C1039_LiteralFilter | captured argv and executed class set both equal A+B |
| PC-23 | accept union.Ok=false | NightlyChunkAdmissionTests.C1039_ExpandedUnion | one missing argument UID: incomplete and exact UID named |
| PC-24 | deduplicate terminal rows before overlap validation | NightlyChunkAdmissionTests.C1039_ExpandedUnion | same UID in two chunks: duplicate count 1 and incomplete |
| PC-25 | break the chunk loop on a nonzero ordinary test exit | NightlyChunkAdmissionTests.C1039_ContinueAfterRed | B execution and B evidence exist after A fails |
| PC-26 | advance to chunk B while A cleanup is held | NightlyChunkAdmissionTests.C1039_SerialChildren | B starts only after A cleanup releases; maximum active native=1 |
| PC-27 | classify by method/class name containing Unit | NightlyUnitReceiptTests.C1039_Categories | misleading Unit name excluded; inherited Unit metadata included |
| PC-28 | join required UID directly to TRX testId | NightlyUnitReceiptTests.C1039_TerminalRows | two argument UIDs with unrelated TRX GUIDs: executed=2 |
| PC-29 | treat an empty terminal set as complete | NightlyUnitReceiptTests.C1039_TerminalRows | discovery-only and InProgress-only: complete=false, passed=0 |
| PC-30 | normalize Skipped to Passed | NightlyUnitReceiptTests.C1039_TerminalRows | required skipped UID: skipped=1 and complete-green=false |
| PC-31 | ignore failed Unit terminal outcomes | NightlyUnitReceiptTests.C1039_TerminalRows | failed UID: failed=1 and complete-green=false |
| PC-32 | collapse duplicate terminal UIDs before counting | NightlyUnitReceiptTests.C1039_DuplicateRows | duplicate UID: duplicate=1 and complete-green=false |
| PC-33 | copy expected identity over the observed receipt identity before comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | each independently changed SHA/ref/policy/assembly/run/job/script field is rejected |
| PC-34 | hash only the count rather than sorted UIDs | NightlyUnitReceiptTests.C1039_Digest | same-count UID replacement changes digest; permutation does not |
| PC-35 | replace overall result with the Unit result | NightlyUnitReceiptTests.C1039_FullNightly | Unit green plus non-Unit red: nightly stays red |
| PC-36 | write total wall time into childElapsedSeconds | NightlyUnitReceiptTests.C1039_PhaseDurations | controlled slot/child/cleanup intervals stay distinct |
| PC-37 | restore liveHash==desiredDigest comparison | ReleaseGateRegistrationTests.C1039_OpaqueRevision | identical content, opaque revision: second apply writes=0 |
| PC-38 | classify failed scripts/get as missing | ReleaseGateRegistrationTests.C1039_UnknownReads | 401/403/503/transport/invalid-200: zero writes |
| PC-39 | classify failed schedules/get as missing | ReleaseGateRegistrationTests.C1039_UnknownReads | 401/403/503/transport/invalid-200: zero writes |
| PC-40 | omit the owned-field comparison after GET | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered stored field: apply fails |
| PC-41 | compare only cron/timezone | ReleaseGateRegistrationTests.C1039_ScheduleFields | wrong target/tag/args/is_flow: apply fails without overwriting drift |
| PC-42 | return success immediately after setenabled POST | ReleaseGateRegistrationTests.C1039_EnableReadback | accepted-but-not-stored enable: failure and no enabled receipt |
| PC-43 | recreate all definitions unconditionally on resumed apply | ReleaseGateRegistrationTests.C1039_RestartApply | each committed cut: one stored object per path and no extra script revision |
| PC-44 | omit parent_hash on an update | ReleaseGateRegistrationTests.C1039_OpaqueRevision | fixture changes revision before POST: refuses and preserves competing content |
| PC-45 | fall through preview into Apply writes | ReleaseGateRegistrationTests.C599_Preview | a preview performs zero writes |
| PC-46 | load tokenFile into preview context | ReleaseGateRegistrationTests.C599_Preview | a preview sends no token |
| PC-47 | copy desired enabled=true into create payload | ReleaseGateRegistrationTests.C599_ApplyReadback | nightly schedule was created DISABLED |
| PC-48 | enable rc while enabling readiness | ReleaseGateRegistrationTests.C599_ApplyReadback | enabling readiness did not enable rc |
| PC-49 | allow manual native trigger in the readiness predicate | NightlyVerificationContractTests.C544_ScheduledIdentity | native trigger must be scheduled |
| PC-50 | remove coverageComplete from native green admission | NightlyVerificationContractTests.C544_CoverageRequired | coverageComplete=false is unready |
| PC-51 | remove testsPassed from native green admission | NightlyVerificationContractTests.C544_GreenRequired | testsPassed=false is unready |
| PC-52 | remove reportDelivered from native green admission | NightlyVerificationContractTests.C544_ReportReceiptRequired | reportDelivered=false is unready |

PC-53..57 are explicitly **non-executable obligations**, not passing or waived
controls. PC-53 needs a real child that outlives its root, a failed native kill/
join result and an observer that distinguishes it; the current constant true
return makes the proposed oracle unverifiable. PC-54 needs an otherwise valid
run with missing/wrong complete recipient evidence and a production receipt
validator to bypass. PC-55 needs persisted report intent and concrete handoff
fault hooks before a restart defect can be named. PC-56 needs a frozen current
Windows census/timing input and a concrete admission validator before a
missing-class/over-budget mutant is executable. PC-57 needs the actual 5.1 -> 7
handoff and a native checkpoint before a bypass mutation can be verified.
Plan must provide these seams, then TestDesign must replace these rejected
entries with exact method/assertion/defect rows.

**Admission audit:** bodies read as listed; guards=57, mapped=57, missing ID
maps=0, duplicate PC maps=0. Concrete offline recipes=52; non-executable
obligations=5 (PC-53..57). No PC was run. The required all-PCs-executable gate
**fails**, as does the whole-ordinary-scope checkpoint union. Therefore next is
Plan, not Code. This audit is not a claim of a completed verification freeze.

### Out of scope

- Re-running CARD-1021 whole Unit: revision-9 waiver remains authoritative.
  Its historical failures/skips remain red evidence; no extra Unit run here.
- jq/images/remote rollout repair and actual timeout diagnosis: CARD-1040;
  checkpoint row-ceiling enforcement: CARD-1041. This manifest does not assume
  that enforcement is installed.
- Broker redesign and unrelated CARD-0589 wrapper/broker regression suites.
  Inspect the landed Round 2 consumer before implementing S1 to avoid overlap.
- Changing eligibility, suite membership, native timeout caps, nightly deadline,
  RC enablement or InterimVerification/reducedDispatchPolicy.
- Live registration, schedule enable, Telegram messages, reader login or choosing
  a new destination: commissioned Q-0..Q-6/CARD-0545 custody only.
- Full nightly qualification is separate operational work, not a hidden Code
  checkpoint. Missing native/delivery proof V-5/V-6 is **required work**, not an
  exclusion accepted for final scope.
- Source-text bootstrap/provenance assertions do not prove Windows process
  behavior. No native success is claimed from the Linux mirror.

### Checkpoints

**NOT ADMITTED FOR CODE:** this table freezes the offline candidate scope only.
Its union is V-1..V-4/R-1..R-5. V-5/V-6 and S3a/native Windows remain missing for
the reasons above; do not run this as the whole plan. This avoids empty or
invented Windows rows. After Plan provides B-1..B-3, TestDesign must finish the
native/report rows and guard splits and recheck the full union before Code.

All new C1039 methods below are single non-parameterized tests: slots=7,
admission=8, Unit receipt=7, registration=6 new + 4 existing. Existing regressions
are slots=4, policy=5, readiness=6, scripts=2. Total candidate executions=49.
Require every named method, zero failed/skipped, not merely the floor.
Use the importer-selected host; no portable row gets a Windows-only pin.
Filters deliberately select only the new C1039 methods or explicit retained
methods. A '*' on one existing class in CP-5 means the five inspected methods,
not permission to absorb later additions without recounting.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1039-s1/ | nightly-slot-consumer | /*/*/NightlyBuildSlotTests/C1039_* | V-1 | all 7 named methods, 0 failed/skipped | 7 | 10 |
| CP-2 | S1 | CP-1 | slot-retained | /*/*/BuildSlotScriptTests/(C589_WrapperRunsUnderLease*)\|(C589_WrapperMaxCpuCountRules*)\|(C589_WrapperTimeout*)\|(C800_WrapperPassesWildcardArgvLiterally*) | R-1 | all 4 listed, 0 failed/skipped | 4 | 4 |
| CP-3 | S2 | tests/Antiphon.Tests -> bin-c1039-s2/ | independent-chunk-admission | /*/*/NightlyChunkAdmissionTests/C1039_* | V-2 | all 8 named methods, 0 failed/skipped | 8 | 12 |
| CP-4 | S3b | tests/Antiphon.Tests -> bin-c1039-s3b/ | unit-duration-receipt | /*/*/NightlyUnitReceiptTests/C1039_* | V-3 | all 7 named methods, 0 failed/skipped | 7 | 10 |
| CP-5 | S3b | CP-4 | policy-parser-retained | /*/*/ReleaseGatePolicyTests/* | R-2 | all 5 inspected methods, 0 failed/skipped | 5 | 4 |
| CP-6 | S3b | CP-4 | nightly-readiness-retained | /*/*/NightlyVerificationContractTests/(C544_ScheduledIdentity*)\|(C544_CoverageRequired*)\|(C544_GreenRequired*)\|(C544_ReportReceiptRequired*)\|(C545_ResultLine*)\|(C545_JobResultFetch*) | R-3 | all 6 listed, 0 failed/skipped | 6 | 4 |
| CP-7 | S4 | tests/Antiphon.Tests -> bin-c1039-s4/ | registration-readback | /*/*/ReleaseGateRegistrationTests/(C1039_*)\|(C599_Preview*)\|(C599_ApplyReadback*)\|(C599_Concurrency*)\|(C599_ScheduleProvenance*) | V-4, R-4 | all 10 named methods, 0 failed/skipped | 10 | 12 |
| CP-8 | S4 | CP-7 | bootstrap-retained | /*/*/NightlyScriptsTests/(The_three_scripts_are_ascii_only*)\|(Shared_tree_WhatIf_exits_3_naming_the_guard*) | R-5 | both listed, 0 failed/skipped | 2 | 2 |

New method roster is exactly the distinct C1039 Class.Method names in PC-1..44.
Tests sharing a method share its TUnit execution, never its independently
required mutation. New harness rows must use the C1039 prefix and assert the
named inventories through ScriptHarness or an equivalent owned-child fixture.
No harness-wide test-nightly-tests invocation is in this table: its constant
true cases are not evidence.

After admission, use tools/Antiphon.Checkpoints run --plan with one committed
slice group (--after S1, S2, S3b or S4), --expected-source-sha and --serial.
Use an isolated gated tool bootstrap only if necessary and record it.
Wait with --max-wait 50s until terminal exit; exit 75 means keep waiting.
The tool owns build slots; any separate driver must use scripts/build-slot.ps1.
Exit 4 is not run. No unleased retry, source edits during execution, or reuse
across After groups. Validate structured source/build receipts and preserve the
unedited CHECKPOINT lines. Delete only the task-owned alternate output inventory
after all children have exited; generated JSON/TRX/logs stay ignored. Run the
full-task-range evidence diff guard in Code/Review.

Code slice budgets for this candidate scope, before the missing native/delivery
work: S1 35 authoring + 14 verification = 49 minutes; S2 40 + 12 = 52;
S3b 35 + 18 = 53; S4 40 + 14 = 54. These are estimates, not measured timings.
S3a and the missing native/report changes must be separately scoped by Plan;
they cannot be added to these dispatches and still be represented by these costs.

### Cost

All new costs are **estimated**; this TestDesign ran zero builds, zero tests and
zero PCs. Historical timings cited under B-1 are attributed measurements and
are not substituted for current checkpoint timings.

- Ordinary offline V/R floor (Code) = CP-1 10 + CP-2 4 + CP-3 12 + CP-4 10 +
  CP-5 4 + CP-6 4 + CP-7 12 + CP-8 2 = **58 minutes** for the exact filters above.
  Four isolated builds are included, estimated at 2 minutes each: 8 build +
  50 execution/fixture minutes. They are not added again.
- Additional setup/tool bootstrap/source receipt inspection/cleanup allowance =
  **8 minutes**. Offline Code verification including setup = **66 minutes**,
  divided among the four slices above; authoring is separate.
- Concrete PC floor (Mutation) = **312 minutes** for PC-1..52 at **6 minutes each**:
  0.5 mutate + 2 red build + 0.5 exact-method red + 0.5 restore +
  2 restored build + 0.5 same-method green. Each uses
  /*/*/ClassName/ExactMethodName from its PC row, never a class or suite.
  This is a **lower bound of one cycle per PC**; field/response variants that
  need separate mutations add 6 minutes per cycle and must be enumerated when
  Plan fixes the guards. Long-lived child controls must be measured and repriced
  if they exceed the 0.5-minute execution estimate, never dropped.
- Priced offline verification lower bound = setup 8 + ordinary 58 + concrete
  PCs 312 = **378 minutes**. This is **not** a full-task total: PC-53..57,
  S3a native census/timing, native integration and Q-0..Q-6 are not yet costable
  from current inputs. Full-task admission therefore fails; zero minutes is not
  assigned to those obligations. CARD-0545's retained S6 band is 300-600 active
  minutes plus overnight boundaries, not a measured CARD-1039 quote.
- Build reuse in CP-2/5/6/8 saves four otherwise redundant estimated 2-minute
  builds = **8 minutes** versus an identical eight-build selection (66 ordinary
  minutes -> 58). No savings are claimed by omitting required native/delivery
  proof or by the CARD-1021 waiver. No PC batching saving is assumed: most
  mutations share production script files.
- Before a Code handoff the revised design must have executable PCs for all
  guards, frozen Windows selections/counts, a whole-scope CP union, and a numeric
  full ordinary/PC cost. The present lower bound must not be presented as that
  completed gate.

TestDesign outcome: preserve this inspection and candidate offline design, then
commission Plan to resolve B-1/B-2/B-3, including guard splitting and bounded
native/report slices. No human choice is requested by this task and no live
activation has occurred.
