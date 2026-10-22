# CARD-1483 S1 repair - Final Review 8e9f68b4 of Code da684330

Reviewed source: feat/card-task-da684330 at 31e867f383c5dd6056b6abee2257f9ce67506ce5 (base 65b4d4a9b; red 7a5f32417; fix aa905becf).
Verdict: found. One fail-open (D-1 below). The tool exits 0 when resolving a link fails with a permission error. Everything else in brief checks 1-6 passed.

## Receipt (ordinary scope: CP-1 of the amendment, the whole required selection)
- Checkpoint tool run 20261022-143750-b1eb, `--rows CP-1 --expected-source-sha 31e867f38...`, server2. Result: GREEN exit 0.
  `CHECKPOINT CP-1 commit=31e867f383c5dd6056b6abee2257f9ce67506ce5 build=ok filter=/*/*/LandGraphEvaluationTests/(C1483_S1_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-8e9f68b4/.antiphon/checkpoints/20261022-143750-b1eb/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=31e867f383c5dd6056b6abee2257f9ce67506ce5 sourceState=clean buildSource=verified`
  The run reported `unlisted: none` and `outputs: deleted bin-c1483-s1/`.
- TRX identities: ApiSurfaceAvailable, EvaluatedSkippableItemRefuses, EvaluationFailureDenies, HarmlessFixtureApproves, LinkedSkippableItemRefuses, UnevaluatedSolutionEntryDenies.
- `scripts/validate-checkpoint-receipt.ps1 -Rows CP-1`: `CHECKPOINT SOURCE VALID source=31e867f38... rows=1`. The tracked tree is clean.
- `git merge-tree --write-tree origin/master(cb7af8ccd) HEAD`: clean (tree ae67f3344), exit 0.
- `check-evidence-diff.ps1 -BaseRef 462379b02 -HeadRef 31e867f38`: commits=8, violations=0.
- Unlisted runs (reasons stated): (a) one build-slot `dotnet build` of the tool at HEAD into a scratchpad copy, plus a copy of the 7a5f32417 tool source; (b) probe runs of the tool, each through build-slot.ps1. Scratch output is outside the repository.

## D-1 (found): a permission error while resolving a link approves (brief check 3)
- Where: `tools/Antiphon.LandGraphAudit/SolutionAudit.cs` `Walk`, line 190: `var target = new FileInfo(next).LinkTarget; if (target is null) { current = next; continue; }`.
- Failure: the fixture has `App/locked/lk -> ../../docs`, `App/locked` mode 000, and `<EmbeddedResource Include="locked/lk/guide.md" />` (EnableDefaultItems=false).
  The tool prints `approved` with exit 0. `LinkTarget` returns null when readlink fails with EACCES, so the walk treats the link as a plain or missing component. The real-path candidates then never reach docs/.
- Why: the brief requires a permission error during link resolution to exit 2, not approve. The tool's contract is default-deny: evaluation selected an item that is docs/guide.md through a link, and the tool approved without knowing where the link points.
  Practical reach is low. git cannot check out a mode-000 directory, and a build running as the same user also cannot traverse it. The caller may weigh it.
- Fix: when `LinkTarget` is null, tell "not a link or missing" apart from "unreadable". For example, call `File.GetAttributes(next)`, which uses lstat. Treat `FileNotFoundException`/`DirectoryNotFoundException` as missing, keep a non-link as written, and let any other exception (`UnauthorizedAccessException`, `IOException`) propagate to exit 2.
  Add a test: a Linux mode-000 directory holding a link into docs gives exit 2 and no `approved`. Skip it when running as root, where the permission does not bind, and use a deny ACL on Windows.

## Checks that passed
1. D-1 closed. Probes ran in scratch fixture repos with the HEAD tool; every fixture was untouched (find size+mtime snapshot).
   - Exit 2 naming the entry: unknown GUID (`type Unknown`), web site (`WebProject`), `.shproj` (`SharedProject`, an over-refusal), `.dcproj` with its GUID (`Unknown`), missing project.
   - Evaluated and refused: a `.dcproj` under the C# GUID; a duplicate project GUID; a project path `App\..\Other\Other.csproj`; a csproj nested two solution folders deep.
   - Approved: a solution folder with a SolutionItems `docs\guide.md` entry. Solution items are not built.
   - Real Antiphon.sln has 35 C# entries, 4 folders and no other type: `approved` in 4.5 s, exit 0. `git status --short --ignored` and file mtimes were unchanged, and no `/tmp/antiphon-landgraph-*` directory was created.
2. D-2 closed. Every case below exits 1 with the docs/README path, or exits 2; none approves:
   - a chain a->b->c->../docs
   - a loop l1<->l2 (exit 2 `more than 40 links` in about 1 s, no hang)
   - a dangling link to docs/missing.md
   - `s/../guide.md` with s->docs/sub
   - an outside link to a second link (App/x -> ../other, other -> docs)
   - an absolute link target
   - a case variant (Docs/Guide.MD, an over-refusal)
   - a Directory.Build.props relative include `lk/../docs/sub/deep.md`
   - `$(MSBuildThisFileDirectory)rl/guide.md` with a root link
   - a link in the project path itself, in two variants: PL->docs/sub/P with `../../guide.md`, and PL->z/inner with `../g.md` and z/g.md->docs
   - exactly 40 links (refused)
   - 42 links (exit 2)
   - The only miss is the permission case (D-1).
3. No side effects and no new fail-open, apart from D-1. The 120 s/300 s watchdogs are unchanged, and every exception in Main exits 2.
4. Red record confirmed. The 7a5f32417 tool source (byte-identical to 65b4d4a9b) printed `approved`, exit 0, on both new tests' fixtures. The tests assert exit 2 and exit 1, so both go red.
   At HEAD the link fixture prints exactly 6 refusals: 3 explicit and 3 from the SDK `None` glob, matching the test.
   Mutations, by equivalence: "skip non-MSBuild entries" is the pre-fix D-1 code, and the unknown-type test goes red (approved). "Drop the realpath" is the pre-fix D-2 code, and the link test goes red (approved).
   "Count exit 2 as approval" is caught by `ExitCode.ShouldBe(2)` in V-3 and V-3a. PCs stay pending for SourceLanding Mutation.
5. Interplay recipe is exact and complete. On a scratch merge of this tip with CARD-1409 tip e6ac1c606, the 1409 script's `-Check` fails on `_StagedHelperFramework`.
   After the report's 4 entries (1 property, 3 metadata), `-Update` leaves the manifest unchanged and `-Check` exits 0 with `digest=90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937 count=58`.
   Master's unchanged script (byte-identical on this branch) `-Check` on this branch also gives the same digest, count 58, exit 0. Not run here: the recipe's `C1385_RegistryMatchesNarrowGraph` step. The scratch worktree was removed.
6. Amendment CP-1 = 6 is right: 4 existing tests plus V-1a and V-3a, and the TRX shows exactly those 6 identities.

## Disclosure (not a defect under the standing policy)
- Linux magic links: `<EmbeddedResource Include="/proc/self/cwd/../docs/guide.md" />` approves. The tool resolves `/proc/self/cwd` to its own working directory, but the compiler opens the file from the project directory.
  This is a deliberate construction in the plan's R-3 trusted-definition class. Backlog suggestion: refuse any item under `/proc` (or any path whose real path leaves through a magic link).
- Not exercised on this host: Windows behaviour (junction fallback, lexical `..`), as already disclosed. Plan residuals S2/O-1 and hard links are unchanged.
