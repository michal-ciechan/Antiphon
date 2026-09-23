# CARD-0610: repair the five identified CI failures

Plan and folded verification design, 2026-09-23. Ready for Code.

Base: `b9974f1d1d2bf854242f74a027cb73c7760cb89a`, including the
[investigation](../../investigations/2026-09-23-card-0610-github-actions-ci-red-on-master.md).
The brief explicitly requests checkpoints and `next: code`; verification design is included here.
No implementation or test execution was performed by this Plan dispatch.

The requested continuation branch, `feat/card-task-ea99330a`, is still checked out in its
investigation worktree. This plan is authored on `feat/card-task-208cc5d2` from the exact requested
base and published by a normal fast-forward push to the continuation branch as well as the task
branch. Do not force a branch into two worktrees or alter the investigation worktree.

## Scope and outcome

Make exactly these five repairs: destructure the two sortable hook results; extract the reorder
helper; replace the routing form's seeding effect; gate the installed-Herdr acceptance fixture;
and make the existing Codex tailer test use its hermetic launcher fixture. Preserve the lint rules,
UI semantics, production launch policy and six live disposal assertions.

This clears the identified failures. It does not establish that every previously blocked CI step
is green: the investigation found client build/tests and Messaging checks hidden behind lint.
Do not fold newly exposed unrelated failures into this card.

## Ground truth

| Card / brief assumption | What the base actually does | Consequence |
|---|---|---|
| Sortable rows have `react-hooks/refs` errors. | `CardRow.tsx:38` and `BacklogRow.tsx:42` retain the whole `useSortable` result, then use its member expressions for callback refs and spreads. Neither reads a ref's `.current`. | Bind the six used values directly; preserve options, refs, spreads and styles. Investigation's minimal destructuring probes already pass the rule. |
| `placementFromReorder` is exported alongside a component. | `SortableCardList.tsx:25` exports it; only that component and `SortableCardList.test.tsx` consume it. It uses `arrayMove` and the `CardDto` type. | Move the function unchanged with those imports to a sibling `.ts` module; update both consumers and remove the component-module export. |
| Specialist routing needs a render-time seeding adjustment. | The effect depends on both `query.data` and `dirty`; it seeds pairs, enabled and token only while clean. Save and Reload clear dirty; the hooks can update or structurally share the cached object. | Track both previous data identity and previous dirty state. A data-only marker would miss Reload of unchanged data. |
| The brief mentions five CARD-0076 precedents. | Commit `dbb5dc94e26bc14bc5165e4e57dc780df40e14ec` names five **helper extractions**, plus about fourteen effect rewrites. Both patterns exist in current code; examples are listed below. | Use the extraction precedents for fix 2 and the render-adjustment precedents for fix 3. |
| Six live tests require a Herdr installation unavailable on hosted Windows. | Five methods expand to six cases. `LiveFixture.StartAsync` writes its temp config, then asserts an installed per-user `herdr.exe`, without an opt-in check. It creates its own isolated Herdr server. | Skip before any filesystem/process setup unless explicitly opted in and the binary exists. Do not require a default running daemon or Grok. |
| The Codex failure can be repaired without skipping coverage. | `HerdrRunnerSessionTests.Codex_request_starts_CodexTranscriptTailer` uses `FakeHerdrServer` and a temp `CODEX_HOME`, but passes literal `codex.cmd`. Windows launch policy now resolves it before contacting Herdr. | Pass `CodexNpmLayout.ShimPath`; preserve the real normalization path and existing Running / Codex sidecar assertions. |
| An empty request PATH would exclude the host installation. | `CodexWindowsLaunchPolicy.PathEntries` falls back to the process PATH for an empty or whitespace request PATH. | Use a nonempty, owned empty directory in the request PATH; do not mutate process PATH. |
| CI configuration needs changing. | `ci.yml` already lints/builds/tests the client and runs the native projects. It installs neither Herdr nor Codex, appropriately for these hermetic/default lanes. | No workflow, package, lint configuration or release/landing-gate changes. |

