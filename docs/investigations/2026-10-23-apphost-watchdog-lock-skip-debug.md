# AppHost watchdog skipped recovery behind a dead launch's fresh lock

The watchdog's intentional 15-minute launch protection also suppresses recovery from a completely dead launch, before it checks HTTP health. The 2026-10-23 log contains 20 consecutive skipped fires across three restart-lock stamps, including 12 dead-holder skips. There is no AppHost-down-duration alert in this watchdog. Filed **CARD-1527**, Backlog, High/Soon, `ac8ec002-ca6e-4fe3-8595-192a6c6f0a6d`, on the Antiphon board for the repair.

Debug task: `58e8197a`. This investigation changed only this report and filed the card; it did not change production code, restart anything, remove a lock, change a Scheduled Task, or send an alert. No builds or tests ran. The documented behavior remains unfixed.

## Evidence and limits

- Inspected the actual machine copy at `C:\src\Antiphon`, whose detached HEAD was `30cc91b1d92aed3cb2313bb5361663d2fd89c1b7`, the rc revision named in the incident. Its watchdog, common helper, restart and installer scripts had no tracked modifications. Source line numbers below refer to that rc copy, not this report's task checkout. The task started at `72c2464cfe572a4b4a5881c09ab1f0265b43ba51`; its restart script has later changes, so it was not substituted for the incident version.
- Primary evidence is `C:\src\Antiphon\logs\watchdog-apphost.log`, lines 1-81298: 2026-08-19 22:42:53 through 2026-10-23 02:05:32. Times printed by this log are local BST; lock stamps are UTC, one hour earlier. The captured prefix includes 76,525 `fire beginning` records, including historical ProbeOnly runs.
- An ignored local evidence copy and counts are at `.antiphon/task-58e8197a-evidence/watchdog-prefix.log` and `counts.json`; prefix-copy SHA-256 is `7221cb934ba9c54961c2e59d47b290dcb821f6202c8451d7a04cea862cdb6591`. The copy normalizes lines through `WriteAllLines`; this hash identifies that copy, not the growing original. Recount helper: `.antiphon/task-58e8197a-count.ps1`. These generated artifacts are not committed; the essential evidence and counts are retained here.
- The brief and CARD-1525 supply the observation that no launcher/server/DCP process or listener remained after the failed restore, and the reported outage extent of approximately 01:41-02:15 BST. This watchdog log cannot independently prove historical child-process absence: it does not collect that evidence. The captured prefix ends at 02:05:32, so it cannot establish a 02:15 recovery time. Fires from 02:01:30 onward in the prefix have no associated skip/failure lines; they are not explicit HTTP-success receipts. No fresh process census can reconstruct the earlier census.
- A later bounded read of lines 81299-81308, through 02:16:24 BST, found ten further `fire beginning` lines and no other entries. It adds no skips or explicit HTTP-success receipts; the fixed-prefix historical counts above remain reproducible.
- The task registration read at approximately 02:05 BST points to the canonical `watchdog-apphost.ps1` with no parameter overrides. It is Ready and has **two PT2M triggers**, logon plus a time trigger. Their observed offset explains roughly one fire per minute in this incident despite the documented two-minute interval. `Antiphon AppHost` itself was Ready when inspected.

## Exact skip rule and incident timeline

`scripts/watchdog-apphost.ps1:154-189` calls `Test-LaunchInFlight` before any `Test-Round`. It first skips whenever the `Antiphon AppHost` logon task is Running, independently of lock age. It then examines the restart lock followed by the launch lock. Either active lock returns a reason, logs `skip: launch in flight`, and exits 0. It does not read/update watchdog outage state, probe health, check Docker, or invoke restart on this path. The same in-flight check runs again immediately before a restart at line 277.

