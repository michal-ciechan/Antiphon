# Why missed: c1906 and c346 base-equivalence red after the CARD-1893 S0 HistoryEnabled line

Master `9cb2550e74a5dd18cae82b4788abf02e50521435` (Merge 266). Two daemon-free Compose fixtures
went red. Each one renders the default runner Compose project and compares it with a render of an
older `docker-compose.server2-runner.yml`:

- `scripts/fixtures/c1906-compiler-policy-compose.mjs`, historical file `d100f8fa2c`. TUnit runs it
  through `RunnerLinuxCompilerPolicyTests.Saved_values_render_and_survive_each_named_phase_independently`
  (`--models-only`).
- `scripts/fixtures/c346-workspace-slot-compose.mjs`, historical file `303c2476ac`. No TUnit class
  runs its base-equivalence path.

Both failed on the final `assert.deepEqual(current, base, ...)`, with one added line:

```
AssertionError [ERR_ASSERTION]: default render changed outside the new zero flag     (c1906 :242)
AssertionError [ERR_ASSERTION]: default render changed outside the new false flag    (c346  :226)
+         SessionRunner__HostStats__HistoryEnabled: 'false',
```

The Review afa5577c (CARD-1406) and its author 9aeb262a found the red. The failure is the
intentional CARD-1893 S0 stop-gap line, not a product defect.

## Introducing commit/card

introducing commit/card: ea4209a167cbaeab7453207485993f285a0cba9c (CARD-1893 S0, cherry-pick of
aaa774ee90d73ba62f2d5680ed3322e00bf6fd12), landed by Merge 266. CARD-1985 owns its retirement in S1.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 3066ee8d (CARD-1893 S0 Review round 1, subject feat/card-task-d2eef712 `6d291e1451`), scope Full,
  selection `C1893S0RunnerHoldTests` (2), the 11-class guard union (115),
  `TestTimingOperationLabelTests` and `ImpactedClassesSelectorTests` (52), and the compose classes
  `DockerStackContractTests`, `DockerStackDocumentationTests`, `CodexRunnerImageContractTests`,
  `DindRunnerContractTests`, `Server2StagingServerContractTests` and `VerifyDindRunnerScriptTests` (201).
  The slow `RemoteScriptContractTests` and recycle groups were waived.
- Merge 266 ran the C1893S0 rows and the DockerStack contract classes. It did not run either fixture.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the Linux Unit lane,
category `Unit`, `[NativeLinuxOnly]`, path `docker-compose.server2-runner.yml`.

- `tests/derived-impact-index.json` maps `docker-compose.server2-runner.yml` to 25 classes. It does
  not map it to `RunnerLinuxCompilerPolicyTests`. The fixture reads the compose file only through
  `CHECKOUT=<repo>` and `compose_host` in a node child. The C# class never names the file, so the
  deriver has no edge to follow. The index maps only the fixture script itself to that class.
- No class maps `scripts/fixtures/c346-workspace-slot-compose.mjs`. Its base-equivalence path runs
  only as a plan checkpoint row (`node scripts/fixtures/c346-workspace-slot-compose.mjs`, CP-4 of
  `docs/superpowers/plans/2026-11-06-c346b7a6-runner-slot-compose-plan.md`). The node harness
  `scripts/fixtures/compose-fixture-cleanup.test.mjs` starts it only with `--refusal-case`, and that
  returns before the base-equivalence step.
- `tests/impacted-classes.json` lists both fixtures under `timing-labels`, but only as changed
  paths that select classes. A change to the compose file does not select them.
- `RunnerLinuxCompilerPolicyTests` has a `platform: windows` exclusion in
  `tests/test-execution-policy.json` (CARD-1906), so the Windows Full qualification never runs it.
  Only a native Linux lane does.

The rc run did not fail on it. `rc/2026-11-26` (`29ba0c5002e6`) predates S0. `rc/2026-11-27`
(`e229fb4d3f`) carries the S0 line and both fixtures. A Linux Unit lane run of rc/2026-11-27 would
fail `RunnerLinuxCompilerPolicyTests.Saved_values_render_and_survive_each_named_phase_independently`.
The Windows qualification of that rc would not, because of the exclusion. No standing lane runs the
c346 base-equivalence path, so it would stay red without being seen.

## Fix

Test-only, on `feat/card-task-937e5617`. Each fixture now asserts that the current base
`session-runner` sets `SessionRunner__HostStats__HistoryEnabled` to exactly `'false'`, and that the
historical render does not set it. The fixture then drops only that key before `deepEqual`. The
comment cites CARD-1893 S0 and CARD-1985. The assertion message is
`CARD-1893 S0 HistoryEnabled stop-gap changed: CARD-1985 retires this allowance`, so S1 goes red
here when it removes the line, and S1 must delete the allowance in the same change.

Negative cases were run against a scratch edit of the compose file and then restored. Every case is
red in both fixtures:

| Case | Failing assertion |
|---|---|
| Another env line added beside S0 (`Volumes__2: /tmp`) | `default render changed outside ...`, `+ SessionRunner__HostStats__Volumes__2: '/tmp'` |
| Another existing env line changed (`Volumes__1: /stateX`) | `default render changed outside ...`, `+ '/stateX'` / `- '/state'` |
| The S0 line set to `"true"` | `CARD-1893 S0 HistoryEnabled stop-gap changed: CARD-1985 retires this allowance` |
| The S0 line removed (the S1 shape) | same S0 message |

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`docker-compose.server2-runner.yml` -> `/*/*/RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently`

The c346 base-equivalence path has no TUnit entry, so a table row cannot select it. The cheapest
check for it is a command row `node scripts/fixtures/c346-workspace-slot-compose.mjs` (about 15 s)
for any change to `docker-compose.server2-runner*.yml`. A follow-up could also add a
`RunnerLinuxCompilerPolicyTests`-style TUnit method that runs that fixture. This task did not edit
`tests/impacted-classes.json` (brief: at most one row, and only if unavoidable).

## Exemption

same cause as an already written note: none.
