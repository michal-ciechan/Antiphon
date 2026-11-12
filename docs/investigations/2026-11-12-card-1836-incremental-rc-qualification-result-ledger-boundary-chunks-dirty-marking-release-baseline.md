# CARD-1836: incremental rc qualification, state and smallest path

Date: 2026-11-12 UTC. Investigation task `b9444a8c-3762-49cc-b860-d747b6c0a6f6`.

**Confirmed:** qualification repeats the complete roster because selection and dispatch deliberately admit only Full execution. CARD-1836 S1 is already landed; it is a library with no qualification caller. Safe reuse of run `0b292fa4` is not an available switch. The existing plan supplies seven remaining activation slices, but does not establish how that pre-ledger run becomes a trusted ledger. Plan must close that evidence gap before an impacted-only next cut can be promised.

## Evidence boundary

Inspected source: task HEAD and fetched `origin/master`, both `cc48c70050a2932eddf4abe18cea8659d478c33e`; fetched `origin/rc/2026-11-18` is `56e96eabf698e9c1fcd462836ad32ed1fb259e83`. `git merge-base --is-ancestor` confirms the latter is an ancestor of master. No branch switch, source change, deployment, live qualification access, or test/build driver was performed. The only repository deliverable is this document. The live run ID, supervisor PID 45428, approximately 17 hours, and approximately 245 impacted of 1,163 chunks are **brief-supplied observations**, not independently measured here. The desktop paths are unreachable from this runner; no current pass count is asserted.

Read board rows with `scripts/card.ps1 get/history CARD-1836 -Board Antiphon`; enumerated board-scoped tasks with `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`; read the identified tasks through `GET /api/agent-tasks/{id}`. Those stored task results and Review evidence, together with Git/source reconstruction and the local probe transcript below, are the evidence. No credentials are included.

The principal existing plan is `docs/superpowers/plans/2026-11-09-card-1836-incremental-rc.md`, abbreviated **P1836** below. The companion is `docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md`, abbreviated **P1820**. All unqualified file:line citations refer to the inspected master SHA. Review paths on other refs explicitly name their ref.

## 1. What exists, and what the Review column meant

At observation, both cards were InProgress because these Investigate tasks had dispatched. CARD-1836 history revision 10 records Review task `b3dbf071` settling Succeeded on 2026-11-10 17:22 UTC; revision 13 records this investigation dispatch on 2026-11-12 23:45 UTC. A successful Review task is evidence about its named slice, not completion of the whole card.

| CARD-1836 slice | State at inspected master | Exact evidence |
|---|---|---|
| Plan | Landed | `c2e687d19ac6e8cfa5fd0ef38df34490e90c9b41`; P1836:247-293 lists S1-S10 |
| S1 schema, reader/writer, carry predicate | Landed, reviewed clean after reader repair | Initial implementation `81a03643086c666b91364b85072e32dc427a18bf`; repair `4e3120b9df5ed45d8426a26c20938082555908fa`; current `scripts/lib/rc-ledger.ps1:290`, `:339`, `:676`, `:848` |
| S2 boundary map and stable IDs | Not started in observed Git/task evidence | Planned at P1836:255; `rc-boundary.ps1`, `rc-boundary-prefixes.json` and its harness absent |
| S3 input digest | Not started | P1836:259; `scripts/lib/rc-chunk-digest.ps1` absent |
| S4 boundary packing, opt-in | Not started | P1836:263; current packer has only Leaves/DiscoveryNodes parameters (`nightly-tests-impl.ps1:399-400`) |
| S5 plan/status and ledger production | Not started | P1836:267; `scripts/rc-ledger-plan.ps1` and `scripts/rc-ledger-status.ps1` absent |
| S6 dirty-only launch and result integration | Not started | P1836:271; `scripts/lib/rc-dirty-order.ps1` absent; current scheduler insists on a complete permutation (`nightly-dispatch.ps1:225-244`) |
| S7 baseline picker, Incremental admission, release branch script | Not started | P1836:275; `scripts/lib/rc-baseline.ps1` and `scripts/cut-release-branch.ps1` absent; release-cut still admits Full/Shadow/ChangeAware only (`scripts/release-cut.ps1:30`) |
| S8 forced Full | Not started | P1836:279; S1 accepts caller-supplied ForcedFull but does not compute contract/SDK/age policy (`rc-ledger.ps1:290-306`) |
| S9 replay | Not started | P1836:283; `scripts/lib/rc-replay.ps1` absent |
| S10 operational documentation pins | Not started | P1836:287; proposed classes and sentences have no implementation commits |
| Related CARD-1842 ledger duration/host enrichment | Landed, separate from activation | `306a5ec42f38765f15dd3e98134309002f79b5cb`; `scripts/rc-ledger-stamp-duration.ps1:2-5,24-30` requires an existing ledger, enriches durations, optionally writes v2 host fields, and has no nightly caller |

