# CARD-0030: a large body CAN be delivered whole — the discriminator is which conhost serves the pty

**Task:** 4f814379 (Debug) · **Date:** 2026-08-12 · **Status:** answered, with the fix identified and
deliberately NOT shipped (the card asked for the mechanism, and the shipped mitigations still work).

## The answer, in one sentence

A pseudoconsole is served by a **conhost binary**, and the one `CreatePseudoConsole` in kernel32
launches — `%SystemRoot%\System32\conhost.exe`, **10.0.19041.1** on this machine, a 2020 build —
**strips `ESC[200~`/`ESC[201~` out of written input before the child ever sees them**, so the TUI
receives our paste as a burst of typing; run the identical write through a modern pseudoconsole
(`conpty.dll` + `OpenConsole.exe`, the redistributable pair that VS Code, the JetBrains IDEs and
Windows Terminal all ship) and the markers arrive intact — at which point **real Claude accepts
86 400 bytes in a single write with zero loss**.

CARD-0027's "bracketed paste is not the discriminator" does not hold: both of its arms ran on the
inbox conhost, so its wrapped arm was silently unwrapped before the TUI saw it. **It compared
unwrapped against unwrapped.**

## The headline measurement

Real Claude (v2.1.228), our production encoding (`PtyInputEncoding.EncodeBody` — LF-normalised,
bracket-wrapped), ONE write, one fresh TUI per trial, composer's own `[Pasted text #N +M lines]`
counter as the oracle, zero model turns. Only the pseudoconsole backend differs.

| backend | body | rep 0 | rep 1 | rep 2 |
|---|---|---|---|---|
| kernel32 → inbox conhost 10.0.19041.1 | 5 400 B / 200 lines | 17 lines (8 %) | 17 (8 %) | 53 (26 %) |
| redist `conpty.dll` → `OpenConsole.exe` | 5 400 B / 200 lines | **200 (100 %)** | **200 (100 %)** | **200 (100 %)** |

And the size sweep on the modern backend, single write, 2 repeats each:

| body | verdict |
|---|---|
| 16 200 B (600 lines) | WHOLE, 2/2 |
| 43 200 B (1 600 lines) | WHOLE, 2/2 |
| **86 400 B (3 200 lines)** | **WHOLE, 2/2** |

86 KB is 84 read chunks. The shipped inline ceiling is 900 bytes.

## The controls, because one green table proves nothing on its own

| control | result | what it rules out |
|---|---|---|
| **A** — same modern backend, markers REMOVED (`NormalizeBody`, no wrap), 3 reps | 150/200 lines (75 %), LOST, 3/3 | that the newer conhost fixes it by some other route. Take the markers away and the loss comes back on the SAME binary. **The markers are the discriminator.** |
| **B** — window size 200×50 and 400×100 on the inbox backend, 2 reps each | 8 %, 93 %, 8 %, 8 % | card hypothesis 2. A wider viewport does not help; the 93 % is the known non-determinism, not a trend. |
| **C** — DECSET 2004 (`ESC[?2004h`) sent by the client before the write, both backends | no effect either way | "the client was not entitled to the markers". The inbox conhost strips them whether or not the client asked for bracketed paste; the modern one forwards them whether or not it asked. |
| **D** — plumbing control (a 6-byte marker typed first, required to appear in the composer) | required in every trial | the two earlier runs of this bench that read NOTHING everywhere. Both were the harness, not the TUI (see "Two ways this bench lied", below). |

## The wire evidence

`probes/stdin-probe.js` now reports the first and last 24 bytes it received, in hex. Same body, same
`PtyAgentRunner`-equivalent write, one variable:

```
backend                       bytes  has200~  has201~  headHex
kernel32/conhost-19041          300  False    False    4c 30 30 30 30 20 …   ("L0000 ")
redist conpty.dll+OpenConsole    312  True     True     1b 5b 32 30 30 7e …   (ESC [ 2 0 0 ~)
                                                        …tail: 1b 5b 32 30 31 7e  (ESC [ 2 0 1 ~)
```

