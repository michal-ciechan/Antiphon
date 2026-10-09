# CARD-1105 redeploy-old: jq Argument list too long

Debug only. Nothing on server2 was stopped, removed, restarted, mounted, or resumed. Observation time for the live status read is 2026-10-09T07:46Z.

## Outcome

`redeploy-old` at SHA `51f175dbf738519e8e50842697499c6c6c12b2c1` died at `c590-remote.sh:5910` because `jq --arg audit` was given the full git-audit receipt. That receipt is 178142 bytes. Linux `MAX_ARG_STRLEN` on this host is 131072 (`getconf PAGE_SIZE` = 4096, so `PAGE_SIZE * 32`). One argv string may be at most 131071 bytes plus its terminating NUL. Bash reports the failed `execve` as `Argument list too long` and status 126 (`DIAGNOSIS=UnhandledExit 126`). `ARG_MAX` is 2097152 and was not the limit that fired.

The audit helper had already finished and been removed. The recycle journal was already saved. No owned container was stopped and no volume was removed. A fresh `redeploy-old` of this SHA, after the fix below, does not need `-ResumeRecycle`. Do not resume operation `c10087844d55007a24e079503d713f1ff6940`.

## 1. Root cause

Failed run `c72755108b4cf1a3` (host jq receipt `observedAtUtc` 2026-10-09T07:23:08Z). Evidence: `C:\src\Antiphon\.antiphon\rolling-server2\c72755108b4cf1a3`. Orchestrator log: `C:\Users\lndco\AppData\Local\Temp\claude\C--src-Antiphon\c83e3c62-c0ec-4a12-b40d-8f083dbd8885\scratchpad\rollout4.log` lines 16-23. Result file `deploy-parent\c590-result.json`: `{"accepted":false,"diagnosis":"UnhandledExit 126","exit":126}`.

### The line, and the deployed copy

`scripts/c590-remote.sh:5910` in this worktree, in commit `51f175db`, and in `/home/mc/antiphon-c590/c590-remote.sh` are the same file.

| Copy | Bytes | sha256 |
|---|---:|---|
| Worktree `scripts/c590-remote.sh` | 383133 | `8a414703ea4c8e383e9ce3270e278c6af9d041508c6fe7b117fc77184e5d53dc` |
| `51f175db:scripts/c590-remote.sh` | 383133 | same |
| `/home/mc/antiphon-c590/c590-remote.sh` | 383133 | same |

`deploy-server2` copies that file with `scp` in `scripts/c590-real.ps1:472` before `bash /home/mc/antiphon-c590/c590-remote.sh`. Host jq is `/usr/local/bin/jq` 1.7.1 (`host-jq-redeploy-old.json` in the failed run).

The deployed lines 5909-5911:

```bash
if [ "${C1008_RESUME:-0}" = 0 ]; then
    C1008_RECORD="$(printf '%s' "$C1008_RECORD" | jq -c --arg audit "$audit" '.audit=$audit')"
    c1008_save || c1008_refuse RecycleReceiptUnavailable
fi
```

`$audit` is the success stdout of `c1008_audit_checked` (`c590-remote.sh:5905`), which is `c1008_audit` (`:5529-5567`) sorted. On success the embedded helper prints only:

- `entry repo=<64 hex> common=<64 hex>` (148 bytes) at `:5375`
- `tip=<40 hex> common=<64 hex>` (117 bytes) at `:5262`
- `repositories=<n> partial=<n>` (29 bytes for `390`) at `:5419`

`digest` is sha256 (`:4507`). There is one tip line per unique tip after `sort -u` (`:5198`, `:5261-5263`).

### How large this argument is

This run did not keep the audit stdout. `c1008_audit` holds it in a shell variable and `c1008_save` runs only after line 5910. The clean replay immediately before the failure did keep it: `/tmp/c1105-6f1e3bff/audit.out`, mtime 2026-10-09T07:14:34Z, sha256 `b9b4c8d65b78aea555548ce29c89c90303bbbeca089720fc67ba29d760c49447`, 178700 bytes. That is the replay named in `docs/investigations/2026-10-09-card-1105-task-0eafbbee-rescue-discard.md` (390 repositories, 0 refusals, final line `repositories=390 partial=390`). Eight lines in that file are replay wrapper text (`=== AUDIT MOUNT ===`, mountinfo, `WORK_OPTS`, `WRITE_PROBE_FAILED`, two `REPLAY-COMMON-PASS` lines). They are not printed by `c1008_git_program`.

The production-shaped lines are 1420 lines and **178142 bytes**:

| Line | Count | Bytes each | Bytes |
|---|---:|---:|---:|
| `entry` | 390 | 148 | 57720 |
| `tip=` | 1029 | 117 | 120393 |
| `repositories=390 partial=390` | 1 | 29 | 29 |
| Total | 1420 | | **178142** |

178142 is 47071 bytes over the 131071-byte payload limit. The NUL makes the argv element 178143 against `MAX_ARG_STRLEN` 131072. The rest of that `jq` argv (`jq`, `-c`, `--arg`, `audit`, the filter) is a few dozen bytes. 390 entry lines alone are 57720 bytes and would fit. The 1029 tip lines are what crosses the limit. Reflogs were already expired (0 left; cleanup note STEP 3), so this is current ref tips, pseudorefs, and peeled objects, not reflog history.

The stock script sorts that text (`:5566`). Sort does not change this byte count. The volume's `CreatedAt` is unchanged since the replay (section 2), and server2 had 0 sessions, so the redeploy audit was the same receipt.

The helper program itself is passed to `docker create -c` and is 52541 bytes (`c1008_git_program`, `:4399-5437`). That fits, which is why the audit ran and only the output overflowed. `set -euo pipefail` is on (`:11`). Line 5910 has no `||`, so the `execve` failure skips `c1008_refuse` and the EXIT trap at `:6715` writes `UnhandledExit 126`.

### Why -DryRun did not hit it

Dry run returns at `c590-remote.sh:5823-5826`, before `c1008_lock`, before the journal, before `c1008_audit_checked`, and before line 5910. It prints the volume preview with `auditPending=true` and `write_result true`.

Dry-run evidence `c727adab8ba8cf34`: `deploy-parent.manifest.json` has `dryRun: true`, `deploy-parent/c590-result.json` is `accepted: true`, and there is no `recycle.json`. Host jq for that run is `observedAtUtc` 2026-10-09T07:22:18Z. `rollout4.log` lines 11-15 record rc 0. The same early return is what `check-census` and `check-host-jq` never reach; they do not call `c1008_recycle`.

### Other jq calls that pass a data-sized argument

Same class: a value that grows with the fleet or the audit, passed as one `--arg` / `--argjson`.

| Lines | Argument | This run | Could hit 131072 |
|---|---|---|---|
| **5910** | `--arg audit "$audit"` | 178142-byte success receipt | This failure |
| 5591 | `--arg audit "$detail"` | Not reached. One `audit check=` line from `:5589` | Only if a single refusal line exceeds the limit. Observed replay lines are at most 162 bytes |
| 5888-5889 | `--argjson` `volumes`, `preserved`, `owned`, `live` (`C1008_STATUS`), `tasks` (`C1008_TASKS`) | Succeeded. Their result is the 12181-byte journal | Not at today's sizes. Each value is a slice of that journal |
| 5484 | `--argjson model`, `--argjson volumes` | Succeeded (`previous-model.json` is 7216 bytes) | Compose model, not the audit |
| 5656 | `--argjson volumes`, `--argjson owned` | Not reached (after removal) | Same size class as 5888, not the audit |
| **4373, 5675, 5677** | `--argjson saved "$C1008_RECORD"` | Not reached. Today's record is 12181 bytes | **Yes, on the next resume after a successful save**, once `.audit` holds the 178142-byte receipt. 4373 is the null-`runnerSessions` resume branch in `c1008_status_proof`. 5675 and 5677 are `c1008_reconcile_owned`, called from `c1008_bind_generation` on resume (`:5780-5781`) |
| 4187-4188 | `--argjson` reads, tasks, snapshot | Succeeded. Census files are 12577 and 13596 bytes | Only if one census blob grows past the limit |
| 6453-6454 | `--argjson owned`, `temp`, `main`, `tasks` on `C994_RECORD` | Not this phase | `retire-temp-containers` receipt. Same argv pattern. It does not receive the git audit |
| 5634 | `--arg raw "$raw"` | `df` text | No |
| per-row `--argjson row` / `facts` / `envelope` (for example `:4242`, `:5455`, `:5863`) | One task row or one volume inspect | Volume rows are inside the 12181-byte journal | A single row, not the audit |

Shell compares of `$audit` (`:5907`, `:5940`) do not `exec` and are not subject to `MAX_ARG_STRLEN`.

## 2. State left behind

`c1008_save` at `:5903` ran. `c1008_audit_checked` then ran (the failure is the next statement). `docker stop` is the loop at `:5913`, which did not start.

Journal, both copies, sha256 `d771ce3115ddd3d30da8dee3879c94c461f486153e44781466bf1c991b7df0c7`, 12181 bytes, mode `0600`, mtime 2026-10-09T07:24Z:

- Host: `/home/mc/antiphon-server2/recycle/c10087844d55007a24e079503d713f1ff6940.json`
- Desktop: `C:\src\Antiphon\.antiphon\rolling-server2\c72755108b4cf1a3\deploy-parent\recycle.json`

| Field | Value |
|---|---|
| operation | `c10087844d55007a24e079503d713f1ff6940` |
| project | `antiphon-runner` |
| phase | `preflight` |
| outcome | `refused` (the initial value from `:5807`, not a `c1008_refuse` stamp) |
| diagnosis | absent |
| audit | absent |
| ownedRemoved | false |
| stopReceipts / removeReceipts | empty |
| volume outcomes | `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data` all `pending` |

`CreatedAt` on the host matches the journal, so those volumes were not recreated:

| Volume | CreatedAt |
|---|---|
| `antiphon-runner_work` | 2026-09-22T19:10:50Z |
| `antiphon-runner_runner-tmp` | 2026-09-30T08:08:35Z |
| `antiphon-runner_dind-data` | 2026-09-22T19:10:50Z |
| `antiphon-runner_runner-state` | 2026-09-22T19:10:50Z |
| `antiphon-runner-cache-nuget-packages` | 2026-10-02T08:38:26Z |
| `antiphon-runner-cache-nuget-scratch` | 2026-10-02T08:38:32Z |
| `antiphon-runner-cache-npm-content` | 2026-10-02T08:38:39Z |

Preserved names were not removal targets. Temp private volumes are still present (`antiphon-runner-temp_work`, `_runner-tmp`, `_dind-data`, `_runner-state`). No volume was mounted for this diagnosis.

Containers, `docker ps -a` / `docker inspect`, still the pre-failure ids:

| Id | Service | State | Since |
|---|---|---|---|
| `5d9e09695ab3f777b56fd87ee9c66adcd476045d51d634a03e2d79ce94d8cdb3` | session-runner | running, healthy, Up 6 days | started 2026-10-02T22:52:31Z |
| `574beddc57947f46adb89fa84f2390a570b2bd967653c5686e9777ef5ca5e132` | state-init | exited 0 | finished 2026-10-02T22:52:29Z |
| `4cabb8afc676` | build-slots | running, healthy, Up 9 days | not an owned recycle target (`:5876` drops `build-slots`) |

Temp runner `927bb1ad3d60` is up 3 days (healthy). Its state-init `19026e1037d9` exited 0 three days ago. `docker ps -aq --filter status=created` is 0, so the audit helper (`docker create` / `docker rm` inside `c1008_audit`) is gone.

Locks: `/home/mc/antiphon-server2/locks/rollout.lock` and `cache-maintenance.lock` exist, size 0, mtime 07:23Z. `lslocks` shows neither held. `flock` drops when the deploy shell exits (`c1008_rollout_lock` `:3774`, `c849_lock` `:1350`).

Runner status, `GET /api/session-runners/server2/status` at 07:46Z:

| | server2 | server2-temp |
|---|---|---|
| buildVersion | `4358939ecd85d6e7ff0941f970879499cb930e3d` | `d985af05b5ace2f13ec3f0d886c15992e9c13c3f` |
| sessions / runnerSessions / queuedTasks | 0 / 0 / 0 | 6 / 6 / 0 |
| draining | true | false |
| acceptingNewWork | false | true |
| redirectTo | `server2-temp` | none |
| retireWhenIdle | false | false |
| retiredAt | null | null |

`GET /api/session-runners` agrees: server2 `occupied=0`, `capacityKind=sessions`, `draining=true`, `acceptingNewWork=false`. That list route does not carry the session counters; the status route does. Temp is still the live runner. This failure did not drain or undrain either runner. The journal's `liveZero.buildVersion` is the same `4358939ecd85` value.

Other journals under `/home/mc/antiphon-server2/recycle/`, names only:

| File | sourceSha12 | phase | outcome | audit key |
|---|---|---|---|---|
| `c100806c6472a03af4614b050771792c49992.json` | `beeaa1902b92` | preflight | refused | present |
| `c10082be2a2e3185a4145869c7000342a20eb.json` | `b5e78700ae9a` | preflight | refused | absent |
| `c10084e85d01056584e1a8f285a1bf59837b6.json` | `51f175dbf738` | preflight | refused | present |
| `c10087844d55007a24e079503d713f1ff6940.json` | `51f175dbf738` | preflight | refused | absent |

### Resume, and whether a re-run is clean

