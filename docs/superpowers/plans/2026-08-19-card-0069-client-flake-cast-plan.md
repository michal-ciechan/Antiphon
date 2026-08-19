# CARD-0069 — The client flake cast: measurements, root cause, what shipped, what remains

**Date:** 2026-08-19 · **Sibling of:** CARD-0050 (.NET flake cast) — same method: measure first,
never widen a deadline on a guess.

The card named four files failing transiently under load — `BoardPage.test.tsx`,
`CardEditModal.test.tsx`, `CardModal.test.tsx`, `DiffReview.test.tsx` — and a buried second
problem: a merge that reported **exit code 0 over six failing tests**. Both are resolved below;
two follow-ups remain as planned work.

All measurements: 2026-08-19, i7-6700K (4 cores / 8 threads), fresh worktree
(`card-task-5cd24ac4`), vitest 4.x default pool, suite at 46 files / 448 tests.

## Finding 1 — the 5s default had effectively zero headroom, and the slowness is real

Per-test durations from `--reporter=json` (same tests, three configurations):

| Test (slowest per cast file) | Alone | Inside full run (cold caches) | Inside full run (warm) |
|---|---|---|---|
| CardEditModal · "sends only the fields that changed…" | 2 702ms | 3 413ms | **4 584ms** |
| DiffReview · "posts a selected diff line range…" | 1 947ms | 3 077ms | 2 920ms |
| BoardPage · "submits the move with its reason…" | 1 117ms | 2 669ms | 2 315ms |
| CardModal · "closes itself once the card is archived…" | 1 623ms | 2 100ms | 1 371ms |

**CardEditModal's worst case reached 92% of the 5 000ms budget on an otherwise-idle machine.**
The load multiplier from sibling vitest workers alone is 1.3–2.5×; on 2026-08-17 the machine was
also running .NET builds and agent sessions, which is all it takes to tip 2.7s-isolated tests
over 5s. The failure then lands in whichever file a worker happens to be running — a rotating
cast presenting as per-file flakiness while the cause is one global budget.

**The slowness is genuine CPU work, not a CARD-0014-class hidden-element wait.** All four files
render through `renderWithProviders` (`client/src/test/utils.ts`), which already applies
`env="test"` to the MantineProvider — the CARD-0014 fix — and all four pass *quickly* in
isolation (a hidden-element bug burns its full timeout even alone). The cost is jsdom rendering
large Mantine trees plus `userEvent` interaction sequences. Real, measured, and shared: one
cause across all four files, unlike CARD-0050's cast of distinct causes.

**The `AgentsPage` 20s override was NOT masking a bug.** Isolated: 2.9s max, all pass. Inside a
full run: 9 233ms max — a genuine ~3× load multiplier on genuinely heavy tests. The override was
correct in size and kind; the problem was the policy being scattered (see Finding 3).

## Finding 2 — run order / caches matter, and merges always run in the worst configuration

Full-suite wall time: **169s on first-ever run** in a fresh worktree (cold vite transform cache,
cold OS file cache, fresh `npm install`) vs **101s warm** — 40% faster. Per-test durations under
load are roughly stable between the two; the cold cost concentrates in transform/import/
environment, which lengthens the contention window and the tail. The suite's wall time is
dominated by per-file overhead, not tests: cold run split was transform 8s / setup 70s /
import 298s / environment 281s / tests 254s (sums across parallel workers).

This is the analogue of CARD-0050's cold-file-cache finding, with a sharper edge: **merge/verify
agents always test in a fresh worktree on its first-ever run** — the slowest cache state the
suite has — while anyone re-running to "check" the flake does so warm. The flake preferentially
appears to exactly the agents doing acceptance runs, and disappears for whoever re-checks.

Also noted: `src/lint.gate.test.ts` (CARD-0076, landed 2026-08-19) occupies one worker for ~36s
running ESLint in-process. It postdates the 2026-08-17 flakes — not their cause — but it is a new
fixed contention source in every future full run. Kept as-is (the gate is deliberate); noted for
the optimization follow-up.

## Finding 3 — the per-file override furniture had already arrived

The card said "do not fix this by adding `testTimeout` overrides file by file". That is how it
was already being fixed: **14 files** carried `vi.setConfig({ testTimeout: 20_000 })`, accreted
since CARD-0014, each individually defensible. The four cast members were simply the interaction-
heavy files that *hadn't* received one yet. The de-facto suite policy was already 20s — scattered,
invisible in any one place, and enforced by whichever file had flaked recently enough to get the
line.

**Shipped:** one global `testTimeout: 20_000` in `client/vite.config.ts` (with the evidence in a
comment), all 14 per-file overrides deleted. This is not "widening the deadline" — 20s was
already the effective policy for a third of the suite; it is making the policy singular,
reviewable, and justified by measurement. The config comment states the rule going forward: the
timeout is a hang detector, not a performance budget; a test that outgrows the global budget gets
made cheaper, never a private override.

