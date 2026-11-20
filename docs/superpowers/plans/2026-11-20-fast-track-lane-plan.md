# Fast-track lane for small changes (plan)

Task 73d0252f (Plan, Opus), base master `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e`.
No card is bound in the brief; the orchestrator files or binds one before the first Code slice.

Operator direction (2026-11-20), as relayed: "[the fast bracket] let us make sure we have some sort
of path for fast tracking changes, and this should be an accepted path where it goes through the
whole thing a lot quicker, so it does not need to build and run as many tests."

**Interpretation of the marker.** The message was garbled. This plan reads "the fast bracket" as a
literal title bracket, `[fast]`, at the start of a task title. A card label `fast` carries the same
request, and `delegate.ps1 -Fast` sets both. It does not mean `/fast`, because Claude Code already
has a `/fast` switch with an unrelated meaning (faster model output). The marker is a **request**.
A mechanical classifier decides the lane from the changed paths, and a mislabelled `[fast]` falls
back to the normal lane (D-1).

## Summary

1. **Measured cost (section 1).** Between 2026-11-19 00:00Z and 2026-11-20 08:42Z, 49 lands
   reached master. Each land took 490 to 1732 s from start to remote confirmation (median 680 s),
   which is 9.9 h of serial land-lane time in 32.7 h. Waits in the queue reached 31 min. The
   five docs-only lands took 490 to 798 s each, even though a docs-only build skip already exists.
2. **Why the docs-only skip never fires (G1).** `LandVerificationPolicy` skips only when the
   candidate's build graph matches the manifest embedded in the **running server**. Product hosts
   run the rc tip, so that manifest belongs to the rc. On master the build graph changes most days,
   because test csproj files keep explicit `Compile` lists. Today the rc manifest is `C5B7C0CE…`
   and the master graph is `EA552D85…`. Every docs-only land therefore takes the
   `unrecognized-build-graph` path and builds.
3. **The biggest single cost is replay tasks, not lands.** In the same window there were 21 replay
   tasks, mostly Code: 16.7 seat-hours and about $400. Ten were titled "(index regen)" and three
   "(pin reseal)". `tests/derived-impact-index.json` changed in 24 of the 49 lands. Regenerating that
   file costs about 38 s on server2 (measured below). A replay task costs about 43 min (median).
4. **The design.**
   - A mechanical classifier sorts every changed path into FAST-DOC, FAST-DOC-CONTRACT, FAST-TEST or
     NORMAL. Any NORMAL path makes the whole change NORMAL.
   - Each fast class drops named rows from Code, Review and land (section 3).
   - Two server changes remove most of the time:
     - (a) the docs skip trusts the target's own committed build-graph manifest when that manifest
       is current (FT-2);
     - (b) the land regenerates `tests/derived-impact-index.json` itself instead of refusing on a
       conflict in it (FT-5).
5. **The interim procedure (section 6)** can start today with no code. It covers docs and test-only
   tasks with no Opus Review, and one index regeneration per landing wave instead of one replay per
   branch.

## Ground truth: what the brief assumes vs what the code does

