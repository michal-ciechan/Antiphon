# Review CARD-1790 round 1 (task 6a7c2ca6)

Subject origin/feat/card-task-d6eca70e f0e9ea9d3128d44f3bd09a00433de563a8790b46 (Code task d6eca70e-49b5-4e82-8533-721a1c703228, Grok), base f54741ef3bc08baa0493f9e0bebd2425acab6fb5, master 8fb32b25be7d17490d2dc999f35b068843ce1f4f. Reviewed 2026-11-08 on server2 mirror /work/worktrees/task-6a7c2ca6.

`git merge-tree --write-tree origin/master HEAD` clean: 043be83620f827b7bb665b3da7458a45eb649510. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f54741ef3b -HeadRef HEAD`: commits=3 entries=0 violations=0, exit 0.

## Verdict

Not clean. One defect (D-1) goes back to Code. The ordinary evidence (CP-1, CP-2, Release solution build) could not be rerun this round because the server2 volume is full; Code's counts stand unverified. Five advisories are filed as CARD-1793 and do not hold the land.

## D-1 The plan's `### Checkpoints` table is not a checkpoint-tool table

- Where: docs/superpowers/plans/2026-11-08-card-1790-pwsh-private-cache-plan.md, the table under `### Checkpoints` (three columns, no header row).
- Failure: `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <plan>` exits 2 with `checkpoint table header must be CP, After, Build, Group, Filter, Covers, Expect, Min, EstimatedMinutes`. The Code report confirms it: "The checkpoint tool was not driven; this named run is CP-1". CP-1 (3 passed, lease 2e4f6d96) and CP-2 (75 passed, lease cc29ce61) were leased ad-hoc `dotnet run` selections with no checkpoint receipts.
- Why: AGENTS.md and docs/testing-and-build.md (CARD-0585, CARD-0723) make the plan table the closed manifest for Code, Review and the SourceLanding Mutation stage; a table the tool refuses leaves every later stage without a runnable manifest or a SHA-validated receipt.
- Fix: replace the table with the one below (my extract `.antiphon/review-1790-checkpoints.md` imported as 2 rows), run it once through the tool, run one leased Release solution build (`scripts/build-slot.ps1 -Label <what> -- dotnet build Antiphon.sln -c Release -p:UseAppHost=false`, native output), and carry both receipts into the Code report.

```
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1790-a/ | pwsh-cache | /*/Antiphon.Tests.Scripts/PwshPrivateCacheTests/* | S1 | 3 executed, 0 failed | 3 | 4 |
| CP-2 | S1 | CP-1 | guards | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | S1 | all listed (75 at f0e9ea9d31), 0 failed | 60 | 8 |
```

## Evidence attempted this round

- `import --plan .antiphon/review-1790-checkpoints.md` (lease 28863ab1 for the tool build): imported 2 rows.
- `run --plan .antiphon/review-1790-checkpoints.md`: run 20261108-142711-0017, build bin-r1790a lease d377ae2f granted after 180 s and released 50 s later; the executor then crashed with `checkpoint terminal persistence failed: No space left on device` and `executor died without phase=done`. No TRX, no report.json. Run folder retained with executor.log; its tool shadow copy and the empty bin-r1790a output were removed by me.
- Release solution build: started in a detached scratch worktree at f0e9ea9d31; it was still waiting for a slot when the volume hit 0 bytes free, so I killed it and removed that worktree. Not run.
- Host state: /dev/mapper/ubuntu--vg-ubuntu--lv 705G, 672-673G used, 3.5-7.4G free during this review (100%). /work/worktrees holds 396 task mirrors, the largest 3.6-3.8G each. Other sessions' builds were in flight; I did not consume the remaining space. Reclaim is an orchestrator or operator action (runner worktree retention, CARD-1677 and CARD-1008), not a review action.

## Brief CHECK items

