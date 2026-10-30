# C514 three-prompt PTY Debug, 2026-10-30

Task `dfd3f49c`, branch `feat/card-task-dfd3f49c`, inherited tip
`126b5e4e5124a1bbf69f7ef5dbac00cb73d1a5ac`.

Investigation in progress. No acceptance or regression classification yet.

The original Debug's pre-fix base is
`80b3a59c40cd18dea905829e428d35844cb89b8d` (parent of `b65503281`).
Its PTY helper skips Linux and resolves only fakeclaude.exe. For a meaningful
Linux comparison the detached scratch base receives exactly the inherited tip's
platform-enabling helper delta (Linux admission and extensionless child path),
without the delivery fix or any changed test assertion. This instrumentation
must be reported rather than calling the base source pristine.

Measurements: exact three-prompt method, 30 native TUnit repetitions per source
alone followed by 15 under an owned concurrent build. The repetition properties
use the existing CheckpointRepeat.targets mechanism. Shared process repetitions
are not independent fresh-host trials. The historical Review edc0f0b6 and round-2
report establish the flake and justify exceeding the normal repeat-proof budget.
Every driver takes a build slot; use TEMP=/tmp, UseAppHost=false, isolated bin
outputs. Raw console/TRX/load evidence stays ignored under
`.antiphon/c514-three-dfd3f49c/`. No native Windows work is authorized here.

The requested `.antiphon/task-5e08c4d4.md` is absent in this checkout, its original
worktree, and the remote branch. The committed investigation and full edc0f0b6
review were read instead; the round-2 report is present and was read in full.

## Measurement outcome

The three-prompt symptom is a PRE-EXISTING FIXTURE FLAKE, reproduced on the
original pre-fix product and the inherited tip. Alone the observed rates are
identical; loaded samples differ by one failure and do not establish a rate
change. No stronger statistical equivalence claim is made.

| Condition | Total | Passed | Failed | Failure rate | 1-min load range |
|---|---:|---:|---:|---:|---|
| base-alone30 | 30 | 26 | 4 | 13.3% | 11.3–28.7 |
| tip-alone30 | 30 | 26 | 4 | 13.3% | 4.96–20.28 |
| base-loaded15 | 15 | 13 | 2 | 13.3% | 22.03–52.37 |
| tip-loaded15 | 15 | 14 | 1 | 6.7% | 26.69–65.59 |

All eleven failures are the same 30-second persisted-prompt wait. Base alone:
two stop after alpha, two after bravo. Tip alone: three after alpha, one after
bravo. Both base loaded failures and the one tip loaded failure stop after bravo.
The SUBMITTED prefixes contain no duplicate or reordered body. Captured child
JSONL includes the last accepted prompt's completed assistant record/end_turn;
subsequent bodies have never been submitted. Per-failure first lines are below.

The fixture pump saves the child's UserPrompt separately from the later assistant
record/TurnEnd. Enqueue returns after confirming UserPrompt, so a subsequent enqueue
can correctly see Working while the TurnEnd is not yet committed. The single
explicit flush can see the same state. Production has a deferred turn-end queue
worker; this fixture's pump only saves transcript rows and has no such worker.
Once its final flush is withheld, passive waiting cannot advance the remaining
queue. This is a missing fixture wakeup, not a need for a longer deadline or a
changed Enter refresh. A deterministic held-TurnEnd witness is the next step.

All case intervals in both loaded samples overlap an owned build. The base load
used one successful nonincremental server build (160.79 s); the tip used two
successful builds (175.99 s and 53.22 s), with a 57 ms gap. The test supervisor and
load supervisor each held their own granted build slot (cap 6); load builds used
maxcpucount:2. The base loaded condition also overlapped the end of the owned tip
measurement build. Ambient host load was recorded, not controlled or inferred.
Every driver and child was joined. Build logs retain admission and execution
separately; per-case TRX and phase JSONL remain in the evidence root. Uninstrumented
per-test DB/IO/startup subphases are unknown, not zero. The assembly fixture records
its DB creation/migration spans. These are direct slot-gated runs, not validated
checkpoint receipts or a claim of whole-assembly/native qualification.

The original streamed-driver summaries contain two empty completion placeholders
alongside each process record. `measurement-metadata.json` deterministically retains
only PID-bearing records; TRX counts and original summaries are preserved. Actual
load build counts come from `load-builds.json`, not the console's placeholder count.

