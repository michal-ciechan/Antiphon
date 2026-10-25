# CARD-1514: rc defect cards and sibling reconciliation

2026-10-25 UTC. Investigate task `9434ee23-5a8c-4b1f-90a4-59ba50c37c6c`.

Filed three distinct product-defect cards, closed the two whose repairs are published on master, and attached infrastructure evidence to existing owners. Both flagged sibling tasks need **nothing to do** for source integration: their task changes are already present on master and rc. No code was changed and no landing or target-branch publication was attempted.

## Scope and evidence identity

Initial fetched master: `a0b51f75fc93b768c6b3bfce883e3378660c53c9`. Final fetched master: `c5ab17338632422905b4c3029b79abe6c0dd1100`. The intervening commit changes only the CARD-1575 report and two Herdr parity test partials, outside the source comparisons below. Fetched rc/2026-10-24: `a97194ee7a4adf215454fd82c453ba46362596ee`.

Read the complete assigned branch reports using `git show origin/feat/card-task-<id>:<path>`. Task DB projections came from authenticated `GET /api/agent-tasks/{id}`; full responses, duplicate-search results, card writes/readbacks and local diagnostic output are ignored under `.antiphon/evidence-9434ee23/`. Essential facts are retained below so the report does not depend on those generated payloads surviving retirement. No provider credentials or unscoped task list was accessed.

All duplicate searches used `scripts/card.ps1 search <phrase> -Board Antiphon -All -Json` and completed pagination (`truncated=false`). Full `card.ps1 get` reads established the candidate cards' mechanisms. Bodies and long revisions used `-DescriptionFile`; close reasons used `-ReasonFile`. New cards were created in Backlog with separate `rc` and `testing` labels, then the two published repairs were moved to Done.

## Product defects and card writes

| Card | GUID | Final status | Defect / repair ownership |
|---|---|---|---|
| CARD-1578 | `f0090f5b-6beb-4bc8-9ace-22f824c8ea90` | Backlog | Guarded conditional input can deliver a spill pointer without runner file bytes; task afef8c76 |
| CARD-1579 | `e84e6ece-eeee-4960-8380-88eec68c7e22` | Done | Competing drain falsely invalidates the active landing owner's verification ticket; task db4f734b |
| CARD-1580 | `bd57d3a6-2c8f-4db2-98db-749d2b069e9b` | Done | Codex pointer retry remains held after proven pre-input spill failure; task 8c208f93 |

Board GUID: `8988ca03-7414-47ad-b0b6-51556c701703`. Every description retains witness, original fix, landing task, publication evidence and the outstanding rc integration/native Windows rerun obligation. Closing the master repair does not claim rc qualification or deployment.

### CARD-1578 / group 36

**Confirmed mechanism:** `PhoneHomeRunnerClient.SendConditionalInputAsync` bypasses `RemoteSpillCourier`, and the runner's ConditionalInput dispatch omits the existing spill writer. A guarded Claude pointer can reach terminal input without complete runner file bytes. Inspect original witness `1ccecc5295b8af3ad5593cab2057b2fe74e2ace5`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:466` and `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:437`.

The branch report `docs/investigations/2026-10-25-group36-debug.md:32` records two executed, intended assertion failures at that witness: missing complete bytes and missing `RunnerSpillWriteException` for an unwritable destination. Original repair `0a18ad14784c23b48398e04fe15a6e1cccda8f28` carries spill fields and invokes the writer before guarded input; five transport cases passed, including ordinary Input controls. Red TRX SHA-256: `b3766bbdc06c2143754206d4093231359db52cb53fb8757d4597a69fc91dbbd9`.

Stored task row `afef8c76-e830-400a-a034-474477a11d0f`, landing operation `ed39f07f-83de-4e35-8181-0f9e2c15bc18`: phase `Refused`, publication `Unconfirmed`, reason `rebase_conflict`, verified SHA null. The fix is not an ancestor of either target; final master still has the original method without spill transfer. Backlog reason: **close once task afef8c76 land is confirmed**. Both server and runner activation and native Windows rerun remain unverified.

Searches: `guarded spill` 0, `conditional-input` 0, `afef8c76` 0, `spill` 38. Read CARD-0888, CARD-0965, CARD-1014, CARD-1056 and CARD-1576; they concern placement, error visibility, late receipt release or local queue source custody, rather than the missing guarded transport.

### CARD-1579 / group 38

**Confirmed mechanism:** a competing drain records `Held/land_execution_busy` while the original owner retains its genuine execution lease. The Running-only ticket check rejects that owner's unchanged ticket. Branch report `.antiphon/task-db4f734b.md:38` and :42 reconstruct the real-lease witness `87d7064d4615d9e814d3addec382dca911a9dee6`: `C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts` returned `phase=Refused; reason=verification_ticket_changed; publication=Refused`; combined selection 22 passed/1 failed. No execution-lease override or production change was present in the witness.

