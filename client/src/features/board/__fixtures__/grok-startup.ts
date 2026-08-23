// CARD-0142 Probe 1 — Grok session startup buffer prefix, captured 2026-08-23.
const ESC = '\x1b'
const BEL = '\x07'

export const GROK_STARTUP =
  `${ESC}[1t` +
  `${ESC}[c` +
  `${ESC}[?1004h` +
  `${ESC}[?9001h` +
  `${ESC}]0;grok${BEL}` +
  `${ESC}[?1003;1006h` +
  `${ESC}[?1004h` +
  `${ESC}[?2004h` +
  `${ESC}[?25l` +
  `${ESC}]12;rgb:c8/c8/c8${BEL}`
