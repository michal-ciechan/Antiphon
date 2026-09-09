# CARD-0470: Separate Code from mutation verification

Date: 2026-09-09. Stage: Plan. Verification design is a separate TestDesign dispatch.

Code implements the plan, runs every ordinary functional/regression V-n and R-n, commits and pushes, then settles `next: mutation`. A separate writable `Mutation` stage runs the deliberate PC-n battery against that committed implementation. The next card can occupy Code's project/role slot while Mutation runs. Landing follows successful mutation verification and any required Review.

## Ground truth

| Assumption or question | Current implementation | Design consequence |
|---|---|---|
| Code must do the PCs before completing. | `server/Bundles/stage-code.md` explicitly requires every V/R and PC, including break/red/restore/green, and adding a missing PC. `InstructionBundleTests.stage_bundle_invariants_are_pinned_by_substring` pins this text. | Change the standing contract and its tests; there is no executable PC-result gate to move. |
| Verification is folded everywhere consistently. | The delegate skill and `docs/orchestration-loop.md` fold Verify into Code. `stage-test-design.md` says Build runs PCs. `stage-review.md` simultaneously says read-only and run PCs. The testing guide calls this Code-stage PC execution. | Update all these owners, including the contradictory Review instruction. |
| Another Code dispatch would free capacity. | `DelegationOpenGate` selects open rows by `ProjectId`, including a distinct null bucket; `Snapshot.RoleCount` compares the exact role. Queued/Dispatched/Working count. `RolePolicy["Code"].RecommendedInFlight` is 1. Project-wide `MaxOpenTasks` and dispatch-time `MaxConcurrentTasks` are additional limits. | A new exact role gets a separate slot; do not increase or bypass any existing cap. |
| Role settings are at the brief's path. | The actual file is `server/Application/Settings/DelegationSettings.cs`, not `Application/Services/DelegationSettings.cs`. Code currently defaults to Frontier. | Add Mutation to this settings dictionary with its own entry. |
| Test or Review can run the battery unchanged. | Test is a helper that runs and reports without changing production/tests. Review is a read-only stage. | Neither owns deliberate source mutations. |
| A new name in prose is sufficient. | `AgentTaskRole` ends at `TestDesign = 15`; `IsStage` and its EF expression enumerate five roles. `PipelineHandoffKind` ends at `None = 7`; formatter/parser/mapping use a closed vocabulary. CLI ValidateSets, complexity routing and client maps enumerate roles too. | Add the role and destination through all public surfaces; preserve old numeric values. |
| `verify` is available for the new stage. | `PipelineHandoff` explicitly aliases `verify` to Review, and `OrchestrationStage.Verify` already names the landing/test outcome axis. | Use `mutation`; retain both existing meanings of verify. |
| `-OnAgent` reliably preserves a Code worktree session. | `AgentTaskService.CreateAsync` keeps requested Role but forces a live follow-up to Shared and inherits the model/kind. `TryReuseWarmAgentAsync` refuses Shared reuse of a `card-task-<8 hex>` worktree delegate (CARD-0221). A retired-agent fallback inherits report context but does not itself select the old worktree. | Same-agent continuation is not the simplest reliable default on this checkout. |
| A changed Role updates a running session's bundle. | `InstructionBundles.ForDelegate` composes at launch. Warm reuse does not generally deliver the new stage bundle; Grok also checks its rules receipt. `DelegateBundleLaunchTests` pins launch-only bundle delivery. | Choose a fresh launch in the retained Code worktree. Do not add live bundle injection or change the reuse safety guard. |
| A new `-Worktree` started from Code's directory necessarily includes Code's commit. | Worktree creation uses the merge target/master lineage, not an unlanded sibling task branch (CARD-0215). | Do not use `-Worktree` for the Mutation recipe and do not land Code early merely to supply the source. |

## Decisions

### D-1: Mutation is a writable pipeline role

Append `AgentTaskRole.Mutation = 16`. Include it in both `AgentTaskRoles.IsStage` and `AgentTaskRoles.Stage`. It is a Worker stage, not a specialist or a helper. Add `RolePolicy["Mutation"]` with `Level = Frontier` and `RecommendedInFlight = 1`, keeping the existing timeout defaults and normal kind/pin/complexity resolution. Frontier matches the current Code tier: evaluating whether a mutant failed at the intended assertion and adding a missing control require judgement beyond Test's run/report remit.

Mutation participates normally in writer leases, scope handling, checks, task/card transitions and project/global capacity limits. Do not special-case it out of any cap. Do not clone, clear or migrate live Code routing pins into Mutation. Add it to `ComplexityRoutingService.RoutableRoles` and the routing/complexity CLI role selectors so the independent policy is actually configurable.

