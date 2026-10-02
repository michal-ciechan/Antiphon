# Pty test health: CARD-0977, CARD-0978, CARD-0868 Pty subset

Code is complete at 7f84752f62dca25bd477e12c72f10bb4e0cf28be: Linux Pty 453 passed / 0 failed / 230 skipped; Antiphon.Tests Unit 3,744 passed / 0 failed / 40 skipped. Both required scratch mutations failed and were restored exactly. This report-only commit follows the code SHA. The final caller response carries the additional whole-Pty and loaded-repeat receipts at the resulting final branch SHA; no source implementation changes follow the Unit checkpoint.

## Design note (recorded before editing)

- CARD-0977: The CARD-0128 S2a design explicitly says CR is not before PreSubmitPause has elapsed and retains the never-weaker floor (docs/superpowers/plans/2026-08-22-card-0128-s2a-sendline-evidence-gate-plan.md:213-217). Runtime delivery requires discrete body/Enter writes and evidence; Codex/Grok confirmation remains a separate verdict. Preserve the strict elapsed floor rather than relaxing the assertion. Task.Delay is timer-based and can return slightly early; add a tiny Stopwatch-based top-up in src/Antiphon.Agents.Pty/EchoGatedLineSender.cs. Keep exact-two-write/no-retry/cancellation assertions. Add a fractional-millisecond test to expose timer truncation red-first. Run the original method 30 times under 24 bounded CPU burners at base and final SHA, the full class, and Antiphon.Tests Unit because production changes.
- CARD-0978: The old IL scanner excludes generated lambda/closure types and requires the variable literal and setter in one body. Attribute generated methods to their outer user method, follow local call/delegate edges including state machines, and propagate setter reachability through helpers. Require every outer method/constructor reaching a directory mutation or parameterised environment setter to be unkeyed NotInParallel. Literal and setter may be in different reachable methods. Parameterised setters are conservative even for callers without literals; a serialized helper cannot exempt an unsafe caller. Known fixed-name setters for other variables remain outside this guard. Retain the known two-mutator census. Compiled fixtures are never invoked: assert exact flagged names for lambdas, captured lambdas and helper callers plus serialized controls. Exclude only the explicitly selected fixture from the real assembly scan. Optional other-assembly guards omitted to avoid project references.
- CARD-0868 Pty subset: CommandLineLengthTests and Windows-parser branches in LaunchArgvGuardTests/ModernConPtyCommandLineTests get named OperatingSystem.IsWindows skips. Keep formatter-only coverage portable. Split Grok Missing/Found coverage from the file-as-directory Unavailable premise; Linux treats that shape as Missing, Windows as inaccessible storage. Guard only the Windows Unavailable case. Other projects named in CARD-0868 remain pending.

## Verification design

No frozen plan was supplied; the cards are the spec. This records direct driver rows rather than a checkpoint-tool manifest. Red-first/fix-driven reruns and the final report-only SHA run are accounted for below.

Use scripts/run-checkpoint.ps1 directly as explicitly required by the brief. Each row gets an isolated build, fresh results directory and host build slot. Full Pty runs establish inherited census and final zero failures (estimated 3 minutes each). Loaded repeat exception: CARD-0964 Final Review task 31859561, documented 48.9954 ms failure; revised budget 30 base plus 30 final executions with 24 owned bounded burners. Red-first source runs and two restored scratch mutations are explicitly necessary additional diagnostic rows. Windows verification is a separate task at final SHA.

