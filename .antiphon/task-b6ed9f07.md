# CARD-0822 Code result (task b6ed9f07)

S1–S4 are implemented on `feat/card-task-b6ed9f07`. The closed checkpoint is green. Post-land Mutation was not run. PC-1 through PC-7 stay pending. `restart: none`. Token bound.

Tested source: `f4957a16b2e97dd195a984da76a7f51a1b66fd3a`.
Worktree: `/work/worktrees/task-b6ed9f07`.
Landing owner: `b6ed9f07`.
Plan: `docs/superpowers/plans/2026-09-29-card-0822-orchestrator-instructions-file-plan.md`.

## Checkpoint

Run `20261010-155741-add3`, verdict GREEN, exit 0, wall 4m16s. Source clean, buildSource verified.

| CP | executed | passed | failed | skipped | slot | waited |
|---|---|---|---|---|---|---|
| CP-1 | 23 | 23 | 0 | 0 | granted | 0s |
| CP-2 | 98 | 98 | 0 | 0 | granted | 0s |
| CP-3 | 16 | 16 | 0 | 0 | granted | 0s |
| CP-4 | 10 | 10 | 0 | 0 | granted | 0s |
| CP-5 | n/a (exit 0) | n/a | n/a | n/a | granted | 0s |

CP-5: `HOOKS TESTS EXIT CODE: 0  (PASS)`, pass 43, fail 0. The matcher case and the seven new SessionStart cases passed (startup inject, resume inject, compact inject plus COMPACT_CONTEXT, missing env silent, missing file silent, 16384-byte cut plus marker, worker silent with a file present).

Unedited CHECKPOINT lines are in the plan `## Results` section and in `.antiphon/checkpoints/20261010-155741-add3/report.md`.

## Bundle length (next to CP-2)

- Before the appendix swap: trimmed LF 13861.
- After the verbatim swap: trimmed LF 13855 (6 shorter; 184 above the plan baseline 13671 because CARD-0505 had already lengthened the standing-policy paragraph).
- After the unpinned budget cut: trimmed LF 13655. Plan-formula argv estimate 29484 of 29500.
- `the_worst_case_composition_measured_sits_far_under_the_budget` and the 14310 cap test passed inside CP-2. Tests and caps were left unchanged. The file does not contain "depth of two".

## Ordinary V/R

All passed in the closed run: V-1 V-2 V-3 V-4 V-5 V-6 (six arguments, including dispatch-concurrency) V-7 V-8 V-9 V-10 V-11 V-12 V-13 V-14, R-1 R-2 R-3 R-4 R-5 R-6 R-7 R-8.

## Signals

`runner-defaults rev {next}` after a successful save (`Same` returns first). `host-budget {hostId}` after commit. `hold {kind}/{alias}` on successful upsert and clear. `routing-pin {role}` after upsert save, after a clear that saves, and once per expired pin after that save. `dispatch-concurrency rev {revision}` only after `PutAsync` commits and `PublishChangedAsync`. `runner-capacity {id}` from `SetDeclaredCapacityAsync` after the runner confirms persistence. `runner-drain {id}` after Drain and after Clear. `runner-retire {runnerId}` from `RunnerRetireService.StampAsync` after `SaveAndMirrorAsync`. Heartbeat capacity is an incident only; the sweep is the backstop.

## Merge

`git merge-tree --write-tree` of `f4957a16b2e97dd195a984da76a7f51a1b66fd3a` against fetched `origin/master` `e43508811033e3996cb4739b9168bce1f315f808` exited 0, tree `2a757e597eeec1428647c480f970ada47d819072`. The branch was not rebased.

## Unlisted

- `c822-s4-hooks` lease `2ecfb1ee-f6ce-4ff3-84a0-383cdf08b017` waited=0s held=3s, hooks exit 0. Reason: S4 hook cases before the closed checkpoint.
- `c822-s4-auth` lease `480ebffc-e8d1-4d43-8935-e03bea1965ec` waited=0s held=177s, build 0 errors. Reason: compile the guidance class and embed the swapped bundle.
- `c822-s4-auth-run` lease `e1267dec-9442-4e43-8c1c-f76a80d9fea4` waited=0s held=4s, guidance 4 passed, bundle pin/budget filter 8 passed. Same reason.
- `c822-tool` lease `5b6c69e9-196a-4d59-aae3-4e532f886b62` waited=0s held=5s. Checkpoint tool bootstrap. The closed run was not wrapped in a second slot.

Slice commits: S1 `719a69c1cfbc1c600ac2656066309f19d29ad2e2`, S2 `0eba109542362df7bb93534e5fd185b948008f5a`, S3 `490566660948ed91593ce5965dad1839040cbdc6`, S4 `f4957a16b2e97dd195a984da76a7f51a1b66fd3a`.