A new `redeploy-old` mints a new operation id (`deploy-server2.ps1:1043`) unless `-ResumeRecycle` is passed. The remote script only refuses an existing file at `$SERVER2_ROOT/recycle/$C1008_OPERATION.json` (`c590-remote.sh:5816`). The failed journal is a different id, so it does not block that new operation.

`Assert-NoIncompleteRecycle` (`deploy-server2.ps1:305-327`) runs only when `buildVersion` already equals the target SHA (`:1022`). server2 is still on `4358939ecd85`, so the upgrade retry does not call it. After the jq fix, re-running `redeploy-old -Sha 51f175dbf738519e8e50842697499c6c6c12b2c1` is clean: new operation, same admission (draining, 0 sessions, temp accepting), audit passed through a file instead of argv.

Do not `-ResumeRecycle c10087844d55007a24e079503d713f1ff6940`. That is human-only and it will not finish the recycle. Resume re-runs the audit (`:5905`), then `:5907` requires the saved `.audit` to equal the new receipt. The saved field is absent (`jq -r` prints `null`). `c1008_refuse RecycleResumeMismatch` runs with `C1008_ACTIVE=1` (`:5904`) and rewrites the journal. Volumes stay. The operation id still exists, so a later non-resume of that same id still refuses at `:5816`.

No human cleanup is required before the fixed retry. The preflight journal can stay. It records no stop and no removal.

One later footgun, not a blocker for this upgrade: once server2's `buildVersion` is `51f175db`, a later `redeploy-old` of that same SHA takes the healthy path and calls `Assert-NoIncompleteRecycle`. The filter keeps every `antiphon-runner` journal for that SHA whose `phase` is not the string `completed` (`deploy-server2.ps1:316`). Success sets `phase` to `verified` (`c590-remote.sh:6158`), never `completed`. This failure's journal stays `preflight`. Both match. `c10084e85d01056584e1a8f285a1bf59837b6.json` matches too. That refuses `RecycleResumeRequired` before any host case. It does not affect the retry that performs the upgrade.

## 3. Smallest fix

Edit `scripts/c590-remote.sh` only.

1. Lines 5909-5911. Write `$audit` to `$CASE_DIR/audit.txt` (mode `0600`, same as the journal) and load it with `jq --rawfile`. The record stays on stdin. Host jq 1.7.1 has `--rawfile` (jq 1.6+). Keep a typed failure on the assignment (`|| c1008_refuse RecycleReceiptUnavailable`) so a later `jq` failure is not `UnhandledExit`.

```bash
audit_file="$CASE_DIR/audit.txt"
( umask 077; printf '%s' "$audit" > "$audit_file" ) || c1008_refuse RecycleReceiptUnavailable
C1008_RECORD="$(printf '%s' "$C1008_RECORD" | jq -c --rawfile audit "$audit_file" '.audit=$audit')" \
    || c1008_refuse RecycleReceiptUnavailable
c1008_save || c1008_refuse RecycleReceiptUnavailable
```

2. Lines 4373, 5675, and 5677. After (1), a saved record is about 190KB and `--argjson saved "$C1008_RECORD"` hits the same limit on resume. Write the current record to a file under the evidence directory and use `jq --slurpfile saved "$file"` with `$saved[0]` in the filter. Do this at all three sites. Resume calls them before line 5910, so fixing only 5910 leaves the next resume of a successful journal broken.

3. Pin it in `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, Linux shell, same shape as `C1105AuditContract` / `LinuxShell`:

- Static: `scripts/c590-remote.sh` has no `--arg audit "$audit"` and no `--argjson saved "$C1008_RECORD"`.
- Behavioral: run the `--rawfile` assignment with a 200000-byte string. Expect exit 0 and `.audit` length 200000. The same string via `--arg` must fail with status 126.
- A 390-repository fixture is not needed. 390 entry lines are 57720 bytes and fit. `C1105_Git_audit_volume_scale` (250 worktrees, content timing) does not pin `MAX_ARG_STRLEN`. The pin is one oversized string.

`c1008_recycle` is the function both `redeploy-old` (`case_deploy_parent`, `:5980`) and `retire-temp` (`retire-temp-runner` via `deploy-server2.ps1:1102`) call. The bug is in the one shipped script every phase `scp`s. `drain-temp` (`deploy-server2.ps1:1062`) does not call it. Dry run still returns at `:5826` and never reads the audit file.

No server2 action belongs in the code change. After it lands, re-run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 51f175dbf738519e8e50842697499c6c6c12b2c1 -Phase redeploy-old` with no `-ResumeRecycle`.