Writable means the dispatch uses Shared in the explicitly retained, isolated Code worktree. Reject an explicitly ReadOnly Mutation request with the existing 422 validation pattern; do not silently upgrade access. Do not change Review's or Test's access contract.

No schema change is expected: Role and NextStage are existing enum columns without a closed database constraint in the inspected mapping. Keep every previous ordinal stable; confirm schema/converter parity in TestDesign. Do not create a migration just to add enum members.

Rejected: reuse Code (same bottleneck), Test (non-editing helper), Review (read-only), Custom/Debug as the permanent stage (no distinct standing contract or pipeline destination), and Verify (breaks an established alias and adds ambiguity).

### D-2: Add the canonical destination `mutation`

Append `PipelineHandoffKind.Mutation = 8`. Add `mutation` to the parser's case-insensitive vocabulary, `Token`, and `TryToStageRole`. Add it to `DelegationReportFormatter.StageHandoffContract`, while retaining the 400-character handoff and existing contract-size budget. No new aliases are needed. `verify` still parses and normalizes to `review`; `test` remains outside the next-stage vocabulary.

Update client Role and PipelineHandoffKind unions, role picker, stage label, and task-detail next-stage token map. Mutation must appear in the pipeline and routing UI and receive ready rows from a settled Code report. The completion header remains the orchestrator's dispatch signal. A missing block still settles as `next=unmarked`; unknown tokens remain visible diagnostics. Do not add a PC-report parser or an automated dispatch engine.

### D-3: Code owns implementation and ordinary verification

Replace the Code bundle's PC loop and missing-PC requirement with these obligations:

1. Implement the plan, including the tests specified by its verification design.
2. Run every V-n and R-n; report every ID and its actual outcome. A shared command may cover several IDs, with the mapping stated.
3. Commit and push each meaningful slice and the final ordinary-tested state. Report the full commit SHA and worktree/branch. Finish all running commands before settlement.
4. Report all PC IDs as pending for Mutation, carrying the plan artifact and any newly noticed coverage gaps. Code must not execute deliberate mutants as part of its default completion criterion.
5. Set `next: mutation` when implementation and V/R are complete, including when the plan lists zero PCs. Mutation then confirms applicability and checks for missing PCs. `next: code` means implementation or ordinary verification work remains; `next: decide` means a human choice is required. Carry required Review and restart information through Mutation.

Code cannot settle `next: land` under this default, and a hard/safety-critical Review requirement does not bypass Mutation. Substantial plan deviations are reported as `review-required: yes`, then pass through Mutation before Review. Review remains an escalation, not a substitute PC runner.

### D-4: Mutation owns every PC and missing-control discovery

Create `server/Bundles/stage-mutation.md`, mapped by `InstructionBundles.StageMutation` and `StageKeyFor`. Keep the stage bundle ASCII, provider-neutral and within the existing 2,500-character cap. Use `delegate-basics` and the testing owner for detailed execution mechanics rather than duplicating them.

The Mutation contract is:

- Read the plan/verification design and Code's full report. Verify the reported base SHA equals this worktree's HEAD and that the index and tracked source are clean before changing anything. Record the starting state of untracked files and outputs. Missing SHA, dirty source, a moved branch or an absent worktree is an unresolved precondition, not permission to switch to master or overwrite changes.
- Run each PC-n and every separately named variant: establish green as needed, apply exactly the specified deliberate defect, observe the intended assertion-level red, restore the fixed bytes, refresh timestamps/rebuild, and observe restored green. Run each PC's exact methods; zero tests, build errors and fixture errors do not count as red. Report the expected assertion as well as counts and evidence paths for both arms.
- Add and name a missing PC for a touched guard. Keep this obligation in Mutation, including the zero-PC-plan case. A control that can use an existing assertion may be added to the plan and executed here. If adequate detection needs production or test repairs, restore all mutants, record the gap, and return `next: code`; Code implements the repair and reruns affected V/R before another Mutation pass. Do not quietly rewrite assertions to make a PC succeed.
- Mutation may retain a plan/evidence amendment and commit/push it after restoration. It does not retain deliberate defects or take over feature implementation. For an amendment-only commit, report both the tested implementation SHA and the final branch SHA, and establish that production/test files did not change. A production/test change requires Code's ordinary verification and a new Mutation pass against the resulting SHA.
- Before returning: await every owned command, restore every mutation, establish a clean tracked source/index, preserve the per-PC evidence, and report the exact final SHA. Leave the delegated worktree for landing. An interrupted run reports any remaining mutation precisely and cannot claim completion.

