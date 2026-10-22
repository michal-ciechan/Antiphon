# Task 4e50d241: partial Codex boot input classified as a wedge

Both Windows Codex launches rendered only a prefix of their brief pointer, never produced a UserPrompt, and were killed by boot-wedge recovery. This is a newly evidenced **partial-consumption variant** of the Codex delivery family, distinct from the fatal account-bootstrap timeout and the historical fully rendered, frozen composer. The underlying input slowdown remains unresolved; the queue's consumption-evidence gap is demonstrated and filed as **CARD-1521**.

Debug task `0f98ac20`, 2026-10-22. Subject `4e50d241-4e46-4229-b24d-61f0ac8a735c`, Merge/Shared, requested `gpt-6-astra`, parent `84f61b86-39be-4636-8edf-537143fae95b`. No product changes, builds, test suites, launches, restarts, or conflict resolution were performed. CARD-1506 recovery remains with `d3bad549`.

The task row selects **runner `desktop`**, on the **Windows desktop host** (`local` in host budgets, `desktop` in host stats), not server2. Both sessions used `PtyHost`; runner capabilities report ModernConPty, no fallback. Both buffers identify Codex **0.160.0**, the owner's Windows worktree, and `GPT-6-Astra xhigh`. The diagnostic bundle reports server SHA `0714a9a89e4bff65aae7e0b4b94098694aeb931c`, runner SHA `81cfb267a0af5c0ddb65e8b238af355067bee9cc`, runner process start `2026-10-19T15:56:41.3474292Z`. Investigation checkout base: `ffa98a8f76588c12df6b0437d24b541762b95f09`.

| Attempt | Session | Recorded session start / end, UTC | Retained composer |
|---|---|---|---|
| First | `b56ac6cd-b325-48ef-9003-4b5fff51b181` | 21:31:30.626395 / 21:32:37.917407 | First **237/676** characters; ends `characters — too long to type into a termin` |
| Relaunch | `b25c1e9e-e63a-4ced-a790-df821e030618` | 21:32:38.027009 / 21:34:31.194865 | First **193/676** characters; ends `YOUR BRIEF IS NOT IN THIS MESSAGE. It is 2,318` |

These are database session times, not measured native process-start timestamps. The warning `boot-wedge relaunch 1/1` restamped dispatchedAt at 21:32:38.027009; failure at 21:34:30.690585 was **112.664 seconds after that reset**, but **180.064 seconds after the original dispatch**. The quoted 1m52 excludes the first launch.

Each authoritative `GET /api/sessions/{id}/transcript?since=0` returned zero entries and lastSequence 0. Each diagnostic session record has one **Canceled**, Delegation-origin queue row, deliveryAttempts 1, bodyLength 676, identical SHA-256 prefix `9dfa6fc0379a16aa`. Runner slots show both `Exited / KilledByRequest`, PIDs 238408 and 413948. Runner logs at 21:32:41.053Z and 21:34:33.895Z say input was delivered but neither child produced an identifiable Codex rollout. At 21:33:55.649Z the second session had zero cwd-matched candidates and zero refused candidates: this is not evidence of an existing rollout rejected by the prompt matcher.

The retained buffers start at terminal initialization, grow in tiny character increments, and end during an unfinished synchronized frame. Both show the ordinary composer, `permissions: YOLO mode`, and an unopened `1 warning · f2 to view` footer. Neither shows a fatal account/read error, update/trust/approval dialog, collapsed-paste placeholder, or a submitted turn. The warning's content is unknown; no keys were sent to inspect it. The pointer's file path and final marker never appeared. Thus “the brief rendered, then the TUI stopped reading” is stronger than the evidence: neither complete pointer rendered, and the captures do not prove permanent loss of input responsiveness.

The delivery path explains the classification. At the live server SHA, `DelegationReportFormatter.BuildBriefPointer` flattens the Codex pointer to one line. Reconstructing it from the formatter, task and spill path yields **676 characters**, SHA-256 `9dfa6fc0379a16aa233a0a8ad0b81bd164055eed40c8551a62d3eadb91bf3b20`, matching both stored length/hash-prefix receipts. `PtyInputEncoding.WrapIfMultiline` consequently leaves this body unwrapped; the LF/bracketed-paste rule applies to multiline bodies. The 2,318-character full brief was spilled, not typed.

