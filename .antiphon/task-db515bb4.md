# CARD-1407 Code report

CP-1 verified source `d8f351859e5bda36434d6fa7f6e055c380eddd5a`. The commit that adds this file does not change the scripts that row ran.

CARD-1407 cutover script and AppHost -StopOnly are on feat/card-task-db515bb4 at d8f351859e5bda36434d6fa7f6e055c380eddd5a. CP-1 is green: 10 passed, 0 failed. WhatIf was the only mode exercised.

Branch feat/card-task-db515bb4. Worktree /work/worktrees/task-db515bb4. Landing owner is Code task db515bb4. Base 8683447c6d80b393a2b6da77aacf6ada0a1e68ee. Commits: 7826390dae62877540d48aea3108c944bb8fc133 (red witness), d8f351859e5bda36434d6fa7f6e055c380eddd5a (script, -StopOnly, plan, runbook). Remote tip of the code commit matched before this report file. Fast-forward only. The branch was not rebased onto origin/master.

Plan docs/superpowers/plans/2026-10-21-card-1407-postgres-cutover-script-plan.md. Design docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md C2 (tip 946344bf635f8576f878c8ef2199135af8f6a43f, also origin/master 3781031b9596086132da21a8d33bf8ec0c73a80b). Evidence .antiphon/checkpoints/20261021-151617-5167/report.md and rows/CP-1/console.log (PASS W-1 through W-10). Board 127.0.0.1:17202 refused the connection, so the card text is that plan.

Files in the code commit (6, +1015/−5): scripts/cutover-postgres-server2.ps1, scripts/test-cutover-postgres-server2.ps1, scripts/restart-apphost.ps1, scripts/test-apphost-graceful-stop.ps1, docs/apphost-runbook.md, the plan above.

CHECKPOINT CP-1 commit=d8f351859e5bda36434d6fa7f6e055c380eddd5a build=n/a filter=pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=d8f351859e5bda36434d6fa7f6e055c380eddd5a sourceState=clean buildSource=notApplicable

Red witness, before the script existed: slot=fbea7f0f-b956-4599-a64b-a885b86865c1 waited=0s held=18s, exit 1, 0 passed, 10 failed. The cutover file was missing (pwsh exit 64). -StopOnly was ignored and the stub printed launching dev-aspire.ps1. Fixture seams only.

Unlisted green, to confirm that witness turned green before the closed row: slot=64205d9b-bc0d-4fd3-bc3e-5408bcc6e0f1 waited=0s held=14s, 10 passed, 0 failed, exit 0. Ran on the same sources that were then committed as d8f351859e5bda36434d6fa7f6e055c380eddd5a.

Unlisted checkpoint-tool build, to bootstrap the runner: slot=fc36d8b8-0185-48e6-a664-85f66965a81e waited=0s held=6s, 0 errors, 4 existing nullable warnings. OutputPath bin-c1407/ with UseAppHost=false. Both bin-c1407 directories were removed after the green row.

Ordinary scope is that one command row. This brief replaces the Final profile's Unit lane with one script row: no production access, no AppHost restart in tests, 40-minute budget, and no Unit class in the plan. The Unit lane stays unrun and is unmarked.

V-1 pass. Default and -WhatIf exit 0, list phases 0-6 and STOP GATE 0-6, and leave POST, docker, and user-secrets unset.
V-2 pass. -Execute without -ConfirmWindow exits 2 and changes nothing.
V-3 pass. -Execute -ConfirmWindow without a snapshot or a passed drill refuses before any stop.
V-4 pass. Cutover source has no volume rm, no docker volume rm, and no -AllowWorktree.
V-5 pass. -StopOnly -WhatIf does not POST or relaunch. Bare -WhatIf without -StopOnly exits 3 and kills nothing.
V-6 pass. -StopOnly posts the graceful shutdown, frees AppHost ports, and exits before dev-aspire.ps1. Port 17204 and antiphon-postgres stay up. The restart lock is released.
V-7 pass. A non-zero landPending fixture refuses before StopOnly. Latency above 20 ms, a failed drill, and dump plus copy above 600 s without -AcceptDrillTime also refuse. Above 1800 s refuses outright.
No R-n. Zero PC-n. Every PC stays pending for method-scoped SourceLanding Mutation. Mutation was not this task.

Behavior: -WhatIf is the default. The operator window is -Execute -ConfirmWindow and stays fail-closed on the snapshot, drill, latency, land queue, and required inputs. Connection strings and passwords stay unprinted. Volumes stay in place. Phase 5 script-owned failures exit 2 and name phase 6; the script does not roll back by itself. Tests bind -ApiBase http://127.0.0.1:9 and shadow docker, dotnet, ssh, scp, age, curl, wget, and pwsh.

Verification: evidence diff 8683447c6d80b393a2b6da77aacf6ada0a1e68ee..d8f351859e5bda36434d6fa7f6e055c380eddd5a commits=2 entries=0 violations=0. The clone is not shallow. git merge-tree --write-tree origin/master HEAD was clean at the code tip (tree 0e66b4040d8278e3d0a8c9cce80c77db75bccc6d against origin/master 3781031b9596086132da21a8d33bf8ec0c73a80b). Nothing was merged. Restart: none.

D-2: behavior, verification, and advisory are separate. The runbook paragraph is advisory documentation of -StopOnly. It does not ask for a Review rerun by itself. Initial ordinary Review remains required. Missing Unit-lane execution is the brief's narrowed ordinary scope, recorded here, and is unmarked rather than passed.

Operator-only, left for the operator window: password custody and rotation; removing antiphon_pgdata or the server2 volume; publishing Postgres off the tailnet; OneDrive sign-in; enabling the Windmill schedule; passing -ConfirmWindow; choosing phase 6 when the new database has rows the old volume lacks; the one dispatch and the one land; the Hangfire dashboard credential; the CARD-1408 thirty-minute monitor. Orchestrator autonomy does not cover this window. CARD-1405 snapshot, CARD-1406 server2 compose, and CARD-1408 monitor stay out of this slice.

--- next stage ---
next: review
handoff: Review CARD-1407 at d8f351859e5bda36434d6fa7f6e055c380eddd5a. CP-1 is the closed script row, exit 0, 10 passed. WhatIf was the only mode run. -Execute -ConfirmWindow stays operator-only. Zero PCs stay pending for SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-21-card-1407-postgres-cutover-script-plan.md
