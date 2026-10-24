# CARD-1514 state reconstruction — 2026-10-24

Confirmed from stored task rows and Git: five completed branches remain unlanded (34a, 34b, 35a, 35b, 37), all six requested branch merge checks are conflict-free, and 33a already landed. The full native run roster and current execution state cannot be recovered from this Linux mirror. This is a state reconstruction, not a newly established product bug.

## Evidence boundary

- Project: `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`; board: `8988ca03-7414-47ad-b0b6-51556c701703`; card: `c92555be-65d0-45b6-a759-3d1ba5938bd2`. CARD-1514 remains InProgress.

- Fetched origin first; origin/master = `67860d9e35ccaac745f1be37923420c0d2214f56`. API snapshot: `2026-10-24T22:25:07.5285943Z`. Task-detail reads followed within minutes. All checks below use that fixed fetched master, not a moving working tree.

- Read GET /api/agent-tasks?projectId=<project> (6,264 rows, scope envelope/exclusions retained), then includeChecks=true&since=2026-10-22T00:00:00Z (486 rows; no additional relevant specialist tasks). Inventory includes every CARD-1514-bound task, every title beginning Run group, the unbound CARD-1514 recut tasks, and instrumentation Plan e981eae8: 68 rows total. No pagination token is exposed by this task-list route (docs/antiphon-api.md:338).

- Ran card.ps1 history CARD-1514 -Board Antiphon -Json and get; history contains revisions 1–36. Complete board searches with -All: 1514, native Windows, rerun. API detail rows are cited below by full task ID and property; these are stored task/database projections, not newly run tests.

- Generated read evidence is gitignored at `/work/worktrees/task-f6650c17/.antiphon/evidence/f6650c17-state-rebuild/` (tasks.json, all-recent.json, detail-<full-id>.json, inventory.json, focus.json, card-history.json, pipeline.json, pending-board.json, live-board.json, card-search results and origin-branches.txt). Source reports were read from their existing local task mirrors.

- No build/test driver, production/test source edit, dispatch, land, release action, native lock, desktop process or daemon was touched. The investigation Markdown is the only assigned-branch deliverable.

## Immediate reconstruction

- 33a `c3c1d420-a039-4224-946c-f2a7b2d450d6`: request `374fbc22-9ee9-4cd2-a438-d7debef488e0` started at 17:19:22.432480Z and completed. Landing operation `6bf2b1e0-8ce2-4a2e-b293-49df22cd6350` confirms master `67860d9e35ccaac745f1be37923420c0d2214f56` at 17:29:27.428732Z. Source `c1a5f96b149106d94378ebdd592b9239f17dbb10` is not an ancestor because landing rebased four commits. git cherry marks all four patches “-”; merge-tree yields master’s identical tree `04a81fa60918be671d66f510da095cf08bae9a6e`. The saved LandAged notice is historical, not current pending work.

- The same 33a landRequest.notifications stores an Outcome notification `3a35fcd0-ab9f-4775-b84f-6129f3a779f6` as DestinationUnavailable, lastErrorCode=destination_failed, destination session `daec6a72-771d-4a66-9634-fbe474d87464`, confirmedAt=null. An earlier Aged notice is Canceled/queue_canceled_unconfirmed. This reconstructs how successful publication can coexist with an absent caller receipt after the orchestrator session was lost. It does not establish why that session failed.

- 33b `f237ea10-3adb-4cab-a562-cfc094a29ee4` is Landed/Completed at `a7895d00b29dab8833559c4cd46552c7375a67d2`, an ancestor of current master.

- 34b includes a product change, not just fixtures: its stored report identifies a committed public API red witness `db72c9dc41e7b74b99d6810c72bb4cfe9457e723`, repair `8807223fb81cebc8e9406851660e7178b3807910`, and 98/98 Linux passes. It expressly does not claim the fixture-only Review waiver for that product delta. Evidence: /work/worktrees/task-8a0ab444/.antiphon/task-8a0ab444.md:10, :35–39; source at that branch server/Application/Services/SessionMessageQueueService.cs:210, :1080, :1504, :2640. Caller’s product-change gate remains recorded, unapplied here.

- 35a succeeded as a task but its final tests are 26 passed, 4 failed, 32 skipped (exit 2). Four Herdr/Claude expectations still fail with observation-unknown. Evidence: /work/worktrees/task-7cd0d43e/.antiphon/task-7cd0d43e.md:62–78. No green class or repaired native qualification can be claimed.

- 34a’s combined rerun was 124 pass/1 intermittent mirror-request timeout; the exact method and full remote-preparer class subsequently passed 1/1 and 27/27. The report labels load sensitivity a hypothesis, not a confirmed cause (/work/worktrees/task-32a1e275/.antiphon/task-32a1e275.md:16–29).

## Complete task inventory

Each row cites GET /api/agent-tasks/<id>. Delegate is summary.status; publication is landing.publication (Unconfirmed when no landing record); land is landRequest.state (NotRequested when absent). Source uses landing.sourceSha, otherwise Primary progressEvidence, otherwise a report progress marker or 40-hex deliverableRef. “—” means no stored source coordinate, not no work. Remote tip is independently from git ls-remote; a tip equal to a baseline is not evidence of authored changes. Ancestor is git merge-base --is-ancestor <source> 67860d9e35ccaac745f1be37923420c0d2214f56 (yes=0, no=1). Rebased landed sources commonly say no. The last column is landing.remoteSha, independently tested as an ancestor for the group lands.

