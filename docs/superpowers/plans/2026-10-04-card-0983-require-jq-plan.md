# CARD-0983: require jq explicitly in rolling-harness evidence

Date: 2026-10-04. Plan task: `b12e3f04-3f1d-4861-9a9d-4b8ee85d97aa`.
Assigned branch base: `7b8e687a73c17167a49b6ce0a3ace1d1ab1f796a`.
Fetched master inspected: `70d7e26128af77d1440d873f297c66af5567fb08`.
The relevant scripts, test sources and Dockerfile are identical between these
two commits; master additionally contains CARD-1012's CP-2 floor correction.

## Outcome and stage boundary

The card remains unsatisfied. Add `-RequireJq` to the offline rolling harness
and its jq driver. When requested, an unavailable jq probe must fail before any
rolling group runs; a jq-expected receipt must include T-20. Without the switch,
retain the existing optional-jq behavior and rosters.

This dispatch changes only this plan. TestDesign is **separate**: the brief did
not fold it into Plan. The proposed verification and checkpoints below give
that stage a concrete scope to freeze. No product decision is outstanding;
D-1 through D-7 are implementation choices within the requested opt-in.

Owners read: `AGENTS.md`, `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, and
`docs/testing-and-build.md`. Card description and history were read with
`scripts/card.ps1 get/history CARD-0983 -Board Antiphon`; history contained the
current Plan dispatch move, with no superseding implementation verdict.

## Ground truth

| Card assumption | Current code / board evidence | Consequence |
|---|---|---|
| jq absence silently lowers 19/55/197 to a green result. | `scripts/test-deploy-server2.ps1:19-40` computes one boolean probe; the false branch at T-20 still prints `C973_SKIPPED`. The current final counters are **23 groups / 62 invocations / 218 assertions** without jq and **24 / 66 / 227** with jq. T-21..T-24 explain the growth since the card's evidence. | Preserve today's totals; do not restore the historical 19/55/197 literals. |
| A probe false negative and actual absence are indistinguishable. | `Test-C973Jq` returns false for the forced-missing seam, missing shell, nonzero command and caught launch exceptions. It already selects the first application from `Get-Command`, fixing the historical duplicate bash-path bug. | Keep that probe and first-result selection. The opt-in rejects every false result without claiming to diagnose whether jq is actually installed. |
| The driver present case requires jq. | `scripts/test-deploy-server2-jq.ps1:37-65` derives `$expectPresent` from both Case and the probe. `present` plus false prints `C973_JQ_SKIPPED` and accepts the smaller roster. No `RequireJq` switch or `C973_REQUIRE_JQ` setting exists in either script. | Add enforcement at both entry points. Installing jq alone does not close the receipt gap. |
| All absence tests should become failures. | Driver cases `absent`, `missing-shell` and `failing-shell` deliberately exercise fallback. `present` clears inherited probe stubs and uses the real shell. All four successful driver cases currently report **31 assertions**. | Leave ordinary negative cases intact. An explicitly required negative case must return failure, useful as a deterministic guard test. |
| Nothing already prevents downgrade in a consumer. | `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain` runs all four driver cases and already rejects `C973_JQ_SKIPPED` for present. It is a Unit test with `ParallelLimiter<ProcessSpawnLimit>`. | This consumer is stricter than the standalone driver. Pass the new flag in its present invocation, making the requirement explicit and keeping its existing oracles and assertion counts. |
| CARD-0927 supersedes this card. | Done at `a70cb666858fb1dbd730e03cf41388f5f0b2506f`. `docker/session-runner-grok/Dockerfile:101-109` installs SHA-256-pinned jq 1.7.1 in runtime-base. It does not change either harness entry point, and the card records activation pending image rebuild/rollout. | This is complementary. Do not change the image or infer installed runtime state from its Dockerfile. |
| CARD-1008 still blocks planning. | Done at `0049877688f895bc4fb75e49cfe8911489b2a937`, contained in the assigned base. Its deploy/recycle code and Remote/Rolling tests are present. | Hold resolved. Retain its current fixtures, faster fake HTTP calls and all recycling assertions. |
| jq inside the runner proves jq on the deployment host. | CARD-1025 is open: the bare-host recycle path requires host jq and refuses `RecycleToolsMissing` without it. Runner, host and Windows WSL are distinct environments. | This card neither installs host jq nor authorizes a rollout. Record which shell produced every required receipt. |
| The image-contract CP-2 minimum is still 100. | Fetched master `70d7e2612` changes the CARD-0927 plan to **163**. Its historical shell CP-3 remains 84. Current Remote source has **77 methods / 95 expanded cases**, after C1008. | Leave those other manifests untouched; do not use their historical counts for this card. |
| Shared checkpoint census should grow for new tests. | `scripts/lib/checkpoint-usage.ps1:114` still has `selected = 377`, for the Checkpoints namespace. New guards belong to Scripts. | Preserve literal **377** and its independent census behavior. |

Source-counted affected regression roster: C849 = 18, C912 = 9, C973 = 12,
C946 = 2, total **41** expanded results from 32 methods. This is source
inspection, not test execution. CARD-0980 plans four other Remote tests, so a
future whole-class count will change; this plan selects its own guards and the
41 existing affected regressions explicitly.

## Decisions

- **D-1 — A switch on both scripts, default false.** Add `[switch]$RequireJq`
  to `scripts/test-deploy-server2.ps1` and `scripts/test-deploy-server2-jq.ps1`.
  Invocation examples are `pwsh -NoProfile -File scripts/test-deploy-server2.ps1
  -RequireJq` and `pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1
  -Case present -RequireJq` (test execution takes a build slot). Reject an
  additional environment-variable interface: one explicit mechanism avoids
  inheritance/precedence rules and accidental changes to optional runs.
- **D-2 — Reject before groups, with a stable diagnosis and exit 1.** Keep the
  single `C973_JQ_PROBE available=False` line. At the beginning of the harness's
  existing `try`, before the `$Only` dispatch and before any `Run-C727`, throw
  exactly `C983_JQ_REQUIRED jq-unavailable: -RequireJq requires jq in the marker-reader shell; T-20 cannot be skipped.`
  when the flag is set and the probe is false. Existing catch/finally then
  reports `C849_ROLLING groups=0 invocations=0 assertions=0 failures=1`, exits
  **1**, and retains failed evidence under the current ownership rules.
  No PASS group, jq-skip notice or success summary may precede or follow this
  failure. Apply the prerequisite even with `-Only`: explicit require must
  never be silently ignored. Reject exit 0 with a warning, or exit 2 (used by
  the deployment driver for a different class of refusal).
- **D-3 — Forward the flag and independently refuse driver fallback.** Add
  `-RequireJq` to the child's `ProcessStartInfo.ArgumentList` when requested.
  After verifying the child exit and single probe line, but before calculating
  fallback/printing `C973_JQ_SKIPPED`, make required-plus-unavailable throw the
  same diagnosis. Use a conditional throw, not another unconditional
  `Assert-Jq`, so successful driver runs keep **31** assertions. The driver
  retains its existing catch, exit 1 and evidence path. A harness failure's
  original diagnosis remains visible in captured output. This second check
  catches a future child that incorrectly returns success with a false probe.
  Reject merely requiring jq in the caller while leaving either entry point
  able to downgrade.
- **D-4 — Preserve all ordinary behavior.** No flag means today's real probe,
  skip text, rosters, marker-state assertions and cleanup behavior. Required
  plus available runs the unchanged T-20 and full roster. Required plus any
  forced negative driver case is an actual failing command; do not translate
  it into a successful expected-failure driver receipt. Keep `present` clearing
  injected probe seams and preserve the default WSL-versus-bash selection.
  Reject new retries, fallback shells, probe redesign or timeout changes.
- **D-5 — Make the existing jq-expected consumer explicit.** Update only the
  present invocation in `C1008_Legacy_rolling_and_jq_rosters_remain` to pass the
  flag; leave the other three invocations optional. It already requires real
  jq, so this does not introduce a new prerequisite into that Unit consumer.
  This also exercises required receipts when the normal Unit/nightly roster
  runs it. Document the standalone required command in the testing owner;
  do not add another nightly job or rewrite old receipt files.
- **D-6 — Native Windows needs its own final-SHA proof.** A separate Debug
  task runs native Windows PowerShell/.NET with the marker reader in WSL at
  the final pushed implementation SHA. Running the whole test host inside
  WSL cannot prove `$IsWindows` and native argument forwarding. Require actual
  WSL bash, jq and pwsh for the named regression lane. Missing prerequisites
  leave that proof incomplete, without changing optional-host behavior.
- **D-7 — Bounded test/script change and regression-only review.** The ordinary
  profile is the seven new guard cases, the existing four-mode rolling consumer
  and the 41 affected script regressions on both platforms. Omit full Unit,
  full Remote/C1008, Docker deployment and Pty suites: no production code,
  shared shell helper, image or application behavior changes. Reproduce extra
  failures at the unchanged task base before calling them inherited. Do not
  repair unrelated portability defects or weaken expectations under this card.

## Slices and files

One atomic implementation slice avoids publishing a flag that its caller cannot
use. Commit and push before any checkpoint; follow-up fixes get their own
commits before affected-row reruns.

| Slice | Files and change | Tests / completion evidence |
|---|---|---|
| S1 | `scripts/test-deploy-server2.ps1`: switch and early guard inside existing error/cleanup boundary. `scripts/test-deploy-server2-jq.ps1`: switch, argv forwarding and independent required-probe guard. | V-1 and V-2 below; exact failure and full/default roster witnesses. |
| S1, same commit group | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: two new C983 methods, seven expanded results. `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`: required flag only in the present consumer. | CP-1..CP-3 on Linux; CP-4..CP-6 on Windows at the identical final SHA. |
| S1, same commit group | `docs/testing-and-build.md`: short offline-harness usage note, required/optional distinction, current receipt counts and shell-specific prerequisites. | Inspection against D-1..D-5; keep current manifest/build-slot/evidence policy unchanged. |

No edits to `deploy-server2.ps1`, `c590-remote.sh`, c727 fixtures, Dockerfile,
nightly scheduling, checkpoint tooling, census, runtime code or generated
`docs/cards` files. Preserve ASCII in touched PowerShell scripts.

## Placement, collisions and ordering

GET `/api/runner-defaults` and `/api/session-runners` were read at approximately
2026-10-04 00:56 UTC. Defaults revision 2 selected an available Linux runner;
an eligible Windows runner was also available; the temporary entry was
unavailable/draining. These are observations, not host pins or fleet locations.
Re-read both at dispatch. Omit `-Runner`; omit `-Platform` for portable work.
Use `-Platform Windows` only for the native Windows proof; `-Platform Any`
clears an inherited pin. The table below names lanes in Group, without adding
an unsupported Platform column to the checkpoint importer.

| Work named in brief | Observed status / collision and required order |
|---|---|
| CARD-1008 | Landed prerequisite; use its current source and preserve its assertions. |
| CARD-0980 Plan `0c961739` | Succeeded, plan `6236cd04a10cdd36ed6867977e3e3ef57f484a32`, next TestDesign. Direct overlap in RemoteScriptContractTests and C946/C849 verification. **Order its shared-file implementation and Windows qualification before CARD-0983 S1**, then refresh this plan's filters/roster. No simultaneous Code edits to that file. Do not import its proposed helper or redo its repair here. |
| CARD-1010 Plan `adbad12b` | Succeeded, plan `2395858ecc0a6ab2e8acc3b2deb2cb6298ed1307`, next TestDesign. Its first slice is the server admission contract; later recycle slices touch deploy scripts and Remote/Rolling tests. Sequence those shared test/script slices after CARD-0980 and CARD-0983, unless already admitted; an existing active owner finishes first, then rebaseline. Its server-only slice need not wait for this card. |
| CARD-0959 Code `bd02f8d9` | Dispatched at scoped read; inert runner-version observation work, no intended S1 overlap. Leave its capability/image/version contracts alone. Recheck footprints before Code. |
| CARD-1011 replay `ffc7e9ad` | Dispatched; predecessor `d422c5a9` Blocked. Bundle/routing replay, no intended source overlap. Coordinate Windows capacity and current dispatch rules; this card needs no Grok/provider/backend canary. |
| CARD-1013 Windows Debug `9efba31e` | Dispatched; Code owner `2c35a27d` Blocked. Shared Windows capacity and possibly checkpoint checkout-byte rules. Await available slots, use own outputs, and read its landed tooling before importing. Do not change coverage tooling or literal 377. |
| CARD-1017 freeze / Code | Card InProgress; Code `c37846cc` Queued at scoped read. Whole-worktree cleanup is separate. Keep evidence ignored and durable facts in the stored report; no cleanup-policy or deletion-authority changes. |
| CARD-1020 / CARD-1022 freezes and plans | Cards Review, absent from that active board listing. Native-process cleanup and inbox-backend removal are separate. No Pty tests or backend matrix here. |
| CARD-1025 | Open host-jq dependency for live recycling, not a blocker to writing this opt-in or running isolated fixture tests with suitable jq. No host install in this dispatch. |
| CARD-1024 Code `aae956f6` | Dispatched evidence-policy cleanup. No implementation overlap; preserve its deletion ownership and do not commit generated evidence. |

Statuses are a point-in-time board-scoped observation, not a reservation.
Repeat the collision check before Code/Windows dispatch and before landing.
Never rebase/reset/amend the assigned pushed task branch to absorb concurrent
work; landing owns integration. A fresh Code task can start from the reconciled
landed base after the shared-file predecessor completes.

## Activation

1. Publish this plan, then freeze it through separate TestDesign.
2. After the shared-file sequence, implement S1 and commit/push. Run the Linux
   rows, then commission the bounded native Windows Debug at that exact final
   SHA. Any subsequent source repair requires affected evidence at its new SHA.
3. Ordinary Review applies the operator's regression-only verdict policy and
   checks both platform receipts. It must distinguish incomplete Windows
   acceptance from an introduced defect, with method-scoped base evidence for
   inherited failures. Land through the implementation's normal landing owner.
4. Activation is **tests/scripts only**: new checkouts/callers get the flag;
   jq-expected invocations opt in. No AppHost restart, runner restart, image
   rebuild, remote deploy, volume recycling or migration is required. CARD-0927
   image activation and CARD-1025 host installation remain separate obligations.
   Do not relabel a pre-landing receipt as proving a different source SHA.

## Verification design

Proposed design for TestDesign to freeze; no builds/tests ran in this Plan.
Use native `pwsh` children with `ProcessStartInfo.ArgumentList`, bounded process
ownership, concurrent stdout/stderr drains, and the existing assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. Do not mutate global environment/PATH.
Set failure seams on each child, clear inherited seams for genuine-present
proof, and never use a live runner, operator credential or Docker daemon.

| ID | Named test / evidence | Required assertion |
|---|---|---|
| V-1 | New `RemoteScriptContractTests.C983_Required_jq_refuses_unavailable_probe_before_any_group`, six argument cases: harness and driver crossed with absent, missing-shell, failing-shell. | Harness uses existing `C973_TEST_JQ_PROBE=missing`, nonexistent shell, or the existing git-as-failing-shell seam; driver uses its three existing negative Cases plus `-RequireJq`. Real processes exit 1, emit one false probe and the exact C983 diagnosis, emit no PASS group or either jq-skip notice, and create no invocation state/trace files. Harness zero-counter failure summary is present. Verify failed evidence retention, then remove only the returned, validated, invocation-owned root. No assertion may depend on jq actually being absent from the host. |
| V-2 | New `RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe`, one result. | In a test-owned scratch layout, copy the real driver unchanged and supply a tiny sibling harness fixture accepting the switch, emitting a single false probe and exiting 0. The real driver must exit 1 with the C983 diagnosis before its fallback/roster checks, with no `C973_JQ_SKIPPED`. This witnesses the independent driver guard rather than only the child's exit-code check. Record argv at the fixture to assert `-RequireJq` forwarding. Clean only owned scratch. |
| V-3 | Existing `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain`, one result containing four driver runs. | Present explicitly requires jq: true real probe, T-20 PASS, exactly 24/66/227, four marker states, no skip. Absent/missing-shell/failing-shell remain optional: one false probe, named skip, exactly 23/62/218, no marker states. All four driver receipts remain assertions=31, failures=0 and exit 0. Missing jq on this expected host is failure, never a smaller green present roster. |
| R-1 | `RemoteScriptContractTests` methods selected by C849, C912, C973 and C946 prefixes, 41 existing results. | Existing cache/marker and owned-root cleanup behaviors still execute with zero failed/skipped results. Includes the five CARD-0980 C849 callers and C946's green-cleanup/failed-retention test; keep their existing assertions. |
| R-2 | Diff/owner-document inspection, included in S1 review. | No default/probe/roster/cleanup changes beyond the flag path; no production script or runtime edits. The existing first application selection remains. Successful driver assertion count remains 31; required mode is documented and selected by the existing expected-jq consumer. |

The two new methods add seven results, not seven new methods. TestDesign must
freeze arguments/names and recount if it changes expansion. Current whole
Remote would be 102 after these additions, or 106 if CARD-0980's four proposed
tests land unchanged first; neither total is the floor for the focused rows.

Guard/control candidates for the separate freeze: remove the harness's
required-false refusal (V-1 harness cases), drop switch forwarding (V-1 driver
cases and V-2 argv), bypass the driver's own required-false check (V-2), or make
require unconditional and break optional absence (V-3). These are distinct
oracles. PCs stay pending for the normal post-land Mutation stage and use exact
method filters. Unknown-parameter errors, missing fixtures, failed builds,
zero tests and timeouts are not the intended red. TestDesign must specify a
red witness after valid parameter plumbing, or an exact guard-removal control,
instead of accepting old-script parameter-binding failure as guard proof.

### Linux and final-SHA Windows procedure

Linux Code selects CP-1..CP-3. The caller separately commissions **Debug with
`-Platform Windows` and `-StartRef <full-final-implementation-sha>`** for
CP-4..CP-6, in its own Worktree. Name the original Code owner in its brief;
this proof task supplies evidence and does not become the implementation
landing owner. No Windows checkout is reachable from this Plan mirror.

The Windows task must record native OS and Debug configuration, and inspect
`wsl -e bash -c 'command -v jq; jq --version; command -v pwsh; pwsh --version; command -v wslpath'`
through the same default WSL distribution the harness uses. Linux records the
corresponding native bash tools. jq 1.7.1 is the existing pin/approved WSL
version; do not install or silently substitute a shell in this card. A native
Windows jq alone cannot satisfy this precondition. V-1/V-2 run on Windows even
when jq is unavailable; V-3/R-1 required success needs the real WSL tools.

Report all seven guard results, four internal driver receipts and 41 regression
results separately. Check the actual C973/C849 receipt lines in addition to
TUnit counts: one test enclosing four child runs is still one executed result.
No jq/pwsh/WSL skip qualifies an expected-tool lane. If an extra Windows failure
appears (C1008 host fixtures have separately noted native-path risks), reproduce
only that exact method at the unchanged Code base using separate owned outputs
before classification. Record the conditional baseline run and reason. A
confirmed inherited failure is a disclosure under regression-only Review;
it does not turn an unmet row into a pass or authorize an unrelated repair.

### Execution, evidence and cost

After S1 is committed/pushed and source frozen, use the checkpoint tool:
`run --plan docs/superpowers/plans/2026-10-04-card-0983-require-jq-plan.md
--rows CP-1,CP-2,CP-3 --expected-source-sha <sha> --serial` on Linux;
Windows selects `CP-4,CP-5,CP-6`. TestDesign validates the real importer before
Code. Each row has one exact filter; rows reuse only a same-slice, same-platform
build. Select the lane explicitly rather than running all six on one OS.

If a bootstrap is necessary, name and lease that administrative build:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c983-checkpoint-tool --
dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c983-tool/ --nologo`.
Invoke the built tool with `dotnet run --project tools/Antiphon.Checkpoints
--no-build --property:OutputPath=bin-c983-tool/ -- run ...`; its executor leases
each build/test row itself. Await completion and keep calling `wait` after
exit 75. Do not leave an executor alive on settlement or edit under a run.
Slot timeout/refusal means not run, never permission to retry unleased.

