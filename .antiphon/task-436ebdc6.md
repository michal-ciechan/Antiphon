# Review 436ebdc6: land-graph canary placeholders for ignored item files (round 1 of 3, full)

Subject: `feat/card-task-aeecbb03` tip `7f12e2d1bbbf1a6451e22346b49f9c92ac368e62`. The base is master `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c` (`git merge-base` = 7dc9e1168, not 907edaebb). Commits reviewed: 9b9796e6c (red witness), 269ce400e (fix), 7f12e2d1b (note). Code landing owner: aeecbb03.

**Verdict: one defect (D-1). The placeholder can hide a real docs dependency. Do not land as is.**

## D-1: a placeholder restores an ignored file's *existence* in pass 2, so a docs item gated on that file's absence is approved

- **Where:** `tools/Antiphon.LandGraphAudit/CanaryRepository.cs:98-107` and `IgnoredItemFiles` at `:119-133`, fed by `EvaluationSnapshot.ItemPaths`.
- **Failure:** a committed definition can include a docs file only when an ignored local file is absent. The author's tree, or the Windows rc checkout, has that file. Pass 1 then sees no docs item. Pass 2 now gets a placeholder at the same path, so the gate stays closed there too. Both passes match and the audit prints `approved`. A clean checkout, which is what the land builds, has no ignored file. There the docs item is evaluated, and the same tool refuses it.

  Probes (tool run directly on scratch git repos, `.gitignore` = `App/TestOutput/` or `bin-*/`):

  | Fixture | tip 7f12e2d1 | 9b9796e6c tool | tip, ignored file removed |
  |---|---|---|---|
  | `<None Include="../docs/a.md" Condition="!Exists('TestOutput/flag.local')"/>` + ignored `App/TestOutput/flag.local` | **approved** | refused: differential canary evaluation refused (docs None) | refused: `App/App.csproj None …/docs/a.md` |
  | same, with `ItemGroup Condition=` | **approved** | refused | refused |
  | `<Content Include="../docs/a.md" CopyToOutputDirectory="PreserveNewest"/>` under `ItemGroup Condition="!Exists('bin-x/App.dll')"` + ignored `App/bin-x/App.dll` | **approved** | refused | — |

- **Why it matters:** before this fix the differential was the only check that caught a graph depending on the existence of an ignored file. `EvaluationExpressionPolicy` admits `Exists()` on non-docs paths. The recorder only refuses probes of docs and README paths. Ignored output (`bin-<name>/`, `TestOutput/`) is routinely present in checkouts. An `!Exists()`-gated docs dependency therefore passes on exactly the machines the rc runs on.
- **Fix (suggested):** keep pass 2 free of ignored files. In `EvaluationSnapshot.Difference`, discount only the `item` rows that are in pass 1 and missing from pass 2, where the FullPath is an existing, non-link, in-repo file outside the copy set (the same test `IgnoredItemFiles` already does). Every other row, including properties, stays compared. The C1483 witness then still approves. The gated cases above still refuse, because pass 2 evaluates the docs item and the canary pass refuses it. Alternative: refuse `FileExists`/`Exists` probes of untracked paths outside glob traversal in `RecordingFileSystem`. That also closes the existing untracked gap below. Add a witness for the `!Exists()`-gated docs item with the ignored gate file present (red at tip, green after the fix).
- **Owner:** Code, aeecbb03.

The gap already existed for **untracked, not ignored** files. With the same fixture and the flag untracked but not ignored, both the 9b9796e6c tool and the tip approve, because the canary copies untracked files. `Assert-HeadSource` (`git diff --quiet HEAD`) allows them. This fix extends that gap to every ignored item file. Worth a card of its own; it is not a blocker for this subject.

## Soundness checks that held

