# Code 8e3c334d: CARD-1409 repair 6, one expand-split-unescape pipeline (Review c64987ea)

Outcome: done. Tested tip `3245dee4216fdc5ec3944f6c93b2631c46b6dc77` on `feat/card-task-8e3c334d` (worktree `/work/worktrees/task-8e3c334d`, server2), fast-forward from `fd20e8dccd3cdbe1f075a6973236dae3728a43a6`, plus this report commit on top. Original Code task / landing owner: this task (8e3c334d-0435-489e-9492-90d829ddd8ea) lands as its own owner per the brief; the whole CARD-1409 chain (28 commits from `9b39df4e0` through `3245dee42`, plus this report commit) lands with it, master first, then the rc port. Plan: `docs/superpowers/plans/2026-10-22-card-1409-import-fail-closed-plan.md` (Repair 6 section: D-33..D-35, escape table N-16..N-29, S20..S22, V-28..V-29). Evidence: `.antiphon/checkpoints/20261022-083845-026f/`.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `aebc26cb3a4357d28c4775d178f1832a6ed4f9e8` | S20 red witnesses: `C1409_EscapeInExpandedTextIsDecodedOnce`, the N-17..N-29 entries of `C1409_NormaliserAndItemOperationTablesHaveWitnesses`; plan Repair 6 section, CP-1 moved to S22 expecting 82 |
| `680a8e8e104602f7539ccc8ebfea7b3786809a56` | S21 fix in `scripts/audit-land-verification-graph.ps1` (D-33) |
| `de3febe86a5278cef2420a2be112ee73617853b8` | S20 follow-up: the N-18 escaped-semicolon value witness expects the segment refusal (see run 3); plan row N-18 reworded |
| `3245dee4216fdc5ec3944f6c93b2631c46b6dc77` | S22 wording: script header, `docs/orchestration-loop.md` |

## What changed (plan D-33)

- **One pipeline.** `Resolve-ItemPathText` is the only function that turns item or import text into path specs, in MSBuild's order: expand properties and metadata (`Expand-Candidates`, values spliced in their escaped form), split the expanded text on `;`, decode `%XX` once per part (`ConvertFrom-PercentEncoding`, now MSBuild's single-character unescape and called from the pipeline only), then `ConvertTo-CanonicalPath` (D-28, unchanged). A root (directory property, output or package root) belongs to the first part of its expanded text; later parts are project-relative. `%00` yields a `nul` spec that every consumer refuses by name. With `DirectoryPropertiesOnly` (imports D-1, literal property values D-25, name items, `Pkg*` ids) only a leading `$(MSBuildThisFileDirectory)`/`$(MSBuildProjectDirectory)` expands and a part that still carries a reference comes back `unresolved` for the consumer to refuse (imports, names) or skip (values, ids).
- **Every consumer routed; the duplicates removed.** `Assert-ItemIncludeAllowed` takes the whole `Include`/`Update` text (the raw `;` split in `Assert-NoDocumentationInput` is gone); `Exclude` text goes through the pipeline whole (an unresolvable text is dropped whole, and a part whose decoded text gained a wildcard character is dropped, so the audit sees more, never less); `Resolve-ImportTargets` replaces `Resolve-ImportPart` and takes the whole `Project` attribute (the raw split in `Add-ImportQueue` is gone; zero parts is the existing `(empty)` refusal); `ProjectReference` parts, name items (`Test-ItemNameIsPathLike` no longer decodes), the `GeneratePathProperty` id, literal property-value parts (the separator-or-document gate now reads the decoded part and the wildcard skip reads the escaped part) and output-root tails (`Get-OutputTailProblem`, shared) all consume pipeline specs. `ConvertFrom-PercentEncoding` has exactly one call site.
- **File-reading function tails (D-25 tightened).** After the output-root prefix check, the argument's tail goes through the pipeline and may not climb (`Get-ExpandedTailProblem`: unresolved reference, NUL, second root, drive, `..` or divergent segment refuse naming the property and the call). A `$(Name)` no tracked file may set (not allow-listed, not defined, not a root, not a task output) is the SDK's own and is read as one opaque segment (`Convert-SdkOwnedReferences`), which keeps the real `$(IntermediateOutputPath)$(CleanFile)` read approved.
- **MSBuild facts the rows rely on** (three scratch `dotnet msbuild -getItem`/`-getProperty` evaluations on server2, build-slot gated, project under the session scratchpad, discarded): a property value `%2E%2E/%2E%2E/README.md` used as an include evaluates to `../../README.md`; a literal `;` in a value splits into two items, `%3B` stays inside one identity; `%252E` becomes the literal `%2E` (once); lowercase hex decodes; `%GG` and a trailing `%2` stay literal; `%C3%A9` becomes the two characters `Ã©`; `%2A.md` stays the literal identity `*.md` while `*.md` globs to `a.md`; `%24(P1)` stays the literal `$(P1)`; `%2F`/`%5C` become separators; a property chain decodes once at the end; `@(SH->'%(Glob)')` metadata decodes once; `%40(SH)` stays literal; `%00x.json` keeps a NUL in the identity (no evaluation error); `Import Project="%2E%2E/%73hared.props"` is followed; `Import Project="a.props%3Bb.props"` is one path `a.props;b.props` (MSB4019 not found).
- **Behaviour deltas beyond the escape class, stated:** (1) the file-reading function tail is now checked for climbing (N-21), which the prefix-only D-25 rule left open for the unescaped form too; (2) `Exclude` is dropped whole when any part is unresolvable, where it was dropped per part (conservative direction); (3) a directory property past the start of a property-value part makes that part unresolved (skipped) instead of being replaced in place; (4) an escaped `*`/`?` in an include is read as a wildcard (over-approximation: at least the literal name is selected) and in an `Exclude` drops the part; (5) non-ASCII escapes decode per MSBuild to one character per escape, not UTF-8.

