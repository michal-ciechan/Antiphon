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

## Provisional verification (Plan stage; superseded by the freeze below)

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

### Proposed checkpoints (historical; not the importer manifest)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c983-linux/` | linux-required-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2 | 7 executed/passed, 0 failed/skipped; both named methods and six negative vectors | 7 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | linux-real-jq-and-optional-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | 1 executed/passed, 0 failed/skipped; all four 31-assertion driver receipts and exact full/fallback harness rosters | 1 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | linux-cache-marker-cleanup-regressions | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | 41 executed/passed, 0 failed/skipped; all five repaired C849 callers present | 41 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c983-windows/` | windows-native-debug-required-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2 | 7 executed/passed, 0 failed/skipped at final SHA; native Windows parent | 7 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-4 | windows-native-debug-wsl-jq-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | 1 executed/passed, 0 failed/skipped; four 31-assertion receipts, true WSL jq probe and T-20 in required present | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | CP-4 | windows-native-debug-wsl-regressions | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | 41 executed/passed, 0 failed/skipped at same final SHA; five repaired C849 callers pass through real WSL | 41 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Verification design

Frozen by TestDesign `896f3d65-e2bf-48d7-aeef-27352fd20582` on 2026-10-04.
This appendix supersedes the provisional verification, checkpoint table and
ordering observations above; **D-1..D-7 and S1 remain the fix design**. The old
headings are renamed solely so the importer reads this single active manifest.
Only this plan changes. TestDesign runs no builds, tests or mutations.

### Inspection

- Read CARD-0983 through `scripts/card.ps1 get CARD-0983 -Board Antiphon`, this
  plan, the checkpoint/import/build-slot/PC owner contracts, and the stage and
  landing rules. Start is `facd671834dc301b26a0ab14a11b9983970fc9b8`; refreshed
  master is `8bb0cea045f5a89359b0ba55712417f02e31a42a`. The two harness scripts,
  Remote/Rolling test sources and census file have no diff between these refs.
- Read `Test-C973Jq`, probe publication, `Run-C727`, the `Only` dispatch, T-20,
  final roster/catch/finally, and the entire jq driver (argument construction,
  seams, child exit/probe guards, roster/state assertions, retention/cleanup) |
  required/optional x available/unavailable, default/Only, child success/failure,
  missing/duplicate probe, owned/caller-owned root -> V-1..V-3, R-1..R-2.
- Read the neighboring C946 cleanup tests and C973 reader tests and
  `C973ReaderHarness`; all five C849 pwsh caller bodies named below;
  `LinuxShell`, cached tool probes, `RequireLinuxPwsh`, `RequireLinuxJq`,
  `CacheSeedTreeHarness`, `Block`, `Order` and `Remote`; `DelegateScriptRunner`
  and `ProcessSpawnLimit` | genuine shell availability, Windows path boundary,
  marker identity, failure retention and process ownership -> V-1..V-3, R-1.
- Read `C1008_Legacy_rolling_and_jq_rosters_remain` and its `C1008Process`
  helper; `c727-fake-verify.ps1` and `c973-marker-reader.sh` |
  real driver/harness/marker reader with private HTTP/Docker substitutes -> V-3.
  New tests use the existing Remote class; no new tracked fixture file is needed.
- Read `PlanTableImporter`, `ManifestValidator`, `CheckpointManifest`,
  `AfterSelector`, `RowTimeout`, `Program.Import`, the import tests for escaped
  filters/rosters/serial/environment, and the manifest/fixture scope setup |
  first exact heading, nine ordered columns, count versus time, derived tokens,
  serial execution and child environment -> six rows below and read-only import.
- Read CARD-0980's freeze at
  `17485f615bdc38c28214b1ab0dca103f929ab86b` on
  `origin/feat/card-task-1e3dddab`: **CARD-0980 S1, then CARD-0983 S1, then
  CARD-1010 S1**, serializing Code/land on the shared source. CARD-0980 Code
  `fa69124f` was in flight in the dispatch brief. Await its implementation and
  native Windows qualification; reconcile its landed source before this Code
  edits Remote. Do not replay the old class or rebase this published branch.
