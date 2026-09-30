# CARD-0418 round 36: running converter death and remaining ordinary matrix

This continues [round 35](2026-09-29-card-0418-round-35-evidence.md) from
`6e78ff27f677dcca02a37cd34f32d5184d98a5f1`. The inherited CP-8
`PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` failures remain the
base verdict. All PC-1–PC-30 remain pending for method-scoped SourceLanding
Mutation; no ordinary or nightly run discharges a PC.

## V-15 running C-3

The new C-3 row held a staged FakeGrok process inside the frozen conversion
request, killed the separate dispatcher probe while it was still running, then
released it. A fresh `AgentSessionRuntime.SyncTranscriptAsync` read the runner's
native transcript and settled the linked task. The probe captured the launch
spec, but the test typed its own short prompt instead of delivering the queued
dispatcher brief. On Linux it used FakeGrok's LF-as-Enter mode and replaced the
shared test-output pty-host apphost with a shell launcher. The session row was
still `Starting` at the cut, and the rebuilt server had no message queue, so a
post-restart queue send was not checked. These receipts are Linux-only; the
Windows branch never ran. Round 38 addresses those limits.

## Checkpoint receipts

The first CP-6 attempts were diagnostic while establishing V-15 C-3; none is
claimed as the final closed-list run. CP-6-a had one C# compile error (CS0136,
fixed in 93ce4597). CP-6-b built but a copied Markdown-escaped pipe selected
zero tests; subsequent filters use literal pipes. CP-6-c, -d and -e each ran
30 tests, 29 passed, one failed in the new `conversion-running` row: first
missing Linux pty-host apphost, then host launch timeout, then no native tool
gate. DIAG-V15-a/b/c each ran the six C-3 parameter rows, five passed and the
running row failed; these unlisted method-scoped runs investigate the native
terminal delivery failure. In DIAG-V15-c the live fake CLI reached Ready and
echoed the prompt, but its native transcript had no submitted UserPrompt.
Receipts: `.antiphon/checkpoints/r36-cp6-*`,
`.antiphon/checkpoints/r36-diag-v15-*`, and
`.antiphon/checkpoints/r36/DIAG-V15-c-20260930-012438-43f8`.
DIAG-V15-d ran six/five pass/one fail: the terminal was demonstrably raw
(`-icanon -echo`) but no native UserPrompt appeared. DIAG-V15-e/f were
compile-only failures in the newly added V-17 test namespace, corrected before
execution. DIAG-V15-g ran six/five pass/one fail after a Linux line-feed Enter;
the native gate still did not open. DIAG-V15-h caught one read-only environment
assignment at build time; the test now copies the launch environment. These
extra diagnostics each had a stated reason: isolate the actual native process
delivery gap before claiming C-3. Their receipts are under
`.antiphon/checkpoints/r36/DIAG-V15-{d,e,f,g,h}-*`.
DIAG-V15-i built and ran only the named running converter row (one/zero pass).
The fake CLI's byte-shape receipt was `bytes=179 cr=0 lf=1 paste=False`:
the body and Enter were consumed in one burst, before the stdin reader had
finished its startup. This explains why no native UserPrompt or tool gate
appeared. The fixture now waits for consumption of the body burst before
sending a separate Enter. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-i-20260930-015325-4413`.
DIAG-V15-j then timed out waiting for a body-only burst: this Linux console
read released the body only after a line terminator. FakeGrok now has an
opt-in Linux LF-as-Enter mode for this native fixture. DIAG-V15-k reached the
actual tool gate, survived dispatcher death, released the worker and proceeded
through reconciliation; its only reported failure was a pty-host directory
deletion race in `finally`, which masked any preceding verdict. The fixture
now explicitly stops its owned runner and retries teardown. Receipts:
`.antiphon/checkpoints/r36/DIAG-V15-{j,k}-*`.
DIAG-V15-l passed the native gate and teardown but timed out waiting for task
settlement. The captured dispatcher spec is a pre-launch-queue spec: production
`AgentSessionService` appends Grok's `--session-id` before calling the runner.
The direct fixture skipped that transform, so FakeGrok wrote to a different
conversation directory than the deterministic transcript tailer watched. It
now applies `AgentSessionService.BuildSessionIdentityArgs` with the captured
session id before direct launch. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-l-20260930-020653-1caf`.
DIAG-V15-m confirmed the fake CLI and runner sidecar now name the same
`updates.jsonl` and the converter wrote its output, but the linked task
remained Dispatched until the catch-up watchdog expired. The next diagnostic
compares native and persisted event kinds and marker presence to locate the
remaining settlement gap. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-m-20260930-021542-8d21`.
DIAG-V15-n narrowed the remaining gap: native and database transcripts both
contain `UserPrompt,AssistantText,TurnEnd`, the prompt has the task marker, and
the assistant text has the `done` report token. The task still remained
`Dispatched`; this is a settlement path issue, not a transcript gap. A scoped
warning logger now records the task reply service's exception class for the
next diagnostic. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-n-20260930-022214-5e73`.
DIAG-V15-o confirmed the same native/stored three-event turn and both markers,
with no task-reply warning exception. This points to a turn-selection guard
returning without settlement. The next diagnostic records the selector kind
and skip codes. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-o-20260930-022722-ceed`.
DIAG-V15-p selected the task's marked turn with no skip code or warning, but
the task still remained Dispatched. The fake Grok fixture had omitted
`promptId` from the assistant chunk's metadata while putting `prompt_id` on
the completion. The normalizer consequently gave AssistantText the anonymous
segment id `:0` and TurnEnd the named prompt id. The task reply service
correctly waited for the ending response's own text. The fixture now emits
the same prompt id on the assistant chunk so the real normalizer can join the
two records. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-p-20260930-023404-f7a1`.
DIAG-V15-q passed the dedicated running-converter row: one executed, one
passed, zero failed. The independent runner's live converter crossed the held
tool gate after dispatcher death; fresh transcript sync settled the original
task and the pump published the sealed output. Receipt:
`.antiphon/checkpoints/r36/DIAG-V15-q-20260930-023940-28ba/run.trx`.

