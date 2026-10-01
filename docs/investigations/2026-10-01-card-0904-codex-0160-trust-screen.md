# CARD-0904: Codex 0.160.0 trust screen and the failing image-verifier TRUST row

Date: 2026-10-01. Stage: Investigate (read-only). Start ref: `4b973670482d3215165de47e00b596d224292c28`
(branch `feat/card-task-27f2985f`). Image under test: `antiphon-card-0904:4b973670482d3215165de47e00b596d224292c28`,
`sha256:9be4695301e89ab7e48dc981654fae659dfa29d7f14e7cce14de189793c09655` (the corrected build; the label
`org.opencontainers.image.revision` is the same SHA). No model turn, no login and no network: every probe ran with
`--network none`, a tmpfs `CODEX_HOME`, and either no auth or the verifier's dummy key aimed at `127.0.0.1:9`.

## Verdict: confirmed, verifier-only defect

The TRUST row's failure is a **probe artifact**. Codex 0.160.0 **does** render the untrusted-folder modal with
the same wording as 0.156.1 ("Trust this folder?" / "› 1. Trust and continue  2. Quit" / "enter continue · esc
quit"), in the same onboarding order. Two things together make the verifier's grep miss it:

1. **0.160.0 changed how it paints text, not what it says.** 0.156.1 wrote the modal as literal text with
   spaces. 0.160.0 positions every word with an absolute cursor move (CUP, `ESC[row;colH`) and emits no space
   bytes between words. Raw bytes from the 0.160.0 control capture:

   ```
   ESC[8;3H ESC[22m Trust ESC[8;9H this ESC[8;14H folder? ESC[8;22H Codex ESC[8;28H can ESC[8;32H read, ...
   ```

2. **The verifier's escape-strip regex eats one letter per positioned word.** `tui_capture` in
   `docker/session-runner-grok/verify-codex-image.sh:46` strips with
   `sed -e 's/\x1b\[[0-9;?]*[ -\/]*[@-~]//g'`. In a POSIX bracket expression the backslash is literal, so
   `[ -\/]` is the *range* 0x20–0x5C (space through backslash, which includes digits and all upper-case
   letters) plus `/`, not "0x20–0x2F". For `ESC[8;9Hthis`, the intermediate class swallows `H` and the
   final-byte class `[@-~]` then swallows `t`. Measured: `printf 'x\x1b[8;9Hy' | sed -e '<that expr>'` →
   `x` (the `y` is gone). The control's stripped text is therefore
   `Trusthisolder?dexanead,dit,...`, so `grep -q 'Trust this folder'` fails and the row reports
   `control never rendered the trust modal`. Even with a correct regex, deleting CUP without putting back a
   space yields `Trustthisfolder?`, which still fails the literal grep.

The defect was latent under 0.156.1 only because that version wrote literal spaces. (The CARD-0660 0.156.1
capture `docs/investigations/evidence/card-0660/0156-linux-worktree-trust-prompt.txt` shows the contiguous text.)

**Production parsers are unaffected.** Feeding the 0.160.0 raw captures through Antiphon's own `TerminalScreen`
(the same class `PtyAgentRunner`/`SessionRunnerRuntime` use) and calling the production detectors gives the
expected verdict on every screen (table below). `CodexTrustPromptDetector` compacts whitespace before matching
(`CodexDetectors.cs:334`), and `AnsiStripper.Clean` does not eat letters, so even the raw-text path
(`IsVisible(raw)`) matches.

## Reproduction

All runs: `docker run --rm --network none --user 1654:1654 --tmpfs /c660-home:exec,... --tmpfs /work:exec,...`,
the image above, `stty cols 120 rows 40`, `TERM=xterm-256color`, `script -q -f -e`, `timeout 20` (or 25/40).
The cwd is either the verifier's linked worktree `/work/worktrees/c660-trust-probe` (root `/work/repos/antiphon`,
not trusted in the scratch home) or a plain non-git dir `/tmp/plain`. `CODEX_HOME` holds only
`check_for_update_on_startup = false`. "key" means the verifier's `OPENAI_API_KEY=c660-dummy-not-a-credential`
plus its stub provider `-c` args.

| Run | cwd | auth | `--dangerously-bypass-…` | Final screen | Old strip has `Trust this folder` | Fixed strip has it |
|---|---|---|---|---|---|---|
| A (= verifier control) | worktree | key | yes | trust modal | no | **yes** |
| B | worktree | key | no | trust modal | no | **yes** |
| C | worktree | none | yes | sign-in chooser | no | no (correct) |
| D | worktree | none | no | sign-in chooser | no | no (correct) |
| E | /tmp/plain | key | no | ready composer, no modal | no | no |
| F | /tmp/plain | none | no | sign-in chooser | no | no |
| G | /tmp/plain | key | yes | ready composer, no modal | no | no |
| H | worktree | key | yes, Enter sent at 10 s | ready composer | — | — |
| I | worktree | none | yes, 40 s window | sign-in chooser for 40 s | no | no |