- Source recount at refreshed master: Remote **77 methods / 95 expanded
  results**. Argument expansions are C951=8, C957=3, and C973=6+5. The selected
  old regression prefixes are **C849 18 + C912 9 + C973 12 + C946 2 = 41**
  results from 32 methods. C983 adds **two methods / seven results**; Remote
  would be 79/102 alone or **83/106** after CARD-0980's four single-result
  additions. The focused regression floor stays **41**, not 106.
- Current harness literals and driver assertions independently agree on
  **23 groups / 62 invocations / 218 assertions** without jq and
  **24 / 66 / 227** with jq; each successful jq driver remains **31 assertions**.
  These internal counts are not TUnit executions. CARD-1008/1012/1013/1011/1018
  are accounted for by the current-master read. **CARD-0927 CP-2 stays 163**;
  this card changes no image-contract floor or historical receipt. No tests
  enter **Antiphon.Tests.Checkpoints**; census literal **377 stays unchanged**.
  Only this card's seven new Scripts results increase its affected test roster.

Missing setup: Code must author both C983 methods and their private native-pwsh
process/scratch helpers. No test or receipt is claimed to exist yet. Resolve
native pwsh and git to absolute executable paths before changing a child's
PATH; use `ArgumentList`, concurrent stdout/stderr drains, bounded wait and
kill-tree/await on failure. Both new methods carry the assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. Do not change global PATH or the shared
`LinuxShell` helper. Expected-tool tests fail on missing prerequisites rather
than skip. Desktop WSL cannot be inspected from this mirror.

### Delivery inventory

**Zero new or changed asynchronous delivery paths.** The changed path is a
synchronous test-process invocation and its exit/output contract. No durable
message identity, queue, recipient session, persistence handoff or recovery
worker changes; busy/eligible recipient and crash/enqueue-failure queue tests
are therefore inapplicable. No output marker, acknowledgement or checkpoint
receipt is claimed to prove session delivery; that would require the matching
complete UserPrompt transcript.

V-2 substitutes a sibling child harness to isolate the real driver's independent
guard and argv forwarding. It cannot prove the real harness probes jq or runs
T-20. V-1 runs the real harness; V-3 closes the real driver -> harness -> shell
marker-reader boundary. Its existing HTTP/Docker fakes cannot prove live
rollout, actual cache publication, restart recovery or caller delivery. The
sealed-PATH vector proves refusal of a false probe, not physical absence of jq.

### Proves it works now

Exactly seven new TUnit results, with these two method names:

- **V-1:** refuse required unavailable jq before any group | native process
  integration | `RemoteScriptContractTests.C983_Required_jq_refuses_unavailable_probe_before_any_group`
  with exactly six `[Arguments]`: `("harness","absent")`,
  `("harness","missing-shell")`, `("harness","failing-shell")`,
  `("driver","absent")`, `("driver","missing-shell")`,
  `("driver","failing-shell")`. Each process uses `-RequireJq`. Harness
  vectors use `C973_TEST_JQ_PROBE=missing`, an absolute nonexistent shell, or
  absolute git as the failing `-c 'command -v jq'` application. Driver vectors
  use the existing `-Case` values. Clear inherited C973 seams before setting
  each vector. Verify exit **1**, one `available=False` probe and exactly this
  diagnosis (match the complete diagnostic line, allowing CRLF):

  `C983_JQ_REQUIRED jq-unavailable: -RequireJq requires jq in the marker-reader shell; T-20 cannot be skipped.`

  Fix assertion labels/order: `c983-required-diagnosis` (exact diagnostic),
  `c983-required-exit` (exit 1), `c983-single-false-probe` (one false probe),
  `c983-before-group` (no `PASS T-`, no `state.json` or `trace.jsonl` recursively),
  `c983-no-skip` (neither `C973_SKIPPED` nor `C973_JQ_SKIPPED`),
  `c983-zero-failure-roster` (one `C849_ROLLING groups=0 invocations=0 assertions=0 failures=1`),
  and `c983-failure-retained` (actual evidence directory exists). No V-32 success,
  zero-failure rolling summary or driver success receipt is permitted.
  Driver failure output must preserve the child's original diagnosis; do not
  mistake its additional `harness exit=1` message for the required diagnosis.

  Within the harness/absent result, run `-Only host-saved` **first**, then default
  all, and then the other accepted Only selections (`retired-start`,
  `cleared-offline-start`, `host-race`, `host-absence`, `host-recovery`,
  `cleanup-failure`) with the same missing seam. Include explicit `-Only all`.
  Within required negatives cross omitted/true `-KeepTemp`; for the direct
  harness exercise both its normal owned root and a separately created
  `C973_TEST_ROOT` containing a
  harmless sentinel. An external root must retain that sentinel. These are
  internal vectors, not extra TUnit results. Early root creation is allowed;
  an invocation state or trace file is not. Validate returned root ancestry,
  expected GUID leaf and no reparse point before test-owned cleanup; delete
  only an invocation-owned root after asserting retention. Do not infer root
  ownership from arbitrary output or delete an ancestor.

  **Deterministic false-negative vector:** also within harness/missing-shell,
  start an absolute native pwsh with a test-owned wrapper and working directory.
  After PowerShell starts (it can prepend its own directory at startup), set
  that child process's PATH to **one newly created, empty scratch directory**,
  with no appended host PATH. Clear both C973 seams, verify the directory is
  empty and PATH equals its exact full path, then invoke the unchanged real
  harness by absolute path with `-RequireJq`. Default bash/WSL command lookup
  must be unavailable and produce the same early failure. Never derive this
  negative from the Windows host PATH. This complements the explicit missing
  shell seam and needs no actual host jq absence. Do not run the driver under
  this sealed PATH: its bare `pwsh` child launch would fail before the jq guard.
  The real driver's three negative Cases and V-2 cover that boundary instead.

- **V-2:** independently reject a successful false probe, retain optional and
  successful neighbors | unchanged real driver in owned scratch layout |
  `RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe`,
  one `[Test]`, no Arguments expansion. Copy the actual driver bytes into
  `<scratch>/scripts/test-deploy-server2-jq.ps1`; write a sibling harness fixture
  with a valid `[switch]$RequireJq` parameter. Pre-create an argv observation
  file outside the driver's evidence root. The child appends bound switch and
  unbound argv observations before printing output; missing forwarding then
  gives a normal assertion failure, not a missing-file error.

  The sibling implements complete valid optional-fallback **and** present
  receipts: 62 ordinary state.json files with a non-cold scenario and no marker,
  one PASS for T-1..T-19/T-21..T-24, named skip and 23/62/218 for false; add four
  cold marker states and T-20, omit skip, and emit 24/66/227 for true. Select
  fixture behavior with a child-only C983 test variable, independent of the
  bound RequireJq value. It exits normally with the selected code. It must
  never print the C983 diagnosis itself. Use `-Case present` in the real driver.

  Execute these internal vectors in order: required + false + exit 0;
  optional + false + exit 0; required + true + exit 0;
  optional + true + exit 0; required + true + exit 7; required + zero probe
  lines; required + two identical true probe lines. The last three otherwise
  emit a valid full roster. A separate optional + false + exit 7 vector covers
  the fallback branch's child-exit refusal. Required/optional covers both
  omitted switch and explicit `-RequireJq:$false` for the optional neighbor.

  First require exactly one child observation, bound RequireJq=true for the
  required vector (`c983-forwarded`), and false for both optional spellings.
  Then require the exact C983 diagnosis (`c983-driver-diagnosis`), exit 1
  (`c983-driver-exit`), no driver skip or successful driver receipt
  (`c983-driver-no-fallback`), and an existing driver-created evidence root
  containing harness.log (`c983-driver-retained`). The synthetic child's own
  fallback skip is expected in its captured output: only **C973_JQ_SKIPPED**
  is forbidden here, unlike V-1. This distinction avoids an impossible oracle.
  Optional/false must exit 0 with `C973_JQ_SKIPPED`, 31 assertions and 23/62/218
  (`c983-optional-driver`); both true success vectors have 31 and 24/66/227 with
  no skip (`c983-available-driver`). Nonzero child exit must yield the driver's
  `harness exit=7` failure (`c983-child-exit-guard`) before any fallback. Missing
  or duplicate probe lines must yield `one jq probe` (`c983-probe-cardinality`),
  not a C983 diagnosis or a success receipt. Check retention with/without
  KeepTemp on failed vectors and cleanup/kept receipt on successful neighbors.
  All file operations stay under the fixture's owned root.

