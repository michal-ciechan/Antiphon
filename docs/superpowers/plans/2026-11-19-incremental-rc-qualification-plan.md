# Incremental (delta) rc qualification: what exists, what a slim rc needs, risk, slices

Plan date: 2026-11-19. Task `17eb0a70`. Inspected source: master `64ad5ba0bd3ffcfa3c7156d8d7a8cd6e63a0f58e`; rc tips read from `origin/rc/2026-11-2x`. Read-only planning: no qualification, build (other than the checkpoint tool for `import --plan`), deploy, restart or desktop access. Mutation waived (operator 2026-11-12).

Operator question: *"What do we need to do to make rc cuts slimmer based on what actually changed?"*

**Short answer.** Most of the machinery is already planned and half built under CARD-1836 (ledger, boundaries, input manifests, admission, forced-Full, baseline picker, exact plan). Two things stop it from saving anything today, and they are what this plan adds:

1. **Nothing is wired.** The CARD-1836 libraries have no caller in the qualification path; `release-cut.ps1`/`nightly-run.ps1` still accept only `Full|Shadow|ChangeAware`, and every hop asserts `effectiveMode=Full` (ground truth G-3, G-4). The remaining CARD-1836 chain S6a–S7d is the wiring; it is not re-planned here.
2. **Even wired, it would carry zero chunks.** The committed owner map `scripts/lib/rc-input-owners.json` has one root, `**` consumed by `*` (G-6): every changed file dirties every suite. And the two suites that dominate cost (`antiphon` ~29 h and `e2e` ~18 h of the 47 h serial class-cost estimate, G-9) both reference the monolithic `Antiphon.Server` project, which changes in practically every cut. A sound project-level map therefore saves only the small suites. **Real savings need a sound class-level dependency map inside `Antiphon.Tests`, and that is the hard, unproven part.** This plan sequences it measure-first.

## Ground truth

