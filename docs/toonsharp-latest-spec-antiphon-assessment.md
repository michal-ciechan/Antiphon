# ToonSharp Latest TOON Spec Assessment via Antiphon

Date: 2026-05-17

## Summary

Antiphon is able to launch Claude Code against a ToonSharp worktree. I created a clean ToonSharp project, board, and assessment card through the Antiphon website using Playwright only. Claude launched in the in-browser terminal and reached an idle state after inspecting the worktree.

The remaining work is primarily in ToonSharp, not Antiphon. ToonSharp `main` is documented as TOON v1.3 support with known deviations, while the official TOON spec repository currently presents spec document v3.0 and the latest GitHub release tag as v3.0.3. ToonSharp needs a fixture refresh, runner option support, removal of removed syntax, parser fixes, encoder fixes, and conformance documentation before it can claim latest-spec support.

## Antiphon Run Evidence

Antiphon interaction was performed through the browser UI with Playwright. No Antiphon API calls were used to create the project, board, card, or Claude session.

Created UI objects:

- Project: `ToonSharp Latest Spec 20260517-213650`
- Board: `Latest TOON Spec Assessment 20260517-213650`
- Card: `Assess ToonSharp against latest TOON spec 20260517-213650`
- Repo URL: `https://github.com/michal-ciechan/ToonSharp.git`
- Local repo path: `D:\src\ToonSharp-antiphon`
- Base branch: `main`
- Agent: `claude` using `cl.bat`
- Antiphon terminal cwd shown by Claude: `D:\src\Antiphon\workspace\worktrees\card-CARD-0001`

Screenshots:

- [Project created](screenshots/toonsharp-antiphon/01-project-created.png)
- [Board created](screenshots/toonsharp-antiphon/02-board-created.png)
- [Card created](screenshots/toonsharp-antiphon/03-card-created.png)
- [Claude launched terminal](screenshots/toonsharp-antiphon/04-claude-terminal.png)
- [Claude active after wait](screenshots/toonsharp-antiphon/05-claude-output-after-wait.png)
- [Claude assessment output](screenshots/toonsharp-antiphon/06-claude-output-second-wait.png)

Claude's visible terminal summary reported that ToonSharp is at a v1.3 implementation baseline and identified v3.0 as the target spec. It also reported 21 failures across 293 total tests in the active Antiphon worktree. Treat that as spawned-agent evidence; the clean clone baseline below is the authoritative local file inspection used for this document.

## Local Baseline

Repositories used:

- Clean ToonSharp clone: `D:\src\ToonSharp-antiphon`
- Existing dirty local checkout intentionally not touched: `D:\src\ToonSharp`
- Read-only official spec clone for comparison: `D:\src\toon-spec-latest`

ToonSharp clean clone state:

- Branch: `main`, clean.
- `README.md` says TOON v1.3 support with 16 known deviations.
- `SPEC.md` says version 1.3, dated 2025-10-31.
- Bundled official-style fixtures: 17 JSON fixture files, 273 fixture test cases.
- `SpecTestRunner` already defines `keyFolding`, `flattenDepth`, and `expandPaths` fields in the JSON model, but `MapOptions` does not map those options into `ToonSerializerOptions`.
- `ToonSerializerOptions` still exposes `UseLengthMarker`, and `ToonReader` still parses `[#N]` length markers.

Official spec comparison:

- Official spec document: v3.0, dated 2025-11-24.
- Latest GitHub release tag observed: v3.0.3.
- Official fixture suite: 22 JSON fixture files, 358 fixture test cases.
- Missing fixture files in ToonSharp compared with latest official fixtures:
  - `decode/arrays-nested.json`
  - `decode/path-expansion.json`
  - `encode/arrays-nested.json`
  - `encode/arrays-objects.json`
  - `encode/key-folding.json`

## Spec Delta

Main changes from ToonSharp's documented v1.3 baseline to latest:

- v1.4: canonical number formatting and normalization rules were clarified. Encoders need no exponent notation, no trailing zeros, and no leading zeros except `0`; decoders need defined behavior for exponent forms and out-of-range values.
- v1.5: optional `keyFolding="safe"` and `flattenDepth` were added for encoders; optional `expandPaths="safe"` was added for decoders.
- v2.0: `[#N]` length-marker syntax was removed. Encoders must not emit it, and decoders must reject it.
- v2.1 and v3.0: list-item object encoding was tightened. Latest v3.0 requires the YAML-style form for list-item objects whose first field is a tabular array: `- key[N]{fields}:` on the hyphen line, tabular rows at depth +2, and sibling fields at depth +1.

## ToonSharp Work Needed

### 1. Refresh Spec Assets

- Replace or clearly version `SPEC.md` with the official v3.0 spec text.
- Copy the latest official fixture suite from `tests/fixtures` into `ToonSharp.Tests/SpecTests/Specs`, preserving `encode` and `decode` folders.
- Add a small manifest or README that records the spec repository commit and release tag used for the fixture import.
- Keep old v1.3/v1.4 fixture snapshots only if they are explicitly namespaced as historical compatibility tests.

