# Code e295bb13: CARD-1409 repair 5, one path normaliser and the item-operation table (Review f0fb9035)

Outcome: done. Tip `c2a6398c771ad2979369e12d44fd4c8ec6079a8e` on `feat/card-task-e295bb13` (worktree `/work/worktrees/task-e295bb13`, server2), fast-forward from `5471da833f9e4b4b1cf08dd4bbd70584d67220f8`, plus this report commit. Original Code task / landing owner: this task (e295bb13-3356-47b9-b725-0804682827b8) lands as its own owner per the brief; the whole CARD-1409 delta (23 commits from `9b39df4e0`) lands with it, master first, then the rc port. Plan: `docs/superpowers/plans/2026-10-22-card-1409-import-fail-closed-plan.md` (Repair 5 section: D-28..D-32, N-1..N-15, O-1..O-22, S17..S19, V-24..V-27). Evidence: `.antiphon/checkpoints/20261022-070324-6180/`.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `c011d2964b4b7def66f772265ab1ec51f9ef671d` | S17 red witnesses `C1409_WindowsPathNormalisationRefusesBySegment`, `C1409_ItemUpdateMetadataFeedsTheCensus`, `C1409_NormaliserAndItemOperationTablesHaveWitnesses`, `C1409_TrackedSymlinkUnderItemPatternRefuses`; the V-20 fixture factored into `BuildRealTestProjectTreeAsync`; plan Repair 5 section with the N- and O-tables |
| `c0706a5b465e3727b4b775ae9d6c14a38cde4d0c` | S18 fix in `scripts/audit-land-verification-graph.ps1` (D-28..D-30) |
| `7804603ce8fbf4c279d3274ed3e6982dc2abaa56` | S18 follow-up: the N-5 `ProjectReference` witness references an untracked `LIB~1.csproj` (a tracked one is refused earlier as a definition path, N-15); assertion unchanged |
| `c2a6398c771ad2979369e12d44fd4c8ec6079a8e` | S19 wording: script header, `docs/orchestration-loop.md`, plan CP-1 row (after S19, expects 81, covers V-24..V-27) |

## What changed (plan D-28..D-30)

- **D-28, one normaliser.** `ConvertTo-CanonicalPath` replaces `Normalize-RepoPattern` and is the only way resolved text becomes a repository path. Every comparison consumes its result: include/update parts, `Exclude` parts, `Import` targets, `ProjectReference` targets, literal property-value paths, output/package-root tails, every tracked match a wildcard selects, and every tracked definition path (so `server/obj./x.props` no longer slips past the `obj/` rule). Per segment, before anything else: trailing `.`/space, only-dots-or-spaces, any `:` (the drive prefix is converted or refused as outside first, so it is the only legal colon), and `~`+digit (8.3 short name) REFUSE naming the segment and reason; `.`/empty segments drop and `..` pops only afterwards (`outside`/`escape` as before); letter case is kept and every comparison is ordinal-ignore-case. Refusal text: `definition file path has a segment the Windows land host would normalise; -Update cannot approve it: <file> <Item> Include=<part> segment=<seg> (<reason>)`.
- **Property values fold instead of refusing (D-28 last clause).** The first `-Check` on the real tree refused `src/Antiphon.Messaging/Antiphon.Messaging.csproj Description` ("Wire contracts (ChannelMessage/ChannelReply) for the Antiphon messaging bridge." has a `/` and ends in `.`). A value is free text, not an input (D-25), so there the normaliser runs in fold mode: it reads the segment as the host would (drop trailing dots/spaces, cut at the colon) and refuses only when that reading is `docs/` or the root README, naming the divergent segment; a `~1` segment still refuses. Includes refuse outright.
- **D-29, the O-table.** `Update` elements join `ItemDeclarations` (F2), so `%(StagedHelper.Glob)` sees an `Update`'s `Glob` and a `PackageReference Update` with `GeneratePathProperty` generates its `Pkg*` root; an `Update` also counts as a declaration for the `@(Item->'%(RootDir)%(Directory)')` build-product rule. `Assert-ItemOperationsAllowed` (was `Assert-ItemMetadataAllowed`): an element with no `Include`/`Update`/`Remove` refuses `(none)`; `KeepMetadata`, `RemoveMetadata`, `KeepDuplicates`, `MatchOnMetadata`, `MatchOnMetadataOptions` refuse by name (`$RefusedItemOperations`); any metadata on a `Remove` refuses; every other attribute is metadata under the per-type allow-list. `Exclude` parts go through the normaliser (divergent segment refuses, naming `Exclude=<part>`), subtract only, unresolvable parts dropped.
- **D-30, symlinks.** `ls-tree` mode `120000` rows are recorded; `Find-SymlinkReached` (prefix-mode `Test-GlobWalk`) refuses a path item whose canonical pattern selects the link or passes through it as a directory, naming the link.

## Runs

Unlisted runs, each with its reason:

1. Red proof at `c011d2964` (reason: red proof before the fix). Build `tests/Antiphon.Tests --property:OutputPath=bin-c1409r5/ -p:UseAppHost=false` under build-slot: `BUILD SLOT granted lease=60e334d5-485a-41c5-8755-508159036b9b waited=0s`, held 275s, 0 errors. Run filter `(C1409_WindowsPath*)|(C1409_ItemUpdate*)|(C1409_NormaliserAnd*)|(C1409_TrackedSymlink*)`: `BUILD SLOT granted lease=270fc9d9-fe9a-4140-8253-95a1b9de05ae waited=15s`; total 4, failed 4, skipped 0. Three failures `ran.Code should be 1 but was 0` (the audit approved the Review's shapes, the real-project `StagedHelper Update`, and the symlink); the table case failed at `N-1` because `../docs./guide.md` was refused as a plain document, not by segment. TRX `.antiphon/c1409r5-red/red.trx`.
2. Green subset at `c0706a5b4` (reason: catch a fixture regression before the checkpoint build; same dll, the test copies the script from the repo root at run time): filter `(C1409_*)|(C1400_*)|(C1390_*)`, `BUILD SLOT granted lease=6296b77d-5d08-4300-a54d-5d7b04d41e1e waited=0s`; total 38, failed 1: `C1409_NormaliserAndItemOperationTablesHaveWitnesses` row N-5, whose fixture tracked `lib/LIB~1.csproj` and so hit the definition-path refusal (`lib/LIB~1.csproj segment=LIB~1.csproj (8.3 short name)`) before the `ProjectReference` rule. Fixture changed to an untracked reference (`7804603ce`), re-proven by running the script on a hand-built fixture repo (`server/App.csproj ProjectReference Include=../lib/LIB~1.csproj segment=LIB~1.csproj (8.3 short name)`, exit 1), then by CP-1. TRX `.antiphon/c1409r5-green/green.trx`.
3. Real-repo acceptance (D-32, V-7): `pwsh scripts/audit-land-verification-graph.ps1 -Check` at `c2a6398c7` prints `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0 (5.6 s). Script parses (`Parser::ParseFile`, 0 errors) and has 0 non-ASCII bytes.
4. Ordinary-edit acceptance in a scratch detached worktree at `c2a6398c7` (discarded): `tests/Antiphon.Tests/Antiphon.Tests.csproj` plus a new `ProjectReference` (`ReferenceOutputAssembly="false"`), a new `PackageReference` and a new linked `Compile`; `-Update` exit 0, count=57.

Checkpoint (the one CP-1 run; tool built under build-slot to `bin-c1409t/`, then `start --plan <plan> --rows CP-1 --expected-source-sha c2a6398c...`, `wait --max-wait 540s` twice, exit 75 then 0; run on server2 (linux), `runner-defaults` revision 2 names server2 globally, no `-Runner`/`-Platform` pin):

```
CHECKPOINT CP-1 commit=c2a6398c771ad2979369e12d44fd4c8ec6079a8e build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=81 passed=81 failed=0 skipped=0 trx=/work/worktrees/task-e295bb13/.antiphon/checkpoints/20261022-070324-6180/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c2a6398c771ad2979369e12d44fd4c8ec6079a8e sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=423.0942065s startup=60.0729841s testsWall=500.1416613s teardown=4.4324341s hostWall=564.6471052s
unlisted: none (the tool ran no other build or test command)
wall: 16m47s  sequential-equivalent: 9m25s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=c2a6398c771ad2979369e12d44fd4c8ec6079a8e rows=1
```

Both validators (`Antiphon.Checkpoints validate` and `scripts/validate-checkpoint-receipt.ps1`) exit 0. TRX by prefix: C1409 25/25, C1400 10/10, C1390 3/3, C1385 1/1, C1380 42/42 (81 cases, 0 not passed). The four new witnesses and `C1385_RegistryMatchesNarrowGraph` Passed. Run id `20261022-070324-6180`.

## Invariants

V-24..V-27 pass (CP-1; red first at `c011d2964`). V-7 passes (CP-1 and the acceptance run). V-1..V-5, V-8..V-23 pass (CP-1). No R-n in the plan. No new PC rows; existing land PCs stay pending for SourceLanding Mutation.

## Landing checks

- `git merge-tree --write-tree origin/master HEAD` (master `ac9ac4a9ab9680c4474dc1a6dfd56532c33a2e4e`): exit 0, 0 conflicts, tree `e229bbc53efa044f596d0799e365617f978f05b7`. Nothing merged.
- Cherry-pick of all 23 CARD-1409 commits (`9b39df4e0..c2a6398c7`, `-x`) onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`) in a scratch detached worktree: exit 0, no conflicts; the script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch; scratch removed, nothing pushed.
- `scripts/check-evidence-diff.ps1`: `9b39df4e0..HEAD` commits=23 entries=1 (the earlier committed report) violations=0; `5471da833..HEAD` commits=4 violations=0.
- The plan file is absent from `origin/master` and `origin/rc/2026-10-20`, so it was amended in place.

## Deviations and residue, stated

- Property values use the normaliser's fold mode (above), because the directive's outright refusal would have refused a real prose `Description` and every future description ending in a period; the fold still refuses a divergent docs/README reading by segment (N-14).
- An `Update` part's path refusal still prints `Include=<part>` (existing message shape; O-4/O-5 rows say so). Metadata and operation refusals print `Update=`.
- Out of contract, as the plan records (D-32): task parameters, `Condition` text, environment-variable properties, Windows reserved device names (`CON`, `NUL`: they select no tracked file), a symlink that a `core.symlinks=false` checkout materialises as a text file (the digest then sees a regular blob the item selects; a `.md` one is a document and refuses by the existing list).
- The table-driven case reads the plan from the repository root; the rc port carries the plan (the whole chain cherry-picks, plan commit included).
- Budget: about 85 minutes against 60; the two reruns (prose fold, N-5 fixture) and the 17-minute checkpoint took the extra time.

## Residue

`bin-c1409r5/` (27 directories) and `bin-c1409t/` deleted; CP-1 `bin-c1409/` deleted by the tool; scratch worktrees removed and pruned; `.antiphon/c1409r5-red`, `.antiphon/c1409r5-green` TRX and the checkpoint run folder retained (ignored). Tree clean apart from this report.
