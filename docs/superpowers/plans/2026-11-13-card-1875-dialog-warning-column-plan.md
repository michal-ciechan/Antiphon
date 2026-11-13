# CARD-1875: anchor heading-less unsafe-delete warnings

Code task / landing owner: c6cfa115. Base: `7e2e7d9173e550588ecdb379c21b536edfb5fef2`.
Brief: `.antiphon/inbox/b461cda1-cf57-48a2-8290-f523f42b51f7.md`.
Review b2cb1533 identified command-preview warning text with no description row as
an advisory false positive. This focused implementation follows that brief.

## Invariant and slices

Only the warning box at the confirmation question's column can authorize the
unsafe-delete refusal. Command preview borders sit two columns further right.
Keep heading-less captured dialogs working, including when the description is
scrolled away. Keep epoch/revision/input-history/adoption fences and exact `2`
without Enter unchanged. Do not change deadlines or approval behavior.

- S1: commit a red runner witness for the exact heading-less, description-less
  command-preview layout, plus the same negative row in the server consumer.
- S2: compare the warning border and question columns; add ordinary dialog
  exclusions and an aligned heading-less positive control.

## Performance budget

No full assembly or Unit lane is needed for this bounded shared predicate.
Named consumers are ClaudeDialogInputGuardTests and WatchdogServiceTests.
The impacted `pty-host-fixture` glob also selects Card1277PtyHostFixtureTests;
the native Windows copy remains owed to the Windows lane, never credited by Linux.
Guard coverage includes the brief's six classes plus the standing mechanical,
platform, layering and lane-category guards.

Review b2cb1533 measured its 89-result server/guard selection at 7m58s on Linux;
the runner class had four green results (no individual durations supplied).
Estimates below are provisional cold-build-inclusive budgets, not measured p95.
Collect one ordinary sample per row for CARD-1875; do not repeat after green.
The importer supplies existing row/build deadlines; this change adds no timeout.
Admission remains bounded by the tool's default 45-minute slot wait, separately
reported from build and execution. No row is expected to exceed 30 minutes.

Runner cases use existing in-memory child/clock fixtures and owned scratch paths.
Watchdog uses the existing shared PostgreSQL setup and per-case isolation; no new
host/container/migration fixture is introduced. The impacted class owns its fake
PTY children and their cleanup. TRX supplies per-test times; tool receipts supply
build, slot and run wall times. Uninstrumented fixture/DB/IO phase splits remain
unknown, not zero. Direct graph: Agents.Pty -> SessionRunner and server consumers;
both named projects build their transitive references. No OS-selected production
behavior or startup factory changes. Activation requires server and all session
runners to restart after landing; Code does not activate them.

## Verification design

| ID | Required outcome |
|---|---|
| V-1 | Exact false-positive runner layout returns ModalBlocked and writes no bytes; assertion red before S2. |
| V-2 | Existing captured scrolled dialog and aligned warning without description still write exactly `2`; other dialogs, unboxed warning, command text and 2/3-option Bash choices do not qualify. |
| V-3 | Server watchdog class stays green, including C566 refusal, pending answers, episodes, failure continuation and the new command-preview negative. |
| R-1 | Runner class retains epoch, revision, history, adoption and all guarded-write outcomes. |
| R-2 | Guard union passes every named class with nonzero fresh TRX counts; prebuild the DerivedImpactIndex driver. |
| R-3 | Impacted Linux Card1277PtyHostFixtureTests pass; report native Windows selection owed. |
| R-4 | Full-range evidence diff, OS declaration lint and fetched-master merge-tree succeed. |
| M-1 | Inspect shared call sites and unchanged input fences; do not send live dialog keys. |
| M-2 | Report server plus session-runner activation required after landing. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1875-s1/` | red-layout | `/*/*/ClaudeDialogInputGuardTests/C1875_CommandPreviewWarningDoesNotAuthorizeNo` | V-1 | C1875_CommandPreviewWarningDoesNotAuthorizeNo, intentional assertion failure before fix | 1 | 3 |
| CP-2 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1875-s2/` | runner-guard | `/*/*/ClaudeDialogInputGuardTests/*` | V-1, V-2, R-1 | all listed, 0 failed | 6 | 3 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1875-server/` | server-watchdog | `/*/*/WatchdogServiceTests/*` | V-3 | all listed, 0 failed | 10 | 8 |
| CP-4 | S2 | CP-3 | policy-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-2 | all listed, 0 failed | 105 | 6 |
| CP-5 | S2 | CP-3 | impacted-pty | `/*/*/Card1277PtyHostFixtureTests/*` | R-3 | all listed, 0 failed | 3 | 2 |

### Supplemental commands and controls

Before CP-4, separately lease a native-output build of
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` with
`UseAppHost=false`, because its test invokes the driver with NoBuild. Bootstrap
Antiphon.Checkpoints through a separate build slot; invoke its built DLL (no implicit
build) and pass committed HEAD as `--expected-source-sha`. Each checkpoint
driver then takes its own slot. Run CP-1 at S1, CP-2 through CP-5 at S2, serially.
The deliberate red CP-1 is closed by the same case in CP-2 after the fix.

PC-1 pending for SourceLanding Mutation: remove the border/question column guard;
run only ClaudeDialogInputGuardTests/C1875_CommandPreviewWarningDoesNotAuthorizeNo
red/restore/green. Operator 2026-11-12 waived mutation execution for this Code
brief; no deliberate mutation is run here and ordinary green does not discharge PC-1.
