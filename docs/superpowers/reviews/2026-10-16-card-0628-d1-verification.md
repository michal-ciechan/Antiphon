# CARD-0628 D-1 verification at current master

Code task `ef29e094-b623-49e4-9947-07e9016b5ad3`, branch
`feat/card-task-ef29e094`, worktree `/work/worktrees/task-ef29e094`.
Task base: `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`.

The requested production repair and named regression assertions are already ancestors
of the assigned base in `962075470e4d0dcfcb890c58f331b046abf4b48d`
(`fix(CARD-1176): honour CLAUDE_CONFIG_DIR when seeding the dind Claude store`).
Step 2c resolves `${CLAUDE_CONFIG_DIR:-/state/claude}`, creates the directory as
root with `install -d -o "$APP_UID" -g "$APP_GID"`, and passes the resolved path
to the seed after dropping privileges. `APP_UID` and `APP_GID` are both 1654.
`Entrypoint_seeds_claude_onboarding_before_dockerd` already pins those lines.
No production or test edit is needed to implement this brief. No pre-fix red or
deliberate mutation is claimed by this task; Mutation owns deliberate controls.

Verification follows the brief's explicit narrow scope (no whole Unit lane):

- The original plan's CP-1, with its exact three-class filter, through the
  checkpoint tool at the committed task HEAD. This includes the named guard and
  the full affected `DindRunnerContractTests` and adjacent image/harness classes.
- The original plan's CP-5 through `scripts/build-slot.ps1`, as explicitly
  requested. The assigned host offers Linux nested Docker rather than Docker
  Desktop; retain the actual host and outcome without claiming Windows proof.
- A single checkpoint-tool bootstrap build through the build-slot wrapper is
  an additional tooling build, required because this worktree has no built tool.

Original plan:
`docs/superpowers/plans/2026-09-23-card-0628-claude-on-server2-runner-plan.md`.
CP-1 covers R-3 and the contract portion of V-5; CP-5 covers V-2, V-3 and V-4.
Other original ordinary rows are outside this narrowly assigned repair and receive
no pass credit here. All PC-1 through PC-12, including every named variant, remain
pending for method-scoped SourceLanding Mutation after ordinary Review and caller
landing of the original Code task. Mutation also owns missing-control discovery.

The stored final task report supplies fresh receipts, exact tested SHA, counts,
unedited CHECKPOINT lines, CP-5 result and any remaining verification gap. This
file records the source and selection before execution; it is not a green receipt.
No production restart is required by this evidence-only task.
