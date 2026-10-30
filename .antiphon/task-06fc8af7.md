# CARD-1613 round 4 — task 06fc8af7

Every compiled suite witness now emits `unwitnessed-suite` for a Linux-prefixed or frozen exclusion whose suite is not one of the four compiled hosts. A repeated key on a policy row emits `duplicate-key`. Linux checkpoints CP-1..CP-8, CP-12 and CP-13 are 73/73 green at the fix. No definition file changed.

## Commits

| SHA | Subject |
|---|---|
| `a96dc222be6257177901ef405794f437287fbb86` | test: expect unwitnessed suites and duplicate policy keys to fail |
| `2d0273e3fcf8750ce454dd93c980fb5dece3c59e` | fix: refuse unwitnessed Linux-only rows and duplicate policy keys |

Branch `feat/card-task-06fc8af7`, base `d84020507f42cf1ad8a1dfedcc1822967d4982a0`. Files changed: `tests/Shared/NativeLinuxPolicyWitness.cs`, `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs`. No definition file, policy, marker, or reason changed. `-Update` was not run. Approval E117DDEC count 68 still covers the definition set.

## Red then green

At `a96dc222b`, `New_exclusion_without_marker_fails` was 1 executed, 1 failed, 0 skipped. The failure is `Has(unwitnessed-suite)` for the messaging row (the first new assertion). The duplicate-key assertion is in that same method and was not reached on the red run because the assertion stops the method.

At `2d0273e3f`, `NativeLinuxPolicyWitnessMutationTests` is 10/10 and `NativeLinuxPolicyWitnessTests` is 3/3 (pre-checkpoint proof, `bin-c1613-r4-red`, UseAppHost=false).

## Fixture table

| Variant | Row | Refusal | Red `a96dc222b` | Green `2d0273e3f` |
|---|---|---|---|---|
| messaging | `SlackGatewayTests`, platform windows, Linux prefix | `unwitnessed-suite` from antiphon, session-runner, pty-host, agents-pty | failed (diagnostic absent) | passed |
| client | `ClientOnlyTests`, same shape | same | in the same red method, not reached | passed |
| e2e | `BrowserOnlyTests`, same shape | same | in the same red method, not reached | passed |
| duplicate platform | frozen `UnixDelegateLaunchArgvTests`, `"platform":"linux"` then `"platform":"windows"` | `duplicate-key` platform; not `witnessed-row-reshaped`; not `policy-hash` | in the same red method, not reached | passed |
| antiphon control | `UngatedNew` with the prefix | `missing-marker`; no `unwitnessed-suite` | held (failure is the later new assertion) | passed |

A prefixed or frozen row is reported before the per-suite filter, so every host emits `unwitnessed-suite`. Known compiled suites stay on the existing marker path.

## Original families

CP-2 ran the ten mutation parents, which hold the 28 original families, at `2d0273e3f`: 10 executed, 10 passed, 0 failed, 0 skipped. The new rows above are inside `New_exclusion_without_marker_fails` and passed with them. No marker was removed, no reason changed, and no test was skipped or relaxed.

## Linux checkpoints

Run `20261030-020534-39dc`. Plan `docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md`. `--serial --expected-source-sha 2d0273e3fcf8750ce454dd93c980fb5dece3c59e`. Import wrote `.antiphon/c1613-r4/manifest.yaml` (13 rows; the only warning is the known CP-9 separate Windows build). Source clean, `buildSource=verified`, `unlisted: none`, slot waits 0, wall 15m52s, verdict GREEN exit 0. Tool deleted `bin-c1613-ant/`, `bin-c1613-runner/`, `bin-c1613-pty/`, `bin-c1613-agents/`.

| Row | Result |
|---|---|
| CP-1 | 3 passed, 0 failed, 0 skipped |
| CP-2 | 10 passed, 0 failed, 0 skipped |
| CP-3 | 2 passed, 0 failed, 0 skipped |
| CP-4 | 4 passed, 0 failed, 0 skipped |
| CP-5 | 4 passed, 0 failed, 0 skipped |
| CP-6 | 4 passed, 0 failed, 0 skipped |
| CP-7 | 17 passed, 0 failed, 0 skipped |
| CP-8 | 23 passed, 0 failed, 0 skipped |
| CP-12 | 3 passed, 0 failed, 0 skipped |
| CP-13 | 3 passed, 0 failed, 0 skipped |

Total 73/73. Parent count is unchanged because the new cases are variants inside the existing R-13 method. Evidence: `.antiphon/checkpoints/20261030-020534-39dc/report.md`.

`check-evidence-diff.ps1 -BaseRef d84020507f42cf1ad8a1dfedcc1822967d4982a0 -HeadRef 2d0273e3fcf8750ce454dd93c980fb5dece3c59e`: 2 commits, 0 violations.

## Not taken

A-2 (a committed audit test that a non-fixture `.cs.txt` is refused) was not added. It belongs in `LandNonCodeSkipTests`, and touching that audit test requires that class plus the C1483 classes. That is a separate long run beside the closed Linux list, so it is not the cheap advisory.

## Merge

- `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` at the fix is clean. Tree `39b073432c291b3ee9f5a0ec94cc7490d40b50e2`. rc tip `ade54222543381e2e1b31ad79d8ae33a4035f70b`.
- Whole-branch `merge-tree` against `origin/master` `0e7dd390520a43ab330396a72e9085a8691ac332` still conflicts on the review's 18 paths. This round's two files are not among them.
- Card-delta `merge-tree --merge-base 8edda999623a473be2d1970727c172bfb5088d59 origin/master HEAD` is clean. Tree `9148c9b0c4920df062481efe27495ead87b5f6f0`.
- Land CARD-1613 first. CARD-1614 slice A then runs one `-Update` on the combined tree. This round does not change that order and did not touch those branches.
- Native Windows CP-9, CP-10 and CP-11 stay owed on this tip.

## Status lines

- Red: `New_exclusion_without_marker_fails` failed 1/1 on missing `unwitnessed-suite`; pushed `a96dc222be6257177901ef405794f437287fbb86`.
- Fix: mutation 10/10 and real-policy witness 3/3; pushed `2d0273e3fcf8750ce454dd93c980fb5dece3c59e`. No definition file changed.
- Checkpoints: run `20261030-020534-39dc` GREEN, 73/73, wall 15m52s.
