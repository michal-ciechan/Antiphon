# Review: CARD-1744 DW-3 desktop-win phase 1 secret helper (round 2, Final)

Subject: `feat/card-task-478a0c11` tip `3b652b37c03c98a023cee9f702182304834c65f8`.
Round-2 range `058dd52a8ae457f4e97dccff8d18139d97a4855c..3b652b37c03c98a023cee9f702182304834c65f8`
(blocker witnesses `af1a06d6`, fix `3b652b37`). Code author: Grok.
Reviewer: Claude Opus 5.5, task e09c2246, runner mirror on server2 (Linux). Round 1:
`docs/investigations/2026-11-19-review-card-1744-dw3-secret-helper.md` at `ea355840`.

Verdict: **BLOCKER (1)**. Do not land. No secret value appears in this document.

## Round-1 blockers

- B-1 (halves diverge on partial failure): fixed for every failure the script can catch
  on Linux. Both halves are staged before either swap. `user-secrets` is committed first,
  then `secret-file`. A caught failure after the first swap restores `secrets.json` from
  the bytes held in memory. Reproduced below.
- B-2 (secret readable during write, left after a permission failure): fixed. On Linux,
  the temp is created by `FileStreamOptions.UnixCreateMode` = 0600 with `CreateNew`.
  No byte exists before the mode is set. On Windows, the temp is created empty, then
  `icacls` and `Set-Acl` run, and `Test-Dw3OwnerOnly` checks the result. Only then is
  the value written, through a `FileShare.None` open. A handle opened during the
  inherited-ACL window makes that open fail with a sharing violation, so no secret byte
  reaches a pre-opened reader. That path is UNOBSERVED on Windows. A failure deletes
  the temp.
- B-3 (in-place rewrite): fixed on Linux. `File.Move(temp, target, overwrite)` is
  `rename(2)` in the same directory. On Windows it is NOT fixed for every failure: see
  the new B-1 below.

## New blocker

### B-1. A partial `File.Replace` failure deletes `secrets.json` and prints `changed: none`

- Where: `scripts/desktop-win-phase1-secrets.ps1:585-610` (`Move-Dw3TempOnto`,
  `Publish-Dw3Staged`) and the outer `catch` at `:834-857`.
- Failure: on Windows, `File.Replace(temp, target, null)` is `ReplaceFileW` with no
  backup. The documented `ERROR_UNABLE_TO_MOVE_REPLACEMENT` (1176) case reads: "If
  lpBackupFileName was specified, the replaced and replacement files retain their
  original file names. Otherwise, the replaced file no longer exists and the
  replacement file exists under its original name." `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2`
  (1177) also leaves the target name empty. In both cases .NET throws an `IOException`.
  `$script:dw3Changed['user-secrets']` is set only after `Move-Dw3TempOnto` returns,
  so it stays false. The `catch` then:
  1. deletes every `.dw3part`, including the only copy of the new JSON;
  2. finds `changedCount = 0` and restores nothing, although `$oldJsonBytes` is still
     in memory;
  3. prints `changed: none`.

  The result is that every production user-secret is gone, and the output says nothing
  changed. This is the round-1 B-3 outcome, reached through a failure instead of a
  crash. The same code path at the `secret-file` commit under `-Rotate` deletes the old
  secret file. The outer `catch` then sees one changed half (`user-secrets`) and
  restores `secrets.json`. The output says `changed: none`, but the secret file is
  missing. That is a divergence the output does not name.
- Reproduced by emulation on Linux, in a scratch copy only. The mutated
  `Move-Dw3TempOnto` deletes the target and throws `IOException`, which is the
  documented 1176 outcome. A `-Execute -Rotate` run against a 17-key fixture printed
  `step=user-secrets-commit` and `changed: none`, exited 2, left `secrets.json`
  ABSENT, and left no `.dw3part` behind. The real Windows API call is UNOBSERVED.
- Why it is a blocker: the brief lists two cases as blocker-grade, a non-atomic rewrite
  of `secrets.json` and a failure that leaves the halves different without saying which.
  This failure produces both.
- Fix (any one of these is enough):
  - (a) In the `catch`, do not trust the flags. For each half, compare the target on
    disk with what was saved (it exists or not, and its bytes). Restore from
    `$oldJsonBytes`/`$oldSecretBytes` whenever they differ. Report each half that still
    differs after the restore.
  - (b) Never delete a temp whose target name is missing. Move it back instead, or
    restore the old bytes from memory.
  - (c) Pass a same-directory, owner-only backup path to `File.Replace`, then delete it
    after success. In the 1176 case, both files then keep their names. This conflicts
    with the current "no .bak" text, which would need amending.

  Add a witness through an injectable replace seam: delete the target, then throw. It
  must assert that `secrets.json` is byte-identical afterwards, or that the output names
  the half.

## Checks that held

- Two-file commit order (brief item 1). A two-file "both-or-neither" result cannot be
  made atomic against a hard kill, and the plan does not claim it. It claims restore on
  a caught failure, which holds apart from B-1. A real `Process.Kill()` was injected
  between the swaps in a scratch copy:
  - Rotate: `secrets.json` held the new value and the secret file the old one
    (mismatch). A dry-run printed `present`/`present` and `shared-secret-key: present`,
    so it cannot show the mismatch. Re-running `-Execute -Rotate` converged: file equal
    to map.
  - First run: the key was set and the secret file was ABSENT. The dry-run shows this. A
    plain re-run refuses `shared-secret-present` ("Re-run with -Rotate"), and
    `-Rotate` converges.

  Operator recovery after any interrupted run is to re-run `-Execute -Rotate` before
  restarting AppHost. The server reads `secrets.json` only at start, so nothing reads
  the mismatched pair before then. See A-1.
