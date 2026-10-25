# CARD-1514: Triage sweep 2, groups 3–17 and 66–74

Task `1ef9c224-17c2-44e0-aa6b-c61bfca95410`, 2026-10-25, server2/Linux.
This is a bounded triage census, not Windows qualification or release approval.
Only this report changes; no implementation or test fixes are authorized.

**Outcome:** All 22 requested classes completed on the master fetched at task start: **165 total, 125 passed, 35 failed, 5 skipped**, exit 2, 32m12.759s. Groups 3, 17, 66, 73 and 74 retain reproduced reds; groups 6, 10 and 67 are Linux-green. A final fetch found newer master with the observability fixture repair already landed; the freshness section below prevents treating all 35 as still-current or commissioning duplicate repairs.

## Source and execution provenance

- Fetched `origin/master` and tested its full SHA `f0911378c59116609161a57141e6fa35bd7d388c`, identical to assigned branch HEAD before the run. No branch switch, merge, rebase, or source transplant.
- Source and tests were unchanged during the single isolated build and combined execution. This is already a reproduction at the unchanged task base.
- Host: server2 container `0c18bb7f591e`, Debian 12, SDK 10.0.402, runtime .NET 9.0.21, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2.
- Docker info succeeded, server version 27.5.1; both requested real-Docker classes were included. Their own custody guards decide whether the daemon is admissible.
- One isolated build: **passed, 0 errors, 183 seconds slot-held**, MSBuild elapsed 3m02.05s; slot granted immediately with maxcpucount 6. Build warnings are preserved in the ignored log.
- No Unit lane, full assembly, unrequested class, checkpoint runner, live release command, deployment, or desktop operation was run. The two explicitly requested C1307 classes carry Unit categories but were selected by class alongside the other requested classes.
- The detached Windows run `a779085c`, candidate `a97194ee7a4a`, was neither queried nor altered. Windows failure counts in the brief are historical inputs, not new measurements.

Generated evidence root (ignored): `/work/worktrees/task-1ef9c224/.antiphon/triage-1ef9c224/`.
Files: `build.log`, `tests.log`, `results/triage.trx`, `rows.json`, `summary.json`, `assembly.sha256`, `source-sha.txt`.
The committed report preserves essential results because the task worktree may be retired. Ignored `c1008-evidence.json`, `c994-evidence.json`, `claude-evidence/`, and `environment-probes.txt` retain the additional mechanism witnesses.
This direct build-slot run does not produce checkpoint source-clean receipts; none are claimed.

## Reproduction commands

From this checkout at the tested SHA:

```sh
git fetch origin master
timeout --signal=TERM --kill-after=15s 900s pwsh -NoProfile -File scripts/build-slot.ps1 -Label triage-1ef9c224-build -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-triage-1ef9c224/ --nologo
timeout --signal=TERM --kill-after=20s 2400s pwsh -NoProfile -File scripts/build-slot.ps1 -Label triage-1ef9c224-tests -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-triage-1ef9c224/ -- --treenode-filter '/*/*/(ClaudeAdapterEffortPromptTests*)|(PhoneHomeEpochAgreementTests*)|(OperatorShutdownEndpointTests*)|(AgentTaskLandHalfResetTests*)|(AgentTaskWorktreeLockOutcomeTests*)|(LandingRegistrationFreshnessTests*)|(WorktreeRemovalAuthorityTests*)|(RepositoryMutationLeaseTests*)|(AmServiceDeployReadinessTests*)|(C1307CancellationTests*)|(C1307ToolRemovalTests*)|(RcProjectSelectionTests*)|(ReleaseBranchModelGateTests*)|(RollingVolumeRecycleDockerTests*)|(RcQualificationStatusTests*)|(ChangeAwareReleaseTests*)|(ReleaseGateAuthorityContractTests*)|(ReleaseGateE2EEvidenceTests*)|(RetiredTempContainerDockerTests*)|(ReleaseGateFollowupTests*)|(NightlyProbeSupervisorTests*)|(ReleaseGateStatusTests*)/*' --report-trx --report-trx-filename triage.trx --results-directory .antiphon/triage-1ef9c224/results
```

