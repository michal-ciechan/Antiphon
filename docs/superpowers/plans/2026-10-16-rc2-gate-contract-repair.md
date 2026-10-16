# RC2 gate contract repair

Code task / landing owner: `8a76c085-3f35-48a9-8a0d-1f65e23ad4e7`.
Base: `b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d`.
Branch: `feat/card-task-8a76c085`. Targets after Review: `rc/2026-10-15` and `master`.

## Scope and attribution

The brief explicitly requests narrow red/green filters and no whole Unit lane;
that scoped Final selection overrides its generic appended profile. No assembly
run is planned. The existing three failing contracts are the primary proof.
The complete small `AbsentLaunchPolicyTests` class additionally protects the
runtime predicate while its inventory is updated. No delivery, landing, lease,
persistence or whole release-roster acceptance is claimed by these metadata and
pure policy tests.

`origin/master` at `59ed791fa4c51ce72c7adb8581a10a383575d1ad` has identical
registry, inventory, guard and classification-policy files; no fix is available
to cherry-pick. The Windows roster report at
`d1551e80ca8781c0e4c108c85902c5021f69f7c9` supplies independent previous-candidate
green / RC2 red. The fetched Linux report currently says execution pending.

Introducing RC commits (source attribution, not execution bisection):

- `f4770aa921e818cc94aef90c95ee6d5613c00e28` added `SessionRuntimeActionTests`
  without its own required reason. It uses isolated PostgreSQL and worker/commit
  fault fixtures, so add that reason without changing its Slow classification.
- `59544042d8937d5e97b266098f494acee1ea5ab1` added marked
  `SessionRuntimeActionDeferralTests` without registering it. Register its real
  queue, isolated schema, backoff and retention work with a reason.
- `b8893359f3c4b27e41c7934bfb229cd16e152191` added `ReviewThreadId`,
  `ReviewReplyConsumedAt`, `RuntimeActionKey`. These are review ownership,
  consumed-reply evidence and runtime-action correlation; require null.
- `6288f26fea0ded62206d74e4f5166d3482ce921a` added
  `DirectReplyPromptSequence`, `DirectReplyThroughSequence`,
  `DirectReplyPendingThroughSequence`, `DirectReplyProvider`,
  `DirectReplyConversationId`, `DirectReplyHandle`, `DirectReplyTailClosedAt`.
  These retain direct reply route, publication frontier and closure; require
  all seven null on a never-attempted brief.

All four commits are CARD-1259 changes. No inspected delta attributes these
three failures to CARD-1299. The intermittent landing-removal `deletion-sha`
failure remains unresolved and out of scope, as directed.

## Implementation

S1 commits ten argument cases before changing production. Each starts with an
accepted pristine brief, sets one new field, and requires refusal. Run the three
existing failing contracts plus the full small policy class; expect the three
reported failures and ten newly exposed missing predicate checks.

S2 adds the ten explicit inventory names and matching null requirements, adds
the two registry reasons and missing entry, and repeats exactly those filters.
Do not alter reflection equality, classification assertions, timeouts or retries.

## Verification design

- V-1: `Registry_matches_compiled_metadata` accepts all compiled Slow metadata
  with a distinct reason for every registration.
- V-2: `C487_G059` has no unregistered marked classes.
- V-3: `C1149_Queue_columns_are_accounted_for` exactly matches entity properties.
- R-1: every `C1149_Whitelist_requires_every_fact` argument retains the pristine
  positive companion and refuses its changed fact, including all ten new fields.
- Manual M-1: inspect the four introducing diffs, current master and unchanged
  guard assertions; confirm each added field/registry reason above.

No new PC execution belongs to Code. SourceLanding Mutation must consider
PC-1 variants (remove each of the ten null clauses independently, exact method
`C1149_Whitelist_requires_every_fact`), PC-2 (remove the deferral registration,
`Registry_matches_compiled_metadata`), PC-3 (remove the new reason for
`SessionRuntimeActionTests`, same guard method), and PC-4 (remove one new
inventory name, `C1149_Queue_columns_are_accounted_for`). All remain pending;
Mutation owns mutant design, missing-control discovery and red/restore/green.
The unbound candidate is [the Mutation handoff](../mutations/2026-10-16-rc2-gate-contract-repair.md).

S1 red evidence: committed `a562ec689b60f62ae9976e82584f0bc164301960`, checkpoint
run `20261016-084234-1d65`. CP-1: 1 failed; CP-2: 1 failed; CP-3: 87 executed,
76 passed, 11 failed (inventory plus exactly the ten added field arguments).
No skips. Every new case reached the expected false-versus-true assertion after
its positive companion passed. All rows have clean source, verified build
provenance, `slot=granted waited=0s`; the tool exited 1 and deleted its red output.
This is the intentional red baseline, not a passed gate. A transient owner-read
HTTP 502 after the rows did not alter the recorded RED result. The tool bootstrap
succeeded with two warnings and no errors; an initial invocation used a nonexistent
`net9.0` subdirectory and exited 1 without starting a checkpoint/build/test, then
the built DLL at the actual OutputPath root launched this run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-rc2-red-8a76c085/ | red-registry | /*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata | V-1 | all listed, 0 failed | 1 | 4 |
| CP-2 | S1 | CP-1 | red-classification | /*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059 | V-2 | all listed, 0 failed | 1 | 1 |
| CP-3 | S1 | CP-1 | red-queue-policy | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/* | V-3, R-1 | all listed, 0 failed | 87 | 1 |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-rc2-green-8a76c085/ | green-registry | /*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata | V-1 | all listed, 0 failed | 1 | 4 |
| CP-5 | S2 | CP-4 | green-classification | /*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059 | V-2 | all listed, 0 failed | 1 | 1 |
| CP-6 | S2 | CP-4 | green-queue-policy | /*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/* | V-3, R-1 | all listed, 0 failed | 87 | 1 |

S1's nonzero exits are intentionally recorded as red, never accepted as green.
Pass committed HEAD as `--expected-source-sha`, use `--serial`, inspect fresh TRX
for every intended method/variant, and validate S2's clean receipts. The tool
owns host build-slot leases. Its bootstrap build is the sole unlisted build,
through `scripts/build-slot.ps1`, needed to execute this manifest. Record all
actual counts and unedited CHECKPOINT lines in `.antiphon/task-8a76c085.md`.

### Cost

Ordinary checkpoint estimate: 12 minutes including two isolated builds; authoring,
attribution and reporting fit within the brief's 45-minute budget. No repeated
proof after green. No service restart is performed during Code. Restart: server
for activation after the caller lands/deploys this production policy repair.
