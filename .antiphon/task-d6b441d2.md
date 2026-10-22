# Final Review d6b441d2 of Code task 15d0382d (CARD-1417 confirm gate anchoring)

Subject `feat/card-task-15d0382d` at `12ebd3c1991980842224e7f90925d4a94f044072` (verified source
`b2fe9e3d15a24ef0634ee88d78c6b75f7ae11eb0`; `git diff b2fe9e3d 12ebd3c1` touches only
`.antiphon/task-15d0382d.md`). Combined CARD-1417 delta reviewed against merge-base `cc3e54fba` and
current `origin/master` `aebf80885bd94554f364e61eaf3b059f8fa6deb2`. Read-only on the subject; no fix made.

Outcome: **found**. CP-1/CP-2 are green with a validated clean receipt, A-2/A-3/A-4 still hold and the
legitimate invocation is not falsely refused. But the confirm gate can still be opened without the
exact `-ConfirmWindow` switch by two routes (D-1, D-2), and the W-27 row that claims to pin A-1 half 1
cannot go red (D-3).

## Defects

### D-1 Gate bypass: in-process call inside a process whose argv anchors the script

- Where: `scripts/cutover-postgres-server2.ps1` `Get-CutoverScriptArguments` (walk loop) and the
  `-Execute` gate at the end of the file.
- Failure (real pwsh 7.5.4 on server2, gate-only copy that prints `GATE-OPENED` and exits 42
  instead of running the window):
  - `pwsh -NoProfile -File wrap.ps1 -File cut.ps1 -ConfirmWindow`, where `wrap.ps1` runs
    `& "$PSScriptRoot/cut.ps1" -Execute -Conf` -> `GATE-OPENED bound=Execute=True;ConfirmWindow=True`
    (no ConfirmWindow token was bound; `-Conf` set it). A dot-sourcing wrapper
    (`. cut.ps1 -Execute -Conf`) also opens.
  - `pwsh -NoProfile -NoExit -File cut.ps1 -ConfirmWindow` (runs the harmless preview, exit 0), then
    at the same process's prompt `& ./cut.ps1 -Execute -Conf` -> `GATE-OPENED`, `$LASTEXITCODE=42`.
    The same applies to a profile, job or runspace in such a process.
- Why: the walker does not stop at pwsh's real `-File <path>` pair when that path is another
  script; it keeps scanning that script's own arguments and anchors on a decoy `-File <this script>`.
  And nothing ties the anchored argv to *this* invocation, so the documented promise that "a
  same-process call refuses -Execute" is false whenever the process argv anchors this script and
  includes the exact token.
- Fix: end the walk at the first `-File`/implicit script pair whatever its path, and treat it as
  unanchored unless it names `$PSCommandPath`. Also require this invocation to be the top-level `-File`
  invocation (for example, call-stack depth or `$MyInvocation` origin checks, or a one-shot
  process-level latch). Add W-27 rows for a decoy pair after a wrapper's `-File` and for a
  `-NoExit` in-process call.

### D-2 Gate bypass: Unicode dash forms (U+2013, U+2014, U+2015)

- Where: `Test-ExactNamedSwitch` (`if (-not $text.StartsWith('-')) { continue }` runs before the
  trailing-colon rule), plus `Get-CutoverScriptArguments` (only `-` and `/` are switch starts).
- Failure (real pwsh):
  - `pwsh -NoProfile -File diag.ps1 -Execute -Conf –HoldReason: -ConfirmWindow` (en dash) gives
    `GATE-OPENED bound=Execute=True;ConfirmWindow=True;HoldReason=-ConfirmWindow`. These also open:
    `–Conf –HoldReason: -ConfirmWindow`, `-Conf —ErrorVariable: -ConfirmWindow` (bound
    `ErrorVariable=-ConfirmWindow`) and `-Conf ―HoldReason: -ConfirmWindow`.
  - `pwsh -NoProfile –c '& ./diag.ps1 -Execute -Conf #' -File diag.ps1 -ConfirmWindow` (en or em dash
    `-c`/`-Command`) gives `GATE-OPENED bound=Execute=True;ConfirmWindow=True`. pwsh runs this as a
    `-Command` start. The walker skips `–c` and anchors on the `-File diag.ps1` text, which sits in the
    commented-out tail of the command. The ASCII `-c` form correctly refuses with `no -File pair`.
- Why: both pwsh's script binder and its host parser accept these dashes as parameter prefixes, but
  the gate only recognises ASCII `-`. So D-1b's trailing-colon value rule and the `-Command` refusal
  can both be sidestepped.
- Fix: treat `[-–—―]` as a dash in both functions, before the half-1 check. Decide
  whether `–ConfirmWindow` should open the gate. Today it refuses, which errs on the safe side. Add a
  W-27 row for each shape.

### D-3 Test adequacy: W-27 `half1-common` cannot go red (A-1 half 1 unpinned)

- Where: `scripts/test-cutover-postgres-server2.ps1:1632`, plan amendment item "A-1 halves pinned
  separately", report claim "Half 1 ... pinned by -ErrorVariable /ConfirmWindow".
- Failure: in a scratch worktree I removed half 1 (`if (-not $text.StartsWith('-')) { continue }`)
  and ran `test-cutover-postgres-server2.ps1 -Case W-27` under build-slot (lease
  `7cb5ed91-a401-46ae-994c-a49482921a60`). Result: `PASS W-27`, 1 passed, 0 failed, so it stayed
  green. The half 2 mutation (`$valueInNextToken -or` -> `$false -or`, lease
  `fc76417c-6998-40e2-b37f-9fee079c512c`) went red as claimed:
  `FAIL W-27 - half2-colon exit=3 || missing-refusal || opened-window`.
