# CARD-1525 unbound Mutation candidate

Original Code/landing owner: f96ac1ff-a893-4d0a-887a-a8803ffd5bce.
Plan: ../plans/2026-10-23-card-1525-build-environment-plan.md.
Every variant is pending method-scoped SourceLanding Mutation. Bind a suitable
pack to the confirmed source landing before execution; script ordinary receipts
are not Mutation receipts. Mutation owns red/restore/green and missing controls.

| ID | Variants | Guarded defect | Ordinary selection |
|---|---|---|---|
| PC-1 | absent; conflicting | unguarded opt-out indexer throws | ProcessAbsent; ProcessConflicting |
| PC-2 | real adapter; case-insensitive dictionary | ignored exact opt-out overwrites caller | ProcessOptOutOne; StringDictionaryMixedCase |
| PC-3 | zero; empty | any present value disables policy | ProcessOptOutZero; ProcessOptOutEmpty |
| PC-4 | UseSharedCompilation; DOTNET_gcServer; DOTNET_gcConcurrent; DOTNET_CLI_USE_MSBUILD_SERVER; MSBUILDDISABLENODEREUSE; DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER | missing setting | ProcessConflicting |
| PC-5 | PowerShell adapter | policy not applied | ProcessAbsent adapter path |

The original CARD-1506 candidate's PC-1 (six variants), PC-2, PC-3 and PC-4 remain
pending. Native Windows proof/activation is separate and owned by the caller.
