# CARD-1006 blocking startup fixtures

## REAL

Source task `1e39a459`, source SHA `8d3148dcf6d0040ef1a69b0e2fceb38ce1a77b18`:
[immutable rendered JSON](../../../../../docs/investigations/2026-10-03-card-1006-linux-grok-frames.json)
and [capture investigation](../../../../../docs/investigations/2026-10-03-card-1006-linux-grok-signin-trust-capture.md).
Screens are copied decoded byte-for-byte, preserving every LF, empty row and
trailing space, with ASCII-escaped Unicode in JSON. Canvas 120x30; maximum
rendered widths 67/85/85. Their decoded UTF-8 SHA-256 values are independently
pinned in test source. Session identifiers are not-retained (null); image tag
and RepoDigest were unavailable (null). Linux/Debian 12; literal reported
backend `InboxConhost`, not a claim of Windows ConPTY. CLI version output
`1.0.41 (4220f3b224a6)`; runner build
`4358939ecd85d6e7ff0941f970879499cb930e3d`.

Captured 2026-10-03T12:47:16.476Z–12:47:51.818Z. C-1 started 12:47:18.661Z:
Connecting at 2336 ms, approval at 2640 ms, 62 observations; individual kill
returned Exited, exit code 0; 15195 ms including kill. C-2 started
12:47:34.256Z: approval at 2511 ms, 62 observations; individual kill returned
Exited, exit code 0; 15505 ms including kill. Both loops stopped polling by
15000 ms. C-2 missed the transient Connecting frame. Neither sampled trust,
updater nor a Ready dashboard.

Each CLI used `grok --no-alt-screen --session-id <unretained-generated-id>`,
120x30, TranscriptEnabled=false, no rules/resume/prompt, no waiting server
adapter. Runner and CLI used `/usr/bin/env -i` and explicit nonsecret PATH,
fresh HOME/GROK_HOME/XDG config/cache/TMPDIR, TERM=xterm-256color,
BROWSER=/bin/false, ANTIPHON_PTY_AUDIT=0. No input calls or bytes, login,
auth-file read, browser approval, trust answer or model turn. Installed updater
setting was unchanged. Startup outbound 443 connections were observed;
network necessity/purpose was not established.

Original isolation was a task-owned local runner inside an existing container,
not an exact-image throwaway container or tmpfs-qualified capture. Ordinary
raw ANSI logs still existed despite audit-off, were never read/copied, and the
owned scratch root was deleted. Successful and aborted roots were absent;
final check found zero owned processes; no home archive. Historical metadata
is intentionally not replaced by the later
[A-2 isolated capture and D-2.6 re-run receipt](../../../../../docs/investigations/2026-10-03-card-1006-a2-capture-receipt.md).

The approval code is replaced by exactly `<CODE-9> `, including its trailing
space: category device-code, zero-based row 15, column 56, length 9. No original
code is retained. Approval text at rows 13/17/25 is unchanged. The classifier
uses those phrases, never the code cells. Connecting is real-rendered;
approval frames are real-rendered-redacted. All synthetic inputs have a
separate machine label.

`realUpdate` is `not-observed` with empty captureIds. Real trust and updater
screens remain unqualified. Synthetic trust tests preserve existing predicate
and waiter behavior; they do not establish real Linux UI keys.

## SYNTHETIC

Every P-01..P-19 definition in synthetic-blocking-frames.json is labelled
`synthetic-derived`, with probeId, source, literal transformation, reason and
independently pinned decoded hash. No fictional session, host, capture time or
CLI version is assigned. B is a 30-empty-row canvas; L is the
[CARD-1004 1.0.41 rendered dashboard](../card1004/linux-startup-frames.json)
([provenance](../card1004/provenance.md)). Windows W is unchanged CARD-0778 idle
afterChunk 40. Patch clones its source and changes only listed rows; Join
inserts LF between rows with no extra terminator. No padding/trimming of real
rows occurs. U/I/D/H are the frozen full-width borders, interior and hint.

P-01 trust question/choices derive from the older trust test and CARD-1004
trust overlay, not an observed Linux trust screen. P-02 approval/menu is a
synthetic menu. P-03/P-04 put trust/approval over a working dashboard; P-05
puts approval over the intact Linux composer. P-06/P-07 are synthetic updater
menu/dialog shapes. P-08 is a choice-like composer interior; P-09/P-10 are
full-width bare boxes at rows 9/11 without hint; P-11/P-12 are lone markers.
P-13 has typed text; P-14/P-15 combine markers. P-16 removes the hint; P-17
adds Working at row 22; P-18 removes the bottom; P-19 removes the final row.
Literal recipes and hashes are stored beside every screen and pinned in C#.

V-7 constructs 38 further synthetic controls in memory: remove opposite-modal
text while retaining the frozen layout/marker, then apply approval A at 3/4/5
or trust T at 5/7/8, column 2. Each has its P ID, variant, source and explicit
row map in test source. Exactly 19 base + 19 A + 19 T cases. Only V-9 mixes
modal anchors deliberately (approval 3/4/5 and trust 7/8/9). V-5 relocates
measured approval phrases and synthetic trust onto otherwise-ready W/L, then
checks ten synthetic geometry boundaries. V-3 includes two partial-trust
negative witnesses. These retain their synthetic status.

Nine-space and nine-X substitutions are synthetic classification controls,
not additional real captures or a historical sanitizer privacy test. V-6's
current/raw-history pairs are synthetic compositions. V-8/V-9/V-10 use
scripted snapshots and a controlled clock, without real input or providers.
They prove state decisions and write lists, not production queue delivery.
V-12 replays all 114 unchanged Windows classifications; CP-5 separately owns
native modern ConPTY fake-provider qualification.