### Checkpoint run inventory

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-BASE | base | tests/Antiphon.Agents.Pty.Tests -> bin-c977-base/ | base-full | /*/*/*/* | baseline census | reproduce failures | 450 | 3 |
| CP-BASE-LOAD | base | tests/Antiphon.Agents.Pty.Tests -> bin-c977-base-load/ | base-loaded | /*/*/EchoGatedLineSenderTests/Evidence_delays_the_one_CR_until_after_the_pre_submit_pause* | R-977 | 30 repeats recorded | 1 | 2 |
| CP-RED | test slice | tests/Antiphon.Agents.Pty.Tests -> bin-c978-red/ | red-fixtures | /*/*/(ConPtyEnvironmentIsolationGuardTests*)\|(EchoGatedLineSenderTests*)/* | red-first | both new tests fail | 7 | 2 |
| CP-FINAL | final | tests/Antiphon.Agents.Pty.Tests -> bin-c977-final/ | final-full | /*/*/*/* | all affected Pty classes and Unit | 0 failed | 450 | 3 |
| CP-FINAL-LOAD | final | tests/Antiphon.Agents.Pty.Tests -> bin-c977-final-load/ | final-loaded | /*/*/EchoGatedLineSenderTests/Evidence_delays_the_one_CR_until_after_the_pre_submit_pause* | R-977 | 30 repeats, 0 failed | 1 | 2 |
| CP-UNIT | final | tests/Antiphon.Tests -> bin-c977-unit/ | production-unit | /*/*/*/*[Category=Unit] | production Unit | report inherited failures | 1000 | 12 |
| CP-MUT-GUARD | scratch | tests/Antiphon.Agents.Pty.Tests -> bin-c978-mut/ | mutation-guard | /*/*/ConPtyEnvironmentIsolationGuardTests*/Guard_detects* | guard mutation | named assertion fails | 1 | 2 |
| CP-MUT-WAIT | scratch | tests/Antiphon.Agents.Pty.Tests -> bin-c977-mut/ | mutation-wait | /*/*/EchoGatedLineSenderTests*/Evidence_delays* | wait mutation | timing assertion fails | 1 | 2 |

## Results

Baseline reproduced exactly: 454 passed, 16 failed, 210 skipped. Loaded baseline repeat passed 30/30 under 24 timeout-bounded owned burners. Loadavg snapshots: 17.80 / 18.80 / 14.34 at launch, 30.79 / 22.12 / 15.70 after loaded build. All 24 recorded process groups were explicitly stopped after completion.

Red-first: initial full-path OR silently selected only the guard class (1 passed, 1 failed). Corrected prefix class-union selected all seven intended tests (6 passed, 1 failed): the fractional test initially passed because first-call JIT obscured the zero delay. Added eight internal warm samples (still one test result); the corrected red-first checkpoint at f516447e4b7d25ce70a629245821ea2059836611 executed seven tests: 5 passed, 2 failed, 0 skipped. Failures were the new fractional-pause assertion and exact named guard-violation assertion. The old guard reported [] instead of four named lambda/closure/helper violations. Final guard fixture adds a fifth runtime-name caller to prove parameter-helper safety without a caller literal.

Baseline failures (duplicate native rows are the inbox/modern arguments):


- Antiphon.Agents.Pty.Tests.CommandLineLengthTests.Measure_equals_the_line_the_child_parses
- Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.ParseArgv_is_the_real_parser
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.Plain_and_whitespace_arguments_round_trip
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_backslash_immediately_before_a_quote_is_doubled
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_trailing_backslash_is_not_swallowed_by_the_closing_quote
- Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.Portas_format_does_not_round_trip_the_shape_that_shredded_production
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.An_embedded_quote_survives_as_one_argument
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_backslash_not_before_a_quote_is_literal
- Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.The_old_doubling_rule_would_have_shredded_the_embedded_quote_case
- Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.The_old_doubling_rule_is_caught_before_the_process_is_created
- Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.A_truncated_bundle_is_caught_by_LENGTH_not_by_presence
- Antiphon.Agents.Pty.Tests.GrokNativeSessionStoreTests.Probe_distinguishes_missing_found_and_unavailable_storage
- Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.An_argument_lost_off_the_end_is_reported_as_missing_rather_than_as_a_mismatch
- Antiphon.Agents.Pty.Tests.FakeGrokContractTests.C467_BusyGatePreservesNativePromptAndTurnEnd
- Antiphon.Agents.Pty.Tests.WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv
- Antiphon.Agents.Pty.Tests.WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv

Baseline receipts:

```text
CHECKPOINT CP-BASE commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/*/*/* executed=470 passed=454 failed=16 skipped=210 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-BASE-20261002-154110-479f/run.trx slot=granted waited=0s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified
CHECKPOINT CP-BASE-LOAD commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/*/EchoGatedLineSenderTests/Evidence_delays_the_one_CR_until_after_the_pre_submit_pause* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-BASE-LOAD-20261002-154317-1488/run.trx slot=granted waited=0s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified repeat=30 repetitions=30/30 hostInvocations=1
CHECKPOINT CP-RED commit=a2e5bca27730a3a587e2236f8312d77b4b9de769 build=ok filter=/*/*/ConPtyEnvironmentIsolationGuardTests*/Guard_detects*|/*/*/EchoGatedLineSenderTests/Fractional* executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-RED-20261002-154527-5517/run.trx slot=granted waited=0s dirty=0 source=a2e5bca27730a3a587e2236f8312d77b4b9de769 sourceState=clean buildSource=verified
CHECKPOINT CP-RED commit=a2e5bca27730a3a587e2236f8312d77b4b9de769 build=ok filter=/*/*/(ConPtyEnvironmentIsolationGuardTests*)|(EchoGatedLineSenderTests*)/* executed=7 passed=6 failed=1 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-RED-20261002-154655-8eba/run.trx slot=granted waited=136s dirty=0 source=a2e5bca27730a3a587e2236f8312d77b4b9de769 sourceState=clean buildSource=verified
CHECKPOINT CP-RED commit=f516447e4b7d25ce70a629245821ea2059836611 build=ok filter=/*/*/(ConPtyEnvironmentIsolationGuardTests*)|(EchoGatedLineSenderTests*)/* executed=7 passed=5 failed=2 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-RED-20261002-155000-deef/run.trx slot=granted waited=0s dirty=0 source=f516447e4b7d25ce70a629245821ea2059836611 sourceState=clean buildSource=verified
```

The remaining three baseline failures were explicit Windows prerequisites: WindowsPtyArgvNativeTests throws on Unix (two argument rows), FakeGrokContractTests.C467_BusyGatePreservesNativePromptAndTurnEnd asserts Windows before its ConPTY launch. Converted only those platform preconditions to named skips; Windows prerequisites/assertions stay in place. LaunchArgvGuard.VerifyOrThrow is a no-op on Linux, so its four previously passing verification cases also need explicit Windows skips to avoid claiming parser evidence from a no-op. Pure formatter coverage remains portable.

## Implementation and mutation evidence

The first full run after the fix at 985936f3ed13140b20f705a2130b322396ec4005 had 452 passed / 1 failed / 230 skipped. The only failure was guard attribution: four TUnit.Generated launcher roots (__Invoke/.cctor for the two census test classes) do not carry the original scheduling metadata. Reflection confirmed GeneratedCodeAttribute("TUnit", "1.44.0.0"). The scanner now excludes only these TUnit-generated roots, while still following generated lambda/closure and state-machine bodies through user roots. The rerun at 7f84752f62dca25bd477e12c72f10bb4e0cf28be passed: 453 passed / 0 failed / 230 skipped.

Scratch mutation 1 removed ldftn/ldvirtftn traversal. The exact named fixture test failed (0 passed / 1 failed / 0 skipped). Scratch mutation 2 replaced the whole pre-submit delay/top-up with Task.Delay(TimeSpan.Zero). The original evidence-to-CR floor assertion failed (0 passed / 1 failed / 0 skipped). Both were restored by rewriting exact backup bytes, refreshing modification times. git diff --exit-code returned 0 after each restoration; both original SHA256 hashes matched:

- guard: db1aca610318fb6e8b099ded7c8da1fe4dd08bccf1cc9fcd238d97d4228aa0ee
- sender: 867f75d6993a4d89adf9d8deb890bb593ab8af46ecbec1068ced2945766f1939

