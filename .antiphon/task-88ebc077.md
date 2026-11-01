# Debug 88ebc077: g125 T1 review D-1 (ancestor instruction files must refuse)

Base 5053dd18e2d84233a978903802b6b94de17a477c (feat/card-task-3c6f388b tip). Branch feat/card-task-88ebc077.

## Decision: keep the full-ancestor refusal and make the fixture root hermetic (the reviewer's second option)
The narrow skip of the host `~/.claude` cannot be proven safe here. CLAUDE_CONFIG_DIR redirects only the
user-level config directory. Claude's project-memory walk reads `<ancestor>/.claude/CLAUDE.md` (and rules)
in every ancestor of the cwd, and the user profile is an ancestor of Windows TEMP. No headed canary on this
host proves the walk skips that directory. So the guard stays at full strength: a host with Claude files above
the lane root refuses, and only the test fixture moves.

## Fix (838cf4e7dc29f36e835c9d0abc464e3d1169f213)
- tests/Shared/LiveClaude/LiveClaudeProfile.cs InstructionDigest walks every ancestor to the filesystem root.
  Above OwnedRoot, any CLAUDE.md, CLAUDE.local.md, AGENTS.md, .mcp.json or .claude (file or directory) throws
  `unowned-instructions`. That includes a host profile's .claude, which used to throw `unowned-path`. Inside the
  root, behaviour is unchanged, except that CLAUDE.local.md is now also an input that must be declared.
- tests/Shared/LiveClaude/LiveClaudeProfileCases.cs: the fixture's default root comes from `CleanTempSubdirectory`.
  It uses the first base with no such entry on or above it: TEMP, then on Windows `%ProgramData%\antiphon-c1545`,
  then `<TEMP drive>\antiphon-c1545`. If no base is clean, it fails with "C1545 fixture has no base without
  Claude instruction inputs above it: <offenders>". It never silently admits.

## Witnesses
- f81ec9b1de99fe287fe391cc8ce3a1493b151118 (red first): `InstructionFilesAboveTheOwnedRootRefuse` puts
  CLAUDE.md, AGENTS.md, .mcp.json and .claude/CLAUDE.md strictly above the root. Red at f81ec9b1:
  `C1545 V-14/above-root-CLAUDE.md expected typed unowned-instructions refusal`. Green at 838cf4e7.
- HostAncestorClaudeDirectoryStaysOutsideTheOwnedClosure is now the hermetic form. A fake host home has
  .claude/{CLAUDE.md,settings.json} with AppData/Local/Temp below it. A profile rooted under that TEMP refuses
  unowned-instructions. CleanBase skips that TEMP for a clean base, and the profile there constructs. The default
  base has nothing above it. A host whose only base is dirty fails the fixture. The WindowsShaped and Symlink
  witnesses are unchanged and green.
- Mutant (a), walk stops at the owned root (`&& Within(dir, OwnedRoot)` restored): both witnesses red
  (above-root-CLAUDE.md, host-ancestor-refuses).
- Mutant (b), skip widened to any .claude above the root: both red (above-root-.claude/CLAUDE.md,
  host-ancestor-refuses).
- Both mutants were restored. A rebuild then ran LiveLaneIsolationTests 8/8 green. PC-46 (`dir = dir.Parent`)
  still has a unique site.

## Runs (server2 Linux, build-slot, TEMP=/tmp, UseAppHost=false, fresh bin-g125*)
- Antiphon.Tests at 838cf4e7: LiveLaneIsolationTests + LiveLaneEvidenceTests + LiveLaneRunnerTests 14 total,
  13 passed, 1 failed. The failure, LiveLaneRunnerTests.WrapperOnlyPaidManifest/full-inputs ("contract mismatch"),
  is INHERITED. The same harness case fails at base 5053dd18e in a detached worktree, because test-live-lane-run.ps1
  pins `$base=f1b957c7f`, which is not an ancestor and has 474 changed files.
- Antiphon.Agents.Pty.Tests at 838cf4e7: LiveClaudeIsolationWiringTests + LiveClaudeEvidenceWiringTests +
  LiveClaudeInventoryTests 9/9 passed, 0 skipped. Neither agents-pty red reproduces on Linux.
- Land graph -Check: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  The evaluator was built through build-slot. -Update was not run.
- check-evidence-diff 5053dd18e..838cf4e7: commits=2 entries=0 violations=0.
- merge-tree onto origin/master and origin/rc/2026-11-01 (both 0096f7c5a): clean.

## What a native Windows rerun of group 125 must show
- LiveLaneIsolationTests (8 tests, including the new above-root witness) and the 6 LiveClaudeIsolationWiringTests
  pass with no `20 unowned-path` and no `unowned-instructions`.
- If they now fail with "C1545 fixture has no base without Claude instruction inputs above it", that is the fixture
  being honest: TEMP, %ProgramData% and the drive root all hold Claude files on or above them, or cannot be
  created. Read the listed offenders. Do not weaken the walk.
- The fixture leaves an empty `%ProgramData%\antiphon-c1545` (or `C:\antiphon-c1545`) base directory behind;
  each fixture root inside it is deleted on dispose.