| # | Card/brief assumption | What the code does (file:line at 64ad5ba0b) | Consequence |
|---|---|---|---|
| G-1 | The derived impact index is a candidate for chunk dependencies | `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs:73-226` builds path→class edges from syntax only: unique simple type names used in `src/`,`server/`,`tests/` `.cs` (`:468-472`), string literals that resolve to tracked paths, and **one** hop through a test's own helper files (`:131-140`). No transitive call graph, no DI, no MSBuild. Each path is **capped at 25 classes** (`:21`, `:189-197`); 154 paths are capped/allowlisted (`cappedPaths`), including `server/Application/Services/AgentTaskDispatcher.cs`, `AppDbContext.cs`, `AntiphonWebAppFactory.cs`, `TestDbFixture.cs`, `AGENTS.md`, `docs/orchestration-loop.md`, `Directory.Build.*` | Positive edges only. A non-hit is **not** evidence of independence. Unusable as a carry authority; usable as an added-dirty source (CARD-1836 D-4 already says this) |
| G-2 | `impacted-classes.ps1` selects by change | `scripts/impacted-classes.ps1:1-20` is a pre-land Code/Review/Merge selector: hand rows in `tests/impacted-classes.json` + derived edges; `-MergeSubset` stays the hand subset. Read-only, no rc role | Not an rc mechanism. CARD-1983 shows it over-selects (one compose line → 17 rows, 3.5 h); under-selection is the rc's job to catch |
| G-3 | Release selection can run a subset | `scripts/lib/release-selection.ps1:229-230,256,281` always emits `effectiveMode='Full'`, `executionEvidence=$false`; `:374-375` throws `selection-hop-scope` if any hop is not Full; `:396` returns full membership. Header `:2` "Intent only: every admitted attempt executes Full" | `Shadow`/`ChangeAware` are proposals only. Incremental needs CARD-1836 S7b to change every hop |
| G-4 | An incremental mode exists | `scripts/release-cut.ps1:30`, `scripts/nightly-run.ps1:91`, `scripts/lib/nightly-tests-impl.ps1:1792`: `ValidateSet('Full','Shadow','ChangeAware')` | No Incremental entrypoint yet |
| G-5 | A result ledger with carry rules exists | `scripts/lib/rc-ledger.ps1:290-337` `Get-RcIdentityCarryDecision` (forced-full, unknown boundary, cross-platform, digest compare, outcome, chunk outcome, same-digest flake, provenance) and `:339` chunk decision. Landed CARD-1836 S1 (81a036430, 4e3120b9d) | Carry predicate is done and default-deny. Only callers are `scripts/rc-ledger-*.ps1` (`rc-ledger-plan.ps1:7`, `rc-ledger-import.ps1:13`, `rc-ledger-status.ps1:5`) |
| G-6 | Chunk input digests can tell what changed | `scripts/lib/rc-input-manifest.ps1:56,217` + `scripts/lib/rc-chunk-digest.ps1:32,166` (S3a/S3b) implement the conservative inventory and digest. **Owner map** `scripts/lib/rc-input-owners.json` = `{"roots":[{"path":"**","consumers":["*"],"reason":"repository-scanners-conservative-all-inputs"}]}` | Sound but maximal: any change dirties all suites. Zero carries until the map is refined (this plan S3) |
| G-7 | Chunks are cut along boundaries | `nightly-tests-impl.ps1:654-658` `ConvertTo-NightlyProfileLaunchGroups -BoundaryAware` (S4a) exists; default caller still packs namespace/time bins with ordinal ids (`all-group-N`, `all-profile-N`, `-split-N` at `:387`). Boundaries from `scripts/lib/rc-boundary-prefixes.json` (seven layers; generic Infrastructure unassigned) | Old run chunk ids (the 1,274 entries of a6278259) are ordinal and **cannot** confer carry (CARD-1836 D-2). The first boundary-packed run has nothing to carry from |
| G-8 | Seeds could skip passed work | `scripts/lib/nightly-dispatch.ps1:3-27` and `nightly-stopped-run-seed.ps1:2` ("Seeds influence order, never execution credit"); `nightly-failure-history.ps1:2` same | Seeds order only. Keep that: the failed-first seed is part of the safety floor, not a carry source |
| G-9 | ~100 commits since the previous cut, small part of tree | `rc/2026-11-27`(e229fb4d3)→`rc/2026-11-28`(997f9b8d9): 139 commits, 129 files: 51 docs, 24 `tests/Antiphon.Tests`, 9 `server/**`, 5 `src/Antiphon.SessionRunner*`, 5 `tests/Antiphon.SessionRunner.Tests`, 11 scripts, 1 `tests/derived-impact-index.json`. Which rc the "previous green run" ran is **UNOBSERVED** (Windows logs). Class costs `scripts/lib/nightly-class-costs.json` (772 priced classes): `Antiphon.Tests` 29.2 h, `Antiphon.E2E` 18.0 h (conservative bounds, CARD-1584) of 47.2 h serial | `server/**` changed → `antiphon` and `e2e` are dirty at project granularity. Docs are not inert: `AGENTS.md` alone feeds 221 classes, `docs/orchestration-loop.md` is capped |
| G-10 | Test classes are separable | 1,810 classes with `[Test]` in `tests/Antiphon.Tests`; 604 use `TestDbFixture` (whole EF model + migrations), 81 `AntiphonWebAppFactory` (whole `Program`/DI), 254 `RepoRoot`, 220 spawn processes, 71 `ScriptHarness`, 44 reflect over assemblies | Roughly 40–60% of classes depend on whole-host or whole-repo inputs by construction. Class-level carry is plausible only for the remainder, and for DB classes only when the model/migrations did not change |
| G-11 | A periodic full run backs carries | `scripts/lib/rc-forced-full.ps1:79-120` forces Full on contract/SDK/runtime/harness/input-map change, missing/expired (>= 7 days) or future anchor, shadow discrepancy. Nightly master full: `docs/testing-and-build.md` § Nightly records the Windmill job as **absent** at the 2026-09-11 audit; current deployment is **UNOBSERVED** from server2 | The 7-day Full anchor is the real backstop; a nightly full cannot be assumed to exist |
| G-12 | CARD-1545 project batches help | `scripts/lib/rc-project-selection.ps1:2` "advisory project batches; never execution or qualification authority"; `docs/release-gates.md` § Advisory project batches: shared build/scripts/policy, client and script inputs fall back to Full | Usable as the project-graph reader for S3's map; not an authority |

