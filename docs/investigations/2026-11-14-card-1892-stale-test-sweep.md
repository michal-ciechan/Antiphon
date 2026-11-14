# CARD-1892 stale delivery-contract sweep

Measurement in progress. No production or test files are changed by this task.

Baseline: current origin/master `b559bfba2e9a420495122d27171352fe9b7e1ba4`, which is also the assigned task branch base. CARD-1892 production changes are inventoried from `58309bad0^..15afa4846`, including the round-3 CARD-1905 attention closure.

The selection will union the impacted-classes selector, all capped reverse consumers of each changed production type, and delivery/fault vocabulary matches. All Linux rows run serially, under the host build-slot gate, using the repository TestDbFixture shared PostgreSQL and its isolated databases. Every row records its exact filter, result counts, duration, and /proc/loadavg. Pure timeouts are retried once alone. No live qualification or host is changed.

Full generated evidence stays ignored under `.antiphon/sweep15406/`.
