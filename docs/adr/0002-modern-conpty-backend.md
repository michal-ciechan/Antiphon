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
