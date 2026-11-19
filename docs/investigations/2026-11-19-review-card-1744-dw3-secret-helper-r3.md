# Review: CARD-1744 DW-3 desktop-win phase 1 secret helper (round 3, Final, last round)

Subject: `feat/card-task-425c2be6` tip `9ddf1e80be7a0ba7a0865000e78e42d9f77b4492` (Grok author).
Round-3 range `3b652b37c03c98a023cee9f702182304834c65f8..9ddf1e80be7a0ba7a0865000e78e42d9f77b4492`
(witness `926caf36`, fix `9ddf1e80`). Reviewer: Claude Opus 5.5, task 84306e5a, server2 runner
mirror (Linux). Round 2: `docs/investigations/2026-11-19-review-card-1744-dw3-secret-helper-r2.md`
at `93ea4607`.

Verdict: **CLEAN**. Round-2 B-1 is fixed. No new blocker. No secret value appears in this
document.

## Round-2 B-1 (partial `File.Replace` deletes `secrets.json`, prints `changed: none`)

Fixed. The outer `catch` no longer trusts the change flags. `Repair-Dw3Half` compares each
target on disk with the existence and bytes saved before the change. It restores any half
that differs from the in-memory bytes, then compares again. Residue is deleted only after
that comparison matches. `changed: none` is printed only when both halves match. Each half
that still differs prints `differs: <half>` and `kept: <path>` lines.

Reproduced on a scratch `git archive` copy of the tip. I used my own fixture: a JSON with a
connection-string canary, a server2 secret canary, an old desktop-win canary and a nested
array. The secret file held the old canary. `secrets.json` was mode 640 and the secret file
600. Every run exited 2. Neither stdout nor stderr contained a canary or any 64-hex string.

| Case | Seams | Output | Final state |
|---|---|---|---|
| A | `-Rotate`, replace fault on user-secrets | `step=user-secrets-commit`, `changed: none` | both sha256 equal to before, modes 640/600, no `.dw3part`/`.dw3bak` |
| B | `-Rotate`, replace fault on secret-file | `step=secret-file-commit`, `changed: none` | both equal to before, modes kept, no residue |
| C | replace and restore fault on user-secrets | `step=restore`, `differs: user-secrets`, `kept:` names one `.dw3part` and one `.dw3bak` | `secrets.json` absent; the kept `.dw3bak` (600) holds the original bytes; secret file unchanged; no `changed: none` |
| D | replace and restore fault on secret-file | `differs: secret-file`, `kept:` names one `.dw3part` and one `.dw3bak` | `secrets.json` restored; the secret file absent; the kept `.dw3bak` (600) holds the original bytes |
| E | replace fault on user-secrets, restore fault on secret-file | `changed: none` | both equal (the secret-file half never differed, so its restore never ran) |

## Brief items

1. **Reviewer scenario.** Reproduced above (A, B), with restore failures (C, D, E). The
   output names the half that differs and the kept paths. It is truthful in every case.
2. **`.dw3bak` ACL.**
   - How the backup is made: `ReplaceFileW` makes it by renaming the replaced file. A rename
     in the same NTFS directory keeps the file's own security descriptor, and inheritance is
     not re-evaluated. The backup is therefore born with the ACL of the old target.
   - Secret-file half: `Publish-Dw3Staged` sets the old target owner-only, and checks it
     with `Test-Dw3OwnerOnly`, before the swap. The backup is owner-only from creation.
   - User-secrets half: the backup carries the operator's `secrets.json` ACL. That ACL is
     usually inherited from `%APPDATA%`: the user, SYSTEM and Administrators.
     `Set-OwnerOnlyFile` then narrows it immediately after the swap. For that window, the
     backup has the same readers as the live `secrets.json`, which holds the same secrets.
     Nobody gains read access, so this is not a new leak.
   - Hard kill: a kill between the swap and the delete leaves a `.dw3bak`. Its ACL is either
     owner-only or the live file's own ACL. This is the exposure class the round-2 `.dw3part`
     advisory A-2 already accepted (a hard-killed `.dw3part` holds the whole new JSON). It is
     not a new class. The next run still does not list or sweep either file (A-R3-1).
   - Not tested: the backup's ACL is re-applied but not checked with `Test-Dw3OwnerOnly`
     (A-R3-2). The real Windows `File.Replace` path with a backup is UNOBSERVED here.
     DW3-WIN stays pending.
3. **The catch.**
   - Both `Repair-Dw3Half` calls run in their own `try`. A comparison or restore that
     throws becomes `differs:`, which is the conservative report.
   - `Write-Dw3Kept` uses only `Test-Path`, string casts and `Split-Path`, which do not
     throw on I/O errors.
   - `Refuse` always exits 2. The original exception text is never printed.
   - Residue deletes use `SilentlyContinue` and are not re-checked. A delete that silently
     fails on a matched half leaves an owner-only file with no `kept:` line (A-R3-2).