No additional functional CARD-1836 slice was found reviewed-clean awaiting land or in flight. Current in-flight work on the card is this investigation. Absence is scoped to the fetched refs and board query, not to unpushed work.

### Original branches and reviews

`git log --remotes --not origin/master -i --grep=1836` finds only the historical original plan/S1/repair/review commits below. The union of their changed paths is P1836, the S1 library, its harness and C# class, and the two review notes. A separate all-remote log over the planned S2-S9 paths found no unlanded implementation. Branches inheriting the landed S1 are not separate work slices.

| Origin branch suffix (`feat/card-task-...`) | Tip | Meaning |
|---|---|---|
| `4a78d574` | `8ea30115b9f4ab35f171223440145d73cc15e0ab` | Original plan, replayed onto master as `c2e687d1...` |
| `7f1e2130` | `4556035c7900fd8995739579fa756c42da5f14b5` | Original S1, replayed as `81a03643...`; original red `c8f00a4a39f928c76d0445f24f2f548f70abefdc` replayed as `2d5139daf6aaf8be1175e5c1afadb013a080f289` |
| `a84bb9fc` | `d46d69a3335b1dbc6590d841bb85edd9677b9ce1` | Round-1 Review found duplicate-key false carry |
| `c7ed4119` | `60f7421e2165cefa3cfd29197d108d756cfa095c` | Reader repair, replayed as `4e3120b9...`; red `bdd2b0918e934e4fb362d3b1b2e2bee32ccf9ff7` replayed as `5db49925dbf47b7aa60dff81edc081f7a4bd54cc` |
| `b3dbf071` | `16df1ecf5bdb554dd0406207625a1d228236d4c4` | Round-2 clean Review of the original repair SHA |

Review notes are under `docs/investigations/2026-11-10-review-card-1836-s1.md` on `origin/feat/card-task-a84bb9fc` and `docs/investigations/2026-11-10-review-card-1836-s1-r2.md` on `origin/feat/card-task-b3dbf071`. No CARD-1836 reviews were found under the requested `docs/reviews/` or `docs/superpowers/reviews/` locations.

Stored Review row `b3dbf071-9365-4e6d-9dca-ff852e481456` has Review evidence ID `007596ca-f32e-4bc6-92cf-0c652fe26efd`, outcome Clean, reviewedSourceClean true, subject `c7ed4119-0ecb-4f6b-b952-282ffbdb91e1`, SHA `60f7421e2165cefa3cfd29197d108d756cfa095c`. Its result records 39/39 reader attacks matched, 13/13 ledger tests, and 78/78 guards. These are historical results, not reruns in this investigation. It explicitly says Incremental stays off.

Essential unedited provenance from stored Code result `7f1e2130-d411-4a7c-9ede-a9032e5fef56`:

```text
CHECKPOINT CP-1 commit=4556035c7900fd8995739579fa756c42da5f14b5 build=ok filter=/*/*/RcIncrementalLedgerTests/(MissingDigestIsDirty*)|(UnexpectedSkipNeverCarries*)|(CrossPlatformNeverCarries*)|(SameDigestCarriesProvenance*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-7f1e2130/.antiphon/checkpoints/20261110-141717-20dc/rows/CP-1/run.trx slot=granted waited=60s dirty=0 source=4556035c7900fd8995739579fa756c42da5f14b5 sourceState=clean buildSource=verified
```

The same stored Code result reports an initial guard batch with four failures and an isolated retry passing; the later Review separately reports 78/78. Neither history is rewritten as an always-green initial run. Mutation is waived by the current operator brief; no controls ran here.

## 2. Confirmed mechanism and reproduction