Successful Mutation uses `next: review` when the card is hard/safety-critical, Code requested Review, or the pass found a substantive plan deviation that needs judgement; otherwise `next: land`. A surviving mutant/missing detection uses `next: code` with the defect and exact test/guard named. An inadequate verification design uses `next: test-design`; a human choice uses `next: decide`. Operational inability to obtain the required evidence is reported truthfully as failed/blocked, never green. Review judges the implementation and PC evidence read-only; remove its instruction to run deliberate PCs.

Existing optional method-scoped batching/sharding guidance remains an execution capability of the PC runner. This card adds no new sharding infrastructure and does not require sharding: its purpose is concurrency between different cards' stages, not concurrent mutations of one card.

### D-5: Fresh dispatch in Code's retained worktree

Use a new task and fresh delegate process against the committed, still-unlanded Code branch. The default recipe is:

```powershell
$mutationGoal = Get-Content -LiteralPath '<prepared-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Mutation -Card CARD-nnnn -Title "mutation checks <card>" -Shared -Dir <code-worktree> -ExpectAbout <pc-floor+analysis> -Goal $mutationGoal
```

`-Dir` is the actual Code task WorktreePath, not its parent WorkingDirectory. The prepared brief contains the prior handoff verbatim, artifact path, Code task ID (the landing owner), full Code commit SHA, branch, PC IDs, V/R evidence/report reference, Review requirement, and restart target. The caller uses the same commissioning project scope; a card ID or filesystem directory does not override `ProjectId`.

Do not pass `-OnAgent`, `-Agent`, `-ReadOnly` or `-Worktree` in this recipe. The existing worktree-name reuse guard makes this normal Shared dispatch take a fresh launch, which composes `stage-mutation`. Pin this behavior with a real dispatcher/launch-contract test. Keep the current CARD-0221 guard and launch-only bundle contract. A configured standing-agent pin or a nonstandard/shared Code checkout needs an explicitly isolated source and a confirmed Mutation composition; do not silently reinterpret this recipe or bypass the pin. Such a fallback is outside the default path.

The `Shared` flag describes the new task's relationship to an existing directory; the directory is still Code's isolated Git worktree. The previous Code task must be terminal and have no running commands. Never run another task in that exact worktree while Mutation is active. The next card's Code uses its own `-Worktree`, so ordinary scope overlap warns rather than serializes it. Additional global caps, provider capacity and shared-resource constraints can still hold it; this change only removes the Code-role bottleneck. Same-assembly process limiters are not cross-process locks: unsafe PC cases requiring shared external state remain coordinated/serial.

Do not land or remove Code's branch before Mutation/Review finish. When Mutation reports `next: land`, invoke `delegate.ps1 -Land <code-task-id>`, not `-Land <mutation-task-id>`: the Mutation task is Shared and does not own a new worktree. Carry that original owner ID in every subsequent handoff, including Review. A repair dispatch also uses the retained branch; carry its original Worktree owner forward. Landing's existing rebase/verification policy still applies.

Rejected: land Code first and mutate master; start a fresh sibling Worktree assuming it contains the unlanded Code commit; or redesign same-agent relaunch, retired-agent fallback, live bundle refresh and worktree ownership just to preserve context. A later optimization may make OnAgent safe, but it is not required for the throughput improvement.

### D-6: Keep handoff context compact and explicit

Code's full report carries V/R outcomes, pending PCs, full SHA, branch/worktree, plan path and evidence locations. Its one-line handoff can be:

```text
Mutation: code-task=<id>; sha=<40-hex>; PCs=PC-1..PC-n; review-required=no; restart: server. Use the retained Code worktree and plan; after PC success land code-task=<id>.
```

The `artifact:` field remains the repo-relative plan path. It is not a JSON manifest or a path to raw test logs. The caller reads the full preceding report when necessary and builds the next brief; it does not infer a ready stage from prose. No new persistence fields or public SHA/worktree-base flags are needed for this default.

Do not introduce a new `OrchestrationStage` enum member. Mutation is an AgentTaskRole and a PipelineHandoffKind. This recipe omits `-Stage`; ordinary role/task reporting is sufficient. An operator may explicitly use the existing `-Stage Verify` for outcome accounting, which remains a separate axis. Keep `verify -> review` and the existing `-OnAgent -> FollowUp` telemetry default unchanged.

### D-7: Update every standing explanation of the default

