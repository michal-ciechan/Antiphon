# CARD-0907: S0 capture contradicts the proposed working-parser repair

Date: 2026-10-19. Original Code task / landing owner:
`e840b26d-1add-46e4-8174-7f24e458d551`.
Branch: `feat/card-task-e840b26d`.
Worktree: `/work/worktrees/task-e840b26d`.

**The authorized PTY capture succeeded.** The observed 0.160.0 running line is
already recognized by the existing parser. No independent queue footer appeared.
The proposed S1 assertion-red is therefore not reproducible on this capture,
and the S2 queue witness cannot be bound from it. No parser fix was invented.

This is new evidence at the plan's mandatory binding gate, not completed Code
verification or a full CARD-0907 acceptance claim. The original plan says to stop
the dependent slice if S0 contradicts its premise and return the queue portion
for investigation when no distinct footer is painted. Its references remain:

- Plan `docs/superpowers/plans/2026-10-19-card-0907-codex-0160-parser-plan.md`
  at `08bdb7087f3903fcc7f2980ff520a3a4997c250a`.
- TestDesign `docs/superpowers/plans/2026-10-19-card-0907-codex-0160-test-design.md`
  at `fa25211a188944e5acd1d41628ab1806eb686fda`.

## Authorized capture and ownership

The caller's resume input `ab3a472c-9e93-4e81-8fc3-658274080e9b` superseded the
host-side docker-exec and scratch-home instructions: run directly in this
container as uid 1654, use the real `/state/codex` in place, copy no credentials,
and retain history. `id -u` returned 1654 and CODEX_HOME was `/state/codex`.
No credential contents or metadata were read, copied, printed or linked.
No service-home file was deleted. No second model turn was submitted.

Capture launch UTC: `2026-10-19T14:58:49.559Z`.
Version: `codex-cli 0.160.0`.
Resolved launcher:
`/opt/codex/0.160.0/package/vendor/x86_64-unknown-linux-musl/bin/codex`.
Launcher SHA-256:
`12eb3e81114588aca3b7998f4f19e8997b056aca08e57a7ca7c8a3ec8c652aad`.
This fingerprints the launched executable; it does not establish the fingerprint
of any shared provider daemon behind it or equivalence to the prior investigation.
Backend: Linux PTY through util-linux `script`, `TERM=xterm-256color`, 120x30.
Scratch cwd: `/tmp/c907-e840b26d-9tcg88`; it was retained.

Arguments:
`--no-alt-screen --dangerously-bypass-approvals-and-sandbox -m gpt-6.1-sol -c model_reasoning_effort=low -c disable_paste_burst=true -c check_for_update_on_startup=false`.

The production TerminalScreen(120,30), CodexStartupScreen and CodexReadyTracker
were built from clean source `a36bf77c10723f2a84c0bc4d589e04b469f5a660`.
The renderer continuously consumed UTF-8 PTY bytes and sampled every 100 ms.
Positive readiness settled at 2616.205 ms. One bracketed-paste body was written
at 2617.011 ms, after output byte 3945; one separate CR at 2718.403 ms, after
byte 4154. The complete 79-character body was:

> Reply with the single word pong. Do not call tools. Do not read or write files.

The screen displayed `pong`. That is a screen observation, **not** a native
UserPrompt/task_complete receipt. No rollout was read or copied in this resumed
capture, consistent with the caller's screen-only curation direction. Native
identity, usage and transcript authority remain unverified.

After inspecting the completed reply, the owner sent SIGINT only to the verified
capture child's PID. It exited 130; its process was subsequently confirmed absent.
The harness awaited both the PTY child and renderer. Total observation was
51.304 seconds; final cleanup took 9.811 ms. Neither 120-second observation
deadline fired. The harness's automatic done check had expected a literal Ready
word; the actual ready composer has no such word, so the owner stopped the
already-completed probe instead. This harness condition was not a production
parser failure. No deadline was widened.

## Observed frame and binding

The first working frame ended at byte 5133 at 2781.228 ms. The last working
observation was at 5053.309 ms. There were 694 snapshots and 34 distinct rendered
frames; 116 snapshots classified Working using unchanged source. All captured
rows, including transient frames, were inspected for queue/tab wording: none.

Representative running frame at 3789.888 ms, raw prefix `[0,6740)`:

```text

  >_ OpenAI Codex (v0.160.0)
     /tmp/c907-e840b26d-9tcg88
  permissions: YOLO mode

  Back for another round?

  To get started, describe a task or try one of these commands:

  /init - create an AGENTS.md file with instructions for Codex
  /status - show current session configuration
  /permissions - choose what Codex is allowed to do
  /model - choose what model and reasoning effort to use
  /review - review any changes and find issues


› Reply with the single word pong. Do not call tools. Do not read or write files.


◦ Working (1s • esc to interrupt)


› Ask Codex to do anything

  GPT-6.1-Sol low · /tmp/c907-e840b26d-9tcg88 · ⠧                                              ⚠ 1 warning · f2 to view
```