1. `nightly-run.ps1:91`, `nightly-run-impl.ps1:289`, `nightly-tests-impl.ps1:1530`, and `release-cut.ps1:30` admit only Full, Shadow, ChangeAware. Default is Full.
2. `release-selection.ps1:219-230` adds `change-aware-unqualified` and writes effectiveMode=Full even when there is a proposal. The compiled counterpart does the same (`Antiphon.Scripts/ReleaseSelection.cs:104-107,279-280`). Hop validation refuses any non-Full scope (`release-selection.ps1:371-375`). `Resolve-ReleaseSelectionMembers` returns FullMembers or throws (`:394-398`).
3. `Get-RcProjectSelection` computes a graph proposal but explicitly stamps shadow-only, executionEvidence=false, gateCredit=false, effectiveMode=Full (`rc-project-selection.ps1:262-268`). Shared script/build/policy changes or unknown paths broaden that proposal to all projects (`:233-250`).
4. `-PriorFailuresPath`/Digest and `-AcceptCrossCandidateSeed` are forwarded to the test loop (`nightly-run-impl.ps1:272-274`). Cross-candidate acknowledgement validates a seed, source candidate and freshness; it grants no execution credit (`nightly-failure-history.ps1:727-776`). Normal previous-run history instead requires the same candidate SHA and a closed run (`:749-756`).
5. Seed ordering remaps failures by class/method, explicitly treats chunk IDs as non-binding (`nightly-stopped-run-seed.ps1:451-457`), then appends the passed band and checks the result count is unchanged (`:591-595`). It does not compute product-change impact. The schedule's expected set equals every prepared chunk (`nightly-dispatch.ps1:225-244`).
6. S1 only decides about inputs supplied by a caller. Search of nightly/release/qualification scripts found no call to Get-RcIdentityCarryDecision/Get-RcChunkCarryDecision or import of rc-ledger.ps1. The current summary remains an execution summary (`nightly-tests-impl.ps1:1924-1938`), not an incremental closure ledger.

Local, foreground PowerShell probes at the inspected SHA dot-sourced only the existing release-selection and ledger libraries. They launched no build/test driver or qualification. Transcript:

```text
PROBE selection proposed=1 returned=3 members=failed-chunk,changed-chunk,green-chunk
PROBE incremental=selected-execution-unqualified
PROBE carry case=same decision=carry reason=carry origin=stored-run
PROBE carry case=product-digest-changed decision=dirty reason=digest-mismatch origin=
PROBE carry case=missing-digest decision=dirty reason=missing-digest origin=
PROBE carry case=policy-full decision=dirty reason=forced-full origin=
PROBE carry case=linux decision=dirty reason=cross-platform origin=
```

Reproduction uses `Resolve-ReleaseSelectionMembers` with FullMembers containing those three strings, ProposedMembers containing only changed-chunk; first with `{effectiveMode='Full';coverageScope='Full'}`, then Incremental. Carry fixture: a passed SampleTests/Runs identity, core boundary, `a` repeated 64 times as its inputDigest, passed source chunk, windows source ledger, complete candidate/run/chunk provenance, no history, FixProven=false, no forced reasons. Calls vary only new digest (`a*64`, `b*64`, unknown), forced reasons, or requested platform. The seven observations match the source paths above. The scratch driver is `/tmp/card1836-probe.ps1`; these inputs and transcript preserve the evidence if that temporary file expires.

**Root cause is confirmed by reconstruction and reproduction.** It is unfinished activation of an explicitly Full-only pipeline, rather than a seed option being accidentally ignored.

## 3. What can justify skipping

The implemented predicate's conditions are necessary but do not supply a complete safety proof. At `rc-ledger.ps1:290-336`, an identity carries only with a recognized boundary, source row and same platform, valid equal 64-hex digest, passed/expected-skip outcome, successful source chunk, no unproven same-digest fail/pass history, no caller-supplied forced Full, and complete carriedFrom provenance. Changed digest becomes dirty. Failed, interrupted, not-run, unproven and unexpected-skip never carry. The chunk predicate requires every **supplied** identity decision to carry (`:339-381`).

Its caller still owes repository/ancestor binding, the complete current identity set, complete history, real input digests, contract comparison and fix proof. Round-1 Review explicitly identifies those obligations: the chunk predicate does not require one decision per identityKeys entry; FixProven is just a Boolean; repository/lineage is not checked by the carry predicate. Thus calling S1 with optimistic arguments is not skip authorization.

