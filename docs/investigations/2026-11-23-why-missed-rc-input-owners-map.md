# Why the rc input owner map kept a deleted path (2026-11-23)

Debug task ce283ff7. `RcInputOwnersMapTests.RealOwnerMapLiteralPathsExist` was red on master
`194233ccb` and `4e9b7efdb`. The other 33 methods of the class passed.

## Reproduction

At `4e9b7efdbea4af35340bfcc55ca7bc74db1c3286` on server2: one build-slot build of
`tests/Antiphon.Tests`, then one build-slot run of
`/*/*/RcInputOwnersMapTests/RealOwnerMapLiteralPathsExist`. Result: 1 total, 1 failed.

```
real-owner-map:missing-literal-path: scripts/qualification <= scripts/qualification/**
```

That was the only missing literal path in `scripts/lib/rc-input-owners.json`.

## Introducing landing

`9cabf2f08` ("feat(scripts): port the rc qualification supervisor set, status and status
snapshot to antiphon-scripts") is unit 7 of `docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md`.
It deleted all seven files under `scripts/qualification/`: `launch-rc`, `observe`, `preflight`,
`qualification-stop`, `start-supervisor`, `stop-supervisor` and `supervise-rc`. Their behaviour
moved to `scripts/lib/Antiphon.Nightly/Qualification*.cs` and
`scripts/lib/Antiphon.Scripts/Qualification*Command.cs`. The commit left the owner-map row
`{ "path": "scripts/qualification/**", "consumers": ["*"], "reason": "forced-full-selector" }`
in place.

Unit 7 was written on `wip/next` (author date 06:09Z). It reached master in an operator-session
replay with units 7 and 8: `9cabf2f08`, `98c30df17`, `794aa6f1f`, `99e3da984` and `641e74119`
all have committer time 2026-11-23 09:25:03-04Z. Master is linear here. That replay is the
landing.

## Fix

The fix is data-only: the row is removed and the test is unchanged. It is not repointed,
because every replacement file already resolves to `scripts/lib/**` with the same consumers
(`*`) and reason (`forced-full-selector`). The real `RcInputInventory.Read` was run at the
base and at the fix commit `391bda2d3`. Its 27 suites and 192,413 input records are identical
apart from the map blob itself. The 192 unowned paths and the unresolved list are identical
too. Only the deleted paths lose the row:

| Path | Before | After |
|---|---|---|
| `scripts/qualification/*.ps1` (gone; also a hypothetical re-add) | `scripts/**` (repository-reader-non-code, `*`) + `scripts/qualification/**` (forced-full-selector, `*`) | `scripts/**` (repository-reader-non-code, `*`) |
| `scripts/lib/Antiphon.Nightly/Qualification*.cs`, `scripts/lib/Antiphon.Scripts/Qualification*Command.cs` (17 files) | `scripts/**` + `scripts/lib/**` (forced-full-selector, `*`) | unchanged |

A file added again under `scripts/qualification/` would still select every suite through
`scripts/**`. Only its reason label would change. `scripts/regenerate-derived-impact-index.ps1`
reports the index unchanged. Nothing pins the map's bytes; an `ownership-map-changed`
unresolved entry on the next rc manifest that spans this commit is the map contract working as
designed.

## Why no gate caught it

- **The deleting commit selects nothing that reads the map.** `RealOwnerMapLiteralPathsExist`
  checks the whole map against the whole working tree. A deletion anywhere can turn it red.
  The derived impact index records only the class's literal reads, which are 34 edges. None of
  the 56 paths changed by `9cabf2f08` is one of them. The deleted `scripts/qualification/*.ps1`
  were not in the index at all. The map file itself was not edited. Its edge selects only
  `RcHistoricalAdmissionTests` and `RcReleaseBaselineTests`, so it would not have selected this
  class anyway. Of the five replayed commits, only `641e74119` (the index regeneration)
  touches an edge that selects the class, `tests/derived-impact-index.json`. That would have
  caught the red, but there is no record that impacted classes were run on the replayed range.
- **No pipeline land.** The units were replayed onto master by an operator session, not
  through Code, Review and land with CP rows. A Merge's short tests did not run on them.
  Unit 7's own record lists only the ported classes' in-process cases
  (`RcQualificationStatusTests`, `QualificationSupervisorTests`, `QualificationStopTests`,
  `StatusSnapshotScriptTests`, `RegisteredCallerScriptTests`, `LongPathsCheckoutScriptTests`).
- **The census command is not a gate.** `antiphon-scripts rc-input-owner-path-census`
  (CARD-2009) gives this exact answer in seconds. It is called only by that card's CP rows
  and by this test. No land, Merge or pre-push step runs it.
- **The port checklist does not mention the owner map.** The remaining-ps1 plan deletes
  directories of scripts unit by unit. Its unit records list the docs and skills that name the
  commands. They do not list `scripts/lib/rc-input-owners.json` rows that name a deleted
  directory.

## Follow-ups (not done here)

- Add the owner-path census, or this one test method, to the land/Merge short set for any
  change that deletes or renames a tracked path. It reads the whole tree, so it cannot be
  impact-selected from the deleted paths.
- Add an owner-map check to the remaining-ps1 plan's per-unit checklist for any unit that
  deletes a whole directory.