The pipeline becomes Investigate (when needed) -> Plan -> TestDesign (separate for medium/hard) -> Code -> Mutation -> optional Review -> Land. Plan is never skipped. Easy has three standard dispatches; medium has four to five; hard has five to six, depending on Investigate. Mutation has its own WIP recommendation of one. Plan-side and Code backlog rules are otherwise unchanged.

Update the delegate skill's role table, stage recipes, vocabulary, timing guidance and landing-owner explanation; `docs/orchestration-loop.md` cycle, role table, folded-verification discussion, complexity shape and WIP discussion; `stage-test-design.md` PC ownership and cost instructions; `stage-review.md`; the orchestrator bundle's stage vocabulary; bundle README; and the AGENTS.md short stage list. Rename the testing guide's Code-stage PC section to Mutation-stage and change actor-specific references there. In delegate-basics, qualify PC execution as belonging to the Mutation runner so Code does not inherit a competing obligation. Preserve the method scoping, restoration, foreground and commit safety rules.

TestDesign's Cost block separates ordinary V/R floor from PC floor and names the suites/filters on each side. Code's ExpectAbout is authoring plus ordinary V/R; Mutation's is PC execution plus analysis/reporting. Preserve the existing total verification-floor information for older consumers. Do not rewrite historical plan documents wholesale; a new Code brief must explicitly state this ownership split when executing an older plan that says Build runs PCs.

### D-8: Roll out without requiring the new role on the old server

This card is self-hosted: editing the worktree does not make the running server recognize Mutation or `next: mutation`. Land the Plan, then the separate TestDesign artifact, before Code is dispatched, as usual.

For CARD-0470's own implementation only, Code's brief explicitly overrides the old folded-PC bundle: implement, run V/R, commit/push, stop. Until the runtime is upgraded, Code reports the existing `next: decide` token and names the bootstrap action. The caller dispatches a **separate Debug worker** in the retained Code worktree with the complete planned Mutation contract and PC evidence requirements in its file-backed `-Goal`, using the same read-then-pass pattern as D-5. Debug is write-capable and has a distinct project/role bucket; do not substitute read-only Review or Test. This temporary worker reads the new bundle as an artifact because the running server cannot compose it yet. Its helper report can use the existing `next: land` or `next: code` vocabulary.

After this separate PC pass and any required Review, the caller lands the original Code worktree and restarts the server from the canonical checkout using the runbook; rebuild/serve the client containing the new role maps. Check the loaded role/bundle/pipeline surface directly, not just health. Only then emit or dispatch `mutation` as the default. Do not change live pins, provider holds or shared-stack ports to make rollout succeed. No runner restart is intrinsically required by this role/bundle/client change.

Do not reparse or rewrite historical reports/tasks. Old `verify` reports continue to mean Review. In-flight agents keep their launch instructions; if adopting the split on one, give the ownership change explicitly in its authorized task brief. Finish already-running mutation cycles before changing that task's work. Subsequent fresh launches receive the new bundles.

## Implementation slices

| Slice | Files / work | Required test surfaces |
|---|---|---|
| S1: Role and destination | `server/Domain/Enums/AgentTaskEnums.cs`, `PipelineHandoffKind.cs`; `Application/Settings/DelegationSettings.cs`; `Application/Services/{PipelineHandoff,DelegationReportFormatter,ComplexityRoutingService}.cs`. Add Mutation to both stage predicates, its exact policy, token and ready-role mapping. | `PipelineHandoffParseTests`, `DelegationUnitTests`, `AgentTaskConcurrencyLimitTests`, `AgentTaskPipelineStatusTests`, `ComplexityChainRoleTests`/`ComplexityChainServiceTests`. Exercise actual create/DB projection, not just enum membership. |
| S2: Executable stage contract | `server/Bundles/stage-mutation.md`, `stage-code.md`, `stage-test-design.md`, `stage-review.md`, `delegate-basics.md`; `InstructionBundles.cs`; `AgentTaskService.cs` for ReadOnly Mutation validation. Include Mutation in the Shared retained-change commit instruction if appropriate, explicitly after restoration. | `InstructionBundleTests`, `DelegateBundleLaunchTests`, `GrokRulesCompositionTests`, `DelegationUnitTests`, `AgentTaskServiceIntegrationTests`. Verify role-specific instructions, no conflicting Code/Review PC obligation, ASCII/size budget, provider-neutral composition, normal report/pointer and readonly validation. |
| S3: Dispatcher/workspace acceptance | Preserve existing fresh-worktree and writer-lease mechanisms. Add behavior coverage for the chosen Shared-in-Code-worktree recipe. Production dispatcher changes are unnecessary unless this acceptance test disproves the inspected path; return to Plan if a new worktree/session mechanism is required. | `AgentTaskPoolTests`, `DelegationScopeHoldTests`, `AgentTaskPipelineStatusTests`: new Mutation task launches its bundle in the correct directory; settled Code stops occupying Code; a different card's Worktree Code is admitted/dispatched during Mutation; a second Mutation is capped. Verify the source branch/HEAD remains the reported commit. |
| S4: CLI and client parity | `scripts/{delegate,routing-pin,complexity-chain}.ps1`; `client/src/api/agentTasks.ts`; `features/orchestrator/pipelineStageModel.ts`; `features/delegations/TaskDetailBody.tsx`; pipeline fixture and affected role-list fixtures/stories. Inspect home ready labels and routing UI consumers for exhaustive lists. | PowerShell ValidateSet/API-body tests using an intercepted endpoint (no real dispatch), `pipelineStageModel.test.ts`, `PipelineStagesPanel.test.tsx`, `RoutingSettingsTab.test.tsx`, relevant task-detail test, fixture contract checks and client type/build validation. |
| S5: Standing guidance | `.claude/skills/antiphon-delegate/SKILL.md`, `docs/orchestration-loop.md`, `docs/testing-and-build.md`, `server/Bundles/{README,orchestrator}.md`, AGENTS.md. Explain fresh dispatch, exact SHA, separate costs, original landing owner, Review placement and bootstrap. | Focused bundle/role contract checks plus a consistency review for obsolete active claims that Code/Build runs PCs. Historical artifacts are excluded from this sweep. |