| Input/proof that must remain equivalent | Existing specification or evidence | Current limitation |
|---|---|---|
| Tests actually selected, including parameter identities, additions/removals and shared test helpers | P1836:93-124,132-141; current expected roster/coverage logic in `nightly-coverage.ps1:2154-2185,2371` | No digest builder or ledger-to-current-universe closure exists; test-file equality alone is insufficient |
| Product sources exercised, with transitive dependencies | P1836:116-122; boundary edges in `tests/Antiphon.Tests/Layering/layers.txt:2-9`; project references in `rc-project-selection.ps1:148-151` | Boundary assignment is not itself proof that a test exercises only that boundary; the full dependency/input set is not implemented |
| Build/project/package configuration and imports | P1836:121-124; graph reads ancestor props/targets, imports, conditional references and linked Compile/Content/None/EmbeddedResource/AdditionalFiles (`rc-project-selection.ps1:87-103,148-185`) | The shadow graph is not an admitted digest/skip certificate |
| Harness/qualification scripts, their imports, configs, fixtures, external content consumed by tests, client lock/config inputs where applicable | Card revision 11's dependency/unknown-dirty requirement; P1836 D-3; existing graph/input rules above | P1836 does not enumerate a proven complete input manifest for these. Equality of a test file or a csproj blob alone does not prove dependency contents equal |
| Policy, catalog pin, selector and runtime; actual host SDK; periodic Full age | P1836:145-150; current release contract at `release-selection.ps1:46-65` | S8 is absent; current contract hashes policy/catalog, two selector files and global.json, not all nightly execution scripts or actual installed SDK |
| Platform and allowed skip set | `rc-ledger.ps1:313-323`; P1836:141,152 | Raw Skipped/NotExecuted is accepted only when the supplied roster names it; a stored expected-skip is not revalidated here. CARD-1832 declarations do not automatically populate a new ledger |
| No unresolved historical failure; fix landed after failure and green contains it | P1836:137,173; `merge-nightly-seeds.ps1:77-103,324-335` | S1 only consumes FixProven/history. CARD-1823 containment exists, but the qualification caller is absent |

Concrete test-file-only counterexample on the actual recut range:

```text
server/Application/Services/SessionMessageQueueService.cs
old blob 74afe71a142ac760c732509188ad18bddfbfb4f0
new blob b4bf7a4f508d246328f2c058674632e2df8fe4fc

tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs
old blob c3a12d084d8d27c92df344309ee54ecac58d4791
new blob c3a12d084d8d27c92df344309ee54ecac58d4791
```

The test registers and resolves that production service (`SessionMessageQueueServiceTests.cs:800,845`). `git diff --name-only 56e96eabf origin/master -- <these two paths>` returns only the product file. Skipping by the unchanged test blob would miss a changed exercised product input.

### Boundaries

P1836:85-108 specifies seven layer boundaries (host/core/persistence/git/runners/integrations/application), plus scripts/checkpoints/tui/e2e/client/messaging/session-runner/pty-host/agents-pty/observability/server-git/markdown-pdf lanes and unassigned. One chunk must contain one suite and one boundary. Assignment is lane path, then first matching production-path literal, then namespace prefix, else unassigned. Unassigned is always dirty. Boundary edges supply transitive input closure, not additional chunk boundaries. Proposed IDs hash suite/boundary/sorted identity keys; per-identity carry can survive repacking.

Today packing is by namespace and time budget, with ordinal `all-group-N` IDs (`nightly-tests-impl.ps1:477-518`), not by those boundaries. Therefore unchanged suite layout does not prove stable chunk membership. P1836 does not require waiting for physical CARD-1729 project extraction, but its logical mapping still requires implementation and validation.

### Release baseline

Current `Read-ReleaseSelectionBaseline` requires an `antiphon-full-release-baseline-v1` published archive with matching repository/platform, roster/TRX bindings and a published step (`release-selection.ps1:68-100,133,155-175`). A branch tip or green stopped-seed is not that archive.

Planned P1836:154-165 instead chooses an ancestor with a same-platform ledger: explicit recorded release/x.y.z baseline, else newest ancestor release branch, else previous ledger-bearing candidate on this rc line, else baseline-absent and Full. A Windows complete green closure can later authorize the separate release branch script; Linux results do not satisfy it. Tag/publish/promote remain human-only. No such picker or branch script is implemented. Release branch creation is not a prerequisite to reusing an eligible earlier rc ledger; obtaining that eligible ledger is.

