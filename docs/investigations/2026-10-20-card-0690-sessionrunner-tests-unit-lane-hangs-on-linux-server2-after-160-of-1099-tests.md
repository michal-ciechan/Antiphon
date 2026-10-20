# CARD-0690 / CARD-0821: Linux SessionRunner test stall

Status: investigation in progress; no implementation changes.

## Scope and source

- Task `3860c88d`, branch `feat/card-task-3860c88d`, Linux runner mirror.
- Assigned base and freshly fetched `origin/master` at start:
  `cff9b9bdadab0285d748be3d725bd76a406450d0`.
- Both cards were read in full with `scripts/card.ps1 get ... -Board Antiphon`.
  CARD-0690 row `5da0f188-9a62-4c25-9381-887ff9c821bf`, revision 1;
  CARD-0821 row `18daa512-6c4f-44c2-8be7-407d838a74e2`, revision 1;
  board `8988ca03-7414-47ad-b0b6-51556c701703`.
- Their 2026-10-13 reconciliation assigns the Windows launch-policy and custody
  backend failures to CARD-1212. This investigation does not reopen that cluster.
- CARD-0868 row `9bb3f5d4-d81e-44bd-bb75-4335e44c98cf`, revision 0,
  records prior class-by-class localization at `d07c7e07`: CodexLaunchRefusalTests,
  GrokRulesStoreFailureTests and GrokRulesFileLaunchTests hang independently;
  every other selected Unit class finished.
- The referenced `docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md`
  is absent from this checkout. Card descriptions remain historical observations,
  not a substitute for current test evidence.

## Diagnostic selection

These are bounded rechecks of the localized classes and the additional failure
retained by CARD-0690. They are not a whole-Unit qualification. No repair is
authorized. Run serially through the checkpoint tool, with `--row-timeout 15m`.
The separate slot-gated tool bootstrap is necessary to execute these rows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.SessionRunner.Tests -> bin-c0690-investigate/ | codex-refusal | `/*/*/CodexLaunchRefusalTests/*` | historical Codex stall and oversized-launch failure | all listed, 0 failed | 3 | 3 |
| CP-2 | all | CP-1 | grok-store | `/*/*/GrokRulesStoreFailureTests/*` | historical store-failure stall | all listed, 0 failed | 6 | 1 |
| CP-3 | all | CP-1 | grok-launch | `/*/*/GrokRulesFileLaunchTests/*` | historical file-launch stall | all listed, 0 failed | 21 | 2 |
| CP-4 | all | CP-1 | capabilities | `/*/*/RunnerCapabilitiesTests/*` | daemon reference-closure failure | all listed, 0 failed | 7 | 1 |

## Results

Pending execution and mechanism reconstruction.

## Not done, noted

No fix designed or implemented; a minimal proposal will be recorded only if current evidence warrants it.