Store unedited CHECKPOINT lines, exact tested SHA, configuration, expanded
roster and executed/passed/failed/skipped counts in the final report. Require
clean source and verified build provenance; validate SHA-bound receipts with
`scripts/validate-checkpoint-receipt.ps1`. Keep raw logs/TRX/JSON and checkpoint
outputs ignored. Code/Review run `scripts/check-evidence-diff.ps1 -BaseRef
<task-base> -HeadRef <pushed-sha>` across the full task history. Remove only the
owned `bin-c983-*` outputs (including per-project bootstrap outputs) after
children exit. No shared worktree/evidence cleanup authority is added.

Estimated ordinary floor: **40 minutes**, 17 Linux and 23 Windows, plus
authoring, tool bootstrap if needed and slot wait. No full suite, live deploy,
loaded repetitions or PC execution in this ordinary list. TestDesign may
refine estimates from current receipts without weakening counts or assertions.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c983-linux/` | linux-required-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2 | 7 executed/passed, 0 failed/skipped; both named methods and six negative vectors | 7 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | linux-real-jq-and-optional-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | 1 executed/passed, 0 failed/skipped; all four 31-assertion driver receipts and exact full/fallback harness rosters | 1 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | linux-cache-marker-cleanup-regressions | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | 41 executed/passed, 0 failed/skipped; all five repaired C849 callers present | 41 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c983-windows/` | windows-native-debug-required-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2 | 7 executed/passed, 0 failed/skipped at final SHA; native Windows parent | 7 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-4 | windows-native-debug-wsl-jq-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | 1 executed/passed, 0 failed/skipped; four 31-assertion receipts, true WSL jq probe and T-20 in required present | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | CP-4 | windows-native-debug-wsl-regressions | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | 41 executed/passed, 0 failed/skipped at same final SHA; five repaired C849 callers pass through real WSL | 41 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