| # | Brief / common belief | What the repo and server show |
|---|---|---|
| G1 | "Each land runs a build verification; docs-only needs none: find whether a path-based skip already exists." | The skip already exists. `server/Application/Services/LandVerificationPolicy.cs:11-33`: it applies when `forceBuild` is off, there is no `-Verify` filter, the target is `refs/heads/master`, and the raw diff holds only regular `100644` files under `docs/**.md` or `README.md` (`:35-65`). It also needs `MatchesAuditedGraph` (`:71-95`), which compares the candidate tree's build-graph fingerprint against `LandVerificationBuildGraph.Fingerprint`. That value is the manifest **embedded in the running server assembly** (`LandVerificationBuildGraph.cs:14,22-28`; `server/Antiphon.Server.csproj:38-40`). The running server is rc tip `26a99f1a7` (`GET /api/version`), and its manifest is `C5B7C0CEC63A2411…/80`. Master `origin/master` (`f66039b1d`) has the graph fingerprint `EA552D856F649EED…/80`. The fingerprints were computed here with the production selector and hash, in the scratch script `fp.js`. So every docs-only land onto master builds today, with reason `unrecognized-build-graph`. The base commit `8a89aa0e5` even carried a stale committed manifest: `DA2C126C…` against its tree's `EA552D85…`. |
| G2 | "The land 'build' phase takes 450–600 s." | The server task API exposes no per-phase times for finished lands. `LandRequestStatusDto` shows only the current phase (`server/Application/Dtos/LandRequestStatusDto.cs:83-84`), and the `build` phase starts at `VerificationStartedAt`, so it includes the build-slot wait. Whole-operation times (`landRequest.startedAt` → `landing.remoteConfirmedAt`) are in section 1. The phase split and the logged decision reason (`"Landing verification decision … reason {Reason}"`, `AgentTaskLandingProtocol.cs:338-340`) are in the desktop server log, which this runner cannot read: `scripts/logs.ps1 -Source desktop-server` fails with "No log files found". **UNKNOWN; FT-0 measures it.** |
| G3 | "Each land does a cold build." | It is warm by default. `Landing:ReuseVerificationArtifacts` defaults to true (`LandingSettings.cs:10`). The verifier keeps a per-target generation under `<git-common>/antiphon/land-verify/v1/<toolchain-key>/` (`LandingVerifier.cs:30-45`; `docs/orchestration-loop.md:2427-2440`). Even so, docs-only lands took 490–798 s. The rest of the time goes to slot wait, a no-op solution build, fetch/rebase/pin and push/confirm, in proportions that are UNKNOWN until FT-0. |
| G4 | "The land build verification could be narrowed to the touched project." | `VerifyUnderSlotAsync` runs `dotnet build --artifacts-path <gen> --configuration Debug` at the land-worktree root, which builds the whole solution. With a `-Verify` filter it then runs `tests/Antiphon.Tests` (`AgentTaskLandService.cs:2012-2019`). Narrowing helps little for test-only changes. `tests/Antiphon.Tests` is the largest project, it references the server, and its files are Compile-linked into five mirror projects (`docs/orchestration-loop.md:683-714`, CARD-1996). A test-only land therefore still builds those mirrors. This plan keeps the land build for FAST-TEST (D-6). |
| G5 | "A land that conflicts on a generated file needs a replay task." | That is true today. A rebase conflict aborts and refuses `rebase_conflict` with the file list (`AgentTaskLandingProtocol.cs:281-305`), and the branch rule (fast-forward only, no rebase) forces a new replay task. The index is a pure function of tracked C# ("parses tracked C# with no compilation", `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs:15-18`), so it can be regenerated mechanically on the rebased tree. |
| G6 | "Regenerating the index is expensive." | `scripts/regenerate-derived-impact-index.ps1` measured on server2 at this base: 38 s wall time leased (driver build 2.3 s warm, run about 36 s), and again 38.6 s with `-NoBuild`. A cold driver build was not measured (UNKNOWN; the project compiles three linked files). DerivedImpactIndexTests costs about 3 min, which is the caller's figure, not measured here. |
| G7 | "Docs are safe to skip tests for." | Only some docs are. The derived index has 42 `docs/` reader edges. Seven `.md` paths are **capped** (more than 25 reader classes): `AGENTS.md` (221), `CLAUDE.md` (194), `README.md` (127), `docs/orchestration-loop.md` (191), `docs/ops-http.md` (181), `docs/agent-card-lifecycle.md` (178) and `.claude/skills/antiphon-delegate/SKILL.md` (183). A literal census of `tests/**/*.cs` and `scripts/**/*.ps1` finds 205 distinct `"docs/…"` literals. Tests enumerate `docs/superpowers/plans/` (for example `PlanCoverageBindingTests`, `TaskPlatformGuidanceTests`) and `docs/investigations/` (`WhyMissedWriteUpContractTests`). Only `docs/reviews/**` and `docs/superpowers/reviews/**` are census-proven no-ops (`scripts/lib/rc-input-noop.json`). `docs/investigations/evidence/card-0462/Reproduce.csproj` is a build-graph file under `docs/`. |
| G8 | "Review is required before a land." | Review is a convention. The server takes a plain `-Land` with no review evidence, gated only by the build (`docs/orchestration-loop.md:2463-2468`). There are precedents for no Review: "No Review for fixture-only changes (operator)" (`docs/orchestration-loop.md:836`) and minimal testing (standing rule 8, `:675`). |
| G9 | "The rc / tag still covers everything." | Yes. A tag needs `full`, an admitted all-executed Windows Full on the tagged SHA (`docs/release-gates.md:656`). The rc cut runs the Merge fixed subset (`docs/orchestration-loop.md:747-760`). The Linux post-land early warning (`scripts/post-land-early-warning.ps1`, CARD-1784) runs shards after a land and skips docs-only batches (`docs/testing-and-build.md:2025-2028`). |
| G10 | "The change-impact classifier can decide the lane." | Not yet. The change-impact project (`docs/superpowers/plans/2026-11-20-change-impact-project-plan.md:105-135`, D-6..D-8) defines `ForcedFull/Owned/TestOnly/NoOp/Unowned` for **rc selection**, and it is still a plan (CI-1..CI-10). The fast-lane classifier here is a smaller, independent allow-list, compiled into the server like `DocumentationPath`. It maps onto those classes (D-3) so they can merge later. |

## Decisions

- **D-1 The marker requests; the classifier decides.**
  - `[fast]` in a title, the card label `fast`, or `delegate.ps1 -Fast` asks for the fast lane.
  - The lane is the output of the classifier over `git diff-tree -r -z --raw --find-renames <target> <candidate>`, the same input `LandVerificationPolicy` already reads.
  - A request whose computed lane is NORMAL is refused at land with `fast_lane_ineligible`, naming the offending paths. The orchestrator then redispatches the normal Review.
  - Rejected:
    - an operator-judged label (the operator asked for a mechanical rule);
    - trusting the title alone (a mislabel would skip Review on product code).
