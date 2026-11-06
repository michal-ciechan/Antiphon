# Build-slot lease waits: what each check is and wait statistics

2026-11-06. Investigate task `18d0e3bd`; source `5abd946dd2c235b6e873b0cbb95c738f35216dcf`, assigned branch `feat/card-task-18d0e3bd`. Read-only investigation apart from this document and ignored analysis evidence. No builds, tests, desktop commands, settings changes, deployment, process termination, or product edits were performed.

## Operator summary

- A lease admits **one build or test driver**, regardless of whether its tests are architecture checks, instruction checks, or product tests. They compete for the same four server2 slots; the label does not reserve a lane or confer priority.
- The “mechanical instruction contract” in the current Merge reports is **`MechanicalPcContractTests`**, 11 cases about mutation instructions, binding, tier routing and owner-document consistency. It does not execute mutants. Its standalone checkpoint leases held slots for a median **9.5 seconds**, but waited a median **45 seconds** in the retained sample.
- `TaskPlatformGuidanceTests` has five cases checking platform/runner instructions, stage-bundle size limits, and preserved Review obligations. Its test bodies totaled **0.11–0.20 seconds** in two stored Merge runs; starting and finishing the test host costs more.
- The five-class policy sweep has 50 cases. Its costly member is `TestClassificationPolicyTests`: **81.4 and 123.4 seconds** of test-body time in two stored Merge runs, including fresh compiler/test-discovery subprocesses. The other policy classes have different jobs and costs; they are detailed below.
- In **413 distinct checkpoint leases** granted from November 5 to November 6, wait p50/p95/max were **15 / 330 / 780 seconds**. Checkpoint builds had wait p95 **450 seconds**; policy-only rows **360 seconds**; standalone mechanical-contract rows **195 seconds**. These are retained-sample measurements, not fleet-wide guarantees or timeout recommendations.
- Labelled history already exists in checkpoint logs and broker lifecycle log statements. A three-hour aggregate metrics endpoint exists in source, but the **shared server2 broker still returns HTTP 405**. The runner's separate loopback endpoint returns zero grants and is the wrong place to measure these delegates.
- Server2 was directly observed at **4/4 simultaneous leases**. Desktop source defaults are **2 slots**, but its live override and historical peak were not queried. One stored Merge had zero wait; another spent **15.5 of 44.0 serial gated minutes waiting (35.3%)**. There is no defensible single “typical Merge” percentage from these two examples, and these totals exclude authoring and Git work.

## Evidence population and limits

The observation environment is the server2 runner mirror, container hostname `dfcc116be984`, with `/work/worktrees` and `/state/runner-logs` locally readable. All analysis ran here. The requested SSH attempt to `mc@server2` first lacked a known-host entry and then failed `Permission denied (publickey)` after accepting the presented host key. Consequently **outer Docker logs, the shared broker's own Serilog files, its image identity and stack-env pin were not retrieved**. Local `docker ps` exposes nested test Docker, not the outer runner/broker containers. No desktop fallback was used.

The frozen statistical extracts are under ignored `logs/investigation-18d0e3bd/` in this worktree. They are working evidence, not a durable publication promise; this note retains the essential numbers, identities and source coordinates.

| Population | Selection and deduplication | Result |
|---|---|---|
| Checkpoint lifecycle logs | Inventory of 143 `task-*` worktree directories; 157 `.antiphon/checkpoints/**/executor.log` paths. New grants deduplicated by lease UUID; releases joined by UUID. 150 logs contained grants. | **413 grants**, 343 distinct labels, **387 observed grant/release pairs**, 26 without a matching release in this extract. Grant window **2026-11-05 10:53:28Z–2026-11-06 18:46:50Z**. |
| Committed and stored Markdown reports | At the inspected source: 473 tracked `docs/investigations/*.md` paths and 400 tracked `.antiphon/*.md` paths; also 53,427 report copies across the worktrees, reduced to 460 report basenames, preferring the owning task's worktree. Identical document content then deduplicated. | 955 distinct documents; 2,432 full numeric `CHECKPOINT ... slot=granted waited=...` receipt lines; **2,265 distinct TRX paths** after deduplicating repeated receipts. These are row admissions, not an independent count of all build leases. |
| Explicit wrapper receipts in those documents | Full UUID required in `BUILD SLOT granted lease=... waited=...`; deduplicate UUID; pair an explicit release/held line where present. | **180 UUIDs**, 120 with explicit held seconds. This overlaps the other populations and is not added to them. Labels are often absent. |
| Detailed Merge timings | Stored TRX, console logs and manifests for `03477baf` at `3737148ee1e43e3695174b7a8e9befeb6d69fb41`, and `e0a09fa9` at `df1fc145181a3bedf444f9113f822f30d3b136f6`. | Two historical Linux runs; no rerun or new qualification claim. |

