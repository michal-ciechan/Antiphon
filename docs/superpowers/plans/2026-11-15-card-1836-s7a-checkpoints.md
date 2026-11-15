# CARD-1836 S7a baseline picker verification

Owner: task 7342010d, base 08cc3dcac230658c9bb7f580d32ad91906665fbd.
Implements activation plan D-7/S7a/V-7 only. No R-n belongs to S7a.
V-7 is the three named RcReleaseBaselineTests methods. Existing S5b reader
fixtures supply sealed source/build/launch/TRX/anchor evidence; an optional
FixtureOnly return shares that setup without running another test host.
RcHistoricalAdmissionTests is the named affected adjacent class.

The picker is diagnostic, never skip authority: it verifies baseline provenance
at its own source, preserves the admission reader's algorithm-coverage Full gate,
and returns a pinned proposal, anchor and later failure facts. It accepts sealed
admission pointers (refName, sourceSha, path, digest, evidenceRoot). Release pointers
use full refs/heads/release/x.y.z names; historical rc candidates use full commit
object IDs, since a moving rc branch cannot pin a previous candidate. No refs are
created by the picker. An explicit recorded release must match its pointer exactly.
Newest means descending committer timestamp, ascending ordinal full ref on ties;
rc fallback additionally requires the same recorded rc line and a strict ancestor.
A corrupt claimed ancestor refuses the request instead of selecting another green.
Current failure history is independently sealed and candidate-bound; nonempty facts
remain Full and are returned, with no attempt to clear fixes in this unit.

Red-first diagnostics run the three standalone script cases through build-slot
before implementation: expected outcome assertions against the default-deny
adapter, not missing commands. These are declared unlisted runs. Final CP-7 has
fresh nonzero TRX. Guards include the brief union plus ProcessSpawnLimitTests;
CP-91/92 cover the impacted spawn census in the other two projects.
CP-93 covers the derived-index guard; CP-94 the existing release-envelope guard.
Build the native derived-index driver first, then regenerate twice, second unchanged,
with the index delta in its own commit. No policy/SDK/pin inputs change.

All fixtures use owned temporary Git/evidence roots. No live qualification, service,
runner process, publication or restart is involved. OS branch audit: no OS-selected
production branch/factory or startup path changes; metadata platform comparisons
are exercised with Windows and Linux inputs, not native Windows execution.
Native integrated S7d remains pending. Restart none.

V-1..6,V-8..18 and R-1..7 remain with their other slices, not passed by S7a.
PC-V7-precedence, PC-V7-ref-pin, PC-V7-platform-lineage, PC-V7-envelope,
PC-V7-anchor-history, PC-V7-no-baseline and all variants remain pending for
SourceLanding Mutation; operator waived Code mutants. No green repetitions needed.

## Performance budget

Provisional CARD-1836: each method <=30s, 60s method deadline; harness 45s plus
10s joined cleanup. Each fixture uses tiny local Git repositories and one retained
corpus per baseline, no DB/AppHost. Git operations retain their 30s bounds.
Collect first ordinary sample, preserve reds and retries; five comparable samples
across Code/Review/pilots before measured p95, never repeat green just to fill it.
CP-7 <=6m, guards <=4m, index <=5m, adjacent <=3m, envelope <=1m, other
project censuses <=2m each. Run rows serially below ten minutes, one build at a
time. Retain per-method/TRX, build/slot/host timings; unobserved IO subphases unknown.
No full assembly or Unit lane run: those remain nightly/rc-owned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-7 | S7a | `tests/Antiphon.Tests -> bin-c1836-s7a/` | baseline | `/*/*/RcReleaseBaselineTests/*` | V-7 | all 3 named methods, no failures/skips | 3 | 6 |
| CP-90 | S7a | CP-7 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(ProcessSpawnLimitTests*)/*` | policy, layering, categories, spawn | every named class, no failures/skips | 79 | 4 |
| CP-93 | S7a | CP-7 | index | `/*/*/DerivedImpactIndexTests/*` | derived guard | all methods, no failures/skips | 27 | 5 |
| CP-94 | S7a | CP-7 | envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | adjacent envelope guard | all methods, no failures/skips | 4 | 1 |
| CP-95 | S7a | CP-7 | admission | `/*/*/RcHistoricalAdmissionTests/*` | affected fixture consumer | all 7 methods, no failures/skips | 7 | 3 |
| CP-91 | S7a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s7a-pty/` | pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 1 |
| CP-92 | S7a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s7a-runner/` | runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 2 |
