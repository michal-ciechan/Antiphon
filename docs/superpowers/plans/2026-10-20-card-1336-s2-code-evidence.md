# CARD-1336 S2 Code evidence

S2 is implemented and its commissioned ordinary verification passes. Direct
nightly RC calls derive their candidate identity from the validated ref and
pinned full SHA, and reject a supplied foreign identity before locks, sync,
selection or workload. Master-only. Restart: none. No gate credit is claimed.

Original S2 Code task / landing owner:
`cde61484-46ec-4c8a-8999-3f95c155b1c3`.
Branch: `feat/card-task-cde61484`.
Worktree: `/work/worktrees/task-cde61484`.
Assigned base: `427114df1314211629e7327a6d824b5c56afaf57`, the reviewed S1 tip
from `c6cc11da`, stacked on CARD-1339.

| Slice | Full pushed commit | Outcome |
|---|---|---|
| Test-only witness | `bb66d691506137b87cee587d8dd96cf0de1ef209` | CP-3 expected missing-CandidateId assertion failure |
| Production fix and expanded fixture | `ab926d971f9fd0ee87fb380bb42104a4348169e9` | First CP-4 failed in the new synthetic npm allowlist |
| Fixture repair | `a19869cc5a78145426de2eebaa115c328bdf7904` | CP-4 green; actual tested source |

Each slice was committed and pushed before its checkpoint. The subsequent
report-only commit changes this evidence; its SHA is in the final caller report.

## Scope and implementation

The brief specifically commissions S2, CP-3 then CP-4, with a 40-minute row
timeout. This and the plan's closed list narrow the generic Final profile:
no Unit-wide lane, whole affected class, whole assembly, full release harness
or actual native nightly/RC profile was run. Final ordinary scope for this
S2 dispatch is complete. No ordinary S2 ID is deferred or silently marked passed.
The initial ordinary Review remains required.

Plan:
`docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md`.

- `scripts/lib/nightly-run-impl.ps1` passes ExpectedSha to the existing parser.
  Ref refusal still precedes full-SHA refusal. Omitted/whitespace identity is
  assigned to both the local variable and returned result; a supplied id must
  equal the parser's canonical id using ordinal comparison, otherwise
  `rc-candidate-id-mismatch`, exit 3, phase `profile`.
- `scripts/nightly-run.ps1` documents both ref/id shapes, Trigger=rc, the full
  SHA pin and explicit private roots in a direct-call example. It grants no
  publication authority or master readiness.
- `tests/Antiphon.Tests/Scripts/ReleaseGateFollowupTests.cs` and
  `tests/fixtures/card1336-release-gate-followups.ps1` add the 25-row identity
  fixture through the existing Integration ScriptHarness and process limiter.
  The 300-second invocation and 10-second cleanup budgets are unchanged.
- The unbound candidate
  `docs/superpowers/mutations/2026-10-20-card-1336-release-gate-followups-candidate.md`
  now records the S2 anchors. Every PC and variant remains pending.

The fixture creates a private seed, bare local origin, independent clone and
two real commits. It validates committed policy/catalog contracts before the
red assertion. The oracle uses a literal date and actual SHA prefix, never the
parser under test. All Git transport stays local.

The real selection worker, validators, self-hop, workload child and completion
conversion run. Observers record child-bound parameters and the real workload
result; selection files/digests, summary, state, green and terminal result are
joined by candidate id, SHA, run id and attempt. Native workload and report
I/O are synthetic and fail closed for unexpected targets. The workload observer
adds an optimistic reportDelivered field only to synthetic summary I/O so
NoReport is tested against a competing claim. Identity, selection and credit
functions are not replaced. The default master root is redirected to four
private sentinels, checked byte-for-byte after success and refusal variants.

No publish-release, promote-release or release-cut entrypoint ran, directly or
indirectly. No real tag operation, live Windmill operation, registration,
deployment, rc-branch mutation or external report delivery ran. Local synthetic
green files prove state transitions only.

## Checkpoints and failures

Every invocation used the checkpoint tool, the exact committed expected SHA,
the plan's filter with trailing method wildcards, a fresh isolated build, and
`--row-timeout 40m`. All foreground runs were awaited to terminal completion.
No source was edited while a run was in flight.

