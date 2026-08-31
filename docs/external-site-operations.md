# External-site operations


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

This is the living owner for browser automation, secret relay, and mail access. Read it before touching a real external site.

## Browser automation, credentials, and ClaudeBot

This machine also runs **ClaudeBot**, the user's personal assistant workspace, at
`C:\src\ClaudeBot` (referred to as `claudebot` in places — same directory, case-insensitive on
Windows). It is a separate Claude Code project from Antiphon, but its infrastructure is shared and
worth knowing about whenever an Antiphon task needs to touch a real external site (creating an
OAuth app, filling a form, checking email) rather than just Antiphon's own UI:

- **Browser automation — `browser-harness`** (`C:\src\browser-harness`, invoked as the `browser-harness`
  CLI via Bash, heredoc scripts). Attaches to a dedicated CDP-enabled Edge profile
  (`C:\Users\lndco\edge-cdp`, port 9222, launched via `C:\src\claudebot\launch-edge-cdp.ps1`) shared
  by every tool that automates this browser — never open a second, uncoordinated CDP browser.
  **Prefer real trusted interactions over synthetic ones**: many sites' client-side validation
  only reacts correctly to genuine `click_at_xy` mouse events and real keystrokes (`type_text`), not
  synthetic `element.click()` or a framework's `setValue()` — a form can visually show the right
  content while its underlying validation state stays stuck, with no error text explaining why (a
  button showing `aria-disabled="true"`/`pointer-events:none` rather than the real `disabled`
  attribute is a common tell). Cookie-consent overlays (OneTrust and similar) commonly reappear on
  every fresh page load and block dialog queries silently — clear them before interacting with any
  dialog. Read `C:\src\browser-harness\SKILL.md` for the full API and gotchas before improvising.
- **Per-site notes — `C:\src\claudebot\sites\<host>.md`**: durable, learned-the-hard-way notes on
  specific sites' automation quirks (selectors, required viewport sizes, click sequences that
  actually register). Check for an existing file before automating a site for the first time, and
  write one after — this is exactly the kind of hard-won knowledge that's expensive to rediscover.
- **Bitwarden vault + password relay**: the `bitwarden` skill (`C:\Users\lndco\.claude\skills\bitwarden\SKILL.md`)
  unlocks the vault via the relay's phone-tap flow (master password never enters chat) and reads/writes
  vault items via the `bw` CLI. **`C:\src\claudebot\scripts\bw-fill.ps1`** is the safe way to get a
  secret from the vault into a browser field — it never prints the value. Never run `Get-Member`,
  `Format-List`, or any bare-echo of a variable that might hold a secret — the default formatter
  prints the value, not just its type.
- **Email — the `mail` skill** (`C:\src\claudebot\scripts\mail`, a .NET CLI, `dotnet run --project scripts/mail -- sync --no-rules`
  for a side-effect-free pull) reads the user's Outlook/Hotmail inbox via Microsoft Graph. Credentials
  are already authorized and live in the Bitwarden item `Microsoft OAuth - Outlook IMAP/SMTP + app
  (lndcobra@hotmail.com)` (see `C:\src\claudebot\scripts\ms-oauth\README.md`) — if a fresh machine's
  local token cache (`email/state.json`) is missing, restore it from that vault item rather than
  attempting a new OAuth device-code flow (this project has no such flow implemented).
<!-- CARD-0254 preserved source ends -->
