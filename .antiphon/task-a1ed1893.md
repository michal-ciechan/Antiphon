# CARD-1238 Code report (task a1ed1893)

Branch feat/card-task-a1ed1893, worktree /work/worktrees/task-a1ed1893 (server2 runner mirror), base 6131aed14930473704e887774767ab17f08dc25a.
Commits: e9bed7f515ad6622771fad64ba5cc6197627b8d1 (guard, csproj unfixed), a95cbe5aecc5b4613d32275de04b5821d7dac072 (fix), 5fbf5ddadb6148172c419f61098b974b9841e94b (comment only; HEAD, pushed, ls-remote equal).

## Cause
The checkpoint tool (BuildConfinement.Rewrite / run-checkpoint.ps1) passes ONE absolute OutputPath plus ArtifactsPath=<out>/artifacts and UseArtifactsOutput=true. As a global property it applies to every referenced project, so every helper builds into the Antiphon.Tests OutDir. Each Copy* target asked the helper for GetTargetPath, got that shared OutDir, and copied `**\*` (or `*`) of the whole test output, including the earlier helper folders and artifacts/obj, into the next helper folder. Ten targets in declaration order give roughly 2x growth per step: nightly-owned-child, custody-child, script-harness-host, channel-outbound-probe, checkpoint-lifecycle/filter/repeat-host, fakeclaude, fakegrok. A relative OutputPath (bin-x/) resolves per project, so it never nested.

## Fix
tests/Antiphon.Tests/Antiphon.Tests.csproj: one batched CopyStagedHelpers target over StagedHelper items replaces the six copy targets. If the producer's output equals this OutDir (shared layout), it builds the helper with OutDir=<OutDir>/<folder>/, which is the CopyOwnedPtyHostAppHost precedent. Otherwise it copies from the producer's own output as before. docs/testing-and-build.md: the target name in the UseAppHost note is updated.
Guard: tests/Antiphon.Tests/TestHelpers/BuildOutputNestingTests.cs (Unit) covers these cases:
- No helper folder contains another helper folder or Antiphon.Tests.dll.
- The output has fewer than 10,000 files.
- Each of the 9 helpers has its dll and runtimeconfig.
- Two synthetic-tree cases.

## Measurements (server2, UseAppHost=false)
| Build | Size | Files (entries) | Build time | Slot wait |
|---|---|---|---|---|
| master, relative OutputPath=bin-m/ | 598 MB | 1,468 (1,713) | 3m50s | 661 s |
| e9bed7f unfixed, checkpoint layout (abs OutputPath+ArtifactsPath+UseArtifactsOutput) | 38 GB | 97,673 (107,818); 288 Antiphon.Tests.dll; fakeclaude/ and fakegrok/ 13 GB each | 12m50s | 390 s |
| a95cbe5 fixed, checkpoint layout | 773 MB (144 MB artifacts/obj) | 2,228 (2,597) | 3m59s | 0 s |
| a95cbe5 fixed, relative bin-r/ | 599 MB | 1,470 | 2m33s | 90 s |
| base csproj, relative bin-b/ | 598 MB | 1,469 | 3m42s | 75 s |
In the relative layout, the fixed and base file sets are identical except for one host-run TestResults html. In the relative build at master the nesting is refuted. In the checkpoint layout it is confirmed, which matches the card.
df /work: 302G free before the measurements, 254G with the nested output present, 277G after cleanup. Every bin-m/bin-n/bin-f/bin-r/bin-b directory is deleted (0 remain).

## Guard red/green
- Red at e9bed7f, nested build: BuildOutputNestingTests 2 failed / 2 passed. The nested check found 418 entries (e.g. nightly-owned-child/Antiphon.Tests.dll). The file count was 97,673, over the 10,000 ceiling.
- Green at a95cbe5: 13/13 in the checkpoint layout and 13/13 in the relative layout (TRX).