| Row | Run | TUnit executed/pass/fail/skip | Outcome | Build seconds | Host test wall seconds | Receipt wall seconds |
|---|---|---|---|---:|---:|---:|
| CP-3 | 20261020-065239-50b4 | 1 / 0 / 1 / 0 | Expected behavioral red, exit 1 | 324.659 | 34.250 | 371.007 |
| CP-4 first | 20261020-070232-fa8e | 2 / 1 / 1 / 0 | Introduced fixture failure, exit 1 | 203.778 | 99.761 | 318.730 |
| CP-4 repaired | 20261020-070914-b4ab | 2 / 2 / 0 / 0 | Green, exit 0 | 196.697 | 98.780 | 310.039 |

All selected build and test slots were `slot=granted waited=0s`.
The manifest declares four builds; only the one selected build ran per invocation.
The other manifest builds are recorded as unused.

CP-3 failed exactly at `C1336 N omitted`: expected
`rc-2026-10-18-4141a59d81de`, actual empty. The returned object was checked
before downstream-success assertions. The validated real selection fixture
subsequently refused `selection-candidate-identity`; that downstream refusal
was not the behavioral oracle. The historical expected-red receipt stays red.

The first CP-4 ran all 25 new rows: 20 passed and five failed
(`sync-selection`, `completion`, `rc-green`, `nosync-selection`, `noreport`).
The existing 24-row credit method passed. The new fixture allowlist rejected
the production workload's Windows-style npm request on Linux, where ComSpec
is empty. This was an introduced fixture defect, not an inherited failure
attributed to production. The repair recognizes only the intended synthetic
`ci`, `run build` and `run lint` command forms; it launches none of them.
It also strengthens both child checks to require successful complete workload
results. No timeout or assertion was loosened.

The repaired CP-4 fresh TRX contains exactly these two methods, both Passed:

- `Antiphon.Tests.Scripts.ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity`
- `Antiphon.Tests.Scripts.ReleaseGateRunTests.C599_CreditVerdicts`

The harness validates 25 + 24 = 49 internal C487 rows. These are not 49 TUnit
executions. The required named roster in C# includes every new row.
No same-source repetition, flake retry or deliberate mutant ran. The only
CP-4 rerun followed the committed fixture repair; none ran after green.

| ID | Actual outcome |
|---|---|
| V-1 | Historical S1 CP-2 passed at `b56091fcfd77528b5d17729b82041a7a774f2c2d`; not rerun in this S2 dispatch. |
| R-1 | Same S1 evidence: nine wrapper rows plus 16 registration rows, two TUnit methods. |
| V-2 | Passed in repaired CP-4 after the decisive CP-3 missing-id red; canonical identities and refusal precedence. |
| R-2 | Passed in repaired CP-4; omitted/whitespace/full-ref/repeat/different pin/explicit/foreign/invalid SHA/ref/legacy cases. |
| V-3 | Passed in repaired CP-4; actual selection, self-hop, workload, persisted state and terminal identity agree. |
| R-3 | Passed in repaired CP-4; both selection branches, child parameters and digest bindings are exercised. |
| V-4 | Passed in repaired CP-4; candidate-only synthetic green, unchanged master sentinels, NoReport and moved pins. |
| R-4 | Passed in repaired CP-4; isolation/fail-closed cases plus all 24 existing credit rows. |

Historical S1 detail:
`docs/superpowers/plans/2026-10-20-card-1336-s1-code-evidence.md`.
Required S2 manual acceptance is the offline help/root contract and evidence
inspection above. Live operating qualification is expressly outside the plan.

## Source provenance, cost and cleanup

Read current GET `/api/runner-defaults` and GET `/api/session-runners`. No runner
or OS pin was added and no fleet location was embedded.

