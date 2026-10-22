# Code d8dc3f5c: CARD-1409 repair 9, a definition path with a character MSBuild escapes (Review 4ecdb753 R3)

Outcome: done. Branch `feat/card-task-d8dc3f5c` (worktree `/work/worktrees/task-d8dc3f5c`, server2), fast-forward from `e6ac1c606b26ad49b446b1f5ed0a21e920c2ef8a` (the Code f78d75df tip). This report is committed before the one CP-1 checkpoint run, so the pushed tip is itself the tested source. The CHECKPOINT line and validator output are in the task's final message.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `a939b3ecdbb7a57b8fad4d77cbfeae12b9da78fe` | S29: red witness `C1409_DefinitionPathWithEscapedCharacterRefuses`; plan Repair 9 (D-42..D-44, S29..S31, V-32); CP-1 moved to S31, expecting 85 (Covers adds V-32) |
| `fc931bb855fe5ffe4726c57e056ad98a50f10384` | S30: fix in `scripts/audit-land-verification-graph.ps1` (D-42) |
| `841cfefbbaea5aacfb295e6f904c91ea0525d37c` | S31: wording in `docs/orchestration-loop.md` and the script header |
| (this commit) | this report |

## What changed (plan D-42)

The audit splices only directories of tracked definition paths: a file's own directory for `$(MSBuildThisFileDirectory)`, the project directory for `$(MSBuildProjectDirectory)` and for relative paths, and every importing project's directory. Every import target and project-reference target must also be a tracked definition path.

The fix adds one check to the ls-tree loop, after the existing canonical-segment and obj/ checks. A tracked definition path that holds any of `%`, `$`, `@`, `;`, `'`, `(`, `)`, `*` or `?` is refused by name before any file is read. These are the characters MSBuild escapes in `$(MSBuildProjectDirectory)` and leaves as written in `$(MSBuildThisFileDirectory)`. The message is `definition file path holds a character MSBuild escapes in $(MSBuildProjectDirectory), so the audit cannot read a directory spliced from it; -Update cannot approve it: <path> char=<c>`.

- MSBuild escaping is not re-implemented anywhere.
- `Resolve-ItemPathText`, `Expand-Candidates` and all consumers are unchanged.
- Every one of the 57 tracked definition paths matches `[A-Za-z0-9._/-]`, so `-Check` is unchanged.
- The set covers all of MSBuild's escapable characters, not only `%`. The sweep found fd20e8dcc-refused / tip-approved shapes for `$(` and `@(` folders as well. A single rule closes every class, and `*`/`?` folders that both versions approved are now refused too (fail-closed).

## Witness (plan D-43, V-32)

`C1409_DefinitionPathWithEscapedCharacterRefuses` is the 29th `C1409_*` method. These shapes refuse naming `<path> char=<c>`:

- `server/a%2Fb/App.csproj` with `PackageReadmeFile` `$(MSBuildProjectDirectory)/../../docs/guide.md`
- `server/a%2Fb/App.csproj` with `x.json;$(MSBuildProjectDirectory)/../../docs/guide.md`
- `server/a%2fb/App.csproj` with the first form
- `server/a$(Description)b/` and `server/a@(None)b/` with the first form
- an imported `eng/a%2Fb/shared.props`, reached as `Import Project="../eng/a%252Fb/shared.props"`

Controls, each keeping its fd20e8dcc outcome:

- `server/ab/c/App.csproj` with `$(MSBuildProjectDirectory)/../../docs/guide.md` is approved with count=1. It reads `server/docs/guide.md`.
- The `$(MSBuildThisFileDirectory)../../docs/guide.md` form in `server/a%2Fb/` refuses. fd20e8dcc refused it as a value naming docs. e6ac1c606 approved it, and real MSBuild reads `server/docs/guide.md` (Review 4ecdb753), so that refusal is fail-closed.

Every outcome was confirmed first against the fd20e8dcc and e6ac1c606 scripts on scratch fixtures (script-only `pwsh`, no driver). At those two scripts, the first five shapes refuse/approve and approve/approve respectively. The imported-file shape is approved at both.

## Runs (unlisted, each with its reason; every build or test driver went through `scripts/build-slot.ps1`)