The investigation's text says both nine refs errors and ten total errors while also listing two
other lint errors. Treat its sites and reproduced rule mechanisms as evidence, not that inconsistent
sum. Code records actual checkpoint diagnostics/counts; acceptance is zero problems.

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Destructure `setNodeRef`, `setActivatorNodeRef`, `listeners`, `attributes`, `transform`, `transition` at each `useSortable` call. This removes the inference trigger without changing behavior. | Suppressions, plugin/version changes, property renaming, or assigning ref members to aliases after retaining the whole object. |
| D-2 | New helper file is `client/src/features/board/placementFromReorder.ts`. Move the body verbatim and import it directly in the component and existing test. | A re-export from `SortableCardList.tsx` retains the mixed-export problem; duplicating the helper or moving it into a shared feature-independent directory adds scope. |
| D-3 | Keep current form state and event handlers. Use a nullable previous-input state marker `{ data, dirty }`, initially null; update the marker whenever either input changes, and seed all three fields only when data exists and dirty is false. Place this conditional after hooks and before early returns. | Unconditional render setters loop; a data-only marker misses a dirty-to-clean transition; remounting on each response or copying fresh tokens while dirty changes conflict behavior; replacing the effect with a layout effect only hides the rule violation. |
| D-4 | Gate `LiveFixture.StartAsync` first, using `HerdrLiveSession.EnvFlag` (`ANTIPHON_HEADED_TESTS`) equal to `"1"`, followed by `File.Exists(executable)`. Throw `TUnit.Core.Exceptions.SkipTestException` with the missing prerequisite. Retain `ParallelLimiter<ProcessSpawnLimit>` and add `[NotInParallel("Headed")]` per the test owner. | Calling `HerdrLiveSession.SkipIfNotEligibleAsync` would unnecessarily require Grok and the operator's default daemon; a CI-only gate would still launch unexpectedly during ordinary local suites. |
| D-5 | Put the two checks in a small test-only, stateless `LiveFixture.EnsureEligible(string? optIn, string executable)` method, called by StartAsync with the real values before setup. Test it with owned temp paths. Startup and assertion failures after eligibility remain failures. | Catch-all skip handling conceals disposal regressions; process-global environment mutation inside unit tests creates races; a new production abstraction is unnecessary. |
| D-6 | Instantiate `using var layout = new CodexNpmLayout()` within the existing Codex test, alive until runtime disposal. Pass its absolute ShimPath and a nonempty owned empty PATH directory in the request environment alongside the existing temp CODEX_HOME. | Skipping, installing the real CLI, using host credentials, disabling normalization or replacing the request with native `codex.exe` avoids the regression instead of fixing the fixture. |
| D-7 | Use targeted ordinary checks and the existing whole-client lint gate, with one client build and one isolated SessionRunner.Tests build. The new eligibility helper tests plus six explicitly checked skips prove the default lane without launching Herdr. | Re-running all native suites or commissioning a live disposal qualification duplicates unchanged CARD-0461 coverage. Changes to CARD-0612 lint enforcement or CARD-0599 release gates are explicitly excluded. |

The decisions implement the supplied brief; there is no pending product choice or caller default
requiring `next: decide`.

### Precedents read

- `WorkflowDetailPage.tsx:81`: compare previous derived mode, record it, then reset local state.
- `WorkflowEditor.tsx:24`: compare previous open/data inputs and protect unsaved edits.
- `AgentAddWorkModal.tsx:27`: composite previous-input key and conditional form population.
- `IgnorePathModal.tsx:38`: previous target key and several related setters inside the guard.
- `AgentTuiProfileModal.tsx:71`: previous open/profile inputs and form population during render.
- CARD-0076's extraction destinations: `transcriptModel.ts`, `filesReviewModel.ts`,
  `ignorePattern.ts`, `describeImpact.ts`, `terminalCopy.ts`. Its commit records verbatim moves
  into non-component modules; D-2 follows that separation.

## Implementation slices

Commit and push each slice. Finish S1-S3 before the client checkpoint group and S4-S5 before the
.NET checkpoint group, so each group builds once after all its edits. No source edits during runs.

### S1: sortable bindings

Files: `client/src/features/board/CardRow.tsx`,
`client/src/features/orchestrator/BacklogRow.tsx`.

Apply D-1. Keep `id`, disabled/archived gates, ref destinations, spread order, transform conversion,
transition and click/key propagation exactly as they are. Existing tests are
`CardRow.test.tsx`, `SortableCardList.test.tsx` (keyboard activation), and
`features/orchestrator/BacklogSection.test.tsx` (single/multiple-board handles and navigation).
No new tests are needed for this mechanical binding change. Closes at CP-1 and CP-4.

### S2: reorder helper extraction

Files: new `client/src/features/board/placementFromReorder.ts`,
`client/src/features/board/SortableCardList.tsx`, `SortableCardList.test.tsx`.

