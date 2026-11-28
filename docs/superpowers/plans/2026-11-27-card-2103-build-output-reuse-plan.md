# CARD-2103 follow-up — cross-task build output reuse

Qualify reuse of a checkpoint's verified runtime output for the exact source and build inputs.
The first shipping slice measures demand without changing execution. The first active slice
consolidates simultaneous eligible Code build requests on the same runner. A later slice lets
Review reuse an eligible completed Code build while running and qualifying its own tests.

Source inspected at `263a2880dde33eac23b542b133baba0c87008ac7`. The investigation is
`docs/investigations/2026-11-27-build-slot-time-analysis.md` at
`9c694e7c987b5f3843e4a061b2e7c2aab1b85fbd` on `feat/card-task-cef6af0a`
(task `cef6af0a`). Read it with
`git show 9c694e7c987b5f3843e4a061b2e7c2aab1b85fbd:docs/investigations/2026-11-27-build-slot-time-analysis.md`.

## Ground truth

| Card assumption | What the investigation or source establishes | Consequence |
|---|---|---|
| A narrow checkpoint might build the whole solution. | Grouped checkpoints build the selected project and its closure. `Antiphon.Tests` has 46 projects in its examined closure, versus 62 C# projects in the solution. Windows rc explicitly built the solution once for its run. [BuildStep](../../../tools/Antiphon.Checkpoints/Execution/BuildStep.cs) | Key by entry project and the evaluated graph. Study Windows checkpoint workloads separately from rc. |
| Checkpoints are usually cold and duplicates are common. | The 24-hour census plus recovered builds found 199 builds or build successes, 115 cold-like full-graph restores with output, three warm, and 81 incomplete traces. In the four-root census the 114 timed cold-like SDK builds took p50 4m29s and p95 14m05s. Compiler-target execution is largely unobserved. | Measure restore, compile and staging separately. Do not treat every restore as a clean compile or every source match as a hit. |
| Worktree-local replay or warm `obj` already covers different tasks. | The sealed intermediate partition is inside a checkout. Grouped rows share a producer within a run and use `--no-build`. Standalone replay checks a binding containing absolute repo, project and output paths, matching properties and source. [Intermediate cache](../../../tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs) · [Source binding](../../../tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs) | Preserve the local stamp and seal. Use a separate authenticated handoff for crossing checkouts. |
| Matching clean SHA and project identifies the same binary inputs. | The investigation found 42 cross-task repeats at that grain. Adding recorded fingerprint and requested properties shrank this to 28. Effective imports, toolchain, hidden inputs and availability when requested remain unknown. | Replay the observed timeline using a complete key before forecasting savings. Missing components remain unknown. |
| `buildSource=verified` proves a shareable runtime tree or test success. | The tool verifies source and local output custody. General receipt eligibility also allows `notApplicable` for command rows. A project row requires verified build source and its own counters and source checks. [Source guard](../../../tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs) · [Report validator](../../../tools/Antiphon.Checkpoints/Report/ReportValidator.cs) | Require a live tool-derived verified producer build outcome, output closure and authenticated publication. Qualify the consumer's execution separately. |
| Removing a build removes its whole checkpoint lease and all wait. | Grouped producer lease release precedes the outer ledger recheck and build binding. Grouped rows acquire their own lease. A standalone row shares one lease across build and test. The sample recorded 240m30s of held time and a separate 236m51s of pre-grant caller wait. A recovered finalization took 16m23s after the rows. [Scheduler](../../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs) · [Standalone row](../../../tools/Antiphon.Checkpoints/Execution/RowCommand.cs) | Join before producer admission, retain test leases, measure publication and cleanup off-slot, and exclude standalone from active reuse in this plan. |
| Shared links or memory-backed outputs make reuse cheap. | The confinement guard refused hard links, including the npm/esbuild case. The historical investigated data mount was ext4 on the same LV as other workloads. CARD-2119 observed 39–44% iowait in a different incident. [Confinement](../../../tools/Antiphon.Checkpoints/BuildConfinement.cs) | Preserve link refusal. Measure metadata writes and storage contention on each lane. |

## Decisions

