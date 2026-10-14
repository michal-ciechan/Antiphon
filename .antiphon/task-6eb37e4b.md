CARD-0097 S-D implemented and verified: CP-17 executed 16 tests, 16 passed, 0 failed, 0 skipped. V-D1 through V-D7 and R-D are complete. Next: Review. Restart: none.

Original Code task / landing owner: 6eb37e4b-de07-4e61-afcc-7857638e71e8.
Branch: feat/card-task-6eb37e4b. Worktree: /work/worktrees/task-6eb37e4b.
Task base and fetched origin/master: f7ba0684c5680e250473a2310e292e23179b3642.
Tested implementation SHA: a0cba3dc2bb6d32f0d3317889265bb6bc2e79985. The later report-only commit does not change tested source.

Plan: docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md.
Test design: docs/superpowers/plans/2026-10-14-card-0097-matrix-test-design.md.
Stored report: /work/worktrees/task-6eb37e4b/.antiphon/task-6eb37e4b.md.
Green evidence: /work/worktrees/task-6eb37e4b/.antiphon/checkpoints/20261014-131447-7908/report.json and rows/CP-17/run.trx.
Red baseline evidence: /work/worktrees/task-6eb37e4b/.antiphon/checkpoints/20261014-130703-0893/report.json and rows/CP-17/run.trx.
Generated receipts, TRX, logs and manifests remain ignored; this Markdown report is the individually committed evidence summary.

Changed source:

- scripts/complexity-chain.ps1: ordered Hard/Medium/Easy set and clear; partial-write status and problem text; ClearStagePin guard and grain lookup followed by DELETE-by-id; UTF-8 ReasonFile; effective routing line. Earlier successful PUTs remain after a later failure, as planned. Cell writes retain last-writer-wins semantics.
- scripts/routing-pin.ps1: header points role candidate changes at complexity-chain.ps1; executable code unchanged.
- tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs: seven required methods, supplementary assertions inside those methods, nine existing method bodies unchanged.
- tests/Antiphon.Tests/Scripts/ScriptedApi.cs: extracted HttpListener pump reused by the legacy StubApi; ordered immutable request snapshots, prefix responses, cell/pin state for read-back and the S-E fixture.

| ID / behavior | Named test (ComplexityChainScriptTests) | Actual outcome and assertion |
|---|---|---|
| V-D1 row set | Set_All_puts_Hard_Medium_Easy_in_order | Red -> passed; exact three PUT paths/order, literal Human/r/Codex/Frontier bodies, tier output. Also UTF-8 multiline ReasonFile and conflicting Reason/ReasonFile refusal. |
| V-D2 partial failure | Set_All_stops_at_the_first_failing_tier_and_names_written_tiers | Red -> passed; Medium 422 produces two PUTs, retained Hard cell, problem text and Easy-not-written status. |
| V-D3 flag guard | ClearStagePin_requires_All_and_a_named_role | Red -> passed; Hard, Any, omitted role, get and clear combinations return 1 with zero requests. |
| V-D4 stage clear | ClearStagePin_deletes_the_stage_pin_only_after_three_2xx | Red -> passed; three PUTs, role GET, stage DELETE in order; card and other-role pins retained. Also absent pin and DELETE 409 with problem code, three stored cells and retained pin. |
| V-D5 failure retains pin | ClearStagePin_is_skipped_when_a_tier_failed | Red -> passed; Easy 422 yields three PUTs, no GET/DELETE, retained stage pin and kept-pin/problem output. |
| V-D6 effective output | Get_Role_prints_the_effective_line | Red -> passed; stored ClaudeCode/High differs from printed effective Codex/Frontier; null effective omits that line. |
| V-D7 row clear | Clear_All_deletes_three_cells | Red -> passed; exact three DELETE paths/order and named output. |
| R-D regression | C470_mutation_cell_round_trip; Set_puts_the_tier_provenance_and_candidates; A_naive_NotAfter_is_refused_before_any_request; Get_hits_the_list_endpoint; Clear_deletes_by_complexity; Set_Role_Plan_puts_the_three_segment_cell_path; Get_Role_Plan_hits_the_role_query; Clear_Role_Plan_deletes_the_cell; Set_without_candidates_is_refused_locally | Nine passed at red baseline and final implementation. |

Final ordinary scope is S-D CP-17, per the brief's explicit NO whole-Unit instruction. No S-D V/R ID is deferred. No full assembly, client suite, live routing write or pin mutation was run. No WhatIf pattern existed in the script, so none was introduced. No bundle bytes changed; no pin test was required. Windows PowerShell 5.1 execution and the new JSON presentation shapes are not separately tested; ASCII/parser/operator inspection passed, and the script tests ran on Linux PowerShell 7.5.4. No ternary, null-conditional or pipeline-chain syntax was added.

Run record:

1. Tests-first commit 9597e10a6d65d93ecbc6f2ce3b88ccb68f280ee2: CP-17 build failed before tests because the fixture refactor dropped the TestHelpers import needed by ProcessSpawnLimit. This was my compile defect, fixed in 1cca63571d47bbda51336646bb8de9468e6bf6f0. Not red-test evidence.
2. CP-17 at 1cca63571d47bbda51336646bb8de9468e6bf6f0: 16 executed, 9 passed, 7 failed, 0 skipped. Production scripts were unchanged from the task base. Fresh TRX confirms the seven new named methods failed assertions for missing row/flag/effective behavior; the nine regression methods passed. One successful red baseline, no deliberate mutant.
3. CP-17 at a0cba3dc2bb6d32f0d3317889265bb6bc2e79985: 16 executed, 16 passed, 0 failed, 0 skipped; fresh TRX identities inspected. Both native and PowerShell receipt validators returned CHECKPOINT SOURCE VALID, rows=1. Clean source, verified build provenance. No test repetition after green.

