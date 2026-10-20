# CARD-0690 / CARD-0821: Linux SessionRunner test stall

Status: **confirmed historical mechanism, already fixed on the inspected master**.
All four narrowly selected classes complete on Linux: **37 passed, 0 failed,
0 skipped**, with four fresh TRX files. Recommend closing CARD-0690 as resolved
and CARD-0821 as a duplicate/resolved report of the same stall. No implementation
changes were made.

## Scope and source

- Task `3860c88d`, branch `feat/card-task-3860c88d`, Linux runner mirror.
- Assigned base and freshly fetched `origin/master` at start:
  `cff9b9bdadab0285d748be3d725bd76a406450d0`.
- Both cards were read in full with `scripts/card.ps1 get ... -Board Antiphon`.
  CARD-0690 row `5da0f188-9a62-4c25-9381-887ff9c821bf`, revision 1;
  CARD-0821 row `18daa512-6c4f-44c2-8be7-407d838a74e2`, revision 1;
  board `8988ca03-7414-47ad-b0b6-51556c701703`.
- Their 2026-10-13 reconciliation assigns the Windows launch-policy and custody
  backend failures to CARD-1212. This investigation does not reopen that cluster.
- CARD-0868 row `9bb3f5d4-d81e-44bd-bb75-4335e44c98cf`, revision 0,
  records prior class-by-class localization at `d07c7e07`: CodexLaunchRefusalTests,
  GrokRulesStoreFailureTests and GrokRulesFileLaunchTests hang independently;
  every other selected Unit class finished.
- The referenced `docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md`
  is absent from this checkout. Card descriptions remain historical observations,
  not a substitute for current test evidence.

## Diagnostic selection

These are bounded rechecks of the localized classes and the additional failure
retained by CARD-0690. They are not a whole-Unit qualification. No repair is
authorized. Run serially through the checkpoint tool, with `--row-timeout 15m`.
The separate slot-gated tool bootstrap is necessary to execute these rows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.SessionRunner.Tests -> bin-c0690-investigate/ | codex-refusal | `/*/*/CodexLaunchRefusalTests/*` | historical Codex stall and oversized-launch failure | all listed, 0 failed | 3 | 3 |
| CP-2 | all | CP-1 | grok-store | `/*/*/GrokRulesStoreFailureTests/*` | historical store-failure stall | all listed, 0 failed | 6 | 1 |
| CP-3 | all | CP-1 | grok-launch | `/*/*/GrokRulesFileLaunchTests/*` | historical file-launch stall | all listed, 0 failed | 21 | 2 |
| CP-4 | all | CP-1 | capabilities | `/*/*/RunnerCapabilitiesTests/*` | daemon reference-closure failure | all listed, 0 failed | 7 | 1 |

## Results

The tested commit was `39ad3b6d6047025caf02449b455b87e9d9536f88`: the assigned
master plus only this investigation's initial Markdown. Production code, tests and
build scripts were identical to `cff9b9bdadab0285d748be3d725bd76a406450d0`.
The host was Debian GNU/Linux 12 (24 cores). One serial checkpoint run took
1m14s, including one isolated build. Every build/row received a host build slot.

| Selection | Executed / passed / failed / skipped | Test wall time | Host wall time |
|---|---|---|---|
| CodexLaunchRefusalTests | 3 / 3 / 0 / 0 | 1.890s | 5.106s |
| GrokRulesStoreFailureTests | 6 / 6 / 0 / 0 | 0.917s | 4.687s |
| GrokRulesFileLaunchTests | 21 / 21 / 0 / 0 | 10.709s | 13.777s |
| RunnerCapabilitiesTests | 7 / 7 / 0 / 0 | 0.325s | 4.273s |

Run evidence: `.antiphon/checkpoints/20261020-134252-35b4/`
(`report.json`, `report.md`, `executor.log`, `rows/CP-1/run.trx` through
`rows/CP-4/run.trx`). Generated files stay ignored. The unedited checkpoint lines
below preserve the essential result if the worktree is retired.

```text
CHECKPOINT CP-1 commit=39ad3b6d6047025caf02449b455b87e9d9536f88 build=ok filter=/*/*/CodexLaunchRefusalTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3860c88d/.antiphon/checkpoints/20261020-134252-35b4/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=39ad3b6d6047025caf02449b455b87e9d9536f88 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=39ad3b6d6047025caf02449b455b87e9d9536f88 build=reused filter=/*/*/GrokRulesStoreFailureTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-3860c88d/.antiphon/checkpoints/20261020-134252-35b4/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=39ad3b6d6047025caf02449b455b87e9d9536f88 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=39ad3b6d6047025caf02449b455b87e9d9536f88 build=reused filter=/*/*/GrokRulesFileLaunchTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-3860c88d/.antiphon/checkpoints/20261020-134252-35b4/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=39ad3b6d6047025caf02449b455b87e9d9536f88 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=39ad3b6d6047025caf02449b455b87e9d9536f88 build=reused filter=/*/*/RunnerCapabilitiesTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-3860c88d/.antiphon/checkpoints/20261020-134252-35b4/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=39ad3b6d6047025caf02449b455b87e9d9536f88 sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=39ad3b6d6047025caf02449b455b87e9d9536f88 rows=4
```