Run H wrote the expected trust entry for the repository root (`config.toml` after Enter):

```toml
[projects."/work/repos/antiphon"]
trust_level = "trusted"
```

The 0.160.0 TUI also wrote `[tui] screen_reader_detection_done = true` (all runs) and
`[tui.model_availability_nux] "gpt-6.1-sol" = 1` (run H) into the home's `config.toml`.

Re-running the real `trust` row with the image (seeded `/state/codex/config.toml` = the exact `SEEDED_CONFIG`):

- the committed script gives `C660_ROW trust fail control never rendered the trust modal` (exit 1), which
  reproduces `.antiphon/card-0904-image-corrected/row-trust.txt` in task 27f2985f;
- a scratch copy changing **only** line 46 (below) gives
  `C660_ROW trust ok root trust covers linked /work/worktrees/c660-trust-probe (control rendered the modal)`
  (exit 0), and `config-accepted` stays `ok` under the same change.

## Answers

### 1. The exact 0.160.0 screens and flow (untrusted folder, no auth)

Onboarding order is **unchanged from 0.156.1**: with no auth the **sign-in chooser** comes first and stays up (40 s
observed, run I); the trust modal is only reached once auth exists (runs A/B with the dummy key). The 0.156.1 evidence
shows the same split (`0156-linux-signed-out.txt` had no auth; `0156-linux-worktree-trust-prompt.txt` used the
dummy key). Before either screen, 0.160.0 briefly paints its new compact header (`>_ OpenAI Codex (v0.160.0)`,
`loading`, a random greeting line, `› Ask Codex to do anything`) and then replaces it with the modal. This matches the
0.159.0 note quoted below. As in 0.156.1, a non-git untrusted cwd (`/tmp/plain`) shows no trust modal at all (runs E/G).

Rendered by `Antiphon.Agents.Pty.TerminalScreen(120, 40)` from the raw `script` logs, trailing blanks trimmed:

0.160.0 trust modal (runs A and B are identical):

```text
  Folder access
  /work/worktrees/c660-trust-probe
  Note: You’re in a subdirectory of a Git project. Trusting will apply to the repository root:
  /work/repos/antiphon
  Trust this folder? Codex can read, edit, and run files here, subject to your permission settings. Folder settings
  can run code automatically, even without a model request. Continue only if you trust these files. Your trust
  decision will be saved.
› 1. Trust and continue
  2. Quit
  enter continue · esc quit
```

0.160.0 signed-out chooser (run I):

```text
  Welcome to Codex, OpenAI's command-line coding agent
  Sign in with ChatGPT to use Codex as part of your paid plan
  or connect an API key for usage-based billing
> 1. Sign in with ChatGPT
     Usage included with Plus, Pro, Business, and Enterprise plans
  2. Sign in with Device Code
     Sign in from another device with a one-time code
  3. Provide your own API key
     Pay for what you use
  Press enter to continue
```

0.160.0 after Enter on the trust modal (run H):

```text
  >_ OpenAI Codex (v0.160.0)
     /work/worktrees/c660-trust-probe
  permissions: YOLO mode
  Hello again. What’s the plot this time?
  Tip: Maximize usage with GPT-6.1 Sol. Try it on complex work for near-Astra performance at a lower cost.
› Ask Codex to do anything
  GPT-6-Sol default · /work/worktrees/c660-trust-probe                                         ⚠ 1 warning · f2 to view
```

(The greeting line is randomised per launch: "Forty-two is an answer…", "What are we getting into today?", and so on.)

The old verifier strip of the same control (run A), which is what the row grepped:

```text
… Folder access/work/worktrees/c660-trust-probe Note: You’re in a subdirectory of a Git project. Trusting will apply
to the repository root:/work/repos/antiphon Trusthisolder?dexanead,dit,ndunilesere,ubjectoourermissionettings.…
› 1. Trust and continue 2.itenter continue · esc quit
```

### 2. Probe artifact or UX change?

Probe artifact. Production detector verdicts on the rendered 0.160.0 screens (scratch console referencing
`src/Antiphon.Agents.Pty`, built under the build-slot gate, output outside the repo):

| Screen | `IsVisibleOnCurrentScreen` (CodexDetectors.cs:306) | `IsAcceptSelectedOnCurrentScreen` (:321) | `IsVisible(raw)` (:296) | `CodexStartupScreen.Classify` (CodexStartupReadiness.cs:53) |
|---|---|---|---|---|
| trust modal (A, B) | True | True | True | NotReady / `Trust` |
| signed-out (I) | False | False | False | NotReady / `SignIn` (via `ContainsSignIn`, :294-299) |
| after Enter (H) | False | False | True (history only) | Ready |
| plain dir (G) | False | False | False | Ready |

