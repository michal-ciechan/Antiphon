# CARD-0610 — GitHub Actions CI red on master: both jobs, confirmed causes

Investigate stage, 2026-09-23. Evidence from `gh api` / `gh run view --log` on the real runs, plus
local reproduction of the ubuntu failure at the same HEAD.

## Verdict

**Confirmed, four independent mechanisms, none of them a CI-environment artifact and none of them a
stale rule set.** The two red jobs have no common cause; they have to be fixed separately.

| # | Job | Failure | Nature | Landed |
|---|---|---|---|---|
| A | `ci` (ubuntu) | 9 × `react-hooks/refs` | eslint rule limitation on member-access; real code is correct | 2026-09-05 (CARD-0098 S3+S5) |
| B | `ci` (ubuntu) | 1 × `react-refresh/only-export-components` | real violation | 2026-09-05 (CARD-0098 S3) |
| C | `ci` (ubuntu) | 1 × `react-hooks/set-state-in-effect` | real violation | 2026-09-09 (CARD-0415) |
| D | `windows-native-tests` | 6 × `File.Exists(executable)` | test asserts a host tool instead of skipping | 2026-09-11 (`c03809e8c`) |
| E | `windows-native-tests` | 1 × `codex_launcher_unavailable` | real regression: CARD-0497 policy vs. un-updated older test | 2026-09-12 (`b67710b97`) |

A+B+C are 10 of the 10 lint errors. D+E are 7 of the 7 Windows test failures.

## Timeline (measured, not inferred)

Queried `repos/:owner/:repo/actions/workflows/ci.yml/runs?branch=master`, then per-run
`/jobs` for the step-level conclusion.

- Last fully green CI run on master: **`ffe5ee89`, 2026-09-01T09:33:25Z**, run `33492836667`.
  (Matches CARD-0599's claim.)
- **`windows-native-tests`** went red at `c7f7816c` (CARD-0292), 2026-09-01T11:13Z, run
  `33501370225` — then a *different* failure: `Queue_operation_enqueue_of_delivered_text_binds_via_C4`
  (`TranscriptAdoptionSafetyTests.cs:1023`). That one has since been fixed; it is green today. The
  job never recovered because D and E landed on top of it on 09-11 / 09-12.
- **`ci`** (ubuntu) was *not* red at that point. It flapped through 09-02 → 09-04, was green again
  from 2026-09-04T21:18Z (`33920454114`) to 2026-09-05T05:42Z (`33947901535`), then:
  - 2026-09-05T05:59Z run `33948604376` (`0d31a6f1`, CARD-0330 S4) — failing step was **`Build client`**
    (`TaskDrawer.test.tsx(94,54): error TS2322`), since fixed.
  - 2026-09-05T09:53Z run `33959173733` — failing step becomes **`Lint client`**, 9 problems, and has
    been `Lint client` in every single master run since. The commit before it is `a2ed452b`
    (CARD-0098 S3, 2026-09-05T09:18Z UTC) plus `73240a2e` (S5, 09:29Z UTC).
  - The 10th error appears on 2026-09-09 with `0f3fce07` (CARD-0415).

So: **`windows-native-tests` red for 22 days, `Lint client` red for 18 days**, latest master run
`35786565547` (`bb89e77b`) red on both.

Because `Lint client` is the second step, **`Build client`, `Test client` and `Test Messaging
packages` have been `skipped`, not run, on master since 2026-09-05** (verified on run
`35799075389`: `Lint client=failure, Build client=skipped, Test client=skipped, Test Messaging
packages=skipped`). Their true status on master is unknown. This matters directly to CARD-0599:
"CI is red" understates it — 3 of the 5 ubuntu checks have not executed at all for 18 days.

## Job A/B/C — ubuntu `Lint client`

### Reproduced locally, identically

`C:\src\Antiphon` is at the same SHA (`bb89e77b`) as the latest master CI run. Running
`node node_modules/eslint/bin/eslint.js . --max-warnings 0` there produces **exactly the same 10
errors in the same files at the same line:column**, exit 1. So this is not a runner-only effect and
not version drift: `client/package.json` pins `eslint-plugin-react-hooks: ^7.0.1`,
`package-lock.json` resolves 7.0.1, and the locally installed tree is also 7.0.1. `npm ci` on the
runner and the local `node_modules` agree.

The rule set is also **not newly introduced**. `eslint-plugin-react-hooks` has been `^7.0.1` since
the client scaffold (`69aaf5b0`), and `dbb5dc94e` (CARD-0076, 2026-08-19) deliberately drove the
whole client to zero problems under it — its own message says "rules from eslint-plugin-react-hooks
v7 were errors since scaffold, nothing gated them". These 10 are new *code*, not a new *rule*.

### A — the 9 `react-hooks/refs` errors are a rule limitation, not a real defect

Sites: `client/src/features/board/CardRow.tsx:49,50,51,139` and
`client/src/features/orchestrator/BacklogRow.tsx:51,52,53,100`. Both read off a `useSortable(...)`
result held in a single local (`CardRow.tsx:38`, `BacklogRow.tsx:~42`):

```tsx
const sortable = useSortable({ id: card.id, disabled: !showHandle })
...
<ActionIcon ref={sortable.setActivatorNodeRef} {...sortable.listeners} {...sortable.attributes} ...>
...
<Box ref={sortable.setNodeRef} ...>
```

`setNodeRef` / `setActivatorNodeRef` are dnd-kit **callback ref setters**, not ref objects; nothing
reads `.current` during render. To pin the actual trigger I ran five minimal probes through the
repo's own eslint config (`--stdin --stdin-filename src/features/board/Probe.tsx`, from
`C:\src\Antiphon\client`):

