# CARD-1039: activate and prove daily whole-Unit coverage through Windmill

Stage: Plan amendment after TestDesign. Verification remains a **separate
TestDesign stage**; the retained candidate manifest is not admitted for Code.
Next: `test-design`. B-1 now has a bounded measurement commission; B-2/B-3 have
implementation and verification seams below. None is claimed implemented.

Source inspected: `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`. Plan task:
`e2c04614-77dd-4b32-92fa-ce4c29c1b624`, branch `feat/card-task-e2c04614`.
The caller lands this plan through normal landing after its branch is pushed;
this delegate does not push to master or alter the canonical checkout.

Amendment task `1064523d-6b1e-43bc-9250-4784646b5d13` inspected landed
TestDesign commit `d0f4adcf6038b3a768fb1e6bec23d563d76d4fe9`. Its changes are
limited to B-1 measurement, B-2 process/interpreter custody, B-3 report
receipt/recovery, and the corrected nightly eligibility premise. The original
schedule, chunking, registration, waiver and qualification decisions remain.

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
| An empty nightly exclusions array makes every discovered node eligible. | `Invoke-AntiphonNightlyTests` sets `ProfileAware = (Profile != 'nightly')`. Nightly therefore uses `Node.Excluded` and OptIn/Explicit category exclusions; RC uses explicit profile dispositions. `Get-NightlyDiscoveryDisposition` records the default exclusion as `category-optin` / `nightly-default`. | Freeze these actual semantics in both census and receipt. Empty nightly profile exclusions do not override the default exclusions. Correct owner prose in S5; no eligibility policy change. |
| A successful cleanup result proves descendants exited. | `Invoke-NightlyOwnedProcess` ignores the timeout cleanup wait result and returns `ChildrenExited=true`; its `StartProcess` seam bypasses the launcher. | D-10/S1n must observe OS containment and pipe completion; add a fixture executable seam without replacing launch/wait/cleanup. |
| A green reporter exit proves a delivered report. | `nightly-run-impl.ps1` falls back to reporter exit zero; `nightly-report.ps1` returns zero on green/no-open-card without a write. No durable delivery queue/receipt is consumed. | D-11/S3c require a persisted whole-report board receipt on every reported run, including green. |
| The board has an exactly-once report endpoint. | `CardCommentService` stores discussion bodies (trimmed, maximum 16,000 characters), returns an acknowledgement, and separately lists stored comments. It has no idempotency key. | Reuse this destination with a durable local outbox, reader-first recovery and at-least-once semantics; do not promise exactly-once writes or human reading. |

Platform reads completed during this Plan using `ANTIPHON_API` and the inherited
task header: `GET /api/runner-defaults` returned revision 2, a global runtime
preference, no kind overrides and no unresolved references; `GET
/api/session-runners` returned eligible Windows and Linux descriptors plus an
unavailable draining descriptor. These observations grant no capacity reservation.
Re-read both before dispatch. Omit `-Runner`; omit `-Platform` for portable work,
or use `-Platform Any` only to remove an inherited pin. Use `-Platform Windows`
only for the native Windows checkpoint/qualification lane below. No fleet
address, SSH identity, execution host or watchdog host is prescribed here.

The amendment repeated both GETs on 2026-10-04: defaults revision 2, zero kind
overrides; available Windows and Linux descriptors and one unavailable draining
descriptor. This is placement evidence only, not a Windows census or timing run.

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
large static roster or durations in Plan: M-1/M-2 supply measured inputs,
TestDesign validates them, and S3a commits the resulting policy and recomputed
hash. TestDesign must not manufacture measurements to finish its manifest.

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

**D-9 — Measure B-1 before admitting chunk configuration.** Use M-0/M-1/M-2
below, starting with retained evidence and discovery without test execution.
Only missing or invalidated class timings justify new class probes. The current
Windows census is an operational measurement prerequisite, not a request to
repeat CARD-1021 Unit. Reject Linux counts, source annotations, the small probe
assembly and sums of per-case durations as replacements for native discovery
and class wall time. A successful Plan amendment does not make B-1 measured.
S3a's proposed `Test-NightlyChunkTimingAdmission` in
`scripts/lib/nightly-coverage.ps1` consumes the measured map separately from the
chunk roster: reject missing/inapplicable timing, incomplete UID execution, and
a chunk whose sum of class wall times plus measured invocation/cleanup allowance
exceeds 80% of its existing 60-minute cap. This 20% reserve is an admission
headroom choice, not a timeout increase or a measured forecast. Also report the
whole-run forecast including build/discovery and observed slot contention; an
unbounded queue delay cannot support a morning-deadline claim.
This validator is the M-3/S3a/Q-0 admission check over retained inputs; it does
not make the nightly depend on a task worktree or fetch a timing payload at
runtime. Production still independently validates the compiled chunk inventory.

**D-10 — Make native custody an observed production result.** Keep the Windows
nightly lane and serial driver design. Add a small Windows process adapter in
`scripts/lib/nightly-owned-process.cs`, loaded by a PowerShell-7-only helper
`scripts/lib/nightly-owned-process.ps1`. Create the root suspended, assign it to
a private kill-on-close Job Object with breakaway disabled, then resume. This
closes the spawn-before-assignment race; a missing/failed assignment refuses
before resume. Do not transplant the checkpoint runner's best-effort,
post-start assignment as proof of containment. Keep argument tokens literal,
capture stdout/stderr independently and retain the job/owned process handles
until cleanup is observed. No process-name sweeps or kills of unrelated work.

`ChildrenExited` is true only after the root handle has signalled and job active
process count is zero. `OutputDrained` independently requires both redirected
readers to reach EOF and final log writes to finish. `CleanupComplete` is their
conjunction; include root PID/start identity, descendant identities, termination
result, observed remaining count, drain result and phase durations in the result.
On timeout, terminate the owned job, observe it empty within the existing 15 s
cleanup wait, then finish drain; never infer success from the kill request.
A root which exits while its child lives still needs cleanup. Uncertain query,
failed termination, nonempty job or drain failure is incomplete/red, not an
ordinary test failure that permits the next chunk. Keep temporary logs.

S1 holds the lease through this cleanup and never emits a normal release or
starts another driver on an unresolved result. Use kill-on-close as the final
containment fallback, then observe the retained process handles; if custody is
still unknown, keep the foreground owner and its lease renewal alive for
diagnosis/owned recovery. Do not abandon a child or background the supervisor to
settle the task. Record a held cleanup explicitly; do not disguise it as green.
The portable controlled-I/O tests prove consumer decisions only; the native
Windows row proves this adapter and containment contract.

Add a `ResolveNativeExecutable` fixture seam to `NightlySeams`: by default it
returns the existing suite path; a fixture may return a built executable under
its owned staging root. Use the same resolved path for discovery and execution,
hash it and its assembly, and forbid qualification credit when fixture seams
are active. This seam never replaces `StartProcess`, wait, job queries, drain or
lease release. Extend `c487-probe` for Unit/argument/OptIn metadata and add an
owned descendant sentinel mode/fixture. Reject hard-coding the test executable
as the production path and reject the broad `StartProcess` mock for native proof.

The 5.1 `nightly-run.ps1` bootstrap continues to launch the tests in `pwsh`.
For a direct 5.1 `nightly-tests.ps1` entry, perform an explicit one-time hop to a
resolved PowerShell 7 executable **before** importing the gate/native libraries;
preserve bound arguments, exit code, run identity and final result exactly once.
Missing/unsupported `pwsh` refuses without starting a driver. Keep bootstrap
files ASCII and 5.1-parseable. Do not make a `#requires -Version 7` dot-source a
substitute for this boundary, and do not add a non-Windows native execution lane.

**D-11 — Deliver every nightly report to the existing board audience.** Keep
red incident creation/update and the unassigned-Backlog green-close rule. Use a
separate persistent **report ledger card on the same resolved Antiphon board**
for complete run reports, including green/no-incident. Q-0 selects or provisions
that card once through the existing board workflow and records its board/card
IDs in an untracked report profile. It carries no `nightly` or `release-gate`
incident label and is never auto-closed/reopened by the reporter. This is a
stored board recipient, not a session or a new messaging recipient. No Telegram
send or session queue change is added. Human reading is not inferred from storage.
Profile v1 holds `schemaVersion`, `boardId`, `ledgerCardId`,
`expectedScriptDigest` and `registeredScriptRevision`; Q-1 supplies the last
two from registration/readback. API resolution
uses the reporter's existing configured base. `nightly-run.ps1` forwards
`-ReportProfile` (or the operator-configured file reference) to the reporter and
recovery consumer. Missing/mismatched profile/card is pending/refused, never an
implicit new card or a different board. Resolve and validate it at Q-0.

Use the existing discussion POST and a separate GET of the persisted discussion
as the receipt reader. Canonicalize the complete report body once (LF, trimmed
ends, UTF-8); include profile, trigger, London due day, Windmill job ID when
present, native run ID, source/ref/policy/script identities, assembly/census
digests, Unit and full-suite verdicts, timings and artifact references. Define
`reportId = SHA256(version, boardId, cardId, profile, dueDay, jobId, nativeRunId)`
using a deterministic serialized envelope, plus a separate full-body digest.
Manual local runs explicitly record no Windmill job; scheduled runs require the
real job identity: S3c-d extends the wrapper to forward `WM_JOB_ID` through the
bootstrap/driver, following the existing RC wrapper's job propagation pattern
with validated tokens and literal argument passing. Carry the registered script
revision/content digest from the qualification identity configuration and check
it at Q-1/Q-2/Q-4; do not treat an opaque revision as a content hash. A supplied
trigger alone is insufficient. Report body reports test outcome; it does not
contain its own eventual delivery flag (no circular hash). Keep complete bytes
in the outbox.

The board's 16,000-character comment limit is real: split a longer body into
ordered deterministic parts with header space reserved, `reportId`, part
ordinal/count, whole-body digest and per-part digest. Prefix and suffix frame
each part so the API's trim cannot remove report whitespace; split only at valid
Unicode boundaries. Receipt requires all parts
from the configured card/board, exact reassembled body and identity match. No
truncation or link-only replacement earns receipt. GET results, not POST bodies,
exit codes, summary booleans or watchdog receipts, supply the stored comment IDs,
body bytes and observation time. Duplicate identical parts from at-least-once
retries may be coalesced by identity; a conflicting part is held as a conflict.

**D-12 — Persist report intent and recover it without rerunning tests.** Add a
file-backed outbox under `<StateRoot>\report-outbox\<reportId>`, outside the
resettable checkout and retained logs. A flushed temporary file + atomic rename
publishes immutable intent/body/parts before any POST or worker wake; a separate
atomic progress/receipt record stores attempts and observed comment IDs. Before
starting the test driver, persist a per-run manifest under
`<StateRoot>\report-producers` with the envelope and expected summary path;
failure to persist refuses that launch. The final summary is atomically
published. Recovery scans these manifests as well as pending outbox entries:
a complete matching summary without an outbox entry still needs enqueue, even
if the producer died immediately after summary publication. An absent/partial
summary stays incomplete, never green. Pending manifests pin their summary and
body evidence against retention cleanup. Missing/corrupt/mismatched intent
is held with an explicit reason; never reconstruct a different body under the
same identity. No generated outbox file is committed to Git.

One foreground consumer at a time owns a file handle lock on this outbox;
concurrent/busy consumers leave committed intent pending. The queue is the
durable directory, not a wake signal. Before each send or retry, independently
read stored parts and reconcile any lost acknowledgement; unknown/failed reads
do not establish absence. Persist attempt intent before POST, accepted IDs only
as sender acknowledgement, and a matched readback receipt before marking
received. Remove the reporter's blind HTTP-write retry for this path. The API
has no idempotency key, so a delayed commit may cause an identical duplicate;
reader-first retries and part identities give one logical receipt, not a false
exactly-once transport guarantee. Do not change server schemas/endpoints.

The nightly bootstrap drains pending reports at startup and at the report phase;
`nightly-report.ps1 -RecoverPending -StateRoot <root> -ReportProfile <path>` is
the same bounded consumer without tests, clone sync or state promotion. A
startup scan/wake processes at most ten intents and two minutes of HTTP work;
each request has a finite timeout and each unresolved intent retains its next
attempt/reason. The current run gets its own bounded delivery attempt. Further
recovery is the next daily wake or the morning operator's explicit command;
no new schedule, daemon or write in the read-only readiness evaluator. This
latency is visible: pending delivery stays unready and the watchdog still alerts.

The producer consumes and validates the correlated receipt file, then sets
`reportDelivered`; remove both the exit-zero fallback and trust in a summary's
self-asserted flag. Receipt absent/invalid, `-DryRun` or `-NoReport` means false;
report failure/pending is nonzero and blocks green. Keep incident reconciliation
as a distinct action/result; a failed incident write cannot turn the job green.
An old report may become received after restart, but recovery never rewrites a
completed Windmill job, advances `last-complete-green.json`, or credits it to a
new run. Receipt-before-final-state crashes recover delivery without rerunning
tests; that interrupted qualification run remains incomplete and Q-4 needs a
future genuine scheduled green. Reject treating an old report retry as a new
nightly success or reusing CARD-0545's separate outage receipt as this receipt.

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

### B-1 measurement commission (before S3a admission)

These are bounded Investigate/measurement dispatches, not whole-Unit Code
checkpoints. They use the native Windows lane (`-Platform Windows`, no
`-Runner` pin), each with an evidence root outside the checkout and one frozen
source/build per dispatch. TestDesign first reviews this commission, then the
caller obtains its evidence before freezing S3a. Portable implementation can be
designed meanwhile; missing measurements are never assigned zero cost.

