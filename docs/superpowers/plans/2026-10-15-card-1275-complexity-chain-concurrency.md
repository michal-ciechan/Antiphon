# CARD-1275 complexity-chain concurrency token — Code plan

Card: CARD-1275 (found by Final Review 9ea4cb0e of CARD-1270, disclosure 1). Base: master
`e7b6b80378dffe7b2326a8bc46ee5a91d30e9e69`. Branch `feat/card-task-8f76f70e`.

## Design

**Version field: a row token, not `updatedAt`.** `ComplexityChain.ConcurrencyToken` (uuid, EF
`IsConcurrencyToken`) is rotated on each write and clear. `updatedAt` was rejected:

- Postgres stores microseconds while the written `DateTime` carries 100 ns ticks, so the value a
  client read back can differ from the in-memory value the same request compares against.
- Two writes inside one clock tick (and every write under a fixed test `TimeProvider`) carry the
  same `updatedAt`, so it does not identify a version.
- A clear sets `ClearedAt` and leaves `UpdatedAt`; a token rotates on both.

Migration `20261015090000_AddComplexityChainConcurrencyToken` adds the column with
`gen_random_uuid()` for existing rows, then drops the default (the model has none). The entity
initialises a fresh GUID so fixtures that insert rows directly never store the empty GUID, which
the wire reserves for "expect no row".

**Wire contract.** `ComplexityChainDto.concurrencyToken` is the token of the cell's own row
(null when absent; on the `?role=` view, null when the role cell resolves through the any-role row
or config). PUT body `concurrencyToken` and DELETE `?concurrencyToken=` carry the version the writer
read: omitted = no version check (last writer wins), empty GUID = expect no active row, other =
must equal the active row's token. A mismatch is 409 `complexity_chain_stale` with a `current`
problem-details extension holding the row as GET returns it (pre-serialized with string enums and
nulls kept, because the problem-details writer drops nulls and writes enum numbers). A DELETE whose
row is already absent stays a 204 no-op. A write committed between the read and the save makes the
save fail (EF concurrency token on UPDATE, unique active-row index on INSERT). A write with a token
is then 409 at once; a write without one re-reads and re-applies its values, up to
`UnconditionalAttempts` (3) saves, before the same 409 (repair D1 of Review 40767d91). A clear that
lost to another clear returns 204. The lazy `NotAfter` sweep rotates the token and re-reads after a
lost race, up to the same bound; after the third lost race its `DbUpdateConcurrencyException`
propagates and the request is a 500 (V-23, repair D2 of Review 72440e28). The `?role=` view reads a
role cell's token, reason and expiry before its candidates, so a cell created or re-created between
the reads pairs newer candidates with an older token (or none) and a save with it is 409 (V-21,
repair D1 of Review 72440e28). The two reads stay separate because a failure of each is reported
as its own part (`chain metadata`, `chain`). The unfiltered list and `complexity-chain.ps1` take
candidates and token from one read.

**Backward compatibility.** `complexity-chain.ps1 set|clear` reads `GET /api/complexity-chains`
once and sends each cell's token (or the empty GUID). `migrate-stage-pins-to-cells.ps1` is
unchanged: its apply and restore paths send no token, so they write without a version check and
re-run a lost race up to the bound (the restore path re-imposes a before-image). Callers outside
the repo that omit the token keep last-writer-wins; only a race lost on each of the 3 attempts is a
409 they did not get before CARD-1275 (V-18).

**Client.** `usePutComplexityChain`/`useClearComplexityChain` take an optional `concurrencyToken`
(omitted from the body/query when undefined). The cell editor sends the version it opened (empty
GUID for an absent row; no token when the stored row or its metadata failed to read, behind the
existing overwrite confirmation). A 409 stale shows "Changed since you opened this: reload" with
the stored row beside the draft and disables Save/Clear; Reload replaces the draft and the token
with the stored row; no retry happens without that click. A paused (offline) mutation shows a
"Queued" note worded by what the queued request carries: with a token, the server does not
overwrite a row changed meanwhile; without one (unread row), it replaces the row stored when sent
(repair D2). `MatrixSection` renders the open editor at a fixed tree position outside the list
read's pending/error branches, so a failed list refresh keeps the draft.

