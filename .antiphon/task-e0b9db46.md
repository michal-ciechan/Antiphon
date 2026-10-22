# CARD-1417 challenge-token confirm gate (task e0b9db46, repair of Code task 15d0382d)

Verified source SHA `26f12d238669a7d5e6b70c756f09bdfd066426c0` on `feat/card-task-e0b9db46`, runner
mirror `/work/worktrees/task-e0b9db46`, start ref `12ebd3c1991980842224e7f90925d4a94f044072` (the
15d0382d tip). Descendant repair in the lineage a025225c -> 15d0382d -> e0b9db46: this task lands
as its own owner, master-only. This file is a report-only commit after the verified SHA.

Outcome: the argv scan is gone and the gate is a challenge token. Review d6b441d2 D-1 (wrapper
decoy `-File` pair, `-NoExit` prompt, in-process call), D-2 (Unicode dashes, `-c` and `HoldReason:`
forms) and D-3 (half-1 row could not go red) are closed by design: no code reads the process
command line. Checkpoint run `20261022-042515-5147` is GREEN (2 rows, 0 red, exit 0; CP-1 30
passed 0 failed with `PASS W-27`, CP-2 4 passed 0 failed). Plan amendment:
`docs/superpowers/plans/2026-10-22-card-1417-challenge-token-plan.md` (the carried plan and the
anchoring amendment are untouched; master carries neither yet, so editing them would add/add).

## Commits

- `a3603d3d64c4f91bba0fb2001ac07acdeb114b0d` test: W-27 table, W-12, launcher stdin/wrapper/command
  switch, every window run passes `-ConfirmToken` (red)
- `26f12d238669a7d5e6b70c756f09bdfd066426c0` fix: challenge token, scanner deleted, header, runbook,
  plan amendment (verified SHA)

Files: `scripts/cutover-postgres-server2.ps1`, `scripts/test-cutover-postgres-server2.ps1`,
`docs/apphost-runbook.md`, the new plan amendment.

## What changed

- `-ConfirmToken <8 hex>` is a new string parameter. The default preview and every gate refusal
  (`-Execute` without the switch, without a valid token, or with `-WhatIf`) end with one line
  `CONFIRM TOKEN <8 hex> (machine <name>, <yyyy-MM-dd> UTC, this script, -Repository '<path>'): pass
  -Execute -ConfirmWindow -ConfirmToken <8 hex> with the same -Repository today or tomorrow UTC.`
  Formula: first 8 hex of SHA-256 over the UTF-8 text `<machine name> LF <UTC date yyyy-MM-dd> LF
  <script file SHA-256 lowercase hex> LF <GetFullPath(-Repository) with trailing separators removed,
  empty when none>`.
- `-Execute` proceeds only when the bound `-ConfirmWindow` switch is set AND `-ConfirmToken`
  (trimmed, case-insensitive) equals today's or yesterday's UTC token for the same `-Repository`.
  Switch miss: `REFUSED: -Execute requires -ConfirmWindow. Nothing was changed.` (unchanged text).
  Token miss: `REFUSED: -Execute requires -ConfirmToken equal to the CONFIRM TOKEN the preview prints
  for this machine, this script, the same -Repository and today or yesterday UTC. Nothing was
  changed.` Both exit 2 via `Fail-CutoverGate`, which prints the token line after the message.
- Deleted: `Test-ExactNamedSwitch`, `Get-CutoverScriptArguments`, `Test-CutoverValueParameterName`,
  `Test-CutoverHostSwitch`, `Test-CutoverSamePath`, the switch/value parameter tables (98 lines).
  No `GetCommandLineArgs` call remains.
- Side effect of the new name: `-Confirm` and `-Conf` are ambiguous between `-ConfirmWindow` and
  `-ConfirmToken` (binder error, exit 1, before the script runs); `-ConfirmW` binds the switch and
  still needs the token.
- Kept: RECOVERY lines, marker messages and ownership, `-TailnetPort`, secret handling, exit codes
  (gate 2, window refusals 3). Header synopsis and runbook describe the token.
- Interpretation: "any refusal prints the token" is applied to the gate refusals (where the operator
  lacks the token); refusals after the gate (snapshot, drill, phase gates) already had a valid
  token and print nothing new.