- **D-2 Four lanes, worst path wins.** Every path gets one class, and the change takes the most expensive class among its paths: NORMAL > FAST-TEST > FAST-DOC-CONTRACT > FAST-DOC. A mixed change is NORMAL when any one path is NORMAL. Status `A/M/D/R` on regular `100644` files only. A rename is classified on both names. Mode changes, symlinks, gitlinks and executable bits are NORMAL, as in `LandVerificationPolicy.cs:53-59`.
- **D-3 Mapping to change-impact classes.** FAST-DOC ⊂ NoOp (proven or proposed). FAST-DOC-CONTRACT and FAST-TEST ⊂ TestOnly (D-7 of the change-impact plan). NORMAL ⊇ ForcedFull ∪ Owned ∪ Unowned. When CI-10 ships, the fast classifier becomes a policy on top of `PathClass`. Until then the two stay separate, and neither selects rc work from the other.
- **D-4 The docs build skip uses a current, committed target manifest (FT-2).** Besides the embedded manifest, `MatchesAuditedGraph` also accepts the manifest committed in the **target** tree when all of these hold:
  - (a) the blob parses under the existing `ParseManifest` rules;
  - (b) its digest and count equal the server-computed fingerprint of the target tree;
  - (c) the candidate fingerprint equals the target fingerprint, which is always true for a FAST-DOC diff.

  A stale or hand-broken target manifest fails (b), so the land builds, which is today's fail-closed behaviour. Rejected:
  - keep embedded-only (status quo; the skip almost never fires, G1);
  - an "audited digest ledger" in the database (more state, the same trust as a reviewed committed manifest);
  - dropping the graph check (CARD-1380 D-3 threat: a build file that embeds `docs/*.md`).
- **D-5 Generated files are reconciled at land, not by a replay task (FT-5).**
  - A server-side registry names generated paths, each with a regenerate command. v1 holds only `tests/derived-impact-index.json`.
  - When every conflicted path of a rebase step is in the registry, the land takes the target's version and continues.
  - After a successful rebase, if the candidate changes any of the builder's inputs (tracked `*.cs` under `tests/` or `scripts/`, or any `*.csproj`), the land runs the regenerator under the land build slot. If the bytes differ, it adds one commit, `chore(land): regenerate tests/derived-impact-index.json`, before verification.
  - A regenerator failure refuses `generated_artifact_failed` with no publication.
  - Rejected: (B) un-commit the index and generate it on demand per selector call. This removes conflicts with no server change, but every consumer (impacted-classes, Merge subset, rc selection) pays about 40 s and needs a cache; it stays O-3 option b. Rejected: (C) a `merge=ours` driver alone, which leaves master's index stale and DerivedImpactIndexTests red until someone regenerates.
- **D-6 The land build stays for FAST-TEST.** A test-only change can fail to compile, and its files may be Compile-linked into mirrors (G4). The fast lane saves the Code and Review time and the replay, not the land build. Rejected: building only the touched project (unsafe for linked files; small saving, G4).
- **D-7 Review per lane.**
  - FAST-DOC: no LLM Review. An automated check runs: classifier, `check-evidence-diff`, merge-tree, and `checkpoint import --plan` when a `### Checkpoints` table changed.
  - FAST-DOC-CONTRACT: the same, plus the reader-row receipt from Code.
  - FAST-TEST: a short diff-only Review on Sonnet, with no checkpoint rerun and a 15 min budget, focused on "can this test fail". Repairs of existing failing tests follow the existing fixture-only precedent, no Review (G8).
  - O-4 carries the alternatives.
- **D-8 No database migration.**
  - The lane is recorded in strings that already exist: the `LandRequested` event detail (`… lane=fast class=FastDoc`), the landing `VerificationCommand` (`Skipped(non-code); … graph=<digest>; trust=target-manifest`), the task title bracket and the card label (`Card.LabelsJson`, `card.ps1 -Labels`).
  - The `LandAgentTaskRequest` DTO gets one optional field, `lane`. It is a public DTO, so the change is listed under Existing tests affected.
  - Rejected: an `AgentTask.Lane` column (a migration for a convention that may change).
- **D-9 Capped docs are FAST-DOC-CONTRACT with deferral (default; O-2).** A capped doc runs the index's first 25 reader classes (the `first` list in `cappedPaths`). The owed rest is deferred to the post-land early warning and the rc run. This matches the operator's accepted test-only deferral.

## 1. Measurement (what a small change costs today)

**Sources.**
- `GET /api/agent-tasks?since=2026-11-19T00:00:00Z&status=Succeeded`: 156 rows.
- `GET /api/agent-tasks/{id}` for the 89 non-Review/Plan/Investigate rows: `landing` (`remoteConfirmedAt`, `verifiedSha`) and `landRequest` (`requestedAt`, `startedAt`).
- Each land range is the previous land's `verifiedSha`..this `verifiedSha` on linear master. Out-of-band pushes inside a range are counted with that land.
- Scratch scripts and raw JSON are kept outside the repo. They are not committed, because they are generated evidence.

**Land operation (start → remote confirmed), by change class of the landed range:**

| Class of landed range | Lands | Median s | Min–max s | Median queue wait | Median task (dispatch→complete) | Replays | Touch index |
|---|---:|---:|---:|---:|---:|---:|---:|
| docs-only (`docs/**.md`) | 5 | 521 | 490–798 | 2 min | 18 min | 0 | 0 |
| md-only incl. `AGENTS.md` | 1 | 552 | 552 | 16 min | 17 min | 0 | 0 |
| test-only (+docs, +index) | 3 | 646 | 577–705 | 1 min | 58 min | 1 | 3 |
| scripts (+docs) | 8 | 665 | 560–759 | 2 min | 50 min | 1 | 0 |
| mixed non-product (scripts, tests, other) | 18 | 745 | 510–1732 | 2 min | 44 min | 9 | 10 |
| normal (server/src/client/build/tests-shared) | 13 | 771 | 527–1157 | 2 min | 61 min | 4 | 11 |
| unknown (range not linear) | 1 | 592 | — | 3 min | 46 min | 1 | 0 |
| **All** | **49** | **680** (p90 978) | 490–1732 | — | — | 16 | 24 |

