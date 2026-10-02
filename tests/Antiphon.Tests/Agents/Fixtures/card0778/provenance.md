# CARD-0778 S1: Grok startup capture provenance

Produced 2026-09-30 by Code task `386a95bf` (slice S1 only), worktree
`C:\Antiphon\worktrees\card-task-386a95bf`, branch `feat/card-task-386a95bf`, from source
`40e77e2212a3d936f303073321dce11b75a1f0f2`. Host: this desktop (Windows 10 Pro 10.0.19045).
Operator sanction: "Yes fine for that capture" (one isolated real Grok launch per needed scenario,
disposable Grok home, nonce submit, residual spend if the redirect failed). No real credential, OAuth
content, `GROK_AUTH_PATH`, device-code approval, production restart or production prompt was used.
No product code was changed. The predicate below is **evidence for review**, not an implemented rule.

## Identities and digests

| Item | Value |
|---|---|
| Grok CLI | `grok 1.0.41 (4220f3b224a6) [stable]` at `%USERPROFILE%\.grok\bin\grok.exe`, SHA-256 `ab5d2a424f08281798acbdbb06076166fe000d7995ede94a673417b805210a25` (same version as the incident) |
| PTY backend | `ModernConPty (requested 'modern'): Microsoft.Windows.Console.ConPTY 1.24.260710001`, `FellBack=false` on every launch |
| `conpty.dll` / `OpenConsole.exe` (probe output) | `39fba2713e2495117b1591ae8c32a3b904bea7aa66069cf7815e2844c76d75d8` / `b7fd936c2668b87b9ecf7b3366dc6568afc1c6f981874cba3e955a1c35cf8160` |
| `Antiphon.Agents.Pty.dll` / `Antiphon.SessionRunner.dll` / `Antiphon.FakeLlmApi.dll` (built from the source SHA) | `4bdb5d538516f93118521cb7da9063abdb89c8635529dab2c75dd8bccc7d00d8` / `7c1838254d2c2a1978ce01ec259db9c480ee60c55ce6a4a1c223387fcaedf947` / `70ee902df50cf192410048a5b89056dd0326cdd603a7d21d90581a9081784d46` |
| Probe source (task-owned, ignored `.antiphon/c778-probe/Program.cs`, not committed) | `e2d9b21d8212dca838cb80145e9784b7c8239eb68751179dd0d0731056c753ef` (last capture build; later edits added read-only verify/diff modes) |
| `startup-frames.json` fixture lineage | S1 blob `2e24da5f63b6cbf5d8731f10e12af9463b9c805e`, LF SHA-256 `0ecef35286943a370b87d21871ec3063c52cafe93585b66f77849ed5caab5674`. S2 added synthetic `syn-other-size-120x29`: blob `e6cc9f49714b8594838ad5dc9be9782be0a39b35`. The predicate review then relabelled 16 checkpoints (8 typed startup rows and 8 ghost suggestions) without changing screens or chunks: blob `db5ae8051a946ee616931c192a35d06a50c8a4f4`, LF SHA-256 `0e4650b9011c3919edf36f0a652f2891da6d214bd04853067b7e0bc5a9495e19`. The next correction changed only the eight typed-row basis descriptions: blob `051da529fdfbb2ef7bda584f430afede1ab0b23e`, LF SHA-256 `c66364f75e8de077a5f24328a9bd6c1310dce1988f6ac0648af1ef5847fa7209`. The current correction changes only the eight ghost-suggestion basis descriptions to name the non-empty input row rule: blob `15ab9a1f097aa1416c07f5ae3d20e6e63346a9dc`, LF SHA-256 `6990d5910e3983005afc6f7dd1eec002f8c59686613c80d081e0d3953394dbee`. Captured content digests below remain valid; Windows `core.autocrlf` working copies differ byte-wise. |

Content digests (SHA-256 of UTF-8; line-ending independent):

