# CARD-1372: align SessionUnowned wording with current ownership

Date: 2026-10-20. Plan task: `034fe3b9-65d9-45c4-874f-edc45078bbca`.
Inspected base: `efc052e64d2f15c16af1640b7e2b8fc67f03bd92` (CARD-0188).
Branch: `feat/card-task-034fe3b9`. Code budget: **30-45 minutes**.
Verification design is folded into this dispatch; next stage: **code**.
This Plan ran no builds or tests.

Make the client tooltip and DTO summary describe missing current agent pointers,
consistent with CARD-0188's emitted headline. Read the live CARD-1372 and the
project, orchestration, HTTP, session-invariant and testing owners before planning.
This is a separate follow-up; preserve CARD-0188's reviewed source and evidence.

## Ground truth and wording inventory

Locations are at the inspected base. Searches covered server, client, tests and
docs for `SessionUnowned`, `standing owner`, and the old ownership exclusion.
There are exactly two stale production descriptions; no stale text assertion.

| Card assumption or surface | What the code/document actually says or does (file:line) | Planned disposition |
|---|---|---|
| DTO summary still requires no standing owner. | `server/Application/Dtos/AttentionDtos.cs:326`: `A live session older than ten minutes has no agent, card, standing owner or open task. Warning.` | Replace with `A live session older than ten minutes has no agent pointer, card or open task. Warning.` |
| Client attention description has the same drift. | `client/src/features/attention/attentionVisuals.ts:69`: `A live session has no agent, card, standing owner or open task. Inspect its evidence.` | Replace with `A live session has no agent pointer, card or open task. Inspect its evidence.` |
| The client string is user-visible. | `client/src/features/attention/AttentionPanel.tsx:235` renders `visual.hint` in the badge tooltip. | Test the production hint in the existing visuals unit suite. |
| A historical standing stamp still suppresses the warning. | `server/Application/Services/AttentionService.Leaks.cs:132` calls the stamp historical; `:134` excludes card/ended/young/nonlive/open-task rows, `:144` collects current pointers, `:151` emits `Live session has no agent pointer, card or open task.` | Premise is already fixed by CARD-0188; no predicate or headline change. Retain session-age threshold and remote confirmation. |
| Tests repeat the stale wording. | `client/src/features/attention/attentionVisuals.test.ts:164` checks the warning bucket and `:177` checks nonempty hints; neither pins this text. `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:24` exercises historical A/current B; `:84` already asserts the correct headline. | Add one client assertion; retain the existing server assertion unchanged. |
| Session inspection needs another test. | `client/src/features/attention/AttentionPanel.test.tsx:46` already covers SessionUnowned drawer/transcript access, without stale wording. | No routing or interaction change. |
| Current owner docs still describe the old rule. | `docs/ops-http.md:557` and `docs/session-runtime-invariants.md:944` already say current pointer; the latter explicitly distinguishes session age from time since ownership loss. | No owner-doc edit needed. |
| Historical docs contain the superseded exclusion. | `docs/superpowers/plans/2026-09-25-card-0691-pool-release-and-ptyhost-leaks-plan.md:159` says no `StandingAgentId`; `:360` specifies `StandingAgentId == null`. `docs/investigations/2026-10-19-card-0188-leaked-incarnation.md:133`, `:136`, `:205`, `:396` record that old behavior. | Preserve dated historical findings; this plan records their supersession. |
| CARD-0188's plan also mentions the old gate. | `docs/superpowers/plans/2026-10-19-card-0188-leaked-incarnation-detection-plan.md:33` records pre-fix ground truth; `:49` removes the exclusion, `:66` specifies the replacement headline, and `:479` deliberately restores the exclusion as a mutation. | Keep historical/design/mutation context. These are not current product descriptions to replace. |

Other uses of “standing owner,” such as `server/Domain/Entities/AgentSession.cs:7`,
describe historical identity or unrelated ownership contracts and remain valid.
`docs/cards/` is absent in this checkout; the live card was read through `card.ps1`,
and generated card documents are not an edit target.

## Decisions

- **D-1 — Match CARD-0188's vocabulary.** Use the exact replacements above, keeping
  the DTO's age/severity and the tooltip's inspection guidance. Reject “no agent”
  alone and “no standing owner”: either can misdescribe a stamped, unpointed session.
- **D-2 — Limit production changes to the two descriptions.** Keep the enum, label,
  color, icon, headline, ownership predicate, evidence and actions. Reject a shared
  cross-language string mechanism or new DTO field for two local descriptions.
- **D-3 — Pin the visible text once.** A direct Vitest equality assertion provides
  the natural red witness. Reuse the existing TUnit historical-incarnation test
  for server agreement. Reject source-file/XML-comment tests and broad suites:
  the summary is checked by diff, while runtime semantics are already covered.
