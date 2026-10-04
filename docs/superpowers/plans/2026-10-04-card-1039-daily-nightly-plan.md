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