| ID / budget | Files or inputs; exact operation | Retained result / stop |
|---|---|---|
| M-0 / 30 min maximum, no build/tests | Read CARD-0487/0474/1021/1040 retained reports and the native qualification owner's existing discovery/TRX/diagnostic stores. Inspect source/build hashes and class-start/end receipts, not a source `[Test]` count. | An applicability table: current usable receipt, historical-only receipt, or missing per class. The partial 0eff2a32 report and Linux Unit receipt are historical only. If no usable receipt exists, finish immediately and commission M-1; do not hunt indefinitely. |
| M-1 / 30-60 min, build + discovery only | On one clean committed Windows source, gated isolated build of `tests/Antiphon.Tests` to `bin-c1039-census/`, then gated execution of that output with `--list-tests --diagnostic --diagnostic-output-directory <fresh-root> --no-ansi --no-progress`. No execution filter and no test execution: this command enumerates the compiled assembly. Parse diagnostics with `ConvertFrom-NightlyDiagnosticLog -Kind discovery`, then the existing discovery document helpers and `Get-NightlyDiscoveryDisposition -ProfileAware:$false` from `scripts/lib/nightly-coverage.ps1`. | Expanded UID/class/category census with eligible/excluded reasons, sorted digests and per-class counts; SHA, dirty=0, policy and assembly hashes, SDK/OS, build and discovery wall times, slot wait, cleanup, exact argv and artifact paths. Count is measured, never fixed to 4,051. Unknown/malformed discovery refuses the census. |
| M-2.n / 30-60 min per dispatch, only missing classes | From M-1, freeze one exact class selection per row `/*/<namespace>/<ExactClass>/*` and its expected eligible UIDs. Use the same unchanged native output, serial gated invocations and fresh TRX/diagnostics. Each row is at most 20 min diagnostic execution plus bounded cleanup; group only rows whose cumulative measured/upper-bound cost fits the dispatch. No wildcard namespace, whole-Unit or full-assembly execution. | Per-class wall/startup/cleanup and slot times, started/terminal/missing UIDs, CPU/memory observations and process-exit proof. Ordinary assertion red remains red but may yield a complete timing if every required case terminates and cleanup completes. A capped/incomplete probe supplies only a lower bound; it cannot be admitted as a full timing. Hand exact evidence to CARD-1040 for the separately bounded follow-up; do not call a 20-min probe cap proof of a 60-min nightly timeout. |
| M-3 / 30 min analysis, no broad rerun | Join census and timing map; draft class groups and run `Test-NightlyChunkMembership` plus the planned timing validator. Reconcile source changes before S3a/qualification. | Every eligible class assigned once with an applicable complete wall-time input and explicit headroom; show predicted total including discovery, build, queue and cleanup against 08:00. Unmeasured, overlapping, changed or over-budget inputs block S3a. |

M-1's build and list driver both use `scripts/build-slot.ps1`; exit 4 is not run.
M-2's exact command/UID manifest must be written and frozen from M-1 before
launch. A test clock/probe cap is recorded separately from the unchanged native
watchdog. Windows execution and slots cannot be simulated by the Linux mirror.
If a checkpoint-tool row is used for a measured class, TestDesign imports the
exact roster and counts first; no placeholder Min or guessed filter is runnable.

Reuse a timing only with its original source/build/host identity and a documented
applicability comparison of that class, hooks, production dependencies and
tool/environment versions. Changed/unknown dependencies invalidate it. After
the new fixtures land, refresh compiled discovery and measure added/affected
classes only; unchanged applicable timings keep their original provenance, never
get relabelled as measured at the new SHA. Q-2 supplies final full-run evidence.
M-2 has a variable total cost; report number of missing classes and sum of row
budgets after M-1, before commissioning, instead of disguising it as one slice.

## Implementation slices

Code budgets below include authoring and their ordinary targeted checks. Freeze
each slice, commit and push before its checkpoint. These slices share nightly
files and therefore execute in order, not concurrently. Split an over-budget
slice at its named behaviour boundary before dispatch; never silently omit its
verification. Review and land each completed slice under the normal workflow.

| Slice / budget | Files | Concrete change and completion check |
|---|---|---|
| S1n-a: native containment adapter, 45-60 min | New `scripts/lib/nightly-owned-process.cs` and `.ps1`; `scripts/lib/nightly-tests-impl.ps1`; new `scripts/test-nightly-native.ps1` and `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` | D-10 suspended start/assignment, literal argv, root + descendant exit and output drain; Windows exact methods `C1039_AssignBeforeResume`, `C1039_DescendantExit`, `C1039_DrainBeforeReturn`. No gate consumer credit yet. |
| S1n-b: custody failure and real fixture, 45-60 min | Same adapter/harness; `scripts/lib/nightly-common.ps1` seam declaration; `scripts/fixtures/nightly/c487-probe/Probe.cs`, proposed owned-child fixture under `scripts/fixtures/nightly/`; `scripts/lib/nightly-tests-impl.ps1` resolution | Production launcher via executable resolution only; Windows methods `C1039_NativeTimeout`, `C1039_CleanupUnknownHolds`, `C1039_NativeFixtureLoop`. Independently observe root/child handles and final output; inject termination/query/drain faults below the owner, never a synthetic successful process result. |
| S1n-c: interpreter boundary, 30-45 min | `scripts/nightly-tests.ps1`, `scripts/lib/nightly-run-impl.ps1` only where propagation needs adjustment; native harness and new `tests/Antiphon.Tests/Scripts/NightlyInterpreterTests.cs` | Real `powershell.exe` 5.1 bootstrap -> resolved `pwsh` 7 -> tiny fixture, with exact methods `C1039_BootstrapUsesPwsh`, `C1039_DirectEntryHopsOnce`, `C1039_MissingPwshRefuses`. Observe actual versions/argv and no native launch on refusal. Preserve ASCII and single completion record. |
| S1: gate consumer, 45-60 min, after S1n | `scripts/lib/nightly-tests-impl.ps1`, its imports in `scripts/nightly-tests.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyBuildSlotTests.cs` | Apply D-4 at all driver callsites, including discovery, client and script drivers. Controlled broker/child evidence proves acquire-before-start, grant CPU cap, no start on slot timeout, and release after observed cleanup. Use D-10 result, not the old true bit. Add a narrow Windows `NightlyNativeOwnershipTests.C1039_LeaseAfterNativeCleanup` integration row. Audit nested script-owned gates; each driver has exactly one owner. Reuse a landed CARD-0589 consumer if equivalent. |
| S2: discovery and partition admission, 45-60 min | `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyChunkAdmissionTests.cs` | Move independent discovery before chunk execution; bind it to the built output. Validate D-3 against it, reuse it in final union accounting, retain serial native execution. Exercise missing class, added argument, overlapping filters and later-chunk execution after an earlier ordinary red. Do not claim a helper-only union test proves the production loop. |
| S3a: measured chunk configuration/admission, 45-60 min | `tests/test-execution-policy.json`, `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`, `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs`; proposed `tests/Antiphon.Tests/Scripts/NightlyPartitionTimingTests.cs`; measured roster in qualification Markdown | After M-3, commit class groups and D-9 timing admission with recomputed policyHash. Exact methods `C1039_MissingTimingRefuses`, `C1039_OverBudgetRefuses`, `C1039_TimingIdentity` guard missing, over-budget and inapplicable inputs. No suite removal, exclusion or timeout change. Generated timing payload stays external; record its digest/path and applicability in Markdown. |
| S3b: Unit and duration receipt, 40-60 min | `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`; proposed `tests/Antiphon.Tests/Scripts/NightlyUnitReceiptTests.cs` | Add D-2 accounting to summary.json and concise human report evidence without changing existing final result identity or green predicates. Preserve independent full-suite verdict. Prove missing/failed/skipped/duplicate Unit rows cannot count as complete-green and non-Unit red stays nightly red. Record timings needed by D-5. |
| S3c-a: durable report enqueue, 45-60 min | New `scripts/lib/nightly-report-delivery.ps1`; `scripts/nightly-report.ps1`, `scripts/lib/nightly-run-impl.ps1`; new `tests/Antiphon.Tests/Scripts/NightlyReportDeliveryTests.cs` and cases in `scripts/test-nightly-report.ps1` | D-11/12 immutable identity/body, deterministic multipart payload, manifest -> outbox publication and exclusive consumer lock. Exact methods `C1039_IntentBeforeWake`, `C1039_RecoverBeforeEnqueue`, `C1039_ReportParts`, `C1039_BusyConsumer`. Committed disk queue is real; no live board writes. |
| S3c-b: board recipient receipt, 45-60 min | Same files; `tests/Antiphon.Tests/Application/CardCommentApiTests.cs` only for fixture reuse; proposed `tests/Antiphon.Tests/Application/NightlyReportRecipientTests.cs` | Consumer reaches private real Antiphon HTTP + PostgreSQL discussion store and performs independent GET. Exact methods `C1039_WholeBoardReceipt`, `C1039_GreenWithoutIncident`, `C1039_RejectMismatchedReceipt`, `C1039_UnknownReadNoSend`. Use private board/card/schema and guarded test host, never production runner/API. Preserve one-open-incident and safe green closure. |
| S3c-c: restart recovery, 45-60 min | Report delivery helper/harness; `NightlyReportDeliveryTests.cs`, `NightlyReportRecipientTests.cs` | Actual consumer process dies/restarts over the same outbox/database at D-12 handoffs; exact methods `C1039_EnqueueFailure`, `C1039_CommitBeforeWake`, `C1039_SendBeforeAck`, `C1039_ReceiptBeforeFinalState`, `C1039_AlreadyEligibleConsumer`. Verify held/pending versus matched received and no blind duplicate-credit or test rerun. Split if measured checkpoint cost exceeds the dispatch. |
| S3c-d: producer consumption and recovery front door, 40-60 min | `scripts/lib/nightly-run-impl.ps1`, `scripts/lib/nightly-tests-impl.ps1`, `scripts/nightly-report.ps1`, `scripts/nightly-run.ps1`, `scripts/nightly-tests.ps1`, `scripts/windmill/antiphon-nightly-tests.json`, narrow C545 result-line fixture changes | Propagate real job/script identity and report profile; validate receipt instead of exit zero/summary boolean; startup drain and `-RecoverPending`. Exact `NightlyReportDeliveryTests.C1039_ExitZeroIsNotReceipt`, `C1039_RecoveryDoesNotPromoteGreen`, `C1039_NoReportNoReceipt` plus affected CP-6 readiness/result regressions. Preserve final JSON compatibility and update definition digest expectations. |
| S4: registration readback, 40-60 min | `scripts/register-release-gates.ps1`, `scripts/test-release-gate.ps1`, `tests/Antiphon.Tests/Scripts/ReleaseGateRegistrationTests.cs`; `scripts/lib/release-gate.ps1` only if the transport needs a bounded read fix | Implement D-6 against an opaque-hash fixture. Preserve existing preview, explicit enable, schedule provenance and concurrent drift behaviour. Prevent unknown reads from writes; verify stored fields rather than just write acceptance. Existing front door remains the only registration path. |
| S5: qualification runbook, 30 min | `docs/testing-and-build.md`, `scripts/windmill/README.md`, `docs/nightly-watchdog.md` only where ownership needs clarification; `docs/investigations/<date>-card-0487-nightly-qualification.md` | Document gate/partition/receipt semantics, commands below and a pending/completed evidence matrix. Update absent-registration statements only with measured live evidence. Preserve ASCII-only daemon scripts and Windows backslash configuration conventions. Documentation-only slice needs link/diff checks, not another suite. |

S3b's JSON is ignored runtime evidence, not a committed generated payload. The
qualification Markdown records source SHA, compact counts, identities and paths
to retained originals; TRX, logs, JSON exports and checkpoint output remain in
their ignored/external evidence stores. No force-add or relocation workaround.

The added slice budgets include authoring and ordinary exact-method checks,
not post-land PCs or operational qualification. Sequence S1n-a/b/c -> S1 -> S2,
then the original slices and S3c-a/b/c/d; M-1 may collect the baseline earlier,
but S3a remains blocked on the reconciled M-3 input. All share source files and
must be commissioned serially. Windows-only containment/interpreter checks use
the native lane; outbox/board checks are portable with no platform pin. Real
board integration uses per-test DB schema isolation and the assembly-local
`ParallelLimiter<ProcessSpawnLimit>` for subprocess fixtures. Run no whole Unit.

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
| Q-0 / native Windows + independent watchdog | Verify deployed bootstrap SHA, isolated clone ownership, SDK/runtime, Docker, jq/tool dependencies, output disk, slot endpoint and worker routing; admit S3a inventory/timings. Resolve the existing board and select/provision the D-11 report ledger card/profile, verifying independent discussion readback. Complete CARD-0545 S6 profile/reader prerequisites. | Sanitized versions/identities and slot test result; actual host/job/SSH outer limits must cover measured native execution plus queue/cleanup. Store report-profile schema/path and board/card IDs, never credentials. Unknown custody, missing tools, unexplained exclusions or insufficient limits block qualification, not Plan completion. |
| Q-1 / installed Windmill API | Run front-door preview, then authorized Apply; GET each stored script/schedule, reapply and GET again. | Before/after content digest and opaque hash; correct cron/zone/tag/args/target; created-disabled state; no second-apply writes or duplicates. The current front door registers nightly, readiness **and disabled RC**; review that full diff, never enable RC here. Failed/unauthorized reads stop writes. |
| Q-2 / native Windows execution via Windmill | Invoke the nightly path manually with the nightly profile and no suite/ref override; await owned job completion. | Job id/result, native run id, bootstrap and tested SHA/ref, policy/assembly/script hashes, seven-suite inventories, Unit UID census/digest, durations, logs/TRX/discovery and report receipt. Manual provenance must remain manual. Red blocks Q-3; target failures rather than repeating the entire battery during diagnosis. |
| Q-3 / installed Windmill API + readiness | Explicitly enable only nightly and readiness after Q-2 and the watchdog/readiness prerequisite is usable. GET each schedule and the next due time. | Exact 00:30 London nightly and existing half-hour readiness; RC still disabled. Store independent watchdog instance/supervisor/snapshot and correct readiness config identities. Expected pending-scheduled-green readiness is not misreported as qualified. |
| Q-4 / real daily scheduled native lane | Observe the next actual 00:30 firing; await completion and inspect Windmill result, native state and monitor. | Scheduled job context, matching native run and local due date, same complete evidence as Q-2; `last-complete-green.json` advances only on true scheduled master green. No manually invented scheduled trigger. Complete by the existing morning deadline or retain a failed/pending qualification. |
| Q-R / isolated operational recovery lanes | Under the qualification caller's authorization, use a private Windmill fixture job/worker and private board ledger destination to cover ready worker, queued worker becoming eligible, death before native launch, and death after native completion before job-result persistence. Exercise report consumer busy/eligible, lost POST acknowledgement and receipt-before-final-state restart using the real outbox/board. | Correlate job/native/report IDs and retained complete bodies; missing job completion stays incomplete even if the board report later arrives. Never interrupt the shared production worker or send to a new chat. S3c offline cuts are prerequisites; installed queue/API evidence belongs here. Q-5 outage controls do not silently substitute for these native handoffs. |
| Q-5 / independent watchdog qualification | Carry CARD-0545 S6 F-1..F-6, production notice and recipient readback forward, using authorized isolated qualification targets. | Stable outage/notification/attempt IDs, complete recipient body match and receipt, correlated recovery, persistence/crash-cut evidence, heartbeat identity and named morning operator. Sender acceptance alone is not received. Never disable shared services to inject failure. |
| Q-6 / artifact and acceptance | Publish the qualification Markdown and then the trusted receipt only after all required rows pass. | Link C/O/L, Review and any SourceLanding companion/disposition, Q-0..Q-5 plus Q-R evidence and excluded inventory reasons. Run the evidence diff guard over the entire committed task range. CARD-0544 remains disabled until separately commissioned. |

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