312 − 300 = 12 = exactly the two 6-byte markers. Nothing else about the body changes.

A marker-only write (`ESC[200~ L0000 hello ESC[201~`) through the production path arrives as
`L0000 hello` — 25 bytes for a 37-byte write, the 12 marker bytes gone. So this is a parser dropping
an unrecognised CSI sequence, not a truncation.

## Why it matters that the TUI sees a paste rather than typing

CARD-0027 established that the composer keeps one ~1024-byte read chunk per event-loop turn and
discards the rest. That is what happens to *typed* input arriving in a burst. A **bracketed paste is
a different code path**: the composer accumulates from `ESC[200~` until `ESC[201~` and collapses the
result into `[Pasted text #N +M lines]`. The accumulation is not per-turn, so the chunking that
destroys typed input is irrelevant to it. Our writes never took that path because the markers never
survived the console.

This also explains the shape of the old data: the loss was never a function of the body, and pacing
had a dose-response (8 % → 56 %), because spreading chunks across turns is the only lever you have
when every chunk is racing to be the survivor of its turn. With a real paste there is no race.

## What this changes about the existing record

- **CARD-0027 §"bracketed paste markers → no"** is wrong as stated, for the reason above. Everything
  else in that document stands: our transport is lossless, the read quantum is ~1 KB, the composer
  keeps one chunk per turn, and the cut point is body byte 1024. Those are all true **of typed
  input**, which is what our writes have been all along.
- **CLAUDE.md's line** "current conhost/OpenConsole builds also strip `ESC[200~`/`ESC[201~` from
  written input before a .NET ReadFile client sees them (real Claude still receives pastes intact)"
  is half right and misleading in the important half: it is not "current builds", it is the *inbox*
  build; a current OpenConsole does not strip them; and real Claude does **not** receive our pastes
  intact — it receives them as typing, which is exactly why it clips them.
- The three shipped mitigations (byte-counted brief ceiling, spill-to-file + pointer, oversize
  incident) are **correct for the pty we currently create** and are untouched by this work.

## The fix that follows (NOT shipped here)

Ship a modern pseudoconsole with the session runner, the way VS Code (node-pty) and the JetBrains
IDEs already do: `conpty.dll` + `OpenConsole.exe` side by side, and call `CreatePseudoConsole` from
that DLL rather than from kernel32. Porta.Pty resolves the entry point through Vanara's
`kernel32.dll` import, so this needs either a Porta.Pty option, a fork, or our own host (the one in
`ConPtyHost.cs` is ~250 lines and complete).

Sequencing, if it is taken up:

1. Ship the binaries and switch the backend behind a flag, defaulting OFF.
2. Re-measure the envelope on the target machines — the numbers above are one machine, one Claude
   version. `PtyPasteMarkerExperiments.Real_claude_delivery_envelope` is the instrument and costs no
   model turns.
3. Only then raise `BriefInlineMaxBytes` and retire the spill-and-pointer path, with the incident
   threshold kept as a tripwire. **Raising the ceilings without shipping the binary would re-open the
   original bug**, since the ceilings and the conhost are now coupled.
4. `FakeClaudeContractTests` / `StdinClipModelTests` model *typed* input clipping. If the paste path
   ships, the fake needs a bracketed-paste arm that does NOT clip, or CI will keep asserting a
   behaviour production no longer has.

Two caveats worth carrying into that work:

- The redistributable `conpty.dll` used here came from `%LOCALAPPDATA%\Programs\Rider\lib\pty4j\
  win\x86-64` (109 944 B, 2025-09-18) with its sibling `OpenConsole.exe` (1 162 112 B). Windows
  Terminal 1.24.11911's own `OpenConsole.exe` (1.24.2607.10001) is copyable but
  `C:\Program Files\WindowsApps` cannot be *enumerated* by an ordinary process, so the finder in
  `ConPtyHost` never located it and that exact pairing was not tested. Ship a known binary, do not
  scavenge one from another product's install.