No migrations, public DTOs/routes, operation labels, remote `.sh` functions or statement budgets change. Existing tests affected are listed under Verification scope; the CARD-1782 impacted-classes table (`tests/impacted-classes.json`) matches no row for the files this plan adds except the derived-index drift guard.

## (1) What exists, mechanism by mechanism

| Mechanism | Does toward change-based selection | Does NOT do |
|---|---|---|
| Derived impact index (CARD-1820, G-1) | Cheap syntax reverse map; adds classes to Code/Review selection; lane split (`windowsOnly`, `linuxOnly`) | No transitivity, capped at 25, no DI/config/compose/script semantics, never a negative proof, no rc consumer |
| Release impact envelope / script closure (CARD-1820 S1R1–S1R5; `scripts/lib/release-impact.ps1:1-3`, `release-impact-scripts.ps1:1-5`) | Candidate-bound evidence; cross-language script closure | Always `incomplete` / fallback Full by design (`release-impact-scripts.ps1:4`) |
| Hand impacted rows (CARD-1782, G-2) | High-radius pre-land selection | Pre-land only; over-selects (CARD-1983) |
| Selection intent (CARD-1313, G-3) | Records a proposal per attempt in `summary.json` | Executes Full always |
| Project batches (CARD-1545, G-12) | ProjectReference graph, linked Compile/content, downstream closure | Advisory; mixed projects whole |
| rc ledger S1 (G-5) | Per-identity carry predicate with flake and provenance rules | No caller; `class/method` key without suite (D-2 envelope fixes) |
| Boundaries S2a / packing S4a (G-7) | Stable membership-hash chunk ids per suite+boundary | Not the default packer yet |
| Input manifest S3a / digest S3b (G-6) | Sound digest over an owner map | Owner map is "everything feeds everything" |
| Historical evidence S5a / admission S5b | Streaming import of a retained run; admitted/dirty/unproven counts | Old runs lack a build→input chain, so most rows land `unproven` (CARD-1836 D-3) |
| Forced Full S8a (G-11) | 7-day anchor, contract hashes | Not wired |
| Baseline picker S7a; exact plan S5c | Baseline ref + partition `U = D ∪ S ∪ (C−S)` | Not wired |
| Failed-first seed (CARD-1823/1633, G-8) | Orders failures, interrupted, untouched, siblings, passed | Order only, by rule |
| Qualification supervisor (`scripts/qualification/launch-rc.ps1`, `supervise-rc.ps1`) | Launches and watches the Windows Full | No notion of carried work |

Remaining CARD-1836 chain (planned in [the activation plan](2026-11-12-card-1836-incremental-activation-plan.md), not landed at the inspected source: no `rc-dirty-order`, `rc-shadow-sample`, `rc-run-evidence`, `rc-closure` libraries): S6a dirty order, S8b shadow sample, S6b prospective build/source evidence, S6c scheduler, S6d closure, S7b hop wiring, S7c front door, S7d native proof, S7e release baseline.

## (2) What a slim rc needs

### Design in one paragraph

Pack each suite by boundary (S4a). For every chunk compute an **input digest** over the complete set of things it can depend on (owner map, below). Store the digest with each chunk result in the CARD-1836 ledger. On a recut, a chunk is **carried** when, and only when, its last result on the same platform passed at an admitted run whose digest equals the new digest, the forced-Full predicate is empty, it is not in the failed-first seed or open-failure history, and it was not drawn into the shadow sample. Everything else runs. The run's terminal envelope lists carried chunks with their origin run, origin digest and origin TRX digest, so an auditor can walk any carried result back to an executed one. This is exactly CARD-1836 D-4..D-8; what is new here is the **owner map** that makes the digest selective, and the **measurement and soundness replay** that must precede trusting it.

### The dependency set ("owner map") per chunk — three levels

