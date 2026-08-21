# CARD-0128 S2a — evidence-gate `PtyAgentRunner.SendLineAsync`'s submitting CR

**Date:** 2026-08-22 · **Status:** plan (design only; no code changed in this slice) ·
**Parent:** `2026-08-21-card-0128-pty-flake-cast-plan.md` §S2a ·
**Evidence:** `docs/investigations/2026-08-21-card-0128-pty-flake-measurements.md` (the
`SendLineAsync_with_CRLF_multiline_body_submits_as_one_intact_turn` row: 6/20 isolated runs show the
complete body on the raw screen and no `SUBMITTED:` marker).

## The mechanism, traced — not guessed

The race is a three-clock disagreement, and every piece of it is already measured and documented in
this repo; what was never done is connecting them to the production primitive:

1. **The writer's clock.** `PtyAgentRunner.SendLineAsync` (`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:181`)
   writes the encoded body as ONE write, `Task.Delay(20, ct)`, then a lone `\r`. The 20 ms is a
   writer-side clock: it measures from the moment the body left our process, not from the moment the
   child consumed it.
2. **ConPTY's delivery clock.** ConPTY does not hand a single write to the child as a single read —
   measured (`ANTIPHON_FAKE_DEBUG_INPUT=1`, documented at `FakeClaudeContractTests.cs`
   `LaunchClippingFakeAsync` doc comment): a 1 399-byte write arrives as 2–5 reads **up to ~14 ms
   apart**, more under load. So the *last* chunk of the body can reach the child at T+δ with δ
   approaching (or under machine load exceeding) the writer's 20 ms.
3. **The receiver's burst clock.** The TUI's input handler distinguishes a typed Enter from a pasted
   newline by arrival-time gap. fakeclaude models this with reader-thread arrival stamps grouped by
   `ANTIPHON_FAKE_BURST_MS` (12 ms, `src/Antiphon.FakeClaude/Program.cs:240-262`), which is the
   model of real Claude's paste heuristic pinned by `Text_and_CR_in_one_write_does_NOT_submit`: a CR
   arriving **in the same burst as body text is folded to a literal newline and nothing submits**.

The gap the receiver sees between the body's tail and the CR is `20 − δ` ms. With δ jittering up to
~14 ms solo (worse under load), that compresses below the 12 ms burst window in a minority of runs:
the CR joins the body's burst, folds to a newline, and the body sits complete, rendered, and
**unsubmitted** in the composer — exactly the raw-output shape S1 captured (full `HEAD … TAIL`, no
`SUBMITTED:`, test times out after its 5 s wait plus overhead = the observed 5.65–8.66 s).

**This is not new analysis — it is CARD-0050 S3's analysis, half-applied.** The doc comment on
`tests/Antiphon.Agents.Pty.Tests/EchoGatedSubmit.cs` states this exact mechanism ("the writer's 20ms
body→CR spacing compresses below [the burst gap] at the reader when ConPTY delivery of the body lags
under load") and fixed it — **in the test helper only**. Production `SendLineAsync` kept the blind
clock, and the CRLF regression pin exercises production `SendLineAsync`, so the pin kept flaking.
The fake's own class doc (`Program.cs:90`, "waits 20ms between the body and the CR, comfortably
above the gap") is wrong on the measured numbers and gets corrected in this slice.

It is a **production defect**, not test-infrastructure noise: the fold-on-merge behaviour is real
Claude's measured contract (the 2026-08-08 live miss — a prompt stranded unsubmitted for half an
hour — was this same fold via a different route), and blind `SendLineAsync` is a live production
path (see "blast radius" below).

## Who rides this primitive today (checked, not assumed)

- **In-process adapters** (`server/Infrastructure/Agents/Pty/`): `RawPtyAdapter.SendPromptAsync`,
  `ClaudeAdapter.SendPromptAsync`, `CodexAdapter` → `_runner.SendLineAsync`. Raw is documented
  "Blind SendLineAsync" in `ProviderContractCatalog` — no higher-layer compensation.
- **The pty-host RPC boundary**: `SendLineMessage` → `PtyHostServer.DispatchAsync:164` →
  `HostSession.SendLineAsync:190` → `_runner.SendLineAsync`. Pure delegation — a fix inside
  `PtyAgentRunner` is automatically in force for detached pty-hosts, with **no protocol change**.
