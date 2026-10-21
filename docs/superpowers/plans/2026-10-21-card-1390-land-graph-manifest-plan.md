# CARD-1390: land graph manifest follow-ups

Date: 2026-10-21. This Code task writes the plan because the dispatch named this path as the checkpoint artifact. Verification design is here. Next stage after the green row: review.

Baseline: `origin/master` = `e79a14dfa9463a04e43bce188a2c9ae744641d67`. Branch `feat/card-task-4fef3a13`. Card: CARD-1390, platform Any. This plan pins no runner and no platform. Checkpoint lane: `tests/Antiphon.Tests`, one row. The task sentence requires three red witnesses and this one green row. It forbids a whole Unit lane, so that lane is not in the table.

## Ground truth

The committed manifest is digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F`, count 57. `git ls-tree -r HEAD` on this master is only `100644 blob` and `100755 blob`. No definition file is a symlink or gitlink. Copying the 57 definition files into a temp repo and running the CARD-1390 script prints that same digest, so `-Update` must not rewrite the real manifest.

`string.EndsWith("\0")` without `StringComparison` returns true for a string that does not end in NUL under `en-US`. `EndsWith` with `StringComparison.Ordinal` returns false. Real `git ls-tree -z` output ends in NUL, so the bug is hidden unless the witness strips that byte.

MSBuild `Include` is relative to the definition file. `server/App.csproj` with `Include="docs/**/*.md"` selects `server/docs/**/*.md`, which is not skippable documentation. The refusal is for a resolved repo-relative glob that can select `docs/**/*.md` or the root `README.md`. From `server/App.csproj`, that include is `../docs/**/*.md`.

`LandVerificationPolicy.MatchesAuditedGraph` already returns false when the embedded manifest is missing, short, or not uppercase hex. Nothing called that path. The first seam ignored its text, so a docs-only land still skipped.

## Decisions

**D-1 — Refuse docs inputs before the manifest is written.** `-Check` and `-Update` parse EmbeddedResource, Content, None, and Compile `Include` values on `.proj`, `.props`, and `.targets` blobs. Split on `;`. `Condition=false` still refuses. Expand only `$(MSBuildThisFileDirectory)` and `$(MSBuildProjectDirectory)`. An unknown `$(...)` that still contains `docs` and `.md`, or `README`, is refused. Other unknown properties stay allowed, because this repo has `$(PkgMicrosoft_Windows_Console_ConPTY)` and `$(_AntiphonRepeatFile)`. Absolute paths under the repo root are judged repo-relative. Paths outside the repo are allowed. `..` after a wildcard is refused. A project-local `README.md` or `docs/**/*.md` is allowed. The message contains `cannot approve it` and the original include. Do not run MSBuild. Refusal happens before the digest is printed and before the manifest is written.

**D-2 — Ordinal trailing NUL.** Compare with `StringComparison.Ordinal`. A missing or empty buffer exits 1 with `missing its trailing NUL`. Also reject U+FFFD in the ls-tree text, matching the C# tree check.

**D-3 — Validate every ls-tree row the way `MatchesAuditedGraph` does.** Safe path, unique path, blob mode `100644`/`100755`/`120000` or commit `160000`, full non-zero lowercase object id. Definition rows must still be regular `100644`/`100755` blobs. This does not change the live digest. No fourth test.

**D-4 — Malformed manifest forces the build, on this async flow only.** `UseManifestText` parses with the embedded-manifest rules and stores the result in an `AsyncLocal`. Malformed text is a manifest with a null digest and a null count, so it shadows the embedded resource. Dispose restores the previous value. A process-wide static would race `C1385_RegistryMatchesNarrowGraph`.

**D-5 — Master only.** Do not change RC skip behaviour. Do not hand-edit the manifest hex.

Rejected: treat every include string that contains `docs/**/*.md` as dangerous, regardless of the project directory. That refuses `server/docs/**/*.md`, which a docs-only land cannot skip.

## Slices

### S1 — Red witnesses

Commits on `feat/card-task-4fef3a13` before the fix:

- `b2c616bafd61b0662863940861864cd23eb00e6e` added the three tests and a no-op seam. The script invocation quoted `-Update`, so the first run failed in the harness.
- `34f8cd368d00b26cc908386eef5e4986199734d8` passes switches unquoted and points the docs include at `../docs/**/*.md`.

The witnessed reds, on that second commit, build slot `0f8738c9-195c-4cb7-863f-dad61902e0cc`, waited=0s, held=247s, 3 failed, 0 skipped:

- `C1390_UpdateRefusesDocsMarkdownInput`: exit 0, digest printed. The old script approved the include.
- `C1390_MissingNulFailsUnderNonInvariantCulture`: stderr was `manifest does not match the selected graph`, not `missing its trailing NUL`. The culture-sensitive check treated the stripped buffer as terminated.
- `C1390_MalformedManifestForcesTheBuild`: `Verifier.Calls` was 0. The docs-only land skipped.

These runs are unlisted. The card asked for one red per behaviour before the fix and one checkpoint row after it.

### S2 — Audit, ordinal NUL, scoped manifest

- `scripts/audit-land-verification-graph.ps1` — D-1, D-2, D-3. ASCII, no PS7-only syntax.
- `server/Application/Services/LandVerificationBuildGraph.cs` — D-4. `ReadManifest` uses the same parser.
- `docs/orchestration-loop.md` — say `-Update` refuses those includes and that regenerating the manifest does not approve them.
- This plan.

Do not change `server/Antiphon.Server.csproj` or the manifest. After S2, `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` must exit 0 and leave the manifest untouched.

## Verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-1 | `-Update` refuses an include that resolves to repo `docs/**/*.md`, names the include, and does not write the manifest. | `C1390_UpdateRefusesDocsMarkdownInput` |
| V-2 | Under `en-US`, ls-tree output with the trailing NUL removed exits 1 with `missing its trailing NUL`. | `C1390_MissingNulFailsUnderNonInvariantCulture` |
| V-3 | A malformed manifest forces the build and records `unrecognized-build-graph`. | `C1390_MalformedManifestForcesTheBuild` |
| V-4 | The registry still agrees with the script and the manifest. | `C1385_RegistryMatchesNarrowGraph` |

No R-n. No positive controls. Mutation has nothing to plant. Zero-PC plan.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1390/` | c1390-graph | `/*/*/LandNonCodeSkipTests/(C1390_UpdateRefusesDocsMarkdownInput*)\|(C1390_MissingNulFailsUnderNonInvariantCulture*)\|(C1390_MalformedManifestForcesTheBuild*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-1 V-2 V-3 V-4 | exactly 4 executed, 0 failed/skipped | 4 | 25 |
