# Review fea7e644: CARD-1407 Final Review of Code 9d5db213

Subject: feat/card-task-9d5db213 at bef8e89f824a49557ffccb819cf12706a559646c.
Reviewed delta: `git diff origin/master...bef8e89f` (merge-base 8683447c, origin/master 5447617c).
Verdict: **found**. The credential leak (D-1 of 05c29832) is closed. X-2/X-3/X-4/X-6 are closed. X-1 is only partly closed: several exit paths after maintenance is entered never lift it.

## Receipt

Run `20261021-162725-f807` at bef8e89f, `sourceState=clean`. `scripts/validate-checkpoint-receipt.ps1 -Rows CP-1,CP-2,CP-3` printed `CHECKPOINT SOURCE VALID ... rows=3`, exit 0.

| Row | Command | Result |
|---|---|---|
| CP-1 | `pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1` | exit 0, W-1..W-14 14/14 PASS |
| CP-2 | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` | exit 0, T-1..T-10, T-12 11/11 PASS |
| CP-3 (extra, brief) | `tests/Antiphon.Tests -> bin-rv1407/`, `/*/Antiphon.Tests.Application/KeepSixDocumentationTests/*` | executed=3 passed=3 failed=0, build verified, `bin-rv1407/` deleted |

Other results: `unlisted: none`. `check-evidence-diff.ps1 -BaseRef 8683447c` gave violations=0. `git merge-tree --write-tree origin/master HEAD` was clean (5447617c). The tree is clean.

Scratch probes ran under `scripts/build-slot.ps1` with PATH fakes and a fake API, and were then removed (confined `rm` within the scratchpad):
- three failure paths of the window (below);
- `dotnet user-secrets set` with stdin JSON, in a scratch HOME. An existing `Other:Key` survived beside the new `ConnectionStrings:DefaultConnection`, so stdin mode **merges** keys.

Two leftover directories predate this run (16:00) and are not created by any committed script: `/tmp/c1407-child-0a0ee115...` (holds a `pwsh` fake) and `/tmp/c1407-which-c8378bdc...`. They come from the author's ad-hoc probing. They were left in place.

## Verified closed

- **Credentials.** The connection string is read from the file and sent as JSON on `dotnet user-secrets set` stdin, with no value argument. Merge was verified empirically.
  - `remove` carries no value.
  - The age identity goes only on ssh stdin to `age -d -i -`. Its path is not in argv.
  - Recipient path, compose dir, SHA and hold reason are the only argv values, and none of them is secret.
  - psql, ssh and dotnet output goes to `Out-Null` or into variables and is never printed.
  - No child env is set.
  - `keep-six.ps1` sends the task token as a header only.
  - The plan, tests and committed task report contain no secret.
  - W-11 is meaningful. It inspects every fake's argv (docker, dotnet, ssh, scp, age, pwsh), every non-dotnet stdin and stdout for both sentinels, and requires the sentinel on dotnet stdin and the identity body on ssh stdin. The author reported it red at bbf970e8; I did not rerun the red (PC stays pending).
- **keep-six.** `-ClearHold` is a new mandatory switch in its own parameter set. Status stays the default. Hold and Ack sets, `Reason`/`Until` and the `X-Antiphon-Task-Token` header are unchanged. KeepSixDocumentationTests passed 3/3.
- **Accidental real run.** WhatIf is the default. Every one of these refuses before any external command: `-Execute` alone, `-Confirm`, `-Confirm:$true`, `-Con` (ambiguous binding), `-ConfirmWindow:$false`, and `-Execute -WhatIf`.
  - The exact token must appear in process argv. A same-process `& script -Execute -ConfirmWindow` therefore refuses, which fails closed.
  - Phase 0 records and every operator input are required for the window. Rollback needs no records; that is by design (X-5).
- **Destructive actions.** There is none. Only `docker compose stop/start postgres`, `docker volume ls`, `rm -f` of the remote `.age` and Remove-Item of temp SQL and the never-created plaintext path. There is no `volume rm`, prune, `down` or `-AllowWorktree`.
- **Ordinary restart.** `restart-apphost.ps1` is byte-identical to 4b44cdf9. The `-StopOnly` exit sits inside the try, so the finally releases the restart lock. CP-2 passed 11/11.
- **Stop gate 6.** It refuses on a count-query failure (null map) and when any server2 table count is above the desktop count, unless `-AcceptDataLossRollback` is passed. It refuses before maintenance, the secret or a restart.

## Defects

### R-1 (found): maintenance mode is not lifted on many exit paths after it was entered
- **Where:** `scripts/cutover-postgres-server2.ps1` `Invoke-CutoverWindow` lines 518-608 and `Invoke-CutoverRollback` lines 470-474. Maintenance is entered at line 518 (and line 470), and no try/finally spans the rest of either function.
- **Failure:** probed with fakes (window args as in W-11). In each case the trace shows `set-apphost-maintenance.ps1` (enter), then `keep-six.ps1 -Hold`, and no `-Clear` and no `-ClearHold`:
  - **P2.** Live landPending GET returns 500. `Invoke-CutoverGet` calls `Fail-Cutover`, exit 3. This happens before StopOnly. AppHost is still running, but the watchdog stays disabled and the dispatch hold stays set.
  - **P3.** pg_dump fails after StopOnly. Exit 3 with "The secret was not changed". AppHost is down, the watchdog is disabled, and there is no restart.
  - **P1.** Phase 3 count mismatch after StopOnly. Exit 3, same state.
  - By code reading, the same holds for the scp/restore failure, the desktop stop failure, the volume and inspect gates, a `user-secrets set` failure, a restart other than 0 after the secret change, the sessions-gate restart failure (line 564 lifts only when exit is 0), a rollback restart failure, and any terminating error.
- **Why:** brief check (4) requires the lift on every exit path of a real run after enter, using try/finally. With parking and maintenance there is no automatic release. A refused pre-secret window leaves production down with the watchdog off and no deadline, while the printed text implies the old string is safe to resume.
- **Fix:**
  - Wrap everything from the enter to the end of each function in try/finally that runs `-Clear` (and `-ClearHold` in the window).
  - For a failure before the secret changed, first restart on the old string with `-ExpectedServerSha`.
  - If stop gate 4/5 (secret already changed) is meant to keep maintenance for phase 6, make that the single documented exception and print it.
  - Add W cases for a pre-StopOnly GET failure, a post-StopOnly dump failure and a phase 3 mismatch, each asserting the `-Clear` call.

### R-2 (found): a maintenance mode entered by someone else is lifted by the cutover
- **Where:** `Invoke-CutoverMaintenance` (line 168) and its callers. `scripts/set-apphost-maintenance.ps1` exits 0 with "Marker already present".
- **Failure:** an operator or another script holds `logs/apphost.down-on-purpose`. The cutover's enter succeeds silently. The success path (line 609), the rollback (line 474) and the pre-StopOnly refusals (lines 522, 528, 536, 548, 555) then `-Clear` it, which re-enables the watchdog under the other owner.
- **Why:** brief check (4) says "never lifted if it was entered by someone else before".
- **Fix:** test for the marker before entering. Refuse, or record "pre-existing" and skip every clear. Add a W case.

## Advisories (no verdict weight)

- **A-1.** Stop gate 6 compares counts only. Retention deletes plus inserts on server2 with equal or lower counts pass without `-AcceptDataLossRollback`. A target with zero public tables reads as "no loss". Consider max-PK or `AgentTaskEvents` max(id) per key table, and refusing an empty target table list.
- **A-2.** A live landPending 200 response with a non-JSON body counts as 0 (`$pending.items` is null), which fails open. Consider requiring `items` to exist.
- **A-3.** The dispatch hold is never cleared on success or on post-StopOnly failures. If that is intentional, say so in the phase 5 OPERATOR text.
- **A-4.** The runbook does not state that the window must be started as `pwsh -File ... -Execute -ConfirmWindow` (a same-process call refuses).
- **A-5.** The author's leftover `/tmp/c1407-*` directories (above).

Pending (operator-only, not discharged): a real `-ConfirmWindow` window, a real keep-six POST, a dispatch, a land, Hangfire, and the phase 6 choice. No PCs are named.
