# CARD-1609 round 15

Production apply is `ssh mc@server2 bash -s`. The proof directory is `/home/mc/antiphon-c590/c1008-proof-<operation>-<started>-<nonce>`, created and owned by `mc`. A root apply does not accept an app-owned proof directory. Custody was not loosened.

Code tip: `7c10c03abc3a48668fb7e098bb387ca4dd166faf`. Product script hashes are unchanged: `scripts/c590-remote.sh` `9ae21bbc7f2a6cebcac5c63aceaed7287ffdab82`, `scripts/c1008-retirement.sh` `1192163c68ec119cfd268dd84ffa8394117e9942`.

## Checkpoint

One run, `20261101-003038-a37b`, verdict GREEN exit 0. Source `7c10c03abc3a48668fb7e098bb387ca4dd166faf`, sourceState=clean, buildSource=verified. Wall 116m26s. 11 green, 0 red, 0 skipped. No quiet re-runs. Windows CP-11, CP-12, and CP-13 were not run.

| Row | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-3 | 26 | 26 | 0 | 0 |
| CP-4 | 12 | 12 | 0 | 0 |
| CP-5 | 30 | 30 | 0 | 0 |
| CP-6 | 132 | 132 | 0 | 0 |
| CP-7 | 52 | 52 | 0 | 0 |
| CP-8 | 11 | 11 | 0 | 0 |
| CP-9 | 22 | 22 | 0 | 0 |
| CP-10 | 3 | 3 | 0 | 0 |
| CP-15 | 16 | 16 | 0 | 0 |
| CP-16 | 23 | 23 | 0 | 0 |
| CP-17 | 10 | 10 | 0 | 0 |

CP-3 floor stays 25. CP-17 floor stays 4. CP-10 is the native N1/N2/N3 class, 3/3. The earlier runs `20261031-224720-95fc` and `20261101-002827-bae3` do not count.

## What changed

- `f57e3c0c4a73de3b8276ff60927a7793ba9c9dab` witnesses proof-directory custody and a valid-token protected-name refusal.
- `3dbcaa60059e3021856c45e8077885f36c279998` applies the native retirement fixture as the proof owner.
- `e9fe79885d88dbd2a7ebbb528592679e04af8794` keeps the production 900s publication window and budgets the close matrix at 150s.
- `da49fb61ae5f79a35d55a4e9e5c793367bbbeb5c` states the proof ownership rule.
- `7c10c03abc3a48668fb7e098bb387ca4dd166faf` keeps the native cancel inside the one-second audit cap. The shim execs the real docker client. Volume rm stays in node.

Gate semantics are unchanged: default off; the gate runs before lock, journal, stop, rm, and preview consume; redeploy-old skips recycle unless `-RecycleVolumes` is passed.

## Mutants

Each mutant was one exact edit, run method-scoped through its own build-slot lease on `tests/Antiphon.Tests/bin-c1609-r15/Antiphon.Tests.dll`, then restored. After every restore, `git hash-object` matched HEAD for both scripts. Each killing run was total 1, failed 1, exit 2. Each restored run of that method was total 1, passed 1, exit 0. No mutant was committed.

| Mutant | Kill |
|---|---|
| M1. Token compare `!=` flipped to `=`. | `Resume_refuses_until_the_operation_token_matches`: expected `DIAGNOSIS=setup-cut`, got `DIAGNOSIS=apply-gate-off`. |
| M2. Token clause removed; only the operation-id regex remains. | `Manual_apply_refuses_until_the_operation_token_matches`: exit 0, removed=4, outcome=completed. |
| M3. Compare became `enabled-c1008*`. | `Scripted_apply_refuses_until_the_operation_token_matches`: wrong operation `…000099` completed, removed=4. |
| M6. Early gate moved into the temp-only branch. | Same scripted method: host apply-off exit 0, removed=3, outcome=reclaimed. |
| Post-stop. Early gate removed and inserted immediately after `docker stop`. | `Apply_gate_is_checked_before_every_volume_removal`: the index conjunction was false (174ms). |
| M4. `return 0` first in `c1609_require_ready_preview`. | `Receipt_shape_custody_and_digest_are_strict`: "J2 must refuse duplicate", exit 0. |
| M4b. `return 0` first in `c1609_recheck_target`. | `Recheck_target_refuses_a_changed_fingerprint`: `changed.Exit` was 0. |
| M4c. The unique digest compare replaced with `:`. | J2: "J2 must refuse digest", exit 0. |
| M4d. Both fingerprint compares replaced with `true`. | `Volume_incarnation_and_operation_are_bound`: "J3 must refuse changed CreatedAt", exit 0. |
| M5. Host target line gained `"${C1008_PROJECT}_runner-state"`. | `Valid_token_refuses_protected_names_and_keeps_host_state`: exit 2, `DIAGNOSIS=RecycleProtectedVolume`, where exit 0 was required. |
| Custody. Directory uid check dropped. | `Proof_directory_custody_matches_the_applying_account`: stranger-dir `99999:700` accepted. |
| Custody. Both `-L` checks removed. | Same method: `symlink-dir-safe-stat` accepted. The real symlink case still refused. |
| Custody. Directory mode `& 022` block dropped. | Same method: world-dir mode 0777 accepted. |

## Land, evidence, merge-tree

`scripts/audit-land-verification-graph.ps1 -Check` exited 0. Digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, count 66, `approved`. `-Update` was not run. The evaluator was built through `scripts/build-slot.ps1` to `tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/` (lease `ef6e7aad-62ca-467d-89d7-91bbb0e11b87`). The canary was removed by the script.

`scripts/check-evidence-diff.ps1 -BaseRef 0efeaa25ac5cd4228da6f2d25ab7fe971361892d -HeadRef HEAD`: not shallow, 5 commits, 0 entries, 0 violations.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`. `origin/master` is `bf0b874e695a642118e2dd55913228e2f635d009`. Merge-tree exited 0 and wrote `951f032a8da57a98fabc5584fdda6ffeb4e297d4`. No conflicted names. Nothing was merged. `origin/rc/2026-10-27` was not touched.