The `[stable]` tag above records the Windows install's output. It is optional in
the image version check: CARD-0986 Review observed `grok 1.0.41 (4220f3b224a6)`
without a channel tag after the installer home was removed. The version is pinned;
the twelve-character lowercase hex hash records the observed build, not a pin.

| Capture | Chunks | Concatenated sanitized chunk text | Checkpoints | Checkpoint screens joined by U+0000 |
|---|---:|---|---:|---|
| `idle-20260930173209-21944bcbd833` | 97 | `bedd9d072a6ed4f07e864e4ad868fb322a1ffb82a0ca8b1b274b086be14e2ac1` | 68 | `cf1b3ede1d73cb24adacad5d50c7a9227957f14d648fad8305868177fea2256a` |
| `startup-20260930173019-de8210c2232b` | 75 | `ee9ef42c92710e5dc57df732526f9a9f8baace20ce5a6de9002cf0b9a01c87a1` | 44 | `a87a1e3dad8b679a225a3ad100127cbc580de42b0cc189e2e5e54522641d4849` |
| `signin-20260930173301-f2d789d4eb73` | 0 (omitted) | - | 2 | `811f878f1134874e2d92f8404056b122087de604caa4a5f71dee262ca1333cf5` |
| `incident-98f50651-ansi-log` | stream | `sanitizedStreamSha256` in the fixture (source file `b8d8c9357cbd6d82f6f1b3f9333828b7e55c3f88029b7f09719c23fa0e3e3505`, 34858 bytes, 0 replacements) | 0 | - |

## Step 1: dated incident evidence (session `98f50651-c481-4b01-9dd3-db0b352514c5`, task `76b7b036`)

| Source (desktop) | Result |
|---|---|
| Canonical checkout `C:\src\Antiphon\server\logs\antiphon-20260927.log` (one file, no rotation) | Found. 16:41:28.207 +01:00 dispatch "at grok-4.7" into `C:\src\Antiphon`; 16:42:31.584 `Failed to start interactive agent session` with `InvalidOperationException: Agent process did not become ready.` at `AgentSessionService.WaitForReadyOrThrowAsync` line 2214; 16:42:32.6 an incidental `40P01 deadlock detected` in the queued-launch failure update; 16:45:35.658 dead-session reconciler fails the task (`KilledByRequest, code 1`). |
| Runner Serilog `%TEMP%\antiphon-logs\session-runner-20260927.log` (runner account = this user; 24.1 MB, no size rotation) | Found. One line: 16:41:31.388 tailing the deterministic `updates.jsonl` (created lazily; never created). |
| Supervisor `C:\src\Antiphon\logs\session-runner.log` | No 2026-09-27 entry for the session (only later 404 lookups). |
| `C:\logs\antiphon\session-runner\pty-hosts\logs\98f50651c4814b019dd3db0b352514c5.log` | Found. 15:41:29.296Z host start; 15:41:31.356Z grok launched (child pid 30468) on ModernConPty 1.24.260710001; 15:42:31.578Z child exit `KilledByRequest`; host exit on runner ack. |
| `C:\logs\antiphon\session-runner\98f50651c4814b019dd3db0b352514c5.ansi.log` | **Found (not previously known to Plan)**: the pty-host's per-chunk `File.AppendAllText` of every decoded chunk from launch. It is a complete raw stream but has no chunk boundaries, timestamps or dimensions, so it is kept as partial incident evidence, not a replayable capture. |
| `/api/sessions/{id}/buffer`, `/transcript` | Per Plan: HTTP 500 / empty. Not re-queried. |

Correlation: child launch 15:41:31.356Z to readiness failure 15:42:31.584Z is 60.2 s, which matches
`GrokReadyMaxWaitMs` 60000. Missing and not substituted: the incident terminal dimensions (120x30 is
inferred from `Delegation:DefaultCols/DefaultRows`), chunk timing, any independently captured incident
frame, and the **names** of the incident's two MCP servers (the screen shows only counts, and no
retrieved log names them).

