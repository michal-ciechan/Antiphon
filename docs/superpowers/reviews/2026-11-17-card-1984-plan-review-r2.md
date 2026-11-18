# CARD-1984 plan review, round 2

Diff-only Final plan review of `docs/superpowers/plans/2026-11-17-card-1984-spawn-lane-plan.md`. Blockers only. Reviewer is Grok. Author is Claude Opus, plan task `8344871d-196a-4876-ae74-5e75c2e1651f`, tip `4a303dd0516308bf23b1e1fb9b4921995135c57b`.

Reviewed range `420ceb9af4ef807a2e6bf0223b4b02aac6d46a57..4a303dd0516308bf23b1e1fb9b4921995135c57b` (1 commit, the plan only, +159/−18). Fetched `origin/master` is `62cfb330883cda0959d4f87f8f456933f35ca1cc`. `git merge-tree --write-tree origin/master HEAD` exited 0, tree `a9d8147204928ccd35cbf0d22e7c35974869db10`. `scripts/check-evidence-diff.ps1` over that base..tip reported commits=3, entries=0, violations=0. The subject changes no code and no tests, so the guard batch was not re-run. CP-1..CP-20 are the future Code rows; they were not executed. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## Verdict

CLEAN. Round-1 B-1 and B-2 are fixed. No new blocker. O-1, O-2, O-3, O-4, and O-6 still stand. O-5 stands as the sharded restore. O-7 stands: keep `C1008_` nightly.

## B-1 is fixed

S5 no longer puts `RemoteScriptContractTests` in `classes`. D-10 adds optional `shards` and `nightly` on the existing `remote-scripts` kind. RS-2, RS-3, and RS-4 are separate method rows (`CP-merge-subset-RemoteScriptContractTests-<slug>`), each its own `dotnet run`, admitted only when both samples are at most 285 s. They never join `CP-merge-subset-tests`. RS-1 `C1008_` is `nightlyOnly`. The five exact `methods` stay, so `CP-merge-subset-RemoteScriptContractTests` and the pins on it stay. W-7..W-9 are red before the selector change.

The selector today refuses an unknown kind property (`allowedKind` is `id`, `globs`, `classes`, `methods`, `added`, `derive`, `windows`). S5a going red on `shards`/`nightly` is the current code. `New-FilterRows` emits one method row per class because `$MethodsByClass` is keyed by class, and a whole class skips that method row. An optional `shards` map that emits its own rows, and that rewrites only a class which has shards, does not change selection for any other class. No kind lists `RemoteScriptContractTests` under `classes`. `native-linux-gate-roster` globs the test file and selects `C1210_Roster_closure_matches_source_gates` only.

`MethodsByClass` must not absorb the shard prefixes, or the five-method row would grow. The plan says that, and it is why one kind per shard was rejected. `MergeHighRadiusSubsetTests.AssertImpactedMethods` (`:410-418`) only requires the existing row to start with `/*/*/RemoteScriptContractTests/(` and to contain `(C849_Deploy_ordering_contract_is_pinned*)`. `ImpactedClassesSelectorTests` (`:252-260`) contains the five method names. Extra shard rows do not force those pins to change.

CARD-1820 `822d7dd68dc9` does not touch `scripts/impacted-classes.ps1`, `tests/impacted-classes.json`, or `MergeHighRadiusSubsetTests.cs`. Its only hunk in `ImpactedClassesSelectorTests.cs` drops `C1960QuarantineReleaseRouteTests` from a different class filter at line 461. The remote-scripts needles and the kind-id list are not that line. The edit is additive. Waiting for CARD-1820 only if CP-16 shows a pin in that file must change is the right hedge.

Fail-closed for an unmatched method is W-8, not the selector. The selector parses no method names (rejected option v). W-8 reflects every `[Test]` method and requires exactly one prefix in `shards` ∪ `nightly`, and no empty prefix. On this tree, 231 `[Test]` attributes bind to 229 methods (`C849_Prune_and_rollback_retain_roots_and_recovery` carries three). Every one of those methods matches exactly one planned prefix. Counts are RS-1 15, RS-2 28, RS-3 48, and the rest under the 46 RS-4 prefixes. No prefix is a prefix of another, and none is empty. The RS-3 and RS-4 lists are ordinal, which is what `New-MethodFilter` emits (`StringComparer.Ordinal`). W-8 is red in S5a and green after the table edit.

## B-2 is fixed

Rule R1 allows every current literal in the W-5 scan set, and the negative controls stay findings.

Fixture globs `c1008-*`, `c1105-*`, and `c1192-*` (no `/tmp` or `/dev/shm` literal under `c1087-*`):

- `mkdtempSync('/tmp/…')` and `mkdtempSync('/dev/shm/c1008-preview-')`, including the `c994 ? '/tmp/c994-real-' : '/tmp/c1008-real-'` ternary, are A2.
- `startsWith('/tmp/c1105-audit-')` and `startsWith('/dev/shm/c1008-preview-')` are A3 (and A4).
- `mktemp -d /tmp/c1192-parent.XXXXXX` is A1 and A2. The cleanup glob `/tmp/c1192-parent.*` is the A3 stem the rule cites. The `case` arm `SERVER2_ROOT="/tmp/c849-fixture-$RUN"` is A4.
- `/tmp/antiphon-pty-hosts` and `/tmp/antiphon-pty-hosts/fixture`, in the fake docker script, the recycle Dockerfile string, and `c1105-compose-v218-config.json`, are A5.