Quantiles throughout are **nearest rank**: sort n observations, select `ceil(q*n)-1` at zero-based indexing. Wrapper/checkpoint wait output is integer seconds. Held seconds for checkpoint leases are elapsed UTC between grant and release; millisecond precision here is an analysis convenience, not a claim of matching broker precision. A release records the lease ending; it does **not** establish that the tests passed. Missing releases and canceled acquisitions are not assigned zero hold/wait. Small samples and mixed revisions/load do not provide a measured budget for a future run.

For reproducibility, the analysis extracts have these SHA-256 identities:

| Ignored file | SHA-256 |
|---|---|
| `inventory.json` | `cd8d04e2164629f3e55af4ebdc1af81c48209ce3ec0c3f9a752756f92dcc590a` |
| `leases.json` | `8766ab829fb4a7e7f8a2e3fa2848563e69dd2b395c8a447ee07463685ca86d23` |
| `receipts.json` | `8971c0823ab56ec6908f440ae30ab88140855eec90f45690b206e2af5a0c7f3e` |
| `stats.json` | `11b124baf010db71d2947f8946a267d08734ca69515c3beda996985a5e7045de` |
| `analyze.js` | `ffe2de4bfdf76788e13639ca39f6ea7f8cdcdcfb6f5e0a95e527ee4f8e545af2` |

`stats.json` was computed at **18:54:05Z**. `executor-events.json` is a later, line-numbered extract of relevant original log lines, SHA-256 `767dda0b16afac62763c552183c1b0f300a38b3958a95f93969aef7b0a4fdae4`; files belonging to ongoing tasks can continue growing. It is not silently treated as a second frozen census. Reconstruct by enumerating the inventory paths, parsing only full UUID grant/release lines, joining the sibling report or resolved manifest for the class filter, and applying the quantile definition above. Do not count polling refusals as leases, copied receipts as new executions, unused manifest build definitions as actual builds, or sum overlapping waits as task wall time.

## What each named check actually does

### Mechanical instruction contract

`tests/Antiphon.Tests/Application/MechanicalPcContractTests.cs:15` defines the Unit class. There are seven test methods, one with five arguments, hence **11 cases**:

| Method | Assertion and source |
|---|---|
| `C479_V05_MutationBundleFailsClosedAtLow` | Composed Mutation instructions contain binding/execution/analysis classifications, missing-binding reasons, and the prohibition on Low discovering sites or controls (`:24`). |
| `C479_V05b_CodePreparesUnboundCandidates` | Code instructions require candidate, unbound mutation material and pending Mutation status (`:38`). |
| `C479_V05c_ReviewJudgesAdequacyWithoutMutants` | Review instructions require adequacy judgment and allow PCs to remain pending (`:48`). |
| `C479_V06_OwnerDocHoldsTheContract` | `docs/mechanical-pc-contract.md` carries version, required sections, identity fields, refusal reasons, forbidden restoration commands, phases, exits, and five labelled PowerShell examples (`:56`). It checks text, not execution of those examples. |
| `C479_V06b_WorkedExamplesAreIllustrative` | Worked examples retain their illustrative qualification and named scenarios (`:105`). |
| `C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow` | Five recipe/document paths retain paragraph-scoped routing and owner links; the orchestrator source, composed instructions and README agree, including matrix/pin qualifications (`:137`, `:179`). Binding is explicitly Frontier, eligible mechanical execution Low, one open Mutation per owner. |
| `C479_R06_LowNeverDiscoversAndTierRowsStayLow` | Discovery follows higher-tier analysis, TestDesign says binding follows Code, and the Mutation tier rows remain Low rather than Frontier (`:208`). |

The separate `scripts/test-mechanical-pc-contract.ps1:2` rehearses fenced examples in disposable fixtures using canned checkpoint text. That script is **not** the 11-case `CP-92` class selection seen in the Merge report.

Why a Merge runs it is explicit commissioning, not special admission behavior in the broker. `/work/worktrees/task-03477baf/.antiphon/inbox/20ec635c-d756-4a83-9e4e-1e2da9131227.md:5` requires the final tree's policy sweep, mechanical instruction contract and `TaskPlatformGuidanceTests`, alongside the changed slice rows and guard. Its report at `.antiphon/task-03477baf.md:14` identifies CP-91 as 50 policy cases and CP-92 as 11 mechanical cases. `e0a09fa9/.antiphon/task-e0a09fa9.md:27` similarly records policy and contract groups. The **broker never chooses a test class**; it receives a label, holder identity and optional task/session IDs (`src/Antiphon.SessionRunner.Contracts/BuildSlotContracts.cs:12`).

### Platform guidance

`tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs:8` is also Unit and reads instructions/documents:

- `Stage_bundles_leave_twenty_characters_below_the_size_cap` (`:11`): stage text is at most 2,480 characters, or 2,580 for Review.
- `Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes` (`:23`): omit runner/platform pins normally, preserve explicit unpin semantics, runtime discovery routes and the orchestration owner pointer.
- `Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning` (`:54`): predecessor/card inheritance, `Any` unpinning, OS-specific scope and filter/budget language remain documented.
- `Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes` (`:71`): preserve OS witnesses, meaningful-test obligations, cleanup safeguards, complete-selection claims, landing identity and transcript-delivery evidence.
- `Review_guidance_preserves_every_instruction` (`:103`): pins the original Review instruction lines verbatim, and proves that removing the repeated-class checkpoint instruction in memory makes its assertion fail. These tests guard the instruction text; they do not perform the described dispatch, delivery or landing operations.

### Policy sweep

| Class | What it guards | Source |
|---|---|---|
| `TestClassificationPolicyTests` — 39 cases | Bidirectional Slow registry/compiled-category agreement, qualified names, duplicates and CARD reasons; adjacent reason/entry layout after union merges; Unit/Integration lane consistency; class-level Slow restriction; real TUnit probe census for inherited/data/partial cases (default 8, Slow 2, discovery at least 8), explicit Manual absence; fake/live test separation; process limiter; duration-tripwire identity, inclusive five-second boundary and invalid-input refusal. | `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:13`, `:146`, `:194`, `:231`, `:269`, `:291`, `:329`, `:385`. |
| `TestClassificationGuardTests` — 1 case per linked assembly | `Registry_matches_compiled_metadata` compares the current assembly's registry and compiled metadata; E2E must not initialize `SharedApp` just to inspect metadata. | `tests/Shared/TestClassificationGuardTests.cs:11`. |
| `SlowTestRegistryMergeTests` — 5 Integration cases | Real disposable Git repos/rebases prove distinct adjacent reason/entry pairs survive the repository's union attribute, collapsed ambiguous pairs are rejected, and a source-only `.gitattributes` cannot rescue the first conflicting replay. This is real Git work, under `GitProcessSpawnLimit`. | `tests/Antiphon.Tests/Infrastructure/SlowTestRegistryMergeTests.cs:10`, `:19`, `:25`, `:29`, `:97`, `:125`. |
| `SlowTestTripwireTests` — 2 Unit cases | The checked-in duration allowlist exists with spawn-lane entries; an unlisted 6.5-second result is reported while a listed slow result and a fast result are not. The helper threshold is five seconds. | `tests/Antiphon.Tests/TestHelpers/SlowTestTripwireTests.cs:10`, `:22`; `SlowTestTripwire.cs:11`. The policy class separately tests the production PowerShell tripwire's harder identity/invalid-input cases. |
| `NativeLinuxPolicyWitnessTests` — 3 Unit cases per linked assembly | Platform exclusions require the pinned NativeLinuxOnly attribute; embedded/checkout attribute and baseline bytes agree; attribute shape and native skip behavior are fixed; marker coverage cannot expand policy exclusions. | `tests/Shared/NativeLinuxPolicyWitnessTests.cs:11`, `:18`, `:46`. |

The policy class is not just a cheap source-text scan. Six parameterized cases call `RunProbeAsync` (`:231`, `:246`, two at `:258`, two at `:281`). Each call creates a GUID-named scratch project (`:467`), builds it with `dotnet` (`:504`), then performs discovery/default/Slow executions (`:529`). Its `CountTrx` counts result elements rather than verifying their outcomes (`:547`); the probe census is not a separate successful-suite receipt. Other cases spawn PowerShell tripwire processes (`:436`). The class holds the assembly's one-wide `ProcessSpawnLimit` (`:10`). Stored timings below show the resulting cost; nested subprocesses are part of the outer driver's held time and are not six additional observed broker grants.

### Layering rows and changed rows

`tests/Antiphon.Tests/Infrastructure/C1729LayeringGuardTests.cs:13` is nine Unit cases, with a shared per-class `C1729LayeringRepository` (`:12`, `:324`). They check complete ordered path-map coverage; pinned file-to-layer assignments; grouped allowlist parsing; the real repository reference graph; synthetic forbidden Core-to-Application references; permitted/listed references without false positives from comments/strings; stale/unmapped strict failures; ambiguous-name resolution; and warning-mode behavior (`:33`, `:43`, `:66`, `:88`, `:104`, `:163`, `:242`, `:261`, `:295`).

The fixture enumerates tracked server C# with `git ls-files`, excludes migrations/build outputs, reads each source and performs syntax analysis once (`:330`, `:353`, `:367`). It is a path-based future-project boundary guard, with namespaces unchanged (`docs/project-context.md:78`). The real repository currently uses `strict: false` in the inspected allowlist: map/parse errors still fail; unlisted/stale references are printed, while synthetic strict cases prove refusal behavior (`C1729LayeringGuardTests.cs:88`, `:295`). A green warning-mode result is not proof of zero architectural violations.

