# CARD-1385: land non-code fingerprint stays valid across unrelated scripts

Date: 2026-10-21. Verification design is in this plan because the dispatch brief required the red witness and the narrow greens here.
Next stage: Code. No test host was run. Source inspection only.

Baseline: `origin/master` = `326c91c7759522751213bf2c1bd24a1a9a1cb66e` (this task branch tip before the plan commit).
Card: CARD-1385, platform Any. `GET /api/runner-defaults` revision 2 has empty kind defaults and a global runner set. `GET /api/session-runners` shows one Windows runner and one Linux runner accepting work, and another Linux runner draining. This plan pins no runner and no platform. Checkpoint lane: `tests/Antiphon.Tests`.

## Ground truth

Measured with the production algorithm (ordinal-sorted `git ls-tree -r -z` rows, NUL-joined, trailing NUL, SHA-256, uppercase hex). `Includes` today selects 292 paths. The compiled constant is `AE2F82A463CD2527B57BF4EE83BBCA269D7FE4101D779EAE3947A113A3FA74A5`.

| What the card assumes | What `326c91c77` does | Consequence |
|---|---|---|
| The audited fingerprint matches master, so a docs-only land skips. | The constant matches commit `8c70d23802bf989e38ed353adb5116b35ed5158a` (292 entries, same digest). HEAD's broad digest is `38117EC061A0E7DC74FFBD811CC7DF3CE289ECCE266177E150DB3282D6AB635B`, still 292 entries. | The defect is present. `MatchesAuditedGraph` returns false, `DecideAsync` returns `unrecognized-build-graph`, and the land builds. |
| Any of the 292 inputs, including an unrelated script, stales the skip and `LandNonCodeSkipTests`. | Since `8c70d238` the only selected-path changes are `scripts/lib/nightly-run-impl.ps1`, `scripts/test-nightly-run.ps1`, `scripts/test-rc-platform.ps1`, and `scripts/test-test-selection.ps1`. None of those files contain `docs/` or `README`. | Refreshing the 292-entry constant would hide the defect until the next script edit. The red witness must add a script commit on top of whatever constant is compiled. |
| Narrowing to files that can read docs/README is safe if definition files stay in the hash. | Land verification is `dotnet build` at the repo root (`AgentTaskLandService.VerifyUnderSlotAsync`). A path-only narrow selector (definition files below) is 57 entries at both `8c70d238` and HEAD, digest `372C6022F310A4BAF38BD869E1C07FB604A12AEE6A90F11246AD4B29972DDAE3`. | Unrelated script edits do not move this digest. A later edit of a definition file does. |
| Fail-closed, and a skip is still `Skipped(non-code)`. | Mismatch, unreadable git, a non-master target, a filter, and `ForceVerificationBuild` all build. A skip sets `VerificationSkipReason=non-code`, stage outcome Skipped / detail `non-code`, and `VerificationCommand` `Skipped(non-code); target=…; candidate=…; graph=<Fingerprint>`. | Keep those branches and that record. Change only which rows the fingerprint hashes and where the hex is stored. |
| Tests copy the live inventory and expect a skip. | `SeedGraphAsync` duplicates the broad extension list, copies those files into the fixture, and commits them. Skip tests then expect zero verifier calls. | They fail on this master. `SeedGraphAsync` must call `Includes` so it cannot drift from production. |

The pre-change 57-entry digest is ground truth for the drift, not the manifest Code commits. S2 adds an `EmbeddedResource` to `server/Antiphon.Server.csproj`, which is itself a definition file, so the committed digest is whatever `-Update` prints after that edit.

## Decisions

**D-1 — Hash `dotnet build` definition files only.** `LandVerificationBuildGraph.Includes` is true when the lowercased extension ends with `proj` or is `.props`, `.targets`, `.sln`, `.slnx`, `.slnf`, or `.rsp`; or the file name starts with `Directory.Build.` (ordinal, ignore case); or the lowercased file name is `global.json`, `nuget.config`, `packages.lock.json`, `packages.config`, or `directory.packages.props`.

