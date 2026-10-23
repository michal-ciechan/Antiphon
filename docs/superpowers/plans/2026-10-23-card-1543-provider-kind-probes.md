# CARD-1543 provider-kind metadata reuse

Code and landing owner: dbf0540f-0274-4d61-85e4-e2c679c61895.
Base: ffb1031fab448cc09929f8142b41f4fc46c923c9.
The card is the specification. The explicit brief selects minimal verification,
overriding the generic Final Unit/full-class profile. No full assembly run.

## Change and verification

Carry the admitted session kind through canceled-body, late-confirm, held-back
and pointer checks within DeliverNextLockedAsync. Other callers retain lookup
fallback, except canceled-body/held-back expectation callers that already have
the admitted session. No dialog observation caching, generation-capture removal,
runtime body/Enter check changes, revision-fencing changes, receipt changes,
no-kill/no-launch assertion changes or timeout widening.

V-1: tighten three exact SQL budgets from 71/67/67 to 67/63/63. CP-1 must fail
these assertions before implementation; CP-2 must pass. Inspect complete rosters:
four eliminated kind-only statements; both fresh runtime provider/generation
reads and independent watchdog read remain.
V-2: CP-2 pointer recovery variants preserve pending/interrupted/send-now/canceled/
capped/expectation holds, complete UserPrompt authority and generation changes.
R-1: CP-2 dialog tests preserve pending admission, automated write fencing and
both absent/Unknown recovery refusal arguments.
R-2: source audit for unchanged generation/dialog/runtime writes and timeouts;
evidence guard base..HEAD, remote tip and merge-tree audit.
PC-1..PC-4 and variants remain pending for SourceLanding Mutation in
 docs/superpowers/mutations/2026-10-23-card-1543-provider-kind-probes.md.
Restart: server; runner none. No AppHost path or OS branch changes. Production
optimization, not an rc qualification fix; no scratch rc pick is commissioned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | witness | `tests/Antiphon.Tests -> bin-c1543-red2/` | exact-budget-red | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | V-1 red witness | 3 expected budget assertion failures; other arguments green | 14 | 2 | true |
| CP-2 | implementation | `tests/Antiphon.Tests -> bin-c1543-green2/` | exact-budget-green | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | V-1 | 14 results, no failure/skip | 14 | 2 | true |
| CP-3 | implementation | `CP-2` | pointer-holds | `/*/*/SessionMessageQueueCodexPointerTests/C1521_Recovery_never_stacks_a_pointer` | V-2 | 6 variants, no failure/skip | 6 | 1 | true |
| CP-4 | implementation | `CP-2` | pointer-receipts | `/*/*/SessionMessageQueueCodexPointerTests/C1521_Recovery_reconciles_before_input` | V-2 | 8 variants, no failure/skip | 8 | 1 | true |
| CP-5 | implementation | `CP-2` | dialog-fencing | `/*/*/ClaudeDialogDeliveryTests/*` | R-1 | 2 methods, no failure/skip | 2 | 1 | true |
| CP-6 | implementation | `CP-2` | unknown-refusal | `/*/*/DelegationBriefRecoveryTests/C1541_Resume_keeps_brief_pending_without_clear_dialog_observation` | R-1 | absent and Unknown, no failure/skip | 2 | 1 | true |

## Performance budget

One isolated build per distinct committed source slice (red and fix), one run
per row. Tool bootstrap is an additional justified build through build-slot.ps1:
this checkout has no checkpoint binary. No repetitions after green. Provisional
row execution budget 4 minutes (sample pending this task); build budget 10 minutes;
total run bound 20 minutes. Historical 381-case evidence is in the combined
CARD-1529/1541/1542 plan; no measured p95 exists for this narrower selection.
Importer minimum timeout is an existing exception tracked by CARD-1404/1563;
use explicit 10-minute row/build cap for this sample and never widen it. Queue
admission is separately finite via broker. Task budget 60 minutes. Shared
TestDbFixture starts once per run with per-test schema isolation; no fixture
changes. Retain TRX durations, slot waits, build/row wall times and SQL rosters in
ignored evidence. Unavailable fixture/external phase attribution remains unknown
under CARD-1514. Category: server/messaging; downstream Application/API through
existing test references. No runner/git changes or unbounded class selection.

The first launcher used bare timeout values, parsed as seconds, and ended before
tests (run 20261023-200114-149a). No red witness is claimed from that run. Correct
the CLI units to the already documented 10m/20m budgets. Correct the counted
argument roster to 14 budget cases and 30 combined cases. Use a fresh red output
root; preserve the failed build evidence.

CP-1 red witness 20261023-200251-c72b at
28c569ae2fbbd0792a758c147d56aa609aadae92: 14 executed, 11 passed, three
expected assertion failures (actual 71/67/67 versus target 67/63/63), no skips.
Clean source and verified build. Build 304.796s, host 54.686s; slot waits zero.
The green row adds both absent/Unknown refusal cases (32 total) for the card
contract; no test/fixture behavior or timeout changes.

CP-2 run 20261023-201000-3ff9 at 5a687844d9086cae46edb3152d739c98b7260015
built clean/verified but the composed class/method OR filter selected zero tests.
This is incomplete execution, never green. Split the same intended 32 cases into
CP-2..CP-6 using exact class/method filters (CP-5 is the two-method full class),
with a single shared fresh build. Keep the original 10m/20m caps. The repeated
build is required by the new committed manifest/source provenance; no test has
passed in the implementation slice yet and none is being repeated after green.
Each row is one assembly invocation with shared TestDbFixture within that run.
The selected five row estimates total six minutes; build remains ten minutes.
