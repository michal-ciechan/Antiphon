# CARD-1836 S6b AD-1 checkpoints

Code/landing owner 460fa431. Base c6565bc95776c93a01109385d25420d19372afe8. Branch feat/card-task-460fa431. Worktree /work/worktrees/ws-05.

Shadow chunk Passed is never weaker than the real results check on the same inputs: executed counts, failed counts, the expected-skip roster, coverageComplete, and the summary parse. Unreadable input fails closed. Recording stays shadow: effectiveMode Full, gateCredit false, coverageComplete false on the envelope, bindingState diagnostic-only, publish false. Nothing new is wired. S6c and S6d stay untouched.

AD-2 through AD-6 are left. The follow-up review text that would name those ids is not in this tree. The original S6b advisories that remain are decisions or more than a few independent lines.

## Verification design

The invariant is one direction: a shadow Passed is a result the real check would also pass. Exit other than 0, a drain timeout, a missing file, and an unparseable file keep the existing reasons exit-N, drain-timeout, results-missing, and results-unparseable. A present summary whose outcome is not Completed is not Passed. A missing failed or executed counter is unreadable, not zero. Count disagreement is NotRun. A caller coverageComplete of false cannot be overridden by a passing row verdict, and a caller value of true cannot override a failing verdict.

V-12 from the S6b follow-up stays. This slice adds ShadowPassedIsAtLeastTheRealCheck and tightens ChunkOutcomeNeedsResults so an empty TestRun and `{"ok":true}` are no longer Passed. PC-V12-join, PC-V12-inputs, PC-V12-interrupt, and PC-V12-once stay pending for SourceLanding Mutation. No new positive control is commissioned here. Mutation is waived for this Merge (operator 2026-11-12). The Unit lane is nightly/rc.

Windows is UNOBSERVED. The Linux task does not restart.

## Performance budget

CP-1 includes the real nightly-run witness OneEvidenceRootPerRun, about one minute per run after the nightly core is cached, three runs in that witness, plus the new results-check witness. The first row EstimatedMinutes is 15. Later rows are separate builds. Serial execution: do not overlap these rows. The derived-impact driver is built before CP-3 because that row runs scripts/regenerate-derived-impact-index.ps1 -NoBuild.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S6b-ad1 | `tests/Antiphon.Tests -> bin-c1836-ad1/` | rc-run-evidence | `/*/*/RcRunEvidenceTests/*` | shadow Passed strictness, existing run-evidence witnesses | all listed, 0 failed/skipped | 9 | 15 |
| CP-2 | S6b-ad1 | `tests/Antiphon.Tests -> bin-c1836-ad1-policy/` | policy | `/*/*/TestClassificationPolicyTests/*` | classification policy once | all listed, 0 failed/skipped | 42 | 10 |
| CP-3 | S6b-ad1 | `tests/Antiphon.Tests -> bin-c1836-ad1-impact/` | derived-impact | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | committed derived-impact index | all listed, 0 failed/skipped | 27 | 15 |

### Restart

Restart target: none. `scripts/lib/nightly-tests-impl.ps1` and `scripts/lib/rc-run-evidence.ps1` are on the startup census. A later rc pick owes the Windows startup probe. This Linux task does not run it.