**D-1 — Exact key and bounded inputs.** Compute a versioned SHA-256 key over a
canonical encoding of repository identity, full Git object ID, source fingerprint,
repo-relative entry project, effective build properties, toolchain identity, and the
evaluated dependency/input graph hash. Properties come from the effective checkpoint
build invocation and resolved defaults, including configuration, TFM, RID, apphost,
repeat instrumentation and compiler policy. Preserve value case and semantic order.
Include OS, architecture, actual resolved SDK/MSBuild/compiler and required runtime
packs or workloads, and the runner's toolchain/image generation. Changes to runtime
layout or qualification rules bump the key version. Exclude scheduling or per-run
labels only when the qualified project demonstrates they do not affect output.
Normalize checkout/output roots only for tool-owned relocatable fields. A path-valued
property whose meaning cannot be traced is ineligible.

The graph digest covers selected and transitive project nodes/edges with their effective
properties, imported props/targets, scripts/generators/staging inputs, and the resolved
NuGet graph and package content identities. Include `global.json`, effective feed/lock
identity, SDK imports, analyzers and qualified external inputs. Canonicalize evaluated
inputs by relative path and content identity. For the pilot, require immutable dependency
resolution and a pre-admission input descriptor tied to the installed toolchain and
allowlisted build environment. Obtain it from existing evaluation and a runner-validated
locked input inventory where available. Cross-check with the producer's actual evaluated
and resolved graph before publication. A consumer must establish the same descriptor
without assuming the producer's resolution. Dynamic/unlocked resolution, temporary
overrides, arbitrary imports, unqualified ignored inputs, unresolved source paths or
unknown graph entries go through the normal build path. A simple SHA/project key would
collapse different builds. A full restore per lookup would consume much of the saving.

**D-2 — Strict producer authority.** The pilot admits only allowlisted, placed,
grouped project-build attempts under the existing build-slot gate. Require a successful
SDK result, the full checkout SHA with known equal clean start/end fingerprints, the
existing `buildSource=verified` outcome, passed confinement/ledger settlement and a
sealed build output while it is still runner-owned. Publish after those checks and before
the producer's test hosts can write to their execution tree. The build attestation
binds the runner-controlled attempt, task/role, lease, run/build ID, checkout/source
observations, tool version, key and manifest digest. It can use the build outcome
even when a later test fails. A build attestation proves only the build step.

The publisher authenticates the invocation and takes custody of the runner-created
build root. The production path requires an isolated runner-controlled checkpoint
executor with an exact pinned clean checkout and a private attempt root. Other tasks
and prior build children must be unable to modify either the handed-off runtime bytes
or publisher state. Separate the publisher identity from delegate/build credentials,
fence publisher writes to the store, and keep attestations/signing authority out of
the child environment. Qualify the lifecycle through actual process exit and custody,
including escaped or lingering children. A copy from an arbitrary worktree, a
task-authored receipt and manifest, or a directory writable by other tasks under a
common UID cannot confer provenance. If current runner isolation cannot enforce this,
ship observation only on that lane. Direct builds, command rows, standalone,
unleased/replayed builds and caller-provided archives are outside the publisher path.

**D-3 — Portable test runtime projection.** Seal the minimum *complete* runtime layout
for the qualified entry project: executable/assembly, deps and runtimeconfig, staged
test helpers and managed/native/content dependencies with modes and relative layout.
Exclude `obj`, caches, unrelated project output, local path-bound stamps, logs and
TRX. Derive the required file set from qualified build output/staging and validate
by relocating into a different clean checkout with the producer's worktree and
intermediates unavailable. Keep the consuming checkout as the working directory.
Prefer the existing `dotnet run --no-build` entry when it resolves the projected
layout without producer paths or restored intermediates. Otherwise, qualify an
explicit artifact-only host launch using the attested entry assembly/apphost and
runtimeconfig, with argument, helper, source-boundary and TRX parity through
`RowRunner`. A project's successful build alone does not establish portability.
Start with a contained `Antiphon.SessionRunner.Tests` checkpoint and a process/helper
witness. Admit `Antiphon.Tests` after its larger closure and staging qualify.
Keep E2E and prebuilt client inputs out of the first pilot.

