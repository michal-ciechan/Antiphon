Completed rc top-up 3: 50 executable commits cherry-picked cleanly with -x; both required builds passed. Publication remains with the orchestrator.

Branch: feat/card-task-bbfa2afa. Verified executable tip: a7946e9c733991738ef8a6a55dbbd21cc06083ee. The only subsequent commit stores this report; the final pushed tip is supplied in the completion message.
Master coverage cutoff, fetched again immediately after the last pick: 2b3ed0ed847be7d1556308fe30fb54ccced5a86c.
Verified rc base: ba5c6619ab0256b0c6d5c6e98259c963319bd4ce. git merge-base --is-ancestor origin/rc/2026-10-25 HEAD returned 0; the executable tip is 50 commits ahead. The report-only commit preserves strict descent and fast-forward publication eligibility.
All 78 touched executable paths match master exactly. The complete remaining tree delta to master consists only of instructions (AGENTS.md), docs and .antiphon reports. Before picking, every touched file matched master at e9703a6334df49f87490a0e67c1e5ac124f01aad; prerequisites are present, with no master-only feature imported.
Blocked picks: none. Cherry-pick conflicts or additional repairs: none.
Only the assigned task branch was pushed. No rc ref, tag, release script, running Windows qualification, native lock or desktop build slot was changed. Host build-slot endpoint: http://build-slots:8080/build-slots.

Verification was run once per required build against a7946e9c733991738ef8a6a55dbbd21cc06083ee:

- Solution: exit 0, 0 errors, 1,250 warnings, elapsed 00:04:05.38. Command: pwsh -NoProfile -File scripts/build-slot.ps1 -Label bbfa2afa-solution -- dotnet build Antiphon.sln --property:OutputPath=bin-bbfa2afa/ --property:UseAppHost=false --nologo
- Docker server-build: exit 0, 23 compiler warnings, image sha256:e4bb6751280b00f74eaca594d892179d0951edd1433e2ca2e699e88d2f9edf6a. Command: pwsh -NoProfile -File scripts/build-slot.ps1 -Label bbfa2afa-docker-server-build -- docker build --target server-build --build-arg SOURCE_REVISION=a7946e9c733991738ef8a6a55dbbd21cc06083ee --iidfile .antiphon/server-build.iid -f Dockerfile .
- Static Windows compile audit: 54 changed test C# files; no #if/#elif directives. Inspected Windows runtime branches, usings, ACL types, stacked helper members, async disposal, and linked lifecycle-host inputs. No missing using, duplicate member or Windows-only compile defect found. Runtime OS branches remain C# type-checked by the solution build. Native Windows execution was not performed.
- Static PowerShell 7 AST parse: 18 changed .ps1 files, 0 parse errors. This is not Windows PowerShell 5.1 execution evidence.
- Evidence-history guard: commits=50 entries=3 violations=0. git diff --check passed. Tests executed: 0, as requested; no test sweeps.
- Removed all 49 producer-owned bin-bbfa2afa output roots; 0 remain.

Essential unedited host-slot receipts:

~~~text
BUILD SLOT granted lease=d702a0d5-a576-4fda-9aa2-1b37e70f49f7 waited=0s maxcpucount=6
BUILD SLOT released lease=d702a0d5-a576-4fda-9aa2-1b37e70f49f7 held=246s
BUILD SLOT granted lease=53e2d713-a0c4-406e-aefa-45b00305e21c waited=0s maxcpucount=6
BUILD SLOT released lease=53e2d713-a0c4-406e-aefa-45b00305e21c held=165s
~~~

Local ignored evidence: .antiphon/solution-build.log, .antiphon/docker-server-build.log, .antiphon/server-build.iid, .antiphon/static-windows.txt, .antiphon/topup-manifest.json, .antiphon/topup-mapping.json and .antiphon/output-inventory.json. This committed report preserves essential provenance if the mirror is retired.

Ordered master -> picked mapping:

| Master SHA | Picked SHA | Subject |
| --- | --- | --- |
| b4133552d573b180506a60ad9dd8ba3fa33f49bd | 14141969e0b97b9ef3eb8d5e7d44b7193f241d05 | test: expose missing cache helper build-slot adapter mounts |
| d765c7fe07a4c2e0d5e57f4c2b8baaaa09b2c2e4 | dacf162fb383e8ab8d43c94d5daaf4f0e8d97a1c | test: correct cache wrapper launch census to five sites |
| 16d663409c27963e22d821af7a82b72822ad386a | 44f1dee77e3473f20f5a38f05a20ba2a6bd507c9 | fix: mount build driver adapter in cache helper containers |
| 40a9d65c371bc85e4fc05a8332b81805811f5c75 | 9389b3f188f99697e8aa1fe1d545105e0e477417 | test: model current transcript readiness for policy relaunch |
| b8188681a589fa013b09abe7f212ff8311671923 | 78c8349d085163bf039d783cbbb7442ba9ad1de3 | test(runner): expose Windows workspace admission string witnesses |
| bb4b2f00495b924be34b85f565062c17b6f35ac0 | f50784e743e1344cc4ac81234c85a529e8951c09 | fix(runner): admit Windows worktrees with native separator semantics |
| 6befcc97c22e12e58ac4ae538010dec8b517d597 | e89b9a967c83499cbc8437ec041d0a724ea76a9e | test: repair Windows lane fixtures and retain native PTY diagnostics |
| f35678dfe7af8a97ce7a92b96c38a6cc056bdcba | dfbc72f9a7c2dc367f244be0aedb00296d86e7b5 | test: witness Windows Git backlink spelling in freshness fixture |
| 6a137d46e4a137fdbde31bbf512c505a8b504c99 | d06d30eac2cb9ae492c5642e80138fc517c71e5d | test: preserve Git-written paths in freshness corruption fixture |
| e6abbff0917d89c69b8eeec83f88ccd0b555ed2a | 878d29b21f141fc0affb798a0827f9ab9403e9e4 | test: retain readiness shell launch diagnostics for native failures |
| 696d9bd81c01c7d48a7d53998e48019021e9133e | ae4cf58ec0beb9f4746429098969389ef14dabd6 | test: expose Windows file and path fixture failures on Linux |
| bed7a4e8147a37d8ce49f3d6cbbd96bb426265a8 | de78f02f7ede6fa490eb35f04e72dfc199edf3d9 | test: expose missing tool custody in Windows recovery fixture |
| f465150fbe21ef383da1107a0faa22a1e8dccbfe | 694ab10791b1b5c865c0c59bb31c5d157f02cc27 | fix(tests): honor Windows path and file custody in fixtures |
| e0eaf5a2cda2364e12dac4c1428d793352795bbb | a8bb39a400cbb0b23d3867cc32a8f1a385f31a04 | fix(tests): type fixture ACL diagnostics explicitly |
| db10d5be67a559aedbed31d3abd3a27e0cac57ba | 04db4b58bd1f7c2db8e9e75ca351beeec57bae2e | fix(tests): include cleanup dependencies in lifecycle host |
| 4c4016d98e8a73a191471575f39619d43cdba374 | 1e43e544e52f7096ea65786b274df0437d69deb1 | test(checkpoints): capture native group 62 and 63 failure evidence |
| d315a08269263125043e131673eef1feeb87286a | 7728327824abc47e63ad6617ec89590d4cd93596 | test(checkpoints): distinguish coverage source reads from JSON receipts |
| dceceed34ea269e79086649395ae7886dae7f00f | 1d95eff0627c0e7b35b1fdccffadb0f3410c8131 | fix(tests): keep checkpoint diagnostics in the owning test assembly |
| d73a77b884427fd61fb9085f9ff29386a10deca0 | ab1a91e2b28b8d3c3a81f6156259e179bdc203d9 | test: expose Windows Git path and checkout fixture assumptions |
| 56829468850f38beb64160af991b38925827a0a2 | 9f52c152af8464035532e07e4c319eae02df5ed9 | fix(tests): compare Windows registrations using Git separators |
| 010a976dc11b2771c3606bde47b441c7720a0db2 | 90d36c8376394c37fc78830d707e575f47532262 | fix(tests): isolate completed-card checkouts from host line endings |
| a46977828e03e2733207ce4c243f21a69e8bd761 | bd46a59100e7f7882f98e30c9c32d754564bb88a | fix(tests): place Linux file-use integration rows on Linux |
| 4709e7b7c2fc091ac8b3719d24589c5e2c241787 | aadaf0170238c9168c30599fed0df0dd145a7f07 | test: retain Windows cleanup and recovery failure diagnostics |
| 330033659535ca1ea74bbf3071d2ed8761175b84 | 893fc2ce4ddd9d710904dffb8bb084dc9863a689 | test: witness CRLF in the rolling census Bash raw string |
| 5f363000650f8a23d2e52bd5fb7eb2f3d1ee7614 | 4b18cda86eea07de8d7d2a5e2baa1b69f23ff7ac | test: preserve LF in rolling census Bash fixture literals |
| 43d7cabac1494ec9577f6442a7e056b1453d290d | 85ea6131ff5afe1585f5f97a7050e84c7ba66c9f | test: account for CARD-1574 recovery receipt checks in exact SQL budgets |
| 3ddefa402a280a57d268d4cd2a2237e24b33594e | 4b058e7b5efee89c385c1b93b7b04d531bdb84c4 | test: record forced-Windows Linux ownership admission red |
| 0dfe25944fa8c9869537e27e9587889aa3f14f79 | cd017ff7d22bd80553ca9307313e0cde808b75e5 | test: admit Linux process ownership tests only on native Linux |
| 255ecdab1aaea8c198f9b9c2e2bfd4a1994fc59c | bf0a6f9e96aab7cf43ca982f770acb6f9162aaf3 | test: witness Windows read-only recovery and retired host admission defects |
| 9a73dbb7e0f4e639c60b455901e469d567867646 | 5f004e1141d408f1134a7303061ef547763d1979 | test: separate Linux retirement host cases and normalize recovery object attributes |
| 5937e9b03edf5bd70468163967e1b44178a83230 | 1a0d605933a8e26e36bcdd03014f02a62ce37802 | test: reproduce inherited C578 output reuse at base and on Windows argv |
| 1683b188891896929d5f1fe123257b9bbf36c795 | d3bd9e5f6fc535344df4233b9e7f7838f04867eb | test: isolate each C578 checkpoint output and retain runtime diagnostics |
| ae6d25953dcb8ceb92a2fb686408744997247eba | e2dd3025eb48f41d235ed0a27762040bee6e63b9 | test: witness Windows script host admission and read-only Git cleanup defects |
| 206f3cf6eb432d1274f5dab8d198cf5a35bade14 | 04d8e5028be2ee584d6039edeca05b69c6a19395 | test: force Windows evidence index inputs and retain native TRX diagnostics |
| 0704f26cc796dcd43ecb246c32058f026c229eb3 | 7ade27670470cd87a277433578ed92e2f7bd66b5 | test: admit retirement and production mount host contracts only on native Linux |
| 8e3675fded78d03bc3172b5ecadae053ba81d99a | 1fc698beeaa695f3f44045919c77ba325b423c22 | test: preserve Windows object-only evidence paths and missing-blob fault cut |
| f5f9665334a798bd5755127f76518a762cfbbc62 | 1194a1d992cd8ef24baa8027687097091656ad74 | test: clean read-only script fixture Git objects without masking retained results |
| 0231899e1f857a5ea438d47c2d0de52ae8d3fbe7 | 9f48cdf6976bab3be23048e45c0091b3cad4d2ad | test: preserve nullable annotations in the standalone helper compilation |
| 8dd5792b12dc57cfaf27d17670492e09baa267c6 | e8a0fd602a02eab16a5fb0e7c090836822910be9 | test: refuse empty script diagnostic TRX input |
| 361bf53622a18afc7cdf38d17282426410c8d8ef | d5ef87af69a53e60c5b5c9d5dea53e9215643641 | test: witness supplemental evidence deletion on read-only Windows objects |
| 497191376f242f92bc3463a34dde36efc00176df | 291a1cb932630cd0edd66a34990c61edd83660f1 | test: clear read-only bit before deleting owned supplemental Git blob |
| 6d841b95ac0bf1f207ce31fb36548b6f788a88ac | 1ff8ca33103e400127b13093dd85607654064ade | test: witness Windows read-only objects in shared script results cleanup |
| 1500f956ccfbe07ab121dc1e653b88d46340724a | 94ec6387d91bf6f94b19c46355d1363b17e0bb5f | test: preserve newlines when loading historical harness witness source |
| 5c2a082d588ccf73bd824835377babfa3304e201 | 0d356ea826b8945d2e55e0f272933ed7b81e345d | test: normalize owned script result attributes before Windows cleanup |
| 3ec41be4be02cf6a9cbb6a66bce2a5c1cb72d5e2 | 931d2cc821b1ebe329fc0a245c42ffd1b2ba28d5 | test: add read-only group 76 native qualification diagnostics |
| 9fe514b85711bd87baedc0de3cccbae777868b8e | 7080be98890b24b021fcf7bc5b5e34bf20db54ab | test: witness group 77 Windows admission and WSL shell selection defects |
| 27c08db2816931dd2974226526d5aa73a6332253 | 803188f067cde3e261ac89721e2b3b41c3286575 | test: keep forced platform witness branches compilable |
| 4d88096f219445b9e7cbc368e117cc78397e6115 | 2f186237e629890dfc69522e2982bf853ec62284 | test: admit native Linux supervisor and owned Docker fixtures explicitly |
| 2f8717406c26a93968379a3719c7519abf709c6e | ed073b7561a2c4063f9963ff9222bc72ae45b746 | test: select Git Bash explicitly for Windows release wrapper captures |
| c4e43598ba77a4ad53a8f25cfdb407c89691e8bc | a7946e9c733991738ef8a6a55dbbd21cc06083ee | test: retain runtime and identity diagnostics for native release fixture reruns |

