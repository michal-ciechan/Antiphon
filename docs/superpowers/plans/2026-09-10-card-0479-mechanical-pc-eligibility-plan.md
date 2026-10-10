# CARD-0479: Mechanical positive-control eligibility

Date: 2026-09-10. Stage: Plan; separate TestDesign follows.
Inspected checkout: `7123a71b099dc99d001ad2e20d8310230e1af9f1`.

Low-tier Mutation executes a closed, implementation-bound PC recipe. Higher-tier work decides what to mutate, resolves its patch and oracle, audits missing controls, and diagnoses unexpected results. Eligibility belongs to each PC variant, never to a card's complexity label or to a successful build alone.

This extends [CARD-0478's landed plan](2026-09-10-card-0478-mutation-after-land-plan.md). Its Code -> Review -> Land -> post-land Mutation order is the required target. At the inspected checkout that plan is landed but its runtime changes are not implemented: active bundles still describe the retained Code worktree. CARD-0479 must integrate with CARD-0478's implementation before enabling the new default, rather than inventing a second snapshot or cleanup mechanism.

## Ground truth

| Assumption | Inspected fact | Design consequence |
|---|---|---|
| TestDesign can emit a patch bound to the final implementation. | `stage-plan.md` and the cycle place TestDesign before Code; tests and production code may not exist yet. | TestDesign emits an explicit design recipe and oracle obligations. Code resolves candidate payloads; a post-land higher-tier gate binds them to actual source. No invented future SHA. |
| An exact-method PC is mechanical already. | `stage-test-design.md` asks for a compiling defect and exact method/assertion; `stage-mutation.md` leaves the actual edit to the executor. | Require literal patches, case enumeration, expected outcomes and restoration instructions before Low execution. |
| A surviving mutant proves a missing assertion. | CARD-0470's PC-8 fixture repair records a warm agent hidden by a reservation window. The fixed fixture seeds idle time beyond that window; the same mutant then fails `launched.AgentId.ShouldNotBe(oldAgent)`. | Require guard reachability and masking analysis. A survivor triggers higher-tier diagnosis, not automatic test repair or a product verdict. |
| Concurrent or multi-file controls must always be expensive. | CARD-0461 V-9 describes acknowledged schedules, real process identities, isolation and backend fences; PC-83..85 need those prerequisites. | Complexity is not an exclusion. Exact multi-file payloads and mature barrier schedules can qualify; unproven backend/process evidence cannot. |
| Code's SHA is the source after landing. | CARD-0478 distinguishes reviewed C, confirmed operation O, `O.VerifiedSourceSha` L, and observed remote target R. Rebase can change C into L; R can advance beyond L. | Bind every executable pack to O/L, even when its payload was prepared at C. Never substitute C or latest master. |
| Changing a stage bundle changes routing. | `DelegationSettings.RolePolicy["Mutation"]` defaults to Frontier; `MutationRoleContractTests.C470_mutation_policy_is_independent` pins it. Existing Human pins and complexity chains have their own precedence. | Change the fallback to Low with manual escalation to High, update its test, and use explicit higher-tier binding/analysis briefs. Preserve operator routing policy. |
| Low Mutation can retain its existing discovery duty. | The Mutation bundle mandates a missing PC even for zero-PC plans. This requires adequacy judgement. | Move that duty to an explicitly commissioned higher-tier part of post-land verification; record it separately from execution. |
| All detail fits in two bundles. | `InstructionBundleTests` caps every normalized stage bundle at 2,500 ASCII characters. `InstructionBundles.ReadNormalised` normalizes LF and trims. | Put the detailed contract/template in a linked owner document and concise mandatory pointers in stage bundles. Preserve delivery and guard coverage requirements. |
| Snapshot artifacts can be committed in the verification task. | CARD-0478 prohibits snapshot commits, autosave, repair and land, including evidence amendments. | Keep candidate payloads in normal Code commits and final binding/evidence outside snapshots. A binding file must not change the SHA it claims to bind. |

Inspected owners: `docs/project-context.md`, the cycle/tiers/briefs/Mutation sections of `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/testing-and-build.md`, and the HTTP/routing owners for source and tier semantics. Inspected relevant bundle bodies, bundle composition/size tests, `MutationRoleContractTests`, and the Mutation routing fixture. These are planning inspections, not executed verification.

## Decisions

### D-1: One contract with a pre-Code recipe and a post-land binding

Add `docs/mechanical-pc-contract.md` as the detailed owner, linked from the orchestration and testing owners. It defines version 1, a copyable PC template, the binding checklist, execution/evidence protocol and refusal reasons. It is a repository instruction contract, not a new service, task role, parser, or admission API. The supplied literal scripts/commands and hash comparisons implement each run's checks; this card does not claim server-enforced eligibility from prose.

The same PC identity progresses through two artifacts:

1. **Design recipe**, authored by TestDesign and checked into the plan's `## Verification design`. It names G/PC/variant IDs, intended compiling defect, existing or proposed target path/symbol, required fixture, case matrix, decisive oracle, reachability obligations, restoration strategy, prerequisites and provisional classification. When final bytes do not exist, say `binding required after Code`, not executable or mechanically eligible. A line number is a navigation hint, never patch authority. New code may have a literal proposed edit, but it is still unbound.
2. **Bound execution pack**, prepared by a higher-tier worker against confirmed L after Code/Review/Land. It contains the literal payloads, complete source and dependency identities, resolved manifest, per-case oracle, reachability record, restoration instructions and per-PC final classification. Its immutable digest and location are handed to the executor. Missing fields mean ineligible. Human-readable markdown may explain the pack; commands, file lists, cases and outcomes must be explicit tables or structured data, with no prose choices for the executor to resolve.

Code implements tests and ordinary V/R, then prepares candidate patches and manifests against its final production/test bytes where possible. Store candidate files under `docs/superpowers/mutations/<card-identifier>/` and link them from the plan/report. Commit those candidates with normal Code work; keep candidate metadata explicitly unbound. Candidate payloads do not embed their containing commit's SHA. If the last Code edit invalidates a candidate, regenerate it or name it pending for the binder.

The final binding envelope and any rebased replacement payloads live in CARD-0478's durable verification root outside the snapshot, keyed by O/binding-task/pack revision. They include L and hashes of every payload. Code's checked-in candidates remain traceable. If a later tracked evidence archive is wanted, a normal Docs task can archive the exact pack after verification; it is not a commit from the Mutation snapshot and does not change tested L. No circular requirement to commit a file containing its own future commit SHA.

Use an index manifest containing the exact relative member paths and SHA-256 of each payload, oracle and execution manifest. The pack digest is SHA-256 of that index's bytes, recorded in a detached receipt and the binding report, not in the index being hashed. Check all member hashes as well as the index digest on receipt. Never overwrite a published revision. This binds the complete payload set without a self-referential hash.

Rejected: demand final patches from TestDesign (unknown implementation); let Low locate sites or repair diffs (retains engineering judgement); embed a guessed SHA; make a docs-only follow-up commit the tested source by accident.

### D-2: Bind after the confirmed landing, in a short higher-tier Mutation pass

Use CARD-0478's `-SourceLanding O` snapshot and companion verification card for both preparation and execution. No new `Bind` role or next-stage token is added. The brief explicitly declares one of three scopes: `binding`, `mechanical-execution`, or `higher-tier-analysis`. These are report/brief distinctions, not new persisted task modes.

Default continuation:

1. Code prepares candidate payloads with its ordinary handoff. Review audits ordinary V/R and guard/design adequacy before land, without requiring deliberate mutants.
2. On confirmed O/L, the caller records binding pending on the verification card and commissions **Role Mutation, explicit Frontier, binding-only**, in its own SourceLanding snapshot. The higher-tier worker reads Code's full report, Review's full report, recipe, touched implementation/tests/fixtures and candidates. It resolves all sites and payloads against L, audits reachability and missing controls, and publishes the pack. It need not execute every red cycle to bind; it must have sufficient ordinary execution/fixture evidence to specify the exact oracle. Unproven controls remain higher-tier. Ambiguity is never delegated downward.
3. The binding report partitions the complete PC/variant inventory into eligible and higher-tier sets, records the missing-control audit, and states any remaining judgement or prerequisites. It makes no claim that pending PCs passed. An intact binding with an eligible set and no unresolved triage uses `next: mutation` with `scope=mechanical-execution`, O/L and the pack reference in the handoff. Findings or unresolved scope use CARD-0478's caller-triage `next: decide`; the report may still supply an independently executable eligible subset.
4. After the binder is terminal, restored and all its commands have exited, the caller reads its full report and commissions **Role Mutation, Low**, for the explicit eligible IDs using a fresh SourceLanding snapshot at the same L. This is the actual cheap execution dispatch. The caller commissions any named higher-tier subset or diagnosis separately, at the same source when applicable. Workers never sub-delegate.

The existing `artifact:` field accepts only a repository-relative `docs/**/*.md` path: keep the plan there. Put the absolute external pack path, detached digest, scope and source identities in the full report and file-backed next brief; do not squeeze an external manifest into `artifact:` or rely on a truncated handoff to carry the pack.

CARD-0478 permits only one open Mutation per O. Binding, Low execution and analysis therefore run sequentially for that source. Every brief lists already completed, assigned, excluded-with-reason and still owed IDs; a subsequent snapshot cannot silently repeat the entire inventory or claim another task's evidence. The caller deduplicates by O, pack revision, scope and assigned IDs in the companion thread before creating a task. No automatic spending or model rerouting is added.

The extra higher-tier dispatch is necessary by default because Code cannot know O/L before land. Fold candidate preparation and the initial adequacy audit into Code, but retain the final post-land check even when C=L. The binder may reuse recorded evidence only when its exact source, fixture/environment identities and case set remain applicable; record the basis. Each actual executor still gets a fresh baseline. If C differs from L, revalidate the complete pack at L, including tests/configuration/build inputs, not just whether the patch applies.

This gate delays the background battery, not Review, land, deploy or the original card's shipped verdict. A binding failure remains an open verification obligation; it cannot turn pending verification into clean.

### D-3: The bound pack has six mandatory parts

The owner document's template must include the following fields. These are required data categories, not literal placeholder values permitted in a dispatch.

| Part | Required contents and invariant |
|---|---|
| Identity and classification | Contract version; original/verification card GUIDs; Code and binding task IDs; O/C/L; plan/recipe revision and digest; pack ID/revision/digest; complete PC/variant IDs mapped to guards; `mechanically-eligible` or `higher-tier-required` with named reasons and owning next action. Execution status is separate from eligibility. |
| Exact mutation | For each variant, a unified diff or byte-preserving before/after replacement; payload SHA-256; repository-relative paths; target symbol and advisory line range at L; unique context; expected match count for each edit (normally 1); exact allowed changed-file set; Git blob IDs plus preimage and predicted mutant byte SHA-256 for each file. No wildcard path discovery, fuzzy hunk matching, three-way application, whitespace relaxation, regex mutation or inferred adjacent edit. |
| Resolved execution manifest | Working directory relative to the confirmed snapshot root and actual directory identity in the run record; exact test project path; shell/executable/argument vector and copyable full command for every build/test/restore verification phase; SDK/configuration/target framework; exact methods/filters; expanded argument cases and outcome counts; prerequisite commands/expected responses; environment values or approved secret-reference names; output paths, schedules, repetition counts and resource ownership/isolation. No credential bytes in the pack. |
| Decisive oracle | Per negative vector: full method plus argument-case identity, assertion site/expression and exception/error type, exact expected and defect-induced actual values or stable error code/message fields. Baseline, mutant and restored outcome for every selected case, including green companions. Expected exit codes, executed/failed/passed/skipped counts and exact acceptable failure set. |
| Reachability | Guard and entry path; every known earlier/parallel guard, cache, fixture default, feature flag, reservation, backend capability, assertion ordering or schedule that could mask the defect; how each is neutralized while keeping unrelated safeguards; source/fixture reference and observable ordinary evidence. Record equivalent-mutant risks and why the mutation changes the protected behavior. A sentence saying 'should reach' is insufficient. |
| Restoration and evidence | Exact backup/hash/restore procedure; allowed mutation state after each step; forced rebuild and loaded output identity checks; fresh baseline/red/restored directories; command/exit/result/trace inventory; task-owned outputs and process cleanup boundaries; final HEAD/index/source/hash checks; interrupted-run recovery and outstanding paths. |

Multiple files can belong to one variant when the defect requires them and all edits are literal and independently checked. Expected match count greater than one needs an explicit list of intended contexts; the executor must not choose among matches. Independently bypassable guards remain distinct PCs, with separately named variants; do not hide several defects behind one aggregate red count.

Bind all compile-relevant tracked inputs through L and additionally record raw checkout hashes for mutated files, tests, fixtures, project/build/configuration files used by the recipe. Git blob hashes alone do not describe CRLF bytes on Windows. Record checkout line endings/encoding and toolchain; a different raw preimage requires rebinding, never automatic normalization by Low. Reject an untracked or ignored file that unexpectedly participates in compilation or fixture setup. External backend source/binaries/protocols need their own repository SHA, file hashes and declared ownership; L does not bind another repository.

An apply command first checks every preimage and context without writing. After exact application it checks the changed-file set, every predicted mutant hash, and unchanged closure inputs before building. Partial application or changed output outside the allowed set triggers restoration and escalation. Never use `git apply --3way`, a whole-worktree reset, or `git checkout .` as a recovery shortcut. The binder supplies the precise apply/restore commands; the Low worker does not write a mutation script.

### D-4: Resolve paths and parameter cases without moving the binding problem to Low

A bound pack is reusable across fresh snapshots of L, but the eventual task's absolute path/ID is not known when the binder settles. Separate immutable source binding from deterministic run instantiation:

- The binder fixes every semantic value: project, relative working directory, arguments/filter, file set, cases, oracle, build configuration and prerequisites.
- Permit only declared location/identity tokens: `snapshotRoot`, `evidenceRoot`, `taskId`, `runId`, `pcId` and `phase`. A literal instantiation procedure substitutes these from the accepted SourceLanding task and the caller-supplied unique evidence directory. Repository paths use `/` in the artifact; expanded Windows filesystem/configuration paths use backslashes. The documented alternate `OutputPath` argument still ends in a forward slash.
- Instantiation writes a resolved manifest before baseline containing canonical absolute paths and the exact argv/working directory for each command. Check containment and root/worktree registration; no `..`, unexpected links or external source targets. Unknown tokens, missing values or an empty results path refuse execution. Selecting a project/filter, changing a schedule, finding a symbol or inventing an environment value is not instantiation; it requires the binder.
- Preserve both immutable pack digest and resolved-manifest digest with every phase. A new task/run/attempt gets a new non-existing evidence directory. Do not overwrite a failed phase, reuse an old TRX, or allow two runs to share build outputs.

Each parameterized method is expanded into stable argument identities and expected counts. Exact method filtering may necessarily select several argument cases; that is acceptable only with the complete matrix. If individual case filtering is unsupported, run the exact method and compare all its rows. Do not invent selector syntax or widen to a class. Actual execution results must establish identities/counts; `--list-tests` is not scoped execution evidence on this repository's pinned runner.

Example of the required outcome shape (illustrative only, not an executable PC for this card):

| Case for one exact method | Baseline | Mutant | Restored | Decisive mutant condition |
|---|---|---|---|---|
| negative: foreign owner | Pass | Fail | Pass | `ShouldAssertException` at `result.Allowed.ShouldBeFalse()`, expected false, actual true |
| companion: matching owner | Pass | Pass | Pass | No failure; still exercises allowed behavior |
| companion: missing prerequisite | Pass | Pass | Pass | Existing refusal/code preserved |

For this example each phase executes exactly three cases, zero skipped. Mutant has exactly one declared failure and two passes. Another failed case makes the run unexpected even if the named assertion also fails. Binder-issued dynamic values may be compared only through an explicit equality relation to a recorded fixture identity; blanket regexes such as 'any exception', broad stacktrace matches or ad hoc output normalization are forbidden. Normalize only predeclared incidental paths/line endings/identifiers, preserving the semantic assertion and code.

### D-5: Low executes one prescribed state transition at a time

Default to one PC variant per cycle. Batching/sharding is optional only when the binder explicitly supplies independence evidence, separate file/method sets, complete case accounting, exact schedules/output ownership and restoration for each member. A large PC count is not permission for Low to invent concurrency. Keep the testing owner's sequential assembly and isolated process/DB rules.

Required cycle, with every command foreground-owned and awaited:

1. **Preflight:** verify SourceLanding publication identity O and HEAD=L, pack/manifest digests, clean tracked/index state, no sequencer or running owner command, known untracked/ignored inventory, preimage/closure hashes and prerequisites. Record original bytes in a task-owned backup outside source plus their hashes. The backup and the live source must both match the bound preimage before mutation. Failure stops dependent work.
2. **Baseline:** execute the supplied forced build and exact test commands with fresh evidence. Verify build success, actual produced and loaded assembly/executable path and hash/build identity, all expected case IDs and all baseline outcomes. A pre-existing red, skip, zero selection, fixture error or missing prerequisite is not permission to apply a mutant.
3. **Mutant:** check all contexts/preimages, apply only the supplied payload, verify predicted mutant hashes/allowed diff and unchanged dependencies, force rebuild, and run the prescribed commands/schedule. The exact case and failure sets must match the manifest. Build/setup failure, unrelated failure, unacknowledged barrier, stale binary or wrong selection is invalid evidence, even with a nonzero exit. A normal timeout is not an oracle; a control intentionally testing a timeout must name the asserted domain timeout result and completed fixture schedule, never a hung test host.
4. **Restore in a finally-equivalent cleanup path:** await owned commands, replace only named mutated paths from verified pre-mutation bytes, verify every restored byte hash equals the bound preimage, and check the remaining source/index against L. The operation runs after a failure too. If bytes have changed outside the recorded mutation state or writer ownership is uncertain, do not overwrite unexplained edits; preserve the snapshot, identify the affected paths and request higher-tier recovery.
5. **Restored run:** perform the explicit forced rebuild of the entire affected project graph with the same isolated configuration, then the same exact cases/schedules in a fresh directory. Record fresh built/loaded binary identity and passing counts. Timestamp refresh may accompany rebuilding but cannot replace this contract's forced rebuild. A matching source diff alone does not exclude CARD-0412's stale mutated DLL.
6. **Settle:** await all children; record final HEAD=L, clean index/tracked state, closure/preimage hashes, known owned outputs and restoration verdict. Preserve evidence outside the snapshot for CARD-0478's guarded cleanup. Never commit, push, land, deploy, weaken an assertion or retain a production/test repair from this task.

The bound commands must force the relevant graph to rebuild after both mutation and restoration, not use `--no-build` on an old output. A runner-only invocation may use a just-built binary only when that exact build/path/hash is recorded and verified. The source-to-output chain includes the loaded production assembly as well as the test DLL and any external backend. Logs stating 'build succeeded' or merely newer timestamps are insufficient provenance by themselves.

### D-6: Make rejection and escalation explicit per PC

Every planned PC and variant appears once in the classification inventory. Eligibility means all six parts are resolved and the executor has no engineering choice left. A candidate designation is not eligibility. The higher-tier row names all applicable reasons, the responsible role/tier and the work needed to resolve them. Use stable reason names with specific detail:

| Reason | Higher-tier responsibility / disposition |
|---|---|
| `binding-missing` / `source-mismatch` / `artifact-mismatch` | Bind the missing recipe or repair provenance at actual O/L; never infer sites or accept another SHA. |
| `patch-unresolved` / `patch-mismatch` | Resolve literal payload/context/preimage or changed-file mismatch at L. Low restores any partial mutation and stops. |
| `selector-unresolved` / `selector-drift` | Establish exact executed case set; repair manifest or tests in appropriate work. Never accept nonzero total alone. |
| `oracle-unresolved` / `unexpected-red` | Specify or diagnose exact failing assertion and companion outcomes; do not bless generic nonzero/build/setup failures. |
| `reachability-unproven` / `surviving-mutant` | Investigate masking, equivalence, schedule and adequacy. CARD-0470 PC-8 is the reference counterexample. |
| `backend-race-unproven` | Higher-tier/backend owner establishes real isolated source/binary/protocol, barriers, process identities and acknowledged trial evidence. A fake or sleep-based schedule cannot stand in. |
| `prerequisite-missing` / `baseline-red` | Resolve environment, permissions, build/setup or ordinary regression; retain unexecuted IDs. No automatic production restart or provider spending. |
| `stale-output` / `restore-mismatch` / `restored-red` | Establish source/output identity and restore/recovery; preserve contaminated state when ownership is uncertain. Never continue to another mutant on an unverified restore. |
| `adequacy-gap` | Discover/design a missing control or repair missing detection in separate authorized TestDesign/Code work. |

High is the default diagnosis/execution escalation; Frontier handles binding/adequacy and unresolved safety/backend judgement. An eligible concurrency or multi-file PC can run at Low when its mutation, schedule, oracle, isolation and evidence are fixed. CARD-0461 PC-83..85 are examples of named higher-tier prerequisites until qualified, not permanently excluded IDs and not waived verification.

On any unexpected result Low stops applying further mutants, preserves raw evidence, restores and attempts the prescribed restored verification when safe, then reports exact observed versus expected and remaining IDs. It never diagnoses a survivor by editing tests, tries nearby patches, loosens an oracle, adds retries or silently changes tier. The caller explicitly commissions the higher-tier next action. A new pack revision is required for changed patch, selector, oracle, fixture or prerequisite semantics. Source/test repairs need ordinary Code/Review/land and a new O/L battery; do not relabel old evidence as the new SHA.

Use CARD-0478's report semantics: a completed finding/triage report uses `next: decide`; missing operational evidence or interruption uses truthful failed/blocked with the specific prerequisite and recovery facts. `decide` is caller triage, not an automatic operator-permission request. A restored completed subset can use `next: none`, but its first line and totals must say `assigned subset complete; full battery pending` when anything remains. The caller closes the verification card only when every PC/variant, the adequacy audit and restoration obligation are accounted for. No new status/token or automatic reopening of the shipped card.

### D-7: Keep missing-control discovery and adequacy at the higher tier

The post-land binder performs the existing 'find one more control' duty, including zero-PC recipes, with the final touched guard inventory and ordinary Review report. Record inspected guards and each discovered control under a new stable ID with a recipe/patch/oracle/classification. When no missing guard exists, supply an explicit inspected-inventory rationale; do not invent a redundant PC to make the count nonzero. Missing detection or an untestable seam is a finding, not a Low task.

This audit does not replace Review's pre-land ordinary-test/coverage judgement. It discharges the discovery part formerly mixed into every expensive Mutation execution. Low reports a noticed gap as an observation and escalates; it is never charged with finding a new mutation or certifying coverage completeness. Zero eligible PCs produce no empty Low spend: the higher-tier audit reports the disposition and outstanding higher-tier work.

The final verification record keeps separate counts: planned/added variants; eligible and higher-tier-required; assigned/executed/killed/survived/invalid/unexecuted; baseline/restored case totals; audit complete or pending; final restoration. These distinguish cheap execution success from full battery completion and test adequacy.

### D-8: Low is an actual default with explicit higher-tier exceptions

Set `DelegationSettings.RolePolicy["Mutation"]` to `Level=Low`, `EscalateTo=High`, retaining WIP, timeout and disabled timed auto-escalation behavior. Do not change Code/TestDesign/Review policies. Update the corresponding independent-policy test. The default is safe only with the fail-closed execution instructions: an unclassified legacy recipe cannot be interpreted at Low. Its first action is refusal and a named binding request.

The orchestration recipe selects Low for a declared eligible subset and explicitly selects Frontier for binding/adequacy or High/Frontier for named analysis. Use the existing tier enum rather than vendor model names; a provider may map Low to a different alias. Stage bundles state tier policy without naming vendors.

Existing configured RolePolicy values, Human pins, complexity matrices, quota/holds and provider availability continue to take precedence as documented. Neither this card nor its rollout clears or rewrites live pins. The caller checks the accepted task's actual level/scope; a binding task routed to Low refuses judgement work. A required higher pin on eligible execution is valid but must not be reported as realized Low-tier savings. Routing exhaustion/authentication refusal remains visible; never silently substitute a provider or override a Human pin. Keep WIP/capacity semantics unchanged.

Rejected: only change bundle prose (fallback remains Frontier); force every task at Low regardless of classification; add a new role/model picker; automatically escalate on elapsed time or on a generic red result; bulk-migrate operator routing state.

### D-9: Evidence, costs and rollout stay honest

Per-PC evidence uses a unique external run directory with `baseline/`, `mutant/`, `restored/` below each PC/variant/attempt. Preserve pack and resolved-manifest digests, O/L, exact payload/diff, source hashes before/after/restore, command argv/cwd/environment-reference names, timestamps, build logs/output identities, test raw logs/TRX, exits, exact result matrix, reachability/schedule traces, and owned process/output inventories. The summary includes paths and hashes; console excerpts cannot replace retained artifacts. Do not publish secrets, provider home content or private transcripts in evidence.

TestDesign costs distinguish Code authoring + ordinary V/R + candidate preparation, ordinary Review, post-land binding/adequacy, eligible PC execution, higher-tier PCs/diagnosis, and final aggregation/restoration. Give suite/filter-backed numeric floors and label estimated/measured. Compiler/test minutes are not removed by a cheaper model. Binding overhead can outweigh savings on tiny batteries; say so. Model savings require measured tokens/rates or a stated estimate, not a lower wall-clock claim.

Roll out only on top of working CARD-0478 SourceLanding/no-autosave/no-land/cleanup behavior and coherent active instructions. Historical plans/reports are not rewritten. Let active cycles finish/restore; do not interrupt them to retrofit eligibility. Legacy or incomplete recipes default to higher-tier binding, not Low experimentation. Verify loaded bundle versions and effective Mutation default after the caller's canonical deployment; healthy HTTP alone is insufficient. This Plan task performs no runtime deployment, routing change, card move or task dispatch.

## Implementation slices

| Slice | Files and change | Validation required |
|---|---|---|
| S1: Detailed owner and recipe shape | New `docs/mechanical-pc-contract.md`; links in `docs/testing-and-build.md` and `docs/orchestration-loop.md`. Include the six-part template, two-artifact lifecycle, copyable binding/execution checklists, exact failure matrix, token instantiation and evidence/recovery rules. | TestDesign defines a complete representative recipe/pack example and a malformed-input review matrix; audit links and require each brief mode to identify its owner. Examples must be clearly illustrative rather than fabricated executed evidence. |
| S2: Stage instructions | `server/Bundles/stage-test-design.md` and `stage-mutation.md` are primary changes. TestDesign mandates recipe/variant/case/oracle/reachability/classification and post-Code/L binding. Mutation defaults to Low only for bound eligible IDs; higher-tier modes retain binding/discovery/diagnosis. Update `stage-code.md` for candidate preparation and `stage-review.md` for ordinary adequacy and pending post-land PCs. | `InstructionBundleTests`: normalized ASCII/size/composition budget and role-composed obligations; preserve delivery inventory, unique guard mappings, V/R execution, precise PC scope, restoration and CARD-0478 report order. |
| S3: Default routing | `server/Application/Settings/DelegationSettings.cs`; `tests/Antiphon.Tests/Application/MutationRoleContractTests.cs`. Default Low, manual escalation High, independent unchanged WIP/timeouts. | Default-policy test plus actual no-pin task creation at Low, explicit binder at Frontier, and existing Mutation routing/pin precedence. Reuse `MutationAdmissionTests`/routing fixtures after reading their helpers; do not call live provider APIs. |
| S4: One orchestration recipe | Mutation/tier sections of `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`, `server/Bundles/delegate-basics.md`, `server/Bundles/README.md`, `.claude/skills/antiphon-delegate/SKILL.md`. Extend CARD-0478's continuation with higher-tier binding then eligible Low subset, serial O admission, remaining-scope accounting and final audit ownership. Read the skill/owner before editing. | Cross-file audit excludes historical artifacts; no active command asks Low to find mutation sites/missing controls, no pre-land final-SHA demand, no old retained-Code/default-Frontier instruction, no snapshot commit or automatic caller triage spend. Preserve routing overrides and command ownership. |
| S5: Focused acceptance and handoff | Tests next to bundle/default/routing fixtures; a durable worked execution acceptance record after the change's own land. Reuse CARD-0478 snapshot/report/cleanup behavior. | TestDesign supplies exact V/R/PCs for changed contract/default guards and a bounded literal execution rehearsal. Real source mutation acceptance remains post-land. Report policy tests and human/agent protocol rehearsal as different evidence. |

No new migration, general mutation engine, JSON admission parser, autonomous scheduler, card status, task role, `PipelineHandoffKind`, provider alias or arbitrary base-ref picker is required. If implementation finds a missing snapshot mechanism, route that to CARD-0478/Plan rather than adding manual reset/cleanup recipes. If a proposed full machine validator is desired, it is separate scope; these contract checks must not be reported as server-enforced admission.

## TestDesign handoff

Append `## Verification design` after inspecting the affected test bodies/helpers on the integrated CARD-0478 checkout. Do not treat this acceptance list as already executable verification. Define V/R IDs, guard inventory, distinct PC/variants, exact assertion/case expectations and cost floors. The card's own future PCs follow D-1: design now, final binding after its Code/Review/land.

Required acceptance cases:

1. Composed TestDesign requires all six recipe parts, distinguishes proposed code from bound payloads, preserves full guard/variant accounting and the asynchronous delivery inventory. Composed Code prepares candidates and runs ordinary V/R; Review precedes land; Low Mutation is not assigned missing-control discovery. Required linked owner content actually exists.
2. All stage bundles remain ASCII and <=2,500 normalized characters; actual role compositions stay within the existing command-line budget. `InstructionBundleTests` must pin semantic requirements without depending on one incidental paragraph order. Preserve the catalog and role/token ordinals.
3. The runtime fallback becomes Low/manual High; exact no-pin create requests use it. Explicit higher-tier binding remains higher-tier, Code policy stays independent, required pins and complexity routing are preserved, and no timed automatic escalation is enabled. Read `MutationAdmissionTests` and relevant routing fixture helpers before choosing the narrow filters.
4. Exercise the binding checklist on C=L, C rebased to L and R later than L. Candidate patches/hash claims at C never suffice for changed L; later R never substitutes. A checked-in candidate with no final envelope is ineligible. Missing payload/pack digest, altered preimage, wrong encoding/line endings and external backend identity drift all stop before a mutant.
5. A worked exact replacement/diff applies once, produces the expected byte hash and only the allowed paths, then restores byte-for-byte. Duplicate context, absent context, an extra file, a partial multi-file application and changed fixture/config input refuse or restore with explicit mismatch. Use disposable source/fixtures, not live stack configuration. Show Low needs no source search or diff authoring.
6. A parameterized exact-method example executes its enumerated negative and companion rows. The intended failing row plus an unexpected companion failure is rejected. Also reject zero results, missing/extra/duplicate cases, skips, build/setup failure, unrelated assertion, stale TRX and wrong binary, even when exit status superficially matches. Evidence needs actual executed identities, not discovery counts.
7. The guard-reachability review exposes CARD-0470's reservation-window mask. A masked mutant is not a valid green conclusion or proof of a missing assertion; it is a higher-tier finding. Include an ambiguous/equivalent mutant disposition and a missing-control audit with both an added PC and a justified zero-addition inventory.
8. Interrupt after application and after red; demonstrate the prescribed restore/evidence steps, byte hash checks and forced restored build. A source-clean diff with stale mutated output cannot pass. Unknown concurrent edits/process ownership retain the tree and report exact paths. Repeating a phase produces a new attempt directory, never overwrites evidence.
9. Qualify a mature barrier-controlled concurrency example from existing test evidence, with exact mutation/schedule/oracle and green success companion. Contrast the review disposition of unproven real backend PC-83..85 prerequisites without attempting those live destructive tests for this instruction card. Do not relabel mock evidence as real process validation.
10. Bind, settle, then commission the eligible subset; no same-O overlapping tasks. A subset completion or binder success does not close full verification while a higher-tier row/audit remains owed. Survivor/unexpected-red reports preserve restoration/evidence and route caller triage without snapshot repair or resuming the shipped card. Reuse CARD-0478 coverage for source admission/cleanup; add tests only where CARD-0479 changes behavior.

Use focused TUnit class/method filters through `dotnet run --project tests/Antiphon.Tests` and the testing owner's isolated-output procedure. Likely ordinary surfaces are `InstructionBundleTests`, `MutationRoleContractTests` and affected methods of `MutationAdmissionTests`; add routing/composition tests only for changed behavior. No full assembly, Pty co-run, client/browser suite, production runner, real provider or external backend mutation is implied. Actual temporary mutants are method-scoped, with every baseline/red/restored case and assertion counted.

Protocol rehearsal is evidence of a worker following the supplied contract, not proof of a server-side validator that this plan does not build. Record transcript/report or retained command evidence for a controlled Low execution if commissioned after landing, with its actual tier. Separate TestDesign must price this acceptance and the higher-tier binding explicitly; no measured savings or pass counts are claimed by this Plan.

Plan completion is this committed/pushed artifact. Active bundles, routing defaults and execution artifacts belong to Code after separate TestDesign and integration with CARD-0478.

## Verification design

Added by TestDesign on 2026-10-10 (task 792ccb43) against master `e27021e31234def34e29d231a6bb7c33b9a25a6d`, where the plan above already sits (commit `717f5cfa4`). The fix design (D-1..D-9, S1..S5) is unchanged. Nothing below ran: Mutation is paused by the operator, so every PC stays pending post-land and every number is an estimate.

**Staleness found against current master (listed, not rewritten):**

1. Ground truth and D-2 say CARD-0478's runtime changes were not implemented at `7123a71b`. They are now: SourceLanding admission (`PostLandMutationAdmissionTests`), snapshot custody, the companion recipe in `docs/orchestration-loop.md` (section "Code, ordinary Review, Land, then Mutation (CARD-0478)"), the restoration contract in `docs/testing-and-build.md`, and the active `stage-code`/`stage-review`/`stage-mutation` bundles. The integration precondition for D-2 is met; Code applies D-2 directly.
2. CARD-0604 added two Mutation producers (`windows-job-v1`, `linux-cgroup-v1`) and the CARD-0451 section now prescribes `scripts/run-checkpoint.ps1` per PC phase (exit 0 green, 1 named red, 2 build/TRX invalid, 3 roster/zero). D-3's execution manifest and D-4's oracle must use that driver's `CHECKPOINT`/`EXECUTED`/`FAILED` lines as the phase transport; the owner document says so explicitly.
3. `PostLandMutationContractTests` (30 results) now pins active CARD-0478 phrases in the Mutation composition, `docs/orchestration-loop.md` and `SKILL.md`. S2/S4 edits must preserve every pinned phrase (R-5). The plan's S2 names only `InstructionBundleTests`.
4. `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc` requires the Mutation composition to contain `missing PC`; D-7 moves discovery to the higher-tier scope, so the scoped sentence must keep those words (R-6 pins the scoping).
5. The Tiers tables in `docs/orchestration-loop.md` and `.claude/skills/antiphon-delegate/SKILL.md` both still carry `| Mutation | ... | Frontier |`; S4 changes both (V-7/R-6).
6. `docs/superpowers/mutations/` does not exist yet; S5 creates it on first candidate. No test depends on it.
7. The external root named in D-1 is now concrete: `<canonical-common-git-dir>/antiphon/verification/<O:N>/<task:N>/` (runner: `/work/repos/antiphon/.git/antiphon/verification/<O>/<task>/`). Binding packs live under `<that root>/packs/<revision>/`.

### Inspection

| Bodies read | Boundaries -> V/R or exclusion |
|---|---|
| `DelegationSettings.RolePolicy` defaults (lines 343-365), `RolePolicyEntry` (`Level`, `EscalateTo`, `EscalateAfterMinutes`, `TimeoutMinutes`), `AgentModelLevel` (Frontier=0 .. Low=3); `AgentTaskService.ResolveLevel` (explicit wins, then policy, then `DefaultLevel`), `ResolveEscalationTarget` (explicit target, else `EscalateTo`, else one rung), `ComplexityRoutingService.ResolveAgainstRolePolicy` | V-1, V-3, V-4; R-1..R-3. `ResolveLevel` is the single seam a no-pin create and an explicit `ModelLevel` both pass through; chains/pins run before it (`CreateAsync` lines 958-1065). |
| `MutationRoleContractTests` (2 methods; `C470_mutation_policy_is_independent` pins Frontier, RecommendedInFlight 1, ceiling 240/73/91, `RoutableRoles`) | V-1/R-1 by editing that method, not adding a second copy of it. |
| `MutationAdmissionTests` (`C470_mutation_routing_does_not_inherit_code`, `AssertRouted`, `CreateService`, `ManualCaller`, `SeedTaskAsync`, `TempWorkspace`, isolated schema) | V-3, V-4, R-2. `SeedTaskAsync` leaves `ModelLevel` at enum default Frontier(0): the escalation seed must set Low explicitly. `CreateService(db)` already wires `RecordingSessionStopper`, which `EscalateAsync` needs. |
| `AgentTaskServiceIntegrationTests.escalating_moves_one_rung_up_the_ladder` and `..._from_the_top_of_the_ladder_is_refused` (class is `[NotInParallel("AgentQueue")]`, 2,300 lines) | Pattern only. The new escalation case goes in `MutationAdmissionTests` to keep the filter narrow and the DB schema isolated. |
| `AgentTaskStallEscalationTests.the_shipped_default_arms_no_role_at_all` (iterates every `RolePolicy` entry; a seeded quiet Debug task prevents a vacuous pass) | R-3 as-is: after S3 Mutation carries `EscalateTo` with null `EscalateAfterMinutes`, exactly the Debug/Test shape. |
| `InstructionBundleTests` (54 methods, 74 results): size cap 2,500 ASCII per stage bundle, vendor-name bans, `stage_bundle_invariants_are_pinned_by_substring`, `C470_composed_roles_separate_vr_from_pc`, `C467_V21_DeliveryInventoryAndReviewAreMandatory`, `the_worst_case_composition_measured_sits_far_under_the_budget` (every role x kind with board-api + style, 500-char headroom), `the_test_design_composition_is_past_the_batch_command_ceiling` (>= 8,192), `delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`, `C1015_SourceLanding_keeps_its_external_evidence_exception` (slice between `SOURCELANDING MUTATION EXCEPTION:` and `- COMMIT AND PUSH`), `C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders`, `the_catalog_holds_exactly_the_bundles_that_ship` | V-2, R-4. Current sizes: test-design 2,477, review 2,481, code 2,406, mutation 2,362. S2 has 23/19/94/138 spare characters respectively; Code must trim before adding. The owner doc is under `docs/`, so the catalog list is untouched. |
| `PostLandMutationContractTests` (29 methods, 30 results; composition and repo-file pins; `RepoFile` walks up to `AGENTS.md`) | V-8/R-5. Reused unchanged. |
| `CheckpointManifestDocumentationTests` (7 methods; pins the exact `| CP | After | ... | EstimatedMinutes |` header inside `stage-test-design.md` and `### Checkpoints` in `stage-code.md`) | R-8: an S2 trim that drops either header line is caught here. `DelegateScriptRunner.RepoRoot` is the repo-file locator for the new doc test class. |
| `DelegateBundleLaunchTests.C470_mutation_claude_and_codex_launch_contract` (2 results; stage-mutation once, before basics, no read-only flags) | R-7. |
| `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests` + `ScriptHarness.RunHarnessCaseAsync(harness, prefix, case, expectedRows, requiredRows)` (`C487 HARNESS EXIT CODE: 0`, `<prefix>: N passed, 0 failed, N rows`, one row per named assertion), `scripts/test-run-checkpoint.ps1` (`-Case`) | V-9..V-12/R-9 reuse this harness protocol for the contract rehearsal; `pwsh 7.5.4` is on the runner. |
| `docs/testing-and-build.md` "Mutation-stage positive-control execution (CARD-0451)", "Verification restoration contract (CARD-0478)", "Checkpoint manifest (CARD-0585)"; `docs/orchestration-loop.md` Tiers and CARD-0478 sections; `server/Bundles/orchestrator.md` lines 150-160, `README.md` lines 88-96, `delegate-basics.md` SourceLanding exception; `SKILL.md` lines 52-150 and 391-490 | V-6, V-7, R-6. `restoration.json` keeps `schemaVersion` 1; the pack is additional external evidence and does not change that record. |
| CARD-0470 plan "PC-8 fixture repair evidence" (`C470_retained_worktree_launch_is_fresh`, `PoolReservedForCallerMinutes` mask) and CARD-0461 G/PC-83..85 rows | V-6 worked examples are quoted from these, labelled illustrative. |

**Missing setup recorded.** (a) No test reads a fenced PowerShell block out of a doc today; the rehearsal harness introduces that (labelled blocks, see V-9). (b) `MutationAdmissionTests` has no `ModelLevel` argument on `SeedTaskAsync`; set the property on the returned entity and save. (c) No fixture for a bound pack exists; the harness builds one in a temp directory from text files, never from a dotnet build.

### Delivery inventory

CARD-0479 adds no asynchronous delivery path. Its runtime change is one settings default; the rest is instruction text. The binding -> Low execution -> analysis sequence is caller-commissioned over CARD-0478's existing paths (land outcome -> caller, accepted SourceLanding task -> worker, Mutation settlement -> caller), each already carrying producer, durable identity (Code task ID + O + L, Mutation task ID + O/L, stored Result digest), crash matrix and complete UserPrompt receipts in `PostLandMutationDeliveryTests.C478_V09a/b/c`. Those tests are not rerun here because no line on those paths changes; if Code touches them, add the class to CP-5's row with reason. Substitute declared: the contract rehearsal (V-9..V-12) proves a worker following the literal snippets reaches the named refusal or restore; it cannot prove a Low model obeys the fail-closed instruction, nor that a provider maps `Low` to a particular alias. That evidence is the caller-owned post-land record (V-13), recorded with the task's actual tier.

### Proves it works now

| ID | Behaviour | Layer | Test / command | Expected |
|---|---|---|---|---|
| V-1 | Shipped Mutation policy is Low, `EscalateTo` High, `EscalateAfterMinutes` null, RecommendedInFlight 1, ceiling 240; Plan/TestDesign/Code/Review stay Frontier before the independence mutation | Unit | `MutationRoleContractTests.C470_mutation_policy_is_independent` (edited in place: assert the shipped Frontier for Code/TestDesign/Review/Plan first, then Low/High/null for Mutation, then the existing ceiling and `RoutableRoles` checks) | 1 passed |
| V-2 | Every stage bundle stays ASCII and <= 2,500; worst-case compositions stay under the budget with headroom; TestDesign >= 8,192; VR/PC separation, delivery inventory and the CP header survive S2 | Unit | `/*/*/InstructionBundleTests/*` | 74 executed, 0 failed |
| V-3 | No-pin, no-chain Mutation create resolves `ModelLevel` Low, `AgentKind` ClaudeCode, `RoutingPinId` null; the same request with `ModelLevel: Frontier` persists Frontier; a no-pin Code create persists Frontier | Integration (isolated schema) | `MutationAdmissionTests.C479_V03a_NoPinMutationCreateResolvesLow`, `C479_V03b_ExplicitFrontierMutationCreateStaysFrontier` (reuse `AssertRouted`'s read-back shape; use `Request(...)` without `Complexity` so no chain applies) | 2 passed |
| V-4 | Manual `EscalateAsync(id, null)` on a Failed Low Mutation task returns `ModelLevel` High, `EscalatedFrom` Low, Queued; a second call from High returns Frontier; a third throws `ConflictException` "top of the ladder" | Integration | `MutationAdmissionTests.C479_V04_ManualEscalationOfLowMutationTargetsHigh` | 1 passed |
| V-5 | Composed Mutation names the three scopes (`binding`, `mechanical-execution`, `higher-tier-analysis`), the two classifications (`mechanically-eligible`, `higher-tier-required`), refuses at Low on `binding-missing`/missing pack digest, and carries the exact sentence `Low never locates sites, authors diffs or discovers controls.`; discovery is stated only inside the binding/higher-tier-analysis sentence; composed Code carries `docs/superpowers/mutations/`, `candidate` and `unbound` plus the existing `pending for Mutation`; composed Review carries `adequacy` plus the existing `PCs may remain pending` | Unit | `MechanicalPcContractTests.C479_V05_MutationBundleFailsClosedAtLow`, `C479_V05b_CodePreparesUnboundCandidates`, `C479_V05c_ReviewJudgesAdequacyWithoutMutants` | 3 passed |
| V-6 | `docs/mechanical-pc-contract.md` exists and contains: `Contract version: 1`; the six part headings of D-3; the six tokens `snapshotRoot`, `evidenceRoot`, `taskId`, `runId`, `pcId`, `phase`; all 17 reason names of D-6; `git apply --3way`, `git checkout .` and `whole-worktree reset` under a "never" sentence; the six cycle steps Preflight/Baseline/Mutant/Restore/Restored run/Settle; driver exit semantics 0/1/2/3; five labelled snippets `# mechanical-pc-contract v1: preflight|instantiate|apply|oracle|restore`; worked examples labelled `illustrative` for CARD-0470 PC-8 (`reservation window`), an `equivalent` mutant disposition, a missing-control audit with one added ID and one `zero-addition` inventory, and a barrier-controlled concurrency qualification contrasted with CARD-0461 `PC-83` prerequisites | Unit | `MechanicalPcContractTests.C479_V06_OwnerDocHoldsTheContract`, `C479_V06b_WorkedExamplesAreIllustrative` | 2 passed |
| V-7 | Active recipes route binding/adequacy at explicit Frontier and eligible execution at Low: `docs/orchestration-loop.md`, `SKILL.md`, `server/Bundles/orchestrator.md`, `server/Bundles/README.md` each contain `binding-only`, `eligible`, `mechanical-execution` and `one open Mutation per O`; both Tiers tables' Mutation row contains `Low` and no longer ends `| Frontier |`; `docs/testing-and-build.md` and `docs/orchestration-loop.md` link `docs/mechanical-pc-contract.md`; `delegate-basics.md` MUTATION RUNNER paragraph names `bound pack` and keeps `method-scoped` | Unit | `MechanicalPcContractTests.C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow` (one `[Arguments]` per file, 5 results) | 5 passed |
| V-8 | CARD-0478 active contracts unchanged | Unit | `/*/*/PostLandMutationContractTests/*` | 30 executed, 0 failed |
| V-9 | Binding checks from the doc's `preflight` snippet on a disposable Git repo: C=L accepts; HEAD != L refuses `source-mismatch`; remote advanced to R while HEAD=L accepts; missing detached digest refuses `binding-missing`; index member hash mismatch refuses `artifact-mismatch`; CRLF preimage vs LF bound bytes refuses `patch-mismatch`; candidate hash computed at C against changed L bytes refuses `patch-mismatch`; recorded external backend file hash drift refuses `artifact-mismatch`. No write occurs on any refusal (source hash unchanged) | Integration (pwsh) | `MechanicalPcRehearsalTests.C479_V09a_BindingSource`, `C479_V09b_BindingPreimage` via `scripts/test-mechanical-pc-contract.ps1 -Case binding-source|binding-preimage` | 2 passed; rows 5 + 3 |
| V-10 | `apply` snippet: exact replacement applies once, produces the predicted mutant SHA-256 and exactly the allowed changed-file set; expected match count 1 with 2 matches refuses, 0 matches refuses, neither writes; an extra changed file restores and refuses `patch-mismatch`; a two-file variant whose second preimage fails restores the first and refuses; a changed fixture/config closure hash refuses before the build phase | Integration (pwsh) | `C479_V10a_ApplyExact`, `C479_V10b_ApplyContext`, `C479_V10c_ApplyClosure` (`-Case apply-exact|apply-context|apply-closure`) | 3 passed; rows 3 + 2 + 3 |
| V-11 | `oracle` snippet over canned driver output: exact negative + two green companions accepts; extra companion failure rejects `unexpected-red` even though the named assertion failed; zero/missing/extra/duplicate rows and skipped > 0 reject `selector-drift`; driver exit 2 rejects `prerequisite-missing`; exit 3 rejects `selector-unresolved`; pre-existing results directory, `buildSource=unknown`, and restored output identity equal to the mutant's identity reject `stale-output` | Integration (pwsh) | `C479_V11a_OracleRows`, `C479_V11b_OracleProvenance` (`-Case oracle-rows|oracle-provenance`) | 2 passed; rows 9 + 3 |
| V-12 | `restore`/`instantiate` snippets: interrupt after apply and after red both restore byte-for-byte from the task-owned backup with every hash equal to the preimage and require the forced-rebuild marker before `restored`; an unknown concurrent edit to a mutated path is not overwritten, the tree is retained and the path is reported `restore-mismatch`; unknown token, missing value, empty results path and a `..` path refuse instantiation; a repeated phase gets a new attempt directory and an existing one refuses | Integration (pwsh) | `C479_V12a_RestoreInterrupt`, `C479_V12b_RestoreForeignEdit`, `C479_V12c_InstantiateTokens` (`-Case restore-interrupt|restore-foreign-edit|instantiate-tokens`) | 3 passed; rows 3 + 2 + 5 |
| V-13 | Caller-owned, after the canonical deployment of this change: `GET /api/version` SHA equals landed L; the first post-deploy no-pin Mutation task shows `modelLevel: Low` in `GET /api/agent-tasks/{id}`; an explicit binder create shows Frontier; composed `stage-mutation`/`stage-test-design` versions equal the embedded hashes at L | Deployed server | Caller record on the verification companion; not a Code checkpoint. Deferred while Mutation is paused. | Recorded SHA, two task levels, two bundle versions |

Harness protocol for V-9..V-12: `scripts/test-mechanical-pc-contract.ps1 [-Case <name>]` creates a temp Git repository of text files (`src/a.txt`, `src/b.txt`, `fixtures/config.txt`, `backend/identity.txt`) and a pack directory (`index.json` with member paths and SHA-256, detached `index.sha256`, one replacement payload per variant, a resolved manifest with the six tokens), extracts each labelled fenced block from `docs/mechanical-pc-contract.md` at run time, invokes it with `snapshotRoot`/`evidenceRoot` pointing inside the temp tree, and prints one `C479 PASS <case>:<assertion>` row per named assertion, `C479: N passed, 0 failed, N rows` and `C487 HARNESS EXIT CODE: 0` (the exit line `ScriptHarness.Validate` requires). Canned driver output for V-11 is a text file of `CHECKPOINT`/`EXECUTED`/`FAILED` lines, never a real dotnet run. The TUnit class is `tests/Antiphon.Tests/Scripts/MechanicalPcRehearsalTests.cs`, `[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>]`, one method per case calling `ScriptHarness.RunHarnessCaseAsync("test-mechanical-pc-contract.ps1", "C479", case, expectedRows, requiredRows...)`.

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-1 | Mutation default drifts back to Frontier, loses `EscalateTo`, or acquires `EscalateAfterMinutes` | `MutationRoleContractTests.C470_mutation_policy_is_independent`: `mutation.Level.ShouldBe(AgentModelLevel.Low)`, `mutation.EscalateTo.ShouldBe(AgentModelLevel.High)`, `mutation.EscalateAfterMinutes.ShouldBeNull()` |
| R-2 | Chain or pin precedence lost for Mutation (Low default applied over a configured chain) | `MutationAdmissionTests.C470_mutation_routing_does_not_inherit_code`: `row.ModelLevel.ShouldBe(level)` on the ClaudeCode/Medium step and the Code pin `preserved.UpdatedAt.ShouldBe(codePin.UpdatedAt)` |
| R-3 | A timed automatic escalation gets armed on any role | `AgentTaskStallEscalationTests.the_shipped_default_arms_no_role_at_all`: `.Any(p => p.Value.EscalateTo is not null && p.Value.EscalateAfterMinutes is not null).ShouldBeFalse(...)` |
| R-4 | A stage bundle exceeds 2,500, gains non-ASCII or a vendor name, or a composition overflows the command line | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` (`text.Length.ShouldBeLessThanOrEqualTo(2_500, ...)`) and `the_worst_case_composition_measured_sits_far_under_the_budget` |
| R-5 | A CARD-0478 phrase is trimmed from the Mutation composition, `orchestration-loop.md` or `SKILL.md` | `PostLandMutationContractTests.C478_V11_ComposedContractsKeepOrdinaryReviewBeforePublication` and `C478_V11_ActiveRecipeHasDurableCompanionAndExplicitContinuation` (`text.ShouldContain(contract, Case.Insensitive)`) |
| R-6 | Low re-acquires discovery or site-finding; the `binding required after Code` rule is dropped; a `| Frontier |` Mutation tier row returns; the sentence scoping discovery to binding/higher-tier-analysis is removed | `MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow`: `mutation.IndexOf("Discover").ShouldBeGreaterThan(mutation.IndexOf("higher-tier-analysis"))`, `design.ShouldContain("binding required after Code")`, `Regex.IsMatch(tiers, @"\|\s*`Mutation`[^\n]*\|\s*Frontier\s*\|").ShouldBeFalse()` for both files |
| R-7 | Mutation launch composition changes (stage bundle twice, after basics, or read-only flags) | `DelegateBundleLaunchTests.C470_mutation_claude_and_codex_launch_contract`: `text.Split("[bundle:stage-mutation v").Length.ShouldBe(2)` |
| R-8 | S2 trimming removes the Checkpoints header from `stage-test-design.md` or `### Checkpoints` from `stage-code.md` | `CheckpointManifestDocumentationTests.the_checkpoint_phrases_are_pinned_in_every_copy` |
| R-9 | A later doc edit weakens a contract snippet (any check removed) | `MechanicalPcRehearsalTests.*`: `passed.Count.ShouldBe(expectedRows, ...)` and each `requiredRows` row |

### Guard inventory

Safety-critical here means a guard whose silent loss either lets a Low executor make an engineering choice, lets a mutant be judged on wrong source/output, or changes the tier/escalation that keeps judgement work off Low.

| ID | Plan ref + guard/invariant | PC |
|---|---|---|
| G-1 | D-8: a no-pin, no-chain Mutation create resolves Low | PC-1 |
| G-2 | D-8: manual escalation of a Low Mutation task targets High via `EscalateTo`, not rung-counting | PC-2 |
| G-3 | D-8: no timed automatic escalation is armed on Mutation (`EscalateAfterMinutes` null) | PC-3 |
| G-4 | D-8: Code/TestDesign/Review/Plan defaults stay Frontier; Mutation policy is independent of Code | PC-4 |
| G-5 | D-2/D-8: an explicit higher-tier binder (`ModelLevel: Frontier`) is honoured on a Mutation create, never clamped to the role default | PC-5 |
| G-6 | D-8: configured chains and pins precede the Low default for Mutation | PC-6 |
| G-7 | D-6/D-8: Mutation bundle refuses at Low on `binding-missing` or a missing pack digest (fail-closed first action) | PC-7 |
| G-8 | D-5/D-7: Mutation bundle states `Low never locates sites, authors diffs or discovers controls.` and scopes discovery to binding/higher-tier-analysis | PC-8 |
| G-9 | D-1: TestDesign bundle requires the recipe parts and `binding required after Code` for unbound payloads | PC-9 |
| G-10 | D-1: Code bundle prepares candidates under `docs/superpowers/mutations/` and labels them `unbound` | PC-10 |
| G-11 | D-2: Review bundle judges `adequacy` before land without executed mutants | PC-11 |
| G-12 | S2: each stage bundle stays <= 2,500 ASCII (the cap that keeps compositions launchable) | PC-12 |
| G-13 | D-3/D-6: owner document carries the complete reason vocabulary and six parts | PC-13 |
| G-14 | S4: active recipes route binding at explicit Frontier and execution at Low; no `| Frontier |` Mutation tier row | PC-14 |
| G-15 | D-5 step 1: preflight refuses HEAD != L (`source-mismatch`) | PC-15 |
| G-16 | D-1/D-3: pack index digest and every member hash are checked on receipt (`artifact-mismatch`, `binding-missing`) | PC-16 |
| G-17 | D-3: every preimage byte hash and context is checked before any write, including CRLF drift (`patch-mismatch`) | PC-17 |
| G-18 | D-3: expected match count is enforced exactly; duplicate or absent context refuses without writing | PC-18 |
| G-19 | D-3: after apply, the changed-file set and predicted mutant hashes must match; extra file or partial multi-file application restores and refuses | PC-19 |
| G-20 | D-5 step 4: restore verifies each restored hash equals the preimage and never overwrites an unexplained edit (`restore-mismatch`, path reported) | PC-20 |
| G-21 | D-4/D-9: a repeated phase gets a new attempt directory; an existing directory refuses | PC-21 |
| G-22 | D-4: the oracle requires exact executed/failed case-set equality; an extra failure, missing/extra/duplicate row or skip is `unexpected-red`/`selector-drift` | PC-22 |
| G-23 | D-5 step 5: restored run requires `buildSource=verified` and an output identity different from the mutant phase (`stale-output`) | PC-23 |
| G-24 | D-4: instantiation refuses unknown tokens, missing values, empty results path and non-contained paths | PC-24 |

Guards = 24, mapped = 24, missing = 0, duplicate PC maps = 0. Not listed as guards: the catalog pin, the `[bundle:...]` header tests and CARD-0478's 230 controls, which are unchanged behaviour covered by their own PCs.

### Positive controls

Bundle and routing PCs edit an embedded resource or `DelegationSettings.cs`, so each cycle rebuilds `tests/Antiphon.Tests` into the PC's own `bin-c479-pc<N>-<phase>/` output (CARD-0478 made the forced rebuild mandatory for embedded-text PCs). Doc and snippet PCs are read at run time and need no rebuild. All filters are exact methods under `/*/*/Class/Method*`; the row's executed count must equal the baseline's. Mutation runs these after this card's own land in a SourceLanding snapshot at its L; Code runs V/R only; Review judges before land.

| PC | Break | Compiling defect | Expect red at |
|---|---|---|---|
| PC-1 | G-1 | `DelegationSettings.cs`: `["Mutation"] = new() { Level = AgentModelLevel.Frontier, EscalateTo = AgentModelLevel.High, RecommendedInFlight = 1 }` | `MutationAdmissionTests.C479_V03a_NoPinMutationCreateResolvesLow` at `row.ModelLevel.ShouldBe(AgentModelLevel.Low)`; companion `C470_mutation_policy_is_independent` also red at `mutation.Level.ShouldBe(AgentModelLevel.Low)` |
| PC-2 | G-2 | `DelegationSettings.cs`: remove `EscalateTo = AgentModelLevel.High` from the Mutation entry | `MutationAdmissionTests.C479_V04_ManualEscalationOfLowMutationTargetsHigh` at `summary.ModelLevel.ShouldBe(AgentModelLevel.High)` (rung-counting yields Medium) |
| PC-3 | G-3 | `DelegationSettings.cs`: add `EscalateAfterMinutes = 25` to the Mutation entry | `AgentTaskStallEscalationTests.the_shipped_default_arms_no_role_at_all` at `.ShouldBeFalse("shipped RolePolicy must leave ...")` |
| PC-4 | G-4 | `DelegationSettings.cs`: `["Code"] = new() { Level = AgentModelLevel.High, RecommendedInFlight = 2 }` | `MutationRoleContractTests.C470_mutation_policy_is_independent` at the new `settings.RolePolicy["Code"].Level.ShouldBe(AgentModelLevel.Frontier)` |
| PC-5 | G-5 | `AgentTaskService.ResolveLevel`: `var level = role == AgentTaskRole.Mutation ? (policy?.Level ?? _settings.DefaultLevel) : explicitLevel ?? ...` (clamp Mutation to policy) | `MutationAdmissionTests.C479_V03b_ExplicitFrontierMutationCreateStaysFrontier` at `row.ModelLevel.ShouldBe(AgentModelLevel.Frontier)` |
| PC-6 | G-6 | `AgentTaskService.CreateAsync`: skip the chain walk when `request.Role == AgentTaskRole.Mutation` (fall straight to `ResolveLevel`) | `MutationAdmissionTests.C470_mutation_routing_does_not_inherit_code` at `row.ModelLevel.ShouldBe(level)` on the `AssertRouted(AgentKind.ClaudeCode, AgentModelLevel.Medium)` step |
| PC-7 | G-7 | `stage-mutation.md`: delete the sentence containing `binding-missing` | `MechanicalPcContractTests.C479_V05_MutationBundleFailsClosedAtLow` at `mutation.ShouldContain("binding-missing")` |
| PC-8 | G-8 | `stage-mutation.md`: delete `Low never locates sites, authors diffs or discovers controls.` | `MechanicalPcContractTests.C479_V05_MutationBundleFailsClosedAtLow` at `mutation.ShouldContain("Low never locates sites, authors diffs or discovers controls.")` |
| PC-9 | G-9 | `stage-test-design.md`: replace `binding required after Code` with `bind after Code` | `MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow` at `design.ShouldContain("binding required after Code")` |
| PC-10 | G-10 | `stage-code.md`: delete the candidate sentence (the one containing `docs/superpowers/mutations/`) | `MechanicalPcContractTests.C479_V05b_CodePreparesUnboundCandidates` at `code.ShouldContain("docs/superpowers/mutations/")` |
| PC-11 | G-11 | `stage-review.md`: replace `adequacy` with `coverage` | `MechanicalPcContractTests.C479_V05c_ReviewJudgesAdequacyWithoutMutants` at `review.ShouldContain("adequacy")` |
| PC-12 | G-12 | `stage-mutation.md`: append 200 ASCII characters of filler to the last line | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` (argument `stage-mutation`) at `text.Length.ShouldBeLessThanOrEqualTo(2_500, ...)` |
| PC-13 | G-13 | `docs/mechanical-pc-contract.md`: delete the `surviving-mutant` row from the reason table | `MechanicalPcContractTests.C479_V06_OwnerDocHoldsTheContract` at `doc.ShouldContain("surviving-mutant")` |
| PC-14 | G-14 | `SKILL.md`: restore the Tiers row to `\| `Mutation` \| post-land PCs and missing-control discovery in a fresh SourceLanding snapshot (stage) \| Frontier \|` | `MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow` at the SKILL.md `Regex.IsMatch(...Frontier...).ShouldBeFalse()` |
| PC-15 | G-15 | `preflight` snippet: delete the `if ($head -ne $L) { Refuse 'source-mismatch' }` block | `MechanicalPcRehearsalTests.C479_V09a_BindingSource` at required row `binding-source:head-mismatch-refuses` |
| PC-16 | G-16 | `preflight` snippet: replace the member-hash loop body with `$true` | `C479_V09a_BindingSource` at required row `binding-source:member-hash-mismatch-refuses` |
| PC-17 | G-17 | `apply` snippet: remove the preimage `Get-FileHash` comparison before the write | `C479_V09b_BindingPreimage` at required row `binding-preimage:crlf-refuses` |
| PC-18 | G-18 | `apply` snippet: change `-ne $expectedMatches` to `-lt 1` | `C479_V10b_ApplyContext` at required row `apply-context:duplicate-refuses-no-write` |
| PC-19 | G-19 | `apply` snippet: drop the changed-file-set comparison after the write | `C479_V10c_ApplyClosure` at required row `apply-closure:extra-file-restores-and-refuses` |
| PC-20 | G-20 | `restore` snippet: make the copy unconditional (remove the current-hash-vs-recorded-mutant-hash check) | `C479_V12b_RestoreForeignEdit` at required row `restore-foreign-edit:path-retained-and-reported` |
| PC-21 | G-21 | `instantiate` snippet: `New-Item -ItemType Directory -Force` for the attempt directory | `C479_V12c_InstantiateTokens` at required row `instantiate-tokens:existing-attempt-refuses` |
| PC-22 | G-22 | `oracle` snippet: compare only that the named failing case is in `FAILED` (drop set equality) | `C479_V11a_OracleRows` at required row `oracle-rows:extra-failure-rejects` |
| PC-23 | G-23 | `oracle` snippet: remove the `restoredOutputHash -ne mutantOutputHash` check | `C479_V11b_OracleProvenance` at required row `oracle-provenance:same-output-identity-rejects` |
| PC-24 | G-24 | `instantiate` snippet: leave unresolved `{token}` text in place instead of refusing | `C479_V12c_InstantiateTokens` at required row `instantiate-tokens:unknown-token-refuses` |

### Out of scope

- Real source mutation of this card's own code by a Low executor, and any Mutation dispatch now: Mutation is paused by the operator; PC-1..24 are pending post-land and are first bound by a Frontier binder per D-2, which is also this card's own first worked execution record (S5).
- A server-side pack validator, admission API, parser or new role: D-1 rejects them; the rehearsal proves the literal snippets, not an enforced gate.
- Model obedience at Low, provider alias mapping, live quota/sign-in refusals: caller-recorded (V-13), not fixture-provable.
- CARD-0478 delivery, custody and cleanup suites: unchanged paths; rerun only if Code touches them (state the reason on the CP line).
- A concurrency or backend PC (CARD-0461 PC-83..85) run: the owner doc contrasts them as a worked classification; no live destructive test.
- Whole Unit lane or full assembly: no cross-cutting invariant changes; every touched surface is named above.

### Checkpoints

Closed list. Group `A` = S1..S4 plus the contract/routing tests committed; group `B` = the rehearsal harness committed. One isolated `Antiphon.Tests` build per group; `CP-n` reuse only inside the same group. Counts are TUnit executed results (argument rows counted). Linux runner (server2) estimates; Windows inherits.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | A | `tests/Antiphon.Tests -> bin-c479-a/` | routing-policy | `/*/*/MutationRoleContractTests/*` | V-1, R-1 | 2, 0 failed/skipped | 2 | 4 |
| CP-2 | A | CP-1 | routing-create | `/*/*/MutationAdmissionTests/(C479_V03a_NoPinMutationCreateResolvesLow*)\|(C479_V03b_ExplicitFrontierMutationCreateStaysFrontier*)\|(C479_V04_ManualEscalationOfLowMutationTargetsHigh*)\|(C470_mutation_routing_does_not_inherit_code*)` | V-3, V-4, R-2 | 4, 0 failed/skipped | 4 | 3 |
| CP-3 | A | CP-1 | stall-default | `/*/*/AgentTaskStallEscalationTests/the_shipped_default_arms_no_role_at_all*` | R-3 | 1, 0 failed/skipped | 1 | 2 |
| CP-4 | A | CP-1 | bundle-pins | `/*/*/InstructionBundleTests/*` | V-2, R-4 | 74, 0 failed/skipped | 74 | 2 |
| CP-5 | A | CP-1 | c478-contracts | `/*/*/PostLandMutationContractTests/*` | V-8, R-5 | 30, 0 failed/skipped | 30 | 2 |
| CP-6 | A | CP-1 | c479-contracts | `/*/*/MechanicalPcContractTests/*` | V-5, V-6, V-7, R-6 | 11 (3 + 2 + 5 + 1), 0 failed/skipped | 11 | 2 |
| CP-7 | A | CP-1 | launch-contract | `/*/*/DelegateBundleLaunchTests/C470_mutation_claude_and_codex_launch_contract*` | R-7 | 2, 0 failed/skipped | 2 | 2 |
| CP-8 | A | CP-1 | checkpoint-doc-pins | `/*/*/CheckpointManifestDocumentationTests/*` | R-8 | 7, 0 failed/skipped | 7 | 1 |
| CP-9 | B | `tests/Antiphon.Tests -> bin-c479-b/` | rehearsal | `/*/*/MechanicalPcRehearsalTests/*` | V-9, V-10, V-11, V-12, R-9 | 10, 0 failed/skipped; 38 harness rows between them | 10 | 8 |
| CP-10 | B | n/a | rehearsal-full | `pwsh -NoProfile -File scripts/test-mechanical-pc-contract.ps1` | V-9..V-12 | `C479: 38 passed, 0 failed, 38 rows` and `C487 HARNESS EXIT CODE: 0` | n/a | 3 |

If Code lands the harness with a different row count, update CP-9/CP-10 `Expect` in the same commit and say so on the CP line; the row names in the PC table are the contract, the count is derived. V-11 is 9 harness rows plus 3 provenance rows: driver exit 2 and exit 3 are rows. 5+3+3+2+3+9+3+3+2+5 = 38, so CP-9 and CP-10 Expect stay 38.

### Cost

All values are planning estimates; this task ran zero builds, tests or mutations.

- **Ordinary V/R floor (Code)** = sum of `EstimatedMinutes` = **29 minutes** (group A: CP-1..8 = 18, including one 3-minute isolated build; group B: CP-9..10 = 11, including one build).
- **PC floor (Mutation)** = **166 minutes**, serial: PC-1..6 (routing, rebuild both ways) 6 x 9 = 54; PC-7..12 (embedded bundles, rebuild both ways) 6 x 9 = 54; PC-13..14 (docs read at run time) 2 x 3 = 6; PC-15..24 (snippets, pwsh only) 10 x 4 = 40; snapshot preflight, baseline build and final restoration census 12. A 9-minute cycle is edit 0.5 + rebuild 3 + red 1 + restore 0.5 + rebuild 3 + green 1. PC-7..12 touch six different files and could batch into one rebuild pair (about 40 minutes saved) if the binder supplies the independence evidence D-5 requires; not credited.
- **Binding overhead**: a Frontier binding-only pass for this 24-PC battery is about 30 minutes of reading and pack authoring plus 6 minutes of caller bookkeeping. On this battery it is close to the 40-minute batching saving and larger than any model saving; D-9's warning applies to this card itself.
- **Total** = ordinary 29 + binding 36 + PC 166 = **231 minutes**, excluding Code authoring, ordinary Review (about 25 minutes: 10 judgement + 15 reruns of CP-1..10) and slot waits.
- **Authoring (Code)**: S1 owner doc with five literal snippets 45; S2 four bundles under the cap 25; S3 default + test edit 10; S4 five recipe files 25; `MechanicalPcContractTests` 20; routing cases 20; harness + `MechanicalPcRehearsalTests` 60. **About 205 minutes**, which exceeds the 30-60 minute Code budget the operator brief implies. Default split: Code dispatch 1 = group A (S1..S4 + CP-1..8), ExpectAbout 165; Code dispatch 2 = group B (harness + CP-9..10), ExpectAbout 75. Dropping group B leaves G-15..G-24 untested until the first real binding, which would then be the only check of the snippets; that is the caller's call, not a default.
- **Savings**: no measured model saving is claimed. Low-tier execution removes judgement work from 24 cycles whose compiler/test minutes (about 120 of the 166) are tier-independent; the recoverable model share is the remaining 46 minutes of reading and bookkeeping, and only after the first measured Low run.

#### Handoff audit

Bodies read: listed in Inspection. Guards = 24, mapped = 24, missing = 0, duplicate PC maps = 0. Every PC is executable after its slice lands: PC-1..6 after S3, PC-7..12 after S2, PC-13 after S1, PC-14 after S4, PC-15..24 after group B. Cost is numeric. No placeholders. The plan's ten acceptance cases map: 1 -> V-5/V-6/R-6; 2 -> V-2/R-4/R-8; 3 -> V-1/V-3/V-4/R-1..R-3; 4 -> V-9; 5 -> V-10; 6 -> V-11; 7 and 9 -> V-6b worked examples (documentation, labelled illustrative); 8 -> V-12; 10 -> V-7 plus CARD-0478's existing same-O admission tests (`C470_concurrent_mutation_creates_admit_only_one`, `C478_G015_OpenOperation`, `C478_G016_AtomicAdmission`), not rerun because unchanged.

## Code handoff

First dispatch: group A. S1 writes `docs/mechanical-pc-contract.md` with the five labelled snippets (`# mechanical-pc-contract v1: preflight|instantiate|apply|oracle|restore` as the first line of each fenced `powershell` block) and the token lists V-6 names; S3 edits `DelegationSettings.cs` and `C470_mutation_policy_is_independent`; S2 trims then edits the four bundles (spare characters: test-design 23, review 19, code 94, mutation 138); S4 edits the five recipe files and both Tiers rows; add `MechanicalPcContractTests` (Unit, `DelegateScriptRunner.RepoRoot`) and the three `MutationAdmissionTests` cases. `checkpoints:` points at this file's `### Checkpoints`, rows CP-1..8. Second dispatch: group B, `scripts/test-mechanical-pc-contract.ps1` and `MechanicalPcRehearsalTests`, rows CP-9..10. Both return `next: review`; PC-1..24 stay pending for a post-land Frontier binding pass, then Low execution, once the operator resumes Mutation.

## Results

Group A only. Code task `020195af` on `feat/card-task-020195af` in `/work/worktrees/task-020195af`. Acceptance run `20261010-144855-f230`, bound (`ANTIPHON_TASK_TOKEN` present). Tested source `68be4213412614fd6fa3a62429ce3513308a4707` (`state=clean`, `buildSource=verified`). Exit 0. 8 green, 0 red. One isolated build `bin-c479-a/` (deleted on green). `bin-c479-b/` was not built. Every row `slot=granted` `waited=0s`. `unlisted: none`. Post-land Mutation was not run. PC-1..24 stay pending. Group B (CP-9, CP-10) was not this dispatch. Restart: none.

An earlier run `20261010-143635-24b0` at `2022160be82a68b30dd0cce9b8b283b200d02fa1` exited 1: CP-2 failed `C479_V03a` and `C479_V03b` (`workspace_default_not_git` on a non-git temp directory), and CP-4 failed the 500-character command-line headroom (Orchestrator/Custom 29836 against 29500). Those two rows were fixed and the closed table was rerun at the SHA above.

CARD-0822 collides on `server/Bundles/orchestrator.md`. The raw SHA-256 pin in `CheckpointRepeatDocumentationTests` is now `42fd7e6d7bca145bae377ff0f97b460a8e891f15d0fa58724bdb6dbb1f09eedb`.

### CP-1

executed=2 passed=2 failed=0 skipped=0

```
CHECKPOINT CP-1 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=ok filter=/*/*/MutationRoleContractTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-2

executed=4 passed=4 failed=0 skipped=0

```
CHECKPOINT CP-2 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/MutationAdmissionTests/(C479_V03a_NoPinMutationCreateResolvesLow*)|(C479_V03b_ExplicitFrontierMutationCreateStaysFrontier*)|(C479_V04_ManualEscalationOfLowMutationTargetsHigh*)|(C470_mutation_routing_does_not_inherit_code*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-3

executed=1 passed=1 failed=0 skipped=0

```
CHECKPOINT CP-3 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/AgentTaskStallEscalationTests/the_shipped_default_arms_no_role_at_all* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-4

executed=74 passed=74 failed=0 skipped=0

```
CHECKPOINT CP-4 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/InstructionBundleTests/* executed=74 passed=74 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-5

executed=30 passed=30 failed=0 skipped=0

```
CHECKPOINT CP-5 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/PostLandMutationContractTests/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-6

executed=11 passed=11 failed=0 skipped=0

```
CHECKPOINT CP-6 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/MechanicalPcContractTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-7

executed=2 passed=2 failed=0 skipped=0

```
CHECKPOINT CP-7 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/DelegateBundleLaunchTests/C470_mutation_claude_and_codex_launch_contract* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### CP-8

executed=7 passed=7 failed=0 skipped=0

```
CHECKPOINT CP-8 commit=68be4213412614fd6fa3a62429ce3513308a4707 build=reused filter=/*/*/CheckpointManifestDocumentationTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-020195af/.antiphon/checkpoints/20261010-144855-f230/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=68be4213412614fd6fa3a62429ce3513308a4707 sourceState=clean buildSource=verified
```

### Group B

Code task `18db3be4` on `feat/card-task-18db3be4` in `/work/worktrees/task-18db3be4`. Acceptance run `20261010-182424-04ee`. Tested source `4d1cc733ef1d8005724f891702241990341c9b13` (`state=clean`; CP-9 `buildSource=verified`, CP-10 command row `buildSource=notApplicable`). Exit 0. 2 green, 0 red. One isolated build `bin-c479-b/` (deleted on green). Both rows `slot=granted` `waited=0s`. `unlisted: none`. CP-9 TRX executed=10 passed=10 failed=0 skipped=0. CP-10 console: `C479: 38 passed, 0 failed, 38 rows` and `C487 HARNESS EXIT CODE: 0`. V-11 is 9 harness rows plus 3 provenance rows, and CP-9/CP-10 Expect stayed 38. Post-land Mutation was not run. PC-1..24 stay pending. Restart: none.

### CP-9

executed=10 passed=10 failed=0 skipped=0

```
CHECKPOINT CP-9 commit=4d1cc733ef1d8005724f891702241990341c9b13 build=ok filter=/*/*/MechanicalPcRehearsalTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-18db3be4/.antiphon/checkpoints/20261010-182424-04ee/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=4d1cc733ef1d8005724f891702241990341c9b13 sourceState=clean buildSource=verified
```

### CP-10

exit=0. Console: `C479: 38 passed, 0 failed, 38 rows` and `C487 HARNESS EXIT CODE: 0`.

```
CHECKPOINT CP-10 commit=4d1cc733ef1d8005724f891702241990341c9b13 build=n/a filter=pwsh -NoProfile -File scripts/test-mechanical-pc-contract.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=4d1cc733ef1d8005724f891702241990341c9b13 sourceState=clean buildSource=notApplicable
```
