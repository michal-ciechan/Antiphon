// CARD-0142 Probe 1 — Codex session startup buffer prefix, captured 2026-08-23.
const ESC = '\x1b'
const ST = `${ESC}\\`
const BEL = '\x07'

export const CODEX_STARTUP =
  `${ESC}[1t` +
  `${ESC}[c` +
  `${ESC}[?1004h` +
  `${ESC}[?9001h` +
  `${ESC}]0;C:\\Windows\\system32\\cmd.exe${ST}` +
  `${ESC}[?2004h` +
  `${ESC}[?1004l` +
  `${ESC}[?1004h` +
  `${ESC}]10;?${ST}` +
  `${ESC}]11;?${ST}` +
  `${ESC}]0;Antiphon${BEL}`