Incident stream, replayed at the inferred 120x30 (derived, not independent):
1. The header paints (`≡ feat/card-task-76b7b036 worktree C:\A\worktrees\card-task-76b7b036`).
2. The composer box paints (`│ >`, footer `Grok 4.7 (high) · always-approve ─╯`).
3. `| Starting session… 0.1s` appears once on the status row.
4. The status row clears and the header gains an ASCII spinner (`\ | / -`), then `MCP (0/2)`, then
   `MCP (1/2)`, then `3.0K / 500K │ [Dashboard]`. The footer gains `Weekly limit left: 6% ·`.
5. The spinner kept redrawing until the kill.

The final frame has an empty composer, the hint `Shift+Tab:mode  │  Ctrl+x:shortcuts`, and no
status row. Because the renderer is differential, `MCP (1/2)` never appears literally in the stream;
only changed cells are rewritten. The quiet-period gate therefore saw a changing buffer for the whole
60 s. That matches the mechanism in the plan's ground-truth table, now with the actual redraw source.

## Captures (reproductions, same CLI version)

Harness: an isolated `PtyAgentRunner("modern")` at 120x30. `OnData` was subscribed **before**
`StartAsync`; each decoded chunk, its monotonic offset, a recorder index and the contemporaneous
`SnapshotScreen()` were held in memory.

- **Stub:** `FakeLlmApiServer` (Grok surface) on owned loopback, with the environment from
  `RealCliStubEnv.ForGrok(stub.BaseUrl, <synthetic key>)` plus a disposable
  `GROK_HOME=%LOCALAPPDATA%\Antiphon\card0778-grok-home-<scenario>-<unique>` and
  `GROK_DISABLE_AUTOUPDATER=1`.
- **Environment check:** the final-merge check (process env overlaid as `MergeEnvironment` does)
  asserted, before every launch, that:
  - both redirect variables point at the stub;
  - `GROK_HOME` is the disposable path;
  - `GROK_AUTH_PATH`, `XAI_API_KEY` and `ANTIPHON_PTY_AUDIT` are absent;
  - the key is the synthetic one (absent for sign-in).
- **Transcript:** the production `GrokTranscriptTailer`/`GrokTranscriptNormalizer`, reached by
  reflection, gave the native receipt. Only method, path, timing, size and nonce flags were kept from
  requests, never headers.
- **Cleanup:** each probe killed and awaited its own child, disposed the tailer and stub, and deleted
  its home and cwd. One home needed manual removal because of a read-only marketplace pack. The
  orphan-process check found 0.

| Capture | Launch args (after `--always-approve --no-alt-screen`) | Setup | What happened |
|---|---|---|---|
| `startup-…de8210c2232b` | `--model grok-4.6 --reasoning-effort low --session-id <id>` | 2 stdio MCP servers that never answer (`cmd /c ping … >nul`), fresh non-git cwd | Early nonce submitted at the first painted startup frame; see probe P-2. `--reasoning-effort low` made grok print "current model does not support reasoning effort" (a probe artifact). |
| `idle-…21944bcbd833` | `--model grok-4.6 --session-id <id>` | Same MCP config, fresh `git init` cwd | Nonce submitted into the incident's long-lived state (composer + header MCP spinner); see probe P-1. |
| `signin-…f2d789d4eb73` | as idle | Empty home, **no key** | Sign-in surface recorded, nothing typed, no approval. |

The `MCP (0/4)` header shows that grok 1.0.41 loads **two MCP servers from outside `GROK_HOME`**, in
addition to the two configured ones. Claude/Cursor MCP import flags exist in the binary
(`claude_mcps_enabled`, `cursor_mcps_enabled`); the user's configs were not read. Those two
connected (`2/4`) within about 5 s. This is the likely source of the incident's `(0/2)`/`(1/2)`: one
of the operator's imported servers never connected during the incident.

## Probe receipts (one correlated timeline per session; ms from just before `StartAsync`)