The original Plan performed repository and authenticated read-only Antiphon
HTTP/card inspection. This amendment re-inspected B-1/B-2/B-3 source and both
placement GETs; it did not repeat the live card/Windmill census. Neither Plan
task ran a build, test, Windmill mutation or live notice. The historical
CARD-1021 test counts above are attributed evidence, not results from this task.
The card's premise is confirmed: registration is absent in the investigation and
the checked-in integration has concrete admission gaps. No operator decision is
needed to write this plan; deployment custody is handled at Q-0, after concrete
reviewable changes exist.

Next TestDesign reviews M-0..M-3 and freezes separate native/report rows from
D-10..D-12, with one PC per behaviour and lane names. B-1 measurement remains
pending: admit the bounded collection first, then freeze S3a against returned
evidence. Reconcile old CP-6's changed receipt fixture and split the retained
compound PC families. Preserve the CARD-1021 waiver and CARD-1040/1041 scope
boundaries. No Code admission follows merely from this amendment or the old
candidate checkpoint table.



## Verification design

Re-verification task `da49d22e`, source
`a54e03b78c6e45a9c78e8d09ca3679859c499605`. This section replaces the superseded
TestDesign appendix from `d0f4adcf6`; the fix design, D-1..D-12, M-0..M-3 and
Q-0..Q-6/Q-R above are unchanged. The older candidate table and stage notices
above describe the handoff **into** this stage; the disposition here supersedes
those notices.

**Admit the named implementation slices, starting with S1n-a.** B-2 and B-3 now
have executable verification seams. B-1 remains an operational measurement gate
on S3a and qualification, with the acceptance contract below; it is not a reason
to repeat CARD-1021 Unit or hold S1n/S1/S2/S3b/report work. Commission M-0 then
M-1 on Windows independently of Code. No Windows measurement, native pass,
delivery, registration or activation is claimed by this document.

### Inspection

Bodies re-read at the source above (new classes are specifications, not existing
tests). The nearest fixtures are named explicitly.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `nightly-tests-impl.ps1`: owned-process/cleanup/native-argv functions, build/suite/chunk loop, union and summary writer; `nightly-tests.ps1`; `nightly-common.ps1`: atomic writer, seam declaration | Suspended containment, root/descendant/pipe completion, argv, all eight nightly driver sites, independent discovery, atomic summary -> V-1/2/5/6. `ChildrenExited=true` currently proves nothing. |
| `nightly-coverage.ps1`: discovery document parser, discovery production, freshness, membership, disposition/census, coverage verdict, expanded UID union and TRX/diagnostic cross-check | Incomplete/malformed discovery, prefix classes, unchanged executable/DLL, eligibility, terminal outcomes -> V-2/3/7, R-2. |
| `test-nightly-tests.ps1`: New-Efx, Invoke-E, fresh-probe helper, G037..039, G041..051, G144; `c487-harness.ps1`: owned clone, seams, TRX/execution/discovery writers | Fabricated launcher outputs and golden parser evidence are bounded substitutes. G048/G050 and G051's first constant-true assertion supply no evidence -> replace claims through V-1/2/5. |
| `NightlyScriptsTests`, `ReleaseGatePolicyTests`, `ReleaseGateRegistrationTests`, `NightlyVerificationContractTests`, `ScriptHarness`: method and helper bodies; `Probe.cs`/`Probe.csproj` | Nearest fixtures for new Scripts classes; non-parameterized wrappers each count once. Existing probe has argument/data/inherited/partial/OptIn cases but no Unit category or owned descendant -> V-5, R-2/4/5. ScriptHarness timeout does not prove process-tree cleanup. |
| `BuildSlotScriptTests`; `test-build-slot.ps1`: launch/wait/environment helpers, lease/CPU/refusal/renew and literal-argv cases; `lib/build-slot.ps1`: enter/renew/exit/CPU functions | Private broker, actual grant/child order, refusal, exception and renewal -> V-1/R-1. `BuildSlotEndToEndTests` budget-one/killed-holder and launcher bodies supply a fixture pattern; broker assembly rerun excluded (CARD-0589 ownership). |
| `register-release-gates.ps1` complete body; `test-release-gate.ps1`: New-C599WindmillFx/read/failure/register helpers, four registration cases, five policy cases | Fixture currently manufactures a digest-like revision and stores too few fields. Opaque revision, unknown-vs-404, persisted field equality, concurrent drift and restart -> V-4/R-2/R-4. |
| `nightly-run-impl.ps1`: watched child, test/report phase, complete-green predicate/state writes; `nightly-report.ps1`: HTTP retry, Finish-Report, report composition and incident selection/green/red paths | Exit-zero/summary-true fallback and green/no-incident early return -> V-6/8. Blind POST retry cannot be reused as recovery. |
| `test-nightly-report.ps1`: summary/store/shim/invoke helpers, top-level T1..T10, G077..079 and G099..104; `test-nightly-run.ps1`: New-RunFx/Invoke-FxRun, Invoke-C545ResultLineRun and ResultLine assertions | T7's zero-write expectation changes; incident-specific assertions stay. Case selection currently still runs T1..T10 first; add case-only dispatch -> V-6/8/R-6. Fabricated delivery flag must become a negative case. |
| `CardCommentApiTests`: seed/reset/cleanup and POST/GET/distinct-session-route cases; `AntiphonWebAppFactory`: schema, host configuration, reset/disposal; `CardCommentService`: complete body; discussion endpoint lambdas | Nearest fixture for `Application/NightlyReportRecipientTests.cs`: real stored discussion, 16,000 UTF-16 code-unit cap, trimmed body, no idempotency key; TestServer needs a loopback bridge for child PowerShell -> V-6. Refusing runner remains structural. |
| `test-nightly-health.ps1`: ScheduledIdentity, FlagRequired, JobResultFetch; C545 ResultLine/JobResultFetch wrappers, ReaderFirstRetry/HeldReaderNoResend bodies; C545World create/restart | R-3 receipt/coverage/green gates; real SQLite with fake Telegram is a different delivery path and cannot prove this report. Retain Q-5/F-1..F-6. |
| Nightly/RC Windmill definition bodies; policy nightly/RC profiles and existing suite/watchdog configuration; CARD-0474 timing and CARD-1021 evidence | Wrapper job/trigger propagation -> V-8/R-4. Retained Windows case sums and Linux Unit receipts are historical, not M-1/M-2. |
| testing-and-build manifest/slots/nightly/delivery/restoration; nightly-watchdog; release-gates registration; project-context; orchestration stage/landing contracts; PlanTableImporter/ManifestValidator | Checkpoint schema, custody and qualification boundaries; no session delivery implementation changes. |

Missing setup is implementation work in the named slices, not a waived check:

- S1n-a/b: staged native sentinel executable, independent root/descendant handles,
  controllable OS-operation barriers and two output streams. Build/stage the probe
  and sentinel as dependencies of the isolated test-project build (including the
  necessary test-project build wiring); never run an ungated compiler inside a
  test. New native harness must own and join its children even when a mutant fails.
- S1/S2: private gate endpoint, saved/restored environment and ordered grant,
  start, root-exit, descendant-zero, drain and release observations. The portable
  controlled-I/O lane exercises consumer decisions; it cannot claim Windows exit.
- S3c-a: genuinely case-only C1039 report harness; real file operations, restart
  barriers and immutable bodies. Do not accidentally run T1..T10 for each method. The inspected nightly owner
  currently has no general log-retention sweep: test D-12 retention pins by
  applying age-based cleanup only to fixture-owned logs and asserting pending
  manifest/body/summary evidence stays available; add no retention daemon.
  Do not change generic ScriptHarness as an incidental repair: use an owned-child
  helper for these fixtures, with bounded finally cleanup and retained red logs.
- S3c-b: derive a private factory with a per-test `IsolatedTestSchema`, use the
  inspected refusing runner overrides, expose TestServer through a loopback-only
  HTTP bridge, and seed a private board, ledger and incident cards. POST/GET must
  traverse the actual endpoint/service/PostgreSQL. An HTTP recorder returning
  posted bodies is insufficient. A fresh independent client supplies the oracle.
- S3c-d: execute the checked-in Bash wrapper with an owned `ssh` argv recorder
  (private PATH; no network), then the real producer against the private queue.
  Portable lane requires pwsh and Bash; DB cases also need the repository test DB
  prerequisites. Windows rows require actual 5.1 plus supported pwsh and Job APIs.
  Missing prerequisites mean not run, never a skipped success.

All new wrappers are single, non-parameterized TUnit methods, Integration lane,
with the assembly-local `ParallelLimiter<ProcessSpawnLimit>` when spawning a
process. Internal fault/field loops do not inflate Min. Use fresh fixture roots
and restore environment. No production runner, board or broker is used.

### Delivery inventory

| Path / durable identity | Producer -> destination | Persistence boundary and recovery | Observable recipient evidence |
|---|---|---|---|
| DL-1 scheduled execution: schedule path/revision, due day, Windmill jobId, nativeRunId, source/ref/policy/script identity | Windmill schedule -> real job queue -> eligible worker -> Windows bootstrap/clone -> completed job result reader | Registered schedule/job persist separately from native state. Q-R covers enqueue refusal, queued/busy worker later eligible, already eligible worker, death before native launch, native completion before job-result persistence. Missing result remains incomplete; recovery cannot invent a new successful run. | Q-2/Q-4/Q-R join independent native complete artifacts and fetched job result to the same IDs. Registration, queue insertion, running event or success boolean is insufficient. V-8 proves only offline wrapper/producer propagation. |
| DL-2 execution evidence: nativeRunId, executable/DLL hashes, expanded UIDs, source/policy identity | Native child -> suite loop -> atomic summary -> report manifest/body and final result -> readiness reader | D-10 contains and drains children; D-12 publishes complete summary atomically. Missing/partial summary is incomplete; prelaunch producer manifest recovers a complete summary whose producer died before enqueue. | V-2/3/5 read persisted summary and actual discovery/TRX/diagnostics; R-3 pins final state/result/readiness. Local data propagation is not a delivered report. |
| DL-3 report: D-11 reportId + whole-body digest + part ordinal/digest, configured board/card, full run envelope | Prelaunch producer manifest -> immutable real disk outbox -> exclusive foreground consumer -> board discussion POST -> separate stored discussion GET -> durable receipt -> producer validation | Publish manifest before driver, summary before enqueue, flushed immutable intent before wake, attempt before POST, receipt before received. Real directory queue survives wake loss. Same StateRoot/database on process restart; read first on retry, identical duplicates coalesce, conflicts hold. | V-6/V-8 require independently fetched every part, exact reassembled canonical bytes, stored comment IDs and observation time, matching envelope and producer-consumed receipt. Green/no-incident follows the same route. Q-R repeats installed recovery. POST/exit zero/summary flag/watchdog receipt never proves this delivery. |
| DL-4 carried watchdog notices: outageId/nid/attempt, recipient/body hash and linked recovery nid | Evaluator -> SQLite ledger -> Telegram -> authorized reader -> receipt importer | Existing CARD-0545 durable intent, reader-first retry, held reader and crash cuts; unchanged code and PCs stay with that card. | Q-5/F-1..F-6 and production notice require complete recipient readback and correlated recovery. C545World is a substitute, not live delivery. |

DL-3 handoff matrix below is mandatory through the **real file queue** and private
real HTTP/DB recipient. The shorter Scripts tests also assert local intermediate
facts; the recipient integration cases must exercise those same cuts end-to-end.
Recovery-only invocations must record zero clone sync/test starts and preserve the
original report identity/body. On expected failures, later successful readback is
required as well as the pending-state assertion.

| Boundary or recipient state | Exact named coverage | Decisive before/restart/recipient assertion |
|---|---|---|
| Prelaunch manifest cannot publish; producer dies before summary; summary partially written | DeliveryTests.C1039_IntentBeforeWake / C1039_RecoverBeforeEnqueue; RecipientTests.C1039_QueueHandoffs | No driver after manifest refusal. No report for partial/missing summary; complete atomic summary eventually recovers to exact body, without test rerun. |
| Summary committed, enqueue fails or producer dies before enqueue | C1039_RecoverBeforeEnqueue / C1039_EnqueueFailure; C1039_QueueHandoffs | Manifest and summary remain; restart creates same reportId and independently reads complete board body. |
| Intent temporary write/flush/rename fails; intent committed before wake; intent corrupt/conflicting; retention | C1039_IntentBeforeWake / C1039_CommitBeforeWake / C1039_IntentIntegrity / C1039_RetentionPins; C1039_QueueHandoffs | No partial published item or POST. Restart scans committed directory without wake. Pending artifacts retained; corrupt/conflicting bytes held, repaired fixture resumes original bytes only. |
| Busy consumer or temporarily unavailable recipient, then eligible; consumer already eligible | C1039_BusyConsumer / C1039_AlreadyEligibleConsumer; RecipientTests.C1039_BusyAndEligibleRecipient | Hold actual file lock in A; B leaves committed intent pending. Release/restore endpoint and drain. Separate ready-from-start arm reaches full independent GET. |
| Attempt persistence fails; death after attempt before send; POST fails before store | RecipientTests.C1039_AttemptBeforeSend / C1039_SendBeforeAck | No send before attempt. Restart reader-first; absent part can be sent under same reportId, then observed complete. |
| Board commits but POST response lost; ack persisted then consumer dies | RecipientTests.C1039_SendBeforeAck | Stored bodies survive; restart GET imports them before retry. One logical report; identical delayed-commit duplicates allowed, never conflicting-body credit. |
| GET fails or returns malformed/partial/conflicting body | RecipientTests.C1039_UnknownReadNoSend / C1039_WholeBoardReceipt | Unknown is not absence. Pending/held, no blind resend, no delivery credit. Restored reader must fetch every exact part. |
| GET complete then receipt write fails; receipt committed before producer final state | RecipientTests.C1039_ReceiptBeforeFinalState | First cut stays pending and restart re-reads; second reuses durable matching receipt without POST. Final job/last-green do not advance retroactively. |
| Recovery front door/startup bounds and old-run receipt | DeliveryTests.C1039_RecoveryFrontDoor / C1039_RecoveryBounds / C1039_RecoveryDoesNotPromoteGreen | Same bounded consumer; 10 intents/120s HTTP work/finite per-request limit; durable remainder. Exact old body received; no completed-job rewrite, test rerun or new-run credit. |

