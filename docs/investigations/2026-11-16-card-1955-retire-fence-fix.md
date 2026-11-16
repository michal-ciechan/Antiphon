# CARD-1955: retirement rendering and refused Ready admission

The full audit rendering now precedes the final origin-binding clock. A late
refusal revokes only the current invocation's mutable Ready receipt. The five
second bound, origin observations, proof protocol and receipt admission lifetime
are unchanged. This repair requires a full ordinary Review before land/activation.

## Change

`scripts/c1008-retirement.sh` prepares the Ready envelope and renders the complete
`audit.txt`, `findings.jsonl` and `proof.md` into a private operation-owned staging
directory. It flushes the three files and their directory before the final binding
clock is observed. After fresh approval, advertisement/control and proof-custody
checks, the writer only writes the bounded receipt payloads and renames staged
files. All three Ready payloads are fsynced before canonical publication, and both
source and destination directories are flushed after the renames.

`scripts/c590-remote.sh` supplies the `ready-published` stage to the final fence.
Its result writer revokes the current invocation's installed receipt on a failed
preview, including a writer/directory-flush refusal or a phase-completion refusal.
The revocation is an atomic durable `state=Refused, consumed=true` transition and
checks the operation and installed preview nonce. It never rewrites a previous
invocation's Ready on an earlier failure. Immutable `preview-<nonce>.json`,
`preview.json`, audit/findings/proof and refusal evidence are preserved.
`lifecycle.readyPublished` remains a conservative historical fact that Ready may
have been published; it is not reset to hide that fact. A revocation persistence
failure refuses lifecycle completion rather than acknowledging an inactive phase.

A failed effect window persists a bounded, scrubbed record with only schema,
phase nonce, fixed stage, elapsed milliseconds and the unchanged 5000 ms limit.
Host records are in `publication-diagnostics.jsonl` (at most 8192 bytes); the
current case also contains `c1609-publication-diagnostic.json`. The normal bridge
copies the case evidence after refusal, so this no longer depends on stderr that
the coordinator discards. No URL, control payload, audit text or raw stderr is
stored in this diagnostic.

The final binding start is still taken from the helper clock before fresh origin
URL reads. Rendering does not reset that clock. Every effect still uses its own
fresh host proof, current approval, fresh advertisement/control and custody gates;
no earlier live-origin result is reused. The 900-second Ready admission, fsync
refusals, dirty-work refusal and clock/nonce/header checks remain in force.

## Focused red and green witnesses

The branch started at current `origin/master`
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. Production scripts were unchanged for
both red runs at `63686b3d2ec3a3737f25e2e85d4b0c04fb3d2df7`.
The first test commit `08e8bb38d` had an assertion-overload compilation error;
`63686b3d2` corrects that test-only error. It is not counted as assertion red.

`VolumeRetirementReceiptTests.Large_ready_rendering_precedes_the_unchanged_final_fence`
uses the existing real publication fixture, a fixture-owned origin, full proof
renewal/live control, at least 10,417 audit lines / 1,131,573 bytes, findings and
a deterministic six-second loaded-render delay. Red: 1 failed, after successful
live control, expected exit 0 but observed exit 2. Green at
`b0c6ab4e1719aab92a000fab9c728724bb620ae9`: 1 passed. The same workload rendered
exactly once before control, with no Ready receipt installed before that control.
The complete audit, findings and Ready proof were retained. Run durations were
47.202 seconds red and 47.173 seconds green (including discovery, excluding slot
wrapper overhead). This is a controlled rendering-cost witness, not a measured
claim about server2's historic renderer latency.

`VolumeRetirementReceiptTests.Late_ready_fence_refusal_invalidates_apply_admission`
delays six seconds after the real Ready writer returns. The fresh control succeeds
and the enclosing preview refuses `proof-stale` with no successful Ready marker.
Red: 1 failed because the mutable state was Ready instead of Refused. Green at
`b0c6ab4e1719aab92a000fab9c728724bb620ae9`: 1 passed. It checks Refused state,
unchanged immutable Ready evidence, low-level Apply rejection and rejection by
the actual `New-C1609RetirementReadPacket`. The persisted `ready-published`
diagnostic reports elapsed greater than 5000 with limit exactly 5000. No fixture
volume is removed. Run durations: 46.946 seconds red and 48.274 seconds green.

The two method timeouts are narrowed to 100 seconds after these provisional
samples (body about 41-43 seconds, n=2 per method), about twice the observed body
cost rounded up. Existing shared fixture/process timing remains active; no new
TestTiming operation or selector-policy row is introduced. The derived impact
index adds the existing fixture and public-reader dependencies; its driver was
built first, regeneration reported changed, then unchanged, and the index has its
own commit. Nightly policy and protected-input pins are unchanged because their
inputs are not changed.