4. **Leaks.**
   - Six streams and child argv: covered by CP-1. `Invoke-Dw3Native` still refuses the
     secret in an argument.
   - The `differs:` and `kept:` lines carry half names and paths only.
   - Mutant M1 (`Write-Host` of the new secret after generation): CP-1 harness
     `RESULT passed=194 failed=3`. Failing checks: `execute-`, `nested-date-` and
     `rotate-success-streams-have-no-secret`.
   - Mutant M2 (the catch prints the saved `secrets.json` bytes): `passed=191 failed=6`.
     Failing checks: `rotate-unwritable-`, `rotate-rollback-`, `replace-fault-json-` x2,
     `replace-fault-secret-` and `replace-restore-fault-*-no-sentinel`.
   - Both mutants were killed. Both ran on scratch copies only; the reviewed tree was never
     edited.
5. **Saved bytes.** `$oldJsonBytes`, `$oldSecretBytes`, `$expected` and `$now` are compared
   or written only to owner-only files. They never appear in an exception or a log. They are
   not zeroed; the `$secret` string cannot be zeroed either (A-R3-3, advisory).
6. **ASCII and PS 5.1 + 7.** Both scripts are ASCII-only (`grep -P '[^\x00-\x7F]'`
   finds nothing). The diff adds `-ceq`/`-cne`, `New-Object System.IO.IOException`,
   `throw $_` of an ErrorRecord, and reflection `File.Replace` with a third argument. All of
   these are valid on 5.1. There is no `??`, `&&`, `||` or ternary.

## Advisories (cards; they do not hold the land)

- A-R3-1. Round-2 A-2 is still open and now covers `.dw3bak` too. After a hard kill, a
  `<guid>.dw3part` or `<guid>.dw3bak` stays beside `secrets.json` or the secret file, and a
  later run neither lists nor sweeps it. The header says that the backup "exists only during
  the swap", which overstates it for a hard kill. Fix: list (dry-run) or sweep stranded
  `*.dw3part`/`*.dw3bak` in both directories. Document the recovery: rename the `.dw3bak`
  back over a missing target, or re-run `-Execute -Rotate`.
- A-R3-2. Two gaps around residue:
  - The `.dw3bak` gets `Set-OwnerOnlyFile` with no `Test-Dw3OwnerOnly` check.
  - Residue deletes (`Remove-Dw3Tracked`) are silent and not re-checked, on the success
    path and on a matched half.

  Fix: re-check absence after the delete and print `kept:` for anything left. Check the
  backup ACL.
- A-R3-3. The saved byte arrays are not cleared after use. Use
  `[Array]::Clear($bytes, 0, $bytes.Length)` in a `finally`.
- A-R3-4. When both halves share one directory, `Remove-Dw3HalfResidue` on the first
  matched half also deletes the other half's staged `.dw3part`. That is not the production
  layout and loses no data: the old bytes stay in the tracked `.dw3bak`. Scope the deletes
  to the half's own tracked paths.
- A-R3-5. After a failure that follows the phase1 example copy, the helper prints
  `changed: none` although it created `phone-home.phase1.json`. That file holds no secret.
  The behaviour was there before this round.
- A-R3-6. `Repair-Dw3Half` assigns to `$matches`, which is PowerShell's automatic
  variable. It works because the assignment is function-local, but rename it.
- Still open from round 2: A-3 (a case-variant or nested `PhoneHomeRunner` key bypasses
  `shared-secret-present`) and A-5 (validate CP-5 with `--rows`).

## Evidence

Checkpoint run `20261119-180050-13b9` at `9ddf1e80be7a0ba7a0865000e78e42d9f77b4492`:
8 green, 0 red, 1 build, `unlisted: none`, sourceState clean, buildSource verified, wall 13m20s.

| Row | Result |
|---|---|
| CP-1 | `RESULT passed=197 failed=0` (floor 197) |
| CP-2 | 42/42 |
| CP-3 | 39/39 |
| CP-4 | 27/27 (194 s) |
| CP-5 | executed=6, passed=6, skipped=1 (the expected Linux skip of C1210, which is not a pass) |
| CP-6 | exit 0 (approved) |
| CP-7 | exit 0, digest `a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c`, not re-pinned |
| CP-8 | `commits=7 entries=0 violations=0` |

- `validate --rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7,CP-8` returned
  `CHECKPOINT SOURCE VALID source=9ddf1e80... rows=7`. CP-5 was left out because of its
  skip (A-5).
- `check-evidence-diff` over the round-3 range returned `commits=2 entries=0 violations=0`.
- `merge-tree --write-tree` against `origin/master` `11d21a183d91463eb39c2f1585e3a26ebba5f127`
  was clean: tree `fb29dfd1fa8eac237e5234ba6fcabca5f40f375a`.
- PCs stay pending. Mutation execution is waived (operator 2026-11-12). The two leak
  mutants above were review probes; they discharge no PC.