The last line is the separate receipt validator's exit-0 result. The tool
deleted its test build output and the observer removed the dead executor's shadow
copy. No row hit its timeout; no hang dump was needed.

One additional build bootstrapped the checkpoint executable through
`scripts/build-slot.ps1`: exit 0, four compiler warnings, zero errors, 7s slot
hold. No other test suite or broad Unit selection was run. An initial invocation
had a mistyped expected SHA and was refused with `source_mismatch or source_dirty`
before any test/build row; the corrected exact SHA produced the run above.

## Confirmed mechanism

Historical source was read from Git objects, without changing branches or source.
`d07c7e070032ae0350c5ffbdcde44ab6bc62f97a` is CARD-0868's localization base;
`125f8dda23cce27abf2acfb37e294e1d7eddee63` is CARD-0690's earlier reported base.
Their `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs:87-89,187-200,217-228`
has the same mechanism:

1. `PipeName` joins `Environment.SpecialFolder.ApplicationData` with
   `herdr/sessions/<session>/herdr.sock`.
2. `Start()` stores the accept-loop Task without awaiting its result.
3. The loop constructs `NamedPipeServerStream` **before** `SignalListening()`.
   Its catches at `:304-311` handle cancellation and IOException, not
   PlatformNotSupportedException.
4. A constructor exception faults the loop without resolving `_listening`.
   `WaitUntilListeningAsync()` awaits that separate promise with a default,
   uncancelable token and no deadline. The test never reaches disposal.

A direct PowerShell/.NET constructor probe on this host, using precisely that
path calculation and `InOut, 4, Byte, Asynchronous` constructor shape, produced:

```text
historical-pipe-shape applicationDataEmpty=True rooted=False
constructor=System.PlatformNotSupportedException message=The name of a pipe on this platform must be a valid file name or a valid absolute path to a file name.
```

This probe starts no child, provider or live service. It reproduces the failing
constructor boundary; the indefinite await is reconstructed from the historical
source and CARD-0868's stored class-isolation observations, not claimed as a new
whole-lane hang reproduction. The same constructor/readiness mechanism is also
recorded in `docs/superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md:25-34`.

The specific affected methods at the localization base are:

- `CodexLaunchRefusalTests.Pty_host_oversized_codex_launch_is_refused_before_herdr_is_contacted`:
  `CodexLaunchRefusalTests.cs:66-73` starts the fake and waits without a token.
- `GrokRulesStoreFailureTests.Storage_failure_refuses_before_any_child_or_pane_effect`:
  `GrokRulesStoreFailureTests.cs:14-21,34-36`; all six arguments start the fake,
  including the non-Herdr arms.
- `GrokRulesFileLaunchTests.Unsafe_final_runner_boundary_has_zero_effects_even_without_server_validation`:
  `GrokRulesFileLaunchTests.cs:14-34`; its 12 variants reach that startup wait.
  `Herdr_receipt_is_durable_before_first_request_and_before_typing` uses the same
  fake-start/wait sequence (current source `:119-123`).

The two Grok classes carry `[ParallelLimiter<ProcessSpawnLimit>]`; the limiter's
`Limit => 1` is at `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimit.cs:12-14`.
A stalled Grok test therefore also prevents other tests using that limiter from
running, consistent with the stored TUnit SemaphoreSlim scheduler wait and
partial progress followed by no final TRX. CodexLaunchRefusalTests gained that
limiter later; it did not need to hold the limiter to leave the test run unfinished.
The exact first test/argument scheduled at the old 150-164 progress mark is not
recoverable from the card rows.

## Why it completes now

CARD-0801 supplied the actual Unix listener plus failure propagation, instead of
leaving the Windows-oriented fixture waiting forever. The source history includes
`8af7a7d6021688fc46a8144cbe918a6ea08a8df3` (listener/transport),
`735cdd49cfe17273a03bff4d198da64d78572387` (portable consumer fixtures),
`e27846446b49331fc2667453c3c95d57bb5dcedb` (startup disposal),
`09773ba0bcb81e3527afd9d755bc2d332db3f702` (listener fault isolation), and
`b5c146952c4cd6c2cd0e7f54923ec8e3954748c2` (bounded busy-pipe retry).
All are ancestors of the inspected master.

