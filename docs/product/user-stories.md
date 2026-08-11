# Antiphon user stories — the loops worth optimising for

**Status:** stories only. Each one is a card; the card is where the UX gets scoped.
**Date:** 2026-08-11.

## Why this document

Antiphon has grown surface-first: `/agents`, `/orchestrator`, `/channels`, `/board`, the home
workspace (feature 008), the proposed Tasks rail (feature 010). Each was designed against a
capability. None was designed against *a person arriving with an intent*.

The result is that common intents cross three or four surfaces, and some have no home at all. These
stories name the intents. They are deliberately written as **what someone is trying to find out or
get done**, not as features — the UX scoping happens per card, against the story.

Two rules for anyone scoping one of these:

1. **Read the code before the design.** Several of these look unbuilt and are half-built (008 is
   shipped, 010 is designed, project creation exists as an API and a settings form). Feature 010's
   proposal is the model: a "what exists today, verified against the code" section first.
2. **Say what the surface must NOT do.** Every one of these competes for the same screen. A story
   that only adds is a story that makes the others worse.

---

## S1 — "What is happening on this project right now?"

**As** someone returning to a project (start of day, after a meeting, after a night),
**I want** one screen that tells me the state of the work,
**so that** I can decide what to do next without opening four pages and reconstructing it.

The screen has to answer, in this order — this is the order of urgency, and it should be the
reading order:

1. **What needs me?** Blocked delegates that asked a question and are waiting on an answer;
   agents whose sessions failed; incidents raised. Nothing else matters if something is stalled on
   me.
2. **What is being worked on?** Which agents are live, what each is doing, how long it has been
   doing it, and whether it is actually progressing or merely "Running".
3. **What is waiting for review?** Specs, plans and diffs an agent has produced and parked. This
   is work already paid for that yields nothing until someone reads it.
4. **What is queued?** What will start next, and why it has not started (concurrency cap, scope
   lease, budget ceiling).
5. **What finished?** Completed cards and tasks, recently, with what they produced.

**Why it is hard.** Cards and delegated AgentTasks are two disjoint record systems with separate
state machines (see feature 010 §1). "Needs me" spans blocked tasks, agent incidents and review
threads, which live in three places. And "is it progressing" is not the same question as its
status field — a task read `Dispatched` for nine hours today while its session had long since
finished.

**Related:** feature 008 (shipped home workspace), feature 010 (Tasks rail design, CARD-0002).

---

## S2 — "Set up a new project"

**As** someone starting work in a new repo,
**I want** to create a project and get it to the point where I can hand it work,
**so that** I am not assembling configuration by hand before anything can run.

A project is not just a name. To be useful it needs: a working directory, at least one agent and
its definition/model tier, delegation settings (allowed roots, concurrency, budget ceiling), a
board with columns, optionally a workflow, and optionally a channel binding.

Today `POST /api/projects` exists and `ProjectConfig.tsx` edits settings, but there is no guided
path from "I have a repo" to "an agent is working in it". The failure mode is silent: a project
that looks created but cannot dispatch, because a setting nobody surfaced is empty.

**The story is done when** someone can go from a directory path to a first successfully dispatched
task without reading the source or hand-editing config, and anything still missing is *stated on
screen* rather than discovered when a task fails.

**Watch out for:** `Delegation:AllowedRoots` is a security boundary — a setup flow must not
encourage widening it casually. Defaults that are safe but non-obvious (empty = caller's own tree)
need explaining at the point of choice.

---

## S3 — "An agent is asking me something — answer it and move on"

**As** the person a delegate is blocked on,
**I want** to see the question with enough context to answer it, and answer it in place,
**so that** the work resumes without me reconstructing what it was doing.

This is separated from S1 deliberately: S1 is *noticing*, this is *acting*. The action is cheap and
frequent, and it is the single highest-value interaction in the system — a blocked delegate is
burning nothing and delivering nothing until answered.

Needs: the question, the task's goal, what the delegate has done so far, and a reply box. Answering
should not require opening the session transcript and reading backwards.

**Note:** `AgentTaskReplyService.AnswerAsync` and `TaskDrawer` already exist. This story is largely
about *surfacing* and *latency to answer*, not new machinery.

---

## S4 — "Review what an agent produced and react to it"

**As** someone with a spec, plan or diff waiting,
**I want** to read it and respond — accept, comment, or hand back a change,
**so that** reviewing is part of the loop rather than a detour out of it.

The reading surface exists and is good (`FilesReviewPanel`, rendered markdown, review marks,
baselines — features 008 and 009). The gaps are around it: knowing something is waiting (S1),
finding it without knowing which agent made it, and turning a reaction into work without leaving
the page.

**The measure:** selecting a passage and saying "change this" should produce a delegated task with
the passage as context, in one gesture.

---

## S5 — "Something is stuck — find out what and unstick it"

**As** someone who notices work is not moving,
**I want** to see what state it is really in and what went wrong,
**so that** I can retry, escalate, re-dispatch or fix it.

This is the diagnostic loop, and it is currently the weakest. Today it means reading 100 MB server
logs, querying the tasks API by hand, and diffing transcripts. Everything learned today came out of
that process, none of it from a screen.

Must distinguish, because the responses differ: *never started* (brief undelivered), *working but
uncorrelated* (doing real work that will never settle), *finished but not reported*, *dead session*,
and *genuinely slow*. A status field that says `Dispatched` for all five is the problem.

**Related:** CARD-0003, CARD-0020, CARD-0021, CARD-0029. The incidents and the watchdog now exist;
the *screen* does not.

---

## S6 — "Catch up on what happened while I was away"

**As** someone returning after hours away, often on a phone,
**I want** a digest of what changed — finished, failed, blocked, spent,
**so that** I can triage from anywhere and only open the laptop for what needs it.

Distinct from S1: S1 is a live state view, this is a *delta over a period*, and its primary surface
may be Telegram rather than the web app. Cost belongs here — spend per root task is tracked and
currently invisible until someone reads a report.

---

## Deliberately not stories yet

- **Multi-user / handoff.** No auth, no principals; `EditedBy` is honest free text (CARD-0019).
  Anything assuming "who did this" is premature.
- **Cross-project rollup.** Every story above is scoped to one project. A portfolio view is a
  different product until the single-project loops are good.
