# Code 18ac0f5a: CARD-1409 repair 7, the D-25 per-part reference skip (Review 7db88932 R1)

Outcome: done. Branch `feat/card-task-18ac0f5a` (worktree `/work/worktrees/task-18ac0f5a`, server2), fast-forward from `ad13d6cd36018d2eaedc39b80f99d5612fefd27c` (the Code 8e3c334d tip); this report is committed before the one CP-1 checkpoint run, so the pushed tip is itself the tested source and the CP-1 receipt for it is in the task's final message and under `.antiphon/checkpoints/<run>/` (ignored), not in this file. Original Code task / landing owner: this task (18ac0f5a-6638-401b-9799-3437c2099980) lands as its own owner per the brief; the whole CARD-1409 chain (32 commits from `9b39df4e0` to `fc277d752`, plus this report commit) lands with it, master first, then the rc port. Plan: `docs/superpowers/plans/2026-10-22-card-1409-import-fail-closed-plan.md` (Repair 7 section: D-36..D-38, S23..S25, V-30; CP-1 after S25 expecting 83). CARD-1483 (well-known metadata, pre-existing) was not touched.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `9c4b88befcb3e51ce21c003b709fb02d61f74979` | S23 red witness `C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart`; plan Repair 7 section; CP-1 moved to S25 expecting 83 (Covers adds V-30) |
| `01aa9add4aacc06ca671469cc5400315c64207d7` | S24 fix in `scripts/audit-land-verification-graph.ps1` (D-36) |
| `fc277d752e639c76e777ec8e5f87ee87ce50d537` | S25 wording: script header, `docs/orchestration-loop.md` |
| (this commit) | this report |

## What changed (plan D-36)

