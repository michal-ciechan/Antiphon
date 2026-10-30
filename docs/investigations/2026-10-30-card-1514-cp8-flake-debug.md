# CARD-1514 CP-8 timing investigation

Assigned base: eaea94cffa8363f6074d51e146916992545a32da. Linux/server2.
Verification is incomplete while this investigation runs. Historical CP-8 failures
remain failures; no production deadline or verdict assertion is authorized to change
without evidence. Measurements and raw receipts are retained in
`.antiphon/79c0c349-evidence/`.

The FIFO predicate times `Read-TestPhaseJsonlBounded` followed by
`Get-QualificationPhaseSums` together. Its 0.2-second outer assertion is test-only:
the first reader receives 200 ms, while the second has a separate default 500 ms
watch. The launch witness combines child exit, exit code, launch marker and a
12-second body ceiling inside a 12-second parent process ceiling.

## Measurement and decision

All numbers below are seconds, nearest-rank empirical p50/p95/max, with 20 fresh
PowerShell child processes per witness per arm. The probe extracts the committed
FIFO/launch here-strings, preserves the actual product calls and inputs, and only
extends the observation timeout to 60 seconds and persists discarded timings. The
FIFO stopwatch encloses the same two calls, not process startup or library import.
The launch body stopwatch encloses the real `Invoke-AntiphonNightlyTests` with the
existing fake process seam. Its child wall includes startup, dot-sourcing and
reader warmup. The seam still creates no real qualification/test launch.

| Assertion/span | What it measures / bound owner | Alone p50 / p95 / max | Loaded p50 / p95 / max | Decision and protective intent |
|---|---|---|---|---|
| FIFO combined body, formerly `<0.2` | Test-only outer wall for bounded FIFO read plus own-directory phase enumeration; **not a foreign-journal read** | .0135712 / .0214237 / .0546215 | .0232277 / .0663999 / .0667877 | `<0.5` wall, zero stream opens, exactly one listing of exactly the own chunk directory and its one entry. Preserve `Complete=false`, zero records, null process-wait contribution. |
| FIFO first reader alone | Product call receives its own 200 ms watch | .0037561 / .0067923 / .0080928 | .0059195 / .0117180 / .0366980 | No product change. Combined outer test wall is not that watch. |
| Launch body, formerly `<12` | Test-only whole nightly wrapper call, including fake build/discovery/run/publication work | 3.0789313 / 6.9710063 / 7.7503037 | 5.3534600 / 6.3884039 / 6.6696224 | Separate `<15` assertion, approximately 2x worse p95; preserve real result and exactly one launch. |
| Launch child, formerly `WaitForExit(12000)` | Test-only cold process, imports/warmup, body and shutdown | 6.5031929 / 11.6432822 / 12.5650205 | 9.5037337 / 12.3959586 / 12.4335206 | 26-second parent wait, approximately 2x worse p95; split exit/exit-code/body/marker facts and assert zero sidecar reads/FIFO opens before launch. |
| Time to launch marker inside body | Real fake-StartProcess witness, excluding cold startup/import | 2.3627672 / 4.8955302 / 5.2793252 | 3.6937186 / 4.5244522 / 4.9467043 | Structural launch ordering is primary; whole-body wall remains finite. |

The caller explicitly requires a wall floor of 0.5 seconds for the replaced FIFO
expectation; that floor exceeds 2x this small sample's p95. The launch limits round
2x the worse observed body/child p95 upward. These are diagnostic shared-host
measurements, not a Windows p95 or qualification receipt. CARD-1514/1404 owns
collecting comparable Windows/cold/warm/load observations at the next ordinary rc
pick. No timeout is derived dynamically from a run's own observed duration.

Alone means no owned build active. A script-only diagnostic harness and tool
`--no-build` import overlapped some baseline samples. Loaded samples all observed
dotnet build PID 12647 and its unchanged start time before and after each trial.
That build compiled the actual Antiphon.Tests graph, maxcpucount=1 within a granted
six-CPU host slot, from 15:45:20.985Z to 15:52:14.889Z; 413.23 seconds, zero errors,
1406 warnings in the inherited graph. The checkpoint/audit tool bootstrap and real
land check also briefly overlapped this arm. Host: 4551c5a7852f, Linux, TEMP=/tmp;
host load observed during sampling was 16.90 / 25.55 / 27.51.

The launch child's old 12-second allowance was exceeded in 1/20 alone and 2/20
loaded diagnostic samples, although their marker and result existed. No body
sample exceeded 12 seconds. This directly identifies startup/import overhead in
the parent allowance, rather than a blocked FIFO reader. Raw instrumented harness
runs at the original tight limits passed both alone (79 s) and loaded (120 s);
the latter measured FIFO .0785187 s and launch child/body 11.8536886/6.2814753 s.
Historical failures are retained, not replaced by these passes.

