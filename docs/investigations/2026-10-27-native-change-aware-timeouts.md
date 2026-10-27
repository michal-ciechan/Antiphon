# Native ChangeAwareRelease timeout and PS5.1 obligation

Task b11dd450, 2026-10-27. Investigation in progress; no root cause or green verdict is claimed by this initial checkpoint.

The assigned branch `feat/card-task-b11dd450` started at `61c85fc84472b3d519df69b3fbf8a9ca5bf1ceb1`, a master-line port, not a descendant of the requested rc tip. Fetch confirmed `origin/rc/2026-10-27` at `f3e302eb9925ecbd1607b0fd03e77c7cd1aa2e32`. To preserve the assigned branch and avoid merging/rebasing it, native diagnostics use a task-owned detached checkout at `C:\Antiphon\scratch\investigate-b11dd450` of that exact rc commit. The report remains on the assigned branch. The selection scripts and ChangeAwareReleaseTests have no diff between these two tips.

Evidence root: `C:\Antiphon\worktrees\card-task-b11dd450\.antiphon\native-b11dd450` (ignored). Preflight available memory was 9,398 MiB, above 6,144 MiB. Exactly one gated native build is in progress, using `bin-native-b11dd450/`. Production services have not been operated. Only mandated build-slot acquisition/release is authorized runner interaction.

Source findings pending measurement: `scripts/lib/release-selection.ps1:293` grants the worker 60,000 ms. `scripts/lib/rc-project-selection.ps1:158` reads both commit graphs. Each blob read invokes Git through `scripts/lib/test-selection.ps1:125`; `scripts/lib/CheckpointProcessRunner.cs:59` separately probes Git config before the requested command. Whether repeated process startup consumes the deadline is still a hypothesis.

Not done, noted: no fix designed or implemented.

--- next stage ---
next: investigate
handoff: Complete the gated native class run and repetitions, capture worker timing attribution, and establish the PS5.1 runtime obligation.
artifact: docs/investigations/2026-10-27-native-change-aware-timeouts.md
