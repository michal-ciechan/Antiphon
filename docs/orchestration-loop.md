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

A finished Plan or Docs deliverable sitting in a worktree is invisible. Cherry-pick or copy it onto
master and push as soon as the task reports — do not wait for the task to formally settle. Two
2026-08-10 cases (the CARD-0002 design doc and the CARD-0001 fix) sat unmerged for 9 hours before
anyone noticed.

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
| `Merge` | resolving a conflict left behind by a worktree task (auto-spawned after TryMergeBackAsync fails, rarely dispatched by hand) | opus |
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

### Launching an agent

Start a new project through `POST /api/projects/setup` (or `scripts/project.ps1 new -Dir ... -Orchestrator -Start`): it creates the project, board, and preset agent in one transaction and returns readiness. `POST /api/agents` remains for adding an agent to a project that already exists.

Create and start an agent through `POST /api/agents` + `POST /api/agents/{id}/start` (or the UI).
Never launch the `claude` CLI directly, and never a `launch-remote` script. When setting
`modelLevel`, send it as the string `"Frontier"` (or `"High"` / `"Medium"` / `"Low"`). A numeric
token (`0`, `1`, `99`) is a **400** — the wire is the member name, not the enum ordinal (CARD-0007).
Frontier maps to fable (Claude) by default, or `grok-4.6` when `Kind=Grok` is passed.

---

## 3. Writing a brief

**The standing rules are delivered automatically now (CARD-0058). Do not retype them.** Every
delegate launches with `server/Bundles/delegate-basics.md` composed into its
`--append-system-prompt` — foreground-only, no sub-delegation, commit-and-push each slice,
`OutputPath` with a forward slash, verify pre-existing red by stashing before blaming yourself. That
file is **canonical**; this section deliberately does not restate its contents, because a rule
restated in three places drifts in three places, and it drifted here first. Improve a rule by
editing that file in a PR: every future dispatch gets the better version, with nothing to
reconcile. A sub-orchestrator additionally launches with `server/Bundles/orchestrator.md`.

So a brief is now **the goal plus the state of today**, and the state is the part no bundle can
carry:

- **Name the known-failing tests** and say to verify by stashing. The *rule* is in the bundle; today's
  actual red list is not — it changes weekly, and a bundle naming last week's would be worse than
  silence.
- **Say what is already done** ("slices 1 and 5 are landed as X and Y, build on them, do not redo")
  and **what is out of scope**.
- **Say the warning count, the flaky suites, the ports in use** — anything true this afternoon and
  false next month.
- Give **outcomes, not procedures**. The delegate decides how.

The test of whether this worked is mechanical: briefs are stored on task rows, so sample the next
week's and grep for the six rule-paragraphs. If they are still being typed, the inversion did not
take and this section is still too gentle.

Bundles reach a delegate **at launch**, which has one bounded consequence worth knowing: a warm
pool delegate keeps the bundles it started with until it retires (60 minutes idle). Nothing types
bundles into a live session, deliberately — if a rule change matters urgently for work in flight,
say it in the brief for that dispatch.

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
- **The move and the cleanup in §8 are mechanical — delegate them.** Hand the agent the verdict text
  and the card identifier; it runs `pwsh -File scripts/card.ps1 close CARD-nnnn -ReasonFile <path>`
  (or `-Reason` for something short) and reports the result.

- **A terminal move preserves its `reason`; use it as the verdict** — what shipped, what was
  corrected, what is still open, with commit hashes.
- **A move into an active column used to spawn an agent silently — CARD-0051 made that opt-in.**
  Two dead sessions and a stray worktree came from one such PATCH before the fix. `card.ps1 move`
  (and the API underneath it) now only starts a session when `-Spawn` / `spawn: true` is passed, and
  says so when it suppressed one. Muscle memory from before the fix should assume nothing starts
  unless asked.
- Corrections to a card's text go through `card.ps1 edit` (`PATCH /api/cards/{id}/content`,
  CARD-0019), which records a revision with a reason. Before that shipped, a wrong card could only
  be corrected by filing another card.
- **`scripts/card.ps1` is the preferred way to touch a card from a shell now** — identifier
  addressing, the limits endpoint, and file-backed text are documented in its own header comment,
  which is canonical; see also the AGENTS.md synopsis. `server/Bundles/board-api.md` (attached to an
  agent that works the board directly) still documents the raw API for callers that can't shell out
  to the CLI — fix a wrong rule there, not here.
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

**The check-in timer (CARD-0047) and the card CLI (CARD-0051, `scripts/card.ps1`) have shipped** —
§4 and §7 now describe them running, not proposed. In rough order of payback for what's left:

1. **The unmerged-branch sweep** from §1 — a scheduled job that reports genuinely unapplied work.
2. **A post-merge deploy script** that reads the diff and decides which restarts §6 requires.