The common `AppHostLockMaxAgeMinutes` is **15** (`scripts/apphost-common.ps1:26`), also the watchdog's default cooldown. `Get-AppHostLock` at lines 220-268 reads `<pid> <UTC stamp>`, falls back to file `LastWriteTimeUtc` for a missing/invalid stamp, and records PID liveness with `Get-Process`. It treats an unparseable PID conservatively as alive. `Test-AppHostLockActive` at lines 271-303 returns inactive only if the file is absent or its known age is **greater than or equal to 15 minutes**. Otherwise it is active regardless of PID liveness. Unknown age remains active; a future stamp also stays active until its computed age reaches the threshold. Holder liveness changes the diagnostic text, not the admission decision. The age-expiry branch also precedes the live-holder branch; an exclusively held restart file still cannot be deleted by another writer.

Representative evidence, original log lines 81261 and 81281:

```text
2026-10-23 01:44:24 [watchdog-apphost] [INFO] skip: launch in flight - restart lock stamp 2026-10-23T00:41:02.4867121Z is 3.4 min old; holder PID 116096 exited; child may still be launching (C:\src\Antiphon\logs\apphost.restart.lock)
2026-10-23 01:54:24 [watchdog-apphost] [INFO] skip: launch in flight - restart lock stamp 2026-10-23T00:41:02.4867121Z is 13.4 min old; holder PID 116096 exited; child may still be launching (C:\src\Antiphon\logs\apphost.restart.lock)
```

| Restart-lock holder and UTC stamp | Observed skip times, BST | Skipped fires | Dead-holder skips | Stamp expires, BST |
|---|---|---:|---:|---|
| 116096; `2026-10-23T00:41:02.4867121Z` | 01:41:30-01:54:24 | 14 | 11, from 01:44:24 | **01:56:02.4867121** |
| 159640; `2026-10-23T00:54:34.6087325Z` | 01:55:30-01:57:30 | 3 | 1, at 01:57:30 | 02:09:34.6087325 |
| 325644; `2026-10-23T00:58:18.3959267Z` | 01:58:24-02:00:24 | 3 | 0 | 02:13:18.3959267 |

These are log lines 81255-81293: **20 consecutive skipped fires**, 12 with a dead holder, and no failed probe rounds or watchdog restart invocation. The first lock was already 13.4 minutes old on its last observation. The precise dead-holder streak for PID 116096 is **11 fires across 10 minutes**, rather than 13-plus minutes after the holder exited; the whole first-lock skip streak spans 12 minutes 54 seconds.

Had the original stamp remained, and absent another in-flight guard, it would cease blocking at **01:56:02.4867121 BST**. The next recorded fire begins 01:56:23 and checks the lock at 01:56:24. Three restart-worthy HTTP-failure rounds, spaced by 15 seconds, would then be necessary before Docker/cooldown/flap checks and the final in-flight recheck. Thus 01:56:24 is the counterfactual first probe opportunity, not a guaranteed recovery time.

In reality a new stamp existed by 01:55:30, before original expiry, and another by 01:58:24. This reset the protection window. The brief attributes lock deletion/retry to the orchestrator around 01:55 and 02:0x; the watchdog records neither deletion nor its actor. It establishes replacement stamps at **01:54:34** and **01:58:18**, not exact manual deletion times. There was no opportunity for the original lock to age out in this sequence. This is not the old CARD-0152 timezone defect: the raw UTC stamp and printed local-time age agree.

## Historical counts

Counting method: assign each `fire beginning` an ordinal, associate its following `skip: launch in flight` line, and group successive skipped fire ordinals into streaks. A fire without that skip breaks the streak; a replacement stamp does not. Two counting passes, one grouping full fire records and one grouping only skip records by fire ordinal, agreed. This measures observed decisions, not independently established outage durations or unique incidents; historical versions and ProbeOnly records are included.

Across the captured prefix: **585 skipped fires in 303 streaks**. Of those, **82 skips in 16 streaks** explicitly say the holder exited. Before this incident: **565 skips in 302 streaks**, with **70 dead-holder skips in 15 streaks**. All 15 earlier dead-holder streaks follow:

| First skip, BST | Last skip, same date | All skips | Dead-holder skips | Original log lines |
|---|---|---:|---:|---|
| 2026-09-03 23:08:23 | 23:22:23 | 8 | 7 | 11534-11548 |
| 2026-09-04 09:06:23 | 09:18:24 | 7 | 6 | 11888-11900 |
| 2026-09-04 10:36:24 | 10:48:27 | 7 | 6 | 11979-11991 |
| 2026-09-04 11:06:27 | 11:20:24 | 8 | 6 | 12010-12024 |
| 2026-09-20 18:04:31 | 18:16:22 | 12 | 11 | 32117-32139 |
| 2026-09-23 09:34:05 | 09:36:26 | 4 | 1 | 36818-36824 |
| 2026-09-23 10:58:05 | 11:02:32 | 6 | 1 | 36904-36914 |
| 2026-09-24 02:26:26 | 02:30:29 | 5 | 2 | 37863-37871 |
| 2026-09-24 15:36:12 | 15:44:24 | 10 | 6 | 38639-38657 |
| 2026-09-25 11:50:12 | 11:56:24 | 8 | 5 | 39866-39880 |
| 2026-09-26 03:26:12 | 03:28:25 | 4 | 2 | 40823-40829 |
| 2026-09-26 04:04:07 | 04:08:08 | 4 | 1 | 40862-40868 |
| 2026-10-16 02:19:31 | 02:33:31 | 15 | 12 | 70461-70489 |
| 2026-10-22 02:39:30 | 02:43:32 | 5 | 2 | 79839-79847 |
| 2026-10-22 16:55:33 | 16:58:26 | 4 | 2 | 80711-80717 |

The three earlier streaks with at least 10 skips are September 20 (12), September 24 (10), and October 16 (15). October 16 reaches 14.4 minutes and then explicitly ignores the stale lock at 02:34:25, age 15.3 minutes (line 70491). September 20 ignores it at 18:18:36, age 15.4 minutes, then logs HTTP health/client 200 at 18:18:56 (lines 32141-32142). These confirm expiry works, but also show why a dead-holder skip cannot alone prove a dead child: that September stack was healthy once probing resumed.

## Why an early launcher failure leaves recovery suppressed

The rule cannot distinguish "child still launching" from "child dead." It checks only the lock holder's PID and stamp, not the spawned child, an owned launcher/AppHost/DCP process tree, port ownership, or HTTP health. The logon's Running state is another unconditional gate, not a child-liveness check.

`restart-apphost.ps1:234-235` saves the `Start-AppHostDevLaunch` process and sets `keepRestartLock=true`. Its 150-second polling deadline starts after that launch, not at lock acquisition. The wait inspects the dashboard URL, a log verdict, health and build identity, but never checks the launch child's exit status to classify early failure. `Stop-AppHostLaunchChild` reads `HasExited` only after a separately recognized build failure.

`Get-AppHostLogVerdict` (`apphost-common.ps1:682-722`) identifies BuildFailed only from a line matching `The build failed` in `apphost.log`, and separately recognizes DCP dependency errors. In `dev-aspire.ps1:102-105`, the gated restore runs before the redirected AppHost build/run log is established at lines 113-132. A restore-path terminating error can therefore escape this detector. The launch-lock `finally` at lines 200-201 removes `apphost.launch.lock`; the parent still retains `apphost.restart.lock` on timeout. The CARD-1525 Windows environment-dictionary exception fits this gap. This investigation did not reproduce that separate startup defect or recover its overwritten console output.

| Restart result | Restart-lock handling at inspected rc |
|---|---|
| 0, verified healthy build | Clear keep flag, remove file |
| 1, recognized BuildFailed | Clear keep flag, stop launcher child, remove file |
| 1, no dashboard / dashboard without health by timeout | Retain original stamp |
| 4, DCP timeout | Retain original stamp |
| 5, healthy but server build unverified | Retain stamp; leave child running |
| Terminating error after successful spawn | Finally closes stream and retains file while keep flag is true |