Verification: full suite green twice on the new config (and a deliberately-failing probe test
correctly failed the run during the exit-code work, proving the suite still reds).

## Finding 4 — "Exit code 0 over six failures": mechanism established, and it is not vitest

Probed with a deliberately failing test, 2026-08-19:

| Invocation | Exit code seen |
|---|---|
| PowerShell: `npx vitest run <file>` | 1 ✓ |
| PowerShell: `npm test` (even piped through `Select-Object -Last N`) | 1 ✓ |
| Bash: `npx vitest run <file>` | 1 ✓ |
| **Bash: `npx vitest run <file> 2>&1 \| tail -3`** | **0 ✗** |

Nothing in the repo, vitest, or npm treats failures as non-fatal. The mechanism is the classic
Bash pipeline trap: the pipeline's exit code is the LAST command's — `tail`'s, always 0 — and
capping test output with `… 2>&1 | tail -50` is a near-universal agent habit. The merge agent's
"Exit code 0 (expected failures, not test suite errors)" was it reading `tail`'s exit code and
inventing a story for the contradiction with the failure text it could see.

**Shipped:**
- `scripts/test-client.ps1` — canonical runner: streams output, tees the full log to
  `logs/client-tests.log`, ends with `CLIENT TESTS EXIT CODE: n (PASS|FAIL …)` **in the text**
  (so the verdict survives any output-capping pipe), exits with the real code.
- CLAUDE.md gotcha bullet: run client tests via the wrapper; never read a Bash pipeline's exit
  code as the verdict; "flaky" may only be claimed after an isolation re-run actually passed.

## What shipped in this session (all on master)

1. Global `testTimeout: 20_000` in `client/vite.config.ts`; 14 scattered `vi.setConfig` overrides
   removed (`AgentsPage`, `AgentBundleAttachments`, `AgentReplyStyle`, `AttentionPanel`,
   `BlockedReplyRow`, `DelegateModal`, `DelegationsBoard`, `HomePage`, `MobileHomePage`,
   `OrchestratorPage`, `RenderedMarkdownReview`, `SelectionDelegate`, `SessionMessageQueue`,
   `TaskDrawer`).
2. `scripts/test-client.ps1` — exit-code-honest client test runner.
3. CLAUDE.md client-test gotcha (runner, pipeline trap, no per-file overrides, isolation-re-run
   rule for calling anything flaky).
4. This document.

## Remaining planned work (not shipped — needs its own slices)

**R1 — Make the heavy tests cheaper (the card's "real" fix; optimization project, not a tweak).**
The budget change removes the rotating-red failure mode; it does not make a 9s test good. Ranked
targets by measured cost inside a full run: `AgentsPage` (43.5s file total; "drafts agent fields
from a description" alone is 9.2s), `DelegateModal` (25.4s/10 tests), `BoardPage` (29.8s/24),
`CardEditModal` (14.3s/7). Concrete levers, in expected-value order:
  1. `userEvent.setup({ delay: null })` where tests type long strings — typing is char-by-char
     with a scheduler yield per keystroke; the long description/reason fields are the slowest
     interactions. Needs per-test verification that nothing depended on inter-key ticks.
  2. Stop re-rendering whole pages per test where a scoped component works (`AgentsPage` is
     rendered by three separate test files: `AgentsPage`, `AgentBundleAttachments`,
     `AgentReplyStyle`).
  3. Per-file wall cost is dominated by environment+import (≈12s/file summed across workers);
     consolidating micro-files is NOT worth it (they're cheap), but any new interaction-heavy
     file should weigh joining an existing file first.
Success criterion, matching the card: slowest test under full-suite load ≤ 2s, so even a 5s
budget would have 2.5× headroom — at which point the 20s global can be REDUCED (do that in the
same slice, so the budget tracks reality downward too).

**R2 — Decide whether the lint gate belongs inside the worker pool.** It burns one of ~8 workers
for 36s of single-threaded ESLint. Options: leave (simplest, costs ~sequential-tail seconds),
or exclude it from the default `vitest run` project and have `scripts/test-client.ps1` run it as
a separate sequential step (same gate strength, zero contention with component tests). Do not
weaken the gate itself (CARD-0076's rule stands).

## What was deliberately NOT done

- No per-file timeout anywhere (the card's "do not", enforced by deleting all existing ones).
- No change to `lint.gate.test.ts` or the ESLint rule set.
- No attempt to reproduce the 2026-08-17 failures bit-for-bit: the mechanism (contention over a
  ~0-headroom budget) is established by measurement; the card had already verified all four files
  pass in isolation.
- The external-load timing experiment (cast files vs a concurrently-running second suite) came
  back flat, but the load process was found already dead at cleanup — recorded as inconclusive,
  not as evidence against the contention mechanism (which the within-run 1.3–3.2× multipliers
  establish on their own).
