# Why missed: <cause>

One committed note per new rc-chunk cause. Path: `docs/investigations/<date>-why-missed-<cause>.md`.
The Debug done report carries the same facts under a `WHY-MISSED` heading.
Copy this file, replace every angle-bracket field, and commit the note with the fix. CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Introducing commit/card

introducing commit/card: <full SHA> (<CARD-nnnn>)

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- <review task id>, scope <Full or Interim>, selection <exact filter or class list>

## Outside the selection

why the failing test was outside that selection (lane/category/path): <lane>, <category>, <path>

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`<path-glob>` -> `<TUnit class filter>`

## Exemption

same cause as an already written note: link it.

The first note for a cause records `none` on this line. A later chunk with this cause links this file.
