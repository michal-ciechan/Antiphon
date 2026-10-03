# CARD-1007 Code report

Implemented and verified the default offline-provider opt-in gate. Both default
oracles produce the named skip before binary lookup; enabled Codex passes and
enabled Grok retains the confirmed inherited CARD-0596 assertion failure.

Acceptance quoted from CARD-1007: "ordinary selection starts neither provider binary;
an explicitly enabled oracle lane still executes both original assertions when prerequisites exist."

Original Code task / landing owner: e1b6cb4a-0051-4bc0-93d5-f2eec3246cf6.
Branch: feat/card-task-e1b6cb4a.
Worktree: /work/worktrees/task-e1b6cb4a.
Assigned desktop mirror: C:\Antiphon\worktrees\card-task-e1b6cb4a (not accessed).
Start ref: c6d5d56b5b4c565d36157e21de9c85029cc45b53.
Implementation SHA verified: f3af343ffef896fe2381c05f968a108e6d599a96.
Final branch tip is an evidence-only commit; its full SHA is in the settlement
progress line. Review must use that committed HEAD as expected SHA.

## Changes and delivery

- tests/Antiphon.Agents.Pty.Tests/ProviderOracleGate.cs: shared exact-`1` environment
  gate following ClSession/CxSession/GkSession's existing opt-in/SkipTestException convention.
- tests/Antiphon.Agents.Pty.Tests/OrchestratorWorkspaceLayoutCanaryTests.cs: first
  statement in each offline method calls the gate; XML comment documents opt-in.
- tests/Antiphon.Agents.Pty.Tests/ProviderOracleGateTests.cs: six disabled values
  (null, empty, 0, true, leading/trailing whitespace) and the enabled body path.
  Injected readers keep tests independent of the process environment. Reflection
  lets the identical disabled regression compile and fail when the baseline has
  no gate. Enabled-path test catches a skip and fails an assertion, preventing an
  always-disabled mutant from merely becoming a skipped test.
- docs/testing-and-build.md: intentional lane, exact filter, prerequisites and reason.
- Plan/manifest: docs/superpowers/plans/2026-10-03-card-1007-provider-oracle-gate-plan.md.
  No separate landed Plan/TestDesign artifact was supplied/found; the committed
  manifest records the full brief's scope before execution.

Pushed slices:
- c4a723b830fa6e12934c3d962aaeeae7128d802a: tests/manifest only; original code
  identical to start ref. Expected baseline red: six assertion failures on absent gate.
- 7709f32e037fb4298fc5e5a769ff8e6640bcc2f5: implementation/docs, verification pending.
- f3af343ffef896fe2381c05f968a108e6d599a96: enabled-path test strength correction.
- Evidence-only tip: full report and compressed original receipts/TRX; no tested file changes.

No assertions in either original oracle changed. Comparing both entire method
bodies to the start ref after removing only the gate call yielded exact equality.
No timeout widened, no retry added, no assertion loosened. No land or deployment.
Restart: none (tests/docs only).

## Final verification outcomes

V-1 PASS: all six disabled values throw the one exact named skip and leave body count zero.
V-2 PASS: exactly `1` reaches the oracle body once; seven new gate cases green.
V-3 PASS: full default canary-class filter gives both offline methods NotExecuted
with DebugTrace `Skipped: Set ANTIPHON_PTY_PROVIDER_ORACLES=1 to opt in to offline Codex/Grok provider oracles`.
Both existing headed methods remain Explicit and are excluded by ordinary selection.
V-4 EXECUTED: both providers exit 0 and both original test methods execute without
skips; Codex PASS, Grok FAIL at `projectPaths should not be empty`. Same exact Grok
assertion FAILS at the unchanged start ref (1 executed/1 failed), confirming CARD-0596.
R-1 PASS: whole Pty Unit lane 321 passed, 0 failed, 2 existing platform skips.
R-2 PASS: whole Antiphon.Tests Unit lane 3910 passed, 0 failed, 52 platform/prerequisite
skips; fresh TRX covers 320 distinct classes. Skips are not credited as passes.
R-3 PASS: 65 passed/0 failed/0 skipped, with fresh executed identities/counts:
CheckpointManifestDocumentationTests 7; ScopedVerificationInstructionTests 17;
RunnerDefaultGuidanceTests 4; DockerStackDocumentationTests 10;
GrokRunnerImageContractTests 26; CheckpointRepeatDocumentationTests 1.
AgentTaskReplyIntegrationTests mentions this document only as a payload string,
does not read/pin it, and is outside the affected documentation scope.
M-1 PASS: original method bodies unchanged; gate first, then ResolveCodexExe or
File.Exists(GkSession.GrokExePath), then layout/process work. Gate reads only the
flag and throws; no ProcessStartInfo, Process.Start or binary lookup precedes it.
GET /api/runner-defaults and /api/session-runners read successfully; runtime global
default server2 and both desktop/server2 eligible at observation. No host/platform pin embedded.

