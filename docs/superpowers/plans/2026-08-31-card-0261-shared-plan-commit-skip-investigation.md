# CARD-0261 — Shared Plan/Docs delegates settle without committing: investigation + plan

Date: 2026-08-31. Investigation only; nothing built. Companion question: does CARD-0215 share the
root cause? (Answer: no — see §4.)

## 1. What actually happened (all four instances, evidence-checked)

Four Shared-workspace Plan/Docs delegates wrote their deliverable, reported success, and never ran
`git commit`. **All four were Codex** (AgentKind=2 on the task row):

| Task | Card | Kind | Settled (UTC) | Rescue commit |
|---|---|---|---|---|
| `fb527101` | CARD-0240 | Codex | 08-30 04:36 | `5be33f72` ("delegate wrote this plan doc but never committed it") |
| `6fc7821e` | CARD-0247 S4 (Docs) | Codex | 08-30 19:39 | `bfb8027d` ("reported success but never committed") |
| `f2a0a68a` | CARD-0256 | Codex sol | 08-30 20:44 | `1012ebd4` ("Committed on the delegate's behalf") |
| `3599b6ab` | CARD-0245 | Codex sol | 08-30 20:59 | `73d62bf7` ("Committed on the delegate's behalf") |

Three corrections to CARD-0261's description, all verified:

- **CARD-0240's plan task was Codex, not Claude.** The task row says AgentKind=2, and the rescue
  commit `5be33f72` (authored by a Claude Sonnet session) says so in its own message.
- **CARD-0033's plan doc is NOT this failure.** It was a Claude *Worktree* task whose plan commit
  `9016a3b1` (08-26) landed on `feat/card-task-7d3d19b6` and never reached master — the CARD-0215
  ancestry shape, hand-relanded as `d9691ee1` (08-30). A real commit happened; it got stranded.
  Counting it as a CARD-0261 instance conflates the two cards.
- **None of the four failing reports claimed a commit.** The card remembers 6fc7821e as having
  "reported the docs edit was made and committed"; its stored Result says the edit was made and
  "`git diff --check` passes" — git *reads* ran fine, and no commit is mentioned anywhere in any of
  the four reports. The reports are accurate descriptions of the file with the git step silently
  absent, settled `done`. That matters for the fix: the delegate isn't lying about a commit, it is
  omitting one and nothing downstream notices the omission.

## 2. Q1 — Codex-specific? Yes, on every observation we have

Census of settled **Shared** Plan/Docs tasks (delegation DB, 08-21 → 08-31), each cross-checked
against the plan doc's git ADD commit:

- **Claude: ~19 tasks, 0 skips.** Every one self-committed — the ADD commit's timestamp falls in
  the same minute as `CompletedAt` (delegates commit, then report), with Claude trailers.
- **Grok: 1 task (`130f43d2`, CARD-0233), 0 skips.** Self-committed `46319adc`.
- **Codex: 6 tasks, 4 skips.** The two that committed:
  - `0634c871` (08-22, CARD-0132 plan) — its brief said, verbatim: *"commit the plan doc directly
    to master when done"*. Committed `f4841a67` minutes before settling.
  - `d79669af` (08-29, CARD-0227 plan) — the task's *subject* was commit attribution; its report
    names the SHA ("Committed/pushed: `217096e` on `master`").

  The four skips (08-30) are exactly the four whose briefs contained **no commit instruction at
  all** (checked: no line in any of the four Goals asks for a commit). So the observed Codex rate
  is 0/4 without an explicit ask, 2/2 with one — while Claude is ~19/19 on the standing bundle rule
  alone.

### Mechanism (verified against the installed codex-cli 0.151.0 binary)

