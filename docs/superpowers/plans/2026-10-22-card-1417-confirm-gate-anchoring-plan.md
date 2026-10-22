# CARD-1417 confirm gate anchoring (repair of Code task a025225c)

Amendment to `docs/superpowers/plans/2026-10-22-card-1417-cutover-followups-plan.md`, which stays
byte-identical to master's copy. Final Review c25ee080 of a025225c found D-1, present at base as
well: `Test-ExactNamedSwitch` in `scripts/cutover-postgres-server2.ps1` reads the whole process
command line, so an abbreviated script switch (`-Conf`) plus a stray literal `-ConfirmWindow`
anywhere else opens the window. Master-only. No Unit lane. No production database, no real
AppHost, no real secret, no real maintenance marker: every case drives a PATH fake and a loopback
listener, and the window is never reached past the snapshot refusal.

## Items

- D-1. The exact-token scan is anchored to the tokens after pwsh's own `-File <path>` pair whose
  resolved path is `$PSCommandPath` (any prefix pwsh accepts for that switch: `-f`, `--File`,
  `/File`). A `-ConfirmWindow` before that pair (the value of `-WorkingDirectory` or
  `-CustomPipeName`) or inside `-Command` text (after `#`, or inside a quoted `-HoldReason` value
  that straddles argv tokens) never counts. When there is no such pair (`-Command`,
  `-EncodedCommand` and their prefixes, an implicit script path, an in-process call) `-Execute`
  refuses with exit 2 and a message that says so.
- D-1b (found while probing). pwsh `-File` hands the next token to a trailing-colon parameter as
  its value (`-HoldReason: -ConfirmWindow` binds HoldReason and no switch), and the scan counted
  that token. A token ending in `:` now consumes the following token as its value.
- A-1 halves pinned separately. Half 1 (only a dash token counts) is pinned by
  `-ErrorVariable /ConfirmWindow`: a common parameter the script's own value list does not know,
  so only the dash rule keeps the value out. Half 2 (the value of a value-taking parameter is
  skipped) is pinned by `-HoldReason: -ConfirmWindow`. The name-resolved half of the skip
  (`-HoldReason <value>` by exact or unique-prefix name) stays as defence in depth: under pwsh 7
  a dash token after such a name is a binder error before the script runs.
- Runbook. `docs/apphost-runbook.md` no longer says "a parameter value never opens the window";
  it says exactly what is checked.

## Verification design

- V-1 (extends the original V-1, W-27). Refusals, each exit 2 with
  `REFUSED: -Execute requires -ConfirmWindow.`, never reaching the snapshot check, calling no
  child: `-Execute -Confirm -HoldReason /ConfirmWindow`; `-Execute -Confirm -HoldR /ConfirmWindow`;
  `-Execute -Confirm -ErrorVariable /ConfirmWindow` (half 1 alone);
  `-Execute -Confirm -HoldReason: -ConfirmWindow` (half 2 alone);
  `-Execute -Confirm -HoldReason:-ConfirmWindow`; `-Execute -Confirm -ConfirmWindow:$false`;
  `pwsh -WorkingDirectory -ConfirmWindow -File <script> -Execute -Conf`;
  `pwsh -CustomPipeName -ConfirmWindow -File <script> -Execute -Conf`;
  `pwsh -Command & '<script>' -Execute -Conf # -ConfirmWindow` and
  `pwsh -Command & '<script>' -Execute -Conf -HoldReason 'window -ConfirmWindow now'` (argv
  tokens `'window`, `-ConfirmWindow`, `now'`), both naming the missing `-File` pair;
  `pwsh -Command & '<script>' -Execute -ConfirmWindow` also refuses (no pair, exact token or not).
  Never opens, exit non-zero, no child: `-Execute -ConfirmWindow<space>` and
  `-Execute -Conf -- -ConfirmWindow` (binder errors before the script runs).
  Opens (reaches the snapshot refusal, exit 3): `-Execute -HoldReason:/ConfirmWindow -ConfirmWindow`
  and `pwsh -f <script> -Execute -ConfirmWindow`.
- V-5 (unchanged). W-1 through W-26 and W-28 through W-30 stay green: the exact `-ConfirmWindow`
  token under `pwsh -File` still opens the window (W-12), the connection secret stays off argv and
  stdout, and the foreign-marker cases still refuse without clearing.

No R-n and no PC-n are named. Mutation has nothing to discharge here.

Operator-only, not performed by tests: a real `-ConfirmWindow` against production.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | cutover-followups | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | V-1, V-5 | exit 0 | n/a | 4 |
| CP-2 | all | n/a | maintenance-exclusive | `pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1` | exit 2 on a present marker (W-29 dependency) | exit 0 | n/a | 1 |

### Cost

Ordinary floor is 5 minutes.