## W-27 table (31 checks, harness `Get-C1407Token` reimplements the formula)

Pre-checks: preview with `-Repository <dir>` and without one exits 0, calls no child, prints exactly
the predicted token; the three formula inputs produce distinct tokens.
Refuse exit 2, token message, token line equals the given repository's token, no child, not opened:
no-token, empty-token (`-ConfirmToken ''`), wrong-token (first hex digit flipped), stale-token (two
days ago), tomorrow-token, other-repo-token (`-Repository A` with B's token; A's token printed).
Refuse exit 2, switch message: token-no-switch, colon-false (`-ConfirmWindow:$false` + valid token).
Open (exit 3 at `REFUSED: verified snapshot reference is missing.`, no refusal text): valid,
valid-upper, yesterday-token, valid-repo.
Old attack strings, all refusing because no valid token is given: abbrev-conf (ambiguous, never
opens), abbrev-confirmw, ev-alias (`-ev /ConfirmWindow`, the D-3 string), holdreason-colon
(`-HoldReason: -ConfirmWindow`), colon-dash, trailing-space, wd (`-WorkingDirectory -ConfirmWindow`),
pipe (`-CustomPipeName -ConfirmWindow`), en-dash (U+2013 `ConfirmWindow`), em-dash-colon (U+2014
`HoldReason:`), bar-command (U+2015 `c` as the host's `-Command`), cmd-exact, cmd-comment, cmd-quoted
(`-Command` starts; exit folded to 1, text is the verdict), wrapper (`pwsh -File wrapper.ps1 -File
<script> -ConfirmWindow`, in-process call), noexit (`pwsh -NoExit -File <script> -WhatIf
-ConfirmWindow` with `& <script> -Execute -ConfirmW` fed on stdin), double-dash (binder error).
Harness: `Invoke-C1407Cutover` gained `-CommandSwitch`, `-ScriptPath`, `-StdinLines`; W-27's
`finally` removes `/tmp/CoreFxPipe_-ConfirmWindow` (absent after every run this session). The
harness stays ASCII (dashes built from `[char]`), W-8 green.

## Witnesses

Red at the start ref with the red harness (lease `f3d0623f-100a-45cb-b3c6-6f0cc4f2952d`,
waited=165s, held=36s): `no-token exit=3 || no-token opened-window || ... || em-dash-colon exit=3 ||
em-dash-colon opened-window || wrapper exit=3 || wrapper opened-window || noexit exit=3 || noexit
opened-window` plus every token row `token-line-missing` and the valid rows `exit=1` (unknown
parameter `ConfirmToken`). So three of d6b441d2's bypasses reproduce at the old gate, and the old
gate accepts a literal `-ConfirmWindow` with no token. W-12 red in the same way (lease
`1efc6910-36b9-4b93-92d3-84ae5f89f8e2`, waited=15s, held=5s).
Green at the fix tree before commit `26f12d23` (lease `052704bc-a09c-4a6d-aff8-36f755f27ec8`,
waited=0s, held=158s): whole harness, 30 passed, 0 failed.
Throwaway mutation (the brief's check, PC-1 shape): `Test-CutoverChallengeToken` made to `return
$true` on the committed tree (lease `74c06735-0c6f-409d-b7a0-eb356741bc1b`, waited=0s, held=29s):
W-27 FAIL with `no-token exit=3 || no-token opened-window || empty-token ... || wrong-token ... ||
stale-token ... || tomorrow-token ... || other-repo-token ...` (all opened). Restored with `git
checkout --`, diff empty, before the checkpoint run.

## Checkpoint lines

```
CHECKPOINT CP-2 commit=26f12d238669a7d5e6b70c756f09bdfd066426c0 build=n/a filter=pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=26f12d238669a7d5e6b70c756f09bdfd066426c0 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=26f12d238669a7d5e6b70c756f09bdfd066426c0 build=n/a filter=pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=26f12d238669a7d5e6b70c756f09bdfd066426c0 sourceState=clean buildSource=notApplicable
```

Run `20261022-042515-5147`, manifest `.antiphon/checkpoints/20261022-042515-5147/manifest.resolved.yaml`,
verdict GREEN exit=0, unlisted: none, wall 3m06s, rows 2 green 0 red 0 skipped, builds 0. Script
rows carry no TRX; the row console logs hold `CARD-1407 cutover: 30 passed, 0 failed`, `PASS W-27`,
`CUTOVER TESTS EXIT CODE: 0` and `CARD-1407 maintenance: 4 passed, 0 failed`. `validate --evidence
report.json --expected-source-sha 26f12d23...` printed `CHECKPOINT SOURCE VALID
source=26f12d238669a7d5e6b70c756f09bdfd066426c0 rows=2`. Driver: `dotnet exec
tools/Antiphon.Checkpoints/bin-c1417-tool/Antiphon.Checkpoints.dll run --plan <amendment>
--expected-source-sha 26f12d23... --max-wait 570s`; the single `run` returned 0 with no exit 75.
The tool was built through `scripts/build-slot.ps1` (lease `ba3e6fcc-3008-448c-bfd5-c15b07162a6f`,
held=7s; the granted line's waited= value was not captured by the tail), `OutputPath=bin-c1417-tool/`,
`UseAppHost=false`, `-nodeReuse:false`. Both `bin-c1417-tool` directories (tool and
`src/Antiphon.HostCleanup`) are removed.

## Unlisted runs (all under build-slot.ps1, reasons stated)

- `c1417-red` (W-27) and `c1417-red-w12` (W-12) at the red tree: the red witnesses.
- `c1417-green-full`: whole harness at the fix tree before commit, pre-commit confirmation so the
  checkpoint ran at the committed tip.
- `c1417-pc1-throwaway`: W-27 against the uncommitted mutant, as the brief required; restored.
- Probes outside the repository (scratchpad `two.ps1`/`wrap.ps1`, plain pwsh, no build or test
  driver): `-Confirm` ambiguity, wrapper exit propagation, `-NoExit` with piped stdin, Unicode-dash
  parameter and host-switch acceptance, empty-string argv, `-Command` exit folding.

Repetition: the full CP-1 selection ran twice green (pre-commit, checkpoint); W-27 alone ran once
red, once red on the mutant. Within budget.

## Scope and invariants

Round Final; the brief's profile is minimal: no Unit lane, no TUnit build. V-1 (W-27, rewritten) and
V-5 passed on CP-1; CP-2 covers the `-EnterIfAbsent` exit 2 contract W-29 relies on. No R-n.
Pending for method-scoped SourceLanding Mutation: PC-1 (token comparison forced true; six rows open)
and PC-2 (yesterday's day dropped; yesterday-token refuses), both scoped to
`scripts/test-cutover-postgres-server2.ps1 -Case W-27`. No restart (server/runner/none: none). No
timeout widened, no assertion loosened: the gate only became stricter (a literal `-ConfirmWindow`
alone no longer opens; W-2, W-10, W-12, W-14, W-20 to W-26 still pass on CP-1).

Production safety: no production database, server2 socket, real AppHost, real secret or real
maintenance marker was touched. Every child command is a PATH fake; HTTP is a closed or ephemeral
loopback port; the tailnet probe is a loopback listener. `logs/apphost.down-on-purpose` in this
checkout is absent. The sentinel password stayed off argv and stdout (W-11, W-16 to W-19, W-22 to
W-25 still assert it). `/tmp/CoreFxPipe_-ConfirmWindow` was absent after every run; the pre-existing
`CoreFxPipe_PSHost.*` sockets from 20/21 October are not this task's and were left alone.

Evidence: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 12ebd3c19... -HeadRef HEAD` printed
`commits=2 entries=0 violations=0` exit 0 at `26f12d23` (before this report commit). `git fetch
origin master` left `origin/master` at `4b4138fc5101513df89e7908f52f2537b6940320`; `git merge-tree
--write-tree origin/master HEAD` wrote `9073f11499545e247b39699dadc0d861f6d9a460` with no conflict
at `26f12d23`. The branch was left unmerged. Master-only; no rc line touched. Platform:
`GET /api/runner-defaults` names server2 globally and `GET /api/session-runners` shows server2
(linux) eligible; this task ran on the server2 mirror with no pinned runner.