“Changed test rows” are the plan's affected product selections, not another standard suite. The Phase 0 plan gives a different service selection per slice and a shared-build guard row, e.g. CP-22/23 (`docs/superpowers/plans/2026-11-05-card-1729-phase0-plan.md:660`). In the merged-tree report CP-2 has 88 cases, CP-4 56, CP-22 22, and the guard result is reused for multiple identically sourced rows (`task-03477baf/.antiphon/task-03477baf.md:16`). **One lease is per actual invocation**, so that Merge's 70-case combined guard/policy/contract invocation held one lease, not three leases inferred from three logical CP numbers.

Master comparisons have two different meanings: baseline discovery parity compares UID sets without executing tests (`phase0-plan.md:690`; `task-03477baf/.antiphon/task-03477baf.md:49`); a narrow failing-test run at an unchanged base establishes whether a failure was inherited. Both builds and both kinds of executable driver take slots. Git patch-ID comparisons, file reads and merge-tree checks themselves are not build/test drivers.

## Measured check execution costs

No checked-in, comparable per-class timing JSON for these exact eight classes was found in the inspected `tests/` tree. `tests/test-execution-policy.json` and selection-policy files are policy inputs, not these measurements. `docs/test-performance-standards.md:34` requires recording phases and keeping queue time separate; `:65` requires labelling small samples provisional. The performance-named investigation `2026-11-05-card-1718-d3-r2-performance-handoff.md` supplies no duration for these classes. One older committed measurement does name `MechanicalPcContractTests`: **11 cases, 0.626 seconds** in `docs/investigations/2026-10-23-card-1572-outage-recovery.md:540`.

The following actual stored TRX records provide newer measurements. Values are **sum of case durations in seconds**, not a serial wall-time estimate. Cases/classes can overlap; these values exclude host startup and can exclude fixture initialization.

| Class / case count | Merge `03477baf`, source `3737148e…` | Merge `e0a09fa9`, source `df1fc145…` |
|---|---:|---:|
| MechanicalPcContractTests / 11 | 0.869 | 0.564 |
| TaskPlatformGuidanceTests / 5 | 0.111 | 0.199 |
| TestClassificationPolicyTests / 39 | 123.354 | 81.360 |
| TestClassificationGuardTests / 1 | 2.963 | 0.936 |
| SlowTestRegistryMergeTests / 5 | 31.121 | 15.350 |
| SlowTestTripwireTests / 2 | 0.208 | 0.088 |
| NativeLinuxPolicyWitnessTests / 3 | 16.245 | 7.656 |
| C1729LayeringGuardTests / 9 | 0.343 **plus shared analysis below** | 0.214 **plus shared analysis below** |

Sources: `/work/worktrees/task-03477baf/.antiphon/c1729-merge/{guard-policy-contract-1,core-guidance-1}/run.trx` and matching `summary.json`; `/work/evidence/merge-e0a09fa9/{contracts,policy-A,policy-B}/*.trx`. Join each result's `testId` to its `TestDefinitions/UnitTest/TestMethod.className`; then sum parsed `duration` by class. Every named case in these two samples passed. No performance assertion about the current checkout was rerun.

The policy class's elapsed span from first case start to last case end was **123.471 / 81.398 seconds** respectively. For the parallel Git merge class the corresponding spans were only **9.572 / 5.029 seconds**, explaining why summing case durations overstates its wall contribution. The slowest policy case in the first Merge was `C487_G069(ordinary)` at **25.999 seconds**; in the second it was `C487_G069(optin)` at **9.136 seconds**. Those are individual observed samples, not p95 budgets.

The layering fixture separately reported the following unedited timings, outside the tiny test-body totals:

```text
LAYERING files=1352 census=0.041s read=0.126s analysis=10.060s total=10.226s
LAYERING files=1355 census=0.055s read=0.133s analysis=9.372s total=9.560s
```

Coordinates: first TRX `guard-policy-contract-1/run.trx:94`; second `/work/evidence/merge-e0a09fa9/policy-B/policy-B.trx:34`. The second Merge's TUnit group execution durations were **87.343 seconds** for policy-A, **18.118 seconds** for policy-B plus layering, and **5.873 seconds** for both instruction classes (`task-e0a09fa9/.antiphon/task-e0a09fa9.md:27`). Their leases held **90 / 21 / 9 seconds**, respectively (`policy-A.log:26`, `policy-B.log:26`, `contracts.log:26`). This distinguishes test time, fixture cost, host work and queue time.

## Lease wait and hold statistics

### Checkpoint lifecycle sample

