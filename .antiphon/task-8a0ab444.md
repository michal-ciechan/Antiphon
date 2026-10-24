# Group 34b Debug result

Fixed a demonstrated product defect in local queued spill delivery. Final Linux verification passed all 98 cases, zero failures and zero skips. Native Windows qualification remains pending.

## Source and change

Task: `8a0ab444`. Branch: `feat/card-task-8a0ab444`.
Starting branch was the commissioned minimum `be55a999b1a8de651eaf85e642257eef356287cc`; it was fast-forwarded to fetched master `dc1a2184526085b8f4305878a3804f9cec41daad` before building. No branch switch, rebase, reset, force push, tag, release script, Unit lane, desktop execution or native lock was used. Qualification supervisor a779085c / PID 166312 was untouched.

This is a **product change**, not a fixture-only repair. No Review was run in this Debug task; the fixture-only Review waiver is not claimed for the product delta.

Changed files:

- `server/Application/Services/SessionMessageQueueService.cs`: generalize the existing `HoldChannelSpillBytes` helper as `HoldGeneratedSpillBytes`. Preserve complete bytes for every newly generated, message-owned spill pointer before replacing the queue body's raw text. The pointer must contain both the spill headline and this row's inbox path. The same three existing call sites use it. Product delta is seven additions and seven deletions. Input admission, locks, delivery limits and complete-UserPrompt receipt/release checks are unchanged.
- `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs`: add `SendNow_preserves_a_complete_local_spill_for_a_queued_ui_message`. It enqueues a large UI message through the public API, promotes it with SendNow, and requires a Sent/Delivered row, exactly one delivery attempt/submitted pointer, exact file bytes and a complete recipient UserPrompt. The witness removes its own temporary spill directory.

Every existing assertion remains. No existing timeout or retry was relaxed. No shared fixture or sibling-owned class was changed.

## Findings and counts

Counts are passed / failed / skipped from fresh TRX.

| Class | Reported native failures | Linux base | Final Linux |
| --- | ---: | --- | --- |
| SessionMessageQueueServiceTests | 12 | 25 / 0 / 0 | 26 / 0 / 0 |
| OutputDistillationApplyRaceTests | 5 | 26 / 2 / 0 | 28 / 0 / 0 |
| ChannelReplyDurabilityTests | 4 | 25 / 0 / 0 | 25 / 0 / 0 |
| AgentTaskLandHeldNotificationTests | 2 | 12 / 0 / 0 | 12 / 0 / 0 |
| SessionMessageQueueSpillTests (additional regression coverage) | not assigned | not run | 7 / 0 / 0 |

Base total: 88 passed, two failed, zero skipped, 90 executed. Final total: 98 passed, zero failed/skipped. All final results come from one run of committed product source `8807223fb81cebc8e9406851660e7178b3807910`.

The two reproduced failures were `OutputDistillationApplyRaceTests.File_validation_does_not_own_delivery_lock` and `Apply_and_SendNow_preserve_one_complete_body(False)`. Both raised `ConflictException: Queued spill pointer has no durable body and the source message is gone.` The local file was generated, but SendNow replaced the row's raw body with the pointer before the durability guard ran. The preservation helper accepted Channel origins only, so these Delegation rows lost their available source bytes before the guard inspected them.

A separate public API witness at test-only commit `db72c9dc41e7b74b99d6810c72bb4cfe9457e723` reproduced the same exception for an ordinary queued UI message: zero passed, one failed, zero skipped. This rules out incomplete distillation fixture setup as the explanation. The minimal product repair makes the new witness and both existing failures pass.

The other 21 failures from the older native summary did not reproduce at this newer Linux base. The base already contained current generation-bound observation setup for standalone queue/distillation adapters and LandOutcomeDeliveryHarness, plus explicit deferred action drains in ChannelReplyDurabilityTests. Their presence is source evidence, not proof of the older Windows failure's disposition. No Windows path/shell, shared-state, missing-registration or load-only cluster was demonstrated, so no forced-Windows string witness or speculative fixture fix was added.

Sample ownership: all four `A_*late_confirm*` samples in the brief belong to ChannelReplyDurabilityTests; both `C641_Held_note_reaches_busy_and_idle_caller` arguments belong to AgentTaskLandHeldNotificationTests. `C1065_BlockedSeatBundleAgreesWithTheBrief` is in sibling-owned AgentBundleAttachmentTests; `the_check_interpreter_gets_its_floor_when_it_is_provisioned` is in sibling-owned AgentWorkspaceProvisionerTests. Those sibling files were only located, not modified or executed.

## Execution and provenance

Host: server2/Linux, Debian 12, linux-x64, .NET 9.0.21, TUnit 1.44 / Microsoft Testing Platform 2.2.2. Every build/test driver used `scripts/build-slot.ps1`; all six leases were granted with zero reported admission wait and maxcpucount 6. Source stayed frozen during each driver, and every driver was awaited to exit.

| Run | Source | Build elapsed / exit | Tests passed / failed / skipped | Test elapsed / exit |
| --- | --- | --- | --- | --- |
| baseline | dc1a2184526085b8f4305878a3804f9cec41daad | 8m36.55s / 0 | 88 / 2 / 0 | 1m51.497s / 2 |
| witness | db72c9dc41e7b74b99d6810c72bb4cfe9457e723 | 3m54.73s / 0 | 0 / 1 / 0 | 1m24.681s / 2 |
| fix | 8807223fb81cebc8e9406851660e7178b3807910 | 2m29.59s / 0 | 98 / 0 / 0 | 1m44.089s / 0 |

