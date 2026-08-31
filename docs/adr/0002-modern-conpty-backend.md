# ADR 0002: Ship a modern ConPTY backend behind a flag

## Status

Accepted (step 1 of CARD-0037; the flag defaults OFF and the ceilings are untouched)

## Date

2026-08-12

## Context

A pseudoconsole is served by a **conhost binary**, and the one `CreatePseudoConsole` in `kernel32`
launches is `%SystemRoot%\System32\conhost.exe` — 10.0.19041.1 on this machine, a 2020 build that
**strips `ESC[200~`/`ESC[201~` out of written input before the child sees them**. Claude therefore
receives every body we write as a burst of *typing*, and its composer keeps one ~1 KB read chunk per
event-loop turn and discards the rest. That is CARD-0027's clipping, and the shipped mitigations
(`BriefInlineMaxBytes`, spill-to-file + pointer, the oversize incident) are all sized for it.

CARD-0030 established that the conhost is the discriminator: the identical write through a modern
`conpty.dll` + `OpenConsole.exe` delivers the markers byte for byte, and real Claude then accepted
**86 400 bytes in a single write with zero loss, 2/2**, versus 8–26 % on the inbox binary. Its control
(same modern binary, markers removed → 75 % loss, 3/3) rules out any other property of the newer
build. Details and the two ways that bench lied before its controls existed:
`docs/investigations/2026-08-12-bracketed-paste-conhost-CARD-0030.md`.

`Porta.Pty` — our pty library — resolves `CreatePseudoConsole` through Vanara's `kernel32.dll`
import, so it cannot be pointed at another module.

## Decisions

### 1. Our own host, not a Porta.Pty fork, and not a resolver hack

`ModernConPtyConnection` loads `CreatePseudoConsole`/`ClosePseudoConsole`/`ResizePseudoConsole` out
of the shipped `conpty.dll` and does the spawn itself.

- **A resolver hack does not work.** `NativeLibrary.SetDllImportResolver` is per-*assembly*: to
  redirect Porta's `CreatePseudoConsole` we would have to redirect every other `kernel32` import in
  the same assembly, and `conpty.dll` exports none of them.
- **A fork is disproportionate.** It means owning a cross-platform package — Linux and macOS native
  shims included — and re-vendoring on every upstream bump, to change one function resolution on
  Windows.
- **We already had the host, measured.** `tests/Antiphon.Agents.Pty.Tests/ConPtyHost.cs` (CARD-0030)
  is ~250 lines and complete; this is that host narrowed to the production shape.

The new backend is hidden behind `IPtySession`, a four-member interface that `PtyAgentRunner` talks
to and that Porta's connection is adapted into (`PortaPtySession`). The seam exists because Porta's
`PtyExitedEventArgs` has an **internal** constructor, so no outside assembly can implement
`IPtyConnection` and raise its exit event — reflecting around that would have been worse.

The spawn deliberately mirrors `Porta.Pty.Windows.PtyProvider` step for step: environment merge
(empty value = unset), `GetAppOnPath` PATH resolution including the WoW64 Sysnative swap, argument
quoting, `bInheritHandles=false`, **`STARTF_USESTDHANDLES` with all three handles NULL**,
kill-on-close job object, unbuffered `FileStream`s over the pipe handles, and the documented teardown
order. Anything that differs beyond which module provides `CreatePseudoConsole` is a bug in that
file. The `STARTF_USESTDHANDLES` line in particular is load-bearing and was measured, not copied:
without it the child loses the pseudoconsole attach to the parent's own std handles whenever the
parent's stdio is redirected — which is every daemon and every test host here — and comes up on the
parent's pipes with `isTTY` false while the pty sits empty.

### 2. One switch, process-wide, inherited — the runner and the pty-hosts move together

`ANTIPHON_PTY_BACKEND` (`inbox`, the default, or `modern`), also settable as
`SessionRunner:PtyBackend` in appsettings, which the runner exports into its own environment at
startup. `PtyHostLauncher` starts hosts with `UseShellExecute=false` and no environment override, so
every detached pty-host inherits the runner's choice for free.

They move **together** because they all deliver bodies sized against **one** set of ceilings. A
per-session backend would make `BriefInlineMaxBytes` correct for some sessions and a data-loss bug
for others, with nothing downstream able to tell which kind it was holding.

**Tests move independently**, by construction: the contract *is* the pair (the inbox conhost strips
the markers, the shipped one delivers them), so both have to be pinnable in one process.
`PtyAgentRunner` therefore takes a per-instance override rather than only reading the environment.