Input path: `PtyAgentRunner.SendLineAsync` → `EchoGatedLineSender` (LF-normalized body, bracketed
paste because it is two lines, echo evidence, separate CR). Body:
`Card 0778 readiness probe, harmless.\nReply with the single word ok. Nonce <nonce>`.

| | P-1 idle (`idle-…`) | P-2 early (`startup-…`) |
|---|---|---|
| Screen class at submit | Ready: chunk 48, composer + spinner, status row gone since 3000 ms | StartingSession: chunk 15, `Starting session… 0.5s` |
| Submit start / end, outcome | 8043 / 8155, `EvidenceSeen` | 4034 / 4158, `EvidenceSeen`, startup row still present |
| After Enter | Processed immediately; `Waiting for response…` | **Queued**: row `#1 Card 0778 …` and hint `Ctrl+;:queue` (chunk 24) while `Starting session…` continues |
| Startup-to-idle transition | none (already idle) | Status row cleared at 4609 |
| Native UserPrompt (`updates.jsonl` timestamp / observed) | 8198 / 8251, seq 1, uuid `690ffe26-…-2` | 5009 / 5172, seq 1, uuid `ffd1b0d2-…-2` |
| Stub title POST / user-turn POST (`/responses`, ≥10 KB) | 8208 / **8277** (70977 B) | 5033 / **5126** (71006 B) |
| `/api-key` oracle; all requests on the stub port | yes; yes | yes; yes |
| Reply rendered | 8369 | 5317 |
| Verdict | **Qualified**: input accepted and turned into a complete prompt without any transition | **Not qualified**: input queued until startup finished; prompt receipt and user turn both follow the transition |

Complete-prompt finding (both sessions): grok 1.0.41 **drops the LF** of a bracketed-paste
two-line body. The rendered composer and queue row show `harmless.Reply`. The P-1 UserPrompt text
equals the body with the LF removed, and the P-1 user-turn body contains that joined form, not the LF
form. P-2 predates text retention; its length (100 = 101 − 1) and the rendered row agree. Every other
character of the nonce body arrived in exactly one UserPrompt. This is a delivery-fidelity defect
candidate, separate from readiness and outside S1's footprint.

## Observed layout facts for the D-1/D-2 review (120x30; rows are 0-based)

- **Qualified input region (P-1)**:
  - box rows 24–26, columns 2–117: `╭─…─╮` / `│ >` with nothing after `> ` / `╰─… · always-approve ─╯`;
  - hint row 28 exactly `Shift+Tab:mode  │  Ctrl+x:shortcuts`;
  - status row 22 (box top − 2) blank.
  Fixture checkpoints carry these bounds on every `Ready` frame.
- **Decorative (varied throughout the qualified window without affecting input)**:
  - header row 1: the ASCII spinner glyph before ` MCP (n/m)`, the MCP counts (0/4→2/4) and the
    token counter;
  - a one-time model footer label change (`grok-4.7` → `grok-4.6`, with `Switched to grok-4.6` on
    row 3);
  - the incident's footer prefix `Weekly limit left: 6% ·` and model label `Grok 4.7 (high)`.

  Chunks 22–48 of `idle-…` hold ASCII spinner redraws across 3335–7954 ms (> 4.6 s, well beyond the
  1000 ms settle) with an unchanged input region.
- **Not ready despite the same empty box**:
  - `Starting session…` on the status row (queues input, P-2), with the identical idle hint;
  - `Waiting for response…` / `Responding…` with hint `… Ctrl+c:cancel …`;
  - a queued `#n` row with `Ctrl+;:queue`.
- **Unqualified or nonempty**:
  - typed text (hint `Enter:send  │  Alt+Enter:newline …`): ComposerUnavailable even when the box is complete and `Starting session…` remains on the status row;
  - a post-turn ghost suggestion (`│ > <suggestion>`, hint `Tab/→:accept suggestion …`): ComposerUnavailable;
  - the welcome card with `Logged in with API key` on row 28.
