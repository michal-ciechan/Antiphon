# CARD-0390 — Claude 2.1.258's unnumbered trust dialog: answer it, seed it, name it

**Date:** 2026-09-05
**Card:** CARD-0390 (`cfd1c1db-1b17-4f24-80d1-5f0f03dd3cb6`). Investigation landed as
[docs/investigations/2026-09-05-card-0390-diagnose-agent-failing.md](../../investigations/2026-09-05-card-0390-diagnose-agent-failing.md)
(`317a56ef`). Predecessors: CARD-0047 (`60720277`, the trust answerer and Gotcha #48), CARD-0324
(`AgentLaunchBlock` / `SessionLaunchBlock.TrustDialogNotCleared`, defined but never set by a Claude
adapter), CARD-0352 (the diagnose specialist whose seat has never been up).
**Verdict in one line:** CARD-0047's answerer types `"1"` at a dialog that no longer has numbers,
whose default is now **No, exit**; the fix is a layout-aware answerer that only ever presses Enter
after the screen *shows* "Yes, I trust this folder" highlighted, a launch-block reason that names
the dialog, and a `~/.claude.json` seed at specialist provisioning so Antiphon's own headless
seats never meet the dialog at all.

---

## 0. Ground truth

Everything below was read from the code, the live pty logs, or the Claude 2.1.258 binary on this
box (`C:\users\lndco\.local\bin\claude.exe`, 218 MB, 2026-09-01). Nothing is inferred from the
card's wording.

| The card / brief assumes | What the code and binary actually do | Where |
|---|---|---|
| The detector "no longer matches" the 2.1.258 dialog and must be retargeted | **Detection already matches.** `hasChoices` fires on `entertoconfirm`/`esctocancel`, the trust arm on `yesitrustthisfolder` and `accessingworkspace`+`quicksafetycheck`. Only the **answer** is wrong: `AffirmativeKey` is hard-coded `"1"`. | `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs:88-109` |
| The dialog has a numbered menu (`❯ 1. Yes / 2. No`) | 2.1.258 renders the dialog through a shared confirm component with `hideIndexes:true, cancelFirst:true, focus:"cancel", confirmLabel:"Yes, I trust this folder", cancelLabel:"No, exit"`. No digits are rendered and the default highlight is **No**. | Binary: `e(Cn,{refuseInput:K,openedAt:Ie,hideIndexes:!0,cancelFirst:!0,focus:"cancel",confirmLabel:"Yes, I trust this folder",cancelLabel:w?"No, continue without these permissions":"No, exit",…})` |
| The correct keystroke is unknown ("likely Down+Enter") | That component renders the generic **Select** list (`Re`, with `options:[cancel,confirm]`, `defaultFocusValue`). The default keybinding table for the `Select` context is `up/k/ctrl+p → select:previous`, **`down/j/ctrl+n → select:next`**, `home/end`, **`enter → select:accept`**, `escape → select:cancel`. No digit bindings. The text-mode sibling (`WPt`, "Please answer y or n.") is only used when `cn()` is true, which is not the pty case. | Binary: `{context:"Select",bindings:{up:"select:previous",down:"select:next",j:"select:next",k:"select:previous","ctrl+n":"select:next","ctrl+p":"select:previous",…,enter:"select:accept",escape:"select:cancel"}}` |
| Sending `"1"` might have picked "No" | `"1"` is unbound in this layout: the dialog repainted unchanged (`cb3696fe….ansi.log`, painted twice) and the process was `KilledByRequest`, not a clean exit. | Investigation §Mechanism 3–6; `C:\logs\antiphon\session-runner\cb3696fe42d241308a5d7dd33d14f25b.ansi.log` |
| Any never-trusted cwd hits the dialog | Claude's check is: exact key `projects[<key>].hasTrustDialogAccepted`, else walk ancestors **bounded by the git root** (`eF`, boundary from `JCe` = git-root-or-null). For a **linked worktree the config key resolves to the canonical (main) repo root** (`wUe()` → `Jr(cwd)` → `canonicalRootByRoot`), which is why every `C:\Antiphon\worktrees\card-task-*` delegate is trusted through `C:/src/Antiphon` and only 15 of 1 534 pty logs ever show the dialog. A non-git scratch dir such as `C:\logs\antiphon\diagnose` has no boundary, walks to `C:/`, finds nothing, and prompts. | Binary: `function Eue(){…if(e.projects?.[n]?.hasTrustDialogAccepted)return!0;return Zz(e,_e())}`, `function eF(e,n,r){…}`, `function wUe(){…r=Jr(n),o=r?D$(r):D$(Xn(lt(n)))…}`; `~/.claude.json` has no `C:/Antiphon` or `C:/Antiphon/worktrees` key |
| The project key format is unknown / both slash spellings are needed | Claude writes and reads **forward-slash, case-preserved** keys (`D$` = `replaceAll("\\","/")`; the file holds both `C:/src/antiphon` and `C:/src/Antiphon`). The backslash twins in the file were written by *our* canary's `Trust()`, not by Claude. Claude's own remote-runner seeds exactly `N[path]={hasTrustDialogAccepted:!0}` and its error text tells users to "set projects[<key>].hasTrustDialogAccepted: true". | Binary: `for(let fe of[Se,…])N[fe]={hasTrustDialogAccepted:!0}`, `"Run Claude Code in that folder once and accept the trust dialog, or set projects[…].hasTrustDialogAccepted: true in …"`; `~/.claude.json` key `C:/logs/antiphon/check-interpreter` |
| Seeding is novel | Two in-repo precedents already write this flag: the canary's `UntrustedDirectory.Trust()` (real `~/.claude.json`) and `RealCliStubClaudeConfig.SeedOnboarding` (isolated `CLAUDE_CONFIG_DIR`, forward-slash key, measured mapping `{CLAUDE_CONFIG_DIR}/.claude.json`). No production code seeds. | `tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs:110-150`, `tests/Antiphon.Tests/Agents/RealCliStubClaudeConfig.cs` |
| `TrustDialogNotCleared` needs a new enum | Both enums exist and are already mapped: `AgentLaunchBlockKind.TrustDialogNotCleared = 2` → `SessionLaunchBlock.TrustDialogNotCleared` via `GrokSignInIncident.ToSessionBlock`. `WaitForReadyOrThrowAsync` throws `AgentLaunchBlockedException(block)` whenever `adapter.LaunchBlock` is set; every launch catch already persists `session.LaunchBlock` and `FailureReason = block.Reason`; the supervisor's `Crash` incident message embeds `FailureReason`. Neither Claude adapter sets `LaunchBlock`. | `server/Application/Dtos/AgentLaunchBlock.cs`, `server/Domain/Enums/SessionLaunchBlock.cs`, `AgentSessionService.cs:298-303, 655-660, 1635-1643`, `AgentSupervisorService.cs:165-168`, `IAgentProtocolAdapter.cs:39` |
| Only the session-runner adapter matters | Two Claude adapters call the same gate in lockstep and both must stay identical: `RunnerClaudeAdapter.ResolveBlockingStartupPromptAsync` (production) and the in-process `ClaudeAdapter.WaitForReadyAsync`. | `RunnerClaudeAdapter.cs:240-278`, `server/Infrastructure/Agents/Pty/ClaudeAdapter.cs:110-140` |
| The readiness budget is ~25 s | 5 s quiet (`ClaudeReadyQuietPeriodMs`) + 15 s `ClaudeTrustPromptSettleMs` + kill. The 15 s settle stays; the new highlight step adds at most ~4.5 s. | `AgentRegistrySettings.cs:9,24` |
| The provisioner runs once | `StandingSpecialistProvisioner.EnsureAsync` (and therefore `PrepareWorkspace`) runs on **every** `CardDiagnosisSweep` / `DiagnoseService` / check-interpreter call, not once. Any seed must be idempotent and cheap on the repeat path. | `StandingSpecialistProvisioner.cs:62-70, 108-112`; callers `CardDiagnosisSweep.cs:105`, `DiagnoseService.cs:94, 213`, `AgentTaskCheckHostedService.cs:90` |
| Tests cover the adapters | Both unit fixtures are the 2026-08-16 numbered screen and clear on `"1"`; the only test that launches real Claude is the `[Category("HeadedCanary")]` canary, opt-in via `ANTIPHON_HEADED_TESTS=1`, and it asserts `AffirmativeKey.ShouldBe("1")`. | `ClaudeStartupTrustPromptTests.cs:18-36`, `RunnerClaudeAdapterTrustPromptTests.cs:34-45`, `ClaudeTrustPromptCanaryTests.cs:65` |
| The pty input path may mangle control bytes | `RunnerTerminalSession.WriteAsync` forwards the string verbatim to `POST /sessions/{id}/input`; Enter is already sent as a separate `"\r"`; Ctrl+U (`"\x15"`) already travels this path for the composer probe. | `RunnerTerminalSession.cs:85-102`, `ComposerInputProbe.cs:91` |

### The live 2.1.258 screen (the new fixture)

Rendered text of `C:\logs\antiphon\session-runner\7bbe0c614ab44807809f51c3a2177c4f.ansi.log`
(this investigation's own restart, 2026-09-05 04:06 local; identical in `ab2c4230`, `f4a032b1`,
`de538b13`, `cb3696fe`). The highlight marker is a plain ASCII `>` (0x3E), not `❯`; the highlighted
row is coloured, which the rendered-screen snapshot drops.

```
────────────────────────────────────────────────────────────────────────────────────────────────────
 Accessing workspace:

 C:\logs\antiphon\diagnose

 Quick safety check: Is this a project you created or one you trust? (Like your own code, a well-known open source
 project, or work from your team). If not, take a moment to review what's in this folder first.

 Claude Code'll be able to read, edit, and execute files here.

 Security guide

 > No, exit
   Yes, I trust this folder

 Enter to confirm · Esc to cancel
```

Two further facts about the component that the design leans on:

- **Refuse window.** The dialog ignores keys for a short window after it opens (`refuseInput` /
  `openedAt` / `windowMs`, the anti-fat-finger guard), and a refused key restarts the window. In
  production the answer is typed only after the 5 s quiet gate, so this never bites; the headed
  canary must **not** answer within the first second of detecting the dialog.
- **Gated-grants sibling.** The same component paints "Yes, I trust this folder" / "No, continue
  without these permissions" when the folder carries project settings with hooks or permission
  grants. Same keys, same layout; the answerer treats it as the same dialog (choosing "Yes" there
  enables the project's own settings, which for an Antiphon-written deny-all hook is the intent).

---

## 1. Decisions

- **D-1 — Answer by layout, and never press Enter blind.** `ClaudeBlockingPrompt` gains a
  `Layout` (`NumberedMenu` | `HighlightedList` | `Unknown`). The numbered arm keeps `"1"`
  (older Claude on the laptop, and any future return of indexes). The highlighted arm reads which
  option the `>`/`❯` marker sits on, moves the highlight with a short **candidate ladder**
  (`j`, then Down `\x1b[B`, then Ctrl+N `\x0e`), verifies on the rendered screen that
  **"Yes, I trust this folder" is highlighted**, and only then sends Enter (`\r`). If the highlight
  never lands on Yes, **no Enter is sent** and the launch fails with a named block.
  *Why:* Enter on the default would choose "No, exit" — the seat would exit itself and the incident
  would read as a clean stop. Verified-highlight-then-Enter makes the worst case "dialog still up,
  named", never "we exited it". *Why a ladder and not one key:* `j` is a single printable byte with
  zero escape-sequence risk; Down is the documented key but a split `ESC` + `[B` read is
  `select:cancel` = exit; Ctrl+N is the third binding. Each rung is harmless when unbound (the
  Select swallows it) and the canary records which rung fired. *Rejected:* `y` (only bound in the
  `Confirmation` context, and it would accept without our screen verification); `end`+Enter (same
  escape-sequence hazard as Down, no upside); Esc / Space / Tab (never).
- **D-2 — Set `LaunchBlock = TrustDialogNotCleared` in both Claude adapters; no new incident
  kind.** The reason text names the cwd, the layout, the keys sent, the final screen title, and
  the remedy (`projects["<key>"].hasTrustDialogAccepted: true`). Everything downstream already
  exists (D-mapping in `GrokSignInIncident.ToSessionBlock`, the catches, the supervisor's `Crash`
  message). *Rejected:* a Critical episode incident like `ProviderSignInRequired` — after this fix
  the block only recurs if the TUI changes again, and the existing `BackoffEscalated` Critical
  already fires; a new kind, migration and sweep wiring buys nothing the named `FailureReason`
  does not. *Rejected:* a new `AgentTaskFailureCode` — `AgentTaskLiveness.ClassifyFailure` already
  prefers the row's `FailureReason` when present.
- **D-3 — Seed `hasTrustDialogAccepted` at standing-specialist provisioning, and only there.**
  `StandingSpecialistProvisioner.PrepareWorkspace` calls a new `ClaudeProjectTrust.Seed(cwd)`
  that writes the forward-slash, case-preserved key into the runner user's `.claude.json`
  (`CLAUDE_CONFIG_DIR/.claude.json` if set, else `%UserProfile%\.claude.json` — the same
  resolution as `ClaudeConfigDirProvider` and Claude itself) with an atomic temp-and-replace, only
  when the key is absent, never creating the file, never throwing, memoised per process so the
  every-call `EnsureAsync` path costs nothing after the first read. *Why:* it sidesteps the dialog
  entirely for the seats Antiphon creates and runs headless; it is Claude's own documented remedy;
  it protects those seats even if the TUI changes again. *Why not everywhere:* the answerer already
  covers every other launch (worktrees are trusted through the canonical repo root anyway), and
  writing the operator's global Claude state on every agent start (`AgentWorkspaceProvisioner`
  runs at every launch of every agent) widens the blast radius of a lost-update race for no gain.
  *Rejected:* seeding in the session-runner before every spawn (same race, per launch, and the
  runner has no notion of "Antiphon-created seat"); seeding both slash spellings (Claude reads only
  the forward-slash form — the backslash twins in the live file are our canary's leftovers).
- **D-4 — The seeder is exact-key only.** It does not replicate Claude's ancestor walk or the
  worktree canonical-root mapping. Seeding an exact key that an ancestor already covers is
  harmless; specialists live in non-git directories where no ancestor is trusted.
- **D-5 — The headed canary is the measurement, and it runs before the key is declared measured.**
  The unit tests pin the algorithm against the live fixture; only a real `claude.exe 2.1.258`
  launch into a never-seen temp directory can prove which rung moves the highlight and that Enter
  clears it. S1 ships the ladder (safe under D-1 whichever rung works); S2 runs the canary and
  records the rung in the plan, the commit message, and a pinned canary assertion. The canary also
  becomes the positive control for D-3 (seed with the production seeder, launch, expect no dialog).
- **D-6 — Stop polluting the operator's `~/.claude.json` from tests.** The canary's
  `UntrustedDirectory.Trust()` is replaced by the production seeder and `Dispose` removes the keys
  it added; the provisioner tests **must** construct the provisioner with an explicit temp config
  path so `dotnet run` never touches the real file (today the live file already carries dozens of
  `antiphon-kind-test*` / `antiphon-rc-off-canary-*` keys from earlier canaries).
- **D-7 — Immediate operator unstick is the orchestrator's call, not this plan's.** Seeding
  `C:/logs/antiphon/diagnose` by hand today would bring CARD-0352's seat up before this lands, but
  it would also remove the post-land positive control for the seeder (PC2 below). The answerer's
  positive control (PC1, a genuinely fresh directory) is unaffected either way. Recommendation:
  leave it until land unless CARD-0352's downtime matters more than one verification step.

---

## 2. Design

### 2.1 `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`

```csharp
public enum ClaudeTrustDialogLayout { None = 0, NumberedMenu = 1, HighlightedList = 2, Unknown = 3 }
public enum ClaudeTrustDialogHighlight { Unknown = 0, Yes = 1, No = 2 }

public sealed record ClaudeBlockingPrompt(
    ClaudeBlockingPromptKind Kind,
    string Title,
    string AffirmativeKey,                                   // "1" for NumberedMenu, "\r" for HighlightedList
    ClaudeTrustDialogLayout Layout = ClaudeTrustDialogLayout.None);

public static class ClaudeTrustDialogKeys
{
    public const string Enter = "\r";
    public const string LegacyDigit = "1";
    /// Order matters (D-1): printable first, escape sequence second, control byte last.
    public static readonly string[] HighlightNextCandidates = ["j", "\x1b[B", "\x0e"];
    public static readonly TimeSpan HighlightSettle = TimeSpan.FromMilliseconds(1500);
}

public enum ClaudeStartupBlockOutcome
{
    None = 0, TrustCleared = 1, TrustNotCleared = 2, NotAnswerable = 3,
    /// A trust dialog was recognised but its layout is not one we know how to answer; NOTHING was typed.
    TrustUnanswerable = 4,
}

public readonly record struct ClaudeStartupBlockResolution(
    ClaudeStartupBlockOutcome Outcome, ClaudeBlockingPrompt? Prompt, string? Detail = null);
// Answered => Outcome is TrustCleared or TrustNotCleared (unchanged); TrustUnanswerable is NOT answered.
```

Detection (`Detect`) is unchanged except the trust arm classifies the layout:

- `NumberedMenu` when the compact screen contains `1yesitrustthisfolder`, or `1yes` together with
  the trust text;
- `HighlightedList` when it contains `yesitrustthisfolder` and (`noexit` or `nocontinuewithout`)
  and `ReadHighlight(screen) != Unknown` **or** the compact contains `entertoconfirm` (the marker
  may sit on a line the compactor cannot see; the highlight reader is the authority at answer time);
- otherwise `Unknown` (the trust text matched but neither shape did).

New pure helper, unit-tested on its own:

```csharp
/// Which option carries the highlight marker. Reads the RENDERED screen line by line: the first
/// line whose first non-blank character is '>' or '❯' names the highlighted option.
public static ClaudeTrustDialogHighlight ReadHighlight(string screen)
```

`compact(line)` of that line containing `yesitrustthisfolder` → `Yes`; containing `noexit` or
`nocontinuewithout` → `No`; anything else (including the composer's own `> ` prompt on a healthy
screen) → `Unknown`.

Answering (`TryAnswerAsync`, both overloads, plus a new `TryAnswerDetailedAsync` returning
`(bool Cleared, string Detail)` that the bool overloads wrap):

```
switch prompt.Layout:
  NumberedMenu:     write("1"); cleared = poll(settle, !IsBlocked)                       // unchanged
  HighlightedList:
     if ReadHighlight(screen) != Yes:
        foreach key in HighlightNextCandidates:
           write(key)
           if poll(HighlightSettle, ReadHighlight(screen) == Yes): break
        if ReadHighlight(screen) != Yes:
           return (false, "highlight never reached 'Yes, I trust this folder' after j/Down/Ctrl+N; Enter withheld; screen title: …")
     write(Enter)
     cleared = poll(settle, !IsBlocked)
     return (cleared, "sent <rung>, highlight on Yes, sent Enter; " + (cleared ? "dialog cleared" : "still on screen after {settle}s: <title of current screen>"))
  Unknown / None:   return (false, "unrecognised trust-dialog layout; nothing typed")
```

`ClearStartupTrustPromptAsync` maps `Layout.Unknown` to `TrustUnanswerable` (typing nothing),
otherwise to `TrustCleared` / `TrustNotCleared` with the `Detail`. The `Answered` property keeps
its meaning so the existing not-cleared test still holds.

Invariants the unit tests pin (the safety property of this card):

1. Enter is **never** written while the highlight is on "No, exit" or unknown.
2. Nothing is written for `Layout.Unknown`, for a permission modal, or for a healthy screen.
3. On a highlighted-list screen whose highlight is already on Yes, the only write is Enter.
4. On the legacy numbered screen, the only write is `"1"`.

### 2.2 Adapters (lockstep)

`RunnerClaudeAdapter` and `server/Infrastructure/Agents/Pty/ClaudeAdapter.cs`:

- add `private AgentLaunchBlock? _launchBlock; public AgentLaunchBlock? LaunchBlock => _launchBlock;`
- on `TrustNotCleared` **and** `TrustUnanswerable`: log `LogError` (existing text, plus `Detail`),
  set `_launchBlock = new AgentLaunchBlock(AgentLaunchBlockKind.TrustDialogNotCleared, reason)`,
  return `false`. Reason text (one line, clipped downstream by `ColumnText`):

  > Claude's trust dialog for `<cwd>` was not cleared (`<layout>`; `<detail>`). Nothing can be
  > delivered to this session. Trust the directory once — set `projects["<key>"].hasTrustDialogAccepted: true`
  > in the session-runner user's `.claude.json` or accept the dialog interactively — then restart.

- `TrustCleared` log gains the `Detail` (which rung fired) at Information so the first live launch
  after land records the measured key in the Aspire console.
- No change in `AgentSessionService`: `WaitForReadyOrThrowAsync` already throws
  `AgentLaunchBlockedException` when `LaunchBlock` is set; the catches persist
  `SessionLaunchBlock.TrustDialogNotCleared` and the reason.

### 2.3 `src/Antiphon.Agents.Pty/ClaudeProjectTrust.cs` (new)

```csharp
public enum ClaudeProjectTrustOutcome { Seeded, AlreadyTrusted, NoConfigFile, Unparseable, Failed }
public readonly record struct ClaudeProjectTrustResult(
    ClaudeProjectTrustOutcome Outcome, string ConfigPath, string Key, string? Error);

public static class ClaudeProjectTrust
{
    /// CLAUDE_CONFIG_DIR set → {dir}\.claude.json (measured, RealCliStubClaudeConfig); else %UserProfile%\.claude.json.
    public static string DefaultConfigPath();
    /// Path.GetFullPath, trailing separators trimmed, '\' → '/', case preserved (Claude's D$).
    public static string ProjectKey(string directory);
    /// Exact-key read (D-4).
    public static bool IsTrusted(string directory, string? configPath = null);
    /// Idempotent, atomic, never throws, memoised per process on Seeded/AlreadyTrusted.
    public static ClaudeProjectTrustResult Seed(string directory, string? configPath = null);
    /// Test-only counterpart used by the canary's Dispose; not called from production.
    public static bool Remove(string directory, string? configPath = null);
}
```

Rules: parse with `JsonNode` (case-sensitive keys, numbers written back verbatim); absent file →
`NoConfigFile`, nothing created; parse failure → `Unparseable`, file untouched byte-for-byte; merge
`hasTrustDialogAccepted: true` into an existing project object or create `{ "hasTrustDialogAccepted": true }`
(mirrors Claude's own remote-runner seed; the check-interpreter entry Claude wrote itself has no
`hasCompletedProjectOnboarding`, so it is not needed); write to `<file>.antiphon-tmp` in the same
directory, `File.Move(tmp, file, overwrite: true)`, UTF-8 without BOM; one retry after 200 ms on
`IOException`, then `Failed` with the message. No logger dependency in the Pty library — the caller
logs from the result.

### 2.4 Provisioner wiring

- `StandingSpecialistProvisioner(…, string? claudeConfigJsonPath = null)`; `PrepareWorkspace`
  calls `ClaudeProjectTrust.Seed(spec.WorkingDirectory, _claudeConfigJsonPath)` after the directory
  and hook writes. Log: `Seeded` → Information ("Seeded Claude trust for {Directory} in {ConfigPath}
  so the {DisplayName}'s first launch skips the trust dialog"); `Unparseable`/`Failed` → Warning
  naming the error and that the launch-time answerer remains the fallback; others → Debug.
- `CheckInterpreterProvisioner` and `DiagnoseProvisioner` take and forward the same optional
  parameter (null → `DefaultConfigPath()`). DI registration unchanged.

### 2.5 Docs

- `docs/session-runtime-invariants.md` Preserved Gotcha #48: append the 2.1.258 shape, the
  verified-highlight rule, the measured rung, the seeder, the canonical-root fact for worktrees, and
  that the failure now names itself (`TrustDialogNotCleared`).
- `docs/agent-kinds.md` §4 "Behaviour worth knowing" bullet: same facts in two sentences.
- `server/Application/Services/ProviderContractCatalog.cs:64` string: mention layout-aware answer
  and CARD-0390.
- `docs/testing-and-build.md`: one line under the headed-tests bullet naming
  `ClaudeTrustPromptCanaryTests` as the canary that must be re-run whenever Claude's trust dialog
  changes, with the run command from §4.4.

---

## 3. Slices

Each slice is one commit, pushed on completion, message carrying the real outcome. Build and test
with `--property:OutputPath=bin-trust/` (forward slash) and delete every `bin-trust` directory
before finishing.

### S1 — Layout-aware answerer (pure, unit-tested)

Files: `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`;
`tests/Antiphon.Agents.Pty.Tests/ClaudeStartupTrustPromptTests.cs`.

- Implement §2.1.
- Replace `TrustScreen` with the live 2.1.258 fixture (§0); keep the old one as
  `LegacyNumberedTrustScreen`. Extend `ScriptedScreen` to model a highlighted list: it tracks the
  highlighted option, moves it on a configurable "moves-on" key (default `j`; a second constructor
  flag makes it move only on `\x1b[B` to exercise rung two), renders the `>` marker on the
  highlighted row, clears to `ReadyScreen` on `\r` **only when Yes is highlighted**, and records
  `Exited = true` if `\r` arrives while No is highlighted.
- Tests (names are the contract):
  - `The_2_1_258_dialog_is_answered_by_moving_the_highlight_then_Enter` → inputs exactly `["j", "\r"]`, `TrustCleared`, `Detail` names `j`.
  - `A_dialog_that_ignores_j_is_moved_with_Down` → inputs `["j", "\x1b[B", "\r"]`.
  - `A_dialog_whose_highlight_never_moves_gets_no_Enter` → inputs contain the three rungs and never `"\r"`; `TrustNotCleared`; `screen.Exited` false.
  - `A_dialog_already_highlighting_Yes_gets_only_Enter` → inputs `["\r"]`.
  - `The_legacy_numbered_dialog_still_takes_the_digit` → inputs `["1"]`.
  - `An_unrecognised_trust_layout_types_nothing` → `TrustUnanswerable`, inputs empty, `Answered` false.
  - `ReadHighlight_names_the_marked_option` → data-driven over `>`/`❯`, Yes/No rows, the gated-grants label, and a healthy composer screen (`Unknown`).
  - Existing healthy-screen and permission-modal tests unchanged and green.
- Run: `dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeStartupTrustPromptTests/*"`.

### S2 — Headed canary retarget and the measurement

Files: `tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs`;
`src/Antiphon.Agents.Pty/ClaudeProjectTrust.cs` (the seeder is needed here for T2, so it is
written in this slice and unit-tested in S4 — or S4 may be pulled ahead of S2; either order works).

- T1 `An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds`: drop
  `AffirmativeKey.ShouldBe("1")`; wait 1.5 s after detection (refuse window); call
  `TryAnswerDetailedAsync`; assert cleared and not blocked; print `Detail`; assert
  `ClaudeProjectTrust.IsTrusted(dir.Path)` is now true (Claude persisted the accept — this is the
  proof the launch changed durable state, and what makes the next launch quiet).
- T1b `The_2_1_258_dialog_is_the_highlighted_list_layout`: `prompt.Layout.ShouldBe(HighlightedList)`
  — the assertion that would have caught this card, and that will loudly fail on the next TUI change.
- T2 `A_trusted_directory_never_reads_as_blocked`: replace `dir.Trust()` with
  `ClaudeProjectTrust.Seed(dir.Path)` and assert `Seeded`; `Dispose` calls `Remove`. This is the
  positive control for D-3 against real Claude.
- **Run it headed, from this worktree, foreground:**
  `$env:ANTIPHON_HEADED_TESTS='1'; dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeTrustPromptCanaryTests/*"`
  Record in the commit message and in §4.4 of this plan: Claude version (`claude --version`),
  which rung moved the highlight, detection time, and that T2 saw no dialog. If **no rung** moves
  the highlight, do not guess a fourth key: capture the screen, try `\x1b[F` (End) and Tab by hand
  in the canary only, and report back with the measurement — the production ladder is only
  extended with a measured key.

### S3 — Named launch block in both Claude adapters

Files: `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs`;
`server/Infrastructure/Agents/Pty/ClaudeAdapter.cs`;
`tests/Antiphon.Tests/Agents/RunnerClaudeAdapterTrustPromptTests.cs`;
`tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`.

- Implement §2.2.
- Replace the adapter test's `TrustScreen` with the live fixture; teach `ScreenScriptedRunnerClient`
  the same highlight model as S1's `ScriptedScreen` (moves on `j`, clears on `\r` only when Yes is
  highlighted, `Exited` on a wrong Enter).
- Tests: `A_launch_into_an_untrusted_directory_answers_the_dialog_before_reporting_ready` → first
  two inputs `["j", "\r"]`, then the probe token and Ctrl+U; new
  `A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block` → ready false,
  `adapter.LaunchBlock!.Kind == TrustDialogNotCleared`, reason contains the cwd and
  `hasTrustDialogAccepted`, `client.Exited` false, no probe input sent; new
  `An_unrecognised_trust_layout_fails_the_launch_without_typing` → inputs empty, block set. In
  `AgentSessionLaunchFailureTests`, mirror `Interactive_sign_in_block_persists_LaunchBlock_and_the_named_reason`
  for `TrustDialogNotCleared`: session row carries `SessionLaunchBlock.TrustDialogNotCleared` and
  `FailureReason` equals the block reason; no `ProviderSignInRequired` incident is raised.
- Run: `--treenode-filter "/*/*/RunnerClaudeAdapterTrustPromptTests/*"` and
  `"/*/*/AgentSessionLaunchFailureTests/*"` on `tests/Antiphon.Tests` (class filters, not the
  namespace).

### S4 — Seeder unit tests and provisioner wiring

Files: `src/Antiphon.Agents.Pty/ClaudeProjectTrust.cs` (if not already in S2);
`tests/Antiphon.Agents.Pty.Tests/ClaudeProjectTrustTests.cs` (new);
`server/Application/Services/StandingSpecialistProvisioner.cs`, `CheckInterpreterProvisioner.cs`,
`DiagnoseProvisioner.cs`;
`tests/Antiphon.Tests/Application/DiagnoseProvisionerTests.cs`, `CheckInterpreterProvisionerTests.cs`.

- `ClaudeProjectTrustTests` (temp config file per test, never the real one):
  - seeds `C:/…` forward-slash, case-preserved key; other projects, top-level fields, numbers and
    the case-variant duplicate keys (`C:/src/antiphon` + `C:/src/Antiphon`) survive byte-equal
    after a second parse;
  - merges into an existing project object without dropping its fields;
  - `AlreadyTrusted` on repeat and the file's last-write time does not move;
  - `NoConfigFile` creates nothing; `Unparseable` leaves the file byte-identical;
  - `ProjectKey` trims a trailing separator and does not lower-case;
  - `Remove` deletes only the key it is given.
- Provisioner tests: construct with the temp config path (D-6 — a test that omits it is a defect);
  new `the_specialists_working_directory_is_seeded_as_trusted_in_claude_json` on both facades;
  new `a_missing_claude_json_is_tolerated_and_logged` (no file → agent still created).
- Run the two provisioner classes and `ClaudeProjectTrustTests` by class filter.

### S5 — Docs and catalog strings

Files: `docs/session-runtime-invariants.md`, `docs/agent-kinds.md`, `docs/testing-and-build.md`,
`server/Application/Services/ProviderContractCatalog.cs`, this plan (§4.4 measurement filled in).
No tests beyond a build; `ProviderContractCatalogTests` if it pins the string (check before editing).

Order: S1 → S2 → S3 → S4 → S5. S2's measurement gates nothing in S1/S3 (the ladder is safe under
D-1) but must be recorded before S5 writes the docs.

---

## 4. Verification design

### 4.1 What "verified" means for this card

The card is a launch-reliability defect that every existing green test failed to see. "Done"
therefore requires all four rungs of evidence, in this order, and the report names each:

| Rung | Proves | Where it runs |
|---|---|---|
| Unit (S1, S4) | the algorithm and its safety invariants against the **live** fixture | this worktree, `Antiphon.Agents.Pty.Tests` |
| Adapter (S3) | the production adapter fails a stuck dialog loudly and by name, and types nothing dangerous | this worktree, `Antiphon.Tests` |
| Headed canary (S2) | real `claude.exe 2.1.258` accepts the ladder and Enter, persists trust, and honours a seeded key | this worktree, `ANTIPHON_HEADED_TESTS=1` |
| Post-land positive control (PC1, PC2) | the whole production path — server, launch queue, runner, supervisor — brings a standing agent in a genuinely fresh directory to `Running` | main checkout, after restart; orchestrator/operator |

A fixture-only pass is explicitly **not** done for this card (that is exactly the state that let
the 2026-08-16 fixture stay green while the seat failed 16 times).

### 4.2 Negative control (already measured)

The pre-fix signature is on record and must not recur after land: `POST /api/agents/fe052653-dfbb-4eae-aae3-8726a5157341/start {"fresh":true}`
→ `Running` for ~25 s → `Failed`, `consecutiveFailures` +1, incidents `Crash` ("Agent process did
not become ready." / `KilledByRequest`), `TranscriptBindFailed`, buffer still showing `> No, exit`.
(Investigation §Live restart, 2026-09-05T03:06Z.)

### 4.3 Unit and adapter runs (Build executes)

```
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeStartupTrustPromptTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeProjectTrustTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/RunnerClaudeAdapterTrustPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/DiagnoseProvisionerTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/CheckInterpreterProvisionerTests/*"
```

Run `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` one after the other, never together. A red
that is not in a touched class is checked at the base commit (`317a56ef`) with the same class
filter before it is blamed on this change.

### 4.4 Headed canary — the measurement protocol (Build executes, foreground)

Preconditions: Windows, `claude` on PATH (2.1.258 today), `ANTIPHON_HEADED_TESTS=1`, no other
headed test running (the `Headed` `NotInParallel` group and `ProcessSpawnLimit` enforce this
inside one run).

```
$env:ANTIPHON_HEADED_TESTS='1'
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeTrustPromptCanaryTests/*"
```

Expected: T1 detects within 3 s, `Detail` names the rung, the dialog clears, `IsTrusted(dir)` is
true afterwards; T1b pins `HighlightedList`; T2 seeds, launches, reaches ready with no dialog.
Both temp keys are removed by `Dispose` (verify with the `pwsh -AsHashtable` key listing that the
count of `antiphon-trust-canary` keys in `~/.claude.json` did not grow).

Record here after the run:

| Field | Value |
|---|---|
| Claude version | _fill in (`claude --version`)_ |
| Rung that moved the highlight | _fill in: `j` / Down / Ctrl+N_ |
| Detection time | _fill in_ |
| T2 (seeded) showed a dialog? | _must be no_ |

If T1 self-skips with "no trust dialog appeared", the temp root is trusted on that machine; pick a
sibling under `C:\logs\antiphon\` for the canary directory instead (it is outside any git root and
no ancestor is trusted on this box).

### 4.5 Post-land positive controls (orchestrator, main checkout)

Run after `-Land` completes and `restart-apphost.ps1` (from the main checkout, after
`git pull --rebase` there) reports healthy; confirm the new code loaded (grep the built server DLL
for `ClaudeProjectTrust`) before trusting any of the following.

**PC1 — the answerer, on a genuinely fresh directory through the production path.**

1. `POST /api/agents` with `{"name":"trust-canary-<yyyymmdd>","workingDirectory":"C:\\logs\\antiphon\\trust-canary-<yyyymmdd>","createWorkingDirectory":true,"modelLevel":"Low"}`
   (field names per `CreateAgentRequest`, `server/Application/Dtos/AgentDtos.cs:268`). Confirm
   `~/.claude.json` has **no** `C:/logs/antiphon/trust-canary-<yyyymmdd>` key before starting.
2. `POST /api/agents/{id}/start` with `{}`. Expect the agent `Running` with a live session within
   ~20 s and **staying** Running past 60 s; `GET /api/sessions/{sid}/buffer` shows a composer, not
   `> No, exit`; the runner row reports `transcriptBound: true` after the first delivery.
3. `~/.claude.json` now carries the forward-slash key with `hasTrustDialogAccepted: true` (Claude
   wrote it on accept — this is the durable proof the dialog was answered, not merely hidden).
4. Aspire console for that session id shows the Information line
   "opened on Claude's trust dialog … answered" with the rung in its detail.
5. Stop and `DELETE /api/agents/{id}`; remove the directory and (optionally) the key.

**PC2 — the seeder, on the diagnose seat.**

1. After restart, the `DiagnoseHostedService` / first sweep calls `EnsureAsync` → `PrepareWorkspace`
   → seed. Expect `C:/logs/antiphon/diagnose` in `~/.claude.json` and the Information log
   "Seeded Claude trust for C:\logs\antiphon\diagnose".
2. `POST /api/agents/fe052653-dfbb-4eae-aae3-8726a5157341/start {"fresh":true}` → `Running`,
   no `Crash` / `TranscriptBindFailed` triplet in `GET /api/agents/{id}/incidents`; buffer shows
   the composer. After `HealthyUptimeResetMinutes`, `consecutiveFailures` returns to 0.
3. `pwsh -File scripts/card.ps1 diagnose <card>` returns a reading instead of `DiagnoseUnavailable`
   — CARD-0352's function restored.

**Failure naming check (either control).** If any Claude launch still fails on the dialog, the
session row must show `launchBlock: TrustDialogNotCleared` and a `FailureReason` naming the cwd
and `hasTrustDialogAccepted`, and the supervisor `Crash` incident must carry that text. A generic
"Agent process did not become ready." on a trust-dialog failure after land is itself a defect.

### 4.6 Cleanup

Delete every `bin-trust` directory (`Get-ChildItem <root> -Recurse -Depth 2 -Directory -Filter bin-trust | Remove-Item -Recurse -Force`),
remove the PC1 throwaway agent and directory, and leave `~/.claude.json` with no new
`antiphon-trust-canary*` keys.

---

## 5. Risks, non-goals, open questions

- **Risk — none of the three rungs moves the highlight.** Under D-1 the outcome is "dialog still
  up, named", identical in effect to today but visible. S2 measures before S5 documents; a
  fourth rung is added only from a measurement.
- **Risk — a second startup modal after the trust dialog.** If 2.1.258 stacks another dialog
  (bypass-permissions warning, external-includes approval), `IsBlocked` stays true after Enter and
  the block reason will carry that screen's title, which is the right thing to see. Not observed on
  this box (the bypass warning is globally accepted; the generated `CLAUDE.md` floor has no
  `@include`).
- **Risk — lost-update race on `~/.claude.json`.** Bounded by D-3: one write per specialist ever,
  atomic replace, never on a parse failure. Claude's own writes are also whole-file rewrites; the
  residual is a dropped `numStartups`-class field, not corruption.
- **Risk — server and session-runner run as different Windows users.** Then the seeded file is the
  wrong user's and the seed is a silent no-op for Claude; the answerer still covers the launch and
  the block reason names the remedy. On this box both are the same user (per-user Scheduled Task
  "Antiphon Session Runner", `docs/bootstrap.md`). Documented, not solved.
- **Non-goal — text-mode trust prompt** (`cn()` true, "Please answer y or n."). Not the pty case;
  would surface as `TrustUnanswerable` with the screen title.
- **Non-goal — seeding for every agent create.** See D-3's rejected alternative; a follow-up card
  if an operator-created standing agent in a non-git directory becomes common.
- **Non-goal — supervision policy for a deterministic block.** Backoff and `BackoffEscalated`
  are unchanged; the block now names itself, which is what the investigation asked for.
- **Open — the refuse window length** (`tw`) was not recovered from the binary. Production answers
  after ≥5 s quiet; the canary waits 1.5 s after detection. If T1 reports the first rung refused,
  raise the canary's pause, not the production gate.

---

## 6. Next stage

`next: code`. Build executes S1–S5 in order, runs the headed canary in S2 and fills §4.4, and hands
PC1/PC2 (§4.5) to the orchestrator as the post-land step.
