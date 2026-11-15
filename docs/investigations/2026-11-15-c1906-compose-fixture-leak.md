# CARD-1953 compose fixture leak repair

Test-only repair on branch `feat/card-task-68f81b18`, starting from fetched current master
`9083d4d003b246f9ab191a7be87997abb77bd1df`. No production source or
`BuildDriverEnvironment.cs` changed. The caller's standing test-only repair authority
was used without additional approval; mutation checks remain operator-waived.

## Defect and ownership

Both `c1906-compiler-policy-compose.mjs` and `c346-workspace-slot-compose.mjs`
created unique checkout-local `.antiphon/*-compose-*` roots without deleting them.
The c1906 native PTY/build-wrapper inheritance test also left its
`.antiphon/c1906/runtime-*` root behind.

The shared test-only Node helper now chooses `/var/tmp` on Linux and the local
OS temp directory on Windows. The root's exact parent, prefix plus unique identity,
ownership marker contents, and absence of root/marker symlinks are checked before
recursive removal. Creation and execution are enclosed by finally, including setup
and assertion failures. A Node supervisor owns the synchronous worker's root,
waits for its exit and removes it after SIGINT/SIGTERM cancellation; an interrupted
worker does not have to service a JavaScript callback while blocked in spawnSync.
The C# test parent owns its supplied root through a using scope, so its existing
forced child-tree kill still returns through a parent cleanup finally. The native
PTY runtime root uses that same C# owner; the PTY session disposes before its root.
No existing functional assertion, child deadline, or test timeout was changed.

`df -T /var/tmp /tmp /work/worktrees/task-68f81b18` showed `/var/tmp` on overlay `/`,
with `/tmp` and `/work` separate ext4 volume mounts. No live runner, Docker volume,
service restart, deployment or Windows qualification run was touched.

## Red-first evidence

