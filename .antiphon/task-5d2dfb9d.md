# CARD-1613 plan round 3

Revised plan and release maintenance procedure are committed and pushed; next stage is Code.
No product/test implementation changed. Review b51c62ad's blocking D-1 and advisories A-1
through A-4 are incorporated; previously confirmed decisions/checkpoints remain.

## Deliverables

- `/work/worktrees/task-5d2dfb9d/docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md`
- `/work/worktrees/task-5d2dfb9d/docs/release-gates.md` (CARD-1613 maintenance subsection only)

Whole-source comparison is an explicitly invoked one-time S2 audit with ignored inputs/receipt.
The permanent Unit witness checks compiled marker identity and frozen migrated reasons, without
live-source digests or Roslyn re-extraction. New marked exclusions need no historical baseline
entry; deliberate reason/identity changes have a same-commit reviewed maintenance procedure.
R-14 includes an unrelated portable-body edit remaining green. The separate small migration
tool and its pure synthetic-fixture API add no TUnit parent results or smaller-suite Roslyn
dependencies. Its five-minute provisional budget is explicit: 29 final-green + 5 RED-1 + 5 audit
= 39 minutes across four separate operations, each under 30 minutes.

The plan covers constant helper-argument propagation, refuses unproved caught/deferred skips,
records potentially suppressed pre-gate/cleanup failures, and acknowledges HostStats' changed
macOS text. No fabricated Windows outcomes or measured p95 are claimed.

Code must start from `origin/rc/2026-10-27` at/after
`8edda999623a473be2d1970727c172bfb5088d59`, copy the final plan and maintenance subsection from
this task branch in its own docs commit, then implement S1-S3. Master port is a separate task
after rc. All six carriers are rc ancestors and none is on master. The review's statement
that rc has no plan docs was too broad: this CARD-1613 plan is absent, historical plans exist.

## Verification and limits

Validated plan source: `4fbbf412a0ac900255562f6b290892b087b8c574`.

- Gated checkpoint-tool bootstrap: 0 errors, 5 warnings, 5.16 s; no TUnit tests.
- Gated final importer: exit 0, 13 rows; only expected CP-9 separate Windows build warning.
- Independent table scan: 80 parent results, 29 minutes (22 Linux / 7 Windows).
- No suffix collisions across 10 distinct class operands / 8 method operands, including
  linked Shared declarations. Three proposed NativeLinuxPolicy names require fresh Code TRX.
- Source review confirms the parameter-folding and pre/post-gate examples. Git confirms
  9 changed inventory files out of 46 since the old base.
- GET runner-defaults and session-runners: read at 17:55 UTC; revision 2, eligible Linux and
  Windows lanes. No fleet address is embedded in the plan.
- Evidence check base cb7fc67c..validated source: 4 commits, 0 violations.
- Fresh master merge-tree exits 1 with the same 25 conflicting paths as the task base;
  inherited rc/master history prevents direct whole-branch master landing. No merge performed.
- No desktop files, native Windows run, tags, target publication, rebase or force push.

Essential importer provenance:

```text
BUILD SLOT granted lease=a7a1679c-943f-41fd-822c-9d9bb7dea342 waited=0s maxcpucount=6
BUILD SLOT released lease=a7a1679c-943f-41fd-822c-9d9bb7dea342 held=6s
BUILD SLOT granted lease=a63539c1-f728-439b-a396-a3f84614d8a3 waited=0s maxcpucount=6
imported 13 rows -> .antiphon/c1613-r3/final-manifest.yaml
BUILD SLOT released lease=a63539c1-f728-439b-a396-a3f84614d8a3 held=2s
```

## Pushed slices

- `a2164fa380321783f35904e5afbf162b1d507c53` — audit boundary and maintenance procedure.
- `66f32be86259e471faee988361a310861990edc8` — explicit rc base, plan transfer and audit budget.
- `c0cf6c068d49631e7933f609154f884ce422b693` — extraction rules and outcome limits.
- `4fbbf412a0ac900255562f6b290892b087b8c574` — baseline roster and source ground truth.

- `aab6231427566e09fa21a67ae28e0773b3480420` — final importer and validation record in the plan.

The final report supplies this report commit's full SHA and remote-tip proof.
Standing task-branch commit/push authority was sufficient; no additional approval was needed.

--- next stage ---
next: code
handoff: Base origin/rc/2026-10-27 at/after 8edda999623a473be2d1970727c172bfb5088d59; copy the final plan and maintenance subsection in a docs commit, then execute S1-S3 with the separate one-time audit and unchanged 13-row manifest. Master port follows separately.
artifact: docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md
