# CARD-0418 round 36: running converter death and remaining ordinary matrix

This continues [round 35](2026-09-29-card-0418-round-35-evidence.md) from
`6e78ff27f677dcca02a37cd34f32d5184d98a5f1`. The inherited CP-8
`PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` failures remain the
base verdict. All PC-1–PC-30 remain pending for method-scoped SourceLanding
Mutation; no ordinary or nightly run discharges a PC.

## V-15 running C-3

The new C-3 row holds a real staged FakeGrok process inside the frozen conversion
request, kills the separate dispatcher probe while that process is still running,
then releases it. A fresh server-side `AgentSessionRuntime.SyncTranscriptAsync`
reads the runner's native transcript and settles the existing linked task through
the ordinary reply service. The probe only captures the dispatcher's immutable
launch spec; the independent runner owns the live process. The row checks the
retained session/task id, native marked turn, output bytes and manifest, one
published payload, input/output hashes, correlation and attention. Receipts below.

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

## Remaining ordinary work

V-17, V-18 and V-23 are next in plan order. R-2–R-6, R-9, R-11 and R-13–R-14
remain open until their whole-ID oracles are satisfied.