At the unmodified base, the cheapest selection was:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1953-red-count -- node scripts/fixtures/c1906-compiler-policy-compose.mjs --refusal-case=empty --refusal-action=compose_host
```

It passed its one refusal assertion, exit 0, while the checkout count increased
from **1 to 2** c1906-compose directories. The first exploratory invocation had
already created the initial directory; the count helper initially attempted
unavailable python3, so the credited before/after observation used Node instead.

The independent cleanup witness was committed and pushed first at
`3c23b09436d28696fb081d36df8bad4b7810c27e`. It then exited 1 at the intended
`fixture disposal must remove its exact root after success` assertion: the root
still existed. It was a real assertion failure, not a build/fixture error.

After recording these findings, only these three producer-owned literal absolute
paths were manually removed:

- `/work/worktrees/task-68f81b18/.antiphon/c1906-compose-ARVtD4`
- `/work/worktrees/task-68f81b18/.antiphon/c1906-compose-XRE31l`
- `/work/worktrees/task-68f81b18/.antiphon/c1906-compose-myE2VI`

No glob cleanup, git clean, foreign root or live volume was involved.

## Audit of other .antiphon writers

The read-only inventory used `rg -n '\.antiphon' tests --glob '*.cs'` (779 source
references) and a narrower root-creation candidate inventory (284 references).
Many references are literal policy vectors, synthetic paths inside already owned
scratch repositories, or deliberately retained report artifacts, not actual roots
in the task checkout. `scripts/fixtures/*.mjs` was checked as well; the only direct
checkout-local fixture root creators there were the two repaired Compose scripts.

| Family / source | Finding and disposition |
|---|---|
| c1906/c346 Compose scripts and RunnerLinuxCompilerPolicyTests | Fixed here, including the PTY runtime root. |
| RemoteScriptContractTests c849 front-door / rolling / c983 cases | Owned checkout-local fixture/evidence paths have explicit cleanup; front-door parent removes its scratch in finally and its harness removes its recorded evidence roots. No matching unowned success leak found. |
| C1008HostFixture, C1008ComposeSeed, VolumeRetirementContentTests, HostJqPrerequisiteScriptTests | Nested `.antiphon` paths belong to disposable scratch parents; these are not additional direct task-checkout roots. Existing C1008 scratch parents use OS temp and are removed by Dispose. No unrelated redesign of that family here. |
| VolumeRetirementPublicationFixture | Already repaired by CARD-1901; inspected its ownership/disposal pattern only. No edit. |
| EvidenceGitFixture, ReportWorkspace, CompletedCardCleanupFixture, completed-card/retention/worktree policy vectors | Owned synthetic parents have disposal paths; `.antiphon` references commonly name content inside those parents. ReportWorkspace retains its required persistent-root semantics and deletes its owned root. |
| WindowsUnitPlatformAdmissionTests, C1008PlatformContractTests, RestartFixture and readiness/diagnostics/restart tests | Retain audit artifacts under c1210-audit, c1050-audit and c420-evidence. Distinguished from operational fixture roots; no synthetic-repository claim made for these evidence paths. |
| CodexBootWedgeProbeTests and other CLI probes | Retain diagnostic artifacts; owned CLI cwd/home runtime roots are separately removed. Literal model-provider names are not filesystem writers. |
| OwnedProcessContainmentFixture, LandQueueRaceWorker | Runtime script, pid and rendezvous files are deliberately retained without filesystem disposal. Filed CARD-1954. |
| LandDeliveryFixture, ChannelOutboundIsolatedTests.Composed, OutputDistillationApplyCanaryTests | Retain real synthetic repositories alongside acceptance evidence under the checkout. Filed CARD-1954 to separate disposable operational roots from retained evidence while preserving report-store custody constraints and Windows placement. |

CARD-1954: `ae39008c-d6cb-494a-8724-0fcab3529290`,
“Separate retained acceptance evidence from disposable fixture repos and rendezvous roots”.
Duplicate searches for `containment-fixtures` and `retained acceptance` completed
all pages and returned zero cards before filing. This audit is source inspection,
not a live-volume cleanup or execution certification of unrelated tests.

## Verification

Implementation source: `67835ba18d3cd97ac092bd04b00aa944193851c3`.
The standalone cleanup witness passes **8 cases**: success, assertion failure,
setup failure and cancellation for each Compose script. Each verifies its exact
root is gone, the base is the chosen platform temp directory, and checkout
c1906/c346-compose root inventory stays empty. The historical red remains retained.

The full c346 fixture passes **120 refusals, 16 render matrices, missing-file and
base-equivalence checks**. The ordinary named rows below are intentionally direct
native-output builds/runs under build-slot, per this Debug brief; no expanded full
suite or checkpoint manifest was commissioned.

| Row / selection | Total / passed / failed / skipped | Leased wrapper wall seconds |
|---|---|---|
| WindowsUnitPlatformAdmissionTests alone | 7 / 6 / 0 / 1 | 34.515 |
| CP-1 RunnerLinuxCompilerPolicyTests, first green | 35 / 35 / 0 / 0 | 33.547 |
| CP-6 ProcessSpawnLimitTests | 57 / 57 / 0 / 0 | 15.346 |
| ImpactedClassesSelectorTests | 50 / 50 / 0 / 0 | 260.504 |
| Required eleven-class guard union | 115 / 114 / 0 / 1 | 217.949 |
| CP-1 RunnerLinuxCompilerPolicyTests, final supervisor | 35 / 35 / 0 / 0 | 33.236 |

Required scope, counting CP-1 once: **264 results, 262 passed, zero failed,
two skips**. Including its justified final rerun: **299 results, 297 passed,
zero failed, two skips**. Both skips are the same explicitly Windows-only
`C1210_Windows_linux_only_outcomes_are_exact` audit, once alone and once in
the guard union; they do not claim Windows execution. Every intended class has
nonzero fresh TRX results. Layering printed exactly:

```text
VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3
```

The original five C# rows ran on clean committed source
`d97c7cbc19ec99e473f67b784b5290d02486db98`. The final CP-1, standalone cleanup
witness and c346 run used `da4faab4b4ee401086706b9503df73b4ac007058`.
All use the one native-output C# build from
`67835ba18d3cd97ac092bd04b00aa944193851c3`: no `.cs`, `.csproj`, `.props` or
`.targets` bytes changed afterward. The subsequent Node-only supervisor change
justified repeating only its affected fixture rows. The guard/roster/selector
source, index, policy and build inputs were unchanged, so their earlier green
receipts retain their actual source identity rather than being relabelled.

### Introduced supervisor cancellation defect

Inspection after the first green rows found that the new supervisor signalled
only its Node worker, allowing an active worker subprocess to survive. This was
introduced by this repair, not inherited from the base. A stronger cancellation
probe was committed and pushed at
`509920eebca996b0ae391443e85b98df118a9cce` before the fix. It starts a real local
Node subprocess with inherited output and requires cancellation to close those
streams within a bounded two-second shutdown interval. It failed as intended:
`worker descendants survived cancellation`. Its finally rescued only the exact
subprocess PID reported by that owned probe and awaited output closure. No test
process was left alive.

`da4faab4b4ee401086706b9503df73b4ac007058` gives the Linux/POSIX worker its own
process group and signals that owned group, including an active bash/Compose
subprocess. The same single cleanup witness then passed all eight cases, in a
two-second leased row. The full c346 fixture passed again in nine seconds, and
CP-1 passed again 35/35. Both CP-1 TRX inventories name **37 unique owned roots**
(35 Compose roots plus two PTY runtime roots); every one was absent afterward.
Final checkout c1906/c346-compose root count is zero.

### Build, index and timing provenance

The leased test build passed: **0 errors, 2138 warnings, 221.27 seconds** MSBuild
elapsed (222-second held lease). Existing functional assertions and deadlines
remain intact. No warning-free claim is made. The index driver was built FIRST in
its own lease: **0 warnings, 0 errors, 8.71 seconds** MSBuild elapsed. Both subsequent
index regenerations had separate leases: first `derived-impact-index changed`,
then `derived-impact-index unchanged` (27 and 29 seconds held). The only index
change is the six-line edge from `ComposeFixtureRoot.cs` to
`RunnerLinuxCompilerPolicyTests`, committed separately at
`d97c7cbc19ec99e473f67b784b5290d02486db98`.

All drivers were foreground local inherited children, run serially through
`scripts/build-slot.ps1` and joined. All recorded admission waits were zero.
Every row was below ten minutes; the longest was the selector at 260.504 seconds.
Initial loadavg was `1.12 1.18 1.56`; observed samples included
`16.23 6.08 3.12`, and the final fixture checks recorded `1.35 2.46 3.01`.
These single-run contended-host observations are provisional, not a measured p95.
TRX preserves per-test durations and opt-in phase JSONL was enabled for every
C# row. Uninstrumented setup/CLI phases remain unknown; wrapper wall times are
not phase sums. The standalone witness and script rows retain build-slot held
seconds; finer uninstrumented phase timing is unknown.

An initial external PowerShell continuation wrapper stalled before launching
any leased driver while resolving a native wildcard argument. Its run.log stayed
empty, there was no test child or TRX, and its owned wrapper/shell were stopped
and joined (exit 143). The corrected wrapper uses ProcessStartInfo.ArgumentList
for literal filters; the real rows above all completed. This preliminary attempt
has no test credit and did not alter source or assertions.

Ignored raw evidence root:
`/work/worktrees/task-68f81b18/.antiphon/task-68f81b18/`.
It contains original red logs, both witnesses' red outcomes, build/regeneration
logs, source audit inventories, and each green row's `row.json`, `run.trx`,
`run.log` and phase `timing/` directory. No generated evidence was committed.

## Policy pins and handoff

The fetched master and task both retain:

- policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`
- PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`
- LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`

The first two were recomputed using the production policy hash and protected-input
readers; the shared native-policy guard validated the legacy exclusion seal.
An exploratory hash command omitted the nightly-common helper import and printed
no computed hash; the corrected command produced the value above. No policy pin
input changed, so no re-pin was made. The new C# helper calls no native-Linux gate;
roster closure passed without a new roster row.

All required Debug selections passed. No full-suite or native-Windows qualification
credit is claimed. Test-only repairs skip Review under the supplied standing
authority. The unrelated retained-fixture follow-up belongs to CARD-1954.
The next stage is **merge**.

Re-run an exact C# class with the existing build after a matching leased build:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1953-class -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/<ClassName>/*' --report-trx --results-directory <fresh-absolute-evidence-directory>
```

Re-run the single disposal witness with:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1953-disposal -- node scripts/fixtures/compose-fixture-cleanup.test.mjs
```

The guard union's exact filter is retained in `guards/row.json`; it contains
TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests,
SlowTestTripwireTests, NativeLinuxPolicyWitnessTests, DerivedImpactIndexTests,
MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests,
TestLaneCategoryGuardTests and WindowsUnitPlatformAdmissionTests.

Commit history (full source identities):

- `3c23b09436d28696fb081d36df8bad4b7810c27e`: initial failing disposal witness.
- `67835ba18d3cd97ac092bd04b00aa944193851c3`: external roots and owned disposal.
- `d97c7cbc19ec99e473f67b784b5290d02486db98`: regenerated index, separate slice.
- `509920eebca996b0ae391443e85b98df118a9cce`: failing subprocess cancellation probe.
- `da4faab4b4ee401086706b9503df73b4ac007058`: worker process-group cancellation repair.

At the final code tip, `git diff --check` passed. The history guard over base
through `da4faab4b4ee401086706b9503df73b4ac007058` reported five commits, zero
generated-evidence entries and zero violations. A fresh `git fetch origin master`
retained `9083d4d003b246f9ab191a7be87997abb77bd1df`; `git merge-tree --write-tree
origin/master HEAD` exited 0 without conflicts, tree
`92bd96019cc7976e5276c31b0878d95e869217e0`. Settlement also checks the final
narrative commit and confirms the pushed task-branch tip. No rebase, merge,
amend, force-push or protected-branch push occurred.
