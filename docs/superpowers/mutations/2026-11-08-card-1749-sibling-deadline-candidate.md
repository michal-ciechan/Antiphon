# CARD-1749 unbound Mutation candidate

Original Code/landing owner: dd320fe1. SourceLanding binding belongs to the caller.
No plan file. The witness is `SiblingInspectionDeadlineWitnessTests.C1749_NullSettingsFallbackIsTheGitSettingsDefault`.
Every control below is pending; ordinary red/green does not discharge Mutation.

| ID/variant | Guarded behavior | Deliberate mutant | Exact method filter |
|---|---|---|---|
| PC-1 | Null `IOptions<GitSettings>` uses `GitSettings.DefaultWorktreeBaseInspectionTimeoutSeconds`, the same identifier as the property initializer | Restore `?? 2` on the `CollectUnlandedSiblingsAsync` `CancelAfter` line | `/*/*/SiblingInspectionDeadlineWitnessTests/C1749_NullSettingsFallbackIsTheGitSettingsDefault` |
| PC-2 | That constant stays 5, so the product default does not move | Change `DefaultWorktreeBaseInspectionTimeoutSeconds` from 5 to another positive integer | `/*/*/SiblingInspectionDeadlineWitnessTests/C1749_NullSettingsFallbackIsTheGitSettingsDefault` |

No cache was added. Maximum extra staleness is zero. Injected `GitSettings` still supplies `WorktreeBaseInspectionTimeoutSeconds`; only the null-options path changed, from the old 2 second literal to this constant. `inspection_timeout` warning text is unchanged. Mutation owns the deliberate mutants, red/restore/green, and missing-control discovery.
