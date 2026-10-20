# CARD-1282: keep the setup runner profile picker available

Date: 2026-10-20. Target: **master only**.
Inspected base and freshly fetched `origin/master`:
`3ab9e9d0d531d15a610e5497a72f1d85988610d8`.
The defect still exists at this SHA; this is source verification, not a runtime
test result. This Plan changes documentation only and runs no builds or tests.

Read CARD-1282 through `scripts/card.ps1 get CARD-1282 -Board Antiphon`.
Owners: [project conventions](../../project-context.md),
[testing/build](../../testing-and-build.md),
[orchestration](../../orchestration-loop.md), and
[HTTP operations](../../ops-http.md).
Test design remains a separate stage: the brief does not fold it into Plan.

## Ground truth

Paths below are relative to `client/src/`; line references are at the base above.

| Card assumption | What the code does | Consequence |
|---|---|---|
| An unread models request removes the Runner profile picker. | `features/settings/ProjectSetupModal.tsx:320-329` returns `PendingRead` for `modelsUnknown` instead of mounting `AgentTuiSelection`. | Confirmed on current master. Move this replacement inside the selector's Exact model position. |
| Pending and failed reads both cause the trap. | `ProjectSetupModal.tsx:49-53,99-101` derives the label from pending state or error, including errors with cached data. `routingSettingsModel.ts:326` distinguishes paused from other pending reads. | Reuse the same label and classification for loading, paused, failed, and stale failed reads. |
| Closing the modal does not clear its selected profile. | `ProjectSetupModal.tsx:99` owns the state outside Mantine's `Modal`; there is no close/reset effect in this component. | Recovery must work in the mounted modal without requiring close/reopen. |
| Create is already protected. | `ProjectSetupModal.tsx:128-129` includes models/profiles unread state in `createBlocked`; `submit` returns before mutation when blocked. | Preserve both the disabled button and submit guard. |
| Profile choice and exact model are currently one component. | `features/agents/AgentTuiSelection.tsx:62-89` renders both controls. Profile `onChange` already calls `onModelChange(null)`. | Add a presentation-only replacement for the model field; retain the profile handler and model reset. |
| Choosing another profile can recover independently. | `api/agentTui.ts:195-201` keys models by profile ID and enables the query only for a nonempty ID. | A successful B read must suffice while A remains failed. No retry-policy or API change is needed. |
| Existing tests prove recovery by switching profiles. | `ProjectSetupModal.test.tsx:421` covers four unread model states, the absent Exact model field, and disabled Create, but never switches profiles. | Add one interaction witness; retain existing boundary tests. |
| Shared-selector changes affect only setup. | `AgentCreateModal.tsx:277` and `AgentSettingsModal.tsx:272` also use `AgentTuiSelection`. Its existing test file has two capability/tier cases. | Keep the new prop optional with the existing rendering as its default; include those two tests in the narrow green run. |

## Decisions

- **D-1 — Replace only the model field.** Add
  `modelReadFallback?: ReactNode` to `AgentTuiSelection`. Render
  `modelReadFallback ?? <existing Exact model Select>` at the current model-field
  position. In `ProjectSetupModal`, remove the outer `modelsUnknown` branch and
  pass `modelsUnknown ? <PendingRead label={modelsUnknown} /> : null` through that
  prop. Keep the profile-read and empty-profile branches. The parent continues
  to own unread-state policy; the shared component only owns layout.
  Reject duplicating the profile picker in setup (two handlers/options to maintain)
  and importing setup-specific read policy into the shared selector (changes other
  consumers and crosses feature responsibilities).
- **D-2 — Preserve the existing guards and selection semantics.** Keep
  `createBlocked`, the early return in `submit`, model-query keys, exact-model
  capability checks, labels, and the profile handler's model reset. Null profile
  still means no selected-profile models read. Reject merely deleting the unread
  branch: that would expose an apparently usable Exact model field during failure.
  Reject resetting the wizard or automatically choosing a different profile:
  recovery should preserve the user's draft and require their profile choice.
- **D-3 — One red invocation, one narrow green invocation.** Add exactly one new
  Vitest witness in the existing modal file. Run it alone before the production
  change, then run the two named test files together once after the fix. Existing
  tests cover the remaining read-state and capability guards. No full suite,
  backend build, browser run, new test infrastructure, or automatic repeat is
  commissioned.
- **D-4 — Portable client lane; resolve placement at dispatch.** Read
  `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-20.
  Defaults revision was 2, with no kind overrides or unresolved references;
  the configured default was available, dispatch eligible, and accepting work.
  Re-read these routes at dispatch. Omit `-Runner` and `-Platform`: jsdom/Vitest
  needs no particular host or OS. Checkpoints use the **portable client Vitest
  lane**, with MSW and no live project writes.
- **D-5 — Publication and stage scope.** Land to master only. This introduces no
  session waiting for input and changes no session lifecycle. TestDesign must add
  `## Verification design` and finalize the bounded checkpoint roster below
  before Code. No product decision is outstanding.

