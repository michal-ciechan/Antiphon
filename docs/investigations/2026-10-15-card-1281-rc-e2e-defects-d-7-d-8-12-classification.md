# RC E2E defects D-7 and D-8..D-12 classification

Investigation task c2c02264; CARD-1281 release-candidate pilot (no separate defect card supplied).
Started 2026-10-15 UTC. Status: investigation in progress; no fix implemented.

Candidate: 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
Baseline: cc8798c1fa4f3c2faa834283aef080d8e5fd28f0.
Original report: 84fb9b546bc2966159feb8812fab426c205be668,
docs/investigations/2026-10-15-release-candidate-defects.md and accompanying release-candidate note.
Raw evidence: /work/evidence/task-c2c02264/raw/.

The requested six cases will run separately at each revision with isolated fixture-owned
Postgres and random-port session runner, through the host build-slot gate.
Candidate worktree is the assigned branch (this documentation-only delta); baseline
is a detached worktree at /work/worktrees/task-c2c02264-baseline.
D-8..D-12 compare JSON contracts, not pixels; no browser renderer is involved in these cases.
Root-cause/classification results pending.
