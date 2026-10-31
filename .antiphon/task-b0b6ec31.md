Implemented S1c-executable diagnostic seams; all six portable witnesses and all six exact-method control rows pass. The canonical land graph -Check remains refused for changed project definitions; this report authorizes no land or native credit.

Base: f34e3c34f5a4d4d0b672d9e90dcdee1678566245. Assigned branch: feat/card-task-b0b6ec31. Verified executable source: 2196368b0d0c22039fdb6def279e6144169ed8aa. A later report-only commit carries this artifact; the executable receipts bind the source SHA above.

Code commits, each pushed immediately:

- d7702d1cb6d6be07fa348d8e37967e195030bd69: compilable red witnesses and behavior-preserving legacy seams, before the fix.
- d17040c0aac8f7e0491c649cf82deeddc0b28506: shared UID/context/lock admission, owned image selection, observed-image validation, core caller wiring and initial controls.
- 2196368b0d0c22039fdb6def279e6144169ed8aa: independent cl.bat/cl.exe cases, request environment seam, 30-second method caps and opt-in method-scoped controls.

Shared support is under /work/worktrees/task-b0b6ec31/tests/Shared/LiveClaude/: LiveClaudeLaunchContext.cs, LiveClaudeExecutable.cs, LiveClaudeExecutableAudit.cs, LiveClaudeExecutableCases.cs and LiveClaudeExecutableMutants.cs. Both test csproj files link these Compile inputs. The six tests live in tests/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests.cs and tests/Antiphon.Tests/Scripts/LiveLaneExecutableTests.cs. Live wiring changes cover ClSession.cs, ClaudeSubmitContractLiveTests.cs, ClaudeSubmitContractTests.cs, ClaudeRemoteControlAtStartupCanaryTests.cs, ClaudeRemoteControlMenuCanaryTests.cs and ClaudeDialogWatchdogCanaryTests.cs.

Core membership is determined from the supplied core identity before looking at context/environment or native eligibility. Admission compares UID, run, attempt, source SHA, profile identity and current lock ownership, then verifies the requested executable against the attested absolute path and bytes. Resolution performs file reads only. Core builders consume the admitted path directly; PATH decoys cannot select it. Non-core ClSession resolution retains its old behavior, exercised through the extracted legacy path scan over owned inert files. The request factory reads only ANTIPHON_LIVE_CLAUDE_EXE through an injected environment lookup.

The process callback boundary records the observed path/hash/PID/start/session and chain, refuses missing or mismatched post-launch observations, and rechecks observations, file bytes and lock state at teardown. Receipts record the supplied executed-image observation. These are synthetic portable observations. No native context issuer or image/session observer is supplied: native core boundaries retain a required skip on missing context, and RequireNativeObservation deliberately refuses unproven native credit. Actual native identity, interpreter/payload discovery and trust/profile/model isolation remain later commissions.

The red commit deliberately retained PATH selection and absent context/image gates, as required for compilable behavioral reds. Both projects built successfully. Every new method failed an assertion, with no native skip, build error, fixture error or zero-test result:

| Witness | Observed expected red | Final ordinary result |
|---|---|---|
| V-1 ClSessionUsesAttestedExecutable | owned probe/start image differs from attested image (PATH-cl selection) | passed |
| V-2 CoreCallersRefuseMissingContext | missing-context did not produce refusal before probe/spawn | passed |
| V-3 ExecutedImageMismatchRefuses | image-path mismatch did not refuse with 20 executable-mismatch | passed |
| V-4 C680UsesAttestedExecutable | owned probe/start image differs from attested image | passed |
| V-5 C680RefusesMissingContext | missing-context did not refuse before probe/spawn | passed |
| V-6 C680ImageMismatchRefuses | image-path mismatch did not refuse with 20 executable-mismatch | passed |

