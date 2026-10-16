# CARD-0907 Windows desktop capture

Outcome: blocked after the egress control was proven. Codex 0.160.0 exits before any requested TUI screen. No fixture was committed. The one interactive submission was not sent (`POST /v1/responses` count 0 on every launch).

Task: ab0e6467. Branch: `feat/card-task-ab0e6467`. Worktree: `C:\Antiphon\worktrees\card-task-ab0e6467`. Base at task start: `e4d7a3fa0b943f67848dcbed3e3f500c17792079`. Host: Microsoft Windows NT 10.0.19045.0, user token not elevated. Date: 2026-10-16.

Plans: [parent capture design](../superpowers/plans/2026-10-02-card-0907-codex-0160-screens-plan.md) and [raw-parser slice](../superpowers/plans/2026-10-15-card-0907-raw-parser-slice.md). Earlier blocked preflight: [2026-10-15 Windows capture preflight](../superpowers/evidence/2026-10-15-card-0907-windows-capture-preflight.md). That preflight stopped because Windows Sandbox was absent and a shared firewall change was prohibited. This task was allowed to use the desktop itself, still with no real Codex account, no shared firewall change, no elevation, and no contact with AppHost 17202 or the runner except the build-slot broker.

## What was proven

A per-user AppContainer profile `AntiphonC907Capture` was created with `CreateAppContainerProfile` (no elevation, no firewall API). SID:

`S-1-15-2-3474344596-1361774275-169937050-1715369765-655753896-4292765343-1302149271-985070300-468719377-2572984982-2285507752`

The profile remains so a follow-up can grant this same SID. Scratch lives only at `C:\Antiphon\worktrees\card-task-ab0e6467\.c907-scratch` and is not part of the commit. It holds a copy of the npm Codex 0.160.0 package (`package.json` version 0.160.0), a modern ConPTY pair, a synthetic stub home, and the capture helper. Nothing under the real user Codex home was read.

Negative control, same process tree, repeated on the probe and on all three capture launches:

| Check | Result |
|---|---|
| Host process outside the container, TCP `1.1.1.1:443` | connected (2026-10-16, this session) |
| AppContainer process, TCP `1.1.1.1:443` | `SocketException` 10013 access denied |
| AppContainer grandchild PowerShell, TCP `1.1.1.1:443` | `SocketException` 10013 |
| AppContainer process, TCP `2606:4700:4700::1111:443` | `SocketException` 10051, no handshake |
| Two processes in the same container, TCP `127.0.0.1` | connected |
| Container process to a listener outside the container | timeout |

10013 is the acceptance failure. A cooperative `HTTP_PROXY` was also set to `127.0.0.1:9` with `NO_PROXY` for loopback, and was not treated as the proof. Build-slot leases for the scratch helper were granted immediately (no exit 4). No other 17204 route was called.

Modern ConPTY from inside the container worked. `CreatePseudoConsole` at 120x30 drew the Codex process's own error text onto the grid. ConPTY is not the missing control.

## What stopped the screens

Codex 0.160.0's `find_codex_home()` canonicalizes `CODEX_HOME` with `std::fs::canonicalize`. On Windows that calls `GetFinalPathNameByHandleW(VOLUME_NAME_DOS)`, which looks up `\GLOBAL??\C:`. An AppContainer token cannot do that lookup. Measured in this container, on both the scratch home and `C:\Windows\System32`:

| API | Result |
|---|---|
| `CreateFileW` of the directory | success |
| `GetFinalPathNameByHandleW` flag 0 (`VOLUME_NAME_DOS`) | error 5 |
| `GetFinalPathNameByHandleW` flag 2 (`VOLUME_NAME_NT`) | success, `\Device\HarddiskVolume3\...` |

Three launches, each after a fresh 10013 proof, none of them a prompt submission:

1. `node.exe` plus `codex.js`. Node aborted with `EPERM` on `lstat` of `C:\` before the native binary. The npm launcher is unusable in this container.
2. Native `codex.exe` with `CODEX_HOME` set to the scratch directory. The grid showed `Error finding codex home: failed to canonicalize CODEX_HOME ... Access is denied. (os error 5)`. `codex.exe --version` still printed `codex-cli 0.160.0`.
3. Native `codex.exe` with `CODEX_HOME` unset. `--version` printed `codex-cli 0.160.0` plus a non-fatal PATH-alias warning. The TUI then stopped with `Error finding codex home: Could not find home directory`.
4. After the refinement, native `codex.exe` with `CODEX_HOME` set to the verbatim `\\?\` scratch home, and again with `\\.\GLOBALROOT\Device\HarddiskVolume3\...`. Both exited 1 with `failed to canonicalize CODEX_HOME ... Access is denied. (os error 5)`. Neither reached a banner.

No startup, trust, ready, working, update, or done screen was produced. The usage-limit line was not written, because there is no captured session to attach it to. Existing adapter, detector, and readiness tests were not re-run: there is no new fixture for them to load. Linux results from the 2026-10-15 raw-parser slice stay historical. Windows checkpoint rows were not run.

This is the same split as [openai/codex#45871](https://github.com/openai/codex/issues/45871) (open; `find_codex_home` still canonicalizes on the 0.160.0 sources) and [microsoft/mxc#694](https://github.com/microsoft/mxc/issues/694). The MXC investigation names the four object-namespace grants `GetFinalPathNameByHandleW(VOLUME_NAME_DOS)` needs: `\GLOBAL??` (directory query/traverse), `\GLOBAL??\C:` (symbolic-link query), `\GLOBAL??\MountPointManager`, and `\\.\MountPointManager`. Those grants are a shared object-namespace DACL. They were not applied. No firewall rule was added. No elevation prompt was raised.

## Refinement

A caller refinement (`task-ab0e6467-refinement-20261016171907.md`) allows a synthetic `version.json` only inside the scratch Codex home, so an update modal can render, and requires that fixture header to say it was synthesised and how. Real screens (startup, trust, ready, working, done) come first. The usage-limit line stays synthesised from vendor text. The time budget says to commit what exists.

That does not unblock rendering. There is still no TUI, so no scratch `version.json` was used to draw a modal, and no synthesised screen fixture was invented in place of a capture.

## Operator decision

The missing control is DOS drive-letter resolution for this AppContainer, which Codex 0.160.0 treats as fatal before the TUI. Network isolation is already in place and should not be weakened to a proxy.

Cheapest next step, pick one:

1. From an elevated shell, grant profile `AntiphonC907Capture` (SID above) the four object-namespace rights from microsoft/mxc#694, then rerun this capture in that same profile. Do not add a firewall rule.
2. Install a Codex build that falls back from `canonicalize()` to `std::path::absolute()` as proposed in openai/codex#45871, then rerun inside the existing AppContainer with no namespace change.

Windows Sandbox is still absent, so it is not the cheaper path. After either change, the capture still owes the real 0.160.0 screens, a synthesised usage-limit fixture labeled as synthesised, the narrow existing parser tests, and a report of which parsers fail. Parser fixes are not done here.
