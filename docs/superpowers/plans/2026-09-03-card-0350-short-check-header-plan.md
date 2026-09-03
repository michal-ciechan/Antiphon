# CARD-0350: bounded check headers and optional card aliases

## Outcome

Keep CARD-0350 open. CARD-0351 and CARD-0352 reduce the frequency of verbose delegate-task
titles, but neither makes check rendering safe: the server still accepts 300-character API and
fallback titles, and CARD-0352's as-yet-unshipped S3 is asynchronous and intentionally only
changes task titles. CARD-0350 owns the synchronous, deterministic display boundary.

## Decisions

1. Add an optional, human-authored `Card.Alias`, rather than another LLM title generator. It is a
   stable, card-level label shared by all of that card's delegated tasks. Validate it as a trimmed,
   single-line, at-most-five-word value with a short absolute length cap; reject invalid input at
   the API and CLI boundary. Do not silently generate or overwrite aliases. CARD-0352's generated
   task title remains a useful fallback only.
2. Bound the header independently of every title source. For an alias-less bound card and for an
   unbound task, normalize the task title to one line and clip it at a word boundary to 64
   characters (including an ASCII `...` suffix). A blank title renders as `Delegated task`.
3. Use the established ASCII envelope and compact ASCII status terms:

   ```text
   [check 1dcf13b9 #1] CARD-0348: Status Stuck | elapsed 10m/10m | running/working | activity 1m ago
   ```

   The optional reply qualification follows the elapsed term, for example
   `after reply; dispatched 4h ago`. Preserve the existing `[check <short-id> #<number>]` prefix:
   check detection and tests rely on it. Do not introduce emoji. The current PTY tests prove the
   ASCII paste/Enter path, not emoji or variation-selector fidelity on both modern and inbox paths.
   A future emoji proposal needs a parity canary proving exact `UserPrompt` preservation and a
   headed composer check first.
4. Spell the display reference `CARD-0348`, not `#348`. The parser deliberately reserves `#N` for
   cards, but raw external tracker values can also be shown as `#N`; the extra five characters
   avoid a reader-facing GitHub ambiguity without compromising the length goal. External references
   in this surface, if ever added, use `GH #N`.

## Implementation slices

### S1 — make the current check header safe before adding schema

- In `AgentTaskCheckService.BuildNote`, replace the unbounded `task.Title` bit with a dedicated
  one-line, word-boundary clipping helper. Keep the current whole-note byte ceiling as a final
  transport guard; it is not the header-length policy.
- Preserve the machine-readable check envelope and every existing degraded/no-session/final-check
  branch. Render the short identity and elapsed/session/activity fields with ASCII separators, not
  the illustrative emoji syntax.
- Add exact-header tests for a long multi-line task title supplied through the normal dispatch
  path, a direct/API-length title, an unbound task, an empty title, reply-reset timing, and the
  terminal/final-check variants. Assert both the fixed `[check ` envelope and a bounded first line.

This slice is independently shippable and protects first checks, legacy rows, direct API callers,
and every CARD-0352 hold/backlog/disabled case.

### S2 — make a card alias durable and editable

- Add nullable `Alias` storage to `Card`, the EF migration/model snapshot, card DTOs and create/
  update/content contracts. Include it in `CardRevision` snapshots and revision logging so a card's
  historical display label can be audited with its other content.
- Validate and normalize aliases in `CardService`; expose the value through `BoardService` card
  projection. Extend `scripts/card.ps1 new` and `edit` with `-Alias`; extend the client card API
  type and `CardEditModal` with a clearly optional “Short alias” input.
- Render the alias in generated card files as metadata so board-derived artifacts retain it, but do
  not replace the canonical title. Add service/API/CLI/client tests covering creation, update,
  clearing, validation, read projection, revision history, and generated-file output.

### S3 — use card context in check rendering

- Extend `DelegateCheckProbe.GatherAsync` to fetch the bound card's identifier and alias as probe
  facts. Do not add a database query in `BuildNote`; it must remain a pure formatter over gathered
  facts.
- Resolve display identity in this priority order:

  1. bound card alias: `CARD-NNNN: <alias>`;
  2. bound card without alias: `CARD-NNNN: <clipped task title>`;
  3. unbound task: `<clipped task title>`;
  4. blank source: `Delegated task`.

- Add probe and check-service tests for bound/unbound rows, alias absent/present/cleared, and the
  error/degraded branches. Update any fixed expected check text without changing its machine
  envelope.

## Integration and non-goals

- CARD-0351 (`ae06d961`) is already landed and should not be reimplemented. Its 80-character CLI
  title limit remains a helpful input improvement, not the rendering guarantee.
- CARD-0352 S1/S2 (`597f43c7`, `eb25e209`) provide diagnose substrate and title parsing; S3 has not
  landed. Consume its eventual short `AgentTask.Title` naturally through the fallback, but do not
  couple this work to its queue, prompt, budget, or title-rewrite timing.
- Do not change `#N` parsing, tracker binding, or the existing check envelope. Do not add automatic
  card alias generation in this card. An opt-in future suggestion/approval flow can be designed
  separately once CARD-0352 proves useful.

## Verification

Run the focused server suites for card CRUD/revisions/file rendering, check probes and check-note
formatting, then the relevant client/Vitest card-editor tests. Add the new migration to the normal
server integration coverage. No PTY emoji verification is required for this plan because v1 emits
no new emoji; any later emoji change must add the modern/inbox parity canary before adoption.
