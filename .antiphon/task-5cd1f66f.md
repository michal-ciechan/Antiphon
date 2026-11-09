# CARD-1441 Review round 1 (Final, Full): runner non-inspection git gc.auto=0

Verdict: CLEAN. No blockers. Two advisories (A-1, A-2) for cards; they do not hold the land.

Subject: Code task 2c34295b, `origin/feat/card-task-2c34295b`, range 0cb7bfa7fcbd98e1fb537aa85dbf669395fe51a7..4fb3120aef38c112d51e22278860c57f646e4235
(red 715ad8430cdf995da6435d20f0af0dae80d2008e, fix 4fb3120aef38c112d51e22278860c57f646e4235). Code landing owner: 2c34295b.

## Evidence

Checkpoint run 20261109-173714-073a at 4fb3120ae, source clean, buildSource=verified, verdict GREEN exit 0,
`validate --expected-source-sha 4fb3120ae...` -> `CHECKPOINT SOURCE VALID rows=4`. Plan: reviewer scratch `.antiphon/c1441-review-plan.md` (CARD-1441 has no plan file).

| Row | Filter | Result |
|---|---|---|
| CP-1 | `/*/Antiphon.SessionRunner.Tests/C1441RunnerGitMaintenanceTests/*` | 6/6 passed |
| CP-2 | `/*/Antiphon.SessionRunner.Tests/RunnerWorkspace*/*` | 83/83 passed |
| CP-3 | `/*/Antiphon.TestSupport/(TestClassificationGuard*)\|(NativeLinuxPolicyWitnessTests*)/*` (SessionRunner.Tests) | 4/4 passed |
| CP-4 | Antiphon.Tests `(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` | 52/52 passed |

Wall 10m58s (CP-4 build 6m19s; TestClassificationPolicyTests 84 s). No unlisted runs inside the tool.

Red: 715ad8430 built and run through build-slot in a detached scratch worktree: C1441 class 1 test, 1 failed,
`gc.Output should be "0" but was "6700"` (right reason).

Reviewer mutants at 4fb3120ae (scratch worktree, build-slot, one lease per build and per run, restored and removed):
- M-A, reset count (`count = 0` before the appends, ~PC-2): 5/6, only `C1441_append_preserves_existing_entries` red (`GIT_CONFIG_COUNT`).
- M-B, inspection also appended (drop `else`, ~PC-4): 5/6, only `C1441_inspection_keeps_its_isolated_override_list` red (`GIT_CONFIG_COUNT` 18 vs 16).
Code's PC-1..PC-4 stay pending for SourceLanding Mutation; these reviewer mutants do not discharge them.

Other gates: `check-evidence-diff.ps1 -BaseRef 0cb7bfa7f -HeadRef 4fb3120ae`: commits=2 entries=0 violations=0.
`git merge-tree --write-tree origin/master(d861149d07) 4fb3120ae`: clean (tree 2d71ebf3f2); master drift since base touches no file in this diff.
No policy JSON, csproj, props/targets or MSBuild change in the range.

Unlisted run (disclosed): one `dotnet run --project tools/Antiphon.Checkpoints -- import --plan` to validate the scratch plan, which built the checkpoint tool outside a slot.
After that, every call used the built dll.

## Check results

1. Env append. `RunnerGitProcess.DisableAutoMaintenance` is identical to the landed `ServerGitProcess.DisableAutoMaintenance` (CARD-1432 D-1).
   - It reads the inherited or caller `GIT_CONFIG_COUNT` and writes KEY/VALUE at count and count+1, then count+2.
     Existing entries are kept (test plus M-A).
   - The appended entries come last, so they win over any inherited env entry for the same key. `GIT_CONFIG_PARAMETERS` (`-c`) would still win, but no runner call passes `-c`.
   - No KEY/VALUE name collides with the inspection scrub, because the call runs only in the non-inspection `else` branch.
   - SessionRunner has exactly one git start site (`RunnerWorkspaceService.cs:1006`), so clone, fetch, worktree add/prune and push all get the entries.
   - `InProcessAnswer` is not disabled. `ChildReadsSameRepository` checks no config vars, and `EndpointTakenLiterally` reads the process env, not the psi.
   - Effect on clone/fetch/worktree/push: only `gc --auto` and `maintenance run --auto` are suppressed. Explicit `git gc` and `git maintenance run` are unaffected, and the runner runs neither.
   - Local-path push, probed on git 2.47.3: git strips `GIT_CONFIG_COUNT` for the receiving side (a post-receive hook saw `count=unset`), so remote auto gc is unchanged.
   - Git floor: `GIT_CONFIG_COUNT` needs git 2.31. The repo floor is 2.43 per the brief, and server2 runs 2.47.3.
2. Inspection list unchanged at 16 entries, already holding `maintenance.auto`/`gc.auto`. The test asserts 16 and M-B is killed.
3. Red/green above.
4. No policy or MSBuild change; evidence-diff 0; merge-tree clean.

## Advisories (cards, not land blockers)

**A-1 No gc owner for runner repositories after this lands.**
- Where: every runner repository (server2 `/work/repos/antiphon` and the mirrors that share it).
- Failure: auto gc and auto maintenance were the only gc path, and now nothing runs gc. CARD-1432's explicit owner was plan D-6 step 4: a nightly `git maintenance run --task=...` and a weekly `git gc`. CARD-1432 closed Done with only S1, S2 and S4 on master; there is no `WeeklyGcDay` or `maintenance run` code anywhere. Today the runner repo has 1139 loose objects, 30 packs and garbage 6, held down by auto gc. After the land, loose objects (commits, fetches under `transfer.unpackLimit`) and packs (`gc.autoPackLimit` 50) grow without bound. That is the CARD-1432 desktop symptom (72k loose, 118 packs). Server-side repos are already in this state since S1.
- Why not a blocker: the change implements the approved CARD-1432 D-1/F-1 design exactly, the drift is gradual and measurable, and an explicit `git gc` restores the repo.
- Fix: card the explicit maintenance owner (CARD-1432 step 4/S3; CARD-1450 already assumes an S3 sweep) and include runner repositories, or run a scheduled `git gc` on server2 until then.

**A-2 An invalid `GIT_CONFIG_COUNT` throws raw and is stricter than git.**
- Where: `src/Antiphon.SessionRunner/RunnerGitProcess.cs:17-19` and the call at `RunnerWorkspaceService.cs:1054`.
- Failure: it throws `InvalidOperationException("git_config_count_invalid")` before the CARD-0631 D-8 start catch and before `InProcessAnswer`.
  - A bogus inherited count therefore surfaces as an unnamed exception, not git's exit 128 or a named 409 admission.
  - Reads that libgit2 used to answer in process now fail too.
  - Git itself accepts `""` (as 0), `" 1"` and `"+1"`, all verified on 2.47.3, but this code rejects them. It matches git on `x` and `-1`. It does not overwrite existing entries, as PC-3 intends.
- Why: it is reachable only when the runner process env carries such a value, and then every git child already died. It is byte-identical to the shipped server helper.
- Fix (optional): parse like git's strtoul (empty = 0, leading whitespace or `+`), and map the refusal to a named `PhoneHomeAdmissionException`.

Note: there is no CARD-1441 plan or `### Checkpoints` table, and the Code report is not in this mirror, so Code's CP-n lines could not be matched. The brief's named selection was run in full instead.