- **D-4 — Preserve historical documentation.** Reject rewriting old investigation
  outcomes or mutation instructions as though they had always used the new rule.
  No unresolved product choice or session/input-wait lifecycle change is introduced.

## Slices

1. **S1, red witness:** add exactly one test to
   `client/src/features/attention/attentionVisuals.test.ts`, named
   `C1372 describes SessionUnowned using current agent pointer ownership`, inside
   `describe('attentionVisuals', ...)`. Assert `ATTENTION_VISUALS.SessionUnowned.hint`
   equals `A live session has no agent pointer, card or open task. Inspect its evidence.`
   Commit and push the test before CP-1; leave production text unchanged.
2. **S2, wording fix:** change only the two descriptions at the paths above.
   Inspect their diff and confirm the headline and current owner docs still agree.
   Commit and push, then run CP-2 and CP-3. No new C# test or behavior change.

## Verification design

**V-1:** the S1 client assertion must fail on the actual old tooltip with the exact
old/new text mismatch (one selected test, one assertion failure), then pass after S2.
Build/setup errors and zero selection do not count as red.
**R-1:** the existing
`AttentionServiceTests.a_historical_standing_incarnation_without_a_current_pointer_is_unowned`
must pass, including its exact headline assertion at line 84, historical evidence,
current-pointer controls, and no-mutation checks. It is one execution, not one per
internal assertion. **V-2:** manually inspect the DTO comment replacement and
confirm both current owner docs remain accurate; no executable check is claimed
for XML-comment text.

Lanes: CP-1/CP-2 are the portable **client unit (Vitest)** lane; CP-3 is the portable
**managed application integration (TUnit, isolated PostgreSQL, fake runner)** lane.
No real runner session is created by these tests. Placement follows freshly read
`GET /api/runner-defaults` and `GET /api/session-runners`; omit `-Runner` and
`-Platform` because no host or OS pin is needed. This Plan's canary executed on
`server2` / Linux, confirmed by `PhoneHome__RunnerId` and the live catalogue.
At 07:00 UTC the defaults revision was 2; the default runner was available,
dispatch-eligible and accepting work. This is an observation, not a fleet pin.

Run the checkpoint tool once per committed slice group: `run --plan <this-plan>
--rows CP-1 --expected-source-sha <S1-SHA> --max-wait 50s`, then after S2 use
`--rows CP-2,CP-3 --expected-source-sha <S2-SHA>`. Bootstrap
`tools/Antiphon.Checkpoints` into a fresh `bin-c1372-tool-s1/` or
`bin-c1372-tool-s2/` through `scripts/build-slot.ps1`, then invoke its coordinator
with `dotnet run --no-build --project tools/Antiphon.Checkpoints` and that same
OutputPath. Tool bootstrap is the only additional build, not another test row.
The coordinator leases each row; do not wrap the coordinator or its commands
in a second slot. On exit 75, continue `wait <run-id> --max-wait 50s` until terminal.
Exit 4 is a reported slot timeout, never an unleased retry.

Preserve CP-1's intentional failed outcome and console mismatch. Record actual
Vitest counts/exit codes (other tests are unselected), and CP-3's fresh TRX roster.
Require clean exact-SHA source binding, verified build provenance for CP-3, and
zero selected failures/skips on green. Retain unedited CHECKPOINT lines and
generated receipts gitignored; run the evidence-diff guard over base..HEAD.
Remove only owned alternate outputs using the testing owner's cleanup rules.
No full suite, browser/E2E, live canary test, or CARD-0188 mutation cycle is added.

Budget: 10-15 minutes edits/inspection, approximately 10 minutes checkpoints
including the isolated server build, and 10-20 minutes tool setup, publication,
receipt review and cleanup; **30-45 minutes total**.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | n/a | c1372-client-unit-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts -t "C1372 describes SessionUnowned using current agent pointer ownership"` | V-1 | exactly 1 selected/executed, 0 passed, 1 expected tooltip equality failure; exit 1 | n/a | 2 | true | n/a |
| CP-2 | S2 | n/a | c1372-client-unit-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts -t "C1372 describes SessionUnowned using current agent pointer ownership"` | V-1 | exactly 1 selected/executed/passed, 0 selected failed/skipped; exit 0 | n/a | 2 | true | n/a |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1372-green/` | c1372-managed-application-green | `/*/Antiphon.Tests.Application/AttentionServiceTests*/a_historical_standing_incarnation_without_a_current_pointer_is_unowned*` | R-1 | exactly 1 executed/passed, 0 failed/skipped; exit 0 | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