- Serial land-lane occupancy: 9.9 h in 32.7 h (30%). Land requests are serialized behind one repository lease. Bunched requests waited 14–31 min (`LandAged` detail `reason=repository_lease_yielded_to_dispatch; queue position=3`, task `7cf66026…`).
- Per-role task durations in the window (all succeeded rows), median / p90:
  - Code: 61 / 98 min (n=71, total $2450);
  - Review: 26 / 39 min (n=52, total $259);
  - Docs: 31 min (n=4);
  - Debug: 18 min (n=11).
- Replay tasks (title contains "replay"): 21 (19 Code, 1 Debug, 1 Review). Median 43 min, sum 16.7 seat-hours, about $400. Ten say "(index regen)" and three say "(pin reseal)". Examples: `2af26f95` (102 min), `094c50ce` (77 min), `6ec9db38` (68 min).
- The pins in "(pin reseal)" are the live-lane `-PrintInputDigest` five-path pin in `scripts/test-live-lane-run.ps1` (touched by `c2d4f69e`, `5782c600` and `b81aa118`). They move when `scripts/lib` nightly files change, which is NORMAL by D-2, so they are out of the fast lane's scope (FT-9 is the later generalization).

**What could not be established here (UNKNOWN):**
- the per-phase split of a land (slot wait vs build vs git vs push), because the desktop log is unreachable from server2;
- the policy decision reason per land, which is inferred from fingerprints (G1), not read from the log;
- the Code-stage checkpoint cost per small class. Checkpoint receipts are gitignored evidence on the task mirrors. The caller's figures are a cold build of about 6 min and DerivedImpactIndexTests of about 3 min;
- the Review cost per class (Review rows are not tied to a diff class in the list API).

FT-0 measures the first two.

## 2. Eligibility (mechanical)

The classifier is a pure function: `Classify(IReadOnlyList<RawDiffEntry>, IReaderLookup) -> (Lane, IReadOnlyList<PathVerdict>)`. Patterns are ordinal and case-sensitive, matched after the existing `SafePath` check (`LandVerificationPolicy.cs:67-69`). Rules apply in order, and the first match wins for a path.

**NORMAL (never fast). Any one of these makes the change NORMAL.**

| Glob / condition | Safety reason |
|---|---|
| any status other than `A/M/D/R`, any mode other than `100644`/`000000` per side | type, exec-bit, symlink and gitlink changes alter build or runtime (`LandVerificationPolicy.cs:53-59`) |
| `server/**`, `src/**`, `client/**`, `tools/**` | product and tool code |
| `scripts/**` (includes `scripts/lib/**` nightly, `scripts/regenerate-derived-impact-index/**`) | scripts run on the landing, nightly, rc and deploy paths, and some carry pins |
| `docker/**`, `deploy/**`, `**/Dockerfile*`, `docker-compose*.yml` | images and stack |
| `**/*.csproj`, `**/*.*proj`, `**/*.props`, `**/*.targets`, `**/*.sln*`, `**/*.rsp`, `**/Directory.Build.*`, `global.json`, `nuget.config`, `**/packages.lock.json`, `**/*.globalconfig`, `.editorconfig` | the build graph (`LandVerificationBuildGraph.Includes`), plus analyzer config |
| `**/Migrations/**`, `**/AppDbContext.cs`, `**/*Model.cs` | migrations (Merge migrations row) |
| `tests/Shared/**`, `tests/**/TestHelpers/**`, any `tests/**` file named in another project's `<Compile Include>` | shared and linked test infrastructure (CARD-1996; change-impact D-7) |
| `tests/test-execution-policy.json`, `tests/linux-test-roster.json`, `tests/Antiphon.Tests/slow-tests-allowlist.txt`, `tests/**/*.json` other than the generated index | policy, roster and pin inputs (Merge verification guard union) |
| `tests/Shared/TestClassificationGuardTests.cs`, `tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs`, `tests/Shared/NativeLinuxPolicyWitness.cs`, `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs` | classification and selector guards (`docs/orchestration-loop.md:700-707`) |
| `tests/Antiphon.E2E/**` | boots real Program and needs Chromium, which server2 lacks; its narrow row cannot run on the fast lane's host |
| `tests/**` non-`.cs` files (fixtures, data) | fixtures can be read by scripts or product code; no reader census exists yet |
| `.claude/**`, `server/Bundles/**`, `AGENTS.md`, `CLAUDE.md` | agent instructions served at runtime and pinned by bundle tests; also outside the land docs skip (`DocumentationPath`) |
| `docs/cards/**` | generated from the board (CARD-0004); a hand edit is wrong in any lane |
| `docs/**` non-`.md` (for example `docs/investigations/evidence/card-0462/Reproduce.csproj`, `docs/schemas/**`) | build-graph files and test-read data under `docs/` (G7) |
| `.gitattributes`, `.gitignore`, `antiphon.areas.json`, `.github/**`, `**/appsettings*.json` | repository, scope or runtime configuration |
| any path matching a secret-handling name: `**/*secret*`, `**/*credential*`, `**/*.pem`, `**/*.key`, `**/*.pfx` | secrets custody (`docs/agent-credentials.md`); never fast, even in docs |
| any path no rule below names | default-deny (the change-impact "Unowned" rule, mapped to NORMAL) |

