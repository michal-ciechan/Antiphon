# Code a1f403eb — CARD-1483 S1 re-cut onto master with the CARD-1409 recipe

Branch `feat/card-task-a1f403eb`, worktree `/work/worktrees/task-a1f403eb` (server2), start ref
`6a2fe6ae390400aff141bf99633d427f78f47346` (master carrying the CARD-1409 chain). This task is the land owner
of the re-cut. The old owners 65d0b3d3, da684330 and d205268d are not landed. Plan addendum:
`docs/superpowers/plans/2026-10-22-card-1483-s1-recut-plan.md`. This report is committed before the one
checkpoint run, so the pushed tip is the tested source. The CHECKPOINT lines and validator output are in
the final message.

## Commits (base 6a2fe6ae3..)

| Slice | SHA | From |
|---|---|---|
| R0 | 4c942c50ec65061e4c04de45c8ea64f7967da112 | 0a991fb7b S1 red witnesses |
| R0 | 0eb0af09914786b7d7cc49f8c38b1dde3898dea1 | feca0ce22 |
| R0 | 06e0e94f3b71d0a264ee91d3b241fe71efc32c53 | 5a5ea5f5b S1 tool |
| R0 | 145061c5d32edd97d76adcdd65240a1b18e87744 | 7a5f32417 repair red |
| R0 | bfdf08222a1028404dca2f2f6b9694b2cbce320d | aa905becf repair fix (+ repair amendment) |
| R0 | 89f45fcfaab232c0afabe62a2749b5d49135c2d3 | c8969a913 repair 2 red |
| R0 | 8c03d73b9c58bb5e21cac1940afe54b71b3f46bc | ede58371c |
| R0 | 2104ef8bd5797bfd8819310b58bcfd10bbebd8e5 | 530f730af repair 2 fix (+ repair2 amendment) |
| R1 red | 70753aecd63405b4d84772aaea09a44bf1538ab3 | manifest regenerated, script unchanged |
| R2 fix | 233b0587ce06e615854be61089f1ba1cbb647746 | four allow-list entries |
| R3 | (this commit) | plan addendum + report |

All eight picks applied cleanly (`-x`). Report commits and the manifest regen 09876e2d5 were dropped. The
tools/, LandGraphEvaluationTests.cs, Antiphon.Tests.csproj and the two amendment docs are byte-identical to
d205268d's tip 2904c9bcb.

## Second-lander recipe (R2)

```diff
-    '_StagedHelperGlob', '_StagedHelperShared', 'AntiphonCleanFileMaxBytes', 'AntiphonMessagingVersion',
+    '_StagedHelperFramework', '_StagedHelperGlob', '_StagedHelperShared', 'AntiphonCleanFileMaxBytes', 'AntiphonMessagingVersion',
+# CARD-1483: the one exception is ProjectReference SetTargetFramework pinned to the literal value below
+# (the LandGraphAudit tool is built for net10.0); any other value, or the child-element form, refuses.
-$ItemMetadata['ProjectReference'] = @('Aliases', 'ReferenceOutputAssembly', 'Private')
+$ItemMetadata['ProjectReference'] = @('Aliases', 'ReferenceOutputAssembly', 'Private', 'SkipGetTargetFrameworkProperties')
-$ItemMetadata['StagedHelper'] = @('Folder', 'Glob')
+$ItemMetadata['StagedHelper'] = @('Folder', 'Glob', 'HelperTargetFramework')
+# Item type -> metadata attribute name -> the only value (ordinal) under which that name is allowed.
+$PinnedItemMetadata = @{ 'ProjectReference' = @{ 'SetTargetFramework' = 'TargetFramework=net10.0' } }
 (in Assert-ItemOperationsAllowed, attribute loop, before the allow-list check)
+        if (-not $isRemove -and $PinnedItemMetadata.ContainsKey($Item) -and $PinnedItemMetadata[$Item].ContainsKey($attributeName) -and
+            [string]::Equals([string]$PinnedItemMetadata[$Item][$attributeName], [string]$attribute.Value, [System.StringComparison]::Ordinal)) { continue }
```

SetTargetFramework is narrowed to the one literal value as the brief asked. The script structure allowed
it: one attribute loop in `Assert-ItemOperationsAllowed`. The child-element form is not pinned and still
refuses.

## Red then green (direct script runs, no build driver)

- R1 `70753aecd` `-Check`: exit 1, `definition file sets a property the audit does not allow; -Update cannot
  approve it: tests/Antiphon.Tests/Antiphon.Tests.csproj _StagedHelperFramework`. This is the same `-Check`
  that `C1385_RegistryMatchesNarrowGraph` runs on the S1 tree; its digest and count steps already match
  the R1 manifest. The test itself was not run at R1.
- R2 `233b0587c` `-Update` and `-Check`: exit 0,
  `digest=90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937 count=58`. This equals the S1-only
  value (master was F240D0F1.../57), and the manifest bytes equal d205268d's.
- Scratch probe worktree at `233b0587c`: one commit per probe, then `-Update`; the worktree was removed.
  - SetTargetFramework `TargetFramework=net9.0`: exit 1, metadata refusal.
  - SetTargetFramework `TargetFramework=net10.0;Configuration=Debug`: exit 1.
  - The child-element `<SetTargetFramework>TargetFramework=net10.0</...>`: exit 1.
  - SetTargetFramework on a PackageReference: exit 1.
  - `Properties="TargetFramework=net10.0"`: exit 1.
  - Removing each of the four entries in turn from the script: exit 1, naming that entry.
  - The three ordinary edits each pass `-Update` with exit 0 and count 58:
    - a new PackageReference: digest CA08E6A7...;
    - a Compile Link: digest B2F2A7E6...;
    - a ProjectReference with ReferenceOutputAssembly=false: digest 789E3286....

No LandNonCodeSkipTests row was added or changed.

## Pending for Mutation

All S1 PCs stay pending. New: the four entries and the SetTargetFramework value pin. No test in either CP
class feeds a non-pinned SetTargetFramework value, so a mutant that allows the name for any value
survives CP-A and CP-B. Only the scratch probe above shows that refusal.

## Unlisted runs

- `dotnet run --project tools/Antiphon.Checkpoints -- import --plan ...` built the checkpoint tool itself
  (default bin, not a test build) to validate the table: exit 0, 2 rows.
- Direct `pwsh` audit script runs as listed above.
