# The orchestration loop

How a card gets from the board to shipped, using delegates. Written from what actually worked
2026-08-13..16, including the parts that did not. The aim is that this is repeatable without
re-deriving it, and mechanical enough to automate later.

The orchestrator's job is to **decide, verify and record**. The reading, the writing and the running
are delegated. The orchestrator's context is the scarce resource: spend it on judgement, not on
archaeology.

---

## 1. The cycle

```
pick a card
  └─ is there a SOLID plan?  ── no ──▶ fable Plan agent ──▶ land the plan on master
  │                                                              │
  └─ yes ◀───────────────────────────────────────────────────────┘
       ▼
   opus Code agent, in a worktree, working the plan's slices
       ▼
   VERIFY ON MASTER YOURSELF (do not take the report's word)
       ▼
   merge --ff-only  ──▶  deploy  ──▶  close the card  ──▶  clean up
```

### Picking

P0 first, but prefer a card that **changes how everything else gets done** over one more feature.
Prefer a card whose plan already exists — but check properly, see below.

### "Is there a solid plan?"

Two places, and the second is the one people forget:

1. `docs/superpowers/specs/` — but **read the date**. A plan written before a big refactor may name
   files that no longer exist. CARD-0019's client section targeted a board page that CARD-0042 had
   replaced two days earlier; it had to be replanned.
2. **Unmerged branches.** Plans get stranded. Run this, not `git branch -r`:

   ```bash
   for b in $(git branch -r --list "origin/feat/*" | tr -d ' '); do
     git cherry master "$b" | grep -q '^+' && echo "UNAPPLIED: $b"
   done
   ```

   `git cherry` compares by patch-id, so it distinguishes "rebased and landed under a different
   hash" from "never landed". On 2026-08-16, 8 of 10 unmerged branches were already applied and
   exactly 2 held real work — including a 187-line plan for an open card.

A plan is **not** solid if it predates a refactor of the area, assumes an API that shipped
differently, or is under ~20 lines for a feature. Replanning costs ~$5; implementing the wrong thing
costs an hour and a merge.

---

## 2. Tiers

The role sets the model. Do not override without a reason stated in the goal.

| Role | For | Tier |
|---|---|---|
| `Plan` | decompose, design, choose an approach | fable |
| `Code` | write or change code | opus (override `-Level High`) |
| `Review` | judge whether logic is correct | fable |
| `Debug` | find out why something is broken | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git plumbing, branches, PRs | sonnet |
| `Test` / `Deploy` | RUN a thing and report what happened | haiku |

**A `Test` agent runs and reports. It does not repair.** The boundary, stated so it is not a matter
of taste:

| Allowed at `Test` (haiku) | Escalate to `Debug` (opus) |
|---|---|
| Run a suite, report pass/fail counts and the failing names | Explain *why* something failed |
| Re-run a failure **in isolation** to establish flaky-vs-real | Change any production or test code |
| Re-run at a known-good commit (stash / worktree) to establish pre-existing-vs-caused | Widen a timeout, loosen an assertion, add a retry |
| Bisect by re-running | Decide a failure is "expected" and move on |

Isolating an error is narrowing *where* it lives; fixing it is deciding *what is wrong*. The first
is cheap and mechanical, the second is the expensive judgement this tiering exists to buy. A haiku
agent that starts editing a test to make it pass is the worst possible outcome — it is the exact
instinct that left a live 64 KB-truncation reproduction red for weeks by treating a real defect as a
flaky test.

Say this in the brief explicitly; do not assume the role name carries it.

The best single result of this period came from a `Debug` agent; the cheapest useful one was a haiku
check at **$0.12**.

---

## 3. Writing a brief

Rules earned the hard way. Each one maps to a real failure.

- **"Run every command in the FOREGROUND and wait — never background a run and end your turn."**
  A planner backgrounded its test runs, ended its turn, settled having written nothing, cost $8.48
  and left orphaned processes running for hours.
- **"Do not sub-delegate; do not use the Agent tool."** A Worker settles when its turn ends, so one
  that fans out and waits settles on its preamble. If it genuinely needs to fan out, make it
  `-Orchestrator`.
- **"Commit and push each slice as it completes; put the real outcome in the commit message."**
  Commits are the durable report. Two delegates were cut loose mid-task and their work survived only
  because it was committed — a third's survived only because it happened to be on disk.
- **Name the known-failing tests**, and say "verify by stashing before blaming yourself". Otherwise
  every report re-litigates pre-existing red.
- **Say what is already done** ("slices 1 and 5 are landed as X and Y, build on them, do not redo")
  and **what is out of scope**.
- **`--property:OutputPath=bin-<name>/` — FORWARD slash**, and delete it across all ~12 project dirs
  afterwards. A trailing backslash creates a directory whose name ends in a space, which breaks the
  entire build with an error naming unrelated projects.
- Give **outcomes, not procedures**. The exception is process constraints like these, which are
  about how the harness behaves, not how to do the work.

---

## 4. Checking on a delegate

**Check-ins are automatic now (CARD-0047, slices 1-3).** Delegate with `-ExpectAbout <minutes>`
(1-1440, defaults to 10) and the server arms a schedule: a deterministic, read-only probe of the
task row, the delegate's session/transcript, its queue and its incidents — plus its git log for a
worktree task — lands in your session as a `[check <id> #n] ...` note, first around the minute mark
you declared, then backing off along a Fibonacci ramp fixed from a 5-minute base (5, 10, 15, 25, 40,
60, 60 …, capped at 60 minutes — CARD-0061) for up to 10 checks. Every gap is rounded to a
human-readable number on the way out — nearest 5 below 30 minutes, nearest 10 from 30 to 60 — a
separate step from the ramp itself, so it keeps the schedule legible even if the base or the ramp
change later; the shipped sequence above is already round, so this doesn't move it. The declared
duration schedules only the first check; it no longer scales the ramp. It costs no model call today
and cannot write to the delegate at all.
**A check note is a progress report, never a completion** — the delegate's own `[task <id> done]`
note still arrives separately, and a check note can never be mistaken for it or settle anything
(see `.claude/skills/antiphon-delegate/SKILL.md`).