In this table DeliveryTests abbreviates `NightlyReportDeliveryTests` and
RecipientTests abbreviates `NightlyReportRecipientTests`; unqualified names are
in the class named immediately before them. No session is a destination. If a
later implementation adds session input, this design must be amended to require
the matching complete **UserPrompt transcript**; an input request/ack cannot close
that path.

Substitutes and limits: C487 StartProcess proves decisions, not native custody,
quoting or elapsed time. Golden parser data proves parsing, not the current
Windows census. OS fault seams operate below the owner; the real suspended
launcher/wait/job/drain code and independently opened handles still run. The
stateful Windmill fixture proves reconciliation, not the installed API or real
scheduler. A loopback bridge with real discussion storage proves persisted board
receipt, not human reading or production deployment. C545World proves watchdog
persistence/matching with fake transport, not Telegram receipt. Q evidence cannot
be replaced by any of these substitutes.

### Proves it works now

These are ordinary Code obligations after implementation, not tests claimed run
by TestDesign. Exact method rosters and commands are in Checkpoints; every PC's
named oracle is also asserted in the corresponding ordinary method.

- V-1: all nightly drivers obey the existing host gate | production entry/library
  with private broker and controlled I/O, plus native integration | CP-5..7 |
  grant before each of the eight starts, literal CPU argv, timeout=4 with no
  child, renewal through custody, cleanup before release, one owner per driver.
- V-2: independent discovery admits an exact partition | production suite loop |
  CP-8/9 | discover once before execution; invalid census or changed executable/
  DLL refuses; exact filters; complete expanded UID union; later chunks survive
  ordinary red but never unresolved cleanup.
- V-3: Unit and phase receipts are honest | production persisted summary reader |
  CP-12 | metadata-based Unit subset, actual default nightly exclusions, exact
  counts/digest and identity; required skipped/failed/missing/duplicate rows never
  earn complete-green; non-Unit red remains nightly red; separate phase clocks.
- V-4: reconciliation uses installed content and fields | real registration entry,
  stateful disk-backed Windmill fixture | CP-26 | opaque revisions, initial full
  census, only 404 means missing, stored field equality, no extra writes on restart,
  disabled create and explicit enable with readback.
- V-5: Windows custody and interpreter boundaries work | actual adapter, sentinel,
  5.1/pwsh and staged TUnit probe | CP-1..4/7/9/24/25 | assignment before resume,
  independent root/job/stream observations, timeout/fault hold, literal argv,
  one hop/record, fixture never qualifies. Final integration rechecks after changed
  discovery/receipt code rather than relying on the early adapter checkpoint.
- V-6: complete report reaches stored board audience and recovers | real disk
  outbox, owned producer/consumer processes, private HTTP/PostgreSQL and independent
  GET | CP-15..20 | every DL-3 handoff and busy/ready recipient reaches exact
  complete stored bytes; pending/error paths cannot claim delivery.
- V-7: only measured partitions are admitted | pure production admission validator
  plus committed policy/independent M-3 roster | CP-10/11 | missing/inapplicable/
  incomplete inputs refuse, 2,880,000ms inclusive chunk threshold, fixed overhead
  included and morning forecast supported. Synthetic cases prove refusal logic;
  only M-0..M-3 receipts prove applicability and measured durations.
- V-8: producer consumes the right receipt and recovery stays bounded | production
  wrapper/bootstrap/report/receipt consumer | CP-21..25 | no exit-zero or summary
  shortcut, actual job/script/profile propagation, recovery without state promotion,
  final record remains compatible and follows all other stdout.

**Boundary combinations.** Each valid control is constructed independently of the
production formatter/comparator. Change one input at a time to reach the intended
guard, plus the coupled cases below; a complete Cartesian corruption product is
excluded because one earlier refusal would mask the later guard.

For V-1/V-5 cover granted/pid, granted/renew, busy, memory-floor, unavailable and
unlimited classifications, normal exit, nonzero exit, timeout and throw-after-start.
Hold root, descendant, stdout EOF, stderr EOF and final log write independently.
Fault assignment, query, termination and cleanup wait below the owner; release
fault barriers in finally, terminate/join only fixture-owned handles. Observe
renewals while held and no next driver or normal release. Test path spaces, empty
arguments, quotes, trailing backslashes, literal wildcard/OR tokens and sanitized
child environment. No process-name kill or test timeout can supply the red oracle.

V-2 uses separate discovery A.Rows(1), A.Rows(2), inherited/partial cases and
B.Plain, with a distinct AExtra class. Cover unconfigured all, valid A/B, missing
B, repeated A, unknown Z, empty configured chunk, changed DLL and changed executable,
nonzero/timeout/empty/missing/version/malformed/duplicate discovery. Use fresh TRX
with unrelated GUIDs, not fabricated GUID=expanded UID as the only success case.
Ordinary A-red still executes B; A-cleanup-held forbids B. Missing+red and
cross-chunk duplicate+missing cannot be hidden by aggregation. Pin TRX basename
and separate results-directory arguments (G144 semantics).

V-3 fixture: five discovered Unit UIDs, three eligible, two default exclusions,
one non-Unit UID. Three passes yield discovered=5, eligible=3, excluded=2,
executed=3, passed=3 and other counts zero. Exercise Failed, Skipped, NotExecuted,
InProgress, unknown state, unknown UID, empty eligible, duplicates within/across
chunks, metadata inherited/mixed/misleading names and mismatch between TRX and
diagnostics. Test UID permutation versus same-count replacement. Independently
cross each bound identity and stale/wrong-directory/previous-invocation evidence.
Nightly remains ProfileAware=false; RC uses its declared dispositions. Exclusions
retain actual reason/owner; an empty nightly exclusions array changes no policy.

V-4 fixture stores every owned script and schedule field, independent monotonic
opaque revision and durable revision count; restart over the same file. Alter
one stored field at a time, not the write response. For each of the three definition
positions test 401/403/503/transport/malformed-200 before writes. Exercise failed
write, committed/lost response and unavailable readback at script-create,
schedule-create and enable. Preserve competing parent revision and schedule drift.
Preview with unreadable token file still sends no token and performs no write.

V-6/V-8 bodies cover red and green/no-incident; framed limits 15,999/16,000/16,001
UTF-16 units, more than two parts, Unicode at a boundary, LF canonicalization,
trimmed outer whitespace and preserved inner whitespace. Reorder stored parts,
omit a middle part, duplicate identical parts, conflict an ordinal, alter per-part
or whole digest and every envelope field independently. Expected body is fixed
input bytes after the declared canonicalization, never output of the production
formatter used as its own oracle. Seed other board/card/run/lane reports with
matching-looking content. Ledger never enters incident selection; Backlog,
assigned-agent, owned-session, InProgress and RC incident cases assert actual
stored state. Receipt plus failed incident action is still overall red.

Timing admission boundaries are 2,879,999/2,880,000/2,880,001ms including measured
allowance, equal versus changed identity/applicability, complete ordinary-red
versus capped/incomplete timing, finite versus unknown queue allowance and an
otherwise valid partition forecast exceeding 08:00. PolicyPartition compares the
shipped configuration to the independent measured roster recorded in
`docs/investigations/2026-10-04-card-1039-partition-admission.md` during S3a, not to
an expected roster derived from that same policy. That Markdown retains original
receipt paths/digests and applicability; generated payloads remain external.

**Measurement receipt acceptance (no unit-test claim).** M-0 ends at its 30-minute
cap with per-class usable/historical/missing classification, original identity and
reason. M-1 is one clean Windows isolated build plus compiled list-tests only:
retain gated build/discovery argv, SHA/dirty=0, SDK/OS/tools, executable/DLL/policy
hashes, raw diagnostics, expanded UID/class/category census with reasons/digests,
slot wait, wall time and observed cleanup. Reparse originals to reproduce counts
and digest. Malformed/unknown/empty census refuses. Linux receipts, source counts,
probe inventory and 4,051 historical executions are inadmissible replacements.

Only after that census, freeze M-2.n's exact class filters, eligible expanded UID
rosters, counts, upper bounds and total dispatch budget. Every probe has at most
20 minutes execution plus bounded cleanup and a real host lease. A complete
assertion-red may be usable timing, never a green result; capped/incomplete rows
are lower bounds and go to CARD-1040. Do not launch another full assembly or Unit
battery. M-3 requires every eligible class exactly once and applicable complete
wall-time receipts, with the 20 percent chunk reserve and whole-run forecast.
After fixtures change discovery, refresh only added/affected classes and preserve
original provenance of reused timings. No M-2 filter/Min is guessed here: this
is an admission algorithm for a later bounded measurement commission, not an
unfilled runnable checkpoint row. S3a cannot be dispatched until M-3 is accepted.

Q-0..Q-6 and Q-R keep their operational acceptance above. Q-2 manual and Q-4 real
scheduled runs are explicitly commissioned full nightly runs after landed code;
they are not extra Code checkpoints and cannot discharge the CARD-1021 waiver.
Retain manual versus scheduled identity, native/job/report joins, seven suite
inventories, full Unit census, true receipt and before-08:00 completion. A report
recovered after an interrupted job does not qualify that job; await a future
scheduled green. Installed API and recipient readback remain mandatory.

### Guards the regression

- R-1: existing slot wrapper semantics | CP-6's four exact BuildSlotScriptTests
  methods | grant/command/release order, granted CPU cap, exit 4/no command and
  literal argv required named PASS inventories remain intact.
- R-2: profile/parser/census contracts | CP-11/13's five exact ReleaseGatePolicyTests
  methods | stale hash refuses, seven-suite/RC distinction, metadata retained,
  named expanded row missing refuses, declared RC exclusion reasons/owners required.
- R-3: readiness and completion identities remain strict | CP-14/23's exact
  NightlyVerificationContractTests methods | false coverage/tests/report is unready,
  crossed/native manual run refuses, missing job result unknown; final JSON and
  state flags/IDs match. C545_ResultLine must use a validated correlated fixture
  receipt; summary.reportDelivered=true alone is now a negative case.
- R-4: preview, disabled create, selector/drift and provenance | CP-26's four
  inspected C599 methods | zero writes/token in preview, preserved enabled state,
  explicit one-schedule enable and drift refusal. Text provenance pins alone cannot
  prove real scheduled delivery; V-8 and Q-4 supply those layers.
- R-5: bootstrap safety | CP-4/25 | exact ASCII and shared-tree guard methods;
  exit 3 names AllowSharedTree before WhatIf. ASCII roster expands to new scripts/
  adapter source. Actual interpreter behavior is V-5, not a source-text assertion.
- R-6: incident separation and safe closure | CP-18 and CP-23 | new recipient
  C1039_IncidentSeparation reuses T1..T7/G077..079/G099..104 scenarios through real
  storage; ledger is persistent and distinct, one incident is reused, green moves
  only its eligible unassigned Backlog incident, other lane untouched.

### Guard inventory