| Level | Chunk depends on | Sound? | Expected selectivity |
|---|---|---|---|
| M0 (today) | Every tracked file | Yes, trivially | Zero carries |
| M1 project closure | Own test project tree; every file of every transitively referenced project (ProjectReference + linked Compile/content/imports, CARD-1545 reader); `tests/Shared/**`; build graph roots (`Directory.Build.*`, `global.json`, `*.sln`, `NuGet.config`, package locks); test-execution/selection policy and the selector/harness contract (forced Full anyway); plus, for suites that read the repo (`antiphon`, `scripts`, `e2e`), **every non-code tracked file** (`docs/**`, `*.md`, `scripts/**`, `docker/**`, compose, `.claude/**`, `client/**` for e2e) | Sound up to what MSBuild can do dynamically (unresolved import → all) | Small suites carry; `antiphon` and `e2e` almost never (G-9) |
| M2 class closure inside `Antiphon.Tests` | M1 for the project minus `server/**`/`src/**` C# files, **plus** the union of: (a) the files the class executed in its last Full run (per-class file-level runtime coverage), (b) the derived-index positive edges for the class, (c) every non-C# tracked file unless the class is in a declared "no repository reads" set proven by the census in S4 | Sound for C# code paths **except** the blind spots below; each blind spot gets a rule | Unknown; S1/S4 measure it |

A class that boots the host (`AntiphonWebAppFactory`, `Program`), uses `TestDbFixture` with a changed EF model/migration, spawns a process, reads the repository root or reflects over an assembly stays at M1 for its project. M2 never narrows those.

E2E stays at M1 (whole server + client + bundle) in this plan. Browser-side coverage and server-side coverage per E2E class are possible later but are not in scope; say so to the operator rather than promise it.

### Blind spots and how each is covered

| Blind spot | Why impact analysis misses it | Rule |
|---|---|---|
| Reflection / assembly scanning (44 classes) | A **new** type in a new file is found by an unchanged scanner; coverage never saw the new file | Any added or deleted `.cs` file in a project dirties every class in its consumer projects that reflects (census of `GetTypes()`, `Assembly.`, `Activator`, `[ModuleInitializer]`); added files are never M2-carriable |
| DI wiring | Registration changes in `Program.cs`/`ServiceCollection` extension files alter behaviour of everything resolved later | Host-booting classes stay M1; registration files are in every M2 class's set via coverage (they execute at startup) |
| Config / `appsettings*` / options defaults | Data, not code; coverage does not record file reads | All config/resource/content files are non-C# inputs → M1 for any class not proven read-free |
| docker / compose / Dockerfile | Read as text by contract tests or used by container fixtures | Non-C# input → M1; CARD-1983's narrowing is a pre-land concern and must not be reused as rc authority until its own soundness witnesses exist |
| PowerShell / shell scripts | Executed in child processes, invisible to .NET coverage | `scripts` suite and every class spawning `pwsh`/`bash` stays M1 over `scripts/**` and `tools/**` (CARD-1836 D-4 already) |
| Docs-as-tests | Documentation tests read `AGENTS.md`, `docs/**`, skills | Non-C# input; docs-only changes still dirty the documentation classes. No docs exemption |
| Generated files (`derived-impact-index.json`, rosters, EF snapshot, client `dist`) | Produced by a build step, compared by a test | Generated outputs and their generators are inputs; the derived index regenerates on most lands, so the index file is an input of `DerivedImpactIndexTests` and of anything reading it, not of everything |
| Test-only changes | A changed test or shared helper | Own test file → class dirty; `tests/Shared/**`, `TestHelpers/**`, fixture projects → every consumer dirty (M1) |
| Time/clock, locale, timezone | Code unchanged, environment changed | Runtime/toolchain contract in the forced-Full hash set (S8a); calendar-sensitive failures are caught by the always-run sample and the 7-day Full |
| Ordering / shared state / shared DB | A carried class's presence changes neighbours' state; flakes depend on co-scheduling | Carry is per chunk; executed chunks run with the same packing; a failure that only appears under the full co-schedule is a Full-only find, so the 7-day Full and the shadow sample are the detector. Known shared-DB theft (`reconciler-shared-db-leftover-fault-theft`) shows this class exists |
| External images, Playwright browser, SDK, NuGet | Floating tags | Immutable ids in the contract; unknown = dirty (CARD-1836 D-4 table) |
| Flaky passes | A pass at digest D does not mean D is green | S1 ledger same-digest flake rule (`rc-ledger.ps1:271`) plus failed-first seed and open-failure history always run |

