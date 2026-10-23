# CARD-1527 Code report

The operator refinement arrived and resolved the transport decision: implement
independent Windows Application Event Log, local marker and optional webhook
sinks. The source now implements that choice; no transport decision remains open.

Original Code / landing owner: cee89aca-0bbe-4902-b1ed-6f03f7824270.
Branch feat/card-task-cee89aca; worktree /work/worktrees/task-cee89aca.
Base 247e615da70ec1f75a05d5aa56a9c7941ae94911.
Plan: docs/superpowers/plans/2026-10-23-card-1527-watchdog-dead-launch-plan.md.

Implemented shared two-fire abandoned-launch admission, launcher-failure timeout
cleanup with captured output, and durable outage/recovery events before recovery
suppression. Restart admission revalidates the exact generation while holding an
exclusive write handle. Live/unknown activity, launch lock, health timeout,
replacement and future stamps cannot authorize early reclamation.

Send-AppHostOutageEvent owns the three independent sinks. Windows Application
Event Log source Antiphon-Watchdog is created lazily; down is Warning, recovery
Information, and source/entry failure never fails recovery or the other sinks.
An attempted local entry is recorded before writing; a crash in that interval
can omit the local entry, while marker and webhook intent remain independent.
logs/apphost-down.alert records outage start, minutes down and the last restart
attempt; it refreshes on fires/attempts and is removed on recovery. Current state
prevents historical webhook retries from recreating a recovered marker.
The optional webhook reads only ANTIPHON_WATCHDOG_ALERT_WEBHOOK. No URL means no
request. Configured requests have a five-second bound and stable Idempotency-Key;
accepted events are not repeated, failed deliveries retry, and receivers must
deduplicate ambiguous HTTP completions. No URL or response body is logged.

## Historical outcomes, preserved

V-1/V-2/V-3 red witnesses on 436a7850054012195b453a2ada58286ff7b93644:
second fire skipped, nonzero launcher timeout retained the lock, no alert behind
a live lock. All failed intended behavioral assertions. Report:
.antiphon/checkpoints/20261023-013712-6e91/report.md (17s wall).
First implementation 5b98b6e72fc6213e14b1b2123aae962b7bbc7cba passed V-2
(3 assertions) and R-1 (35); V-1/V-3 failed an introduced File.Replace null-to-empty
binding defect. Fixed with NullString, without weakening tests. Report:
.antiphon/checkpoints/20261023-014233-09a0/report.md (2m16s wall).
Initial qualified tip 637fb5c70ecf002dc8ac2c99da2dfe3ff0b4f8c6 passed six
rows / 123 assertions; transport selection remained unresolved at that report.
Report: .antiphon/checkpoints/20261023-014828-49e8/report.md (17s wall).
The operator refinement explicitly resolves that historical hold.

New V-4/V-5/V-6 witnesses on 51dab1689130bb4a967bbfc58f3c20c527539fea failed
on missing Event Log delivery, missing marker creation, and the new environment
variable not being consumed. Report:
.antiphon/checkpoints/20261023-020124-04c0/report.md (1m39s wall;
slot=granted, waited=75s/90s/0s for CP-7/CP-8/CP-9).

The first nine-row refinement run on d40ae704187fdce00b47927f5b90678325b65d81
passed eight rows. CP-8 failed exact marker timestamp equality: the shared UTC
parser cast PowerShell JSON DateTime values through a display string and lost
fractional seconds. The unchanged assertion exposed this defect. The repair
preserves typed DateTime/DateTimeOffset instants and adds a persisted 179.5-second
outage check to prevent an early threshold. No assertion or timeout was loosened.
Report: .antiphon/checkpoints/20261023-020700-93da/report.md (1m23s wall).

## Final qualification and limits

This tracked report is frozen before the final source run. Final actual tested SHA,
counts and every V/R outcome, unedited CHECKPOINT lines, receipt validation,
master/rc simulations and cleanup facts are retained in the ignored report
.antiphon/task-cee89aca-evidence/refinement-final.md and its checkpoint reports.
No later report commit may relabel tested source. The nine closed command rows
cover ordinary Linux scope: no Unit lane or full assembly per the explicit brief.
Nonzero assertion counts come from fresh console logs; non-TUnit rows have no TRX
and buildSource=notApplicable. A common-helper/entry-script behavior change requires
this final run; it is not an unchanged green proof repetition.

Unlisted builds were checkpoint tool bootstraps only, both build-slot gated:
initial bin-c1527-tool/ (0 errors, 4 existing warnings, waited=0s, held=13s), and
refinement bin-c1527-refinement/ (0 errors, 4 existing warnings, waited=211s,
held=7s). No unlisted test runs. Alternate output directories are removed only
after the tool and all executors finish.

R-3 remains OWED to Windows: native CIM, denied-access census, 5.1 launcher capture
and OS-specific file sharing. R-4 remains OWED to Windows: real Event Log source
creation/permissions/Warning/Information and observer-registration audit. No live
webhook is configured or sent; HTTP and native Event Log calls are fixture seams
in Linux tests. No desktop operation, supervisor PID 133964 or releases-directory
access. Restart: none. No installation, land or activation occurred.

PC-1 (two-fire, identity, absence), PC-2 (nonzero-exit, own-lock), PC-3 (threshold,
deduplication, recovery, maintenance) and PC-4 (independent sinks, optional variable,
marker recovery): every variant remains PENDING method-scoped SourceLanding
Mutation. No deliberate mutants were run. Native OWED work is not a green gate.

Next: ordinary Review of the original Code task, retaining the explicit Windows
OWED scope. The caller lands this original Code task only after the required gates,
then commissions SourceLanding Mutation.
