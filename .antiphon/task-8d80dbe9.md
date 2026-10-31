S3b is complete and pushed: uncertain journal liveness returns unknown, both failed/unfinished lanes resume after each suite's last pass, and CP-6/7/8 pass at the new source. Portable S3 is ready for ordinary Review together with S2; no operational Windows activation is claimed.

Branch: feat/card-task-8d80dbe9. Assigned base: de2e0b94ce40b7e97c00f5a4a452cdf862db1b68.
Tested source: 593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1. The subsequent report-only commit does not change implementation or verification selections.
Standing caller authority covered the work; no additional approval was requested.

Implementation and completed S3 scope

- scripts/lib/qualification-supervisor-heartbeat.ps1: add Assert-QualificationJournalUnsettled. A present journal must have a real nonlinked generation, valid pointer/version/root and metadata digest, matching generation/run/attempt/source, a complete contiguous digest-valid prefix, consistent optional publication state, no settlement of the Active key and no terminal seal. Recovered/seeded or unresolved custody returns unknown conservatively. Pointer/metadata and record roster are checked again after reading. Full publication remains diagnostic; no launch or release-credit gate was added.
- The observed 1800 is plannedSeconds (the chunk's legitimate price), not an age field or a hard-coded age default. The defect allowed that price despite an unprovable journal generation. Missing-generation now returns null and Get-QualificationHeartbeatIdleBudget uses its existing 20-minute default-unknown-group budget. This detection library never declares a process dead or stops one. Existing isolated plan producers without any journal remain supported; once a journal exists, missing pointer/generation evidence cannot be treated as an empty successful prefix.
- scripts/lib/nightly-dispatch.ps1: explicit unknown/malformed liveness in a progress record returns the original order and previous-run-liveness-unknown, with empty lane cursors and without modifying/classifying the old record. Failed and unfinished partitions now both rotate after the last passed chunk per suite; passed work remains last. Keyed permutation validation remains intact.
- scripts/lib/nightly-failure-history.ps1: optional history ingress rejects explicit unproved progress liveness into a stated history-liveness-unknown fallback, clearing both history and previous progress. A valid closed summary remains usable without inferring process death from a heartbeat. Ordinary accounted failed chunks retain S1 behavior; no export/accounting or Partial resolver rule changed.
- scripts/test-nightly-failed-first.ps1 and RcFailedFirstExecutionTests.cs: CP-6 now requires 13 named assertions, including 17 independently bound liveness variants and unknown-history ingress/pure-order controls. A separate short PreviousOrder diagnostic exercises two suites with failures before and after their last pass. CP-7 retains 27 named assertions.
- Both plan copies are identical and carry the commissioned S3b contingency, one extra isolated build, fresh bin-8d80-s3b1/ output and provisional 17-minute ordinary estimate (cumulative planned ordinary cost 95 minutes; historical costs retained). No production/native watchdog or test timeout was loosened.

The inherited S3 public chain is complete for future Full candidates: scripts/nightly-run.ps1 -> Invoke-AntiphonNightlyRun/Core -> sync/self-reexec -> scripts/nightly-tests.ps1 -> Invoke-AntiphonNightlyTests -> Invoke-NightlyDispatchCoordinator. PriorFailuresPath/PriorFailuresDigest are forwarded, a bound copy survives sync outside the checkout, and the receiving executor revalidates it. The actual prepared schedule is consumed serially across suites with per-suite state and one run journal context. These forwarding/execution changes were already present at the assigned base; this task verified the integrated entrypoint and completed its liveness/order obligations. No private supervisor exists in this repository. Private commissioning hops/readback remain external work; no claim that an existing private invocation automatically forwards new flags.

Graph/default-planner reuse: Resolve-NightlyPreviousRunOrder is the pure deterministic ordering function (canonical entries, normalized previous record, candidate SHA and caller-supplied UTC time -> entries/reason/lane cursors). New-NightlyDispatchSchedule binds canonical object references and verifies the exact permutation/digest. Resolve-NightlyPriorFailures maps historical classes to current Full chunks. Initialize-NightlyDispatchHistory is the IO/fallback adapter; Invoke-NightlyDispatchCoordinator and Invoke-NightlyPreparedSchedule are execution boundaries. No completed work is reused or credited.

RED-first evidence and guard controls

1. d533ede98ef72dea8a0ebb24b986338e2e8c5f81 (test/scaffold only, inherited production): gated HeartbeatBindings executed 4 assertions, passed 3, failed 1. Valid-empty/valid-prefix controls returned 1800. Missing generation/metadata, independently rebound metadata run/attempt/source/version/digest, pointer root/version, gap, truncation, terminal and seed cases incorrectly returned 1800; unknown history also reordered. Malformed-record corruption and settled-key guards already rejected. Log: .antiphon/8d80-liveness-red.log. Lease wait 0s, held 8s. This reproduces the inherited acceptance defect, not a new ordinary regression.
2. 9ab295eb369a437beee16fe2bbdadbefad08de55: narrow changed-seam diagnostic passed 4/4, zero failures, including all 17 liveness variants and both history controls. Wait 0s, held 14s. Historical green at this SHA only; final CP-6 verifies the later source. Log: .antiphon/8d80-liveness-green.log.
3. 25781f8aaa84e4179418e507b67d9dd03b156f8e: gated PreviousOrder passed 0/1, failed progressOrder. Actual A1,A4,B1,B5,A5,A2,B4,B2,A3,B3; expected A4,A1,B5,B1,A5,A2,B4,B2,A3,B3. Wait 0s, held 6s. Final CP-6 confirms the fix. Log: .antiphon/8d80-order-red.log.

Discriminating later method-scoped Mutation controls: restore the old journal evidence bypass/1800 result -> missing-generation/metadata witness fails; omit metadata digest -> metadata-digest; omit individual metadata run/attempt/source/version binding -> its independently rebound row; omit pointer root/version -> corresponding row; accept incomplete/gapped/corrupt prefix or inconsistent state -> corrupt/gap/truncated rows; ignore settled key or terminal seal -> settled/terminal; accept unverified seed -> seeded; remove either unknown-liveness fallback -> pure-order/ingress checks; rotate only unfinished or restore canonical failed ordering -> the two-suite progressOrder witness. Existing valid-empty/valid-prefix and legacy 1800/120 controls prevent an always-unknown implementation. These are guard-to-witness designs plus baseline RED evidence, not post-land SourceLanding Mutation credit; the earlier S2/S3 mutation pack remains owed.

Verification and costs

CP-6: executed 1 / passed 1 / failed 0 / skipped 0; inner 13 assertions, all passed, including 17 liveness variants. CP-7: 1/1/0/0; inner 27 assertions, all passed. CP-8: 1/1/0/0; existing authority-byte witness. Exact filters, source/slot/build proof and phase costs are retained verbatim below. Tool source validation accepted all three rows. One isolated build group; three serial exact-method rows; six other declared manifest builds unused.

Run 20261031-081531-b3a4 exited 0. Wall 19m50s; execution 1190.600s; admission-only 0.249s. Build 376.032s; test bodies CP-6 439.119s, CP-7 86.404s, CP-8 138.684s. Output cleanup 107.644s. Samples remain provisional, not p95. Phase/operation/assertion archives are ignored under .antiphon/failfirst/593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1/portable/. No operational first-failure latency measured. Retained D-2 advisory: the inherited authority fixture uses private loopback HTTP while generic performance metadata says HTTP not-used; HTTP attribution is unknown, not zero. No rerun solely for that advisory.

All drivers used host build slots, TEMP=/tmp and isolated outputs, UseAppHost=false. Timing-sensitive rows ran alone. The tool bootstrap passed with 0 errors / 7 warnings in unchanged tool/dependency source, held 7s. The ordinary isolated build had 0 errors / 1429 warnings; no claim those warnings are new. Direct PowerShell runs above were explicitly red-first/changed-seam diagnostics, not substitute checkpoint credit. No native tests/full assembly ran. An initial Python edit command found no python3 and changed nothing; an initial abbreviated expected SHA was refused before any checkpoint build/test. Neither is assertion RED. No failed required row or interrupted run is hidden.

No definition file changed. No -Update ran. The real scripts/audit-land-verification-graph.ps1 -Check passed through the gate using eight SHA-inventoried audit files copied from this verified checkpoint build. Provenance: .antiphon/8d80-land-tool-provenance.json. Digest E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806, count69, pass2 approved, wait0/held31s. Five known not-built projects are recorded in .antiphon/8d80-land-check.log.

Fresh fetched target simulation: origin/rc/2026-10-27 at 53835c8c2033618a76e6cb512f386e31c11e4ed7 merges cleanly, tree 65cda08fc490ec5299910e5cb59176e5fd809163. origin/master at 96715b56623d57a16c6788838bd18281a40e768d has exactly the same 48 conflict paths at both assigned base and tested source, none newly introduced. This rc work does not claim master landability. No merge/rebase/reset/amend/force-push, target update, tag, publication, deployment, desktop read, C:\Antiphon change or live d47848cc access occurred.

Evidence guard over assigned base..tested source: five commits, zero generated-evidence entries, zero violations. The final report-only range is checked after publication. Compiler outputs were deleted by the checkpoint tool. After the last command, 51 hash-verified files and 20 directories were removed nonrecursively from exactly src/Antiphon.HostCleanup/bin-8d80-tool1, tools/Antiphon.Checkpoints/bin-8d80-tool1 and .antiphon/8d80-land-tool; inventory retained ignored. All owned clients/executors were awaited and leases released.

Owed work

- Ordinary Review of S2 and completed S3 together, then authorized landing and bound method-scoped Mutation.
- S4a CP-9/10 Partial producers; S4b CP-11/12 independent consumers; S5 portable CP-13/14 hop/report/acceptance matrices. S3's implemented Full flags do not claim those later mode/consumer guarantees.
- Windows CP-15/16 are OWED, not skipped or green. No native tests were run here. Exact native commands from a fresh authorized Windows checkout, after the S5-W implementation exists:

```powershell
$env:TEMP=$env:TMP
$plan='docs/superpowers/plans/2026-10-30-card-1514-failed-first-qualification-ordering-plan.md'
$sha=(git rev-parse HEAD)
pwsh -NoProfile -File scripts/build-slot.ps1 -Label failfirst-native-tool -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj --property:OutputPath=bin-8d80-native-tool1/ --property:UseAppHost=false
$tool='tools/Antiphon.Checkpoints/bin-8d80-native-tool1/Antiphon.Checkpoints.dll'
dotnet $tool import --plan $plan --out .antiphon/failfirst-native.yaml
dotnet $tool run --plan $plan --rows CP-15,CP-16 --expected-source-sha $sha --row-timeout 12m --total-timeout 28m --hard-wall-timeout 30m --serial
# If run returns 75, await the returned run ID:
dotnet $tool wait --run '<returned-run-id>' --max-wait 1m
```

Native exact filters: /*/Antiphon.Tests.Scripts/RcFailureHistoryNativeTests/WindowsHopAndLock and /*/Antiphon.Tests.Scripts/RcFailureHistoryNativeTests/WindowsJournalAndScope. Request Windows placement, no host pin. Use the reviewed authorized checkout and existing shared coordination root. Private supervisor history/identity forwarding and explicit/legacy price readback, operational Full qualification, first-failure latency and the separate master port remain commissioning work. Nothing is to be applied to d47848cc.

Pushed implementation/documentation commits

- d533ede98ef72dea8a0ebb24b986338e2e8c5f81 test: reject unprovable journal liveness and preserve original order
- 9ab295eb369a437beee16fe2bbdadbefad08de55 fix: return unknown for unproven journal liveness
- 25781f8aaa84e4179418e507b67d9dd03b156f8e test: require both priority lanes to resume after each suite last pass
- 3d93f4fb71377ac4ca4b220ff4bd15dd626220e2 fix: rotate failed and unfinished work after the last pass per suite
- 593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 docs: bind S3b liveness completion and extra isolated verification cost

Essential unedited checkpoint provenance

```text
--- checkpoint report ---
run: 20261031-081531-b3a4   manifest: /work/worktrees/task-8d80dbe9/.antiphon/checkpoints/20261031-081531-b3a4/manifest.resolved.yaml
commit: 593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1  branch: feat/card-task-8d80dbe9  worktree: /work/worktrees/task-8d80dbe9  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 state=clean buildSource=verified
CHECKPOINT CP-6 commit=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 build=ok filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/ActualLoopFollowsSchedule executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d80dbe9/.antiphon/checkpoints/20261031-081531-b3a4/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=445.074s wall=445.923s
PHASES CP-6 slotWait=0s build=376.0320812s startup=4.1971021s testsWall=439.1191514s teardown=1.038635s hostWall=444.3551521s
SLOW CLASS Antiphon.Tests.Scripts.RcFailedFirstExecutionTests 439s tests=1 (CP-6)
CHECKPOINT CP-7 commit=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/JournalCustodySurvivesInterleaving executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d80dbe9/.antiphon/checkpoints/20261031-081531-b3a4/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=92.073s wall=92.127s
PHASES CP-7 slotWait=0s build=0s startup=4.3140395s testsWall=86.4038474s teardown=0.5596585s hostWall=91.277545s
SLOW CLASS Antiphon.Tests.Scripts.RcFailedFirstExecutionTests 86s tests=1 (CP-7)
CHECKPOINT CP-8 commit=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8d80dbe9/.antiphon/checkpoints/20261031-081531-b3a4/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=144.255s wall=144.293s
PHASES CP-8 slotWait=0s build=0s startup=4.1855092s testsWall=138.6839998s teardown=0.6247867s hostWall=143.4942953s
SLOW CLASS Antiphon.Tests.TestHelpers.RcManifestIntegrationTests 138s tests=1 (CP-8)
BUDGET wall=1190.850s execution=1190.600s admissionOnly=0.249s
unlisted: none (the tool ran no other build or test command)
wall: 19m50s  sequential-equivalent: 11m22s  builds: 7  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-8d80-s3b1/
evidence: /work/worktrees/task-8d80dbe9/.antiphon/checkpoints/20261031-081531-b3a4/report.md
verdict: GREEN exit=0
```

```text
CHECKPOINT SOURCE VALID source=593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1 rows=3
```

--- next stage ---
next: review
handoff: Review S2 at c2d6f95582f041beaa33890af4d0ff83c83ee6a4 together with completed S3 at tested source 593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1; CP-6/7/8 are green and source validated. Native and later scopes above remain owed. Report publication is narrative-only.
artifact: /work/worktrees/task-8d80dbe9/.antiphon/task-8d80dbe9.md
