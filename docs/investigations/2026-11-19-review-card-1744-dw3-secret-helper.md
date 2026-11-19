# Review: CARD-1744 DW-3 desktop-win phase 1 secret helper (round 1, Final)

Subject: `feat/card-task-066a9073` tip `058dd52a8ae457f4e97dccff8d18139d97a4855c`,
range `4da65a3cb107d42578dca963f5c914030b477437..058dd52a8ae457f4e97dccff8d18139d97a4855c`
(red `8c2d3bf6`, helper `a387209f`, here-string removal `058dd52a`). Code author: Grok.
Reviewer: Claude Opus 5.5, task 54633aec, runner mirror on server2 (Linux).

Verdict: **BLOCKERS (3)**. Do not land. No secret value appears in this document.

## What holds

- Leak paths. Every console line is fixed text, a key name, or a path. The `catch`
  prints a fixed refusal and never the exception. No `Write-Verbose`, `Write-Debug`,
  `Write-Information`, `Write-Progress`, `Write-Error` or `ConvertTo-Json` touches the
  value. No environment variable carries it. The child processes are `stat -c`
  (path), `icacls <path> /inheritance:r`, `icacls <path> /grant:r DOMAIN\user:(F)`,
  `chmod 600 <path>` and `chmod <mode> <path>`. `Invoke-Dw3Native` refuses any argv
  that contains the value. `dotnet` is never started. No temp file or `.bak` is written.
- Value equality. The file holds lowercase hex plus one LF. The user-secret holds the
  same 64 hex characters. The runner reads `File.ReadAllTextAsync(...).Trim()`
  (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:119-120`). The server
  compares the raw UTF-8 bytes of `SharedSecret`
  (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:260`).
  UTF-8 is written without a BOM, so the two sides are equal. CP-1
  `execute-secret-matches-user-secrets` proves this for the first `-Execute`.
- Refusals. The agent-env guard reads only `ANTIPHON_ORCHESTRATOR`/`CLAUDECODE`. No
  parameter can bypass it. Dry-run and `-WhatIf` print key names only and write
  nothing. A second `-Execute` without `-Rotate` refuses before writing anything.
  Malformed JSON refuses before writing anything.
- PS 5.1/7. Both files and the example are ASCII-only. There is no ternary, `??`, `&&`
  or `||`. `RNGCryptoServiceProvider` exists in both runtimes.
- Witness strength. The red witness was re-run from a `git archive` of `8c2d3bf6`:
  `FAIL helper-missing`, `RESULT passed=0 failed=1`, exit 1. Three local mutants ran on
  scratch copies of the tip, each under its own build-slot lease, and each turned
  exactly one witness red (`passed=52 failed=1`):
  - `Write-Warning` of the value: `execute-streams-have-no-secret` failed.
  - `[Console]::Error.WriteLine` of the value: `execute-streams-have-no-secret` failed.
  - `& icacls $Secret` (passes the value as a child argument): `execute-child-argv-has-no-secret` failed.

  All three mutants were reverted. They existed only in scratch.

## Blockers

### B-1 The secret file and the user-secret can diverge, with no rollback

- Where: `scripts/desktop-win-phase1-secrets.ps1:315-334`. The phase1 copy and the
  secret file write (321) come before the user-secrets serialization and write
  (`Write-UserSecretMap`, 334). The `catch` (350-352) reports only `REFUSED: failed`.
- Failure (probes on scratch copies, values compared by hash only):
  - Rotate, then the user-secrets write fails (`secrets.json` read-only): exit 2,
    `REFUSED: failed`. The file holds the NEW value and `secrets.json` holds the OLD
    one (`equal=False`). Nothing is rolled back. The message does not say which half
    changed. The live runner gets 403 on its next registration.
  - First `-Execute` against a `secrets.json` that has a nested object, or (pwsh 7) an
    ISO-8601 date string that `ConvertFrom-Json` turns into a `DateTime`: exit 2. The
    secret file is created and the phase1 config is copied, but `secrets.json` has no
    key. `Format-Dw3JsonScalar` rejects the value only after the secret is on disk.
- Why: this matches the brief's blocker "file and user-secret able to diverge". The
  serialization problem can be detected before generating anything.
- Fix: build and validate the complete new JSON text in memory before generating or
  writing anything. Stage both writes (B-2/B-3 temps), commit them, and on a failure
  after the first commit restore the other half (keep the old file bytes in memory, or
  use the `File.Replace` backup and delete it after). Print which half is new if
  restore fails. Add witnesses for a Rotate whose user-secrets write fails and for a
  non-scalar `secrets.json`, each asserting the two values are still equal (old/old).

### B-2 The secret file is world-readable during the write and is left on disk after an ACL failure

- Where: `scripts/desktop-win-phase1-secrets.ps1:321-322`. `WriteAllText` creates the
  file with the default or inherited ACL. Only then does `Set-OwnerOnlyFile` restrict
  it. The `catch` does not delete the file.
