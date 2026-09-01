# CARD-0310 — AppHost Watchdog must not kill a launch that is still inside its own budget

**Date:** 2026-09-01 (Plan pass, task ae4987bd — design only; no production code changed)
**Card:** CARD-0310 "AppHost Watchdog can livelock a slow-but-legitimate launch: its restart cooldown ignores restart-apphost.ps1's own locks"
**Diagnosis:** done on the card; this pass re-read `logs/watchdog-apphost.log` (2026-09-01 16:06–16:36 local), `scripts/watchdog-apphost.ps1`, `scripts/restart-apphost.ps1`, `scripts/apphost-common.ps1` (`Test-AppHostLockActive`), `dev-aspire.ps1` launch-lock lifetime, CARD-0075/0011 plans, bootstrap gotchas 12 and 14.

**Not CARD-0075.** Two `restart-apphost.ps1` invocations still refuse correctly (exit 3). This is the watchdog's independent fire, which does not go through that refusal when the lock file is already gone.

---

## Decision

Pick **direction 1 as the load-bearing fix**, with a small piece of direction 2 so the two clocks are one number. Raising cooldown alone cannot stop the first kill: cooldown only tracks **watchdog-stamped** restarts, and the 16:11 fire was of a **manual** restart whose lock had already been deleted.

Three facts from the live log, not from theory:

1. **The watchdog already checks both locks** (`Test-LaunchInFlight` at fire start and again immediately before invoke, CARD-0075). At 16:08:25 it correctly skipped: `restart lock held by PID 51624 (stamp 2026-09-01T15:06:40Z, 1.8 min old)`.
2. **`restart-apphost.ps1` then released that lock on TimeoutSec exit 1** (`finally` → `Remove-AppHostLock` deletes the file) while the detached `dev-aspire.ps1` / AppHost were still coming up. Documented on the script itself (`:189-192`) and gotcha 12: a non-zero exit does not mean it stopped launching.
3. **The 16:11:19 fire was on a coming-up stack, not a dead one.** All three probe rounds: `health=FAIL` HttpClient 5 s timeout, **`client=200`**. Eleven seconds later `restart-apphost.ps1 exited 1` (not 4). That kill is what started the livelock. 16:23:13 fired again once the 10-minute **watchdog** cooldown elapsed, while a later attempt was again `client=200` / health timing out.

So "honor the same lock-freshness check" is not "add a check the watchdog lacks". It is: **a lock stamp inside `LockMaxAgeMinutes` must keep meaning in-flight after the writer process has exited and after TimeoutSec**, and **a probe that sees the client up is not a dead stack**.

Direction 2 (raise cooldown) is the belt: `CooldownMinutes` becomes the same 15 as `LockMaxAgeMinutes`. It still does not protect a manual restart. Do not make it the primary fix.

---

## Ground truth (checked, not guessed)

### Live timeline (`logs/watchdog-apphost.log`, local times, BST = UTC+1)

| Time | What |
|---|---|
| 16:06:40 | Manual `restart-apphost.ps1` took `apphost.restart.lock` (stamp `15:06:40Z`) |
| 16:08:25 | Watchdog skip: restart lock held, 1.8 min old. CARD-0075 working. |
| ~16:09:10 | Manual TimeoutSec 150 s expires; `finally` **deletes** the lock. Child may still be launching. |
| 16:10:25 | Watchdog fire: **no** in-flight skip |
| 16:10:36–16:11:17 | Three failed rounds: health 5 s timeout, **client=200** |
| 16:11:19 | `restarting AppHost via restart-apphost.ps1` — kills the 16:06 launch |
| 16:11:30 | `exited 1; stamped restart` (11 s — BuildFailed or equivalent, **not** TimeoutSec, **not** exit 4) |
| 16:12–16:15 | Both ports connection-refused; cooldown skip (1.8 then 3.9 min) |
| 16:16–16:18 | Skip: a new restart lock (PID 6712, stamp `15:16:04Z`) |
| 16:20:30–16:21:12 | health timeout, client=200 again; cooldown 9.7 min, skip |
| 16:23:13 | cooldown elapsed; **fires again**, same coming-up signature |
| 16:23:13–16:26:00 | `exited 1` after ~167 s (TimeoutSec path). Log line is still only `exited 1` |
| 16:36:26 | OK, after `set-apphost-maintenance.ps1` let one attempt finish |

CPU 92–93% that window is why launches were slow. The watchdog does not consult load, TimeoutSec, or lock stamps once the file is gone.

### Why the lock check did not save 16:11

`Test-AppHostLockActive` (`apphost-common.ps1:266-297`) returns null when:

- the file is absent, or
- **the recorded PID is dead** (`:284-286` "holder is gone; the file is litter"), or
- age ≥ `LockMaxAgeMinutes` (15).