- **`VerifiedPromptSubmitter`** does NOT ride it — it has its own write path and is already
  evidence-gated *before* the Enter (evidence → 20 ms pause → `\r`), plus an Enter-retry keyed on
  output advance. Fixing `SendLineAsync` therefore makes **nothing** in VerifiedPromptSubmitter
  redundant, and nothing there should be simplified: its `SubmitAttempts` retry also covers the
  residual case where head-arm evidence fires while the body's tail is still in flight (its
  post-submit advance check catches the fold and re-presses). Leave it alone; only its
  "Same pre-Enter pause as SendLineAsync" comment needs re-wording once SendLineAsync changes.
- **The queue's `DeliverAsync`** (CARD-0055) does NOT ride it either — its own evidence-before-Enter
  plus transcript confirmation. Out of scope, nothing to change.
- **The server-side twin**: `RunnerTerminalSession.SendLineAsync`
  (`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:47`) re-implements the same
  blind 20 ms as two `SendInputAsync` HTTP calls — **with an HTTP hop's jitter added on both
  writes**, so its effective body→CR gap at the receiver is even less predictable. Its riders:
  `RunnerGrokAdapter`, `RunnerRawAdapter`, `RunnerOpenCodeAdapter` (all catalogued blind), and
  `RunnerClaudeAdapter` when delivery verification is disabled. This twin is named here as
  **S2a-2** below so it cannot be forgotten, but the regression pin and this slice's proof target
  the `PtyAgentRunner` primitive first.

## The fix — shape, exactly

**One sentence: the 20 ms pre-Enter pause stops being measured from the body *write* and starts
being measured from positive composer evidence that the body's TAIL was consumed (bounded, with the
old behaviour as the fallback), and the CR is still sent exactly once — no retry at this layer.**

### 1. A shared, delegate-based gate helper (new, `src/Antiphon.Agents.Pty/`)

`EchoGatedLineSender` (name bikesheddable), the production sibling of the test-only
`EchoGatedSubmit`, delegate-based like `VerifiedPromptSubmitter` and `ComposerInputProbe` and for
the same reason — the in-proc runner and the server-side twin must reach identical semantics by
different transports, and the rules must not fork:

```csharp
public sealed record SendLineGateOptions(
    TimeSpan PreSubmitPause,     // 20 ms — the existing discrete-Enter gap, unchanged
    TimeSpan EvidenceTimeout,    // 2 s default — bound, not a promise (see below)
    TimeSpan PollInterval);      // 25 ms

public enum SendLineGateOutcome { EvidenceSeen, TimedOutProceeded, EmptyBody }

public static async Task<SendLineGateOutcome> SendAsync(
    string body,                                     // raw; encoded internally via PtyInputEncoding
    Func<CancellationToken, Task<string>> snapshotScreen,
    Func<string, CancellationToken, Task> write,     // raw write, no CR appended
    SendLineGateOptions options,
    CancellationToken ct)
```

Algorithm:
1. `screenBefore = snapshotScreen()` — captured **before** the body write (the placeholder-index
   diff needs it).
2. `write(PtyInputEncoding.EncodeBody(body))` — the body stays **ONE write**, per the CARD-0037
   single-write ceilings. Nothing about encoding changes.
3. Poll `snapshotScreen()` at `PollInterval` until `ComposerDeliveryEvidence.BodyConsumed(
   screenBefore, screenAfter, normalizedBody)` (new matcher, §2) or `EvidenceTimeout` expires.
4. `await Task.Delay(PreSubmitPause, ct)` — **after** evidence (or after the timeout), not after
   the write. This is the load-bearing move: on evidence, the body's burst is already consumed and
   rendered, and the 20 ms settle additionally covers a partial-tail render racing the last in-flight
   chunk (chunk jitter is ~14 ms; 20 ms clears it).
5. `write("\r")` — **exactly once**. Return the outcome for diagnostics.

Properties worth stating because they are the safety argument:
- **Never weaker than today.** Instant (even stale/false) evidence reproduces today's behaviour
  bit-for-bit: body → 20 ms → CR. Stale evidence is possible — `ComposerDeliveryEvidence`'s
  fragment arms don't diff against `screenBefore`, so a body textually identical to something
  already on screen (a repeated "Continue.", a re-sent `/exit`) short-circuits the poll — and that
  is *fine*, because the outcome is precisely the current contract, not something worse. The gate
  only ever ADDS delay when evidence is slow, and a later discrete CR is a *more* discrete Enter.