### Carry-forward rule (precise)

- **Digest**: `SHA-256` over the sorted, length-delimited canonical records `(manifestVersion, ownerSuite, repoPath, gitMode, gitObjectId | absent, edgeReason)` for every input of the chunk at the given level, plus the contract hash set from `rc-forced-full.ps1:83` (S3b format, unchanged). Paths are included so rename/add/delete changes the digest. The level (M1/M2) and owner-map digest are part of the record set, so changing the map dirties everything once.
- **Where stored**: the CARD-1836 D-2 ledger shards (one suite per shard) under the release root, outside the checkout, gitignored; the admission envelope binds candidate SHA/tree, map digest, contract digest and shard digests.
- **Run record**: the terminal envelope lists every obligation as `executed`, `carried(originRunId, originChunkId, originDigest, originTrxSha256, anchorRunId)`, `sampled` or `not-run`; `rc-ledger-status.ps1` prints counts and the carried wall time separately from executed wall time. A carry chain is bounded by the Full anchor and depth 64 (CARD-1836 D-2).

### Safety floor (always runs, every cut)

1. The failed-first seed and every identity with open failure history since the Full anchor (CARD-1823 containment rule).
2. The shadow sample: `N = min(C, max(30, ceil(0.05 C)))` carried chunks, stratified per suite×boundary (CARD-1836 D-8), seed stored before launch.
3. Every chunk of any suite with no result adapter (client/lint until proven), and every Windows-only chunk whose last native result is older than the anchor.
4. Forced Full of the **whole tree** when any of these change: SDK/runtime/`global.json`, `Directory.Build.*`, `*.sln`, package locks, `tests/test-execution-policy.json`, `tests/test-selection-policy.json`, `tests/rc-project-categories.json`, the owner map, the selector/ledger/admission/dispatch/coverage scripts and `Antiphon.Nightly`/`Antiphon.Scripts`, any `ParallelLimiter`/spawn-limit declaration, `tests/Shared/**` test infrastructure (M1 for all consumers is already almost all), or an unresolved import.
5. **Cadence**: an all-executed Windows Full at least every 7 days (`full-anchor-expired`, already coded), and always before a human tag (below). A nightly master full is desirable but not assumed (G-11, UNOBSERVED).

## (3) Risk

- **What could ship unqualified**: a defect whose cause lies outside the chunk's dependency set: a missed edge (reflection on a new type, a config read by an untracked path, a script reached dynamically, environment drift) in a chunk that passed at an equal digest and was not sampled. At M1 the residual set is MSBuild dynamics and environment; at M2 it adds every coverage blind spot above.
- **How a carried chunk that would have failed is caught**: (a) the shadow sample, with miss probability `choose(C−k,N)/choose(C,N)` for k bad chunks (30 samples detect a 10% bad population ~96% of the time; a single bad chunk among 600 carries is detected only 5% of the time); (b) the 7-day all-executed Full; (c) the Linux post-land early warning (CARD-1784) for portable classes. The **primary** defence is the conservative map, not the sample.
- **Blast radius**: at most every cut since the last all-executed Full (≤ 7 days, roughly 3–7 rc cuts at the current pace) could carry the same undetected failure. On discovery: the whole carry chain since the anchor is invalidated (`shadow-discrepancy` forces Full), a Debug task follows the repair loop, and every release cut on that chain is reported as **not Full-backed** for that chunk.
- **Tag/promotion rule (decision D-5)**: a human tag/promotion must be backed by an all-executed Windows Full of the exact tagged SHA, **or** by a chain of carried runs that bottoms out in an all-executed Windows Full strictly under 7 days, every link admitted, no shadow discrepancy, no open failure. Until a CARD-1836 D-8 producer of trusted shadow cuts exists, only the first form is allowed (`RcTagBacking.ChainFormEnabled` is false). Publication consumers already refuse ledger-basis success (`gateCredit=false`, CARD-1836 D-6); this rule is the policy text that S5 adds to `docs/release-gates.md`.

## (4) Sliced implementation plan

