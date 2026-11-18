# CARD-1984: a wider spawn lane for the script-model test classes (plan)

Date: 2026-11-17. Task `003fea68` (Plan, Opus). Plan only: no code, no setting change, no test run.
Master read: `62cfb330883cda0959d4f87f8f456933f35ca1cc` (`origin/master` and this branch's base at 2026-11-17).
Profile read in full: `docs/investigations/2026-11-17-slow-script-test-classes-profile.md` at
`f588b93945f9e00d9b869d8786591fb26e237c46` (branch `feat/card-task-95bdabde`). Every timing below comes from that profile
unless a line says otherwise. **UNOBSERVED** marks anything this plan did not read or measure.
Authority: operator 2026-11-17 (CARD-1984 is the fix; plan only; mutation checks waived 2026-11-12).
Amendment R1: task `8344871d` (Plan, Opus), for plan review round 1 `ddfc4db5d859a11d07b8c932b88ddaf877129432`
(`docs/superpowers/reviews/2026-11-17-card-1984-plan-review.md` on `feat/card-task-a8755037`). `origin/master` re-read:
still `62cfb330883cda0959d4f87f8f456933f35ca1cc`. Round 2 is the diff from `420ceb9af4ef807a2e6bf0223b4b02aac6d46a57`.

## Amendment R1 (review round 1)

- **B-1 (S5, O-5).** Microsoft.Testing.Platform `--timeout` is one global execution timeout per `dotnet run`, and Merge and
  review briefs pass `--timeout 570s`. The whole class at width 6 is about 10-11 minutes, so it cannot be one run. S5 no
  longer puts `RemoteScriptContractTests` in `classes`. It adds a `shards` map to the `remote-scripts` kind. Each shard is
  its own method row and its own run, and is never joined to `CP-merge-subset-tests`. The admission bar is now **per shard:
  at most 285 s measured** (half the 570 s cap, the 2x rule), and never over 570 s. The C1008 recycle matrices (`C1008_*`,
  longest case 315 s) cannot meet that bar. They stay with the nightly Unit lane until their scenario matrices are split
  (O-7). The rewrite is in §5 S5, D-10, W-7..W-9, CP-16..CP-20, §7 and §8.
- **B-2 (W-5).** The allow rule now accepts a literal that is an argument of `mkdtemp`/`mkdtempSync`/`mktemp -d`, a
  same-file stem of such a call, a string comparison, and five exact allowlisted tokens. These include the in-container
  `/tmp/antiphon-pty-hosts`. Negative controls on fixed literals (`/tmp/foo`) still fail. A scratch prototype of the rule
  found **0 findings** over the W-5 scan set at `62cfb330` and all five controls were findings (§3 W-5).
- **Advisory (D-5).** D-5's first sentence now matches the rule: a checkout-cwd child stays strict only when it writes the checkout.

## Summary

`ProcessSpawnLimit.Limit` is 1 (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15-18`). Every script-model case queues
behind every other one, so a row costs the sum of its cases. The census below found **no state that two concurrent cases
share by name**: every fixture root is a fresh `CreateTempSubdirectory`, `mktemp -d` or Guid path; the fake Docker, HTTP,
lock and trace files live under that root; no case binds a port, sets an environment variable in the test process,
changes the current directory or writes `HOME`/global git config. The one shared resource is the **shared checkout**: some
children run with it as their working directory, and four production scripts write a uniquely named evidence directory
under its ignored `.antiphon/`. The plan adds a third lane, `ScriptModelSpawnLimit`, on the `GitProcessSpawnLimit` and
`CheckpointToolSpawnLimit` pattern. Its width is 6 on server2. Methods whose child runs from the shared checkout stay on the
strict lane. That lane runs **beside** the wide lane (Gotcha #18), so keeping them strict costs only its own small sum.

## Ground truth (card / brief assumption vs the code at `62cfb330`)

| # | Assumption | What the code does | Source |
|---|---|---|---|
| G-1 | Every case carries `[ParallelLimiter<ProcessSpawnLimit>]`, limit 1 | True. Method-level in RemoteScript (193 uses), Rolling (39), RollingProductionMount (12), CensusChurn (1), ApplyGate (9), Content (34), Receipt (27), Wrapper (7), HostJq (40), RetiredTempHost (22), RetiredTempScript (16), C1364 (9). RunCheckpointScriptTests carries it **class-level** (1). | `grep -c` per file |
| G-2 | An "assembly-wide limiter" exists | **None found.** No `[assembly: ParallelLimiter/NotInParallel]` in `tests/Antiphon.Tests`; no `--maximum-parallel-tests` in the checkpoint tool or `run-checkpoint.ps1`. TUnit's own default parallelism applies; its value is UNOBSERVED here. | grep |
| G-3 | Wider-lane precedent exists | `GitProcessSpawnLimit` (`Clamp(ProcessorCount/2,2,4)`, `ANTIPHON_TEST_GIT_SPAWN_LIMIT` 1-8) and `CheckpointToolSpawnLimit` (2, override 1-4). Both "run beside the strict lane". | `TestHelpers/*SpawnLimit.cs` |
| G-4 | RemoteScript has 516 cases | Profile count (384 + 132 `C1105_Git_audit_hidden_locations` from a data source). A source count gives 211 `[Test]` methods, 188 `[Arguments]`. | profile, grep |
| G-5 | "Host census" cases read the host | **False.** C1361/C1374/C1697 "host census" means the server2 task census (API rows), simulated from `tasks.json` under the fixture root (`C1361CensusProbes.cs:1025` `CreateTempSubdirectory("c1361-")`; `c1087-host-http.sh` reads `$C1008_FIXTURE_ROOT/tasks.json`). No case reads `/proc` beyond its own process or runs `docker ps`. | code |
| G-6 | The fake Docker shares state | **False.** `c1008-fake-docker.sh:6` uses `process.env.C1008_FIXTURE_ROOT`; state is `<Root>/docker.json`; its scratch uses `mkdtempSync('/tmp/c1008-pty-')` and `mkdtempSync('/dev/shm/c1008-preview-')`; mounts happen in a private mount namespace (`mount --make-rprivate /`). | `scripts/fixtures/c1008-fake-docker.sh:138,296-345` |
| G-7 | Fixed `/tmp` names | Only `"/tmp/c849-guard-$$"` (PID-unique, `RemoteScriptContractTests.cs:7137`) and `/tmp/donor` (`:8411`, a refused argument, never created). `c590-remote.sh:830` `/tmp/c604-smoke-$RUN` is the live SSH smoke; no fixture case reaches it (the fixture stubs `ensure_*`; UNOBSERVED by trace). | grep |
| G-8 | Locks are host-wide | `c849_lock`/`c1008_rollout_lock` use `$SERVER2_ROOT/locks/*` (per root); the C1008 fixture stubs `c849_lock`. HostJq `HJ_LOCK = Root + "/lock"`. `c1192-workspace-contract.sh` locks under `$C1192_ROOT`. | `c590-remote.sh:1433-1440,6041-6046`; `HostJqPrerequisiteScriptTests.cs:1193` |
| G-9 | Per-run deadline 120 s | `C1008HostFixture.Run` (`RollingVolumeRecycleScriptTests.cs:3517-3519`) and `LinuxShellResult` (`RemoteScriptContractSupport.cs:90`) are 120 s (180 s via WSL). `C994ScriptProcess` 30 s; fake-docker inner `spawnSync` 15 s; `flock -w 60`. | code |
| G-10 | Shared seeds | `C1008ComposeSeed` is one `PerTestSession` seed (`RollingVolumeRecycleScriptTests.cs:3317`). It is immutable in memory and `CloneInto` only reads it; if two first callers race, the factory may run twice into two private temp roots, each deleted (harmless). | `C1008ComposeSeed.cs:8-80` |
| G-11 | Children write in the repo | Four production scripts write `<checkout>/.antiphon/<family>/<guid>` when run from the real checkout: `deploy-server2.ps1:68-70` (`c727<12 hex>`), `verify-card0849-caches.ps1:47-48` (`c849<16 hex>`), `c1609-manual.ps1:174`, and the C913 receipts fixture through the C849 front door. The names are unique. Git use there is `rev-parse` (read-only); `git status` runs only in `Invoke-HostJq -Mode provision`, and HostJq copies the scripts into its own root first (`HostJqPrerequisiteScriptTests.cs:1288-1296`). | code |
| G-12 | Text-only guards are about 190 | Sub-second is not spawn-free: many sub-second cases run `LinuxShell`. A crude source scan (scratch, not committed) gives RemoteScript 41 methods / 52 cases with no spawn token, and it misreads compact files (it called 15 RetiredTempHost methods text-only; they are not). The list must come from the IL witness W-4. | scan |
| G-13 | Derived-impact index is free to touch | `tests/derived-impact-index.json` is committed and checked byte-for-byte (`DerivedImpactIndexTests.committed_index_matches_the_builder`, `:177`). A new test class or helper changes it, and `scripts/regenerate-derived-impact-index.ps1` must run last in each slice. CARD-1820 S1R6 and CARD-1893 S0 also edit it. | code |

## 1. Shared-state census

Columns: T temp roots, P TCP ports, D docker/container names, G git config/HOME, E env vars in the test process,
C current directory, R writes under the repo, F fake Docker state, H host census reads, DL deadline.
"per-case" means created fresh per case with a unique name.

| Class (fixture family) | T | P | D | G | E | C | R | F | H | DL | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|---|
| HostJqPrerequisiteScriptTests (own `Fixture`: Root `GetTempPath()/c1025-<guid>`, `TMPDIR`, `WorkingDirectory=Root`, scripts copied into Root) | per-case | none | none (fake tools on PATH) | none | child env only | Root | none | n/a | own `/proc/<child>/stat` | 120 s C1008 run in 1 case | **SAFE-AT-WIDTH-N** |
| RollingProductionMountTests (C1008HostFixture + `node c994-production-compose-model.mjs`, cwd checkout, read-only) | per-case | none | fake, per root | none | child env | checkout (read) | none | per root | none | 120 s | **SAFE-AT-WIDTH-N** |
| RollingVolumeRecycleCensusChurnTests (`CreateTempSubdirectory("c1099-census-")`) | per-case | none | fake | none | child env | — | none | per root | none | 120 s | **SAFE-AT-WIDTH-N** |
| RollingVolumeRecycleScriptTests: C1008HostFixture cases | per-case | none | fake | none | child env | — | none | per root | none | 120 s | **SAFE-AT-WIDTH-N** |
| RollingVolumeRecycleScriptTests: C727 wrapper cases (`pwsh scripts/deploy-server2.ps1 -Rolling`, cwd checkout, `:3218-3240`) and C1008 host-proof (`:3037`) | per-case state/trace | `127.0.0.1:1` (refused, no listener) | fake | none | child env | **checkout** | **`.antiphon/rolling-server2/c727<12hex>`** | per root | none | 30 s run | **KEEP-AT-1 (method level)** under the existing shared-checkout rule (D-5); SAFE by effect |
| VolumeRetirementApplyGate / Content / Receipt / Wrapper (C1008HostFixture, `VolumeRetirementFixture`, `CreateTempSubdirectory("c1689-exchange-"/"c1609-w2-"/"c1609-w3-")`, `/tmp/c1609-custody-<guid>`) | per-case | none | fake | none | child env | checkout for `VolumeRetirementFixture.cs:147` pwsh, `Wrapper:112,495`, `Receipt:749` node | **c1609-manual / retire-runner-volumes evidence under `.antiphon/`** for the pwsh drivers | per root | none | 120 s | **SAFE-AT-WIDTH-N** for C1008HostFixture-only methods; **KEEP-AT-1** for methods that start pwsh from the checkout (D-5) |
| RetiredTempContainerHost / Script, C1364BoundLandWaitProofTests (same C1008 family; `C994ScriptProcess` cwd checkout, `chmod`/pwsh on absolute paths) | per-case | none | fake | none | child env | checkout (chmod, read) | UNOBSERVED per method | per root | none | 120 s / 30 s | **SAFE-AT-WIDTH-N** after the per-method R check in S3 (not in the brief's list; same fixture family, profile CP-8 group) |
| RemoteScriptContractTests: C1008 host, C1105, C1192, C983, C849 shell, C1066, C1208, C912 shell, C913 shell (LinuxShell `bash -s`; `mktemp -d /tmp/<prefix>-XXXXXXXX`; C1192 `mktemp -d /tmp/c1192-parent.XXXXXX`; C1105 `mkdtemp('/tmp/c1105-audit-')`; C983 per-fixture root) | per-case | none | fake, per root | none (only `git config --file` under the root) | AsyncLocal only (`ForceNoLinuxPwsh`, `ForceLinuxJqAvailability`) | process cwd (dll dir), not changed | none | per root | none | 120 s | **SAFE-AT-WIDTH-N** |
| RemoteScriptContractTests: `C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` (`:1903`, `c913-receipts.ps1 -Repo <checkout>`), `C849_front_door_passes_every_full_case_name_to_the_invoker` (`:5039`, cwd checkout), `C912_Cold_marker_has_distinct_validation_and_full_context_refusal` (`:8349`), `C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` (`:8439`), `C1105_Git_audit_volume_scale` (`:2819`, node cwd checkout, read-only) | own roots | none | none | none | child env | **checkout** | **`.antiphon/c849-<16 hex>`** (all but C1105 volume-scale) | n/a | none | 120 s | **KEEP-AT-1 (method level)**, D-5. C1105 volume-scale is read-only and may move in S4. |
| RemoteScriptContractTests text-only guards (no spawn) | none | none | none | none | none | none | none | none | none | none | **No limiter** (S4, W-4 decides the list) |
| RunCheckpointScriptTests (26 cases; `scripts/test-run-checkpoint.ps1` does `Push-Location $RepoRoot`, cwd checkout, output paths under `tests/Antiphon.Tests/<OutputPath>`) | `c487-<guid>` results | `127.0.0.1:1` slot URL | none | none | child `$env:` only | **checkout** | **build-output paths in the checkout** | n/a | none | ScriptHarness 300 s | **KEEP-AT-1**: on the `StrictClasses` floor (`ProcessSpawnLimitTests.cs:83`) and the CARD-1747 shared-checkout rule |

No class needs per-case isolation work to widen: **there is no NEEDS-ISOLATION finding.** The closest risks are named in
§7, and W-5 (a fixed-temp-name lint) keeps that true.

Windows: `LinuxShellResult` starts `wsl.exe -e bash -s`, and C1008 classes are `[NativeLinuxOnly]`. Concurrency of several WSL
shells on the desktop is **UNOBSERVED**, so the Windows default width is 1 (D-3).

## 2. Design

### Decisions

- **D-1 A new limiter type, not a change to `ProcessSpawnLimit`.** Add `tests/Antiphon.Tests/TestHelpers/ScriptModelSpawnLimit.cs`
  (`IParallelLimit`). A script-model method swaps `[ParallelLimiter<ProcessSpawnLimit>]` for `[ParallelLimiter<ScriptModelSpawnLimit>]`.
  Rejected: raising `ProcessSpawnLimit.Limit` (it guards ConPTY/FakeClaude classes; `Caps_concurrent_process_spawning_tests_at_one`
  pins 1). Rejected: a per-class `IParallelLimit` per family (several 6-wide lanes would stack to 18+ processes).
  Rejected for now: profile change #4 (several test processes), because each process takes its own build-slot lease.
- **D-2 Width.** `Resolve(processorCount, overrideValue, slotCpu, isWindows)`:
  override `ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT` (1-8, clamped) wins; else on Windows 1; else if
  `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT` parses, `Clamp(slotCpu, 1, 6)`; else `Clamp(processorCount / 4, 2, 6)`. server2 has 24 processors
  (`nproc` on this runner: 24), so the width is 6. The value is read once at type initialisation (the `CheckpointToolSpawnLimit` idiom).
  Why tie it to the slot: the row already holds a lease of `maxcpucount=6` while using about 1 core (profile §c); width 6 uses
  the CPU the lease already grants. Whether the checkpoint tool passes `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT` to a row's test host
  is **UNOBSERVED**; without it, the processor formula still gives 6. Rejected: `ProcessorCount/2` (12 bash+node chains
  per row beside peer rows risk the 120 s per-run deadline; CARD-1130 measured more than 30 s at load 19-23).
- **D-3 Windows stays 1** until a measured WSL sample exists. The lane is still separate from the strict lane, so on Windows a
  script-model case may run beside a ConPTY case; that pairing is **UNOBSERVED**. Override available for a measured trial.
- **D-4 One lane for all script-model families**, so CPU use is bounded by one width per test process whatever the row mixes.
- **D-5 Classify by effect, keep the existing rule.** A method whose child runs with the shared checkout as its working
  directory **and writes there** (a production script that writes `.antiphon/` evidence in the checkout) stays on
  `ProcessSpawnLimit` at method level (G-11 list). A checkout-cwd child that only reads, or works on absolute fixture paths
  (`C994ScriptProcess` running `git -C <fixture repo>` or `chmod <fixture path>`), joins the wide lane. These children write unique names, so this is caution, not a measured collision; the strict lane
  runs beside the wide lane, so the cost is only that lane's own sum (RemoteScript C849/C912/C913: under 4 minutes, profile families).
  Widening them is decision O-2.
- **D-6 Text-only guards lose the limiter only through a mechanical rule** (W-4): a `[Test]` method whose compiled body
  (its async state machine `MoveNext`, closures, local functions and nested compiler types), followed transitively through
  methods declared in `Antiphon.Tests` and `Antiphon.TestSupport`, never references `System.Diagnostics.Process.Start`, a
  `ProcessStartInfo` constructor, or `ScriptHarnessProcess`. This reuses `ProcessSpawnLimitTests.ReferencedMembers`/`HelperReferences`
  (`:858-905`). The rule decides the list; this plan does not hand-list them (G-12). Their whole serial cost is about 2 minutes,
  so this is cleanup, not saving (O-4).
- **D-7 Assembly scope and Pty.Tests.** The limiter is assembly-local like the other two. Never co-schedule `Antiphon.Tests` with
  `Antiphon.Agents.Pty.Tests` (unchanged). Checkpoint rows that run widened classes are `Serial: true`, so no other row or build
  shares the host CPU with a 6-wide row. The build-slot lease is unchanged: one lease per row.
- **D-8 Mutation checks are waived** (operator 2026-11-12). The witnesses below are red-first by construction instead.
- **D-9 Docs.** Gotcha #18 in `docs/testing-and-build.md` and the `ProcessSpawnLimit` summary name the third lane and its rule.
- **D-10 Merge restoration is sharded (R1, B-1).** `scripts/impacted-classes.ps1` accepts two new optional kind properties.
  `shards` maps a shard slug to a list of `Class/MethodPrefix` entries. Each shard emits its own method row:
  `<Class>-<slug>` in the plain output, or `CP-merge-subset-<Class>-<slug>` under `-MergeSubset`. The filter is
  `New-MethodFilter` over that shard's prefixes. `nightly` lists `Class/MethodPrefix` entries that are deliberately outside
  every shard and printed as `nightlyOnly`. When any kind selects a sharded class whole, the selection becomes that class's shard
  rows in `New-FilterRows`. It never becomes a `(Class*)` operand in a class row, so no `--timeout 570s` run and no
  `CP-merge-subset-tests` row ever holds the whole class. The kind's existing `methods` list (the five exact guards) stays,
  so the `CP-merge-subset-RemoteScriptContractTests` row and the pins on it are unchanged. Those five run again inside their
  shards, which costs seconds.
  Rejected: (i) `"classes": ["RemoteScriptContractTests"]`, because it fails B-1: the class joins `CP-merge-subset-tests` and
  exceeds 570 s. (ii) A shard list only in the Merge brief, because the Merge executes the selector's `filters`
  (docs/orchestration-loop.md Merge process), so a hand list drifts. (iii) One kind per shard, because kinds that select the
  same class merge back into one method row (`$MethodsByClass` is keyed by class), and new kind ids change the kind-id pins
  in `ImpactedClassesSelectorTests.cs:35,:254`, a file CARD-1820 edits. (iv) A larger `--timeout`, because the 570 s cap is
  the brief's, not this card's. (v) A computed "rest" shard, because the selector parses no method names. The rest prefixes
  are listed, and W-8 fails when a new method family is not in any shard.

### Rule for joining the lane (Gotcha #18 text, S1)

A method joins `ScriptModelSpawnLimit` only when all its children are bash, node, pwsh, jq, git or coreutils running on
paths under a root the fixture created for that case, with no port bound, no test-process environment or directory change,
and no child with the shared checkout as its working directory that writes there. Classify by what is spawned, never by name.

## 3. Witnesses (red first)

New class `tests/Antiphon.Tests/Scripts/ScriptModelSpawnLaneTests.cs` (`[Category("Unit")]`). S1a commits it with
`ScriptModelSpawnLimit.Resolve` returning 1 and the families still on the strict lane, so every row below is red; S1b makes it green.

| W | Test | Red at S1a because | Green at S1b because |
|---|---|---|---|
| W-1 | `Lane_width_is_six_on_a_24_core_linux_host` plus `[Arguments]` rows for 4/8/16/64 processors, slot values 2/6/12, override "1"/"0"/"9"/"x", Windows | stub returns 1 | `Resolve` implements D-2 |
| W-2 | `Probe_A_overlaps_probe_B` / `Probe_B_overlaps_probe_A`: two `[ParallelLimiter<ScriptModelSpawnLimit>]` cases rendezvous on a static `TaskCompletionSource` pair; each waits up to 120 s for the other; the second to arrive fails fast if the first already gave up | width 1: B cannot start until A ends, A times out | width ≥ 2: both run, overlap under 1 s alone |
| W-3 | `Script_model_families_use_the_wide_lane`: reflection over a family floor (S1: HostJq; S2: Rolling trio; S3: VolumeRetirement four, RetiredTemp two, C1364; S4: RemoteScript). Every `[Test]` method carries `ParallelLimiter<ScriptModelSpawnLimit>`, or `ParallelLimiter<ProcessSpawnLimit>` and is on the D-5 keep list, or carries none and passes W-4. No method carries two. | families still strict | swapped |
| W-4 | `Unlimited_script_model_methods_reach_no_process_start` (IL walk, D-6) plus negative control `..._rejects_a_method_that_reaches_process_start` on a fixture type | S4a: the method list is not yet unlimited (the control is red with the walker stubbed) | walker implemented, list applied |
| W-5 | `Wide_lane_sources_name_no_fixed_temp_path` plus controls `Fixed_temp_literal_is_a_finding` (`[Arguments]` rows below). Scan set: the widened class files and `scripts/fixtures/c1008-*`, `c1087-*`, `c1105-*`, `c1192-*`. Rule R1 is below the table. | S1a stub scanner returns no findings, so every control row is red | rule R1 implemented: 0 findings on the scan set, every control a finding |
| W-6 | `ProcessSpawnLimitTests`: add `ScriptModelSpawnLimit` to `No_class_carries_both_limiters` and a floor `ScriptModelClasses`; keep `RunCheckpointScriptTests` on `StrictClasses` | — (extension) | — |
| W-7 | New class `tests/Antiphon.Tests/Scripts/ImpactedClassesShardRowTests.cs` (`[Category("Unit")]`, scratch `-Table` JSON, the `MergeHighRadiusSubsetTests` runner idiom): `Sharded_kind_emits_one_row_per_shard` (shards `a`/`b` of `XTests` give rows `CP-merge-subset-XTests-a` and `-b` with `/*/*/XTests/(A_*)` and `(B_*)`, and plain ids `XTests-a`/`-b`); `Whole_selection_of_a_sharded_class_becomes_its_shards` (a second kind with `"classes": ["XTests"]` gives the shard rows and no `(XTests*)` operand in any class row, under `-MergeSubset` and without it); `Nightly_prefixes_are_reported_not_run` (`nightlyOnly` lists the prefix and no row filter names it) | S5a: `shards`/`nightly` are unknown kind properties, so the selector refuses the table (exit 1) | D-10 implemented |
| W-8 | `Remote_script_shards_partition_the_class` (same class): it reads `tests/impacted-classes.json` `remote-scripts` and reflects every `[Test]` method of `RemoteScriptContractTests`. It asserts that each method matches exactly one prefix in the `shards` ∪ `nightly` set, and that no prefix matches nothing. | S5a: the row has no `shards`, so 225+ methods are unmatched | S5b table edit |
| W-9 | `Remote_script_shards_never_join_the_project_row` (same class): `-MergeSubset -Range` over the existing `MergeHighRadiusSubsetTests.RemoteScriptsRange` asserts `CP-merge-subset-tests` names no `RemoteScriptContractTests` and that the four `CP-merge-subset-RemoteScriptContractTests-<slug>` rows exist with the §5 S5 filters byte for byte | S5a: no shard rows | S5b |

**W-5 rule R1 (amended for review B-2).** A *finding* is an absolute literal `/tmp/<name>` or `/dev/shm/<name>`. The
literal must not follow an identifier character, `.` or `/`, so `../tmp/real.db` (`VolumeRetirementContentTests.cs:416`) is
not one. A literal on a comment line (`//`, `#`) is not one either. A finding is allowed when any of these holds:

- **A1 unique suffix:** the literal contains `XXXX`, `$$`, `$RANDOM` or `$BASHPID`, or a `Guid.NewGuid()` or an interpolated
  `{…Guid…}` follows it in the same statement (`VolumeRetirementReceiptTests.cs:77`).
- **A2 creation argument:** the literal is inside the argument list of `mkdtemp(` or `mkdtempSync(`, including a ternary
  (`c1008-fake-docker.sh:138` `/tmp/c1008-pty-`, `:296` `/dev/shm/c1008-preview-`, `c1008-recycle-real-cases.mjs:40`,
  `c1105-git-audit.cjs:5`), or it is the operand of `mktemp -d [-p dir]`.
- **A3 same-family stem:** the literal starts with a stem that A2 created in the same file (`c1105-git-audit.cjs:892`
  `startsWith('/tmp/c1105-audit-')`, and the `c1192-workspace-contract.sh:306` cleanup glob `/tmp/c1192-parent.*` beside its
  `mktemp -d /tmp/c1192-parent.XXXXXX`).
- **A4 comparison:** the innermost open call before the literal is a string comparison (`ShouldContain`, `ShouldNotContain`,
  `ShouldStartWith`, `Contains`, `StartsWith`, `startsWith`, `includes`, `IndexOf`), or the line is a shell `case` arm
  pattern. Such a literal names text, not a path the case creates: `RemoteScriptContractTests.cs:287,:968`,
  `VolumeRetirementContentTests.cs:1058-1059`, `c1192-workspace-contract.sh:333`.
- **A5 exact tokens:** the literal is one of these, or one of these followed by `/`:
  - `/tmp/antiphon-pty-hosts`: the in-container `SessionRunner__PtyHostDir` token, substituted or used inside the
    simulated root (`c1008-fake-docker.sh:155,:556`, `c1008-recycle-real-cases.mjs:515,:526`,
    `c1105-compose-v218-config.json`).
  - `/tmp/donor` and `/tmp/escape`: refused arguments, never created.
  - `/tmp/c994-command-sentinel`: an injection payload the script must refuse (`RetiredTempContainerScriptTests.cs:245,:318`).

Control rows: each must be a finding.
- `Directory.CreateDirectory("/tmp/foo")`
- `mkdir -p /tmp/foo`
- `"/tmp/antiphon-pty-hosts-x"`: an allowlisted token extended, so A5 is not a prefix rule.
- `"/tmp/c1105-audit-x"` in a file with no `mkdtemp`: A3 is per file.
- `File.WriteAllText("/tmp/foo", "")` after a closed `ShouldContain(...)`: A4 looks only at the innermost open call.

Scratch evidence: a node prototype of A1-A5 in the session scratchpad (not committed) gave **0 findings** over the scan set at
`62cfb330`, and all five controls were findings. Before A2's ternary case and A4's mid-string case were added, it
flagged exactly the lines cited above. Code implements the rule in C#, and TestDesign pins the control rows.

There is no per-finding collision witness because §1 has no NEEDS-ISOLATION finding; W-5 is the standing guard that a
fixed name cannot be added silently. The concurrency proof for real cases is CP-8's measured overlap (§4). W-7..W-9 are the
S5 witnesses for B-1. S5a commits them red before the selector change and the table edit.

## 4. Checkpoints

Lane: Linux, server2 (`GET /api/runner-defaults`: `globalRunnerId=server2`; `GET /api/session-runners`: server2 linux, 10 seats).
No `-Runner`/`-Platform`. Export `ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet command. One run per After group.
Run the stress rows in pairs (`--rows CP-11,CP-12`, `--rows CP-13,CP-14`, `--rows CP-15`) so no single run exceeds 30 minutes (R1).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1984-s1/ | lane-witness | `/*/*/ScriptModelSpawnLaneTests/*` | W-1, W-2, W-3 (HostJq floor), W-5 | every case, 0 failed/skipped | 14 | 15 | true |
| CP-2 | S1 | CP-1 | spawn-rosters | `/*/*/ProcessSpawnLimitTests/*` | W-6, spawn-limit-rosters impacted row | 0 failed | 40 | 3 | true |
| CP-3 | S1 | CP-1 | hostjq-wide | `/*/*/HostJqPrerequisiteScriptTests/*` | S1 family at width 6; record TRX wall and overlap | 40 cases, 0 failed | 40 | 6 | true |
| CP-4 | S1 | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | G-13 regenerated index | 0 failed | 1 | 3 | true |
| CP-5 | S2 | tests/Antiphon.Tests -> bin-c1984-s2/ | rolling-wide | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)/*` | S2 families; wall and overlap | 0 failed | 75 | 15 | true |
| CP-6 | S2 | CP-5 | s2-guards | `/*/*/(ScriptModelSpawnLaneTests*)\|(ProcessSpawnLimitTests*)/*` | W-3 S2 floor, W-6 | 0 failed | 55 | 3 | true |
| CP-7 | S3 | tests/Antiphon.Tests -> bin-c1984-s3/ | retirement-wide | `/*/*/(VolumeRetirementApplyGateTests*)\|(VolumeRetirementContentTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementWrapperTests*)\|(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)\|(C1364BoundLandWaitProofTests*)/*` | S3 families; wall and overlap | 0 failed | 100 | 15 | true |
| CP-8 | S4 | tests/Antiphon.Tests -> bin-c1984-s4/ | remote-script-whole | `/*/*/RemoteScriptContractTests/*` | whole class at width 6; wall recorded for the nightly Unit lane (target under 15 min; not a Merge bar, see S5); TRX overlap (sum of case durations ÷ span) at least 3 | 0 failed, 0 skipped on Linux | 500 | 15 | true |
| CP-9 | S4 | CP-8 | s4-guards | `/*/*/(ScriptModelSpawnLaneTests*)\|(ProcessSpawnLimitTests*)/*` | W-3 S4 floor, W-4, W-6 | 0 failed | 55 | 3 | true |
| CP-10 | S4 | CP-8 | linux-gate-roster | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | native-linux-gate-roster impacted row (RemoteScript and Rolling files edited) | 0 failed | 1 | 2 | true |
| CP-11 | S4 | CP-8 | stress-1 | `/*/*/RemoteScriptContractTests/*` | stress run 1 of 5 | 0 failed | 500 | 15 | true |
| CP-12 | S4 | CP-8 | stress-2 | `/*/*/RemoteScriptContractTests/*` | stress run 2 of 5 | 0 failed | 500 | 15 | true |
| CP-13 | S4 | CP-8 | stress-3 | `/*/*/RemoteScriptContractTests/*` | stress run 3 of 5 | 0 failed | 500 | 15 | true |
| CP-14 | S4 | CP-8 | stress-4 | `/*/*/RemoteScriptContractTests/*` | stress run 4 of 5 | 0 failed | 500 | 15 | true |
| CP-15 | S4 | CP-8 | stress-5 | `/*/*/RemoteScriptContractTests/*` | stress run 5 of 5 | 0 failed | 500 | 15 | true |
| CP-16 | S5 | tests/Antiphon.Tests -> bin-c1984-s5/ | merge-restore | `/*/*/(ImpactedClassesSelectorTests*)\|(ImpactedClassesShardRowTests*)\|(MergeHighRadiusSubsetTests*)/*` | W-7, W-8, W-9; D-10 selector change; existing selector and Merge-subset pins unchanged | 0 failed | 8 | 15 | true |
| CP-17 | S5 | CP-16 | shard-c1105 | `/*/*/RemoteScriptContractTests/(C1105_*)` | shard RS-2 as one Merge run; run-phase wall at most 285 s | 0 failed, 0 skipped | 210 | 5 | true |
| CP-18 | S5 | CP-16 | shard-workspace | `/*/*/RemoteScriptContractTests/(C1192_*)\|(C912_*)\|(C913_*)\|(C983_*)` | shard RS-3; run-phase wall at most 285 s | 0 failed, 0 skipped | 50 | 5 | true |
| CP-19 | S5 | CP-16 | shard-rest | `/*/*/RemoteScriptContractTests/(C0995_*)\|(C1066_*)\|(C1067_*)\|(C1087_*)\|(C1168_*)\|(C1189_*)\|(C1191_*)\|(C1193_*)\|(C1194_*)\|(C1195_*)\|(C1197_*)\|(C1207_*)\|(C1208_*)\|(C1316_*)\|(C1331_*)\|(C1333_*)\|(C1335_*)\|(C1338_*)\|(C1361_*)\|(C1374_*)\|(C1514_*)\|(C1697_*)\|(C849_*)\|(C905_*)\|(C944_*)\|(C946_*)\|(C951_*)\|(C957_*)\|(C973_*)\|(C976_*)\|(C980_*)\|(C997_*)\|(Child_*)\|(Custody_*)\|(Deploy_*)\|(Every_*)\|(Host_*)\|(Lane_*)\|(Linux_*)\|(Nested_*)\|(Persistent_*)\|(Retirement_*)\|(Scrub_*)\|(Smoke_*)\|(Temp_*)\|(Testing_*)` | shard RS-4; run-phase wall at most 285 s | 0 failed, 0 skipped | 220 | 5 | true |
| CP-20 | S5 | CP-16 | shard-c1008-measure | `/*/*/RemoteScriptContractTests/(C1008_*)` | RS-1 (nightly) measured for O-7; expected over 285 s, not admitted unless O-7 says so | 0 failed, 0 skipped | 15 | 8 | true |

Min values are floors for the Code stage to confirm from the fresh TRX (W-1 has 13 argument rows plus the probe pair; CP-2 has
33 `[Test]` methods and 28 `[Arguments]` rows; CP-5 is 80 + 12 + 1 profile cases; CP-7 is 135 profile cases (ApplyGate 13, Content 41,
Receipt 27, Wrapper 7, RetiredTempHost 22, RetiredTempScript 16, C1364 9); CP-8 is 500 against 511-516 observed). Suffix check for every OR operand: each named class is the only class in `tests/` whose name
starts with that text (`grep -rhoE "class <Name>[A-Za-z0-9_]*"`, run 2026-11-17), except `ScriptModelSpawnLaneTests` and `ImpactedClassesShardRowTests`,
which do not exist yet (no class in `tests/` starts with either name at `62cfb330`). The shard rows CP-17..CP-20 use method-prefix
operands. Each operand ends in `_*`, so `(C1192_*)` cannot match `C11920…` and `(Host_*)` cannot match `HostJq…`. W-8 proves the
four shard sets are disjoint and complete. Their Min floors are 210 / 50 / 220 / 15 against 218 / 53 / ≈230 / 15 profile cases.
The filters are byte for byte the selector's `New-MethodFilter` output (ordinal sort), and W-9 pins that equality. `ProcessSpawnLimitTests` also exists in SessionRunner.Tests and Pty.Tests; the build is `Antiphon.Tests` only.

Import (read-only, 2026-11-17, plan at `aab198a012ea63bb50c13463fba9ad895dc2172f`): `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <this file> --out .antiphon/c1984-plan-import.yaml` under a build slot with `ANTIPHON_LINUX_COMPILER_POLICY=0`: exit 0, **16 rows, 0 import warnings** (only unrelated compiler warnings). Row timeouts: 45 min for estimate 15, 15 min for the short rows; every row `serial: true`.
Re-import R1 (2026-11-17, task `8344871d`, the same read-only command with `--no-build` under build-slot lease `8c2bc085`, `--out .antiphon/c1984-plan-import-r1.yaml`, gitignored): exit 0, **20 rows, 0 import warnings**. CP-17..CP-19 derive 15 min (estimate 5) and CP-20 24 min (estimate 8). The CP-19 46-operand filter imported unchanged.

### Cost

Ordinary floor (sum of EstimatedMinutes): S1 27, S2 18, S3 15, S4 95 (20 without the stress rows), S5 38 (CP-16 15, CP-17..CP-19
5 each, CP-20 8) plus the 15-minute second shard sample. Total 208, of which 75 is the operator-optional stress step (O-3).
Merge cost after S5, only for a batch that matches `remote-scripts`: three shard rows of about 2-5 minutes each, about 8-10 minutes
beside the fixed subset, against nothing today (the class is waived).

## 5. Slices

| Slice | Files | Tests | Notes |
|---|---|---|---|
| S1a (red) | new `tests/Antiphon.Tests/TestHelpers/ScriptModelSpawnLimit.cs` (stub width 1), new `tests/Antiphon.Tests/Scripts/ScriptModelSpawnLaneTests.cs` (W-1, W-2, W-3 HostJq floor, W-5 stub) | CP-1 red (W-1, W-2, W-3, W-5 control) | commit the red run before S1b |
| S1b | `ScriptModelSpawnLimit.cs` (D-2), `HostJqPrerequisiteScriptTests.cs` (40 attribute swaps), `ProcessSpawnLimitTests.cs` (W-6), `docs/testing-and-build.md` Gotcha #18, `tests/derived-impact-index.json` (regenerated last) | CP-1..CP-4 | lowest risk: every child runs in its own root with its own cwd and TMPDIR. Saving: 506 s → max(144.5 s, 506/6 = 84 s) ≈ 2.5 min (≈ 6 min saved). |
| S2 | `RollingProductionMountTests.cs`, `RollingVolumeRecycleCensusChurnTests.cs`, `RollingVolumeRecycleScriptTests.cs` (C1008 fixture methods only; C727 wrapper and host-proof methods keep the strict lane, D-5), W-3 floor, index | CP-5, CP-6 | one fixture family (C1008HostFixture) |
| S3 | `VolumeRetirementApplyGateTests.cs`, `VolumeRetirementContentTests.cs`, `VolumeRetirementReceiptTests.cs`, `VolumeRetirementWrapperTests.cs`, `RetiredTempContainerHostTests.cs`, `RetiredTempContainerScriptTests.cs`, `C1364BoundLandWaitProofTests.cs` (per-method: pwsh drivers from the checkout stay strict), W-3 floor, index | CP-7 | re-check every `WorkingDirectory = DelegateScriptRunner.RepoRoot` child in these files for writes (§1 marks the per-method R column UNOBSERVED for RetiredTemp/C1364) |
| S4a (red) | W-4 walker stub + negative control | CP-9 red on the control | |
| S4b | `RemoteScriptContractTests.cs` (all spawning methods except the D-5 keep list), text-only methods drop the limiter per W-4, W-3 floor, index | CP-8..CP-15 | highest risk (most families); CP-8 records the wall |
| S5a (red) | new `tests/Antiphon.Tests/Scripts/ImpactedClassesShardRowTests.cs` (W-7, W-8, W-9) | CP-16 red (selector refuses `shards`; partition and shard rows absent) | commit the red run before S5b |
| S5b (S-last) | `scripts/impacted-classes.ps1` (D-10: `shards`/`nightly` in `$allowedKind`, parse and validate as `Class/Prefix`, `New-FilterRows` shard rows and whole-to-shards expansion, `nightlyOnly` in both payloads); `tests/impacted-classes.json` `remote-scripts` (keep `methods`, add `shards` RS-2..RS-4 and `nightly` RS-1 below); `docs/testing-and-build.md` and `docs/orchestration-loop.md` (exact text below); `tests/derived-impact-index.json` (regenerated last) | CP-16..CP-20; the Merge-shape run of CP-17..CP-19 twice (second run `--rows CP-17,CP-18,CP-19`, reason: a second sample for the bar) | only after CP-8 and the stress rows are green; each shard admitted only under the bar below |

#### S5 shards (method-prefix families of `RemoteScriptContractTests`)

Sums and cases come from the profile's family table and slowest-case list. The expected wall at width 6 is
max(longest case, sum ÷ 6), with the shard's strict-lane methods (D-5) running beside it. Method counts are from a scratch
census of `[Test]` names; it found 230 of the 231 `[Test]` attributes by name, and W-8 decides membership.

| Shard | Row id under `-MergeSubset` | Prefixes | Cases | Observed serial sum | Longest case | Strict lane beside it | Expected wall | Bar (285 s) |
|---|---|---|---:|---:|---:|---|---:|---|
| RS-1 `nightly` | none; `nightlyOnly` | `C1008_` (15 methods) | 15 | ≈1390 s for 11 of 15; 3 UNOBSERVED (incl. `refuses_references_and_unknown_census`) | 315.0 s (`C1008_Recycle_receipt_records_disk_and_partial_failure`) | none | ≥ 315 s; ≈ 330-400 s with the unobserved cases | **fails**: stays nightly (O-7) |
| RS-2 `c1105` | `CP-merge-subset-RemoteScriptContractTests-c1105` | `C1105_` (28 methods) | 218 | 985 s | 74.0 s (`C1105_Resume_identity_refusals`) | `C1105_Git_audit_volume_scale` 49.4 s | ≈ 165-200 s | expected pass |
| RS-3 `workspace` | `CP-merge-subset-RemoteScriptContractTests-workspace` | `C1192_`, `C912_`, `C913_`, `C983_` (48 methods) | 53 (31 + 9 + 6 + 7) | 595 s + C913 ×6 UNOBSERVED | 70.8 s (`C983_Required_jq_refuses_unavailable_probe_before_any_group` #1) | C912 two methods (family total 72 s) + `C913_Receipts_*` (UNOBSERVED) | ≈ 100-150 s | expected pass |
| RS-4 `rest` | `CP-merge-subset-RemoteScriptContractTests-rest` | the 46 prefixes in CP-19 (C0995_ … Testing_) | ≈ 230 | ≈ 294 s observed (C849 164 + others ≈ 130) + 46 UNOBSERVED cases (C1208 ×25, C1066 ×10, C1207 ×5, C1316, C1361 ×2, C1697, C1087, C1331) | 54.8 s (`C849_Deploy_prepares_and_verifies_before_acceptance`) | `C849_front_door_*` (UNOBSERVED) | ≈ 90-150 s | expected pass |

Cases: 15 + 218 + 53 + 230 = 516. The 2026-11-01 receipt (3597 s for 511) was a different run at a different load, so it
gives only a rough bound of about 236 s on the unobserved residue.

**Admission bar (replaces "under 15 minutes").** A shard joins the Merge only when both of its own rows (CP-17..CP-19, each
one `dotnet run` the way the Merge runs it) meet all of these:
- the run phase (host start, discovery and test execution, from the row report) takes at most **285 s**, half the 570 s
  `--timeout`, which is the 2x rule of `docs/test-performance-standards.md`;
- 0 failed and 0 skipped;
- the load average is recorded.

A shard over 285 s in either run moves from `shards` to `nightly` in the same S5b commit and stays with the nightly Unit
lane. Any shard row over 570 s is a stop, never a retry. CP-8 and CP-11..CP-15 measure the whole class for the nightly lane
and the stress check. They are **not** the Merge bar. No shard row joins `CP-merge-subset-tests`, whose EstimatedMinutes is 5
and derived deadline 15 minutes. D-10 makes that structural, and W-9 pins it.

**Exact doc changes in S5b.**
- `docs/testing-and-build.md`, Impacted classes. Replace "Remote deploy checks and the statement-budget checks stay
  method-scoped." with: "The statement-budget checks stay method-scoped. Remote deploy checks run as the `remote-scripts`
  shards (CARD-1984). A kind's `shards` map emits one method row per shard (`<Class>-<shard>`, or
  `CP-merge-subset-<Class>-<shard>` under `-MergeSubset`), and each row is one run. A shard is admitted only when it measures
  at most 285 s, so it fits a 570 s `--timeout` at 2x. A whole selection of a sharded class becomes its shard rows, never a
  class row. Prefixes in the kind's `nightly` list (today `RemoteScriptContractTests/C1008_`) stay with the nightly Unit lane
  and are printed as `nightlyOnly`." In the JSON-shape paragraph after "Method rows use
  `/*/*/Class/(MethodA*)|(MethodB*)`.", add: "A sharded kind adds one such method row per shard."
- `docs/orchestration-loop.md`, Merge process, the paragraph that begins "The batch reports one TUnit row per `filters`
  entry". After "a method row is `CP-merge-subset-<Class>`", add: "; a shard row is `CP-merge-subset-<Class>-<shard>`
  (CARD-1984). It is its own row with EstimatedMinutes 5, the Group, Serial and Environment of the rows below and Min 1, and
  it never joins `CP-merge-subset-tests`". At the paragraph's end, add: "`RemoteScriptContractTests` is no longer waived as a
  whole. A batch that matches `remote-scripts` runs the shard rows, and only the selector's `nightlyOnly` prefixes stay with
  the nightly Unit lane."
- **Merge brief template** (orchestrator-owned, outside this repo). Replace the whole-class waiver line with: "Run
  RemoteScriptContractTests as the selector's `remote-scripts` shard rows, one `dotnet run --timeout 570s` per row (or one
  CHECKPOINT row each). The `nightlyOnly` prefixes (`C1008_`) stay waived to the nightly Unit lane." Never put the class in
  one run.

Where the Merge waiver is recorded today: **not in `docs/orchestration-loop.md`, `tests/impacted-classes.json` or any `tests/*.json`
list.** It lives in Merge briefs and in `docs/superpowers/plans/2026-11-17-card-1960-checkpoints.md:52` ("The whole class stays with the
nightly Unit lane"). The `remote-scripts` impacted row names 5 methods only (`tests/impacted-classes.json:107-119`), and
`docs/testing-and-build.md` says "Remote deploy checks and the statement-budget checks stay method-scoped". S5b adds the shards to
the row (keeping its five `methods`), replaces that sentence, and adds the shard-row and no-longer-waived sentences to the Merge process. The
orchestrator then replaces the waiver line in its Merge brief template with the shard text above (outside this repo, O-5). The CARD-1960
plan line stays as history.

### Existing tests affected

| Area | Affected |
|---|---|
| Migrations, public DTOs/routes, operation labels | none |
| Remote script functions | none: `scripts/c590-remote.sh` and `scripts/fixtures/*` are not edited |
| Statement budgets | none |
| Impacted-classes table (CARD-1782) | `spawn-limit-rosters` (new `*SpawnLimit.cs`, added `ParallelLimiter<`) selects `ProcessSpawnLimitTests` (CP-2, CP-6, CP-9); `native-linux-gate-roster` (RemoteScript and Rolling file edits) selects `C1210_Roster_closure_matches_source_gates` (CP-10); S5b edits the table and `scripts/impacted-classes.ps1` (CP-16). Because the `remote-scripts` `methods` list stays, the pins on it stay green unedited: `MergeHighRadiusSubsetTests.AssertImpactedMethods` (`:410-418`, the `CP-merge-subset-RemoteScriptContractTests` row) and the five method-name needles in `ImpactedClassesSelectorTests.cs:252-260`. If CP-16 shows that a pin needs an edit in `ImpactedClassesSelectorTests.cs`, S5b waits for CARD-1820 S1R6 to land and edits it on top. That is UNOBSERVED until CP-16 runs. |
| Committed generated data | `tests/derived-impact-index.json` regenerated in every slice (CP-4; the S2-S4 guard rows need the same check, Code adds `committed_index_matches_the_builder` to their filter if the index changed) |

## 6. Collisions with in-flight work

| Work | Branch tip read | Files it changes | Overlap with this plan |
|---|---|---|---|
| CARD-1893 S0 `d2eef712` | `6d291e1451a6` | `docker-compose.server2-runner.yml`, its plans, `C1893S0RunnerHoldTests.cs`, **`tests/derived-impact-index.json`** | index only; its S0 checkpoint rows name RemoteScript classes but no source |
| CARD-1883 `0729df64` (and Review branches) | `f44a5775ce5a` | `ReleaseImpactProjectGraph.cs`, `ReleaseImpactProjectTests.cs`, its plan | none |
| CARD-1977 | no branch found | — | none |
| CARD-1820 S1R6 `8a7544d8` | `822d7dd68dc9` | `scripts/lib/release-impact-scripts.ps1`, `ImpactedClassesSelectorTests.cs`, `ReleaseImpactScriptTests.cs`, **`tests/derived-impact-index.json`** | index; S5's CP-16 runs `ImpactedClassesSelectorTests` after it lands. S5b edits `scripts/impacted-classes.ps1`, which neither CARD-1820 (`822d7dd68`) nor CARD-1893 S0 (`6d291e145`) changes (`git diff --stat` from `62cfb330`, R1). `MergeHighRadiusSubsetTests.cs` is likewise untouched by both. |
| CARD-1983 | no branch found (Backlog) | — | it will change which rows a compose diff selects; S5 must be re-resolved after it lands |

Must NOT touch: `scripts/c590-remote.sh`, `scripts/fixtures/*`, `scripts/lib/release-impact-scripts.ps1`, `ReleaseImpact*`,
`ImpactedClassesSelectorTests.cs` (unless CP-16 forces it after CARD-1820 lands, §5 Existing tests affected), `Apply-Derived` and the derived selection in
`scripts/impacted-classes.ps1` (D-10 changes only kind parsing and `New-FilterRows`), `docker-compose.server2-runner.yml`, `C1893S0RunnerHoldTests.cs`, `ProcessSpawnLimit.Limit`.
Never hand-merge `tests/derived-impact-index.json`: regenerate it at the slice tip (`scripts/regenerate-derived-impact-index.ps1`).

## 7. Expected saving and risks

| Row (profile) | Serial now | Width 6: max(longest case, sum ÷ 6) |
|---|---:|---:|
| RemoteScript whole class (3361 s observed of 516; 59m57s full receipt) | 56-60 min | max(315 s, 3600/6 = 600 s) ≈ **10-11 min** plus the strict-lane C849/C912/C913 sum running beside it. This is the nightly and stress shape; it is over a 570 s `--timeout`, so the Merge never runs it (B-1) |
| Merge shards RS-2 / RS-3 / RS-4 (S5) | waived today | ≈ 165-200 s / 100-150 s / 90-150 s, each one run, bar 285 s |
| Merge shard RS-1 `C1008_` | waived today | ≥ 315 s (one case); stays nightly (O-7) |
| Profile row CP-6 `C11*` (1388 s) | 23 min | max(74 s, 231 s) ≈ **4 min** |
| Profile row CP-8 recycle group (≥ 2689 s) | ≥ 45 min (deadline) | ≈ 8 min (the C727 strict-lane cases run beside it; their sum is UNOBSERVED) |
| HostJq (506 s) | 8.4 min | 2.5 min |
| S0 17-row plan (≈130 min serial test time) | 130+ min | about 30-35 min with the build; target **under 40 min** |

Risks: (a) hidden shared state not found by this census. Mitigation: W-5 lint, per-method review in S3, and the stress step
(CP-11..CP-15: five whole-class runs, any red stops the rollout and that method returns to the strict lane with a card).
(b) The 120 s per-run deadline under CPU load. Six chains use about six of 24 cores; rows are `Serial: true`. A
deadline hit shows as `C1008_FIXTURE_CHILD_LIMIT` in the row console; any one is a stop, not a retry. (c) Windows/WSL width
UNOBSERVED (D-3). (d) TUnit's own default parallelism caps all lanes together; its value is UNOBSERVED; CP-8's overlap
ratio is the measurement. (e) 65 leftover `/tmp/c1008-host-*` roots from deadline-killed rows (profile) are not affected by width.

## Performance budget

- Selection: per row above; `Antiphon.Tests` only; no downstream projects (test-only change). Basis: profile `f588b939`
  (server2, Linux, 24 processors, load 1.05-13 across runs 194416/214544, TRX sums), and the 2026-11-01 full receipt
  (511 cases, 59m57s). p95 per case: RemoteScript 37.5 s, Rolling 140.2 s, HostJq 85.3 s, ApplyGate 76.3 s.
- Build: one cold `Antiphon.Tests` build per After group, about 10 minutes (607.7 s at load 14.85, CARD-1893 S1/S2 test design);
  slot wait reported separately.
- Phases: fixture setup 1-2 s per class (profile; `TestTiming` `fixture-setup`); test body is the bash/node/pwsh work; teardown
  is `Directory.Delete` of the root. No AppHost, database, container or migration. Shared setup: `C1008ComposeSeed` once per
  session (unchanged). No new shared setup.
- Recording: TRX per row; `TestTiming` phase journal (`rows/<CP>/phase/*/cases-*.jsonl`) gives per-case start/end and the
  overlap ratio. `LinuxShellResult` spawns stay uninstrumented (profile); not changed here.
- Timeouts: no test or fixture timeout changes. Existing: 120 s per run (CARD-1130, 2x a 58.7 s loaded sample), 30 s
  `C994ScriptProcess`, class `[Timeout(600_000)]` on RemoteScript (the slowest case is 315 s: about 2x). Probe W-2: 120 s,
  provisional, chosen so a peer can take a slot while the longest case (p95 37.5 s, max 315 s) holds others; its own body is
  under 1 s. Row deadlines: `max(15, 3 × EstimatedMinutes)` = 45 min for 15-minute rows; at width 6 the expected row walls
  are 3-11 minutes, so 45 min is about 4x for CP-8 (no explicit pin; the importer rule).
- Merge `--timeout 570s` (R1, B-1): a global execution timeout per `dotnet run`, not per test, so the class `[Timeout(600_000)]`
  does not raise it. Each S5 shard row is one such run and is admitted at most 285 s measured (2x). RS-1 `C1008_` cannot
  meet that bar while one case takes 315 s; profile change #3 (split the scenario matrices) is the fix, outside this card (O-7).
- Over 30 minutes: no single run in this table is expected over 30 minutes; the stress rows run in pairs. The current
  over-30-minute rows are the Debug evidence in profile `95bdabde` itself.

## 8. Operator decisions

| # | Question | Recommendation |
|---|---|---|
| O-1 | Width 6 on Linux (`Clamp(slot, 1, 6)` / `Clamp(cores/4, 2, 6)`)? | **Yes.** It matches the 6-CPU lease the row already holds. |
| O-2 | Widen the shared-checkout children too (C727 wrapper, C849/C912/C913 front doors, retirement pwsh drivers)? | **No, not now.** Their writes are unique, but the existing rule keeps them strict and they run beside the wide lane. Revisit with a measured strict-lane sum after S4. |
| O-3 | Run the 5x stress step (75 minutes of host time)? | **Yes**, once, in S4 before S5. |
| O-4 | Remove the limiter from text-only guards (W-4)? | **Yes, but last** (S4b): about 2 minutes of serial cost, mostly cleanup. |
| O-5 | Restore RemoteScriptContractTests to Merge verification (S5) **as shards** RS-2..RS-4? Each is its own row and run, admitted only at most 285 s in both shard samples, after CP-8 and the stress rows are green, and never in `CP-merge-subset-tests`. | **Yes.** At the same time, replace the Merge brief waiver line with the shard text in §5 S5. |
| O-6 | Keep Windows at width 1 until a WSL sample exists? | **Yes.** |
| O-7 | Keep `C1008_*` (RS-1) on the nightly Unit lane, waived from the Merge, until its scenario matrices are split (profile change #3; file a card)? The alternative is to admit RS-1 under 570 s with less than 2x headroom (≈ 1.4-1.8x on CP-20). | **Yes, keep it nightly.** One 315 s case leaves no 2x room under 570 s. CP-20 records the measured wall for that card. |

## Verification design

Not folded into this dispatch. TestDesign refines W-1..W-6 (exact argument rows and Min counts) and confirms the Min floors.