Dropped from the selector: `.ps1`, `.sh`, `.cmd`, `.bat`, `.py`, `.gitattributes`, `.gitmodules`, `package.json`, `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`, and `dotnet-tools.json`. Those paths do not decide `dotnet build` items. Making one of them read `docs/**/*.md` or the root `README.md` requires an edit to a definition file, and that edit changes this digest, so the skip stops until the manifest is regenerated.

Rejected: hash only blobs whose text contains `docs/` or `README`. Comments and project-local `README.md` already match, and a property such as `$(DocGlob)` can start embedding docs without a new literal in the file that changed. Rejected: the card's third option, skip when the diff itself touches no broad graph path and re-audit on a schedule. After a definition file starts embedding docs, later docs-only lands would skip until the next audit.

**D-2 — A script writes a checked-in manifest; one test is the registry.** Replace the hand-edited hex as the source of truth with `server/Application/Services/LandVerificationBuildGraph.manifest.txt`, two lines: uppercase SHA-256, then the decimal selected count. Embed it with logical name `Antiphon.Server.LandVerificationBuildGraph.manifest.txt`. That name does not use the `Antiphon.Server.Bundles.` prefix, so `InstructionBundles` ignores it.

`scripts/audit-land-verification-graph.ps1` (ASCII) takes `-Check` (default) or `-Update`. It runs `git ls-tree -r -z HEAD` in the repo root, applies the D-1 predicate, and hashes the same way as production. `-Check` prints `digest=<HEX> count=<N>` and exits 0 only when both lines match the manifest. `-Update` rewrites the manifest from that computation. Exit 1 is a mismatch or bad usage. Exit 2 is a git failure. Do not hand-edit the hex.

`LandVerificationPolicy.MatchesAuditedGraph` and the registry test both call one new helper, `LandVerificationBuildGraph.FingerprintOf`, so the hash cannot fork. `Fingerprint` becomes the manifest digest (a static property, not a hand-maintained const). A missing, short, or non-hex manifest makes `MatchesAuditedGraph` return false. Do not throw from type initialization.

The registry test `C1385_RegistryMatchesNarrowGraph` hashes the live checkout with `Includes` / `FingerprintOf`, runs `-Check`, and requires the C# digest, the script digest, and the manifest digest to be equal, with the same count. Selector drift or a definition-file edit fails this test until `-Update` is committed.

**D-3 — Fail-closed behaviour and the skip record stay.** Do not change `OnlyDocumentation`, `DocumentationPath`, force/filter/non-master handling, or the `Skipped(non-code)` record except that `graph=` reads the manifest digest. Unknown repositories, malformed or failed git, type/mode changes, and a definition-file blob the manifest does not name still build. `SeedGraphAsync` stops duplicating the extension list and copies paths `Includes` accepts, including `git add -f` for paths the fixture ignore rules hide.

**D-4 — Master only. No RC backport.** `DecideAsync` already returns `target-requires-build` unless the target is `refs/heads/master`. RC lands do not use this skip. Product hosts track the rc line, and this card does not change rc behaviour. Do not backport.

## Slices

### S1 — Red witness

Add `C1385_UnrelatedScriptChangeStillSkips` to `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs`. No production edit.

Shape, using the existing harness:

1. `CreateAsync()` (seeds the live graph and rebases the fixture source onto that master).
2. `AddDocsOnlySourceAsync()`.
3. `PushIndependentAsync("unrelated-script", "scripts/unrelated-card-1385.ps1", "# unrelated script\n")` so the script commit is already on fixture master. The land rebase then makes the candidate diff docs-only while the candidate tree contains the new script.
4. `LandAndAssertAsync(h, build: false)`, then assert `VerificationCommand` contains `Skipped(non-code)` and `graph=`.

On today's selector the script row changes the broad digest, the policy builds, and `Verifier.Calls` is 1. That is the expected red. Do not make it pass by writing today's 292-entry digest into the constant: the extra script commit is not in that digest, so the test stays red. Commit S1 before S2.