Keep `server/Infrastructure/Data/Seeding/DatabaseSeeder.cs`'s BMAD workflow-template steps separate: their `test-design`/`code-review` strings are workflow template IDs, not this AgentTaskRole pipeline. Do not rename them because they match a search. Do not add areas, migrations, a second dispatch engine, new board columns or live routing configuration.

## TestDesign handoff

Produce the executable `## Verification design` in this document in a separate dispatch. Name each V/R case, exact existing or proposed test method, expected assertion, and each localized PC mutation. The following are acceptance obligations, not a substitute for that section:

- End-to-end role admission in one isolated project: settled Code A -> working Mutation A; Code B creation/dispatch succeeds with normal limits; concurrent Mutation B is refused on the Mutation role axis. Cover the reverse occupancy order, distinct projects and null project. The absolute cap must still refuse when exhausted. Use isolated test schemas and no production runner.
- A Code report carrying `next: mutation` persists Mutation, yields `next=mutation`, and creates a Mutation-ready pipeline row. A later Mutation consumes readiness; its `next: review`/`land` behaves normally. Omitting Mutation from the EF stage predicate must be detected. Preserve old verify alias, unmarked/unknown handling and non-stage helpers.
- Mutation resolves its role policy and complexity cell independently of Code, and the scripts and UI can choose/configure it. Preserve old enum serialization/ordinals. A new role must not become a specialist or lose writer-lease participation.
- Fresh launch in the retained `card-task-<id>` worktree carries `stage-mutation` across supported launch composition paths and uses the correct SHA. Pin the existing no-Shared-reuse guard rather than making an assertion against the role map alone. Explicit ReadOnly Mutation is refused; no real provider session or user profile is used.
- A meaningful report/contract check proves Code is allowed to stop after green V/R and commit while PCs are pending, Mutation owns red/restore/green and missing controls, and Review stays read-only. No forced PC execution in Code's instructions should survive through delegate-basics or the TestDesign template.
- Include targeted defect mutations for destination/role-map removal, loss of the stage EF predicate, accidental shared Code/Mutation admission counting, missing Mutation role cap, missing/wrong stage bundle, and readonly validation bypass. Each PC names one exact assertion method; do not obtain red through compilation errors or by merely deleting expected prose.
- Separate the ordinary V/R cost from the PC cost, with exact filters. Follow `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.Tests`, scoped classes for ordinary regression, exact methods for each PC, fresh nonzero execution evidence, and forward-slash alternate outputs. Use `scripts/test-client.ps1` for scoped client tests. No full assembly, browser E2E or Pty suite is automatically forced by this metadata/dispatch-contract change; justify any added scope. Process-spawning tests retain their assembly-local limiter and never overlap the Pty test assembly.
- Explicitly document this card's first-rollout Debug PC dispatch from D-8 so Code can finish without either consuming its own PC tail or emitting a token the old server cannot parse.

Plan completion is this committed artifact and the decisions above. No product files or live configuration are changed by the Plan dispatch, and no runtime behavior or test outcomes are claimed here.
