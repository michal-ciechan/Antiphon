# CARD-1199: run and gate the 35 excluded Windows checkpoint rows

Date: 2026-10-30. Plan task: `42241722`. Source base:
`fca55bdda2e1616bcae107ba17dab69d8de69452`.

Status: working skeleton; checkpoint roster and verification design are being
verified against source. This revision is not an executable Code handoff.

## Objective

Make the exact 35 rows in the [native feasibility report](../../investigations/2026-10-30-card-1199-windows-runnable-feasibility.md)
execute their assertions on native Windows and contribute to its required release
gate. Preserve Linux behavior and every existing assertion. No product code edit
is justified by the investigation. Native junction and wider R-2 qualification
remain open.

## Decisions

- D-1: sequence gate-only rows (S-A), common Git isolation (S-B), then native
  fixture ports (S-C), subject to source-confirmed parameterized-row selection.
- D-2: isolate fixture Git configuration on both platforms, without changing
  machine settings or weakening the production Git hardening.
- D-3: make exclusion removals explicit and refresh land-graph approval once on
  the final definition tree. Do not run its update for intermediate trees.
- D-4: design verification here for caller-requested Opus plan review, then Code.
  Native Windows evidence is required; Linux evidence cannot substitute for it.

## Ground truth

| Card assumption | Observed source / evidence | Design consequence |
|---|---|---|
| All 35 are inherently Linux-only | 12 pass with gates lifted; 13 more pass with isolated Git configuration | Gates and fixture portability are the demonstrated scope |
| Ten failures imply missing Windows production APIs | Ten arrangements use POSIX shell commands | Port fixtures while retaining the production seam |
| A passing directory symbolic-link row closes junction qualification | The fixture uses a symbolic link, not a junction | Keep R-2 disclosed and request its distinct operator decision |
| This is a cheap extra Full selection | Initial 35-row selection plus its sibling took 35m44s, excluding build | Debug obligation and measured performance budget are mandatory |

## Slices

S-A: 12 gate-only rows, exact exclusion removals and land-graph consequences.

S-B: 13 isolated-Git rows, shared helper and deterministic red-first leak witness.

S-C: ten POSIX fixture rows, per-row Windows port preserving production seams.

## Verification design

Pending source census: exact per-row seam, anti-vacuity witness, checkpoint
selector, whole-minute estimate and lane. Each Code slice must fit 150 minutes.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards).
Historical native samples are single observations, not measured p95. Record build,
slot wait, setup, test body, external process/file operations and teardown
separately; unknown phases remain unknown. Set measured deadlines near twice p95
with an explicit provisional sampling exception where history is insufficient.
The 35m44s aggregate triggers a caller-owned Debug task. Manifest publication and
speed-up work are separate and must not become prerequisites for fixture repair.

## Open R-2 decision

Native Windows junction qualification, privilege-independent link setup and
process-custody races remain unqualified by these 35 rows. Completing this plan
must not silently close those obligations.

## Plan validation

Pending: checkpoint import, roster collision audit, final diff and pushed-tip check.

--- next stage ---
next: review
handoff: Opus reviews the completed plan and verification design before commissioning its bounded Code slices; the current skeleton is not executable.
artifact: docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md
