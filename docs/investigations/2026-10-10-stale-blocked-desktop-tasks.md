# Stale Blocked desktop tasks 0726e42d and a01b0301

Date: 2026-10-10. Read-only investigation, Antiphon task 6d382627. Neither
task was cancelled, replied to or modified.

Both tasks belong to the **markdown-package** project
(`160cda04-2245-45c6-8570-ff5e5a28e8b6`, repo `C:\src\markdown-package`,
GitHub `michal-ciechan/markdown-package`), not to Antiphon. The Antiphon
master history is irrelevant to both. The evidence below comes from
`GET /api/agent-tasks/{id}`, `GET /api/sessions/{id}/transcript`,
`card.ps1` against board `markdown-package`
(`bd4b30fb-03fe-4527-bbbd-a488b964ad68`), and a read-only clone of the
markdown-package remote taken on 2026-10-10. markdown-package
`origin/master` was `d28f512` (2026-10-06 08:03 +0100), its last commit.

## Verdicts

| Task | Verdict | Safe to cancel? |
| --- | --- | --- |
| a01b0301 "Desktop GUI acceptance checks" | **Obsolete.** Task e3922461 later ran the same three checks hands-on | Yes. Its branch holds nothing that is still needed |
| 0726e42d "Fix forwarded launch stall" | **Still carries unmet work.** The fix is unlanded and unverified, and no card tracks it | Cancelling the task is safe only once a card owns branch `feat/card-task-0726e42d` (proposed below). The task itself cannot finish the work: its session is dead and it needs an interactive desktop |

## (b) a01b0301 Desktop GUI acceptance checks

**Record.** Worker/Investigate, Codex High, bound to CARD-0072. Created
2026-09-24 10:23Z, Blocked 10:38Z. Worktree base `e55be09` (explicit). Session
`e88b0851` is `Stopped`, ended 2026-09-27 15:54Z. Deliverable
`docs/investigations/2026-09-24-card-0072-w2-os-entry.md` on
`feat/card-task-a01b0301` at `724e392`.

**Brief.** It asked for three hands-on checks: (1) CARD-0071 R13, the
enhanced `showOpenFilePicker` route; (2) a physical Explorer drag of a
`.mdpkg`; (3) right-click `.md` **Open with** after installing the NSIS debug
installer, followed by an uninstall-restoration check.

**Why it is blocked.** `blocked.reason=marked-blocked` and
`kind=Question`. The "question" is just the delegate's own handoff: "Repeat
the three physical GUI checks in an unlocked interactive Windows session".
Transcript entries 4–6 show that Windows was at the lock screen and that
`SetForegroundWindow` returned Access is denied. Nothing was verified, and the
installer was not run.

**Superseded.** Task **e3922461** "Desktop GUI verification" (ClaudeCode,
created 2026-10-05 20:06Z, Succeeded 20:51Z) was given the same checks on an
unlocked desktop. Its notes commit `db19017` on `feat/card-task-e3922461`
(on top of `18c502e`) adds a "Hands-on desktop pass (2026-10-05)" section to both
investigation docs:

- **Item 1:** `#enhanced-open` fired `showOpenFilePicker`, the typed package
  path opened `guide.md`, and the draft and recents survived a relaunch. "R13
  is now verified."
- **Item 2:** "Physical Explorer drag of `dropped.mdpkg` onto the window":
  opened and added to recents.
- **Item 3:** Installed **Open with** listed the app and a cold start
  opened the `.md`. `.md` UserChoice stayed `Typora.md`. Uninstall `/S`
  removed the app, the uninstall entry and the `MdpkgViewer.md` candidate.
  The only residue was the known W4b `.mdpkg` orphan key.

All three a01b0301 items are therefore covered. The **Open with** check
gained one finding, the running-instance stall, which is task (a)'s defect.

