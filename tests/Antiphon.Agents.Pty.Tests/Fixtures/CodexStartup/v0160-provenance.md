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

## Authorized second-prompt measurement

One additional capture at 2026-10-19T15:13:39.210Z used the same binary, argv, uid,
home-in-place and geometry. Scratch cwd `/tmp/c907-ddd05d7e-HGTnYt` was retained.
Renderer source: `2a41cd91773f0c5f90c70bc01497e2600ab5d084`.
Evidence: `/work/worktrees/task-ddd05d7e/.antiphon/card-0907-queue/`.
No credential file or native rollout was read, copied or removed.

The first body was the S0 pong prompt, at 2006.318ms/output offset 3787, followed
by separate CR at 2106.613ms/4076. On observed Working, the second body was
`Reply with the single word ping. Do not call tools. Do not read or write files.`
at 2216.964ms/5094, then separate CR at 2317.572ms/5544. Both body writes used
bracketed paste. The queue-draft grid was observed before that second Enter.

The bottom row really contains `tab to queue message`. After Enter the pending
banner is `• Messages to be submitted after next tool call (press esc to interrupt and send immediately)`.
Both frozen frames also contain Working. The existing classifier already recognizes
the queue token; the independent test explicitly transplants it into the captured
idle frame's bottom model footer, with the existing working/modal arms absent.
This derived frame is not claimed as a native queue-only screen. The pending banner
is replay coverage, not a newly implemented pending-state detector.

Two transient samples at offsets 9312/9377 classified Ready while the pending banner
remained, before Working repainted. This is a measured classification limitation;
no settled readiness or native delivery claim follows from these samples.
Both `pong` and `ping` appeared on screen. No native prompt/turn receipt was read.

347 samples, 38 distinct screens; full original 16541-byte stream SHA-256
`d020c1d3d45e5071fed13f775465fdf544e3b62bd6f629be9a233fac137de8c3`.
The consumed prefix is [0,6696), from blank TerminalScreen(120,30), fresh UTF-8
decoder; queue-chunks lists original nonempty reads. No payload redactions.

| Payload | End offset | SHA-256 |
|---|---:|---|
| v0160-queue-draft.txt | 5544 | 903eb9e496ac34d0c3685f5075fe4335780e0c69d8ed9ca90f3e2fbcf7d941a1 |
| v0160-queue-pending.txt | 6696 | f36d6fed0f52d38e1dc89ef570d34354613cbe583dfcfb2c156baba140fd14ec |
| v0160-queue-prefix.ansi | 6696 | f5eeb50685f3d33cb565b44ae54008b512c2ec94d95b96ed4140b7bb5bfe93da |
| v0160-queue-chunks.txt | 6696 | 4b3f9afec868b7b42cc151b6c73364948ad732201f76d81ae9e1e3cad9bb0286 |

The owner stopped only its PTY child after the ping/Ready screen, using two Ctrl-C
writes. The screen then reported conversation interrupted; screen completion is
not upgraded to a task_complete receipt. Child exited 0; PID 15643 subsequently
returned ESRCH. Child and renderer were awaited. Total 8092ms, cleanup 979ms,
within original deadlines. No repeat capture or modal probe ran.