## Verification and performance

The explicit Debug brief authorizes direct gated build/test rows. They use native
project output, `UseAppHost=false`, TUnit `dotnet run --no-build`, fresh TRX and
phase timing under `.antiphon/c1955/`. Initial load was `0.61 0.79 0.87`; load is
also retained per execution in `executed.jsonl`. Every driver leases a build slot.
Serial test rows have a nine-minute session deadline. The closed selection is the
retirement classes below, the required guard union and the derived selector
contracts; heavy classes are split into exact method selections in `rows.json`.
This avoids a whole-assembly test run. All rows retain source SHA, filter, exit,
wall time and load. Uninstrumented/remainder phases remain unknown.

Classes: VolumeRetirementApplyGateTests, VolumeRetirementContentTests,
VolumeRetirementReceiptTests, VolumeRetirementHostProofTests,
VolumeRetirementPublicationTests, VolumeRetirementWrapperTests,
RollingVolumeRecycleScriptTests, RollingVolumeRecycleCensusChurnTests,
RetiredTempContainerHostTests, RetiredTempContainerScriptTests,
RetireTempRunnerTimestampTests, C1008PlatformContractTests,
C1008HostFixtureSetupInsertionTests, RemoteScriptContractTests,
C0824BranchDeletionRecycleAuditTests and C1364BoundLandWaitProofTests.
Native Docker classes are separate rows so their owned-daemon prerequisites and
cost cannot be mistaken for the shell fixtures.

Guard filter:
`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/*`.

Mutation checks are waived by the operator's 2026-11-12 instruction. Native
Windows/live server2 acceptance is not inferred from these local Linux fixtures.
The assigned brief prohibits touching server2, either live qualification run,
live volumes, deployment/restart or token-bearing retirement commands; none is
part of this repair execution. The initial PowerShell row helper was stopped
before launching any test (unquoted native wildcard expansion); execution uses a
literal-argv Node helper instead. That helper issue is not assertion red.

Final execution counts and unresolved environment failures are recorded in the
stored task report, alongside exact tested SHAs and fresh TRX paths.

## Activation without a new rc