- **Content is out of scope by design.** Rows are item `(type, FullPath)`, evaluated properties and imports, never content. `EvaluationExpressionPolicy` refuses filesystem property functions (only lexical members are admitted). Evaluation therefore cannot read an item's bytes, and an empty placeholder cannot change any row through its content. Compile, EmbeddedResource and CopyToOutput items with ignored paths are compared by name only, as before.
- **Only files pass 1 listed.** Placeholders come only from pass-1 `AllEvaluatedItems` FullPaths that `File.Exists` in the repo (not a directory, not a reparse point) and are not in the copy set. Mutants confirm this: M3 drops `File.Exists`/reparse, so a directory item gets a placeholder and the audit fails. Killed. Placeholders never overwrite: M6 drops the `File.Exists(destination)` guard and the copy-set check, the project file is emptied, and S4 goes red. Killed.
- **Containment.** An item outside the repo is skipped by the ordinal `repo + sep` prefix. M4 removes that skip, and the line-102 guard then throws `canary path outside repository: ../../usr/share/dotnet/...`. Killed. A crafted `../../outside/x.txt` or `$(MSBuildProjectDirectory)/../../outside/f.txt` item approves, and nothing outside is written (md5 unchanged). An ignored directory link to outside the repo approves: the placeholder lands under a real canary directory and the outside files are unchanged. An ignored file symlink is skipped and refuses (fail closed).
- **"Ignored" test.** It is not `git check-ignore`. It is the complement of the existing `git ls-files --cached --others --exclude-standard -z` (one process, NUL-separated stdout, no per-path argv, so no 8191-character limit on Windows). That honours nested `.gitignore`, `.git/info/exclude` and `core.excludesFile`; both nested and info/exclude probes approve. A tracked file matching an ignore pattern is in `--cached`, so it is copied, never replaced by a placeholder.
- **Windows path forms (by reading; nothing ran on the desktop).**
  - `repo` comes from `GetFullPath` + `TrimEndingDirectorySeparator` in `Program`, and MSBuild FullPaths are built from the same solution path, so prefix casing and separators match.
  - The backslash-to-slash replacement is Linux-only. Windows leaves the path native and replaces backslashes with `/` only for the copy-set lookup, matching git's form.
  - Case or index mismatches only cause a redundant `File.Exists(destination)`, which is true on a case-insensitive file system, so nothing is written. Case-insensitive duplicates behave the same way.
  - Reparse points are skipped through `FileAttributes.ReparsePoint`. Junctions in the destination ancestry are walked by `RealPath`/`LinkTarget`.
  - A `\\?\` repo root, or a FullPath that differs in prefix, gets no placeholder and refuses. That is fail closed, the same as before the fix.
  - Long paths: the canary root name is about 41 characters longer than `checkout-8a7dbe6d`. This was already true for tracked files and is handled by .NET.

## Advisories

- **A-1: cost is unmeasured and grows with the number of ignored item files.**
  - Linux scratch repo: 20,000 ignored files take 7.5 s at the tip, against 3.6 s for the base tool, which refuses.
  - Real repo `-Check` under load 20-29: 5,000 files in `tests/Antiphon.E2E/TestOutput/runner/*` approve (34.7 s). 20,000 files give `evaluation-failed: … copy exceeded 5 s` and refuse.
  - This is bounded and fails closed, but the 5 s Linux copy budget and the provisional 68 s Windows copy budget now also cover placeholders. Neither was remeasured (performance standard).
- **A-2: tracked directory links pointing outside the repo now fail with exit 2 (`canary destination escapes through a link`).** Items under such a link are not in the copy set, so they are treated as ignored and hit `CheckDestination`. The base tool approved this case. Antiphon has no tracked links, so this does not affect the rc. Skipping a placeholder whose destination already exists, before `CheckDestination`, would restore the old result.
- **A-3: two guards are unpinned, both equivalent by construction.** M2 (placeholders for copied items too) and M5 (no `CheckDestination` on placeholders) survive the targeted selection. M2 is a no-op because of the no-overwrite guard, and M6 shows that guard is pinned. M5 writes nothing outside: the destination resolves to the same existing file through the same link, and the md5 of the outside files is unchanged.
- **A-4: docs.** The owner doc (`docs/orchestration-loop.md`, land graph section) and the tool header (`Program.cs`: "The tool writes nothing") do not describe the canary copy set or the placeholder rule. Only the `CanaryRepository.Create` doc comment and the investigation note do.

## Runs (server2 Linux, tree at 7f12e2d1b, build-slot leases)

- **Builds:**
  - `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`: 3m57s, 0 errors.
  - `dotnet build tools/Antiphon.LandGraphAudit -c Release -p:UseAppHost=false`: 2.6 s, one lease. One lease per mutant build, with the source restored after each through `git checkout HEAD -- <exact path>`.
- **Classes:** `/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)/*` with ignored `tests/Antiphon.E2E/TestOutput/Logs/run.log` present.
  - 249/249 passed (TRX: LandGraphEvaluationTests 138, LandNonCodeSkipTests 111), 0 skipped, 25m44s.
  - Includes the witness `C1483_IgnoredFileInDefaultGlobApproves` (2.2 s) and the three rc failures: `C1385_RegistryMatchesNarrowGraph`, `C1483_S4_RealRepositoryApproves` and `C1483_S5_CheckRequiresEvaluationRecord`.
- **Witness red at 9b9796e6c:** M0, the tool source at 9b9796e6c, via `ANTIPHON_LANDGRAPH_TOOL_DLL`. The witness and S4 fail, 3 others pass. M1 (no placeholder write): the same 2 fail.
- **Mutant selection:** witness, S4, `C1483_InsideFileAndDirectoryLinksApprove`, `C1483_TrackedDocsLinkRefusesBeforeWrite`, `C1483_SourceRewrittenToLinkBeforeCopyRefuses`.
  - Killed: M0 2/5, M1 2/5, M3 3/5, M4 3/5, M6 1/5 failed.
  - Survived: M2 0/5, M5 0/5.
- **`-Check` through the real script at the tip:** with `run.log` present, `digest=7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22 count=69`, `approved`, exit 0, 46.2 s. The evaluator build takes about 2.6 s. The script does no build or caching of its own; `/work/cache/land-graph-audit/` is not used by the repo.
- **Records:** manifest, evaluation and pins are unchanged by the branch.
- **`check-evidence-diff.ps1 -Repository . -BaseRef 7dc9e1168… -HeadRef 7f12e2d1…`:** commits=3, violations=0.
- **Merges:**
  - `git merge-tree --write-tree origin/master HEAD`: clean (f11689ec8).
  - Each of the three commits picks clean onto `origin/rc/2026-11-01` (10a826b9d) with `--merge-base <c>^`. The four touched files are byte-identical on rc and master.
  - A whole-branch merge onto rc conflicts, as expected from master/rc drift.
- **Not run:** the rest of the Unit lane (outside the commissioned selection), so the scope is Interim. Nothing ran on the desktop.
- **Cleanup:** `tests/Antiphon.E2E/TestOutput` was removed by exact path. No canary directories are left.
