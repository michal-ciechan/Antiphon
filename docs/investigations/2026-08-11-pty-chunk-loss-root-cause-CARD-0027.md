# CARD-0027 root cause: the pty is not losing anything — the receiving TUI is

**Task:** d89281e5 (Debug) · **Date:** 2026-08-11 · **Status:** root-caused to a component and a
mechanism; the exact line of third-party code is out of reach and that limit is stated below.

## Root cause, in one sentence

ConPTY hands a multi-KB write to the child as a run of **~1024-byte reads that land in the same
event-loop turn**, every byte present; the Claude Code TUI's composer keeps **one whole chunk per
turn and silently discards the rest**, so a body that spans more than one read chunk arrives
truncated to a whole number of chunks — and which chunks survive depends on the TUI's render
timing, not on the body.

That is why size never predicted it. Size decides *how many* chunks there are; the TUI's event loop
decides *how many survive*.

## The answer to "why does 4 262 survive and 1 402 not"

They are the same failure. A 1 402-byte body is 2 chunks and lost the first; a 4 262-byte body is 5
chunks and happened to be delivered across enough turns to keep them all. **Reproduced directly:**
three identical 1 400-byte trials, same encoding, same process, back to back — two lost their head
at byte 1029, one arrived whole (R4 below). The non-determinism is not a story about the data, it
is the observable.

## What was measured

Four independent instruments, each varying one thing. Raw output is under
`tests/Antiphon.Agents.Pty.Tests/bin-card27/TestOutput/card-0027/`.

### 1. The transport is lossless — to a *JavaScript* peer (negative result, and the important one)

The prior investigation cleared our stack using **fakeclaude, a .NET console program**. The real
Claude is not .NET. `claude.exe` is a **292 MB Bun v1.4.0 single-file executable** (`Bun.stripANSI`
and JSC `@call` builtins are in the binary; `grep -a "Bun v"` reports the version). So the runtime
that actually reads our bytes had never been on the bench.

`probes/stdin-probe.js` is that peer: raw mode, accumulates every byte, reports totals, chunk sizes
and which event-loop turn each chunk landed in. Driven through `PtyAgentRunner` — the same ConPTY
path production uses — under **Node 24.6** and under **Bun 1.3.14** (the closest public release;
1.4.0 is unreleased).

| body bytes | node: got | node chunks | bun: got | bun chunks | missing |
|---|---|---|---|---|---|
| 1 366 | all | 3 | all | 3 | **0** |
| 2 320 | all | 4 | all | 3 | **0** |
| 4 262 | all | 5 | all | 5 | **0** |
| 5 185 | all | 6 | all | 6 | **0** |
| 43 000 | all | 43 | all | 43 | **0** |

Zero loss at every size, on both runtimes. Also zero loss with a peer that **blocks 25 ms on every
read** (models a TUI rendering between reads), across **12 deliveries down one long-lived session**
(rules out session age and stream phase), and with **1–1024 bytes of unterminated text already in
the buffer** (rules out a prior partial line). Every one of those was a hypothesis in the card; every
one is negative.

**So: the console input path, ConPTY, libuv, Bun's stdin and our whole chain deliver every byte.**

### 2. The enabling fact: ~1 KB reads, delivered in one turn

The same runs show *how* the bytes arrive:

- read quantum **1024 bytes** under Node, **1040** under Bun 1.3.14 — never the whole body;
- for a 43 KB body, **up to 27 chunks landed in a single event-loop turn**.

Not loss. But it is exactly the input a consumer has to reassemble, and the boundaries match the
live cut points.

### 3. Real Claude: ground truth from its own JSONL

`ClaudePasteLossCanaryTests` types marked bodies into a real Claude TUI through the production
encoding and diffs its transcript against what was sent.