All 413 grants: wait **p50 15s, p95 330s, max 780s, mean 75.33s**. The 387 paired lease intervals: held **p50 110.212s, p95 789.139s, max 2703.159s, mean 256.92s**. Every observed grant carried `maxcpucount=6`. Their scope is checkpoint logs in the specified worktrees, not direct wrappers, all host jobs or only successful tests.

Labels are actual `build:<build-id>` and `<row-id>@<build-id>` strings. The following interpretation joins those labels to sibling report/resolved-manifest filters. “Policy only” allows a subset of the five named policy classes but no unrelated class; “standalone” allows the named class's methods or whole class; mixed guards retains other combined selections. Unit includes whole and sharded Unit predicates. Missing selection metadata remains in other rows. These buckets partition the observed grants; they do not change admission policy.

| Command kind | Grants / paired holds | Wait p50 / p95 / max (s) | Held p50 / p95 / max (s) |
|---|---:|---:|---:|
| Checkpoint build | 148 / 141 | 15 / 450 / 780 | 330.086 / 690.669 / 894.559 |
| Policy-only test row | 16 / 16 | 0 / 360 / 360 | 119.675 / 155.337 / 155.337 |
| Standalone layering row | 18 / 18 | 30 / 330 / 330 | 20.495 / 40.220 / 40.220 |
| Standalone mechanical contract | 5 / 5 | 45 / 195 / 195 | 9.518 / 13.575 / 13.575 |
| Standalone platform guidance | 1 / 1 | 0 / 0 / 0 | 9.050 / 9.050 / 9.050 |
| Mixed guard/product selections | 43 / 37 | 30 / 360 / 615 | 103.277 / 436.333 / 901.171 |
| Unit predicate, whole or shard | 26 / 16 | 0 / 105 / 480 | 1681.185 / 2703.159 / 2703.159 |
| Other checkpoint row | 156 / 153 | 0 / 285 / 690 | 52.362 / 343.995 / 1738.852 |

**Held Unit intervals include deadline-stopped runs**. A 45-minute hold is not evidence of a completed successful Unit suite, nor a measured execution p95 for it. Missing releases also bias the hold sample. There is no statistically established “slowest-waiting test class”: queue state at arrival, other holders and 15-second retry cadence affect these small, differently timed cohorts. Builds have the largest observed category wait p95 here; Unit has the longest held intervals.

### Longest waits, with identity and time

All timestamps below are UTC grant times. Approximate queue-entry time is grant minus printed wait; the checkpoint run-directory timestamp is a run start, not a grant timestamp.

| Grant / lease / label | Wait / held | Original coordinate |
|---|---:|---|
| Nov 6 02:08:23, `306d62bb-a71f-43d6-91fb-2e8821eaef0b`, `build:bin-11212cb7-report` | 780s / 81.130s | `/work/worktrees/task-11212cb7/.antiphon/checkpoints/20261106-015456-f370/executor.log:68` |
| Nov 6 02:06:13, `f8dc1e7c-60f3-4b91-825e-1d1922c62007`, `CP-2@bin-c1708r3` | 690s / 326.030s | `/work/worktrees/task-bb0a6ce8/.antiphon/checkpoints/20261106-014505-8730/executor.log:82` |
| Nov 5 19:36:25, `806b5d47-80c9-462c-9de1-bcf9661249fd`, `CP-4@bin-c1713-unit` — mixed guards | 615s / 152.900s | `/work/worktrees/task-013b9ff7/.antiphon/checkpoints/20261105-191934-d314/executor.log:97` |
| Nov 5 19:36:19, `4574a5b8-4a1e-4e39-9c5c-74ecae8f1300`, `build:bin-digest-repair` | 585s / 545.078s | `/work/worktrees/task-29e42131/.antiphon/checkpoints/20261105-192619-1dbe/executor.log:52` |
| Nov 5 23:29:21, `e7f50ee6-02e8-4a26-89cb-2423bdb339d8`, `build:bin-d69fe301-lanes` | 585s / 646.214s | `/work/worktrees/task-d8bdfd3a/.antiphon/checkpoints/20261105-231908-fcc1/executor.log:52` |

The longest observed paired hold was **2703.159 seconds**, lease `c11d88e1-a8ee-469e-8622-e3fc8739c58f`, `CP-4@bin-c1581-final`, Nov 6 **17:36:13.867–18:21:17.027**, Unit filter, in `/work/worktrees/task-edc7b76c/.antiphon/checkpoints/20261106-172638-186e/executor.log:10`. It waited zero seconds. Long holds and long waits are different measurements.

A particularly clear small-check example is `/work/worktrees/task-aae1b064/.antiphon/checkpoints/20261106-054459-9fd7/executor.log:49`:

