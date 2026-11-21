# CARD-1514 land-graph admission review, round 1 (Final)

**Verdict: CLEAN.** `SatelliteResourceLanguages` is pinned to the literal `en`. Every other value tried on the probe and on other projects is refused, and `-Update` cannot write a manifest for those values. The recorded digest matches a fresh evaluation through pass 2 both at the reviewed tip and on a scratch merge with current `origin/master`. No blocker. Two advisories are recorded for cards. Positive controls stay pending.

- Subject: Debug `0a7dbc69-42cb-40d7-b029-e7a4a1719c2e` (Codex). Branch `origin/feat/card-task-0a7dbc69`, tip `c6b3a684115c60772e574ce2c397be9eee283961`.
- Range: `2f3caabcc83d77b043806ab80a8d3d6ef1d95055..c6b3a684115c60772e574ce2c397be9eee283961`.
- Reviewer: Grok. The author and reviewer are from different companies. Mutation is waived (operator 2026-11-12). PCs stay pending.
- `origin/master` at this review: `f6c1de79065d49d76ed363e477d35f10e07bee25`. `git merge-tree --write-tree` produced tree `81b06fa710362bc1077f4de602a37e8aee59a468` with no conflicts. The built scratch merge is detached commit `98efde8653612342b9bcc30741c7c1236e88bfb6` (parents that master tip and the subject tip). It was not pushed.

Commits:

- `424942f299a6cdd8dae8b712d6e97fc00ccc5cf5` witness.
- `59db0f719986ab15067cad12eeb4799e0c56d6b7` pin the literal `en`.
- `1ecef2e25abb1d45e891afb15886167b667eb2d6` refresh the land-graph records.
- `c6b3a684115c60772e574ce2c397be9eee283961` the why-missed note.

## Admission scope

`PinnedProperties` in `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs` maps `SatelliteResourceLanguages` to the single ordinal text `en`. The name is on the allow-list. A definition whose inner text is anything else is refused with "definition file sets a pinned property to a value the audit does not allow", and `-Update` does not write the manifest.

Scratch precheck (`LandGraphPrecheck.Run`, `-Update`, one temporary git repo per case):