Use a fresh results directory when rerunning. Every OR operand has its own trailing wildcard.
Default script-fixture execution/cleanup budgets are 300/10 seconds (`tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:15,65,102`).
The Docker fixtures have their own 20-minute and 12-minute deadlines and owned cleanup (`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleDockerTests.cs:27`, `tests/Antiphon.Tests/Scripts/RetiredTempContainerDockerTests.cs:26`).
Outer limits are hard ceilings, not measured performance recommendations; no deadline or production setting was changed.

## Mechanisms and cluster attribution

Confirmed below means the run reproduces the stated failure and the cited source reconstructs the mechanism. It does not establish equivalence with every historical Windows failure.

| Cluster | Evidence and mechanism | Remaining uncertainty |
|---|---|---|
| G3-local-launch | Every effort fixture hard-codes `pwsh.exe` (`tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs:29,109`) and forces the Windows modern selector at `:101`. `ClaudeAdapter` passes the executable unchanged (`server/Infrastructure/Agents/Pty/ClaudeAdapter.cs:65`). PATH lookup on this Linux runner finds `pwsh` but no `pwsh.exe`. Four retained C1516 artifacts show empty raw/rendered screens, missing trace, empty logger and cleanup complete; the fifth method reports zero expected logger errors. **Confirmed nonportable executable selection; missing child startup is the supported cluster inference.** | Child exec errno was not retained, so the exact PTY startup failure is not proven. The five Linux failures are not proof of the four historical Windows effort failures. Native Windows effort/clearance rows still need execution. |
| G17-Linux-backend | `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs:54,57` feeds `modern` to `PtyDeliveryProfile` and asserts 86400. `server/Application/Services/PtyDeliveryProfile.cs:70` resolves the actual platform; `src/Antiphon.Agents.Pty/PtyBackend.cs:90` returns UnixPty before examining Windows selectors. `server/Application/Settings/DelegationSettings.cs:292` maps everything except ModernConPty to the conservative ceiling. Captured failures report expected 86400, actual 1024. **Confirmed Linux fixture/platform mismatch.** | Native modern rows must still run on Windows; this Linux setup assertion prevents their notification/delivery bodies from executing. |
| G17-diagnostic-code | `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs:100` demands `Git git_exit_128 exit=128`. Its `RemovalHarness` injects exit 128 for the first registration-removal vector (`tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs:592,598`; `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs:254,262`). Production now labels that operation `unregister_exit_128` (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:253`), which is rendered into the Git diagnostic (`server/Application/Services/WorktreeCleanupPresentation.cs:86`). The captured inbox failures stop at the old literal assertion. **Confirmed stale expected operation code, reconstructed from this executed path.** | Shouldly abbreviates the full body in the output; no fresh transcript-delivery verdict is claimed. Downstream delivery assertions were not reached. Historical CARD-0838 attribution is in `docs/investigations/2026-10-23-rc-5a126f6fc-qualification-groups-15-20-debug.md:68`. |
| G66-reflection-arity | `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:442` invokes the recovery worker. `tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs:56` calls `Activator.CreateInstance` with exactly `($git, $null)`. `server/Infrastructure/Git/RepositoryMutationLease.cs:6` now has three constructor parameters, including optional `RepositoryLeaseWaiters`. Reflection does not supply the omitted argument for this overload. Captured recovery-worker line 28 throws constructor-not-found, child exit 1 instead of 0. **Confirmed worker fixture constructor drift.** | The recovery fencing assertions after construction are not exercised; this failure is not evidence that a live child fence was incorrectly admitted. |
| G73-observability | `scripts/test-release-gate.ps1:319` copies current policy, but `:293` fixes discovery suites to antiphon/session-runner/pty-host/agents-pty/messaging/e2e/markdown-pdf, omitting observability. `Get-C599Discovery` iterates that list (`:334`); `New-C599Authority` uses it (`:411`). Current authority requires observability when its project disposition exists (`scripts/lib/release-authority.ps1:220`); an empty required UID set refuses at `:786`. Captured publication-positive rows report exactly `required-uids-empty:observability`, causing missing-tag downstream assertions. **Confirmed shared release fixture census drift.** | Negative guard rows can still pass despite this unrelated refusal; those passes alone do not prove each intended guard. Individual affected methods are listed in the census below. |
| G73-chunk-contract | `ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts` exits at `scripts/lib/nightly-tests-impl.ps1:608`, which refuses multiple bare classes without a method/group/exact filter. **Confirmed fixture invocation reaches a changed chunk contract.** | No E2E execution-artifact assertion is reached; no production E2E coverage defect is established by this exception. |
| G73-Windows-only | `RcQualificationStatusTests.C1500_WindowsSharedReadAndPs51` selects the fixture even on Linux (`tests/Antiphon.Tests/Scripts/RcQualificationStatusTests.cs:21`). `scripts/fixtures/c1500-status.ps1:127` explicitly throws `Native Windows fixture required; never a passing skip.` **Confirmed platform refusal, not a reproduced Windows sharing failure.** | Native Windows PowerShell 5.1 and pwsh shared-read evidence remains required. |
| G73-parameter-hop | `ChangeAwareReleaseTests.C1313_ParameterHops` reports `FAIL C1313 hop-baseline-bound`. `scripts/test-change-aware-release.ps1:302` is a conjunction over the consumed summary selection and ten bindings. Output shows the cut returning a selection, but does not identify the false comparison or retain the consumed summary. **Reproduced, narrow root unconfirmed.** | Retain the fixture summary plus the original bound selection and report each comparison separately before choosing a fix. Do not infer which field differed from the aggregate assertion. |
| G74-project-census | `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` captures `unmatched project scripts/fixtures/nightly/c1375-probe/Probe.csproj` in its child CLIXML, followed by seven failed harness assertions and exit 2 from the child hop. `tests/fixtures/card1336-release-gate-followups.ps1:71` copies current policy, but `:78` copies only `tests/*.csproj` plus the old c487 probe. `scripts/lib/nightly-policy.ps1:169` compares actual projects against every policy disposition and refuses the difference at `:184`. **Confirmed fixture project census drift.** | This refusal prevents the successful hop/completion/rc-green path from being verified. The expected-sha-mismatch from its deliberate moved-candidate negative row is separate and is not the primary failure. |
| G73-Docker-wrapper | Stored `/tmp/c994-real-3gSicv/evidence.json` has one RD-1 baseline result, failure `WrapperFailed:drain-temp:Rolling deploy stopped: HostJqTransportUnavailable`, and cleanup `all recorded containers, volumes, networks and fixture image absent`. The C994 wrapper copies only deploy-server2/c590-real (`scripts/fixtures/c1008-recycle-real-cases.mjs:358`) and clears `C727_TEST_STATE` (`:397`). The new host-jq fixture condition requires both VERIFY_STUB and STATE (`scripts/deploy-server2.ps1:75`), so it takes the real SSH prerequisite branch (`:84`). It then reads a helper at `:110` that is absent from the stored wrapper directory; the catch at `:120` maps transport/local setup errors to the captured refusal. **Confirmed fixture boundary drift and missing wrapper helper; exact caught exception is not retained.** | Docker worked and real owned containers were created; this is not a Docker-unavailable skip. Nine changed RD rows never ran. The fixture unexpectedly selects an SSH prerequisite check; no live deployment was requested or observed, and no manual SSH/deploy command was run. Preserve this boundary finding for the fixer; do not rerun it as a release command. |

## Native Windows obligations and platform limits

No selected class can be credited as Windows-qualified by this run. In particular:

- `ClaudeAdapterEffortPromptTests` hard-codes the Windows executable name and requires native Windows effort/clearance evidence; all five Linux rows failed before a populated fixture trace.

- `RcQualificationStatusTests.C1500_WindowsSharedReadAndPs51` needs native Windows PowerShell 5.1 and pwsh. Its Linux outcome is a failure by explicit platform refusal, not a skip or a reproduced file-sharing failure.
- `RepositoryMutationLeaseTests.C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` and `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` are explicit Linux skips (`tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:21,501`). Native inherited-handle and junction boundaries remain unverified here.
- `AmServiceDeployReadinessTests.ResolvesGitToolsWithoutUsrBinOnPathAsync`, `ResolvesGitToolsPastIncompleteInstallationAsync`, and `RefusesWhenNoCompleteGitInstallationExistsAsync` explicitly skip Linux (`tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:118,171,218`). The first three HTTP/curl cases do execute on Linux; Windows-specific refusal codes and native Git installation discovery still need Windows results.
- The ten G17 `modern` argument rows hit a Linux setup mismatch before the intended behavior. Their native modern backend execution still needs Windows; the ten inbox rows do execute far enough to expose the changed diagnostic code.
- `NightlyProbeSupervisorTests.C1375_DeadlineKillsAndJoinsTree` is **Linux-only**, explicitly throwing on Windows (`tests/Antiphon.Tests/Scripts/NightlyProbeSupervisorTests.cs:13`). Its historical Windows red is consistent with a platform/lane restriction, not evidence that the Linux expiry supervisor failed. No Windows rerun of this row can qualify the intended Linux boundary.

Linux passes for the remaining rows establish only Linux results at the tested SHA. They do not erase the historical Windows reds in groups 3, 6, 10, 66, 67, 73, or 74. This sweep does not reconstruct absent Windows transcripts or claim that a shared repair fixed each historical method.


## Per-group and per-class Linux census

Counts are fresh TRX outer test-case rows, including argument expansions; inner script/Docker assertion inventories are reported separately. Every requested class has nonzero results.

| Group | Class | Passed | Failed | Skipped | Cluster / interpretation |
|---|---|---:|---:|---:|---|
| 3 | ClaudeAdapterEffortPromptTests | 0 | 5 | 0 | G3-local-launch; native Windows still required |
| 3 | PhoneHomeEpochAgreementTests | 2 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 6 | OperatorShutdownEndpointTests | 3 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 10 | AgentTaskLandHalfResetTests | 55 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 17 | AgentTaskWorktreeLockOutcomeTests | 0 | 20 | 0 | 10 G17-Linux-backend + 10 G17-diagnostic-code |
| 66 | LandingRegistrationFreshnessTests | 7 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 66 | WorktreeRemovalAuthorityTests | 9 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 66 | RepositoryMutationLeaseTests | 19 | 1 | 2 | G66-reflection-arity + 2 native skips |
| 66 | AmServiceDeployReadinessTests | 3 | 0 | 3 | HTTP rows pass; 3 native Git-discovery skips |
| 67 | C1307CancellationTests | 5 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 67 | C1307ToolRemovalTests | 5 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 73 | RcProjectSelectionTests | 4 | 0 | 0 | No Linux reproduction; historical Windows cause not established by this run |
| 73 | ReleaseBranchModelGateTests | 1 | 2 | 0 | G73-observability; later repair landed |
| 73 | RollingVolumeRecycleDockerTests | 1 | 0 | 0 | 34 internal outcomes pass; owned cleanup confirmed |
| 73 | RcQualificationStatusTests | 2 | 1 | 0 | 2 pass; G73-Windows-only refusal |
| 73 | ChangeAwareReleaseTests | 2 | 1 | 0 | G73-parameter-hop; narrow cause open |
| 73 | ReleaseGateAuthorityContractTests | 2 | 1 | 0 | G73-observability; later repair landed |
| 73 | ReleaseGateE2EEvidenceTests | 2 | 1 | 0 | G73-chunk-contract |
| 73 | RetiredTempContainerDockerTests | 0 | 1 | 0 | G73-Docker-wrapper; one baseline internal row only |
| 74 | ReleaseGateFollowupTests | 1 | 1 | 0 | G74-project-census |
| 74 | NightlyProbeSupervisorTests | 1 | 0 | 0 | Native Linux row passes; explicitly rejects Windows |
| 74 | ReleaseGateStatusTests | 1 | 1 | 0 | G73-observability; later repair landed |

| Group | Passed | Failed | Skipped |
|---|---:|---:|---:|
| 3 | 2 | 5 | 0 |
| 6 | 3 | 0 | 0 |
| 10 | 55 | 0 | 0 |
| 17 | 0 | 20 | 0 |
| 66 | 38 | 1 | 5 |
| 67 | 10 | 0 | 0 |
| 73 | 14 | 7 | 0 |
| 74 | 3 | 2 | 0 |

## Every failing method and one-line error

Evidence references below are relative to `.antiphon/triage-1ef9c224/`. Names and argument values are verbatim TRX identities. The committed rows preserve the essential assertions from the ignored transcript; no full user/provider transcript is involved.

| Group / class | Failing test case | Error / decisive boundary | Captured test transcript |
|---|---|---|---|
| 3 / ClaudeAdapterEffortPromptTests | Local_adapter_names_an_uncleared_effort_dialog | FileNotFoundException: trace.json absent after readiness false. | tests.log:872 |
| 3 / ClaudeAdapterEffortPromptTests | Local_clear_failure_retains_evidence_before_scratch_cleanup | Rendered screen empty; expected probe token zze1ce6f85 (line 135). | tests.log:892 |
| 3 / ClaudeAdapterEffortPromptTests | Local_adapter_keeps_requested_effort_and_probes_after_clearance(high) | Expected readiness true; actual false, empty screen (line 141). | tests.log:934 |
| 3 / ClaudeAdapterEffortPromptTests | Local_adapter_keeps_requested_effort_and_probes_after_clearance(xhigh) | Expected readiness true; actual false, empty screen (line 141). | tests.log:979 |
| 3 / ClaudeAdapterEffortPromptTests | Local_probe_failure_logs_token_elapsed_and_writes | Expected one logger error; actual zero (line 34). | tests.log:1024 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsOutcome(False, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:14 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsUnlandedSiblingWarning(True, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:35 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsOutcome(True, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:65 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsUnlandedSiblingWarning(True, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:86 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsUnlandedSiblingWarning(False, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:116 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(False, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:146 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsOutcome(True, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:176 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsOutcomeWhenIdle(False, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:206 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(False, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:224 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(True, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:242 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsUnlandedSiblingWarning(False, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:260 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsUnlandedSiblingWarning(False, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:285 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsUnlandedSiblingWarning(True, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:309 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsUnlandedSiblingWarning(False, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:327 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(True, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:345 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsOutcomeWhenIdle(True, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:370 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsOutcomeWhenIdle(True, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:395 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsOutcomeWhenIdle(False, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:413 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_EligibleRecipientGetsOutcome(False, modern) | Expected SingleWriteMaxBytes 86400; actual 1024 (setup, line 57). | tests.log:438 |
| 17 / AgentTaskWorktreeLockOutcomeTests | C443_BusyRecipientGetsUnlandedSiblingWarning(True, inbox) | Expected Body to contain Git git_exit_128 exit=128; assertion line 100 fails. | tests.log:463 |
| 66 / RepositoryMutationLeaseTests | C448_C24_KilledWorkerLeavesLiveGitChildFenced | Recovery child exit 1, expected 0: RepositoryMutationLease constructor not found for two arguments. | tests.log:483 |
| 73 / ReleaseBranchModelGateTests | C1311_PreReleaseTag | Publication refuses required-uids-empty:observability; tag/control row fails (7/1 inner). | tests.log:576 |
| 73 / ReleaseBranchModelGateTests | C1311_MovingTipAuthority | Publication refuses required-uids-empty:observability; missing first tag and promote empty-tag (4/2 inner). | tests.log:617 |
| 73 / RcQualificationStatusTests | C1500_WindowsSharedReadAndPs51 | Native Windows fixture required; never a passing skip (host exit 1). | tests.log:550 |
| 73 / ChangeAwareReleaseTests | C1313_ParameterHops | FAIL C1313 hop-baseline-bound; harness 0 passed / 1 failed; false comparison unidentified. | tests.log:516 |
| 73 / ReleaseGateAuthorityContractTests | C599B_AllChunks | Positive complete multi-chunk publication refuses required-uids-empty:observability (5/1 inner). | tests.log:653 |
| 73 / ReleaseGateE2EEvidenceTests | C599_ExecutionArtifacts | multiple classes require split exact roster chunks; exception before any artifact assertions. | tests.log:689 |
| 73 / RetiredTempContainerDockerTests | C994_Real_retired_temp_lifecycle | WrapperFailed:drain-temp: HostJqTransportUnavailable; outer child exit 1; 1 baseline/0 changed rows. | tests.log:819 |
| 74 / ReleaseGateFollowupTests | C1336_NightlyCandidateIdentity | unmatched project scripts/fixtures/nightly/c1375-probe/Probe.csproj; child exit 2 (18/7 inner). | tests.log:715 |
| 74 / ReleaseGateStatusTests | C599_NoActivation | Publication control refuses required-uids-empty:observability; no push observed (11/2 inner). | tests.log:779 |

The five skipped TRX identities are the three AmService Git-discovery methods and two RepositoryMutationLease Windows-boundary methods listed above. No Docker class was skipped.

## End-fetch freshness: avoid duplicate Debug work

The authorized single build/run used `f0911378c59116609161a57141e6fa35bd7d388c`. A final fetch at approximately 02:42 UTC found master advanced to **`f5456c2663d85ef3cc66c02f2d3ccaf393252857`**. No second build/run was authorized by the brief, and no results at that newer SHA are claimed.

Source audit of that delta finds the exact missing-observability fixture repair already present in **`501ebf1b3b48494104f54078542e006e2d1edcee`**: it adds `observability` to `scripts/test-release-gate.ps1`'s native suite list. This covers the reconstructed mechanism for four observed failed outer rows:

- G73 `ReleaseBranchModelGateTests`: `C1311_PreReleaseTag`, `C1311_MovingTipAuthority` (2).
- G73 `ReleaseGateAuthorityContractTests.C599B_AllChunks` (1).
- G74 `ReleaseGateStatusTests.C599_NoActivation` (1).

Treat these four as **repair landed, verification outstanding**, not four current-tip reproduced failures or four new fixer requests. No green outcome is inferred from the source diff.

The other recent release-harness changes (`0cd36ac0258ee2872522911b89360c1b5a13c484`, `3c94a31d4b3f44628d5d0d0122616d29c1de7ca5`) change `scripts/test-release-gate.ps1`'s New-C599RunFx project census and its run-summary/platform observation. They do **not** change the independently located `tests/fixtures/card1336-release-gate-followups.ps1`, `scripts/test-change-aware-release.ps1`, or the bare multi-class call at `scripts/test-release-gate.ps1:2082`. Do not conflate similarly named RC-hop fixtures. Other later changes touch shared queue/runtime components, so the complete 165-row outcome remains bound to the tested SHA.

## Ranked Debug handoff

This ranks unaddressed reproduced rows after discounting the four mechanism-matching landed repairs. Counts are from the tested SHA, not a new execution of end-fetch master.

| Rank | Group | Debug scope / observed failed rows | Admission limits |
|---:|---:|---|---|
| 1 | 17 | `AgentTaskWorktreeLockOutcomeTests`: **20** (10 Linux modern setup mismatches + 10 old diagnostic-code assertions). | Preserve separate platform and diagnostic clusters. Intended delivery assertions were not reached. Native Windows rows still needed. |
| 2 | 3 | `ClaudeAdapterEffortPromptTests`: **5**. `PhoneHomeEpochAgreementTests` passed both rows. | Linux hard-coded executable/startup cluster; do not claim it explains the historical Windows failures. Retain child launch/exit evidence and obtain native effort rows. |
| 3 | 73 | `ChangeAwareReleaseTests`: **1**; `ReleaseGateE2EEvidenceTests`: **1**; `RetiredTempContainerDockerTests`: **1**. | Parameter-hop cause needs consumed-summary/bound-selection evidence. Docker fixture unexpectedly reaches real SSH prerequisite admission and lacks the helper. Keep those causes separate. |
| 4 | 66 | `RepositoryMutationLeaseTests`: **1** constructor-arity failure. | Recovery behavior after worker construction remains untested; 2 native rows skipped. Other G66 Linux rows passed. |
| 5 | 74 | `ReleaseGateFollowupTests`: **1** project-census failure. | Independent fixture still omits c1375 probe; not repaired by the similarly named C599 helper change. Linux-only NightlyProbeSupervisor passed. |

Additional G73 native obligation: `RcQualificationStatusTests` has **1 failed Windows-only row**, which needs native Windows evidence, not an inferred Linux product fix. Three G73 observability rows and one G74 observability row belong to verification of the landed repair rather than duplicate Debug work. Groups 6, 10 and 67 need no Linux Debug fixer from this sample; their original Windows reds remain native verification obligations.

## Performance and retained evidence

- Test host terminal summary: 165 total / 125 succeeded / 35 failed / 5 skipped, **32m12.759s**, wrapper exit 2. Slot granted immediately, held 1937 seconds, then released. The 40-minute outer limit did not fire. There were no TRX timeout/aborted/error outcomes.
- Build: 1197 warnings, 0 errors, MSBuild 182.05 seconds / slot-held 183 seconds. No additional build or test invocation followed the red run.
- Slowest outer rows: rolling Docker 786.797s; C1313_ModeCredit 212.259s; C1313_BaselineAuthority 189.958s; C1336_NightlyCandidateIdentity 152.484s; retired Docker 66.981s. These are single shared-host observations, not p95 measurements. Strict process-spawn scheduling serializes much of this selection; no speedup attribution is claimed.
- The combined selection exceeds the owner's 30-minute threshold. Carry this measured batch cost into the CARD-1514 Debug/performance follow-up; repeating it or widening budgets is not a remedy established here. Fixture phase instrumentation beyond existing command timings is incomplete.
- C1008 stored ledger: **34 outcomes (5 baseline, 29 changed), no failure**, owned containers/volumes/networks/image absent; real df evidence retained. C994 stored ledger: **1 baseline outcome, 0 changed outcomes**, wrapper error above, owned cleanup complete. These internal counts do not replace the TRX's one outer case per Docker class.
- Tested DLL: 68,573,184 bytes; SHA-256 `a42be4cdc73153380a914701f98cb4d5dc8de4d484aa031b77dc63d02d8cf88b`. This exceeds the checkpoint runner's 64 MiB boundary, so the brief's direct build-slot route was used.
- TRX SHA-256: `3944152525c9069ad039085ce70c91e68aeaa35bc552ee9ff53c8d4e8e8b48a0`.
- Test transcript SHA-256: `dfdc33c1141dca005d4c8572d2aaf667c5e83b12f298dd758a80564c70cad9ec`.
- Four C1516 artifacts were copied from the producer output to `.antiphon/triage-1ef9c224/claude-evidence/` before output cleanup. They preserve empty terminal observations, missing trace, empty logger, primary assertions and `cleanup: complete`. The fifth, logger-only case does not publish a C1516 artifact; its failure is retained in the TRX.
- Generated logs, TRX, JSON, hashes, HTML and retained diagnostic text remain ignored. All 33 producer-owned `bin-triage-1ef9c224` output roots listed in `output-directories.txt` were removed after the test host exited. Exact-label Docker censuses for both task-owned fixture prefixes were empty after execution. No other producer's output was removed.

## Remaining uncertainty / resolution

Current-tip execution, native Windows evidence, the exact C1313 false comparison, Claude child exec errno, and the exact exception hidden behind HostJqTransportUnavailable remain open. Resolve them through the named bounded Debug/native rows with retained per-comparison/child-start evidence. The confirmed mechanisms above support scoped repair commissioning; this census does not establish a single common root for all 35 failures or prove every original Windows failure fixed.

## Not done, noted

No fix was designed or implemented; repairs, native Windows qualification and verification of the four already-landed fixture repairs remain separate work.

--- next stage ---
next: investigate
handoff: Dispatch bounded Debug work for groups 17 (20), 3 (5), 73 (3 plus one native Windows row), 66 (1), and 74 (1); counts bind f0911378. Verify the four observability rows repaired after the run by 501ebf1b3 rather than duplicate that fix. Retain C1313 consumed-summary and Claude child-start evidence; preserve Windows/platform distinctions and the 32m13s batch cost.
artifact: docs/investigations/2026-10-25-card-1514-triage-sweep-2-groups-3-17-66-74.md