**D-4 — Runner-local sealed store.** Put a versioned namespace on a runner-managed
volume shared read-mostly across that authority's task executors. Only the trusted
publisher/janitor may mutate it. Initial format is a small authenticated canonical
manifest and one bounded uncompressed pack of relative regular files. The manifest
records file paths, sizes, mode and SHA-256, the pack digest, source and build
attestation, version, generation and publication time. Validate against an
allowlisted runner identity. Stage on the same filesystem, flush the pack and
manifest, then publish the directory by atomic no-replace rename and perform the
platform durability checks. Readers ignore staging. First valid publication wins.
The pack reduces shared-volume directory and small-file traffic while preserving
normal runtime files at the destination.

Use a held read pin through verification/materialization. Check manifest auth,
key, generation, age and limits, then stream once to a fresh owned private output,
verifying each file and the pack while copying. Reject missing/extra entries, nonregular
files, links, unsafe names, platform case collisions and size/count overruns. Use
checked handles and the existing anchored custody primitives. Run from consumer-side
sealed/readonly inputs with a separate writable results/scratch area. Recheck source
and sealed materialization at launch/use under the row's lease. Preserve the exact
verified bytes through execution. A consumer whose execution needs to mutate packaged
runtime inputs is ineligible until its writable paths can be isolated. A whole
second file walk on every row would compound the I/O cost. Hard links would share
the producer's inode and violate existing guards. Consider reflink or a readonly
mount/overlay only after measured gains and native custody qualification. Keep the
copy path as the reference implementation for Linux and NTFS.

**D-5 — Bounded lifetime and reclamation.** S1 keeps a sealed object only for already
registered followers, with a provisional 30-minute handoff TTL from publish. S2
starts with a 24-hour TTL from publish, no hit-based extension, an 8 GiB aggregate
per-authority cap, and at most 512 MiB and 10,000 regular files per pilot entry
(provisional caps to validate against measured closures). Account for pinned and
staged bytes, final pack, and the consumer expansion on its own volume. Reserve
capacity before staging with concurrent reservations counted. Keep at least
`max(20 GiB, 10% of volume)` free after reservation and respect any stricter host
floor. Probe each actual volume, including Windows' destination. The numbers
borrow the CARD-0824 floor while keeping this cache's own accounting.
[Workspace budget](../../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs)

The janitor removes only owned, expired or oldest unpinned objects under disk
pressure. Pins are bound to verified process/attempt lifetimes so an expired entry
cannot receive new consumers, while a live reader can finish. Reclaim abandoned
staging after verified owner release/death. Quarantine by generation or revoke
lookup when storage health or a digest fails. On startup recover only complete
sealed objects with known authority/version. A version/toolchain/key change is a
miss, an incident can revoke a producer generation, and replacement needs a fresh
leased build. Report retained bytes, outstanding reservations, cleanup lag and
refusals. An ENOSPC, uncertain pin or unavailable cache disables new publication
or consumption until the condition clears. Keep the artifact root outside task
cleanup and worktree retention. Evidence outlives cache eviction.

**D-6 — Single-flight before broker admission.** Per authority/key, a
runner-owned generation coordinates claim, queue, build, post-release validation,
publish and terminal outcome. Claim before entering the build-slot FIFO. Exactly
one cache leader requests a producer lease; the existing grant/grace, node cap
and process owner stay authoritative. Followers wait on a cancellable keyed signal
under a separate admission clock and hold no build slot. Keep the claim live through
post-release settlement; only the current fenced generation can publish. Release
the existing producer lease at its current boundary. A bounded off-slot I/O lane
per runner limits publishing and materialization workload. Retest the key/visibility
under the claim when an intermediate step races completion. Once materialized, each
follower still acquires a row lease and executes its own selection.

Canceling a follower drops its registration. Canceling the leader stops and joins
its owned build through the current driver, then releases the claim. Producer
failure or death wakes followers. Promote at most one next claimant when the previous
owned build is known gone, generation is fenced and budget permits. Fencing alone
cannot stop an old compiler. If custody is uncertain, stop reuse for that key and
use normal independent verification only when existing admission and deadline permit.
Waits end at publication, terminal failure, cancellation or the earliest of the
row's remaining admission allowance, a measured join bound and the hard wall.
A follower may rebuild with a new normal grant and remain outside that generation,
or report verification incomplete with the appropriate timeout. There is no
automatic retry of a build/test failure and no change to sessions that wait for
operator input. A lock acquired after leasing would strand slots behind the leader.
Keep this authority beside the runner and its broker, with no change to broker FIFO
or API and no distributed coordination across runners.