The original FIFO failure's exact elapsed time was discarded by its old cleanup.
It did not reproduce in the 40 cold samples or in 1200 additional loaded warm
calls (p50 .0107043, p95 .0203333, max .0757434; zero over .2). Thus a specific
historical descheduling interval cannot be proved. What is proved is that its
assertion is a test-only scheduling proxy around **two independent watches**;
the stronger zero-open/directory oracle detects regressions without treating that
proxy as a product deadline. No claim of a reproduced product regression is made.

## Product deadline and verdict boundary

Unchanged production `scripts/lib/test-phase-evidence.ps1`:

- `Read-TestPhaseJsonlBounded` at line 1163 shares its 200 ms watch between
  `Read-TestPhaseBoundedBytes` (1034) and JSONL conversion. The bounded byte reader
  rejects nonregular identity **before** stream open (1047); FIFO means no bytes
  are read. The native Linux open (993) also uses nonblocking/no-follow flags and
  refuses nonregular descriptors, so bypassing the first guard is still safe but
  unnecessary work and is detected by the new zero-open assertion.
- `Get-QualificationPhaseSums` at 3500 creates a separate default 500 ms watch
  (3508) when called as in this witness; it lists only the identity directory and
  rejects its FIFO by identity at 3564 before header/content readers. It never
  scans the foreign corpus in this witness. The distinct foreign-journal witness
  and its actual-byte assertions remain unchanged.
- Actual terminal publication supplies one shared 200 ms watch to prior sidecar,
  TRX and journal reading (`Update-QualificationChunkTiming`, 4230-4294). Expiry
  sets phase metrics and stats to null and `why=timing-read-refused` when no earlier
  reason exists. This is an observable diagnostic gap, not only a `complete=false`
  flag; explicit timing-summary quality/incompleteness may also reflect missing
  evidence. It cannot manufacture complete timing evidence.
- `Invoke-QualificationPostRunAggregation` at 4359 catches timing update failures
  and returns the supplied `ChunkExitCode` and `ChunkResult` unchanged. Verdict
  assertions, including the foreign witness's `5` / `KEEP-5`, are retained.

No evidence here justifies changing a production deadline for loaded Windows.
Only test expectations and test instrumentation change; the real consumer remains
owed native verification and the timing publication path still cannot change a
verdict.

## Evidence and pending verification

Raw retained artifacts are under `.antiphon/79c0c349-evidence/`: extracted probe
scripts, `alone-samples.json`, `loaded-samples.json`, per-child results/markers,
`statistics.json`, `loaded-warm-fifo.json`, host/build receipts and raw logs.
Each cold arm contains 20 observations of each witness, not 20 total observations.
Product code measured at `0d72d646fe92f33c6bf2525e0b20dbe1bf742449` is byte-identical
to assigned base. Probe setup failures (unavailable Python, missing generated
script, then result-variable/path collision) were corrected before accepted
samples; their short failed setup attempts earn no product-test credit. Earlier
setup logs reused the accepted-run filename, so only transcript errors and the
retained accidental result artifact remain for those attempts.

All requested extra probes/builds are explicitly brief-directed diagnostics, not
unlisted ordinary coverage. No DB, Docker, migrations, web host, or production
runner is started by these witnesses. IO and CLI are measured at the call/body
boundaries above; finer startup/import/IO subdivisions remain unknown rather than
zero. Slot admission is separate from execution in retained `BUILD SLOT` logs.