- A paced 86 KB delivery (1 KB chunks, 25 ms apart) read NOTHING once while the single-write arm was
  whole twice. Paced writes inside one paste are not obviously safe; the single write is the
  measured-good path.

## Two ways this bench lied before the controls existed

Both are harness failures that produced a confident, wrong, *negative* answer, and both are now
prevented in code:

1. **A child with no console at all.** `CreateProcess` with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`
   silently loses to the parent's own std handles when the parent's stdio is redirected — which is
   true of every test host and every daemon here. The child came up on the parent's pipes,
   `isTTY` false, while the pty sat empty; the probe's output appeared on the test's stdout and read
   like a successful run. Fix: blank our own std handles across the `CreateProcess` call.
   (`STARTF_USESTDHANDLES` + NULL handles also attaches the child, but leaves `claude.exe` with a
   stdin it never reads — it renders its entire TUI and ignores every byte, which looks exactly like
   total input loss.) `Host_selftest` now asserts attachment through a file the child writes, not
   through text on a screen.
2. **Snapshotting before the composer rendered.** A fixed 900 ms quiet window made 16 200 bytes read
   as NOTHING twice; with a wait for the paste placeholder it is WHOLE, repeatedly. Any measurement
   of "how much arrived" needs a positive wait for the composer, not a timeout.

## Artifacts

| path | what it is |
|---|---|
| `tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs` | **CI-runnable, no Claude, no turns.** Pins both halves: production pty strips the markers, a modern one delivers them byte for byte |
| `tests/Antiphon.Agents.Pty.Tests/ConPtyHost.cs` | a pseudoconsole host we own — backend, window, input-pipe size and creation flags are all parameters |
| `tests/Antiphon.Agents.Pty.Tests/ConPtyProbe.cs` | runs the JS probe under that host, reads the report from a file rather than off a rendered screen |
| `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs` | the bench: markers by backend, real Claude by backend, the controls, the size sweep, window size |
| `tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js` | now also reports head/tail hex, inter-chunk gaps, and can request DECSET 2004 or report on quiet |

Raw output: `tests/Antiphon.Agents.Pty.Tests/bin-card30/TestOutput/card-0030/` (E0 self-test,
E1 markers, E3 backends, E4 real Claude by backend, E5 controls + sizes, E6 envelope, plus every
rendered screen).

```powershell
# the CI-runnable fact (~10 s)
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card30/ `
  -- --treenode-filter "/*/*/PtyBracketedPasteContractTests/*"

# the headline, against real Claude — no model turns, one fresh TUI per trial (~2 min)
$env:ANTIPHON_HEADED_TESTS=1
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card30/ `
  -- --treenode-filter "/*/*/PtyPasteMarkerExperiments/Real_claude_by_conpty_backend"
```

## What was NOT established

- **Windows Terminal's own paste was never captured.** The plan was to diff a human Ctrl+V against
  our write; it became unnecessary once the same write differed by backend alone, and driving the
  clipboard + `SendInput` on the operator's live desktop is not a benign thing to do unattended. The
  inference "WT works because it ships OpenConsole" is consistent with everything measured but is an
  inference, not a capture.
- **Card item 4 (buffer sizing).** `ConPtyHost` takes a `CreatePipe` size and it was never needed:
  the read quantum belongs to the client runtime, not the pipe (CARD-0027 measured 1024 under Node
  and 1040 under Bun on the same path), and with a real paste the quantum stops mattering. Left as a
  knob, unexercised.
- **Which line of Claude Code treats the two paths differently** — still out of reach for the reason
  CARD-0027 gave: the app is JSC bytecode inside a Bun executable.