## Runs (unlisted, each with its reason; every driver build-slot gated)

1. **MSBuild probe** (reason: verify the escape order the design relies on rather than trust memory): three `dotnet msbuild` evaluations of scratch projects, leases `56b2296a`/`a7403152`, `befee86f`, `75e545bf`, each held 1-2 s, waited 0 s. Facts above.
2. **Red proof at `aebc26cb3`** (reason: red proof before the fix). Build `tests/Antiphon.Tests --property:OutputPath=bin-c1409r6/ -p:UseAppHost=false`: `BUILD SLOT granted lease=3ae19391-d637-4374-9d49-b2d63fe3faaf waited=105s`, held 295 s, 0 errors. Run `--treenode-filter '/*/*/LandNonCodeSkipTests/(C1409_EscapeInExpanded*)|(C1409_NormaliserAndItem*)'`: `BUILD SLOT granted lease=6557092e-5a6f-4293-a39e-4af7aa30451e waited=105s`, held 43 s; total 2, failed 2, skipped 0. `C1409_EscapeInExpandedTextIsDecodedOnce`: `ran.Code should be 1 but was 0` (the real test project with the `$(PackageTags)` escape was approved). Table case: `table row N-18 failed` with `segment=a.json%3B.. (trailing dot or space)` (the escaped value spliced raw, never decoded). TRX `.antiphon/c1409r6-red/red.trx`.
3. **Green subset at `680a8e8e1`** (reason: catch a fixture regression before the checkpoint build; same dll, the tests copy the script from the repo at run time). Filter `(C1409_*)|(C1400_*)|(C1390_*)`: `BUILD SLOT granted lease=ccdd3346-8a01-46f9-b5a4-fd0120d7f9a9 waited=15s`, held 329 s; total 39, failed 1: the table case at row N-18, second shape. Witness defect, not a fix defect: `RootNamespace` of `a.json%3B..` decodes with the include to the one segment `a.json;..`, whose trailing dots the normaliser refuses first (N-1) before any document rule, which is the right outcome and still proves no split (the literal `a.json;..` control splits and refuses as docs markdown). The witness now expects `Windows land host` and `segment=a.json;..` (`de3febe86a5278cef2420a2be112ee73617853b8`); the plan row says so. TRX `.antiphon/c1409r6-green/green.trx`.
4. **Narrow rerun at `3245dee42`** (reason: the corrected witness needs a fresh dll). Build to `bin-c1409r6/`: `BUILD SLOT granted lease=83ef09c4-3c2f-4ded-bbbf-da231b66da5d waited=0s`, held 155 s. Run of the two witnesses: `BUILD SLOT granted lease=b5b00a5f-783a-4777-9c19-81223bbf5430 waited=0s`, held 184 s; total 2, passed 2. TRX `.antiphon/c1409r6-green2/green2.trx`.
5. **Real-repo acceptance (D-35, V-7):** `pwsh scripts/audit-land-verification-graph.ps1 -Check` at `680a8e8e1` prints `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0 (20 s on a loaded host). `Parser::ParseFile` 0 errors and 0 non-ASCII bytes at `680a8e8e1` and again at `3245dee42` (header comment only).
6. **Ordinary-edit acceptance (D-27/D-35)** at `680a8e8e1` in a scratch detached worktree (discarded): `tests/Antiphon.Tests/Antiphon.Tests.csproj` plus a new `PackageReference` (`Humanizer.Core`), a new linked `Compile` (`..\..\server\Program.cs`) and a new `ProjectReference` to `tools/Antiphon.Checkpoints` with `ReferenceOutputAssembly="false"`: `-Update` exit 0, count=57.
7. **Checkpoint tool build** to `tools/Antiphon.Checkpoints/bin-c1409t/`: `BUILD SLOT granted lease=9ca77c32-fb77-4c25-b261-28d6c70b46d2 waited=0s`, held 4 s.

## Checkpoint (the one CP-1 run; `start --plan <plan> --rows CP-1 --expected-source-sha 3245dee42...`, `wait --max-wait 540s` twice, exit 75 then 0; server2 (linux), `runner-defaults` revision 2 names server2 globally, `session-runners` lists server2 available, no `-Runner`/`-Platform` pin)

```
CHECKPOINT CP-1 commit=3245dee4216fdc5ec3944f6c93b2631c46b6dc77 build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=82 passed=82 failed=0 skipped=0 trx=/work/worktrees/task-8e3c334d/.antiphon/checkpoints/20261022-083845-026f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3245dee4216fdc5ec3944f6c93b2631c46b6dc77 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=264.6460257s startup=60.3847817s testsWall=524.5346274s teardown=2.7354252s hostWall=587.65503s
unlisted: none (the tool ran no other build or test command)
wall: 14m28s  sequential-equivalent: 9m48s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=3245dee4216fdc5ec3944f6c93b2631c46b6dc77 rows=1
```

Both validators (`Antiphon.Checkpoints validate --evidence report.json --expected-source-sha 3245dee42...` and `scripts/validate-checkpoint-receipt.ps1 -Evidence report.json -ExpectedSourceSha 3245dee42...`) exit 0. TRX by prefix: C1409 26/26, C1400 10/10, C1390 3/3, C1385 1/1, C1380 42/42 (counters total=82 executed=82 passed=82 failed=0). `C1409_EscapeInExpandedTextIsDecodedOnce`, `C1409_NormaliserAndItemOperationTablesHaveWitnesses` and `C1385_RegistryMatchesNarrowGraph` Passed. Run id `20261022-083845-026f`. Repetitions: one normal CP-1 run; no loaded repetition.

## Invariants

V-28 and V-29 pass (CP-1; red first at `aebc26cb3`, run 2). V-7 passes (CP-1 and run 5). V-1..V-5, V-8..V-27 pass (CP-1). No R-n in the plan. No new PC rows; existing land PCs stay pending for SourceLanding Mutation (nothing discharged here).

## Landing checks

- `git merge-tree --write-tree origin/master HEAD` at `3245dee42` (master `d57f4bc7b504b542314c819417774df57a7fbd55`, which contains `bb43ec352`): exit 0, no conflicts, tree `211637d5bc9960005cfcc8c1d488832982c0ac2c`. Nothing merged.
- Cherry-pick of the 28 commits `9b39df4e0..3245dee42` (`-x`) onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`) in a scratch detached worktree: exit 0, no conflicts; the script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch; scratch removed and pruned, nothing pushed.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef 3245dee42`: commits=28 entries=2 violations=0; `-BaseRef fd20e8dcc`: commits=4 entries=0 violations=0.
- The plan file is absent from `origin/master`, so it was amended in place.

## Out-of-contract residue (explicit, D-18/D-27/D-32/D-35)

- Task parameters (`Exec`, `Copy`, `ReadLinesFromFile`, the `MSBuild` task's `Properties`): not evaluated.
- `Condition` text: not evaluated; every value counts.
- Environment-variable properties: a `$(Name)` no tracked definition file may set is read as one opaque segment in a file-reading function tail (the real `$(CleanFile)`); elsewhere an undefined property refuses.
- Windows reserved device names (`CON`, `NUL`): they select no tracked file.
- A symlink a `core.symlinks=false` checkout materialises as a text blob.
- Escaped wildcards: MSBuild reads `%2A`/`%3F` literally; the audit over-approximates (include as a glob, `Exclude` part dropped), the conservative direction, stated in D-33 and N-25.
- An `Update` part's path refusal still prints `Include=<text>` (existing message shape; metadata and operation refusals print `Update=`).

## Residue and cleanup

`bin-c1409r6/` (28 directories) deleted before the checkpoint; `bin-c1409t/` (tool and `src/Antiphon.HostCleanup`) deleted; CP-1 `bin-c1409/` deleted by the tool; scratch worktrees removed and pruned; the MSBuild probe lives under the session scratchpad. `.antiphon/c1409r6-red`, `-green`, `-green2` TRX and the checkpoint run folder retained (ignored). Tree clean apart from this report. Budget: about 95 minutes against 50 (reading the 62 KB plan and 84 KB script, the MSBuild probe, the N-18 witness rerun and the 15-minute checkpoint).