Current source evidence:

- `FakeHerdrTransport.cs:282-295,323-344`: Unix uses an AF_UNIX socket;
  NamedPipeServerStream is in the Windows branch.
- `FakeHerdrServer.cs:208-228`: the loop fault is observed and tokenless readiness
  has a five-second deadline.
- `FakeHerdrServer.cs:232-251,366-371`: disposal settles readiness; a fatal listener
  exception calls `FaultListening` and is rethrown after transport cleanup.
- Current consumer waits and explicit socket paths are in
  `CodexLaunchRefusalTests.cs:89-97`,
  `GrokRulesStoreFailureTests.cs:34-38`, and
  `GrokRulesFileLaunchTests.cs:34-41,119-136`.

CP-1/2/3 executed those real consumers and teardown successfully; they were not
skipped on Linux.

## The additional named failures

**CARD-0690: daemon project-reference closure.** At `125f8dda`,
`RunnerCapabilitiesTests.cs:85-90` passes the csproj `Include` directly to the
host filesystem; `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj:33-37`
uses Windows backslashes. On Unix those are literal filename characters.
Commit `65af334f9b74280eb248ea519f81d8b84ea4d9c1` normalized them to the host
separator. The current normalization is `RunnerCapabilitiesTests.cs:114-115`.
The exact named test passed in CP-4. This is a separate historical fixture defect,
not the readiness hang.

**CARD-0821: oversized Codex launch before registration.** The historical test
at `d07c7e07:CodexLaunchRefusalTests.cs:42-61` unconditionally uses the Windows
npm shim and expects Windows command-line rejection. Production
`src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:39-42` bypasses that policy
off Windows. CARD-0801 commit `735cdd49cfe17273a03bff4d198da64d78572387` added the
portable executable and platform-specific expectation. Current
`CodexLaunchRefusalTests.cs:42-79` checks rejection on Windows and successful
registration followed by explicit kill on Unix. CP-1 passed this exact method.
The historical failure's precise exception text was not available in the card;
the unconditional Windows expectation and subsequent correction are source facts.

## Duplicate judgment and remaining uncertainty

The cards duplicate the **same historical fake-listener stall**, independently
localized by CARD-0868. Their ancillary failures differ, so the descriptions were
not byte-identical duplicates. Both retained ancillary failures now pass; the
already reassigned launch-policy/custody cluster remains owned by CARD-1212.
Recommend closing the retained scopes of both cards; no further Plan is warranted.
This investigation has not changed board state.

Limits:

- The historic 1,099-test selection/filter and exact first blocked argument are
  not preserved in the retrieved cards. No historical TRX or dump was available;
  the historical runs explicitly reported no TRX. Commit `2ee0387a` is not present
  in this mirror; the two full historical SHAs above were inspectable.
- These 37 results establish completion of the localized blockers and named
  residual failures. They do **not** certify the current whole Unit lane or every
  unrelated runner test. No broader run is needed to identify this mechanism.
- Windows behavior was not run here. No production runner restart/deployment,
  real Herdr session or provider invocation was performed.

## Reproduction

The exact commands used after committing the initial evidence selection:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0690-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0690-tool/ --nologo -v:q
dotnet tools/Antiphon.Checkpoints/bin-c0690-tool/Antiphon.Checkpoints.dll run --plan docs/investigations/2026-10-20-card-0690-sessionrunner-tests-unit-lane-hangs-on-linux-server2-after-160-of-1099-tests.md --serial --row-timeout 15m --total-timeout 25m --expected-source-sha 39ad3b6d6047025caf02449b455b87e9d9536f88 --max-wait 45s
dotnet tools/Antiphon.Checkpoints/bin-c0690-tool/Antiphon.Checkpoints.dll wait --run 20261020-134252-35b4 --max-wait 45s
dotnet tools/Antiphon.Checkpoints/bin-c0690-tool/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261020-134252-35b4/report.json --expected-source-sha 39ad3b6d6047025caf02449b455b87e9d9536f88
```

For a new source revision use its actual committed SHA and the new printed run ID;
never relabel the receipt above. The bootstrap and row outputs were isolated and
are removed after use.

## Not done, noted

Minimal fix proposal: none for the retained scopes; the required listener and platform-fixture corrections already exist on master, and no fix was designed or implemented here.

--- next stage ---
next: none
handoff: Close CARD-0690 as resolved and CARD-0821 as duplicate/resolved: historical fake-listener readiness hang reconstructed; all three localized classes and both residual named failures pass on inspected master (37/37); retain CARD-1212 ownership of its separate failure cluster.
artifact: docs/investigations/2026-10-20-card-0690-sessionrunner-tests-unit-lane-hangs-on-linux-server2-after-160-of-1099-tests.md