| Probe | Source | Result |
|---|---|---|
| 1 | `const sortable = useSortable({id}); <div ref={sortable.setNodeRef} {...sortable.listeners} />` | **2 errors** |
| 2 | `const { setNodeRef, listeners, attributes } = useSortable({id}); <div ref={setNodeRef} {...listeners} {...attributes} />` | **0 errors** |
| 3 | plain prop object, no hook at all: `<div ref={o.setNodeRef} />` | **1 error** |
| 4 | same, property renamed to `applyNode`: `<div ref={o.applyNode} {...o.listeners} />` | **2 errors** |
| 5 | `const { setNodeRef } = o; <div ref={setNodeRef} />` | **0 errors** |
| 6 | `<div ref={o.fn} />` alone | **1 error** |
| 7 | `<div {...o.data} />` alone | **0 errors** |
| 8 | `const fn = o.fn; <div ref={fn} {...o.data} />` | **2 errors** |

Conclusions, in order of what they rule out:

- Probe 4 rules out a **property-name heuristic** — renaming `setNodeRef` to `applyNode` still errors.
- Probe 3 rules out any **dnd-kit / hook-specific** knowledge — a plain prop object errors too.
- Probe 6 vs 7: the trigger is a **member expression reaching `ref=`**. A bare spread of a member
  expression is fine on its own (7), but once the object has supplied a `ref=` value the rule treats
  the whole object as ref-typed and every later read of it during render errors (8) — which is
  exactly the 1→3 and 1→4 clustering seen in `CardRow` and `BacklogRow`.
- Probes 2 and 5 show the rule is satisfied by **destructuring at the binding site**, with no
  suppression and no behaviour change.

So the finding is a false positive about the code's *correctness*, produced by a real limitation of
the compiler rule's member-access inference — and it has a clean, zero-suppression resolution that
is also the shape dnd-kit's own docs use.

### B — `react-refresh/only-export-components`, `SortableCardList.tsx:25`

`export function placementFromReorder(...)` sits in a module that also exports the `SortableCardList`
component. Real violation, and the repo already has five precedents for the fix: CARD-0076 moved
test-facing helpers verbatim into non-component modules (`transcriptModel.ts`, `filesReviewModel.ts`,
`ignorePattern.ts`, `describeImpact.ts`, `terminalCopy.ts`). `placementFromReorder` has exactly two
consumers: `SortableCardList.tsx:78` and `SortableCardList.test.tsx:8`.

### C — `react-hooks/set-state-in-effect`, `SpecialistRoutingPanel.tsx:18`

```tsx
useEffect(() => {
  if (!query.data || dirty) return
  const data = query.data
  setPairs(...); setEnabled(...); setToken(...)
}, [query.data, dirty])
```

Three `setState` calls synchronously in an effect body to seed form state from a query — the textbook
case the rule exists for, and the same shape CARD-0076 converted ~14 times to the
adjust-state-during-render pattern (precedent named in that commit: `WorkflowDetailPage`).

### The CARD-0076 gate exists, is working, and is being walked past

`client/src/lint.gate.test.ts` (added by `dbb5dc94e`) runs the ESLint API over the client inside
vitest and fails on any problem. `client/vite.config.ts:105` sets no `include`/`exclude`, so the
default glob picks it up — it is part of the ordinary suite.

Measured: `node node_modules/vitest/vitest.mjs run src/lint.gate.test.ts` in `C:\src\Antiphon\client`
→ **1 failed (1)**, 132.7s. So the full local client suite (`pwsh -File scripts/test-client.ps1` with
no filter) has *also* been red on master for 18 days. The gate did not fail to fire; it has been
firing, locally and in CI, and 18 days of landings went through anyway. Its own header comment says
"This repo has no CI, so the enforceable surface is the test suite" — that premise is now stale in
both directions: CI exists, and the test surface is being filtered around.

## Job D/E — `windows-native-tests`

Latest master run `35786565547`, job `106944684976`:

- `Antiphon.PtyHost.Tests`: total 130, **failed 0**, succeeded 107, skipped 23. Clean.
- `Antiphon.SessionRunner.Tests`: total 1001, **failed 7**, succeeded 987, skipped 7, 5m19s.

The 7 failures:

```
Codex_request_starts_CodexTranscriptTailer
Isolated_backend_closes_only_reviewed_incarnation(True)
Isolated_backend_closes_only_reviewed_incarnation(False)
Isolated_backend_recovers_lost_close_result
C461_G083_Foreign_after_runner_inspection
C461_G084_External_tree_race_at_teardown
C461_G085_Backend_rpc_fence
```

### D — 6 × `File.Exists(executable)`: an un-gated host-tooling assert

All six are `HerdrPaneDisposalGuardedLiveTests`, all six fail in the same place, from the CI stack:

```
ShouldAssertException: File.Exists(executable) should be True but was False
  Additional Info: V-9 requires the installed stock Herdr binary
  at HerdrPaneDisposalGuardedLiveTests.LiveFixture.StartAsync() in ...HerdrPaneDisposalGuardedLiveTests.cs:102
```

`tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardedLiveTests.cs:101-102`:

```csharp
var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Herdr", "bin", "herdr.exe");
File.Exists(executable).ShouldBeTrue("V-9 requires the installed stock Herdr binary");
```

That is a per-user desktop install (`%LOCALAPPDATA%\Programs\Herdr\bin\herdr.exe`). It is **not**
expected on a GitHub-hosted `windows-latest` runner and nothing in `.github/workflows/ci.yml`
installs it — the job's only setup steps are `actions/checkout@v4` and `actions/setup-dotnet@v4`.
This class is not merely "unavailable on CI": it carries **no opt-in gate at all**, unlike every
sibling. The precedent sits in the same assembly,
`tests/Antiphon.SessionRunner.Tests/HerdrLiveSession.cs:20-36`:

```csharp
if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
    throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed herdr tests");
if (!File.Exists(GrokExePath))
    throw new SkipTestException($"grok.exe not found at {GrokExePath}");
```