| Task ID | Group/title | Role | Delegate | Publication / land | Branch | Stored source SHA | Source ancestor? | Origin branch tip (— absent) | Published SHA |
|---|---|---|---|---|---|---|---|---|---|
| 85ca6a8c-81d0-4b46-b199-0dfeafcec583 | — · CARD-1514 slow test profile | Investigate | Succeeded | Landed / Completed | feat/card-task-85ca6a8c | c83766f97d232b991aa20a76ea28e2641805e9a1 | no | c83766f97d232b991aa20a76ea28e2641805e9a1 | c54c0eea50daa7bdd9ccc1da8ff2bc655def1ea2 |
| f4f07e8f-7c05-4e37-a1d0-f75c256719e9 | — · CARD-1514 speed-up plan | Plan | Succeeded | Landed / Completed | feat/card-task-f4f07e8f | d218aa55ec97495c30239c2342bbe191ee64d8f7 | no | d218aa55ec97495c30239c2342bbe191ee64d8f7 | cb87cfc42caf63502205787f51713f935721de56 |
| e8b38df0-705d-45cc-a1dc-985d9a2570a8 | — · Debug build-slot starvation | Debug | Succeeded | Landed / Completed | feat/card-task-e8b38df0 | f98035ec900a2e6eddb9c7259106837845e1b566 | no | f98035ec900a2e6eddb9c7259106837845e1b566 | 0643a3a721cfee6e9934e8fa0b01fddd405b9ce0 |
| 4f0126ba-2bc1-4859-844d-6b1d2bff5136 | — · Debug checkpoint 64MiB stamp refusal | Debug | Succeeded | Landed / Completed | feat/card-task-4f0126ba | e159b7327d618dcac7dd2a14a73efbbafe6a32ab | no | e159b7327d618dcac7dd2a14a73efbbafe6a32ab | 9a9fee9ab34385765bea5e84bb7471fb12891865 |
| 2c5c38b8-548d-40ee-8948-6bb8557b1b5a | — · CARD-1514 S3 | Code | Succeeded | Landed / Completed | feat/card-task-2c5c38b8 | 2178c71f39065c9c85065a265d82390b695520fb | no | 2178c71f39065c9c85065a265d82390b695520fb | 71e578108d6a9604f6f8d7d9f1aae5e45dbd0130 |
| 068a47b3-7a6b-44e1-834a-8f029fbca488 | — · Review CARD-1514 S3 | Review | Succeeded | Unconfirmed / NotRequested | feat/card-task-068a47b3 | 2178c71f39065c9c85065a265d82390b695520fb | no | 2178c71f39065c9c85065a265d82390b695520fb | — |
| f35fcce8-eb5a-4ecb-ad7d-3ba0217c8852 | — · CARD-1514 S1 | Code | Failed | Unconfirmed / NotRequested | feat/card-task-f35fcce8 | d8be87cd21da424c3f6484773168d3923421371a | no | d8be87cd21da424c3f6484773168d3923421371a | — |
| e981eae8-5bc9-4022-950c-6575f4970c64 | — · Plan test timing instrumentation | Plan | Succeeded | Landed / Completed | feat/card-task-e981eae8 | 380cca36c9430663c0d92bcc74f6577b073ef618 | no | 380cca36c9430663c0d92bcc74f6577b073ef618 | f2b0393fe5b1c27db1025adb2a196ce2d9f709e8 |
| 5375ffdf-51be-496e-a3de-c627c0d55285 | — · Docs test performance standards | Docs | Succeeded | Landed / Completed | feat/card-task-5375ffdf | 6115cffd728377d450a9da28e0f514af8973c259 | no | 6115cffd728377d450a9da28e0f514af8973c259 | 7a79cdb33908befbfcfbdd60ad1de57bc4bd2fc1 |
| 021f53d8-5059-4ae3-9268-89a2a7a33ab7 | — · CARD-1514 S1+S2 timing instrumentation (rc direct) | Code | Succeeded | Unconfirmed / NotRequested | feat/card-task-021f53d8 | 14fc240120d17a4eb6aad222091a6c699123c0f3 | no | 14fc240120d17a4eb6aad222091a6c699123c0f3 | — |
| 8395f840-7d2d-481e-ab18-34a64ee4ee50 | — · CARD-1514 timing S1+S2 | Code | Canceled | Unconfirmed / NotRequested | feat/card-task-8395f840 | — | unknown | 380cca36c9430663c0d92bcc74f6577b073ef618 | — |
| 59fe5914-ca0c-4046-b282-ffc60cad9327 | — · CARD-1514 timing instrumentation re-cut onto master | Code | Succeeded | Landed / Completed | feat/card-task-59fe5914 | fe596d62f40d4ee4061c87de729ed6772f352086 | no | fe596d62f40d4ee4061c87de729ed6772f352086 | a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3 |
| a6e98178-f2f4-4d66-806a-7bc0cb0a1618 | — · rc combined tip compile verification | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-a6e98178 | 877b9ce108e6e2bb4ba81840a99c4dd788302ca4 | yes | 877b9ce108e6e2bb4ba81840a99c4dd788302ca4 | — |
| a37ceaf1-7624-4b6a-b9e2-2256b6091202 | — · Windows preflight on rc 12a0b107f (no gate credit) | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-a37ceaf1 | — | unknown | — | — |
| 78334d64-f81a-4388-a7ec-7252641c1645 | — · CARD-1514 Windows timing fixes (CP-8 reds) | Code | Succeeded | Landed / Completed | feat/card-task-78334d64 | 588394e0a95a3ea6baf0492788c8655ec3a25e5f | no | 588394e0a95a3ea6baf0492788c8655ec3a25e5f | ffb1031fab448cc09929f8142b41f4fc46c923c9 |
| ae4df578-c11b-48a0-a15e-cf6d17c89fa1 | — · Debug C1380 persistent red on rc | Debug | Succeeded | Landed / Completed | feat/card-task-ae4df578 | 4497af9b48136a8003def54331f49ac66fd2dec9 | yes | 4497af9b48136a8003def54331f49ac66fd2dec9 | 4497af9b48136a8003def54331f49ac66fd2dec9 |
| 01b04ae0-cc92-4977-bc71-98ffa572037a | — · Refresh stale land-graph manifests (C1380) on master | Code | Succeeded | Landed / Completed | feat/card-task-01b04ae0 | 1ddbd58c7ec7ae16ff2d21a0f5710d49b450b628 | no | 1ddbd58c7ec7ae16ff2d21a0f5710d49b450b628 | 38567feb5c59b0099d10d5f69f60979b0aed9eaf |
| 1cc5d6df-b96e-4879-86fe-c3e43be6cb7b | — · rc forward batch 4 (timing repairs and C1380) | Merge | Succeeded | Unconfirmed / NotRequested | feat/card-task-1cc5d6df | — | unknown | — | — |
| c1441bd3-656a-41ae-85ac-7ac58857e2eb | — · Windows preflight 2 on rc 9e97df3f7 (no gate credit) | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-c1441bd3 | — | unknown | — | — |
| 089ef857-3ae2-4158-91b1-44a7d747d31c | — · Native drain hang in C1380 non-allowlisted row | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-089ef857 | — | unknown | a121becb6a0240e2f1743ee9948131c54d34b7c8 | — |
| 87928ecc-3ed9-4791-bad1-57f367237d4b | — · Unmatched projects in qualification policy (probe projects) | Debug | Failed | Unconfirmed / NotRequested | feat/card-task-87928ecc | — | unknown | 31d6db92ed14fc298e775963e6a5eed9fbba114f | — |
| 9cfdf561-1d4f-4ac2-87a5-1df87e8e81fc | — · rc forward batch 5 (native drain fix) | Merge | Succeeded | Unconfirmed / NotRequested | feat/card-task-9cfdf561 | — | unknown | — | — |
| dc2b4959-409b-4fe8-ad8f-9327e739750c | — · Native drain fix re-cut onto master | Code | Succeeded | Landed / Completed | feat/card-task-dc2b4959 | dfebf941d7d343009bb819cea4d65bb3dca8fbcf | yes | dfebf941d7d343009bb819cea4d65bb3dca8fbcf | dfebf941d7d343009bb819cea4d65bb3dca8fbcf |
| a7b2b64a-d93f-4fc0-8a21-27c436254d07 | — · Unmatched projects fix continuation on desktop | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-a7b2b64a | — | unknown | — | — |
| f311d8a7-5d4a-439a-ad68-320dd497fcf8 | — · a7b2b64a master branch adopt and finish | Custom | Succeeded | Landed / Completed | feat/card-task-f311d8a7 | c47d39950d19c1a5dddacf8dc8ef8b7a24f7f2f9 | no | c47d39950d19c1a5dddacf8dc8ef8b7a24f7f2f9 | b642005b5da05dd80aacd4a9aa6c1e596efa1bbb |
| 296560a2-602e-4676-bc35-ef2072926ad2 | — · rc forward batch 6 (unmatched projects policy) | Merge | Canceled | Unconfirmed / NotRequested | — | — | unknown | — | — |
| aabe84e3-e1ea-43b4-9edc-3be3fbabb626 | — · rc recut from latest master | Merge | Succeeded | Unconfirmed / NotRequested | feat/card-task-aabe84e3 | e4c483654009d1f8224a8734360225f239a24a78 | no | e4c483654009d1f8224a8734360225f239a24a78 | — |
| e492185b-50b2-43cf-add4-c7acd6d7a936 | — · Stuck land holding the repository lease | Debug | Canceled | Unconfirmed / NotRequested | feat/card-task-e492185b | — | unknown | — | — |
| f03d0cfa-8e5e-4793-8da0-ebe88a8aec36 | — · rc/2026-10-24 fast-forward and combined compile | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-f03d0cfa | dfebf941d7d343009bb819cea4d65bb3dca8fbcf | yes | dfebf941d7d343009bb819cea4d65bb3dca8fbcf | — |
| fb7c6043-90ba-4c17-abe8-a77fec346f64 | — · Linux copy-target collision in Pty and E2E projects | Code | Succeeded | Landed / Completed | feat/card-task-fb7c6043 | 7a6a2e82279749250e4afb12a568d67fcda30c8e | no | 7a6a2e82279749250e4afb12a568d67fcda30c8e | e9f0259e5fd07dbe9f50365f07c382839af6fdbd |
| 399d76fc-bc09-46c0-9afe-c7d0ac3cc1a7 | — · Windows preflight 3 on rc/2026-10-24 (no gate credit) | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-399d76fc | — | unknown | — | — |
| 217e7700-11c1-47da-9e1b-a5843ba1b83d | — · Launch full Windows rc qualification on rc/2026-10-24 | Test | Succeeded | Unconfirmed / NotRequested | feat/card-task-217e7700 | — | unknown | — | — |
| 8b9a6a63-d5cb-441e-866f-7824c2a6920b | — · CP-8 overhead bound and startup teardown open log | Debug | Succeeded | Landed / Completed | feat/card-task-8b9a6a63 | 88976d2aecaebe7b2c4e5f641826b291dcf58bf0 | no | 88976d2aecaebe7b2c4e5f641826b291dcf58bf0 | d685550ca417d867c43d523aef3a5a5bd529eb2d |
| 228377d1-36e9-44f8-b8b8-d26e4e5622bf | 2 · Full run group 2: 30 failures | Debug | Succeeded | Landed / Completed | feat/card-task-228377d1 | ceb6b8da3b0c11d062594d84c7b98751a681e99c | yes | ceb6b8da3b0c11d062594d84c7b98751a681e99c | ceb6b8da3b0c11d062594d84c7b98751a681e99c |
| aeb8592f-850d-4cdc-ac9e-2d599560339f | 2 · Resolve merge conflict: Full run group 2: 30 failures | Merge | Succeeded | Unconfirmed / NotRequested | — | — | unknown | — | — |
| 592e4630-a1a2-478a-bf05-f09f576babab | 11 · Full run group 11: 27 C478 land-queue failures | Debug | Blocked | Unconfirmed / NotRequested | feat/card-task-592e4630 | — | unknown | a9275a54390c255a3b6922830ba249d540736146 | — |
| 7f4f9927-973c-460b-9ca8-da62c9df8d1d | — · server2 stale though healthy and reachable | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-7f4f9927 | — | unknown | — | — |
| 5e38fb0f-f27f-4902-9113-b07caa20309f | — · Runner eligibility latch after heartbeat lease expiry | Code | Failed | Unconfirmed / NotRequested | feat/card-task-5e38fb0f | 24516d74cdcb47d40fc647039e1e9a47330e5c50 | no | 24516d74cdcb47d40fc647039e1e9a47330e5c50 | — |
| 6c76c765-4bb9-49d8-8f34-c80a7c85cad4 | 16 · Full run group 16: 64 message delivery failures | Debug | Succeeded | Landed / Completed | feat/card-task-6c76c765 | 6b425461510f6184718ed8dea7ed97f3fe4aadc6 | no | 6b425461510f6184718ed8dea7ed97f3fe4aadc6 | 48f5f1b1a89de07063db8fd956acec40a4bcf326 |
| 2c9106ba-e6eb-454a-b2c6-fba108646074 | 11 · group 11 fixture fix adopt and finish | Custom | Succeeded | Landed / Completed | feat/card-task-2c9106ba | a9275a54390c255a3b6922830ba249d540736146 | yes | a9275a54390c255a3b6922830ba249d540736146 | a9275a54390c255a3b6922830ba249d540736146 |
| 4cd09059-f8f5-47c4-a564-ccf32469abc9 | — · Sweep: guarded Claude observation missing in delivery fixtures | Debug | Succeeded | Landed / Completed | feat/card-task-4cd09059 | 1b835265bd94a17a2388999c15772226831b77bb | no | 1b835265bd94a17a2388999c15772226831b77bb | ca250de0b48ca222cacdbaa3a3793ac0993c521f |
| 95b7c2dd-3165-4354-9a51-2203ef50d72d | 19 · Full run group 19: C1349 stale inspection adopted checkout | Debug | Blocked | Unconfirmed / NotRequested | feat/card-task-95b7c2dd | 51cb95240a505caf35eeb7c7aac5f554e899e1e3 | no | 51cb95240a505caf35eeb7c7aac5f554e899e1e3 | — |
| fd07317d-3374-4bf0-8164-fbb17d217e84 | 20 · Full run group 20: detached fix verification C448/C488/C494 | Debug | Succeeded | Landed / Completed | feat/card-task-fd07317d | 50480f0935cbe45044bc7bf9f038129ec3922bf0 | no | 50480f0935cbe45044bc7bf9f038129ec3922bf0 | a196b9fe772edf94b1514bb97bd79f493dbda052 |
| d2a0a930-6e80-4c2d-9457-1a12717d25e3 | 21 · Full run group 21: spawned child brief-gated receipt | Debug | Succeeded | Landed / Completed | feat/card-task-d2a0a930 | f566d28c6c8ad01d983e13d111a10f8766976fea | no | f566d28c6c8ad01d983e13d111a10f8766976fea | c9dc34ed2f038cfff72106c3b17a04cdbff5bf3c |
| 4a602395-c553-44a6-8fc1-9816e9bf899e | — · Adopt 5e38fb0f: runner eligibility latch fix | Merge | Succeeded | Landed / Completed | feat/card-task-4a602395 | 24516d74cdcb47d40fc647039e1e9a47330e5c50 | no | 24516d74cdcb47d40fc647039e1e9a47330e5c50 | 11ea992b97c93c38bb72859b6b4153009a2da2a4 |
| 920190ea-605d-409b-9ff4-134010bb16bc | 24 · Full run group 24: C1349 inspection warnings and pre-dispatch recovery | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-920190ea | 672be075c3c494445338c2fdf417a55e5501ab53 | no | 672be075c3c494445338c2fdf417a55e5501ab53 | — |
| 867c8362-5e11-4b63-a127-41f2668bd619 | — · Adopt 95b7c2dd: C1349 path fix | Merge | Succeeded | Landed / Completed | feat/card-task-867c8362 | 51cb95240a505caf35eeb7c7aac5f554e899e1e3 | no | 51cb95240a505caf35eeb7c7aac5f554e899e1e3 | 3d2ffccc4498838b71d65ae9dfabe128a81c2975 |
| 2de763a6-d4d3-46ab-8135-d1431765e1b1 | 25 · Full run group 25: reply integration and check-note delivery (residue) | Debug | Succeeded | Landed / Completed | feat/card-task-2de763a6 | 49f1e7b022fe5bd594a9eb06af9573b09e18fbcb | no | 49f1e7b022fe5bd594a9eb06af9573b09e18fbcb | c2e35da7c1ae24792b1f115c7f47996f7463f625 |
| 409d401c-d141-40d9-a1bb-ed42bc2ca7c4 | 26 · Full run group 26: wedged head, launch failure (residue) | Debug | Succeeded | Landed / Completed | feat/card-task-409d401c | e970c2c9ff2f723dd1cccf130e61901eeca5f200 | yes | e970c2c9ff2f723dd1cccf130e61901eeca5f200 | e970c2c9ff2f723dd1cccf130e61901eeca5f200 |
| df70be52-02f4-402b-a7f8-57ea95720952 | 27 · Full run group 27: output distillation, pending settlement delivery | Debug | Succeeded | Landed / Completed | feat/card-task-df70be52 | e143ad0f754cc4d9afa313a857d49f4946c91cda | no | e143ad0f754cc4d9afa313a857d49f4946c91cda | 2cc2617ead0e0a71543b11f27a9dddea076a7beb |
| ed7af4f3-b016-44c6-a7a3-d72c1e3e3140 | 27 · Full run group 27: land graph evaluation, land verifier | Debug | Succeeded | Landed / Completed | feat/card-task-ed7af4f3 | 9195917a8159d047d47c235d673c9e4490264ee1 | no | 9195917a8159d047d47c235d673c9e4490264ee1 | ec5123c39db2a03e4b51a2abaebc74db17c32d9b |
| f7289c48-1236-423b-b61e-413c02080662 | 28 · Full run group 28: session routing, channel bridge, codex startup delivery | Debug | Canceled | Unconfirmed / NotRequested | feat/card-task-f7289c48 | — | unknown | e970c2c9ff2f723dd1cccf130e61901eeca5f200 | — |
| 6d1c77a9-7d77-4e20-9663-a024ff68a2e2 | 28 · Full run group 28: session routing, channel bridge, codex startup delivery | Debug | Succeeded | Landed / Completed | feat/card-task-6d1c77a9 | 971e894099e44bfe96546511967d663563073026 | no | 971e894099e44bfe96546511967d663563073026 | e0893861ee15129ed280f800c2b3d915f25e5a8f |
| 62a77bb0-8234-4eb9-ba6d-6d57db5d04a8 | 29 · Full run group 29: channel prompt correlation, data retention, check sweep | Debug | Succeeded | Landed / Completed | feat/card-task-62a77bb0 | b83d5c7583a7203d838d20624fbbe9865f403acd | no | b83d5c7583a7203d838d20624fbbe9865f403acd | 0e64a6aba1b4e28052faebfac88f01232b4cb66b |
| b76d2a8c-b1f5-488f-a2fc-51e4cb789f49 | 30a · Full run group 30a: herdr label follow, compaction receipt, rc maintenance queue | Debug | Succeeded | Landed / Completed | feat/card-task-b76d2a8c | 99bd60f861eab6ffdcb98d5a4a006719ff4d97bc | yes | 99bd60f861eab6ffdcb98d5a4a006719ff4d97bc | 99bd60f861eab6ffdcb98d5a4a006719ff4d97bc |
| 40bb57ba-4296-45c9-ad4e-12a1b3349186 | 28b · Run group 28b: branch, repair, land evidence, park | Debug | Succeeded | Landed / Completed | feat/card-task-40bb57ba | df3af6d01e53ea17fe79d13aca648eb972c59cc2 | no | df3af6d01e53ea17fe79d13aca648eb972c59cc2 | 5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82 |
| b8c3a4cd-f3ef-4261-b9ea-be92544a89ba | 30b · Run group 30b: routing, outbound, review loop | Debug | Succeeded | Landed / Completed | feat/card-task-b8c3a4cd | 74b9af96bce67731cb61f0f2ac8504a8a97a8e52 | no | 74b9af96bce67731cb61f0f2ac8504a8a97a8e52 | 828f17ac2fc3d69707b56331c74ec81006428872 |
| d55dd41d-c13a-4065-b3c9-549a3a09a7fd | 31a · Run group 31a: specialist identity, phone-home, unverified | Debug | Succeeded | Landed / Completed | feat/card-task-d55dd41d | f8a62534de8dbd42d107133c1651ad73fa25d18f | no | f8a62534de8dbd42d107133c1651ad73fa25d18f | a28f1faa9dfcdee08b28f3d60c6fe530592194c4 |
| 74315dd5-8a75-44ce-8ec8-4898b4b43169 | 31b · Run group 31b: land refusal witness, receipt failure | Debug | Succeeded | Landed / Completed | feat/card-task-74315dd5 | d4f7bc5c54a2d67005d68fe6c041a820e263d089 | no | d4f7bc5c54a2d67005d68fe6c041a820e263d089 | be55a999b1a8de651eaf85e642257eef356287cc |
| cea2c546-f4a2-41db-9972-67c6ee452eda | 32 · Run group 32: seat release, mid-turn refine, deferral | Debug | Succeeded | Landed / Completed | feat/card-task-cea2c546 | 97390bf4c7d9effb0e31ebf7e0bc350218d71832 | no | 97390bf4c7d9effb0e31ebf7e0bc350218d71832 | dc1a2184526085b8f4305878a3804f9cec41daad |
| c3c1d420-a039-4224-946c-f2a7b2d450d6 | 33a · Run group 33a: rolling runner, transcript hot path | Debug | Succeeded | Landed / Completed | feat/card-task-c3c1d420 | c1a5f96b149106d94378ebdd592b9239f17dbb10 | no | c1a5f96b149106d94378ebdd592b9239f17dbb10 | 67860d9e35ccaac745f1be37923420c0d2214f56 |
| f237ea10-3adb-4cab-a562-cfc094a29ee4 | 33b · Run group 33b: system prompt launch, report store | Debug | Succeeded | Landed / Completed | feat/card-task-f237ea10 | 30649ed0dca92720ec1a836d561ad1345ba073fd | no | 30649ed0dca92720ec1a836d561ad1345ba073fd | a7895d00b29dab8833559c4cd46552c7375a67d2 |
| 32a1e275-f87f-4387-90c0-239a219d982b | 34a · Run group 34a: workspace preparer, mutation admission | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-32a1e275 | ec7f3beb13d7adea8355e016a090428102207e7f | no | ec7f3beb13d7adea8355e016a090428102207e7f | — |
| 8a0ab444-0ef3-4097-8b8d-516910c5c726 | 34b · Run group 34b: message queue service, reply durability | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-8a0ab444 | cf1cbb3d8c89549107044626eefd40923cad7f68 | no | cf1cbb3d8c89549107044626eefd40923cad7f68 | — |
| 22bd72e0-f737-43db-a619-e729aa315406 | 37 · Run group 37: dispatch projection, alarms, sweeps | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-22bd72e0 | 8af8db2df2355d574c18f12a2f23cbfab8b7c3db | no | 8af8db2df2355d574c18f12a2f23cbfab8b7c3db | — |
| 991f01d8-8cc7-45bd-a77d-5cc6ace5fde5 | 35b · Run group 35b: bundle launch, policy refresh, singles | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-991f01d8 | 02880afba2d6600c93abc8fc2212ecc75bb2ad53 | no | 02880afba2d6600c93abc8fc2212ecc75bb2ad53 | — |
| 7cd0d43e-4184-4ef1-98fc-e8e5299e1bf2 | 35a · Run group 35a: receipt plumbing, herdr parity, pty queue | Debug | Succeeded | Unconfirmed / NotRequested | feat/card-task-7cd0d43e | bbfbdd278d3e024c6169971f90ef333b2c1c27c4 | no | bbfbdd278d3e024c6169971f90ef333b2c1c27c4 | — |
| f6650c17-6957-4ba3-ad9f-d9ef6ca1461a | — · CARD-1514 state rebuild | Investigate | Dispatched | Unconfirmed / NotRequested | feat/card-task-f6650c17 | — | unknown | 67860d9e35ccaac745f1be37923420c0d2214f56 | — |