The real `scripts/audit-land-verification-graph.ps1 -Check` passed both evaluator
passes and canary comparison: digest
`20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, count 66.
`-Update` was not run. No definition file changed.

Structural controls and required checkpoint outcomes remain pending at this
measurement/fix commit; no clean overall receipt is claimed yet.

## Additional inherited wall proxy exposed by the exact baseline

The first exact CP-8 control baseline at `2dff9f80f21e2d05f7c910b995183e33cbab2ec1`
executed one test and failed it in 112.95 s. The repaired FIFO/launch guards passed
(FIFO .1172675 s, launch child/body 12.6102721/5.1037013 s), but the unchanged
`post-run-hostile-files-keep-exit-inside-budget` combined assertion failed. No
mutation had started. Its original receipt remains under `pc-baseline/1/`.

Before attributing it, the **assigned base** hostile-aggregation fixture and old
predicate were extracted with `git show eaea94cffa8363f6074d51e146916992545a32da`.
The production library and compiled exact test are unchanged from that base. The
focused baseline probe reused its 100 MB hostile corpus, reset the same dense
sidecar before each invocation, and ran its exact `Invoke-QualificationPostRunAggregation`
call and predicate 200 times. No own build or other own test was active. This is
base-footprint diagnostic execution, not a clean checkpoint at a different HEAD.

| Assertion/span | Alone p50 / p95 / max (s) | Original failures | Decision |
|---|---|---|---|
| Hostile terminal aggregation outer `<0.2` | .0743901 / .1442766 / .6720185, n=200 | 7/200; all 200 retained exit 7, KEEP-7, timing-read-refused and null metrics | Test-only outer wall includes read-budget expiry, JSON rendering and atomic write; use `<1.5`, approximately 2x observed max, and structural read bounds. |

Evidence: `base-agg-source.ps1`, `base-agg-probe.ps1`, `base-agg-samples.json`,
`base-agg.log`. The latter diagnostic held its granted slot 28 s after 15 s
admission. A separate loaded p95 was not collected for this newly exposed proxy;
its 1.5-second individual assertion is a provisional shared-host allowance tracked
under CARD-1514/1404, not a changed per-test/row timeout. The explicit CPU-only
two-arm measurement obligation above remains fulfilled for both commissioned
FIFO/launch witnesses.

The replacement observes stream positions at disposal in the actual bounded,
header, JSONL and TRX readers, plus every attempted stream-open path. It permits
at most two successful streams, only the dense regular sidecar and huge regular
journal, and one additional refused open of the regular mode-000 file. The exact
byte ceiling is dense-sidecar length plus the 256 KiB header cap plus its one
8 KiB sentinel refill (470345 bytes for this corpus); FIFO and link paths must
never reach open, and the denied path must never yield a stream. This protects
bounded work on the 100 MB corpus independently
of shared-host scheduling. The exit/result/refusal/null-metric assertion is
unchanged except for extracting its old wall predicate into its own named check.

The first added structural baseline at `e5c05093773738dc8365f327af37a0824e7fde5d`
correctly remained red: my initial census wrongly allowed only two attempted opens
instead of two streams plus the refused mode-000 attempt. It recorded .100705 s,
470345 bytes and three attempts. No product behavior or verdict failed. The test
now distinguishes attempts from successful streams and accounts for the scanner's
8 KiB sentinel refill; the failed receipt is retained in `pc-baseline-final/1/`.

## Structural positive controls

Final harness source: `1c67818f5cabecbf7d0a8017ca304054a48052f9`. One exact
`PosixHarnessToolTests.Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path`
selection per fresh process. Baseline: 1 executed/passed, 97.557 s. Each red below
executed one and failed one at its named assertion; each restored green executed
and passed one, zero skipped. No build, fixture or empty-selection error is credited.

| Control | Intended named failure | Red / restored green seconds | Result |
|---|---|---|---|
| Bypass initial FIFO identity refusal before the native regular-file check | fifo-cases-file-never-opens-stream | 82.500 / 93.521 | Killed, restored green |
| Replace identity directory with shared timing root | fifo-cases-file-lists-only-own-chunk | 107.595 / 100.241 | Killed, restored green |
| Read sidecars on the nonterminal path before fake process launch | fifo-sidecar-no-reads-before-launch | 70.262 / 71.634 | Killed, restored green |
| Add three redundant opens to the sidecar reader | post-run-hostile-files-bounded-reads | 86.372 / 111.271 | Killed (6 attempts/5 streams), restored green |

Evidence: `controls.json`, `controls-certified.log`, exact-method TRX/logs in
`pc-*-red/1/` and `pc-*-green/1/`, `control-provenance.json`, `restoration.json`.
The one gated driver held its lease 837 s, waited zero. It owned and awaited every
child and restored between every red/green pair. Original/restored production
library SHA256 is
`0B384A9E866476C400002CC55E7D94B4ADB922B8F1E50FE26F226057D754EF02`.
Tracked source is clean after restoration. These are pre-land controls, not
SourceLanding credit. The unchanged compiled .NET test pack was built at
`0d72d646fe92f33c6bf2525e0b20dbe1bf742449`; these controls load the final harness
from disk. They do not substitute for the upcoming fresh checkpoint build or its
ten-alone/five-loaded repetitions.

The checkpoint table was imported unchanged. An ignored execution manifest keeps
the closed CP-1..CP-9 selections and applies provisional coupled row/build ceilings
CP-1=40 minutes, CP-2=32, CP-3=4, CP-4=2, CP-5=6, CP-6=6, CP-7=4, CP-8=6, CP-9=2.
CP-1 and CP-2 account for the review's 12-14 minute loaded builds plus measured
test work, as recommended in the predecessor report; they are not measured p95s.
Selected-group execution allowances are M1=40 and M2=58 minutes, with a separate
finite 45-minute admission allowance. Two independent M1/M2 runs each execute
their own manifest build once. Native CP-10/11 are excluded explicitly. No plan,
policy, definition or test selection changed. Final required outcomes remain
pending until these fresh builds and repetitions finish.