| sent bytes | recorded | surviving lines | cut |
|---|---|---|---|
| 1 414 | 390 | 38–49 (last only) | byte 1026 |
| 2 332 | 284 | 76–83 (last only) | byte 2052 |
| 5 194 | — | 0–37 (**first only**) | byte 1026 |

Both live signatures, on demand: head-loss and — for the 5 194-byte body — a first-chunk-only shape.
Every survivor is a whole ~1024-byte chunk.

### 4. The boundary and the cut point, pinned

`ClaudeComposerCaptureProbeTests` reads the loss straight off the composer's own
`[Pasted text #N +M lines]` counter, so it costs **no model turns** and runs at will. One fresh
Claude process per trial (reusing a session made the matrix alternate in lockstep with the trial
index — residue, not physics).

Boundary, 3 repeats each:

| body bytes | whole | lost |
|---|---|---|
| 810 | 3/3 | 0 |
| 972 | 3/3 | 0 |
| 1 026 | 0/3 | **3/3** (kept 27 bytes) |
| 1 080 | 0/3 | **3/3** |
| 1 350 | 0/3 | **3/3** (kept 351 bytes) |

**A body that fits in one read chunk cannot lose anything. Above it, loss is the norm.**

Cut point at 7-byte resolution (R4), 1 400-byte body:

```
rep 0   survivors = bytes 1029-1400   (one contiguous run)
rep 1   survivors = bytes 0-1400      (whole)
rep 2   survivors = bytes 1029-1400   (one contiguous run)
```

1029 ± 7 ⇒ the cut is at **body byte 1024**.

And what survives is always a whole number of chunks — captured sizes across the matrix were 459,
999, 1 026, 1 431, 1 998, 3 051, 4 050 bytes, i.e. ~1, 1, 1, 1.4, 2, 3, 4 chunks of a 6-chunk body.

### 5. The dose-response that names the mechanism

Same 5 400-byte body, written in 1024-byte pieces, varying only the gap between writes:

| gap | captured | of 5 400 |
|---|---|---|
| 0 ms | 459 B | 8 % |
| 2 ms | 999 B | 18 % |
| 10 ms | 999 B | 18 % |
| 25 ms | 1 431 B | 26 % |
| 50 ms | 3 051 B | 56 % |
| 100 ms | 3 051 B | 56 % |

More time between chunks ⇒ more chunks survive, monotonically. That is the signature of chunks
racing inside one turn: spreading them across turns is what saves them. It does not reach 100 %,
which fits — as the composer grows its re-render gets slower, so chunks keep piling into one turn.

Bracketed paste is **not** the discriminator: the same body unwrapped lost the same way (8 % kept).
It is not the paste protocol, it is the accumulation.

## What this rules out, by measurement

| hypothesis | verdict |
|---|---|
| our stack (server → runner → pipe → pty-host → write) | lossless — already pinned, re-confirmed |
| ConPTY / conhost dropping input | **no** — a JS peer on the same path receives every byte |
| the ~4 094 console line-input cap | real, but irrelevant: raw-mode TUIs never touch it, and loss starts at 1 024 |
| Bun's stdin implementation | **no** — Bun 1.3.14 receives every byte |
| drain-rate / slow consumer | **no** — blocking 25 ms per read loses nothing |
| session age, stream phase, cumulative bytes | **no** — 12 deliveries down one session, all whole |
| a prior partial line in the buffer | **no** — 1–1024 byte prefixes, all whole |
| a concurrent writer | not reached (the above already localise it) |
| bracketed paste markers | **no** — unwrapped bodies lose identically |
| body size as the predictor | **no** — 1 400 bytes: whole once, truncated twice, same trial |

## The limit of this investigation, stated plainly

**Which line of Claude Code does it cannot be determined from outside.** The app is compiled to JSC
bytecode inside the Bun executable — string tables are readable, logic is not. Everything above
localises the defect to the composer's paste accumulation and characterises its behaviour
precisely, but the specific defect (a stale-snapshot append, a last-write-wins state update, a
paste buffer replaced instead of concatenated) is inference from the dose-response, not something I
read.