The delegate-basics bundle's "COMMIT AND PUSH EACH SLICE" reaches Codex — `InstructionBundles.
ForDelegate` is kind-agnostic and the dispatcher passes the composition as
`-c developer_instructions=` (`CodexLaunchArgs.DeveloperInstructions`), which lands at the **head
of the first developer message, with Codex's own base instructions after it**. Those base
instructions (extracted from `codex.exe` strings) contain:

1. Final-answer guidance that frames committing as a next step to **offer, not do**: *"If there's
   something that you think you could help with as a logical next step, concisely ask the user if
   they want you to do so. Good examples of this are running tests, **committing changes**, or
   building out the next logical component."*
2. *"Do not amend a commit unless explicitly requested to do so."* (amend, not commit — but the
   same don't-touch-history-uninvited posture).
3. A risk rubric that keeps git actions **"high" risk when they touch a protected/default
   branch** — and a Shared plan delegate commits straight to `master`.

So Codex's trained default is "edit files; leave git to the user; offer the commit in the final
answer" — and in a delegate harness there is no user to accept the offer, so the offer simply
doesn't appear and the work dies uncommitted. A brief-level instruction (a *user* message) clears
its own "unless explicitly requested" bar and wins (2/2); the developer-channel bundle rule loses
(0/4). It is **not** sandboxing: the codex profile runs `--dangerously-bypass-approvals-and-sandbox`
(profile revision 3), and 6fc7821e demonstrably ran git commands.

Nothing regressed on 08-30: delegate-basics.md changed once that day (`eab67a33`, adds the
verdict-line section; the commit bullet untouched). The cluster is explained by exposure — 08-30
was the first night several Codex Shared plan dispatches ran with briefs that never mentioned
committing.

## 3. Q2 — does settlement verify a commit? No, structurally

`AgentTaskReplyService.TryDescribeGitAsync` (the CARD-0159 S3 git-facts step) short-circuits:

```csharp
if (task.Workspace is WorkspaceMode.Shared or WorkspaceMode.ReadOnly)
    return ("unattributable", null);
