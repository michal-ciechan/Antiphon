# Review round 2: owner-map guard (CARD-1836)

Reviewed source: `89e88d19232ab396be85a712d3de928f1ba55004` on `feat/card-task-5c78b016` (Code task `5c78b016-63e7-40fa-9c49-c5fd8cf6592c`). Diff-only range `eb5e7ead43396148fe5b9f8f4c7b90d185e7258f..89e88d19232ab396be85a712d3de928f1ba55004` (commits `998d4b7767999c66991fbe29951c9f9732a5ae33`, `89e88d19232ab396be85a712d3de928f1ba55004`). Round 2, Final, blockers only. Shadow mode only: no activation and no gate credit.

Verdict: clean. Both round-1 blockers are closed. The missed witness fails closed when the census is empty, and each of the six Composer paths fails on its own when that inventory row is removed. No assertion was deleted or relaxed.

## B1. Missed witness

The witness is not a pinned path. After the twelve historical cases, `HistoricalSelectionSoundness` takes `$inventory.unowned[0].path` from `Read-RcInputInventory` (tracked paths with no owning input and not a declared no-op, sorted by `Get-TestSelectionSorted`) and requires `Get-RioOwnershipStatus` on the single Pty suite to return `missed`.

At this tip the census has 202 paths. The head is `.antiphon/c959-code-evidence/archive-files.txt`. It is a tracked blob (`git cat-file -e` exit 0) and `Test-RioPresentOwned` is false for every suite (`owners=0`).

If a later map row owns that file, it leaves the census and the witness moves to the new head. That is the acceptable revisit, not a stale green. If `unowned` is empty or the property is removed, the status string is `no unowned path`, which is not `missed`, so the row fails. It does not skip.

Mutants, one inventory read, lease `31582e19-83ca-4767-bf4e-f2f43d3e3958`:

- Always-selected `Get-RioOwnershipStatus`: red, `got=selected` for that path. The twelve `currentMap` values are still all `selected`, so this row is what makes that lookup fail.
- Empty `unowned` and removed `unowned`: both red, `status=no unowned path`.

## B2. Composer paths

The six literals are exactly the changed paths of fixture case `6591e378-composer-probe` that the live Pty inventory present-owns. Set equality was true (6 and 6). They are not a trimmed copy. Each reason is `project-tree` from the inventory walk, not a handwritten owner-map row:

- `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs` — `project-tree:src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`
- `src/Antiphon.Agents.Pty/ComposerInputProbe.cs` — same project
- `src/Antiphon.FakeClaude/Program.cs` — `project-tree:src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj`
- `tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs` — the Pty test csproj
- `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs` — the Pty test csproj
- `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` — the Pty test csproj

Those projects are the Pty suite's own tree and its `ProjectReference`s. Removing one in-memory input row, with the other five left in place, failed `RIO HistoricalSelectionSoundness.composer-inputs` naming that path. All six drops went red.

The historical case still ORs changed paths and still expects `selected`. The per-path assert is what catches a single deleted row.

## Diff

`git diff eb5e7ead43396148fe5b9f8f4c7b90d185e7258f..89e88d19232ab396be85a712d3de928f1ba55004` touches three files. The lookup move into `Get-RioOwnershipStatus` is the same first-owned-path rule. `ExpectedRows` goes from 1 to 3. The C# harness now requires `.contract`, `.unowned`, and `.composer-inputs`. The fixture is untouched. No expected status was relaxed and no assertion was removed.

CP-1 at the pre-fix script blob `c6bbaca22e1416a4ae7ac81a106aa2eba9d7adcc` (the script as of `eb5e7ead` and `998d4b776`): harness `C487: 1 passed, 0 failed, 1 rows`, and the plan wrapper exited 1 because both new row names were absent. Lease `ed0f7979-0c1b-4f91-9ce3-50eb78e8dcc8`. The script bytes were restored before any later run (`git diff --exit-code` 0).

## Runs

Native output. `UseAppHost=false`, `TEMP=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`. No custom output directory. One lease per command. Positive controls stay pending for SourceLanding; the mutants above are review witnesses, not those cycles.

- Green replay `HistoricalSelectionSoundness`: `C487: 3 passed, 0 failed, 3 rows`, exit 0. Lease `77232256-cdc6-408c-a1fb-4cf6b1480fa3`, held 84s. Started at load 13.75.
- Build `tests/Antiphon.Tests/Antiphon.Tests.csproj`: 0 errors, 2424 warnings, 3m 51s. Lease `f19ffc23-90a8-437f-9149-ba62de2a3df5`.
- `IncrementalSelectionSoundnessTests`: total 2, succeeded 2, failed 0, skipped 0, 1m 03s. Lease `82236372-ad11-48a6-8d1c-4732e047a1e5`.
- Nine-class guard filter (plan CP-3, which also covers the named five-class batch and impacted `TestLaneCategoryGuardTests`): total 84, succeeded 84, failed 0, skipped 0, 1m 09s. Floor in the plan is 81. Lease `bff56092-1d2c-4d5a-a551-32b122e85ea8`. The brief's inherited 83/1 did not reproduce.
- `DerivedImpactIndexTests`: total 27, succeeded 27, failed 0, skipped 0, 3m 05s. Lease `55942ded-aa23-4f3d-a640-23bce659d2e8`. The plan estimated 2 minutes. The run finished under the 570s cap.
- `scripts/check-evidence-diff.ps1` over `eb5e7ead..HEAD`: commits=2, entries=0, violations=0.
- `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: clean. `origin/master` is `6be80dcccb9a9748f46d7c77f9c418d80e32f8bf`. Merged tree `f5985c8b6a612f7b2d1d0fdceb974aae83507892`. That tree still contains `Get-RioOwnershipStatus`, `.unowned`, and `.composer-inputs`.
- `scripts/audit-land-verification-graph.ps1 -Check`: exit 0, `approved`, digest `EF5DF60245B1F7B96F1A74B6E8CB09C355F966193D2431465BE1BBE28BFA33AA`, count=80. The brief's inherited `SatelliteResourceLanguages` refusal did not reproduce. Load was 30.03 when this script started.
- Live-lane input digest matches the pin `15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365`. Policy hash printed `8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2`.

`scripts/impacted-classes.ps1` on this range selected kind `test-lane-category` only: `IncrementalSelectionSoundnessTests` and `TestLaneCategoryGuardTests`. Both ran. The migration row did not match, so the downgrade selector added 0 rows. No `windowsFilters`. No OS-gated production branch, factory, or startup path changed.

`reviewedSourceClean` is false. These rows are build-slot runs, not checkpoint-tool receipts. The plan's CP-2 build cell names `bin-owner-map-r2/`; this review used the worktree's native output.

## Landing

`feat/card-task-9e6138ac` is still `eb5e7ead43396148fe5b9f8f4c7b90d185e7258f` (task `9e6138ac-b0f5-4cdb-8fdd-2823ac9d0c88`, deliverable that SHA). The reviewed tip is only on `feat/card-task-5c78b016`. Land the stack owner with `-FromTask`.

## Advisory

The six Composer paths are a literal list that matches the inventory today. A later fixture path that becomes present-owned would not be pinned until that list is edited. Removing any current path already goes red. This does not hold the land. The 3m 05s index run against a 2 minute estimate does not hold the land. PCs in the plan stay pending.