## Six requested branches: containment, files and conflicts

Commands: git merge-base --is-ancestor <source> <master>; git merge-tree --write-tree <master> <source>; git cherry <master> <source> <declared-base>; git diff --name-only <merge-base(master,source)> <source>. Using the actual merge base avoids attributing already-landed sibling commits to 34b, whose report records an authorized fast-forward before its first build. None of these commands checked out, rebased or merged a branch.

| Group | Task ID | Source SHA | Outcome at master | merge-tree exit / tree |
|---|---|---|---|---|
| 34b | 8a0ab444-0ef3-4097-8b8d-516910c5c726 | cf1cbb3d8c89549107044626eefd40923cad7f68 | Still unlanded; task patches marked + | 0 / 9b018cc4d6ebe61dad439634f841019f100feaf9 |
| 35b | 991f01d8-8cc7-45bd-a77d-5cc6ace5fde5 | 02880afba2d6600c93abc8fc2212ecc75bb2ad53 | Still unlanded; task patches marked + | 0 / 05f361c264e7291715e2ab1e910412f0942ae9c7 |
| 37 | 22bd72e0-f737-43db-a619-e729aa315406 | 8af8db2df2355d574c18f12a2f23cbfab8b7c3db | Still unlanded; task patches marked + | 0 / 47d32284c2771f2378be9732b37f5b17c6afd3ef |
| 35a | 7cd0d43e-4184-4ef1-98fc-e8e5299e1bf2 | bbfbdd278d3e024c6169971f90ef333b2c1c27c4 | Still unlanded; task patches marked + | 0 / 837c0b496399909ca95a87b40db2654a498bff2e |
| 33a | c3c1d420-a039-4224-946c-f2a7b2d450d6 | c1a5f96b149106d94378ebdd592b9239f17dbb10 | Already landed; all four patches equivalent | 0 / 04a81fa60918be671d66f510da095cf08bae9a6e |
| 34a | 32a1e275-f87f-4387-90c0-239a219d982b | ec7f3beb13d7adea8355e016a090428102207e7f | Still unlanded; task patches marked + | 0 / e1478f18ecb089a6ea85cc9c1e770f0c8522a97e |