- **V-3:** a real jq-required consumer still runs T-20 and all default modes |
  native TUnit -> real driver -> real harness -> bash/WSL marker reader |
  `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain`.
  Keep its four modes and existing assertions. Construct the actual argument
  list once; add `-RequireJq` only for present and assert that very list contains
  it before launch (`c983-consumer-requires-jq`). Pass the same list to the
  existing process helper. This argv observation proves consumer opt-in; V-1
  and V-2 prove child enforcement. Do not add an assertion to `Assert-Jq` or
  change the driver's successful 31-assertion count.
  Preserve `rolling-regressions-preserved`: four `C973_JQ case=... assertions=31 failures=0`
  receipts and exit 0, with no present skip. The real driver's existing oracles
  require one true probe, T-20, four cold marker states and 24/66/227 for present;
  three false probes, no marker states, named skips and 23/62/218 for negatives.
  Record those harness lines as well as the single passed TUnit result.
  A jq-free present run is a failure, never accepted as a smaller green roster.

Required + available + Only is covered by the same flag predicate and default
full success; no second full eight-selector success matrix is needed. The
unavailable Only cross explicitly tests guard placement before every early exit.
No additional environment-variable interface or invalid CLI-value behavior is
introduced. Existing PowerShell parameter binding owns unsupported arguments.

### Guards the regression

- **R-1:** preserve cache/marker/cleanup regressions | exact prefix filter in
  CP-3/CP-6, **41 results**, zero failed/skipped. Keep every existing assertion,
  including C946 GREEN_EXIT=0 / GREEN_ROOT_REMOVED / FAILED_KEEP_EXIT=1 /
  FAILED_KEEP_ROOT_RETAINED. The five C849 native Windows witnesses must remain
  in the executed roster: `C849_Cache_cases_use_only_the_validated_host_lane`,
  `C849_Seed_publishes_complete_payloads_before_its_marker`,
  `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`,
  `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`,
  `C849_Saved_donor_rejects_declared_size_bomb_before_writing`.
- **R-2:** preserve defaults and scope | V-2/V-3 positive neighbors plus source
  review. Default switches are false; the first application probe selection,
  default Windows WSL/Linux bash branches, marker semantics, rosters, timeouts,
  ownership guards and successful driver count remain intact. The documentation
  describes required versus optional usage and shell prerequisites. The diff
  stays in S1 files; ASCII PowerShell is preserved. No new Checkpoints tests,
  census changes, production deploy script edits or runtime changes.

**Native Windows Debug gate:** separately commission `-Platform Windows` at
exactly the final pushed implementation SHA, with the original Code owner named,
selecting CP-4..CP-6 and native .NET Debug configuration. Record OS/configuration,
full source SHA, clean source/build provenance and executed/passed/failed/skipped
counts for each row. Only the marker-reader/bash children run inside WSL; a WSL
TUnit host cannot discharge native Windows argument forwarding or `$IsWindows`.
In the default desktop WSL distribution, inspect the actual noninteractive
`wsl.exe -e bash -c` lane: bash, wslpath, **/usr/local/bin/jq = jq-1.7.1** and
**/usr/local/bin/pwsh = PowerShell 7.6.6**, plus tar, perl, git and the GNU tools
used by the existing fixtures. Record resolved paths and versions. Do not count
native Windows jq or Linux-image installation as WSL proof. Linux uses its actual
native bash/jq/pwsh lane. Missing setup means incomplete qualification; do not
install tools, append host PATH, add skips or alter prerequisites under this card.