```text
2026-11-06T06:02:37.8903536+00:00 BUILD SLOT granted lease=213a39c7-c0df-4e3e-9401-41f82508e17d label="CP-92@bin-aae1b064-r3" waited=195s maxcpucount=6
2026-11-06T06:02:46.1546521+00:00 BUILD SLOT released lease=213a39c7-c0df-4e3e-9401-41f82508e17d label="CP-92@bin-aae1b064-r3"
```

It waited **3m15s**, then held the slot **8.264s**. The preceding lines explicitly report `build_slot_busy`, `occupied=4`, `budget=4`. This reconstructs queueing independently of assistant commentary.

### Longer historical report sample

The 2,265 unique numeric checkpoint receipts in Markdown have wait **p50 0s, p95 105s, max 1260s**, mean **18.76s**. They span a longer, differently selected history and are not merged with the recent lifecycle sample. Of these, 2,200 have `/work/` TRX paths (**0 / 105 / 1260s**), 27 `/tmp/` paths (**0 / 405 / 645s**), and 38 Windows drive paths (**0 / 0 / 30s**). Paths identify the stored execution platform, not a live desktop measurement. Completed reports omit canceled/no-TRX rows, which further favors short/successful waits.

The 180 explicit wrapper UUIDs in those documents have wait **p50 0s, p95 271s, max 691s**; the 120 explicit held values have **p50 105s, p95 914s, max 2220s**. Their often missing labels prevent a reliable by-command breakdown. They overlap the checkpoint population and cannot be summed into a fleet total.

The longest historical checkpoint receipt is `.antiphon/task-3a7c132f.md:50`, preserved here with its original provenance:

```text
CHECKPOINT CP-26 commit=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-3a7c132f/.antiphon/checkpoints/20261031-125359-1559/rows/CP-26/run.trx slot=granted waited=1260s dirty=0 source=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-26 waited=1260s executed=125.315s wall=1385.673s
```

That is **21 minutes waiting** in the October 31 12:53:59 run. An exact grant timestamp and UUID are not present in this receipt, so neither is invented.

Comparison-at-master work can be dominated by admission too: `/work/worktrees/task-5fb6b1b2/.antiphon/task-5fb6b1b2.md:130` records base `df1fc145…`, the `RepairSourceDocumentationTests` method waiting **705s** before **7.904s** execution; `:133` records `SpecialistRoleContractTests` waiting **660s** before **11.161s** execution. Its budget at `:136` is **1859.120s wall, 1440.272s admission-only, 418.848s execution**: **77.5%** admission-only. This is an inherited-failure comparison, not an instruction-test speed estimate or typical Merge statistic.

## Merge time: waiting versus running

The two retained Merges provide an accountable comparison, not a representative fleet average.

| Actual invocation in Merge `03477baf` | Wait (s) | Held (s) |
|---|---:|---:|
| Baseline/master solution build (`c1729-master-parity-build`) | 120 | 770 |
| Baseline/master UID discovery (`c1729-master-parity-list`) | 541 | 14 |
| Intermediate guard project build | 0 | 428 |
| Final solution build | 0 | 240 |
| Final UID discovery (`c1729-final-parity-list`) | 60 | 38 |
| Final guard + policy + mechanical contracts (`c1729-guard-policy-contract`) | 105 | 143 |
| Changed core rows + platform guidance (`c1729-core-guidance`) | 60 | 13 |
| Changed driver/resilience rows (`c1729-drivers-resilience`) | 45 | 60 |
| **Serial driver total** | **931 (15.52 min)** | **1706 (28.43 min)** |

Source: `/work/worktrees/task-03477baf/.antiphon/task-03477baf.md:61` through `:75`; exact filters/source/cleanliness and start/end are retained in `.antiphon/c1729-merge/*/manifest.json`. Thus **931/(931+1706) = 35.3%** of recorded serial gate time was queueing. The final three test invocations alone were **210s waiting / 216s held**, or **49.3% waiting**. The base discovery's 541-second wait followed by a 14-second hold illustrates why the name of a check is a poor prediction of its next wait.

Merge `e0a09fa9` provides the contrasting quiet sample: all six invocation waits were **0s**. Holds were solution **205s**, baseline graph-evaluator prerequisite build **6s**, changed fixtures **35s**, policy-A **90s**, policy-B/layering **21s**, contracts/platform **9s**: **366s total held, 0% recorded gate time waiting**. Raw coordinates are `/work/evidence/merge-e0a09fa9/{solution-build,base-tool-build,fixtures,policy-A,policy-B,contracts}.log`, each grant at line 1 and held value in its release line.