Move `arrayMove`, the type import needed by the helper, and the function unchanged. Retain any
imports still used by the component. Update the test import without moving or rewriting the tests.
The existing neighbor/order assertion and keyboard/placement tests cover this extraction.
Closes at CP-2 and CP-4.

### S3: routing form seed adjustment

Files: `client/src/features/agents/SpecialistRoutingPanel.tsx`,
`SpecialistRoutingPanel.test.tsx`.

Remove the effect and its import. Implement D-3; preserve candidate formatting, empty-candidates
fallback, `enabled ?? true`, nullable token, validation and all handlers. Record observed inputs
even when dirty; this keeps render adjustment bounded and makes a later dirty-to-clean transition
observable even for the same query object. The marker starts null so cached data seeds on mount.

Retain the two existing tests. Add four behavior tests using the existing MSW handlers and
`renderWithProviders` QueryClient (do not mock the seeding implementation):

1. `adopts a clean refresh including enabled state and revision`: refresh clean data to new
   candidates, enabled=false and revision-two; assert input/switch, then capture Save's full body
   using revision-two. This proves the token changed along with visible values.
2. `reloads unchanged cached data after discarding edits`: edit candidates and toggle enabled;
   Reload returns identical remote data; assert cached reference remains the same, original values
   return and a subsequent request uses the original token. This catches a data-only marker.
3. `adopts the successful save response before the next save`: respond to the first save with
   canonical candidates, changed enabled and revision-two, then serve the same data on GET;
   assert the form adopts them and the next save sends revision-two. No request ordering sleeps.
4. `seeds the primary pair and enabled default from cached data`: use `renderWithProviders(<></>)`,
   seed its returned QueryClient inside `act`, then rerender the panel under the same providers.
   Use candidates=[], enabled=null and token=null and serve the same value on GET; assert primary
   Kind/Level, enabled=true, Revalidate disabled, and a captured Save body with token=null. This
   explicitly exercises data already present on the panel's first render.

The existing stale-refresh/409/explicit-reload test remains the main dirty-revision assertion;
extend it to check the enabled value is retained across the dirty refresh too. Closes at CP-3/CP-4.

### S4: Herdr fixture eligibility

Files: `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardedLiveTests.cs`,
new `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalEligibilityTests.cs`.

Implement D-4/D-5. Resolve the same installed executable path before setup, call EnsureEligible,
then execute the existing fixture body unchanged. Remove the File.Exists assertion it replaces.
The opt-out path must precede Directory.CreateDirectory, config writes and Process.Start.
Do not alter the six disposal assertions, owned session/home isolation or exact-process teardown.

The new `[Category("Unit")]` class has three non-spawning tests:

- `Requires_explicit_opt_in`: with an owned existing placeholder file and then an absent path,
  check null, empty, `"0"`, and `"true"`; each throws SkipTestException naming the opt-in flag.
  Catch it with `Should.Throw<SkipTestException>` so the verifier itself **passes**, not skips.
- `Skips_when_opted_in_but_binary_is_missing`: `"1"` plus an absent owned path throws
  SkipTestException identifying the missing binary/path.
- `Accepts_opt_in_with_existing_binary`: `"1"` plus the owned placeholder file does not throw.
  This tests eligibility only; never execute the placeholder. Clean owned temp files in finally.

CP-5 also runs the actual six live cases with the flag cleared. Require exactly six skips for the
opt-in reason, not six passes, failures, undiscovered tests, or skips caused by unrelated setup.
Closes at CP-5.

### S5: hermetic Codex launcher

File: `tests/Antiphon.SessionRunner.Tests/HerdrRunnerSessionTests.cs`, only
`Codex_request_starts_CodexTranscriptTailer`.

Reuse the existing `CodexNpmLayout` without changing its implementation. Create an empty directory
under this test's log root for the **request** PATH, separate from layout.Root; neither PATH nor
cwd may contain a `codex.cmd`. Pass layout.ShimPath as Exe. Keep the fake named-pipe server,
Codex agent kind, request arguments, temp CODEX_HOME, transcript settings, Running assertion,
sidecar non-null/Format assertion and cleanup. Do not add a skip condition. The fake observes a
launch script but does not execute the fixture's dummy binaries. Closes at CP-6.

## Verification design

### Inspection

