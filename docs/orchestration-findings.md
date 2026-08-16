# Orchestration findings

A running log of what running `docs/orchestration-loop.md` has taught us — the self-improvement
record behind that document. Each entry states what we learned, the evidence for it, and what
changed (a doc edit, a card, a rule) or what should still change. Append new entries at the
bottom; correct old ones in place rather than superseding them with a new entry.

---

## 2026-08-16 — A Test agent isolates, it never repairs

**What we learned:** "interpreting a failure is Debug's job, not Test's" was true but too soft to
act on — it never said what a haiku `Test` agent MAY do when a suite goes red, so the boundary
stayed a matter of taste until someone actually hit it.

**The evidence:** a live 64 KB-truncation reproduction sat red for weeks precisely because the
instinct at that boundary was to treat a real defect as a flaky test and paper over it, instead of
isolating and escalating.

**What changed:** `docs/orchestration-loop.md` §2 now states the boundary as a table — allowed at
Test: run and report, re-run in isolation to establish flaky-vs-real, re-run at a known-good
commit to establish pre-existing-vs-caused, bisect by re-running; escalate to Debug: explain why,
change any code, widen a timeout, loosen an assertion, add a retry, or decide a failure is
"expected" (commit `5951f06`).

---

## 2026-08-15/16 — "Check, don't infer from silence" was treating a symptom

**What we learned:** the loop's standing advice not to infer completion from silence was correct
but aimed at the wrong layer. The real defect is that a delegate's completion note can be typed
into the parent's composer and never actually get submitted.

**The evidence:** task `817682e9` measured that every sampled completion note was enqueued and
typed into the pty within seconds of settlement — the queue and delivery machinery are not the
defect. But one note (`ea2feb92`) sat unsubmitted in the composer for 104 minutes, only reaching
the transcript when an unrelated later delivery's Enter submitted it; the note behind it
(`15c9150e`) was lost entirely, overwritten before it was ever sent.
`VerifiedPromptSubmitter.SubmitAsync` confirms submission by "output advanced after Enter," not by
the prompt actually landing as a transcript record — so a delivery can be marked Sent and never
have happened at all.

**What changed:** filed as its own card, CARD-0055, deliberately separate from CARD-0047 — the
scheduled checks that card added are a safety net on top of this, not a fix for it. **Update
2026-08-16: shipped and closed.** `4bb65fb`/`0134964`/`8410d9a`/`165da34` land the fix this entry
called "still open" — delivery verification now confirms a matching `UserPrompt` transcript record
within a window, re-presses Enter (never re-types), late-confirms before any redelivery, and parks
after `MaxDeliveryAttempts` with an incident instead of silently marking the delivery Sent. See the
CLAUDE.md gotcha for the mechanism, and `docs/orchestration-loop.md` §4 for the updated advice.
Lesson for future briefs, still true: when a workaround is being written into a process document,
ask what makes the workaround necessary — §4 of the loop doc stayed useful advice, but stopping
there would have left the actual bug unfiled.

---

## 2026-08-16 — Independent verification is cheap and it works

**What we learned:** commissioning a second, independent test run is worth it even when the
implementer already reports green — it catches things a single run can miss and turns a
single-observer pattern into a corroborated one.

**The evidence:** task `ba6cf700` (delegate `/usr/bin/bash.141`, 6m42 wall time) independently
re-ran the suite and confirmed 43/43 new tests passing — and in doing so hit a *different* member
of the known flaky-test cast than the implementer's own run had. That is a second, independent
observation of CARD-0050's "rotating cast" claim, not just a repeat of the first one.

**What changed:** no rule changed — this confirms the loop's existing practice (a verification
pass separate from the implementer) pays for itself, and it strengthens the evidence behind
CARD-0050 rather than opening new work.

---

## 2026-08-16 — Positive controls should be a standing expectation, not a happy accident