**D-7 — Review executes independently.** Start S1 with eligible Code-to-Code
followers that registered before publication. In S2 only the assigned subject
Code task's eligible producer can supply a Review of its exact immutable reviewed
tip. Review creates its own clean checkout, validates the expected full SHA,
fingerprint and complete key, and authenticates the producer attestation and each
materialized file. Review launches its own test host per required checkpoint with
the original filters, minimums, discovery/member checks, fresh identities and TRX.
Its own required red/baseline witnesses, fresh-build controls, evidence diff,
ordinary scope and Final/Interim duties still execute. Use ordinary builds whenever
the brief or policy requires independent compilation or source transformation.
No artifact eligibility for red/PC/mutation, SourceLanding, land verification,
nightly/rc, or direct builds in this design.

Keep `buildSource=verified` and the existing source-state/counter vocabulary.
Teach the evidence writer and qualification path to distinguish local build,
existing local replay, and authenticated cross-task consumption. For consumption
retain a versioned artifact proof bound to the new build/row identities and
receipt, including key, digest, producer identity, exact SHA, verifier version and
result. Pair a retained attestation and bounded manifest with the independent
consumer's evidence pack so validation survives eviction and tool restarts.
Bind the proof and the fresh TRX digest to the consumer attempt and row, and
validate the actual TRX against the checkpoint selection and receipt counters.
Require matching proof for any cross-task origin. Version the receipt deliberately
and teach the validator first, so older tools fail closed on new evidence rather
than qualifying it under the legacy shape. Old receipt versions keep their verdicts.
Retain a recognizable provenance field in the new CHECKPOINT/receipt pair without
rewriting historical lines or counting Code's TRX. `reviewedSourceClean: true`
requires the independently qualified Review receipt as usual. Pre-launch artifact
failure means a typed miss and ordinary build if budget permits. A source change,
lost custody after launch, or failed/missing test evidence disqualifies that
attempt and stays visible. An actual test failure retains its result.

**D-8 — Conservative rollout.** Read live `GET /api/runner-defaults` and
`GET /api/session-runners` before dispatch. At planning, both Linux and Windows
lanes had eligible runners. Leave portable rows unpinned. Use the named OS lane
for native filesystem or runner witnesses and apply the same build-slot rule.
Disable producer and consumer switches independently. A reader that cannot
negotiate the version uses ordinary local verification. Roll back through the
flags, allow active generations to settle, and reclaim by custody; historical
evidence and legacy validation keep their meaning. No cross-runner sync, shared
solution-wide cache, shared writable `obj`, generic CI artifact service, increased
node/slot caps, blanket qualification changes or changes to dispatch/parking.

## Slices and expected savings

Use the investigation's 24-hour Linux window as an exposure sample. The 29
paired SHA/project repeats cost 149m24s in producer leases. The narrower
19 paired repeats with matching recorded fingerprint/properties cost 97m.
Neither is an observed hit rate. The 236m51s of caller wait is a separate,
overlapping delay metric. Unpaired repeats have unknown producer hold. Track
net lease minutes separately from caller wall time and storage cost.

For S1 and S2, compute daily net savings as the sum of the matched avoided
producer holds minus any added slot-held work in the same cohort. Partition
requests by first eligible opportunity: in-flight at request, already published,
or neither. Attribute S2 only the ready-at-request opportunities that S1 would
still build. Report publish/copy/cleanup and join delay separately even when
off-slot. For planning sensitivity, capturing 10% of the 97-minute matched
exposure with S1 would avoid about **10 slot-min/day gross**. If S2 then qualifies
a disjoint 25% of that exposure, it would add **24 slot-min/day gross**. These are
scenarios, not forecasts. Each slice can save zero. Quantify observed net
savings after S0, including fallback and handoff cost, before enabling active reuse.