Files for Run group 34b: message queue service, reply durability (`8a0ab444-0ef3-4097-8b8d-516910c5c726`), delta base `dc1a2184526085b8f4305878a3804f9cec41daad`:

- `.antiphon/task-8a0ab444.md`
- `server/Application/Services/SessionMessageQueueService.cs`
- `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs`

Files for Run group 35b: bundle launch, policy refresh, singles (`991f01d8-8cc7-45bd-a77d-5cc6ace5fde5`), delta base `5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82`:

- `.antiphon/task-991f01d8.md`
- `tests/Antiphon.Tests/Application/DelegateBundleLaunchTests.cs`
- `tests/Antiphon.Tests/Application/PolicyRefreshServiceTests.cs`
- `tests/Antiphon.Tests/Application/ReceiptScanDestinationIntegrationTests.cs`
- `tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.Delivery.cs`
- `tests/Antiphon.Tests/Application/WorktreeDefaultContinuationTests.cs`

Files for Run group 37: dispatch projection, alarms, sweeps (`22bd72e0-f737-43db-a619-e729aa315406`), delta base `dc1a2184526085b8f4305878a3804f9cec41daad`:

- `docs/investigations/2026-10-24-group-37-fixture-repairs.md`
- `tests/Antiphon.Tests/Application/AgentTaskLandQueueAgingTests.cs`
- `tests/Antiphon.Tests/Application/RunnerAlarmDeliveryTests.cs`
- `tests/Antiphon.Tests/Application/ScheduleSweepTests.cs`

Files for Run group 35a: receipt plumbing, herdr parity, pty queue (`7cd0d43e-4184-4ef1-98fc-e8e5299e1bf2`), delta base `5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82`:

- `.antiphon/task-7cd0d43e.md`
- `src/Antiphon.FakeClaude/Program.cs`
- `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`
- `tests/Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs`

Files for Run group 33a: rolling runner, transcript hot path (`c3c1d420-a039-4224-946c-f2a7b2d450d6`), delta base `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc`:

- `tests/Antiphon.Tests/Application/ContinuationSettlementTests.cs`
- `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerDrainTests.cs`
- `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs`
- `tests/Antiphon.Tests/Application/TranscriptHotPathMigrationTests.cs`
- `tests/Antiphon.Tests/TestHelpers/StandingBootWatchFixture.cs`

Files for Run group 34a: workspace preparer, mutation admission (`32a1e275-f87f-4387-90c0-239a219d982b`), delta base `be55a999b1a8de651eaf85e642257eef356287cc`:

- `.antiphon/task-32a1e275.md`
- `tests/Antiphon.Tests/Application/AgentBundleAttachmentTests.cs`
- `tests/Antiphon.Tests/Application/AgentTaskWorktreeContinuityTests.cs`
- `tests/Antiphon.Tests/Application/AgentWorkspaceProvisionerTests.cs`
- `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs`
- `tests/Antiphon.Tests/Application/InterimVerificationLandGuardTests.cs`
- `tests/Antiphon.Tests/Application/MutationAdmissionTests.cs`