`restart-apphost.ps1` `finally` always `Remove-AppHostLock` (`:196-201`), including the TimeoutSec exit 1 that prints "the spawned dev-aspire.ps1 may STILL be launching". After that, there is no file for the watchdog to honor. `dev-aspire.ps1` also deletes `apphost.launch.lock` in its `finally` when the **script** exits (dashboard URL found, AppHost backgrounded) — dashboard/client up is not `/health` 200. At 16:10 client was already 200.

Watchdog cooldown (`restartsUtc` in `logs/apphost-watchdog.state`) is only written after **this** script invokes restart-apphost. A manual restart is invisible to it.

### Exit 4 vs this incident

`restart-apphost.ps1` **does** have the CARD-0075 exit-4 / `Show-DcpTimeoutVerdict` path (`:167-172`). The watchdog invokes it as `& $psExe -File $RestartScript` and then logs **only** `$LASTEXITCODE` (`watchdog-apphost.ps1:258-278`). Stdout/stderr never reach `logs/watchdog-apphost.log`. Task Scheduler discards the rest.

This fire was **exit 1**, twice: 11 s (likely `BuildFailed` after killing the previous tree) and ~167 s (TimeoutSec). The richer exit-4 text would not have appeared even if it had been a DCP timeout. S3 is to capture that output into the watchdog log and name the exit code; it is not this incident's cause.

---

## Slices

### S1 — A fresh lock stamp stays in-flight after the writer exits (direction 1)

**Files:** `scripts/apphost-common.ps1` (`Test-AppHostLockActive`), `scripts/restart-apphost.ps1` (`finally`), `scripts/test-apphost-lock-age.ps1`.

**`Test-AppHostLockActive`:** drop the "dead PID ⇒ litter" short-circuit **when age < MaxAgeMinutes**. Dead+fresh is "the writer exited; the child it spawned may still be launching" (gotcha 12). Dead+stale (age ≥ MaxAge) stays litter. Live+fresh stays in-flight. Reason string must say the holder exited, e.g. `restart lock stamp 15:06:40Z is 4.0 min old; holder PID 51624 exited; child may still be launching`.

This is the one-number rule CARD-0075 already wanted (`LockMaxAgeMinutes` 15). It also makes `New-AppHostLock` refuse (exit 3) instead of deleting a fresh leftover and starting a second teardown — which is exactly the CARD-0075 collision, now covering the watchdog too.

**`restart-apphost.ps1` `finally`:** do **not** delete the lock file on the "child may still be launching" exits:

| Exit | Lock file |
|---|---|
| 0 (healthy) | Remove (operator may restart immediately) |
| 1 TimeoutSec / dashboard-up-health-not-confirmed (`:185-193`) | **Keep** (stamp remains; PID will be dead) |
| 4 DCP timeout | **Keep** |
| 1 `BuildFailed` | Remove, and **Stop-Process** the `Start-Process` child if it is still alive (today the child is abandoned) |
| 3 refused | never acquired |

Implement as: close/dispose the lock stream so the file is not held exclusive, then skip `Remove-Item` on keep-paths. Add `Remove-AppHostLock -KeepFile` (or a sibling) in `apphost-common.ps1` rather than inlining Dispose in the restart script.

Do **not** hold `launch.lock` for 15 minutes after a **successful** `dev-aspire.ps1` (that would block a deliberate restart for 15 min after every healthy start). Successful dashboard-ready still deletes launch.lock. S2 covers "client is up, health is slow" after that deletion.

**Tests** (`test-apphost-lock-age.ps1`, same ASCII/temp-dir style; never touch `logs/apphost.*.lock`):

- T8: 5-min-old stamp, PID that is not alive → `Test-AppHostLockActive` is **non-null** (today it is null).
- T9: 20-min-old stamp, dead PID → null (still litter).
- T10: `New-AppHostLock` against a 5-min-old dead-PID leftover → `Acquired = $false` (refuse, do not steal).
- T3 unchanged: live PID 5 min old still active.

### S2 — Client-up + health-timeout is not a dead stack

**File:** `scripts/watchdog-apphost.ps1` (`Test-Round` / the failed-round loop).

A round is a **restart-worthy failure** only when the stack looks down: connection-refused / no listener on the health URL, or both endpoints failed that way. **HttpClient timeout on `/health` while `client=200` is not a failed round toward restart.** Log `WARN health slow, client up - not counting as down`. If all three rounds are that shape, exit 0 without touching cooldown or restart-apphost.

This is the 16:11 signature. Under 92% CPU a 5 s probe timeout is a loaded server, not a corpse. A truly dead AppHost at 16:12 showed **both** ports connection-refused — that still restarts.

Do not raise `ProbeTimeoutSec` as the fix (masks load; does not restore the lock). Do not add a CPU-load sensor.

Pin with `-ProbeOnly` against a stub: if that is too coupled to live 17202, extract `Test-Round` classification into `apphost-common.ps1` as a pure function of `{ HealthOk, HealthError, ClientOk, ClientError }` and unit-test it next to the lock tests.

### S3 — One clock, and the watchdog log must carry restart-apphost's verdict

**Files:** `watchdog-apphost.ps1`, `apphost-common.ps1` (shared default).