Visible in the same CI log: 7 sibling tests skip cleanly through that path ("Set
ANTIPHON_HEADED_TESTS=1 to opt in to headed herdr tests"; "V-21 needs ANTIPHON_HEADED_TESTS=1 and
ANTIPHON_C462_HERDR_SESSION..."). `HerdrPaneDisposalGuardedLiveTests` was added by `c03809e8c`
(2026-09-11) and simply never adopted it.

Other `SkipTestException` precedents in the same assembly:
`CodexCommandLengthHttpAcceptanceTests.cs:23`, `GrokRulesHttpAcceptanceTests.cs:23`,
`HerdrLabelFollowLiveTests.cs:22`, `HerdrPaneChildKillTests.cs:106`,
`LinuxPhoneHomeRunnerTests.cs:14`, `PtyBackendSeamTests.cs:79`, `PtyHostAdoptionTests.cs:142`,
`RunnerCustodyTests.cs:248`.

### E — `codex_launcher_unavailable`: a real regression from CARD-0497

```
CodexLaunchException: codex_launcher_unavailable: the Codex cmd launcher was not found beside the
working directory or on PATH...
  at CodexWindowsLaunchPolicy.Apply(...) in src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:100
  at SessionRunnerRuntime.StartCoreAsync(...) SessionRunnerRuntime.cs:346
  at HerdrRunnerSessionTests.Codex_request_starts_CodexTranscriptTailer() HerdrRunnerSessionTests.cs:271
```

The test (`HerdrRunnerSessionTests.cs:253-297`) is otherwise hermetic — it drives a `FakeHerdrServer`
and a temp `CODEX_HOME`. Its only host dependence is the literal exe it passes,
`HerdrRunnerSessionTests.cs:274`: `"codex.cmd"`. The test dates from `7f4e8cf9e` (CARD-0187,
2026-08-25), when nothing resolved that string. `CodexWindowsLaunchPolicy` did not exist until
`b67710b97` / `b2661115d` (CARD-0497, 2026-09-12); it now resolves the launcher up front and throws
at `CodexWindowsLaunchPolicy.cs:98-102` when a `.cmd`-looking name resolves to nothing. On the dev
machine `where codex` returns `C:\Users\lndco\AppData\Roaming\npm\codex.cmd`, so the test passes; on
the runner there is no Codex CLI, so it throws. CARD-0497 built the hermetic seam for its own tests
(`tests/Antiphon.SessionRunner.Tests/CodexNpmLayout.cs` — a temp dir with a zero-byte `codex.cmd`
carrying `CodexWindowsLaunchPolicy.StockNpmShimText`, a sibling `node.exe`, `codex.js` and a native
`codex.exe`) but did not carry the older CARD-0187 test onto it. The production policy is behaving
as designed; the test is the thing left behind.

## Why nobody noticed: CI is unowned

- Neither host tool is missing locally — `%LOCALAPPDATA%\Programs\Herdr\bin\herdr.exe` exists and
  `where codex` resolves — so all 7 Windows failures are invisible to every local run.
- `grep -rn "GITHUB_ACTIONS|GetEnvironmentVariable(\"CI\")|ANTIPHON_CI"` across `*.cs`, `*.ts`,
  `*.ps1`: **zero hits**. The repo has no CI-awareness of any kind; the only environment gate is
  `ANTIPHON_HEADED_TESTS`, which is opt-*in* and therefore already correct for CI — the failing
  classes just don't use it.
- `grep -rn "GitHub Actions|ci.yml|windows-native-tests" docs/*.md`: **zero hits**. AGENTS.md's
  testing section and `docs/testing-and-build.md` describe local and nightly running only.
  `docs/testing-and-build.md:83,101` say "CI/nightly keep the broad run" — the only two references
  to CI in the owner doc, and neither says who watches it or what it runs.
- Nothing gates landing on CI: `-Land` pushes to `master` directly.

## Recommendations (for the plan stage — not designed here)

1. **A (9 × refs)** — fix the code, do not relax the rule. Destructure the `useSortable` result at
   the call site in `CardRow.tsx` and `BacklogRow.tsx` (probe 2/5 show this clears all 9 with no
   suppression and no behaviour change). Do **not** add `eslint-disable`; CARD-0076 set the norm and
   `lint.gate.test.ts`'s header explicitly calls relaxing the rule "the lint equivalent of widening a
   test timeout".
2. **B** — move `placementFromReorder` into a sibling non-component module, per the five CARD-0076
   precedents; update the two importers.
3. **C** — convert `SpecialistRoutingPanel`'s seeding effect to the adjust-state-during-render
   pattern already used in `WorkflowDetailPage`.
4. **D** — give `HerdrPaneDisposalGuardedLiveTests.LiveFixture` the `HerdrLiveSession`-style
   `SkipTestException` guard (env opt-in + binary presence). Correct gate, not a fix: stock Herdr
   genuinely cannot run on a hosted runner.
5. **E** — point the CARD-0187 test at `CodexNpmLayout` like CARD-0497's own tests, so it stays
   hermetic. This is a real test defect, not a CI-appropriate skip: the behaviour under test
   (Codex request starts `CodexTranscriptTailer`) is CI-runnable, only the launcher path is not.
6. **The job does not need reconfiguring.** Both jobs are correctly scoped; nothing in `ci.yml` is
   wrong. What is missing is ownership, and — for CARD-0599 — the fact that green on the `ci` job
   currently proves nothing about `Build client` / `Test client` / `Test Messaging packages`, which
   have not run on master since 2026-09-05.
7. Worth a separate card either way: the CARD-0076 lint gate fires on every unfiltered local client
   run and has been ignored for 18 days. Fixing the 10 errors clears today's symptom but not the
   habit that let them land.

## Remaining uncertainties

- `Build client`, `Test client` and `Test Messaging packages` have not executed on master since
  2026-09-05. They may be green, or they may be hiding further failures behind the lint red. Nothing
  here measures them; the first green `Lint client` will reveal it.
- Whether `react-hooks/refs`'s member-access behaviour is an acknowledged upstream bug in
  `eslint-plugin-react-hooks@7.0.1` was not checked against the upstream tracker. The probes
  establish the trigger and a clean resolution regardless, so this does not change the
  recommendation.
- The ubuntu job's 09-02 → 09-04 flapping was characterised only at step level (`Lint client`, then
  `Build client`). Those causes were fixed by others and are out of scope.
- Feature-branch runs fail the same way (`35799075389`: `Lint client` + `Test session runner`), so
  nothing here is master-specific; no branch-specific effect was looked for.

## Not done, noted

- No fix written — this is an Investigate stage. The five fixes above are one-line-per-site changes;
  none of them was applied.
- No card filed for recommendation 7 (the standing-red local lint gate). It is structural and
  arguably belongs on its own card rather than inside CARD-0610's fix.

## Evidence index

- Runs: last green `33492836667` (`ffe5ee89`); Windows onset `33501370225` (`c7f7816c`); lint onset
  `33959173733`; latest master `35786565547` (`bb89e77b`), jobs `106944685181` (ubuntu) /
  `106944684976` (Windows); feature-branch sample `35799075389`.
- Local repro: `C:\src\Antiphon` @ `bb89e77b` — eslint 10/10 identical, exit 1;
  `vitest run src/lint.gate.test.ts` → 1 failed.
- Probes: `--stdin --stdin-filename src/features/board/Probe.tsx` against `client/eslint.config.js`.
