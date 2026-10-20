# CARD-1312: isolate the two checkpoint harness cases

Date: 2026-10-20. Plan and folded TestDesign; ready for Code.
Source: `17841814de918b64bd104d68e65794f68232a4b1`.
Read first: [investigation](../../investigations/2026-10-20-card-1312-shared-bin-c585h.md).
No tests or builds were run for this plan.

## Ground truth

| Card assumption | Code observation | Consequence |
|---|---|---|
| Fresh results isolate a test. | `New-C585Case` creates unique results, but `Invoke-C585Runner` defaults to `bin-c585h/` in `scripts/test-run-checkpoint.ps1`. | Give each affected case its own build-output path too. |
| These failures require concurrent tests. | Both methods are serialized by `RunCheckpointScriptTests`'s one-wide process limiter. A successful shim build writes `checkpoint-build-source.json`; the next build refuses it. | An explicitly ordered, single-test witness suffices. |
| Both methods always fail in an empty checkout. | FreshResultsDir can succeed first and leave the shared stamp; SlotGranted then refuses `pre-existing-build-source`. An earlier successful case makes both fail. | The red witness asserts the sequence succeeds; expect failure in its second case on a clean starting output. Do not claim two native failed results. |
| Isolation alone repairs SlotGranted. | Its exact build expectation omits `ArtifactsPath` and `UseArtifactsOutput=true`. The shim receives relative build OutputPath; the run uses an absolute path. | Correct the exact build oracle without weakening the slot/order assertions. |
| A temp-directory absolute OutputPath would work. | `scripts/run-checkpoint.ps1` admits only `^bin-[A-Za-z0-9._-]+/$`. | Keep a relative, GUID-qualified `bin-.../` token and the existing guard. |

## Decisions

- **D-1: scope the repair to the two affected cases.** Allocate one fresh full-GUID output token inside each `Test-C585_FreshResultsDir` and `Test-C589_SlotGranted`, for example `bin-c585h-freshdir-<guid>/` and `bin-c585h-slot-granted-<guid>/`. Pass it explicitly to every invocation in that case. FreshResultsDir's two arms share its token: its first arm must refuse the occupied results directory before any build. Reject changing the shared runner default here: that also changes unrelated multi-build, NoBuild and C671 exact-argv cases and exceeds this narrow verification scope.
- **D-2: retain the driver's ownership rules.** Do not pre-create output directories, delete a shared stamp to make admission pass, or change `scripts/run-checkpoint.ps1`. Record the exact newly produced roots; after child exit and evidence capture, Code removes only its inventoried, unchanged, unlinked outputs. Retain unfamiliar residue. Do not introduce a general cleanup mechanism in this repair.
- **D-3: keep SlotGranted's exact build assertion.** Construct the expected string independently from the case's output token and repository path, with slash-normalized absolute artifacts path:
  `build tests/Antiphon.Tests --property OutputPath=<relative-token> --property ArtifactsPath=<absolute-output>/artifacts --property UseArtifactsOutput=true -maxcpucount 3 --nologo`.
  Its existing simulated `windows` platform suppresses the Linux `UseAppHost=false` default. Preserve all seven named assertions, including no maxcpucount on the run and `POST,build,run,DELETE` with one release. Reject substring-only matching and copying the observed command into the expected value. Leave shared C671 constants alone.
- **D-4: one red and one narrow green, within 30-45 minutes.** Fold verification design into this dispatch as requested. No full class, namespace, assembly, repetition or separate platform sweep. No session waiting for input is added or changed.

## Slices and overlap

1. **S1 — commit/push the failing witness first.** In `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`, add `[Test] C1312_FreshResultsDirThenSlotGranted`, an async method that awaits the existing `C585_FreshResultsDir()` wrapper and then `C589_SlotGranted()`. Keep the existing class limiter. Both wrappers retain their existing named PASS inventories (4 and 7). Run CP-1 before changing the PowerShell cases.
2. **S2 — commit/push the repair, then CP-2.** Change only the two case bodies in `scripts/test-run-checkpoint.ps1` as D-1/D-3 specify; keep the script ASCII-only. No shared `ScriptHarness` or process-lifetime changes. Retain S1's witness and both original TUnit methods.