## Host-launching classes, a95cbe5 checkpoint layout (one run, filter of 28 classes; TRX 382 total, 304 passed, 19 failed, 59 not executed)
Fully green: BuildOutputNesting 13, ChannelOutboundDelivery 38, ChannelOutboundRecovery 18, CheckpointRepeatHost 3, CheckpointTempLifecycle 6, DirectSessionRunnerClientDisposal 6, GrokDelegateDispatch 38, HerdrLabelFollowFlow 19, HerdrLabelFollowWire 4, RerunFilterHost 1, ScriptHarnessCancellation 5, ScriptHarnessLinuxOwnership 21, ScriptHarnessProcessContract 26, ScriptHarnessProcess 11, TestAppHostPath 5. DelegationBriefCeilingPty, RemoteControlRecoveryPty, RunnerGrokAdapterReadyTestsPty and SessionQueueReceiptPlumbing had passes and environment skips (NotExecuted). Fully NotExecuted: GrokDelegateEndToEnd, GrokSignInRuntime, SessionMessageQueuePty, SessionMessageQueueGrokPty.
Failures, none caused by this change:
- NightlyNativeOwnershipTests 7 and PostLandMutationCustodyTests.C478_CanonicalAuthorizationRejectsAnEscapingJunction: Windows-only (they assert IsWindows or start cmd.exe).
- CheckpointTempUsageTests.assembly_and_allocation_invoke_bounded_sweep: inherited. It fails the same way at the base csproj (bin-b), in the fixed relative layout and on a rerun.
- HerdrAlwaysOnChannelParityTests Herdr_launch_definition_starts_adopts_and_exits(Grok) and Named_AlwaysOn_herdr_agent_holds_after_three_failures...: inherited. The same 2 of 4 fail at base (bin-b), and the rerun in the fixed layout matches.
- ChannelOutboundUnifiedTransportTests C519_* 8: testcontainers create/readiness timeouts inside DockerContainer.StartAsync, before any helper runs, at host load of about 20. The base run of C519_Queue_to_adapter* had 1 of 3 fail the same way. A rerun of the class in the fixed layout was cut off at my 10-minute foreground timeout after 5 more failures of the same kind. I killed that orphaned dotnet run (my PIDs). Environmental.
Process note: the 28-class run overlapped my reruns and the base build, each in its own slot. During it the csproj was briefly checked out at base and restored. That run was --no-build on bin-f binaries, so it is unaffected.

## Not done / pending
- No checkpoint tool run: the card has no plan with a Checkpoints table. All drivers went through scripts/build-slot.ps1.
- Mutation not run. PCs are pending: revert CopyStagedHelpers to copy the shared output and the guard must go red; drop a StagedHelper item and Own_output_stages_helper_program must go red.
- Not fixed, possible follow-up: tests/Antiphon.Agents.Pty.Tests CopyFakeClaude has the same glob. In the shared layout it copies the whole Pty.Tests output into fakeclaude/ once, with no recursive nesting.
- Evidence: scripts/check-evidence-diff.ps1 -BaseRef 6131aed1 -HeadRef HEAD gave commits=3 entries=0 violations=0 (exit 0). git merge-tree --write-tree origin/master(87ec0d82) HEAD gave exit 0, tree 47301a6d. origin/master did not touch the changed files. The worktree is clean.

## Proposed separate card (checkpoint tool hashing)
Title: Checkpoint tool hashes every untracked and ignored file (all bin-*/obj/artifacts trees) while holding the build slot.
Body: Found by CARD-1238. In tools/Antiphon.Checkpoints/BuildConfinement.cs, CaptureUntracked runs `git ls-files --others` plus `--others --ignored --exclude-standard`. Stamp() then reads and SHA-256-hashes every listed file in full (ReceiptFile.ReadBytes) and skips only `.antiphon/`. That set includes every bin/, bin-*/, obj/ and artifacts tree in the worktree, the row's fresh output among them. It is captured before and after the build while the slot is still leased, which is about 53 minutes on the 38 GB output. Task: exclude the tool-owned output roots recorded in state, and known ignored build trees, from content hashing (stamp them by path, size and mtime, or not at all), or release the slot before the post-build capture. Keep tamper detection for tracked and non-build untracked paths. Red-first: count file reads through a seam so an N-file output is not read N times under a lease.
