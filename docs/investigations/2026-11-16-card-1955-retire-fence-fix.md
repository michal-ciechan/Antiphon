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
