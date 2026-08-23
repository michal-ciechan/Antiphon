# CARD-0137 — overlay-aware normal delivery: fix design

**Date:** 2026-08-23 · **Card:** CARD-0137 (`92aa7757-507d-4988-a98a-dfc7ba90a9a2`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `9b5b1dc`. Every line/behaviour claim below was read out of the code on
that commit.

**Built on:** `docs/investigations/2026-08-23-card-0137-overlay-focus-normal-delivery-investigation.md`
(Grok, task `6e2ec08d`, commit `4657875`) — its live measurements against Grok 4.6 session
`1e4976d4` are treated as established fact and are not re-derived here. **Related:** CARD-0143 (the
narrow poll transport this card's gap is the negative image of), CARD-0141 (the Codex `/usage`
redemption hazard), CARD-0055/CARD-0024 (delivery verification), CARD-0056 (retype-before-evidence
is safe), CARD-0103 (the refund arm), CARD-0047 (why generic modal shape-matching is refused),
CARD-0082 (auto-compact, which ships a local command through this very path).

---

## Verdict up front

The card's framing — "an already-open overlay blocks re-sending that command" — is the *smaller* half
of what is actually wrong. Reading `DeliverAsync` against the catalog turned up a **third failure,
not in the investigation, that is live on master today and is irreversible**:

> **`POST /api/sessions/{id}/messages {"Body":"/usage"}` against a Codex session can redeem the
> account's one usage-limit reset, and nothing stops it.**
>
> `SubscriptionUsagePollContract.Forbidden` names Codex `/usage` with exactly that reason
> (`ProviderContractCatalog.cs:158-162`, "a `Mode:"Now"`-style send auto-confirms the highlighted
> option and can redeem the account's one usage-limit reset (CARD-0141)"). The contract's own doc
> comment says it is "Enforced by test AND at runtime" (`ProviderContract.cs:114`). It is enforced at
> runtime in **one** place — `TryPollLocalCommandAsync` (`SessionMessageQueueService.cs:2111`). The
> normal path never reads it. A `Mode:"Now"` send types `/usage`, the body renders (so composer
> evidence passes), Enter fires the highlighted option, and CARD-0055's confirm loop then re-presses
> Enter up to twice more into the picker before timing out and — if the agent is AlwaysOn — killing
> the session.

So the fix has three layers, and only the second is about overlays:

| # | Layer | Fixes |
|---|---|---|
| **L0** | **Refuse `Forbidden` bodies on the normal path**, before a byte is typed | the redemption hazard above |
| **L1** | **A local-command arm in `DeliverAsync`**: a declared local command skips CARD-0055's transcript-confirm, presses Enter **once**, and can never kill | the investigation's §4 "code-only" confounder — 3 Enters into an open overlay, then a kill |
| **L2** | **A bounded, evidence-gated overlay recovery on `NoComposerEvidence`**: pull → confirm idle → one Esc → re-type | the card's headline — a leftover overlay making the composer deaf to the next real message |

**Answers to the three questions the investigation left open:**

1. **Should `DeliverAsync` grow overlay-aware Esc handling?** Yes — but **reactively** (after composer
   evidence has already failed) and, optionally, **proactively behind a per-kind measured detector**.
   **Never as an unconditional Esc-before-send**, and **never gated on
   `SubscriptionUsagePollContract.OpensOverlay`** — that field is a fact about *one command per kind*
   and reusing it would re-create the exact narrowness this card exists to name. §2, §3, R1, R2.
2. **Should overlay-opening slash commands be rerouted onto `TryPollLocalCommandAsync`?** Reroute the
   **semantics**, not the **method**. That method is genuinely too narrow to reuse — it skips on
   pending messages, returns a screen buffer instead of a queue DTO, carries sweep-only `Navigation`,
   and Esc-closes the panel a human asked to see. Extract its core into one shared primitive and let
   both paths call it. §4, R3.
3. **Should a leftover overlay be detected before a real message is typed?** Yes, but only for modals
   somebody has actually measured, via a per-kind detector in the same family as
   `ClaudeBlockingPromptDetector` / `CodexTrustPromptDetector`. A *generic* "is that a modal?"
   shape-match is refused for CARD-0047's stated reason. §6, R7.

**Landing without S1's measurements, L2 is Grok-only.** That is deliberate and is called out as the
plan's main limitation (§8).

---

## 1. What is actually on master

`DeliverAsync` (`server/Application/Services/SessionMessageQueueService.cs:1182-1294`) is reached
from three call sites, all of which treat any verdict other than `Delivered` as a failure:

| Call site | Line | Holds the per-session lock? |
|---|---|---|
| `EnqueueAsync`, `Mode.Now` | `:163` | **No** — the lock is taken at `:181`, *after* this branch returns (§7) |
| `SendNowAsync` (queued send-now) | `:438` | Yes |
| `DeliverNextLockedAsync` (turn-end flush) | `:900` | Yes |

Its sequence: normalize → size-gate → snapshot the screen → capture the transcript baseline
(`confirmTranscript = baseline.Observable`, `:1248`) → `SendInputAsync(payload)` (`:1262`) →
`WaitForComposerEvidenceAsync`, else `NoComposerEvidence` with Enter withheld (`:1264-1270`) →
`SendInputAsync("\r")` (`:1278`) → `WaitForTranscriptConfirmAsync` if observable, else
`WaitForSequenceAdvanceAsync`.

A search for `\u001b`, `Esc`, or `OpensOverlay` in that file hits **only** the six lines inside
`TryPollLocalCommandAsync` (`:2122-2127`, `:2162-2163`, `:2178-2179`). The normal path has no
overlay concept at all.

The kill predicate is `HandleDeliveryFailureAsync:1794`:

```csharp
var kill = agent is { AlwaysOn: true } && !working && !allSupervision && !preFirstTurn;
```

A `Mode.Now` send passes `messageIds: null`, so `allSupervision` and `preFirstTurn` are `false` by
construction. **An idle AlwaysOn agent whose composer is behind somebody else's `/usage` panel is
killed by the next queued message, channel reply or task note that lands.**

Two facts that constrain everything below, both already in tree:

- **Esc (`\u001b`) is inert to the input bookkeeping.** `PendingTerminalInput.Append`
  (`AgentSessionRuntime.cs:1083-1112`) drops control characters and only sets `submittedCommand` on
  CR/LF, so an Esc starts no manual turn and adds nothing to `SessionInputLog` (CARD-0006's C4
  evidence). Sending it with `trackManualTurn: false` — as `TryPollLocalCommandAsync` already does —
  is belt-and-braces.
- **A local slash command through the queue is not hypothetical, and one of them works today.**
  `ContextCompactionService` enqueues `CompactTriggerBody` (`"/compact Focus the summary on: …"`)
  with `MessageSendMode.WhenIdle` (`ContextCompactionService.cs:193`). It survives CARD-0055's
  confirm *because Claude writes the raw typed `/compact …` text as a plain user record* — the same
  record CARD-0041 had to learn about — so there genuinely is a `UserPrompt` row to match, and the
  body is deliberately long enough to take the strong text-match arm (`ContextCompactionService.cs:30-37`).
  **Grok `/usage` and Codex `/status` write no such row** (CARD-0141 and CARD-0136 both measured 0
  transcript entries across their whole investigation).

That last pair is the crux: **"is this body a local command?" is not the question. "Does *this*
command on *this* kind produce a confirmable row?" is** — and the answer differs per (kind, command)
and is only ever known by measurement.

---

## 2. Why there is no unconditional Esc-before-send (Q1, first half)

The literal reading of Q1 — mirror CARD-0143 by Esc'ing at the top of `DeliverAsync` — is **rejected**.
Four independent reasons, any one of which is disqualifying:

1. **Esc is the interrupt key.** CLAUDE.md's own requirement: an Esc mid-turn makes Claude write
   `[Request interrupted…` with no TurnEnd (`TranscriptKinds.IsInterruptPrompt`), and a plain Esc
   during a text stream unwinds the turn outright (`ClaudeInterruptCanaryTests`). Codex renders the
   interrupt in its status line — `Working (Ns · esc to interrupt)` — which is what
   `CodexTurnScreenTracker` keys on.
2. **`DeliverAsync` runs against working sessions on purpose.** `Mode.Now` is the "type into the
   composer while it works, so it lands next" feature; `EnqueueAsync`'s Now branch (`:145-179`)
   checks live-ness and input-readiness and *never* checks `working`. An unconditional Esc would
   interrupt a live turn on every send-now — trading a rare, recoverable deafness for a common,
   destructive one.
3. **Two Escs are not one Esc twice.** In Claude, Esc-Esc opens the rewind/history picker. The
   terminal panel is a live second writer into the same pty (`POST /api/sessions/{id}/input`), so a
   prophylactic Esc can *create* the overlay it was meant to clear if an operator keystroke is in
   flight. Whatever ships must send **at most one Esc per delivery**.
4. **It is unmeasured on two of three kinds.** Esc-on-an-idle-composer is measured for **Grok only**
   (investigation §3.1, twice). Claude and Codex are guesses, and this file is full of the cost of
   guessing.

The cost argument is secondary but real: the overwhelming majority of deliveries land on a clean
composer, and an Esc + settle on each adds latency to every channel reply for a state that is rare.

**What survives:** an Esc sent only when the composer has **already proved itself deaf** (L2), and an
Esc sent only when a **measured detector positively identifies a known modal** (L3/S6). Both carry
the positive evidence this repo demands before typing into a live session.

---

## 3. The contract axes, and why not `SubscriptionUsagePoll.OpensOverlay` (Q1, second half)

**Reusing `SubscriptionUsagePollContract.OpensOverlay` is rejected.** It is scoped to the single
subscription-usage command per kind (`Command`, `Navigation`, `Forbidden` are its neighbours). Gating
general message delivery on it would mean "overlay handling applies to `/usage` and to nothing else"
— which is precisely the narrowness the investigation identified as the bug. It also asserts a fact
about a **command**, where L2 needs a fact about a **kind**: *is a single Esc a safe dismiss on an
idle session?* Those are different questions with different evidence.

**Two new axes on `ProviderContract`** (`server/Application/Dtos/ProviderContract.cs`), following the
file's stated rules — every kind declares every axis, `Unknown` is a valid complete answer, and
`Unknown` behaves as `Unsupported` for enabling machinery:

```csharp
/// <summary>
/// Mid-life overlay handling (CARD-0137). NOT the launch-time modal contract
/// (BlockingStartupModal) — this is about a modal a live, idle session is sitting behind.
/// </summary>
public sealed record TerminalOverlayContract(
    AgentTuiCapabilityState State,
    string Reason,
    /// <summary>Key sequence measured to dismiss an overlay AND to be a no-op on an idle empty
    /// composer. Null unless State is Supported. Sent at most ONCE per delivery.</summary>
    string? DismissKey,
    /// <summary>Screen fragments that positively identify a MEASURED overlay for this kind,
    /// matched by ComposerDeliveryEvidence.FragmentIsVisible. Empty = no proactive detector.</summary>
    IReadOnlyList<string> DetectFragments);

/// <summary>
/// Exact TUI-local command bodies for this kind, and what each is measured to do. The key is the
/// first whitespace-delimited token of the body, lowercased. Absence is not a claim of absence —
/// an undeclared /-prefixed body keeps the ordinary prompt-delivery path unchanged.
/// </summary>
public sealed record LocalCommandContract(
    AgentTuiCapabilityState State,
    string Reason,
    IReadOnlyDictionary<string, LocalCommandFact> Commands,
    /// <summary>Bodies nothing may ever type for this kind, with the reason. Moved here from
    /// SubscriptionUsagePollContract so there is ONE list and it governs every typing path.</summary>
    IReadOnlyDictionary<string, string> Forbidden);

/// <param name="WritesUserPrompt">Does submitting it produce a UserPrompt transcript row carrying
/// the typed text? This is the ONLY thing that decides whether CARD-0055's confirm can be used.</param>
public sealed record LocalCommandFact(bool OpensOverlay, bool WritesUserPrompt, string Evidence);
```

Catalog entries as the evidence stands today:

| Kind | `TerminalOverlay.State` | `DismissKey` | `LocalCommands` |
|---|---|---|---|
| **Grok** | `Supported` | `"\u001b"` | `/usage` → `OpensOverlay: true, WritesUserPrompt: false` (CARD-0136 + CARD-0137 §3.2) |
| **Codex** | `Unknown` until S1 | — | `/status` → `false, false` (CARD-0141). **`Forbidden["/usage"]`** moves here verbatim |
| **ClaudeCode** | `Unknown` until S1 | — | `/compact` → `false, **true**` (CARD-0041's raw typed record; this entry is the regression guard that keeps auto-compact on today's path) |
| **OpenCode / Raw** | `Unsupported` | — | empty |

`Forbidden` **moves** rather than being duplicated: two lists of "never type this" would drift, and
the one that drifts is the one nobody reads. `TryPollLocalCommandAsync:2111` and
`ProviderContractCatalogTests:81-83` are updated to read the new home; the sweep's behaviour is
unchanged.

---

## 4. L1 — the local-command arm, and why not `TryPollLocalCommandAsync` (Q2)

**Rejected: rerouting `POST /messages` onto `TryPollLocalCommandAsync`.** Read as shipped
(`:2084-2182`), it is a *sweep* transport, and four of its properties are wrong for an operator's
explicit send:

- It returns `Skipped("pending messages")` when anything is queued — correct for a background poll
  that must not jump the queue, silently wrong for a human who just pressed send.
- It returns `LocalCommandPollResult.Sent(Buffer)` — a screen buffer. The message API's contract is a
  `SessionQueueDto`.
- It carries `Navigation` and a `PanelTimeoutSeconds`, both meaningful only to the quota sweep.
- **It Esc-closes on the way out** (`:2178-2179`) — it would close the panel the operator sent
  `/usage` specifically to look at.

**Accepted: extract its core.** A new private primitive carries the part both paths need and nothing
else:

```csharp
// Type a local TUI command and prove the composer took it. No transcript confirm (a local command
// may write no UserPrompt row), ONE Enter (CARD-0141: a re-press lands on a picker's highlighted
// option), no incidents, no kill. Callers own everything else.
private async Task<bool> TypeLocalCommandAsync(Guid sessionId, string command, CancellationToken ct)
```

`TryPollLocalCommandAsync` is refactored onto it with **no behaviour change** (its Esc-before /
Esc-after / `Navigation` / buffer capture stay in the caller). `DeliverAsync` grows an arm that fires
only when the body's first token is a **declared** command for the session's kind:

```
if (fact is { WritesUserPrompt: false }):
      confirmTranscript = false          // there is no row coming; CARD-0055 would time out and kill
      one Enter, WaitForSequenceAdvanceAsync
      success -> DeliveryVerdict.Delivered
      failure -> DeliveryVerdict.LocalCommandNotAccepted
if (fact is { WritesUserPrompt: true }) or no declared fact:
      unchanged — today's path, byte for byte
```

Claude `/compact` therefore keeps CARD-0082's exact behaviour, which is the point of declaring it.

**Verdict plumbing, deliberately minimal.** Success stays `DeliveryVerdict.Delivered` so none of the
three `outcome.Verdict != DeliveryVerdict.Delivered` call sites (`:172`, `:445`, and the flush at
`:900`) needs to change. Two new failure verdicts join the enum at `:1092` and get `Describe()` text
at `:1119`, and the kill predicate at `:1794` gains one clause:

```csharp
var kill = agent is { AlwaysOn: true } && !working && !allSupervision && !preFirstTurn
    && verdict is not (DeliveryVerdict.ForbiddenBody or DeliveryVerdict.LocalCommandNotAccepted);
```

Nothing else about the queue bookkeeping moves: attempts charge normally, a local command that
repeatedly fails parks like anything else. **Parking is fine; killing is not** — the same distinction
CARD-0143 drew when it gave the poll transport a `NotAccepted` result with "Nothing queued, no
incident, no kill".

---

## 5. L0 — refusing `Forbidden` bodies (the urgent slice)

Two checks, because the contract already claims both:

- **At `EnqueueAsync`, both modes**, before anything is typed or persisted: first token in the kind's
  `Forbidden` ⇒ `ValidationException` ⇒ HTTP 400 carrying the reason string verbatim. A human sending
  Codex `/usage` gets told why, and the account keeps its reset.
- **At `DeliverAsync`, immediately before the first write**: the same check ⇒
  `DeliveryVerdict.ForbiddenBody`. This is the belt-and-braces arm for rows already in the queue when
  the fix lands, and for any future caller that reaches `DeliverAsync` without going through
  `EnqueueAsync`.

`ForbiddenBody` parks immediately (`DeliveryAttempts = MaxAttempts`, the shape `HandleTruncationAsync`
already uses — retrying a body we refuse to type is pointless) and raises a new
`AgentIncidentKind.ForbiddenTerminalBody = 31` at **Error**, never Critical, never killing. The enum
is stored as an int on an existing column, so **no migration**.

Matching is on the **first whitespace-delimited token**, lowercased, `/`-prefixed — not exact body
equality. `/usage --json` must be refused too, and `/compact <instructions>` must still match
`/compact`. First-token matching makes the refusal strictly broader and the diversion correctly
argument-tolerant.

---

## 6. L2 — overlay recovery on `NoComposerEvidence` (Q3)

The reactive arm, inserted between the failed evidence wait (`:1264`) and the `NoComposerEvidence`
return (`:1270`). It runs **at most once per delivery** and only when every one of these holds:

1. `verify` is on (an unverifiable session has no evidence to act on).
2. `ProviderContractCatalog.For(kind).TerminalOverlay.State == Supported` — i.e. somebody measured
   that this kind's `DismissKey` dismisses overlays *and* is a no-op on an idle empty composer.
3. `_runtime.CatchUpTranscriptAsync(sessionId, ct)` has just run, **and then** `IsWorkingAsync`
   returns false. The pull is not optional: CLAUDE.md's CARD-0055 rule is "never kill a session on
   'the transcript does not contain X' without PULLING the transcript first", and the same reasoning
   binds harder here — reading stale rows as idle and then sending Esc would *interrupt* a live turn
   rather than merely mis-report one. `GraceConfirmAsync:1399` already does exactly this pull
   (`:1416`) on the failure path; L2 moves it a few lines earlier.

Then: one `DismissKey` write with `trackManualTurn: false` → wait `OverlaySettleMs` (400, shared with
the poll) → **re-snapshot the screen** (the post-Esc screen is the correct `screenBefore`; a stale one
would feed `ComposerDeliveryEvidence`'s paste-placeholder arms the overlay's rows) → re-type the body
→ `WaitForComposerEvidenceAsync` again. Success continues the delivery normally; failure returns
today's `NoComposerEvidence` with today's consequences.

**Re-typing is legal here, and the licence is already written down.** CLAUDE.md, on CARD-0056's
`SendBootPromptWithRetryAsync`: *"re-typing is safe here specifically because the exception means no
composer evidence appeared, i.e. the same check that would gate an Enter says the composer does not
hold the body, so a retry cannot double-submit — CARD-0055's never-re-type rule governs the phase
*after* evidence, this is the phase *before* it."* L2 sits in exactly that phase: Enter was withheld
at `:1270`, so nothing was submitted and nothing can be double-sent.

**The permission-dialog case is the one that must not go wrong**, and the idle gate is what handles
it. A session parked on a tool-permission modal is mid-turn — no TurnEnd has been written — so
`IsWorkingAsync` reads **working**, condition (3) fails, and no Esc is sent. This is not incidental;
it is the reason the gate is `working`-based rather than "did the composer refuse us". It gets its own
test (§9, case 4) and its own line in §10's risks.

**Proactive detection (S6)** is the same machinery run *before* typing instead of after: match the
`before` snapshot `DeliverAsync` already takes (`:1231-1232`) against `TerminalOverlay.DetectFragments`
with `ComposerDeliveryEvidence.FragmentIsVisible` — the same normalisation the evidence check uses, so
box-drawing and wrapping cannot break it. On a match **and** an idle session, Esc first and skip the
doomed typing entirely. It is strictly narrower than L2 and exists for one reason L2 cannot cover:
typing an arbitrary body into an unknown modal **presses its keys**. Grok's panel footer offers
`c copy session ID`; another kind's modal may offer something that is not free. S6 is the only layer
that prevents that, and it can only ever cover modals someone has measured.

---

## 7. The `Mode.Now` lock gap (adjacent, separable)

`EnqueueAsync`'s Now branch calls `DeliverAsync` at `:163`, **before** the lock is taken at `:181`.
`TryPollLocalCommandAsync` holds the lock throughout (`:2091`) precisely so "a poll cannot race a real
message" — but a Now-mode send is exactly the real message it cannot exclude. On master the collision
is two bodies interleaving in one composer. Once Esc is in play on both paths it gets sharper: a Now
send's Esc can land between the poll's type and its Enter, or vice versa.

**Recommendation: take the per-session lock for `Mode.Now`.** It is a five-line change, and it makes
the poll transport's stated invariant true. Two implementation cautions: `DeliverAsync` must not take
the lock itself (`:438` and `:900` already hold it), and `HandleDeliveryFailureAsync` is already
called *inside* the lock at `:445`, so calling it inside the Now branch's lock is consistent and
non-reentrant. Kept as its own slice (S7) because it changes when a send-now blocks, which is a
behaviour change in its own right.

---

## 8. Slices

| # | Slice | Depends on | Notes |
|---|---|---|---|
| **S1** | **Measure Esc-on-idle and capture detector fragments, per kind.** One `[Explicit]` headed canary each for Claude and Codex: on an idle session with an empty composer, write one `DismissKey`, assert the rendered screen is unchanged after settle and that a body typed afterwards still renders. Then open a known overlay (Claude `/model`) and assert one Esc restores the composer. Grok is already measured (investigation §3.1, twice) and needs only a CI-side mirror. | — | **Gates S5/S6 per kind.** Without it those arms are Grok-only and Claude/Codex stay `Unknown`. |
| **S2** | **Contract axes.** `TerminalOverlayContract` + `LocalCommandContract` + `LocalCommandFact` on `ProviderContract`; entries for all five kinds; move `Forbidden` off `SubscriptionUsagePollContract` and repoint `:2111` and the catalog tests. **No behaviour change.** | — | Pure, DI-free, fast to test. |
| **S3** | **L0 — refuse `Forbidden` bodies.** `EnqueueAsync` pre-check (both modes) + `DeliverAsync` runtime refusal → `ForbiddenBody`, park, `ForbiddenTerminalBody = 31`, never kill. | S2 | **Land this first.** It closes an irreversible, live hazard and depends on nothing else. |
| **S4** | **L1 — extract `TypeLocalCommandAsync`; local-command arm in `DeliverAsync`.** `TryPollLocalCommandAsync` refactored onto it with no behaviour change. `LocalCommandNotAccepted` verdict; kill predicate clause. **No Esc-after-success** (R6). | S2 | |
| **S5** | **L2 — overlay recovery on `NoComposerEvidence`.** Pull → recompute working → one Esc → settle → re-snapshot → re-type → re-evidence. One-shot. New `DeliveryVerificationSettings.OverlayRecoveryEnabled` (default `true`) and `OverlaySettleMs` (400). | S2, S1 per kind | The card's headline fix. |
| **S6** | **L3 — proactive detector.** Match `DetectFragments` against the pre-typing snapshot; Esc before typing when matched **and** idle. | S5, S1 per kind | Optional. Only layer that stops a body being typed *as keys* into a modal. |
| **S7** | **`Mode.Now` takes the per-session lock.** | — | Separable; §7. |

Recommended landing order: **S2 + S3 as one commit**, then S4, then S1, S5, S6, S7.

---

## 9. Test coverage

**`ProviderContractCatalogTests`** (existing) — every kind declares both new axes; a `Supported`
`TerminalOverlay` implies a non-null `DismissKey`; Codex `Forbidden["/usage"]` still names the reset
(assertions move, not weaken); **Claude `/compact` is declared `WritesUserPrompt: true`** — this is the
regression guard that keeps CARD-0082 on the unchanged path.

**`SessionMessageQueueDeliveryVerificationTests`** (existing, in-process test adapter + fake
`TimeProvider`) — seven new cases:

1. A `Forbidden` body is refused with **zero bytes written to the adapter**, parks immediately, raises
   `ForbiddenTerminalBody`, and does **not** kill an AlwaysOn agent. (Assert on bytes, not on the
   verdict alone — the whole point is that nothing was typed.)
2. A declared `WritesUserPrompt: false` command sends **exactly one `\r`** and never enters the
   confirm loop — asserted by advancing the fake clock past `TranscriptConfirmTimeoutSeconds` and
   showing the call already returned.
3. A declared `WritesUserPrompt: true` command (`/compact …`) takes the **unchanged** path: transcript
   confirm runs, Enter re-presses happen. Pins that S4 did not touch auto-compact.
4. **`NoComposerEvidence` on a *working* session sends no Esc**, on every kind, including one whose
   contract is `Supported`. The permission-dialog guard.
5. `NoComposerEvidence` on an *idle* session of a `Supported` kind sends **exactly one** Esc, re-types,
   and succeeds — and on an `Unknown` kind sends none.
6. Recovery is one-shot: two consecutive evidence failures produce **one** Esc in total.
7. `LocalCommandNotAccepted` never kills an AlwaysOn idle agent — CARD-0143's kill-predicate test,
   inverted.

**`SessionMessageQueuePtyIntegrationTests`** (through a real ConPTY) — a new opt-in fakeclaude knob
pair modelling the measured shape: `ANTIPHON_FAKE_OVERLAY_ON_COMMAND=/usage` (after this command,
render a panel and **discard** every typed byte) and dismissal on Esc (restore the composer). Default
OFF, following `ANTIPHON_FAKE_DEAF_START_MS` (`src/Antiphon.FakeClaude/Program.cs:71-76`) — the
closest existing sibling, and deliberately *not* the same thing: deaf-start **buffers** input and
processes it late, an overlay **consumes and discards** it. Test: send `/usage`, then send a real body
→ **red without S5** (409 `NoComposerEvidence`), green with it.

**`FakeClaudeContractTests`** — pin the knob's own behaviour (typed bytes vanish while the overlay is
up; one Esc restores the composer; the panel renders), so the model cannot drift from what CARD-0137
measured. Same discipline as the clip and swallow-Enter knobs.

**Headed canaries, `[Explicit]`** — `GrokUsageOverlayCanaryTests` plus the S1 Claude/Codex pair. These
are what the contract's `Supported` claims rest on, and they are what goes red when a TUI upgrade
moves the fragments.

**Client:** no changes. **Migration:** none.

---

## 10. Rejected alternatives

| | Alternative | Why not |
|---|---|---|
| **R1** | Unconditional Esc-before-send in `DeliverAsync` (Q1 as literally posed) | §2. Esc is the interrupt key; `Mode.Now` legitimately targets working sessions; Esc-Esc opens Claude's rewind picker; unmeasured on two of three kinds; pays on every message for a rare state. |
| **R2** | Gate on `SubscriptionUsagePollContract.OpensOverlay` | §3. A fact about one command per kind, used to govern all delivery, would re-create the exact narrowness this card names — and would make a record called `SubscriptionUsagePoll` own general delivery behaviour. |
| **R3** | Reroute onto `TryPollLocalCommandAsync` (Q2 as literally posed) | §4. Skips on pending messages; returns a buffer not a queue DTO; sweep-only `Navigation`; Esc-closes the panel the operator asked to see. Its **core** is extracted instead. |
| **R4** | Refuse all `/`-prefixed bodies through the queue | Breaks CARD-0082: auto-compact ships `/compact …` through `EnqueueAsync` WhenIdle today and it works. |
| **R5** | Use the `/` prefix as the local-command discriminator | `/compact` writes a confirmable row and `/usage` does not. The fact is per (kind, command) and measured. CARD-0041 already settled that raw `/`-prefixed text is not a classifier ("matching raw `/`-prefixed text stays REJECTED"). |
| **R6** | Esc-close after a *successful* overlay-opening command, mirroring CARD-0143 | It cleans up only the overlays Antiphon opened — while the incident CARD-0137 actually measured was an overlay **the operator opened by hand in the terminal panel**. It would also close the panel a human explicitly asked to see. L2 covers both origins. |
| **R7** | A generic overlay detector (any box-drawn modal, any "Esc close" footer) | CARD-0047 refused exactly this class: "the generic numbered-menu arm is too weak a shape-match to type into a live session on". Per-kind measured fragments only. |
| **R8** | Widen `EvidenceTimeoutSeconds` so the overlay "has time" | The composer is not slow, it is deaf. Buffer SHA and sequence were byte-identical across the whole 15.6 s (investigation §3.3). This is the "quietly widen a timeout" anti-pattern. |
| **R9** | Stop killing on `NoComposerEvidence` altogether | A genuinely wedged composer is what the kill is for. Remove the false positive, not the brake — CARD-0056's shape. |
| **R10** | Detect the overlay from the transcript instead of the screen | A local command writes no transcript row. The screen is the only signal there is. |

---

## 11. Risks and open items

- **Esc into a permission dialog.** Handled by L2's idle gate (a session on a permission modal is
  mid-turn, so `working` is true), but it is the single most important guard in the design and the
  reason condition (3) pulls the transcript before reading `working`. Test §9 case 4.
- **`DetectFragments` drift.** A TUI upgrade moves the footer text and S6 silently stops detecting —
  failing *safe* (back to L2's reactive arm), but silently. The headed canaries are the alarm.
- **Grok-only on landing.** Without S1, `TerminalOverlay.State` stays `Unknown` for Claude and Codex
  and L2/L3 do nothing there. Honest, and visible in the catalog and on the contract surface — but it
  means the card is not fully closed until S1 runs.
- **The operator's own terminal panel is an unsynchronised second writer.** S7 fixes the races between
  Antiphon's own paths; a human typing into the panel mid-delivery cannot be locked out and is out of
  scope.
- **Unmeasured:** what Enter does inside Grok's `/usage` panel (CARD-0141 measured the Codex picker,
  which is why Codex `/usage` is forbidden; the Grok panel's Enter is simply unknown). S4 removes the
  re-press that would have found out the hard way; nobody should go measure it on a live account.
- **Not in scope:** whether other Grok/Claude commands (`/help`, `/model`, `/config`) open overlays.
  The catalog declares only what is measured, and an undeclared command keeps today's behaviour
  exactly. Adding one is an S1-shaped measurement, not a code change.
