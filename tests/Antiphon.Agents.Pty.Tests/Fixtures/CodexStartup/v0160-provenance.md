# Codex 0.160.0 captured compatibility inputs

These are consumed test fixtures, not a second capture or a native delivery receipt.
The immutable S0 account is
`docs/superpowers/plans/2026-10-19-card-0907-s0-capture-binding.md` at
`b5e08211edff926f8e5e18cc2a6d644611f9f766`.
Original evidence: `/work/worktrees/task-e840b26d/.antiphon/card-0907-capture/`.

Captured 2026-10-19T14:58:49.559Z as uid 1654, Linux util-linux script PTY,
TERM=xterm-256color, 120 columns by 30 rows. Codex 0.160.0 executable SHA-256:
`12eb3e81114588aca3b7998f4f19e8997b056aca08e57a7ca7c8a3ec8c652aad`.
The S0 record supplies exact argv, original prompt and cleanup. No auth or rollout
files were read for this curation. No redaction, trimming or ANSI stripping occurred.

Initial state is a blank production TerminalScreen(120,30) and fresh UTF-8 decoder.
`v0160-turn-prefix.ansi` is exactly raw bytes [0,9621), all of which tests consume;
it excludes the unused exit tail. `v0160-chunks.txt` lists the 175 original nonempty
read lengths through that offset, one decimal length per LF line. Periodic samples
with no new bytes are excluded. Replays truncate at each exclusive endpoint.
The three `.txt` grids are the independently frozen S0 renders, retaining all 30
rows and trailing blanks. Metadata lives only here, outside parser payloads.
The tests pin byte digests directly; `.gitattributes` preserves them across hosts.

| Payload | End offset | SHA-256 |
|---|---:|---|
| v0160-idle.txt | 3945 | 78e3caf57049ecc5c093b772c07a179a6088322c3d68726ca47fa66445db987e |
| v0160-running.txt | 6740 | 34fa58f2e96838318e67628b01e2df02b987a1de2eb303df2d8826bcbb535dd0 |
| v0160-done.txt | 9621 | 78c8411e4f1ece0085969d5205b44184ffe865733938cd1946f6de7e4a712db8 |
| v0160-turn-prefix.ansi | 9621 | 362830f2f97af513e58161a8336e3ef1043c21d678a37025e4d40251084faaea |
| v0160-chunks.txt | 9621 | cba78b081a7f8457b34b573ea9f658691c6f56f5d243ec00c0f67cced20d6d0a |

The active row is `◦ Working (1s • esc to interrupt)`. Idle and done are not Working.
Thinking, a distinct queue footer, native receipt/usage, and current modal scenes
were not captured in S0. Historical version labels do not bind those missing scenes.
Whole-prefix, original-read and single-byte replays compare entire grids, then the
unchanged working predicate. Deliberate corruption/guard removal belongs to Mutation.