1. **Red proof at `a939b3ecd`** (reason: red proof before the fix, S29).
   - Build: `tests/Antiphon.Tests --property:OutputPath=bin-c1409r9/ -p:UseAppHost=false`, `BUILD SLOT granted lease=f19e148a-c3d5-402c-90c2-82705083f264 waited=256s maxcpucount=6`, held 246 s, Build succeeded.
   - Run: `dotnet tests/Antiphon.Tests/bin-c1409r9/Antiphon.Tests.dll --treenode-filter '/*/*/LandNonCodeSkipTests/C1409_DefinitionPathWithEscapedCharacterRefuses'`, `BUILD SLOT granted lease=a8cf583b-0474-4871-ba60-685d48a328bd waited=165s`, held 3 s.
   - TRX counters: total=1 executed=1 passed=0 failed=1. The failure is `ShouldAssertException: ran.Code should be 1 but was 0`: `server/a%2Fb` was approved. TRX: `.antiphon/c1409r9-red/red.trx`.
2. **Checkpoint tool build** (reason: the tool runs CP-1): `tools/Antiphon.Checkpoints --property:OutputPath=bin-c1409r9t/`, `BUILD SLOT granted lease=c8f7233b-64df-4ffd-b754-60096240c0df waited=30s`, held 7 s.
3. **Real-repo acceptance (D-44, V-7)** at `fc931bb85`: `-Check` printed `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0. Script checks at `841cfefbb`: 0 non-ASCII bytes, and `Parser::ParseFile` reports 0 errors. The added lines use no `??`, `&&`, `||` or `?.`. The added orchestration-loop lines are ASCII.
4. **Ordinary-edit acceptance (D-27/D-44)** at `841cfefbb`, in a scratch detached worktree (discarded). The edits to `tests/Antiphon.Tests/Antiphon.Tests.csproj` were `PackageReference Humanizer.Core`, a linked `Compile ..\..\server\Program.cs` and `ProjectReference ..\..\tools\Antiphon.Checkpoints\Antiphon.Checkpoints.csproj ReferenceOutputAssembly="false"`. After committing them, `-Update` exited 0 with `digest=5562F875FB78C91A2A928A78337C5453A77A34ABE6335D3E8873053CD32706F0 count=57`.
5. **Differential sweep (D-44)**: script-only `pwsh`, no slot. 22 folder classes x 15 directory-splice shapes x {fd20e8dcc, e6ac1c606, fix at `fc931bb85`}, 990 runs.

Repetitions: one red run and one CP-1 run. There was no green subset run and no loaded repetition.

## Differential sweep

Each fixture has a tracked `docs/guide.md`. The folder `D` holds the definition file. Shapes, in column order:

- A, the project is at `server/D/App.csproj`:
  1. `PackageTags $(MSBuildProjectDirectory)/../../docs/guide.md`
  2. the same with `x.json;` before it
  3. `$(MSBuildThisFileDirectory)../../docs/guide.md`
  4. the same with `x.json;` before it
  5. EmbeddedResource `$(MSBuildProjectDirectory)/../../docs/guide.md`
  6. EmbeddedResource `$(MSBuildThisFileDirectory)../../docs/guide.md`
  7. EmbeddedResource `../../docs/guide.md`
  8. Import `../../eng/evil.props`
  9. Import `$(MSBuildProjectDirectory)/../../eng/evil.props`
  10. Import `$(MSBuildThisFileDirectory)../../eng/evil.props`

  `eng/evil.props` sets `PackageTags $(MSBuildProjectDirectory)/../../docs/guide.md`.
- B, `server/App.csproj` imports `$(MSBuildThisFileDirectory)../eng/D/evil.props`, and that file holds:
  1. `PackageTags $(MSBuildThisFileDirectory)../../docs/guide.md`
  2. the same with `x.json;` before it
  3. `PackageTags $(MSBuildProjectDirectory)/../docs/guide.md`
  4. EmbeddedResource `$(MSBuildThisFileDirectory)../../docs/guide.md`
- C, `server/D/Directory.Build.props` sets `PackageTags $(MSBuildProjectDirectory)/../../docs/guide.md`.

`A` = approved (`-Update` exit 0), `R` = refused for another reason, `E` = refused by the D-42 rule. `a/b` is a real two-level folder, the control.

| folder | fd20e8dcc | e6ac1c606 | fix |
|---|---|---|---|
| `ab` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | RRRRRRRAAARRARR |
| `a/b` | AAAARRRRRRAAARA | AAAARRRRRRAAARA | AAAARRRRRRAAARA |
| `a%2Fb` | RRRRRRRAAARRRRR | AAAARRRARRRRRRA | EEEEEEEEEEEEEEE |
| `a%2fb` | RRRRRRRAAARRRRR | AAAARRRARRRRRRA | EEEEEEEEEEEEEEE |
| `a%41b` | RRRRRRRAAARRRRR | RRRRRRRAAARRRRR | EEEEEEEEEEEEEEE |
| `a%25b` | RRRRRRRAAARRRRR | RRRRRRRAAARRRRR | EEEEEEEEEEEEEEE |
| `a;b` | RRRRRRRAAARRRRR | RRRRRRRAAARRRRR | EEEEEEEEEEEEEEE |
| `a$b` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | EEEEEEEEEEEEEEE |
| `a$(Description)b` | RRRRRRRARRRRRRR | AAAARRRARRRRRRA | EEEEEEEEEEEEEEE |
| `a@b` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | EEEEEEEEEEEEEEE |
| `a@(None)b` | RRRRRRRARRRRRRR | AAAARRRARRRRRRA | EEEEEEEEEEEEEEE |
| `a(b)` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | EEEEEEEEEEEEEEE |
| `a'b` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | EEEEEEEEEEEEEEE |
| `a*b` | AAAARRRRRRRRRRA | AAAARRRRRRRRRRA | EEEEEEEEEEEEEEE |
| `a?b` | AAAARRRRRRRRRRA | AAAARRRRRRRRRRA | EEEEEEEEEEEEEEE |
| `a,b` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | RRRRRRRAAARRARR |
| `a b` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | RRRRRRRAAARRARR |
| `a[b]` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | RRRRRRRAAARRARR |
| `aé` | RRRRRRRAAARRARR | RRRRRRRAAARRARR | RRRRRRRAAARRARR |
| `a.` | RRRRRRRRRRRRRRR | RRRRRRRRRRRRRRR | RRRRRRRRRRRRRRR |
| `a ` | RRRRRRRRRRRRRRR | RRRRRRRRRRRRRRR | RRRRRRRRRRRRRRR |
| `%2E%2E` | RRRRRRRAAARRRRR | AAAARRRARRRRRRA | EEEEEEEEEEEEEEE |