Not changed (disclosed, out of the card's task list): closing the modal does not cancel an
in-flight or queued PUT; the token makes a late queued PUT refuse rather than overwrite.

## Slices

| Slice | Commit | Content |
|---|---|---|
| S1 | test | `ComplexityChainConcurrencyHttpTests` (red: no token in DTO/request) |
| S2 | feat | entity, migration, DTOs, service, endpoint |
| S3 | test | `ComplexityChainScriptTests` token expectations, `ScriptedApi` tokens |
| S4 | feat | `complexity-chain.ps1` read-then-write |
| S5 | test | client API, editor and routing-tab tests |
| S6 | feat | client API, editor, `MatrixSection` |
| S7 | docs | `docs/antiphon-api.md`, this plan |
| S8 | test | repair D1/D2 (Review 40767d91): `ComplexityChainRaceTests`, editor queued-note tests (red before S9) |
| S9 | fix+docs | bounded no-token re-run, sweep token rotation and re-read, per-case queued note; API doc, DTO and client comments, this plan |
| S10 | fix+test+docs | repair D1-D3 (Review 72440e28): `?role=` token read before the candidates, V-21 replaced, V-23, fault ordinals in `ComplexityEffectiveHttpTests` and `OrchestratorInstructionsRefreshTests`; API doc, DTO comments, this plan |

## Verification design

| ID | Kind | Test | Asserts |
|---|---|---|---|
| V-1 | new | `ComplexityChainConcurrencyHttpTests.Stale_put_is_409_with_the_current_row_and_leaves_it_unchanged` | 409 `complexity_chain_stale`; `current.concurrencyToken`, reason and candidates are the intervening write's; GET unchanged |
| V-2 | new | `ComplexityChainConcurrencyHttpTests.Stale_delete_is_409_and_keeps_the_row` | stale DELETE 409 with current token, row kept; fresh DELETE 204, `resolvedFrom` none, null token; repeat 204 |
| V-3 | new | `ComplexityChainConcurrencyHttpTests.Create_when_absent_needs_no_version_and_the_empty_token_refuses_an_existing_row` | no-token create 200; empty token over a row 409; no-token overwrite 200 with a new token; token of a cleared row 409 with `current.source` config |
| V-4 | new | `ComplexityChainConcurrencyHttpTests.Concurrent_writers_with_one_token_leave_exactly_one_winner` | six parallel empty-token creates: one 200, five 409; six parallel updates from one token: one 200, five 409; GET equals the winner |
| V-5 | new | `ComplexityChainConcurrencyHttpTests.Effective_view_carries_the_cells_own_token_not_the_any_role_rows` | role cell token on `?role=`; null for a cell resolved via the any-role row; list any-role token |
| V-6 | new | `ComplexityChainScriptTests.Set_and_clear_send_each_cells_token_from_one_read_and_stop_on_stale` | one GET then per-cell token or empty GUID on PUT and DELETE; 409 stops `All` with `code complexity_chain_stale` |
| V-7 | changed | `ComplexityChainScriptTests` request-sequence and path assertions (`C470_mutation_cell_round_trip`, `Set_puts_the_tier_provenance_and_candidates`, `Clear_deletes_by_complexity`, `Clear_Role_Plan_deletes_the_cell`, `Set_All_*`, `ClearStagePin_*`, `Clear_All_deletes_three_cells`) | leading `GET /api/complexity-chains`; DELETE `?concurrencyToken=`; PUT body token from the stub list |
| V-8 | new | `complexityChains.test.tsx` `C1275 sends concurrencyToken in the PUT body and as ?concurrencyToken= on DELETE`, `C1275 reads the current row from a 409 complexity_chain_stale and nothing else` | exact PUT body with token; DELETE search string; `staleComplexityChain` returns `current` only for 409 + stale code + object |
| V-9 | new | `ComplexityChainCellEditor.test.tsx` `C1275 a stale Save shows the stored row, does not retry, and Reload adopts the new version only on click` | stale alert text, Save/Clear disabled, one PUT before Reload, draft kept, Reload loads stored row, second PUT carries the new token |
| V-10 | new | `ComplexityChainCellEditor.test.tsx` `C1275 a stale Clear sends the opened token and shows that the row is now absent` | DELETE search `?concurrencyToken=<opened>`; "Stored now: no own row for this cell" |
| V-11 | new | `ComplexityChainCellEditor.test.tsx` `C1275 an unset cell sends the empty token; an unread cell confirmed for overwrite sends none` | empty GUID for an absent row; no `concurrencyToken` key after a metadata read failure |
| V-12 | new (text exact since repair D2) | `ComplexityChainCellEditor.test.tsx` `C1275 an offline Save shows queued and sends once with the opened token when back online` | queued note text equals `CHAIN_QUEUED_NOTE` while offline with zero PUTs; one PUT with the opened token after reconnect |
| V-13 | new | `RoutingSettingsTab.test.tsx` `C1275 a failed list refresh keeps the open Any role editor and its unsaved draft` | same editor element and draft value through a failed and then a successful list refresh |
| V-14 | changed | `ComplexityChainCellEditor.test.tsx` `PUTs the exact Human wire payload…`, `PUTs the any-role cell…` | exact payloads now include the opened token |
| V-15 | new (repair D1) | `ComplexityChainRaceTests.A_write_without_a_token_that_loses_a_race_is_retried_and_the_callers_values_win` | a competing commit between read and save: no-token PUT (update, and create racing a create) saves on attempt 2 with the caller's values; new token differs from the competitor's and the seed's |
| V-16 | new (repair D1) | `ComplexityChainRaceTests.A_delete_without_a_token_that_loses_a_race_is_retried_and_clears` | no-token DELETE racing a PUT clears on attempt 2; racing a clear returns after 1 save with no row |
| V-17 | new (repair D1) | `ComplexityChainRaceTests.A_write_with_a_token_that_loses_a_race_is_stale_at_once_with_the_current_row` | token PUT, token DELETE and empty-token create each lose a race: 1 save, 409 stale, `current` token is the competitor's, stored row the competitor's; the old token again is 409 |
| V-18 | new (repair D1) | `ComplexityChainRaceTests.A_race_lost_on_every_attempt_exhausts_the_bound_and_is_stale` | bound is 3; a competing commit before each save: PUT and DELETE without a token make 3 saves, then 409 |
| V-19 | new (disclosure 2) | `ComplexityChainRaceTests.The_expiry_sweep_rotates_the_token_so_a_write_that_read_the_row_live_loses_its_save` | sweep between a token PUT's read and save: 409 with no row, swept row's token rotated; no-token PUT recreates the row on attempt 2 |
| V-20 | new (disclosure 2) | `ComplexityChainRaceTests.A_sweep_that_loses_to_a_write_reads_again_and_returns_the_writers_row` | a sweep whose save loses to a writer returns the writer's row, no exception |
| V-21 | new (repair D1, Review 72440e28) | `ComplexityChainRaceTests.The_role_view_reads_the_token_before_the_candidates_so_a_change_between_them_makes_a_save_stale` | after the first `?role=` chain read: re-created cell gives newer candidates and the older token, a save with it 409, the newer row kept; created cell gives no token, a save with the empty GUID 409; cleared cell gives `none`, no candidates, no token |
| V-23 | new (repair D2, Review 72440e28) | `ComplexityChainRaceTests.A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate` | a writer rotates the expired row's token before each sweep save: `SweepAttempts` saves, then `DbUpdateConcurrencyException` out of `FindActiveAsync`, row still active |
| V-24 | changed (repair D1, Review 72440e28) | `ComplexityEffectiveHttpTests.Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded` (independent reads); `OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover` (`chain metadata` cases) | a fault on the first `ComplexityChains` read of a role cell is `chain metadata read failed` with the stored candidates, no reason and no token; a fault on the second is `chain read failed` with no candidates |
| V-22 | new (repair D2) | `ComplexityChainCellEditor.test.tsx` `C1275 D2 an offline Save from an unread row says the queued write overwrites and sends no token` | queued note text equals `CHAIN_QUEUED_UNCONDITIONAL_NOTE`; one PUT without `concurrencyToken` after reconnect |
| R-1 | regression | `ComplexityChainHttpTests`, `ComplexityChainRoleHttpTests`, `ComplexityEffectiveHttpTests` | no-token callers unchanged (incl. `Put_twice_is_last_writer_wins_without_a_409`) |
| R-2 | regression | `ComplexityChainServiceTests`, `ComplexityChainRoleTests`, `AppDbContextModelTests` | service paths; model matches the hand-written migration snapshot |
| R-3 | regression | `MigrateStagePinsScriptTests` | migration apply/restore through the shared `ScriptedApi` (query stripped from the cell key, tokens kept out of `Cells`) |
| R-4 | regression | client files listed in CP-5 | routing tab, editor, model and settings page |
| R-5 | regression | `npm --prefix client run build`, `npm --prefix client run lint` | typecheck, production build, ESLint |
| R-6 | regression (S10) | `OrchestratorInstructionsRefreshTests` | the orchestrator snapshot reads the `?role=` cells through `GetEffectiveAsync`; its read-fault cases follow the new read order |

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutant | Expected red |
|---|---|---|
| PC-1 | `ThrowIfStaleAsync` returns without checking | V-1, V-2, V-3 |
| PC-2 | treat `Guid.Empty` as "no expectation" | V-3 (empty token over a row), V-11 server side via V-3 |
| PC-3 | drop `IsConcurrencyToken()` (and snapshot line) | V-17 (token PUT and DELETE save over the competitor), V-18 (no 409); V-4 update half only when the race window is hit |
| PC-4 | `IsLostRace` returns false | V-15, V-16, V-17, V-18 (DbUpdateException escapes instead of a re-run or 409); V-4 when the window is hit |
| PC-5 | effective view returns `answering?.ConcurrencyToken` regardless of `ResolvedFrom` | V-5 |
| PC-6 | do not rotate the token in `UpsertAsync` | V-1 (`current.ShouldNotBe(opened)`) |
| PC-7 | serialize `current` as the raw DTO (middleware options) | V-3 (`current.concurrencyToken` null omitted), V-1 enum names |
| PC-8 | script sends `$EmptyToken` for every cell | V-6, V-7 (`Set_puts_the_tier_provenance_and_candidates`) |
| PC-9 | client `staleComplexityChain` ignores `code` | V-8 |
| PC-10 | editor retries the PUT with `current.concurrencyToken` on 409 | V-9 (`bodies` length) |
| PC-11 | editor sends `chain.concurrencyToken ?? EMPTY` even when metadata is unread | V-11 |
| PC-12 | `queued` reads `isPending` instead of `isPaused` / note removed | V-12 |
| PC-13 | editor node moved back inside the list-success branch | V-13 |
| PC-14 | `RetriesLostRace` returns false (no re-run without a token) | V-15, V-16 first half, V-18 (`Saves` 1, not 3) |
| PC-15 | `RetriesLostRace` stops one attempt early (`attempt < UnconditionalAttempts - 1`) | V-18 (`Saves` 2, not 3) |
| PC-16 | expiry sweep does not rotate `ConcurrencyToken` | V-19 (token PUT saves onto the cleared row, no 409) |
| PC-17 | expiry sweep rethrows a lost race (catch removed) | V-20 (`DbUpdateConcurrencyException`) |
| PC-18 | queued note ignores the queued token (always `CHAIN_QUEUED_NOTE`) | V-22; the reverse (always the unconditional note) turns V-12 red |
| PC-19 | `GetEffectiveAsync` reads the cell's metadata and token after `LoadChainAsync` (the pre-repair order) | V-21 (token equals the re-created row's; no 409), V-24 |
| PC-20 | expiry sweep swallows the lost race on its last attempt | V-23 (no exception) |

