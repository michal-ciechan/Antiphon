# CARD-1209: two inherited Unit assertions

Both current-master failures are stale tests. The Grok test still requires the retired install.sh flow; the channel test pins a word removed by equivalent bundle prose. No product defect is demonstrated by either red. No application, test, Dockerfile or bundle source was edited in this Debug task.

## Current-master reproduction

At the start of this investigation, origin/master and the assigned branch both advertised e19186dc8e156216a2875c8c87237b94b7a857f8. CP-1 and CP-2 ran only the two named methods, serially, through the checkpoint tool with that exact expected source SHA. Each executed one result and failed at its inherited assertion; zero skips. The complete Unit lane and assembly were not run.

* Antiphon.Tests.Application.ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries, tests/Antiphon.Tests/Application/ChannelOutboundContractTests.cs:48: `ShouldAssertException: InstructionBundles.All["orchestrator"].Text should contain (case insensitive comparison) "configured channel step"`.
* Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers, tests/Antiphon.Tests/Infrastructure/GrokRunnerImageContractTests.cs:20: `ShouldAssertException: installed.Success should be True but was False`.

The independent prior report is docs/superpowers/reports/2026-10-13-card-0971-code-evidence.md on feat/card-task-bc957ed9. It recorded the same two failures, each with one executed exact method, at original base 02275c3ba8ec6969a6c7bbec8dbe48609367d045. This task adds current-master and commit-boundary evidence.

## Root causes and smallest repair

Grok first bad: 730cb2ac7512ea72b3735ee4ea49e77fa34cc811, `fix(CARD-0989): verify the Grok CLI download before install`, 2026-10-12. Parent: edbf47d91cdeba94342a2b79d54d5730bde96b17. `git log -n 1 -S 'https://x.ai/cli/install.sh' -- docker/session-runner-grok/Dockerfile` identifies this transition. The qualification test is unchanged across it. This is earlier than 593278bf2ebaa04c3eb615f2d3421ca800fc7710, which refined the checksum comment and related contract note, rather than first replacing the installer.

The production Dockerfile deliberately retains qualified Grok 1.0.41, downloads the versioned raw artifact, verifies the reviewed SHA-256, installs mode 0755, and probes the pinned version. The test's installer regex cannot match that flow. Two following assertions are stale as well: the old download path and literal version grep. Updating only the regex would expose those next failures.

Repair only GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers: extract the runtime-base `ARG GROK_VERSION` with a line-anchored regex, assert it equals the independent qualified Version constant, require the versioned raw artifact URL, update the install assertion to `/tmp/grok-download/grok`, and the version probe to `grep -F "${GROK_VERSION}"`. Preserve all documentation, fixture and verifier assertions. Keep an assertion rejecting x.ai/cli/install.sh. Retain DockerStackContractTests.Runner_pins_grok_cli_by_version_and_digest's existing digest, malformed-pin, empty-download and checksum-before-install/execute coverage; do not restore the installer or drop verification.

Channel first bad: e27954e5b2b7a6732abcf8fd1adc812be104818e, `feat(CARD-0822): inject orchestrator instructions on session start`, 2026-10-10. Parent: a64dccaa1ad6d3a36c4f9982a31401e35c8eb43d. `git log -n 1 -S 'configured channel step' -- server/Bundles/orchestrator.md` identifies the transition. The corresponding test is unchanged across it. The commit compresses prompt prose to stay within the launch budget. It changes `a configured channel step may add a converted file` to `a channel step may add a converted file`, and retains Markdown as the default deliverable. The contract's meaning is present; the lexical assertion is obsolete.

