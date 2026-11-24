---
name: antiphon-cli-upgrade
description: Upgrade an agent CLI (Codex first, then Claude Code or Grok) so new models are picked up - capture the new TUI screens, run the tests, then roll. Use when a new agent CLI or model version is available, for a model ladder change, or for a CLI bump on server2 or the desktop.
---

# Antiphon CLI upgrade

Run commands from the repository root. The runbook with every command, file and commit is
[cli-upgrade](../../../docs/cli-upgrade.md); this skill is the judgment layer. Rollout mechanics
stay with [docker-stack](../../../docs/docker-stack.md#staged-server2-rolling-rollout-card-0934),
dispatch with [antiphon-orchestrator](../antiphon-orchestrator/SKILL.md) and
[antiphon-delegate](../antiphon-delegate/SKILL.md), kinds and the ladder with
[agent-kinds](../../../docs/agent-kinds.md).

## The rule

**Upgrade the CLI, then run the tests.** Operator, 2026-11-21. Every model or CLI change starts
there, and the routing change comes last. Do not restart anything to "see if the new CLI works".

The codex-cli 0.170.0 / GPT-6.2 rollout (2026-11-20/21) found three bugs one restart at a time:
the ready gate misread a new last row, pointer evidence misread a chip-only last row once text
was typed, and a derived (not measured) fixture stayed green over it. All three were on the
first real screen of the new CLI.

## Order

1. **Find the version and what it unlocks.** `npm view @openai/codex version`, the vendor
   changelog, the first CLI whose catalogue lists the slug. List every repo place that names a
   model (runbook step 0 table): `ModelLevelAliases.cs`, `ModelAlias.cs`, `AgentTuiRunnerCatalog.cs`,
   holds, `GET /api/complexity-chains`, `docs/agent-kinds.md`, the Dockerfile pins and their
   contract tests, the desktop install.
2. **Throwaway environment, real screens.** New CLI in a scratch prefix, a logged-in throwaway
   home, production argv, 120x30, uid 1654. Capture idle, text typed, a wrapped boot pointer, a
   warning chip, trust, sign-in, update or model-migration, running, done. Save as fixtures with
   the version in the name plus provenance. Never use the shared `/state/codex` auth and never
   copy `~/.codex`.
3. **Tests first.** Commit the red test, then the fix. Readiness and composer classes
   (`CodexStartupReadinessTests`, `CodexV0160StartupTests`, `CodexAccountBootstrapTests`,
   `CodexReadyTrackerTests`, `CodexReadyWaitTests`) on the measured captures; alias, ladder and
   image-contract classes. Run the real-CLI boot pointer canary against the candidate image
   binary using the command in [Step 2](../../../docs/cli-upgrade.md#step-2-run-the-tests-first).
   Checkpoint tool or build-slot gate.
4. **Land, then rc.** Reviewed to master, then ported or cut onto the rc. The classifier and the
   ladder run in the server; the image carries only the CLI.
5. **Image before AppHost.** Staged server2 phases one at a time, smoke `codex --version` as uid
   1654 on temp and on old; then the desktop `npm install -g`. `MinimumCodexCliVersion` is not
   enforced at launch, so an AppHost on the new ladder against an old CLI fails every launch.
6. **AppHost restart** onto the rc tip with `-ExpectedServerSha`.
7. **Real turns.** Ladder canary (`--kind Codex --level High`, no `--model`), explicit canary
   (`--model <slug>`), a real-work task with a checkable answer; confirm the slug in the rollout
   file, `requestedModelAlias`, a `UserPrompt` in the transcript, and the 422 probes.
8. **Routing last.** Ladder cells and holds, recorded with the operator date.

## Judgment calls

- A screen you derived by editing another capture is not evidence. Say "derived" in the test
  comment and get the measured one before calling the reader fixed.
- Require one passed boot pointer canary in the fresh candidate-image TRX. Record the
  version, model slug and screen evidence path before rollout.
- When a canary fails, read before you restart: incidents (`GET /api/agents/<agentId>/incidents`),
  runner log `Phone-home Launch` then `KillGeneration`, then the buffer replayed at 120x30. Each
  0.170 failure was diagnosable read-only from those three.
- Do not widen the ready timeout or loosen a footer predicate to get past a new row. Step off the
  exact new row, keep the previous version's fixture green, and probe adversarial grids.
- Keep dispatching during the rollout. One of server2 and server2-temp is always accepting.
- The rollout gotchas recur: a stale NeedsResolution land on server2 blocks `redeploy-old`
  (cancel the stale owner, rerun); `retire-temp` refuses on an unclosed journal (Close, fix series
  `328eafa3d`) or a nested scratch worktree (CARD-2043, one fresh preview and apply, then Debug).
- Claude Code rides family aliases, so a new Claude model can arrive with a CLI bump alone; Grok is
  one id at every tier and its Windows CLI auto-updates. Both have less version-specific test
  coverage than Codex; the runbook lists what exists and what is missing.

## Never

- `-Phase all`, `-RecycleVolumes`, `-AllowWorktree`, `restart-session-runner --kill-sessions`, Docker prune.
- An AppHost restart before the runners carry the CLI the new ladder needs.
- `git pull` in `C:\src\Antiphon` before an AppHost restart: hosts track the rc tip, not master.
- Reading or printing `auth.json`, a token, or a secret. Session rollout files are readable; auth
  files are not.

## Paste-ready checklist

The checklist is at the end of [cli-upgrade](../../../docs/cli-upgrade.md#checklist-paste-into-a-tracking-note).
Copy it into the tracking card or the status note, fill the counts and SHAs, and keep the
rollback line filled before step 5.
