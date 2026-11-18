# Retire-temp fresh preview stopped `TempContainerCensusUnavailable` (gate 9, rc/2026-11-27)

Task d4f90255 (Investigate), 2026-11-18. Read-only. Source read: main checkout
`C:\src\Antiphon` at master `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (= rollout SHA, = origin/master
at read time). Receipt: `C:\src\Antiphon\.antiphon\rolling-server2\c727e1dbc9dd02d7`.
Related: Investigate 36d4e397 (`docs/investigations/2026-11-18-retire-temp-resume-mismatch.md`,
commit abd91c4d301679254cd3998d7b6ef75bcf4d67c9) for why Resume of c100884f fails.

## Verdict

**Transient, client-side, not caused by the partial operation.** The census is a single
`ssh mc@server2 docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp`.
At 06:26:10Z that command ran on server2 and its session completed in the same second, yet the
desktop process reported failure about 30 s later. That gap matches the census's
`WaitForExit(30000)` timeout, so the local `ssh.exe` most likely did not exit in time. The same
census succeeded after the PartialRefused journal existed (Resume run at 06:13Z) and in 4 of 4
read-only repros at 06:30Z (exit 0, empty output, 1.7 to 3.7 s). A fresh preview can work, so
the documented recovery stands: rerun one fresh preview, then the apply.

## (A) Which census, and what was unavailable

- `TempContainerCensusUnavailable` on this path is raised only by `Assert-TempProjectAbsent`,
  `scripts/deploy-server2.ps1:1292-1321` (master e229fb4d3):
  - It runs `ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'` (`:1304-1305`).
  - It throws if the process has not exited within 30 s (`:1314`, then Kill), or if the exit code is non-zero (`:1317`).
  - A catch-all `catch { throw 'TempContainerCensusUnavailable' }` (`:1320`) also turns any other exception into the same token. The stderr text and the exit code are discarded, so the receipt cannot say which branch fired.
  - On success it writes `temp-project-containers.txt` (`:1318`).
- The c590 host-side raises (`scripts/c590-remote.sh:10136,10186,10200,10206`) belong to the apply
  path's `c1008_container_census`. They were not reached: the host case for this run was
  `retire-temp-containers` (preview), and it returned `accepted:true exit:0`.
- Call path for a `-DryRun` retire-temp (`deploy-server2.ps1:1479-1493`):
  1. `Invoke-TempContainerCleanup -Preview` succeeded. Receipt `retire-temp-containers/temp-containers.json` has `outcome:"preview"`, `candidates:[]`, `temp.runnerSessions:null`, and main `available/dispatchEligible:true`, `buildVersion:e229fb4d3…`.
  2. `Assert-RetiredTempCounters` runs (`:1492`). Because `runnerSessions` is null, it calls `Assert-TempProjectAbsent` (`:994`). That call failed.
  3. The second census at `:1493` and the `retire-temp-runner` host preview were never reached. This receipt has no `temp-project-containers.txt` and no `retire-temp-runner/`, while every earlier receipt (c727c1b8…, c727ceb3…, c7276c73…, c7276181…) has both.
- Server2 evidence: tailscaled journal, `journalctl -t tailscaled` filtered to non-pty commands.
  - Every earlier preview or Resume (04:52:31, 05:01:33, 05:11:26, 05:25:34, 05:32:47, 06:04:20) shows **two** `docker ps -aq --filter …antiphon-runner-temp` sessions in the same second: the `:994` call and the `:1493` call.
  - The 06:26 run shows **one** such session, at 06:26:10 (`sess-20261118T062610-5f42101e74`), with "Session complete" at 06:26:10. No session from the desktop follows until another caller at 06:28:20.
  - The docker daemon journal has no entries between 06:25:50 and 06:27:00.
- Timing:
  - The last receipt write was at 06:26:09.84.
  - The census ssh landed at 06:26:10.
  - The wrapper logged `PREVIEW exit=2 end 06:26:40`.
  - The top-level `try/catch` has no `finally` and does no further I/O (`:1532-1564`).
  - So the roughly 30 s between the remote completion and the stop is the census's own `WaitForExit(30000)` timeout.
  - Conclusion: the remote `docker ps` finished at once, but the local `C:\Windows\System32\OpenSSH\ssh.exe` child did not exit within 30 s. Its stdin is inherited and not redirected, and there is no `-n`.
- What changed versus 05:24, 05:32 and 06:05: **no input changed.** The temp project had no containers at any of these times; every census at 05:24, 05:32, 06:04 and 06:13 wrote an empty `\r\n` `temp-project-containers.txt`. The PartialRefused journal (c100884f receipt written 06:13:12) and the removed volumes are not inputs to this census. The temp runner API is not involved: its status read succeeded.

## (B) Deterministic or transient: transient