| Case | Exit | Manifest |
|---|---|---|
| probe csproj `en` | 0 | written |
| probe `all`, empty, self-closing, `fr`, `$(Configuration)` | 1 | absent; refusal names the probe path |
| `server/Antiphon.Server.csproj` `all` and empty | 1 | absent |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj` `all` | 1 | absent |
| server csproj `en` | 0 | written |
| Antiphon.Tests csproj `en` | 0 | written |
| probe `en` and server `en` together | 0 | written |

The committed witness `Satellite_resource_languages_admits_only_the_reviewed_literal` was re-run at the reviewed SHA (`--timeout 570s`, `--no-build`, load 12.70, lease `00a93bf3-641b-4272-8469-438706168587`). 3 passed, 0 failed: `en` expects 0, `fr` expects 1, `$(Configuration)` expects 1. The witness project path is `App/App.csproj`, so the committed test itself treats `en` on a non-probe project as admitted.

The named danger is closed: a later edit cannot set this property to `all`, empty, another culture, or an MSBuild expression and have the precheck accept it. Adding the literal `en` on another selected project still changes the definition digest, so `-Check` fails until the sanctioned refresh. There is no path allow-list for properties. Path scope exists for the reviewed SDK item removal, which is a different mechanism.

## Probe output

The property was introduced by `b93c459685d670472d17c3ec0941d77c5e2b4f07`, which is an ancestor of the merge base. This branch does not add the filter. A fresh probe output at `tests/Antiphon.ChannelOutbound.Probe/bin/Debug/net9.0` contains 0 `*.resources.dll` files and does contain `Microsoft.EntityFrameworkCore.Design.dll` and `Microsoft.CodeAnalysis.dll`. A with-versus-without rebuild was not run.

The incremental staged directory `tests/Antiphon.Tests/bin/Debug/net9.0/channel-outbound-probe` still contains 16 `*.resources.dll` files (Hangfire cultures and Vanara `fr`) whose timestamps are the package timestamps. `CopyStagedHelpers` copies the new probe assembly and leaves those satellites in place. A clean stage of the current probe output would not include them.

## Records and a later land

The pin diff against the merge base is one new line plus a sort move. The new line is the conditional `System.Management.Automation` package reference:

`tests/Antiphon.Tests/Antiphon.Tests.csproj/Project[1]/ItemGroup[2]/PackageReference[16]0C2F6D5765616E540122185C3B384A6FE097C413D3E14893BCB52A32BD8C1410`

The PtyHost `CopyOwnedPtyHostAppHost` line moved and its hash `BB66B7F90B3027C3DF64B3112F4452FBBAF9A828DA329E05FEC8871720F04F4C` is unchanged. The manifest count stays `80`. The digest is `819858AE650809E79A56F06FEC131EE85FA720D2A05DCF53293403A766F7B8DD`. The evaluation file records that digest, SDK `10.0.404`, and pins hash `b0eae52a5d27d8e818ef815727d4c9e7b7323f5d`.

`scripts/audit-land-verification-graph.ps1 -Check` (leased, no `-Update`):

- At tip `c6b3a684`: exit 0, `digest=819858AE… count=80`, `phase=pass2`, `approved`. Lease `120a3f61-c33a-40b3-9ca5-9ba6e681f9d0`.
- On scratch merge `98efde865` (tree `81b06fa7`, `origin/master` `f6c1de790`): exit 0, the same digest and count, `phase=pass2`, `approved`. Load at start 12.87. Lease `08f4fad1-bbff-42df-abbf-4ba805b96fc8`.

The same approval was also observed on the intermediate merge with `origin/master` `adce1900` before master moved. Definition files on master since the merge base do not change this digest. A later land of another task does not need a further refresh because of this admission.

## C519 transport

`ChannelOutboundUnifiedTransportTests`, one run, `--timeout 1500s` for this row only (the 570s figure is the per-run default). `--no-build` against the reviewed-SHA build. Load at start 12.74. Lease `e00e8bc8-9be5-460e-9677-a99b5166c032` held 1345s. Session duration 22m 18s. Total 17, failed 0, succeeded 17, skipped 0. TRX: `/tmp/c519-transport-4fa334d8/transport.trx`.

| Case | Result |
|---|---|
| `C519_Queue_to_adapter(main)` | Passed |
| `C519_Queue_to_adapter(trailing)` | Passed |
| `C519_Queue_to_adapter(machine)` | Passed |
| `C519_Queue_to_busy_adapter(main)` | Passed |
| `C519_Queue_to_busy_adapter(trailing)` | Passed |
| `C519_Queue_to_busy_adapter(machine)` | Passed |
| `C519_Partial_prompt_requires_complete_receipt` | Passed |
| `C519_Enabled_loss_notice_reaches_adapter` | Passed |
| `C519_Size_refusal` | Passed |
| `C519_Converter_handoff` (8 cuts: busy true/false × four cut points) | Passed |

## Nine-class guard union

Built and run on the scratch merge `98efde865` (current `origin/master` `f6c1de790` plus the subject tip). `7c9d47ba4` is an ancestor of that master. Filter is the documented parenthesized nine-class form. `--timeout 570s`. Load at start 12.06. Lease `7aca9565-46b1-41a2-b331-b48a4d2dffbd`.

Total 84, failed 0, succeeded 84, skipped 0. Duration 2m 15s. TRX: `/tmp/guards-merge-4fa334d8-c/guards.trx`.

The merge build before that run was 0 errors (lease `e19b5d7d-452a-4c6c-954b-d9f5b6dc1799`, load 8.34). An earlier build of the `adce1900` merge also ran 84/84/0; the authoritative row is the `f6c1de790` merge, which includes the later test classes the lane census reads.

## Generated files and selectors

- Derived impact index, two leased runs on the subject tree: both printed `derived-impact windowsOnly count=31 linuxOnly count=1` and `derived-impact-index unchanged`. Leases `8f5a478b-3d4d-49e1-b541-b44a001d7167` and `7a41d042-f73c-4b4c-8ee8-b02899f5e7a4`. The index was not committed.
- `scripts/check-evidence-diff.ps1` over the reviewed range: `commits=4 entries=0 violations=0`, exit 0.
- Impacted-classes kinds: `land-graph-precheck`, `test-lane-category`. The new witness method was executed (3 cases). `TestLaneCategoryGuardTests` is inside the 84. The rest of the partial class `LandGraphEvaluationTests` was not executed; those rows shell full MSBuild evaluation and are outside this round's closed attack list. `windowsFilters` is empty.
- Migrations downgrade selector: the migrations row did not match. Count 0. Not applicable.
- OS-branch witnesses: the diff is the precheck pin, the witness, the three graph record files, and the investigation note. There is no OS-gated production behavior. `scripts/check-os-witnesses.ps1` was not run, and no declaration was invented.

## What was not re-executed

The author's recovery class (claimed 18/18) and `DerivedImpactIndexTests` (claimed 27/27) were not re-run. The required generated-file check is the index regenerate, which was re-run and stayed unchanged. The author's five crash cases were already executed by that task and were not the open row.

## Advisories (record as cards; the land proceeds)

- A-1. The `en` pin applies to every selected project file. Scratch precheck admitted `en` on `server/Antiphon.Server.csproj` and on `tests/Antiphon.Tests/Antiphon.Tests.csproj`, and the committed witness expects exit 0 for `en` on `App/App.csproj`. Non-en values are refused on those paths. A future addition of the literal `en` still changes the digest and fails `-Check` until the sanctioned refresh. Path-scoping the property would be a new mechanism and would contradict this witness.
- A-2. The investigation's "zero satellite DLLs" sentence matches the fresh probe output. The incremental staged `channel-outbound-probe` directory still holds 16 leftover satellite assemblies that `CopyStagedHelpers` did not delete.

## Unlisted runs

Scratch mutant-harness build, the merge-worktree `Antiphon.Tests` builds, and the audit-tool rebuilds inside `-Check` are the runs the brief requires in order to judge admission, the merged guard union, and pass 2. Each build and test took its own build-slot lease. Native default output. `UseAppHost=false`. Rows started only when the 1-minute load was below 14.

--- review evidence ---
subjectTaskId: 0a7dbc69-42cb-40d7-b029-e7a4a1719c2e
reviewedSourceSha: c6b3a684115c60772e574ce2c397be9eee283961
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 2f3caabcc83d77b043806ab80a8d3d6ef1d95055..c6b3a684115c60772e574ce2c397be9eee283961