## Every measured failure (verbatim first line)

### base-alone30 — 2026-10-30T18:05:00.6455977+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
```

### base-alone30 — 2026-10-30T18:06:10.3660938+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
```

### base-alone30 — 2026-10-30T18:06:59.2489010+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```

### base-alone30 — 2026-10-30T18:07:45.9481722+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```

### tip-alone30 — 2026-10-30T18:10:05.5714234+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```

### tip-alone30 — 2026-10-30T18:11:07.4160689+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
```

### tip-alone30 — 2026-10-30T18:12:02.8997292+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
```

### tip-alone30 — 2026-10-30T18:12:53.6911096+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
```

### base-loaded15 — 2026-10-30T18:19:35.1797883+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```

### base-loaded15 — 2026-10-30T18:20:09.4172780+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```

### tip-loaded15 — 2026-10-30T18:23:03.6696732+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
SUBMITTED:/remote-control
SUBMITTED:c514 lane shared prefix alpha
SUBMITTED:c514 lane shared prefix bravo
```


## Deterministic witness and fixture repair

Red witness commit `d4a293992` compiled successfully and executed exactly one real
UnixPty/fake-Claude case. It failed its required receipt assertion after 36.597 s,
not during build/setup. The first real assistant-record commit was held while the
later bodies were enqueued and the legacy single flush ran. Before release the
production working-state query was asserted Working and the child held only alpha.
After release, the final failure diagnostic proved persisted sequences 1 UserPrompt,
2 AssistantText, 3 TurnEnd/end_turn for alpha, with alpha Sent/Delivered (one
attempt), bravo Pending (zero attempts), charlie Pending (zero attempts). The only
SUBMITTED body was alpha. Evidence: `witness-red/results.trx`, console, timing,
child JSONL and source-bound build log under the evidence root.

The repair is confined to tests. `DrainQueuedPromptsAsync` drives the production
`FlushIfIdleAsync`, which reads production persisted Working state and applies the
normal queue/receipt/custody gates, while waiting for the complete native receipts.
The original 30-second deadline, 150 ms polling interval, real modal/one-Esc setup,
exact ordered child-file and persisted-body equality, and exactly-once assertions
remain. No longer sleep, assertion relaxation, invented UserPrompt or idle row was
added. The new before-save seam forwards the pump's existing optional hook and
forces the formerly intermittent ordering. Diagnostics retain child, transcript,
queue verdict/attempt counts and raw output on failure.

No product source or arm refresh changed. The finding does not require replaying
an ambiguous Enter; unknown/observation-unknown remain terminal. Earlier mutation
results are retained in the round-2 report; fresh final-fixture controls and five
portable repetitions remain pending at this repair commit. Native Windows remains
OWED; this Linux fixture diagnosis does not qualify the original native arm failure.
## Subsequent portable result: separate health-fixture writer race

The first five-repeat portable selection at `1c0eb2f13b58dae675c38ba7732b2d107e38ff0c`
executed 285, passed 284, failed 1, skipped 0. All five original three-prompt cases,
five held-TurnEnd witnesses and five RV514 cases passed. The one failure was
`C514_Health_request_reaches_busy_and_idle_recipients`, repetition ordinal 3,
at `RemoteControlPtyLane.InsertAsync`, called by `MarkWorkingAsync` on line 42,
before any automatic arm. Its verbatim first line:

```text
DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
```

Console inner exception: PostgreSQL `23505`, duplicate key on
`IX_TranscriptEntries_AgentSessionId_Sequence`. The pump and synthetic marker each
allocate MAX(Sequence)+1 independently. Waiting for UserPrompt alone permits their
allocations/saves to race while the real assistant record is still arriving; a
late real TurnEnd can also overwrite the intended synthetic Working state.
The repair adds an assertion-backed wait for the real persisted TurnEnd and the
production idle query before inserting the marker. It changes no input operation,
assertion, existing deadline or production source, and adds no retry. The original
base's exact health method is being rerun in its separate frozen scratch worktree
for inherited attribution; this subsequent repair/build only changes the main
worktree. The 284/285 outcome is retained, never relabelled green. A fresh complete
five-repeat selection remains required after this repair.