- Temps (item 2). The Linux mode is 600 at creation, observed and witnessed. Windows
  creates the file empty, applies the ACL, verifies it, and only then writes the value
  (UNOBSERVED). Temps left by a hard kill are mode 600 (A-2).
- `File.Replace` ACL (item 3). `ReplaceFileW` keeps the DACL of the replaced file. With
  `ignoreMetadataErrors=false`, a failure to merge it is a failure before any change. The
  script also re-applies the saved `Get-Acl` afterwards. The operator's ACL is therefore
  kept, and the owner-only ACL of the temp is not carried over (UNOBSERVED). A locked
  target gives `ERROR_UNABLE_TO_REMOVE_REPLACED` (1175) or a sharing violation. Both
  files keep their names, so that path is safe. The unsafe 1176/1177 path is B-1.
- Leftover state (item 4). I ran four failure injections myself with `-Rotate` against a
  realistic fixture:
  - `DW3_FAIL_STEP=user-secrets-write`
  - `DW3_FAIL_STEP=secret-file-commit`, after the JSON swap
  - a secrets directory at mode 500
  - a user-secrets directory at mode 500

  Each printed the correct `step=` and `changed: none` and exited 2. In each,
  `secrets.json` and the secret file were byte-identical (sha256), modes stayed
  640/600, and no `.dw3part` was left.
- Leaks (item 5). Five malformed documents were each refused with
  `user-secrets-malformed`, a fixed text that names neither the content nor a position:
  - a duplicate key
  - a trailing comma
  - a comment
  - a bad escape
  - an array root

  A canary in the content never appeared in the output, and the file stayed
  byte-identical. The leak mutant `Write-Warning` of the value, in a scratch copy,
  ran CP-1's witness at `RESULT passed=136 failed=3`. The failing witnesses were
  `execute-`, `nested-date-` and `rotate-success-streams-have-no-secret`. The mutant was
  killed. Child argv is still guarded by `Invoke-Dw3Native`.
- JSON round trip (item 6). I used a realistic fixture of my own:
  - a UTF-8 BOM
  - flat colon keys, including `PhoneHomeRunner:Runners:server2:SharedSecret`
  - a connection string with `\"`, `\\` and `\/`
  - `\uXXXX` escapes, including a surrogate pair, and literal non-ASCII
  - control escapes
  - date-shaped strings
  - nested objects and arrays, empty `{}` and `[]`
  - `2.50`, `-0`, `1e10`, `1.5E-3`, `12345678901234567890123` and `9223372036854775808`
  - `true`, `false`, `null`
  - an empty key

  All 17 original keys compared equal under node `JSON.parse`. Number lexemes were
  preserved verbatim, 15 keys were added, and the file mode (640) was kept. The secret
  file equals the map value. Duplicate keys are refused with no write.
- PS 5.1 + 7 (item 7). Both scripts are ASCII-only, with no `??`, `&&`, `||` or
  ternary. The 3-argument `File.Move` and `UnixCreateMode` are only on the non-Windows
  branch. `File.Replace` is reached by reflection.

## Advisories (cards; they do not hold the land)

- A-1. A dry-run cannot detect a mismatch between the secret file and the map after an
  interrupted `-Rotate`. Print `secret-file-matches-map: yes|no` (never the value), and
  name `-Rotate` as the recovery in the plan or runbook.
- A-2. A hard kill leaves `<guid>.dw3part` files at mode 600. They hold the new value,
  or the whole user-secrets JSON. A later run never removes or reports them. Sweep them,
  or list them by name.
- A-3. Key lookup is case-sensitive, and the server's JSON configuration rejects a key
  that differs only in case. A pre-existing case variant, or a nested `PhoneHomeRunner`
  object, bypasses `shared-secret-present` and creates a duplicate key that stops the
  server from starting. Refuse when a key matches case-insensitively, or when a nested
  form is present.
- A-4. Escape letters are matched with case-insensitive `-eq`, so the invalid `\N` is
  accepted and rewritten as `\n`. Valid JSON is unaffected. Use `-ceq`.
- A-5. CP-5's expected Linux skip makes `validate` refuse the whole run (`row_failed`).
  Put CP-5 in its own run, or validate with `--rows`.

## Evidence

Checkpoint run `20261119-165743-8b59` at `3b652b37c03c98a023cee9f702182304834c65f8`:
8 green, 0 red, 1 build, `unlisted: none`, sourceState clean.

| Row | Result |
|---|---|
| CP-1 | `RESULT passed=139 failed=0` |
| CP-2 | 42/42 |
| CP-3 | 39/39 |
| CP-4 | 27/27 |
| CP-5 | executed=6, passed=6, skipped=1 (the expected C1210 skip, which is not a pass) |
| CP-6 | exit 0 |
| CP-7 | `digest=C5B7C0CE...43FC count=80`, exit 0, not re-pinned |
| CP-8 | `commits=5 entries=0 violations=0` |

`validate --rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7,CP-8` returned VALID. Whole-run validate
returned INVALID `row_failed`, from the CP-5 skip only.

`merge-tree` against `eb31e339` gave tree `e98d9938` and against `origin/master`
`9c41f590` gave tree `61e81e1a`. Both merges were clean.

PCs stay pending. Mutation execution is waived (operator 2026-11-12).
