# CARD-1506 unbound Mutation candidate

Original Code/landing owner: 84f61b86-39be-4636-8edf-537143fae95b.
Plan: ../plans/2026-10-22-card-1506-no-resident-compiler-plan.md.
Status: unbound; every control below is pending SourceLanding Mutation.
No deliberate mutant was run by Code. Bind to the confirmed source landing and
method-scoped pack before execution; the ordinary script checkpoint is not a
Mutation receipt. Mutation owns missing-control discovery and red/restore/green.

| ID | Variant | Guarded defect | Ordinary witness |
|---|---|---|---|
| PC-1 | UseSharedCompilation | missing false assignment | windows-default child observation |
| PC-1 | DOTNET_gcServer | missing 0 assignment | windows-default child observation |
| PC-1 | DOTNET_gcConcurrent | missing 0 assignment | windows-default child observation |
| PC-1 | DOTNET_CLI_USE_MSBUILD_SERVER | missing 0 assignment | windows-default child observation |
| PC-1 | MSBUILDDISABLENODEREUSE | missing 1 assignment | windows-default child observation |
| PC-1 | DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER | missing 1 assignment | windows-default child observation |
| PC-2 | Windows guard | helper changes Linux child environment | direct policy Linux cases |
| PC-3 | opt-out | helper overwrites explicit opt-out | windows-optout and direct policy cases |
| PC-4 | wrapper adapter | wrapper launches without policy | windows-default child observation |

Additional discovery should inspect omission of each compiled/native launch
adapter. Native Windows memory/process-census acceptance stays with the rc gate;
the portable Windows-branch witness does not attest live desktop process state.