- Failure (probe with a `chmod` that exits 1): exit 2 `REFUSED: failed`, and the secret
  file remains at mode `644`. On Windows, a new file under a newly created
  `C:\Antiphon\runner\secrets` inherits from `C:\Antiphon`. That ACL commonly includes
  `BUILTIN\Users` read inherited from `C:\`. So the window, and any leftover after an
  `icacls`/`Set-Acl` failure, can be read by other local accounts.
- Why: this matches the brief's blocker "world-readable window or leftover secret file
  after failure".
- Fix: create the file owner-only before any value byte is written. On Windows, create
  an empty file or temp sibling, apply the protected DACL, verify it, then write. On
  Linux, use a 0600 create mode or umask. Delete the file or temp on any failure. Add a
  witness that fails the restrict step and asserts no file remains.

### B-3 The operator's `secrets.json` is rewritten in place, so the update is not atomic

- Where: `scripts/desktop-win-phase1-secrets.ps1:117`. The write is
  `[System.IO.File]::WriteAllText($Path, ...)` (`FileMode.Create`: truncate, then write).
- Failure: a crash, power loss, antivirus lock or full disk between the truncate and
  the write leaves an empty or partial file. That destroys every production
  user-secret, including the database connection string and the server2 phone-home
  secrets. There is no backup. A server reloading user-secrets at that moment reads a
  truncated file.
- Why: this matches the brief's blocker "an in-place non-atomic rewrite of the
  operator's user-secrets".
- Fix: write a sibling temp file in the same directory and flush it. Then use
  `[System.IO.File]::Replace(temp, target, $null)` on Windows, which keeps the target
  ACL, or a rename on Linux, after copying the mode. Never leave a `.bak` (it would hold
  every secret). Remove the temp on failure.

## Advisories (cards, do not hold the land)

- A-1. The `catch` hides the failing step. Print a step name (for example
  `step=secret-acl`) with no exception message.
- A-2. The owner check compares the first existing ancestor's owner with `USERNAME`. A
  `C:\Antiphon` created from an elevated shell is owned by `BUILTIN\Administrators`,
  so the check refuses with `secrets-dir-owner`. Document this in the operator steps.
- A-3. If a secret file exists but `secrets.json` has no key, the helper overwrites the
  file without `-Rotate`.
- A-4. pwsh 7 `ConvertFrom-Json` turns date-shaped strings into `DateTime`. After the
  B-1 fix this becomes a clean refusal. Consider telling the operator to run under
  Windows PowerShell 5.1, or parse without date handling.
- A-5. Coverage gaps: no witness for `-Rotate` success (old replaced, both equal), for
  malformed or non-object JSON (refuses and leaves the file byte-identical), or for
  failure-path cleanup.

## Windows ACL: UNOBSERVED, operator check on the desktop

After `-Execute`, run these in the operator shell. They print no secret:

    icacls C:\Antiphon\runner\secrets\phone-home
    (Get-Acl C:\Antiphon\runner\secrets\phone-home).AreAccessRulesProtected
    (Get-Acl C:\Antiphon\runner\secrets\phone-home).Access | Select-Object IdentityReference,FileSystemRights,IsInherited

Expected: exactly one ACE, `<DOMAIN>\<user>:(F)`, not inherited, and
`AreAccessRulesProtected = True`. Run `icacls` on
`%APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json` before and after, and
confirm the ACE list is unchanged.

## Checkpoint rerun (tool run 20261119-154224-d952, commit 058dd52a8, sourceState=clean, buildSource=verified)

| CP | Result |
|---|---|
| CP-1 | exit 0, RESULT passed=53 failed=0 |
| CP-2 | 42/42 passed |
| CP-3 | 39/39 passed |
| CP-4 | 27 run, 26 passed, 1 failed: `regenerate_script_reports_changed_then_unchanged`, because the regenerate driver dll was not built (the test runs it with `-NoBuild`). This is an environment precondition the row's Antiphon.Tests build does not produce, not caused by the subject. After a build-slot `dotnet build scripts/regenerate-derived-impact-index`, the method re-ran green from the kept `bin-dw3` output: 1/1 |
| CP-5 | 6 run, 6 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`, Linux skip, not a pass) |
| CP-6 | exit 0 |
| CP-7 | exit 0, digest `a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c`, not re-pinned |
| CP-8 | exit 2 `shallow_or_unknown_history` on the shallow mirror. After `git fetch --unshallow`, the same script on the same range: commits=3 entries=0 violations=0, exit 0 |

The tool verdict is RED (exit 1) because of the two environment reds above. Both
re-ran green outside the tool, for the stated reasons. `merge-tree --write-tree` is
clean against brief master `d86d0f39422b` (`fc873338`) and against current
`origin/master` `a12ff2ee` (`f4b56dc8`).

Unlisted runs, with reasons: the red-witness run; three mutant runs (scratch,
build-slot); four failure-path probes (scratch paths, throwaway values, files deleted
by literal path afterwards); the regenerate-driver build plus the one-method rerun
(CP-4 precondition); the check-evidence-diff rerun (CP-8 shallow history). PCs: none
in the plan. Mutation is waived (operator 2026-11-12) and stays pending.
