# Why missed: composer input probe write cap

One committed note for rc qualification run `6591e378`, chunk
`agents-pty-all-group-1`, on `rc/2026-11-15` at
`ac6d00022e940ee3b90584c7d313567b4491c498`. CARD-1781.
Windows extract: `docs/investigations/2026-11-10-run-6591e378-composer-input-probe-g1-windows-failure.md`
on `feat/card-task-a03364d9` at `438eaafec`.

## Failure

`ComposerInputProbeTests.An_unrendered_token_is_re_typed_up_to_the_write_cap_and_no_further`
failed once. `result.Writes` should be 3 but was 2. Duration `00:00:01.1116500`.
The probe checks the 1000ms budget before the 100ms retype. A poll that wakes
after the budget returns the writes already issued. Fourteen sibling results
in the same class passed, including the `DeafForever` outcome test, which does
not count writes. 125 results overlapped the window in one 599-test process.

Classification: test fixture timing. The production loop is doing what it
states. The repair does not widen the 1000ms budget. One Windows sample is not
a p95 of `Task.Delay(10)`, and a single late poll already consumed the
remainder of that budget, so a larger fixed timeout is not a bound on the
condition. The cap test now steps the probe clock by the requested poll
(10ms). One hundred steps cover the 1000ms budget, three retypes land, and a
fourth does not. A separate test jumps each poll by 600ms and expects the two
writes already issued and `NeverAppeared`.

Linux after the fix: `/*/*/ComposerInputProbeTests/*` on
`tests/Antiphon.Agents.Pty.Tests`, 16 passed, 0 failed, duration 1.037s.
The red witness on the stepped seam, before that assertion change, was the
same message: `result.Writes` should be 3 but was 2.

## Introducing commit/card

introducing commit/card: `754d02f72f91245c85427f1ed2db7aa7ea9be7f3` (CARD-0103).
`feat(readiness): CARD-0103 S1+S2`, Mike Ciechan, 2026-08-20. The write-cap
assertion and the timeout-before-retype order both come from that commit.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `9b347270-c21b-485a-be06-2dbc9926d68f`, CARD-0103 review, succeeded. Scope was
  not recorded as Full or Interim. Selection actually run, isolated:
  `ComposerInputProbeTests` 9/9, `SessionMessageQueueDeliveryVerificationTests`
  43/43, `RunnerClaudeAdapterTrustPromptTests` 6/6. Headed canary skipped.
- `45f3fabb-84b9-4326-be92-ae5b89ada25e`, CARD-0449 review, succeeded. Scope was
  not a Full rc profile. Selection actually run: Pty
  `ClaudeStartupReadinessTests` + `ComposerInputProbe*` + `ClaudeEffort*`,
  89 passed, 0 failed, 7.9s, inside a 160-test superset.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the
method is in `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs`.
Lane is `agents-pty`. The class has no Unit or Integration category. Both
reviews ran that class. What they did not run is `agents-pty-all-group-1`,
the 599-test process in which this method failed. An isolated class run keeps
a 10ms poll near 10ms, so three 100ms retypes fit in 1000ms. The loaded group
does not. The impacted-classes row `pty-host-fixture` matches
`src/Antiphon.Agents.Pty/**` and selects `Card1277PtyHostFixtureTests`
(`windows: required`), not this class.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`src/Antiphon.Agents.Pty/ComposerInputProbe.cs`, `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs` -> `/*/*/ComposerInputProbeTests/*`

Proposed row (not added; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "composer-input-probe",
  "globs": [
    "src/Antiphon.Agents.Pty/ComposerInputProbe.cs",
    "tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs"
  ],
  "classes": [
    "ComposerInputProbeTests"
  ]
}
```

## Exemption

same cause as an already written note: none.