- `CodexDetectors.cs:288`: the trust wording is unchanged, so no change is needed.
- `CodexStartupReadiness.cs:42`/`:53` (hint, Classify): `Ask Codex to do anything` is still the idle hint, so
  ready is still detected.
- `:298` (`ContainsSignIn`): `Sign in with ChatGPT` and `Sign in with Device Code` are still present.
- Trust-acceptance keystroke: Enter on the default highlight (`› 1. Trust and continue`) accepts and writes root
  `trust_level = "trusted"` (run H), so the contract is unchanged.
- `config-accepted` step: the row passes on the corrected image (task 27f2985f evidence and the rerun above).
  It greps `Sign in with ChatGPT`, which 0.160.0 writes contiguously.

`IsVisible(raw)` stays true after acceptance because the raw history still holds the modal. This is the existing,
documented CARD-0574 D-5 behaviour (current-frame decisions use the rendered screen) and is not new in 0.160.0.

### 3. Minimal fix

It is a verifier-only fix: one line, `docker/session-runner-grok/verify-codex-image.sh:46` (`tui_capture`'s strip).
Turn cursor-position/forward moves into a space before the generic strip, and correct the intermediate range.
This is the exact line validated above:

```sh
  sed -e 's/\x1b\[[0-9;]*[HC]/ /g' -e 's/\x1b\[[0-9;?]*[ -/]*[@-~]//g' -e 's/\x1b\][^\x07\x1b]*\(\x07\|\x1b\\\)//g' "$log" | tr -s '\r\n ' ' ' > "$log.txt"
```

Checked against all nine captures: the fixed strip finds `Trust this folder` exactly on the modal runs (A, B, H's
history) and nowhere else. `Sign in with ChatGPT`, `Ask Codex to do anything` and `OpenAI Codex (v0.160.0)` are found
wherever they are on screen. `scripts/verify-card0660-codex-image.ps1` needs no change: it only runs the rows and
collects `C660_ROW` lines.

Optional parser regression coverage. No production change is needed, but the 0.160.0 paint style is new. Proposed
fixture paths under `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/`:
`v0160-trust-prompt.txt`, `v0160-signed-out.txt`, `v0160-ready.txt` (the three rendered screens above, verbatim).
Add `v0160-trust-prompt.raw.txt` (the raw `script` body of run A) as well, to exercise `TerminalScreen` + the detectors
end to end on CUP-positioned words. Wire them in `CodexStartupFixtures.cs` and mirror the
`CodexV0156StartupPromptTests` cases.
A shell-level guard in `CodexRunnerImageContractTests.cs` could pin the strip line so the `[ -\/]` range cannot return.

### 4. Can the deploy proceed?

Yes, after the one-line verifier fix lands and `scripts/verify-card0660-codex-image.ps1` is rerun on the image
built from that commit and shows **8/8**. Nothing found here blocks the Codex 0.160.0 trust/sign-in/ready handling
in Antiphon's runtime.

## Vendor changelog (github.com/openai/codex/releases, 0.157–0.160)

Only lines touching onboarding or startup screens. No stable release note mentions the trust prompt, directory
approval or a TUI paint change.

- 0.159.0: "New sessions get a compact welcome screen and consistent headers, with occasional tips during and after turns."
- 0.159.0: "Local ChatGPT sign-in opens the browser reliably; onboarding also provides a shortcut to copy the login link."
- 0.159.3: "Eligible local sessions signed in with ChatGPT can now show optional reminders to complete account security setup."
- 0.160.0, 0.158.0, 0.157.x: nothing on trust or onboarding (0.158.0 approval lines concern macOS path aliases and
  approval-review retries).

The per-word CUP painting is not in any release note. It is measured here, not sourced.

## Remaining uncertainties

- Geometry: these captures used 120×40. Production `TerminalScreen` defaults to 120×30. The modal has 10 rows, so it
  fits, but a 30-row capture was not taken.
- Post-sign-in order: whether a real ChatGPT login lands on the trust modal next could not be observed without a
  credential. Here, auth by API key reached the modal directly, which is consistent with the 0.156.1 flow.
- The optional account-security reminder (0.159.3) for ChatGPT-signed-in sessions is not observable without a
  real login. If it appears as a startup modal, it is a separate, unmeasured screen.

## Not done, noted

- Fix idea (not implemented): replace line 46 of `verify-codex-image.sh` with the strip above, rerun the image
  verifier for 8/8, and optionally add the `v0160-*` fixtures.
- No raw capture files were committed (single-doc constraint). The scratch captures were not kept beyond this session.
  The Code stage can regenerate them with the `docker run` shape above.