The only build outside checkpoint rows was the required checkpoint-tool
bootstrap, implicit in the plan's gated `dotnet run` command:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1336-checkpoint -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md --rows CP-3 --expected-source-sha bb66d691506137b87cee587d8dd96cf0de1ef209 --row-timeout 40m
```

The outer bootstrap slot was granted with zero wait and held for about 390s
including CP-3. Subsequent calls used that built tool DLL directly, with no
implicit build; executors acquired their own build and test slots. No unlisted
test, warm-up or broad native profile ran. Static PowerShell parsing and
`git diff --check` passed.

Measured checkpoint receipt wall total, including the failed CP-4 attempt:
999.777s (16.663 minutes), plus bootstrap/launcher and authoring overhead.
This preserves actual failed-run cost and claims no measured savings.

All three receipts name clean start/end source with zero dirty files and
verified producing-build provenance. Fresh TRX method identities and nonzero
counts were inspected independently. The green validator returned:

```text
CHECKPOINT SOURCE VALID source=a19869cc5a78145426de2eebaa115c328bdf7904 rows=1
```

Ignored raw reports, TRX, source/build receipts and logs remain under:

- `/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-065239-50b4/`
- `/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-070232-fa8e/`
- `/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-070914-b4ab/`

Both failed runs kept their output and remained exit 1. Their exact owned
`tests/Antiphon.Tests/bin-c1336-cp3` / `bin-c1336-cp4` directories were removed
only after executor completion. The final green run cleaned its output; all
three tool shadow copies were removed. No `bin-c1336-*` directory remains.
There was no receipt-refusal retry.

The evidence-history guard passed over assigned base through the tested tip:
3 commits, 0 evidence entries, 0 violations. It is run again over full base..HEAD
after this report is committed; the final caller report records the outcome.

`git fetch origin master` resolved to
`5fb0d72e9ee81e97bd8a84865fe190cac28a00dd`.
`git merge-tree --write-tree origin/master HEAD` passed without conflicts at
the tested source, producing `4319a2f5e1a59cc16ac9f6991efc4b28c6043aa0`.
The report-only tip is checked again before settlement. No merge was made.

## Unedited checkpoint lines

```text
CHECKPOINT CP-3 commit=bb66d691506137b87cee587d8dd96cf0de1ef209 build=ok filter=/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-065239-50b4/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=bb66d691506137b87cee587d8dd96cf0de1ef209 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=ab926d971f9fd0ee87fb380bb42104a4348169e9 build=ok filter=/*/Antiphon.Tests.Scripts/(ReleaseGateFollowupTests*)|(ReleaseGateRunTests*)/(C1336_NightlyCandidateIdentity*)|(C599_CreditVerdicts*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-070232-fa8e/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=ab926d971f9fd0ee87fb380bb42104a4348169e9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=a19869cc5a78145426de2eebaa115c328bdf7904 build=ok filter=/*/Antiphon.Tests.Scripts/(ReleaseGateFollowupTests*)|(ReleaseGateRunTests*)/(C1336_NightlyCandidateIdentity*)|(C599_CreditVerdicts*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-cde61484/.antiphon/checkpoints/20261020-070914-b4ab/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a19869cc5a78145426de2eebaa115c328bdf7904 sourceState=clean buildSource=verified
```

## Handoff and outstanding boundaries

Next: ordinary Review. The plan's final Review selection is CP-2 and CP-4 at
the exact reviewed tip, with historical CP-1/CP-3 red evidence inspected:

```sh
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md --rows CP-2,CP-4 --expected-source-sha <reviewed-sha> --row-timeout 40m
```

On a fresh checkout bootstrap the tool through the host build-slot gate first.
All PC-1 through PC-42, and every variant listed in the plan, remain pending
for independently bound, method-scoped SourceLanding Mutation. That includes
PC-1..4 (wrapper), PC-5..23 (identity/propagation/state), PC-24..38 (credit) and
PC-39..42 (registration). Mutation owns deliberate mutants, red/restore/green
and missing-control discovery. Ordinary green is not PC-clean.

After Review the caller lands the original Code owner and commissions the
SourceLanding Mutation obligation. This report does not settle next=land.

As requested, `docs/release-gates.md` was not edited: its current-line sentence
still says `rc/2026-10-15`, while the wrapper pin is `rc/2026-10-18`.
That advisory inconsistency remains explicitly recorded.

CARD-1339 retains exact-ref/allocation/publication ownership. Its code is in the
assigned stack; this task does not certify its separate landing or evidence.
The caller records its actual S1 landing/evidence dependency before card closure.
Master publication does not update an rc-hosted entrypoint; the plan's conditional
backport requirements remain. No deployment or restart is required by this Code
slice, and none occurred.
