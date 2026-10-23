# CARD-1527 dead AppHost launch recovery

Code/landing owner: cee89aca; base 247e615da70ec1f75a05d5aa56a9c7941ae94911.
Scope: scripts/apphost-common.ps1, watchdog-apphost.ps1, restart-apphost.ps1,
isolated PowerShell fixtures and the AppHost runbook/bootstrap owners.
The explicit brief excludes the generic Unit lane and whole assemblies.

S1: commit this plan and three failing entry-script witnesses before production edits.
S2: share a pure, injected-observation decision between watchdog and restart admission.
A fresh restart lock is reclaimable only for the same generation on two distinct
fires with a known dead holder, complete empty owned-process/listener census and
dead health. Unknown evidence, a replacement, a healthy/slow observation, a live
child or launch lock cancels the proof. Revalidate under exclusive restart-file
ownership, preserve atomic acquisition, and never bypass the launch lock.
S3: capture launcher stdout/stderr; confirmed nonzero launcher exit at health timeout
clears only this restart's lock. Successful detachment and unknown exit retain it.
S4: persist outage time before recovery guards, emit one event at >=180 seconds and
one recovery event, with durable pending delivery and stable deduplication identity.
Refinement decision (operator, input 92375fc6-31ae-42ec-affd-01d28ef7a745):
three independent sinks behind one function: Windows Application Event Log source
Antiphon-Watchdog (created lazily; Warning down / Information recovery; creation
failure never fails the watchdog), logs/apphost-down.alert (outage start, minutes
down, last restart attempt; removed on recovery), and OPTIONAL webhook only from
ANTIPHON_WATCHDOG_ALERT_WEBHOOK, once per outage plus recovery. Never log its URL.
S5 commits failing sink witnesses. S6 implements these sinks, updates entry-script
coverage and freezes the report before final qualification. No other transport.
Do not send live notifications in tests. Preserve maintenance suppression.

## Verification design

V-1: `test-apphost-dead-launch.ps1 -Case Recovery`: real copied watchdog entry,
two separate fires reclaim a fully dead fresh launch through shared admission.
V-2: `-Case Launcher`: real copied restart entry, failed launcher at timeout removes
its lock and prints captured-output paths, before any apphost.log build verdict.
V-3: `-Case Alert`: copied watchdog entry, down threshold while recovery is skipped;
one outage/recovery intent, persistence/retry/dedup across fires, maintenance.
R-1: `-Case Guards`: pure decision matrix for live/unknown children and holders,
PID reuse, one fire, repeated fire ID, generation change, future/unreadable stamps,
client-up timeout, launch lock, exact 15-minute boundary; file admission replacement
and competing holder checks; failed/successful/unknown launcher outcomes.
R-2: existing lock-age and probe-class harnesses (CARD-0310/0152 compatibility).
R-3: native Windows census (CIM/encoded wrapper/denied access/PID identity), held-file
races and captured launcher output under PowerShell 5.1 are OWED to Windows lane.
No desktop operation, supervisor PID 133964 or C:\Antiphon\releases access here.
R-4: native Windows Application Event Log source creation, denied-source handling
and Warning/Information entry qualification plus observer-registration audit are
OWED to the Windows lane. Optional webhook remains unconfigured here; no live alert.
V-4: test-apphost-alert-sinks.ps1 -Case EventLog: one down/recovery local event;
source failure is isolated from marker/webhook. Native adapter details remain R-4.
V-5: -Case Marker: actual file contents, refresh, last attempt, recovery removal,
old pending down event cannot recreate marker, write failure leaves other sinks working.
V-6: -Case Webhook: exact optional variable, absence is harmless, bounded request,
durable deduplication, failed-delivery retry does not repeat the local event.

PC-1 (two-fire/identity/absence variants), PC-2 (nonzero exit/own-lock variants),
PC-3 (threshold/dedup/recovery/maintenance variants) and PC-4 (independent
sinks/optional-variable/marker-recovery variants) remain pending for method-scoped
SourceLanding Mutation; Code creates no deliberate mutants. Candidate binding belongs
under docs/superpowers/mutations/ after Review.

### Checkpoints

Non-TUnit command rows have assertion counts in their logs, no TRX or build; source
receipts must still bind the clean committed HEAD. Run CP-1..3 red after S1 and
rerun red rows green after S2-S4. CP-4..6 run once on the final report-bearing tip.
All rows have a hard 120-second timeout; tool bootstrap is an explained unlisted build.
Refinement: CP-7..9 run red at S5, then all nine rows qualify the report-bearing
S6 tip because common helper and entry-script notification behavior changed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S4 | n/a | dead-launch | `pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case Recovery` | V-1 | 0 failures; named assertions >0 | n/a | 1 |
| CP-2 | S1-S4 | n/a | launcher-exit | `pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case Launcher` | V-2 | 0 failures; named assertions >0 | n/a | 1 |
| CP-3 | S1-S4 | n/a | outage-alert | `pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case Alert` | V-3 | 0 failures; named assertions >0 | n/a | 1 |
| CP-4 | S1-S4 | n/a | conservative-guards | `pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case Guards` | R-1 | 0 failures; named assertions >0 | n/a | 1 |
| CP-5 | S1-S4 | n/a | lock-compatibility | `pwsh -NoProfile -File scripts/test-apphost-lock-age.ps1` | R-2 | 0 failures; named assertions >0 | n/a | 1 |
| CP-6 | S1-S4 | n/a | probe-compatibility | `pwsh -NoProfile -File scripts/test-apphost-probe-class.ps1` | R-2 | 0 failures; named assertions >0 | n/a | 1 |
| CP-7 | S5-S6 | n/a | alert-eventlog | `pwsh -NoProfile -File scripts/test-apphost-alert-sinks.ps1 -Case EventLog` | V-4 | 0 failures; named assertions >0 | n/a | 1 |
| CP-8 | S5-S6 | n/a | alert-marker | `pwsh -NoProfile -File scripts/test-apphost-alert-sinks.ps1 -Case Marker` | V-5 | 0 failures; named assertions >0 | n/a | 1 |
| CP-9 | S5-S6 | n/a | alert-webhook | `pwsh -NoProfile -File scripts/test-apphost-alert-sinks.ps1 -Case Webhook` | V-6 | 0 failures; named assertions >0 | n/a | 1 |

### Cost

Nine narrow command rows: nine estimated minutes plus authoring; no full assembly,
no repeated green proof. Commit report before final qualification; preserve final
receipts externally without changing the tested tip. Check full base..HEAD evidence,
merge-tree current master, and simulate the rc cherry-pick without moving task refs.
