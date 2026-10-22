# CARD-0167 S5 test repair - Code 6c1c3a92

This report is frozen before qualification so the final pushed tip is the tested source.
Actual run outcomes, source SHA, unedited CHECKPOINT lines and the final publication check
are in this task's completion report; no green outcome is asserted here in advance.

## Source and ownership

- Assigned branch: `feat/card-task-6c1c3a92`.
- Worktree: `/work/worktrees/task-6c1c3a92`.
- Repair base: `68444cb48f6a518b3e8d3999d249a40a4cf5c5f3`.
- Whole S5 delta base: `445df8a805dac8525203081c03f6e58fa3f9ff3d`.
- Original S5 Code: `f9e4bfe6`; this repair task `6c1c3a92` owns landing the whole S5 delta.
- Review: `a4cdbeab`, report at `origin/feat/card-task-a4cdbeab:.antiphon/task-a4cdbeab.md`.
- Landed plan: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan.md`, S5 / CP-6.
- Master only; no RC pick. Restart: none for this test-only repair.

## Test changes

Only `client/src/features/settings/ProviderEndpointFields.test.tsx` changes executable source.

- D-1: the document-wide sentinel assertion runs with the project modal and key picker
  still open, after the loaded key option is found, before choosing a key or saving.
- D-2: an unrelated Name edit saves once, verifies the changed name in the captured PUT
  body, and asserts the absence of the `providerEndpoints` property.
- D-3: Own endpoint saves once after changing the URL; the captured PATCH must contain
  mode `Endpoint` and the exact endpoint object, including the edited URL and stored key name.
  Both agent tests share the same route fixture.

## Qualification selection

The brief bounds this Final repair to CP-6 and the ten full neighbouring files. No server
integration class or whole-suite run is requested for this test-only delta. The plan defines
no V-n or R-n IDs for S5. The original plan's real-CLI/operator launch acceptance belongs
to the full feature and is not claimed by this repair.

CP-6: run the checkpoint tool with the landed plan, `--rows CP-6`, and
`--expected-source-sha <committed HEAD>`. This command row runs
`pwsh -File scripts/test-client.ps1 ProviderEndpointFields.test`; expect 7 tests (the original
5 plus D-2 and D-3), with typecheck and lint green. The checkpoint runner takes its own
host build slot. Non-TUnit rows have no TRX or compiled test-build binding; inspect each
Vitest result and the clean source receipt instead.

An unlisted tool bootstrap build is necessary because this checkout has no checkpoint
binary; run it through `scripts/build-slot.ps1` with `OutputPath=bin-c0167repair/`, then
remove only its producer-owned alternate output directories after qualification.

The additional run is explicitly required by the repair brief: every neighbouring test
file rendering ProjectConfig or AgentSettingsModal, each in full, through build-slot:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0167-s5-neighbours -- pwsh -NoProfile -File scripts/test-client.ps1 AgentPreamblePreset.test AgentBundleAttachments.test AgentSessionBackend.test AgentHerdrPlacement.test AgentRemoteControl.test AgentReplyStyle.test AgentAutoCompact.test settingsPendingReads.test AgentLaunchEnv.test ProjectConfig.test
```

Expected neighbour count: 122 tests / 10 files. Preserve generated logs and receipts under
ignored `logs/c0167-s5-repair/` and `.antiphon/checkpoints/`. No timeout or assertion is loosened.
Run `scripts/check-evidence-diff.ps1` over both the repair base and the full S5 base through
the final HEAD, and fetch/merge-tree current origin/master without merging it.

## Pending Mutation and follow-up

Historical qualification at `94635d3ee16b7cc2e711d42c18cb80e22c6062dd`:
the checkpoint bootstrap succeeded (0 errors, 4 nullable warnings; slot granted,
waited=0s, held=7s). CP-6 was red before Vitest: TS2769 at the new Name selector,
because ByRoleOptions has no `exact` property. The repair uses an anchored name
matcher. This was an introduced test authoring error, not a guarded product defect
or a Mutation red result. No tests executed in this attempt. Its original receipt:

```text
CHECKPOINT CP-6 commit=94635d3ee16b7cc2e711d42c18cb80e22c6062dd build=n/a filter=pwsh -File scripts/test-client.ps1 ProviderEndpointFields.test executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=2 slot=granted waited=0s dirty=0 source=94635d3ee16b7cc2e711d42c18cb80e22c6062dd sourceState=clean buildSource=notApplicable
```

Evidence: `.antiphon/checkpoints/20261022-185859-9605/report.md` and its CP-6 console log.
The corrected final tip must run CP-6 again and then the ten neighbour files once.

Second attempt at `d9d0ec449683e2033980e7316ca60e95ef1521d1`: typecheck and lint
passed; Vitest executed 7 tests, 6 passed and D-2 failed because both the project and
API-key editor have a textbox named Name. D-2 now targets the existing `My Project`
placeholder and checks its initial value before editing. The project fixture also
handles the profiles read, matching the adjacent ProjectConfig test fixture; this
removes the unhandled MSW request observed during that attempt. No product change
or relaxed assertion. Original receipt (wall 2m45s):

```text
CHECKPOINT CP-6 commit=d9d0ec449683e2033980e7316ca60e95ef1521d1 build=n/a filter=pwsh -File scripts/test-client.ps1 ProviderEndpointFields.test executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=d9d0ec449683e2033980e7316ca60e95ef1521d1 sourceState=clean buildSource=notApplicable
```

Evidence: `.antiphon/checkpoints/20261022-190118-fd20/report.md` and its CP-6 console log.
The two failures above are test authoring errors, not Mutation evidence. No unchanged
selection has been repeated more than once, and neighbours have not yet run.

The stage-code contract reserves deliberate mutations and red/restore/green cycles for
Mutation. Therefore the brief's requested new mutant runs are not executed in Code and
must not be reported as proved. Review a4cdbeab's historical mutation outcomes remain:
M1's project half survived; M2 and M3 survived every named S5 test. The repaired tests'
ordinary green does not discharge the following pending controls:

| Control | Mutation | Exact detecting test in ProviderEndpointFields.test.tsx |
|---|---|---|
| M1-project / D-1 | Render the key rows including sentinel value in ProjectProviderEndpoints | renders stored endpoints per kind and submits the parsed map |
| M1-agent | Render the key rows including sentinel value in AgentProviderEndpoint | Direct mode hides the fields and submits providerEndpoint null |
| M2 / D-2 | ProjectConfig always sends providerEndpoints on save | omits providerEndpoints when saving an unrelated project field |
| M3 / D-3 | Own endpoint sends providerEndpoint null | Own endpoint mode submits the edited URL and full endpoint |

No PC-n IDs are defined in the original plan. These variants and missing-control discovery
remain pending for method-scoped SourceLanding Mutation after ordinary Review and landing.

Backlog-only follow-up: reject userinfo in endpoint URLs in both client forms before sending
the request. Exhaustive card searches for `userinfo`, `provider endpoint`, and `URL credentials`
found no covering card; CARD-1423 was inspected and concerns monitor logs instead.
Filed and read back **CARD-1509**, Backlog, id `42857453-08aa-449d-af5b-28d73ca44823`.
The initial POST returned HTTP 499 after creating it; search and GET confirmed the stored
description, so no duplicate write was attempted.

Next: Review the repaired S5 branch and its ordinary evidence. The caller lands original
Code task 6c1c3a92 after approval, then commissions SourceLanding Mutation for all pending
controls. No land or deploy is performed by this task.
