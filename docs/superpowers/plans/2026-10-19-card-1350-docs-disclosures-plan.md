# CARD-1350 docs follow-up: release lessons disclosures

Docs task 483f1c1b on `feat/card-task-483f1c1b`, base
53f5ad89e9141aa7a781437d123a18bc0f13bf1c. Review 1e280329 of the release-lessons
commit was clean but disclosed three issues; this plan binds the one verification
row for the single commit that fixes them.

## Slices

- S1: bring the eleven `docs/investigations/2026-10-19-*.md` files linked from
  `docker-stack.md`, `release-gates.md`, `testing-and-build.md` and
  `orchestration-loop.md` onto this branch from their `feat/card-task-*` tips;
  reword the Merge-role paragraph in `release-gates.md` so it says the flow starts
  at the target tip and no longer claims a launch-contract refusal; retire the
  stale "SDK remains 10.0.401, selected by global.json" sentence in
  `docker-stack.md` while keeping the 10.0.402 paragraph.

## Verification design

- V-1: the documentation guards that read the three edited owner docs still pass:
  `ReleaseBranchModelDocumentationTests` (release-gates.md),
  `DockerStackDocumentationTests` (docker-stack.md, testing-and-build.md,
  orchestration-loop.md) and `CheckpointManifestDocumentationTests`
  (testing-and-build.md, orchestration-loop.md). Together they hold 24 test
  methods, all `Category("Unit")`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1350docs/` | docs-guards | `/*/*/(ReleaseBranchModelDocumentationTests*)\|(DockerStackDocumentationTests*)\|(CheckpointManifestDocumentationTests*)/*` | V-1 | all 24 methods, 0 failed/skipped | 24 | 12 |

### Cost

Ordinary Code floor: 12 minutes (the one row, build included).