The **E2E fixtures** need nothing of their own. `AntiphonAppFixture` hosts the server in-process
(`WebApplicationFactory`), so the `PtyAgentRunner` its adapters create reads the *test process's*
environment: unset in CI, so E2E keeps measuring the default, and a run that wants the modern
backend sets the same one variable. That is the same switch, applied at a different process — not a
second mechanism.

### 3. Missing redistributable falls back to the inbox conhost, ceilings still in force

`PtyBackendPolicy.Resolve` reports `FellBack` and the runner logs it at Warning. A `conpty.dll`
**without** a sibling `OpenConsole.exe` is treated as a MISS, not a hit: in that state conpty.dll
silently falls back to the inbox conhost, which would put us back on the stripping binary while
claiming the modern backend.

The consequence is deliberate and is the most important line on the card: **the ceilings cannot
simply be deleted even after the modern path ships**, because some machine will not have the binary.

### 4. A known binary, with provenance, restored rather than vendored

| | |
|---|---|
| Package | `Microsoft.Windows.Console.ConPTY` **1.24.260710001** (Microsoft-signed, MIT, `github.com/microsoft/terminal`) |
| Source | `https://api.nuget.org/v3-flatcontainer/microsoft.windows.console.conpty/1.24.260710001/microsoft.windows.console.conpty.1.24.260710001.nupkg` |
| `conpty.dll` | `runtimes/win-x64/native/conpty.dll`, 109 920 B, sha256 `39fba2713e2495117b1591ae8c32a3b904bea7aa66069cf7815e2844c76d75d8` |
| `OpenConsole.exe` | `build/native/runtimes/x64/OpenConsole.exe`, 1 066 296 B, sha256 `b7fd936c2668b87b9ecf7b3366dc6568afc1c6f981874cba3e955a1c35cf8160` |
| File version | 1.24.2607.10001 — the same build Windows Terminal 1.24 ships |