| Bodies / helpers inspected | Boundary and coverage |
|---|---|
| CardRow, BacklogRow, SortableCardList and their tests; BacklogSection handle/navigation tests | Callback refs/styles/handles and helper imports: V-1/V-2, R-1/R-2. jsdom's existing keyboard test only proves Space activates the sensor; it does not prove spatial drag movement. |
| SpecialistRoutingPanel and both existing tests; specialistRouting API hooks; test/utils.ts | Data/dirty transitions, cache structural sharing, save/409/reload and token coupling: V-3/R-3. |
| Full HerdrPaneDisposalGuardedLiveTests fixture and six cases; HerdrLiveSession eligibility; test project and checkpoint runner | Explicit opt-in before setup, missing binary, positive eligibility, expected skip roster: V-4/R-4. |
| Existing Codex method, CodexNpmLayout, FakeHerdrServer launch seam, CodexWindowsLaunchPolicy resolution and its Standard_npm_shim / Sibling_node tests | Real launch-policy resolution with fake Herdr and no host CLI: V-5/R-5. |
| lint.gate.test.ts, eslint.config.js, scripts/test-client.ps1, client/package.json, ci.yml | Actual pinned rules, whole-client zero-problem check and exact client build command: V-1/V-2/V-3/V-6/R-6. |

Required Code setup is the worktree's npm lockfile install (if dependencies are absent), existing
.NET restore and Windows for CP-5/CP-6. No PostgreSQL, live runner, Herdr install, real CLI login,
network model call, new environment secret or shared stack restart is required.

### Delivery inventory

No new or changed asynchronous agent outcome-delivery path exists. The routing UI still consumes
the same query cache and awaits the same mutation/refetch promises; tests drive the real hooks
through MSW and assert form values plus outgoing revision-bearing request bodies. They do not
claim server persistence or specialist/model execution. The fake Herdr test proves runner startup
and Codex sidecar creation; it does not claim actual model launch, transcript content delivery,
or live disposal safety. Those production paths are unchanged.

### Proves it works now

- **V-1:** Run ESLint under the repository config on CardRow and BacklogRow; zero errors/warnings,
  specifically no `react-hooks/refs`. Inspect the diff for the six direct bindings and no disables.
- **V-2:** Lint SortableCardList and placementFromReorder; zero problems, specifically no
  `react-refresh/only-export-components`. Existing helper test retains both neighbors and order.
- **V-3:** Lint SpecialistRoutingPanel; no `react-hooks/set-state-in-effect` or other problems.
  All six routing behavior tests pass, including dirty and unchanged-data reload transitions.
- **V-4:** Three eligibility tests pass without real processes; actual live class reports its six
  cases skipped at the flag guard with default environment. Positive helper case prevents a
  permanently skipping gate. Inspection proves only prerequisite checks can throw skips.
- **V-5:** On Windows, the exact Codex method passes with request PATH pointing only at the empty
  owned directory, using the absolute fixture shim; Running and Codex sidecar assertions execute.
- **V-6:** `npm.cmd --prefix client run build` succeeds and the existing lint gate reports zero
  problems across the complete client. The lint gate stays unchanged.

### Guards the regression

- **R-1:** Existing CardRow/BacklogSection tests preserve handle visibility, archived behavior and
  navigation; SortableCardList's Space assertion stays `aria-pressed=true`.
- **R-2:** Existing `names both neighbours after a one-step move down` pins helper output and its
  direct `.ts` import; existing placement request test still sends `placement: 'Top'`.
- **R-3:** The existing dirty-refresh test must send revision-one and edited candidates after a
  revision-two cache refresh, show the 409, and require Reload. Four new S3 tests pin the remaining
  clean/dirty/cache/save/default transitions with visible values and captured request bodies.
- **R-4:** The three S4 tests exercise every eligibility boundary, and CP-5 verifies that the real
  fixture calls the gate rather than leaving it unused. The result is three passes plus six skips.
- **R-5:** The repaired Codex test keeps its original decisive assertions and no skip; resolving a
  bare host `codex.cmd` again would fail because request PATH and cwd contain no launcher.
- **R-6:** Existing whole-client `eslint reports zero problems across the client` detects all three
  lint mechanisms returning. No new test that mirrors source syntax is needed.

### Guard inventory

| Guard | Changed invariant | Distinct control |
|---|---|---|
| G-1 | S3: `!dirty` protects edits and their concurrency token from a server refresh. | PC-1 |
| G-2 | S4: exact `optIn == "1"` is required before live test setup. | PC-2 |
| G-3 | S4: an absent installed binary yields an explicit prerequisite skip, not a launch attempt or assertion failure. | PC-3 |

