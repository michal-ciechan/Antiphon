# CARD-0097 S-F D1/D2 repair — Code b52a06a7

D1 and D2 are repaired. Scoped Final ordinary verification passed **266/266**, with zero failures or skips.
No Mutation, landing, server restart or runner restart was performed.

## Source, ownership and scope

- Repair task: `b52a06a7-2d70-43b7-996a-ef2575636b39`.
- Original Code task / landing owner: `e4c19130-d80f-4275-855f-db5b826c9948`.
- Branch: `feat/card-task-b52a06a7`.
- Worktree: `/work/worktrees/task-b52a06a7`.
- Start: `d5a44985d699f4dee51de43296ef1434405b30ab` (the reviewed S-F tip).
- Full task evidence base: `ba774271a61870261e86fb39a6fc0614fb826d53`.
- Tests-first: `2d28a675bfda42d7ea60f20b73e5b4701c86d0e5`.
- Repair: `6750fd5a778557a01365134a2663e47973f3506d`.
- Merge: `fa0494675f05f41e0016d14607b5dd17cd1c4bd3`, incorporating freshly fetched master
  `a02389cab727d508532e6d3db8ff6cbcf3eb7366`; no conflicts, rebase or force push.
- **Actual final tested SHA:** `43ac0380f33623ede29165bf9f7f2910956f90d8`.
  The subsequent commit contains this evidence only; do not relabel the receipts with that later SHA.
- Plan: `docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md` (S-F, CP-19–21).
- Design: `docs/superpowers/plans/2026-10-14-card-0097-matrix-test-design.md` (repair V-F9–11).
- Review repaired: `44e2b653`, report at
  `/work/worktrees/task-44e2b653/.antiphon/task-44e2b653.md`.

The repair brief explicitly says **NO whole-Unit lane**. This scoped Final follows that override:
three complete checkpoint classes and the 24 complete supplementary classes named below.
No required S-F ordinary ID is deferred to another Final. V-F4–6 and V-F8 remain withdrawn;
they are not passes. Other matrix slices' ordinary tests receive no credit from this run.

## Changes and decisions

D1: the model-disabled API paragraph now names the current routing owner heading. A broad scan
covered tracked docs, skills, bundles and tests, including the old anchor and line-wrapped title.
The other stale title references in the investigation and older plan now point to the current
owner; the matrix plan's rename description and M-1 scan were updated. The only remaining search
hit is M-1's own obsolete-title/anchor search pattern. The new documentation test extracts two API
pointers and one HTTP-guide pointer and resolves their titles against the owner's bold paragraph
headings. It checks the pointer counts before membership; the `#2-tiers` heading also exists.

D2: `OrchestratorInstructionsSnapshotBuilder.MatrixCell` consumes the chain-read failure marker
already provided by `GetEffectiveAsync`. Each tier uses this formatter, so a failed chain read
becomes `unknown (chain read failed)` instead of the empty-candidate `unset` representation.
The smaller notification choice is **retain the existing notice path and report unknown cells**.
The same snapshot feeds the file and delta; there is no suppression or additional recovery state.
Repeated identical failed reads leave revision/notice count unchanged; recovery reports the
unknown-to-candidate transition.

The production repair is confined to the snapshot builder. Renderer, delta formatter, notice
ordering, prompt budgets, byte limits, timeouts and existing assertions were not relaxed.
The plan now describes the rendered explicit-Kind/Level bypass sentence accurately. The two
routing-exhaustion passages again say `do not pick a kind yourself`, matching the delegate skill.
New documentation behavior rows cite the repair tests.

## Red-first evidence and fixture correction

At tests-only SHA `2d28a675b…`, CP-19 passed 9/9, CP-20 passed 18/26 with the eight intended
D2 assertion failures, and CP-21 passed 5/5. The four file cases failed on the missing unknown
label; the four independent notice cases failed on candidate-to-unknown text. The injected-read
count checks passed first. The retained build then ran the exact D1 method through the slot:
2 executed, 1 expected failure (API stale title), 1 passed (HTTP-guide control), zero skips.
It failed on owner-heading membership, not on a missing fixture or an empty test selection.
No deliberate source mutants were used.

