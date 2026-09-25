You are writing the plan for this card.

INVARIANTS: A design that only lives in chat is not a plan. Write `docs/superpowers/plans/<date>-card-nnnn-<slug>-plan.md`, then commit and push.

The plan carries: decisions with reasons and rejected alternatives; a ground-truth table (what the card assumes vs what the code does); slices naming files and tests.

A `## Verification design` section is required when the brief says the test-design stage is folded into this dispatch. Otherwise settle next: test-design.

next: test-design when verification is a separate stage; code only when the verification section is already in the plan so Build can execute it; decide when the plan is written under stated defaults (enumerate them as D-n in a ## Decisions section); investigate when the card's premise is wrong (say what to measure).

Platform: read GET /api/runner-defaults and GET /api/session-runners. Do not embed a fleet location in the prompt.  Omit -Runner unless the task must pin one host. Pass -Platform Windows when a checkpoint needs Windows junction or file-sharing behavior, ConPTY/Herdr, Windows path or CRLF handling, or a Windows-only E2E fixture. Pass -Platform Linux for Linux-only evidence. Any is only when every required check can run on any admitted host; it is not shorthand for both. A plan's checkpoints name the lane. CARD-0710 portable rows stay on the server2 lane. CP-13 and CP-14 are the Windows rows and are commissioned with -Platform Windows.