There are zero direct path overlaps among these six branch deltas. 35a changes the shared fake provider src/Antiphon.FakeClaude/Program.cs, so other tests may consume it, but that is not a file collision. Pairwise/future landing compatibility is not inferred from the six independent master merge checks. No fix design was produced.

33a rebase mapping, reconstructed by equal stable patch IDs in the two four-commit series:

| Source | Published master |
|---|---|
| 4d977f62e5226d6f805f708b417313c5896ad0dd | 5d05f4bf78ceecc19763b142a0e0179d8d18c917 |
| 794ef99ab8a183d1bd26f952ddc85769a6a02673 | de6c8438b368306e96435cebd899494caf128eb4 |
| 779b3b94e8a91d279e8e2561d42de3bf56de0fc1 | 42f540c7471dde570b481d9aea28fa084c05381b |
| c1a5f96b149106d94378ebdd592b9239f17dbb10 | 67860d9e35ccaac745f1be37923420c0d2214f56 |

## Run groups: dispatched work versus native execution

These are two distinct records: repair task completion/publication, and the detached Windows qualification’s group execution. No new native run status was read. Task `217e7700-11c1-47da-9e1b-a5843ba1b83d` stores the launch of run `a779085c09ec4397a4b49941fd3cad07` at candidate `a97194ee7a4adf215454fd82c453ba46362596ee`, supervisor PID 166312, start 2026-10-24T07:20:22.2178776Z. Its launch report records 175 Antiphon groups (14.645 estimated serial hours), group 1 passed 94/94 and group 2 executing at handoff. Later Debug goals prove failures were reported through group 37. They do not establish current process liveness.

| Groups (complete numeric partition of reported 1–175) | Recorded repair dispatch / publication | Remaining evidence |
|---|---|---|
| 1 | Launch report: native 94/94 passed; no repair task | Current run receipt unavailable |
| 2 | 228377d1 landed; aeb8592f merge helper succeeded | Native rerun owed |
| 3–10 | No numbered repair dispatch in the complete task list | Native outcomes/membership unavailable; absence of dispatch does not prove pass |
| 11 | 592e4630 still Blocked; 2c9106ba adoption Landed/Completed | Native 67-row rerun; historical cleanup custody remains unverified here |
| 12–15 | No numbered repair dispatch | Native outcomes/membership unavailable |
| 16 | 6c76c765 Landed/Completed | Native rerun report is desktop-only |
| 17–18 | No numbered repair dispatch | Native outcomes/membership unavailable |
| 19 | 95b7c2dd still Blocked; 867c8362 adoption Landed/Completed | Native 15-row rerun |
| 20 | fd07317d Landed/Completed | Native rerun owed |
| 21 | d2a0a930 Landed/Completed | Native rerun owed |
| 22–23 | No numbered repair dispatch | Native outcomes/membership unavailable |
| 24 | 920190ea succeeded, evidence-only unlanded branch; sibling 4cd09059 Landed/Completed supplies code | Native two-row selections owed; evidence report itself not published on master |
| 25 | 2de763a6 Landed/Completed | Native rerun owed |
| 26 | 409d401c Landed/Completed | Native rerun owed |
| 27 | df70be52 and ed7af4f3 both Landed/Completed | Native reruns owed |
| 28 | f7289c48 canceled; replacement 6d1c77a9 and split 28b 40bb57ba Landed/Completed | Native reruns owed |
| 29 | 62a77bb0 Landed/Completed | Native reruns owed |
| 30 | 30a b76d2a8c and 30b b8c3a4cd Landed/Completed | Native reruns owed |
| 31 | 31a d55dd41d and 31b 74315dd5 Landed/Completed | Native reruns owed |
| 32 | cea2c546 Landed/Completed | Native six-class rerun owed |
| 33 | 33a c3c1d420 and 33b f237ea10 Landed/Completed | Native nine class rows owed |
| 34 | 34a 32a1e275 and 34b 8a0ab444 succeeded, unlanded, no land requested | Seven + five native class rows; 34b product gate |
| 35 | 35a 7cd0d43e and 35b 991f01d8 succeeded, unlanded, no land requested | Four + six native class rows; 35a remains red |
| 36 | No numbered repair dispatch | Need actual Windows outcome/membership before declaring a missing repair task |
| 37 | 22bd72e0 succeeded, unlanded, no land requested | Ten native class rows owed |
| 38–175 | No numbered repair dispatch | Native progress/membership unavailable; cannot label not-yet-run or green |

The plans do not contain a fixed 175-row run roster. docs/investigations/2026-10-23-rc-rerun-failed-groups-first.md:24–29 explains that numbers come from the exact run’s discovery/cost inputs. CARD-1514 slow-test-speedups-plan.md:142–296 describes optimization S1–S4, not native numbered repair groups; timing-instrumentation-plan.md:565–580 requires run manifests. Substituting an old plan or Linux discovery would fabricate membership.

For the complete named roster and undispatched/running status, the missing files are C:\Antiphon\releases\task-217e7700\group-order-estimates.csv and C:\Antiphon\releases\candidates\rc-2026-10-24-a97194ee7a4a\logs\2026-10-24-0720-a779085c\timing\groups-planned.json / groups-executed.json, plus summary.json and current group logs/TRX. The feed is C:\Antiphon\releases\task-217e7700\commission-console.log. A read-only Windows investigation of those exact files resolves group 36 and 38–175; no valid dispatch brief for an unknown failure can be named here. Existing groups already have their full briefs in the cited task goal rows and reports.

## Live tasks, pending lands and effective pipeline

Board-scoped GET /api/agent-tasks?boardId=<board>&status=Working,Blocked,Queued,Dispatched&includeChecks=true returns only:

| Task ID | Group | Role/status | Current meaning |
|---|---|---|---|
| 592e4630-a1a2-478a-bf05-f09f576babab | 11 | Debug / Blocked | Original report blocked by approval-policy rejection of 33 Windows output-directory cleanup paths. Adoption 2c9106ba already published its code. No fresh cleanup assertion. |
| 95b7c2dd-3165-4354-9a51-2203ef50d72d | 19 | Debug / Blocked | Original brief’s contradictory rebase instructions. Adoption 867c8362 already published its code. |
| f6650c17-6957-4ba3-ad9f-d9ef6ca1461a | State reconstruction | Investigate / Dispatched | This investigation; no repair task is Working or Queued. |

Board-scoped landPending=true returns one unrelated pending row: `f4fd378a-75cf-4e60-b57a-db7bdf0ee62e` / CARD-1349, “Implement dispatch sibling walk fix”. None belongs to CARD-1514. All CARD-1514 detail land requests are Completed or absent.

Pipeline snapshot 2026-10-24T22:25:07.5285943Z: mode LegacyOpen (default); project parallel cap 12 (project override revision 4), open=1, parallel=1, queued=0, remaining=11. Historical Blocked task rows are shown separately from that occupancy. Global revision=1; no queued caps are set.

| Role | Effective parallel cap / source | Open / parallel / queued | Remaining |
|---|---|---|---|
| Custom | unbounded role; combined 12 / default | 0 / 0 / 0 | role unbounded |
| Plan | 3 / default | 0 / 0 / 0 | 3 |
| Code | 5 / default | 0 / 0 / 0 | 5 |
| Review | 5 / default | 0 / 0 / 0 | 5 |
| Debug | 4 / project | 0 / 0 / 0 | 4 |
| Coverage | 1 / default | 0 / 0 / 0 | 1 |
| Docs | 1 / default | 0 / 0 / 0 | 1 |
| Commit | 1 / default | 0 / 0 / 0 | 1 |
| Test | 1 / default | 0 / 0 / 0 | 1 |
| Deploy | 1 / default | 0 / 0 / 0 | 1 |
| Merge | 1 / default | 0 / 0 / 0 | 1 |
| Investigate | 3 / default | 1 / 1 / 0 | 2 |
| TestDesign | 1 / default | 0 / 0 / 0 | 1 |
| Mutation | 1 / default | 0 / 0 / 0 | 1 |
| Host (fleet totals) | In flight / effective limit | Remaining | Source |
|---|---|---|---|
| local | 0 / 2 | 2 | config |
| server2 | 2 / 10 | 8 | runner |
| server2-temp | 0 / unknown | unknown | runner |

The legacy maxConcurrentTasks=2 and inFlightAgainstCap=0 fields describe the local host only (docs/antiphon-api.md:515). Desktop and server2 are available/eligible; server2-temp is unavailable/stale. Runner defaults revision 2 selects server2. Snapshot capacities are observations, not reservations or authorization to change settings.

## Native Windows debt and tracking

