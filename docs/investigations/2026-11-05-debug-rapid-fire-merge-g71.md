# Group 71 rapid-fire inbound merge investigation

## Outcome

The failing assertion has a real-clock precondition that the test does not establish: all three messages must reach `ChannelInboundDebouncer.AddAsync` within its 150 ms quiet window. Retained failing rc and master traces prove that this precondition was false. This is a test timing defect, with no demonstrated production merge regression. Recommend a test-only controlled debounce clock; do not widen production windows or assertion deadlines.

Task: `f059674d`; host: Linux server2 runner mirror. The Windows qualification `b19a9880` was not touched. No product code, tests, project files, build properties files, graph inputs, live services or deployment refs were changed.

## Resume harvest

The predecessor `/work/worktrees/task-8592d536` was inspected read-only before investigation: HEAD `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`, no commits beyond that base, empty staged/unstaged diffs, and no untracked files. No patch or source change existed to salvage; no inherited mutant existed to validate. Ignored run evidence was found separately under `/tmp/cb-*-8592d536-*` and copied to this task's ignored evidence directory. Neither the old worktree nor its `/tmp/antiphon-master-8592d536` checkout was modified or used to launch a new process.

The old task is harvested and may be cancelled. The prior author's assertions are not used as evidence: each retained TRX was parsed, selections and nonzero counts checked, build logs inspected, clean source checkouts confirmed, and both retained test DLLs checked for their embedded source revisions. Binary hashes and provenance are retained. These are investigation measurements, not new rc qualification receipts.

## Revisions and evidence

- Rc: `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`.
- Inherited master sample: `cc8845f030d934507e7a0d2db7d939be06d9f2bc`.
- Master pinned after fetch: `28cdefab326782f2c91ca361768a7252dc875ec7`. The delta from the inherited master changes only two generated landing-graph manifest/evaluation files; channel bridge, debounce, queue and this test are unchanged.
- CARD-1716 branch tip: `05da67f7853cad8c5c3726a29173976e1b5017a8`; implementation: `97011c425`. It is not an ancestor of the pinned master.

Raw evidence was collected under `/work/worktrees/task-f059674d/.antiphon/g71-evidence/` and archived after all foreground sessions joined as `/tmp/antiphon-f059674d-evidence.tar.gz`. It contains original copied logs/TRX/HTML, per-attempt CSV summaries, decoded failing HTML trace payloads, SQL timelines, source/binary hashes, fresh build/run logs and reproduction drivers. The archive was verified by reading its complete member list. Generated evidence is not committed; essential conclusions and counts are preserved here. The unpacked evidence/results, this task's native `bin`/`obj` outputs and its owned master source extraction are removed at settlement. The protected predecessor's paths remain untouched.

Archive SHA-256: `38c04e43bb0ed3fd7c493160ca3db9f6e2ef309eb04387f9501a70e42bff2721`.

## Runs

Only `ChannelBridgeTests*` and the specifically failing method are relevant: the invariant is same-sender batching and durable queue ownership. No full suite or unrelated namespace was selected. Existing deterministic debounce unit tests were read, not rerun as a substitute for the bridge selection.