Regression-only classification: reproduce an unexpected failure using **only its
exact method** at Code's recorded unchanged start SHA, same native OS, WSL setup,
Debug configuration and build-slot gate, in a separate owned baseline checkout
with fresh output. The freeze tip is the base if that is the actual Code start;
if integration supplies a later start, record that exact SHA. Failure at both
final and base is inherited; base passing means introduced; missing method,
build/fixture failure or no execution is unclassified. New C983 tests absent at
base are not inherited failures. Keep conditional baseline receipts and reasons
separate from the six ordinary rows. Inherited failure is a disclosure, never a
passing row or completed Windows acceptance. Do not repair unrelated portability
under this card. Subsequent implementation edits invalidate affected evidence
and require final-SHA Linux/Windows reruns before Review.

### Guard inventory

These are all safety-critical contracts newly enforced, newly wired, or directly
relied upon by the new refusal path. Child-exit and single-probe checks are listed
because they gate the new driver decision. Independent predicates, ordering,
forwarding, exit/diagnosis, and evidence-retention boundaries are split. The
unchanged production cache/recycle guards and generic deletion-authority checks
remain their owners' PC obligations and R-1 regression coverage; this card makes
no new claim to requalify all deployment guards. None of the listed guards is
left untested.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-2: harness required + false probe refuses | PC-1 |
| G-2 | D-2: refusal precedes every Only/group early exit | PC-2 |
| G-3 | D-3: driver forwards RequireJq to its actual child | PC-3 |
| G-4 | D-3: driver independently refuses successful false probe | PC-4 |
| G-5 | D-1/D-4: harness absence remains optional without the switch | PC-5 |
| G-6 | D-1/D-4: driver absence remains optional without the switch | PC-6 |
| G-7 | D-4: available jq passes the harness's new prerequisite | PC-7 |
| G-8 | D-4: available jq passes the driver's new prerequisite | PC-8 |
| G-9 | D-2: harness refusal preserves the exact stable diagnosis | PC-9 |
| G-10 | D-3: independent driver refusal preserves that diagnosis | PC-10 |
| G-11 | D-2: harness refusal exits 1 | PC-11 |
| G-12 | D-3: driver refusal exits 1 | PC-12 |
| G-13 | D-2: new harness failure retains its owned evidence | PC-13 |
| G-14 | D-3: new driver failure retains its owned evidence/log | PC-14 |
| G-15 | D-5: jq-expected existing consumer opts in explicitly | PC-15 |
| G-16 | D-3: child nonzero exit cannot certify required or optional success | PC-16 |
| G-17 | D-3: exactly one child probe is required before classification | PC-17 |

### Positive controls

Code runs V/R; ordinary Review judges this design and evidence before land.
Post-land SourceLanding Mutation runs each **break / intended assertion red /
exact-byte restore / fresh build / green** cycle. Discover the named methods
and establish green first. Every defect below is syntactically valid and leaves
parameter plumbing intact; parameter-binding, timeout, fixture, build or zero-test
errors do not qualify. A surviving/equivalent control is a finding, not green.

Exact method filter dictionary (all in `tests/Antiphon.Tests`):

| Key | Exact --treenode-filter | MinExecuted |
|---|---|---:|
| F1 | `/*/*/RemoteScriptContractTests/C983_Required_jq_refuses_unavailable_probe_before_any_group*` | 6 |
| F2 | `/*/*/RemoteScriptContractTests/C983_Required_jq_driver_rejects_successful_false_probe` | 1 |
| F3 | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | 1 |

For F1 inspect all six argument rows. Its trailing `*` follows the owner's
parameterized-method rule and matches only this full method name and argument
suffixes; never widen to `C983_*` or the class. Retain MinExecuted=6.

