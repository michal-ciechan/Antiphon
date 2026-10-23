# CARD-1572 Unit census continuation

Investigation in progress at baseline e4928494298b304507fdf2e520445c98e7219211. The six inherited repairs are retained unchanged.

The operator authorized Debug+Fix, bounded selections, no Review, and no rc publication. One shared isolated build will cover the three unrun groups (141 classes, 1,710 intended cases). Interrupted shell and remote selections will use method pieces with hard collection ceilings of 620 seconds; these are provisional diagnostic caps, not measured p95. No existing test timeout changes are authorized.

Generated run manifests, exact selectors, source and assembly hashes, TRX, per-case durations and deadline diagnostics are retained under /work/worktrees/task-fb7c4a41/.antiphon/1572-cont. Build/slot admission and test execution are reported separately. Uninstrumented startup, readiness, data, database, IO/HTTP/CLI and teardown subphases remain unknown, never zero. No full-lane green is asserted.