## Slices

**S1 — Commit the red witness.** Change only
`client/src/features/settings/ProjectSetupModal.test.tsx`.
Add `C1282 switches runner profile after a models read fails`, using
`seedRepair`, `firstAgent`, the real selector, MSW, and the existing query client
with retries disabled. Extend fixtures locally to this test:

1. Supply enabled profiles A and B in both the setup catalog and profile response.
   Give A one selectable exact model and B a successful models response.
2. Reach First agent, select A and its exact model, then make A's models endpoint
   return 503 and await an explicit refetch of A's exact query key. Confirm query
   status `error` with its cached data retained and the existing failed-read label.
3. Assert Runner profile remains present and enabled while Exact model is absent.
   Visit the final step, assert Create disabled and no POST, then return to First
   agent.
4. Choose B through the real dropdown while A's handler still returns 503.
   Await B's successful query; assert the failed-read label is gone, Exact model
   is back, and the old exact-model selection is cleared. Confirm A remains in
   error so recovery cannot be attributed to a successful A refetch.
5. Reach the final step, assert Create enabled, and submit once. Assert the payload
   uses B with `modelId: null`, and that exactly one setup POST occurred.

Commit and push S1 before its red run. On the inspected production code, the
first new assertion about Runner profile must fail because that control is absent.
A fixture error, timeout before the established failed-read state, or zero selected
tests is not the required red witness. Use query completion/state assertions, not
sleeps or broader timeouts.

**S2 — Move the rendering boundary.** Change only
`client/src/features/settings/ProjectSetupModal.tsx` and
`client/src/features/agents/AgentTuiSelection.tsx` as D-1/D-2 specify.
Commit and push, then run the narrow green checkpoint. Keep existing tests intact.
There are no required API, query-hook, other modal, or settings-model edits.

## Bounded execution handoff

These are the proposed checkpoint rows for TestDesign, not executed evidence.
Both use the **portable client Vitest lane** and `scripts/test-client.ps1` with
literal file filters. Preserve a single host build-slot owner: the direct command
forms below use `build-slot.ps1`; if the checkpoint tool owns the slot, it invokes
the filtered `test-client.ps1` command without nesting a second slot acquisition.
TestDesign must bind this to the checkpoint tool's client lane and committed source.
No separate application build is required for these client tests.

## Verification design

Code task `e24e5109-2f8a-4696-9240-19c8f506e389` explicitly skips the separate
TestDesign stage and commissions the two proposed client selections, with no
whole-suite or backend run. The source census confirms 27 existing modal cases
and two selector cases; S1 adds one modal case. No browser/manual acceptance is
required. This is the complete bounded Final ordinary scope for this brief.

- **V-1:** the new interaction witness establishes cached A models, makes A fail,
  switches to B through the real selector while A remains failed, checks the
  exact-model reset and preserved project draft, and submits exactly once with B.
  CP-1 must fail at the missing Runner profile after the failed read is established;
  CP-2 must pass the same witness.
- **R-1:** CP-2 runs every modal case, including all four unread-state variants,
  disabled Create/no submission, preset recovery, and empty-preset behavior.
- **R-2:** CP-2 runs both shared-selector tier/capability cases with the new
  optional prop omitted.
- **PC inventory:** no PC-n/variants are defined by this plan. Post-land
  SourceLanding Mutation still owns deliberate controls and missing-control
  discovery; ordinary red/green does not discharge that obligation.

The checkpoint tool owns the build slot; commands below therefore omit the
nested `build-slot.ps1`. Its generic command lane records source/exit evidence
(`buildSource=notApplicable`); fresh Vitest JSON provides the nonzero test counts
and exact title roster instead of TUnit TRX. Run each row with committed HEAD as
`--expected-source-sha`. Building the checkpoint tool itself through
`build-slot.ps1` is infrastructure bootstrap, not an application build.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---:|
| CP-1 | S1 | n/a | client-vitest-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ProjectSetupModal.test.tsx -t "C1282 switches runner profile after a models read fails" -JsonResultPath ../logs/c1282-cp1-vitest.json` | V-1 red | Exactly 1 selected test fails at missing Runner profile; exit 1; other titles excluded by filter | n/a | 2 |
| CP-2 | S2 | n/a | client-vitest-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ProjectSetupModal.test.tsx src/features/agents/AgentTuiSelection.test.tsx -JsonResultPath ../logs/c1282-cp2-vitest.json` | V-1, R-1, R-2 | 30 passed, 0 failed/skipped at this base plus S1 | n/a | 3 |

The green source census is 27 existing modal cases + 1 new witness + 2 selector
cases; TestDesign confirms the roster before Code. Estimates total five minutes
for execution, excluding dependency installation and slot waits. Allow roughly
15 minutes for the small implementation and ten for setup, publication, and
reporting; no larger test run is authorized to fill that budget. Record each
tested full SHA, actual counts, the expected red assertion, and the final client
exit trailer. Preserve generated evidence in ignored paths. A slot timeout is
not run, never a reason to run unleased.
