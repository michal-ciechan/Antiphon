# CARD-0649 bound brief retry verification

## Scope and decisions

Fix review defects D1-D3 and D5 on the pinned 7fb7bc5cb commit. Runner pointers use a relative path because the runner starts in its mirror cwd; queue binding replaces it with a row-owned inbox path. The dispatcher reserves that exact path growth before deciding between the explanatory and compact pointer. Keep CARD-0647's repeated task marker: it remains useful for other oversized task messages, although marker loss from a second spill was already fixed by 7851254ea. The observed aff1b9c6 brief was 2,481 characters; the long title, scope and cwd in the sizing unit test are synthetic and do not establish a production root-cause length.

## Acceptance

- V-1: A runner-bound pointer still names the row-owned relative inbox path and preserves the complete original brief bytes.
- V-2: A pointer near the staged-path limit fits after binding, is delivered without a second spill, and has one complete recorded UserPrompt.
- R-1: Existing phone-home projection and spill transport classes remain green.
- R-2: The full Unit lane remains green, with unrelated host-load flakes named separately if observed.
- PC-1: Method-scoped SourceLanding Mutation remains pending; ordinary tests and nightly do not discharge it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c649retry/` | unit | `/*/*/*/*[Category=Unit]` | R-2, pointer sizing | Whole Unit lane; 0 failed except triaged inherited host-load flakes | 1 | 8 |
| CP-2 | S1 | `CP-1, --no-build` | integration | `/*/*/(PhoneHomeTaskDispatchProjectionTests*)\|(DurableRunnerSpillReceiptTests*)\|(PhoneHomeSpillTransportTests*)/*` | V-1, V-2, R-1 | All three full classes; 0 failed/skipped; recorded UserPrompt and original brief assertions execute | 3 | 12 |

Run the closed list with `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-28-card-0649-bound-brief-retry-plan.md`, then `wait` while exit is 75. Each row's driver obtains a host build slot. Code or plan changes after a red row require a new committed slice and a rerun of the affected row.

## Cost

Ordinary checkpoint floor: 20 minutes. No live stack, production runner, browser, or source mutation is part of these two rows. The method-scoped PC stays for the Mutation stage.