Each row is one independently bypassable guard, mapped 1:1 to its own PC. The
field comparisons are split even when implemented by a shared projection loop:
removing a field is a separate defect. Eight driver callsites likewise retain
separate controls. No `PC family` or unpriced hidden variant is admitted.
Unchanged watchdog internals and qualification orchestration are governed by
CARD-0545's existing PCs and Q receipts; this card does not mutate them.

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
| G-29 | S3b/D-2 zero terminal set cannot be complete | PC-29 |
| G-30 | S3b/D-2 required skipped row never earns green | PC-30 |
| G-31 | S3b/D-2 failed row never earns green | PC-31 |
| G-32 | S3b/D-2 duplicate terminal rows are invalid | PC-32 |
| G-33 | S3b/D-2 receipt SHA equality | PC-33 |
| G-34 | S3b/D-2 sorted UID digest covers the admitted set | PC-34 |
| G-35 | S3b/D-1/D-2 Unit success cannot override full nightly red | PC-35 |
| G-36 | S3b/D-5 duration fields describe separate phases | PC-36 |
| G-37 | S4/D-6 opaque revision is not a content digest | PC-37 |
| G-38 | S4/D-6 failed script read is unknown, never absent | PC-38 |
| G-39 | S4/D-6 failed schedule read is unknown, never absent | PC-39 |
| G-40 | S4/D-6 stored script content equality | PC-40 |
| G-41 | S4/D-6 stored schedule cron equality | PC-41 |
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
| G-53 | S3b/D-2 receipt ref equality | PC-53 |
| G-54 | S3b/D-2 receipt policyHash equality | PC-54 |
| G-55 | S3b/D-2 receipt assemblyHash equality | PC-55 |
| G-56 | S3b/D-2 receipt nativeRunId equality | PC-56 |
| G-57 | S3b/D-2 receipt jobId equality | PC-57 |
| G-58 | S3b/D-2 receipt scriptDigest equality | PC-58 |
| G-59 | S3b/D-2 receipt scriptRevision equality | PC-59 |
| G-60 | S4/D-6 stored script path equality | PC-60 |
| G-61 | S4/D-6 stored script language equality | PC-61 |
| G-62 | S4/D-6 stored script tag equality | PC-62 |
| G-63 | S4/D-6 stored script schema equality | PC-63 |
| G-64 | S4/D-6 stored script summary equality | PC-64 |
| G-65 | S4/D-6 stored script description equality | PC-65 |
| G-66 | S4/D-6 stored schedule path equality | PC-66 |
| G-67 | S4/D-6 stored schedule script_path equality | PC-67 |
| G-68 | S4/D-6 stored schedule timezone equality | PC-68 |
| G-69 | S4/D-6 stored schedule tag equality | PC-69 |
| G-70 | S4/D-6 stored schedule args equality | PC-70 |
| G-71 | S4/D-6 stored schedule is_flow equality | PC-71 |
| G-72 | S4/D-6 matching schedule preserves enablement | PC-72 |
| G-73 | S2/D-3 filter cannot match class prefixes | PC-73 |
| G-74 | S2/D-3 empty configured chunk is invalid | PC-74 |
| G-75 | S3b/D-2 terminal UID must be discovered | PC-75 |
| G-76 | S3b/D-2 zero eligible Unit cannot earn complete-green | PC-76 |
| G-77 | S3b/D-2 nightly default exclusions remain authoritative | PC-77 |
| G-78 | S3b/D-2 independent TRX/diagnostic agreement | PC-78 |
| G-79 | S2/D-3 timeout discovery refuses before execution | PC-79 |
| G-80 | S2/D-3 empty discovery refuses before execution | PC-80 |
| G-81 | S2/D-3 missing discovery refuses before execution | PC-81 |
| G-82 | S2/D-3 version discovery refuses before execution | PC-82 |
| G-83 | S2/D-3 malformed discovery refuses before execution | PC-83 |
| G-84 | S2/D-3 duplicate-discovery discovery refuses before execution | PC-84 |
| G-85 | S3b/D-2 InProgress never earns complete-green | PC-85 |
| G-86 | S3b/D-2 NotExecuted never earns complete-green | PC-86 |
| G-87 | S3b/D-2 Unknown never earns complete-green | PC-87 |
| G-88 | S3b/D-2 wrong-run-directory is rejected | PC-88 |
| G-89 | S3b/D-2 stale-timestamp is rejected | PC-89 |
| G-90 | S3b/D-2 previous-invocation is rejected | PC-90 |
| G-91 | S1n-a/D-10 suspended root until assignment | PC-91 |
| G-92 | S1n-a/D-10 no spawn-before-assignment race | PC-92 |
| G-93 | S1n-a/D-10 job forbids breakaway | PC-93 |
| G-94 | S1n-a/D-10 root signal is independently required | PC-94 |
| G-95 | S1n-a/D-10 root exit is not descendant exit | PC-95 |
| G-96 | S1n-a/D-10 stdout EOF required | PC-96 |
| G-97 | S1n-a/D-10 stderr EOF required | PC-97 |
| G-98 | S1n-a/D-10 final log write completion required | PC-98 |
| G-99 | S1n-b/D-10 timeout terminates owned job | PC-99 |
| G-100 | S1n-b/D-10 failed termination is not normal completion | PC-100 |
| G-101 | S1n-b/D-10 unknown job query is not empty | PC-101 |
| G-102 | S1n-b/D-10 cleanup wait expiry is unresolved | PC-102 |
| G-103 | S1n-b/D-10 output failure is unresolved | PC-103 |
| G-104 | S1n-b/D-10 kill-on-close fallback stays armed | PC-104 |
| G-105 | S1n-b/D-10 fixture execution cannot qualify | PC-105 |
| G-106 | S1n-b/D-10 same executable resolution for discovery and execution | PC-106 |
| G-107 | S1/D-10 native cleanup precedes lease release | PC-107 |
| G-108 | S1/D-10 held custody retains foreground owner and renewals | PC-108 |
| G-109 | S1n-c/D-10 bootstrap uses PowerShell 7 | PC-109 |
| G-110 | S1n-c/D-10 hop preserves all bound parameters | PC-110 |
| G-111 | S1n-c/D-10 exactly one interpreter hop | PC-111 |
| G-112 | S1n-c/D-10 missing supported pwsh refuses | PC-112 |
| G-113 | S1n-a/D-10 native argv remain literal | PC-113 |
| G-114 | S3a/D-9 every class needs a timing | PC-114 |
| G-115 | S3a/D-9 only complete UID timing is admissible | PC-115 |
| G-116 | S3a/D-9 20 percent headroom | PC-116 |
| G-117 | S3a/D-9 measured invocation/cleanup allowance is included | PC-117 |
| G-118 | S3a/D-9 timing source applicability | PC-118 |
| G-119 | S3a/D-9 whole-run forecast includes queues and fixed phases | PC-119 |
| G-120 | S3a/D-9 unknown queue allowance cannot prove deadline | PC-120 |
| G-121 | S3a/D-9 native timeout cap unchanged | PC-121 |
| G-122 | S3a/D-9 checked-in partition is the admitted measured partition | PC-122 |
| G-123 | S3c-a/D-12 producer manifest precedes launch | PC-123 |
| G-124 | S3c-a/D-12 immutable intent precedes wake/POST | PC-124 |
| G-125 | S3c-a/D-12 flushed files precede atomic publication | PC-125 |
| G-126 | S3c-a/D-12 complete summary without enqueue is recoverable | PC-126 |
| G-127 | S3c-a/D-12 partial summary never becomes report success | PC-127 |
| G-128 | S3c-a/D-12 intent body immutable under reportId | PC-128 |
| G-129 | S3c-a/D-12 corrupt persisted intent is held | PC-129 |
| G-130 | S3c-a/D-12 pending producer evidence survives retention | PC-130 |
| G-131 | S3c-a/D-12 lock serializes consumers | PC-131 |
| G-132 | S3c-a/D-11 complete ordered multipart receipt | PC-132 |
| G-133 | S3c-a/D-11 part conflicts hold | PC-133 |
| G-134 | S3c-a/D-11 parts respect 16000 UTF-16-unit API limit | PC-134 |
| G-135 | S3c-a/D-11 part framing preserves whitespace | PC-135 |
| G-136 | S3c-a/D-11 part digests are validated | PC-136 |
| G-137 | S3c-a/D-11 whole-body digest is validated | PC-137 |
| G-138 | S3c-a/D-11 report identity is deterministic and unambiguous | PC-138 |
| G-139 | S3c-b/D-11 green without incident still delivers | PC-139 |
| G-140 | S3c-b/D-11 stored body alone proves receipt | PC-140 |
| G-141 | S3c-b/D-11 receipt reader uses configured card | PC-141 |
| G-142 | S3c-b/D-11 profile/card must resolve to configured board | PC-142 |
| G-143 | S3c-b/D-11 missing profile never chooses another recipient | PC-143 |
| G-144 | S3c-b/D-11 ledger is not an incident | PC-144 |
| G-145 | S3c-b/D-12 unknown GET never proves absence | PC-145 |
| G-146 | S3c-c/D-12 enqueue failure stays pending | PC-146 |
| G-147 | S3c-c/D-12 committed intent survives lost wake | PC-147 |
| G-148 | S3c-c/D-12 already eligible consumer drains work | PC-148 |
| G-149 | S3c-c/D-12 attempt intent precedes POST | PC-149 |
| G-150 | S3c-c/D-12 reader-first lost-ack recovery | PC-150 |
| G-151 | S3c-c/D-12 absent-part send failure remains retryable | PC-151 |
| G-152 | S3c-c/D-12 GET observation is durably published | PC-152 |
| G-153 | S3c-c/D-12 receipt survives producer death | PC-153 |
| G-154 | S3c-d/D-12 producer must consume receipt | PC-154 |
| G-155 | S3c-d/D-12 summary cannot assert delivery | PC-155 |
| G-156 | S3c-d/D-12 NoReport never grants receipt | PC-156 |
| G-157 | S3c-d/D-12 DryRun never grants receipt | PC-157 |
| G-158 | S3c-d/D-12 recovery never promotes old green | PC-158 |
| G-159 | S3c-d/D-12 startup uses same bounded consumer | PC-159 |
| G-160 | S3c-d/D-12 recovery count bound | PC-160 |
| G-161 | S3c-d/D-12 recovery wall bound | PC-161 |
| G-162 | S3c-d/D-12 finite HTTP request timeout | PC-162 |
| G-163 | S3c-d/D-7 Windmill job identity propagates | PC-163 |
| G-164 | S3c-d/D-7 scheduled run requires actual job identity | PC-164 |
| G-165 | S3c-d/D-7 tokens cannot become commands | PC-165 |
| G-166 | S3c-d/D-11 incident write failure prevents green | PC-166 |
| G-167 | S3c-b/D-11 receipt schemaVersion equality | PC-167 |
| G-168 | S3c-b/D-11 receipt boardId equality | PC-168 |
| G-169 | S3c-b/D-11 receipt cardId equality | PC-169 |
| G-170 | S3c-b/D-11 receipt profile equality | PC-170 |
| G-171 | S3c-b/D-11 receipt trigger equality | PC-171 |
| G-172 | S3c-b/D-11 receipt dueDay equality | PC-172 |
| G-173 | S3c-b/D-11 receipt jobId equality | PC-173 |
| G-174 | S3c-b/D-11 receipt nativeRunId equality | PC-174 |
| G-175 | S3c-b/D-11 receipt sha equality | PC-175 |
| G-176 | S3c-b/D-11 receipt ref equality | PC-176 |
| G-177 | S3c-b/D-11 receipt policyHash equality | PC-177 |
| G-178 | S3c-b/D-11 receipt scriptDigest equality | PC-178 |
| G-179 | S3c-b/D-11 receipt scriptRevision equality | PC-179 |
| G-180 | S3c-b/D-11 receipt assemblyDigest equality | PC-180 |
| G-181 | S3c-b/D-11 receipt censusDigest equality | PC-181 |
| G-182 | S3c-b/D-11 receipt bodyDigest equality | PC-182 |
| G-183 | S4/D-6 full read census before any apply write | PC-183 |
| G-184 | S1/D-4 unleased is diagnostic only | PC-184 |
| G-185 | S2/D-3 executable and DLL both remain bound | PC-185 |
| G-186 | S3c-c/D-12 retry after receipt-write failure uses reader | PC-186 |
| G-187 | S3c-d/D-7 schedule context controls trigger | PC-187 |
| G-188 | S3c-b/D-11 safe incident closure requires Backlog | PC-188 |
| G-189 | S3c-b/D-11 safe incident closure requires no assigned agent | PC-189 |
| G-190 | S3c-b/D-11 safe incident closure requires no owner session | PC-190 |
| G-191 | S3c-b/D-11 RC/nightly incidents remain distinct | PC-191 |
| G-192 | S3a/D-9 timing assemblyHash applicability | PC-192 |
| G-193 | S3a/D-9 timing classDigest applicability | PC-193 |
| G-194 | S3a/D-9 timing hooksDigest applicability | PC-194 |
| G-195 | S3a/D-9 timing dependencyDigest applicability | PC-195 |
| G-196 | S3a/D-9 timing toolVersions applicability | PC-196 |
| G-197 | S3a/D-9 timing os applicability | PC-197 |
| G-198 | S3c-a/D-11 Unicode boundaries preserved | PC-198 |
| G-199 | S3c-a/D-12 summary publication is atomic | PC-199 |
| G-200 | S3a/D-1 full suite inventory is preserved | PC-200 |
| G-201 | S4/D-6 lost schedule-create response reconciles stored state | PC-201 |
| G-202 | S4/D-6 lost enable response reconciles stored state | PC-202 |
| G-203 | S3c-d/D-11 report profile reaches producer and recovery | PC-203 |

### Positive controls

Mutation runs **break/red/restore/green after land**, on the commissioned immutable
SourceLanding source. Code runs V/R; Review judges all recipes before land. Each
row below means: break its identically numbered G by the listed compiling defect;
run exactly `/*/*/ClassName/ExactMethodName` from the row; expect red at the stated
assertion. Restore, fresh isolated build and run that same exact method green.
No whole class/namespace/Unit filter is allowed for a PC. PowerShell parse failure,
C# build error, fixture setup failure, timeout before the assertion or zero selected
tests is not red. All internal valid/invalid arms execute for that method on both
runs, but each PC has only the one listed mutation. No production repair occurs in
a mutation snapshot.

Sites: slot/chunk/Unit consumer defects are in `nightly-tests-impl.ps1` or its
named `nightly-coverage.ps1` helper; registration defects in
`register-release-gates.ps1`; native defects in D-10's new `.cs` adapter/`.ps1`
owner; interpreter defects in `nightly-tests.ps1`/run bootstrap; outbox/receipt
consumer defects in D-12's new `nightly-report-delivery.ps1` or its named producer/
reporter callsite; wrapper defects in the nightly Windmill definition content.
Use the exact boundary described, never alter expected assertions to create red.
For new code these are implementation acceptance recipes, with fully specified
seams/methods/oracles; they are not claims that the methods exist at this source.