## 4. Smallest path supported by the evidence

**No already reviewed branch or parameter combination can make the next cut safely carry the current run.** S1 is already in both compared tips. Re-landing its old reviewed branch yields no incremental execution.

Within the existing slice structure, S2-S8 are the seven pending activation slices: boundary/identity mapping (S2), complete input equivalence (S3), boundary packing (S4), ledger planning/status (S5), dirty-only launch and result/coverage integration (S6), mode/baseline admission (S7), and forced Full policy (S8). P1836:249-281 supplies those scopes. S9 measures savings; S10 documents the new contract. Neither substitutes for activation. The release-ref write portion of S7 is used after green, not to make the first carry possible. This is a lower bound from the existing plan, not a certified sufficient set.

Two unresolved requirements prevent a sufficient minimal set from being confirmed:

1. **Bootstrap from run 0b292fa4.** The run predates ledger production. S1 writes caller-supplied rows; CARD-1842's enrichment requires an existing ledger; seeds are order-only. No existing slice or stored receipt inspected here demonstrates import of this run's roster, complete chunk membership and terminal outcomes, source/build provenance, contract/SDK identity, expected skips and full failure history into trusted carry rows. Git can reconstruct committed blob inputs; that alone cannot prove which working bytes/SDK/fixtures a historical test process executed. To resolve: retained, immutable Windows run evidence and a Plan that states precisely which facts are reconstructable, which are unknown/dirty, and how the old run is admitted. Until then, safe fallback is Full, not a manufactured green ledger.
2. **Complete digest and execution contract.** Existing D-3 lists intended inputs, but no implementation proves complete shared-helper, fixture/config, script-import or cross-boundary dependency coverage. The current contract omits the nightly execution script closure. The real diff changes `nightly-run-impl.ps1`, `nightly-coverage.ps1` and adds `Antiphon.Nightly/NightlyCsIndex.cs` and `NightlyTestSources.cs`. A Plan must determine whether those changes dirty particular boundaries or invalidate whole-run execution semantics. It must also bind the current universe and lineage and connect every admission hop, not merely add Incremental to release-cut's ValidateSet.

Observed Git facts help bound that work: 49 paths differ between the run SHA and inspected master. The blobs for test-execution-policy.json, test-selection-policy.json, select-test-rosters.ps1, lib/test-selection.ps1 and global.json are identical at both tips. Thus those five source inputs alone do not require a forced Full across this specific range. The actual SDK string, prior Full time, full execution contract and run receipt remain unverified. The derived index blob changed from `843868c48555abc7bc30ff1bd480e5b0390db3eb` to `402f36c1a68c7a4dfe2525559389cd25a4cf0787`.

The safety argument is conditional and testable: each identity in the current platform universe must be represented exactly by either an eligible carried result with proven equivalent inputs and provenance, or a newly executed admissible result; unknowns, open failures and forced-Full reasons cannot disappear. The scheduler must execute the dirty set and coverage must account for both sets without crediting nonexistent launches. Those conditions are not established today.

The brief's 245/1,163 estimate would leave at most 918 unimpacted chunks (78.9%) before failures, incomplete results, boundary broadening, skipped identities and forced Full rules. It is not a certified carry count or a wall-time estimate. Chunk runtimes are unequal, and P1836:106 warns that a shared build duration was copied onto many historical chunk sidecars; summing those build times would overstate savings.

Unsafe shortcuts evidenced here: unchanged test files; same all-group-N IDs; seed completed/green bits without ledger eligibility; AcceptCrossCandidateSeed as permission to resume; a shadow project proposal or selector non-hit as proof of no dependency; Linux passes as Windows credit; clearing failures on same-tip greens; changing Full-only hop/coverage labels without a complete closure; ignoring a changed harness or unknown fixture/config input.

## 5. Dependency on CARD-1820 and execution placement

