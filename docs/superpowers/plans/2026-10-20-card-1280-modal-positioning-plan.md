# CARD-1280: remove agent-modal positioning cost from jsdom tests

Date: 2026-10-20. Stage: Plan; next: TestDesign (separate dispatch).
Target: **master only**. Measured source: `9177159a0711948ab8c0f81f9ab03db2056204fc`,
equal to fetched `origin/master` and the clean task HEAD before the run.

## Current-master measurement

One invocation, one named file, through the host build-slot gate and the required
client wrapper. The slot was granted immediately and released after 38 seconds,
including the fresh worktree's dependency installation (22 seconds).

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1280-master-baseline -SlotWaitMinutes 5 -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentCreateModal.test.tsx --pool=forks --no-file-parallelism --execArgv=--cpu-prof --execArgv=--cpu-prof-dir="$PROFILE_DIR" -JsonResultPath "$PROFILE_DIR/baseline.json"
```

`PROFILE_DIR` was the absolute path to this checkout's ignored
`logs/card-1280-profile` directory. Raw verdict: **1 file passed, 3 tests passed,
0 failed/skipped; CLIENT TESTS EXIT CODE: 0**. Vitest duration: **12.80 s**;
test bodies: **8.62 s**. Case durations:

| Existing case in `AgentCreateModal presets` | Duration |
|---|---:|
| defaults to Standing orchestrator and submits preset with filled bundles | 3,865 ms |
| Worker chip submits the worker preset and empty bundles | 2,349 ms |
| creates an explicitly Phone agent | 2,387 ms |

This is sufficient continuing cost to pursue the small fix, rather than close
the card as negligible. It does **not** establish a new CPU attribution or prove
the loaded-host timeout: the requested CPU profiling flags produced no profile.
No second measurement or stress run was made. Profiling overhead is unquantified.
The 1,564 style reads / 1,403 Floating UI calls remain **historical** observations
from [CARD-1246 evidence](../evidence/2026-10-15-card-1246-code.md), not fresh counts.
Raw JSON and console output remain ignored; the results above are the durable record.

## Ground truth

| Card assumption | What current code/evidence actually does | Consequence |
|---|---|---|
| Agent modal tests remain expensive | The three create tests take 8.62 s on current master; they pass without a stressor. | Proceed, without claiming a reproduced timeout. |
| Transitions need disabling | `client/src/test/utils.ts` already passes `env: 'test'` to `MantineProvider`, disabling transition waits, portals, and the detached-reference visibility decision. | Keep that provider configuration. |
| The provider avoids positioning | Installed Mantine `components/Popover/use-popover.mjs` still builds offset/hide/flip/shift/arrow/size middleware, calls `useFloating`, and calls `autoUpdate` directly as well as through `whileElementsMounted`. | Disabling only observers or only transitions is insufficient. |
| A small test seam exists | `@floating-ui/react-dom` imports `computePosition` from `@floating-ui/dom`; React reexports `autoUpdate` from the same chain. Installed versions: Mantine 8.3.17, Floating UI React 0.27.19 / DOM 1.7.6, Vitest 4.1.0, jsdom 29.0.0. | Substitute the DOM positioning boundary while retaining the real React hook. |
| Layout-sensitive tests must remain meaningful | Search of client test bodies found no Floating UI placement/flip/shift assertions. `SessionTerminal.mouse.test.tsx` supplies xterm geometry and checks measured cells/canvas; `normal drag copies selection` depends on it. Browser tests `PipelineGlanceE2ETests.C557_combined_glance_on_phone` and `DispatchConcurrencySettingsE2ETests.Project_override_clear_and_live_receipts_preserve_inheritance` inspect real bounding boxes. | Preserve native style/rect APIs. Browser tests do not load the Vitest setup. This is not new coverage of modal placement. |
| A particular host is required | Both required GETs, `/api/runner-defaults` and `/api/session-runners`, succeeded on 2026-10-20. Defaults revision 2 has a global preference and no per-kind overrides; eligible accepting Linux and Windows runners were advertised. | Lane is portable client/jsdom. Resolve live defaults at dispatch; omit `-Runner` and `-Platform`. |

## Decisions

- **D-1 — Test-only partial DOM mock.** Add one hoisted
  `vi.mock('@floating-ui/dom', async importOriginal => ...)` in
  `client/src/test/setup.ts`. Preserve all actual exports except
  `computePosition` and `autoUpdate`. `computePosition` resolves an object with
  `x: 0`, `y: 0`, the requested placement/strategy (defaults `bottom`/`absolute`),
  and empty `middlewareData`. `autoUpdate` invokes its update callback once and
  returns a callable no-op cleanup. Do not register observers or run middleware.
  This covers Mantine's direct and hook-driven update paths while preserving real
  refs, context, open/close state, focus, keyboard handling, and submission.
- **D-2 — Shared setup, bounded qualification.** Choose the existing setup over
  repeating imports throughout agent test files: it is the smallest change and
  gives all jsdom consumers the same layout policy. A test that needs Floating UI
  positioning must use hoisted `vi.unmock('@floating-ui/dom')` before importing
  its component; keep any such test in a separate file. The narrow green below
  proves the named modal flows only, not every consumer of the shared setup.
- **D-3 — Preserve the real style engine.** Reject replacing/caching
  `window.getComputedStyle`, mocking entire Mantine controls or `useFloating`,
  and changing application components. Those approaches can conceal
  accessibility or interaction defects or require maintaining a large hook fake.
  Reject middleware tuning alone: hide/size and DOM measurements still run.
- **D-4 — Count work, not elapsed milliseconds.** Use one forwarding style-read
  counter with caller attribution in a real modal flow. Reject a wall-time
  threshold because host scheduling and cold imports vary. Retain the global
  20-second hang detector; do not change timeout, polling, skip, or fixture rules.
- **D-5 — Small verification contract.** The dispatch supersedes the card's
  older loaded-host-run suggestion: one red witness and one narrow green,
  no stressor or whole suite. No production build, deployment, rc cherry-pick,
  dependency upgrade, or session lifecycle change. Verification design remains
  a separate stage; no product decision is outstanding.

## Slices

### S1 — Commit the cost witness before the fix

File: `client/src/features/agents/AgentCreateModal.test.tsx`.
Extend the existing `defaults to Standing orchestrator and submits preset with
filled bundles` case, keeping its name and existing submitted-preset assertions.

Install a forwarding spy on `window.getComputedStyle` before rendering. Count
calls whose normalized caller stack contains a frame from `@floating-ui/dom/`
or `@floating-ui/core/`; return the real result on every call. Keep separate
total-call and positioning-call counters. During the flow open the real
Assignment policy select, choose **Manual confirm**, and assert the selected
value and submitted `assignmentPolicy: 'ManualConfirm'`. Finish the existing
create/settle flow, explicitly unmount and flush its pending React work, then
assert total calls are positive and **positioning calls equal zero**. Restore
the spy in `finally`, including on assertion failure.

The missing optimization must make this exact case fail on the positioning
count, after reaching the dropdown and submitted-body assertions. A missing
module, hook error, zero tests, timeout, or a stack matcher that never sees the
old path is not the intended red. Record the actual old count; it has not been
measured in this Plan dispatch. Commit/push S1, run the one red selection below,
and retain the expected failure without labelling it green.

### S2 — Substitute positioning and run the narrow green

File: `client/src/test/setup.ts`; retain the S1 witness.
Implement D-1 with signatures checked against the installed DOM package.
No fabricated React hook result, timers, extra package, or global style spy.
The actual dependency's native ESM imports must reach the mock: the S1 counter
is the reachability check. Vitest 4.1's native loader supports dependency mocks
on the observed Node 22.23.3; do not add broad dependency inlining pre-emptively.
If interception fails, report the concrete import boundary to TestDesign rather
than accepting a green assertion on a mock's own call count.

Commit/push S2 and run the single green selection below. The unchanged
`AgentSessionBackend.test.tsx` case
`does not disable or remap herdr when AlwaysOn is toggled on` supplies one
settings-modal interaction and PATCH-body check alongside the three create
tests. Report counts, the witness count, and per-case durations. Do not present
a speedup percentage against the baseline: the witness adds dropdown interaction
and stack capture. Successful proof is removal of the counted positioning work
with the real interactions still passing.

## TestDesign handoff

Code dispatch `865422b6-ccd8-43c8-a0c6-d78586cc68d9` supersedes the separate
TestDesign dispatch below: implement the existing witness and four-test selection
directly. The verification design below binds that selection without expanding it.

Append the formal `## Verification design`; this brief did not fold that stage
into Plan. Bind the one witness and narrow green below without expanding to a
suite. The guard is the real modal's zero positioning-style-read assertion,
not a spy on `computePosition`. Any later positive control should restore the
actual positioning exports and target this one case; do not add a PC campaign
to ordinary Code. Explicitly preserve the limitation that clipping, target-width
sizing, arrow placement, and viewport collision geometry are intentionally
unproved by the mock.