`SessionMessageQueueService.WaitForComposerEvidenceAsync` accepts `ComposerDeliveryEvidence.IsVisible`, which matches any ten-character window from the head or tail. It then settles output for at most three seconds, writes a separate CR, and awaits submit/transcript evidence. The `NoSubmitOutput` cold-delegation path cancels the queue row, kills the session, relaunches once, then fails at the limit. That branch establishes a failed submission verdict, not complete body consumption or a proven TUI-internal deadlock. Exact Enter timestamps/counts were not retained in the retrieved incident logs; they are not asserted here.

Two offline replays used the existing `/app/Antiphon.Agents.Pty.dll`, informational SHA `81cfb267a0af5c0ddb65e8b238af355067bee9cc`, without compilation. Its `TerminalScreen` reproduces both partial screens. Its consumption predicates are unchanged at the live server SHA. **IsVisible=true and BodyConsumed=true on both partial captures**, although the complete suffix is absent. For the reconstructed pointer, IsVisible first passes after ten initial characters; BodyConsumed passes after fourteen because the closing task marker repeats at the head. Simply substituting BodyConsumed is therefore insufficient. These are diagnostic replays, not passing product-test claims.

Incident-window host samples supply relevant context: 37 desktop CPU samples from **21:31:31.1545634Z–21:34:31.0379499Z** range **99.8069–100%**, mean **99.9768%**, on eight cores. Memory used was 29.15–30.05 GB of 34.30 GB. Saturation supports slow consumption as a hypothesis; these samples do not identify the responsible process or establish the Codex/ConPTY mechanism. No timeout or readiness setting was changed.

The project-scoped Failed-task census was captured at **2026-10-22T21:55:32.8661103Z**, covering the preceding 24 hours by completedAt. `GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&status=Failed` returned 466 rows overall, 13 recent failures, **three recent Codex failures**:

| Class | Tasks | Process attempts evidenced | Subject |
|---|---:|---:|---|
| Fatal account/workspace-routing bootstrap timeout | 1 | 1 | `0751ffb8`, desktop; exact fatal error re-read from its retained buffer |
| Partial pointer / no submission, this variant | 1 | 2 | `4e50d241`, desktop |
| Entity-save dispatch failure before any session | 1 | 0 | `5fdc4967`, server2 selection |
| CARD-0103/0121 collapsed-paste/no-composer-evidence pattern | 0 | 0 | None in this snapshot |
| Historical fully rendered Codex freeze; update/trust/approval blockade | 0 | 0 | None positively identified in this snapshot |

These are current Failed task rows, not a launch-rate denominator or a historical event census after retries/status changes. The bootstrap error remains `account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603)` and is already covered by CARD-1508. CARD-0103 withheld Enter without composer evidence; CARD-0121 concerns stacking collapsed pastes. CARD-0133/0299 documented the whole pointer rendered in a closed frame before silence. This incident has a different observable boundary, although the deeper TUI cause may overlap.

Created **CARD-1521**, Backlog, `e23bd604-3202-412c-a4ee-256008d9682f`, Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`: *Codex boot-wedge recovery mistakes a partial pointer for consumed input*. It asks for occurrence-safe consumption evidence, truthful failure classification, regression captures, and bounded cold-launch write/consume/Enter measurement. Complete card.ps1 searches and full candidate reads are retained; no duplicate was found among CARD-0103/0121/0133/0299/0574/0500/0142/1151/1303/1508. No separate card duplicates the known bootstrap diagnostics defect.

Evidence root: **`/state/session-runner/investigations/2026-10-22-0f98ac20`**, outside the worktree. It contains both raw buffer JSONs and normalized transcripts; `first-bundle`/`second-bundle` diagnostic archives with queue metadata, runner logs and version/capabilities; exact pointer reconstruction; TerminalScreen/predicate replay scripts and results; incident CPU/memory samples; `census.json`; and card search/create/update receipts. Bundles were obtained through the documented read-only diagnostic collection POST. Direct desktop port 17204 timed out; the retired agent's history/incidents routes returned 404. The bundle server-log tail starts after the incident, and native per-session pty-host/input audit files were not accessible from this mirror. Those limits leave precise input timing and the upstream slowdown unresolved, without preventing the classification above.

--- next stage ---
next: none
handoff: Investigation complete. Commission CARD-1521 through Plan/TestDesign/Code; preserve these partial captures and measure the Windows input path. Keep CARD-1508 separate and CARD-1506 recovery with d3bad549.
artifact: /work/worktrees/task-0f98ac20/docs/investigations/2026-10-22-task-4e50d241-boot-wedge-debug.md