The package is referenced with `ExcludeAssets="all" GeneratePathProperty="true"` and consumed purely
as a file source: its own MSBuild targets key off `$(PlatformTarget)` and stage `OpenConsole.exe`
into an architecture subdirectory, which is not the layout we ship. Both files are staged **side by
side** into `conpty\win-x64\` as `Content`, so they flow to every referencing project's output
(pty-host, session runner, server, tests) and each resolves them from its own
`AppContext.BaseDirectory`. Measured: this `conpty.dll` accepts either `<dir>\OpenConsole.exe` or
`<dir>\x64\OpenConsole.exe` and prefers the sibling when both exist.

Only `win-x64` is staged — the only architecture whose envelope has been measured. Anywhere else the
locator reports "not shipped" and the fallback in decision 3 applies.

`ConPtyRedistributable.VerifyShippedHashes` pins both files, and
`PtyBackendContractTests.The_shipped_binaries_are_the_ones_with_recorded_provenance` asserts it: a
package bump that changes the binary has to be a deliberate re-measurement of the envelope, not a
silent build-time swap. This is also why the binary is **not** scavenged out of a Rider install the
way the investigation had to.

`ShadowCopyStore` carries the pair explicitly. Its copy is filtered to the pty-host's `deps.json`
dependency closure, and a `Content` item appears nowhere in `deps.json`; without the two names added
to the closure, every detached host would silently run on the inbox conhost.

## The measurement this shipped with (CARD-0037 step 2)

`PtyPasteMarkerExperiments.Real_claude_delivery_envelope`, headed, 2026-08-12, this machine
(Windows 10.0.19045), real Claude, no model turns — the body is pasted into the composer and never
submitted, and the composer's own `[Pasted text #N +M lines]` counter is the oracle. The
`production-*` rows go through `PtyAgentRunner` + `PtyInputEncoding` — the objects that ship.

| case | body | write | captured | verdict |
|---|---|---|---|---|
| production, modern backend | 16 200 B (600 lines) | one | 600 / 600 | **WHOLE**, 2/2 |
| production, modern backend | 43 200 B (1 600 lines) | one | 1 600 / 1 600 | **WHOLE**, 2/2 |
| production, modern backend | 86 400 B (3 200 lines) | one | 3 200 / 3 200 | **WHOLE**, 2/2 |
| **production, inbox control** | 43 200 B | one | **413 / 1 600 (25 %)** | **LOST** |
| bench host, modern backend | 16 200 / 43 200 / 86 400 B | one | full | WHOLE, 2/2 each |
| modern backend, paced 1 KB / 25 ms | 43 200 B | 43 writes | 1 600 | WHOLE |
| modern backend, paced 1 KB / 25 ms | 86 400 B | 85 writes | **0** | **NOTHING** |

Three things follow:

1. The shipped path reproduces CARD-0030's headline on the production objects, not just on the
   bench: **86 400 bytes in a single write, zero loss**, against a shipped inline ceiling of 900.
2. The inbox control taken in the same run, through the same code, is 25 % — so the table carries its
   own before/after and the coupling between the ceilings and the conhost is visible in one place.
3. **Paced writes inside one paste remain unsafe**, and now with a second data point: 43 200 B paced
   was whole, 86 400 B paced read NOTHING, while the same 86 400 B in one write was whole twice. Any
   step-3 ceiling must be a *single-write* ceiling.

## Consequences

- Default behaviour is unchanged: flag unset ⇒ Porta.Pty ⇒ kernel32 ⇒ inbox conhost, ceilings and
  all.
- `PtyAgentRunner.Backend` records the resolved decision; the runner logs it once at startup and each
  pty-host logs it per session. A silent fallback is the one failure mode that is invisible from
  everywhere else, right up until a body over ~1 KB is clipped.
- Steps 3 and 4 of CARD-0037 (raising `BriefInlineMaxBytes`, retiring spill-and-pointer, giving
  fakeclaude a non-clipping bracketed-paste arm) remain **gated on measurements taken with the flag
  ON**. Raising the ceilings without the binary re-opens the original bug.

## Step 3 — the backend is ON here, and the ceilings follow the pty (2026-08-12)

The flag still defaults OFF in code. What changed is this deployment: the session runner asks for
`modern` in its own `appsettings.json` (its detached pty-hosts inherit that through the environment
it exports), and the AppHost sets `ANTIPHON_PTY_BACKEND=modern` on the server. A standalone server
started without that variable resolves `inbox` and keeps every old ceiling — which is the point.

**The ceilings are resolved per delivery, from the backend actually serving the pty**, by
`PtyDeliveryProfile` (`server/Application/Services`). Two facts have to agree before the raised set
is used:

1. this process's own `PtyBackendPolicy.Resolve()` — which is also what its in-proc pty adapters
   spawn under, so the profile can never disagree with the ptys the server itself creates; and
2. the session runner's answer to `GET /capabilities`, added here for the purpose. The runner is a
   separate process with separate config and its pty-hosts inherit ITS environment, so a server told
   `modern` in front of an inbox runner would type 43 KB briefs into a pty that clips at 1 KB — the
   original failure, restored, with the logs claiming otherwise. A runner that answers
   `InboxConhost` downgrades the ceilings and says why; a runner that cannot answer (an older build,
   an in-proc client, every test fake) is no evidence either way and leaves fact 1 standing.

| ceiling | inbox conhost | modern conpty | where the number comes from |
|---|---|---|---|
| `BriefInlineMaxBytes` | 900 B | **43 200 B** | whole 2/2 on both the bench and the production path, and the only size that also survived a *paced* delivery — a 2x margin under the largest single write measured |
| `ReplyInlineMaxChars` | 3 000 | **14 400** | 43 200 / 3: this ceiling counts UTF-16 chars, the envelope counts UTF-8 bytes, and an em-dash costs 3 |
| oversize tripwire | 1 024 B | **86 400 B** | the largest body measured to arrive whole, 2/2, single write, production path |

Three constraints from the measurement, all honoured:

- **Every raised ceiling is a SINGLE-WRITE ceiling.** 86 400 B paced read NOTHING while the same
  86 400 B in one write was whole twice. Our path does not split a body: the queue issues one
  `SendInputAsync`, the pty-host frame carries it whole (16 MB cap), and `PtyAgentRunner` does one
  `WriteAsync`. That has to stay true for these numbers to mean anything.
- **The oversize incident is kept as a tripwire, not removed** — only moved to the edge of the
  evidence. A body past 86 400 B is past everything anyone has measured and still raises
  `OversizedTerminalDelivery`; on the paste path the wording changes, because an abandoned paste
  leaves nothing rather than a fragment.
- **No ceiling is above what was measured**, and the brief ceiling is deliberately half of it,
  because the envelope is one machine and one Claude version.

The pointer path is *not* deleted. At 900 bytes every real brief spills (the reporting contract
alone is 838 bytes, so `BuildBrief`'s floor is ~915) — that is the state a machine without the
redistributable stays in, and `DelegationBriefCeilingPtyTests` still drives it.

### What a working paste does to delivery verification

`ComposerDeliveryEvidence` had a hole that would have fired on every large delivery the moment the
markers started arriving. A real paste is COLLAPSED by the composer to `[Pasted text #N +M lines]`
and the body is not rendered at all — no head, no tail, no fragment of a line — so head-or-tail
matching finds nothing, the queue withholds the submitting Enter, reverts the message, and for an
always-on agent kills the session as wedged. The placeholder arm already existed but counted
occurrences on the *rendered screen*, which holds only the visible rows: a tall paste pushes the
previous placeholder off the top as it draws its own, leaving the count unchanged. Placeholders are
now identified by their `#N` (Claude numbers them per session and never reuses one), with the count
comparison kept underneath as a fallback.

### Step 4 — fakeclaude models both paths

The clip model (CARD-0028) is a model of **typed** input, which is all the inbox conhost can
deliver. fakeclaude now decides the input path before the clip model sees a byte: content inside a
bracketed paste is exempt, and paste MODE persists from `ESC[200~` to `ESC[201~` across bursts
because ConPTY splits one write over several reads. `ANTIPHON_FAKE_PASTE_PLACEHOLDER` (opt-in)
additionally renders the collapsed placeholder instead of echoing the body, so the verification path
above has a CI-runnable peer that can exhibit it. It stays opt-in: the line count at which real
Claude collapses a paste has not been measured, and defaulting it on would model a guess.
`FakeVsRealClipParityTests` now has an arm for each path — the typed one still has to lose a chunk in
both peers, the pasted one must lose nothing in both, with clipping armed.

## The modern backend has a startup handshake, and answering it is part of the contract (CARD-0048, 2026-08-16)

This ADR owns "what the modern backend is", and it turned out to be one fact short. `OpenConsole.exe`
opens by writing **`ESC[c`** (DA1, primary device attributes) into the pty and **holds the console
client** until either a DA1 response arrives on the input pipe or **~3.0 s** expires. The inbox
conhost asks nothing and waits for nothing. Nothing in our stack answered, so from the day this
backend went on in this deployment **every child on it did nothing at all for three seconds** before
executing its first instruction.

Proven, with the controls that could have falsified it:
`docs/investigations/2026-08-16-modern-conpty-da1-stall-CARD-0048.md`. The load-bearing rows: the
child unblocks **16 ms after the reply, whenever it arrives** (538 / 1532 / 2520 ms → 554 / 1548 /
2536 ms), so the 3.0 s is a timeout on that wait and not an unrelated init timer; an arbitrary
printable byte and a cursor-position report do **not** unblock it, so it is the DA1 response
specifically; a bat that touches a file before its first `echo` touches it at 3048 ms, so the
**client** is held rather than its output buffered; and `CreatePseudoConsole` flags 0/1/2/4/8 all
stall, so there is no cleaner lever on the create call.

**The decision: `ModernConPtyConnection` answers `ESC[?1;0c` once per session.** The connection that
introduced the query owns the answer; the Porta path has no responder at all, not a disabled one, so
the default backend stays byte-identical. `Da1StartupResponder` is a byte state machine on a
transparent tap over the output pipe — every byte still reaches the snapshot, the screen and the
audit unmodified — and the reply goes out as one write on a **dedicated** `FileStream` over the input
handle, never the instance `PtyAgentRunner` writes through, so the single-write ceilings above are
unaffected.

Three things about that answer are decisions, not defaults:

- **The string is `ESC[?1;0c` ("VT101, no options") because it is the only one measured to work**
  (43 ms vs 3061 ms) **and because it is true.** The DA1 response describes the *hosting* terminal,
  and ours is `PtyAgentRunner`'s scraper: no sixel, no soft fonts, no rectangular editing, and it
  ignores the `ESC[?9001h` win32-input-mode request. The risk is asymmetric — claiming too much
  invites OpenConsole to emit sequences `TerminalScreen` cannot parse, degrading every snapshot-based
  detector silently. **Never claim sixel (`4`).** If a future package needs a richer claim, capture
  what a real Windows Terminal sends or read it out of the `microsoft/terminal` source at the pinned
  version; do not guess.
- **Only the FIRST query is answered.** The startup query is guaranteed to be the first `ESC[c` on
  the pipe *because of the defect* — the child is frozen until it is answered, so nothing else can
  have written yet — and that one was measured to be consumed by the pty's input state machine and to
  never reach the child. A later `ESC[c` could be a child's own query forwarded by OpenConsole, and
  answering that one **would** reach the child and change what the TUI negotiates. Later queries are
  counted (`Da1QueriesSeen`) and left alone.
- **Marker passthrough was re-checked against this claim before merging**, because it is the one
  thing we depend on OpenConsole for and a device-attributes claim is exactly the sort of thing a
  console host adapts its translation to. With the responder active:
  `PtyBackendContractTests` 9/9 (including the production write path delivering `ESC[200~`/`ESC[201~`
  on modern), `PtyBracketedPasteContractTests` 2/2, `FakeClaudeContractTests` 32/32 with its modern
  paste arm green three times, `PtyDeliveryCeilingsTests` 9/9. **The ceilings in the tables above
  stand unchanged.**

**The quiet-window constants did not move, deliberately.** `CodexReadyQuietPeriodMs` 1000,
`CodexDoneQuietPeriodMs` 3000, `ClaudeReadyQuietPeriodMs` 5000, `ReadyGrace` 500 ms and
`TurnQuietPeriod` 2 s are all correct against a pty that starts its child promptly, and with DA1
answered there is no configuration left that does not — fixed modern starts in ~40 ms, inbox never
stalled, and a modern request that falls back runs inbox. Widening them would have hidden the live
Codex-ready exposure instead of fixing it. The enforcement that replaces a settings validator is
empirical: `ModernPtyDa1Tests` pins first child output **under 2.5 s against a 3.0 s stall floor**, so
a future ConPTY bump that introduces a new handshake goes red before any readiness window silently
becomes a coin flip.

Two consequences worth stating outright. **CARD-0049 was never an adoption defect** — a 3 s frozen
start pushed the child's exit past the test's 4 s adoption point; it is now regression-locked by
`PtyHostAdoptionTests.Exit_while_runner_down_is_collected_on_adoption_on_the_modern_backend`, which
also pins the fix through the detached pty-host and the shadow-copy path. And **any new code path
that creates a modern pseudoconsole without going through `ModernConPtyConnection` re-inherits the
3 s frozen client**, with no symptom except slowness and every sub-3 s quiet window reading the stall
as a settled session.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #42

- **The binary is now shipped, behind a flag that DEFAULTS OFF** (CARD-0037 step 1; ADR `docs/adr/0002-modern-conpty-backend.md`): `Microsoft.Windows.Console.ConPTY` 1.24.260710001 is restored by `Antiphon.Agents.Pty.csproj` and staged, hash-pinned, into `conpty\win-x64\` of every output. Set **`ANTIPHON_PTY_BACKEND=modern`** (or `SessionRunner:PtyBackend`, which the runner exports into its own env so detached pty-hosts inherit it) and `PtyAgentRunner` spawns through `ModernConPtyConnection` — our own host, loading `CreatePseudoConsole` out of that DLL — instead of Porta.Pty/kernel32. Unset, or on a machine without the pair, it is the inbox conhost **with every existing ceiling still in force**, which is why the ceilings cannot simply be deleted. A "modern" request that fell back is invisible from everywhere else, so `PtyAgentRunner.Backend` records the decision, the runner logs it at startup (Warning on fallback) and each pty-host logs it per session. `ShadowCopyStore`'s deps.json closure names the two files explicitly — a `Content` item is in no deps.json, and dropping them would silently put every detached host back on the stripping binary. Pinned by `PtyBackendContractTests` (default-off, provenance hashes, the child's console host really is our `OpenConsole.exe`, and the production write path delivering the markers) and `ShadowCopyStoreTests.Shipped_conpty_binaries_survive_the_deps_json_closure_filter`.

### Preserved Gotcha #43

- **The ceilings are now CONDITIONAL on the backend, and this deployment is on `modern`** (CARD-0037 steps 3-4; same ADR): the flag still defaults OFF *in code*, but `src/Antiphon.SessionRunner/appsettings.json` asks for `modern` (detached pty-hosts inherit it) and the AppHost sets `ANTIPHON_PTY_BACKEND=modern` on the server. `PtyDeliveryProfile` (server) resolves which ceilings are in force and it needs TWO agreeing facts: this process's own `PtyBackendPolicy.Resolve()` **and** the session runner's `GET /capabilities` — the runner is a separate process whose pty-hosts inherit ITS environment, so a server told `modern` in front of an inbox runner would type 43 KB into a pty that clips at 1 KB. A runner that answers `InboxConhost` downgrades and logs why; one that cannot answer is no evidence and leaves the local decision standing. Inbox: brief 900 B / reply 3 000 chars / tripwire 1 024 B (unchanged). Modern: **43 200 B / 14 400 chars / 86 400 B**, all measured, all **SINGLE-WRITE** ceilings — the same 86 400 B delivered paced read NOTHING, so nothing may split a body on its way to the pty (queue: one `SendInputAsync`; host frame: whole; `PtyAgentRunner`: one `WriteAsync`). The oversize incident is **kept**, moved to the edge of the evidence, and the spill-and-pointer path is **not deleted** — it is what a machine without the redistributable still runs. Pinned by `PtyDeliveryCeilingsTests`.

### Preserved Gotcha #44

- **A working paste renders as a placeholder, and delivery verification had to learn it** (CARD-0037 step 3): the composer collapses a real bracketed paste to `[Pasted text #N +M lines]` and shows NONE of the body, so `ComposerDeliveryEvidence`'s head-or-tail match finds nothing and every large delivery would report "no composer evidence" — Enter withheld, message reverted, always-on session killed as wedged. Placeholders are now matched by their `#N` index (per-session, never reused) rather than by counting occurrences on the rendered screen, which holds only visible rows and loses the older placeholder to scrolling exactly when a tall paste adds its own. Pinned by `ComposerDeliveryEvidenceTests` and, end to end through a real ConPTY, `FakeClaudeContractTests.A_collapsed_paste_still_produces_composer_evidence`.

### Preserved Gotcha #45

- **fakeclaude clips TYPED input only** (CARD-0037 step 4): the CARD-0028 clip model is a model of typing, which is all the inbox conhost can deliver. Content inside a bracketed paste is exempt — paste MODE tracked from `ESC[200~` to `ESC[201~` across bursts, because ConPTY splits one write over several reads — so with the markers delivered the fake carries 43 KB whole *with clipping armed*. `ANTIPHON_FAKE_PASTE_PLACEHOLDER=1` (opt-in, default OFF because the real collapse threshold is unmeasured) renders the placeholder instead of echoing the body. `FakeVsRealClipParityTests` now has an arm per path: typed must still lose a chunk in both peers, pasted must lose nothing in both.

### Preserved Gotcha #46

- **The modern pseudoconsole demands a DA1 answer at startup, and `ModernConPtyConnection` is the only thing that gives it** (CARD-0048, ADR `docs/adr/0002-modern-conpty-backend.md`; investigation `docs/investigations/2026-08-16-modern-conpty-da1-stall-CARD-0048.md`): `OpenConsole.exe` writes `ESC[c` (DA1) the moment it comes up and **holds the console CLIENT** until a DA1 response arrives on the input pipe or **~3.0 s** expires - the inbox conhost never asks. So from the day this deployment went to `modern`, every child was frozen for 3 s before its first instruction, with **no input involved**, and everything that infers ready/done/idle from a quiet period shorter than 3 s read the stall as quiet (`RawPtyAdapter` ready at 500 ms; `WaitForTurnCompleteAsync` returning `TurnCompleted: true` with an EMPTY snapshot; `CodexDoneQuietPeriodMs` 3000 against a 3000 ms stall - a false positive, not a timeout). `ModernConPtyConnection` now answers `ESC[?1;0c` **once per session** through `Da1StartupResponder` (later queries counted in `Da1QueriesSeen`, never answered - a forwarded child query's answer WOULD reach the child). **Any new code path that creates a modern pseudoconsole outside `ModernConPtyConnection` re-inherits the 3 s frozen client**, and the only symptom is slowness plus silently-wrong readiness. The quiet windows deliberately did NOT move; the enforcement is `ModernPtyDa1Tests` pinning first child output under 2.5 s against a 3.0 s stall floor, so a ConPTY package bump that adds a new handshake goes red first. Never claim sixel (`4`) in the response - `TerminalScreen` cannot parse sixel. CARD-0049 was the same bug (a 3 s frozen start pushed the child's exit past the 4 s adoption point) and is regression-locked through the detached pty-host by `PtyHostAdoptionTests.Exit_while_runner_down_is_collected_on_adoption_on_the_modern_backend`.
<!-- CARD-0254 preserved source ends -->