### Checkpoints

Closed selection confirmed by the Code brief. **Lane for both rows:
portable client/jsdom, serial.** Each row uses the checkpoint tool's slot-owning
execution, once per committed slice, with exact source SHA. No application build
is needed. Run CP-1 alone at S1: its expected exit 1 is a recorded witness, not a
successful checkpoint. Run CP-2 alone at S2 as the acceptance gate. If the tool
must be bootstrapped, name that slot-gated isolated tool build separately and
clean its owned alternate output before settlement.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | n/a | modal-positioning-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentCreateModal.test.tsx -t "defaults to Standing orchestrator and submits preset with filled bundles" --no-file-parallelism -JsonResultPath ../logs/card-1280/red.json` | V-1 red witness | 1 selected case fails only at positioning count; 2 cases filtered out; exit 1 expected | n/a | 1 | true |
| CP-2 | S2 | n/a | modal-positioning-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentCreateModal.test.tsx src/features/agents/AgentSessionBackend.test.tsx --no-file-parallelism -JsonResultPath ../logs/card-1280/green.json` | V-1, R-1, R-2 | 2 files, 4 tests passed, 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 1 | true |

Estimated Code test floor: **2 minutes** (1 expected-red + 1 acceptance-green),
plus dependency/tool setup and slot wait if needed. Estimated implementation:
15 minutes. A later single-case PC red/restore/green costs approximately 2
minutes, separately commissioned. Plan measurement executed exactly three
cases once; no implementation, red witness, fix qualification, or full suite
was run in this dispatch.