What would settle it:

1. **A Claude Code build that logs each stdin chunk and the composer length after it.** One run
   would show whether chunk *n*'s append saw the length after chunk *n−1*. Needs upstream, or a
   `CLAUDE_CODE_*` input-debug flag if one exists.
2. **Upstream report with this repro.** `ClaudeComposerCaptureProbeTests` is free to run and fails
   in three lines of output; it is a complete bug report as it stands.
3. **A source-mapped or non-bytecode bundle** (an npm `cli.js` install rather than the native
   executable) would make the composer's input path directly readable — worth trying if this needs
   to go further.

## What follows for us

Not implemented here — the card asked for the mechanism, not a fourth mitigation. Ordered by value:

1. **The only safe inline size is one read chunk (~1024 bytes), and it must be counted in UTF-8
   BYTES.** Every gate in `DelegationSettings` compares `string.Length` (UTF-16 chars). Briefs here
   are em-dash-heavy — the 2026-08-11 investigation had to convert char offsets to byte offsets for
   that reason — and an em-dash is 3 bytes. A 900-*character* brief can be 2 700 *bytes*, three
   chunks, and mangle. `BriefInlineMaxChars = 900` is safe for ASCII and only for ASCII. Pinned by
   `PtyInlineCeilingTests`.
2. **`PtyInlineSafeChars = 4 000` is not a safety property and its name says it is.** Nothing is
   safe above ~1 024 bytes. It is a useful *incident* threshold; it should not read as a guarantee.
3. **Pacing was dismissed on evidence from the wrong receiver.** The card records "not drain-rate
   pacing — 1024-byte chunks 2 ms apart gave byte-identical results", but that was measured against
   the console line-input cap, which is a different mechanism. Against the real TUI, pacing has a
   clear dose-response (8 % → 56 %). It is a mitigation, not a fix — it never reached 100 % — so the
   spill-to-file ceilings remain the right primary defence.
4. **Verification could actually detect this now.** `ComposerDeliveryEvidence` matches head *or*
   tail because the middle is not on screen. The composer's own `[Pasted text #N +M lines]` counter
   is on screen and is exact — comparing M against the body's line count would catch a truncated
   delivery at delivery time instead of days later.

## Artifacts

| path | what it is |
|---|---|
| `src/Antiphon.FakeClaude/StdinClipModel.cs` | **the mechanism above, modelled** (CARD-0028). Opt-in `ANTIPHON_FAKE_STDIN_CLIP` |
| `tests/Antiphon.Agents.Pty.Tests/StdinClipModelTests.cs` | **CI-runnable.** The model's arithmetic, incl. UTF-8 bytes vs chars |
| `tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs` | **the honesty check.** Same bodies through real Claude and the fake; costs no turns |
| `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs` | **CI-runnable.** The brief ceiling, end to end, into a clipping receiver |
| `tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js` | JS peer: byte totals, chunk sizes, event-loop turn per chunk |
| `tests/Antiphon.Agents.Pty.Tests/NodeStdinProbe.cs` | drives it over ConPTY; `CARD27_RUNTIME` selects node or bun |
| `tests/Antiphon.Agents.Pty.Tests/PtyInputChunkingTests.cs` | **CI-runnable.** Pins losslessness to a JS peer + the ~1 KB/one-turn precondition |
| `tests/Antiphon.Agents.Pty.Tests/PtyInputLossExperiments.cs` | the bench: size, drain rate, session phase, encoding, prior partial line |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeComposerCaptureProbeTests.cs` | **the at-will reproduction.** Real Claude, zero model turns |
| `tests/Antiphon.Agents.Pty.Tests/ClaudePasteLossCanaryTests.cs` | ground truth via Claude's own JSONL; costs real turns |
| `tests/Antiphon.Tests/Application/DelegationUnitTests.cs` | `PtyInlineCeilingTests` — ceiling tripwire + the char-vs-byte gap |

Headed probes need `ANTIPHON_HEADED_TESTS=1` and are `[Explicit]`; they never run in a normal suite.

```
# the reproduction, free to run
$env:ANTIPHON_HEADED_TESTS=1
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card27/ `
  -- --treenode-filter "/*/*/ClaudeComposerCaptureProbeTests/Cut_point_resolution"

# the CI-runnable facts
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card27/ `
  -- --treenode-filter "/*/*/PtyInputChunkingTests/*"