- **Sign-in (current screen)**: `Connecting...`, then `A browser window will open for
  authentication.`, the `Paste your token here...` input and `enter submit  ctrl+q quit`. The
  device URL and code are never rendered or emitted (0 OSC 8/52 payloads, 0 `http`).
- **Trust dialog: not reproduced.** Three disposable-home launches, into two non-git cwds and one
  `git init` cwd, never showed it with API-key login. Only a labeled synthetic trust case is
  included. A captured 1.0.41 trust frame remains outstanding.

Labels in the fixture (`expectedReason`/`basis`) apply the facts above mechanically to the recorded
screens. Ready frames carry a `qualification` naming P-1; post-turn Ready frames are marked "not
separately receipted". Positive spinner input **was** observed and receipted, so V-1/V-6/V-17 have
qualified input. The startup (`Starting session…`) state stays negative, now with direct evidence.
The classifier checks a nonempty input row before the status row, so the eight typed startup
checkpoints have `ComposerUnavailable` rather than `StartingSession`; both reasons reject readiness.

## Sanitization

- **Method:** replacements are applied to the concatenated stream, so values split across chunks
  are covered. Each character is mapped by class (A–Z→`X`, a–z→`x`, 0–9→`0`, others kept), which
  preserves length, cell width and delimiters. Screen-found literals are masked everywhere. The
  write is refused if any literal survives in the stream or screens.
- **Checks:** raw replay against the contemporaneous screens and sanitized replay against the
  sanitized checkpoints, for every chunk, done in memory before writing. Raw frames were never
  printed or persisted.
- **Replacements (manifest per capture in the fixture):**
  - idle: 0;
  - incident: 0;
  - startup: 2 × `long-token`, over-masking of the disposable cwd tilde-path, consistent in chunks
    and screens;
  - sign-in: 7 × `mixed-alnum` that matched CSI parameter tails (for example `…55H` + `Connecting`),
    not secrets. The sanitized sign-in replay differed from 2 checkpoints, so its chunk stream is
    **omitted** and only its 2 independently recorded screens are kept (`replayable: false`).
- **Committed-fixture scan:** 0 synthetic keys, JWTs, URLs, emails, `Bearer`, user names or
  `auth.json`. The only 4-4 code-shaped strings are `CARD-0778` and a GUID fragment.
- **Replay verification of the committed fixture:** probe `verify-fixture`, production
  `TerminalScreen`. idle 97 chunks, 68/68 checkpoints equal; startup 75 chunks, 44/44 equal;
  indices contiguous; chronology non-decreasing; initial screen blank; incident stream digest and
  derived final frame equal.

## Side effects and non-provider traffic

- Network egress was not observed directly. The stub oracle shows the redirect held: the stub
  received the `/api-key` call, the title POST and the user-turn POST, and the rendered reply was the
  stub's scripted `C778REPLY…` marker. One unexplained `GET /` per launch also hit the stub and is
  recorded.
- Grok cloned its plugin marketplace (`marketplace-cache/<hash>/.git`) into each disposable home.
  That is network access that is not provider spend; the homes were deleted.
- The sign-in launch **auto-opened the default browser**: msedge started 17:33:05Z, 4 s after launch.
  It was not interacted with and nothing was approved. The operator should close that xAI sign-in
  tab.
- Real launches: 3 (startup, idle, sign-in). Build-slot-leased probe builds: 9.

## Missing or outstanding evidence

- A captured 1.0.41 trust dialog and trust-to-startup transition (not reproducible here).
- Replayable sign-in chunks (omitted, as described above).
- Incident chunk timing, dimensions and MCP names (not recorded by any source).
- The operator's imported MCP server identities (not inspected by design).
- Whether an LF-preserving multi-line paste is possible on 1.0.41 (defect candidate).
- A newer CLI would need its own capture; these captures qualify only
  Grok 1.0.41 at 120x30 on ModernConPty 1.24.260710001 (observed build `4220f3b224a6`).