```

— CARD-0227 made Shared git state deliberately unattributable (a commit in the shared checkout
can't be credited to the delegate), and the baby went out with the bathwater: **no git question of
any kind is asked about a Shared settlement**. The only zero-commit warning that exists is
Worktree-only *and* gated on `DelegationGitFacts.IsCodeProducing`, which does not include `Plan`.
Scope-drift recording doesn't catch it either: `AgentFilesService.GetFilesAsync`'s transcript arm
keys on `ToolName ∈ {Write, Edit, NotebookEdit}`, and `CodexTranscriptNormalizer` deliberately maps
no FileChange/CommandExecution items (v1), so a Codex Shared task contributes no attributable file
evidence at all.

### Proposed check — S1: "the report's own paths must not be dirty"

Cheap, mechanical, existence-only; lives in the Shared branch of `TryDescribeGitAsync`:

1. Extract candidate file paths from the settled report text. Two shapes are required, because the
   failing Codex reports used **absolute Windows paths**
   (`C:\src\Antiphon\docs\superpowers\plans\...md`) while Claude reports use repo-relative
   forward-slash paths: strip a `task.RepoPath` prefix case-insensitively, normalize `\` → `/`,
   keep paths that resolve under the repo. Cap at ~20 paths.
2. `git status --porcelain -- <paths>` in `task.RepoPath` (one process, scoped to the named paths).
3. Any dirty/untracked path ⇒ completion header `git=uncommitted:N` (instead of
   `unattributable`), a Warning `AgentTaskEvent` naming the paths, and a caller-facing warning
   line above the report: *"The report names N file(s) that are still uncommitted in the shared
   checkout — the work has not landed. Commit before building on it."* All clean and present in
   HEAD ⇒ `git=landed` (positive evidence, same spirit as CARD-0159).
4. **Warning only. Never blocks, holds, kills, or re-types** — same contract as scope drift
   (CARD-0063 §2.5), same never-throw wrapper as the rest of `TryDescribeGitAsync`.

Why this respects the standing constraints:

- **CARD-0227 (unattributability)**: the check never claims *who* committed. It reports dirt on
  paths **the report itself names** — row-correlated, positive evidence, not archaeology.
- **CARD-0247 (trust the report)**: no diffs are read, no content judged. It is precisely the
  existence check the card asked for, and it converts "orchestrator runs git status by hand after
  every plan settle" (three manual interventions in 20 minutes on 08-30) into a header the
  orchestrator reads for free.
- Known benign false positive: a concurrent Shared writer (or the orchestrator) dirties the same
  file after the delegate's commit ⇒ spurious warning. Acceptable for a warning-only surface, and
  `Delegation:SerialiseSharedWriters` already makes it rare.
- Report names no paths ⇒ no check; header stays `unattributable`. Every observed instance named
  its file (that is what a plan report is), so the gap is theoretical.

### Fixes at the source — S2 (both, both one-liners in effect)

- **S2a — bundle wording.** Extend the COMMIT AND PUSH bullet in `server/Bundles/delegate-basics.md`
  with a sentence aimed squarely at the measured failure: *"This instruction IS the explicit
  request: committing and pushing what you changed is part of the task itself, never a 'next step'
  to offer in your report — there is no user at the other end to accept the offer, and a report
  naming an uncommitted file is flagged at settlement."* Content-hash versioning ships it to every
  future launch automatically. (`InstructionBundleTests` pins bundle presence, not wording — check
  whether any assertion needs the new text.)
- **S2b — brief-level line, the channel Codex actually obeys.** In the server-composed brief footer
  (the "how to report back" block `DelegationReportFormatter` appends), add one line for
  Shared-workspace, non-ReadOnly, non-Check tasks: *"When finished: git add the files you changed,
  commit with the real outcome in the message, and push, before your final report."* Kind-agnostic
  on purpose — it is true for every kind, Claude already complies, and keying instruction text on
  AgentKind would rot as CLIs update. The 2/2-vs-0/4 evidence says the user-message channel is the
  one that moves Codex.

### Deferred — S3 (separate card candidate)

Map Codex `FileChange` rollout items into normalized tool records so `AgentFilesService` /
ObservedScope / scope drift work for Codex at all. Useful independent of this bug; not needed for
S1/S2, and the v1 "deliberately not mapped" decision deserves its own revisit rather than a rider
here.

## 4. Q3 — merge with CARD-0215? No. Separate cards, separate fixes

They share only the symptom "plan doc absent from master":

| | CARD-0215 | CARD-0261 |
|---|---|---|
| A commit happens | **Yes** — recoverable via `git show <sha>:<path>` every time | **No** — file sits untracked |
| Workspace | Worktree (task branches) | Shared (branchless) |
| Kind correlation | Claude observed (9016a3b1 was Claude) | Codex, 4/4 |
| Mechanism | Build dispatch's branch not based on the plan commit's ancestry (still to be traced; happens even on clean single attempts) | Codex base-instruction default "don't commit, offer it" beating the bundle rule |
| Fix lives in | `delegate.ps1` branch basing / merge-back ancestry verification | Bundle + brief wording (S2), settlement dirt check (S1) |

One deliberate seam rather than a merge: S1's primitive ("do the report's claimed files actually
exist, clean, where the caller will merge from?") has a natural CARD-0215 sibling at **merge-back**
time (is the plan commit an ancestor of what just merged?). If CARD-0215's investigation lands on
an orchestrator-side verification, it can reuse S1's shape — but coupling the cards now would hold
the cheap fix hostage to the unfinished ancestry trace.

## 5. Suggested slices

- **S1** — Shared settlement check in `TryDescribeGitAsync`: report-path extraction (new small
  pure helper + tests for both path shapes), scoped `git status --porcelain`, header/event/warning.
- **S2a** — delegate-basics.md commit-bullet amendment.
- **S2b** — brief footer line for Shared write-role tasks.
- **S3** (defer / new card) — Codex FileChange normalization.

S2a+S2b are prevention, S1 is the tripwire that tells us whether prevention worked — S1's
`git=uncommitted` rate per kind is the success metric. All three are independently landable.
