# CARD-1527 Code report

Original Code / landing owner: cee89aca. Branch feat/card-task-cee89aca;
worktree /work/worktrees/task-cee89aca. Base 247e615da70ec1f75a05d5aa56a9c7941ae94911.
Plan: docs/superpowers/plans/2026-10-23-card-1527-watchdog-dead-launch-plan.md.

Implemented shared two-fire abandoned-launch admission, launcher-failure timeout
cleanup with captured output, and durable outage/recovery events before recovery
suppression. Restart admission revalidates the exact generation while holding an
exclusive write handle. Live/unknown launch activity, access failure, launch lock,
health timeout, replacement and future stamps cannot authorize early reclamation.
Pending alerts survive failed delivery and reuse their event IDs.

Blocking acceptance decision: the brief's existing AppHost watchdog alert channel
does not exist in source (also established by Debug 58e8197a). A proposed HTTPS
webhook adapter is implemented, but no approved receiver/configuration was supplied.
It requires receiver-side Idempotency-Key deduplication and independent availability.
No live alert was sent. R-4 delivery remains OWED, not passed. The caller must choose
the approved independent channel; adapting its transport may require a Code follow-up.

V-1/V-2/V-3 red witnesses ran first on 436a7850054012195b453a2ada58286ff7b93644:
second fire still skipped, nonzero launcher timeout retained the lock, and no alert
was emitted behind a live lock. All failed their intended behavioral assertions.
Report: .antiphon/checkpoints/20261023-013712-6e91/report.md (17s wall).
The first implementation run on 5b98b6e72fc6213e14b1b2123aae962b7bbc7cba
passed V-2 (3 assertions) and R-1 (35 assertions), but V-1/V-3 failed because
PowerShell bound File.Replace's null backup path as an empty string. This is an
introduced implementation defect; fixed with NullString, without weakening tests.
Report: .antiphon/checkpoints/20261023-014233-09a0/report.md (2m16s wall).

Final qualification facts, all unedited CHECKPOINT lines, actual source SHA,
per-ID outcomes, counts, slot waits, receipt validation and merge/pick simulations
are retained after this report-bearing commit in the ignored external report
.antiphon/task-cee89aca-evidence/final.md and its referenced checkpoint report.
This tracked report is frozen before that final run; no later report commit may
relabel its tested source. Command rows have no TRX or compiled test provenance;
nonzero named assertion counts come from each fresh console log. The checkpoint
receipt reports buildSource=notApplicable. No Unit or full-assembly run is allowed
by the brief. The six rows are the closed ordinary Linux scope.

Unlisted build: checkpoint tool bootstrap only, required to execute the closed
manifest. Through build-slot: dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-c1527-tool/ --nologo. Exit 0, 4 existing compiler warnings,
0 errors, slot=granted waited=0s held=13s. No unlisted test run.

R-3: native Windows CIM, denied-access census, captured-launcher output under
PowerShell 5.1 and OS-specific file-sharing race qualification remain OWED.
R-4: independent live notification delivery and observer registration audit remain
OWED. No desktop operation, supervisor PID 133964 or C:\Antiphon\releases access.
Restart: none. Installation/activation has not happened.

PC-1 (two-fire, identity, absence variants), PC-2 (nonzero-exit, own-lock variants),
PC-3 (threshold, deduplication, recovery, maintenance variants): all PENDING for
method-scoped SourceLanding Mutation. No deliberate mutants were run by Code.
Native/manual OWED work is not represented as a green Final acceptance gate.