**Do not infer from silence** as general practice still holds, but the specific cause behind the
worst observed lags is now found and fixed, not just worked around. It was never a slow pipeline —
task `817682e9` traced the 90-minutes-late and never-arrived notifications to CARD-0055's root
cause: a queued completion note was marked Sent as soon as the screen merely redrew after Enter, so
a swallowed Enter left the note sitting unsubmitted in the composer (one sat there 104 minutes,
only reaching the transcript when a LATER delivery's Enter pushed it in), and a second note was
lost outright when its own Enter resubmitted the first note's stale body instead. CARD-0055
(shipped `4bb65fb`..`165da34`) fixes it at the source: a delivery is now Delivered only when a
matching `UserPrompt` transcript record appears, with Enter re-presses and a late-confirm brake
against double-submission — see the CLAUDE.md gotcha for the mechanism. The schedule above and the
manual probes below still earn their keep as a safety net and for a faster look, but they are no
longer standing in for this specific defect.

Cheapest first:

```bash
# has it settled?  (one request, no model)
curl -s localhost:17202/api/agent-tasks | python -c "..."   # filter by short id

# the stored report — available BEFORE the notification
pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>

# the truest progress signal: commits exist before reports do
git log --oneline master..feat/card-task-<id>

# what it is doing right now
curl -s localhost:17202/api/sessions/<sessionId>/transcript
```

**Traps:**

- **Do not scan processes by command-line substring.** A scan for `*bin-c45*` matched *the scanning
  command itself*, "finding" a runaway agent that was the orchestrator's own session, and the
  follow-up kill terminated its own tool shell. Exclude self and ancestors, or key on session
  ownership.
- **Do not trust the agent's account of what it left behind.** One reported its spec "untracked, not
  committed" when the commit existed and was pushed. Check the repo.
- **`*.ansi.log` files are raw pty capture**, not structured logs. Grepping them yields escape
  sequences. Use `GET /capabilities` and the API instead.

---

## 5. Verify before merging

Run the acceptance criterion **yourself**, on master, after merging is too late to be cheap:

```bash
git merge --ff-only feat/card-task-<id>     # never a merge commit
dotnet run --project tests/<X> --property:OutputPath=bin-v/ -- --treenode-filter "/*/*/<Class>/*"
```

Read the commit messages rather than the report — in this repo they carry the real outcome.

---

## 6. Deploy

What changed decides what restarts:

| Changed | Action |
|---|---|
| server / API | `pwsh -File scripts/restart-apphost.ps1` (never a second `dev-aspire.ps1`) |
| client | **`npm run build` in the MAIN checkout** — a worktree build does not transfer, `dist` is gitignored, and the E2E fixture hard-fails on a stale bundle |
| pty / session-runner | also `pwsh -File scripts/restart-session-runner.ps1` — sessions survive, hosts are re-adopted, and only NEW sessions get the new shadow-copied binaries |
| DB schema | the AppHost restart applies the migration |

Verify: `/health` on 17202/17203/17204/17205, `GET :17204/capabilities` for the pty backend
(it states whether it fell back), and compare running-session counts across a runner restart.

---

## 7. Close the card — orchestrator writes the verdict, haiku executes it

Split by what each part actually is:

- **The verdict is judgement and stays with the orchestrator.** It is synthesis across the whole run
  — what shipped, what was corrected, what was disproved, what is still open, which other cards it
  touches. A haiku agent cannot see that from the repo.
- **The `PATCH` and the cleanup in §8 are mechanical — delegate them.** Hand the agent the verdict
  text and the card identifier; it makes the call and reports the result.

- **A terminal move preserves its `reason`; use it as the verdict** — what shipped, what was
  corrected, what is still open, with commit hashes.
- **Never move a card into an ACTIVE column for bookkeeping — it SPAWNS AN AGENT.** Two dead
  sessions and a stray worktree came from one such PATCH.
- Corrections to a card's text go through `PATCH /api/cards/{id}/content` (CARD-0019), which records
  a revision with a reason. Before that shipped, a wrong card could only be corrected by filing
  another card.
- Findings that outlive the card go in `docs/investigations/`. Agent scratch output lands in
  `.antiphon/`, which is **gitignored** — an 11 KB proven root-cause writeup was nearly lost that way.

---

## 8. Clean up

```bash
git worktree remove <path> && git worktree prune && git branch -d <branch>
```

```powershell
Get-ChildItem C:\src\Antiphon -Recurse -Depth 3 -Directory |
  Where-Object { $_.Name -like 'bin-*' -or $_.Name -match '\s$' }
```

A directory whose name ends in a space must be deleted via the `\\?\` prefix; normal path APIs
cannot open it.

---

## 9. What to automate first

**The check-in timer (CARD-0047) shipped** — §4 now describes it running, not proposed. In rough
order of payback for what's left:

1. **`scripts/card.ps1`** (CARD-0051) — every card operation here is currently a hand-written script,
   because there is no card CLI and shell quoting mangles card text.
2. **The unmerged-branch sweep** from §1 — a scheduled job that reports genuinely unapplied work.
3. **A post-merge deploy script** that reads the diff and decides which restarts §6 requires.