```text
CHECKPOINT CP-FINAL commit=985936f3ed13140b20f705a2130b322396ec4005 build=ok filter=/*/*/*/* executed=453 passed=452 failed=1 skipped=230 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-FINAL-20261002-155318-4ad1/run.trx slot=granted waited=300s dirty=0 source=985936f3ed13140b20f705a2130b322396ec4005 sourceState=clean buildSource=verified
CHECKPOINT CP-FINAL commit=7f84752f62dca25bd477e12c72f10bb4e0cf28be build=ok filter=/*/*/*/* executed=453 passed=453 failed=0 skipped=230 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-FINAL-20261002-160044-231f/run.trx slot=granted waited=30s dirty=0 source=7f84752f62dca25bd477e12c72f10bb4e0cf28be sourceState=clean buildSource=verified
CHECKPOINT CP-MUT-GUARD commit=7f84752f62dca25bd477e12c72f10bb4e0cf28be build=ok filter=/*/*/ConPtyEnvironmentIsolationGuardTests*/Guard_detects* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-MUT-GUARD-20261002-160257-267a/run.trx slot=granted waited=60s dirty=1 source=7f84752f62dca25bd477e12c72f10bb4e0cf28be+dirty:1a325047cf51923c10ce7510c760214b94b18708b6f3592ec26c79ca44d6530d sourceState=dirty buildSource=verified
CHECKPOINT CP-MUT-WAIT commit=7f84752f62dca25bd477e12c72f10bb4e0cf28be build=ok filter=/*/*/EchoGatedLineSenderTests*/Evidence_delays* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-MUT-WAIT-20261002-160521-bb81/run.trx slot=granted waited=60s dirty=1 source=7f84752f62dca25bd477e12c72f10bb4e0cf28be+dirty:05224e9763c82c91ac57cad52a4adc0f1d80fab6a3f903e3b75d5938275f36e2 sourceState=dirty buildSource=verified
```

These are diagnostic Code spot-checks, not SourceLanding Mutation receipts. Every PC remains PENDING for method-scoped SourceLanding Mutation.

## Required application Unit lane

3,744 passed / 0 failed / 40 skipped, source-qualified at the final code SHA. The later report-only commit does not change production or test sources, so no extra Unit rebuild is needed. Antiphon.Tests and Pty ran sequentially.

```text
CHECKPOINT CP-UNIT commit=7f84752f62dca25bd477e12c72f10bb4e0cf28be build=ok filter=/*/*/*/*[Category=Unit] executed=3744 passed=3744 failed=0 skipped=40 trx=/work/worktrees/task-1a1a64fc/.antiphon/c977-checkpoints/CP-UNIT-20261002-160749-da1f/run.trx slot=granted waited=75s dirty=0 source=7f84752f62dca25bd477e12c72f10bb4e0cf28be sourceState=clean buildSource=verified
```

## Files changed and rerun commands

- `src/Antiphon.Agents.Pty/EchoGatedLineSender.cs`: monotonic minimum pause and tiny rounded-up timer top-up.
- `docs/session-runtime-invariants.md`: explicit elapsed-floor contract; post-Enter confirmation remains separate.
- `tests/Antiphon.Agents.Pty.Tests/EchoGatedLineSenderTests.cs`: fractional-millisecond red-first pin, eight internal warm samples, original timing assertions unchanged.
- `tests/Antiphon.Agents.Pty.Tests/ConPtyEnvironmentIsolationGuardTests.cs`: local call/delegate/state-machine traversal, parameterised setter caller checks, exact five-name violation assertion, method/class unkeyed controls, census retained, explicit fixture-only scan and TUnit-generated root attribution.
- `CommandLineLengthTests.cs`, `LaunchArgvGuardTests.cs`, `ModernConPtyCommandLineTests.cs`, `WindowsPtyArgvNativeTests.cs`, `FakeGrokContractTests.cs` in the same test directory: named Windows-only skips for native prerequisites; pure formatting still runs on Linux; Windows assertions retained.
- `GrokNativeSessionStoreTests.cs` in the same directory: portable Missing/Found leg plus separately named Windows non-directory storage Unavailable leg. The fixture is an OS-specific I/O error shape, not an ACL denial.

