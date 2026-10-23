# CARD-1572 Unit census continuation

Investigation in progress at baseline e4928494298b304507fdf2e520445c98e7219211. The six inherited repairs are retained unchanged.

The operator authorized Debug+Fix, bounded selections, no Review, and no rc publication. One shared isolated build will cover the three unrun groups (141 classes, 1,710 intended cases). Interrupted shell and remote selections will use method pieces with hard collection ceilings of 620 seconds; these are provisional diagnostic caps, not measured p95. No existing test timeout changes are authorized.

Generated run manifests, exact selectors, source and assembly hashes, TRX, per-case durations and deadline diagnostics are retained under /work/worktrees/task-fb7c4a41/.antiphon/1572-cont. Build/slot admission and test execution are reported separately. Uninstrumented startup, readiness, data, database, IO/HTTP/CLI and teardown subphases remain unknown, never zero. No full-lane green is asserted.

## New inherited red witness

At d39433fc2 (documentation-only descendant of the commissioned baseline), unit-08 hit its 620.302s collection deadline: native diagnostics retain 537 passes, 15 independent `CheckpointShapeConflictTests.shape_conflict_rule` assertion failures, one deadline-contaminated `C994_Absent_cleanup_is_idempotent` failure (exit 143), and 17 cases without terminal events. The fifteen failures occurred well before SIGTERM and all report `receipt-invalid: negative qualification mismatch`. Raw evidence: `.antiphon/1572-cont/unit-08`; no complete TRX exists for this group.

Cause: fixture drift after 3a7ed451668051b3627b8802ed9005e7bf3db327. CARD-1370 made `ReportValidator.Validate` refuse non-green reports as `report_failed` before source eligibility. The fixture still expects `report_source_ineligible`. Production detects tampering and retains user bytes/index; the expectation needs the new report refusal plus a separate source-ineligibility assertion. This witness precedes the fix; no production change or timeout increase is intended.

Unit-09 completed in 538.379s: 565 pass, zero fail, five platform skips, all 45 classes. Unit-10 completed in 201.601s; final TRX reconciliation pending. Both used the unchanged shared build.