| PC | Compiling defect breaking its mapped guard | Exact method and decisive red assertion |
|---|---|---|
| PC-1 | In the harness's new prerequisite condition append `-and $false` | F1, harness/absent default or Only vector: `c983-required-diagnosis` absent; real optional execution completes |
| PC-2 | Move the intact prerequisite below all Only early returns, immediately before `if ($hasJq)` | F1, first harness/absent `-Only host-saved`: `c983-required-diagnosis` absent after normal T-24 execution |
| PC-3 | Remove only the driver's ArgumentList addition of `-RequireJq` | F2: `c983-forwarded` expects true in the first child's recorded bound switch, gets false |
| PC-4 | Disable only the driver's required-unavailable condition with `-and $false` | F2: `c983-driver-diagnosis` absent; complete synthetic fallback reaches an otherwise successful driver receipt |
| PC-5 | Remove `$RequireJq -and` only from the harness prerequisite | F3: existing `rolling-regressions-preserved` fails on absent mode's missing 31/0 receipt after present succeeds |
| PC-6 | Remove `$RequireJq -and` only from the driver's prerequisite | F2 optional/false neighbor: `c983-optional-driver` expects exit 0 and 31/0, gets required refusal |
| PC-7 | Change only the harness prerequisite to `if ($RequireJq)` | F3 present: `rolling-regressions-preserved` lacks the 31/0 receipt because true jq is refused |
| PC-8 | Change only the driver prerequisite to `if ($RequireJq)` | F2 required/true neighbor: `c983-available-driver` expects exit 0 and 31/0, gets refusal |
| PC-9 | Replace only the harness's thrown C983 diagnostic with `C983_WRONG_DIAGNOSIS` | F1: `c983-required-diagnosis` exact line absent |
| PC-10 | Replace only the driver's independent thrown C983 diagnostic with `C983_WRONG_DIAGNOSIS` | F2: `c983-driver-diagnosis` exact line absent |
| PC-11 | Change the harness catch exit from 1 to 0; retain diagnostic and finally | F1 harness vectors: `c983-required-exit` expects 1, gets 0 |
| PC-12 | Change the driver catch exit from 1 to 0; retain diagnostic/evidence | F2 first vector: `c983-driver-exit` expects 1, gets 0 |
| PC-13 | In harness finally replace `$success -and -not $KeepTemp` with `-not $KeepTemp`, keeping all existing path/ownership checks | F1 owned root, no KeepTemp: `c983-failure-retained` fails because its valid root was removed |
| PC-14 | At driver catch entry delete only its already-created `$evidenceDirectory` via `.Delete($true)` | F2: `c983-driver-retained` expects its owned root and harness.log, both absent |
| PC-15 | Remove only the present-mode argument-list addition of `-RequireJq` from C1008 consumer, retaining its actual-list assertion | F3: `c983-consumer-requires-jq` expects the literal flag, absent before launch |
| PC-16 | Replace only `Assert-Jq ($proc.ExitCode -eq 0)` with `Assert-Jq $true`, preserving its assertion increment/name | F2 exit-7 valid-roster vector: `c983-child-exit-guard` lacks `harness exit=7` failure; child output is otherwise fully accepted |
| PC-17 | Replace only the one-probe Assert-Jq condition with `$true`, preserving count/name | F2 zero-probe vector: `c983-probe-cardinality` lacks `one jq probe` failure (new C983 refusal is the wrong classification); duplicate-true neighbor would also incorrectly succeed |

No mutation changes expected values or deletes a witness assertion. PC-15 changes
the test's real consumer invocation, not its oracle. PC-1/PC-2 use the ordinary
working PATH forced-absence vector for their red witness; a launch error from the
additional sealed-PATH vector is not accepted as their red. PC-13/PC-14 touch only
owned scratch evidence, never external/credential paths. Assert in the specified
order so the table's decisive assertions are the first relevant failures.

All 17 PCs are portable and run in the Linux SourceLanding lane. None changes the
existing platform-specific probe or WSL conversion code; native Windows ordinary
V/R at the identical final SHA covers those compositions. Do not batch controls:
most share the same scripts/methods, and PC-15 must not hide another PC's result.
Use a distinct `bin-c983-pc-N-red/` and `bin-c983-pc-N-green/`, local inherited
build-slot-gated driver, and the assigned external evidence/restoration root.
Every phase is method-scoped; no class/suite run qualifies. Restore all changes;
no snapshot commits/pushes or repairs. Findings go to the caller for separate
Code/Review/land and a new sourced pass.