Summary:
- **Refused at fd20e8dcc and approved by the fix: none of the 330 shapes.**
- Refused at fd20e8dcc and approved at e6ac1c606, now refused by D-42:
  - `a%2Fb`, `a%2fb` and `%2E%2E`: A1-A4 and C. These are R3 and its spellings.
  - `a$(Description)b` and `a@(None)b`: A1-A4 and C. These are new findings of this sweep, closed in this round.
- Approved at fd20e8dcc, refused by the fix (fail-closed): every shape in a folder holding an MSBuild-escaped character. That includes the `*`/`?` folders' A1-A4 and C, which both earlier versions approved.
- Unchanged: `,`, space, `[`, `]`, non-ASCII, and trailing dot or space give the same outcome in all three versions. MSBuild does not escape the first five, and the last two were already refused by the Windows-segment rule.
- Disclosure, unchanged in all three versions and outside this repair: the A8-A10 and B3 cells for plain folders (`ab`) are approved. An imported file's property value is resolved from that file's own directory only (repair 4 / D-25 design: "resolved from the file's own directory; an include that uses the value is expanded from every importing project directory"). So `$(MSBuildProjectDirectory)` in an imported `.props` value is read from the `.props` folder. This is not a regression against fd20e8dcc.

## Invariants

- V-32 is red first at `a939b3ecd` (run 1) and is covered green by CP-1.
- V-7 passes (run 3 and CP-1).
- CP-1 covers V-1..V-5 and V-8..V-31. That is every ordinary V-n of the plan. The plan has no R-n.
- No new PC rows. The existing land PCs stay pending for SourceLanding Mutation, and nothing is discharged here.

## Landing checks (at `841cfefbb`; re-run at the final tip in the final message)

- `git merge-tree --write-tree origin/master 841cfefbb` (master `e70a567107f56ffa35a9baa861f208d3ca5eea77`): exit 0, no conflicts, tree `f2c3eab8e5892d98fd3383a8eaf186c42aa8ac64`. Nothing was merged.
- The 40 commits `9b39df4e0..841cfefbb` were cherry-picked (`-x`, in order) onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`) in a scratch detached worktree: exit 0, all 40 applied, no conflicts. The script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch. The scratch worktree was removed and pruned, and nothing was pushed.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef 841cfefbb`: commits=40 entries=5 violations=0. With `-BaseRef e6ac1c606`: commits=3 entries=0 violations=0.

## Out-of-contract residue

Unchanged from D-35/D-38/D-41. The imported-value disclosure above is pre-existing. CARD-1483 (well-known item metadata) is pre-existing and untouched.

## Residue and cleanup

- All 28 `bin-c1409r9/` directories were deleted before the checkpoint. `bin-c1409r9t/` (`tools/Antiphon.Checkpoints`, `src/Antiphon.HostCleanup`) runs the checkpoint tool and is deleted after `validate`.
- Scratch worktrees were removed and pruned. Sweep fixtures live under the session scratchpad.
- The `.antiphon/c1409r9-red` TRX and the checkpoint run folder are kept (ignored).