CARD-1820 S1 is already landed: initial index selection `ceccdaad01225a83fde0661972894f9620057163`; lane-classification repair `af1c08c73807cf57760f81570c65e5aacac88309`; later index refresh `188ac942166884f060fdcc7cbf3b15f670a27b43`. Current `tests/derived-impact-index.json` has 2,332 path edges and 152 capped/allowlisted paths. `scripts/impacted-classes.ps1:12-18,1311-1315` says keyword and roster fields remain empty in S1. The S2 keyword file, S3 roster file, S4 receipt contract class and S5 why-missed ledger class are absent; an all-remote log of their planned paths found no unlanded implementations.

Stored final Review `dfefd8e0-ee3d-4095-960b-da6237834b44` approved original source `cb931f0890126ee1327eb2fc9fe29103e3667fe4`; evidence ID `d6d761f7-4a3c-4c30-9415-843329646b76` is Clean, reviewedSourceClean true. Its exact report lives at `.antiphon/task-dfefd8e0.md` on `origin/feat/card-task-dfefd8e0` (`16df2ab6c550c981e6874687f01ff36a3211597d`). It records CP-1 64/64, guards 78/78, spawn census 57/57, and explicitly excludes CP-2 through CP-5 as later slices. The replay task `d6d56b84-7130-4682-82f8-49676290f542`, original SHA `66428403011a078edd4257dbf5fd1f4be8fe7443`, documents the replay and index regeneration onto master. These are historical evidence, not new qualification receipts.

**CARD-1820 S1 is useful positive impact evidence, but it is not a complete reverse dependency certificate.** The builder parses syntax without semantic compilation (`DerivedImpactIndexBuilder.cs:523-525`), follows only the recorded one-hop string targets (`:113-151`), omits allowlisted paths entirely (`:176-179`), and retains only 25 ranked classes plus owed metadata for capped paths (`:182-188`). This is narrower than the card's hoped-for dependency closure. A missing index even yields an empty derived state (`impacted-classes.ps1:963-966`). P1820:218 reports 13/17 hypothetical catches when selection executes, with four remaining misses; its own goal is pre-land selection, not proof that every unselected rc test is unaffected. S2-S5 improve catches/reporting but do not by themselves establish safe negative selection.

P1836:102,189 explicitly makes CARD-1820 and physical project extraction non-prerequisites for its initial boundary ledger. Therefore the original S2/S3 work need not wait for CARD-1820 S2-S5. If the revised Plan relies on the index for skip eligibility, its cap, missing-index, stale-index and incomplete-edge cases must be accounted for under unknown-means-dirty; today's absence of a hit cannot authorize carry.

| Work from existing plans | Placement and parallelism |
|---|---|
| CARD-1836 Plan gap resolution, S2 mapping, S3 digest, S4 fixture pack tests, S5 plan/status, S6 order/coverage fixtures, S7 local fixture Git, S8 policy, S9 replay, S10 docs | Linux/server2 Codex or Grok; P1836:295-315 names Linux checkpoints. Separate worktrees; S3 consumes the boundary contract, S4 consumes S2, S5-S7 integrate outputs, and S8 shares rc-ledger.ps1 with S1/CARD-1842. Integration changes to nightly-tests-impl.ps1 should not overlap other edits to that file |
| CARD-1820 S2/S3 | Linux/server2; independent of the ledger, but both wire scripts/impacted-classes.ps1 and should not run as overlapping Code edits. They can run beside CARD-1836's boundary/digest work |
| CARD-1820 S4/S5, CARD-1836 S10 | Linux; owner-doc collisions include testing-and-build.md, orchestration-loop.md and the rc skill. Separate dispatch scopes or sequential edits are needed |
| Historical run evidence/measurement | Native Windows read-only follow-up on retained completed evidence, after the current run is finished and the caller commissions access. This investigation did not inspect or disturb the live run |
| Native launch/Job Object custody, actual Incremental Windows closure, Windows-only/pty/E2E owed lanes, 5.1 reader behavior if 5.1 is used | Native Windows. `nightly-owned-process.ps1:34-39` refuses production native custody on Linux. Historical S1 Review notes the 5.1 ConvertFrom-Json 2,097,152-character limit fails closed; real-size Windows evidence is still needed before promising usable carry |

No agent-kind-specific technical dependency was found: host/OS and scopes determine placement. The brief's seven-seat target does not override enforced dispatch or build-slot caps; this delegate did not dispatch additional tasks or alter budgets.

## 6. Sizes and dispatch handoff