- `Assert-PropertyValuesAllowed`: the line `if (($value.IndexOf('@(') -ge 0) -or ($value.IndexOf('%(') -ge 0) -or ($value.IndexOf('$([') -ge 0)) { continue }` that skipped the WHOLE value is gone. Inside `foreach ($spec in (Resolve-ItemPathText $value $fileDir $projectDir $true))` the same three-way test now runs on `$spec.Source` (the part's escaped text) before the `Kind -ne 'repo'` skip, so a part carrying an item, metadata or property-function reference is skipped on its own and every other part is still classified, as at `fd20e8dcc`. Nothing else in the function changed: the file-reading function rule, the wildcard skip, the separator-or-document gate and the folded docs/README refusal are as repair 6 left them. Seven lines added, three removed; no other file of the script changed except the header sentence (S25).
- No per-example patch elsewhere.

## Witness (plan D-37, V-30)

`C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart` (27th `C1409_*` method): the real test project tree (V-20 scratch tree) is approved unmodified; with `<PackageReadmeFile>../../docs/guide.md;@(None)</PackageReadmeFile>`, `...;$([System.String]::Copy(''))` or `...;%(None.Identity)` appended it refuses naming `tests/Antiphon.Tests/Antiphon.Tests.csproj PackageReadmeFile -> docs/guide.md`; a fixture refuses the reference-first `@(None);../docs/guide.md` and `%(None.Identity);../docs/guide.md` naming `server/App.csproj PackageTags -> docs/guide.md`; `@(None)`, `%(None.Identity)`, `$([System.String]::Copy('../docs/guide.md'))` and `@(None);src/x.json` are approved with `count=1`.

Every outcome was confirmed first with the `fd20e8dcc`, `ad13d6cd3` and fixed scripts on a scratch fixture (script-only `pwsh`, no build or test driver): the four two-part spellings exit 1 `-> docs/guide.md` at `fd20e8dcc` and with the fix, exit 0 count=1 at `ad13d6cd3`; the four controls exit 0 count=1 at all three with identical digests. Two extra shapes with the fix: `$(MSBuildThisFileDirectory)../docs/guide.md;@(None)` refuses `-> docs/guide.md`; `@(None);$(Undefined)/x.json` is approved (the unresolved part is still skipped).

## Runs (unlisted, each with its reason; every build or test driver through `scripts/build-slot.ps1`)

1. **Red proof at `9c4b88bef`** (reason: red proof before the fix, S23). Build `tests/Antiphon.Tests --property:OutputPath=bin-c1409r7/ -p:UseAppHost=false`: `BUILD SLOT granted lease=62b5ca02-e162-4376-9241-de1ece3deffd waited=60s maxcpucount=6`, held 513 s, Build succeeded. Run `dotnet tests/Antiphon.Tests/bin-c1409r7/Antiphon.Tests.dll --treenode-filter '/*/*/LandNonCodeSkipTests/C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart'`: `BUILD SLOT granted lease=323b352f-710f-4b86-b3f8-8c397e6d7d39 waited=15s`, held 21 s; TRX counters total=1 executed=1 passed=0 failed=1 skipped=0; `ShouldAssertException: ran.Code should be 1 but was 0` (the real test project with `../../docs/guide.md;@(None)` was approved). TRX `.antiphon/c1409r7-red/red.trx`.
2. **Green subset at `01aa9add4`** (reason: catch a value-rule regression before the checkpoint build, S24; same dll, the tests copy the script from the repo at run time). Filter `(C1409_PropertyValuePart*)|(C1409_PropertyValueNaming*)|(C1409_NormaliserAndItem*)`: `BUILD SLOT granted lease=3956ac8c-4202-4c84-9994-627722e21cdd waited=30s`, held 239 s; TRX counters total=3 executed=3 passed=3 failed=0 skipped=0 (`C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart`, `C1409_PropertyValueNamingDocsRefuses`, `C1409_NormaliserAndItemOperationTablesHaveWitnesses`). TRX `.antiphon/c1409r7-green/green.trx`.
3. **Real-repo acceptance (D-38, V-7)** at `01aa9add4`: `pwsh scripts/audit-land-verification-graph.ps1 -Check` prints `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0 (16 s). Script: 0 non-ASCII bytes; `Parser::ParseFile` 0 errors; the added lines contain no `??`, `&&`, `||` or `?.` (checked at `01aa9add4` and again at `fc277d752`).
4. **Ordinary-edit acceptance (D-27/D-38)** at `01aa9add4` in a scratch detached worktree (discarded): `tests/Antiphon.Tests/Antiphon.Tests.csproj` plus `PackageReference Humanizer.Core`, linked `Compile ..\..\server\Program.cs` and `ProjectReference ..\..\tools\Antiphon.Checkpoints\Antiphon.Checkpoints.csproj ReferenceOutputAssembly="false"`: `-Update` exit 0, count=57.
5. **Checkpoint tool build** `tools/Antiphon.Checkpoints --property:OutputPath=bin-c1409t/`: `BUILD SLOT granted lease=3713b6e0-dc94-4f98-822b-44f0e817ab4b waited=135s`, held 50 s.
6. Script-only `pwsh` probes of item 'Witness' above: no dotnet build or test driver, no slot.

Repetitions: one red run, one green subset run, one CP-1 run (below); no loaded repetition.

## Checkpoint

The one CP-1 run (`start --plan <plan> --rows CP-1 --expected-source-sha <this tip>`, then `wait --max-wait 540s` until the exit is not 75; server2 (linux): `GET /api/runner-defaults` revision 2 names server2 globally, `GET /api/session-runners` lists server2 available (capacity 10) and desktop available; no `-Runner`/`-Platform` pin) is started after this commit. Its `CHECKPOINT` line, `verdict` and `CHECKPOINT SOURCE VALID` line, the TRX prefix counts (expected C1409 27, C1400 10, C1390 3, C1385 1, C1380 42 = 83) and both validator exits are in the task's final message; the receipts are under `.antiphon/checkpoints/<run>/` in this worktree.

## Invariants

V-30 is red first at `9c4b88bef` (run 1) and green at `01aa9add4` (run 2) and in CP-1. V-7 passes (run 3 and CP-1). V-1..V-5, V-8..V-29 pass in CP-1 (every ordinary V-n of the plan; no R-n in the plan). No new PC rows; existing land PCs stay pending for SourceLanding Mutation (nothing discharged here).

## Landing checks (at `fc277d752`; re-run at the final tip in the final message)

- `git merge-tree --write-tree origin/master fc277d752` (master `8fce73036a83ac817e4ba118801ff936f690103c`, which contains `cbb73f50f`): exit 0, no conflicts, tree `dbc1e66489b04bd4aa0a0fabaabda5b8511984ad`. Nothing merged. The plan file is absent from `origin/master`, so it was amended in place (no add/add).
- Cherry-pick of the 32 commits `9b39df4e0..fc277d752` (`-x`, in order) onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`) in a scratch detached worktree: exit 0, 32 applied, no conflicts; the script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch; scratch removed and pruned, nothing pushed. (A first attempt that wrongly included the master base commit `9b39df4e0` itself conflicted on a CARD-1263 doc; that commit is not part of the chain.)
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef fc277d752`: commits=32 entries=3 violations=0; `-BaseRef ad13d6cd3`: commits=3 entries=0 violations=0.

## Out-of-contract residue

Unchanged from D-35 (task parameters, `Condition` text, environment-variable properties with `$(CleanFile)` read as one opaque segment, Windows reserved device names, symlink-as-text, escaped wildcards over-approximated, the `Include=` message shape for an `Update` part). CARD-1483 (well-known item metadata read as empty in a batched `%(Item.Meta)`) is pre-existing and untouched.

## Residue and cleanup

`bin-c1409r7/` (27 directories) deleted before the checkpoint; `bin-c1409t/` (tool and `src/Antiphon.HostCleanup`) deleted after `validate`; CP-1 `bin-c1409/` deleted by the tool on green; scratch worktrees removed and pruned; the probe fixtures live under the session scratchpad. `.antiphon/c1409r7-red`, `-green` TRX and the checkpoint run folder retained (ignored). Budget: about 50 minutes against 30 (two slot waits plus a 513 s build on a loaded host).
