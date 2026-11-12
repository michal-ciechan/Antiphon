# Why missed: C443 SingleWriteMaxBytes 1024 vs 86400

One committed note for the merge-batch 189 failure of the twenty `C443_*` rows in
`AgentTaskWorktreeLockOutcomeTests` (master `11eaf3c5106cbc2e71bbad7b82b6554780fbd60b`,
identical at parent `5b10b55de`). CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Failure

Linux `PtyDeliveryProfile.RefreshAsync` returns `SingleWriteMaxBytes` 86400. The fixture
expected 1024 whenever the host is not Windows, for both the `modern` and `inbox` arguments.
All twenty rows fail on that first assertion. The class is
`[Category("Integration")]` and `[Category("Slow")]`.

`PtyBackendPolicy.Resolve` returns `UnixPty` off Windows before any Windows selector
(`src/Antiphon.Agents.Pty/PtyBackend.cs`). Until CARD-1224, `DelegationSettings.CeilingsFor`
had no `UnixPty` arm, so that value fell through to the inbox triple and
`PtySingleChunkBytes` 1024. `de13507f7e27ec8e3ceaa5650675c4a680b4ed6f` (2026-10-25) pinned
that fall-through:

`OperatingSystem.IsWindows() && backend == "modern" ? 86400 : 1024`

The parent of the introducing commit, `72a552bdcdd335ac57595bc3d71215b60a875ee9`, still has
no `UnixPty` arm. The outcome body budget is a separate constant,
`LandNotificationPayload.EnvelopeBytes` 1024, and is unchanged.

## Introducing commit/card

introducing commit/card: `bb4f2811c2516d3754db5e34487ce4f6a4497532` (CARD-1224), cherry-picked
onto this line as `b35ed98633c4234f037a0fe589547206cf6b747d`

CARD-1224 is `b818f418-6efd-4d2a-9da0-89996e87f461`. S3 added
`UnixPtySingleWriteMaxBytes = 86_400` and a `CeilingsFor` arm, and deleted
`PtyDeliveryCeilingsTests.Unix_backend_keeps_conservative_limits` (the unit pin of 900 / 3000 /
1024). It did not edit `AgentTaskWorktreeLockOutcomeTests`. `git log -S 'PtyBackend.UnixPty =>'`
on `DelegationSettings.cs` names only `b35ed9863`. That commit is an ancestor of parent
`5b10b55de`, of master `11eaf3c51`, and of rc cut
`56e96eabf698e9c1fcd462836ad32ed1fb259e83`. The authored commit `bb4f2811` is not itself in
the rc cut; the cherry-pick is.

The product change is the measured Unix envelope (plan D-3 / D-4, receipt
`card-1224-unixpty`). The 1024 expectation in the C443 fixture is stale. The inbox and modern
Windows numbers are unchanged.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `a3e112b7-a37f-454f-88dc-40c8926d3d60`, scope Full, round Final, outcome Clean,
  `ordinaryScopeCompleted` Full, `reviewedSourceClean` true, reviewed
  `0c9b47baca178ebd8cd2eced8a70a6bdd82c0b7e` (S1, before the Unix arm). Selection: CP-1
  `/*/*/UnixPtyDeliveryEnvelopeTests/*` executed 7 passed 7 failed 0; nine-class guard union
  78/78; `PlanCoverageBindingTests` 7/7; Linux `Card1277PtyHostFixtureTests` 3/3. Recorded
  2026-11-11 as stage outcome `3dd81d57-d50e-4485-ab87-9c4f9d57c2cd`.
- `b6ebea9c-63c0-4f40-8930-acf27cafc647`, scope Full, round Final, outcome Clean,
  `ordinaryScopeCompleted` Full, `reviewedSourceClean` true, reviewed
  `bb4f2811c2516d3754db5e34487ce4f6a4497532` (S2+S3, the introducing commit). Selection:
  checkpoint run `20261111-122546-8617`, CP-1 `/*/*/UnixPtyDeliveryEnvelopeTests/*` 7/7, CP-2
  `/*/*/UnixDeliveryCeilingTests/*` 9/9, CP-3 the three named `PtyDeliveryCeilingsTests`
  methods 3/3, CP-4 `DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets`
  14/14, nine-class guard union 78/78. Record:
  `docs/superpowers/reviews/2026-11-11-card-1224-unix-delivery-ceilings.md` on
  `feat/card-task-b6ebea9c` at `97889de69b195a786ff737eaf4bceae95e32fc31`. Stage outcome
  `fe30ddea-8624-470e-b3ff-c75ddd9f741f`, recorded 2026-11-11.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
category `Integration` and `Slow`, path
`tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs`.

Neither Final selection names that class. The plan's ordinary rows are CP-1 through CP-4.
`tests/impacted-classes.json` `land-notification` matches `AgentTaskDispatcher.cs`, which S3
edited, but its class list does not include `AgentTaskWorktreeLockOutcomeTests`, and the S3
review treated that kind as outside the ordinary rows. The derived index for
`server/Application/Settings/DelegationSettings.cs` keeps `PtyDeliveryCeilingsTests` and
`UnixDeliveryCeilingTests` and does not keep this class (cap summary: 353 classes, 328 owed).
`Unix_backend_keeps_conservative_limits` was the pin the plan deleted; the same 1024 number
inside the C443 fixture was left behind. A Windows run of the class does not execute the
failing branch: the assertion still expects 86400 for `modern` and 1024 for `inbox`, and
Windows `Resolve` honors those selectors.

The live Windows rc run `0b292fa4` covers this class. Its logs are
`C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4`.
This server2 runner cannot read that path. In the antiphon chunk TRX, search for
`AgentTaskWorktreeLockOutcomeTests` and `SingleWriteMaxBytes`. The rc tip already contains
both the Unix arm and the stale assertion. On Windows the twenty `C443_*` rows should pass;
a `1024 vs 86400` mismatch is the Linux branch of the same line. A Windows pass is not
evidence the Linux expectation was updated.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Settings/DelegationSettings.cs` -> `/*/*/AgentTaskWorktreeLockOutcomeTests/*`

Proposed CARD-1801 row (not applied; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "c443-unix-single-write",
  "globs": [
    "server/Application/Settings/DelegationSettings.cs",
    "tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs"
  ],
  "classes": [
    "AgentTaskWorktreeLockOutcomeTests"
  ]
}
```

No `"windows": "required"`. The failing branch is off Windows. The class is Slow: the
roster bound is 20 nodes and 948 serial seconds. The unit pins
`UnixDeliveryCeilingTests` and `PtyDeliveryCeilingsTests` stay necessary and did not see
this fixture.

## Exemption

same cause as an already written note: none
