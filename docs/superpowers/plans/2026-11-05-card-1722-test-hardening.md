# CARD-1722 test-only timing hardening

Original Code/landing owner: 133255e3. Base: 3ce7f87ef06315ace62572d7e51b2de0347dbef9.
Only the two named test files change executable behavior. Scheduler item 3 is excluded.

V-1: Both zero-gap and 300 ms real-gap rapid-fire cases preserve the existing merged body,
one header, one correlation, three inbound members and one queue-owner assertions.
Inject a stationary FakeTimeProvider only into the debouncer; advance after all messages arrive.
V-2: A blocked inline syscall refuses within the unchanged one-second observation bound
at the fake three-second stall deadline, while its stream remains open until release.
R-1: The full bridge class preserves adjacent channel routing/delivery behavior.
R-2: The full receipt class preserves progress, overall-deadline and occupied-pool coverage.
R-3: All four named classification/slow registry guards execute.
R-4: Whole Unit lane executes. Unit alone omits delivery, landing, leases and persistence;
R-1 supplies the named affected integration coverage. No full assembly run is planned.

Red witnesses: commit the 300 ms rapid-fire variant before clock injection, then CP-1
must fail the unchanged merged-body oracle. Invoke the unchanged receipt method from
an isolated evidence driver with its only pool worker held for six seconds (the ff3ef62e
reproduction); require the exact five-second startup assertion, then run the same driver
after repair and require both ordinary and starved calls green. This manual driver and
its build are explicitly additional verification, not TUnit counts. Driver outputs remain
under /work/evidence/task-133255e3/. No production files or project graph inputs change.

PC-1 (pending SourceLanding Mutation): remove only the debouncer fake-clock injection;
run the exact rapid-fire method, require the 300 ms case red, restore exact test path, green.
PC-2a (pending): restore the old five-second startup guard, run the isolated starvation
witness and require its assertion. PC-2b (pending): make refusal await syscall completion;
the exact blocked-inline method must fail its unchanged strict one-second refusal assertion.
Mutation owns all deliberate mutant/red/restore/green cycles; Code supplies ordinary red-first
witnesses only. No repeats are required after green.

## Performance budget

Provisional shared Linux host budgets; CARD-1722 explicitly authorizes the 60-second
startup safety gate as an exception for worker starvation. Preserve the existing one-second
refusal path and five-second late-close guard. ff3ef62e measured receipt full-class wall
98.6-116.6 seconds, targeted ordinary maximum 416 ms, six-second starved reproduction.
f059674d measured rapid-fire real arrival gaps 194-244 ms exceeding the old 150 ms window.
New variant pays exactly two 300 ms scheduling gaps. Build budget 15 minutes cold,
focused method 2 minutes, bridge class 10 minutes provisional, receipt class 4 minutes,
guards 3 minutes. Unit cost is unmeasured on this revision; CARD-1573 documents that the
current lane can exceed 30 minutes. Bound the collection to one execution with a 35-minute
row budget and retain incomplete status if it cannot finish; CARD-1514 already tracks the
slow-lane investigation. Total execution cap 55 minutes; admission separately up to runner
configured slot limit, reported per row. No repeated samples or full assembly selection.
Assembly Postgres is shared; per-test existing isolation is retained. No new host setup.
TRX per-case elapsed/outcomes and checkpoint build/admission timing are retained. Internal
DB/IO phases without existing spans remain unknown, not claimed measured.

OS witness audit: no changed OS production branches, factories, optional inputs, startup
configuration or helpers. Receipt fixture continues its existing Linux-shaped fake IO;
existing native-only pool witnesses remain unchanged. Native Windows attribution remains
owed to the live qualification owner; no desktop work or restart is authorized here.
Restart: none. Next ordinary stage: Review, then caller lands original Code and commissions
method-scoped SourceLanding Mutation; this supersedes the brief's direct-land suggestion.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-card1722/` | merge-witness | `/*/*/ChannelBridgeTests*/Rapid_fire_same_sender_inbound_merges_within_window` | V-1 | all listed, 0 failed | 2 | 8 |
| CP-2 | all | CP-1 | bridge | `/*/*/ChannelBridgeTests*/*` | R-1 | all listed, 0 failed | 41 | 5 |
| CP-3 | all | CP-1 | receipt | `/*/*/CheckpointReceiptStallBudgetTests*/*` | V-2, R-2 | all listed, 0 failed | 6 | 2 |
| CP-4 | all | CP-1 | guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-3 | all listed, 0 failed | 4 | 2 |
| CP-5 | all | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-4 | >= 1 executed, 0 failed | 1 | 15 |