InstructionBundles loads server/Bundles/*.md as embedded resources, normalizes LF and derives the content version. The generated live operating-settings file is separate; editing that file or adding a database bundle would repair the wrong artifact.

Repair only the line-48 assertion to require `channel step may add a converted file`, and separately assert `Source Markdown is the default deliverable`. Preserve the no-Prefer-PDF assertion and all source/transport dependency checks. This keeps the tested policy explicit without depending on the removed adjective. Do not expand or restore the bundle merely to satisfy the stale wording pin.

## Follow-up

Mentioned follow-up under CARD-1209: commission a test-only Code slice for the two assertion updates above, followed by ordinary Review. No new card or live tracker mutation was made from this read-only Debug task. No product repair, restart, land or deploy is warranted by these two failures. Ordinary repair verification should include the two repaired exact methods and the existing Runner_pins_grok_cli_by_version_and_digest guard. A new defect discovered by that repair must be reported separately, not hidden by retries, widened deadlines or weaker outcomes.

## Diagnostic manifest and rerun

The Debug selection is a closed two-method list. The same table is used at master and the suspected commits/parents, selecting CP-1 or CP-2 at a boundary. Reds are diagnostic expected outcomes, not green certificates. Both rows keep a nonzero execution floor.

# CARD-1209 exact inherited-red diagnostic selection

Read-only diagnostic; no code edits. Rerun this selection at current master and the two suspected bad commits/parents. Red is expected at current/bad, green at parents. CP minima count results, not assertions.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1209/` | channel-boundary | `/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries` | V-1 | exactly 1 executed; diagnostic expected-red on current/bad | 1 | 6 | true |
| CP-2 | all | CP-1 | grok-pin | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | V-2 | exactly 1 executed; diagnostic expected-red on current/bad | 1 | 1 | true |

Bootstrap (unlisted infrastructure build, needed to execute the mandated checkpoint tool):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1209-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1209-tool/ --property:UseAppHost=false --nologo
```

It received slot=granted, waited=30s, maxcpucount=6; build succeeded with 0 errors and 1 existing nullable warning in TaskOwnerGuard.cs. Every diagnostic test build/row was also slot-gated. No NoSlot or unleased driver was used.

Rerun using the diagnostic manifest above, copied to `.antiphon/c1209/plan.md`:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1209-tool/Antiphon.Checkpoints.dll run --plan .antiphon/c1209/plan.md --expected-source-sha e19186dc8e156216a2875c8c87237b94b7a857f8 --serial --max-wait 55s
```

For each boundary, use a clean detached checkout at the full SHA, copy the ignored manifest there, and add `--repo-root <checkout> --rows CP-1` for channel or `--rows CP-2` for Grok. Use that checkout's SHA as --expected-source-sha. Exit 75 means the run is still active: invoke the same tool's `wait --repo-root <checkout> --run <run-id> --max-wait 55s` until completion. Expected assertion reds exit 1; parent greens exit 0. Do not treat a zero-result, fixture or build error as boundary proof.

## Provenance

Raw evidence is archived outside the worktrees at `/work/repos/antiphon/.git/antiphon/debug/2cb07437/`. The current, channel-parent, channel-bad, grok-parent and grok-bad subdirectories retain original reports, schema receipts, TRX, console/failure logs and build provenance. Original absolute paths in CHECKPOINT lines remain unedited even after temporary checkouts are removed. Fresh TRX definitions/results were inspected for exact method identity and nonzero counts. Reds are assertion evidence only, never clean Review certificates. There were no deliberate mutants or code repairs.

The checkpoint source validator independently accepted the two green parent receipts:

```text
CHECKPOINT SOURCE VALID source=a64dccaa1ad6d3a36c4f9982a31401e35c8eb43d rows=1
CHECKPOINT SOURCE VALID source=edbf47d91cdeba94342a2b79d54d5730bde96b17 rows=1
```

## Boundary results

| Selection | Source SHA (prefix) | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| Current master: both exact methods | e19186dc8 | 2 | 0 | 2 | 0 |
| Channel parent | a64dccaa1 | 1 | 1 | 0 | 0 |
| Channel first bad | e27954e5b | 1 | 0 | 1 | 0 |
| Grok parent | edbf47d91 | 1 | 1 | 0 | 0 |
| Grok first bad | 730cb2ac7 | 1 | 0 | 1 | 0 |

Total: 6 exact-method results, 2 parent passes, 4 expected assertion failures, 0 skips. Five isolated checkpoint test builds, plus the single explained tool bootstrap build. No build or fixture failures. Every row has dirty=0, sourceState=clean, buildSource=verified and slot=granted. Each exact bad commit reproduces its current-master assertion. The parent passes and adjacent commit diffs establish the two first-bad boundaries without whole-lane bisection.

### Original CHECKPOINT lines

current

```text
CHECKPOINT CP-1 commit=e19186dc8e156216a2875c8c87237b94b7a857f8 build=ok filter=/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2cb07437/.antiphon/checkpoints/20261013-004126-cba5/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=e19186dc8e156216a2875c8c87237b94b7a857f8 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=e19186dc8e156216a2875c8c87237b94b7a857f8 build=reused filter=/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2cb07437/.antiphon/checkpoints/20261013-004126-cba5/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e19186dc8e156216a2875c8c87237b94b7a857f8 sourceState=clean buildSource=verified
```

channel-parent

```text
CHECKPOINT CP-1 commit=a64dccaa1ad6d3a36c4f9982a31401e35c8eb43d build=ok filter=/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-2cb07437-channel-parent/.antiphon/checkpoints/20261013-004547-b104/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a64dccaa1ad6d3a36c4f9982a31401e35c8eb43d sourceState=clean buildSource=verified
```

channel-bad

```text
CHECKPOINT CP-1 commit=e27954e5b2b7a6732abcf8fd1adc812be104818e build=ok filter=/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2cb07437-channel-parent/.antiphon/checkpoints/20261013-004821-7b8a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=e27954e5b2b7a6732abcf8fd1adc812be104818e sourceState=clean buildSource=verified
```

grok-parent

```text
CHECKPOINT CP-2 commit=edbf47d91cdeba94342a2b79d54d5730bde96b17 build=ok filter=/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-2cb07437-channel-parent/.antiphon/checkpoints/20261013-005303-6b08/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=edbf47d91cdeba94342a2b79d54d5730bde96b17 sourceState=clean buildSource=verified
```

grok-bad

```text
CHECKPOINT CP-2 commit=730cb2ac7512ea72b3735ee4ea49e77fa34cc811 build=ok filter=/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2cb07437-channel-parent/.antiphon/checkpoints/20261013-005644-b9c9/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=730cb2ac7512ea72b3735ee4ea49e77fa34cc811 sourceState=clean buildSource=verified
```

## Restoration and deliverable

All commands were awaited. The checkpoint tool confirmed completed executors before cleanup. Green runs removed their isolated outputs automatically; red outputs were removed using the tool's owned-output cleanup after evidence preservation. The separate bin-c1209-tool bootstrap output was removed after its last use. A final directory inventory found zero bin-c1209* outputs. The clean, task-created detached boundary worktree was removed through ordinary git worktree remove, without force. No source mutation, stash, reset, rebase, amendment, production repair or broad test was needed.

The sole tracked deliverable is this authored Markdown report at docs/superpowers/reports/2026-10-13-card-1209-debug-evidence.md, also retained at .antiphon/task-2cb07437.md and in the external evidence root. Raw JSON/TRX/logs remain uncommitted. The report is pushed on feat/card-task-2cb07437; its publication SHA is supplied by the final ls-remote progress line. No product/test source was changed after or between the diagnostics.

--- next stage ---
next: code
handoff: CARD-1209 test-only repair of the two stale contract assertions; preserve the qualified version, checksum guard and source/transport policy. Run exact affected methods and the existing Grok digest guard, then ordinary Review. No restart or deployment.
artifact: docs/superpowers/reports/2026-10-13-card-1209-debug-evidence.md