| PC | Compiling defect in matching G | Exact method expected red | Decisive assertion |
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
| PC-20 | ignore nonzero discovery ExitCode while preserving valid diagnostic parsing | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | nonzero discovery with otherwise valid diagnostics: zero execution |
| PC-21 | skip the assembly hash comparison | NightlyChunkAdmissionTests.C1039_BuildBinding | changed DLL after discovery: execution refused |
| PC-22 | drop the B operand from Get-NightlyNativeExecutionArguments | NightlyChunkAdmissionTests.C1039_LiteralFilter | captured argv and executed class set both equal A+B |
| PC-23 | accept union.Ok=false | NightlyChunkAdmissionTests.C1039_ExpandedUnion | one missing argument UID: incomplete and exact UID named |
| PC-24 | deduplicate terminal rows before overlap validation | NightlyChunkAdmissionTests.C1039_ExpandedUnion | same UID in two chunks: duplicate count 1 and incomplete |
| PC-25 | break the chunk loop on a nonzero ordinary test exit | NightlyChunkAdmissionTests.C1039_ContinueAfterRed | B execution and B evidence exist after A fails |
| PC-26 | advance to chunk B while A cleanup is held | NightlyChunkAdmissionTests.C1039_SerialChildren | B starts only after A cleanup releases; maximum active native=1 |
| PC-27 | classify by method/class name containing Unit | NightlyUnitReceiptTests.C1039_Categories | misleading Unit name excluded; inherited Unit metadata included |
| PC-28 | join required UID directly to TRX testId | NightlyUnitReceiptTests.C1039_TerminalRows | two argument UIDs with unrelated TRX GUIDs: executed=2 |
| PC-29 | treat an empty terminal set as complete | NightlyUnitReceiptTests.C1039_TerminalRows | discovery-only: complete=false and passed=0 |
| PC-30 | normalize Skipped to Passed | NightlyUnitReceiptTests.C1039_TerminalRows | required skipped UID: skipped=1 and complete-green=false |
| PC-31 | ignore failed Unit terminal outcomes | NightlyUnitReceiptTests.C1039_TerminalRows | failed UID: failed=1 and complete-green=false |
| PC-32 | collapse duplicate terminal UIDs before counting | NightlyUnitReceiptTests.C1039_DuplicateRows | duplicate UID: duplicate=1 and complete-green=false |
| PC-33 | omit only SHA from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed SHA alone is rejected |
| PC-34 | hash only the count rather than sorted UIDs | NightlyUnitReceiptTests.C1039_Digest | same-count UID replacement changes digest; permutation does not |
| PC-35 | replace overall result with the Unit result | NightlyUnitReceiptTests.C1039_FullNightly | Unit green plus non-Unit red: nightly stays red |
| PC-36 | write total wall time into childElapsedSeconds | NightlyUnitReceiptTests.C1039_PhaseDurations | controlled slot/child/cleanup intervals stay distinct |
| PC-37 | restore liveHash==desiredDigest comparison | ReleaseGateRegistrationTests.C1039_OpaqueRevision | identical content, opaque revision: second apply writes=0 |
| PC-38 | classify failed scripts/get as missing | ReleaseGateRegistrationTests.C1039_UnknownReads | 401/403/503/transport/invalid-200: zero writes |
| PC-39 | classify failed schedules/get as missing | ReleaseGateRegistrationTests.C1039_UnknownReads | 401/403/503/transport/invalid-200: zero writes |
| PC-40 | omit content from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered stored content: apply fails |
| PC-41 | omit schedule from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | stored cron differs: apply fails without overwriting drift |
| PC-42 | return success immediately after setenabled POST | ReleaseGateRegistrationTests.C1039_EnableReadback | accepted-but-not-stored enable: failure and no enabled receipt |
| PC-43 | unconditionally repost the desired script during a restarted apply despite matching GET content | ReleaseGateRegistrationTests.C1039_RestartApply | each committed cut: one stored object per path and no extra script revision |
| PC-44 | omit parent_hash on an update | ReleaseGateRegistrationTests.C1039_OpaqueRevision | fixture changes revision before POST: refuses and preserves competing content |
| PC-45 | fall through preview into Apply writes | ReleaseGateRegistrationTests.C599_Preview | a preview performs zero writes |
| PC-46 | load tokenFile into preview context | ReleaseGateRegistrationTests.C599_Preview | a preview sends no token |
| PC-47 | copy desired enabled=true into create payload | ReleaseGateRegistrationTests.C599_ApplyReadback | nightly schedule was created DISABLED |
| PC-48 | enable rc while enabling readiness | ReleaseGateRegistrationTests.C599_ApplyReadback | enabling readiness did not enable rc |
| PC-49 | allow manual native trigger in the readiness predicate | NightlyVerificationContractTests.C544_ScheduledIdentity | native trigger must be scheduled |
| PC-50 | remove coverageComplete from native green admission | NightlyVerificationContractTests.C544_CoverageRequired | coverageComplete=false is unready |
| PC-51 | remove testsPassed from native green admission | NightlyVerificationContractTests.C544_GreenRequired | testsPassed=false is unready |
| PC-52 | remove reportDelivered from native green admission | NightlyVerificationContractTests.C544_ReportReceiptRequired | reportDelivered=false is unready |
| PC-53 | omit only ref from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed ref alone is rejected |
| PC-54 | omit only policyHash from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed policyHash alone is rejected |
| PC-55 | omit only assemblyHash from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed assemblyHash alone is rejected |
| PC-56 | omit only nativeRunId from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed nativeRunId alone is rejected |
| PC-57 | omit only jobId from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed jobId alone is rejected |
| PC-58 | omit only scriptDigest from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed scriptDigest alone is rejected |
| PC-59 | omit only scriptRevision from the receipt comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | changed scriptRevision alone is rejected |
| PC-60 | omit only path from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered path alone: apply fails |
| PC-61 | omit only language from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered language alone: apply fails |
| PC-62 | omit only tag from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered tag alone: apply fails |
| PC-63 | omit only schema from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered schema alone: apply fails |
| PC-64 | omit only summary from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered summary alone: apply fails |
| PC-65 | omit only description from the owned-script projection | ReleaseGateRegistrationTests.C1039_ScriptFields | accepted write with altered description alone: apply fails |
| PC-66 | omit only path from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered path alone: apply fails without overwriting drift |
| PC-67 | omit only script_path from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered script_path alone: apply fails without overwriting drift |
| PC-68 | omit only timezone from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered timezone alone: apply fails without overwriting drift |
| PC-69 | omit only tag from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered tag alone: apply fails without overwriting drift |
| PC-70 | omit only args from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered args alone: apply fails without overwriting drift |
| PC-71 | omit only is_flow from the owned-schedule projection | ReleaseGateRegistrationTests.C1039_ScheduleFields | altered is_flow alone: apply fails without overwriting drift |
| PC-72 | post enabled=false on every matching reapply | ReleaseGateRegistrationTests.C599_ApplyReadback | a reapply PRESERVES an already enabled schedule |
| PC-73 | restore ClassName* selection for A beside AExtra | NightlyChunkAdmissionTests.C1039_LiteralFilter | A assigned: argv selects A exactly and AExtra never executes |
| PC-74 | allow an empty classes list in a configured chunk | NightlyChunkAdmissionTests.C1039_Admission | configured empty chunk refused; unconfigured all invocation still works |
| PC-75 | ignore terminal UIDs outside the discovery set | NightlyUnitReceiptTests.C1039_TerminalRows | unknown UID named and complete-green=false |
| PC-76 | return complete-green=true for an empty eligible set | NightlyUnitReceiptTests.C1039_TerminalRows | zero eligible: complete-green=false |
| PC-77 | force ProfileAware=true for nightly receipt census | NightlyUnitReceiptTests.C1039_Categories | OptIn/Explicit remain excluded with category-optin/nightly-default |
| PC-78 | accept Test-NightlyTrxDiagnosticCrossCheck.Ok=false | NightlyUnitReceiptTests.C1039_TerminalRows | equal UID set but missing TRX argument row: incomplete |
| PC-79 | ignore TimedOut with otherwise valid diagnostics | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | timeout: zero native execution starts; named discovery refusal |
| PC-80 | allow Nodes.Count=0 | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | empty: zero native execution starts; named discovery refusal |
| PC-81 | substitute configured classes when the diagnostic file is missing | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | missing: zero native execution starts; named discovery refusal |
| PC-82 | bypass the pinned diagnostic version check | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | version: zero native execution starts; named discovery refusal |
| PC-83 | drop the invalid record in the diagnostic parser | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | malformed: zero native execution starts; named discovery refusal |
| PC-84 | deduplicate discovery UIDs instead of rejecting | NightlyChunkAdmissionTests.C1039_DiscoveryFailure | duplicate-discovery: zero native execution starts; named discovery refusal |
| PC-85 | normalize InProgress to Passed | NightlyUnitReceiptTests.C1039_TerminalRows | InProgress UID does not count as passed; complete-green=false |
| PC-86 | normalize NotExecuted to Passed | NightlyUnitReceiptTests.C1039_TerminalRows | NotExecuted UID does not count as passed; complete-green=false |
| PC-87 | normalize Unknown to Passed | NightlyUnitReceiptTests.C1039_TerminalRows | Unknown UID does not count as passed; complete-green=false |
| PC-88 | skip the run-directory containment check | NightlyUnitReceiptTests.C1039_IdentityBinding | wrong-run-directory: receipt refused although counts match |
| PC-89 | skip the evidence timestamp check | NightlyUnitReceiptTests.C1039_IdentityBinding | stale-timestamp: receipt refused although counts match |
| PC-90 | skip the invocation marker comparison | NightlyUnitReceiptTests.C1039_IdentityBinding | previous-invocation: receipt refused although counts match |
| PC-91 | resume the root despite AssignProcessToJobObject failure | NightlyNativeOwnershipTests.C1039_AssignBeforeResume | assignment refused: fixture start marker absent and root handle signalled |
| PC-92 | omit CREATE_SUSPENDED when creating the root | NightlyNativeOwnershipTests.C1039_AssignBeforeResume | assignment barrier held: neither root marker nor descendant exists |
| PC-93 | enable JOB_OBJECT_LIMIT_BREAKAWAY_OK | NightlyNativeOwnershipTests.C1039_DescendantExit | fixture breakaway request cannot leave an independently live descendant |
| PC-94 | treat job active-count zero as sufficient without root wait | NightlyNativeOwnershipTests.C1039_DescendantExit | held root wait: ChildrenExited=false despite zero-count observation |
| PC-95 | set ChildrenExited from root.HasExited alone | NightlyNativeOwnershipTests.C1039_DescendantExit | root exited/child held: no clean return until child handle signals |
| PC-96 | ignore the stdout reader completion in OutputDrained | NightlyNativeOwnershipTests.C1039_DrainBeforeReturn | held stdout EOF: OutputDrained=false and no successful return |
| PC-97 | ignore the stderr reader completion in OutputDrained | NightlyNativeOwnershipTests.C1039_DrainBeforeReturn | held stderr EOF: OutputDrained=false and no successful return |
| PC-98 | return OutputDrained=true before final log flush completes | NightlyNativeOwnershipTests.C1039_DrainBeforeReturn | held final write: no return; released log contains both terminal sentinels |
| PC-99 | omit TerminateJobObject on the timeout path | NightlyNativeOwnershipTests.C1039_NativeTimeout | timeout arm records termination before fallback and root/child handles both signal |
| PC-100 | normalize TerminateJobObject failure to successful cleanup | NightlyNativeOwnershipTests.C1039_CleanupUnknownHolds | termination-failed arm: red/held, next driver starts=0 |
| PC-101 | convert a failed active-process query to count zero | NightlyNativeOwnershipTests.C1039_CleanupUnknownHolds | query-failed arm: CleanupComplete=false, next starts=0 |
| PC-102 | treat root/child join timeout as success | NightlyNativeOwnershipTests.C1039_CleanupUnknownHolds | join-expired arm: held cleanup, retained logs and no next driver |
| PC-103 | catch a redirected reader exception and set OutputDrained=true | NightlyNativeOwnershipTests.C1039_CleanupUnknownHolds | reader-error arm: cleanup incomplete and logs retained |
| PC-104 | omit JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | NightlyNativeOwnershipTests.C1039_NativeTimeout | faulted termination then close: independently opened descendant handle signals |
| PC-105 | clear the fixture-resolution-active disqualification bit | NightlyNativeOwnershipTests.C1039_NativeFixtureLoop | real staged probe evidence exists but qualificationEligible=false |
| PC-106 | ignore ResolveNativeExecutable for execution only | NightlyNativeOwnershipTests.C1039_NativeFixtureLoop | observed discovery/execution executable path and hashes are identical staged values |
| PC-107 | release native lease after root exit before descendant/drain completion | NightlyNativeOwnershipTests.C1039_LeaseAfterNativeCleanup | DELETE follows independent descendant signal and both output sentinels |
| PC-108 | return from held cleanup and dispose the renewal job | NightlyNativeOwnershipTests.C1039_LeaseAfterNativeCleanup | held query/drain: owner alive, renewals continue, DELETE and next-start absent |
| PC-109 | launch driver with powershell.exe instead of resolved pwsh | NightlyInterpreterTests.C1039_BootstrapUsesPwsh | fixture observes major version >=7 before gate import |
| PC-110 | omit RunId from the hop argument map | NightlyInterpreterTests.C1039_DirectEntryHopsOnce | literal argv including RunId matches input and single final record names it |
| PC-111 | continue in the 5.1 parent after the child returns | NightlyInterpreterTests.C1039_DirectEntryHopsOnce | one driver start and one final completion record |
| PC-112 | fall back to the current 5.1 interpreter when pwsh resolution fails | NightlyInterpreterTests.C1039_MissingPwshRefuses | nonzero refusal before any driver marker; missing and version-5 executable arms |
| PC-113 | join native tokens with spaces without Windows escaping | NightlyNativeOwnershipTests.C1039_AssignBeforeResume | space/empty/quote/backslash/filter tokens equal independently expected argv |
| PC-114 | default a missing timing to zero | NightlyPartitionTimingTests.C1039_MissingTimingRefuses | missing B refuses; exact complete A+B map passes |
| PC-115 | ignore incomplete UID execution in timing input | NightlyPartitionTimingTests.C1039_MissingTimingRefuses | capped probe with a missing expanded UID refuses |
| PC-116 | use 100 percent instead of 80 percent of 3600000 ms | NightlyPartitionTimingTests.C1039_OverBudgetRefuses | 2880001 ms refuses; 2880000 ms is admitted |
| PC-117 | sum class wall time without the measured allowance | NightlyPartitionTimingTests.C1039_OverBudgetRefuses | class sum under threshold plus allowance over it refuses |
| PC-118 | omit source applicability from timing validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed source without explicit dependency applicability: refused |
| PC-119 | drop slot/build/discovery costs from the forecast | NightlyPartitionTimingTests.C1039_OverBudgetRefuses | otherwise fitting chunks with forecast after 08:00 do not admit qualification |
| PC-120 | coerce missing queue bound to zero | NightlyPartitionTimingTests.C1039_OverBudgetRefuses | unknown queue input: deadline claim is inadmissible |
| PC-121 | change antiphon watchdog to 7200000 and recompute policyHash | NightlyPartitionTimingTests.C1039_PolicyPartition | shipped antiphon watchdog must equal 3600000 |
| PC-122 | remove one measured class from chunks.antiphon and recompute policyHash | NightlyPartitionTimingTests.C1039_PolicyPartition | committed roster digest equals admitted M-3 roster digest |
| PC-123 | launch driver before publishing report-producers manifest | NightlyReportDeliveryTests.C1039_IntentBeforeWake | manifest publication failure: zero driver starts |
| PC-124 | wake consumer before outbox atomic rename | NightlyReportDeliveryTests.C1039_IntentBeforeWake | held rename: zero wakes/POSTs and no committed intent visible |
| PC-125 | omit Flush(true) before publishing intent | NightlyReportDeliveryTests.C1039_IntentBeforeWake | file-operation observer records flush before rename for body/intent; crash leaves no partial published entry |
| PC-126 | skip report-producers scan during recovery | NightlyReportDeliveryTests.C1039_RecoverBeforeEnqueue | producer death after complete summary: same reportId queued on restart |
| PC-127 | accept incomplete summary publication as ready | NightlyReportDeliveryTests.C1039_RecoverBeforeEnqueue | partial/missing summary: pending reason and zero POSTs |
| PC-128 | overwrite an existing reportId body with a recomposed changed body | NightlyReportDeliveryTests.C1039_IntentIntegrity | same ID/different bytes: conflict held; original digest and bytes unchanged |
| PC-129 | skip persisted intent/body digest validation | NightlyReportDeliveryTests.C1039_IntentIntegrity | torn/missing/mismatched intent: held with reason and zero send |
| PC-130 | remove a pending manifest summary during retention cleanup | NightlyReportDeliveryTests.C1039_RetentionPins | aged pending summary/body remain readable and recoverable |
| PC-131 | continue consumption when exclusive file lock acquisition fails | NightlyReportDeliveryTests.C1039_BusyConsumer | consumer B sends zero while A owns lock; committed intent stays pending |
| PC-132 | accept available parts without requiring every ordinal | NightlyReportDeliveryTests.C1039_ReportParts | missing middle part: received=false |
| PC-133 | take first part when same ordinal has different digest | NightlyReportDeliveryTests.C1039_ReportParts | conflicting ordinal held; identical duplicates coalesce |
| PC-134 | split payload at 16000 without reserving frame/header length | NightlyReportDeliveryTests.C1039_ReportParts | every framed part.Length<=16000 at limit-1/limit/limit+1 |
| PC-135 | omit suffix framing | NightlyReportDeliveryTests.C1039_ReportParts | API trim simulation preserves exact canonical body bytes after reassembly |
| PC-136 | skip per-part digest comparison | NightlyReportDeliveryTests.C1039_ReportParts | one altered part rejected even when header whole digest matches |
| PC-137 | skip reassembled whole digest comparison | NightlyReportDeliveryTests.C1039_ReportParts | valid part digests but wrong declared whole digest: received=false |
| PC-138 | hash delimiter-joined values without the versioned canonical envelope | NightlyReportDeliveryTests.C1039_IntentIntegrity | ambiguous field-boundary pair gets distinct IDs; reordered equivalent input gets same ID |
| PC-139 | return early for green and zero open incidents | NightlyReportRecipientTests.C1039_GreenWithoutIncident | no incident created; independent GET contains entire correlated ledger report |
| PC-140 | construct receipt from POST response without a separate GET | NightlyReportRecipientTests.C1039_WholeBoardReceipt | accepted-but-unreadable/altered stored body: reportDelivered=false |
| PC-141 | read a matching report from a different ledger card | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | matching bytes on other card do not satisfy recipient receipt |
| PC-142 | omit ledgerCard.BoardId comparison at profile validation | NightlyReportRecipientTests.C1039_ReportProfile | card on other board: no writes and explicit refusal |
| PC-143 | fall back to creating a ledger card when profile resolution fails | NightlyReportRecipientTests.C1039_ReportProfile | missing/malformed profile: zero writes and pending/refused |
| PC-144 | include configured ledger in nightly incident selection | NightlyReportRecipientTests.C1039_IncidentSeparation | ledger content/status/labels unchanged by red update and green close |
| PC-145 | turn failed discussion GET into an empty successful list | NightlyReportRecipientTests.C1039_UnknownReadNoSend | 401/403/404/503/transport/malformed GET: zero POSTs; intent remains pending |
| PC-146 | delete producer manifest when outbox rename fails | NightlyReportDeliveryTests.C1039_EnqueueFailure | failed enqueue retains complete summary; restart delivers same ID without tests |
| PC-147 | scan only volatile wake IDs after restart | NightlyReportDeliveryTests.C1039_CommitBeforeWake | crash after rename/before wake: real directory scan delivers unchanged body |
| PC-148 | skip drain when lock is immediately available | NightlyReportDeliveryTests.C1039_AlreadyEligibleConsumer | immediately ready consumer completes same report through independent GET |
| PC-149 | POST before persisting attempt intent | NightlyReportRecipientTests.C1039_AttemptBeforeSend | attempt persistence failure: no POST, durable report still pending |
| PC-150 | blindly retry POST before discussion GET | NightlyReportRecipientTests.C1039_SendBeforeAck | after committed POST/lost response: one stored part per ordinal and receipt from GET on restart |
| PC-151 | mark part received after a failed POST | NightlyReportRecipientTests.C1039_SendBeforeAck | failure before board commit: pending then same ID delivered after recovery |
| PC-152 | mark received before atomic receipt publication | NightlyReportRecipientTests.C1039_ReceiptBeforeFinalState | failure after GET/before receipt rename: no received state; restart rereads full body |
| PC-153 | delete receipt on consumer restart | NightlyReportRecipientTests.C1039_ReceiptBeforeFinalState | crash after receipt rename/before final state: same receipt/body retained without resend |
| PC-154 | restore exit-zero fallback when receipt is absent | NightlyReportDeliveryTests.C1039_ExitZeroIsNotReceipt | report exit 0 without receipt: reportDelivered=false and nonzero run |
| PC-155 | restore summary.reportDelivered override | NightlyReportDeliveryTests.C1039_ExitZeroIsNotReceipt | summary true without receipt: reportDelivered=false |
| PC-156 | allow valid old receipt to override NoReport | NightlyReportDeliveryTests.C1039_NoReportNoReceipt | NoReport: reportDelivered=false and no green state |
| PC-157 | allow valid receipt to override DryRun | NightlyReportDeliveryTests.C1039_NoReportNoReceipt | DryRun: reportDelivered=false and no report write |
| PC-158 | write last-complete-green after recovering an old report | NightlyReportDeliveryTests.C1039_RecoveryDoesNotPromoteGreen | old receipt received; final job/state and green file byte-identical; no clone/test launch |
| PC-159 | omit startup RecoverPending call | NightlyReportDeliveryTests.C1039_RecoveryFrontDoor | pending earlier report received before current driver start without old tests |
| PC-160 | raise scan batch limit from 10 to 11 | NightlyReportDeliveryTests.C1039_RecoveryBounds | 11 eligible intents: at most 10 attempted this wake, remainder durable |
| PC-161 | ignore the two-minute recovery HTTP-work deadline | NightlyReportDeliveryTests.C1039_RecoveryBounds | controlled monotonic clock at 120s: no new request; remaining work pending |
| PC-162 | omit request timeout from delivery HTTP call | NightlyReportDeliveryTests.C1039_RecoveryBounds | held endpoint cancels within configured request bound and persists next reason |
| PC-163 | omit WM_JOB_ID from wrapper hop | NightlyReportDeliveryTests.C1039_ProducerIdentity | executed wrapper/argv recorder and producer manifest carry exact jobId |
| PC-164 | accept scheduled trigger with empty jobId | NightlyReportDeliveryTests.C1039_ProducerIdentity | missing scheduled jobId refuses credit; local manual run records absent job explicitly |
| PC-165 | skip validation of jobId before SSH command construction | NightlyReportDeliveryTests.C1039_ProducerIdentity | invalid token refuses before owned ssh recorder; no injected sentinel executes |
| PC-166 | ignore incident reconciliation failure after ledger receipt | NightlyReportRecipientTests.C1039_IncidentSeparation | complete ledger receipt plus failed required incident write: overall red |
| PC-167 | omit only schemaVersion from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only schemaVersion differs: received=false and producer refuses credit |
| PC-168 | omit only boardId from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only boardId differs: received=false and producer refuses credit |
| PC-169 | omit only cardId from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only cardId differs: received=false and producer refuses credit |
| PC-170 | omit only profile from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only profile differs: received=false and producer refuses credit |
| PC-171 | omit only trigger from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only trigger differs: received=false and producer refuses credit |
| PC-172 | omit only dueDay from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only dueDay differs: received=false and producer refuses credit |
| PC-173 | omit only jobId from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only jobId differs: received=false and producer refuses credit |
| PC-174 | omit only nativeRunId from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only nativeRunId differs: received=false and producer refuses credit |
| PC-175 | omit only sha from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only sha differs: received=false and producer refuses credit |
| PC-176 | omit only ref from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only ref differs: received=false and producer refuses credit |
| PC-177 | omit only policyHash from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only policyHash differs: received=false and producer refuses credit |
| PC-178 | omit only scriptDigest from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only scriptDigest differs: received=false and producer refuses credit |
| PC-179 | omit only scriptRevision from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only scriptRevision differs: received=false and producer refuses credit |
| PC-180 | omit only assemblyDigest from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only assemblyDigest differs: received=false and producer refuses credit |
| PC-181 | omit only censusDigest from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only censusDigest differs: received=false and producer refuses credit |
| PC-182 | omit only bodyDigest from receipt envelope comparison | NightlyReportRecipientTests.C1039_RejectMismatchedReceipt | only bodyDigest differs: received=false and producer refuses credit |
| PC-183 | write definition 1 before reading definition 3 | ReleaseGateRegistrationTests.C1039_UnknownReads | third definition unknown: total writes=0 |
| PC-184 | serialize an unleased driver as granted | NightlyBuildSlotTests.C1039_AllDriversLeased | unleased arm preserves classification and qualificationEligible=false |
| PC-185 | skip executable hash recheck while retaining DLL comparison | NightlyChunkAdmissionTests.C1039_BuildBinding | changed executable alone after discovery refuses |
| PC-186 | clear accepted comment IDs then resend without GET after receipt rename failure | NightlyReportRecipientTests.C1039_ReceiptBeforeFinalState | GET-before-retry recovers same stored parts; no duplicate POST |
| PC-187 | set TRIGGER=scheduled unconditionally in wrapper | NightlyReportDeliveryTests.C1039_ProducerIdentity | absent/wrong schedule context stays manual even with jobId |
| PC-188 | remove status=Backlog from closure predicate | NightlyReportRecipientTests.C1039_IncidentSeparation | InProgress incident stays InProgress after green |
| PC-189 | ignore assignedAgentId in unassigned predicate | NightlyReportRecipientTests.C1039_IncidentSeparation | assigned Backlog incident stays Backlog |
| PC-190 | ignore ownerSessionId in unassigned predicate | NightlyReportRecipientTests.C1039_IncidentSeparation | owned Backlog incident stays Backlog |
| PC-191 | select release-gate incidents for a nightly run | NightlyReportRecipientTests.C1039_IncidentSeparation | nightly green/red leaves RC incident bytes and status unchanged |
| PC-192 | omit only assemblyHash from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed assemblyHash alone without an applicable retained receipt: refused |
| PC-193 | omit only classDigest from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed classDigest alone without an applicable retained receipt: refused |
| PC-194 | omit only hooksDigest from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed hooksDigest alone without an applicable retained receipt: refused |
| PC-195 | omit only dependencyDigest from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed dependencyDigest alone without an applicable retained receipt: refused |
| PC-196 | omit only toolVersions from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed toolVersions alone without an applicable retained receipt: refused |
| PC-197 | omit only os from timing applicability validation | NightlyPartitionTimingTests.C1039_TimingIdentity | changed os alone without an applicable retained receipt: refused |
| PC-198 | split a part at a UTF-16 high surrogate without adjusting the boundary | NightlyReportDeliveryTests.C1039_ReportParts | valid supplementary code point at split edge survives as identical UTF-8 bytes |
| PC-199 | write summary directly to its final path | NightlyReportDeliveryTests.C1039_RecoverBeforeEnqueue | held partial write: final summary absent; restart cannot enqueue until atomic publication |
| PC-200 | remove messaging from nightly requiredSuites and recompute policyHash | NightlyPartitionTimingTests.C1039_PolicyPartition | committed nightly requiredSuites equal the seven named suites |
| PC-201 | recreate an already stored schedule on resumed apply | ReleaseGateRegistrationTests.C1039_RestartApply | schedule-create lost response: no second create and stored enablement unchanged |
| PC-202 | reissue setenabled when GET already matches the selected enabled schedule | ReleaseGateRegistrationTests.C1039_RestartApply | enable lost response: matching readback succeeds without extra setenabled |
| PC-203 | drop ReportProfile in bootstrap report/recovery argv | NightlyReportDeliveryTests.C1039_ProducerIdentity | same explicit profile path and expected script identities reach both children |

