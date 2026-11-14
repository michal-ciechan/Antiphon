# CARD-1892 stale delivery-contract sweep

Measurement in progress. No production or test files are changed by this task.

Baseline: current origin/master `b559bfba2e9a420495122d27171352fe9b7e1ba4`, which is also the assigned task branch base. CARD-1892 production changes are inventoried from `58309bad0^..15afa4846`, including the round-3 CARD-1905 attention closure.

The selection will union the impacted-classes selector, all capped reverse consumers of each changed production type, and delivery/fault vocabulary matches. All Linux rows run serially, under the host build-slot gate, using the repository TestDbFixture shared PostgreSQL and its isolated databases. Every row records its exact filter, result counts, duration, and /proc/loadavg. Pure timeouts are retried once alone. No live qualification or host is changed.

Full generated evidence stays ignored under `.antiphon/sweep15406/`.

## Published interim findings

The uncapped reconstruction has 272 distinct production consumers (257 are absent from the prior Code evidence's named class list). Adding fault/outcome vocabulary and Pending-plus-fault matches yields 472 Linux project/class selections and nine Windows-only selections. These are a census, not a passing claim. The diagnostic copy only raises the index builder's cap/serialization limit in ignored evidence; tracked test/product files are unchanged.

| Family | Executed | Passed | Failed | Skipped | Wall seconds | Load start/end (1/5/15 min) | Classification |
|---|---:|---:|---:|---:|---:|---|---|
| LandRefusalDeliveryWitnessTests | 24 | 23 | 1 | 0 | 168.516 | 14.16/15.68/15.86 -> 27.78/19.59/17.19 | Known STALE-CONTRACT, C1196_Pretyping_transport_failure_recovers_the_same_row; Backlog CARD-1922, repair Debug b4f39ca7 |
| CodexCliObservationGapTests | 7 | 6 | 1 | 0 | 409.265 | 27.78/19.59/17.19 -> 26.73/24.75/20.67 | Known STALE-CONTRACT, C1029_Unobservable_timestamp_floor_is_original; Backlog CARD-1923, repair Debug 31c683a8 |
| SessionMessageQueueWedgedHeadTests | 62 | 62 | 0 | 0 | 161.778 | recorded in ignored results.jsonl | Passed |
| SessionMessageQueueCodexPointerTests | 28 | 28 | 0 | 0 | 119.060 | recorded in ignored results.jsonl | Passed |
| SessionMessageQueueDeliveredSpillRecoveryTests | 5 | 5 | 0 | 0 | 169.167 | recorded in ignored results.jsonl | Passed |

After these complete rows, the measurement driver was stopped at the next row boundary before launching another test. No test process was interrupted. Continued selections group related class families to amortize repeated discovery/fixture startup, retain serial execution and the ten-minute row ceiling. Execution remains in progress; this interim report does not establish an exhaustive list of remaining failures.