Widened class files use the same clauses: `mktemp -d` with `XXXXXXXX` plus a same-file `[[ == /tmp/…???????? ]]` (A3, the c1192 shape), `/tmp/c849-guard-$$` (A1), `Guid.NewGuid()` in the same statement (A1), `ShouldContain` / `ShouldNotContain` / `Contains` (A4), and the exact tokens `/tmp/donor`, `/tmp/escape`, and `/tmp/c994-command-sentinel` (A5). `../tmp/real.db` is excluded because `/tmp` follows `.`. A `//` comment line is excluded.

The five controls stay findings: `Directory.CreateDirectory("/tmp/foo")`, `mkdir -p /tmp/foo`, an extended allowlisted token, a stem in a file with no `mkdtemp`, and `File.WriteAllText("/tmp/foo")` after a closed `ShouldContain`. CP-1 can go green on the fixtures.

## Shard times

The 90–150 s figure is the RS-4 shard wall, not 90–150 s for each of the 46 prefixes. Strict-lane methods run at width 1 beside the wide lane, so the shard wall is the max of the two, not the sum. None of the admitted shards is past 285 s on the profile numbers.

| Shard | Strict serial | Wide wall at width 6 | Expected wall | Past 285 s? |
|---|---:|---:|---:|---|
| RS-2 `C1105_` | `C1105_Git_audit_volume_scale` 49.4 s | max(74 s, (985 − 49.4) / 6 = 156 s) | 165–200 s | no |
| RS-3 workspace | two C912 methods, inside a 72 s / 9-case family, so ≤ 72 s; `C913_Receipts_*` unobserved | max(70.8 s, 595 / 6 = 99 s), plus the unobserved C913 cases | 100–150 s | no on the profile bound |
| RS-4 rest | `C849_front_door_*`, inside the observed C849 164 s / 32 cases and absent from the top 15, so < 43 s | max(54.8 s, 294 / 6 = 49 s); the 46 unobserved cases share the ~236 s residue | 90–150 s | no |

The ~236 s residue is the full-receipt delta (3597 s for 511 cases versus 3361 s for 462), at a different load, spread across the unobserved set (C1008 ×3, C913 ×6, and the RS-4 46). Giving all of that residue to one `C913_Receipts` case and adding the entire C912 family (72 + 236 = 308 s) is the only way RS-3 clears 285 s, and that attributes every unobserved second to one method. The plan does not admit on the estimate. A shard over 285 s in either sample moves to `nightly` in the same S5b commit. A shard over 570 s stops. That is the B-1 bar.

## Checkpoints

Read-only import at this tip, load 1.30, lease `7ec6c4a3-b3e3-40aa-b3af-1751ee41bbf5`, `dotnet run --no-build` of the already-built checkpoint tool: exit 0, 20 rows, no warning line. Output `.antiphon/c1984-plan-import-r2.yaml` (gitignored). Ids are `CP-1`..`CP-20`. Every row `serial: true`. Output paths end with `/`.

Derived deadlines are `max(15, 3 × EstimatedMinutes)`: CP-1 and the other estimate-15 rows, including CP-8, are 45 minutes. CP-3 (estimate 6) is 18. CP-17..CP-19 (estimate 5) are 15. CP-20 (estimate 8) is 24. CP-8's width-6 wall is 10–11 minutes, under 45. Each admitted shard's 285 s bar is under its 15-minute deadline. CP-20 is a measurement row, expected over 285 s and about 5–7 minutes at width 6, under 24 minutes. It is not a Merge row.

## O-7

Keeping `C1008_` on the nightly Unit lane is acceptable. One case is 315 s, and 315 × 2 is already over the 570 s cap.

What leaves Merge is the C1008 recycle matrix: `C1008_Recycle_refuses_unpublished_and_dirty_work` (140.7 s), `C1008_Recycle_refuses_uninspectable_git` (267.9 s), `C1008_Recycle_receipt_records_disk_and_partial_failure` (315 s), and the C1008 resume-receipt variants. The 132-location hidden-commit audit and the receipt-gated resume gates are `C1105_` (hidden locations are 70 s for 132 cases; the resume cases are the bulk of the 985 s). Those are RS-2 and come back.

A later per-scenario split of the 315 s case (profile change #3, about 35 s) removes the longest-case floor. The observed 11 cases already sum to about 1390 s, and 1390 / 6 = 232 s, which is under 285 s. The three unobserved cases, at that mean of about 126 s, add about 379 s, and (1390 + 379) / 6 is about 295 s. The split admits RS-1 only if those three are cheaper than the mean, or the split also cuts the sum. This plan is right not to admit RS-1. The follow-up card should re-measure.

## Advisory (does not hold the next stage)

W-8 is not on the selection for a later edit of `RemoteScriptContractTests.cs`. `native-linux-gate-roster` matches that file and selects only `C1210`. A new method that matches no prefix is omitted from Merge until `ImpactedClassesShardRowTests` runs. The nightly Unit lane runs it. The plan already chooses a listed partition because the selector parses no method names.

The C1008 follow-up should not treat "split the 315 s case" as sufficient by itself. Re-measure sum / 6, as in O-7 above.