The ordinary scope is complete; it is not an all-green certificate because the
intentional Grok lane is inherited red and platform/prerequisite skips remain.
No ordinary IDs deferred to a later Final round.

PC-1 PENDING for SourceLanding Mutation: always-enabled gate; method-scoped
Disabled_gate_skips_before_the_oracle_body*, variants null, empty, 0, true,
leading-space 1, trailing-space 1. Must fail Should.Throw/zero-body guard.
PC-2 PENDING: always-disabled gate; method-scoped
Enabled_gate_invokes_the_oracle_body; must fail the explicit-opt-in admission
assertion rather than skip. Mutation owns red/restore/green and missing-control discovery.
Standing Code contract reserves deliberate mutants for Mutation and takes
precedence over the brief's request for Code scratch mutants. None executed here.

## Execution and evidence

Each slice was committed/pushed before a run; each row received the exact full
committed SHA and a granted host build slot, waited=0s. Fresh TRX files inspected
for all intended classes/methods and parameter rows, nonzero counts and skip reasons.
CP-4 source receipt additionally passed the checkpoint tool's strict validate.
The two Unit lanes ran because a shared Pty test helper was added and Final
requires the whole Unit lane. No full integration-assembly run, namespace widening,
timeout change, loaded repetition or additional repeat-proof run.

Checkpoint tool run once per committed slice group:
`run --plan docs/superpowers/plans/2026-10-03-card-1007-provider-oracle-gate-plan.md --after S1 --expected-source-sha c4a723b830fa6e12934c3d962aaeeae7128d802a`
and `--after S2 --expected-source-sha f3af343ffef896fe2381c05f968a108e6d599a96`.
Both awaited to completion; every interim exit 75 followed by another foreground wait.

One intermediate S2 run (20261003-102756-3ba0 at 7709f32e0) was explicitly stopped
after discovering that an always-disabled gate would skip the enabled proof.
It completed CP-1 (7 pass/2 named skips), started the Unit build and was stopped;
it supplies no final evidence. Stop and wait completed before source edits.
Corrected committed slice reran the entire S2 list; no lingering owned process found.

Unlisted drivers, with reasons:
- Gated one-time checkpoint-tool bootstrap into bin-c1007-tool/; remaining tool
  invocations used that already built DLL or --no-build. Launcher wrappers all
  showed slot=granted/waited=0s. Direct DLL wait/validate/clean are control commands,
  not builds/test runs. Tool stop ended the deliberately abandoned intermediate run.
- BASELINE-GROK, direct scripts/run-checkpoint.ps1, exact failed method only, clean
  detached checkout at c6d5d56b5b4c565d36157e21de9c85029cc45b53. Required inherited-red
  qualification and red-method rerun; one isolated build, slot=granted waited=0s,
  one executed/one assertion failure matching final source. No fetch/pull/reset/rebase.
  No extra final enabled-lane repetition; only the failed method reran at baseline.

Provider prerequisites: command -v codex and command -v grok found /usr/local/bin
binaries. Codex used its existing ANTIPHON_CODEX_EXE override. Grok's existing
Windows-named resolver was supplied one owned temporary symlink to /usr/local/bin/grok;
removed with any newly created empty parent directories after baseline qualification.
Only debug prompt-input and inspect --json were run; no login, direct auth reads or model turns.

