# CARD-1544: verify the already-landed census gate repair

Code task and landing owner: `5fb6b1b2-44dd-47c3-8a99-8eea293f1ad6`.
Assigned base: `df1fc145181a3bedf444f9113f822f30d3b136f6`.
Round: Final. Restart: none.

## Disposition

The card's defect was already repaired by
`01231c66d570daceba09608edb59343647d4ce82` (CARD-1572), an ancestor
of the assigned base. That commit removed the conditional gate from
`C1374CensusProbes.Check`, put `C1008HostFixture.RequireNativeLinux()`
first in `RemoteScriptContractTests.C1374_Host_census_recovers_transient_reads`,
and added that caller to `WindowsUnitSkipRoster.Existing` with one result and
the unchanged `ExistingReason`. The helper has zero independent skip results.
The wrapper caller remains portable. No additional roster/helper entry is needed.
The caller is outside the Windows child selection, so child runtime totals do not
change. Preserve the bidirectional all-source closure assertion unchanged.

This task changes documentation only. Do not recreate a corrected defect to
manufacture a new Code red/fix pair. The card records historical red at
`77eb7806d35b3b522d46e3942742e4864c688de3`: 1 executed, 0 passed,
1 assertion failure naming `Antiphon.Tests.Scripts.C1374CensusProbes.Check`.
That historical result is attribution from the card, not a fresh run by this task.
New deliberate mutants belong to SourceLanding Mutation.

## Verification design

- V-1: establish ancestry and inspect the exact three-file repair; assert current
  helper/caller placement by reading source, without weakening any gate or reason.
- V-2: execute the existing exact closure method. Its real repository scan rejects
  an unrostered helper and a roster entry without a gate, including qualified and
  aliased gates. A fresh green confirms that the original omission is absent.
- R-1: full `WindowsUnitPlatformAdmissionTests`, including guard/reason,
  first-statement, closure, negative audit vectors and Windows-shaped host census.
  Its native Windows child test is explicitly skipped on Linux; a skipped native
  audit is not native Windows proof. No OS-selected production behavior changes.
- R-2: full named policy sweep, layering guard, mechanical contract and platform
  guidance classes in CP-2. `SlowTestRegistryMergeTests` is the named Integration
  class; no product Integration class is affected by this documentation task.
- R-3: whole Unit lane in CP-3, including method-level Unit tags on non-Unit
  classes. This is required by the explicit Final profile. It does not replace
  named integration, delivery, landing, lease or persistence verification.
- Manual: inspect fresh TRX identities and nonzero counts for every intended
  class/method; validate source/build receipts; check full base..HEAD evidence
  history; inspect OS/startup impact; fetch master and run merge-tree; verify the
  pushed task ref. Policy, nightly policyHash and live-lane input pins stay unchanged.

## Performance budget

CP-1 is a single existing syntax scan, provisional 1 minute. CP-2 is a bounded
combined class selection, provisional 5 minutes. CP-3 is the broad Unit lane,
provisional 15-minute estimate with the existing derived 45-minute row deadline.
There is no measured p95; the known whole-Unit cost can exceed 30 minutes.
Keep per-test TRX durations and checkpoint build/host/slot timings. Phase timings
not emitted by existing fixtures remain unknown. No timeout will be widened.
Existing fixtures own their setup and cleanup; this task introduces no fixture.
The full lane has unbounded class impact, including process-spawning Script and
Application tests, `RemoteScriptContractTests`, `RollingVolumeRecycleScriptTests`
and the checkpoint tool tests. It is not a full-assembly selection. The invariant
is preservation of the repository's complete Unit classification lane alongside
the exact source-gate closure, not a new claim about product runtime behavior.
If observed over 30 minutes, retain the run identity/timing and require the caller
to commission performance Debug; this delegate may not sub-delegate.

One native incremental compile of `tests/Antiphon.Tests` is explicitly required
by the brief. It also bootstraps the referenced checkpoint tool. Run it once
through `scripts/build-slot.ps1`, native output, `UseAppHost=false`, `TEMP=/tmp`.
This is the only non-table build. Checkpoints use their own isolated outputs.
Run the three-row closed list through the checkpoint tool, passing committed
HEAD as `--expected-source-sha`, `--max-wait 50s --heartbeat 30s` and waiting
until terminal. Never rerun a green row. Preserve failed receipts and rerun only
red rows for attribution. Do not run an entire assembly to attribute a failure.

### Checkpoints

totalTimeoutMinutes: 65

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1544-closure/` | exact-closure | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | V-2 | 1 passed, 0 failed/skipped | 1 | 1 | true |
| CP-2 | all | CP-1 | named-guards | `/*/*/(WindowsUnitPlatformAdmissionTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/*` | R-1, R-2 | nonzero for every class; only explicit native/capability skips | 40 | 5 | true |
| CP-3 | all | CP-1 | full-unit | `/*/*/*/*[Category=Unit]` | R-3 | nonzero full Unit receipt; disclose every failure/skip | 1000 | 15 | true |

### Positive controls

All remain pending for method-scoped SourceLanding Mutation, including
missing-control discovery. Candidate:
`docs/superpowers/mutations/2026-11-06-card-1544-source-closure-candidate.md`.
No Code mutant cycles are authorized by the stage contract.
