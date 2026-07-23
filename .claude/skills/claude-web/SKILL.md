---
name: claude-web
description: Drive claude.ai (Claude web) via browser-harness over CDP Edge — verify agent remote-control sessions, their Recents titles, and browse as the logged-in user. Also points at the shared SITES.md site-knowledge bases (ClaudeBot/MikeysBot + claude-home). Use when you need to check or interact with claude.ai, or automate any website from this repo.
---

# claude-web — driving claude.ai via browser-harness

How to open Claude web (claude.ai) as the user and verify Antiphon's remote-control
agent sessions, using the `browser-harness` skill (CDP). Learned live 2026-07-23.

## The CDP Edge (canonical setup)

One shared Edge instance with remote debugging serves browser-harness, the relay, and
claude-in-chrome. **Canonical on this machine** (see `C:\src\ClaudeBot\launch-edge-cdp.ps1`
header for the full rationale):

- Port **9222**, profile **`C:\Users\lndco\edge-cdp`** (dedicated automation profile,
  flag method `--remote-debugging-port=9222 --remote-allow-origins=*`).
- Launch/ensure: `powershell -File C:\src\ClaudeBot\launch-edge-cdp.ps1` (`-EnsureOnly` never kills).
- Check it's up: `curl -s http://127.0.0.1:9222/json/version`.
- The machine-wide `browser-sites` skill mentions an older profile
  (`C:\Users\lndco\.claude\edge-cdp-profile`); the **live instance is `edge-cdp`** — always
  check the running process's `--user-data-dir` if in doubt.
- The profile is logged into claude.ai (lndcobra@gmail.com / "MC"), Google, Telegram Web.

Drive it:

```bash
BU_CDP_URL=http://127.0.0.1:9222 browser-harness <<'PY'
new_tab("https://claude.ai/code")
wait_for_load()
print(page_info())
PY
```

## claude.ai specifics

- **Home tab** (`claude.ai/recents`) lists chats. **Code tab** (`claude.ai/code`) lists
  cloud sessions in the main pane; **local/remote-control sessions appear in the left
  sidebar "Recents"** — this is where Antiphon agent sessions (Family, school-revision,
  torquay-leander, …) show up.
- Sidebar rows are **not `<a>` elements** (no hrefs). To read them, find a leaf element by
  known text and walk up to the container:

```bash
BU_CDP_URL=http://127.0.0.1:9222 browser-harness <<'PY'
out = js("""(() => {
  const all = [...document.querySelectorAll('*')];
  const item = all.find(e => e.children.length === 0 && /KnownSessionName/.test(e.textContent));
  if (!item) return 'NOT FOUND';
  let box = item; for (let i=0;i<10 && box; i++){ box = box.parentElement; if (box && /OtherName/.test(box.textContent)) break; }
  return box ? box.textContent.slice(0, 900) : 'NO BOX';
})()""")
print(repr(out))
PY
```

- **Trap:** text queries can match elements in hidden panels (e.g. Home-tab recents while
  on the Code tab). Before `click_at_xy`, sanity-check the rect is inside the visible
  sidebar (x < ~280 at default width).
- The session list is fed over a sync channel, **not a fetchable REST endpoint** — probing
  `/api/organizations/{org}/...sessions` variants returns 404. Scrape the DOM instead.
- Multi-line JS in the heredoc: join with `' || '`, not `'\n'` (nested-quoting breaks).

## Remote-control session titles (the naming rule)

claude.ai's sidebar entry for a remote-controlled CLI session only picks up a title from a
**`/rename` that fires while the bridge is armed**. Titles set before arming (`--name` at
launch, pre-arm `/rename`) never sync — the entry falls back to the first message's text
("New session started. Follow your CLAUDE.md…"). Antiphon therefore sends `/remote-control`
**before** `/rename <agent name>` at agent boot (`AgentSessionService.SendRemoteControlCommandsAsync`).
To fix a live unnamed session by hand, enqueue a rename through the queue:

```bash
curl -s -X POST http://localhost:17202/api/sessions/<sessionId>/messages \
  -H "Content-Type: application/json" -d '{"body":"/rename <Name>","mode":"Now"}'
```

## Site knowledge bases (SITES.md convention)

Non-claude.ai site know-how lives in two existing bases — consult them before automating a
site, and record what you learn per their own rules:

| Base | Repo | Checkout | What's in it |
|---|---|---|---|
| ClaudeBot (a.k.a. **MikeysBot**) | <https://github.com/michal-ciechan/ClaudeBot> (private) | `C:\src\ClaudeBot` | `SITES.md` index + `sites/{host}.md` cheat sheets + `sites/frameworks/` + 20×-use `sites/scripts/` — maintained by its `.claude/skills/browse-sites` skill |
| claude-home (machine-wide) | <https://github.com/michal-ciechan/claude-home> (private) | `C:\Users\lndco\.claude` | `skills/browser-sites/` — persistent-profile logins + `sites/` for web.telegram.org, accounts.google.com, login.tailscale.com |

Rule of thumb from browse-sites: if you had to screenshot-and-squint more than twice to
figure something out, write it down in the matching base (and this file, if it's about
claude.ai itself).
