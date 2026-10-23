# CARD-1525 Code evidence

The missing opt-out regression is reproduced and fixed. The extended original
checkpoint passes. This report is committed before the final all-row run so the
pushed tip can be the tested source. Final qualification facts and unedited lines
are retained in `.antiphon/c1525-evidence/final-report.md` and the final caller
report; no pre-qualification receipt is relabeled as the final SHA.

Original Code/landing owner: `f96ac1ff-a893-4d0a-887a-a8803ffd5bce`.
Branch: `feat/card-task-f96ac1ff`.
Worktree: `/work/worktrees/task-f96ac1ff`.
Task base/initial clean origin/master: `72c2464cfe572a4b4a5881c09ab1f0265b43ba51`.
Initial rc: `30cc91b1d92aed3cb2313bb5361663d2fd89c1b7`.
Plan: `docs/superpowers/plans/2026-10-23-card-1525-build-environment-plan.md`.
Mutation candidate: `docs/superpowers/mutations/2026-10-23-card-1525-build-environment-candidate.md`.
Round: Final, using the explicit brief's narrow incident scope; no Unit lane or
full assembly. Restart: none. No desktop commands or live qualification actions.

## Source and defect

`scripts/lib/BuildDriverEnvironment.cs` now checks `ContainsKey` before reading
the optional opt-out. A real ProcessStartInfo.EnvironmentVariables adapter throws
for missing keys although a standalone StringDictionary returns null. The guard
retains native Windows case-insensitive lookup, exact `1` bypass, Linux no-op and
all six policy values. No signature or caller changed.

`scripts/test-build-driver-environment.ps1` retains six wrapper scenarios and four
direct cases and adds seven separately selectable regression cases (twelve path
executions). The five real ProcessStartInfo cases run through C# and PowerShell:
absent, `1`, `0`, empty and absent/all six conflicting. Exact opt-out compares the
whole output environment to captured input; other cases assert literal settings,
unrelated value, key count and opt-out presence/value. Two standalone cases cover
absence and mixed-case opt-out names. The old default fixture always set an empty
opt-out, explaining why it missed the defect.

Commits, each pushed immediately:

- `eb1fb08c2b4300d7e5ee9c831086d16c064165e9`: tests, plan and pending controls.
- `a4e553eb0b3d5dd081f71e97cb73f86482506006`: fixture correction before production fix.
- `f8a7c0d265c6161d3097454b03c8ed927ec568fc`: minimal production guard.
- This report's commit is the final source qualification candidate; its full SHA
  is in the final receipts and caller report.

## Ordinary outcomes

| ID | Actual evidence before final tip qualification |
|---|---|
| V-1 | PASS red-to-green: CP-2 at a4e553eb0 throws the exact missing-key exception from policy.Apply; CP-1 at f8a7c0d26 passes both absent paths |
| V-2 | PASS: zero, empty and all-six-conflicting values apply the literal policy, preserve unrelated values and opt-out state |
| R-1 | PASS: exact `1` preserves the entire real ProcessStartInfo environment on both paths |
| R-2 | PASS: all six original child scenarios and four direct cases; Linux no-op and wrapper parent restoration |
| R-3 | PASS: standalone StringDictionary absence and mixed-case opt-out |
| V-3 | PASS: both linked C# consumers compile; boundary audit below; six PowerShell files parse and remain ASCII |
| V-4 | Final base..HEAD evidence guard, merge-tree and rc three-way applicability are recorded in the final addendum after this report commit |

Parent CARD-1506 V-1/V-2/R-1/R-2 are covered by the unchanged six child/four direct
assertions in CP-1. Its V-3 maps to this V-3; its V-4 maps to this V-4. No ordinary
V/R is intentionally deferred. Native Windows activation proof is the caller's
post-pick action, explicitly outside this Code run.

Initial CP-2 run `20261023-010748-9835` at eb1fb08c2 was RED because PowerShell
enumerated an empty dictionary into null in fixture setup. It is **not** the
product red witness. Corrected before production changes, including renaming the
old `$case` loop variable to avoid collision with the new typed `-Case` selector.

Valid red witness: `20261023-010858-4a78`, CP-2, a4e553eb0, 1 attempted case,
exit 1 at `$policy.Apply($environment, $true)`:
`The given key 'ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT' was not present in the dictionary.`
Production policy bytes at this commit match the task base. No deliberate mutant.

Green after fix: `20261023-011046-472b`, CP-1, f8a7c0d26, exit 0:
6/6 original child scenarios, 4/4 original direct cases (24 setting assertions),
7/7 added cases (12 paths), zero failures. Each run has fresh console logs and
schema-2 receipts; script rows have no TUnit counts or TRX (`n/a`), not zero tests.
All three runs have `dirty=0`, `sourceState=clean`, `buildSource=notApplicable`,
`slot=granted waited=0s`. Command rows load the shared C# from their certified
source through Add-Type; no cached test executable is used.

Final selection is CP-1..CP-8 at the report commit, serial, hard row timeout 2m and
total timeout 10m. CP-2..8 each requires its single nonzero named case and the
expected path count. Source and counts are inspected after the run and qualified
with the checkpoint receipt validator. The final run is required because the
brief requires the final pushed tip to be tested after committing this report.
No loaded repetitions; no flake claim or timeout/assertion change.

