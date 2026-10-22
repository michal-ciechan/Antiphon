# CARD-1417 challenge-token confirm gate (repair of Code task 15d0382d)

Amendment to `docs/superpowers/plans/2026-10-22-card-1417-cutover-followups-plan.md` and its first
amendment `2026-10-22-card-1417-confirm-gate-anchoring-plan.md`; both stay byte-identical. Final
Review d6b441d2 of 15d0382d found two more bypasses of the command-line-parsing confirm gate in
`scripts/cutover-postgres-server2.ps1` (D-1: an in-process call or a decoy `-File` pair after a
wrapper's own `-File`, or a `-NoExit` prompt; D-2: Unicode dashes U+2013/U+2014/U+2015 that pwsh
accepts as parameter prefixes, including the `-c` host switch and the `HoldReason:` forms) and D-3:
the W-27 half-1 row could not go red. Master-only. No Unit lane. No production database, no real
AppHost, no real secret, no real maintenance marker: every case drives a PATH fake and a loopback
listener, and the window is never reached past the snapshot refusal.

## D-note: why the argv scan was dropped

Three rounds (A-1, Review c25ee080 D-1, Review d6b441d2 D-1/D-2) each closed one pwsh
command-line quirk and each Review found the next: abbreviations, parameter values, trailing
colons, host options before `-File`, `-Command` text, decoy `-File` pairs seen from a wrapper or
a `-NoExit` prompt, and Unicode dashes the binder and the host both accept. Parsing the process
command line cannot be made complete, because pwsh's own tokenizer, its host option parser and
every wrapper between the operator and the script all have a say in what argv means. The design
now needs no argv at all: the switch is the bound parameter, and the proof of intent is a token
the actor can only have obtained by reading this script's own preview on this machine, for this
repository, today. `Test-ExactNamedSwitch`, `Get-CutoverScriptArguments` and their helpers are
deleted (less code, no pwsh quirks); nothing in the script reads `GetCommandLineArgs` any more.

## Items

- T-1. `-ConfirmToken <8 hex>` is a new string parameter. The default preview (`-WhatIf`) and every
  gate refusal end with one line `CONFIRM TOKEN <8 hex> (machine <name>, <yyyy-MM-dd> UTC, this
  script, -Repository '<path>'): pass -Execute -ConfirmWindow -ConfirmToken <8 hex> with the same
  -Repository today or tomorrow UTC.` The token is the first 8 hex characters of SHA-256 over the
  UTF-8 text `<machine name> LF <UTC date yyyy-MM-dd> LF <this file's SHA-256, lowercase hex> LF
  <resolved -Repository path, trailing separators removed, empty when none>`.
- T-2. `-Execute` proceeds only when the bound `-ConfirmWindow` switch is set AND `-ConfirmToken`
  (trimmed, case-insensitive) equals today's or yesterday's UTC token for the same `-Repository`,
  so a window that crosses midnight still works. Either miss refuses with exit 2: the switch
  message `REFUSED: -Execute requires -ConfirmWindow. Nothing was changed.` or the token message
  `REFUSED: -Execute requires -ConfirmToken equal to the CONFIRM TOKEN the preview prints ...`.
  `-Execute -WhatIf` stays an exit 2 refusal and also prints the token.
- T-3. Side effect of the new name: `-Confirm` and `-Conf` are now ambiguous between
  `-ConfirmWindow` and `-ConfirmToken`, a binder error before the script runs. `-ConfirmW` binds the
  switch and still needs the token.
- Kept unchanged: RECOVERY lines, marker messages and ownership, `-TailnetPort`, secret handling
  (value never on argv or stdout), exit codes (gate refusal 2, window refusal 3).
- Runbook (`docs/apphost-runbook.md`) and the script header describe the token, not a scan.

## Verification design

- V-1 (W-27, replaces the earlier V-1). Harness copy of the formula (`Get-C1407Token`) so the table
  can name other days and paths. Pre-checks: the preview with `-Repository <dir>` and without one
  exits 0 and prints exactly the predicted token. Rows, each calling no child:
  refuse with exit 2 and the token message, printing the token for the given `-Repository`:
  no token; empty token; wrong token (first hex digit flipped); token of two days ago; tomorrow's
  token; another repository path's token (printed token is the given repository's).
  Refuse with exit 2 and the switch message: token without the switch; `-ConfirmWindow:$false`
  with a valid token. Open the window (exit 3 at `REFUSED: verified snapshot reference is
  missing.`, no refusal text): valid token; valid token in upper case; yesterday's token;
  repository-bound token with that `-Repository`.
  Old attack strings (Reviews c25ee080 and d6b441d2), all without a valid token: `-Conf`
  (ambiguous binder error, never opens); `-ConfirmW`; `-ConfirmW -ev /ConfirmWindow`;
  `-ConfirmW -HoldReason: -ConfirmWindow`; `-HoldReason:-ConfirmWindow`; `-ConfirmWindow<space>`;
  `pwsh -WorkingDirectory -ConfirmWindow -File ...`; `pwsh -CustomPipeName -ConfirmWindow -File
  ...`; U+2013 `ConfirmWindow`; `-ConfirmW` U+2014 `HoldReason:` `-ConfirmWindow`; U+2015 `c` as the
  host's `-Command`; `-Command` with the exact token, after `#`, inside a quoted `-HoldReason`;
  a wrapper started `pwsh -File wrapper.ps1 -File <script> -ConfirmWindow` that calls the script
  in-process; `pwsh -NoExit -File <script> -WhatIf -ConfirmWindow` with the in-process `-Execute
  -ConfirmW` typed at the prompt; `-- -ConfirmWindow` (binder error, never opens).
- V-5 (unchanged intent). W-1 through W-26 and W-28 through W-30 stay green with every window run
  passing `-ConfirmToken`; W-12 expects the ambiguity error for `-Confirm`/`-Con`, the token
  refusal for `-ConfirmW`, and the window for the exact switch plus the token.

### Positive controls (pending for Mutation)

- PC-1. In `Test-CutoverChallengeToken` return `$true` unconditionally (the token comparison is
  removed). W-27 rows no-token, empty-token, wrong-token, stale-token, tomorrow-token and
  other-repo-token then open the window with exit 3 (`opened-window`, `exit=3`). Method scope:
  `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1 -Case W-27`.
- PC-2. In `Test-CutoverChallengeToken` drop `$today.AddDays(-1)` from the accepted days. W-27 row
  yesterday-token refuses (`yesterday-token exit=2`, `not-opened`). Same scope.

Operator-only, not performed by tests: a real `-ConfirmWindow -ConfirmToken` against production.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-followups | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-5 | exit 0 | n/a | 4 |
| CP-2 | all | n/a | maintenance-exclusive | `pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1` | exit 2 on a present marker (W-29 dependency) | exit 0 | n/a | 1 |

### Cost

Ordinary floor is 5 minutes.