Original receipts and fresh TRX are preserved in `.antiphon/card1007-evidence.tar.gz`.
Extract with `tar -xzf .antiphon/card1007-evidence.tar.gz`; original paths are retained.
Original evidence roots:
- /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102539-1ae3
- /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35
- /work/worktrees/task-e1b6cb4a/.antiphon/c1007-baseline-grok/BASELINE-GROK-20261003-104353-bc44

Producer-owned bin-c1007-red/, bin-c1007-pty/, bin-c1007-unit/, bin-c1007-tool/
and detached-baseline outputs are removed before settlement. Temporary baseline
worktree and its owned empty parent are removed; original worktree stays clean.

Intentional oracle command (normal binary resolver prerequisites must exist):

```powershell
$env:ANTIPHON_PTY_PROVIDER_ORACLES='1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name provider-oracles -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-provider-oracles/ -Filter '/*/*/OrchestratorWorkspaceLayoutCanaryTests*/(Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root*)|(Grok_inspect_is_bounded_at_the_nested_checkout_git_root*)' -MinExecuted 2 -Expect 'Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root,Grok_inspect_is_bounded_at_the_nested_checkout_git_root' -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/provider-oracles
Remove-Item Env:ANTIPHON_PTY_PROVIDER_ORACLES
```

## FOLLOW-UPS

Completed board searches with -All for Grok inspect, Unit lane Linux and offline oracle.
Read CARD-0596 and CARD-0681 in full. CARD-0596 owns this independently reproduced
oracle assertion failure; no duplicate filed. CARD-0681 is closed as previously
fixed and the fresh full Unit lane has zero failures. Remaining positive controls
belong to caller-commissioned SourceLanding Mutation after ordinary Review and landing
of this original Code owner. No new structural defect requiring a separate card found.

## Unedited checkpoint receipts