Run `20261014-151814-1610`:

```text
CHECKPOINT CP-19 commit=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 build=ok filter=/*/*/OrchestratorInstructionsRendererTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-151814-1610/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 build=reused filter=/*/*/OrchestratorInstructionsRefreshTests/* executed=26 passed=18 failed=8 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-151814-1610/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 build=reused filter=/*/*/OrchestratorInstructionsDeltaTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-151814-1610/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=2d28a675bfda42d7ea60f20b73e5b4701c86d0e5 sourceState=clean buildSource=verified
```

The first post-fix run at merged SHA `fa0494675…` passed 39/40. The remaining case was V-F10's
simultaneous-failure fixture: the existing `ChannelPreamble` 700-character wrapper clipped its
third transition after a long filename and candidate text. This is a fixture-contract mismatch,
not an inherited-red claim. The correction commit `43ac0380f…` uses a short real filename
`i.md` and the real `ClaudeCode/Low (haiku)` candidate. The exact transition, count and revision
assertions remain; explicit <=700-character assertions were added for both notices. No timeout,
production cap or clipping algorithm changed. A long path/large delta can still clip later
transitions; the design discloses this existing limit. The four single-tier/together cases were
rerun in the complete CP-20 class and passed.

Run `20261014-152703-07ff`:

```text
CHECKPOINT CP-19 commit=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 build=ok filter=/*/*/OrchestratorInstructionsRendererTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-152703-07ff/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 build=reused filter=/*/*/OrchestratorInstructionsRefreshTests/* executed=26 passed=25 failed=1 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-152703-07ff/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 build=reused filter=/*/*/OrchestratorInstructionsDeltaTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-152703-07ff/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=fa0494675f05f41e0016d14607b5dd17cd1c4bd3 sourceState=clean buildSource=verified
```

## Final checkpoint evidence

Run `20261014-153534-8d70`, source `43ac0380f33623ede29165bf9f7f2910956f90d8`.
Imported manifest: rowMinutes 40; command overrides `--row-timeout 40m --total-timeout 150m`.
No `--keep-outputs`. One isolated build served CP-19, CP-20 and CP-21. Build and rows each had
slot=granted, waited=0s; source was clean and build provenance verified. Run wall 5m03s;
build phase 184.4702478s. Both validators returned exit 0:

```text
CHECKPOINT SOURCE VALID source=43ac0380f33623ede29165bf9f7f2910956f90d8 rows=3
```

```text
CHECKPOINT CP-19 commit=43ac0380f33623ede29165bf9f7f2910956f90d8 build=ok filter=/*/*/OrchestratorInstructionsRendererTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-153534-8d70/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=43ac0380f33623ede29165bf9f7f2910956f90d8 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=43ac0380f33623ede29165bf9f7f2910956f90d8 build=reused filter=/*/*/OrchestratorInstructionsRefreshTests/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-153534-8d70/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=43ac0380f33623ede29165bf9f7f2910956f90d8 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=43ac0380f33623ede29165bf9f7f2910956f90d8 build=reused filter=/*/*/OrchestratorInstructionsDeltaTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-b52a06a7/.antiphon/checkpoints/20261014-153534-8d70/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=43ac0380f33623ede29165bf9f7f2910956f90d8 sourceState=clean buildSource=verified
```

Fresh TRX was checked for the intended classes/methods and nonzero counts, including the eight
read-failure argument rows, seven covered-write rows, the complete UserPrompt receipt method,
busy-recipient behavior, matrix byte/elision method and unchanged-matrix delta method.