Original fix `24052d14fcb0aa9736bf495daef9939e06a92238` became landed fix `2ec2ecd544d9affdfe99faf745256eff0e919638`. The latter is an ancestor of master. `server/Application/Services/AgentTaskLandingProtocol.cs:618` contains the narrow Held predicate while retaining ticket/identity checks; the full file blob is identical in original fix and master: `159156771d2fa10b67feaf2206a92ce7617c57f0`. Final affected selection: 30 passed/0 failed/0 skipped, including seven verification-window guards and the unrelated-Held negative case.

Task DB row `db4f734b-0f03-4dbe-b2d6-dfc6a29056c0`, operation `e055bb66-0f9f-406f-beaf-fe6a338e1ba9`: `Landed`, destination `refs/heads/master`, verified and remote SHA `a0b51f75fc93b768c6b3bfce883e3378660c53c9`, confirmed `2026-10-25T00:50:16.371882Z`. Original SHA ancestry fails because landing rebased the repair; landed SHA ancestry and identical content establish publication. The old Running-only predicate remains on rc. Card created then closed with the master publication reason; rc integration with prerequisites and native execution remain owed.

Searches: `verification_ticket_changed` 1, `active landing verification` 0, `db4f734b` 0. Full CARD-1388 owns missing refusal-witness coverage, not this valid-owner false refusal.

### CARD-1580 / group 45

**Confirmed mechanism:** after the runner proves `spill_write_failed_before_input`, `RevertRunAsync` retains a charged attempt and its metadata; Codex recovery then holds the empty composer as possibly consumed input. The branch report `.antiphon/task-8c208f93.md:28` records witness `4327d44312a135cfd9ce0df30ed230cdcf647307`: `C959_Compatible_launch_keeps_model_and_runner` fails after an owned directory blocks the spill file, zero input/submission is established, and removing the fault still produces no recipient receipt. The selection was 7 passed/1 failed.

Original fix `22f56ce3f976259ff0f0556131905c637733c73c` became landed fix `5064a3124eebb6752ca6a62824c4acd8d9210910`, an ancestor of master. `server/Application/Services/SessionMessageQueueService.cs:2762` and :3205 contain the narrowly gated refund. Original fix and master have identical full-file blob `7e409e7e9c3e5797380ca70425d6a93a68a17f3b`. Final selection: 28 passed/0 failed/0 skipped, comprising the end-to-end case and 27 existing C1521 safety cases. The four added metadata assertions were committed before repair but were not separately executed red; no stronger red claim is made.

Task DB row `8c208f93-85f4-4175-a981-78a7ba66da62`, operation `a2d49593-861c-49c4-ab04-ef3aa4e7610c`: `Landed`, destination master, verified/remote `f0c1c658a708b0f0afc932021033288ba13d9eef`, confirmed `2026-10-25T00:39:02.878335Z`. Original SHA ancestry fails after landing's rewrite; landed ancestry/content establish publication. Rc lacks the refund. Card created then closed, retaining rc/native obligations.

Searches: `typed-attempt` 0, `pre-input refusal` 0, `8c208f93` 0, `spill_write_failed_before_input` 3. Read all three hits CARD-1014/0965/0888 plus CARD-1056 and CARD-1521: distinct visibility/placement/release/partial-consumption mechanisms.

## Infrastructure owners: no duplicate cards

**CARD-1217** (`178d533f-bd87-4cab-a13a-10a9b6744d20`): existing Backlog owner titled “Checkpoint receipts cannot validate rows with declared CONDITIONAL skips”. Attached the Linux incident evidence and added separate `rc`, `testing` labels, preserving its existing description. Full card describes the same strict-validator mechanism for Windows symlink capability skips; the platform differs but the root cause does not.

Current `scripts/validate-checkpoint-receipt.ps1:85` rejects a non-command row when `skipped != 0`; :107 rejects it on the single-receipt path. Task DB result `75da8841-5b4f-405d-b058-048f131a4343` records tool run `20261025-003904-2d85`, source `26143a06b4fab5d88b5b9a429f3b3e607f8a0c04`, Herdr parity 19 passed/0 failed/6 contracted Linux skips, validator exit 2 `row_failed`. A deliberately synthetic row with 19/19/0/6 reproduced that same rejection before source validation at this investigation's initial source. It is a control-flow probe, not a qualification receipt. The historical Review asserted clean/full; this investigation does not independently certify that assertion or land eligibility. No validator policy was changed.

