# CARD-1452 C100 migration pin: predecessor, not newest

Date: 2026-10-22. Code task c82c4495 (Final round, minimal testing: no Unit lane). Test-only change;
no product code moves.

`CardRelationshipSchemaTests.C100_Duplicate_pair_is_refused_by_the_unique_index` asserted that the
last two migrations returned by `GetMigrations()` end with `_AddCardRelationships` and
`_AddCardCleanupScheduling`. That pinned the newest migration as of 2026-10-14, so every later
migration turned the test red (`but was 20261020142734_AddStandingAutomaticFreshRecovery` at the
task base and on `rc/2026-10-20`).

## Slice S1 (one commit group)

1. `tests/Antiphon.Tests/Application/CardRelationshipSchemaTests.cs`: locate
   `_AddCardRelationships` by name, require a positive position and pin its predecessor to
   `_AddCardCleanupScheduling` (the CARD-0100 ordering intent, in the shape
   `CardCleanupSchedulingSchemaTests` and `ChannelOutboundDurabilitySchemaTests` already use).
   Add a model assertion that `IX_CardRelationships_CardId_RelatedCardId_Kind` is the unique index
   over `CardId, RelatedCardId, Kind`. The pending-migration, pending-model-change, duplicate-pair
   refusal (`UniqueViolation` on that constraint name) assertions are unchanged.
2. Census of the same anti-pattern across `tests/` (`migrations[^n]`, `.Last()` on the migration
   list asserted against a name, `ShouldEndWith("_...")` on the newest element): no other instance.
   `CardVerificationPolicyTests` compares `GetAppliedMigrationsAsync().Last()` to the migration it
   just downgraded to, which is correct; the `SessionRuntimeAction*` tests use `TakeWhile(...).Last()`
   to find a predecessor, which is also correct.

## Verification design

Red witness (one unlisted run, base source `e1e3beb5c60c44335c0cb4482d78e966adf1d1de`, isolated
`bin-c1452-red/` build through `scripts/build-slot.ps1`): filter
`/*/*/CardRelationshipSchemaTests/C100_Duplicate_pair_is_refused_by_the_unique_index` executes 1
and fails 1 at the `migrations[^1].ShouldEndWith("_AddCardRelationships")` line. The same method is
green inside CP-1 after S1.

| ID | Invariant | Evidence |
|---|---|---|
| V-1 | `AddCardRelationships` immediately follows `AddCardCleanupScheduling` in the migration list, the model carries the unique pair index, and a duplicate pair is refused by `IX_CardRelationships_CardId_RelatedCardId_Kind`, whatever migration is newest. | CP-1 |
| R-1 | The self-reference check constraint and the cascade-on-delete cases in the same class keep their outcome. | CP-1 (whole class) |

Positive controls: none planned. The change is test-only; deliberate mutants and missing-control
discovery stay with SourceLanding Mutation (zero-PC plan).

Lane: portable .NET/PostgreSQL on the eligible default runner. No `-Runner`, no `-Platform`.
The class carries no platform skip, so the whole-class filter runs every case and the receipt
validates.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1452/` | card-relationship-schema | `/*/*/CardRelationshipSchemaTests/*` | V-1, R-1 | all 3 methods, 0 failed/skipped | 3 | 8 |

### Cost

Ordinary floor 8 minutes (CP-1) plus the unlisted red witness, the checkpoint tool bootstrap
build and the rc/2026-10-20 scratch check described in the Code report.

## rc/2026-10-20 check (Code report records the outcome)

In a discarded scratch worktree: cherry-pick the S1 commit onto `origin/rc/2026-10-20` and report
conflicts; at the unmodified rc tip run one narrow unlisted run of the red-witness filter and report
whether the same red exists there.