Skipped 20 docs-only/investigation commits (no executable delta or needed prerequisite):

- 98215c7e5788a245a10ec75187a1fef555a62337 debug: record db2c2088 lane-L reproduction and policy fixture failure
- c37155d602f767817110398dad936c021b252637 debug: retain policy refresh green receipt and native rerun handoff
- 151ce388f663d130f90c4aa32d44d4d87f8d87c1 docs: record db2c2088 Windows PTY argv fixes and pending native evidence
- 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9 docs: record group 66 Windows fixture repair and native rerun rows
- 7b132e411f8ede7d7a429487879298c3e63675f9 docs: record Windows file lane repairs and native diagnostics
- 3b7e33beeb18f5126230efed0afed4f2057dee01 docs: record checkpoint groups 62 and 63 diagnostics and native work
- 15eae477992779635cc809e0ddf54df6cd5b1f3b docs: record group 65 Windows fixture repairs and native follow-up
- 1870edcd61a04b98cc28e2973b4dd4b5d791f6ba docs: record rolling census CRLF repair and native rerun obligation
- 3d9d2567609f5f901f7c149d68b63aae3ba8d181 docs: record four-branch integration and inherited verification blocker
- 95d0bad51d0838b186861ea11831b9a79cdecb8e docs: diagnose split-8 recovery budget failures against master
- d78b580951d34bb6907d92cfbbba695553e4033c docs: record verified split-8 recovery budgets and rc fixture handoff
- 308bdd8beeeae7fb2e92bd423ee826533a4e4b8a docs: report script fixture repairs and native qualification handoff
- faae4a9f9c8f645bc745d8bf1297c21c614c295b docs: record groups 74/75 Debug baseline and bounded verification
- edfe601cee11667db1f5be88a4361511f08ef241 docs: report groups 74/75 Windows fixture witnesses and native rerun obligations
- f0dbd4092dc598743e662f6c69693ea88a9529da docs: publish the commissioned task report at its required handoff path
- 812486dd71ed22743a55b5cff7539473aec4710e docs: record script fixture cleanup and native diagnostic behavior
- 60d7458ea3d6da0f26b4d3bd58b68c87182408f8 docs: report group 76 shared fixture repairs and native obligations
- 8364e9c7dc726e6922ef14c3ba48318fd1b751a4 docs: record group 77 debug baseline and bounded verification scope
- 44de6c6d5352cd4040bdfb8f56b43a5ee7c61886 docs: report group 77 fixture repairs and remaining native qualification
- 2b3ed0ed847be7d1556308fe30fb54ccced5a86c docs: report round-two Windows script integration and verification

The integration summaries 3d9d2567609f5f901f7c149d68b63aae3ba8d181 and 2b3ed0ed847be7d1556308fe30fb54ccced5a86c are report-only; all their executable predecessor fixes and compile repairs are included above. Mixed executable commits retain their accompanying historical report lines unchanged.

--- next stage ---
next: none
handoff: Orchestrator publishes the task tip as an rc fast-forward; no publication was performed here.
artifact: /work/worktrees/task-bbfa2afa/.antiphon/task-bbfa2afa.md