**Its branch.** `724e392` sits on `e55be09`, a pre-rebase W2 line. markdown-package master
carries rebased equivalents of that W2 line (for example `6604f9b` "Record W2
native acceptance…"), and its only new content is the locked-screen attempt.
It is not an ancestor of master. Nothing on it is needed.

**Residual, not a01b0301's.** `db19017` itself is also not on markdown-package
master. `git grep 'Hands-on desktop pass' origin/master` finds no match, so
master's W1/W2 investigation docs still record R13, drag and Open with as
unverified. This belongs with the CARD-0071/0072 close-out (both cards are
still `InProgress`), not with a01b0301.

## (a) 0726e42d Fix forwarded launch stall

**Record.** Worker/Custom, ClaudeCode High, no card (`cardId=null`), run on
desktop, Windows. Created 2026-10-05 22:42Z, Blocked 23:24Z. Base
`1d08ff4` (master at the time). Session `e231d8bf` is `Failed`, ended
2026-10-06 07:00Z. `blocked.canAnswer=true`, but no live session remains to
answer it.

**Brief.** Find and fix the defect where a forwarded second launch or
Open with brings the window to the front, but the page does nothing until
a click. It was found by e3922461 in 4 of about 21 forwarded launches. The
brief also asked for a debugger-free regression check, at least 20
before/after forwarded launches across the absolute, relative, Open with and
unsaved-draft cases, a green web-viewer lane, and "commit and push; do not
land".

**What it did (transcript tail, entries 198–202, and the result).**

- **Root-cause hypothesis:** tao's `set_focus` falls back to injecting a
  synthetic Alt key when `SetForegroundWindow` is refused. The lone Alt puts
  WebView2 into keyboard menu mode, which holds IPC replies until a click or
  a focus change. Sourced to
  `tao-0.35.3/src/platform_impl/windows/window.rs` `force_window_active` and
  upstream issue tauri-apps/tauri#13300.
- **Status of the hypothesis:** its own words are "The stall itself has not
  been reproduced". The before variant also went 20/20 with 0 stalls on the
  display-less session, because "without a display Windows never takes the
  path that injects Alt".
- **Commits:** `3b1f95a` replaces `set_focus` with `bring_to_front` (restore,
  show, one plain `SetForegroundWindow`, `windows-sys` 0.60) and adds a
  debug-only `MDPKG_ACK_LOG`. `fb06b0a` adds
  `docs/investigations/2026-10-06-card-0072-forwarded-open-stall.md`,
  `src/desktop/tests/forward-focus-smoke.mjs`, a static guard in
  `shell.test.mjs` and README notes. The diff is 7 files, +328/−4.
- **Tests:** cargo 2/2, desktop node 3/3, web-viewer 244/244. Playwright 289
  passed, 6 skipped and 1 failed (webkit `persistence.spec.js:666`,
  attributed to load; compare markdown-package CARD-0082).

**What it is blocked on.** The delegate could not run the visible-desktop
before/after because the RDP client was minimized. The question asks for step 3 of
its "To finish" list: a manual matrix of about 22 launches with the viewer
behind another window. Steps 1–2 are `forward-focus-smoke.mjs` before (using
`C:\Antiphon\evidence\card-task-0726e42d\before-exe\mdpkg-viewer-before.exe`)
and after, 20 launches each.

**Not fixed or superseded on master.**

- `git ls-remote` shows `feat/card-task-0726e42d` = `fb06b0a`.
  `git merge-base --is-ancestor` reports that neither `3b1f95a` nor `fb06b0a`
  is on markdown-package `origin/master` (`d28f512`).
- master `src/desktop/src-tauri/src/lib.rs:88-94` still has the
  single-instance callback calling `window.show(); window.set_focus();` (line
  92).
- The only master commits after the base `1d08ff4` are `b5a82ac`, `fbca318`
  and `d28f512`, all desktop CI/release-doc work (task a6fe70f2/4051f897).
- No later markdown-package task targets the stall. The project task list
  since 2026-09-20 ends with `4051f897` "Fix CI doc findings" on 2026-10-06.

**Not filed as a card.** The markdown-package board was searched with
`-All` for `forward`, `stall`, `set_focus`, `focus`, `launch`,
`click inside` and `WebView2`. No card describes the forwarded-open stall.
CARD-0072 (W2, `InProgress`) is its parent milestone, but its text predates the
finding. CARD-0085 (W2 test hardening, Backlog) covers fs scope-grant and Ctrl+V
mutation survivors only.

**Unmet work.**

1. The visible-desktop reproduction is missing. Nothing yet confirms that the
   stall reproduces on the before build, so the root cause is still a
   hypothesis.
2. Before/after counts: `forward-focus-smoke.mjs` 20 + 20, and the manual
   matrix of about 22 launches covering absolute, relative, double-click,
   Open with and unsaved draft, each with the viewer occluded.
3. Review and landing of `3b1f95a`/`fb06b0a`. The branch is based on `1d08ff4`;
   master has since gained three CI/doc commits. Those commits touch the
   desktop README release section, so a README conflict is possible. Not checked.
4. The webkit `persistence.spec.js:666` failure is already tracked on the
   board as CARD-0082.

## Proposed card (not filed)

Board: `markdown-package`. Parent: CARD-0072 / CARD-0070.

**Title:** W2 defect: forwarded opens stall until a click (verify and land
`feat/card-task-0726e42d`)

**Description:**

> Found in the hands-on W2 pass (task e3922461, notes `db19017` on
> `feat/card-task-e3922461`). A second launch, double-click while running, or
> running-instance Open with forwards the path and raises the window, but the
> page does nothing until the user clicks inside it: no open, no confirm, no
> IndexedDB write. Seen on 4 of about 21 forwarded launches. Tauri IPC replies
> were held and then delivered in one burst on a focus change.
>
> Candidate fix on `feat/card-task-0726e42d` (`3b1f95a`, `fb06b0a`, task
> 0726e42d, not landed). The hypothesis is that tao `set_focus`
> (`force_window_active`) injects a lone Alt when `SetForegroundWindow` is
> refused, which leaves WebView2 in keyboard menu mode. The fix replaces it
> with `bring_to_front`: restore, show, one `SetForegroundWindow`, and no
> injected input. It also adds a debug-only `MDPKG_ACK_LOG`,
> `src/desktop/tests/forward-focus-smoke.mjs` and a `shell.test.mjs` guard.
> Root cause and procedure are in
> `docs/investigations/2026-10-06-card-0072-forwarded-open-stall.md` on that
> branch. The stall has NOT yet been reproduced: the author session had no
> visible display.
>
> Needs an interactive, unlocked Windows desktop (not minimized RDP):
>
> 1. Run `forward-focus-smoke.mjs` 20 launches against the before exe
>    (`C:\Antiphon\evidence\card-task-0726e42d\before-exe\`) and show stalls.
> 2. Run the same 20 against the fixed build and show 0 stalls.
> 3. Run the manual matrix of about 22 launches (absolute, relative + cwd,
>    double-click, Open with, unsaved draft) with the viewer behind another
>    window.
>
> Then rebase onto master (master gained `b5a82ac`/`fbca318`/`d28f512`; the
> desktop README may conflict), review, and land. Keep tauri-plugin-dialog
> unregistered. If step 1 shows no stalls, the root cause is unconfirmed:
> return to investigation and do not land on the strength of the static guard.
> Supersedes task 0726e42d, which can then be cancelled.

A smaller separate follow-up, at the owner's discretion: land `db19017`'s
hands-on notes, or an equivalent, into master's
`docs/investigations/2026-09-24-card-0071-w1-shell.md` and
`…-card-0072-w2-os-entry.md`, so the CARD-0071/0072 close-out has its
evidence on master. This also lets a01b0301 be cancelled with no loss.

## Remaining uncertainties

- The tao Alt-injection mechanism is a source-read hypothesis, not a
  reproduction. This investigation did not run anything on Windows (the
  runner is Linux).
- No check was made that `C:\Antiphon\evidence\card-task-0726e42d\` and
  `C:\Antiphon\evidence\card-task-e3922461-handson\` still exist on the
  desktop.
- The rebase of `feat/card-task-0726e42d` onto current master was not
  attempted.

## Not done, noted

- Fix idea, per the investigate-stage rule: commission the proposed card as a
  hands-on Windows task on the existing branch, then cancel 0726e42d and
  a01b0301.