CARD-1368 tip `359cc912235d840e9f94202592e86480d87db468` was reported queued
to land. It changes checkpoint output adoption at observed host exit and content
stamping/cleanup, including `OwnedOutputLedger`, `CheckpointFileReader` and
`tools/Antiphon.Checkpoints/Execution/RowRunner.cs`. Compared with this plan's
source, the two repair files and `scripts/run-checkpoint.ps1` are unchanged at
that tip: no direct edit overlap. It can affect the outer checkpoint's cleanup
verdict, so record the actual Code source/tool revision and distinguish a tool
cleanup refusal from a harness assertion failure. Do not port its changes or
weaken cleanup/source qualification as part of CARD-1312.

## Verification design

**W-1 / V-1 — ordered isolation witness.** Start CP-1 with
`tests/Antiphon.Tests/bin-c585h/` absent in the assigned Code worktree; inspect
before running and do not erase pre-existing foreign output. Run no other
legacy harness case first. On S1, FreshResultsDir must finish its four assertions,
then SlotGranted must actually execute and fail with `pre-existing-build-source`.
The witness contributes one native failed TUnit result. Missing tests, build
failure, timeout, an earlier FreshResultsDir failure, or only the stale-argv
failure do not establish this isolation red. Preserve the failure diagnostics
and the exact S1 SHA. This is the single authorized red-first run.

**V-2 — results admission and standalone coverage.** CP-2 selects the witness
and the two existing methods. FreshResultsDir still rejects its occupied results
directory without invoking dotnet and accepts its second stamp. Repeated calls
through the witness and standalone methods allocate different output roots.
Do not remove the CP-1-owned shared stamp before CP-2: success must no longer
depend on that path being empty.

**V-3 — exact slot contract.** SlotGranted passes all seven existing assertions
with the corrected exact build argv. The inner harness continues using its
offline slot shim/dead endpoint; the outer real build/test takes a host build
slot. Three selected TUnit methods mean three results, regardless of internal
PASS counts or repeated wrapper calls.

Run the checkpoint tool once per committed slice group using
`run --plan docs/superpowers/plans/2026-10-20-card-1312-shared-bin-isolation-plan.md --rows CP-1 --expected-source-sha <S1-sha>`
and then CP-2 with the S2 SHA. Use the owner's gated tooling bootstrap and
isolated output procedure; await completion, including `wait` after exit 75.
CP-1's exit 1 is the expected historical red, never a green receipt. CP-2 must
be green with clean source and verified build provenance. Keep unedited
CHECKPOINT lines, counts and generated evidence ignored; run
`scripts/check-evidence-diff.ps1` over Code base..HEAD. Clean only inventoried
task outputs after verification; no source edits while a row is running.

Lane: portable offline script integration, serial, on the effective default
runner. `GET /api/runner-defaults` and `GET /api/session-runners` were read on
2026-10-20: defaults revision 2; the configured default was stale/draining,
and eligible Linux and Windows capacity existed. Re-read at dispatch and let
placement resolve; omit `-Runner` and `-Platform`. No fleet address or host pin
is required. The shim's `windows` override tests argv behavior on either OS.

### Cost

Code budget: **30-45 minutes**, including 16 estimated checkpoint minutes,
14-24 minutes authoring/evidence/cleanup, and up to 5 minutes contingency.
There are no additional mutation cycles in this dispatch. A real slot timeout
or unrelated tool failure is reported separately; do not broaden the selection
or bypass the slot gate to spend the remaining budget.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1312-red/ | portable-script-red | `/*/*/RunCheckpointScriptTests/C1312_FreshResultsDirThenSlotGranted*` | W-1, V-1 | 1 executed, 1 failed at SlotGranted after FreshResultsDir passed; expected red | 1 | 8 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1312-green/ | portable-script-green | `/*/*/RunCheckpointScriptTests/(C1312_FreshResultsDirThenSlotGranted*)\|(C585_FreshResultsDir*)\|(C589_SlotGranted*)` | V-1, V-2, V-3 | all 3 listed, 0 failed, 0 skipped | 3 | 8 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