1. Build scoping. The Directory.Build.targets item group compiles the file only when `IsTestProject` or `OutputType=Exe` and the project directory starts with `<repo>/tests/`. src/Antiphon.Hosting has no project file, so the type exists only where linked: the 23 tests/ projects that match (15 test hosts, 8 fixture executables) and Antiphon.SessionRunner. No assembly exposes a second `Antiphon.Hosting.PwshPrivateCache`, so no CS0436 or duplicate type; tools/ and the docs/ Reproduce project are outside the prefix. Not verified by a build this round.
2. Mechanism. `[ModuleInitializer]` in each entry assembly runs before Main. `Environment.SetEnvironmentVariable` updates the managed block that `ProcessStartInfo.Environment` copies, so default-environment children, copied-and-edited environments, and bash-wrapped scripts all inherit it (the inherited-pwsh witness proves the first). The spawn sites that clear the environment (GitStatusConfigAgreement, CodexCliVersionProbe, WindowsUnitPlatformAdmissionTests line 507, JqRunnerImageContractTests, RunnerCodexCliEvidenceTests) start git, codex, docker or a child test host, never pwsh. ScriptHarnessHost is a tests/ Exe and installs its own directory, which its pwsh root inherits; the probe runs from Main, which avoids the Environment.Exit deadlock. A second copy in one process reuses through OwnedByThisProcess and never deletes. The runner exports its directory to every pty-host and agent (PosixProcessSpawner and ModernConPtyConnection.MergeEnvironment copy the whole environment). Not a blocker: Claude, Codex and Grok use CLAUDE_CONFIG_DIR, CODEX_HOME and GROK_HOME; git reads XDG_CONFIG_HOME; dotnet and npm ignore XDG_CACHE_HOME; Windows pwsh uses LOCALAPPDATA. Residual: all agent-started pwsh under one runner share one profile (A-1).
3. Cleanup. The creator deletes on ProcessExit (SIGTERM included) and the harness supervisor releases before `kill(-pid, 9)`. Names carry pid plus GUID, so concurrent hosts differ, and the owner marker stops a process deleting a foreign directory. There is no stale-directory bound: a SIGKILLed host or fixture child leaks its directory forever and the runner temp sweep has no registered family for it (A-2); a soft runner restart leaves surviving sessions pointing at a deleted path (A-3).
4. Stated gaps. The checkpoint tool's ProcessDriver pwsh shim and every pwsh the agent starts run under the agent, so they get the runner-wide directory, not a private one. The processes that died in the 11:11 burst (harness pwsh root, the tripwire pwsh, bash-wrapped deploy-server2.ps1) were all test-host descendants and are now isolated per host. GitInProcess dropping XDG_CACHE_HOME affects git only. Follow-up: CARD-1793.
5. Witnesses. Red commit c8c5be8e8b0de4ae7d59f32e9124179b046b69dc adds the three tests before the fix (Code: 3 failed, 0 passed at base). Code reports three mutants (unset variable, shared name, skip delete) with line-level failures; not rerun here. PCs stay pending for method-scoped SourceLanding Mutation. C1729LayeringGuardTests is in CP-2; the change adds no ProjectReference, so no layering edge.

Performance: `[Timeout]` 60 s and 90 s against 30 s child waits and 20 s probe waits; CP-2 held a slot 143 s at Code. OS-branch witnesses: Code ran check-os-witnesses exit 0; the Linux-only harness finally has no direct witness (A-5).

## Advisories (filed as CARD-1793, id 25db4458-d968-4f52-a6ed-a60031978de9)

- A-1 Agent shells and the checkpoint tool share one profile per runner; give each session its own directory at launch and link the installer into tools/Antiphon.Checkpoints.
- A-2 No stale-directory sweep; sweep dead-owner siblings at Install() or register the temp-sweep family.
- A-3 Surviving sessions keep a deleted path after a soft runner restart.
- A-4 tests/Antiphon.E2E on Linux would move Playwright's browser lookup to the private directory; set PLAYWRIGHT_BROWSERS_PATH for any Linux browser lane.
- A-5 The harness supervisor's Release() before kill has no test.

--- review evidence ---
subjectTaskId: d6eca70e-49b5-4e82-8533-721a1c703228
reviewedSourceSha: f0e9ea9d3128d44f3bd09a00433de563a8790b46
reviewedSourceClean: false
ordinaryScopeCompleted: None
reviewRound: 1
reviewedRange: f54741ef3bc08baa0493f9e0bebd2425acab6fb5..f0e9ea9d3128d44f3bd09a00433de563a8790b46