**FAST-TEST**
- Pattern: `tests/<Project>/**/*.cs` with `<Project>` one of the test projects except `Antiphon.E2E`, and not excluded above.
- What the fast lane does: Code builds the touched test project once and runs the touched classes (one red witness plus one green, standing rule 8). The land still builds (D-6). The generated index is reconciled at land (D-5).

**FAST-DOC-CONTRACT**
- Pattern: `README.md`, or `docs/**/*.md` that the reader lookup says is read. "Read" means one of:
  - a path edge in `tests/derived-impact-index.json`;
  - an entry in `cappedPaths`;
  - a path under an enumerated directory: `docs/superpowers/plans/**`, `docs/investigations/**`, `docs/superpowers/mutations/**`.
- What the fast lane does: Code runs one row, the reader classes from `scripts/impacted-classes.ps1 -Range <base>..<tip>`, plus the fixed enumerator classes for the enumerated directories (`PlanCoverageBindingTests`, `TaskPlatformGuidanceTests`, `WhyMissedWriteUpContractTests`; TestDesign confirms the set). A capped path runs its 25 `first` classes, and the rest is deferred (D-9). The land skips the build (D-4).

**FAST-DOC**
- Pattern: `docs/reviews/**/*.md`, `docs/superpowers/reviews/**/*.md`, and any `docs/**/*.md` with no reader under the rule above.
- What the fast lane does: no test rows and no build. The land skips the build (D-4).

`tests/derived-impact-index.json` is GENERATED. It never makes a change NORMAL on its own, and the land recomputes it (D-5). Until FT-5 ships it counts as FAST-TEST.

**Recompute anywhere.** The classifier source, `server/Application/Services/FastLaneClassifier.cs`, is compiled into the server and Compile-linked into a small CLI, `tools/Antiphon.FastLane`, the same pattern as the regenerator driver. A reviewer, the orchestrator or a Merge runs `dotnet run --project tools/Antiphon.FastLane -- classify --base <sha> --head <sha> --json`. It prints the lane and one verdict per path, and exits 0 for fast or 3 for NORMAL. The land computes the same function on the rebased range, so a branch cannot be classified differently by the tool and by the server.

## 3. What the fast lane skips or shrinks, row by row

| Step | Today | FAST-DOC | FAST-DOC-CONTRACT | FAST-TEST | Risk accepted |
|---|---|---|---|---|---|
| Code checkpoint list | cold build (about 6 min) + plan rows + guard batch + DerivedImpactIndexTests (about 3 min) | none; no build | build `tests/Antiphon.Tests` + one reader row | build the touched test project + touched classes; no guard batch unless a guard file changed (it cannot: that is NORMAL) | a doc reader outside the index is missed (found by post-land or rc) |
| DerivedImpactIndexTests in Code | each branch regenerates and commits the index, so lands conflict | not needed: index inputs are C#/csproj only (G5) | not needed | not run in Code; the index is regenerated at land (FT-5), and in the interim once per wave (section 6) | master's index is stale between lands and the wave regen (interim only) |
| Review | Opus, full rows rerun (median 26 min) | automated check only (D-7) | automated check + reader receipt | Sonnet diff-only, no rerun, 15 min; none for repairs of failing tests | a weak test lands (operator-accepted test-only deferral) |
| Land build verification | `dotnet build` of the solution, warm generation (490–1732 s per op) | skipped once FT-2 lands (`non-code`, trust `target-manifest`) | skipped once FT-2 lands | kept (D-6) | none beyond today's docs skip (D-4 keeps the graph check) |
| Replay on conflict | a new Code task per conflict (median 43 min) | none (no generated inputs) | none | land-time regen (FT-5); interim: Merge-batch or wave regen | a regen bug lands a wrong index; DerivedImpactIndexTests still byte-compares at the next Merge |
| Merge verification | short (`docs/orchestration-loop.md:683-714`) | unchanged | unchanged | unchanged | — |
| rc inclusion | next cut takes master | unchanged; FAST-DOC is a NoOp candidate for CARD-1836 selection, but this plan does not change rc selection | unchanged (TestOnly, deferred) | unchanged (TestOnly, deferred) | — |
| Tag | needs an all-executed Windows Full on the SHA | unchanged | unchanged | unchanged | — |

## 4. Safety net

- **The classifier is mechanical.**
  - It is default-deny: an unknown path is NORMAL.
  - The server recomputes it at land (D-1).
  - Its rules are a table with one witness test per row (FT-3), so a new glob cannot slip in without a test.
- **Cap (O-5).** At most 8 fast lands between two completed post-land early-warning runs. The orchestrator counts this in the interim. From FT-4 the server refuses `fast_lane_cap` (setting `Landing:FastLane:MaxBetweenWarnings`, default 8, 0 disables the lane).
- **Audit.** Every fast land records its lane and class in the `LandRequested` detail, and its skip in `VerificationCommand`. A weekly read of the `Landed` events with `lane=fast` lists them, and the post-land miss retrospective (CARD-0811) treats a fast land like any other.
- **Coverage still runs everywhere else.** The post-land early warning (Linux), the nightly backstop, the rc qualification run and the tag's all-executed Full are unchanged and cover every fast land.
- **Demotion.**
  - Anyone (orchestrator, Review, operator) can drop the marker. The change then takes the normal lane with no penalty.
  - `Landing:FastLane:Enabled=false` turns the lane off server-side.
  - A fast land that turns master red suspends the fast lane until its Debug task lands. This is an orchestrator rule, written in FT-7.
