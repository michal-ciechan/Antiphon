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

## Repair 9277c201 ordinary red evidence

Repair branch `feat/card-task-9277c201`, worktree `/work/worktrees/task-9277c201`.
Original Code/landing owner remains `6889fec1-9b2d-441e-a3fd-28a5c887e913`.
Review `010d7154` F1-F4; start `97acbfa6e1008b850594672d2b6ebef9bc57c24a`.
The independent component/read/state inventory is the repair section in the plan.

Tests committed before production edits:
`9016bc69588f1b040005d0bf234245da2547ea15`, then locator correction
`b2ba84ec817811dfcaaa9008d77473cbdf10f6f7`; both pushed.

- CP-1 `20261015-070830-2635`: 147 executed, 31 failed, 116 passed, 0 skipped.
  Four profile rows stopped at an incorrect exact switch name; 27 rows reached the guarded defect.
- CP-1 `20261015-071142-f8ef`: 147 executed, 31 failed, 116 passed, 0 skipped.
  Corrected profile locators reached the missing profile-read messages. The other failures were
  missing catalog/delegation/model/empty-preset messages, Group placeholder/retained-id display,
  and Runner type placeholder. Test names and assertion diagnostics inspected in each console.log.

```text
CHECKPOINT CP-1 commit=9016bc69588f1b040005d0bf234245da2547ea15 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectSetupModal.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=9016bc69588f1b040005d0bf234245da2547ea15 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=b2ba84ec817811dfcaaa9008d77473cbdf10f6f7 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectSetupModal.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=b2ba84ec817811dfcaaa9008d77473cbdf10f6f7 sourceState=clean buildSource=notApplicable
```

Drivers outside the table: dependency installation (`c1274-repair-npm-ci`, slot granted,
waited=30s, exit 0); checkpoint-tool bootstrap (`c1274-repair-checkpoint-tool`, slot granted,
waited=0s, exit 0, two existing compiler warnings). Tool built once to
`tools/Antiphon.Checkpoints/bin-c1274-repair-tool/`. esbuild's executable was copied and renamed
before checkpoint execution to break npm's hard link. A shortened expected SHA and a mistaken
`--manifest` flag were refused before a driver launched. Subsequent runs use full SHAs and the
positional manifest, rowMinutes 40, totalMinutes 150, `--total-timeout 150m`, serial, no keep-outputs.
The first red run used effective row/total overrides 40/150 (recorded in request.json).

F3 choice: disable Create project while the setup catalog is unknown, including skipped-agent
submissions. For agent creation, profile/model read states and missing selected preset also gate
Create. The server trace is ProjectSetupService -> AgentService -> AgentPresets.Apply:
explicit false and empty lists override preset values, so client initialization and recovered
payload assertions matter. Server source was inspected, not changed or executed in this repair.

Final CP-2/CP-3 qualification and CP-4 outcome will be recorded with the actual tested SHA in the
stored task report. PCs are pending; Mutation was not run. Restart: none.

## Repair Final qualification

Tested source: `18a05f2b7cb267cfa9d26a6480ad48791eab4d2a`. Run
`20261015-071611-e532`, 3m47s. Its combined verdict is RED because CP-4 retains the
known CARD-1279 failures. CP-2 and CP-3 independently qualify green.

```text
CHECKPOINT CP-2 commit=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectDeleteDialog.test.tsx src/features/settings/ApiKeysSection.test.tsx src/features/settings/ProjectConfig.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/ProjectSetupModal.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/SettingsPage.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a build=n/a filter=npm --prefix client run build executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a sourceState=clean buildSource=notApplicable
```

| Row | Actual result |
|---|---|
| CP-1 at b2ba84ec8 | 147 executed: 31 failed, 116 passed, 0 skipped; intended repair red |
| CP-2 | 480 passed, 0 failed, 0 skipped, 11 files |
| CP-4 | 4 executed: 2 passed, 2 inherited failures, 0 skipped |
| CP-3 | tsc -b and vite build exit 0; chunk-size advisory retained |

CP-4 failures: `does not fetch routing queries until the Routing tab is opened` and
`?tab=routing selects the tab and mounts routing queries without fetching templates`,
each `expected 15 to be 1`. Review 010d7154 independently reproduced these at the task
base; this run matches its names and assertions. They remain CARD-1279, not repaired here.

Both validators, with report.json, expected tested SHA, and rows CP-2,CP-3, exited 0:

```text
CHECKPOINT SOURCE VALID source=18a05f2b7cb267cfa9d26a6480ad48791eab4d2a rows=2
```

The commands were scripts/validate-checkpoint-receipt.ps1 and Antiphon.Checkpoints validate.
The client command rows produce Vitest console logs, not TRX; their CHECKPOINT count fields
and buildSource remain n/a/notApplicable as emitted. Actual counts above come from fresh logs.
Vitest's results.json was inspected: the 11 CP-2 files have failed=false, SettingsPage has
failed=true. Its observational copy is .antiphon/c1274-repair-vitest-roster.json (ignored).

| Verification ID | Actual outcome |
|---|---|
| V-1 | PASS: 44 existing read/state rows in CP-2 |
| V-2 | PASS: 8 existing offline/recovery/stale-impact cases in CP-2 |
| V-3 | PASS: 2 history pending rows, within full DispatchConcurrencyEditor (54 cases) |
| V-4 | PASS: 10 Group selector cases, within settingsPendingReads (66 cases) |
| V-5 | PASS: 17 wizard repair cases, within full ProjectSetupModal (27 cases) |
| V-6 | PASS: 4 Runner type read cases, within settingsPendingReads |
| R-1 | CP-2 PASS, 480; CP-4 inherited RED, 2 failed / 2 passed |
| R-2 | PASS, typecheck and production bundle |
| Manual | Component/read/state source inventory and server preset precedence trace completed; no browser run |

No deferred ordinary verification IDs. No whole Unit lane, per client repair brief.
PC-1 through PC-13 and the variants in the candidate doc are pending SourceLanding Mutation.
No deliberate mutants, timeout increases, assertion removals or skipped cases in this repair.
The first CP-1 used a faulty switch locator for four new cases; the corrected CP-1 was red
on their intended messages before the production fix. CP-2 was queued once after the fix.

The remaining source-inspected surfaces are explicitly disclosed in the plan inventory:
open model-routing Provider select after a read reset; project readiness dash; incomplete
Concurrency scope options/raw id; DispatchConcurrency scope spinner; missing existing-project
and repository-picker feedback; DirectoryAutocomplete suggestions; cached RolePolicy preview;
open Any-role matrix editor snapshot; optional/malformed DTO compatibility defaults.
These are disclosures, not asserted runtime coverage.
