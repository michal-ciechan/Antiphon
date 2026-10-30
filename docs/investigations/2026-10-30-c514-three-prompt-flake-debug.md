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
successful builds (174.97 s and 53.22 s), with a 57 ms gap. The test supervisor and
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
/remote-control
c514 lane shared prefix alpha
```

### base-alone30 — 2026-10-30T18:06:10.3660938+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
```

### base-alone30 — 2026-10-30T18:06:59.2489010+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

### base-alone30 — 2026-10-30T18:07:45.9481722+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

### tip-alone30 — 2026-10-30T18:10:05.5714234+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

### tip-alone30 — 2026-10-30T18:11:07.4160689+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
```

### tip-alone30 — 2026-10-30T18:12:02.8997292+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
```

### tip-alone30 — 2026-10-30T18:12:53.6911096+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
```

### base-loaded15 — 2026-10-30T18:19:35.1797883+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

### base-loaded15 — 2026-10-30T18:20:09.4172780+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

### tip-loaded15 — 2026-10-30T18:23:03.6696732+00:00

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30))
```

Child SUBMITTED lines:

```text
/remote-control
c514 lane shared prefix alpha
c514 lane shared prefix bravo
```