Owner: [Exact origin salvage recovery / Script-only retirement activation while
the desktop stays on rc](../docker-stack.md#script-only-retirement-activation-while-the-desktop-stays-on-rc-card-1955).
`scripts/c590-real.ps1` reads sibling files under its own `$PSScriptRoot` when
preview runs and copies c590-remote, c1008-retirement, c1008-content-audit and
c1008-volume-metadata into a fresh owner-only host operation directory. It
verifies SHA-256 and seals that bundle read-only, retaining `tooling.sha256`.
The credential-capable producer is loaded from the same local script set.
These host shell scripts are not baked into the selected runner image.

With the existing invocation from `C:\src\Antiphon`, source bytes would still
come from that desktop checkout, detached at rc/2026-11-22
`a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`. Publishing the repair to master does
not change it. Hosts continue to track rc, never master.

After full Review and confirmed land, the owner procedure exports the entire
confirmed tooling commit using `git archive` into a fresh ignored desktop
`C:\src\Antiphon\.antiphon\retirement-tooling\<landed-tooling-sha>\` directory.
Invoke that export's `scripts\deploy-server2.ps1` with explicit
`-Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -DryRun`.
The exact commands and publication checks are in the owner section above.
Retain the export SHA, producer hash, operation-owned tooling hashes and preview
exit/result. Its receipts are below the export's `.antiphon/rolling-server2/`.
The `-Sha` remains runtime provenance, while the fixed script bytes are selected
by the invoking script directory. No product image rebuild, rc cut, checkout
switch, restart or live proof reuse is needed. The first action is read-only
preview; only a newly successful full preview can authorize a later Apply under
the existing owner policy, immediately within 900 seconds, using the same export.
A refused operation must never be used as the Apply proof.

Standing caller authority already commissions this repair; no additional go-ahead
was requested. Review and host activation remain separate caller work.

## Verification settlement: incomplete gate

The final production/test candidate is
`b7bd91d4f73146ca61f53f04b271701e2232c456`; the subsequent commit only records this
settlement. Latest distinct TUnit outcomes across the recorded source revisions
are **297 passed, 1 failed, 1 skipped**, not a single-tip whole-suite run. All
foreground drivers and owned children were joined before this amendment.

| Selection | Latest outcome |
|---|---|
| Required guard union plus ImpactedClassesSelectorTests | 164 passed, 1 Windows-only skip |
| VolumeRetirementApplyGateTests | 13 passed, whole class |
| VolumeRetirementContentTests | 41 passed, whole class |
| VolumeRetirementReceiptTests | 17 passed, whole class, including both new witnesses |
| VolumeRetirementHostProofTests | 14 passed, whole class |
| VolumeRetirementWrapperTests | 7 passed, whole class, including real Preview/Apply renewal |
| VolumeRetirementPublicationTests | 25 passed; eight graph-corpus tests not run |
| VolumeRetirementDockerTests | N1 and N2 passed; N3 failed |
| PTY ProcessSpawnLimitTests | 2 passed |
| SessionRunner ProcessSpawnLimitTests and C1818GcOwnerRobustnessTests | 12 passed |

The guard union originally reported 163 passed, 1 failed, 1 skipped at
`bc205d20edfdac55b81aacaa9d63787b359a99eb`: the introduced failure was missing
source-only admission entries for the two new native tests. Commit `b7bd91d4f`
adds those entries; the affected whole WindowsUnitPlatformAdmissionTests class
then reported 6 passed, 1 Windows-only skip. Preserve the original failure.
The layering receipt is `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

Native N3 (`Exact_deletion_and_crash_resume_preserve_other_volumes`) failed its
first exact-delete assertion: expected exit 0, observed exit 2 with
`RecycleGitAuditUnknown reason=proof-stale`, removed=0. The unchanged native N3
fixture was rerun against a complete `git archive` of base
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`, with a local detached provenance HEAD
and shared read-only object lookup. It failed the same assertion/refusal at
`c1609-retirement-real-cases.mjs:455`. This is confirmed inherited red, not a green
acceptance result. Both candidate and baseline reported
`C1609_NATIVE session cleanup=absent`. No assertion, deadline or refusal was
loosened. The baseline fixture driver took 41.13 seconds, plus slot overhead.

The longest test driver was 461.878 seconds; rows remained below ten minutes.
Final load sample was `1.65 1.48 1.47`. Build drivers completed with zero errors;
warnings are not claimed absent. The derived-index driver was built first and
regeneration twice returned changed/unchanged. Protected policy inputs did not
change; the policy, protected-input and legacy-exclusion pins remain those in the
brief. Mutation checks remain explicitly waived.

Required execution remains absent for the eight
`VolumeRetirementPublicationTests.Origin_graph_corpus_seed_190[1-4]` methods
(including each `_second_half`), and these whole classes:
RollingVolumeRecycleScriptTests, RollingVolumeRecycleCensusChurnTests,
RetiredTempContainerHostTests, RetiredTempContainerScriptTests,
RetireTempRunnerTimestampTests, C1008PlatformContractTests,
C1008HostFixtureSetupInsertionTests, RemoteScriptContractTests,
C0824BranchDeletionRecycleAuditTests, C1364BoundLandWaitProofTests,
RollingVolumeRecycleDockerTests and RetiredTempContainerDockerTests.
Additional derived-impact closure support selections also remain unrun; the
retained manifest records them. Source-inventory helper names in that manifest
are not executable tests and must not be credited as coverage or run as zero-test
rows. Use actual discovered test methods for bounded continuation.

There is a concrete row-contract conflict: legacy
`RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` documents measured
618/709/827-second runs and a 20-minute timeout, while this brief requires every
row under ten minutes. Its fixture has no case selector. Bounded sharding or an
explicitly revised row contract is needed; a nine-minute cancellation is not a
passing run. The caller was asked whether to commission that separately or
extend this task; no extension was received. The 150-minute budget was exhausted
at the end of joined verification; only settlement checks/reporting followed.

Retained evidence is under `/work/worktrees/ws-01/.antiphon/c1955/`: per-row TRX,
`executed.jsonl`, `summary.json`, `deferred.jsonl`, `rows.json`, phase logs and
`base-native.log`/`base-native.json`. These are ignored generated evidence.
The evidence-diff guard over the seven implementation commits reported zero
violations; the final report amendment is checked separately. No live host,
qualification run, deployment, restart or server2 volume was touched.

Product witnesses pass; verification completeness is blocked by inherited N3 red,
missing execution and the legacy row conflict. This is neither Clean/Full nor
Interim. Continue the exact missing execution and inherited fixture repair under
Code, then obtain the requested full ordinary Review before land/activation.

## Reopened Debug: native N3 answer and bounded verification

This section supersedes the earlier settlement's current-status conclusions; the
older outcomes, costs and missing rows above remain historical facts.

N3 is an **expected-success native end-to-end witness**, not a test whose desired
outcome is proof-stale. Its old form exercised real Docker deletion/crash-resume
but neither supplied the required live bidirectional proof exchange nor included
the large rendering workload. Therefore the identical base/candidate refusal was
an inherited fixture defect, not evidence that the production repair had cured
the real path and not an unavailable Docker/host prerequisite. N1/N2 had already
proved those native prerequisites. The original failing run predated diagnostic
instrumentation and persisted no elapsed timing.

The instrumented old N3 failed before the final binding clock even started:
`stage=exchange-required`, `elapsedMs=null`, `limitMs=5000`; stderr explicitly says
`elapsed_ms=unstarted`. No numeric zero or invented elapsed is attributed to that
failure. The bounded scrubbed case/host diagnostic now supports this pre-clock
refusal alongside control, Ready and per-effect timing. No audit, token, URL or
raw control response is persisted there.

N3 now drives the real live protocol: fresh origin proof production and validation,
renewal/control over owned docker exec pipes, current object sets, UID/custody and
fresh approval/advertisement checks. The first successful preview also renders at
least 1,131,573 bytes / 10,417 lines and 105 findings with a deterministic six-second
render delay. Both file flushes and staging-directory fsync finish before binding.
At source `771876d2858903f7646ab3313c606e6ff7b551da`, persisted native observations are:

| Stage | Elapsed ms | Clock/result |
|---|---:|---|
| Full render and staged durability | 6260 | final binding clock unstarted |
| Final live control | 1240 | passed; limit 5000 |
| Ready publication including remaining durable writes | 1360 | passed; limit 5000 |
| Four exact deletion effects | 1560 / 1880 / 3050 / 2300 | each passed; limit 5000 |
| Crash-path effect before intentional stop | 2120 | passed; limit 5000 |
| Three resume effects | 2290 / 1670 / 2180 | each passed; limit 5000 |

`NATIVE_GATE totalMs=19090` includes render/renewal work before the final binding
clock; it is not the five-second elapsed. No remaining audit/file/directory render
work consumed that final window. The separate focused late-refusal witness still
proves canonical Ready revocation and actual public read-packet Apply refusal.

The original combined N3 exceeded its existing 360-second child deadline after
its live fences passed. It is now two exact-method pieces, retaining all four
original scenarios: large preview/exact deletion and after-rm crash/resume in
`Exact_deletion_and_crash_resume_preserve_other_volumes`; replacement and
unattributed absence refusals in `Replacement_and_unattributed_absence_refuse_resume`.
Both passed (1 test each), gated wall times 254.705 and 179.409 seconds, with owned
cleanup checked absent. Intentional crash exit 137 and resume refusal exit 2 are
asserted subcall outcomes, not failed tests. Intermediate fixture integration and
deadline failures remain in their original TRX; they are not the rendering red
witnesses and are not relabelled green.

Legacy native comparison rows are now closed serial shards, not revised or
omitted coverage. RollingVolumeRecycleDockerTests has four pieces: the original
`C1008_Real_docker_comparison` name selects part 1; suffixes `_part_2` through
`_part_4` select the rest. Their exact native case counts are 12/8/7/7, union 34
(base 5, changed 29), with 1 passed TUnit test per piece. Gated wall times were
252.967/189.590/222.994/232.496 seconds. Part 1 source was
`9acb89cc4a81eb791572a9423e4457d8ae5ff67b`; parts 2-4 source was
`135c40fbdcf0f1e86725f6d883a17101b7f6d9d0`. The later first-method rename only
preserves the frozen baseline identity; the executable shard body is unchanged.
Native evidence JSON was copied byte-for-byte into retained ignored evidence and
hash-checked, including source SHA, exact roster and failure=null. Checked cleanup
was absent on every successful piece.

RetiredTempContainerDockerTests similarly retains its original
`C994_Real_retired_temp_lifecycle` name for part 1 (RD-1..4), with `_part_2` for
RD-5..10. Stateful sequences remain together. Children retain an eight-minute
deadline plus at most one minute owned cleanup; TUnit invocations remain below ten
minutes. No production resource-name firewall, identity assertion, crash assertion,
refusal branch or cleanup check was removed. Legacy fixture setup was updated for
current module paths, audit descriptors, actual fresh Preview before Apply and
actual public read-packet handling. Its first attempted part-1 runs found missing
current proof-producer/reader files in the fixture export; those failures remain
recorded.

The required guard union at `b68748d40918fbc9f2c5eed50aaae9c584b185d2` reported
113 passed, 1 failed, 1 Windows-only skip in 214.301 seconds. The introduced failure
was NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute: the first
shards had renamed the two frozen baseline method identities. The fix restores
those original method names for part 1 and keeps the other explicit shard names.
No policy/baseline exclusion or marker is loosened. Only that failed guard needs a
narrow green rerun; the passing union members are not rerun merely to aggregate.

No TestTiming span or selector-policy row was added. The original 50 passing
ImpactedClassesSelectorTests remain credited; they are not redundantly rerun.
Optional unrelated derived-impact support closure rows are excluded from this
Debug task's explicit retirement/guard acceptance scope under the caller's minimal
testing instruction. Actual retirement classes, all eight corpus tests and the
required guard union remain required. Source inventory helpers are not tests.
