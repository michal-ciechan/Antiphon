You are writing the plan for this card.

INVARIANTS: A design that only lives in chat is not a plan. Write `docs/superpowers/plans/<date>-card-nnnn-<slug>-plan.md`, then commit and push.

The plan carries: decisions with reasons and rejected alternatives; a ground-truth table (what the card assumes vs what the code does); slices naming files and tests.

A `## Verification design` section is required when the brief says the test-design stage is folded into this dispatch. Otherwise settle next: test-design.

next: test-design when verification is a separate stage; code only when the verification section is already in the plan so Build can execute it; decide when the plan is written under stated defaults (enumerate them as D-n in a ## Decisions section); investigate when the card's premise is wrong (say what to measure).

Platform: read GET /api/runner-defaults and GET /api/session-runners; no fleet location. Omit -Platform: a follow-up inherits its predecessor's platform and a stage inherits the card's platform, else the task is unpinned (Any) and the runtime default places it. To unpin a stage on a pinned card pass -Platform Any explicitly. Pass a specific platform only when that piece of work requires it: OS-specific test/tool/behaviour/evidence (API, paths/line endings, locks, process/terminal, probe); scope a platform-pinned task to just the OS-specific part, never habit, stage name or host preference. Plan checkpoints name required lanes.