Final CP-17: 3 executed, 3 passed, 0 failed/skipped. Final CP-23: 3 executed, 3 passed, 0 failed/skipped. The six supporting PC rows each executed exactly one named method and passed, so the final run reports 12 executions across six distinct methods. Matrices are internal cases and do not inflate TUnit counts. Admission covers 13 negative variants per UID (all five PTY core identities and C680 separately); image validation covers seven observation faults at launch and teardown plus byte drift. V-1 independently covers wrapper cl.bat and MSVC-style cl.exe. Source audits use Roslyn method/block syntax and consumed builder shapes; they also reject comment, dead-call and ignored-admission copies.

Cheap in-process controls completed: 30 distinct PC IDs, 31 required variant cycles, missing=0. Mutated resolver copies must compile and fail the exact C1545 assertion, then the original source copy must pass. Caller controls mutate owned syntax copies and restore the original audit. No checkout mutation remains. Additional comment/dead/ignored syntax checks are supplementary and are not counted as companion PC variants.

| Exact-method row | Bound companion controls | Required cycles |
|---|---|---:|
| PC-V1 / ClSessionUsesAttestedExecutable | PC-1..7, including separate PC-1/cl.bat and PC-1/cl.exe | 8 |
| PC-V2 / CoreCallersRefuseMissingContext | PC-8, PC-10..18 | 10 |
| PC-V3 / ExecutedImageMismatchRefuses | PC-22..29 | 8 |
| PC-V4 / C680UsesAttestedExecutable | PC-19..20 | 2 |
| PC-V5 / C680RefusesMissingContext | PC-9, PC-21 | 2 |
| PC-V6 / C680ImageMismatchRefuses | PC-30 | 1 |

These cheap controls are the caller-authorized Code supplement. PC-31..141 (135 further variant cycles) and the formal forced-build/SourceLanding battery remain owed; no part of the planned 2,890-minute battery was run or credited as saved. Native CP-4..7 and CP-21 remain owed. Profile CP-18/24, startup CP-3, delivery CP-19, sanitization, reuse/signing and gate work remain separate. D-3/D-4/D-5/D-6/D-9/D-11 remain provisional.

Final execution used the companion import, fresh bin-b0b6-final-pty/ and bin-b0b6-final-c680/ outputs, UseAppHost=false, TEMP=/tmp, serial rows, a 16-minute scheduler build default, 2-minute PTY and 4-minute Antiphon.Tests rows, 30-second test caps, 26-minute charged execution and 29-minute wall caps. Slot acquisition allowed 25 minutes in keeping with the brief's 10–25-minute normal waits (the companion proposed five). All selected builds/rows obtained granted slots. Supporting PC rows reuse their run's freshly built outputs, with an exact method filter and row-only C1545_EXECUTABLE_CONTROLS=V-1 through V-6. This is the stated reason for six additional test rows beyond the two closed ordinary rows; there were no additional test builds for them.

The initial red and first-fix runs used the parent import's timeout defaults; their history remains below. The final run applies the companion execution/body caps. Two CLI preflights refused before any driver (the manifest option form and an abbreviated expected SHA); these are not assertion-red evidence. The only separately bootstrapped build was the required checkpoint tool, through scripts/build-slot.ps1: 45-second slot wait, 6.31-second build, 0 errors. Tool version 1.0.0+d7702d1cb6d6be07fa348d8e37967e195030bd69; DLL SHA-256 829d1b5b2c587747c2cc5d903d405ffe829be4973c9b7308d6e8c7b997f89f5b.

Measured whole-run costs retain admission separately:

| Run | Wall seconds | Charged execution seconds | Admission-only seconds |
|---|---:|---:|---:|
| expected red | 490.596 | 460.612 | 29.984 |
| first fix | 695.282 | 560.213 | 135.069 |
| final | 624.440 | 594.588 | 29.852 |

