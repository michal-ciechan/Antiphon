# CARD-0861 unbound Mutation candidate

Recipe revision 1. **Binding required after Code.** No deliberate mutant or
SourceLanding cycle has run. All PCs and variants below are pending.

Original card: cfcaf4e3-c55b-4ae5-8e51-f7e373be2f41 (Antiphon CARD-0861).
Original Code/landing owner: 6d69efc4-d755-4b24-8529-4ac30cdfdf98.
Plan: [size qualification](../../plans/2026-10-12-card-0861-grok-readiness-sizes.md).
Contract: [mechanical PC contract](../../../mechanical-pc-contract.md).

This is a design recipe, not a mechanically eligible execution pack. The binder
must resolve reviewed C, confirmed source operation O and landed L, exact raw
preimages, unique literal payloads, predicted mutant hashes, allowed changed-file
set, SDK/configuration, output identity and external evidence paths at L. Publish
the bound pack externally under the SourceLanding verification root. Nothing in
this file authorizes Low to locate sites or author mutations.

All proposed edits target src/Antiphon.Agents.Pty/GrokStartupReadiness.cs,
GrokStartupScreen. They share one file and must execute serially. Ordinary base
regression evidence is not a deliberate mutant/red/restore/green cycle.

| PC | Pending variant IDs | Target/defect recipe | Exact detecting filter | Baseline/restored; mutant roster |
|---|---|---|---|---|
| PC-1 | PC-1/80x24, PC-1/100x30, PC-1/160x50, PC-1/200x60 | IsQualifiedSize admits only the original 120x30 pair | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | 6 green; 4 native-ready assertions red, 120x30 and 40x20 green |
| PC-2 | PC-2/80x24, PC-2/100x30, PC-2/120x30, PC-2/160x50, PC-2/200x60 | Remove the input-content guard requiring exactly > or U+276F | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | 6 green; 5 changed-marker IsReady false assertions red, 40x20 green |
| PC-3 | PC-3/40x20 | At the unsupported-height guard, return a ready observation for a 20-row screen after blocker precedence | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | 6 green; 40x20 IsReady false assertion red, 5 admitted layouts green |
| PC-4a | PC-4a/80x24, PC-4a/100x30, PC-4a/120x30, PC-4a/160x50, PC-4a/200x60, PC-4a/40x20 | Skip the current-screen sign-in branch | `/*/*/GrokStartupSizeTests/Captured_sign_in_stays_blocked` | 6 green; all 6 Reason=SignIn assertions red |
| PC-4b | PC-4b/80x24, PC-4b/100x30, PC-4b/120x30, PC-4b/160x50, PC-4b/200x60, PC-4b/40x20 | Skip the current-screen trust branch | `/*/*/GrokStartupSizeTests/Historical_trust_prompt_replayed_at_size_stays_blocked` | 6 green; all 6 Reason=Trust assertions red |
| PC-5 | PC-5/80x24, PC-5/100x30, PC-5/160x50, PC-5/200x60 | Original size restriction as PC-1, through the real adapter's snapshot/wait entry | `/*/*/RunnerGrokAdapterReadyTests/Captured_nondefault_size_reaches_ready_without_input` | 4 green; all 4 native-adapter WaitForReady true assertions red |

26 argument variants are pending. A method filter selects its complete expanded
argument roster; bind each case identity and the exact acceptable failure set.
The dimension restriction exposes all four variants in one method-scoped cycle;
do not invent four identical cycles just to recount the same observation.

## Reachability and missing-control inventory

- PC-1/5: native ready screens have blank immediate status/separator rows,
  captured borders and enabled hint. Default 120x30 and narrow 40x20 are companions.
  The adapter takes coherent snapshots, zero settle, two observations and no
  input; a third-read clock advance exposes the old rejection without waiting
  for the real five-second test budget.
- PC-2: each admitted original is asserted ready first, then exactly one marker
  cell changes to x. All other geometry and status remain eligible.
- PC-3: the native narrow screen is tested first against handwritten Unknown.
  Keep sign-in/trust precedence and all other admitted-size companions intact.
- PC-4a: six real native sign-in screens, with device-code cells redacted before
  storage. The oracle names SignIn, so merely returning not-ready cannot hide
  the defect. A blank code-row companion still requires SignIn.
- PC-4b: explicitly synthetic terminal replays of the historical real Windows
  trust text; no native 1.0.41 trust capture was observed. The affirmative phrase
  changed to Yes, continue is an Unknown companion.
- Audit existing guards too: upper/lower/input borders, maximum row width,
  unsupported sizes, blank status and separator, exact enabled hint, working
  status, stale raw history and settlement reset. Full existing fixture classes
  cover these ordinary outcomes; Mutation owns missing-control discovery and
  reports adequacy gaps rather than assuming this recipe is exhaustive.

Each bound cycle uses a host build slot, isolated bin-c861-pc/ output, fresh
external baseline/mutant/restored directories and a forced affected-graph build.
The checkpoint driver must report the exact method roster, nonzero execution,
zero skips and the bound assertion failures; compile/fixture failures are not red.
Restore exact tracked/index bytes and await every owned command. SourceLanding
never commits or pushes from its managed snapshot; repairs return to Code,
ordinary Review, landing and a newly commissioned SourceLanding pass.