### 2. Upgrade the Fixture Runner

- Map fixture options into runtime options:
  - `keyFolding`
  - `flattenDepth`
  - `expandPaths`
  - `delimiter`
  - `indent`
  - `strict`
- Keep `minSpecVersion` filtering if the project intentionally tests multiple target versions, otherwise run the latest suite unfiltered.
- Produce a conformance summary in test output: total, passed, failed, skipped, and target spec version.
- Add one end-to-end test that loads every fixture file from disk, fails if any expected latest fixture is absent, and validates fixture schema compatibility.

### 3. Remove Removed Length Marker Behavior

- Delete or obsolete `ToonSerializerOptions.UseLengthMarker`.
- Remove encoder emission of `[#N]`.
- Make strict decode reject `[#N]`.
- Decide whether non-strict decode can accept legacy `[#N]`. If retained, document it as legacy compatibility, not spec-conformant behavior.
- Update README examples and unit tests that currently expect length markers.

### 4. Implement v3 List-Item Object Encoding

- Rework `ToonWriter.WriteObjectAsListItem`.
- Ensure tabular array headers are not emitted twice in list-item cases.
- Emit the canonical v3 form for a list item whose first field is a tabular array.
- Ensure sibling fields after the tabular array render at depth +1.
- Add direct BDD-style examples around:
  - single-field tabular list item
  - multi-field object with first field as tabular array
  - nested primitive arrays inside list items
  - nested object arrays inside list items

### 5. Implement v3 Decode Semantics

- Make array-header detection quote-aware. Quoted keys containing brackets must not be parsed as array headers.
- Parse quoted tabular field names correctly.
- Fix blank-line handling after arrays and inside arrays according to strict/non-strict mode.
- Fix delimiter scoping for nested arrays and object values inside list items.
- Fix negative leading-zero handling such as `-05` so it remains a string where required.
- Support empty document and root primitive behavior expected by the latest fixtures.
- Reject unterminated strings consistently.
- Add `expandPaths="safe"` support with strict conflict behavior.

### 6. Implement Optional Key Folding

- Add options for `KeyFolding` and `FlattenDepth` to `ToonSerializerOptions`.
- Implement safe key folding only for identifier segments.
- Prevent folding when a segment needs quoting, contains the path separator, or collides with another output path.
- Keep the default as off.
- Add encode tests from `encode/key-folding.json` and direct unit tests for collision and `flattenDepth` behavior.

### 7. Normalize Numbers and Precision

- Replace ad hoc double-oriented formatting with a formatter that preserves integer and decimal fidelity.
- Avoid exponent notation in canonical encoder output.
- Trim trailing zeros only where allowed.
- Preserve large integers that cannot round-trip through `double`.
- Add tests for max safe integer, large integer strings, decimals, negative zero, exponent input, and out-of-range values.

### 8. Update Public Documentation

- README should state the exact target: TOON spec document v3.0, release tag v3.0.3 or later, and the official fixture commit.
- Replace the current known-deviations section with a conformance matrix.
- Document non-strict legacy compatibility separately from latest-spec conformance.
- Update package notes for any breaking API changes, especially removal or obsoletion of `UseLengthMarker`.

## Suggested Antiphon Card Breakdown

Use Antiphon cards against the clean clone, one at a time, with each card producing tests first or alongside implementation:

1. Import official v3 fixture suite and schema-check the runner.
2. Remove length-marker emission and strict acceptance.
3. Fix quote-aware key and header parsing.
4. Fix blank-line, delimiter scope, and list-item decoding.
5. Implement v3 list-item object encoding.
6. Implement number normalization and precision-safe encoding.
7. Add safe key folding and path expansion options.
8. Update README, SPEC copy, compatibility notes, and conformance matrix.

For each card, the Definition of Done should include:

- Relevant latest official fixtures pass.
- Focused BDD-style integration tests cover the user-visible encode/decode behavior.
- `dotnet test` passes from the Antiphon worktree.
- Antiphon review shows a clean diff limited to ToonSharp implementation, tests, fixtures, and docs.

## Risks

- The latest spec introduces both mandatory removals and optional features. ToonSharp should not claim latest support until mandatory v2/v3 behavior is implemented and optional features are either implemented or explicitly marked unsupported.
- Removing `UseLengthMarker` is a breaking public API change unless it is first marked obsolete.
- The current reader detects headers with broad string checks like `Contains("[")`, which is risky for quoted keys and will likely require parser-level cleanup rather than small patch fixes.
- Numeric fidelity may require avoiding `double` as the universal representation for parsed numbers.
- The generated Antiphon worktree is separate from `D:\src\ToonSharp-antiphon`; do not use the dirty `D:\src\ToonSharp` checkout as an implementation base.