- The census succeeded **after** the partial operation: the Resume run at 06:13Z (receipt `c727618127f406cb`) has `temp-project-containers.txt` = `\r\n`, and server2 shows its two census sessions.
- Repro at 06:30Z: four read-only runs of the exact census.
  - One run used Git Bash ssh.
  - Three runs used the script's own `Process.Start` pattern, with `ssh.exe` from `C:\Windows\System32\OpenSSH`, BatchMode, ConnectTimeout=15, redirected stdout and stderr, and stdin not redirected.
  - Every run returned exit 0 with empty stdout and empty stderr, in 1656 to 3738 ms.
- Server2 at 06:30:53Z: load average 16.15 / 19.34 / 15.78 on 24 cores. Tailscale SSH check mode was not involved: every session in the window logged `access granted`.
- Remaining uncertainties:
  - Neither the exit code nor stderr of the failed ssh child was recorded, so a fast non-zero exit cannot be strictly excluded. The roughly 30 s gap with no other I/O makes the timeout branch the reconstructed mechanism.
  - Why `ssh.exe` stayed alive after its remote session closed is not reproduced. A plausible cause is that the inherited stdin was not closed.
  - The same pattern exists in `Assert-NoIncompleteRecycle` (`:350-374`) and in the census paths.

## (C) Next commands

Preconditions, all verified read-only at 06:30Z:
- Main runner healthy on e229fb4d3.
- No `antiphon-runner-temp` containers.
- Only `antiphon-runner-temp_work` remains among the four temp volumes.
- c100884f lifecycle owner pid 32268 is not running.
- Registry and census inputs unchanged.

No manual operator step is needed for the census: there is no census file to refresh. The
orchestrator may run steps 1 to 3 itself (docker-stack.md gate 9: "Any other refusal … gets one
fresh preview and apply"; operator 2026-11-03: retire-temp is automatic).

1. Fresh preview, from `C:\src\Antiphon` at e229fb4d3:
   ```powershell
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -DryRun
   ```
   - Expect `C1609_RETIREMENT state=Ready operation=<c1008…>`.
   - If it stops `TempContainerCensusUnavailable` again, run the read-only census by hand once (the command in (A)). If that returns quickly with exit 0, retry the preview once; it is cheap and makes no change.
   - A second consecutive census failure makes this a defect to fix in code; see "Not done, noted" below.
2. Within 900 s of the Ready preview, apply with that preview's token:
   ```powershell
   $env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -RetirementProof <operation-id>
   ```
   This removes `antiphon-runner-temp_work`. Confirm with
   `ssh mc@server2 'docker volume ls --format "{{.Name}}" | grep antiphon-runner-temp'`; expect no output.
3. Close c100884f. Until this is done, the next rollout's `deploy-temp` refuses
   `TempRetirementUnresolved` through `c1609_temp_retirement_clearable`
   (`scripts/c590-remote.sh:9946`, docs/docker-stack.md:136-142). Close only after all four
   volumes are absent:
   ```powershell
   pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId c100884f1e02a03b541118d329379ce16e480 -ExpectedJournalDigest <sha256 of receipt.json at that moment> -ReasonFile <file>
   ```
   - The receipt digest at 06:31Z was `a006437f65f3f756716f7ae229615bd79949eb29bb810bb7d8b10ca03e7365fa`. Re-read it with `ssh mc@server2 sha256sum /home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/receipt.json` immediately before Close.
   - Close needs the desktop operator credential (`Get-CloseAuthority`, `scripts/retire-runner-volumes.ps1:43-57`). The orchestrator can run it only where that credential resolves, optionally with `-AuthorityFile`. Otherwise this one step is the operator's.
   - Suggested reason text: "c100884f PartialRefused at 06:13Z (3/4 removed); Resume mismatch per Investigate 36d4e397; antiphon-runner-temp_work removed by fresh operation <id>; all four absent."

## (D) Standing runner and temp project (read-only, 06:30:53Z)

- `antiphon-runner-session-runner-1` was Up 3 hours (healthy). `antiphon-runner-build-slots-1` was Up 7 weeks (healthy).
- No container carries the label `com.docker.compose.project=antiphon-runner-temp`: `docker ps -aq` with that filter returned empty, four times.
- Temp volumes: only `antiphon-runner-temp_work`, created 2026-11-18T01:30:21Z, labelled compose project `antiphon-runner-temp`.
- The 06:26 cleanup receipt reports server2 main as available, dispatchEligible, acceptingNewWork and not draining, with `buildVersion` e229fb4d3…. It reports temp as retired, with 0 sessions and 0 queued tasks.

## Not done, noted

- Fix idea (one line): `Assert-TempProjectAbsent` (`deploy-server2.ps1:1292`) should redirect and close stdin (or pass `-n`) and record the exit code and stderr instead of collapsing them into a catch-all token.
- Candidate defect card: "deploy-server2 census ssh can outlive its remote command and time out (TempContainerCensusUnavailable at 06:26:10Z gate 9, remote session completed in <1 s); census discards exit code/stderr so the cause is unrecorded."