- Why: `$MyInvocation.MyCommand.Parameters` includes the common parameters under `[CmdletBinding]`,
  so `ErrorVariable` *is* in `$script:CutoverValueParameters` (printed list ends
  `...ApiBase,CommandHost,ErrorAction,WarningAction,...`). The name-resolved skip therefore removes
  `/ConfirmWindow` without half 1.
- Fix: pin half 1 with a common-parameter *alias* that the name check does not know:
  `-Execute -Conf -ev /ConfirmWindow`. Real pwsh binds `ErrorVariable=/ConfirmWindow`. It opens the
  gate with half 1 removed and refuses with half 1 present (both observed).

## Checks that held

- Legitimate invocation opens the gate (exit 42 in the gate-only copy) for all of these: `-File cut.ps1`,
  `./cut.ps1`, an absolute path, `../probe/./cut.ps1`, a symlink, a directory and file name with
  spaces and single and double quotes (absolute and relative), `-f`, `--file`, `-FILE`, `-fi`, `/File`,
  `-ExecutionPolicy Bypass`, `-WorkingDirectory /` or `/tmp` with an absolute or relative path,
  `-ConfirmWindow:$true`, a nested `pwsh -File` started from `-Command`, and
  `-WorkingDirectory -f cut.ps1`.
- Default path: `-WhatIf` and no `-Execute` give exit 0 (preview).
- These refuse as designed: `-Execute -Conf`; `-WorkingDirectory -ConfirmWindow ... -Conf`;
  `-Command "& ./cut.ps1 -Execute -ConfirmWindow"`; splatting under `-Command`; a ScriptBlock
  `-Command {}` (encoded); an implicit script path; a wrapper forwarding `@args` (no pair, documented);
  `-WorkingDirectory -File -File cut.ps1 ... -HoldReason: -ConfirmWindow`; `-ev /ConfirmWindow`;
  `-HoldR /ConfirmWindow`. A script path given twice is a binder error, as are
  `-ev|-ov|-ea|-TailnetPort -ConfirmWindow`.
- `PSCommandPath` in the environment has no effect, whether spoofed for a legitimate run (still opens)
  or for a wrapper (still refuses).
- Not testable on Linux: 8.3 short names, UNC paths and case-different paths. The production target
  is the Windows desktop, and this check has never run on Windows. On Linux, pwsh itself rejects a
  case-different path before the script runs. Treat Windows equality as unverified.
- Safe false refusals, which do not affect the verdict: an en-dash host `–File` refuses with
  `no -File pair`, and an en-dash `–ConfirmWindow` refuses.
- A-2 (RECOVERY line on a non-zero `-ClearHold`, W-28), A-3 (marker messages split between exit 2
  and written-by-this-run, W-29), A-4 (`-TailnetPort`, harness ephemeral loopback port) and the
  sentinel staying off argv and stdout (W-11, W-16..W-25, W-28, W-29) all pass on CP-1.

## Receipt

- Checkpoint run `20261022-035336-e3f6` at `12ebd3c1991980842224e7f90925d4a94f044072`, plan
  `docs/superpowers/plans/2026-10-22-card-1417-confirm-gate-anchoring-plan.md`. It ran without
  hitting exit 75 and finished GREEN, exit 0, in 2m09s.
- `CHECKPOINT CP-1 ... exit=0 slot=granted waited=0s dirty=0 source=12ebd3c1... sourceState=clean buildSource=notApplicable`.
  The console shows `CARD-1407 cutover: 30 passed, 0 failed`, including `PASS W-27`.
- `CHECKPOINT CP-2 ... exit=0 slot=granted waited=0s dirty=0 source=12ebd3c1... sourceState=clean buildSource=notApplicable`.
  The console shows `CARD-1407 maintenance: 4 passed, 0 failed`.
- `validate-checkpoint-receipt.ps1 -Rows 'CP-1','CP-2'`: `CHECKPOINT SOURCE VALID source=12ebd3c1... rows=2`, exit 0.
- Unlisted runs, each under build-slot with its reason stated:
  - the checkpoint tool build `d6b4-tool` (lease `0f2cc46e-e56c-4530-b6b6-c7d3a8158178`), whose
    `bin-d6b4-tool` directories are now deleted;
  - two single-case W-27 mutation runs (leases above) in a detached scratch worktree, since removed;
  - gate probes with plain pwsh against a gate-only scratch copy, outside the repository, with no
    build or test driver.
- `check-evidence-diff.ps1 -Repository . -BaseRef cc3e54fba -HeadRef HEAD`: `commits=7 entries=2 violations=0`.
  From `b2fe9e3d`: `commits=1 entries=1 violations=0`. The force-added reports contain no secret.
- `git merge-tree --write-tree origin/master HEAD` -> `2f4543454818cfd8cd44a5c21228ef110075d514`, no conflict.
- Cleanup: CP-1's pipe sub-case leaves the socket `/tmp/CoreFxPipe_-ConfirmWindow` behind (the
  harness still has no `finally` to remove it); I removed it. No real AppHost, database, marker or
  secret was touched.
- PCs: none named; nothing pending.