| Slice | Independently shippable change and file entry points | Tests to design and existing coverage | Effort, risk, deployment |
|---|---|---|---|
| **S0 — Observe** | In `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `RowCommand.cs` and `CheckpointApp.cs`, emit eligibility, comparable key coverage, would-join and would-hit inputs and timestamps into bounded per-run evidence via `Timing/TimingJournal.cs` and `Execution/ExecutorLogWriter.cs`. Join across runs under CARD-2103. Record candidate finish/validation/cleanup, authority and checkout identity, role, and consumer request time. Distinguish definite matches from incomplete keys and simulated publication availability. No additional MSBuild/restore child, cache or receipt schema change. **0 slot-min/day** by design. | Proposed `tests/Antiphon.Tests/Checkpoints/CheckpointArtifactObservationTests.cs` verifies complete/missing keys, producer active/ready/failed at the request timestamp, and identical build, lease, filter and verdict behavior when observation is on/off or the journal fails. Retain `CheckpointConfinementLedgerTests`, `CheckpointSlotContractTests` and CARD-1782 receipt guards. | Smallish code change, medium measurement risk. Tool publication only, no runner restart or DB migration. Keep event collection bounded and best-effort. |
| **S1 — Collapse in-flight Code builds** | Add an artifact coordinator and pack/publish support under `tools/Antiphon.Checkpoints/Artifacts/`; integrate the producer hook in `Execution/RunScheduler.cs` and the leased consumer in `Execution/RowRunner.cs`. Runner-owned local custody and authority entry point in `src/Antiphon.SessionRunner/Program.cs`; depend on the existing `BuildSlotBroker.cs` protocol. Teach `Report/ReportModel.cs`, `ReportWriter.cs`, `CheckpointLine.cs`, `ReportValidator.cs` and `ReceiptQualification.cs` the proof before enabling consumption. Retire handoff objects after registered followers or TTL. Prove positive net slot-minutes for overlapping Code requests independent of S2. | Proposed `CheckpointArtifactFlightTests.cs`, `CheckpointArtifactCustodyTests.cs` and `CheckpointArtifactReceiptTests.cs` beside the other checkpoint tests. Witness two separate task processes, one producer grant, two new test executions, canceled leader/follower, crash while building and after release, partial publish, lost pin and corrupt pack. Keep `CheckpointSlotContractTests`, `CheckpointBuildBindingPathTests` and the CARD-2085 handle and warm-overlap witnesses. Use `tests/Antiphon.SessionRunner.Tests/BuildSlotBrokerTests.cs` and `BuildSlotEndpointTests.cs` for broker parity if runner wiring changes. | Large, high custody and crash risk. Requires runner/tool release and per-runner store provisioning for live use. Roll a compatible runner with writer/reader off, then opt one qualified lane in. No DB migration. |
| **S2 — Review from sealed completed output** | Permit verified Code-to-subject-Review ready hits. Extend the artifact audience/retention policy under `tools/Antiphon.Checkpoints/Artifacts/` and the receipt qualification checks in `Report/ReceiptQualification.cs` and `Report/ReportValidator.cs`. Document the opt-in evidence form and controls in `server/Bundles/stage-review.md` and `docs/testing-and-build.md`. Keep separate task checkouts and evidence roots. | Proposed `CheckpointArtifactReviewTests.cs` starts two clean checkout roots with the pinned SHA, removes producer scratch, requires a fresh Review TRX and matching proof, and tests rejection of stale/cross-task TRX, wrong subject/SHA, changes in toolchain/properties, missing helpers and corrupt or absent proof. Existing `CheckpointSourceExecutionTests`, `CheckpointSelectedOutputTests` and `CheckpointReceiptSafetyTests` cover local source/output validation and legacy compatibility. Qualify a real relocated checkpoint and matched cold control. | Medium to large after S1, high Review evidence risk. Deploy validator compatibility first. Bundle rollout through the usual server activation gate. Runner restart only for a runner binary/config/mount change. No DB migration. |
| **S3 — Preserve sealed `obj` in eligible slot resets** | Separately measure and tighten the existing same-checkout warm path in `tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs` and `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs`. Keep only owned, sealed matching project/base SHA, effective properties and toolchain partitions; guard retention across warm reset, identity change, deletion and cold reset. Keep native direct build outputs under their existing CARD-0824 rules. Apply to residual local builds after S1/S2 or ship independently. | `CheckpointWarmBuildTests`, `CheckpointConfinementLedgerTests`, and `tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests.cs` and `C0824WorkspaceSlotColdResetTests.cs`. Add same-slot reset reuse and wrong-key/dirty/changed/refused cold fallback coverage. Test on a native Windows lane for held-file/reset cases. | Medium, stale input and reset debt. Tool update and ordinary rolling runner update if the slot reset changes. No DB migration. The observed three-project pair saved **17m59s** of hold. One comparable eligible warm group per day would be about **18 slot-min/day gross**. Frequency and matched-load effect are unknown. Subtract reuse already credited to S1/S2. |

### Dependencies and ownership

- **CARD-2085** owns held-handle correctness and warm cache overlap. S1 uses
  its settled custody primitives and needs the native Windows witnesses before
  enabling Windows writes. Its local intermediate seal remains separate from
  an artifact attestation. [Held-handle plan](2026-11-26-card-2085-checkpoint-held-handles.md)
- **CARD-0824** owns task slot checkout/reset and `bin/obj` lifecycle. S3 reuses
  its warm/cold reset seams. The artifact store belongs outside the slot
  directory, so slot retirement can proceed once normal holders release.
  [Slot worktree plan](2026-11-02-card-0824-per-slot-worktrees-plan.md)
- **CARD-2103** owns the joining/reporting vocabulary and history. S0 adds
  artifact availability/qualification signals to that stream, using lease,
  run, project and task-attempt correlation. Reuse its timing/reporting
  surface instead of creating a new CLI or database history. Hash identifiers
  where needed and allowlist logged fields. Keep sensitive properties,
  environment values, feed credentials and raw file contents out of events.
- **CARD-2117** owns per-runner/stage caps and scheduling/reservations. Reuse
  follows current build and row admissions and supplies separate join,
  materialization and lease/resource counts. Joining keys cannot bypass a
  scoped hold, native qualification reservation or test grant.
- **CARD-2119** owns testing of tmpfs for short-lived `obj/bin`, I/O admission
  and control-plane protection. A bounded memory-backed private staging/
  execution root can be benchmarked when its memory accounting and custody
  qualify. Keep sealed reuse on the runner-managed volume. Include publish,
  extraction and cleanup in disk and memory measurements so a CPU win does
  not add storage latency. Windows uses native NTFS ownership/ACLs, checked
  handles, file-sharing/rename/delete behavior, and its configured native
  build slot gate. Use local-copy timing before proposing Windows acceleration.

### Existing tests affected and candidate checkpoint roster

The plan-only diff has no executable coverage. Code and Review resolve the
[CARD-1782 impacted-classes table](../../testing-and-build.md#impacted-classes-card-1782)
at each actual diff. `checkpoint-tool` selects `CheckpointReceiptSafetyTests`,
`CheckpointTerminalReceiptTests`, and `HostCleanupCheckpointReceiptTests`.
Add `checkpoint-tool-project-contract` if its project globs match, and
`timing-labels` on an operation-label change or derived timing-hook match.
No migration, public HTTP DTO/route, remote script or statement-budget edit is
planned. If rollout work later changes those surfaces, resolve the corresponding
impact rows and their existing tests before that slice ships. Code and Review
also retain the fixed subset required by the testing guide and report exact
per-project coverage and native obligations.

This roster identifies scope and lanes for TestDesign. The closed checkpoint
manifest, exact new methods, floors and calibrated costs are due in that phase.

| Slice | Lane | Project | Candidate filter and use |
|---|---|---|---|
| S0 | Portable | `tests/Antiphon.Tests` | `/*/Antiphon.Tests.Checkpoints/CheckpointArtifactObservationTests/*` (proposed); one narrow red and green for the availability classification and observation-only behavior. |
| S1 | Portable | `tests/Antiphon.Tests` | `/*/Antiphon.Tests.Checkpoints/(CheckpointArtifactFlightTests*)\|(CheckpointArtifactCustodyTests*)\|(CheckpointArtifactReceiptTests*)/*` (proposed); fake driver/broker logic first, then distinct processes/leases with real files. Split exact witnesses for red/green. |
| S1 | Native Windows | `tests/Antiphon.Tests` | `/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/overlapping_build_keeps_first_rows_partition` and exact held-handle and artifact-pin/rename witnesses, each in its own row if a suffix adds cases. |
| S1/S2 | Native Linux and native Windows, separate runs | `tests/Antiphon.SessionRunner.Tests` | `/*/*/BuildSlotEndpointTests/Build_slots_only_host_serves_health_and_build_slots_and_nothing_else` as the first relocated runtime/helper candidate; run the same selection via ordinary build and a qualified artifact. Take actual per-lane TRX and provenance. |
| S2 | Portable | `tests/Antiphon.Tests` | `/*/Antiphon.Tests.Checkpoints/CheckpointArtifactReviewTests/*` (proposed); test consumer auth, source and receipt refusal paths independently of producer green. |
| S3 | Portable and native Windows reset witnesses | `tests/Antiphon.SessionRunner.Tests` | `/*/*/(C0824WorkspaceSlotResetTests*)\|(C0824WorkspaceSlotColdResetTests*)/*`; verify worktree ownership and retained/deleted paths. Include exact checkpoint warm/cold witnesses in `Antiphon.Tests` in a separate group. |

Confirm full class/method membership against the compiled roster at the
committed slice SHA and by actual TRX, with no suffix collisions or unintended
parameter cases. OR operands each use parentheses and a trailing `*`, with
pipes escaped in Markdown. Use separate exact-method rows sharing the build if
a suffix selects extra cases. Never use discovery output alone as execution
evidence. [Combined filter rules](../../testing-and-build.md#combined-class-filters-card-0403)

## Measurements required before enablement

1. **Actual opportunity timeline.** For seven consecutive eligible days per
   pilot lane, capture attempted and qualified counts/bytes by project/role,
   exact key coverage, every refusal/unknown reason, authority, live-at-request
   and sealed-at-request opportunities. Include time to publication, first
   eligible follower, Review arrival, eviction and lower/upper hit bounds.
   Join exact producer lease interval and report point-in-time hits. During S0,
   use the settled output boundary plus separately sampled pack time to model
   earliest publication, and distinguish this estimate from a real seal.
   Keep Linux and desktop results separate. Quantify unknowns in the denominator.
2. **Key and runtime portability.** For the selected project and each OS,
   compare effective args, SDK/runtime/toolchain hash, evaluated graph, restored
   package identities and every ignored/environment/path-valued input across
   two clean checkouts. Save a matched cold build and relocated run with
   inaccessible producer paths, checking helper loads, test names and
   method/count/outcome parity from actual TRX. Deliberately vary each key
   dimension and confirm typed misses. Check the closure size/hash cost and
   absolute runtime input paths before adding the larger main project.
3. **Phases and slots.** Sample phase/lease timing at matched load for local
   cold, local sealed warm, time joining, and published consumption. Capture
   lookup, graph evaluation, restore, `Csc/CoreCompile`, staging, source/ledger
   checks, pack/hash/verify/materialize, slot wait/hold, test host, and cleanup/
   receipt finalization. Join monotonic elapsed clocks by task/attempt/lease and
   use correlated UTC endpoints for cross-process ordering. Record actual
   build nodes, compiler mode, available memory, load, runnable/blocked work,
   device queue/iowait and host/DB I/O latency where observed. No causal claim
   from an unmatched machine-load pair.
4. **Disk and resource pressure.** Record logical and physical bytes, paths
   created/deleted, peak concurrent staging/expansion bytes, sequential read
   rate and metadata IOPS. Measure cold and warm page caches and representative
   concurrent publisher/reader/cleanup load. Sample temporary volume capacity
   and Windows delete deferrals. Compare the copy baseline before testing an
   optional clone or tmpfs. Graph what is live, pinned and attributable to the
   cache separately from the worktree pool. Confirm quota/headroom under
   interrupted publish, TTL eviction and disk pressure.
5. **Fallback and policy.** Measure key-join abandonment, bounded failure
   recovery, I/O contention and deadline consumption with representative
   producer delays and failed generations. Before enabling S1 require
   attributable positive net slot savings, no p95 caller-wall regression at
   matched load, and clean custody witnesses. Before enabling S2 also require
   independent Review qualification on each admitted lane/project and p95
   validation/materialization within the smaller of 60 seconds or 25% of the
   comparable producer p95 hold. Take at least 20 comparable observations per
   enabled path and lane, with explicit sample counts; collect ordinary
   observations first. Keep a lane in observe mode if samples or custody
   remain insufficient. Flag storage or control-plane regressions to
   CARD-2119 with correlated phase evidence.

## Performance budget

This docs-only Plan runs no test or build. Budget the implementation's ordinary
checkpoints in the named project/category: checkpoint tool and shared runtime
custody under `Antiphon.Tests`, runner/broker and warm-slot reset under
`Antiphon.SessionRunner.Tests`. Use the evaluated entry-project and downstream
dependency maps plus CARD-1782 for the exact impacted set. Avoid splitting the
main test graph for this feature. A smaller runner project gives the pilot a
contained runtime witness without changing ordinary coverage.

| Phase | Baseline and expected test design budget |
|---|---|
| Cold SDK build by entry project | The investigation's four-root Linux sample gives `Antiphon.Tests` cold-like p50 7m20s/p95 14m10s (63), and `Antiphon.SessionRunner.Tests` timed SDK p50 1m06s/p95 2m30s (27, mixed layout). Use these only as baseline SDK clocks. Approximately 29m/5m are provisional SDK-phase ceilings, subject to matched per-project checkpoint build measurements including outside-SDK work. Share one isolated producer across related rows on each committed slice. |
| Test body and row | Replayed test hosts in the census had pooled p50 34s/p95 6m03s (429, varied scope). That is not a timeout sample for these methods. Expect short fake-driver unit witnesses and an initial native process witness in under one minute per invocation; target each narrow row below two minutes including host startup/teardown. Collect per-method/row measured p95 and use about 2x separately for native and portable paths. |
| Fixture and row subphases | Use one initialized fake broker/temp owner and small committed Git fixture per fixture run where safe. Give cases separate scratch, credentials, roots or test clocks. Native process/crash cases create fresh scoped children to witness real custody. The relocated runner witness starts one isolated build-slots-only web host on a random loopback port because runner startup and helper resolution are part of the witness. Record startup, readiness, build/run body, HTTP, process exit/join and teardown. AppHost, Docker, DB, migrations, seed data and DB snapshot/restore/clone are not applicable. Record unmeasured Git/file/host phases as unknown. |
| Admission and join | The earlier sample's producer admission p50/p95 was 1m00s/11m45s and row admission 0s/8m15s; about 24m/17m are comparison points, not new broker settings. Collect lane/interval-specific admission under the current caps. Derive a separate join allowance from about 2x measured join p95, bounded by the existing remaining admission and hard wall. Charge no join time to slot hold. |
| Artifact I/O and finalization | The 60 off-slot cleanup entries had p95 8m22s, with a separate 16m23s whole finalization incident. These are legacy observations, not headroom for the artifact path. Target lookup cost below one second and the D-4 I/O path within the enablement threshold above. Set finite per-operation deadlines after measuring roughly 2x p95 for open/read/hash/materialize/pin/release and cleanup. Keep staged bytes bounded even after cancellation. |
| Batch/run | One narrow red witness plus a narrow green or guard group per slice as the profile allows. Plan for one already-leased build per project/committed state and record red/green states separately. A representative main-project run is provisionally 8–12m of execution before admission at historical median-scale load, including build, one short host and finalization. A smaller runner witness is provisionally 2–5m before admission. Split native witnesses from the portable batch. Report hard wall, charged execution and admission separately. If any selection, batch, row or run is expected or observed above 30m, dispatch Debug with the exact selection, timing/run evidence, source SHA and host/load before another expensive run. |

The baseline is the investigation pin above, with heterogeneous source SHAs
and load (earlier Linux host load1 median 35.67 and p95 61.64). Three same-checkout
warm samples and pooled row metrics are insufficient for the new windows.
TestDesign owns initial short timings and finite provisional caps under
CARD-2103/CARD-1404, then refines from the repeated observations above. Target
about 2x measured p95 for every newly set timeout and document any longer
exception with measurements and a tracking card. Keep existing policy caps
until comparable measurements justify changes. Measure independent setup,
readiness, body, process/IO/HTTP calls, teardown, and unattributed remainder
as distinct intervals. Bound external calls individually. Keep per-test
durations and actual roster/phase membership in owned TRX and timing journals
alongside checkpoint reports and producer attestations. Run builds and rows
through the checkpoint tool or the host build-slot gate as applicable.
[Performance standards](../../testing-and-build.md#performance-standards)

## Handoff

TestDesign starts with S0. The observation contract spans durable timestamps,
availability classification, receipt compatibility and no-effect-on-execution
behavior. Bind a narrow red/green witness and the impacted roster before Code.
Gate a later decision to activate S1 or S2 on the measured opportunities,
matched costs, runner trust boundary and pilot runtime portability.