No individual row/run exceeded 30 minutes. Aggregate selected-run wall was 30m10.318s, excluding bootstrap and graph evaluation; Debug cost analysis remains owed before any further combined commissioning/repetition. Final PC method test-wall maxima were 17.285 seconds, under the provisional 30-second cap. These are small, differing-source samples, not calibrated p95. All six new parameterless methods produce TUnit0015 (missing TimeoutAttribute cancellation-token parameter). The declared TUnit timeout is present, but cooperative cancellation of these synchronous bodies is not proven; the checkpoint process deadlines remain the outer bound. The parameterless method contract was preserved. Review should retain this timing limitation rather than treating the measured durations as cancellation qualification. Slot/build/startup/test/teardown spans are retained below; independent file-IO/parse/callback spans are unknown, not invented. No Full, broad Unit, native, paid or provider run occurred.

Definition files changed: tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj and tests/Antiphon.Tests/Antiphon.Tests.csproj. The canonical command was pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tests/Antiphon.Tests/bin-b0b6-final-c680/land-graph-audit/Antiphon.LandGraphAudit.dll. Exit 1:

```text
digest=9D7BBFDE28CEF5693596472D737003825248CDA632285B8E17077AE0FD0CC528 count=68
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

To perform actual MSBuild evaluation despite that pre-check refusal, the same staged evaluator was run through scripts/build-slot.ps1 with --repo /work/worktrees/task-b0b6ec31 --solution Antiphon.sln. It exited 0 after both evaluation passes; its canary was removed. Evaluator DLL SHA-256 35d0ffda33ebc7bf3ef2448e4aafcd710f6519ce555872b3327ee8284f7a3a0f. This approval does not refresh or override the canonical graph record. No -Update ran; the definition-record gate remains incomplete and prevents land approval.

```text
BUILD SLOT granted lease=d915f0f4-9fb5-4290-93bb-dac8caf2f471 waited=0s maxcpucount=6
not-built: docs/investigations/evidence/card-0462/Reproduce.csproj
not-built: scripts/fixtures/nightly/c1375-probe/Probe.csproj
not-built: scripts/fixtures/nightly/c487-probe/Probe.csproj
not-built: tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj
not-built: tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj
canary=/work/worktrees/antiphon-landgraph-canary-47bdc6fcfa3e4ccd9bad4fd2e512e847
phase=pass2
approved
BUILD SLOT released lease=d915f0f4-9fb5-4290-93bb-dac8caf2f471 held=17s
```

The source-footprint audit compared actual dispatch-base Git objects with verified source 2196368b0d0c22039fdb6def279e6144169ed8aa. All five policy/reader files below are byte-identical:

| Path | Unchanged SHA-256 |
|---|---|
| tests/test-execution-policy.json | 994f8b6d608d44f4a36c8781db6e262b01fbbcb876790d6c8a1077c068deea1f |
| scripts/lib/nightly-common.ps1 | e36682d6b79148ee05f78ef97ed91c2f5b86a34c1acf18877e0a8c1f2fba5e43 |
| scripts/lib/nightly-policy.ps1 | 2b64405c0317cb2089a85e8b3a14ff9d30940d3ce2f375eaeac6bb636ecf0359 |
| scripts/lib/nightly-coverage.ps1 | 3c9cd3653bc853ff06f42e0d67b36bd89e2e57b46a843e40089535fa8998b26c |
| docs/release-gates.md | 89c923492e1c934e32feed75282a00e032bc536a086a38a67733715c8031a641 |

Existing Category/Explicit/Arguments/Test attributes and public test declarations in the five existing caller/fake files also compare equal. This is a source-footprint audit; the separate S1a compiled Full census is not claimed. C680 remains a required Full skip, not an exclusion or Full pass. Only the six new Unit methods are added by this slice. The full changed source footprint is confined to the test files listed above; release/cut/publish/supervisor files are untouched.

Evidence history guard over f34e3c34f5a4d4d0b672d9e90dcdee1678566245..2196368b0d0c22039fdb6def279e6144169ed8aa passed: commits=3, entries=0, violations=0. git fetch origin master resolved 256b861468518f1f18489798b2f109f48d78e0c8; git merge-tree --write-tree origin/master 2196368b0d0c22039fdb6def279e6144169ed8aa was clean, tree 88a0296a4266b4698e91e7fa30fb0b191f811d0f. No merge, rebase, force push, tag, deployment or activation occurred.

To reproduce, bootstrap tools/Antiphon.Checkpoints through scripts/build-slot.ps1 with a fresh alternate OutputPath and UseAppHost=false, import docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md, select CP-17/CP-23, and use the limits above. Regenerate fresh output names for every build invocation. Ordinary command shape:

```text
TEMP=/tmp dotnet <built-checkpoint-dll> import --plan docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md --out <owned-manifest.yaml>
TEMP=/tmp dotnet <built-checkpoint-dll> run <owned-manifest.yaml> --rows CP-17,CP-23 --expected-source-sha <full-reviewed-sha> --serial --total-timeout 26m --hard-wall-timeout 29m
```

For cheap controls, add six serial rows reusing those two builds, with the six exact method filters from the companion and C1545_EXECUTABLE_CONTROLS=V-1..V-6 respectively; Min=1 each. Payload sites and exact expected assertion predicates are bound in LiveClaudeExecutableMutants.cs and the two audit methods. Every wait was continued until terminal status; no owned executor remains.

Runtime evidence stays ignored under /work/worktrees/task-b0b6ec31/.antiphon/checkpoints/. Final run 20261031-064822-34ec: resolved-manifest SHA-256 ad7c837f19fa83572bdd8b92935dd36aeea71d618a15df7bb0ebe68de2c9d4e6; report.md SHA-256 c7f08f419a895992c2e34330c478086d3ea5c5bd84bc25341457aac9d9928a78; report.json SHA-256 ec2ecc79f63665583e9e58d40a6d93dcab9dacb229030dd91a9bfbcb2e9c2f69. The expected-source terminal wait validated the final receipts and returned 0, with sourceState=clean and buildSource=verified. Raw JSON/TRX/logs are not committed.

Unedited checkpoint provenance, expected red, run 20261031-062556-66df:

```text
CHECKPOINT CP-17 commit=d7702d1cb6d6be07fa348d8e37967e195030bd69 build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-062556-66df/rows/CP-17/run.trx slot=granted waited=15s dirty=0 source=d7702d1cb6d6be07fa348d8e37967e195030bd69 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-17 waited=15s executed=6.866s wall=21.922s
PHASES CP-17 slotWait=30s build=29.7300003s startup=4.4775871s testsWall=0.4786228s teardown=0.7063942s hostWall=5.6627129s
CHECKPOINT CP-23 commit=d7702d1cb6d6be07fa348d8e37967e195030bd69 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-062556-66df/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=d7702d1cb6d6be07fa348d8e37967e195030bd69 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-23 waited=0s executed=5.669s wall=5.718s
PHASES CP-23 slotWait=0s build=365.6405244s startup=3.7983583s testsWall=0.1119262s teardown=0.8184687s hostWall=4.7287504s
BUDGET wall=490.596s execution=460.612s admissionOnly=29.984s
verdict: RED exit=1
```

Unedited checkpoint provenance, first fix, run 20261031-063527-6f02:

```text
CHECKPOINT CP-17 commit=d17040c0aac8f7e0491c649cf82deeddc0b28506 build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-063527-6f02/rows/CP-17/run.trx slot=granted waited=15s dirty=0 source=d17040c0aac8f7e0491c649cf82deeddc0b28506 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-17 waited=15s executed=24.928s wall=39.978s
PHASES CP-17 slotWait=30s build=36.6012575s startup=2.2565408s testsWall=21.281865s teardown=0.690151s hostWall=24.228574s
CHECKPOINT CP-23 commit=d17040c0aac8f7e0491c649cf82deeddc0b28506 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-063527-6f02/rows/CP-23/run.trx slot=granted waited=30s dirty=0 source=d17040c0aac8f7e0491c649cf82deeddc0b28506 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-23 waited=30s executed=14.194s wall=44.304s
PHASES CP-23 slotWait=105s build=393.400985s startup=5.5453302s testsWall=6.3831334s teardown=0.5944319s hostWall=12.5228954s
BUDGET wall=695.282s execution=560.213s admissionOnly=135.069s
verdict: GREEN exit=0
```

Unedited checkpoint provenance, final ordinary + exact-method controls, run 20261031-064822-34ec:

```text
CHECKPOINT CP-17 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-17 waited=0s executed=3.704s wall=3.749s
PHASES CP-17 slotWait=15s build=69.7511251s startup=1.9510141s testsWall=0.4885266s teardown=0.5544334s hostWall=2.9939929s
CHECKPOINT PC-V1 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/ClSessionUsesAttestedExecutable executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V1/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V1 waited=0s executed=12.321s wall=12.362s
PHASES PC-V1 slotWait=0s build=0s startup=2.5011425s testsWall=8.4532439s teardown=0.6651507s hostWall=11.6195372s
CHECKPOINT PC-V2 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/CoreCallersRefuseMissingContext executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V2/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V2 waited=0s executed=21.629s wall=21.675s
PHASES PC-V2 slotWait=0s build=0s startup=2.992259s testsWall=17.2848901s teardown=0.6787248s hostWall=20.9558737s
CHECKPOINT PC-V3 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/ExecutedImageMismatchRefuses executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V3/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V3 waited=0s executed=15.860s wall=15.892s
PHASES PC-V3 slotWait=0s build=0s startup=2.1049671s testsWall=12.312304s teardown=0.7101713s hostWall=15.1274424s
CHECKPOINT CP-23 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-23 waited=0s executed=4.517s wall=4.566s
PHASES CP-23 slotWait=15s build=333.7171817s startup=2.6872905s testsWall=0.414954s teardown=0.5872416s hostWall=3.6894865s
CHECKPOINT PC-V4 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680UsesAttestedExecutable executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V4/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V4 waited=0s executed=5.763s wall=5.809s
PHASES PC-V4 slotWait=0s build=0s startup=4.4304288s testsWall=0.2994007s teardown=0.4554497s hostWall=5.185279s
CHECKPOINT PC-V5 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680RefusesMissingContext executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V5/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V5 waited=0s executed=10.856s wall=10.886s
PHASES PC-V5 slotWait=0s build=0s startup=3.5149217s testsWall=6.1644146s teardown=0.6888169s hostWall=10.3681534s
CHECKPOINT PC-V6 commit=2196368b0d0c22039fdb6def279e6144169ed8aa build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680ImageMismatchRefuses executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b0b6ec31/.antiphon/checkpoints/20261031-064822-34ec/rows/PC-V6/run.trx slot=granted waited=0s dirty=0 source=2196368b0d0c22039fdb6def279e6144169ed8aa sourceState=clean buildSource=verified
CHECKPOINT-TIMING PC-V6 waited=0s executed=4.280s wall=4.314s
PHASES PC-V6 slotWait=0s build=0s startup=3.024955s testsWall=0.2937793s teardown=0.4289793s hostWall=3.7477133s
BUDGET wall=624.440s execution=594.588s admissionOnly=29.852s
verdict: GREEN exit=0
```

Cleanup: all eight named bin-b0b6 output roots are removed after terminal runs and graph evaluation; checkpoint receipts/logs remain ignored. No cleanup touched foreign paths. The report is the only later tracked addition. No operator decision or additional approval was assumed.

--- next stage ---
next: review
handoff: Review the diagnostic S1c-executable source and evidence. Keep the stale definition-record gate incomplete; native/Full/reuse credit and all other S1c slices remain pending.
artifact: /work/worktrees/task-b0b6ec31/.antiphon/task-b0b6ec31.md
