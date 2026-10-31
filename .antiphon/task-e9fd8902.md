# CARD-1609 continuation 11

Assigned baseline 8e1dea3e6931ef96b98c88ceb8e9a3b8bdda4f53; branch feat/card-task-e9fd8902. S5 remains incomplete. Production deletes remain disabled; first production action is preview-only.

## Long-Apply admission red

New assertions were committed and pushed before repair at 445a22abe88002218c8c9f870b39621fdac563ff. Existing immutable-receipt expiry, future skew and unsupported schema/policy controls remain unchanged. The added real host Apply advances wall clock during the actual audit; it reaches the intended assertion red because expiry is checked after the scan. No fixture/build error or delete occurred.

The unchanged product at the assigned baseline plus the new witness executed 1/0/1/0; method46.909s, compiler210.06s, scheduler build274.273s, row58.213s. Granted slots: build0s wait,row30s. Run wall401.038s, charged371.030s. Terminal wait exit1, clean source and producing build verified.

```text
CHECKPOINT ADMIT-RED commit=445a22abe88002218c8c9f870b39621fdac563ff build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementReceiptTests/Freshness_and_policy_version_are_rechecked executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-054440-4134/rows/ADMIT-RED/run.trx slot=granted waited=30s dirty=0 source=445a22abe88002218c8c9f870b39621fdac563ff sourceState=clean buildSource=verified
```

Expected0, actual2, diagnosis RecycleGitAuditUnknown reason=preview-required. The test does not claim long authenticated-origin renewal: that remains separate bridge/coordinator work. The second added control (expired before any expensive audit) was not reached in this red run.

Tool bootstrap was leased,5 warnings/0 errors,8.85s compiler/10s held. Gated plan import succeeded16 rows,45s admission/5s held, with the expected CP-3 importer-default and CP-11 duplicate cross-host-build warnings. Imported defaults are not execution budgets. Focused red uses4m row/12m charged/57m wall bounds, fresh bin-c1609-11-admit-red, UseAppHost=false,TEMP=/tmp. Four-minute bound is provisional collection, not measured p95. Full phases live at /tmp/c1609-11-admit-red-phases; unrecorded internal phases remain unknown.

## Outstanding ordered scope

Complete long-scan/manual/v2 acceptance (including publication renewal and single-owner exchange), then fresh full CP-15, then the whole closed Linux roster and portable Linux roster/docs checks. Windows CP-11/12/13 and production preview/audit remain owed. Do not add focused passing counts to historical red to claim a fresh row.