None of the late group repair reports supplies post-repair native Windows receipts. Their run a779085c stayed on the old pinned candidate, so a repair’s Linux pass or master land does not satisfy the native rerun. The mandatory handoffs are retained below, with earlier per-task original excerpts afterward.

| Group/task | Exact native obligations | Recorded tracking / limitation |
|---|---|---|
| 11 / 592e4630, adoption 2c9106ba | /*/*/PostLandMutationDeliveryTests/* — 67 cases | Both stored task reports; original Blocked row still open |
| 19 / 95b7c2dd, adoption 867c8362 | /*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch* — 15 rows | Both stored task reports; original Blocked row still open |
| 24 / 920190ea | PreDispatchFailureDeliveryTests/*; DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller* | Source repair is landed sibling 4cd09059; this evidence-only branch remains unlanded |
| 33a / c3c1d420 | PhoneHomeRollingRunnerTests (30), TranscriptHotPathMigrationTests (11), StandingBootWatchdogTests (31), ContinuationSettlementTests (10), each /*/*/<Class>*/* | Four native rows in original report, after land |
| 33b / f237ea10 | AgentSystemPromptLaunchTests, ExpectationDirectDeliveryTests, AgentTaskLandPreparationIdentityTests, AgentReportStoreTests, LandRequestWriteDiagnosticTests | Five rows in original report |
| 34a / 32a1e275 | RemoteWorkspacePreparerTests (27), MutationAdmissionTests (25), AgentTaskWorktreeContinuityTests (8), AgentBundleAttachmentTests (24), InterimVerificationLandGuardTests (13), AgentWorkspaceProvisionerTests (24), CheckpointSourceApprovalTests (4); /*/Antiphon.Tests.Application/<Class>/* | Seven rows; preserve combined mirror timeout |
| 34b / 8a0ab444 | SessionMessageQueueServiceTests (26), OutputDistillationApplyRaceTests (28), ChannelReplyDurabilityTests (25), AgentTaskLandHeldNotificationTests (12), SessionMessageQueueSpillTests (7); /*/*/<Class>*/* | Five rows; public-path product witness is included; caller product gate recorded |
| 35a / 7cd0d43e | SessionQueueReceiptPlumbingTests (16 Windows-only rows), SessionMessageQueuePtyIntegrationTests (8), RemoteControlRecoveryPtyIntegrationTests (2 real ConPTY rows), HerdrAlwaysOnChannelParityTests (6 Linux-skipped rows plus parity of repaired history) | Four class selections; four Herdr/Claude failures remain visible, separately owed acceptance-contract follow-up |
| 35b / 991f01d8 | N-1 DelegateBundleLaunchTests (24); N-2 PolicyRefreshServiceTests (24); N-3 CardFilePrivacySyncTests (23); N-4 StandingSessionSwitchConcurrencyTests (24); N-5 ReceiptScanDestinationIntegrationTests (23); N-6 WorktreeDefaultContinuationTests (8); exact /*/*/<Class>/* | All six stored in report; N-2 original policy failures and N-3 privacy failure were not diagnosed by Linux green |
| 37 / 22bd72e0 | N37-1 PhoneHomeTaskDispatchProjectionTests (18); N37-2 RunnerAlarmDeliveryTests (9); N37-3 RemoteControlRecoveryTests (17); N37-4 RunnerSeatOrphanSweepTests (17); N37-5 ContextCompactionSweepTests (18); N37-6 CardWorktreeCleanupRecoveryTests (6); N37-7 AgentTaskLandQueueAgingTests (9); N37-8 PhoneHomeQueuedTurnTests (17); N37-9 ScheduleSweepTests (18); N37-10 ChannelOutboundRecoveryTests (18); /*/Antiphon.Tests.Application/<Class>/* | Ten rows in docs/investigations/2026-10-24-group-37-fixture-repairs.md:19–34 on its unlanded branch |

No dedicated post-repair native-rerun task for these late groups appears in the complete scoped task inventory or the recent includeChecks inventory. CARD-1514 is still their parent work record; its 36 card-history revisions do not enumerate N-1…N-6. Complete 1514/native Windows/rerun card searches found no explicit companion owning all these obligations. This establishes no discovered tracker, not proof against an unlinked card with different wording. CARD-0865 (Backlog), read in full, already records older standing-history/parity failures and Linux skip debt; its description does not record this 35a four-case observation-unknown finding or N-1…N-6. No tracker was created or changed.

### Earlier original native handoffs

These unedited excerpts preserve the prior task’s native scope. Each citation is an existing source report file and line; reports may describe execution commands for a future commissioned owner. No command in an excerpt was run by this investigation. Desktop-only group 2/16 reports and their full native inventories are unavailable here; their stored result links are C:\Antiphon\verification\task-228377d1\report.md and C:\Antiphon\verification\task-6c76c765\windows-rerun.md.

Stored `fd07317d-3374-4bf0-8164-fbb17d217e84` / Full run group 20: detached fix verification C448/C488/C494 — `/work/worktrees/task-fd07317d/.antiphon/task-fd07317d.md:31`:

> Native rows to rerun next candidate, with exact method filters ending in *:
> - /*/*/AgentTaskLandConcurrencyControlledTests/C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget* (10 cases, especially source-switch).
> - /*/*/AgentTaskLandSourceFreshnessTests/C488_DetachedFollowUpPublishesReviewedFix*
> - /*/*/AgentTaskLandSourceFreshnessTests/C494_DetachedFixVerifierFailurePreventsPublication*

Stored `d2a0a930-6e80-4c2d-9457-1a12717d25e3` / Full run group 21: spawned child brief-gated receipt — `/work/worktrees/task-d2a0a930/.antiphon/task-d2a0a930.md:28`:

> ## Next native candidate
>
> Rerun AgentTaskCommitEndpointTests.Spawned_child_complete_brief_gated_operation_and_parent_receipt(False) and (True) using the exact filter above on native Windows. Linux evidence does not credit native Windows qualification. The desktop qualification, supervisor PID 166312, logs and native lock were not accessed or changed; Windows logs were unavailable from this mirror, so the brief's permitted Linux reproduction route was used. No Review per operator instruction; no tags or release scripts.
>
> For a fresh output, build under `pwsh -NoProfile -File scripts/build-slot.ps1 -Label d2a0a930-native-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-d2a0a930-native/ --nologo`, then execute through the same wrapper with `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-d2a0a930-native/ -- --treenode-filter '/*/*/AgentTaskCommitEndpointTests/Spawned_child_complete_brief_gated_operation_and_parent_receipt*' --report-trx`.
>

Stored `920190ea-605d-409b-9ff4-134010bb16bc` / Full run group 24: C1349 inspection warnings and pre-dispatch recovery — `/work/worktrees/task-920190ea/.antiphon/task-920190ea.md:72`:

> Native rows to rerun on the next candidate AFTER the sibling fixes land:
> - `/*/Antiphon.Tests.Application/PreDispatchFailureDeliveryTests/*` (13 rows: the 11 reproduced failures listed above plus two controls).
> - `/*/Antiphon.Tests.Application/DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller*` (all 10 scenario rows listed above).

Stored `2de763a6-d4d3-46ab-8135-d1431765e1b1` / Full run group 25: reply integration and check-note delivery (residue) — `/work/worktrees/task-2de763a6/.antiphon/task-2de763a6.md:96`:

> ## Native rows still owed
>
> Rerun the following rows on native Windows at the eventual landed/picked source, under its host
> build-slot gate and an isolated output directory. This task makes no Windows qualification claim.
> The supplied Windows total was 524 versus 485 locally; its case roster is unavailable, so the
> 39-case difference is unresolved and must be reconciled from fresh native receipts.
>
> | Row | Exact filter |
> |---|---|
> | G25-1 | `/*/*/AgentTaskReplyIntegrationTests/*` |
> | G25-2 | `/*/*/CheckNoteDeliveryHandoffTests/*` |
> | G25-3 | `/*/*/AgentTaskDeliveryWatchdogTests/*` |
> | G25-4 | `/*/*/VerificationRoundDeliveryTests/*` |
> | G25-5 | `/*/*/WorktreeLandingCleanupRetryTests/*` |
>

Stored `409d401c-d141-40d9-a1bb-ed42bc2ca7c4` / Full run group 26: wedged head, launch failure (residue) — `/work/worktrees/task-409d401c/.antiphon/task-409d401c.md:77`:

> ## Native rows to rerun after landing
>
> Rebuild the reviewed candidate on the owned native qualification lane, then rerun these exact class filters (71 + 58 + 9 = 138 cases expected; native expansion is authoritative):
>
> 1. `/*/*/SessionMessageQueueWedgedHeadTests*/*`
> 2. `/*/*/AgentSessionLaunchFailureTests*/*`
> 3. `/*/*/CheckCompactionCrashTests*/*`
>
> Use the host build-slot gate and a fresh results directory for every invocation. These Linux results do not supply native Windows qualification credit or qualify the other group-26 classes.
>