Guards=3, mapped=3, missing=0, duplicate mappings=0. Reorder bindings/extraction introduce no
safety guard. S5 repairs test inputs and changes no launch-policy guard. Existing production
disposal/identity/ownership/kill guards are unmodified and are outside this card's PC inventory.

### Positive controls

Post-land Mutation executes these; Code only writes the tests and runs ordinary V/R. Do not
mutate live disposal behavior or launch a real provider to prove this card.

| PC | Compiling mutation | Exact test selection | Required red, then restored green | Estimated minutes |
|---|---|---|---|---|
| PC-1 | In the S3 guarded block, remove only the `!dirty` condition while retaining previous-input updates and the data guard. | `pwsh -File scripts/test-client.ps1 src/features/agents/SpecialistRoutingPanel.test.tsx -t '^Specialist routing retains the edited revision across AgentChanged refresh, rejects stale save and requires explicit reload$'` | Edited form/request assertion fails after refresh; restored selection runs one passing test. | 2 |
| PC-2 | Make only the opt-in rejection condition false in EnsureEligible. | `/*/*/HerdrPaneDisposalEligibilityTests/Requires_explicit_opt_in` | `Should.Throw<SkipTestException>` fails for an existing placeholder without opt-in; restore and one passes. No real server starts. | 2 |
| PC-3 | Make only the missing-file rejection condition false in EnsureEligible. | `/*/*/HerdrPaneDisposalEligibilityTests/Skips_when_opted_in_but_binary_is_missing` | `Should.Throw<SkipTestException>` fails for the absent path; restore and one passes. No real server starts. | 2 |

PC-2/PC-3 touch the same helper and must run separately. Use exact-method TUnit filters and a
fresh build after each mutation/restoration. Restore source timestamps as documented. A fixture
error, build failure, timeout, zero tests or skip is not the expected red. Mutation also performs
the required missing-control discovery against the landed SHA, keeping evidence externally.

### Out of scope

- CARD-0612 enforcement/ownership gap and CARD-0599 release gates; no changes to workflows,
  test-client.ps1, run-checkpoint.ps1, lint.gate.test.ts, lint rules or dependencies.
- Full native/full server/full client behavioral suites: the five repairs have bounded classes;
  CI/nightly retain broader coverage. Whole-client lint is deliberately included because this
  card repairs its standing red; it is not a new enforcement mechanism.
- Requalification of the six live pane-disposal scenarios. Their bodies, service and process
  cleanup remain unchanged. This card measures skip/default behavior and positive eligibility,
  not an opted-in native acceptance pass. Do not report six skips as native safety evidence.
- New UI behavior on changing agentId while dirty, new synchronization architecture, token
  conflict resolution policy, production Codex/Herdr changes or new asynchronous delivery paths.

### Execution and evidence

CP-1 through CP-3 commands run from `client/`; CP-4 through CP-6 run from the repo root. Install
client dependencies with `npm.cmd --prefix client ci` once if missing, preserving the lockfile.
CP-1 owns the client build (`npm.cmd --prefix client run build` from root); its dist and TypeScript
outputs are isolated by the task worktree. Reuse it for CP-2/3/4 without another build.

Use `scripts/run-checkpoint.ps1` for CP-5 and CP-6, with
`-Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c610-ci-fixes/`
and a fresh task-owned `-ResultsRoot`. CP-6 adds `-NoBuild`. For CP-5, save the inherited
`ANTIPHON_HEADED_TESTS` value, remove it in the launching PowerShell process for the awaited
checkpoint child, then restore it in finally. The pure eligibility tests receive explicit values
and do not alter any environment variable. CP-6 does not alter process PATH; S5 controls request PATH.

CP-5 uses `-MinExecuted 3 -Expect HerdrPaneDisposalEligibilityTests`. In addition to the wrapper's
executed roster, inspect the fresh TRX and captured output for exactly six **NotExecuted** results
from HerdrPaneDisposalGuardedLiveTests and the `ANTIPHON_HEADED_TESTS=1` skip reason. The wrapper
does not validate skipped names/reasons, so its exit zero alone is insufficient. Required skipped
roster: both `Isolated_backend_closes_only_reviewed_incarnation` argument cases,
`Isolated_backend_recovers_lost_close_result`, `C461_G083_Foreign_after_runner_inspection`,
`C461_G084_External_tree_race_at_teardown`, `C461_G085_Backend_rpc_fence`.