**CARD-1573** (`472c5f9b-fce7-4eb2-bf7b-cba3d0917d76`), Backlog, already owns output-inventory refusal above 64 MiB. Attached the 75da8841 incident and current-source counterevidence, added `rc`, `testing`, retained status and history. Search `output-inventory` returned this single hit. Full CARD-1532 owns the overlapping implementation and was also inspected; CARD-1570 concerns a different cleanup/receipt ordering issue.

The stored Review result reports `output-inventory-failed`: test DLL **68,645,376 bytes > 67,108,864**. That historical report is preserved, but the size-only mechanism is **already corrected in current master and rc** by `82f027c50ddacacbdbb577013c843f1e5ef5921c`. `scripts/run-checkpoint.ps1:894` calls `Get-CheckpointFileDigest`; `scripts/lib/checkpoint-file-reader.ps1:196` streams with ContentLimit 1 GiB (:22), while :194 retains the distinct 64 MiB byte/text cap.

Local read-only-source probe created an owned 68,645,376-byte regular file. `Read-CheckpointBytes` rejected it as “selected file exceeds size limit”; production digest helper accepted its full length and SHA-256 `8352ef2384d6c7e89f7a752e2a7c93b230a64b52f29c488e776898ad6214f2ae`. The probe file was removed. This contradicts a claim that every current checkpoint still requires a bypass, without claiming an end-to-end checkpoint passed. Why the older Review checkout still took the failing path remains unresolved; its exact reader identity and original inventory log would resolve it. No second repair was designed.

## C: sibling reconciliation

### 021f53d8 — recommendation: nothing to do

Task DB row `021f53d8-5059-4ae3-9268-89a2a7a33ab7`: Succeeded, task tip `14fc240120d17a4eb6aad222091a6c699123c0f3`, base `5a126f6fc5ae4e16d803b628716b8f0485a37174`. The completed result identifies exactly five task commits and a prior direct rc replay. A Completed event at `2026-10-23T16:16:11.394358Z` says the task branch was kept because no merge target was set; that event is not evidence of missing source.

Both `git cherry origin/master <task-ref>` and `git cherry origin/rc/2026-10-24 <task-ref>` return 281 equivalent and 56 unmatched commits (337 total), reproducing the caller's count. Of the 56 unmatched, **54 predate the task base**; the other two are the large red-witness introduction commits, which have context-adjusted integration commits. This explains why the aggregate patch-id count cannot decide this task's content obligation.

| Original task commit | Integrated commit, ancestor of both master and rc |
|---|---|
| `9e8f527b2482e2dc4122ef1ce57e7f527913e782` | `83128c416968fd6884c3a42ebff3a988fcff2168` |
| `ff10083cf485a57091794dcb680eb8f798fff9ad` | `33ce69c67ca1d4d110c0321a257780479440ed22` |
| `53b2cf3bb840e8f72c04c6a909b36647660ee610` | `0b44a680cf0887dc526668a56034880a5ec00d76` |
| `1ff4caa1d9ac84a7f5354e2590fa73dafc0f99c1` | `3b66b19bb937c2feb8e7a8f3d6b5e406340b94e1` |
| `14fc240120d17a4eb6aad222091a6c699123c0f3` | `8f5840b50d8c392170febe32a2ba2c22de44a955` |

Content inspection covered **all 27 task-changed paths**, not just patch IDs. Thirteen are byte-identical at both targets: summary driver, SessionRunner csproj, probe csproj/writer, custody tests, web-app fixture, nesting tests, landing Git fixture, supervisor tests, redactor and three shared timing support files (ReadOnlyDirectory/attribute/registration). Fourteen differ through subsequent changes:

| Changed path/group | Content disposition at both targets |
|---|---|
| `tests/Shared/TestTiming.cs` | Working recorder remains; later stable test identity, assembly name/module ID and contained sink-failure/disposal handling |
| `scripts/lib/test-phase-evidence.ps1` | Planned writer remains; later live-writer file sharing and explicit native-ID/TRX duration/group joins |
| `scripts/lib/nightly-tests-impl.ps1` | Timing/native-child integration retained; later 2400-second launch-estimate ceiling |
| `scripts/lib/nightly-owned-process.ps1` | Native wrapper retained; later packaged-PowerShell custody resolution |
| `TimingProbeTests.cs`, `C1514Support.cs`, `TestPhaseSummaryTests.cs`, `TestTimingLifecycleTests.cs`, `TestTimingRecorderTests.cs` | Additional plain/async identity, stable assembly, real-Windows/live-reader coverage; earlier assertions remain |
| `TestTimingChokePointTests.cs` | Explicit cache-disabled migration witness; later whole-sample paired overhead, CPU/allocation and recorder-off controls |
| `TestDbFixtureLifecycle.cs` | Later schema-cache implementation; existing create/migrate/clone/drop timing sites retained, migration goes through MigrateSharedAsync |
| `Antiphon.Tests.csproj`, `slow-tests-allowlist.txt`, `docs/testing-and-build.md` | Timing links/contracts retained alongside later helper projects, class allowances and documentation |