These are **serial driver totals**, not complete task wall time: Git integration, source inspection, evidence analysis, finalization, cleanup, conversation waits and unmeasured gaps remain outside them. Summing per-row waits from concurrent checkpoints would overstate wall-clock admission. The checkpoint budget record's `admissionOnlySeconds` is preferable where available; the inherited-failure comparison above demonstrates that distinction. Neither these two Merges nor the heterogeneous historical receipts establishes a fleet “typical Merge” wall-clock percentage.

## What is recorded, where, and why the dashboard can be empty

| Layer | Existing record / limitation | Source |
|---|---|---|
| Wrapper | Sends label/PID/start/task/session. Wait messages contain label. **Successful grant/release messages omit label**, carrying UUID and wait/held; zero-wait invocations may never print a labelled waiting line. | `scripts/lib/build-slot.ps1:83`, `:107`, `:135`, `:174`; wrapper calls acquire/release at `scripts/build-slot.ps1:94`, `:167`. |
| Checkpoint client | Timestamped labelled grant/release lines in `executor.log`, plus per-build/per-row slot and timing fields in reports. Its manifest maps CP IDs to the exact command/filter. These already support offline per-label wait/hold reconstruction. | `tools/Antiphon.Checkpoints/Slots/BuildSlotClient.cs:246`, `:375`; example original lines above and sibling `report.json` / `manifest.resolved.yaml`. |
| Broker lifecycle logs | Grant logs already include UUID, **label**, PID, task/session, waited seconds and occupancy. Release/reap logs include UUID, **label** and held seconds. These are sufficient for an offline label-grouped summary if the shared broker logs are retained and accessible. | `src/Antiphon.SessionRunner/BuildSlotBroker.cs:125`, `:145`, `:225`. |
| Broker metrics | In-memory three-hour window, at most 4096 lifecycle records; mean/p95/max of grant wait and released/reaped hold, grants/releases/reaps, refusal polls and releases/hour. **No p50, per-label field, per-lease identity, or historical peak field.** Refusal counters count polls, not jobs. | `src/Antiphon.SessionRunner/BuildSlotMetricsRecorder.cs:14`, `:23`, `:42`, `:88`, `:99`; `src/Antiphon.SessionRunner.Contracts/BuildSlotContracts.cs:73`. |
| Read route | `GET /build-slots` exposes current labels/occupancy, not historical wait. `GET /build-slots/metrics` is registered read-only in current source. | `src/Antiphon.SessionRunner/BuildSlotRoutes.cs:69`; `docs/ops-http.md:201`. |
| Log retrieval | Runner Serilog files live at `/state/runner-logs/session-runner-YYYYMMDD.log`; trusted outer-host retrieval is documented. Local runner logs had no matching build-slot lease/waiter lifecycle lines. They are not the separate broker's logs. | `docs/logs.md:80`. |

Direct read observations, **2026-11-06 18:54:24Z**, recorded in ignored `runtime.json`:

```text
ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots
GET http://build-slots:8080/build-slots -> 200
enabled=true budget=4 maxCpuCount=6 occupied=4 availableMb=95438 floorMb=16384
GET http://build-slots:8080/build-slots/metrics -> 405, Allow: DELETE
GET http://127.0.0.1:8080/build-slots/metrics -> 200, grants=0 releases=0 retainedRecords=0
loopback brokerStartedAtUtc=2026-11-03T15:11:07.2892252Z
```

The shared route's 405 and `Allow: DELETE` are the older-route shape already documented at `docs/testing-and-build.md:1206`. Current source has a metrics route, so source availability cannot establish activation on the separate broker. `scripts/c590-remote.sh:998` initializes the shared broker image pin only when absent; the compose image uses `BUILD_SLOTS_SHA12` (`docker-compose.server2-runner.yml:168`). This is an established deployment mechanism compatible with the observation, **not an independently verified value for today's image pin**. No container was recreated or pin changed.

The metrics recorder stores only `(kind, timestamp, duration)` (`BuildSlotMetricsRecorder.cs:99`), so an upgraded broker's aggregate endpoint alone still cannot answer “which label waited longest.” Existing labelled logs can answer it without new product instrumentation. Wrapper-only reports cannot always do so because successful wrapper messages omit labels. Qualification timing JSON is also not a substitute: the documented qualification wrapper takes no build slot, so its build-slot wait remains null (`docs/test-performance-standards.md:57`).

## Capacity and lease lifetime

| Host/broker | Configured slots / CPU hint / memory floor | What was verified |
|---|---|---|
| Shared server2 broker | **4 / 6 / 16384 MiB**, `HolderLiveness=renew`, 20s renewal, 90s renewal grace | Compose `docker-compose.server2-runner.yml:166`, `:176`; inherited runner budget variables also 4/6/16384; shared GET directly confirmed budget/CPU/floor. Renewal mode/intervals are declared config, not exposed in that GET. |
| Desktop defaults | **2 / 4 / 6144 MiB**, `HolderLiveness=pid` | `src/Antiphon.SessionRunner/BuildSlotSettings.cs:17`, `:20`, `:23`, `:39`. No desktop process or live endpoint was queried, so overrides and actual peak are unknown. |