Stored `ed7af4f3-b016-44c6-a7a3-d72c1e3e3140` / Full run group 27: land graph evaluation, land verifier — `/work/worktrees/task-ed7af4f3/.antiphon/task-ed7af4f3.md:73`:

> Native rows to rerun, under the qualification owner's scheduling on the updated rc:
>
> 1. `LandGraphEvaluationTests`: all 92 expanded cases, especially the five original Exists variants, metadata controls, imported-directory wrapper, real-repository approval/check and the six forced path witnesses. The Unix-mode unreadable-link case intentionally skips on Windows; reconcile that platform skip explicitly.
> 2. `AgentTaskLandVerifierTests`: all eight cases, including pass/fail/cancel/throw and all three real TUnit selection outcomes.
>
> Use one fresh isolated build and the combined filter above through the host build-slot gate; collect fresh native TRX per class. Do not credit Linux results as native qualification or rerun the stale candidate without its existing master graph repair. The fixture delta requires no new Review under the operator's fixture-only instruction. Current Windows verification and complete attribution of the original ten graph failures are still absent.

Stored `6d1c77a9-7d77-4e20-9663-a024ff68a2e2` / Full run group 28: session routing, channel bridge, codex startup delivery — `/work/worktrees/task-6d1c77a9/.antiphon/task-6d1c77a9.md:86`:

> ## Native rerun owed after landing
>
> Rebuild the candidate on the owned Windows qualification lane through the build-slot gate, then run these exact rows into fresh TRX directories using `dotnet run --project tests/Antiphon.Tests --no-build` with that build's isolated OutputPath:
>
> 1. `/*/*/SessionRuntimeActionRoutingTests*/*` (35 Linux cases).
> 2. `/*/*/ChannelBridgeTests*/*` (40 Linux cases).
> 3. `/*/*/ChannelInboundRecoveryTests*/*` (41 Linux cases; reported native failure not reproduced).
> 4. `/*/*/CodexStartupDeliveryTests*/*` (62 Linux cases).
>
> The native executed roster is authoritative. Do not use the still-owned qualification supervisor or its lock for an independent rerun. No native Windows result is claimed here.
>

Stored `62a77bb0-8234-4eb9-ba6d-6d57db5d04a8` / Full run group 29: channel prompt correlation, data retention, check sweep — `/work/worktrees/task-62a77bb0/.antiphon/task-62a77bb0.md:78`:

> ## Native rows still owed
>
> Rerun all six class rows above on the landed/current Windows candidate using its normal isolated build and native qualification ownership, after the existing supervisor permits it. Keep the exact class filters and inspect the TRX counts. Linux does not close native qualification.
>
> Prioritize the three `SessionRuntimeActionTests` rows named above, `C467_V14_LandNotesDoNotCountAsReports` (AgentTaskLandNotificationRecoveryTests), `C1136_RecreatedRegistrationKeepsHeldDebtAndReschedules` (SettlementSyncRecoveryTests), both named check-sweep flush rows, all channel correlation rows, and the five named retention rows. The saturation failures have no verified Windows root cause in this task; if they recur, retain their native timing/phase evidence rather than increasing deadlines.
>

Stored `b76d2a8c-b1f5-488f-a2fc-51e4cb789f49` / Full run group 30a: herdr label follow, compaction receipt, rc maintenance queue — `/work/worktrees/task-b76d2a8c/.antiphon/task-b76d2a8c.md:50`:

> Native rows to rerun after landing: the complete HerdrLabelFollowFlowTests (19), CheckCompactionReceiptTests (27), RemoteControlMaintenanceQueueTests (21), and HerdrLabelFollowWireTests (4), using the final combined filter above and a freshly built native isolated output through the build-slot gate. Linux green does not certify Windows pipes/native qualification.

Stored `40bb57ba-4296-45c9-ad4e-12a1b3349186` / Run group 28b: branch, repair, land evidence, park — `/work/worktrees/task-40bb57ba/.antiphon/task-40bb57ba.md:72`:

> ## Native rerun owed after landing
>
> The caller should rebuild the candidate on its owned Windows qualification lane through the build-slot gate and execute these rows into fresh TRX directories. Do not borrow the existing qualification supervisor or native lock:
>
> 1. `/*/*/DelegateBranchDeliveryTests*/*` (seven Linux results).
> 2. `/*/*/RepairSourceSettlementTests*/*` (36 Linux results).
> 3. `/*/*/LandEvidenceWarningDeliveryTests*/*` (seven Linux results).
> 4. `/*/*/BlockedTaskParkDeliveryTests*/*` (six Linux results).
>
> Build command from the checkout: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label group28b-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g28b/ --nologo`.
>
> Test command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label group28b-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g28b/ -- --treenode-filter '<row above>' --report-trx --report-trx-filename run.trx --results-directory <fresh-directory>`.
>
> The actual combined Linux filter was `/*/*/(DelegateBranchDeliveryTests*)|(RepairSourceSettlementTests*)|(LandEvidenceWarningDeliveryTests*)|(BlockedTaskParkDeliveryTests*)/*`. The final run used only the parking row. The diagnostic selection was `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt`.
>

Stored `b8c3a4cd-f3ef-4261-b9ea-be92544a89ba` / Run group 30b: routing, outbound, review loop — `/work/worktrees/task-b8c3a4cd/.antiphon/task-b8c3a4cd.md:52`:

> ## Native qualification owed
>
> Rerun all four original class rows on Windows through the qualification owner: RoutingExhaustedPlacementTests (54), ChannelOutboundUnifiedTransportTests (17), ReviewLoopTests (18), SessionRuntimeActionRecoveryTests (17). No native qualification is claimed from Linux.
>
> Combined exact class filter:
>
> `/*/*/(RoutingExhaustedPlacementTests*)|(ChannelOutboundUnifiedTransportTests*)|(ReviewLoopTests*)|(SessionRuntimeActionRecoveryTests*)/*`
>

Stored `d55dd41d-c13a-4065-b3c9-549a3a09a7fd` / Run group 31a: specialist identity, phone-home, unverified — `/work/worktrees/task-d55dd41d/.antiphon/task-d55dd41d.md:89`:

> ## Landing and native handoff
>
> Code commits, each pushed:
>
> - `75218754ffec61d3bbc17b45f44cecdd3f38698f`: guarded terminal peer and guarded-write assertions.
> - `b3b3f4e77d6ba46bd6d239304ef67ccf33ddb38b`: opt-in limited to the two affected recovery scenarios.
>
> `git fetch origin master` followed by `git merge-tree --write-tree origin/master HEAD`
> completed without conflict against `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc`.
> The executable-source merge tree was `fbde0e45622650ba1fec822b0887b9a54fe410ef`.
> The evidence history guard passed for both code commits with zero violations.
>
> After landing, rerun these three native Windows class rows, or their exact combined filter
> above, under the caller's existing qualification ownership:
>
> - `/*/Antiphon.Tests.Application/SpecialistExecutionIdentityTests/*` (48 cases).
> - `/*/Antiphon.Tests.Application/PhoneHomeImmediateSendTests/*` (13 cases).
> - `/*/Antiphon.Tests.Application/DeliveryUnverifiedDiagnosticTests/*` (3 cases).
>
> Linux results do not replace those native receipts. The detached Windows qualification
> supervisor and its lock were never accessed or modified.
>

Stored `74315dd5-8a75-44ce-8ec8-4898b4b43169` / Run group 31b: land refusal witness, receipt failure — `/work/worktrees/task-74315dd5/.antiphon/task-74315dd5.md:52`:

> ## Caller rerun and landing
>
> Native rows to rerun: every expanded case in `LandRefusalDeliveryWitnessTests` (24) and `ReceiptFailureDeliveryTests` (21), especially the two formerly failing cases named above. Use a fresh isolated build and the same exact selector:
>
> ```sh
> pwsh -NoProfile -File scripts/build-slot.ps1 -Label group31b-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-group31b/ --nologo
> pwsh -NoProfile -File scripts/build-slot.ps1 -Label group31b-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-group31b/ -- --treenode-filter '/*/*/(LandRefusalDeliveryWitnessTests*)|(ReceiptFailureDeliveryTests*)/*' --maximum-parallel-tests 2 --report-trx --report-trx-filename group31b.trx
> ```
>
> The existing native qualification supervisor and its lock were untouched. Sibling-owned classes were untouched.
>
> Freshly fetched master was `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc`. `git merge-tree --write-tree origin/master HEAD` passed without conflicts at tested source, producing tree `84e2ad8f7beb7e19f52e349a7cb2e9b4a2c09191`. Evidence-history validation over base..tested-source passed with two commits and zero violations. Final publication/ref and report-only commit checks are supplied in the caller-facing completion report.
>