Reproduce one class invocation from a native build of the desired source revision on server2:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label g71-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label g71-class -- timeout 5m dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/ChannelBridgeTests*/*'
```

For the isolated witness replace the filter with `/*/*/ChannelBridgeTests*/Rapid_fire_same_sender_inbound_merges_within_window`. The archived drivers add unique TRX/phase paths and retain every repetition's exit status. Do not run the desired revision by changing the assigned task branch.

| Selection | Revision | Attempts | Results | Provenance |
|---|---|---:|---|---|
| `/*/*/ChannelBridgeTests*/*` | rc | 20 | 800 passed, 0 failed; 40 tests each; target passed 20/20 | Validated predecessor TRX and embedded rc DLL SHA |
| Exact rapid-fire method | rc | 20 | 19 passed, 1 failed; attempt 1 at merged-body assertion | Validated predecessor TRX/HTML, embedded rc DLL SHA |
| Exact rapid-fire method | inherited master | 20 | 19 passed, 1 failed; attempt 8 at merged-body assertion | Validated predecessor TRX/HTML, embedded master DLL SHA |
| Exact rapid-fire method, fresh build | rc | 1 | 1 passed, 0 failed | Own gated build, TRX, HTML and phase journal |
| `/*/*/ChannelBridgeTests*/*`, fresh build | pinned master | 20 | 800 passed, 0 failed; 40 tests each; target passed 20/20 | Own gated native build, pinned source archive, TRX, HTML and phase journals |

The inherited rc class batch took 2,189.04 seconds of summed test-process wall time (36.48 minutes), plus admission/driver overhead; 03:47:25–04:31:17 UTC overall (43.87 minutes). This exceeds 30 minutes and is recorded in this Debug investigation rather than treated as a hang. Class process p95 was 120.995 seconds; rapid-fire method p95 within the warmed class was 1.231 seconds (maximum 1.338 seconds). The isolated failing method durations were 20.483 seconds on rc and 22.068 seconds on master, including harness initialization. The inherited focused batches summed to 590.78 and 528.03 seconds of process wall time respectively. Unknown fixture/external-call phases remain unknown; no zero cost is implied. Embedded HTML spans retain measured lifecycle/database phases, but the predecessor hit TUnit's 100-external-span cap, so later external calls are incomplete.

Fresh direct builds and no-build test drivers use `scripts/build-slot.ps1`, native default output, `UseAppHost=false`, and `TEMP=/tmp`. Rc compiled in 12:04.24 (slot wait zero), master in 9:14.35 (slot wait 180 seconds); both builds succeeded with zero errors. Host load was approximately 73 on 24 visible CPUs during compilation. The reproduction driver bounds each execution at five minutes (conservative rounding above approximately twice the inherited 121-second class p95), separately from a five-minute slot-admission bound. No slot timeout is bypassed. Fresh master is an exact `git archive` extraction under an owned `/tmp` directory, not a branch switch; its assembly revision is `unknown` because there is no `.git`, so source provenance is established with the pinned archive/checksum instead of an invented embedded SHA. This is Debug evidence, not a qualification build.

Master repetitions 1–3 used individual leases. Repetitions 2 and 3 paid 60 and 165 seconds in admission alone. After repetition 3 and its lease finished, the original loop was deliberately stopped before another test could start (driver exit 137, documented separately from test verdicts). A replacement pending 4–20 admission was cancelled before acquiring a lease or starting a child (exit 143). Remaining work was partitioned into two supervised foreground no-build batches, 4–12 and 13–20, each with its own lease, distinct result/phase paths and a separate PostgreSQL container per test process. This class uses fake adapters and no PTY/real-runner children; source and binary outputs remain frozen. No builds occur inside either batch. Both sessions are joined before cleanup. The driver transition and cancelled admission remain in the evidence rather than being counted as failed or passed tests.

Both split batches' first admission attempts timed out after five minutes with exit 4 and launched no tests. They remain recorded as NOT RUN, never passing tests. One further fully gated attempt for each missing selection acquired leases after 195 and 165 seconds; no execution was unleased. Concurrent repetitions 4 and 13 report distinct PostgreSQL containers `30180edfbd2f` and `c6f47dddd1a2`. The 13–20 batch exited 0 and released its lease after 974 seconds. These admission costs are separate from the counts and test-process durations.

The fresh master class sample completed 04:45:44–05:26:15 UTC, 40.52 minutes of wall time including all pauses/admissions; summed process wall time was 2,408.67 seconds (40.14 minutes, with parallel overlap). Process p95 was 140.635 seconds, maximum 143.598 seconds; the rapid-fire target p95 was 1.238 seconds, maximum 1.265 seconds. All 20 target results passed. The 4–12 batch also exited 0, releasing its lease after 1,103 seconds. Both local foreground sessions were joined before archiving or removing outputs. The two initial admission timeouts and deliberately cancelled drivers remain historical costs, not test results. All required class repetitions are now complete; the known isolated assertion failures remain unresolved until the test-only Code repair.

## Root-cause evidence

`Rapid_fire_same_sender_inbound_merges_within_window` constructs three messages, then awaits `h.Bridge.HandleInboundAsync` three times sequentially. Constructing all envelopes together does not cause them to arrive at the debouncer together. Each call first commits its acceptance/catalog transaction, publishes a catalog event, resolves the agent, acquires the agent wake claim, reads pending members, resolves the running session and reads the channel. Only then does it call `AddAsync`. `HarnessAsync` registers `TimeProvider.System` and configures a 150 ms debounce. A concurrent real-time timer can flush while the next call performs that work.

The following conservative bounds come from Npgsql spans in each failing HTML report. A message's `AddAsync` occurs after its final channel-read response and before its following advisory-unlock call. No production or test instrumentation was added.

| Failing run | First Add bounded by test-body offsets | Second Add bounded by offsets | Minimum gap |
|---|---|---|---:|
| Rc focused attempt 1 | 15,406.80–15,418.64 ms | 15,612.65–15,613.07 ms | 194.01 ms |
| Master focused attempt 8 | 18,853.08–18,900.65 ms | 19,144.63–19,149.35 ms | 243.98 ms |

Both minimum gaps exceed 150 ms. The master third Add is also at least 179.14 ms after the second. Thus multiple lane flushes are permitted by the specified quiet-window behavior. The retained rc SQL records show two queue-owner inserts, one owning two inbound members and one owning one; master shows three separate owner inserts. Both failures reach the exact `body.ShouldContain("] line one\nline two\nline three")` assertion, with actual text beginning with the queue's `[Chat messages since your last reply - for context]` batch wrapper. That wrapper is the defined format when separately queued channel notes are delivered together; it is not a single merged envelope. The Windows symptom, a first submitted body containing only `line one`, is another permitted scheduling outcome once the initial lane expires.

The trace proves elapsed gaps, not their complete attribution to CPU scheduling versus JIT/query compilation or I/O. Individual retained SQL operations around those boundaries are short; managed gaps and cold initialization are visible. No CPU stress was necessary to discover a failure in isolation. Passing warmed class repetitions do not disprove the isolated failure.

An independent rerun from this task's freshly built rc source passed 1/1. Its first-to-second Add gap was bounded above by 79.95 ms, and its second-to-third gap by 32.44 ms; the SQL trace then shows one queue-owner insert updating all three inbound members. The higher diagnostic span limit (1,000) retains that complete handoff. This contrasts a valid within-window sample with the two retained failures whose minimum gaps exceeded the window. It does not erase those failures.

The harness constructs `ChannelBridgeService` but never starts its hosted `ExecuteAsync`; this test calls `HandleInboundAsync` directly. Its wake worker therefore does not run. A race with that worker cannot explain this reproduction. `ChannelInboundDebouncer` measures `LastAt` at `AddAsync`, not the earlier envelope timestamp or acceptance time, and flushes once the quiet window elapses. This matches its documented contract and existing fake-clock unit tests.

The test explicitly compresses the quiet window to 150 ms; `ChannelBridgeSettings` defaults it to 500 ms. The finding is about the test's unmet timing assumption, not evidence that production's default window should be changed. The constructed messages share provider, conversation and author ID `1001`, so a differing debounce key is not the explanation.

## Master, regression history and CARD-1716

Rc and pinned master have byte-identical `ChannelBridgeService.cs`, `ChannelInboundDebouncer.cs`, `ChannelBridgeTests.cs` and `ChannelBridgeSettings.cs`. The only main queue-file difference is an optional lock-wait argument on `EnqueueSupervisionNoticeAsync`, unrelated to channel batching. History since the rc cut shows no bridge/debouncer/rapid-fire-test change on master; completion/land notification queue-lock fixes do not change this path. The same invalid test assumption exists on master, and the inherited master failure demonstrates it at runtime.

CARD-1716 limits queue-lock waits for an open wake-worker pass and adds pass/stall observability. Its direct `HandleInboundAsync` path passes no `WakePass`; `FlushLaneAsync` therefore chooses a null lock bound, preserving the existing wait. The debounce clock, quiet window, lane key and this test's sequential database work remain unchanged. It neither causes nor fixes this test timing defect. This conclusion is source analysis of that branch, not a claim that its complete branch was executed here.

## Smallest repair and handoff

Change only the rapid-fire test and its private harness: let the debouncer use an injected `FakeTimeProvider`, keep that clock stationary through all three awaited inbound calls, advance it beyond the quiet window only after all three are buffered, and await observable completion before checking the existing one-body, one-header, one-correlation and three-members/one-owner assertions. Prefer injecting the clock specifically into the debouncer so queue-delivery clock behavior stays unchanged. Preserve all existing assertions. Do not merely increase 150 ms, the five-second observation deadline, or add retries; those retain the same unproven timing precondition.

A Code task should supply the test-only red-first witness and method-scoped positive control, then the exact bridge class checkpoint. No repair or mutant was authored in this Debug task, as instructed. This investigation does not authorize a land or mark the known test failures green.

## Item classification

| Item | State | Basis |
|---|---|---|
| Harvest predecessor work | CLOSED | No source patch; ignored evidence copied and independently parsed |
| Twenty rc class repetitions | CLOSED | Twenty nonzero 40-test TRX receipts, 800/800 passed, embedded rc SHA |
| Twenty master class repetitions | CLOSED | Twenty fresh nonzero 40-test TRX receipts, 800/800 passed; pinned source archive |
| Explain rc and master failures | CLOSED | Measured Add intervals exceed 150 ms; same assertion fails on both |
| Exclude wake-worker explanation for this test | CLOSED | Hosted worker is never started |
| Determine CARD-1716 effect | CLOSED | Direct path remains pass-null; no debounce change |
| Propose smallest repair | CLOSED | Test-only controlled debounce time; implementation intentionally deferred |