Concrete retained sites: recorder enable at `tests/Shared/TestTiming.cs:48` and `TryOpen`; planned-manifest implementation `scripts/lib/test-phase-evidence.ps1:269`; production supervisor call `scripts/lib/nightly-tests-impl.ps1:1058`; DB phase sites `TestDbFixtureLifecycle.cs:42`, :54, :99, :138; host startup/shutdown `AntiphonWebAppFactory.cs:189`, :238; Git spawn/wait `LandingGitFixture.cs:181`, :224; oversized fixture still exactly two 1300-second methods in `TestPhaseSupervisorTests.cs:229` and :252. The labeled no-op guards are absent. Later integration includes `f5723b59d9038427a3990f0d929b980f824b18ca` (Windows reader/identity corrections) and `a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3` (migration witness/cache isolation).

The qualification's claimed active timing is consistent with these source facts; this task did not retrieve or requalify the live run. No task-owned change is missing from master or rc; rc cherry-pick list is **empty**. Do not land the historical branch containing its divergent rc ancestry. Windows CP-8, deferred S3 and pending Mutation obligations from the original result remain outside this integration verdict.

### 089ef857 — recommendation: nothing to do

Task DB row `089ef857-3ae2-4158-91b1-44a7d747d31c`: Succeeded. Its primary progress fields are null, but the completed result names witness `2c4160acf77e9d455330555b657d19c4866899a7` and repair `a121becb6a0240e2f1743ee9948131c54d34b7c8`; the remote branch tip is exactly the latter. Null progress is not absence of a committed repair.

Stored result reconstructs the mechanism: packaged PowerShell's dotnet/test-host descendants escaped its private job and retained pipe writers after a 600-second timeout; drainage consumed another 331 seconds. It records two intended red controls, then 9 passed/0 failed native regression cases. These are historical task results, not new execution here. Its full native report is at `C:\Antiphon\verification\task-089ef857\report.md`, inaccessible from this mirror.

Integrated witness `1f83384379b317a3009df4358ba956907960da91` and repair `94ff5de4b221cd6a74b1453059690005841c23b5` are ancestors of both targets. All **five changed files** are byte-identical across task tip, master and rc: `scripts/fixtures/nightly/owned-child/Program.cs`, `scripts/test-nightly-native.ps1`, `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`, and both `scripts/lib/nightly-owned-process` files. Production C# blob `544d991af9e4bcce2f5bb9819631dced4742945a`; PowerShell blob `6d63261cd8dba7aadb41ae800a8f052f35e60c1a` on all three refs. C# :61 sets the 15-second drain bound; :155 rejects packaged native roots before resume; :200-224 applies the independent drain clock. PowerShell :39 resolves unpackaged PowerShell.

No task-owned source is missing from either target; rc cherry-pick list is **empty**. No `delegate.ps1 -Land` or further pick is needed. The task's historical generated-output/scratch-worktree residue and native continuation/CP-8 obligations are separate; this task did not access or clean that desktop evidence.

## Remaining uncertainties and work not performed

- CARD-1578 publication is still unconfirmed; a successful landing row and target content would resolve it. It remains Backlog, not Done.
- Product repairs on master still owe rc integration and native Windows reruns; these were recorded on every new card. Existing repairs' original reports require ordinary product Review; explicit-caller publication is not an independently validated Review receipt.
- The 75da8841 historical output-inventory failure cannot be attributed to its exact loaded reader from its result alone; raw inventory diagnostics/source identity are needed. The current digest helper accepted the same-size witness.
- No builds, suite runs, native qualification, daemon changes, release scripts, tags, sibling lands or target pushes ran. Two local diagnostic probes produced the expected validator refusal and current digest acceptance. No source mutation, test relaxation or fix implementation was performed.

Not done, noted: no new fix design; CARD-1217 and CARD-1532/CARD-1573 retain their existing implementation/incident ownership.

--- next stage ---
next: none
handoff: Board hygiene complete: CARD-1578 awaits afef8c76 publication; CARD-1579/1580 are fixed on master with rc/native obligations recorded; CARD-1217/1573 reused; both sibling source integrations require nothing further.
artifact: docs/investigations/2026-10-25-card-1514-investigate-and-speed-up-the-slowest-test-classes.md