## V-17, V-18 and V-23 ordinary verdicts

V-17's owned, old correlation receives a late transcript confirmation without
creating a second intent, an inbound-loss incident or an early reply stamp.
After the conversion deadline, the pump publishes one annotated original
fallback and settles that correlation; another TTL sweep stays silent. The
attention test checks Held, Failed and PublishUncertain references and severity
from fresh state, plus absence of a Published row. It does not check the other
four states. CP-5 and CP-7 pass for that stated coverage.

V-18 exercises profile clear/remove, channel disable/unbind/rebind and a
cross-project replacement on both pre-launch and post-worker states. Profile
revocation uses the original fallback; changed bindings hold without a send or
success stamp. The final producer-call barrier races a binding update, which
is revalidated before send. Two actual admissions across a same-name prompt
file/settings edit retain distinct frozen prompt revisions and deadlines.
CP-3 passes.

V-23 checks a converted PDF whose base64 and metadata exceed a 2048-byte
serialized payload cap: the pump discards the obsolete output, publishes the
original within the cap once and creates no replacement worker. CP-4 retains
the exact/+1 and near-default storage limits, and CP-11 crosses a disposable
Redpanda broker with near-default serialized bytes, exact key, Unicode fields
and original attachment bytes. CP-4, CP-5 and CP-11 pass.

## Final checkpoint receipts

The closed CP-1–CP-13 list ran on committed source
`fb12fdb591110f78504b352dd83769b87155ad95`, with one isolated .NET build
and exact plan filter per row. `scripts/run-checkpoint.ps1` acquired the build
slot for CP-1–CP-11; CP-12/13 used `scripts/build-slot.ps1`. Logs and TRX
files are under `.antiphon/checkpoints/r36-final/`.

| Row | Executed / passed / failed / skipped | Verdict |
|---|---:|---|
| CP-1 Unit | 3491 / 3491 / 0 / 33 | Pass |
| CP-2 source settlement | 373 / 373 / 0 / 0 | Pass |
| CP-3 policy/schema | 34 / 34 / 0 / 0 | Pass; V-18 |
| CP-4 file boundary | 64 / 64 / 0 / 0 | Pass; V-23 |
| CP-5 purpose/deadline | 64 / 64 / 0 / 0 | Pass; V-17/V-23 |
| CP-6 crash/transport | 30 / 30 / 0 / 0 | Pass; V-15 running C-3 |
| CP-7 routing/attention | 320 / 320 / 0 / 0 | Pass; V-17 |
| CP-8 existing deadlines | 71 / 69 / 2 / 0 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified`; no introduced failure |
| CP-9 renderer | 19 / 19 / 0 / 0 | Pass |
| CP-10 real browser | 1 / 1 / 0 / 0 | Pass after fixture build and same-row rerun |
| CP-11 gateway wire | 125 / 125 / 0 / 0 | Pass; real broker V-23 |
| CP-12 channels client | 26 / 26 / 0 / 0 | Pass, two files |
| CP-13 client bundle | — | Pass, production Vite bundle |

CP-10's first attempt ran one test and failed before rendering because this
Docker host lacked the local `antiphon-card0418-browser:latest` fixture image.
The two repository Dockerfiles were built under `CP-10-browser-fixture` build
slot; its exact-filter rerun passed at
`.antiphon/checkpoints/r36-final/CP-10-20260930-032356-910b/run.trx`.
The resulting four-page PDF is
`.antiphon/test-output/card-0418/v20-r7/66929bf74b1149fba6e66ec86c80f856/combined.pdf`
(SHA-256 `4b616dab9334404e0a85fdbba5c669fbc99e4caad87d0261545b68d530fca45a`).
I visually inspected all four `page-*.png` images there: each source begins on
its own readable page, the table/code/Polish text/emoji are visible, and no
section is blank or clipped. The test independently checked four pages,
extracted sentinels and unchanged source hashes.

The local ordinary V-1–V-24 matrix and its dependent R-1–R-13 oracles now have
passing test receipts, apart from the inherited CP-8 pair, subject to the
V-15 and attention coverage limits corrected above. R-14 and V-25 still
require the separately authorized actual Slack destination receipt and evidence
accounting; local fake, native transcript and broker receipts do not fulfill
that live gate. PC-1–PC-30 stay pending for method-scoped SourceLanding Mutation,
which is paused; this round did not claim any PC result.