--- checkpoint report ---
run: 20261003-102539-1ae3   manifest: /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102539-1ae3/manifest.resolved.yaml
commit: c4a723b830fa6e12934c3d962aaeeae7128d802a  branch: feat/card-task-e1b6cb4a  worktree: /work/worktrees/task-e1b6cb4a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: c4a723b830fa6e12934c3d962aaeeae7128d802a state=clean buildSource=verified
CHECKPOINT CP-0 commit=c4a723b830fa6e12934c3d962aaeeae7128d802a build=ok filter=/*/*/ProviderOracleGateTests*/Disabled_gate_skips_before_the_oracle_body executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102539-1ae3/rows/CP-0/run.trx slot=granted waited=0s dirty=0 source=c4a723b830fa6e12934c3d962aaeeae7128d802a sourceState=clean buildSource=verified
PHASES CP-0 slotWait=0s build=26.9545161s startup=4.2977215s testsWall=0.1327319s teardown=0.8948031s hostWall=5.3252643s
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
FAILED Antiphon.Agents.Pty.Tests.ProviderOracleGateTests.Disabled_gate_skips_before_the_oracle_body (CP-0) ShouldAssertException: type     should not be null but was  Additional Info:     ordinary provider oracle selection requires an opt-in gate -> rows/CP-0/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 0m34s  sequential-equivalent: 0m06s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1007-red/, bin-c1007-pty/, bin-c1007-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-102539-1ae3
evidence: /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102539-1ae3/report.md
verdict: RED exit=1
--- checkpoint report ---
run: 20261003-102925-ba35   manifest: /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/manifest.resolved.yaml
commit: f3af343ffef896fe2381c05f968a108e6d599a96  branch: feat/card-task-e1b6cb4a  worktree: /work/worktrees/task-e1b6cb4a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: f3af343ffef896fe2381c05f968a108e6d599a96 state=clean buildSource=verified
CHECKPOINT CP-1 commit=f3af343ffef896fe2381c05f968a108e6d599a96 build=ok filter=/*/*/(OrchestratorWorkspaceLayoutCanaryTests*)|(ProviderOracleGateTests*)/* executed=7 passed=7 failed=0 skipped=2 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f3af343ffef896fe2381c05f968a108e6d599a96 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=63.9208141s startup=4.480596s testsWall=0.2165854s teardown=1.0138259s hostWall=5.711036s
CHECKPOINT CP-2 commit=f3af343ffef896fe2381c05f968a108e6d599a96 build=reused filter=/*/*/*/*[Category=Unit] executed=321 passed=321 failed=0 skipped=2 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=f3af343ffef896fe2381c05f968a108e6d599a96 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=6.2406181s testsWall=8.0713018s teardown=0.7792091s hostWall=15.091129s
SLOW CLASS Antiphon.Agents.Pty.Tests.ClaudeEffortPromptTests 67s tests=64 (CP-2)
CHECKPOINT CP-4 commit=f3af343ffef896fe2381c05f968a108e6d599a96 build=reused filter=/*/*/(CheckpointManifestDocumentationTests*)|(ScopedVerificationInstructionTests*)|(RunnerDefaultGuidanceTests*)|(DockerStackDocumentationTests*)|(GrokRunnerImageContractTests*)|(CheckpointRepeatDocumentationTests*)/* executed=65 passed=65 failed=0 skipped=0 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=f3af343ffef896fe2381c05f968a108e6d599a96 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=321.6695464s startup=5.4639299s testsWall=0.5225956s teardown=0.7249251s hostWall=6.7114501s
CHECKPOINT CP-3 commit=f3af343ffef896fe2381c05f968a108e6d599a96 build=ok filter=/*/*/*/*[Category=Unit] executed=3910 passed=3910 failed=0 skipped=52 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=f3af343ffef896fe2381c05f968a108e6d599a96 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=182.0047407s testsWall=208.0261622s teardown=3.1366341s hostWall=393.1675367s
SLOW CLASS Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests 111s tests=65 (CP-3)
SLOW CLASS Antiphon.Tests.Checkpoints.PlanCoverageCommandTests 103s tests=10 (CP-3)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 97s tests=25 (CP-3)
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupPresentationTests 96s tests=33 (CP-3)
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 90s tests=65 (CP-3)
SLOW CLASS Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests 74s tests=4 (CP-3)
SLOW CLASS Antiphon.Tests.Application.ChannelOutboundStorageTests 71s tests=28 (CP-3)
SLOW CLASS Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests 69s tests=23 (CP-3)
SLOW CLASS Antiphon.Tests.Infrastructure.LogRetentionTests 61s tests=21 (CP-3)
CHECKPOINT CP-5 commit=f3af343ffef896fe2381c05f968a108e6d599a96 build=reused filter=/*/*/OrchestratorWorkspaceLayoutCanaryTests*/(Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root*)|(Grok_inspect_is_bounded_at_the_nested_checkout_git_root*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=f3af343ffef896fe2381c05f968a108e6d599a96 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=3.0752454s testsWall=0.8096947s teardown=0.3915393s hostWall=4.2764789s
FAILED Antiphon.Agents.Pty.Tests.OrchestratorWorkspaceLayoutCanaryTests.Grok_inspect_is_bounded_at_the_nested_checkout_git_root (CP-5) ShouldAssertException: projectPaths     should not be empty but was  Additional Info:     the checkout's AGENTS.md / CLAUDE.md must appear as project instructions -> rows/CP-5/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 13m28s  sequential-equivalent: 7m08s  builds: 3  max-concurrent-builds: 1  rows: 4 green 1 red 0 skipped
outputs: kept bin-c1007-red/, bin-c1007-pty/, bin-c1007-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-102925-ba35
evidence: /work/worktrees/task-e1b6cb4a/.antiphon/checkpoints/20261003-102925-ba35/report.md
verdict: RED exit=1

CHECKPOINT BASELINE-GROK commit=c6d5d56b5b4c565d36157e21de9c85029cc45b53 build=ok filter=/*/*/OrchestratorWorkspaceLayoutCanaryTests*/Grok_inspect_is_bounded_at_the_nested_checkout_git_root executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e1b6cb4a/.antiphon/c1007-baseline-grok/BASELINE-GROK-20261003-104353-bc44/run.trx slot=granted waited=0s dirty=0 source=c6d5d56b5b4c565d36157e21de9c85029cc45b53 sourceState=clean buildSource=verified
CHECKPOINT BASELINE-GROK EXIT CODE: 1