```

## Follow-up: the fake can now exhibit it (CARD-0028, 2026-08-11)

The instrument that cleared our stack could not reproduce the defect, so CI could not either. It can
now, **opt-in only** — `ANTIPHON_FAKE_STDIN_CLIP=1` makes fakeclaude keep one ~1024-byte read chunk
per burst (a burst is its event-loop turn) and discard the rest, in UTF-8 **bytes**. Default OFF, so
`PtyLargeWriteTests` keeps pinning that our transport is genuinely lossless. `=random` with
`ANTIPHON_FAKE_STDIN_CLIP_SEED` offers the live non-determinism, replayably; the fake prints its
model and seed at startup (`CLIP:mode=…`).

```
# the fake vs the real thing — no model turns, one fresh TUI per trial
$env:ANTIPHON_HEADED_TESTS=1
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-fake/ `
  -- --treenode-filter "/*/*/FakeVsRealClipParityTests/*"
```

Run 2026-08-11, artifacts under `TestOutput/card-0028/`. Body of 7-byte marker lines, one write:

| body bytes | fake | real #0 | real #1 | real #2 |
|---|---|---|---|---|
| 804 | whole | whole | whole | whole |
| 965 | whole | whole | whole | whole |
| 1 399 | markers 147-199 | 147-199 | 147-199 | whole |

Both peers cut at marker 147 = byte 1029, i.e. body byte 1024 — the same number section 4 measured,
reproduced against a live Claude today, and the same whole/clipped/clipped non-determinism.

**Two things measured while building it that change how these tests must be written:**

1. **ConPTY does not hand one write to the child as one read.** A 1 399-byte write arrives as 2-5
   reads up to ~14 ms apart, and conhost strips the bracketed-paste markers on the way, so the first
   read is the body's first 6 bytes. Anything grouping reads into "turns" by a quiet gap must use a
   window wider than that jitter (the clip tests use `ANTIPHON_FAKE_BURST_MS=80`) or the same body
   lands in one turn or two depending on scheduling.
2. **ConPTY narrows non-ASCII input to ONE BYTE per character for a .NET peer** — even with the
   peer's console input codepage reading back as 65001. A 1 291-byte em-dash-heavy body reaches
   fakeclaude as 1 023 bytes. So the byte-vs-char rule — the one that shipped wrong in the ceilings —
   **cannot be tested through the pty at all**; `StdinClipModelTests` drives the model directly for
   it, and `FakeClaudeContractTests.Non_ascii_input_reaches_a_dotnet_peer_narrowed_to_one_byte_per_char`
   pins the transport fact so nobody re-writes that unit test as a pty test and gets a green run for
   the wrong reason. Real Claude is unaffected: em-dashes reach it intact.

And one thing the end-to-end ceiling test exposed: **at the shipped `BriefInlineMaxBytes = 900`, no
brief is ever delivered inline.** `DelegationReportFormatter.BuildBrief` has a floor of ~915 bytes —
the reporting contract alone is 838 — so every brief takes the pointer path. That is a safe state,
not a broken one, but the inline branch is dead code in production and the ceiling is doing no
sizing work; if the contract ever shrinks, the branch reopens silently.
`DelegationBriefCeilingPtyTests` pins both the floor and the inline path at the physical 1024-byte
boundary.