- **Bounded.** No evidence within `EvidenceTimeout` ⇒ proceed with the CR anyway (today's
  behaviour, logged via the returned outcome). `SendLineAsync` is the best-effort primitive; a
  non-echoing child (raw-mode app with echo off, a deaf TUI, a trust modal) must not turn every
  `SendLineAsync` into a throw or an unbounded stall. Strictness — withholding the Enter, failing
  the launch — is `VerifiedPromptSubmitter`'s and the queue's job, and stays there.
- **No retry, by design.** A second CR at this layer needs a "did the first one submit?" oracle,
  and this layer has none (the `SUBMITTED:` marker is fakeclaude-only; real Claude offers only
  transcript records and output advance, which belong to CARD-0055's layer). Both existing oracles
  and both existing retry loops (VerifiedPromptSubmitter's Enter-retry, the queue's
  `ReEnterIntervalSeconds` + late-confirm) sit ABOVE `SendLineAsync`; adding a third underneath
  would stack retries and re-create the double-submit class CARD-0055 exists to prevent. The gate
  makes the single CR land right; the layers above already own making it land *at all*.

### 2. The evidence: "body consumed", not "body visible"

New method on `ComposerDeliveryEvidence` (extended there, never re-implemented — CARD-0103's
"there must not be a second copy of the matching rules to drift"):

`BodyConsumed(screenBefore, screenAfter, body)` = `IsVisible` **minus the head arm**:
- the body's **tail** fragment visible (same `FragmentSpan`/window-quorum machinery), OR
- a **new** `[Pasted text #N]` placeholder index (diffed against `screenBefore`), OR
- the placeholder-count fallback for an unreadable index.

The head arm is deliberately excluded: head evidence proves the *first* chunk was consumed while
the CR-merge hazard lives entirely with the *last* chunk. Tail-or-placeholder is exactly the "the
receiver has processed the final bytes" signal. Coverage against every rendered shape the canary
pinned (`ClaudeComposerRenderCanaryTests`, quoted in `ComposerDeliveryEvidence`'s doc):
- short single line → verbatim → tail == whole body ✔
- huge single line → suffix visible → tail ✔
- multi-line, TAIL-lines rendering → tail ✔
- multi-line, PREFIX+placeholder rendering → placeholder arm ✔
- modern-backend collapsed paste (the usual case per CARD-0037) → placeholder arm ✔

`IsVisible` itself is untouched — VerifiedPromptSubmitter and the queue keep the head arm, whose
laxness is safe there because both have post-Enter verification and retries.

### 3. `PtyAgentRunner.SendLineAsync` — the seam

Body of the existing method becomes a call to the helper, inside the existing `_writeGate`
(unchanged: nothing may interleave a write between body and CR), with `SnapshotScreen`/`WriteCoreAsync`
as the delegates and defaults for the options. Signature unchanged ⇒ `HostSession`, the
`SendLineMessage` RPC, `PtyHostClient` and every test caller compile untouched. Add a
`SendLineGateOutcome? LastSendLineOutcome` property (the `Backend` property pattern) so a
fallen-back gate is observable instead of silent.

Two accepted, documented costs:
- `_writeGate` and the pty-host's serial `DispatchAsync` loop are held up to ~2 s worst case on a
  non-echoing child (today: 20 ms). Frames behind a SendLine (status, input) queue for that bound.
  Acceptable: SendLine is rare (prompts, not keystrokes), the bound is small against every caller's
  own timeout, and the alternative — dispatching SendLine off-loop — would let raw input interleave
  between body and CR, which is the exact interleaving the gate exists to prevent.
- Callers see the CR up to `EvidenceTimeout` later than today when evidence is slow. Every measured
  caller (detector setups, adapters, canaries) waits seconds for its *next* signal anyway.

### 4. What is explicitly NOT touched

- `PtyInputEncoding` and the two-write contract (body one write, CR separate) — unchanged.
- Delivery ceilings (`PtyDeliveryCeilingsTests`) — the body is still a single unsplit write;
  nothing here re-chunks input. No ceiling value moves.
- DA1 (`ModernPtyDa1Tests`) — no new pseudoconsole creation path; no quiet-window change.
- `ANTIPHON_FAKE_BURST_MS` — stays 12 ms (CARD-0050 S3 judged the margin too thin to tune; this fix
  removes the need to tune it, which is the point).
- The regression pin's assertions and its 5 s window — unmodified, per the S2a contract.
- `VerifiedPromptSubmitter` logic and the queue's delivery verification — unchanged (comment
  touch-ups only).

### S2a-2 (follow-up, same card): `RunnerTerminalSession.SendLineAsync`

Adopt the same helper with `SnapshotScreenAsync`/`SendInputAsync` as the delegates (this is why the
helper is delegate-based). Options must be injectable because
`RunnerTerminalSessionInputEncodingTests` scripts a fake client whose snapshots never change — those
tests either script evidence screens or set a short `EvidenceTimeout` and assert the fallback arm.
Kept as a separate sub-slice so the `PtyAgentRunner` fix — the one with the reproducible failing
pin — lands and proves out first; the twin has no failing pin today, only the same shape read from
its code.

## Testing beyond the existing pin

The pin (`SendLineAsync_with_CRLF_multiline_body_submits_as_one_intact_turn`, unmodified) proves
the flake is gone only statistically. Add mechanism-level proof:

1. **Deterministic reproduction of the race class** (new `FakeClaudeContractTests` member):
   launch the fake with `ANTIPHON_FAKE_DEAF_START_MS=300` and call production `SendLineAsync`.
   Under the OLD code this fails deterministically — body and CR are both written inside the deaf
   window, sit together in the pty input pipe, drain as one read on wake, group as ONE burst, and
   the CR folds (this is the race's limit case: δ → ∞). Under the gated code, evidence cannot
   appear until the fake wakes and echoes, so the CR goes out only after the body's burst is
   consumed, as its own burst, and `SUBMITTED:<body>` must appear. This turns the 6/20 timing flake
   into a 100% red/green mechanism pin, which is what actually protects against regression.
2. **Helper unit tests** (scripted delegates, no pty — the `VerifiedPromptSubmitter` test style):
   - evidence appears ⇒ exactly ONE `\r` written, and not before `PreSubmitPause` has elapsed
     after the evidence-satisfying snapshot;
   - evidence never appears ⇒ exactly ONE `\r`, written only after `EvidenceTimeout`
     (the no-double-submit pin for the no-retry policy — the count assertion is the point);
   - instant evidence ⇒ CR no earlier than body-write + `PreSubmitPause` (the never-weaker floor);
   - cancellation mid-poll ⇒ no CR is ever written.
3. **`BodyConsumed` matcher tests**: tail visible ⇒ true; head visible with tail absent ⇒ **false**
   (the arm exclusion is the fix's core and gets its own assertion); new placeholder index ⇒ true;
   pre-existing placeholder index alone ⇒ false.
4. **Must-stay-green sweep** (no changes expected, run to prove it): full
   `Antiphon.Agents.Pty.Tests` including `PtyDeliveryCeilingsTests`, `ModernPtyDa1Tests`,
   `PtyLargeWriteTests`, `PtyBackendContractTests`, `HostSessionPipeTests` (cmd echoes, evidence
   fast) and `SessionMessageQueuePtyIntegrationTests` in `Antiphon.Tests` (queue path untouched).
5. **The S1 bar**: 20× isolation reruns of the pin plus the parent plan's S-final counts
   (5 consecutive green solo suite runs). `SendLineAsync_submits_a_turn_and_emits_idle_signal`
   (a B/C-cast member riding the same primitive) is expected to benefit; record, don't claim.
6. **Doc corrections in the same commit**: `Program.cs:90` ("comfortably above the gap" — it is
   not; describe the gate), `EchoGatedSubmit`'s doc (production now shares the semantics; the
   helper may later replace it, not in this slice), `VerifiedPromptSubmitter`'s pause comment.

## Build/run constraints (standing rules)

`--property:OutputPath=bin-c128a/` with a FORWARD slash, sweep `bin-c128a` dirs afterwards; never
co-schedule the two test projects; any new spawning test class takes
`[ParallelLimiter<ProcessSpawnLimit>]` (the deaf-start test spawns fakeclaude ⇒ it lives in
`FakeClaudeContractTests`, which already carries it).