## Verification design

The Code brief explicitly limits ordinary qualification to CP-1 once before the
mock and CP-2 once after it. No whole Unit lane, suite, stressor, production build
or browser run is part of this card's commissioned scope. The checkpoint command
rows report counts as n/a; inspect the fresh Vitest JSON assertions for their
exact names, outcomes and durations instead of claiming a TRX was produced.

| ID | Required proof |
|---|---|
| V-1 | `AgentCreateModal presets` / `defaults to Standing orchestrator and submits preset with filled bundles`: real Assignment policy selection and submitted ManualConfirm body pass; forwarding style spy observes positive total calls and zero Floating UI DOM/core calls after unmount/settling. CP-1 must first fail on a positive positioning count; CP-2 must pass the same assertion. |
| V-2 | Manual source-reachability inspection: the positioning mock exists only in `client/src/test/setup.ts`, referenced by Vitest `test.setupFiles`; no production entry/import reaches it. Retain the inspected import-graph evidence without running a production build. |
| R-1 | Both remaining create cases in CP-2: `Worker chip submits the worker preset and empty bundles`; `creates an explicitly Phone agent`. Preserve their existing submission assertions. |
| R-2 | Full one-case `AgentSettingsModal session backend` file in CP-2: `does not disable or remap herdr when AlwaysOn is toggled on`; preserve real control and PATCH-body assertions. |

No geometry is proved: clipping, target-width sizing, arrow placement and viewport
collision handling remain outside this jsdom policy. Shared-setup consumers
outside the four cases are not qualified by this narrow run. Real style/rect APIs,
controls, accessibility reads and the 20-second hang detector remain intact.

PC-1 (pending SourceLanding Mutation, not ordinary Code): restore the actual
computePosition and autoUpdate exports and run only the exact V-1 case with the
CP-1 file/name filter; require failure at its positioning-count assertion, restore
the mock, and require the same case green. No variants are currently specified;
Mutation owns missing-control discovery and the deliberate red/restore/green cycle.