At **18:54:24Z**, server2's listing contained four simultaneously granted, live leases:

| UUID | Label | Granted UTC |
|---|---|---|
| `8ba76b96-35db-43c2-9814-d0489350cab7` | `CP-4@bin-c1581-final` | 18:46:50.506 |
| `527ed00a-d2b4-4390-bad2-49e51cfe728b` | `c1217-unit-5` | 18:52:19.563 |
| `7a622047-3b63-4b60-af2a-045f551db037` | `c1737-r2-path-clean` | 18:52:51.281 |
| `f969bc77-9ae5-48a5-8596-80d59e1f0ac4` | `CP-ISO-1@tests/Antiphon.Tests` | 18:53:04.762 |

Thus the **observed peak is four**, equal to current configured capacity. The incomplete paired checkpoint intervals alone reconstruct only a peak of three; direct-wrapper and unmatched intervals are absent from that reconstruction. Historical refusal payloads independently report 4/4, e.g. `/work/worktrees/task-013b9ff7/.antiphon/checkpoints/20261105-191934-d314/executor.log:9` at Nov 5 19:26:24Z. There is no historical maximum counter proving that capacity never differed earlier.

Admission has one FIFO waiter list and one lease list under a lock; the grant checks available positions, then live memory (`BuildSlotBroker.cs:95`, `:103`, `:111`). Label text is not inspected to prioritize policy checks or test execution. The 157 inventoried logs contained **no `reason=build_slot_memory_floor` lines**; they repeatedly contained busy/4-of-4 responses. This supports capacity queueing in this sample, not a claim that server2 has never hit its memory floor. The present memory reading was well above the floor.

The settings' default TTL is **90 minutes**, absolute maximum hold **48 hours**, retry **15 seconds**, waiter silence **60 seconds**, sweep **30 seconds** (`BuildSlotSettings.cs:26` through `:37`). Absent or zero `LeaseMaxHoldHours` chooses 48; a negative value fails validation (`:57`). Grant age is not reset by renew/re-acquire. A local PID broker can keep a still-matching live holder past TTL; the absolute bound takes precedence at the next acquire/renew/sweep (`BuildSlotBroker.cs:87`, `:151`, `:202`). A renewal broker also checks renewal liveness, and its existing TTL behavior is separate. Reaping releases a lease and logs the reason; it does not kill the command (`:224`). Current source is not proof that the pre-metrics shared image contains the later maximum-hold change. Today's deployed maximum-hold implementation is unverified.

## Confirmed mechanisms and remaining uncertainties

**Confirmed from stored output and current source:** all these checks use ordinary driver admission. Short instruction/architecture checks can queue for minutes behind holders because they share a finite FIFO budget, with no label-based priority. Compilation and policy subprocess work account for substantially longer holds than instruction-text assertions. The shared broker does not serve the metrics route that the newer runner source exposes; loopback's zero-count metrics describe a different broker. Per-label reconstruction is possible from existing labelled checkpoint/lifecycle logs, but not from all successful wrapper lines or aggregate metrics.

What remains uncertain:

1. Exact current shared broker image/pin and whether its binary contains `LeaseMaxHoldHours`; outer-host read access or its retained deployment receipt would resolve this. The route result itself is confirmed, the precise image cause is not.
2. Complete shared-broker grant/release/reap history, actual waiters that timed out or lost their place, and which four holders caused every historical long wait. Shared broker logs with timestamps/UUIDs would resolve these; the retained worktrees are a selected sample.
3. Desktop live configuration and historical peak; these require a separately authorized observation under the brief's no-desktop constraint.
4. A representative complete-Merge wall-clock split and causal effect of host load. A defined cohort with task start/end plus non-overlapping driver/admission intervals is absent here. Per-lease sums alone cannot resolve it.
5. Whether the very long Unit holds would have completed, and their unrecorded fixture/external-call phases. Deadline-held intervals are preserved as such, without successful-Full or measured-p95 credit.

## Not done, noted

Smallest logging change, not implemented: include the existing `Label` in successful wrapper grant/release lines at `scripts/lib/build-slot.ps1:107` and `:174` (retain it in the returned lease at `:120`); broker lifecycle logs already include it at `BuildSlotBroker.cs:125`/`:145`, so no new lease store is needed for offline per-label statistics.

--- next stage ---
next: decide
handoff: Decide whether to commission the minimal wrapper label logging change or a separate shared-broker activation/history task; use the retained-sample wait/hold evidence without treating it as a fleet p95, successful Unit timing, or authority to change capacity.
artifact: docs/investigations/2026-11-06-investigate-build-slot-waits.md
