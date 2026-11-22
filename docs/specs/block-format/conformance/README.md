# ABF v1 conformance corpus

The test cases for the [Antiphon Block Format v1](../v1.md). The spec is a draft, so cases may change
until v1 is declared settled. The C# parser (server) and the TypeScript parser (client) must both
pass every case. The current C# legacy parsers (`PipelineHandoff`, `ReviewEvidence`) must pass
`legacy/`, reading `currentParsers` where a case has it. The ABF parser replaces them in design P0,
and `currentParsers` retires with them.

## Layout

| Folder | Input | Expected `.json` | Mode | Cases |
|---|---|---|---|---|
| `valid/` | `NNN-<name>.abf` | The canonical JSON (spec §4), with `"diagnostics": []` | strict | 10 |
| `tolerant/` | `NNN-<name>.abf` | The canonical JSON of the selected document, with its diagnostics | tolerant | 10 |
| `invalid/` | `NNN-<name>.abf` | `{ "expect": "<type>/<ver>", "errors": [ { "line", "code", "name"? } ] }` | strict | 10 |
| `legacy/` | `NNN-<name>.txt` | `{ "source", "envelope", "legacy", "values", "currentParsers"? }` | report envelope | 11 |

Each input has a `.json` file with the same stem. Files are UTF-8 with LF line endings and no
trailing whitespace that matters, so editors and Git may normalise them safely.

## How a parser runs each folder

Every case is parsed with the **report envelope** (spec §6.1). That is a no-op for files without a
`[antiphon-report:…]` line.

- **`valid/`:** strict mode, expecting the type and version in the `.json`. The result must equal
  the `.json`, and strict mode must raise no diagnostics.
- **`tolerant/`:** tolerant mode, expecting the `type` and `version` in the `.json`. That
  expectation is what lets `006-headerless` be read at all. The selected document's canonical JSON,
  including `diagnostics`, must equal the `.json`.
- **`invalid/`:** strict mode, expecting `expect`. The parser's errors must equal `errors`, the
  complete list.
- **`legacy/`:** the legacy scan (spec §6), which skips ABF documents (§6.2). `legacy` must equal
  the legacy document (§6.6), and `values` must equal the typed values (§6.4, §6.5).
- **The current C# parsers** are checked against `currentParsers` when the case has it, and against
  `values` otherwise. `next-stage` is `PipelineHandoff.TryParse(text)` and `review-evidence` is
  `ReviewEvidence.TryParse(text)`. `currentParsers` appears only in cases where an ABF document
  holds a line those parsers act on, so their whole-text reading differs from ABF's (§6.2). The enum
  and GUID fields are written as below.

## Comparison rules

- JSON values are compared structurally: object member order doesn't matter, array order does.
- In canonical JSON every value is a string, or an array of strings for a repeated name.
- **Diagnostics** are compared on `line`, `code` and `severity`, plus `name` when the expected entry
  has one. `message` is never compared. Lists are compared in the order the spec sorts them: line,
  then code, then name.
- **Errors** (`invalid/`) are compared on `line` and `code`, plus `name` when present. Their
  severity is always `error` in strict mode.
- **Legacy values:**
  - `kind` is the canonical token from `PipelineHandoff.Token`, or `null`.
  - `scope` is the `VerificationScope` name: `Unknown`, `None`, `Interim` or `Full`.
  - `subjectTaskId` is the GUID in D format, lower case.

## Context some cases need

- **`valid/005` and `tolerant/010`** answer the request in **`valid/004`**
  (`R-20261121-0042`), so run decision-response validation against it.
- **No assignment context.** Review-findings cases come with no list of assigned checks, so
  `missing-check` (spec §7.1) is not evaluated anywhere in the corpus.
- **`legacy/` envelope.** Every legacy case uses the report envelope. The stored-body asymmetry
  (spec §6.1) is covered by the spec text, not by a case.

## What the cases cover

| Case | Covers |
|---|---|
| `valid/001` | Every `review-findings` record, single-line and multi-line fields, optional row columns |
| `valid/002` | Multi-line content: indentation, blank lines, `---`/`===` alone, diff headers, `key: value` and closed fences as content |
| `valid/003` | Backslash stuffing, including `\\===` and a non-structural `\` line |
| `valid/004`, `005` | `decision-request` (noul, choice, score) and the matching `decision-response` |
| `valid/006` | `completion-request` with a diff whose deleted SQL comment is escaped |
| `valid/007` | `comment-triage`: multi-label intents, scope, reason category |
| `valid/008` | `seed-candidates`: source, provenance rows before multi-line fields, eval case, related hint |
| `valid/009` | Quoted columns with `\"`, a quoted last column, a repeated list field |
| `valid/010` | A document inside a report: preamble, trailing legacy blocks, report token |
| `tolerant/001` | A wrapped document with no `=== end`; trailing prose ignored |
| `tolerant/002` | Name fold: case, snake and camel case, spaced keys, `key:value` |
| `tolerant/003` | An unclosed fence protects nothing |
| `tolerant/004` | A closed tilde fence protects unescaped structural lines |
| `tolerant/005` | Row bullets, `field-as-row`, a percent probability, a bad row enum |
| `tolerant/006` | A headerless document read as the expected type |
| `tolerant/007` | An echoed example document, then the real one |
| `tolerant/008` | A legacy heading ends a document that has no `=== end` |
| `tolerant/009` | Stray text, a duplicate field, a duplicate record, `--- body ---` |
| `tolerant/010` | Decision-response repairs: percent, renormalised probabilities, `yes` |
| `invalid/001`–`010` | Missing header or end, unknown names, missing required fields, bad values, unescaped fence content, name spelling, unsupported version, decision-request rules, `=== check candidate C1` |
| `legacy/001`–`006` | `next-stage` from real report tails: basic, fenced, last block wins, unrecognised token, clipped handoff, prose `Next:` and a block after the token |
| `legacy/007` | `review-evidence` with an ABF document in the same report. A tilde-fenced example in a finding body is skipped with the document; today's parsers read it as a second standalone block (`currentParsers`: `review_evidence_duplicate`) |
| `legacy/008`–`010` | `review-evidence`: ignored examples, grammar edges, placement after next stage |
| `legacy/011` | A fenced `next-stage` example inside an ABF document, with no real block: ABF finds none; today's parser reads `next: plan` (`currentParsers`) |

Each `legacy/*.json` has a `source` member naming the report it was trimmed from
(`.antiphon/task-*.md`) or the parser test it reproduces.

## Adding a case

1. Pick the folder by mode and the next free `NNN`. Use a name that says what the case shows.
2. Keep inputs small and free of secrets. Trim real reports to the block and the lines that matter.
3. Write the expected `.json` from the spec, not from a parser's output.
4. For `legacy/`, also confirm the values against the current C# parsers before you commit. Where
   they differ because a line falls inside an ABF document, record theirs in `currentParsers`; any
   other difference is a defect in the case or the spec.
5. Add a row to the coverage table above.