The no-dashboard timeout says `Leaving ...apphost.restart.lock for LockMaxAgeMinutes; delete it to force a retry` (`restart-apphost.ps1:316-320`). Finally closes the handle but preserves the original stamp (`:329`), so this is **15 minutes from lock acquisition**, not a new 15 minutes starting at exit. Ordinary manual restart admission also uses the same helper and returns exit 3 while the leftover is fresh. The machine is blocked from ordinary automatic recovery for the remaining window; it is not permanently unrecoverable, and a legitimate detached child can still finish during that time.

After lock expiry, the watchdog's independent Docker-responsive guard, **15-minute cooldown after its own completed restart attempt**, and **3-restarts/60-minute flap cap** can still defer another attempt. Failed watchdog restarts spend budget; exit 3 does not. A watchdog-originated 150-second timeout can therefore make cooldown last beyond the lock's expiry. Manual restarts do not stamp that budget. The observed state file held only `lastOkUtc=2026-10-23T00:26:07.4594241Z` and an old restart at `2026-09-02T17:03:15.0293940Z`; this incident's log shows no watchdog restart to blame on cooldown.

CARD-1525 remains independently necessary: retrying unchanged deterministically broken startup code would fail again. Removing the blind interval improves recovery opportunity and diagnosis; it does not repair that build-policy exception.

## Alerting: no down-duration threshold

`watchdog-apphost.ps1` writes console/file log lines and a state object containing only `lastOkUtc` and `restartsUtc`. It has no `downSince`, consecutive-fire failure count, notification record, Telegram sender, or attention request. The flap cap logs ERROR and exits 0. There is **no N-minute AppHost-down alert**. `lastOkUtc` is an hourly log-heartbeat throttle, updated only on selected successful fires, so it cannot safely be reused as an exact outage-start clock.

CARD-0245's separate `apphost-watchdog-state-observer.ps1:57-80` samples whether the Scheduled Task is Enabled, Disabled, Missing or Unknown. Enabled is considered healthy even if every fire skips recovery. It never probes AppHost. `AppHostWatchdogStateDocument.IsUnhealthy` only recognizes Disabled/Missing/Unknown (`server/Application/Services/AppHostWatchdogStateDocument.cs:24`). The in-server `AppHostWatchdogStateAttentionService:74` ignores a healthy/maintenance/no-episode snapshot and can create Critical incidents only while the server is running, normally after recovery. It cannot provide outage-time alert delivery for this scenario.

Additional installation evidence: both wildcard and exact Task Scheduler queries found **no `Antiphon AppHost Watchdog State Observer` task**. The canonical `apphost-watchdog-state.json` and observer log had last been written September 1; the snapshot says `Enabled`, `healthy=true`, `maintenance=false`, observed `2026-09-01T15:36:16.8614509Z`. The service has no stale-observation rejection before the healthy-state return. This stale deployment evidence is not proof that a current observer ran or that the stack was healthy. Restoring the existing observer registration alone would still not implement down-duration alerts. No scheduler repair was performed.

This conclusion concerns the AppHost watchdog and its state observer. It does not claim that every external messaging, ingress-lag or nightly-monitor system was inspected or that none could emit its own separate alert.

## Smallest safe repair and regression traps

Commission the repair under CARD-1527. Preserve CARD-0310's slow-launch protection; do not revert to "holder PID dead means stale," shorten all lock ages, or automatically delete every fresh lock.