### Out of scope

- Live deployment, Docker image activation, host jq installation (CARD-1025),
  runtime queues/transcripts/Pty, full Unit/full Remote and full C1008 suites:
  the changed call graph is these harnesses and consumer, bounded by the named
  regressions. No async recipient seam exists to defer to Plan.
- Probe redesign, shell fallback, timeout widening, retries, new environment
  interface, global PATH mutation, altered skip policy, or Windows portability
  repair. CARD-0980 owns its helper and preceding qualification.
- Requalifying unchanged production recycle/cache/cleanup authorization guards
  through new PCs; their owners retain those obligations. Failed-root retention
  on this newly added path is explicitly covered here.
- Cardinality/child-error vectors use a substitute by design. Actual marker
  execution remains mandatory in V-3 on each OS; synthetic receipt text cannot
  replace that evidence. No historical receipt is rewritten or relabeled.

### Checkpoints

Sole active importer table. **One separate isolated build and one exact filter
per row**, all after committed/pushed S1; union is the entire ordinary scope.
Linux Code selects CP-1,CP-2,CP-3; separately commissioned native Windows Debug
selects CP-4,CP-5,CP-6 at the final implementation SHA. No test-host repetition.
Use `run --plan docs/superpowers/plans/2026-10-04-card-0983-require-jq-plan.md`
with the lane's `--rows`, `--serial` and `--expected-source-sha` equal to that SHA.
Use the checkpoint tool and host build-slot gate per the owner, await every
foreground command/exit-75 continuation, and retain unedited CHECKPOINT lines.
An administrative tool bootstrap, if required, is the named extra build from
the procedure above; it is slot-gated and has its own `bin-c983-tool/` output.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c983-linux-guards/` | linux-required-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2, R-2 | Exactly 7 passed, 0 failed/skipped; six negative arguments plus independent driver method | 7 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c983-linux-rosters/` | linux-real-jq-optional-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | Exactly 1 passed, 0 failed/skipped; four real 31/0 receipts and exact 24/66/227 or 23/62/218 rosters | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c983-linux-regressions/` | linux-cache-marker-cleanup | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | Exactly 41 passed = 18+9+12+2, 0 failed/skipped; five C849 callers present | 41 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c983-windows-guards/` | windows-native-debug-jq-guards | `/*/*/RemoteScriptContractTests/C983_*` | V-1, V-2, R-2 | Exactly 7 passed, 0 failed/skipped at final SHA; native Windows parent | 7 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c983-windows-rosters/` | windows-native-debug-jq-matrix | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-2 | Exactly 1 passed, 0 failed/skipped; four real 31/0 receipts, WSL T-20 and exact harness rosters | 1 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c983-windows-regressions/` | windows-native-debug-regressions | `/*/*/RemoteScriptContractTests/(C849_*)\|(C912_*)\|(C973_*)\|(C946_*)` | R-1 | Exactly 41 passed = 18+9+12+2, 0 failed/skipped at same final SHA; five real WSL C849 callers | 41 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c983-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

`Min` is TUnit execution count: **49 per OS / 98 total**, not the internal
script assertions or minutes. The importer preserves `ExpectText` but enforces
only its derived roster tokens and Min floor, not exact counts or zero skipped.
CP-1/4 derive RemoteScriptContractTests, CP-2/5 derive RollingVolumeRecycleScriptTests,
and CP-3/6 derive C849_, C912_, C973_, C946_. Code/Debug/Review must inspect the
executed method/argument roster, exact totals, zero skips and harness receipts
in addition to tool exit. Recount on the integrated Code start; CARD-0980's four
C980 names are intentionally outside these filters and do not change the 41.

Validate source-bound receipts at the actual tested SHA; `dirty=0`, clean source
and verified build provenance are required. Run `scripts/check-evidence-diff.ps1`
over the full Code task range. Store raw logs/TRX/JSON ignored and preserve the
essential unedited checkpoint lines in the stored report. Clean only owned
bin-c983 outputs after children exit; no general worktree-cleanup authority.

