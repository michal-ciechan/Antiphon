# Is the land graph audit worth keeping?

2026-11-24 UTC · Investigate task `f6386f72-7662-42a0-9315-582b9ab3fb03` · Source pin `2a79f46749de0ecf2c516fd77a1eb3825de25046`

**Mechanism confirmed.** The graph certifies the build-definition tree behind the docs-only master build skip. The audit tests whether those definitions can consume otherwise skippable documentation and whether the recorded approval applies to the tree. A fresh Git fingerprint alone answers neither question. The strongest operational catch was an unreviewed change to two build targets after a clean Review. The recurring cost was re-recording approval on the tree that actually reached master.

The 60-day census found **67 record-only commits on master and 10 additional on rc-only history**. The retained evidence substantiates **four rc-cut episodes** affected, one separate rc top-up held, **five master Merge batches with a subject held**, and **14 with historical audit setup or copy-budget retries**. Eleven other Merge batches reported one inherited mismatch and landed. Recommend B, starting with automatic graph re-recording on the final integrated tree and retaining audit refusals that challenge coverage.

## What land-v2 uses

The API takes an optional `verify` filter. The server rebases reviewed source in its detached land worktree, pins the resulting candidate and calls `LandVerificationPolicy.DecideAsync` against the pinned target and candidate. It logs the reason, fingerprint and trust source for a skip. Otherwise it verifies the prepared worktree and can refuse on build or selected-test failure before publication. It checks the remote target again before push. [API contract](../ops-http.md#L223) · [Prepared pin and verifier decision](../../server/Application/Services/AgentTaskLandingProtocol.cs#L327) · [Publication check](../../server/Application/Services/AgentTaskLandingProtocol.cs#L393)

| Changed path or request | Inventory and actual server verification |
| --- | --- |
| Regular lowercase `docs/**/*.md` and root `README.md` | Eligible for skipping the build and verifier child on master. Both paths of a rename must qualify. Only ordinary non-executable additions, deletions, edits or qualifying renames pass. An empty raw diff can qualify. The candidate must also match an approval. |
| `.csproj` and other `*proj` extensions, `.props`, `.targets`, `.sln/.slnx/.slnf`, `.rsp`, `global.json`, `Directory.Build.*`, `Directory.Packages.props`, `*proj.user`, NuGet config and package lock/config | Included in the repository-wide fingerprint. The diff requires a build even with an approved graph. A later docs-only diff can build if approval is stale. Arbitrarily named `Directory.*` files qualify by the exact names or extensions listed here. |
| Embedded resources | The resource declaration affects the definition fingerprint and builds. A resource file under `server/`, including seed Markdown, builds by path. Its content usually sits outside the definition hash. An item that reads skippable docs or root README is the case the audit must exclude. |
| Scripts, C# source, tests, test fixtures, policy/index JSON, non-Markdown docs, executable files and links | These diffs require a build. Ordinary script or test source bytes sit outside the fingerprint. A project edit linking test source is inside. An unrelated script already on the target does not invalidate a subsequent docs-only skip. |
| Non-master target, forced build, or explicit test filter | Each disables the skip. The verifier runs a Debug `dotnet build` from the prepared repository, ordinarily resolving `Antiphon.sln`. If a filter is supplied, it then runs that TUnit filter in `tests/Antiphon.Tests` with the built artifacts. A blank filter ends after the build. |

[Diff and force/filter/target rules](../../server/Application/Services/LandVerificationPolicy.cs#L11) · [Literal path and mode rules](../../server/Application/Services/LandVerificationPolicy.cs#L56) · [Inventory and SHA-256](../../server/Application/Services/LandVerificationBuildGraph.cs#L41) · [Build and test commands](../../server/Application/Services/AgentTaskLandService.cs#L2013) · [Unrelated target-script witness](../../tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs#L309)

The server already computes the candidate digest from ordinal-sorted, NUL-terminated `git ls-tree -r -z` rows with mode, type, blob ID and path. It first accepts a digest **and count** matching the manifest embedded in the running server. Otherwise it requires the target and candidate graphs to match each other and the manifest read from the **target commit**. A candidate-supplied record cannot confer approval. Missing or malformed approval on both paths and unrecognized graphs cause a build fallback. FT-2 added the target-manifest fallback in `1c2b1600e28c7e5cf6883b3c1759272aaa2111cb`, which matters while the server is running an older rc. The server reads neither the evaluation record nor the pins for a skip. [Runtime trust order](../../server/Application/Services/LandVerificationPolicy.cs#L25) · [Resource embedding](../../server/Antiphon.Server.csproj#L37) · [FT-2 witness](../../tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs#L2770)

**Merge verification is short** is a separate caller workflow. It does one project build, or a solution/mirror closure build for shared helper links, and narrow subject witnesses. It also calls the graph audit, recomputes pins, regenerates the derived index once and runs evidence-diff. Its policy guard union runs for designated changes. The test impact selector and derived index route test classes independently. After the seed fix, a scratch seed-only change selected `ReviewCheckParserTests` via `review-check-seeds` in `tests/impacted-classes.json`. Before that mapping the same change selected no class, and server land would still have built. [Short Merge rules](../orchestration-loop.md#merge-verification-is-short-2026-11-18) · [Seed selector transcript](2026-11-23-why-missed-master-graph-audit-embedded-md.md#L81)

## What the audit adds

1. **A closed definition pre-check.** It validates tracked rows and inspects definitions, imports, properties and file-item patterns. It expands potential items against tracked blobs and checks whether resulting Markdown or text paths are admitted, before comparing the current hash with the manifest. A prohibited item refuses even during an update. Matching a fresh hash to itself would approve the first docs-only change after a definition began consuming docs. [Pre-check order](../../tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs#L447) · [Document patterns](../../tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs#L307)
2. **Real evaluation and exact pins.** Both successful check and update require the real MSBuild evaluator. It checks expressions, evaluates the solution and project-reference closure, rejects skippable evaluated items, and compares imports, properties and item rows against a canary with changed docs and root README absent. It evaluates without executing build targets. Targets and environment-sensitive evaluation elements have exact pins. A normal build can compile a docs consumer successfully without finding that it invalidates future skips. [Evaluation phases](../../tools/Antiphon.LandGraphAudit/SolutionAudit.cs#L31) · [Project evaluation](../../tools/Antiphon.LandGraphAudit/SolutionAudit.cs#L176) · [Target pins](../../tools/Antiphon.LandGraphAudit/TargetPins.cs#L20)
3. **Durable approval.** The manifest records digest and count. The evaluation record binds the digest to the pins blob and a selectable author SDK. Check binds source to HEAD, validates those records, and evaluates on the checking host even when its SDK matches the record. Update writes after approval. A stale digest can short-circuit before real evaluation. Runtime fallback preserves the build, at the cost of lost skips. [Check/update sequencing](../../tools/Antiphon.LandGraphAudit.Core/LandGraphAuditGate.cs#L104) · [Source binding](../../scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs#L329)

### Stored cases

- **Origin and coverage.** CARD-1380 (`44889223-04f4-4f40-97bf-ceedf5e993df`) followed a 42-land queue reported at 6–8 minutes per land. CARD-1385 (`6f0905d5-047f-4406-b1d9-4377da266d67`) narrowed the old 292-input fingerprint after unrelated scripts invalidated skips. CARD-1390 (`e65f05c7-43dd-46cf-be25-a0fb50ee54b1`) recorded that mechanical update had no docs-input approval. CARD-1400 (`ff41096c-8417-4308-9454-9a47db1a440c`) stored case/import/escaping bypasses in temp repositories. CARD-1483 (`c222c993-0e43-4183-b8b6-806622f73ec0`) found emulated metadata approved a graph that MSBuild read as selecting docs or README. Commit `21e102a96f7e890e714b4a689198db92122e7fe0` introduced real evaluation. [Earlier investigation and board rows](https://github.com/michal-ciechan/Antiphon/blob/b028d5e66228f65ec4944cfd75c39a72f7d84c9d/docs/investigations/2026-11-05-investigate-land-graph-gate-value.md#L54)
- **Operational pin catch.** Merge 20 (`89cb5516-572b-4e55-abeb-7e39fffea014`) hit `(changed pin)` for `CopyOwnedPtyHostAppHost` and `CopyOwnedPtyHostAppHostForHerdr` after clean Review. Publication stopped until independent pin review and commit `6d268b2b887dc6311787180e61d0db597f41dc86`, followed by record `907edaebbd301af95629c31095f1b530803baea2`. The block-to-replacement-settlement span was 50m47s, including repair, Review and verification. The solution builds found the underlying CARD-1664 NETSDK1047. [Stored transcript and timestamps](https://github.com/michal-ciechan/Antiphon/blob/b028d5e66228f65ec4944cfd75c39a72f7d84c9d/docs/investigations/2026-11-05-investigate-land-graph-gate-value.md#L156)
- **Buildable but unclassified property.** `b93c459685d670472d17c3ec0941d77c5e2b4f07` added `SatelliteResourceLanguages=en` in a model probe. Its parent matched the manifest, master got the property, and Debug reproduced exit 1. No stored receipt resolves whether publication skipped or bypassed the check. This output-size optimization shows the audit's distinct classification boundary. [Property evidence](2026-11-21-why-missed-satelliteresourcelanguages-graph-refusal.md#L20)
- **Embedded seed Markdown, November 23–24.** `34b0f5c1602e94e50a6a1b5e8b7ab7dbb002f9cc` added the server resource glob via a direct push. At `c04934e50` the pre-check reported `definition file item selects a markdown or text document the audit does not allow`, naming `SEED-0004.md`. The file is outside the docs-only path. `b1f667b80822ddf36b8c41ae4cf5b874903f90a4` admitted precisely the seed pattern. That exposed an independent stale record left by pre-rebase refreshes. `8732a7c6b9510e6f94ef1ef960d749403c76616b` re-recorded master and the full check approved. [Refusal and bypass](2026-11-23-why-missed-master-graph-audit-embedded-md.md#L5) · [Approval receipt](2026-11-23-why-missed-master-graph-audit-embedded-md.md#L93)
- **November 24 recut.** At `3e0cbab19324fb3ca2f50df1b74cb8b957fef06a` pre-check exited 1, `manifest does not match the selected graph`. Three script/test csproj edits moved the digest with the count still 80. The independent derived index also failed byte comparison in CP-1. Unit 20's first graph record `0ea79cf89cd22aa2c4530ff3e5c19f3899539be4` was already stale on master, and `60528ce73fd6284922f455ae22c6ad08eaaca1a0` corrected it. `d512288a68beeca66da21f5a5154fad9f3dd2790` changed only the evaluation SDK on the recut host. Later verification hit a separate slow-test registry guard fixed by `9e9a1cd9df1af097f6da904fc6916e9455b86f1f`. [Cut gate transcript](https://github.com/michal-ciechan/Antiphon/blob/f6a37bc6927d2e25e8df653490acb18a38d8cd64/docs/investigations/2026-11-24-rc-recut-commands-after-port.md#L5) · [Record and registry repair](2026-11-24-why-missed-master-record-stale-again.md#L17)

I independently reconstructed these digest prefixes from stored Git tree rows using the production path predicate, ordinal sort and NUL terminators. Every row has 80 selected definitions.

| Git tree | Recomputed | Committed | Result |
| --- | --- | --- | --- |
| `a3fa41468` | F7476276 | F7476276 | Match before review API definition changes |
| `34b0f5c16` | 11E7E8FB | F7476276 | Seed definition and older record |
| `f3ac671e2` | CD086842 | A95B349D | Integrated refresh stale |
| `3aae33ea8`, `c04934e50` | 39B06B3A | 5E0AD924 | Second integrated refresh stale |
| `8732a7c6b` | 39B06B3A | 39B06B3A | Match after re-record |
| `3e0cbab19` | 2AE7A583 | 39B06B3A | Failed recut pin |
| `0ea79cf89` | D1C23661 | EDB44499 | Unit 20 initial record stale |
| `60528ce73`, `26abbafb6`, `d512288a6`, `2a79f4674` | D1C23661 | D1C23661 | Match including assigned base |

## Cost and provenance

**Git census.** UTC committer dates from September 26 inclusive through November 25 exclusive, 60 calendar days ending November 24. I enumerated nonmerge commits reachable from the source pin and the mirror's 40 `origin/rc/*` refs, de-duplicated by SHA, and checked every candidate's full changed-path set. The 67 master commits are first-parent. Of 75 master commits touching the three records, eight also touched other files. Of 15 rc-exclusive candidates, five also touched other files. Examples include mainline repair `38567feb5c59b0099d10d5f69f60979b0aed9eaf`, the two November 23 pre-rebase refreshes and the November 24 SDK-only re-record. The count covers two initial manifest recordings (`553d9511a6abf18661c88aacdbd25d5fb684ea13` on master and `91b4655ad68015ae0a3ae6c565f6d9888a3ce5b1` on rc) and one deliberate red witness (`d2bf22dc09dfa728bee05be172cfdb797bab1148`). Excluding those yields **65 master + 9 rc-only = 74 later refreshes**. Before the checked-in records, three more master commits updated the old hard-coded fingerprint only (`909591a0eda25c5d75a21290a82384885cff9897`, `638bcac7df623072636bb88d62d6f606f122f214`, `8c70d23802bf989e38ed353adb5116b35ed5158a`). Record writes include automated and manual runs. Cherry-picks with distinct SHAs count as commits. [Pre-manifest evidence](https://github.com/michal-ciechan/Antiphon/blob/b028d5e66228f65ec4944cfd75c39a72f7d84c9d/docs/investigations/2026-11-05-investigate-land-graph-gate-value.md#L108)

| Changed files only | Master | Additional rc |
| --- | ---: | ---: |
| Manifest and evaluation | 53 | 5 |
| Manifest, evaluation and pins | 5 | 0 |
| Manifest only | 4 | 4 |
| Evaluation only | 2 | 0 |
| Pins only | 3 | 1 |
| **Total** | **67** | **10** |

**Publication costs observed.** I read the Antiphon board through the documented HTTP routes to avoid the CLI launcher build. Searches including archived cards returned 25 unique rows over `land-graph`, `land graph`, `graph audit`, `graph-audit`, `SatelliteResourceLanguages`, `embedded seed markdown` and `recut`, with no remaining pages. The 60-day board task list returned 7,150 rows. I fetched 456 unique detail rows, including all 425 Merge rows, 41 cut/recut/top-up title matches (some of them Merge), and named investigations. Three initial 502s resolved on retry. The result field was empty in 21 initial responses and an additional retried row, and the other two retried rows reported green on the graph. The evidence also includes published notes and the prior census, including a recut Verify receipt outside the board result. Counts below are lower bounds from explicit holds, retries or mandatory re-records before cutting. Git commit count and task status alone cannot establish a delay. [Board API](../ops-http.md#L190)

| Observed effect | Count | Receipt coordinates |
| --- | ---: | --- |
| RC cut/recut gate needed a reseal or refusal repair | **4** | `rc/2026-11-03` via `eb43fc0e-fed1-4382-9c64-dde95abb0d81` / `919d8d42f`; `rc/2026-11-06` via `0e8a1b50-9eb9-4149-a16b-441bad36a514` / `5a95ae7a5`; November 23 seed refusal before approval of `6ed4cb58c` via `02f9ccec-4973-47bb-856f-5f0f279e1ec3`; November 24 recut failed at `3e0cbab19` per Verify `81f39228`. Two routine reseals, one construct refusal followed by stale records, and one stale record alongside an independent red. |
| Existing rc top-up withheld | **1** | `b2ae1ae9-1f63-4d19-a213-f062d9a1c6d4` (top-up 38), `_PtyHostNativeProps` refusal. Subsequent rc record `952c22e93d9caa7441c995296c7590f045c621ce`. |
| Master Merge held at least one subject for audit approval | **5** | Batch 20 `89cb5516-572b-4e55-abeb-7e39fffea014` (two target pins), 109 `dc68779f-b547-4e8f-b3e4-5f06772f11eb` (CARD-1734 property), 133 `3646f5a9-76a3-4868-a173-576e5a66e438` (CARD-1790 property), 176 `c9c34d15-28b2-490e-a54b-2dcabc2cec20` (CARD-1829 property), 241 `8f001b6d-378f-44d8-929f-c6b1bdd48e3f` (CARD-1901 property, plus independent roster duplicate). Batches 176 and 241 published other subjects. |
| Additional master Merge retries after the old 5 s copy refusal | **9** | Batches 155 (`17885722`), 170 (`f077518c`), 179 (`1ad1dd2b`), 194 (`22521507`), 213 (`bfd0c681`), 214 (`19b61d3a`), 218 (`2ea19578`), 227 (`fc30e791`), and 259 (`926788ad`). All ultimately approved. CARD-1991 (`d004cc71-cfed-4530-a2f6-410b85815b30`) measured and raised the Linux copy budget. |
| Additional master Merge retries after the evaluator was missing | **5** | Batches 4 (`f08f77e3`), 6 (`cd2caf13`), 14 (`b124e131`), 15 (`5fd6ac3e`), 136 (`6155693e`). Each first check refused before the evaluator was built. The current resolver can build a missing tool automatically. |
| Same inherited mismatch reported despite successful publication | **11** | Batches 54–64 on November 4 after a LibGit2Sharp reference changed the graph. One underlying incident. [Stored batch receipts](https://github.com/michal-ciechan/Antiphon/blob/b028d5e66228f65ec4944cfd75c39a72f7d84c9d/docs/investigations/2026-11-05-investigate-land-graph-gate-value.md#L174) |

One further Windows qualification failure came from a canary omitting git-ignored E2E `TestOutput` files that appeared as SDK `None` items in the working tree. That happened after the cut, outside the publication counts. Current differential tolerance covers only the missing item row representing the ignored file itself. [Windows reconstruction](2026-11-03-c1385-differential-none.md#L7) · [Current differential](../orchestration-loop.md#L2786)

**Leased runtime.** Successful server2 examples include 20 s held with zero queue wait (task `e57194b7`, prebuilt evaluator and a separate 3.14 s build), 36 s held on November 19 (lease `45eb49e2-08cf-41ed-9bb2-af6342ebe515`) and 39 s in cut task `bf04e183-8c2e-4d0d-b17f-e8d45e0f9c25`. A cached successful Merge check was 26 s and a successful updater evaluation was 27 s on November 3. The cited normal server2 examples took roughly **20–40 s per full pass pair**. Four Windows cut checks took 75, 85, 81 and 85 s (tasks `ff89ca56`, `964900d9`, `053aa2ea`, `4b599b91`). These are observed runs. The separate 20-run server2 measurement on November 19 gives copy p50 2.315 s, p95 3.751 s and pass 1 about 10–23 s. A missing/stale evaluator rebuild adds about 10 s per the resolver documentation. Merge 259 spent 11.6 minutes in the graph row with retries, including 6.2 minutes of load waiting and 1.7 minutes of compaction, under the old budget. [20-second receipt](../../.antiphon/task-e57194b7.md#L65) · [36-second receipt](2026-11-19-operator-nightly-perf-commits-review.md#L67) · [Measurement](../orchestration-loop.md#L2762) · [Windows receipt](2026-11-16-rc-cut-2026-11-24.md#L15) · [Retry timing](2026-11-17-merge-259-timing.md#L243) · [Resolver cost](../testing-and-build.md#L1405)

**Manual pattern.** A caller runs a leased check, distinguishes malformed records from property/pin refusals and base drift, finds the matching base, invokes `land-graph-land-refresh` and reports old/new digest, count, pins and changed paths. The helper compares base and HEAD, rejects unrelated pin changes, runs update, commits only the three files and checks again. A successful reseal can pay for two real evaluations, update and post-check, in addition to the first check. CARD-1753 (`284f9e98-b93b-433e-86a9-e737e615a998`) records three former approval requests, two around CARD-1734 and one around load telemetry S1, with hours of idle Merge time per the card. The sanctioned helper abolished routine questions but still runs on the caller's pre-land tree. A rebase can change that tree and a direct push never calls the helper. [Refresh and reporting](../../scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs#L245) · [Why the final tree drifted](2026-11-24-why-missed-master-record-stale-again.md#L23)

## Dependencies and standing policy

- **Runtime.** The server policy, graph inventory, compiled resource and skip-state machinery depend on a trusted manifest. `LandNonCodeSkipTests.cs` covers malformed and candidate-only manifests, the FT-2 target path, graph drift and filter overrides. The landing protocol stores skip outcomes and carries them across publication and recovery. Deleting the records under the existing policy removes the trust inputs and causes docs-only builds once the running process has no matching embedded record. [Manifest policy](../../server/Application/Services/LandVerificationPolicy.cs#L25) · [State and rollback](../orchestration-loop.md#L2131)
- **Tooling and tests.** `land-graph-audit`, `land-graph-precheck` and `land-graph-land-refresh` are CLI verbs at the examined pin. `1145054d8c8b745dffa186c4ed54507d5cb0fb82` deleted the old PowerShell audit, precheck, refresh and resolver scripts. CLI registration, the Nightly process and tool resolver, both tool projects, solution/build wiring and staged test helpers all have live callers or witnesses. `C1385_RegistryMatchesNarrowGraph` runs the actual audit against the committed repository. Refresh tests pin the instruction text, including the bare-update rule. Selector and test policy, discovery and timing files carry classes that would require accounting if retired. [CLI commands](../../scripts/lib/Antiphon.Scripts/Program.cs#L131) · [Registry guard](../../tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs#L351) · [Instruction guards](../../tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs#L369) · [Selector rules](../../tests/impacted-classes.json#L350)
- **Shared release evaluation.** The same tool binary provides `release-inputs` with independent tests. `RcAdmission` fingerprints several of its source files as release driver inputs. That surface has a separate purpose if land-only commands or records are removed. The derived impact index is a separate artifact. The server only regenerates it on an index-only rebase conflict. A clean rebase goes to Prepared. Nightly `policyHash` and live-lane `PinnedInputDigest` are separate identities. [Tool dispatch](../../tools/Antiphon.LandGraphAudit/Program.cs#L41) · [RC admission inputs](../../scripts/lib/Antiphon.Nightly/RcAdmission.cs#L96) · [Index conflict path](../../server/Application/Services/AgentTaskLandingProtocol.cs#L293) · [Digest boundaries](../orchestration-loop.md#land-graph-refresh)
- **Instructions and cut.** The committed rule warns on inherited digest mismatch in Merge/Review, refreshes and reports batch-owned drift, forbids bare update outside that path, and keeps unknown constructs, malformed manifests, out-of-batch changes and failed post-checks hard. The helper checks current-tree construct refusals before any inherited warning. The rc cut runbook requires the check to succeed on the exact pin before creating the rc ref. The orchestrator and rc qualification skills, server bundle, AGENTS.md, Merge and testing docs repeat or enforce parts of the rule. The November 24 operator ruling tells agents to re-record **master's** stale record automatically. The committed instruction and guard still conflict with that exception. The diagnosed conflict includes standing authority outside Git. [Standing rule](../orchestration-loop.md#land-graph-refresh) · [RC skill](../../.claude/skills/antiphon-rc-qualification/SKILL.md#L144) · [Operator ruling and copies](2026-11-23-why-missed-master-graph-audit-embedded-md.md#L102)

## Options for the operator

Effort is comparative, based on the affected surfaces. This stage measured existing behavior.

| Option | Coverage and behavior | Effort and risk | Decision authority |
| --- | --- | --- | --- |
| **A. Keep as is** | Retains both approval authorities, pre-check, exact pins, two-pass evaluation and mandatory rc check at the pin. | Little implementation effort. Definition edits still trigger approval work. Caller-side refresh can be stale after integration and direct pushes can defer detection to a cut. The committed rule and November 24 operator instruction still conflict. | Operator retains the gate. Orchestrator applies the standing policy. |
| **B. Keep selection and make the records self-maintaining** | Land regenerates the graph record and index on its final integrated tree. Retains runtime skip, current-tree audit and pins. Stale digest at the cut becomes advisory only if current-tree coverage otherwise passes. | Medium effort. The current helper already evaluates and writes records but runs before rebase. A stale-record exit short-circuits real evaluation and so cannot justify the advisory. A target move can invalidate earlier computation. Direct pushes that skip preparation can reproduce the gap. Graph and index have different inputs even when maintained together. | Operator owns advisory cut policy and direct-push expectations. Orchestrator commissions Plan/Code. |
| **C. Drop the audit and records and compute the graph fresh at each land** | Can keep tree fingerprints and the separate test selector. Removes durable graph approval and exact cut evidence. Equal target/candidate fingerprints can miss a docs dependency introduced earlier. With no equivalent approval the safe choice is to build docs-only changes as well. | Medium-to-high effort, including runtime witnesses, commands, policy and shared evaluator consumers. Fresh two-pass evaluation could preserve much coverage while paying the audit's cost on each selected land. Fresh hashes alone lose pre-check, pin approval and differential evidence. Recomputing a code land's graph buys no existing skip. | Operator chooses whether to give up docs-only skips or pay for equivalent per-land coverage, and whether to retire the release gate. |

**Recommendation.** Choose B. The smallest scope that addresses the observed re-record toil is automatic graph approval on the final integrated tree, keeping unknown constructs, unapproved pin changes and evaluation failures hard. Make digest-only staleness at the cut advisory only with successful fresh coverage. The failed recut also found a stale test index, which has its own validation and can be considered alongside the graph.

**Remaining uncertainties.** No examined receipt proves a production docs-only land would otherwise have skipped a required build. Counts do not measure human idle time or fleet-wide frequency. Empty, cancelled, overwritten or differently titled reports can hide other cases. No live activation or docs-skip frequency was measured. The latest matching digest here is a Git reconstruction. This investigation ran no builds, tests, updates, desktop commands or stack mutations.

**Not done, noted.** Implementation and rollout remain with Decide and the follow-on stages.

## Reproduction and file inventory

The exact path census can be repeated from the assigned base.

```sh
base=2a79f46749de0ecf2c516fd77a1eb3825de25046
graph=server/Application/Services/LandVerificationBuildGraph
records() {
    git log --full-history --no-merges --since-as-filter=2026-09-26T00:00:00Z \
        --before=2026-11-25T00:00:00Z --format=%H "$@" -- \
        "$graph.manifest.txt" "$graph.evaluation.txt" "$graph.pins.txt" |
    while IFS= read -r sha; do
        if git diff-tree --root --no-commit-id --name-only -r "$sha" |
            awk -v g="$graph" '$0 != g ".manifest.txt" && $0 != g ".evaluation.txt" && $0 != g ".pins.txt" { other=1 } END { exit(other || NR==0) }'
        then git show -s --format='%H %cI %s' "$sha"
        fi
    done
}
records "$base"
records --glob='refs/remotes/origin/rc/*' --not "$base"
git grep -li -e 'land-graph' -e 'landgraph' -e 'land verification graph' "$base"
```

For history, `git log -S'39B06B3A948BF44BF1BAF758201BAC0D00D37A6DDB1A361A4075B9CF6EECD71A' -- "$graph.manifest.txt"` locates the November 24 record and its next replacement. `git log -S'server/Application/Review/Checks/Seed/**/*.md' -- tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs` locates the admission change.

To reconstruct a fingerprint, pass the indicated commit to `git ls-tree -r -z`, select rows using the runtime predicate, keep the original mode/type/object ID/path bytes, ordinal-sort the rows, append a NUL to each and SHA-256 the result. Compare the count and uppercase digest to `git show <sha>:server/Application/Services/LandVerificationBuildGraph.manifest.txt`. [Runtime predicate](../../server/Application/Services/LandVerificationBuildGraph.cs#L41)

To repeat the board census, query `/api/cards/search` with the phrases above, `includeArchived=true` and board ID `8988ca03-7414-47ad-b0b6-51556c701703`, then use `/api/agent-tasks?boardId=<id>&since=2026-09-26T00:00:00Z` and fetch details by ID. The verification command in the retained recut receipt is `pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-graph -- ./scripts/antiphon land-graph-audit --check`.

The literal census has 307 tracked files at the examined pin. It includes 50 task reports, two skills, 175 docs, 10 script files, three server files, 42 test/policy/fixture files, 22 tool files and three root files. Historical references are included. The manifest, evaluation record, pins, `LandVerificationPolicy.cs` and `AgentTaskLandingProtocol.cs` are functional dependencies even where absent from the literal search. `docs/release-gates.md` covers surrounding release rules. Two fixture files have graph names at path level, `tests/Antiphon.Tests/Application/Fixtures/land-graph-c1528.json` and `tests/Antiphon.Tests/Application/Fixtures/land-graph-c1540.json`.

<details>
<summary>All 307 literal matches at the source pin</summary>

```text
.antiphon/task-03d6c869.md
.antiphon/task-09322a8b.md
.antiphon/task-0a670765.md
.antiphon/task-0ca57dc9.md
.antiphon/task-0cbd3a9e.md
.antiphon/task-14947ff2.md
.antiphon/task-1757a065.md
.antiphon/task-18255e41.md
.antiphon/task-1fa8a516.md
.antiphon/task-3521f183.md
.antiphon/task-3e5b2540.md
.antiphon/task-40e35f61.md
.antiphon/task-426578d6.md
.antiphon/task-4965dfb4.md
.antiphon/task-561196a9.md
.antiphon/task-5b4b9532.md
.antiphon/task-5dd200c3.md
.antiphon/task-69851e84.md
.antiphon/task-698bf6b9.md
.antiphon/task-6e80fc7e.md
.antiphon/task-6fb028cd.md
.antiphon/task-79c0c349.md
.antiphon/task-7d513c4d.md
.antiphon/task-7f6736bc.md
.antiphon/task-7f9d46dc.md
.antiphon/task-82f0a842.md
.antiphon/task-8b5c63b3.md
.antiphon/task-8f0a71bb.md
.antiphon/task-97010794.md
.antiphon/task-a1f403eb.md
.antiphon/task-a2935adb.md
.antiphon/task-a584c99e.md
.antiphon/task-a62610b7.md
.antiphon/task-a869b1b6.md
.antiphon/task-a9634687.md
.antiphon/task-ad4999db.md
.antiphon/task-ae45156c.md
.antiphon/task-b0b6ec31.md
.antiphon/task-b2fc0aa5.md
.antiphon/task-b9729f4b.md
.antiphon/task-bb5773d2.md
.antiphon/task-cadd1086.md
.antiphon/task-cba5ec74.md
.antiphon/task-d3bad549.md
.antiphon/task-deb6730d.md
.antiphon/task-e57194b7.md
.antiphon/task-ed7af4f3.md
.antiphon/task-f72769c9.md
.antiphon/task-f9d4a394.md
.antiphon/task-fd701dd9.md
.claude/skills/antiphon-orchestrator/SKILL.md
.claude/skills/antiphon-rc-qualification/SKILL.md
AGENTS.md
Antiphon.sln
Directory.Build.targets
docs/investigations/2026-10-22-rc-bundle-c-dry-run.md
docs/investigations/2026-10-22-rc-group-3-reds-debug.md
docs/investigations/2026-10-23-c1380-persistent-rc-red-debug.md
docs/investigations/2026-10-23-card-1538-code.md
docs/investigations/2026-10-23-card-1540-timestamp-path-land-audit.md
docs/investigations/2026-10-23-card-1572-outage-recovery.md
docs/investigations/2026-10-23-card-1572-unit-lane-debug.md
docs/investigations/2026-10-25-rc-db2c2088-lane-l-linux-7425f29e.md
docs/investigations/2026-10-25-rc-db2c2088-windows-file-lane.md
docs/investigations/2026-10-29-rc-speedups-timing-census.md
docs/investigations/2026-10-30-c1379-land-restart-replay-debug.md
docs/investigations/2026-10-30-c448-lease-child-record-debug.md
docs/investigations/2026-10-30-c514-arm-unconfirmed-debug.md
docs/investigations/2026-10-30-card-1609-s5-checkpoint-debug.md
docs/investigations/2026-10-30-card-1610-s2s3-verification-debug.md
docs/investigations/2026-10-30-card-1613-native-windows-add64715.md
docs/investigations/2026-10-30-g104-substring-sha-debug.md
docs/investigations/2026-10-30-reportbudget-posix-git-path-debug.md
docs/investigations/2026-10-30-reportbudget-posix-git-path-sweep.md
docs/investigations/2026-11-03-c1385-differential-none.md
docs/investigations/2026-11-04-s4a-independent-audit-b.md
docs/investigations/2026-11-08-run-4e0d1159-group-85-windows-failure.md
docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md
docs/investigations/2026-11-08-why-missed-server-git-windows.md
docs/investigations/2026-11-09-run-231f573c-land-graph-land-refresh-windows-failure.md
docs/investigations/2026-11-09-why-missed-evaluation-budget-slow-dotnet.md
docs/investigations/2026-11-09-why-missed-land-graph-land-refresh.md
docs/investigations/2026-11-09-why-missed-landgraphaudit-double-build.md
docs/investigations/2026-11-10-run-ae84478d-g131 DerivedImpactIndex+ReleaseGateFollowup-windows-failure.md
docs/investigations/2026-11-10-why-missed-antiphon-g2-platformtaxonomy-filter-tests-red.md
docs/investigations/2026-11-10-why-missed-coverage-rule-expected-skips.md
docs/investigations/2026-11-10-why-missed-g131 DerivedImpactIndex+ReleaseGateFollowup.md
docs/investigations/2026-11-10-why-missed-g133 ReleaseSelectionCli red.md
docs/investigations/2026-11-10-why-missed-in-process-status-spawn.md
docs/investigations/2026-11-12-card-1820-selector-v2-accuracy-on-real-diffs-smallest-path.md
docs/investigations/2026-11-14-why-missed-c1620-timing-labels.md
docs/investigations/2026-11-15-card-1901-ptyhost-csproj-property.md
docs/investigations/2026-11-17-desktop-worktree-inventory.md
docs/investigations/2026-11-17-empty-failed-chunks.md
docs/investigations/2026-11-17-merge-259-timing.md
docs/investigations/2026-11-19-operator-nightly-perf-commits-review.md
docs/investigations/2026-11-19-server2-housekeeping-census.md
docs/investigations/2026-11-20-sha-stamp-recompiles.md
docs/investigations/2026-11-20-sha-stamp-recompiles.patch
docs/investigations/2026-11-20-why-missed-run-e7f92f66-split-8-and-land-graph.md
docs/investigations/2026-11-21-astra-6.2-test-confirmation.md
docs/investigations/2026-11-21-codex-real-cli-tests-on-0170.md
docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-group-137.md
docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-group-71.md
docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-group-86.md
docs/investigations/2026-11-21-why-missed-satelliteresourcelanguages-graph-refusal.md
docs/investigations/2026-11-22-rc-run-3528e6b1-final-analysis.md
docs/investigations/2026-11-23-why-missed-master-graph-audit-embedded-md.md
docs/investigations/2026-11-24-why-missed-master-record-stale-again.md
docs/orchestration-loop.md
docs/specs/block-format/conformance/legacy/001-next-stage-basic.json
docs/specs/block-format/conformance/legacy/001-next-stage-basic.txt
docs/superpowers/evidence/2026-10-23-card-1528-recut-evidence.md
docs/superpowers/mutations/2026-10-22-card-1483-s2-candidates.md
docs/superpowers/mutations/2026-10-22-card-1483-s3-candidates.md
docs/superpowers/mutations/2026-10-23-card-1483-s4-candidates.md
docs/superpowers/mutations/2026-10-23-card-1483-s5-candidates.md
docs/superpowers/mutations/2026-10-23-card-1528-candidates.md
docs/superpowers/mutations/2026-10-23-card-1538.md
docs/superpowers/plans/2026-10-21-card-1400-docs-input-bypasses-plan.md
docs/superpowers/plans/2026-10-22-card-1409-import-fail-closed-plan.md
docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md
docs/superpowers/plans/2026-10-22-card-1483-s1-recut-plan.md
docs/superpowers/plans/2026-10-22-card-1483-s1-recut-repair-amendment.md
docs/superpowers/plans/2026-10-22-card-1483-s1-repair-amendment.md
docs/superpowers/plans/2026-10-22-card-1483-s1-repair2-amendment.md
docs/superpowers/plans/2026-10-22-card-1483-s2-amendment.md
docs/superpowers/plans/2026-10-22-card-1483-s3-amendment.md
docs/superpowers/plans/2026-10-23-card-1483-s4-amendment.md
docs/superpowers/plans/2026-10-23-card-1483-s5-amendment.md
docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md
docs/superpowers/plans/2026-10-23-card-1540-land-audit-environment-timestamp-plan.md
docs/superpowers/plans/2026-10-23-card-1571-land-graph-refresh-plan.md
docs/superpowers/plans/2026-10-24-card-1540-aspire-precheck-repair-plan.md
docs/superpowers/plans/2026-10-24-card-1540-master-recut-plan.md
docs/superpowers/plans/2026-10-24-task-fdd9519d-land-graph-record-repair.md
docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md
docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md
docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md
docs/superpowers/plans/2026-10-30-card-1514-incremental-manifest-publication-plan.md
docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md
docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md
docs/superpowers/plans/2026-10-31-task-8562dc2a-tree-kill-checkpoints.md
docs/superpowers/plans/2026-11-02-card-1545-S2a-S2b-test-design.md
docs/superpowers/plans/2026-11-03-card-1653-S2-plan.md
docs/superpowers/plans/2026-11-05-card-1729-phase0-plan.md
docs/superpowers/plans/2026-11-08-card-1790-pwsh-private-cache-plan.md
docs/superpowers/plans/2026-11-09-card-1546-server-git-project.md
docs/superpowers/plans/2026-11-09-card-1568-audit-fixture-containment-plan.md
docs/superpowers/plans/2026-11-09-card-1802-complete-report-never-blocks-on-lease.md
docs/superpowers/plans/2026-11-09-card-1823-cumulative-failure-ledger.md
docs/superpowers/plans/2026-11-09-card-1832-platform-taxonomy.md
docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md
docs/superpowers/plans/2026-11-11-card-1830-gitinprocess-tests.md
docs/superpowers/plans/2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md
docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md
docs/superpowers/plans/2026-11-13-card-1820-evaluator-checkpoints.md
docs/superpowers/plans/2026-11-16-card-1820-s1r5-s1r6-sequencing-plan.md
docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md
docs/superpowers/plans/2026-11-16-card-1882-testdesign.md
docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md
docs/superpowers/plans/2026-11-18-card-1896-checkpoints.md
docs/superpowers/plans/2026-11-18-card-1896-code-evidence.md
docs/superpowers/plans/2026-11-18-card-1976-checkpoints.md
docs/superpowers/plans/2026-11-18-card-1978-checkpoints.md
docs/superpowers/plans/2026-11-18-card-1981-checkpoints.md
docs/superpowers/plans/2026-11-18-card-1994-checkpoints.md
docs/superpowers/plans/2026-11-18-card-1996-checkpoints.md
docs/superpowers/plans/2026-11-18-claude-cli-2-1-327-checkpoints.md
docs/superpowers/plans/2026-11-19-astra-b-advisories-checkpoints.md
docs/superpowers/plans/2026-11-19-astra-fixes-a-checkpoints.md
docs/superpowers/plans/2026-11-19-astra-fixes-b-checkpoints.md
docs/superpowers/plans/2026-11-19-c487-probe-checkpoints.md
docs/superpowers/plans/2026-11-19-c488-fix-checkpoints.md
docs/superpowers/plans/2026-11-19-card-1968-checkpoints.md
docs/superpowers/plans/2026-11-19-card-1972-checkpoints.md
docs/superpowers/plans/2026-11-19-card-1991-checkpoints.md
docs/superpowers/plans/2026-11-19-card-1995-checkpoints.md
docs/superpowers/plans/2026-11-19-card-2000-checkpoints.md
docs/superpowers/plans/2026-11-19-card-2004-checkpoints.md
docs/superpowers/plans/2026-11-19-card-2005-astra-a-advisories-checkpoints.md
docs/superpowers/plans/2026-11-19-g139-checkpoints.md
docs/superpowers/plans/2026-11-19-incremental-s5-checkpoints.md
docs/superpowers/plans/2026-11-19-nightly-linux-reds-checkpoints.md
docs/superpowers/plans/2026-11-19-ps1-to-csharp-migration-plan.md
docs/superpowers/plans/2026-11-19-summary-writer-witness-checkpoints.md
docs/superpowers/plans/2026-11-19-wrapper-only-identity-checkpoints.md
docs/superpowers/plans/2026-11-20-astra62-selectable-checkpoints.md
docs/superpowers/plans/2026-11-20-astra62-selectable-fix-checkpoints.md
docs/superpowers/plans/2026-11-20-astra62-selectable-fix2-checkpoints.md
docs/superpowers/plans/2026-11-20-card-2008-shadow-vs-real-checkpoints.md
docs/superpowers/plans/2026-11-20-card-2010-s6c-port-advisories-checkpoints.md
docs/superpowers/plans/2026-11-20-change-impact-project-plan.md
docs/superpowers/plans/2026-11-20-close-receiptless-exempt-checkpoints.md
docs/superpowers/plans/2026-11-20-cut-reds-resilience-link-checkpoints.md
docs/superpowers/plans/2026-11-20-fast-track-ft2-checkpoints.md
docs/superpowers/plans/2026-11-20-fast-track-ft5-checkpoints.md
docs/superpowers/plans/2026-11-20-fast-track-lane-plan.md
docs/superpowers/plans/2026-11-20-incremental-s3-testonly-checkpoints.md
docs/superpowers/plans/2026-11-20-incremental-s5-followup-checkpoints.md
docs/superpowers/plans/2026-11-20-run-e7f92f66-failures-checkpoints.md
docs/superpowers/plans/2026-11-20-run-e7f92f66-group-134-checkpoints.md
docs/superpowers/plans/2026-11-20-run-e7f92f66-split-8-checkpoints.md
docs/superpowers/plans/2026-11-20-sha-stamp-checkpoints.md
docs/superpowers/plans/2026-11-21-card-2046-codex-boot-canary.md
docs/superpowers/plans/2026-11-21-card-2049-replay-checkpoints.md
docs/superpowers/plans/2026-11-21-checkpoint-contract-r2-checkpoints.md
docs/superpowers/plans/2026-11-21-checkpoint-contract-replay-checkpoints.md
docs/superpowers/plans/2026-11-21-close-advisories-checkpoints.md
docs/superpowers/plans/2026-11-21-codex-0170-chip-fix-checkpoints.md
docs/superpowers/plans/2026-11-21-codex-ladder-62-checkpoints.md
docs/superpowers/plans/2026-11-21-land-path-advisories-checkpoints.md
docs/superpowers/plans/2026-11-21-owner-map-guard-r2-checkpoints.md
docs/superpowers/plans/2026-11-21-w2-nightly-port-slices.md
docs/superpowers/plans/2026-11-22-card-1939-boot-prompt-diagnostics.md
docs/superpowers/plans/2026-11-22-card-2047-grok-startup.md
docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
docs/superpowers/plans/2026-11-23-debug-02f9ccec-seed-md-precheck-checkpoints.md
docs/superpowers/reports/2026-11-13-card-1820-evaluator-code-da81e16e.md
docs/superpowers/reviews/2026-11-16-card-1820-s1r5-s1r6-sequencing-plan-review.md
docs/superpowers/reviews/2026-11-16-card-1882-plan-testdesign-review-r2.md
docs/superpowers/reviews/2026-11-16-card-1882-plan-testdesign-review.md
docs/superpowers/reviews/2026-11-17-card-1890-review.md
docs/superpowers/reviews/2026-11-18-card-1978-review.md
docs/superpowers/specs/2026-11-05-solution-split-by-dependency.md
docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
docs/superpowers/testdesign/2026-11-06-card-1737-r1-testdesign.md
docs/superpowers/testdesign/2026-11-06-card-1737-r2-testdesign.md
docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md
docs/testing-and-build.md
scripts/lib/Antiphon.Nightly/ImpactedClasses.cs
scripts/lib/Antiphon.Nightly/LandAuditTool.cs
scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs
scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs
scripts/lib/Antiphon.Nightly/NightlyLaunch.cs
scripts/lib/Antiphon.Nightly/RcAdmission.cs
scripts/lib/Antiphon.Scripts/LandGraphAuditCommand.cs
scripts/lib/Antiphon.Scripts/LandGraphLandRefreshCommand.cs
scripts/lib/Antiphon.Scripts/Program.cs
scripts/lib/nightly-class-costs.json
server/Antiphon.Server.csproj
server/Application/Services/LandVerificationBuildGraph.cs
server/Bundles/orchestrator.md
tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj
tests/Antiphon.Tests/Antiphon.Tests.csproj
tests/Antiphon.Tests/Application/LandAuditToolResolverTests.cs
tests/Antiphon.Tests/Application/LandGraphAuditCleanupTests.cs
tests/Antiphon.Tests/Application/LandGraphCanaryToleranceTests.cs
tests/Antiphon.Tests/Application/LandGraphCommandHost.cs
tests/Antiphon.Tests/Application/LandGraphCommandTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationCanaryEntryTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationCanaryGuardTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationCanaryLinkTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationClockTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationEnvironmentTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationFailOpenTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationFilesystemTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationIgnoredItemTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationPinsTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationProbeTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationSolutionMembershipTests.cs
tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs
tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs
tests/Antiphon.Tests/Application/LandGraphSdkRemovalPolicyTests.cs
tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs
tests/Antiphon.Tests/Application/OrchestratorBundleOwnershipTests.cs
tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs
tests/Antiphon.Tests/Infrastructure/LandGraphAuditProjectInstanceTests.cs
tests/Antiphon.Tests/Infrastructure/SourceRevisionStampContractTests.cs
tests/Antiphon.Tests/Infrastructure/TestFixtureCleanupGuardTests.cs
tests/Antiphon.Tests/PlatformTaxonomyDerivationTests.cs
tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs
tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs
tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs
tests/Antiphon.Tests/Scripts/ReleaseImpactEvaluationTests.cs
tests/Shared/NativeLinuxPolicyWitness.cs
tests/Shared/NativeLinuxWindowsBaseline.json
tests/Shared/expected-duration-roster.json
tests/derived-impact-index.json
tests/expected-platform-skips.json
tests/fixtures/live-lanes/full-discovery-before.json
tests/impacted-classes.json
tests/rc-project-categories.json
tests/test-execution-policy.json
tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj
tools/Antiphon.LandGraphAudit.Core/CanaryEntries.cs
tools/Antiphon.LandGraphAudit.Core/EvaluationBudget.cs
tools/Antiphon.LandGraphAudit.Core/LandGraphAuditGate.cs
tools/Antiphon.LandGraphAudit.Core/LandGraphCanaryRemoval.cs
tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs
tools/Antiphon.LandGraphAudit.Core/PhaseClock.cs
tools/Antiphon.LandGraphAudit.Core/PowerShellSemantics.cs
tools/Antiphon.LandGraphAudit.Core/PsJson.cs
tools/Antiphon.LandGraphAudit.Core/WallClock.cs
tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj
tools/Antiphon.LandGraphAudit/ApiProbe.cs
tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs
tools/Antiphon.LandGraphAudit/CanaryRepository.cs
tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs
tools/Antiphon.LandGraphAudit/EvaluationSnapshot.cs
tools/Antiphon.LandGraphAudit/Program.cs
tools/Antiphon.LandGraphAudit/RecordingFileSystem.cs
tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs
tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs
tools/Antiphon.LandGraphAudit/SolutionAudit.cs
tools/Antiphon.LandGraphAudit/TargetPins.cs
```

</details>