V-4 depends on requests reaching the read-then-write window together and stays as a probabilistic
witness only; V-15 to V-18 commit the competing write from a save interceptor and are the
deterministic controls for PC-3 and PC-4 (repair D1). Not a PC: letting `RetriesLostRace` re-run a
write that sent a token is an equivalent mutant, because the re-read's stale check refuses it
before a second save (V-17 asserts one save either way).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S10 | `tests/Antiphon.Tests -> bin-c1275/` | chain-concurrency | `/*/*/(ComplexityChainConcurrencyHttpTests*)\|(ComplexityChainRaceTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-15, V-16, V-17, V-18, V-19, V-20, V-21, V-23 | 13 executed, 0 failed/skipped | 13 | 12 | false |
| CP-2 | S1-S10 | `CP-1` | chain-http | `/*/*/(ComplexityChainHttpTests*)\|(ComplexityChainRoleHttpTests*)\|(ComplexityEffectiveHttpTests*)/*` | R-1, V-24 | 9 executed, 0 failed/skipped | 9 | 8 | false |
| CP-3 | S1-S10 | `CP-1` | chain-service | `/*/*/(ComplexityChainServiceTests*)\|(ComplexityChainRoleTests*)\|(AppDbContextModelTests*)/*` | R-2 | at least 26 executed, 0 failed/skipped | 26 | 8 | false |
| CP-4 | S1-S10 | `CP-1` | chain-scripts | `/*/*/(ComplexityChainScriptTests*)\|(MigrateStagePinsScriptTests*)/*` | V-6, V-7, R-3 | at least 24 executed, 0 failed/skipped | 24 | 10 | false |
| CP-5 | S1-S10 | n/a | client-tests | `pwsh -NoProfile -File scripts/test-client.ps1 src/api/complexityChains.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/orchestrator/ComplexityChainPanel.test.tsx` | V-8, V-9, V-10, V-11, V-12, V-13, V-14, V-22, R-4 | 5 files (SettingsPage.test.tsx excluded: 2 failures inherited at base e7b6b80, Review 40767d91), nonzero count, 0 failed/skipped, `CLIENT TESTS EXIT CODE: 0` | n/a | 5 | false |
| CP-6 | S1-S10 | n/a | client-build | `npm --prefix client run build` | R-5 | typecheck and production build exit 0 | n/a | 4 | false |
| CP-7 | S1-S10 | n/a | client-lint | `npm --prefix client run lint` | R-5 | ESLint exit 0 | n/a | 4 | false |
| CP-8 | S1-S10 | `CP-1` | instructions-refresh | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-24, R-6 | 50 executed, 0 failed/skipped | 50 | 15 | false |

Red-first is by commit order (S1, S3, S5 precede their fixes); the red runs belong to Mutation.