The tool initially returned exit 2 after the green report because its separate wait process
could not delete the shadow tool directory (`output-memory-authority-missing`). After the
completed run's literal shadow directory was removed, another wait returned exit 0 with
`TOOL CLEANUP AlreadyAbsent`. The green build output was deleted by the tool. Red build outputs
and task-owned tool/supplementary outputs were removed at literal paths. Ignored TRX, JSON
receipts, logs and checkpoint evidence remain ignored; they were not added to Git.

## Supplementary selection

At the same clean tested SHA, a single slot-gated build (`b52-supp-build`, UseAppHost=false,
maxcpucount=6, slot=granted waited=0s held=152s) produced `bin-b52-supp/`.
The single `--no-build` test invocation (`b52-supp-tests`, slot=granted waited=0s held=59s)
passed **226/226**, zero failed/skipped, test duration 55.097s. The filter used the documented
parenthesized `(ClassName*)` operands. The fresh TRX contained exactly these 24 classes:

| Class | Executed / passed |
|---|---:|
| InstructionBundleTests | 78 / 78 |
| OrchestratorInstructionsGuidanceTests | 6 / 6 |
| OrchestratorInstructionsEndpointTests | 2 / 2 |
| PostLandMutationContractTests | 30 / 30 |
| InstructionFileStampTests | 15 / 15 |
| DockerStackDocumentationTests | 14 / 14 |
| MechanicalPcContractTests | 11 / 11 |
| StandingPipelinePolicyDocumentationTests | 11 / 11 |
| PublishedRoutingDocumentationTests | 9 / 9 |
| RunnerBranchContractDocumentationTests | 8 / 8 |
| CheckpointManifestDocumentationTests | 7 / 7 |
| TaskPlatformGuidanceTests | 5 / 5 |
| BootStallDocumentationTests | 5 / 5 |
| PolicyRefreshDeltaTests | 5 / 5 |
| PostLandRetrospectiveContractTests | 4 / 4 |
| RepairSourceDocumentationTests | 3 / 3 |
| StandingBootDocumentationTests | 3 / 3 |
| BlockedTaskParkProjectionTests | 2 / 2 |
| CommitOnSettleDocumentationTests | 2 / 2 |
| DelegationCapabilityContractTests | 2 / 2 |
| PipelineDefinitionDocumentationTests | 1 / 1 |
| MidTurnInterruptDocumentationTests | 1 / 1 |
| CheckpointRepeatDocumentationTests | 1 / 1 |
| SessionRunnerAbsenceEvidenceDocumentationTests | 1 / 1 |

The supplementary run is direct TRX with clean before/after SHA observations, not a checkpoint
source receipt. Both D1 argument rows passed. The whole instruction-bundle class retains its
budget/pin coverage; no budget constant was edited.

## Ordinary IDs and manual acceptance

| ID | Actual outcome |
|---|---|
| V-F1 | Passed: matrix section/default/source and renumbered sections |
| V-F2 | Passed: saved/equal/clear/already-clear cell writes, exact signals and notices |
| V-F2b | Passed: one complete post-baseline UserPrompt equal to the queued body and matching version |
| V-F3 | Passed: equal matrix produces empty delta; changed role/tier produces literal delta |
| V-F7(a) | Passed: 14 role lines, candidate summary and <=160 UTF-8 bytes |
| V-F7(b) | Passed: full-minus-one elides the matrix before the sample card pin |
| V-F9 | Passed: Hard, Medium, Easy, and simultaneous Plan failures in persisted file; byte cap and elision |
| V-F10 | Passed: the same four fault cases, exact unknown/recovery notices, repeated-failure stability, <=700 chars |
| V-F11 | Passed: two API pointers plus one HTTP-guide pointer resolve |
| R-F | Passed: full renderer, refresh and delta classes, plus requested full supplementary classes |
| M-1 | Passed: expanded reference scan; current owner heading/anchor; documented default-tier, any-role/child and preview qualifications retained; whole-pin-delete/read-back procedure retained; migration/rollback plan text is a verbatim 4,604-character substring of the owner doc |