All three builds reported 1,197 warnings and zero errors. Each rebuild compiled a distinct committed slice. The added exact-method red run was necessary to demonstrate the public-path product defect. The seven additional spill tests check shared helper behavior for UI, Channel, batching, file-write failure and Now delivery. No namespace/full-suite or Unit run was performed.

Provisional Debug command bounds: builds 600 seconds including up to five minutes admission; baseline/final tests 900 seconds including five minutes admission; witness tests 300 seconds including two minutes admission. No bound was reached. These are bounded investigation samples, not measured p95 budgets. A build-time host load observation was 34.78 / 13.88 / 5.74; no failure is attributed to load.

Shared DB setup ran once per test process. Phase records measured container creation/readiness and migration respectively: baseline 7.081s / 29.118s; witness 9.081s / 58.154s; final 7.113s / 31.175s. TRX and phase JSONL preserve per-case timing and exact membership, with run IDs g34b-baseline/g34b-witness/g34b-fix, group 34b, attempt 1, suite antiphon-suite. Uninstrumented external calls and remainder remain unknown. Witness output reports the external span cap of 100; no complete external-call attribution is claimed. Shared fixture sidecars label fixture outcome incomplete; TRX determines test outcome.

Evidence root: `/work/worktrees/task-8a0ab444/.antiphon/8a0ab444-evidence/`. It retains `build-<run>.log`, `tests-<run>.log`, `<run>/<run>.trx`, `phases-<run>/`, source/status records and DLL hashes. Generated evidence is ignored, not committed. `outputs-before.txt` was empty; `owned-outputs.txt` inventories all 33 task-created bin-g34b roots, removed after all drivers exited.

Checkpoint execution was bypassed exactly as commissioned: the test DLL was 68,516,352 bytes at the witness build, above the 64 MiB inventory limit. Direct `dotnet run --no-build` under the slot gate supplied execution evidence, not a checkpoint-qualified receipt.

Final DLL SHA-256, verified unchanged after execution before cleanup:
`d2c48c4e88158e6d924a6791b8f871a9eb617980155ceedb5a248f637158f9a1`.

TRX SHA-256:

- baseline: `e9b351a1cab41fb88040815baecb361e4aab5774069d59730b83be1174748451`
- witness: `34a16747b511f3e11d4021cb49df9d86b536a26bdcd146dde2d5d7c2f17ed04a`
- fix: `d2508b9bd648ed689ec83c3000ae778f340b21e2b5af994d8c9fea4c9e24ee0c`

## Commands and native handoff

Linux build form (each invocation used a distinct label/log):

```sh
timeout 600 pwsh -NoProfile -File scripts/build-slot.ps1 -Label g34b-fix-build -SlotWaitMinutes 5 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g34b/ --property:UseAppHost=false --nologo
```

Test driver form:

```sh
timeout 900 pwsh -NoProfile -File scripts/build-slot.ps1 -Label g34b-fix-tests -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g34b/ --property:UseAppHost=false -- --treenode-filter '<filter>' --report-trx --report-trx-filename fix.trx --results-directory '<fresh-absolute-directory>'
```

Baseline filter: `/*/*/(SessionMessageQueueServiceTests*)|(OutputDistillationApplyRaceTests*)|(ChannelReplyDurabilityTests*)|(AgentTaskLandHeldNotificationTests*)/*`.

Witness filter: `/*/*/SessionMessageQueueServiceTests/SendNow_preserves_a_complete_local_spill_for_a_queued_ui_message`.

Final filter: `/*/*/(SessionMessageQueueServiceTests*)|(OutputDistillationApplyRaceTests*)|(ChannelReplyDurabilityTests*)|(AgentTaskLandHeldNotificationTests*)|(SessionMessageQueueSpillTests*)/*`.

After rebuilding the landed candidate on the caller's owned Windows qualification lane, run these exact native rows through the build-slot gate, each into fresh TRX:

1. `/*/*/SessionMessageQueueServiceTests*/*` (26 Linux cases, includes public API witness).
2. `/*/*/OutputDistillationApplyRaceTests*/*` (28).
3. `/*/*/ChannelReplyDurabilityTests*/*` (25).
4. `/*/*/AgentTaskLandHeldNotificationTests*/*` (12).
5. `/*/*/SessionMessageQueueSpillTests*/*` (7).

Windows build: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label g34b-native-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g34b-native/ --nologo`.

Windows test: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label g34b-native-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g34b-native/ -- --treenode-filter '<native-row>' --report-trx --report-trx-filename native.trx --results-directory '<fresh-directory>'`.

No native result, release qualification, publication or activation is claimed here.

## Publication checks

Witness commit and product repair were each pushed before their build/test run. `git diff --check` passed. Evidence-history guard over dc1a2184..8807223f passed: two commits, zero evidence entries, zero violations. Fetched master was dc1a2184526085b8f4305878a3804f9cec41daad; merge-tree at the verified product tip was clean, tree `43fdc42fb522a964431dee9f923af50e612b2dd5`. Report-only publication is checked again in the final handoff and changes no tested source.

--- next stage ---
next: land
handoff: Product fix demonstrated by a committed red public API witness and 98 green cases. Apply the caller's product-change gate, land the pushed branch, then rerun all five native rows. No Review or native qualification was performed by this Debug task.
artifact: /work/worktrees/task-8a0ab444/.antiphon/task-8a0ab444.md