## Launch-boundary audit

| Boundary | Finding |
|---|---|
| PowerShell adapter Set/Enter/Exit | Uses the shared policy; old parent values use null-safe GetEnvironmentVariable. Override/restore maps are read by enumerated existing keys. No additional optional environment indexer |
| build-slot | Scoped Enter/Exit; child PSModulePath removal is safe; children inherit the corrected policy |
| checkpoint ProcessDriver / BuildConfinement | Environment entries only written, then shared Apply; no optional indexer read |
| run-checkpoint driver / graph evaluation | Same adapter; writes plus guarded Remove; no extra missing-key assumption |
| AgentTaskLandService.RunProcessAsync | Shared Apply before start; no direct environment read |
| nightly native owner | Local environment serialized by enumerating its own Keys after shared Set; keys are known to exist |
| nightly exact driver / bootstrap | Shared Set before creation; inputs copied by enumerated keys; bootstrap has no direct build policy read |
| watched tests | Shared Enter/Exit around Start-Process |
| restart-apphost / dev-aspire | Restart calls dev-aspire; restore/build route through build-slot, runtime uses no-build |
| daemon | Build through build-slot; runtime environment writes only |

No additional production edits were needed. No live service was launched,
restarted or stopped. Runner defaults and catalogue were read through
`GET /api/runner-defaults` and `GET /api/session-runners`; no host pin was added.

## Builds, costs and provenance

These are the only builds outside the command-row table, explained in the plan:

| Build | Source SHA | Result | Slot/wait/held |
|---|---|---|---|
| checkpoint bootstrap, bin-c1525-bootstrap/ | eb1fb08c2b4300d7e5ee9c831086d16c064165e9 | PASS, 0 errors, 4 warnings | granted, 0s, 41s |
| checkpoint linked consumer, bin-c1525-fixed/ | f8a7c0d265c6161d3097454b03c8ed927ec568fc | PASS, 0 errors, 4 warnings | granted, 0s, 31s |
| server linked consumer, bin-c1525-server/ | f8a7c0d265c6161d3097454b03c8ed927ec568fc | PASS, 0 errors, 22 warnings | granted, 0s, 99s |

Every build used `timeout --signal=TERM --kill-after=10s 300s pwsh -NoProfile -File
scripts/build-slot.ps1 -Label <label> -- dotnet build <project>
--property:OutputPath=bin-c1525-<name>/ --nologo`. Logs are under
`.antiphon/c1525-evidence/{bootstrap,tool-build,server-build}.log`.
Bootstrap is necessary to run the checkpoint tool; the two final-source builds
check the shared C# compile in its actual consumers. No test suites were run by
these builds. The tool holds each script row's own slot, so its rows are not
nested inside a second build-slot lease. Builds and rows were sequential.

Parse/ASCII check: test-build-driver-environment.ps1, adapter, build-slot,
restart-apphost, dev-aspire and run-daemon; 6/6 pass. No timeout was widened.
Build output cleanup and full task-history evidence guard are finalization work;
their actual outcomes are in the final addendum.

## Native proof and remaining ownership

Caller only, after ordinary Review and the reviewed pick onto rc, from an
independent Windows shell in the picked checkout (not C:\Antiphon\releases):

```powershell
Remove-Item Env:\ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT -ErrorAction SilentlyContinue
pwsh -File scripts/build-slot.ps1 -Label probe -- dotnet --version
```

Require exit 0, a version and a released slot with no KeyNotFound. Removal above
affects only that shell's process environment. Do not touch live qualification
supervisor PID 133964 or its release tree. The temporary USER opt-out removal
and AppHost/runner activation are the orchestrator's later actions when permitted;
the Linux tests do not prove native Windows activation or memory behavior.

All new PCs remain pending for method-scoped SourceLanding Mutation: PC-1 absent
and conflicting; PC-2 real adapter and case-insensitive dictionary; PC-3 zero and
empty; PC-4 each of the six settings; PC-5 PowerShell adapter (13 variants total).
All original CARD-1506 PC-1 six variants, PC-2, PC-3 and PC-4 remain pending too.
No ordinary green or this natural red witness discharges a PC. Mutation owns
deliberate red/restore/green and missing-control discovery.

Next: ordinary Review of the final pushed SHA. Preserve this Code task as landing
owner; the caller lands it after Review and commissions SourceLanding Mutation.
Restart: none in this task.

## Unedited checkpoint receipts before report commit

```text
CHECKPOINT CP-2 commit=eb1fb08c2b4300d7e5ee9c831086d16c064165e9 build=n/a filter=pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessAbsent executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=eb1fb08c2b4300d7e5ee9c831086d16c064165e9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=a4e553eb0b3d5dd081f71e97cb73f86482506006 build=n/a filter=pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessAbsent executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=a4e553eb0b3d5dd081f71e97cb73f86482506006 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=f8a7c0d265c6161d3097454b03c8ed927ec568fc build=n/a filter=pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f8a7c0d265c6161d3097454b03c8ed927ec568fc sourceState=clean buildSource=notApplicable
```