### S2 — Narrow selector, manifest, registry

Files:

- `server/Application/Services/LandVerificationBuildGraph.cs` — D-1 `Includes`, load the manifest, `FingerprintOf`.
- `server/Application/Services/LandVerificationBuildGraph.manifest.txt` — written by `-Update` after the csproj edit.
- `server/Application/Services/LandVerificationPolicy.cs` — call `FingerprintOf`; compare digest and count; invalid manifest returns false.
- `server/Antiphon.Server.csproj` — one `EmbeddedResource` with the logical name in D-2.
- `scripts/audit-land-verification-graph.ps1` — D-2.
- `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` — `SeedGraphAsync` uses `Includes`; add `C1385_RegistryMatchesNarrowGraph`.
- `docs/orchestration-loop.md` — the CARD-1380 paragraph that says the candidate must match the audited 292-entry inventory. State the D-1 selector, the manifest, and that unrelated scripts no longer stale the skip. Keep the fail-closed and `Skipped(non-code)` sentences.

Run `-Update` only after the csproj item exists, and commit the rewritten manifest in the same commit as the selector. Delete `bin-c1385-red/` and `bin-c1385/` before finishing.

## Verification design

| ID | Behaviour | Witness | Red lever |
|---|---|---|---|
| V-1 | A script commit already on master does not defeat a docs-only skip. | `C1385_UnrelatedScriptChangeStillSkips` | Broad `Includes` hashes the new `.ps1`, so the skip assertion fails. S1 is this red. S2 makes the same method green: zero verifier calls, `VerificationSkipReason=non-code`, stage outcome Skipped / detail `non-code`, command contains `Skipped(non-code)`. |
| V-2 | The compiled manifest, the C# selector, and the script agree. | `C1385_RegistryMatchesNarrowGraph` | Change one manifest hex digit, or change `Includes` without `-Update`. The test fails. Added with S2, green on that commit. |
| V-3 | A docs-only land still records `Skipped(non-code)`. | `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` | Already red on this master because the seeded broad graph misses the constant. Green after S2. |
| V-4 | A definition-file change on master still builds. | `C1380_TargetGraphDriftAndNewProjectsBuild` (both arguments) | Pushing a changed or new project still yields `unrecognized-build-graph` and one verifier call. Stays green. |

No whole-Unit run. Do not retarget `OnlyDocumentation` cases (`C1380_NonAllowlistedInputsBuild`, mode/rename, force/filter/rc, malformed git). They stay as they are because those branches are untouched. One new test that only compares a constant to itself is a stub; V-2 is not that test.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1385-red/` | unrelated-script-red | `/*/*/LandNonCodeSkipTests/(C1385_UnrelatedScriptChangeStillSkips*)` | V-1 | exactly 1 executed, 1 failed | 1 | 10 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1385/` | unrelated-script-green | `/*/*/LandNonCodeSkipTests/C1385_UnrelatedScriptChangeStillSkips` | V-1 | exactly 1 executed, 0 failed/skipped | 1 | 10 |
| CP-3 | S2 | CP-2 | registry-green | `/*/*/LandNonCodeSkipTests/C1385_RegistryMatchesNarrowGraph` | V-2 | exactly 1 executed, 0 failed/skipped | 1 | 2 |
| CP-4 | S2 | CP-2 | docs-skip-green | `/*/*/LandNonCodeSkipTests/C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` | V-3 | exactly 1 executed, 0 failed/skipped | 1 | 4 |
| CP-5 | S2 | CP-2 | definition-drift-green | `/*/*/LandNonCodeSkipTests/C1380_TargetGraphDriftAndNewProjectsBuild` | V-4 | exactly 2 executed, 0 failed/skipped | 2 | 5 |

### Cost

Ordinary Code floor is 31 minutes (sum of `EstimatedMinutes`), plus authoring. Run the checkpoint tool once per slice group (`--after S1`, then `--after S2`). CP-1's failure is the witness, not a defect to fix inside S1.

## RC

No backport. The skip is master-only today, and D-4 keeps it that way.
