# CARD-1299 implementation evidence

Original Code/landing owner: 095a7f1a-4260-43dc-b1dd-f0505eb03695.
Branch: feat/card-task-095a7f1a; worktree: /work/worktrees/task-095a7f1a.
Base: 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
Plan: [closed selection](../plans/2026-10-15-card-1299-plain-echo-plan.md).

The exact reviewer witness failed before the production fix: one executed result,
one assertion failure, no skips. Actual response was `> Run abc?\nDone.` instead
of `Done.`. Fresh TRX confirms the intended class and method. Source was clean,
build provenance verified, and ownership stayed bound; no unbound fallback used.

```text
CHECKPOINT CP-1 commit=ca8e61f0c7baf4fc0a314119b3623dbc5a8d373a build=ok filter=/*/*/CodexResponseAnalyzerTests/Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215133-5a8f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=ca8e61f0c7baf4fc0a314119b3623dbc5a8d373a sourceState=clean buildSource=verified
```

Red run: `.antiphon/checkpoints/20261015-215133-5a8f/report.json` and sibling
report.md, TRX and build log. Build 201.247s; test host 3.319s; total 3m38s.
The report lists two manifest builds but only bin-c1299-red ran; the final build
was unused. The leased launcher bootstrap was the sole unlisted build, required
to obtain the checkpoint executable: 11.08s MSBuild wall, 2 warnings, 0 errors,
slot=granted waited=0s, lease hold 13s. No unlisted tests.

S2 prioritizes the plain echo match for opted-in Codex callers and searches the
cursor-separated text only on a plain miss. The separated response is retained
when neither form matches. Final CP-3/6/7/8 results are pending this commit.
The brief explicitly excludes whole Unit; no aggregate or Windows qualification
is claimed. PC-1 remains pending for SourceLanding Mutation, as do parent
CARD-0907 PC-1..PC-12 and D1-C1..D1-C4 variants. Restart after activation: server.
