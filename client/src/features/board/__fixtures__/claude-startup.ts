// CARD-0142 Probe 1 — Claude Code session startup buffer prefix, captured 2026-08-23.
// Carries two DA1 queries: OpenConsole.exe at offset 4, then Claude's own after ESC[>0q.
const ESC = '\x1b'
const ST = `${ESC}\\`

export const CLAUDE_STARTUP =
  `${ESC}[1t` +
  `${ESC}[c` +
  `${ESC}[?1004h` +
  `${ESC}[?9001h` +
  `${ESC}]0;claude${ST}` +
  `${ESC}7` +
  `${ESC}[r` +
  `${ESC}8` +
  `${ESC}[?25h` +
  `${ESC}[?25l` +
  `${ESC}[?2004h` +
  `${ESC}[?1004h` +
  `${ESC}[?2031h` +
  `${ESC}[>0q` +
  `${ESC}[c`