The frozen full frame retains all 30 rows, including trailing blank rows. The
excerpt above is presentation only; fixture-candidate hashes below bind the
unmodified UTF-8/LF payloads. The two active footer forms differ only in the
braille spinner suffix; neither contains a queue instruction. The completed
frame contains `• pong`, `Worked for 2s • 14:58`, an idle composer and the model
footer. The earlier investigation's claim that this CLI never paints the legacy
Working shape is not supported by this new observation. No cause for that
discrepancy was established.

| Binding | Actual result |
|---|---|
| B-1 working | Observed `•`/`◦ Working (0s/1s/2s • esc to interrupt)` on one row, already accepted by the existing matcher. Thinking was not observed. |
| B-2 queue | Unbound: no distinct queue/footer token in this one turn; an independent queue-only witness cannot be derived from the observed payload. This does not prove that no Codex mode ever paints one. |
| B-3 native receipt/rollout | Not examined: native session/turn/item IDs and all four usage values remain unknown. Screen reply does not discharge this binding. |
| B-4 modals | Not captured: real home retained; no signed-out scratch home, version.json manipulation or extra modal probe was performed. |
| B-5 test/mutation sites | Unbound: no new parser branch or test method added. The planned S1 baseline would be green, not the required assertion-red. |

## Replay evidence and artifact custody

Ignored evidence root:
`/work/worktrees/task-e840b26d/.antiphon/card-0907-capture/`.
It contains the capture/renderer scripts, launch/input/cleanup receipts,
`frames.jsonl`, `turn.raw`, `summary.json`, `replay.json`, and the rendered
`idle.txt`, `running.txt`, `done.txt` fixture candidates. These are retained.
No unconsumed capture payload was represented as a finished test fixture.

| Rendered candidate | Exclusive raw prefix end | SHA-256 of exact UTF-8 payload |
|---|---:|---|
| idle.txt | 3945 | `78e3caf57049ecc5c093b772c07a179a6088322c3d68726ca47fa66445db987e` |
| running.txt | 6740 | `34fa58f2e96838318e67628b01e2df02b987a1de2eb303df2d8826bcbb535dd0` |
| done.txt | 9621 | `78c8411e4f1ece0085969d5205b44184ffe865733938cd1946f6de7e4a712db8` |

Full original 9967-byte stream SHA-256:
`5ea42ca4d5e351e13b4ca2de6cc5b13cb10e27bc5f84eada3c396d61717bc223`.
No redactions, ANSI stripping or position changes were used. Initial renderer
state was a blank 120x30 terminal. Replaying prefix `[0,6740)` in one chunk,
117 recorded nonempty chunks, and 6740 single-byte chunks reproduced the entire
frozen running frame exactly in all three cases; each classified Working.
This is S0 curation evidence, not three TUnit results or a CP-1 certificate.

One explicitly planned S0 renderer bootstrap build ran:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c907-capture-renderer-bootstrap -- dotnet build .antiphon/card-0907-capture/renderer/CaptureRenderer.csproj --property:OutputPath=bin-c907-capture/ --nologo`.
It succeeded: 0 errors, 1 existing GrokStartupReadiness.cs CS8620 warning;
MSBuild elapsed 32.81 seconds, slot held 34 seconds, `slot=granted waited=0s`.
No suite was used to obtain the renderer. The three offline replays used that
same built renderer without another build. No checkpoint bootstrap was needed.

## Ordinary verification and acceptance remain incomplete

CP-1 through CP-9: **not run**; TUnit results: **0**. No CHECKPOINT line, TRX
or checkpoint source-clean certificate exists. V-1 has the manual replay
observation above, but its required committed fixtures and two checkpoint tests
are not complete. V-2 and V-5 have limited unchanged-classifier observations,
not their prescribed test matrices. V-3, V-4, V-6, V-7, V-8, V-9 and R-1, R-2,
R-3, R-4, R-5 remain unexecuted. The Q-7 seam was not assessed; it is not claimed
impossible. Every PC-1 through PC-73 and every case/variant remains pending
SourceLanding Mutation; no deliberate mutant ran.

Keep native prompt/reply/usage, independent queue recognition, modal scenes,
queue/caller receipt and persistence recovery, quota refusal, compaction/continue,
post-login, MCP, unseen spellings/geometries, Windows qualification and the
post-deployment canary incomplete. There is no release/deployment claim.
Restart: **none**.

The next plan must reconcile the measured legacy working shape, the absent queue
witness and the caller's screen-only/in-place capture constraint with the old
S0 and red-first requirements. It should select compatibility coverage from the
captured frames or commission a specifically authorized missing measurement;
do not spend a second turn or weaken the readiness contract by default.
