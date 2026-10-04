# CARD-1022 release A: Code admission blocked

Original Code / landing owner: `ffc43849-8d59-468d-8b6a-2a00033acbda`.
Branch: `feat/card-task-ffc43849`.
Worktree: `/work/worktrees/task-ffc43849`.
Task base: `3e3436b329da772134c24c8ce4925063bf7d898a`.
Plan: [release A freeze](../superpowers/plans/2026-10-03-card-1022-modern-conpty-only-plan.md).

No release-A production or test changes were made. Admission stopped after the
required prerequisite integration exposed an evidence-history conflict with the
fixed task base. Merge `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` imports
`origin/master` at `97e697017ede03643576a5dba199b13c55457e5a` without rewriting
the task lineage. It is pushed. Relative to that master, its only tree difference
is the two inherited CARD-1022 plan/freeze commits' plan file.

## Admission evidence

The exact card expression `InboxConhost|PtyBackend|ANTIPHON_PTY_BACKEND`,
searched in tracked Git objects excluding generated docs/cards, matches 172 files
at task base (171 without the plan) and 179 at current master (plan absent).
These are updated inventories, not the planner's historical 167-file baseline.
No tests are added to Antiphon.Tests.Checkpoints. The independent census literal
remains `selected = 377`; compiled-census execution has not run.

Source Test/Arguments recount at both refs is unchanged for DA1 parser 22,
RunnerCapabilities 7, backend contract 9 (five obsolete default rows plus four
retained), ModernPtyDa1 4, environment isolation scanner 2, input chunking 6,
ceiling/profile/Grok/spill classes 12/7/14/9, queue receipt plumbing 20 and Unix
argv 17. These are static counts, not executed results. CARD-1011's named backend
method is absent at task base and has two arguments on master; A must retain
modern only. The frozen additions still require 8 policy, 5 capability, 3 owned
launch, 1 typed-input, 1 receipt-negative and 2 ceiling methods.

The current runtime/Program/phone-home/DTO and direct-client deltas were inspected:
CARD-0959 adds four CLI observation fields and both producer projections; append
nullable deprecation after those fields. CARD-1020 introduces TestOwnedPtyHost
capture and physical owned-process teardown; preserve that helper. Broader S1-S3
current-file reconciliation remains implementation work, not completed admission.

Pipeline, runner-defaults and runner catalogue were read through the configured
ANTIPHON_API endpoint (localhost:17202 is unavailable inside this container).
Only this task was in-flight Code. delegate.ps1 -Status confirmed this task is
Code and 4e6ed8fa / 1df2bada are Review, not Code owners. Code file overlap: none.
Defaults revision 2; eligible Windows and Linux lanes observed; no placement pin
was added.

The real importer and ManifestValidator accepted all nine rows for both
isWindows=false and true: exit 0, rows=9, warnings=0. Importer/validator sources
are unchanged across prerequisite integration. The one unlisted infrastructure
build was explicitly required for admission:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1022-importer -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1022-tool/ --property:UseAppHost=false --nologo`

Build: 0 errors, 1 existing CS8602 warning in TaskOwnerGuard.cs:170;
`slot=granted waited=0s`, lease held 5s. Built at the task-base SHA. It is syntax
validation, not ordinary verification. Owned alternate output is removed before
settlement; logs/JSON remain ignored.

## Blocking history result

The mandatory full-task guard at the prerequisite merge reported:

`EVIDENCE result commits=191 entries=594 violations=290 base=3e3436b329da772134c24c8ce4925063bf7d898a head=0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f`

264 violation records come from already-landed history (238 CARD-1008 and 26
CARD-0959); 26 more record the existing CARD-0959 payloads arriving through the
merge. These include TRX/log/JSON/archive paths. No new generated payload was
authored or force-added by this task. Deleting tip files cannot remove introduced
history violations; resetting/rebasing/force-pushing this published branch is
forbidden. No evidence-policy bypass, unrelated deletion or history rewrite was
performed. The full guard is rerun after this Markdown report commit; exact final
result and remote SHA are in the stored task report.

Suggested caller resolution: commission a fresh Code Worktree at current master,
carry the plan/freeze file from this branch, preserve original landing owner
ffc43849, and use the documented reviewed adoption flow when complete. This makes
the already-landed prerequisite history part of the new task baseline. The caller
must decide that continuation/baseline; this task does not rewrite its contract.

## Verification and handoff

CP-1 through CP-9: NOT RUN. Unit: NOT RUN. V-1, V-2, V-3, V-4: NOT RUN.
R-1, R-2, R-3, R-4, R-5, R-6: NOT RUN. Required manual/native acceptance: NOT RUN.
No CHECKPOINT result or clean source certificate is claimed. No red-first
implementation test, deliberate mutant, repetition or live provider ran.
PC-1 through PC-73 and all argument variants remain pending SourceLanding Mutation.

Restart: none performed. After implementation, ordinary V/R, Windows Debug,
Review and land, the caller owns the desktop runner activation; no Linux ConPTY
activation is required. Release B/C remain separate follow-ups.