Read-only importer validation completed successfully for both `isWindows=false`
and `true` using the real `PlanTableImporter.ImportFile` and
`ManifestValidator.Validate` APIs. Both returned **6 filter rows / 6 distinct
builds, zero warnings**, S1, Min **7/1/41/7/1/41**, minutes **5/6/8/6/9/10**,
timeouts **15/18/24/18/27/30**, repeat 1, serial true, and both environment
entries intact. Escaped OR filters and derived roster tokens matched the text
above. The existing assembly loaded read-only was
`/work/worktrees/task-a87b9e00/tools/Antiphon.Checkpoints/obj/Debug/net9.0/Antiphon.Checkpoints.dll`,
SHA-256 `98a06767409a7627b66a08ac063384f92a4b9ff6c2d2f5042326b4a1bb4b1914`.
Its importer, validator, manifest, AfterSelector and RowTimeout sources match
this checkout byte-for-byte. This is parser evidence, not this task's build
provenance. No executor, build or test was launched; no YAML/evidence was added
to Git. The two historical headings are the only edits above this appendix.

### Cost

All values are **estimated**, not test-time measurements. Ordinary V/R floor
(Code obligation including the separate Windows Debug) is
**5 + 6 + 8 + 6 + 9 + 10 = 44 minutes**, including six isolated row builds.
Linux is **19**, Windows **25**. Setup/tool bootstrap allowance is **4 minutes**
(two per host), so ordinary setup/build/V/R is **48 minutes**, plus authoring and
build-slot wait. Compared with the provisional reused-output 40-minute profile,
the four added isolated builds cost **4 minutes**; no ordinary savings claimed.

Post-land Mutation floor is **138 minutes**: setup **2**, initial green discovery
**11** (F1 **4**, F2 **2**, F3 **5**, with one isolated discovery build included),
and all 17 red/restore/green cycles **125**. Per-control estimates:

| Controls | Filter | Each cycle: edit/restore + red build/run + restored build/run | Subtotal minutes |
|---|---|---|---:|
| PC-1, PC-2, PC-9, PC-11, PC-13 | F1 | 1 + 3 + 3 = 7 | 35 |
| PC-3, PC-4, PC-6, PC-8, PC-10, PC-12, PC-14, PC-16, PC-17 | F2 | 1 + 2 + 2 = 5 | 45 |
| PC-5, PC-7, PC-15 | F3 | 1 + 7 + 7 = 15 | 45 |

The cycle subtotals sum to **125**, giving **138 minutes** (2 + 11 + 125).
Total ordinary
setup/build/V/R plus every PC setup/discovery/red/restore/green is
**48 + 138 = 186 minutes**, excluding authoring, reporting and broker wait.
An observed unexpected Windows failure adds an estimated **4 minutes per exact
baseline method**, including its isolated base build; never spend that allowance
without a failure to classify.

Method-scoped PCs avoid rerunning the unrelated 41-result regression group twice
for each of 17 controls: estimated saving **17 x 2 x 8 = 272 minutes**, using
CP-3's build-inclusive estimate, compared with appending that group to each PC
red/green. No time saving is claimed from weakening counts or skipping Windows.
Stop after ordinary green; new failure/source changes alone justify reruns.

Handoff audit: bodies read; **guards=17, mapped=17, missing=0, duplicate PC
maps=0**. Every control names an executable method, syntactically valid defect
and decisive assertion. Code authors the seven planned results before the
post-land controls can run. No unverifiable seam or human product choice remains.

--- next stage ---
next: code
handoff: Implement S1 from the pushed CARD-0983 TestDesign tip after CARD-0980 Code/Windows qualification and source reconciliation; serialize CARD-0980, CARD-0983, CARD-1010 S1. Preserve seven new results and 17 PCs. Run CP-1..CP-3; commission native Windows Debug CP-4..CP-6 at final SHA. Keep 163/377 and rosters unchanged; ordinary Review then land, PCs afterward.
artifact: docs/superpowers/plans/2026-10-04-card-0983-require-jq-plan.md