1. **Separate observation from recovery admission.** Make bounded health observations and persist a real `downSinceUtc` even when recovery is held by a launch lock, cooldown, Docker condition or flap cap. Store the reason for withholding recovery. A healthy observation clears the outage episode. Keep recovery's existing three-round classification, including health timeout with client 200 being non-restart-worthy.
2. **Add one shared, conservative abandoned-launch decision.** Before the normal 15-minute fallback, reclaim only when the same lock generation has a positively dead holder, no owned launcher/dev-aspire/AppHost/DCP/listener, and dead health on **two separate scheduled fires**. Reset the proof on lock replacement, healthy observation, live owned activity, or unknown/failed inventory. Bind PID evidence to creation identity and the canonical root. Inspect the detached wrapper identity (`apphost.pid`/owned descendants); `dev-aspire` backgrounds an encoded PowerShell command and may exit successfully while AppHost lives. Merely grepping process names for `dev-aspire` is insufficient. A listener vetoes this strong absence claim; it is not a health-success signal.
3. **Use the same evidence at restart admission.** A watchdog-only bypass still meets `Test-AppHostLockActive`/`New-AppHostLock` in `restart-apphost` and gets exit 3. Revalidate the exact lock and live evidence immediately before the existing guarded acquisition. Preserve `CreateNew` and exclusive-handle protection; never remove a lock replaced after observation or owned by another live restart. Record launcher completion/failure explicitly where possible and clear only an owned lock after confirmed abandoned launch. A launcher `HasExited` flag alone is insufficient because successful launch deliberately detaches children.
4. **Alert on the outage independently of restart permission.** At the first fire observing elapsed downtime of at least three minutes, durably record and deliver one outage notification through an approved path that survives AppHost being down; retry delivery with deduplication and issue a recovery notification. Keep evidence for attention import after recovery. The threshold is elapsed time, not three samples, and dispatch is bounded by actual scheduler cadence. Preserve intentional maintenance suppression. Choice/configuration of the independent notification transport belongs in the repair plan; logging WARN/ERROR or posting only to the dead AppHost is insufficient. Also audit/install the missing observer through its owner, without treating that as the new alert feature.

| Regression trap | Required evidence in the repair |
|---|---|
| CARD-0310's slow detached launch | Dead holder with a live child stays protected; slow restore/build-slot wait and encoded wrapper are recognized |
| Entire launch dead | Same lock, two separate failed-health/complete-absence observations permits one guarded attempt before 15 minutes |
| One transient failure or inventory access denied | No early reclamation; missing permission/CIM result is unknown, not absence |
| PID reuse, unrelated DCP/build process, different checkout | Ownership and process creation identity prevent unsafe reclamation or false protection |
| New lock between fires or at invocation | Old proof is invalidated; neither lock is stolen; concurrent restarts preserve atomic acquisition |
| HTTP health timeout with client 200 | No destructive restart; alert policy can still report prolonged health degradation separately |
| Stamp edge cases | Preserve UTC/BST correctness, exact age >=15 boundary, unreadable/future stamps and both lock files |
| State, exits and budgets | Exit 3 stays unstamped; real failed attempts retain intended cooldown/flap accounting; outage clock survives separate fire processes |
| Alerts during suppression | Three-minute elapsed threshold still evaluated behind lock/cooldown/Docker/flap gates; delivery failure retries and duplicate fires deduplicate |
| Maintenance and independent runner | Deliberate downtime suppresses recovery/alerts; port 17204 and its sessions remain untouched |

Existing `scripts/test-apphost-lock-age.ps1` T8/T10 explicitly require dead-plus-fresh to stay active/refuse acquisition. Refine these with live/unknown-child versus proven-abandoned-child evidence rather than simply inverting assertions. Retain T9 stale expiry and timezone cases, `test-apphost-probe-class.ps1` slow-health classification, and the relevant restart timeout/build-failure/version/refusal fixtures. New coverage should use isolated roots and mocked process/HTTP/task/restart boundaries; this Debug task ran none against the live stack.

## Duplicate check and handoff

Completed full-paging `card.ps1 search` on the Antiphon board with `-All`: `watchdog` returned 104 cards, `apphost.restart.lock` four, and `LockMaxAgeMinutes` zero at the time of search. Read full CARD-0310, CARD-0011, CARD-0152, CARD-0245, CARD-0663 and CARD-1525. CARD-0310 deliberately accepted a 15-minute dead-fresh skip to avoid slow-launch livelock; CARD-0152 fixed UTC age; CARD-0245 detects disabled tasks; CARD-0663 concerns a backend bind collision; CARD-1525 repairs this incident's triggering Windows build-policy failure. None implements the proven-dead-child exception or AppHost-down-duration alert described here.

Created CARD-1527 in Backlog; no agent was spawned. The caller can land this documentation through owner task `58e8197a` and commission the implementation separately. Historical skip counts do not establish historical outages, and no runtime-fix/test-success claim is made by this report.