- **What could go wrong, and the answer:**
  - A FAST-TEST change makes a test wrong or vacuous. It is found late, by the post-land warning, the rc or mutation. The operator accepted test-only deferral.
  - A doc-contract test breaks from a doc edit. Readers outside the index are found by post-land or rc, and the capped-doc deferral is explicit.
  - The land regen commit is wrong. DerivedImpactIndexTests (full-byte compare) in the next Merge and in post-land catches it, and the regen is deterministic.
  - A committed manifest is hand-edited to match a graph that embeds docs. That needs a NORMAL land (it changes `server/**`), which builds and is reviewed. The `-Check` registry test `C1385_RegistryMatchesNarrowGraph` still guards the manifest.

## 5. Selection and surfacing

| Where | What | Enforced by |
|---|---|---|
| Card | label `fast` (`card.ps1 update -Labels fast`) | convention |
| Task title | `[fast] ` prefix | convention; FT-6 `delegate.ps1 -Fast` adds it |
| Task brief | `delegate.ps1 -Fast` appends the fast brief block for the computed lane (rows, no guard batch, no index regen, `next: land` or `next: review` per D-7) | script (FT-6) |
| Land request | `delegate.ps1 -Land … -Fast` sends `lane: "fast"`; the server classifies and refuses `fast_lane_ineligible` or `fast_lane_cap` | server (FT-4) |
| Land receipt | `LandRequested` detail `lane=fast class=<FastDoc\|FastDocContract\|FastTest>`; `VerificationCommand` skip trust | server (FT-2, FT-4) |
| Docs build skip | applies to any docs-only diff whether or not it is marked | server (FT-2) |
| Index regen at land | applies to any land whose conflicts are only registry paths, marked or not | server (FT-5) |
| Review choice | orchestrator dispatches per D-7 from the classifier output, never from the title | convention, owner `docs/orchestration-loop.md` (FT-7) |

## 6. Interim procedure (today, no code)

| # | Rule | Risk |
|---|---|---|
| I-1 | Before dispatching, classify with `git diff --name-only origin/master...<branch>` against section 2. Any NORMAL path means the normal pipeline. The orchestrator writes the lane in the brief and puts `[fast]` in the title. | a human misreads a glob (the Review of the first FT-3 run cross-checks interim classifications) |
| I-2 | FAST-DOC: dispatch a Docs task with no checkpoint rows and no build. It runs `check-evidence-diff.ps1`, `git merge-tree --write-tree origin/master HEAD`, and `checkpoint import --plan` when a checkpoint table changed, then reports `next: land`. No Review. Plain `-Land`. | the land still builds for 8–13 min until FT-2 (G1); no new risk |
| I-3 | FAST-DOC-CONTRACT: as I-2, plus one row with the reader classes from `scripts/impacted-classes.ps1 -Range <base>..<tip>` (one build of `tests/Antiphon.Tests`). | readers not in the index are missed until post-land or rc |
| I-4 | FAST-TEST: Code brief "minimal testing: build the touched test project, run the touched classes; do NOT run `regenerate-derived-impact-index.ps1`, do NOT commit `tests/derived-impact-index.json`, do NOT run the guard batch or DerivedImpactIndexTests". Review: Sonnet diff-only, 15 min, no rerun; none for repairs of failing tests. | the index on master is stale until I-5; a weak test lands |
| I-5 | Index wave regen: after each landing wave, and always before an rc cut or a Merge batch, dispatch one small Code task. It runs the regenerator and DerivedImpactIndexTests and lands the index alone. It replaces one replay per conflicting branch. | between waves DerivedImpactIndexTests is red on master (declare it inherited in the Merge and rc briefs), and `impacted-classes.ps1` misses new test classes until regen |
| I-6 | A branch that already committed the index and now conflicts goes to a Merge batch with its peers, which regenerates once, instead of a per-branch replay. | Merge batch latency |
| I-7 | Cap: at most 8 fast lands between post-land early-warning runs. Any red on master from a fast land stops fast lands until its Debug lands. | — |
| I-8 | Never fast: anything in the NORMAL table, including `AGENTS.md`, `.claude/**`, `scripts/**`, and a pin or manifest reseal. | — |

## 7. Slices

Platform: `GET /api/runner-defaults` revision 2 names `globalRunnerId: server2` with empty kind defaults, and `GET /api/session-runners` returned three rows: one Windows, one Linux, and one with no platform field. Slices omit `-Runner` and `-Platform` unless they say otherwise.