**What we learned:** a negative result ("it correctly does not settle," "the probe correctly wrote
nothing") is only meaningful if there is a matching positive proving the mechanism it guards
against actually fires when unguarded. Without one, a negative can pass vacuously, for a reason
that has nothing to do with the thing under test.

**The evidence:** task `26809d78` added two controls: one proving its cannot-settle test's own
settlement path could genuinely reach settlement (so the negative was not just exercising a path
that never runs), and a second proving that a bare `git status` really does rewrite `.git/index`
(so a "the probe doesn't write" negative was checked against a git command that actually writes
when nothing guards it).

**What changed:** still open — `docs/orchestration-loop.md` §3 ("Writing a brief") should say to
ask for a positive control alongside any negative or read-only claim. Not yet added; recorded here
so it isn't lost.

---

## 2026-08-16 — A spec can be wrong in small factual ways; verify, don't trust

**What we learned:** even a carefully-researched spec can get a small mechanical detail wrong, and
an implementer has to check it against the real tool rather than transcribe it.

**The evidence:** the CARD-0047 spec
(`docs/superpowers/specs/2026-08-16-card-0047-delegate-check-ins.md`) wrote a bare `git status
--no-optional-locks`, placing the flag after the subcommand — where git rejects it. It is a
git-LEVEL option (`git --no-optional-locks status`). The shipped implementation gets this right:
`GitWorkspaceService`'s read-only wrapper prepends `--no-optional-locks` ahead of every argument,
including the subcommand (`server/Application/Services/GitWorkspaceService.cs:312`).

**What changed:** nothing to fix — the implementer caught the discrepancy during slice 2 and
shipped it correctly. Recorded so the same slip isn't repeated by a future reading of the spec
text alone.

---

## 2026-08-16 — Plans get stranded, and the branch list won't tell you

**What we learned:** an unmerged remote branch is not evidence of unmerged *work* — most of what
accumulates there has already landed, just under a different commit hash after a rebase or squash.
A plain branch listing can't tell the two apart; comparing by patch-id can.

**The evidence:** `git cherry master <branch>` (patch-id comparison, not `git branch -r`) run
against every `origin/feat/*` branch on 2026-08-16 found that 8 of the 10 unmerged branches were
already fully applied to master. Exactly 2 held real, unlanded work — including a 187-line plan
for a still-open card, and a second amendment that turned out to have already been superseded in
practice by an independently-written change that happened to agree with it.

**What changed:** `docs/orchestration-loop.md` §1 now gives the `git cherry` loop as the standing
method for "is there a solid plan already," in place of trusting `git branch -r`.

---

## 2026-08-16 — Agent scratch output under `.antiphon/` is gitignored

**What we learned:** work an agent writes to its own scratch directory does not survive by
default — `.antiphon/` is gitignored, so a genuinely valuable writeup left there is one `git
clean` or worktree removal away from gone.

**The evidence:** an 11 KB root-cause investigation for CARD-0048 (the DA1 startup stall) was
written to `.antiphon/` and nearly lost with it. It was rescued into `docs/investigations/`
(commit `a7bf42f`).

**What changed:** `docs/orchestration-loop.md` §7 now says findings that outlive the card belong
in `docs/investigations/`, explicitly distinct from `.antiphon/` scratch output.

---

## 2026-08-16 — Cards being correctable in place changed the workflow itself

**What we learned:** before CARD-0019, a wrong or outdated card could only be fixed by filing a
new addendum card pointing back at the original — an indirection that split one correction across
two cards.

**The evidence:** CARD-0020 exists as exactly that kind of addendum, filed because CARD-0019 (at
the time it was needed) had no other way to be corrected.

**What changed:** `PATCH /api/cards/{id}/content` (CARD-0019) now records a revision with a reason
directly on the card. Corrections go on the wrong card itself, in place, instead of spawning a new
one — `docs/orchestration-loop.md` §7 documents this as the current process.

---

## 2026-08-16 — The orchestrator read a UTC timestamp as local and invented a bug

**What we learned.** Every timestamp the API and the DB return is **UTC**; this machine's wall clock
is **BST (UTC+1)**. Comparing an API `nextCheckAt` against a local `ls` mtime silently shifts the
answer by an hour — enough to turn "due in 40 minutes" into "overdue by 20".

**The evidence.** Task `6011f623` had `NextCheckAt = 16:05:49Z`. The orchestrator compared it to a
file mtime of `16:26` — local time, i.e. `15:26Z` — concluded check-ins were 20+ minutes overdue and
never firing, and dispatched an opus `Debug` agent (`8ae80695`, $7.05) on that premise. The premise
was false: nothing had come due yet, because the server carrying the feature had only started at
`15:04:04Z` and every task dispatched since had settled before its first check.

**What changed.** Two rules:

1. **Compare UTC to UTC, and say which you have.** When reading a timestamp from the API or Postgres
   (`timestamptz`), convert explicitly or print both. Never compare an API timestamp to a filesystem
   mtime or a mental clock.
2. **Before dispatching an investigation, state the premise as a falsifiable claim and check it.**
   "X should have happened by now" is a claim about a clock. One query — `SELECT now(), NextCheckAt`
   in the same statement — would have cost nothing and saved the run.

**The honest postscript.** The investigation still found two real defects — an off-by-one in check
numbering, and (seriously) five bare `await`s in `TickAsync` where one throwing sweep aborted the
remaining sweeps *and* the dispatch loop, permanently. That is luck, not method: a false premise
that happens to land near a real bug is not a technique. The orchestrator had also *seen* the
off-by-one in a live check note (`#2` on a first check) and rationalised it away as an artefact of
the agent's own probe.

---

## 2026-08-16 — Measuring an assumption can remove planned work, not just validate it

**What we learned.** The default expectation for a headed canary is that it either confirms an
assumption (nothing changes) or falsifies it (something in the design has to change). CARD-0055
slice 4 surfaced a third outcome that the loop hadn't named: the assumption holds, and BECAUSE it
holds, a piece of work the spec had already planned for the falsified case turns out to be
unnecessary and should not be built at all.

**The evidence.** CARD-0055's shipped design (slices 2-3) rested its full weight on two things
nobody had measured against real Claude: that pressing Enter on an already-empty composer is a
no-op (so the confirm loop's re-press can never double-submit), and that a COLLAPSED paste's JSONL
`UserPrompt` record carries the full body rather than the `[Pasted text #N +M lines]` placeholder
the composer shows (CARD-0037) — the delivery matcher reads that record, so a placeholder-only
record would fail every large delivery's confirmation. The spec named an explicit contingency for
the second assumption failing: "for bodies above the collapse threshold, fall back to the weak-match
arm." Slice 4's two headed canaries (`ClaudeSubmitConfirmCanaryTests`) measured both directly
against real Claude on the modern pseudoconsole and BOTH held — three empty Enters produced zero
new JSONL records, and a 4 804-char/62-line collapsed paste's record carried all 4 804 chars intact.
The commit message states the consequence plainly: "The spec's contingency … is NOT needed and is
not implemented." No fallback code was written and then deleted — the measurement itself is what
kept it off the branch.

**What changed.** No process rule existed to say this could happen, so briefs asking for an
assumption to be pinned implicitly framed the canary as a validation-or-bug-report step. Worth
carrying forward: when a spec names a contingency for an assumption's failure, treat the canary
that measures the assumption as also deciding whether the contingency gets built — a held
assumption is itself a reason to cut planned scope, not just a green checkmark.