For CP-6 use `-MinExecuted 1 -Expect Codex_request_starts_CodexTranscriptTailer` and require zero
skips. Preserve fresh TRX, client JSON and lint output with tested commit IDs. Emit the documented
CHECKPOINT lines for every row; for direct lint rows record selected file count and problem count
instead of inventing test counts, and use `trx=n/a`. CP-4 produces JSON with the wrapper's
`-JsonResultPath`; create the task-owned `.antiphon` parent first and record actual test counts
(planned roster: 26 CardRow, 3 SortableCardList,
10 BacklogSection, 6 SpecialistRoutingPanel, 1 lint gate = 46).

Unexpected red requires a targeted base reproduction before calling it pre-existing; that
diagnostic run is an explicitly reported exception to the closed list. Do not broaden scope or
loosen assertions/timeouts. Inventory alternate .NET outputs produced by this dispatch, verify
resolved paths are inside its worktree, and remove only those owned `bin-c610-ci-fixes` directories
after all runs. No shared daemon restart or shared output deletion is needed.

### Cost

Estimates, not timings measured by this Plan task: ordinary Code V/R floor = **12 minutes**, the
sum of CP Min below (includes one client build and one .NET build). Allow **15 minutes authoring**
plus **3 minutes fresh dependency/setup overhead**: suggested Code ExpectAbout **30 minutes**.
PC red/restore/green floor = **6 minutes**; Mutation setup/discovery/reporting allowance =
**4 minutes**, suggested Mutation ExpectAbout **10 minutes**. Combined ordinary + PC execution
floor = **18 minutes**, or **22 minutes** including Mutation setup/discovery, before authoring
and cold setup. Three mapped controls means the PC floor is not zero.

The table uses two builds rather than six independent build-and-test cycles: four duplicate
builds are avoided. Compared with the investigated Windows full-project run (1001 cases,
5m19s), the selected runner rows cover four executing cases and six deliberate skips; the
previously measured 132.7-second lint gate is budgeted in CP-4, not replaced by per-file lint.
No repeated whole-client behavioral run is part of the ordinary scope.

### Checkpoints

This is the closed ordinary Code/Review verification list. Rows sharing outputs also share After.

| CP | After | Build | Group | Filter / exact command | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S3 | `client -> task-worktree client/dist` via `npm.cmd --prefix client run build` | refs-rule | `node node_modules/eslint/bin/eslint.js src/features/board/CardRow.tsx src/features/orchestrator/BacklogRow.tsx --max-warnings 0` | V-1, V-6 (build) | 2 selected files, 0 errors, 0 warnings; build succeeds | 2 |
| CP-2 | S1-S3 | CP-1 | refresh-rule | `node node_modules/eslint/bin/eslint.js src/features/board/SortableCardList.tsx src/features/board/placementFromReorder.ts --max-warnings 0` | V-2 (lint) | 2 selected files, 0 errors, 0 warnings | 1 |
| CP-3 | S1-S3 | CP-1 | state-effect-rule | `node node_modules/eslint/bin/eslint.js src/features/agents/SpecialistRoutingPanel.tsx --max-warnings 0` | V-3 (lint) | 1 selected file, 0 errors, 0 warnings | 1 |
| CP-4 | S1-S3 | CP-1 | client-behavior-and-lint | `pwsh -File scripts/test-client.ps1 src/features/board/CardRow.test.tsx src/features/board/SortableCardList.test.tsx src/features/orchestrator/BacklogSection.test.tsx src/features/agents/SpecialistRoutingPanel.test.tsx src/lint.gate.test.ts -JsonResultPath ../.antiphon/c610-client.json` | V-2, V-3, V-6 (lint gate), R-1, R-2, R-3, R-6 | All 5 files and all named cases, planned 46 passed, 0 failed/skipped; report actual expansion | 4 |
| CP-5 | S4-S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c610-ci-fixes/` | herdr-default-eligibility | `/*/*/(HerdrPaneDisposalEligibilityTests*)\|(HerdrPaneDisposalGuardedLiveTests*)/*` with headed flag absent | V-4, R-4 | 3 eligibility tests passed, exactly 6 named live cases skipped for opt-in, 0 failed | 3 |
| CP-6 | S4-S5 | CP-5 | codex-hermetic-tailer | `/*/*/HerdrRunnerSessionTests/Codex_request_starts_CodexTranscriptTailer` | V-5, R-5 | 1 passed, 0 failed, 0 skipped | 1 |