Order: measure first, then soundness, then the map, then wiring (CARD-1836 chain), then policy. S1–S3 and S5 are portable (server2). S1's **execution** on real evidence and the CARD-1836 S7d native proof need the desktop.

| Slice | Goal | Files | Red witnesses | CP | Lane |
|---|---|---|---|---|---|
| S1 | Read-only carry replay report: for two run roots (a6278259 vs the previous green run) and their SHAs, compute per chunk the M0/M1 digests from Git objects, decide carry with `Get-RcIdentityCarryDecision`, and print carried count and carried wall time per suite. Also a pure-Git mode over the last N rc cut pairs (no Windows evidence) that prints the dirty-suite matrix per cut. Writes only to an output path outside the checkout | `scripts/rc-carry-replay.ps1`, `scripts/lib/rc-carry-replay.ps1`, `scripts/test-rc-carry-replay.ps1`, `tests/Antiphon.Tests/Scripts/RcCarryReplayTests.cs` | `M0CarriesNothing` (a one-file docs diff under the committed `**` map reports carries); `M1CarriesUntouchedSuiteOnly` (fixture: change in `src/Antiphon.Messaging` must dirty messaging and every referencing suite, carry pty-host); `ReportSumsWallOnce` (a split chunk's wall is double-counted) | CP-1 | server2 code; real-evidence run on desktop (read-only, operator OD-1) |
| S2 | Selector soundness replay: a committed table of known historical rc failures (CARD-1823 ledger + `docs/investigations/2026-11-08-why-missed-*`, 2026-11-09 notes): failing class, introducing commit, previous cut. Replay each through M1 (and later M2) and assert the failing chunk is **dirty**. One case per known failure family | `tests/rc-soundness-cases.json`, `tests/Antiphon.Tests/Scripts/RcSelectorSoundnessReplayTests.cs` | `EveryKnownFailureIsDirtyAtM1`; `MapThatDropsDocsRootsIsCaught` (a deliberately narrowed map carries the docs-driven failure) | CP-2 | server2 |
| S3 | M1 owner map, redesigned 2026-11-20: project closure, census readers, and explicit roots own a path; `scripts/lib/rc-input-noop.json` is silent; every other path is unowned, selects nothing, and blocks carry credit. No `**` fallback and no all-suite broadening | `scripts/lib/rc-input-owners.json`, `scripts/lib/rc-input-noop.json`, `scripts/lib/rc-input-manifest.ps1`, `tests/Antiphon.Tests/Scripts/RcInputOwnersMapTests.cs` | `UnknownPathSelectsNothing`; `ServerChangeDirtiesAntiphonAndE2E`; `DocsChangeDirtiesRepoReaders`; `BuildGraphChangeForcesFull` | CP-3 | server2 |
| — | CARD-1836 S6a, S8b, S6b, S6c, S6d, S7b, S7c (wiring, Shadow mode) | per the activation plan | per the activation plan | its CP-10..CP-16 | server2 |
| — | CARD-1836 S7d native proof | per the activation plan | per the activation plan | its CP-17..19 | **desktop** |
| S4 | Measure-only M2 feasibility: census of `Antiphon.Tests` classes into host-booting / DB / process / repo-reader / reflecting / pure, and a per-class file-coverage capture design for one Windows Full (collector choice, overhead, output size). Report only; no carry authority | `scripts/rc-class-census.ps1`, `tests/Antiphon.Tests/Scripts/RcClassCensusTests.cs`, investigation note | `HostBootingClassIsNeverPure` (a class whose base derives `AntiphonWebAppFactory` classified pure); `RepoReaderDetectedThroughHelper` | CP-4 | server2; coverage capture later on desktop |
| S5 | Tag/promotion backing rule: `Test-RcTagBacking` reads an admission envelope chain and answers `full`, `chain-to-full(anchor, age)` or `not-backed(reason)`; `docs/release-gates.md` gains the D-5 rule. No tag/publish code calls it yet | `scripts/lib/rc-tag-backing.ps1`, `docs/release-gates.md`, `tests/Antiphon.Tests/Scripts/RcTagBackingTests.cs` | `ChainOlderThanSevenDaysIsNotBacked`; `ShadowOnlyChainIsNotBacked`; `CarryWithoutAnchorIsNotBacked` | CP-5 | server2 |
| S6 (later, gated on S4 numbers) | M2 class-level carry for the pure/DB classes, with coverage sets as inputs, behind its own map version | new card | new card | new plan | desktop + server2 |

Every slice also runs CP-90 (the standing guard roster) and CP-91 (derived-index drift: adding a test class changes `tests/derived-impact-index.json`; regenerate with `scripts/regenerate-derived-impact-index.ps1` before the row).

## (5) Estimated saving

All numbers below are **UNOBSERVED** unless marked; the Windows per-chunk timings, the previous green run and the a6278259 chunk-to-suite split live on the desktop.

- Observed: a6278259 scheduled 1,274 TRX entries, ~16 h wall (brief). Class-cost serial bound 47.2 h: `antiphon` 29.2 h, `e2e` 18.0 h (G-9; E2E figures are conservative provisional bounds, not measurements).
- **M0 (today's map)**: 0%.
- **M1** on a typical cut (rc/27→28: `server/**`, SessionRunner, scripts and docs changed): `antiphon`, `e2e`, `session-runner`, `scripts` and every suite referencing SessionRunner contracts are dirty; carriable are some of `messaging`, `markdown-pdf`, `observability`, `server-git`, `git-in-process`, `resilience`, `pty-host`, `agents-pty`, `client`. Estimate **0–8%** of wall, minus the floor (sample, seed). Of the last four rc pairs (git, server2), `server/**` changed in three (24→25: 15 files plus 10 `client/**`; 25→26: 2; 27→28: 9). The fourth, 26→27, changed no server or client file but changed `tests/test-execution-policy.json` (forced Full under the floor) and two `tests/Antiphon.Tests` files and docs (`antiphon` dirty at M1 as a repository reader). So M1 would have carried `antiphon` or `e2e` in none of the four.
- **M2** (class closure inside `Antiphon.Tests`, E2E still whole): if 40–60% of classes stay M1-bound (G-10) and a ~10-file server delta touches the coverage set of a further 10–30% of the rest, `antiphon` carries roughly **15–45%** of its time; with E2E always executed the whole-run saving is roughly **10–30%** of wall. This is a guess pending S1/S4 and must not be quoted as a promise.
- A larger and safer lever is wall-time parallelism (the one-slot `ProcessSpawnLimit` lane admitting ~46 UIDs/min, `docs/testing-and-build.md` § Windows rc watchdogs); it is outside this plan's scope and named so the operator can compare.

## Decisions

- **D-1 Build on CARD-1836; do not fork it.** The ledger, digest format, admission and forced-Full rules stay as planned. Rejected: a second "carry" implementation keyed on the derived index (unsound, G-1); using seeds for credit (forbidden by rule, G-8).
- **D-2 Project closure (M1) is the first authority; class closure (M2) only after measurement.** Rejected: activating M2 from coverage on a single run (coverage blind spots above); using CARD-1983 narrowing for rc.
- **D-3 Unowned selects nothing and is flagged.** A path is owned (project closure, a census-proven reader, or an explicit root), a checked-in no-op (`scripts/lib/rc-input-noop.json`: selects nothing and raises no flag), or unowned. Unowned selects no suite. The selection reports each unowned path with a hint (`new-csproj`, `new-docs-folder`, or `unknown`) and sets `needsOrchestratorDecision`. While that list is non-empty, or an unresolved closure flag remains, the cut records `creditBlockedBy=unowned-paths` and is not eligible for carry credit. `gateCredit` stays false. The orchestrator adds an owner row or a no-op row and recomputes. This supersedes the earlier "unknown means dirty everywhere" rule: `unmatched-path-fallback` and `unresolved-conservative-all-inputs` no longer select every suite. Rejected: a docs-only exemption that hides an unread class without a no-op row.
- **D-4 Safety floor as listed**, including 7-day all-executed Windows Full. Rejected: a nightly-only floor (not observed to run).
- **D-5 Tag rule**: Full of the exact SHA, or a chain to a Full strictly under 7 days once a CARD-1836 D-8 producer exists; until then Full only (`ChainFormEnabled` is false). Operator decision OD-2 below.
- **D-6 Measure first.** S1's report on real evidence decides whether S3/M1 wiring is worth its cost before more wiring is commissioned; if S1 shows <5% for M1, the next investment is S4/M2 or wall-time parallelism instead.

Operator decisions with stated defaults: **OD-1** run S1 read-only on the desktop against a6278259 and its previous green run (default: yes, once S1 lands; read-only, no live-run access). **OD-2** tag rule D-5 (default: Full only). **OD-3** whether to spend one instrumented Windows Full with per-class coverage for S4/M2 (default: decide after S1 numbers). **OD-4** whether the CARD-1836 S6–S7 chain is resumed now or after S1 (default: S1–S3 first; they are independent of the chain).

## Performance budget

Apply [testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards). This plan ran no tests. All budgets are provisional.

| Activity | Budget | Basis |
|---|---|---|
| New script-harness test method (S1–S5) | per-method timeout 60 s; target ≤ 20 s body; one fixture Git repository per class created once, cases clone cheaply | No measurement yet; ~2x p95 replaces it after five ordinary runs |
| CP-1..CP-5 rows | 15 min first row (includes the `Antiphon.Tests` build, ~8–12 min on loaded server2), 5–6 min reuse rows | `unit-lane-row-deadline-loaded-server2` memory: build cost dominates |
| CP-90/CP-91 shared guards | 5 min each | Existing CARD-1836 rows (79 tests, 27 tests) |
| S1 real-evidence replay (desktop) | ≤ 15 min, streaming readers (885 MB summary class), ≤ 256 MiB working set | CARD-1836 D-3 streaming rule; any run >30 min gets a Debug task |
| Shared setup | One fixture repo per class; no AppHost, DB, container, browser | — |

Phases recorded: build, slot wait, discovery, fixture setup, body, teardown, per the checkpoint tool. No new operation labels.

## Verification scope (not a full Verification design)

TestDesign is a separate stage; it finalises method names, counts and Min. Existing tests affected: `DerivedImpactIndexTests` (index drift on new classes, CP-91); `RcIncrementalLedgerTests`, `RcLedgerStatusTests`, `RcHistoricalAdmissionTests` (readers S1 consumes, CP-95-style adjacency in TestDesign); `RcInputManifestTests`, `RcChunkInputDigestTests` (S3 changes the owner map those classes read: their fixtures must keep passing with the committed map). No migrations, DTO/routes, operation labels, remote script functions or statement budgets.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-irq-s1/` | linux-replay | `/*/*/RcCarryReplayTests/*` | S1 witnesses | all named methods, no failures/skips | 3 | 15 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-irq-s2/` | linux-soundness | `/*/*/RcSelectorSoundnessReplayTests/*` | S2 witnesses | all named methods, no failures/skips | 2 | 15 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-irq-s3/` | linux-owners | `/*/*/(RcInputOwnersMapTests*)\|(RcInputManifestTests*)\|(RcChunkInputDigestTests*)/*` | S3 witnesses + existing readers of the map | all methods, no failures/skips | 4 | 15 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-irq-s4/` | linux-census | `/*/*/RcClassCensusTests/*` | S4 witnesses | all named methods, no failures/skips | 2 | 15 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-irq-s5/` | linux-tag | `/*/*/RcTagBackingTests/*` | S5 witnesses | all named methods, no failures/skips | 3 | 15 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-irq-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | policy, categories, layering, spawn census | all named classes, no failures/skips | 79 | 5 |
| CP-91 | all | CP-90 | linux-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | derived-index drift | all methods, no failures/skips | 27 | 5 |

Lane: all rows Linux/portable on server2 (read `GET /api/runner-defaults` and `GET /api/session-runners` at dispatch; global default was `server2` with one Windows and one Linux runner listed; omit `-Runner` and `-Platform`). Suffix check at plan source: none of `RcCarryReplayTests`, `RcSelectorSoundnessReplayTests`, `RcInputOwnersMapTests`, `RcClassCensusTests`, `RcTagBackingTests` exists yet; `RcInputManifestTests` and `RcChunkInputDigestTests` have no longer-named siblings. TestDesign must recheck at its tip.

No session that waits for input is added or changed; the CARD-1083 release checklist does not apply.