Unedited checkpoint lines:

CHECKPOINT CP-17 commit=9597e10a6d65d93ecbc6f2ce3b88ccb68f280ee2 build=failed filter=/*/*/ComplexityChainScriptTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=9597e10a6d65d93ecbc6f2ce3b88ccb68f280ee2 sourceState=clean buildSource=unknown
CHECKPOINT CP-17 commit=1cca63571d47bbda51336646bb8de9468e6bf6f0 build=ok filter=/*/*/ComplexityChainScriptTests/* executed=16 passed=9 failed=7 skipped=0 trx=/work/worktrees/task-6eb37e4b/.antiphon/checkpoints/20261014-130703-0893/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=1cca63571d47bbda51336646bb8de9468e6bf6f0 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=a0cba3dc2bb6d32f0d3317889265bb6bc2e79985 build=ok filter=/*/*/ComplexityChainScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-6eb37e4b/.antiphon/checkpoints/20261014-131447-7908/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=a0cba3dc2bb6d32f0d3317889265bb6bc2e79985 sourceState=clean buildSource=verified

The two reruns were the same CP-17 selection after committed changes (compile repair, then implementation); no unchanged-proof repetitions. Build slots were granted with waited=0s for the bootstrap, each checkpoint build and each executed row. The first run's row slot was skipped because compilation failed. Checkpoint runs used the imported landed manifest, rowMinutes=40, --row-timeout 40m, --total-timeout 150m, --rows CP-17, committed --expected-source-sha, and no --keep-outputs. UseAppHost=false was the checkpoint's Linux default. The red baseline recorded two transient owner HTTP 502 reads, recovered without exit 7 or retrying the run.

Unlisted build: the checkpoint CLI bootstrap, required because this checkout had no built tool. Command: pwsh -NoProfile -File scripts/build-slot.ps1 -Label c097m-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c097m-tool/ --property:UseAppHost=false --nologo. It passed (0 errors; 2 warnings), slot=granted waited=0s. No other unlisted build/test selection. Manual work: ASCII and PowerShell AST parse checks on both scripts, source diff inspection, named TRX inspection and receipt validation.

The green executor recorded exit 0 and removed bin-c097m-d. A later wait initially returned 2 solely because its separate process had no in-memory authority to remove the tool shadow copy. I removed that exact tool directory and waited again: AlreadyAbsent, GREEN exit=0. Failed-run bin-c097m-d outputs and each tool shadow copy were removed by literal owned paths; receipts remain. The CLI bootstrap output is removed after the last validation. No shared output was deleted.

Validation commands (both returned VALID):

    dotnet tools/Antiphon.Checkpoints/bin-c097m-tool/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261014-131447-7908/report.json --expected-source-sha a0cba3dc2bb6d32f0d3317889265bb6bc2e79985 --rows CP-17
    pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261014-131447-7908/report.json -ExpectedSourceSha a0cba3dc2bb6d32f0d3317889265bb6bc2e79985 -Rows CP-17

Rerun recipe after bootstrapping the CLI with the gated command above: import --plan docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md --out .antiphon/c097m-sd.yaml; set that imported manifest's timeouts.rowMinutes to 40; run .antiphon/c097m-sd.yaml --rows CP-17 --row-timeout 40m --total-timeout 150m --expected-source-sha <committed-HEAD> --max-wait 45s; continue wait --run <run-id> until terminal. These are CLI arguments to dotnet tools/Antiphon.Checkpoints/bin-c097m-tool/Antiphon.Checkpoints.dll. No runner/platform pin is needed. GET /api/runner-defaults and /api/session-runners returned 200 before work; the portable lane used the assigned default runner.

History checks before this report: scripts/check-evidence-diff.ps1 -BaseRef f7ba0684c5680e250473a2310e292e23179b3642 -HeadRef HEAD returned violations=0 over three commits. git merge-tree --write-tree HEAD origin/master returned exit 0 (tree 085b4678dbf1cf30c04ad5af526574f79f1a9cae). The final response records the repeat checks over the report-inclusive HEAD and git ls-remote equality. Each meaningful slice was committed and pushed to the assigned branch; no amend, reset, rebase or force-push.

Mutation pending: PC-27 (V-D3; Hard, Any and omitted-role guards, plus get/clear variants) and PC-28 (V-D5; clear after failed Easy). The design's additional defect variants also remain pending: V-D1 Hard-only PUT; V-D2 continue after refusal; V-D4 early DELETE; V-D6 stored list displayed as effective; V-D7 Hard-only DELETE. ReasonFile, absent-pin and DELETE-failure control discovery belongs to Mutation. No PC is discharged by ordinary green. Caller should commission Review, land the original Code task after Review, then commission method-scoped SourceLanding Mutation. Any unbound candidate artifact belongs under docs/superpowers/mutations/.
