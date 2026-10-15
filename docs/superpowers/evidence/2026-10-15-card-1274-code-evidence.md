# CARD-1274 Code evidence

Task `6889fec1`, branch `feat/card-task-6889fec1`, worktree `/work/worktrees/task-6889fec1`, base
`78e4001788e99ebc04f0590a84db18442f6324c6`. Plan:
`docs/superpowers/plans/2026-10-15-card-1274-settings-pending-reads-plan.md`.

Commits: S1 tests `48855ce665284b1c0f745c08b86027b9a420b592`; S2 fix
`f4615d92271cbe5e090379d480e3d7125eeab5e9`; plan amendment (CP-4)
`abfdd29b063744aef15dedaa37e79cc2b8c6f7e1`.

## Checkpoint lines (unedited)

CP-1 red at S1, run `20261015-061415-636f` (vitest: 42 failed, 64 passed; the failing names are
the C1274 rows whose surface was wrong at the base; the C1274 rows green at S1 are the existing
failed/success controls of the top-level pages):

```text
CHECKPOINT CP-1 commit=48855ce665284b1c0f745c08b86027b9a420b592 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=30s dirty=0 source=48855ce665284b1c0f745c08b86027b9a420b592 sourceState=clean buildSource=notApplicable
verdict: RED exit=1
```

CP-2 and CP-3 at S2, run `20261015-061840-d5fc`: CP-2 red in `SettingsPage.test.tsx` (2 tests,
`expected 15 to be 1`), which fail with the same assertion at the base (classified in a detached
base worktree through build-slot `c1274-settingspage-classify`). The plan moved that file to CP-4.

```text
CHECKPOINT CP-2 commit=f4615d92271cbe5e090379d480e3d7125eeab5e9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectDeleteDialog.test.tsx src/features/settings/ApiKeysSection.test.tsx src/features/settings/ProjectConfig.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/settings/SettingsPage.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=f4615d92271cbe5e090379d480e3d7125eeab5e9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=f4615d92271cbe5e090379d480e3d7125eeab5e9 build=n/a filter=npm --prefix client run build executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=30s dirty=0 source=f4615d92271cbe5e090379d480e3d7125eeab5e9 sourceState=clean buildSource=notApplicable
verdict: RED exit=1
```

Receipts at `abfdd29b` (client source equals S2; the amendment changes the plan doc):

```text
run: 20261015-062857-0cbc
CHECKPOINT CP-2 commit=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectDeleteDialog.test.tsx src/features/settings/ApiKeysSection.test.tsx src/features/settings/ProjectConfig.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/settings/ConcurrencySettingsTab.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=45s dirty=0 source=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 sourceState=clean buildSource=notApplicable
verdict: GREEN exit=0
run: 20261015-063537-07e5
CHECKPOINT CP-4 commit=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/SettingsPage.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 sourceState=clean buildSource=notApplicable
verdict: RED exit=1
run: 20261015-063727-d06e
CHECKPOINT CP-3 commit=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 build=n/a filter=npm --prefix client run build executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=90s dirty=0 source=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 sourceState=clean buildSource=notApplicable
verdict: GREEN exit=0
```

CP-2 vitest summary: `Test Files  10 passed (10)`, `Tests  439 passed (439)`. CP-4: `Tests  2 failed | 2
passed (4)`, the two inherited tests named in the plan row.

Validators (`scripts/validate-checkpoint-receipt.ps1` and `Antiphon.Checkpoints validate`, each with
`report.json` and the run's commit):

- `20261015-062857-0cbc` (CP-2) and `20261015-063727-d06e` (CP-3): both print
  `CHECKPOINT SOURCE VALID source=abfdd29b063744aef15dedaa37e79cc2b8c6f7e1 rows=1`.
- `20261015-061415-636f` (CP-1, intended red), `20261015-061840-d5fc`, `20261015-063537-07e5`
  (CP-4, inherited red): both print `CHECKPOINT SOURCE INVALID reason=row_failed`.

Drivers outside the table, each under build-slot: `npm --prefix client ci` (`c1274-npm-ci`);
two vitest probes on the working tree before the S1/S2 split (`c1274-red-probe`,
`c1274-green-probe`, fixture debugging); the checkpoint-tool bootstrap `dotnet build
tools/Antiphon.Checkpoints` (`c1274-checkpoint-tool`, default output, deleted afterwards); the base
classification of `SettingsPage.test.tsx` (`c1274-settingspage-classify`). The esbuild hard link was
broken by copy-and-rename before the client rows. Manifests rowMinutes 40, totalMinutes 150,
`--serial`, no keep-outputs.

## Disclosures (not findings)

1. `SettingsPage.test.tsx` `does not fetch routing queries until the Routing tab is opened` and
   `?tab=routing selects the tab and mounts routing queries without fetching templates` fail at the
   base `78e400178` with `expected 15 to be 1`; not caused here, not chased.
2. The whole Unit lane was not run, as the brief instructs; no .NET source changed.
3. Mutation was not run; PC-1..PC-6 are pending
   (`docs/superpowers/mutations/2026-10-15-card-1274-candidate.md`).
4. The `success` rows of template groups, template providers, template model routing and project
   boards were red at S1 because their surface test ids were added in S2 (a test seam, not a
   product defect).
5. Enumerated and not changed: ProjectConfig readiness cell (`--` for a pending or failed read),
   ConcurrencySettingsTab scope label for an unread project, DispatchConcurrencyEditor scope loader
   for a paused read, ProjectSetupModal existing-project hint, AgentTuiProfileModal runner list.
6. Tests run under jsdom; no real-browser acceptance.