| Slice | Hours | Touches | Files | Tests (red first) | Lane / host |
|---|---|---|---|---|---|
| **FT-0 measure** | 1 | nothing (read-only) | report `docs/investigations/2026-11-2x-land-phase-split.md` | none (no-test: measurement) | desktop Investigate `-ReadOnly -Platform Windows`: the server log lives there. For the 49 lands of section 1, read `Landing verification decision … reason`, `land_artifacts` lines, `BUILD SLOT` waits and the verifier spans; confirm the G1 reason and the slot/build/git/push split |
| **FT-1 interim rules** (start now) | 1 | docs | `docs/orchestration-loop.md` (new "Fast lane (2026-11-20)" subsection with section 6 and the brief blocks) | doc-contract readers of `orchestration-loop.md` (capped, D-9: first 25) | server2 Docs. Not fast itself: `orchestration-loop.md` is capped; by D-9 it would be FAST-DOC-CONTRACT once the lane exists |
| **FT-2 target-manifest trust** | 2–3 | server | `server/Application/Services/LandVerificationPolicy.cs` (read `git show <target>:server/Application/Services/LandVerificationBuildGraph.manifest.txt`, D-4), `LandVerificationBuildGraph.cs` (expose `ParseManifest` for text), `AgentTaskLandingProtocol.cs:336` (record `trust=`) | `LandNonCodeSkipTests`: new `C_FT2_CurrentTargetManifestSkipsWhenEmbeddedDiffers`, `C_FT2_StaleTargetManifestBuilds`, `C_FT2_MalformedTargetManifestBuilds`, `C_FT2_CandidateGraphDiffersFromTargetBuilds`; update `C1380_TargetGraphDriftAndNewProjectsBuild` to the stale-manifest case | server2 Code |
| **FT-3 classifier + CLI** | 2–3 | server + tool | `server/Application/Services/FastLaneClassifier.cs`, `tools/Antiphon.FastLane/*` (links the classifier), `Antiphon.sln`; then `audit-land-verification-graph.ps1 -Update` (new csproj = graph change; NORMAL lane) | new `FastLaneClassifierTests`: one witness per section 2 row, plus mixed → NORMAL, rename both sides, mode change, symlink, capped doc, unknown path → NORMAL, index-only → FAST-TEST | server2 Code |
| **FT-4 land lane request** | 2 | server + script | `server/Application/Dtos/AgentTaskDtos.cs:700` (`Lane`), `AgentTaskLandService.cs` (classify the rebased range, refuse `fast_lane_ineligible` / `fast_lane_cap`, detail), `LandingSettings.cs` (`FastLane:Enabled`, `MaxBetweenWarnings`), `scripts/delegate.ps1` (`-Fast` on `-Land`), `docs/ops-http.md`, `docs/antiphon-api.md` | `AgentTaskLandRequestTests`: new `C_FT4_FastLaneRefusesNormalPaths`, `C_FT4_FastLaneRecordsClass`, `C_FT4_FastLaneCapRefuses`; `ContractSnapshotTests` (E2E) if the DTO is snapshotted (TestDesign checks) | server2 Code (E2E row goes to the desktop lane, since server2 has no Chromium) |
| **FT-5 generated-file reconciliation** | 3 | server | `AgentTaskLandingProtocol.cs:281-305` (registry-only conflict → take target, continue; post-rebase regen and commit), new `LandGeneratedArtifacts.cs` (registry), `AgentTaskLandService.cs` (run the driver under the held slot) | new `LandGeneratedArtifactTests`: two branches both change the index, the second land regenerates and lands; mixed conflict still `rebase_conflict`; driver failure `generated_artifact_failed` with no push; regenerated bytes equal `DerivedImpactIndexBuilder` output. Existing: `AgentTaskLandTargetRaceTests`, `AgentTaskLandRequestTests`, `LandMergeBranchWitnessTests` keep `rebase_conflict` for non-registry files | server2 Code |
| **FT-6 dispatch switch** | 1–2 | script | `scripts/delegate.ps1` (`-Fast`: title prefix, brief block by lane via the FT-3 CLI, refuse when the CLI says NORMAL), `.claude/skills/antiphon-delegate/SKILL.md` pointer | delegate script tests (names: TestDesign) | server2 Code (NORMAL lane: `scripts/**`, `.claude/**`) |
| **FT-7 promote to standing rule** | 1 | docs | `docs/orchestration-loop.md` (replace interim with server-backed rules), `docs/testing-and-build.md` recipe pointer | doc-contract readers | server2 Docs |
| FT-8 (optional) | 2 | server + tool | extend `DocumentationPath` to `AGENTS.md`, `CLAUDE.md`, `.claude/skills/**/*.md` **and** the LandGraphAudit refused-include set in the same slice | `LandNonCodeSkipTests`, `LandGraphEvaluation*` | server2 Code |
| FT-9 (optional) | 2–3 | server | add the land-graph manifest (via `scripts/land-graph-land-refresh.ps1`) and the live-lane digest pin to the FT-5 registry | `LandGraphLandRefreshTests`, new registry rows | server2 Code |

Order: FT-1 now, FT-0 in parallel on the desktop, then FT-2 (largest land-lane win, smallest diff), FT-5 (largest seat-time win), FT-3, then FT-4 and FT-6, then FT-7. FT-2 and FT-5 both touch `AgentTaskLandingProtocol.cs`, so they must not run concurrently (same-area rule).

## Existing tests affected (CARD-1782)

Impacted-classes table: run `pwsh -NoProfile -File scripts/impacted-classes.ps1 -MergeSubset -Range <base>..<tip> -Json` per slice. Known up front:

| Slice | Class | Why |
|---|---|---|
| FT-2 | `LandNonCodeSkipTests` (60 tests; `C1380_TargetGraphDriftAndNewProjectsBuild` changes expectation) | policy trust root |
| FT-3 | `LandNonCodeSkipTests/C1385_RegistryMatchesNarrowGraph`, `LandGraphLandRefreshTests` | new csproj moves the graph digest |
| FT-4 | `AgentTaskLandRequestTests`, `AgentTaskLandApprovalRequestTests`, `ContractSnapshotTests` (E2E, public DTO) | public DTO `LandAgentTaskRequest`; new refusal codes |
| FT-5 | `AgentTaskLandTargetRaceTests`, `AgentTaskLandAdoptionTests`, `LandMergeBranchWitnessTests`, `DelegationWorktreeMergeBranchLandTests`, `DerivedImpactIndexTests` | rebase-conflict path; index bytes |
| FT-1/FT-7 | readers of `docs/orchestration-loop.md` (191, capped) | doc contract |

No migration, no operation label, and no remote script function (FT-5 adds land events only as detail strings; if TestDesign finds a new `TestTimingOperationLabel` is needed, it is listed then).

## Performance budget

Standard: `docs/testing-and-build.md#performance-standards`.

- **This plan.** It ran no tests. One measurement: the regenerator took 38 s wall time, run twice on server2 at `8a89aa0e5`, leased with zero wait (build 2.3 s warm, run about 36 s). Per D-5 it runs inside the land's existing build slot. Budget: 90 s (about 2x of 38 s plus a cold driver build, UNKNOWN; FT-5 measures it and resets the timeout to 2x p95).
- **Expected savings per land** (from section 1, before FT-0 refines the split):
  - FAST-DOC/FAST-DOC-CONTRACT land: from 490–798 s to the no-build path (git + push + confirm). That path's time is UNKNOWN until FT-0; estimate under 2 min.
  - Every conflicting land with a generated file: from about 43 min of a replay task to about 1 min inside the land.
- **Slice rows** (TestDesign refines; each row under 10 min, no whole class over 30 min):
  - FT-2: `/*/*/LandNonCodeSkipTests/(C_FT2*)|(C1380_TargetGraphDrift*)` (fixture git repos; the class has 60 tests, duration UNKNOWN, measured in TestDesign).
  - FT-3: `/*/*/(FastLaneClassifierTests*)/*`. Pure function, expected under 10 s; timeout 1 min.
  - FT-4: `/*/*/AgentTaskLandRequestTests/(C_FT4*)`.
  - FT-5: `/*/*/(LandGeneratedArtifactTests*)/*` (fixture repos plus one real driver build; shared driver build once per class, about 40 s); timeout 2x measured p95.
- Setup ownership: the FT-5 fixture builds the regenerator driver once per class run (shared, not per test). The git fixture repos follow the existing `LandNonCodeSkipTests` pattern. No Docker. No AppHost.
- Phase recording: FT-0 records slot wait, build, git and push per land. FT-5 records the regen phase in the land observer lines (`generated_artifact regenerated=<bool> ms=<n>`).
- No duration over 30 minutes is expected; any row over budget gets a Debug task (standing rule 10).

## Checklist: sessions that wait for input

Not applicable. This plan adds or changes no session that waits for input.

## 8. Decisions for the operator

| ID | Question | Options | Recommendation |
|---|---|---|---|
| O-1 | Docs build-skip trust root | a) embedded only (status quo); b) also accept a current committed target manifest (D-4); c) audited-digest ledger in the database | **b**: it makes the existing skip work on master with one server change and keeps fail-closed behaviour |
| O-2 | Capped docs (`orchestration-loop.md`, `ops-http.md`, `agent-card-lifecycle.md`, `README.md`) | a) FAST-DOC-CONTRACT, run the first 25 readers, defer the rest (D-9); b) NORMAL | **a**: these are the most-edited docs; deferral matches the accepted test-only deferral |
| O-3 | Generated index conflicts | a) regenerate at land (D-5, FT-5); b) stop committing the index, generate on demand with a tree-keyed cache; c) wave regen only (interim forever) | **a**, with c as the interim |
| O-4 | Review for fast lanes | a) D-7 (no LLM for docs, Sonnet diff-only for tests); b) Sonnet diff-only for every fast lane; c) no Review for any fast lane | **a** |
| O-5 | Fast-land cap | a) 8 per post-land warning; b) N per day; c) none | **a** |
| O-6 | Marker | a) `[fast]` title + `fast` label + `-Fast` (this plan); b) label only | **a** |
| O-7 | Scripts-only changes (8 lands, 50 min median task) | a) NORMAL (this plan); b) a later FAST-SCRIPT class for `scripts/test-*.ps1` harnesses read only by tests | **a** now; revisit after the change-impact reader census (CI-6) |
| O-8 | `AGENTS.md`, `CLAUDE.md`, `.claude/**` | a) NORMAL (this plan); b) FT-8 extends the docs skip with the audit | **a** now, FT-8 later |

Stated defaults (D-n above) apply until the operator answers. FT-1 and FT-0 do not depend on any answer.

## Next

next: decide. The plan is written under stated defaults D-1..D-9. The operator picks O-1..O-8. FT-1 (Docs, server2) and FT-0 (desktop read-only Investigate) can start now. TestDesign follows for FT-2..FT-6 before Code.