Every newly introduced safety-critical guard must have the corresponding planned
method and observer. If Code splits an implementation into additional independently
bypassable safety checks, update this inventory before Review; do not silently
broaden a PC into an uncounted mutation family. This does not authorize dropping
any row. Mutation stores assertion/receipt/restoration evidence externally, runs
only locally inherited children and never commits/pushes from SourceLanding.

### Out of scope

- CARD-1021 further whole-Unit verification is waived at revision 9; historical
  failures/skips remain red facts. This plan adds no whole-Unit Code/Review run.
- CARD-1040 owns jq/environment/image repair and actual timeout diagnosis;
  CARD-1041 owns checkpoint ceiling enforcement. No wider timeout, assertion
  relaxation or retry makes a missing measurement admissible.
- CARD-0589 broker redesign/other assemblies; check for an overlapping landed
  nightly consumer before S1, reuse equivalent code and keep its consumer checks.
- Eligibility/suite changes, RC enable/publication and reduced verification
  activation are excluded. InterimVerification remains disabled.
- Live Windmill writes/notices, operator profiles/credentials/login and production
  process interruption are excluded from this delegate and ordinary Code/Review.
  Q lanes are required separate commissioned work, not waived proof.
- No Linux native implementation, generic ScriptHarness cleanup refactor, session
  queue change or server schema/endpoint change. Native Windows and real report
  recipient evidence are included; none is omitted to meet a budget.
- Power-loss/hardware-cache guarantees exceed these process-crash tests. The flush
  ordering control proves the implemented syscall contract, not storage firmware.
  Human reading is not inferred from persisted board discussion.

### Checkpoints

This is the single runnable ordinary manifest. All filters list exact methods,
not class wildcards; all C1039 methods are unparameterized (one execution each).
Require **all listed methods, zero failed/skipped**, in addition to Min. Harness
assertion totals and nested probe results do not increase the outer TUnit floor.

Windows lane: CP-1..4, CP-7, CP-9, CP-24/25 (`-Platform Windows`, no runner pin).
Portable controlled-I/O lane: CP-5/6, CP-8, CP-10..16, CP-21..23, CP-26/27.
Portable private HTTP/DB lane: CP-17..20 (including actual process restarts).
CP-23's incident recheck also needs private HTTP/DB; CP-19's three queue tests
use that same fixture to reach stored recipient evidence. `--rows` selects one lane
and committed slice; do not run a Windows row on Linux and count its skip.
S3a rows CP-10/11 additionally require accepted M-3; their test filters themselves
are portable and do not execute the measured production inventory.