Live migration dry-run/apply, operator read-back, stage-pin clearing and rollback remain operator
rollout work. They were not performed or marked passed. No S-F ordinary ID is deferred-to-final.
No whole-assembly or whole-Unit run was made. The only extra drivers beyond CP-19–21 were the
leased checkpoint-tool bootstrap (needed to run the tool), the exact D1 red method using the
retained red build, and the requested supplementary build/run. No driver ran outside the gate.
The bootstrap was slot=granted waited=0s held=11s; D1 red was slot=granted waited=0s held=2s.
No owner-unverified exit, slot timeout, commit_refs retry or post-green repetition was needed.

## Commands and artifact locations

Evidence root: `/work/worktrees/task-b52a06a7/.antiphon/`.
Final receipts/TRX: `checkpoints/20261014-153534-8d70/report.json` and
`checkpoints/20261014-153534-8d70/rows/CP-{19,20,21}/run.trx`.
D1 red: `b52-doc-red/run.trx`; supplement: `b52-supp/run.trx`.
Logs: `b52-tool.log`, `b52-doc-red.log`, `b52-final-wait.log`,
`b52-supp-build.log`, `b52-supp.log`.

The commands below record the tested checkout. For a later rerun, use that checkout or pass
the later committed HEAD as expected source, with fresh manifest/output/run identities.

```sh
# Bootstrap through the host slot if rerunning; use a new isolated output name.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label b52-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-b52-tool/ --nologo
# Import the plan, set rowMinutes: 40 in the ignored manifest, and use a fresh build id/output.
dotnet tools/Antiphon.Checkpoints/bin-b52-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md --out .antiphon/b52-final.yaml
dotnet tools/Antiphon.Checkpoints/bin-b52-tool/Antiphon.Checkpoints.dll run .antiphon/b52-final.yaml --rows CP-19,CP-20,CP-21 --row-timeout 40m --total-timeout 150m --expected-source-sha 43ac0380f33623ede29165bf9f7f2910956f90d8 --max-wait 50s
# Continue wait on the printed run until it exits other than 75.
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261014-153534-8d70/report.json -ExpectedSourceSha 43ac0380f33623ede29165bf9f7f2910956f90d8 -Rows CP-19,CP-20,CP-21
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef ba774271a61870261e86fb39a6fc0614fb826d53 -HeadRef HEAD
```

## Publication, pending Mutation and activation

Each meaningful slice and the merge were committed and pushed to this task's explicit ref.
The full-range evidence guard passed before the report commit (34 commits, 0 violations at the
merge SHA); the guard and remote equality check are repeated after this report commit and stored
in `.antiphon/task-b52a06a7.md`. A fresh fetch still showed master `a02389cab…`;
`git merge-tree --write-tree HEAD origin/master` exited 0 before the evidence commit.
The final source-to-report delta is evidence only.

`GET /api/runner-defaults` and `GET /api/session-runners` succeeded. Placement was not pinned;
no fleet location was embedded. `GET /api/version` returned
`893ddfceeebf4d13d9401848c535abada3983c27`; activation is not claimed.
**Restart target: server after landing, caller-owned; none performed here.**

S-F pending Mutation inventory: **PC-32; PC-33 (complexity-chain); PC-34 (equal rewrite);
PC-35 (V-F7 part a); PC-42 (V-F7 part b); PC-44; PC-50 (Hard/Medium/Easy/together);
PC-51 (Hard/together); PC-52 (API pointer).** The plan's other PC-1–52 obligations receive no
execution credit here. Deliberate mutants, red/restore/green and missing-control discovery belong
to SourceLanding Mutation. Recipe: `docs/superpowers/mutations/2026-10-14-card-0097-sf-candidate.md`
(unbound). The inherited save-before-enqueue loss window and unmeasured per-regeneration reads
remain disclosed; this repair adds neither recovery nor extra reads.

Next: ordinary Review, retaining original Code task `e4c19130-d80f-4275-855f-db5b826c9948`
as landing owner. After a clean Review the caller lands that owner and commissions SourceLanding
Mutation. No human decision is needed for this repair.
