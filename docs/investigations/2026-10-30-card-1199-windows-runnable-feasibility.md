# CARD-1199 native Windows feasibility

Investigation in progress, 2026-10-30. Task `6a38c2dd`, branch
`feat/card-task-6a38c2dd`, product/test base
`ade54222543381e2e1b31ad79d8ae33a4035f70b`.

## Evidence established before execution

The requested census is 17 ShapeConflict rows, 13 TrackedBaseline rows and five
Linux-gated OutputConfinement rows. The sixth OutputConfinement row is an existing
Windows control and is accounted separately. All three gates are historical
qualification gates, not evidence that all their arrangements require Linux.

The Antiphon board read (`scripts/card.ps1 get CARD-1199 -Board Antiphon`) returned
card `3a994d16-4fa0-4e03-b85f-18806ba7ad1a`, revision count 33, Done, with native
Windows R-2 still disclosed. The prior policy report is
`origin/feat/card-task-93cbfd8a:.antiphon/task-93cbfd8a.md`.

ShapeConflict fixtures include POSIX shell commands. TrackedBaseline and
OutputConfinement predominantly arrange ordinary Git/MSBuild operations. Current
Windows product branches and per-row execution results remain under inspection.

## Execution custody

Only this assigned worktree is writable. Temporary gate bypasses and generated
evidence stay local and are not deliverables. Each build/test driver uses
`scripts/build-slot.ps1`; test outputs use `bin-c1199win6a/`. Test temp roots are
redirected to this worktree's ignored `.antiphon/probe-6a38c2dd/temp` directory.
No AppHost/runner restart or source change is authorized or needed.

## Remaining evidence

First native class completed: **ShapeConflict 17 total, 7 passed, 10 failed,
0 skipped**, 12m51.773s. Build: 0 errors, 1,380 warnings, 9m56.13s. The failures
are the ten shell-driven arrangements; Windows cannot resolve their POSIX
commands. The existing directory-symlink refusal case passed unchanged.
Raw evidence: `.antiphon/probe-6a38c2dd/shape-native/results.trx`,
`shape-native.log`, `shape-rows.json`; all are local ignored artifacts. This is
an investigative build with three local gate edits, not clean-source release
qualification. The product source remains the base SHA above.

Unedited console outcome:

```text
  total: 17
  failed: 10
  succeeded: 7
  skipped: 0
  duration: 12m 51s 773ms
BUILD SLOT released lease=30108bdc-f780-426b-ba0b-62417fc8639b held=778s
```

Native executions, row-level outcomes, platform code references, exact failure
mechanisms, timing, restoration and final publication are pending. This skeleton
does not claim verification or a confirmed product gap.

## Not done, noted

Implementation and fix design belong to a subsequent stage; this artifact records evidence only.