Verification subdivisions keep the existing design within 30-60 minute dispatches:
S3c-a1 is producer/queue/lock; a2 is immutable parts/retention; b1 is recipient
fixture/profile/receipt; b2 is unknown-read/incident semantics; c1 is enqueue/wake/
ready recovery; c2 is send/receipt crash recovery; d1 is receipt consumption/modes;
d2 is front-door bounds/identity. Each builds the preceding slice; no file-sharing
concurrency. CP-20 additionally closes the real recipient queue-handoff matrix
after c1/c2. This is verification sequencing, not a change to D-11/D-12. S1 portable
and native rows may be separate
placement dispatches at the same source. S1n-b's early loop test exercises the
available producer; CP-9/24 recheck it after the dependent discovery/receipt changes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1n-a | tests/Antiphon.Tests -> bin-c1039-s1na/ | native-containment | /*/*/NightlyNativeOwnershipTests/(C1039_AssignBeforeResume)\|(C1039_DescendantExit)\|(C1039_DrainBeforeReturn) | V-5 | all 3 listed, 0 failed/skipped | 3 | 10 |
| CP-2 | S1n-b | tests/Antiphon.Tests -> bin-c1039-s1nb/ | native-faults | /*/*/NightlyNativeOwnershipTests/(C1039_NativeTimeout)\|(C1039_CleanupUnknownHolds)\|(C1039_NativeFixtureLoop) | V-5 | all 3 listed, 0 failed/skipped | 3 | 12 |
| CP-3 | S1n-c | tests/Antiphon.Tests -> bin-c1039-s1nc/ | interpreter-hop | /*/*/NightlyInterpreterTests/(C1039_BootstrapUsesPwsh)\|(C1039_DirectEntryHopsOnce)\|(C1039_MissingPwshRefuses) | V-5 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-4 | S1n-c | CP-3 | bootstrap-early | /*/*/NightlyScriptsTests/(The_three_scripts_are_ascii_only)\|(Shared_tree_WhatIf_exits_3_naming_the_guard) | V-5, R-5 | all 2 listed, 0 failed/skipped | 2 | 2 |
| CP-5 | S1 | tests/Antiphon.Tests -> bin-c1039-s1p/ | slot-consumer | /*/*/NightlyBuildSlotTests/(C1039_AllDriversLeased)\|(C1039_SlotTimeout)\|(C1039_CpuGrant)\|(C1039_ReleaseAfterCleanup)\|(C1039_RenewLease)\|(C1039_WaitClock)\|(C1039_NoNestedLease) | V-1 | all 7 listed, 0 failed/skipped | 7 | 10 |
| CP-6 | S1 | CP-5 | slot-retained | /*/*/BuildSlotScriptTests/(C589_WrapperRunsUnderLease)\|(C589_WrapperMaxCpuCountRules)\|(C589_WrapperTimeout)\|(C800_WrapperPassesWildcardArgvLiterally) | R-1 | all 4 listed, 0 failed/skipped | 4 | 4 |
| CP-7 | S1 | tests/Antiphon.Tests -> bin-c1039-s1w/ | native-slot-custody | /*/*/NightlyNativeOwnershipTests/C1039_LeaseAfterNativeCleanup | V-1, V-5 | all 1 listed, 0 failed/skipped | 1 | 6 |
| CP-8 | S2 | tests/Antiphon.Tests -> bin-c1039-s2p/ | chunk-admission | /*/*/NightlyChunkAdmissionTests/(C1039_IndependentDiscovery)\|(C1039_Admission)\|(C1039_DiscoveryFailure)\|(C1039_BuildBinding)\|(C1039_LiteralFilter)\|(C1039_ExpandedUnion)\|(C1039_ContinueAfterRed)\|(C1039_SerialChildren) | V-2 | all 8 listed, 0 failed/skipped | 8 | 12 |
| CP-9 | S2 | tests/Antiphon.Tests -> bin-c1039-s2w/ | native-partition-loop | /*/*/NightlyNativeOwnershipTests/C1039_NativeFixtureLoop | V-2, V-5 | all 1 listed, 0 failed/skipped | 1 | 5 |
| CP-10 | S3a | tests/Antiphon.Tests -> bin-c1039-s3a/ | timing-admission | /*/*/NightlyPartitionTimingTests/(C1039_MissingTimingRefuses)\|(C1039_OverBudgetRefuses)\|(C1039_TimingIdentity)\|(C1039_PolicyPartition) | V-7 | all 4 listed, 0 failed/skipped | 4 | 8 |
| CP-11 | S3a | CP-10 | policy-contract | /*/*/ReleaseGatePolicyTests/(C599_ProfileSchema)\|(C599_ProfileSuites) | V-7, R-2 | all 2 listed, 0 failed/skipped | 2 | 3 |
| CP-12 | S3b | tests/Antiphon.Tests -> bin-c1039-s3b/ | unit-receipt | /*/*/NightlyUnitReceiptTests/(C1039_Categories)\|(C1039_TerminalRows)\|(C1039_DuplicateRows)\|(C1039_IdentityBinding)\|(C1039_Digest)\|(C1039_FullNightly)\|(C1039_PhaseDurations) | V-3 | all 7 listed, 0 failed/skipped | 7 | 10 |
| CP-13 | S3b | CP-12 | parser-retained | /*/*/ReleaseGatePolicyTests/(C599_MetadataParsers)\|(C599_EligibilityCensus)\|(C599_ExpandedCoverage) | R-2 | all 3 listed, 0 failed/skipped | 3 | 3 |
| CP-14 | S3b | CP-12 | readiness-unit | /*/*/NightlyVerificationContractTests/(C544_ScheduledIdentity)\|(C544_CoverageRequired)\|(C544_GreenRequired)\|(C544_ReportReceiptRequired) | R-3 | all 4 listed, 0 failed/skipped | 4 | 3 |
| CP-15 | S3c-a1 | tests/Antiphon.Tests -> bin-c1039-s3ca1/ | report-intent | /*/*/NightlyReportDeliveryTests/(C1039_IntentBeforeWake)\|(C1039_RecoverBeforeEnqueue)\|(C1039_BusyConsumer) | V-6 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-16 | S3c-a2 | tests/Antiphon.Tests -> bin-c1039-s3ca2/ | report-body | /*/*/NightlyReportDeliveryTests/(C1039_ReportParts)\|(C1039_IntentIntegrity)\|(C1039_RetentionPins) | V-6 | all 3 listed, 0 failed/skipped | 3 | 8 |
| CP-17 | S3c-b1 | tests/Antiphon.Tests -> bin-c1039-s3cb1/ | board-receipt | /*/*/NightlyReportRecipientTests/(C1039_WholeBoardReceipt)\|(C1039_GreenWithoutIncident)\|(C1039_ReportProfile)\|(C1039_RejectMismatchedReceipt) | V-6 | all 4 listed, 0 failed/skipped | 4 | 12 |
| CP-18 | S3c-b2 | tests/Antiphon.Tests -> bin-c1039-s3cb2/ | board-incidents | /*/*/NightlyReportRecipientTests/(C1039_UnknownReadNoSend)\|(C1039_IncidentSeparation) | V-6, R-6 | all 2 listed, 0 failed/skipped | 2 | 8 |
| CP-19 | S3c-c1 | tests/Antiphon.Tests -> bin-c1039-s3cc1/ | queue-restart | /*/*/NightlyReportDeliveryTests/(C1039_EnqueueFailure)\|(C1039_CommitBeforeWake)\|(C1039_AlreadyEligibleConsumer) | V-6 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-20 | S3c-c2 | tests/Antiphon.Tests -> bin-c1039-s3cc2/ | recipient-restart | /*/*/NightlyReportRecipientTests/(C1039_AttemptBeforeSend)\|(C1039_SendBeforeAck)\|(C1039_ReceiptBeforeFinalState)\|(C1039_QueueHandoffs)\|(C1039_BusyAndEligibleRecipient) | V-6 | all 5 listed, 0 failed/skipped | 5 | 15 |
| CP-21 | S3c-d1 | tests/Antiphon.Tests -> bin-c1039-s3cd1/ | producer-receipt | /*/*/NightlyReportDeliveryTests/(C1039_ExitZeroIsNotReceipt)\|(C1039_RecoveryDoesNotPromoteGreen)\|(C1039_NoReportNoReceipt) | V-8 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-22 | S3c-d2 | tests/Antiphon.Tests -> bin-c1039-s3cd2p/ | recovery-front-door | /*/*/NightlyReportDeliveryTests/(C1039_RecoveryFrontDoor)\|(C1039_RecoveryBounds)\|(C1039_ProducerIdentity) | V-8 | all 3 listed, 0 failed/skipped | 3 | 12 |
| CP-23 | S3c-d2 | CP-22 | receipt-regressions | /*/*/(NightlyVerificationContractTests)\|(NightlyReportRecipientTests)/(C544_ScheduledIdentity)\|(C544_CoverageRequired)\|(C544_GreenRequired)\|(C544_ReportReceiptRequired)\|(C545_ResultLine)\|(C545_JobResultFetch)\|(C1039_IncidentSeparation) | V-8, R-3, R-6 | all 7 listed, 0 failed/skipped | 7 | 5 |
| CP-24 | S3c-d2 | tests/Antiphon.Tests -> bin-c1039-s3cd2w/ | native-final | /*/*/(NightlyNativeOwnershipTests)\|(NightlyInterpreterTests)/(C1039_NativeFixtureLoop)\|(C1039_BootstrapUsesPwsh)\|(C1039_DirectEntryHopsOnce) | V-5, V-8 | all 3 listed, 0 failed/skipped | 3 | 10 |
| CP-25 | S3c-d2 | CP-24 | bootstrap-final | /*/*/NightlyScriptsTests/(The_three_scripts_are_ascii_only)\|(Shared_tree_WhatIf_exits_3_naming_the_guard) | V-5, V-8, R-5 | all 2 listed, 0 failed/skipped | 2 | 2 |
| CP-26 | S4 | tests/Antiphon.Tests -> bin-c1039-s4/ | registration | /*/*/ReleaseGateRegistrationTests/(C1039_OpaqueRevision)\|(C1039_UnknownReads)\|(C1039_ScriptFields)\|(C1039_ScheduleFields)\|(C1039_EnableReadback)\|(C1039_RestartApply)\|(C599_Preview)\|(C599_ApplyReadback)\|(C599_Concurrency)\|(C599_ScheduleProvenance) | V-4, R-4 | all 10 listed, 0 failed/skipped | 10 | 12 |
| CP-27 | S5 | n/a | runbook-diff | git diff --check a54e03b78c6e45a9c78e8d09ca3679859c499605 HEAD | R-5 | exit 0, no whitespace errors | n/a | 1 |

CP-27 is a documentation check (0 build/test cases); review the S5 links, corrected
nightly eligibility prose and pending qualification statuses alongside its diff.
All other rows bind exactly one isolated build output, with reuse only at the same
After value. `run --plan` once per committed slice group/explicit lane rows,
`--expected-source-sha` and `--serial`; wait in foreground with `--max-wait 50s`
until terminal exit (75 means keep waiting). The tool owns row build slots.
Any separate tool bootstrap takes `scripts/build-slot.ps1`; exit 4 is not run.
No nested outer production lease against a private fixture broker. Do not edit
source during a run. Retain SHA-clean receipts and unedited CHECKPOINT lines;
Code/Review run `scripts/check-evidence-diff.ps1` over the full task range. Remove
only recorded alternate outputs after every owned child has exited.

### Cost

All figures below are **estimated**, not measured test durations. Counts are
planned outer TUnit executions derived from the explicit rosters. Measurement and
qualification are separately bounded commissions, never zero-cost omitted rows.

- Ordinary V/R floor (Code) = **209 minutes**: CP-1 10, CP-2 12, CP-3 9, CP-4 2, CP-5 10, CP-6 4, CP-7 6, CP-8 12, CP-9 5, CP-10 8, CP-11 3, CP-12 10, CP-13 3, CP-14 3, CP-15 9, CP-16 8, CP-17 12, CP-18 8, CP-19 9, CP-20 15, CP-21 9, CP-22 12, CP-23 5, CP-24 10, CP-25 2, CP-26 12, CP-27 1. The table contains **27 rows, 19 isolated builds, 100 TUnit executions**, plus the one non-TUnit diff command. Builds are included at 2 minutes each (38 build + 171 fixture/execution/check minutes), not added twice.
- Additional setup/tool bootstrap/source-receipt inspection/output cleanup allowance = **12 minutes** across the ordinary campaign; Code verification including setup = **221 minutes**. Authoring is separate. Dispatch authoring+ordinary budgets in minutes: S1n-a 38+10=48; S1n-b 35+12=47; S1n-c 30+11=41; S1 28+20=48; S2 32+17=49; S3a 35+11=46; S3b 32+16=48; S3c-a1 35+9=44; S3c-a2 35+8=43; S3c-b1 38+12=50; S3c-b2 35+8=43; S3c-c1 35+9=44; S3c-c2 35+15=50; S3c-d1 35+9=44; S3c-d2 27+29=56; S4 35+12=47; S5 29+1=30. These sum to 569 authoring + 209 ordinary = 778; every dispatch is 30-60 minutes. The shared 12-minute setup allowance is spread across dispatch slack.
- PC floor (Mutation) = **1470 minutes** for **203 separate cycles**. Every filter is exactly the Class.Method in its PC row expanded to `/*/*/Class/Method`. Portable slot/chunk/Unit/policy/registration/readiness: 111 PCs x 6 min = 666; native ownership/interpreter: 23 x 8 = 184; disk producer/recovery: 35 x 8 = 280; real HTTP/DB recipient: 34 x 10 = 340. This class-based price assignment uniquely names every filter/PC; no unnamed variants.
- A 6-minute cycle is mutate 0.5 + isolated red build 2 + exact red method 0.5 + restore 0.5 + fresh green build 2 + exact green method 0.5. The 8-minute cycles allow 1.5 minutes per red/green method; 10-minute recipient cycles allow 2.5 each. Include each cycle's independent cleanup within its execution allowance. Mutation setup/discovery/receipt audit/restoration inventory allowance = **15 minutes**, so Mutation commission floor is **1485 minutes** (split into method-scoped serial commissions at landed slices; do not compress it into a one-hour Code task).
- Combined engineering verification floor = setup 12 + ordinary 209 + mutation setup 15 + PCs 1470 = **1706 minutes**. This is the complete priced V/R/PC scope; authoring and operational evidence are separate. No actual pass or PC result is claimed.
- Reuse saves 7 duplicate builds x 2 = **14 minutes** versus one build per TUnit row. No PC build/batching saving is assumed: controls predominantly share production files. Narrow selections replace a whole-Unit run; no invented historical runtime saving is claimed for the waiver.
- Measurement allowance: M-0 30 + M-1 60 + M-3 30 = **120 minutes maximum commissioned fixed allowance**, plus M-2 dispatches **30-60 minutes each**. Exact M-2 total is calculated from accepted M-1 class roster before those dispatches are authorized. A 20-minute class cap is a diagnostic lower-bound stop, not a fabricated full timing. For planning scale only, 10 such dispatches would reserve 300-600 additional minutes (420-720 total measurement); this example is not a roster, forecast or admitted run.
- Operational Q-0..Q-6/Q-R: reserve **600 active minutes plus two scheduled boundaries**, including the carried CARD-0545 S6 work and Q-R. The two actual full-nightly execution durations remain measurement-derived, not this active-work reserve; admission requires each genuine scheduled run fit 00:30-08:00 London (450 minutes including queue/build/cleanup). A conservative budget envelope is **1,500 minutes** (600 active + two 450-minute execution windows), excluding calendar wait and additional failure diagnosis. It grants no timeout extension and is not a claim the workload fits. Fixed measurement plus this envelope plus engineering verification is **3326 minutes**, with M-2 additions disclosed above; a measured full-card total cannot be claimed before M-1/M-2.

Admission audit: bodies above read; **guards=203, mapped=203, missing=0, duplicate PC maps=0**. All 203 PC recipes have a compiling defect, named exact method and decisive red assertion in the ordinary roster; all are executable specifications after their implementation slice lands. Native and recipient seams are specified, not unresolved. Whole ordinary scope is V-1..V-8/R-1..R-6; its union is the manifest above. M/Q evidence has separate acceptance and cost, not blank Min cells or substituted unit tests.

Handoff: land this documentation branch promptly through normal caller-owned
landing, then commission S1n-a Code on Windows and M-0/M-1 measurement separately.
Continue the serial bounded slices; obtain accepted M-3 before S3a. No operator
choice is needed for this verification freeze. Live profile custody and explicit
qualification remain with Q-0's commissioned owner.