Every checkpoint built in its own producer-owned `bin-c977-*` / `bin-c978-*` output and acquired a granted build slot. No raw builds or unlisted tests ran. CP-RED needed two refinements (filter correction, then warm sample fixture) as recorded above. CP-FINAL needed the TUnit root fix rerun; the extra final-SHA full run is required because this durable report adds a commit. The two dirty scratch runs intentionally omit strict clean-source qualification, report their dirty hashes, and are not clean acceptance certificates.

On the final source SHA:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-FINAL -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c977-final/ -Filter '/*/*/*/*' -MinExecuted 450 -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c977-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-FINAL-LOAD -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c977-final-load/ -Filter '/*/*/EchoGatedLineSenderTests/Evidence_delays_the_one_CR_until_after_the_pre_submit_pause*' -Repeat 30 -MinExecuted 1 -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c977-checkpoints
```

Use 24 owned CPU burners bounded by timeout for the loaded row, start them after build-slot grant, record host load and confirm all are alive through test completion, then terminate only their recorded process groups. The actual final receipt and load measurements are in the final caller response. Repeat budget exception is the documented CARD-0964 Final Review flake: 30 base plus 30 final trials, one test host per SHA. No intervening green-repeat proof runs.

## Pending handoff and Windows acceptance

Separate Windows task at the final pushed SHA is PENDING. Exact requested Windows class prefix union (plus the newly affected FakeGrok class):

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-WINDOWS -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c977-windows/ -Filter '/*/*/(PtyBackendContractTests*)|(PtyCustodyTests*)|(WindowsPtyArgvNativeTests*)|(LaunchArgvGuardTests*)|(ModernConPtyCommandLineTests*)|(ConPtyEnvironmentIsolationGuardTests*)|(CommandLineLengthTests*)|(GrokNativeSessionStoreTests*)|(EchoGatedLineSenderTests*)|(FakeGrokContractTests*)/*' -MinExecuted 1 -Expect PtyBackendContractTests,PtyCustodyTests,WindowsPtyArgvNativeTests,LaunchArgvGuardTests,ModernConPtyCommandLineTests,ConPtyEnvironmentIsolationGuardTests,CommandLineLengthTests,GrokNativeSessionStoreTests,EchoGatedLineSenderTests,FakeGrokContractTests -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c977-windows-checkpoints
```

Windows native prerequisites must be available; Linux skips are not Windows evidence. No new project references or other-assembly guards were added.

CARD-0868 remains open. Its out-of-scope items are PENDING:

- `Antiphon.Tests`: `CodexTranscriptTailerTests.A_rollout_held_open_by_the_writer_is_still_read`.
- `Antiphon.Tests`: `TranscriptAdoptionSafetyTests.Post_start_stranger_transcript_reports_AdoptionRefused_before_stale_C3_candidates` and `ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt`.
- `Antiphon.Tests`: `PtyHostAdoptionTests` native apphost coverage.
- `Antiphon.SessionRunner.Tests`: `CodexLaunchRefusalTests`, `GrokRulesStoreFailureTests`, `GrokRulesFileLaunchTests` stalls (CARD-0821 items).

All PCs remain PENDING for method-scoped SourceLanding Mutation. No cards were closed and no landing was performed by Code. This task is the landing owner; plain land follows a clean Final Review. Next stage is Review, including the separate Windows SHA row and final Linux receipts. Changes are fast-forward-only from e4706adabfb74c5498da506dcce25e1e098ad1f8, committed on feat/card-task-1a1a64fc and pushed after each slice; no colliding task files were edited.