- Default `CooldownMinutes = 15`, same as `LockMaxAgeMinutes`. One constant (e.g. `$AppHostLockMaxAgeMinutes = 15` in `apphost-common.ps1`) used by watchdog cooldown, both lock checks, and `restart-apphost.ps1`. Direction 2, subordinated: it only covers **watchdog-stamped** fires; S1 covers manual ones.
- Capture restart-apphost stdout+stderr (or at least the last ~40 lines) into `Write-Log`. Name the code: `0=healthy`, `1=timeout/build`, `3=refused (already unstamped)`, `4=DCP dependency timeout`. Exit 4's `Show-DcpTimeoutVerdict` text must appear in `logs/watchdog-apphost.log` so the next incident is not `exited 1` with no body.
- Still do not stamp `restartsUtc` on exit 3. **Do** stamp on 1 and 4 (cooldown is what stops an immediate retry after a real teardown).

### S4 — Docs

`docs/bootstrap.md` watchdog bullet (`:436`) and gotcha 12 (`:468`):

- Watchdog skips while a lock **stamp** is younger than 15 min, even if the holding PID has exited.
- `restart-apphost.ps1` TimeoutSec/exit 4 **leaves** `apphost.restart.lock` for that window; deleting it is how you force a retry.
- A probe of `client=200` + `/health` timeout does not bounce the stack.
- Cooldown is 15 min and is **not** a substitute for the lock (it never saw the 16:06 manual restart).
- Watchdog log now includes restart-apphost's exit-4/1 text; `exited N` alone is no longer the record.

ASCII-only on the three `.ps1` files.

---

## What this card does not do

- Raise `TimeoutSec` / DCP `DependencyCheckTimeout` / `ProbeTimeoutSec` as the livelock fix.
- A load/CPU sensor.
- Disconnect or kill the session-runner on 17204.
- Change exit 3 refusal between two `restart-apphost.ps1` humans (CARD-0075 stays).
- Hangfire / a new scheduled task.
- Holding `launch.lock` for 15 min after a **successful** `dev-aspire.ps1` (would block intentional restarts).

---

## Test matrix

| Layer | Test |
|---|---|
| `test-apphost-lock-age.ps1` | T8 dead+5 min active; T9 dead+20 min stale; T10 New-AppHostLock refuses dead+fresh leftover; T3 live+5 min still active |
| `apphost-common` probe classifier (if extracted) | health timeout + client 200 → not down; both connection-refused → down; health 200 + client 200 → up |
| `watchdog-apphost.ps1 -ProbeOnly` | live healthy stack: no restart (existing CARD-0011 check). Optional: temp lock file in a copied fixture is out of scope if it would touch production `logs/` — the lock unit tests are the pin |
| Live acceptance (commit message, CARD-0075 style) | 1. Start `restart-apphost.ps1`, wait until it prints the TimeoutSec "child may still be launching" line. Watchdog fire during the next 15 min must log skip naming the leftover stamp, **not** `restarting AppHost`. 2. `client=200` + forced health timeout must not invoke restart. 3. Two `restart-apphost.ps1` still exit 3. 4. After 15 min the leftover lock is litter and a watchdog fire may restart. Session-runner PID on 17204 unchanged throughout |

Run: `pwsh -NoProfile -File scripts/test-apphost-lock-age.ps1` (and the new classifier test if split out). No `Antiphon.Tests` / Pty / client unless a C# file is touched (none should be).

---

## Sequencing and risks

**Order: S1 → S2 → S3 → S4.** S1 without S2 still allows a 16:11-shaped fire once a **successful** `dev-aspire` has deleted `launch.lock` while `/health` is slow. S2 without S1 still allows a fire after TimeoutSec if both probes look down (connection-refused during the kill/respawn gap is real — 16:12). Both required. S3 is diagnostics + the one-number cooldown. One PR.

| Risk | Standing |
|---|---|
| Leftover lock blocks a deliberate restart for 15 min | Same as today's "do not re-run blind". Delete the lock file (already in the exit-3 text). Exit 0 still removes it. |
| Dead+fresh treats a crashed restart as in-flight | 15 min of skip is the budget. After that, litter, watchdog may fire. Better than livelock. |
| Server truly wedged, client still serving | S2 will not bounce it. Operator runs `restart-apphost.ps1`. Watchdog's job is "stack down", not "server hung". |
| `New-AppHostLock` steal path vs leftover keep-file | T10 pins refuse. After MaxAge, existing dead-holder clear + CreateNew still works. |
| Cooldown 15 vs lock 15 double-counts | Same number on purpose. Cooldown only applies after a **watchdog** teardown. |

---

## Execution notes

After deploy, do not disable the watchdog as the workaround (`set-apphost-maintenance.ps1` remains the "leave it down on purpose" tool). A slow launch under load should log `skip: launch in flight` or `health slow, client up` until `/health` is 200 or 15 minutes have passed. The 16:06–16:23 sequence must be impossible without deleting the lock by hand.