Stored `cea2c546-f4a2-41db-9972-67c6ee452eda` / Run group 32: seat release, mid-turn refine, deferral — `/work/worktrees/task-cea2c546/.antiphon/task-cea2c546.md:140`:

> ## Native handoff
>
> Rerun all six class rows named in the counts table on the Windows qualification owner,
> one exact `/*/*/<ClassName>/*` selection per row, after rebuilding the landed candidate.
> In particular retain all arguments of the two refinement sample methods, the three
> watermark busy/crashed arms, the Herdr owned-launch cut and the agent-control remote-control
> boot test. Linux green does not replace these native rows. The a779085c supervisor and its
> native lock were not touched. Land is the next stage as explicitly commissioned; no Review.
>
> Whitespace validation and scripts/check-evidence-diff.ps1 over base..HEAD passed. Fetched
> master was 99bd60f861eab6ffdcb98d5a4a006719ff4d97bc; merge-tree at the repair tip completed
> without conflicts (tree 9c8b3d2cdf1e22b74d2b18caf97d4b6c78d1a992). Landing and native reruns
> remain caller actions, not completed publication or release qualification.
>

Stored `f237ea10-3adb-4cab-a562-cfc094a29ee4` / Run group 33b: system prompt launch, report store — `/work/worktrees/task-f237ea10/.antiphon/task-f237ea10.md:49`:

> ## Native rerun rows
>
> Schedule these on the native qualification owner's lane after landing/building the resulting candidate, through the build-slot gate and `--no-build`; do not compete with the existing qualification supervisor or take its native lock from this task.
>
> | Native filter | Expected cases |
> |---|---:|
> | `/*/Antiphon.Tests.Application/AgentSystemPromptLaunchTests/*` | 28 |
> | `/*/Antiphon.Tests.Application/ExpectationDirectDeliveryTests/*` | 14 |
> | `/*/Antiphon.Tests.Application/AgentTaskLandPreparationIdentityTests/*` | 16 |
> | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` | 28 |
> | `/*/Antiphon.Tests.Application/LandRequestWriteDiagnosticTests/*` | 15 |
>
> The launch suite already contains the adapter-binding repair `99f73d3d00bdf0198002aa9269e7dd8bc2348ba4`, which landed between native candidate `a97194ee7a4a` and this base. Its 28 Linux cases, including forced CRLF and Windows drive-path input in the phone variants, pass. The two report-store failures (existing-ignore-rule and atomic publication) do not reproduce on Linux; their Windows cause remains unproven. No Windows-semantics cluster was established, so no speculative platform fix or new string witness was invented.
>
> `git merge-tree --write-tree origin/master HEAD` at the code tip succeeded against fetched master, producing tree `2832c3a45b8852b6de88d64ef19cb36f338bc53e`. Evidence history check over base..code-tip: 3 commits, 0 violations. All 33 inventoried producer-owned `bin-f237ea10` directories were removed after completed runs; no children remain owned by this task.
>

### Class rosters referenced by earlier native handoffs

These class names are copied from the existing reports’ class-table rows; exact methods and native restrictions remain in the excerpts. An absent table is not a zero native obligation.

| Task ID / group | Stored class roster | Source |
|---|---|---|
| 2de763a6-d4d3-46ab-8135-d1431765e1b1 / 25 | AgentTaskReplyIntegrationTests, CheckNoteDeliveryHandoffTests, AgentTaskDeliveryWatchdogTests, VerificationRoundDeliveryTests, WorktreeLandingCleanupRetryTests | /work/worktrees/task-2de763a6/.antiphon/task-2de763a6.md:49 |
| 409d401c-d141-40d9-a1bb-ed42bc2ca7c4 / 26 | SessionMessageQueueWedgedHeadTests, AgentSessionLaunchFailureTests, CheckCompactionCrashTests | /work/worktrees/task-409d401c/.antiphon/task-409d401c.md:29 |
| df70be52-02f4-402b-a7f8-57ea95720952 / 27 | OutputDistillationDeliveryTests, PendingSettlementDeliveryTests, AgentSessionRuntimeTests | /work/worktrees/task-df70be52/.antiphon/task-df70be52.md:7 |
| 6d1c77a9-7d77-4e20-9663-a024ff68a2e2 / 28 | SessionRuntimeActionRoutingTests, ChannelBridgeTests, ChannelInboundRecoveryTests, CodexStartupDeliveryTests | /work/worktrees/task-6d1c77a9/.antiphon/task-6d1c77a9.md:23 |
| 62a77bb0-8234-4eb9-ba6d-6d57db5d04a8 / 29 | ChannelPromptCorrelationTests, DataRetentionServiceTests, SessionRuntimeActionTests, AgentTaskCheckSweepTests, SettlementSyncRecoveryTests, AgentTaskLandNotificationRecoveryTests | /work/worktrees/task-62a77bb0/.antiphon/task-62a77bb0.md:14 |
| 40bb57ba-4296-45c9-ad4e-12a1b3349186 / 28b | DelegateBranchDeliveryTests, RepairSourceSettlementTests, LandEvidenceWarningDeliveryTests, BlockedTaskParkDeliveryTests | /work/worktrees/task-40bb57ba/.antiphon/task-40bb57ba.md:22 |
| b8c3a4cd-f3ef-4261-b9ea-be92544a89ba / 30b | RoutingExhaustedPlacementTests, ReviewLoopTests | /work/worktrees/task-b8c3a4cd/.antiphon/task-b8c3a4cd.md:32 |
| d55dd41d-c13a-4065-b3c9-549a3a09a7fd / 31a | SpecialistExecutionIdentityTests, PhoneHomeImmediateSendTests, DeliveryUnverifiedDiagnosticTests | /work/worktrees/task-d55dd41d/.antiphon/task-d55dd41d.md:23 |
| 74315dd5-8a75-44ce-8ec8-4898b4b43169 / 31b | LandRefusalDeliveryWitnessTests, ReceiptFailureDeliveryTests | /work/worktrees/task-74315dd5/.antiphon/task-74315dd5.md:22 |
| cea2c546-f4a2-41db-9972-67c6ee452eda / 32 | TerminalRunnerSeatReleaseTests, AgentTaskMidTurnRefineTests, SessionRuntimeActionDeferralTests, AgentTaskLandReceiptWatermarkSafetyTests, HerdrSupervisionBackoffTests, AgentControlServiceIntegrationTests | /work/worktrees/task-cea2c546/.antiphon/task-cea2c546.md:16 |
| f237ea10-3adb-4cab-a562-cfc094a29ee4 / 33b | AgentSystemPromptLaunchTests, AgentReportStoreTests, ExpectationDirectDeliveryTests, AgentTaskLandPreparationIdentityTests, LandRequestWriteDiagnosticTests | /work/worktrees/task-f237ea10/.antiphon/task-f237ea10.md:23 |

## Remaining uncertainties and exact resolution

- The desktop-only C:/src/ClaudeBot/temp/orchestrator-pending-messages.txt does not exist in this Linux filesystem; the assigned desktop checkout is explicitly unreachable under the runner contract. Its 2026-10-24 entries were not read. Task landing/notification rows supersede the brief’s stale 33a notice, but do not reproduce the full notice file.

- The 175 named run groups, all native outcomes, current running group/process identity and later suite plans remain unknown. Read the exact Windows paths listed above and compare their run/candidate IDs. Group 36 and groups 38–175 have no named repair task in the scoped data; this does not establish a missing dispatch requirement.

- No post-repair native receipt or complete native-rerun companion ownership was established. Resolve by a read-only Windows evidence pass and tracker reconciliation; do not credit the historical old-candidate run for new master repairs.

- 34a’s intermittent timeout and 35a’s four inherited expectation failures remain unresolved in their source reports. No new mechanism or fix is asserted here. CARD-0865 is related historical debt, not confirmed ownership of all current 35a cases.

- Card history is not a complete dispatch ledger: it stops at revision 36 on 09:06Z despite later task creation/settlement. It records automatic active/review transitions only when applicable; the later task rows are the direct evidence. No new card-transition bug is inferred.

- The exact cause of the lost orchestrator session is outside this reconstruction. DestinationUnavailable/destination_failed is a delivery outcome, not a crash diagnosis.

Not done, noted: No fix designed or implemented; the source reports retain 34b’s product-change gate and 35a’s separate acceptance-contract follow-up.

--- next stage ---
next: investigate
handoff: Read the exact a779085c Windows roster/progress/TRX and pending-message file to resolve group 36 and 38–175, native rerun ownership and current run state; preserve the five unlanded branches, 34b product gate and 35a four failures.
artifact: docs/investigations/2026-10-24-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md