P1836:249 estimates each slice at 60-120 minutes. S2-S8 therefore total **7-14 authoring hours**, before ordinary reviews, queue/build-slot waits, any new Plan scope for bootstrap/input completeness, and Windows validation. S9 and S10 add 1-2 hours each if dispatched in their existing form. These are plan estimates, not measured task completion forecasts. Historical S1 Code elapsed about 0.93 h (13:51-14:47 UTC), reader Debug about 0.92 h (15:47-16:42), and the two Reviews about 0.91/0.62 h from board task rows. CARD-1820 initial S1 Code actually took about 4.13 h, followed by several repairs; its original 90-minute estimate is not a reliable integration forecast. P1820:290-304 estimates remaining S2/S3 at 1.5 h each, S4 at 1 h, S5 at 1.25 h.

The first three handoff briefs, in dependency order, are below. The first two are dispatchable immediately in disjoint scopes. The third can be prepared now and is gated on the input contract from the first; presenting all three as unconditionally ready Code would conceal the central unresolved requirement.

**1. Plan, CARD-1836 next-cut evidence contract (provisional 1-2 h; unmeasured).** Reconcile P1836 with master `cc48c70050a2932eddf4abe18cea8659d478c33e` and run source `56e96eabf698e9c1fcd462836ad32ed1fb259e83`. Resolve the absent historical-run admission proof, complete exercised-input/fixture/harness contract, full current-roster accounting, and the Full-only hop/dispatch/coverage constraints documented above. Establish which existing slices are sufficient and which evidence is still owed on Windows. Produce a reviewable Plan with scoped checkpoints and estimates; do not promise 918 carries without retained-run evidence.

**2. Code, existing CARD-1836 S2 boundary IDs (1-2 h per P1836).** Execute the existing P1836:255-257 scope: rc-boundary.ps1, rc-boundary-prefixes.json, test-rc-boundary.ps1 and RcBoundaryChunkIdTests, reading layers.txt without editing it. Its goals are stable membership-derived IDs, explicit unassigned behavior and one-boundary chunk classification. Preserve the production Full launch path. Existing CP-2 is the acceptance row. This work can proceed on server2 beside the Plan because it does not choose historical-run admission or activate skipping.

**3. Code, existing CARD-1836 S3 input digest (1-2 h in the original plan; re-estimate after Plan).** Execute the P1836:259-261 digest scope only after the complete input contract is settled. Its existing goals are committed Git-blob identity, dependency-closure invalidation, outside-input stability and unknown on missing/unreadable required inputs, with RcChunkInputDigestTests and CP-3. Include the Plan's explicit treatment of shared helpers, fixtures/configs and executed scripts; a test-source-only or csproj-only digest would not meet the card. S4 and S8 are subsequent independent-file opportunities once their input contracts are stable; S6/S7 are integration work, not an opt-in flag-only task.

## Remaining uncertainties and completion standard

- Run 0b292fa4's exact retained roster, pass/fail/interrupted/skip rows, immutable build/source provenance, SDK, last qualifying Full date and cumulative failure history were not available here. A native retained-evidence read resolves these; no live run operation is needed to establish the code mechanism.
- No implemented digest/ownership closure proves which of the 49 changed paths leave which identities clean. The companion CARD-1820 investigation may refine positive impact counts, but cannot turn capped/non-hit results into proof of input equality.
- No end-to-end Incremental closure checkpoint exists in the current S1 evidence. P1836's future function-level CPs must not be mistaken for an already tested qualification integration.
- All remote/task state is an observation at the cited fetch/API reads. Unpushed work and later task dispatches are outside this finding.

**Not done, noted:** the existing S2-S8 activation plan needs a Plan-owned historical-run admission and complete-input proof before it can safely reuse 0b292fa4; no fix was designed or implemented in this investigation.

Verdict: confirmed mechanism; next Plan. Safe carry of the requested old run remains unproven, with the resolving evidence listed above.

--- next stage ---
next: plan
handoff: CARD-1836 S1 is landed; S2-S10 are absent. Resolve old-run 0b292fa4 ledger admission and complete input/contract proof, then scope S2-S8 activation across Full-only selection hops, scheduler and coverage. CARD-1820 S1 is already landed but its capped heuristic index cannot certify non-impact.
artifact: docs/investigations/2026-11-12-card-1836-incremental-rc-qualification-result-ledger-boundary-chunks-dirty-marking-release-baseline.md
