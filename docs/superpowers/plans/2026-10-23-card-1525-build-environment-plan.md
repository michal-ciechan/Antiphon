# CARD-1525: absent build-driver opt-out

Original Code/landing owner: f96ac1ff-a893-4d0a-887a-a8803ffd5bce.
Branch: feat/card-task-f96ac1ff. Base: 72c2464cfe572a4b4a5881c09ab1f0265b43ba51.
Round: Final, with the explicit incident brief's narrow scope (no Unit lane).
Parent plan: 2026-10-22-card-1506-no-resident-compiler-plan.md.

## Implementation

Commit the regression before its fix. Real ProcessStartInfo.EnvironmentVariables
is a StringDictionary adapter backed by a Dictionary: an absent indexer can throw,
unlike a standalone StringDictionary. Guard the opt-out read with ContainsKey.
Keep the native Windows dictionary's case-insensitive lookup and exact value `1`
semantics. Do not change the six overrides, adapters, deadlines or assertions.

Extend the existing script; retain all six wrapper scenarios and four direct
cases. Add separately selectable real ProcessStartInfo cases for absent, `1`,
`0`, empty and absent-with-all-six-conflicting settings, each through both the
C# entry and the PowerShell adapter. Compare the entire environment to a captured
input when opted out; otherwise assert literal expected settings, unrelated value,
key count and opt-out presence/value. Also cover standalone StringDictionary
absence and mixed-case opt-out names (the Windows case-insensitive contract).
On Linux, Windows=true selects policy behavior; it does not change the underlying
ProcessStartInfo dictionary's native case comparer.

Inspect every CARD-1506 launch boundary, including both checkpoint launches,
land verification, run-checkpoint, nightly native/exact, watched tests,
build-slot, restart/dev-aspire and daemon builds, for other unchecked reads.

## Verification design

- V-1: new absent real ProcessStartInfo witness throws KeyNotFound before fix;
  after fix both direct and adapter paths set all six values without exception.
- V-2: explicit zero/empty and absent conflicting settings apply all six values;
  absence stays absent, empty stays empty, unrelated values survive.
- R-1: exact opt-out `1` preserves the complete environment on both paths.
- R-2: all original CARD-1506 six child scenarios/four direct cases stay green,
  including Linux no-op and wrapper parent restoration.
- R-3: standalone StringDictionary absence and case-insensitive opt-out still work.
- V-3: inspect launch boundaries and compile linked C# consumers; parse changed
  PowerShell and retain ASCII compatibility. No live process/restart acceptance.
- V-4: full base..HEAD evidence guard; current-master merge-tree and three-way
  application onto origin/rc/2026-10-20 in a temporary index, without checkout.

S1 commits tests/plan/candidate and runs CP-2 red on unchanged production source.
S2 commits the minimal fix and the report before the final checkpoint selection.
Run every row once on that final tip. CP-1 retains the parent plan's exact command
and now includes seven regression cases (twelve path executions); CP-2..8 expose
each added case as a narrow independent selection. No TUnit rows/TRX are expected;
assert nonzero script case counts in each fresh console log. Keep tool receipts
and CHECKPOINT lines with exact source SHA; command builds are notApplicable.
All rows use the checkpoint tool's build-slot gate, 2-minute hard row timeout,
10-minute total timeout and serial execution. Bootstrap/compiler builds use
scripts/build-slot.ps1, isolated bin-c1525-* outputs and a 5-minute hard timeout.
Do not nest a build-slot wrapper inside the tool's already leased rows.

Native Windows proof is owned by the caller after the reviewed rc pick:
in a separate shell with the opt-out removed from that shell's process environment,
run `pwsh -File scripts/build-slot.ps1 -Label probe -- dotnet --version` once.
Do not touch supervisor PID 133964, C:\Antiphon\releases or the live qualification.
The temporary USER opt-out and AppHost/runner activation belong to the orchestrator
after qualification permits it. This Code task runs nothing on the desktop.

### Positive controls pending for SourceLanding Mutation

All remain unbound/pending; Code performs no deliberate mutants.
PC-1: remove absent-key guard (ProcessAbsent and ProcessConflicting variants).
PC-2: remove opt-out check (ProcessOptOutOne and StringDictionaryMixedCase variants).
PC-3: change exact opt-out comparison to presence (ProcessOptOutZero/Empty variants).
PC-4: omit each of six overrides (six ProcessConflicting variants).
PC-5: omit PowerShell adapter application (ProcessAbsent adapter variant).
Parent CARD-1506 PCs remain pending; no ordinary green discharges them.
Candidate: ../mutations/2026-10-23-card-1525-build-environment-candidate.md.

### Cost

40-minute incident budget. Ordinary rows estimate 9 minutes total (normally much
less), authoring/audit 15 minutes, bootstrap/consumer builds up to 10 minutes.
Only unlisted builds: checkpoint bootstrap and final linked checkpoint/server
compilation, needed to execute the tool and check the shared-source consumers.
No full assembly, no Unit lane, no loaded repetition. No repeat after green;
CP-1 plus individual selections gives two executions per new case at final source.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | n/a | original-and-regression | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1` | V-1, V-2, R-1, R-2, R-3 | 6 original child scenarios, 4 original direct cases, 7 regression cases (12 paths), 0 failed | n/a | 2 |
| CP-2 | all | n/a | process-absent | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessAbsent` | V-1 | 1 case, 2 paths, 0 failed | n/a | 1 |
| CP-3 | all | n/a | process-optout-one | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessOptOutOne` | R-1 | 1 case, 2 paths, 0 failed | n/a | 1 |
| CP-4 | all | n/a | process-optout-zero | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessOptOutZero` | V-2 | 1 case, 2 paths, 0 failed | n/a | 1 |
| CP-5 | all | n/a | process-optout-empty | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessOptOutEmpty` | V-2 | 1 case, 2 paths, 0 failed | n/a | 1 |
| CP-6 | all | n/a | process-conflicting | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case ProcessConflicting` | V-2 | 1 case, 2 paths, 0 failed | n/a | 1 |
| CP-7 | all | n/a | stringdictionary-absent | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case StringDictionaryAbsent` | R-3 | 1 case, 1 path, 0 failed | n/a | 1 |
| CP-8 | all | n/a | stringdictionary-case | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1 -Case StringDictionaryMixedCase` | R-3 | 1 case, 1 path, 0 failed | n/a | 1 |
