# Task 1fddbdba: Final Review of CARD-1493 S1 (Code task 4921bd9e)

Subject: `feat/card-task-4921bd9e` at `aeab490d3b597a0d8874a06be7a8eed56f9f1c6a` (base `cb7af8ccd`).
Verdict: **found**. There is one defect: a warning that tells the author to take a wrong action. Everything else passed.

## D-1 (found): the warning tells authors to write a reuse reference the importer refuses

- **Where:** `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:236` (the warning text). The
  reuse grammar is at `:200-204` (`^CP-\d+(?: \(-NoBuild\))?$`, then an Ordinal `rowBuilds` lookup).
- **Failure:** the warning always says `write '<earlier row id>' in the Build cell`. A reuse
  reference works only when the id is `CP-<digits>`. Row ids such as `CP-2-remove`, `CP-1a` or
  `V-1` are legal and import today. Following the advice for one of them turns an importing plan
  into a refused one. I reproduced it on a real plan:
  `docs/superpowers/plans/2026-10-02-card-1004-linux-grok-composer-plan.md` warns
  `CP-3-any: ... write 'CP-2-remove' in the Build cell`. I applied that advice to a scratch copy and
  got `refused: manifest-identity: CP-3-any: Build 'CP-2-remove' is not '<project> -> <bin-x/>', CP-n, or n/a`,
  exit 2. The same plan's `CP-5-unit` warning (`write 'CP-4-final'`) has the same defect. Those are
  2 of the 356 census warnings; the other 354 name a plain `CP-<digits>` id.
- **Why:** the warning is advisory only, but its whole value is the action it recommends. Under
  the standing verdict policy, a recommended action that breaks a currently-accepted plan is a finding.
- **Fix (Code, landing owner 4921bd9e):** when `equivalent.RowId` does not match `^CP-\d+$`
  (as the reuse branch reads it), recommend reusing the earlier output instead:
  `write '<project> -> <equivalent.BuildId>/'`. That form is accepted. Verified on the scratch
  copy: Build cell `tests/Antiphon.Tests -> bin-c1004-remove/` imports with exit 0. The other
  option is to suppress the warning for such ids. Add a V-5 test with a non-numeric defining row id
  and assert that the recommended cell imports.

## Checks

1. **Behaviour unchanged (code read):** the new block runs only inside `existingBuild is null`,
   after `manifest.Builds.Add`. It only appends to `warnings`. It cannot throw:
   `NormalizeProject` handles an empty project, `/`, `a/.csproj` and backslashes with in-range
   slices, and `FirstOrDefault` on the tuple list gives a default value. It does not touch exit
   code, error, Build/Group/Repeat or the manifest. Command rows, `n/a` and CP-n cells never reach it.
   Malformed rows (empty id, bad Repeat, bad Build grammar) are refused by the existing checks
   before the block runs. No new return path exists.
2. **Rule:** the Build column grammar is `<project> -> bin-x/` only. It cannot carry properties,
   a TFM or a configuration, so project + After + Repeat match the existing reuse constraints.
   Gap (disclosed, not a finding): the row's `Environment` reaches `dotnet build`
   (`Execution/RowRunner.cs:210`), and the rule ignores it. 9 of the 356 census pairs differ in
   Environment. All 9 are runtime-only variables (`TUNIT_MAX_PARALLEL_TESTS`, `ANTIPHON_*_TESTS`,
   `C804_ORPHAN_SWEEP_ROOT`, `ANTIPHON_PTY_BACKEND`, `C1364_WAIT_MUTANT`), none referenced by
   `*.props/*.targets/*.csproj`, so their builds really are equivalent. Backlog suggestion: skip the
   warning when the two rows' Environment differs, so an MSBuild-visible variable such as
   `Configuration` is never told to merge.
3. **Census (all 890 plans, not sampled):** I built the base (cb7af8ccd) and head (aeab490d3)
   tools, each through the build slot, and imported every plan with both. Exit codes are identical
   for all 890 (471 exit 0, 419 exit 2). The Code report says 470/420; the totals agree and no plan's
   exit changed. 101 plans gain 356 `duplicates` warnings, which matches the report. Apart from those
   lines, the output is identical, and all 471 YAML manifests are byte-identical.
4. **Tests:** the CP-1 red at 337d9ecf comes from the Code report's run 20261022-145754-f7d0
   (2/2 `Warnings should be [...] but was []`). It was not re-run, as the brief directs.
   Mutation probes ran in a scratch build with env-gated mutants:
   - ignore Repeat: **red**, `C1493_distinct_after_repeat_or_project_do_not_warn` (1/4 failed, line 491).
   - ignore After: **not run**. Two attempts hit the build-slot timeout (queue position 6-9, over 9
     minutes). By the code, V-3 row CP-2 (S2 against S1) would warn, so the same test would go red.
   - raw strings: **not run** (same slot timeout). V-2's backslash/`.csproj` against `./.../` pair
     would stop matching, so `C1493_DuplicateBuild_project_spellings_normalise` would go red.
   - unmutated control under the mutant build: not run (slot). The CP-2 receipt is the green control.
   None of the four tests is a stub: each asserts an exact warning list or an exact exit/manifest.
5. **Unrun classes:** grep found no warning-list or `WARNING` assertion in
   CheckpointRepeatConfigurationTests, CheckpointStampSafetyTests, CheckpointTaskOwnershipTests,
   PlanCoverageCommandTests or any other test outside CheckpointImportTests. No script consumes
   import warnings. `start`/`run` print them to stderr only (Program.cs:636). Not run, as the brief
   directs.
6. **rc:** all four commits cherry-pick clean onto `origin/rc/2026-10-20` (0714a9a89) in a
   scratch worktree, which I then discarded. The plan doc is in the pick and can be dropped.

## Evidence

- Receipt: run `20261022-162351-4003`, plan `.antiphon/r1493-cp2-plan.md` (CP-2 row copied
  verbatim from the task plan):
  `CHECKPOINT CP-2 commit=aeab490d3b597a0d8874a06be7a8eed56f9f1c6a build=ok filter=/*/*/CheckpointImportTests/* executed=25 passed=25 failed=0 skipped=0 ... slot=granted waited=150s dirty=0 source=aeab490d3b597a0d8874a06be7a8eed56f9f1c6a sourceState=clean buildSource=verified`
  `scripts/validate-checkpoint-receipt.ps1`: `CHECKPOINT SOURCE VALID source=aeab490d3... rows=1`.
- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master 445df8a80).
- `scripts/check-evidence-diff.ps1 -BaseRef cb7af8ccd -HeadRef aeab490d3`: commits=4 violations=0.
- Builds outside the plan (disclosed, all gated): the checkpoint tool at head (`bin-r1493tool/`);
  the tool at base for the census (`bin-r1493base/`); Antiphon.Tests with mutants (`bin-r1493mut/`,
  scratch only, about 6.5 minutes). All were deleted, along with the scratch worktrees.
