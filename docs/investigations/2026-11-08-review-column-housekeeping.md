# Review column housekeeping, 2026-11-08

Antiphon board. Base `origin/master` `74a8004f405bc54666fea33889839f0ff4d62ec7`.
353 cards were in Review. 170 closed because the described change is on `origin/master`. 183 left in Review.
No follow-up cards were filed. Residuals named by a review (Windows native rerun, SourceLanding mutation positive controls, an operator drill) are in the close reason. Cards that already track those residuals were left open.
CARD-1753, CARD-1762, CARD-1731, CARD-1747, and CARD-1769 were not in Review. CARD-1745 was in Review and stayed open (repair in review).

## Left open by reason

- 65: origin/master only mentions the card in docs, tests, or commit bodies
- 34: the reviewed tip is not an ancestor of origin/master, and no later product commit landed it
- 21: no land publication and no product commit on origin/master
- 15: the latest review asked for another review round
- 12: an investigations path in the description is context, not this card's landed change
- 11: the latest review handoff is Decide, Plan, or Mutation
- 10: the latest task still hands the remaining work to Code, Decide, or Review
- 10: the latest review handed the card back to Code
- 3: the review tip is newer than the last product commit and is not on origin/master
- 1: CARD-1745, operator hold, repair in review
- 1: CARD-0972 is the pending Windows native row and SourceLanding mutation PCs

## Closed

| Card | Evidence | Shipped |
|---|---|---|
| CARD-0148 | `42d48feab716a35c0b7153a8c51d1fe9cd3feabc` | Grok Terminal panels arm SGR mouse tracking - measure whether hovering leaks mouse reports into the  |
| CARD-0151 | `b6615e643e570be7af25ec946153b52f03d3ee79` | Add card effort estimates (days) and a stage-transition audit trail, for outstanding/added/completed |
| CARD-0188 | `972ede0a2778037181021417edb89dd360008e7c` | Leaked prior always-on incarnation is never reconciled (CARD-0056 twin, supervision direction) |
| CARD-0223 | `04d015d2ea36a6261ce867d0b098d7a4e2e71d3a` | CARD-0216's git-gate kill-on-timeout and runner-list-deadline need dedicated tests |
| CARD-0242 | `f5df4d3f6eab8ef298eafb3f79e228f215c46bc4` | Codex ingests no turn-abort signal - an interrupted delegate strands until the deadline |
| CARD-0262 | `2e0a26f77cacec31d6d81960803a8c2dc113e87e` | A user preference stored in KB does not reach the agent's own instructions |
| CARD-0407 | `a75ccdfd1c9286515b90910fb000ce7ec9fc556f` | A blocked 'may I continue this internal fix' should be a Question that auto-continues when there is  |
| CARD-0519 | `28b66c20c7e688f9fb689ce95d870d293aee0f8e` | Channel outbound: refused/crashed publish can silently strand a reply forever |
| CARD-0604 | `741c8c0eb1a1b86e2a96c837853212894fa64388` | Rework CARD-0590 server2 stack into persistent-runner DinD shape (not sibling compose) |
| CARD-0675 | `3a8d0f27371412335303d0876da25901094f7654` | Landing a -StartRef repair needs a manual force-push of the owner branch (no adopt-repair path; runn |
| CARD-0680 | `3334e82ae8e52cb075893a7ff74e61aa6aab511f` | Pty backend does not detect Claude permission/question dialogs; direct sends (watchdog) can answer t |
| CARD-0701 | `7d2ff34d5f2d10c494548dbb72b79caac96c7454` | Cache session working state in memory (ingest-updated, event-driven) instead of re-deriving it from  |
| CARD-0718 | `735ad68d7f7e448644af1e5c4a2d24adcee7f507` | Host stats: 5s CPU/memory/load + parallel tasks per host, in-memory 1/5/15/30-min rollups, Hosts pag |
| CARD-0822 | `d962d8e43f9ee5b465aa2a14df1804d17a3d8f32` | Live orchestrator instructions file: generated from settings, re-read on start/compact, pushed on ch |
| CARD-0826 | `b26c97658e712c53083e6777e5753bbf336011cc` | Daily per-host cleanup of worktrees, work volume and temp older than 24 hours |
| CARD-0907 | `1781471b9a1163e923d91f6a3177ad29a69a67e6` | Capture Codex 0.160.0 TUI screens and verify readiness/trust/done/update/usage-limit parsers before  |
| CARD-1065 | `951e5bf43f23016779eefc2234f70fb396ea076e` | Blocked tasks hold runner seats indefinitely; park must push WIP and release the seat |
| CARD-1076 | `78894b45dbb886993e13367aa9a6248137e5c72b` | Remote-prep git push killed at 5 minutes serially fences lease-taking dispatch |
| CARD-1079 | `64f43a5783b6ffc28af3b4000db7c081067c7dea` | Alert on idle runner seats, host occupancy above Working count, and orphan slots (detection only) |
| CARD-1087 | `0c79cce42ede008ba0c28dc2be4c533f6ffc255b` | Rolling redeploy-old stops with RecycleTaskCensusUnknown: empty project URL, 90 s census vs 15 s tim |
| CARD-1097 | `db4350ff329d5e8b8ec8e5209a261b86f5e30f92` | CARD-1065 S7 follow-ups: dedupe park_resume_refused warnings and read runner mirror identity at resu |
| CARD-1103 | `efb6cb1e7696a247e8d59bbe3faf25a5d11ef6e1` | CARD-1065 parked follow-up guidance: scope HasConfirmedPublishedParkAsync to the attempt and name Re |
| CARD-1104 | `5a402d6d96da3fff88013faaaf01beb99ce9a98d` | CARD-1065 V-27: prove the parked caller receipt on the inbox pointer path (default PtySingleChunkByt |
| CARD-1113 | `319236fefba040315fad7e0ca6d9152e895dc5d6` | CARD-1082 S1 disclosure: pin SettlementSyncDebtPolicy inputs (FullRef, null evidence, Pending guard) |
| CARD-1133 | `a77c7a3a575471ba0a5bffdaf486935b15c0d660` | CARD-1082 S5 disclosure: Blocked lease-busy settlements (unfetched Code, bind-refusal recovery) reco |
| CARD-1135 | `2f751049b860f56d1e1d46aa3b293ec2a7274bcb` | CARD-1108 S3 disclosures: a Held park refused before PersistIntent is not re-stamped; the Held resta |
| CARD-1136 | `23ad878aaa6b45e1f12cdb2becd6fb3349eb22dc` | CARD-1082 S6 disclosures: Code report omitted an unleased registry-guard exec; Held sync-debt attent |
| CARD-1150 | `755cf5c425ad02326eda1cba28182e61e0660756` | Crash after Running commit loses the brief and the watchdog kills |
| CARD-1151 | `f8ecf3e6b4a5bd6895b3b0bc7e368d4d7c3cd0f6` | Boot-stall tail stops a Working session (TryFailBootStallAsync + RetryAsync) |
| CARD-1155 | `93da52fcb8031881f9d10719bf4cfa4f89d78e13` | Add DispatchBaseWarningDeliveryE2ETests as named checkpoint rows |
| CARD-1168 | `0aa2193e796f695a0cb243314a3029d944748737` | state-init chown -R races live Grok SQLite files on the shared runner-state volume; redeploy-old Hos |
| CARD-1186 | `97b95acfb26a93e58ed98c2094df9ef718465036` | Dead-session recovery follow-ups: recovered note omits obligation/git bits; partial index for the Fa |
| CARD-1232 | `4bebe535f98babed8486e042f9736f93745dd1ca` | Pre-dispatch task failures can lose the caller notification (no durable delivery obligation; CheckEn |
| CARD-1259 | `6e23e0b4c8d6b2ae12218c2c0058593dbf773530` | Live transcript ingestion lags the CLI by 28 s; delivery verifier presses Enter again and fails at 3 |
| CARD-1263 | `9b39df4e065bb7267b9572cfd2c1acdf2b498e5b` | 2026-10-21: review rescue/* branches and clean the 22 kept desktop worktrees |
| CARD-1274 | `316e1a2501bd88d1e7969f5428060aa2bea7f353` | Other Settings tabs render paused/pending reads as definite values or empty states (CARD-1270 follow |
| CARD-1277 | `e1383249fab9025f5b8f82424d4ece37030529ee` | Standing_history_recovery PtyHost rows: FakeGrok truncates its reply at 60 chars; Claude PtyHost nor |
| CARD-1278 | `d7b7bdc5a22375152d539fd0b249b88ddcfd9d07` | Expiry sweep: detach the failed sweep rows after the third lost race so a default-tier task creation |
| CARD-1282 | `9b05461295644e700f1391d97d4861f78422016b` | ProjectSetupModal hides Runner profile picker while models read is unread |
| CARD-1283 | `9177159a0711948ab8c0f81f9ab03db2056204fc` | CARD-1278 PC-4 hangs instead of failing readable-red |
| CARD-1285 | `6a2fc6a3cfac85de811027021937b35f2f2e30d3` | Review delegates get no ANTIPHON_TASK_TOKEN; bound checkpoint start refused |
| CARD-1286 | `4dd7fb9f1fca0205f1f585fe31c7c041aeaee691` | Attribute ops-http.md routing-pointer test failure (OrchestratorInstructionsGuidanceTests) |
| CARD-1289 | `f7c2ebfc9a48fda2df3abfa18b0046b19d6a3dc6` | card_admission_changed spurious refusal, card-lock hold time, lock-order coverage |
| CARD-1290 | `ddbb790c8325c8df73510f0fd1395d7ef392b3c0` | D-2 verification-incomplete next-stage ambiguity; check-evidence-diff base handling |
| CARD-1296 | `e03508908b510dc43b60b3a3aa2a54f0d008e1c5` | D-2 policy doc: guard the section and pointer, reach non-Antiphon repos, fix dangling above |
| CARD-1297 | `e8bd995b0481e838c0110217dc4f680fe4de8374` | Checkpoint wait rejects an unused build output from an earlier SHA |
| CARD-1299 | `a1e52ce85c685350c5f8696c9486ead4fadea165` | Codex screen-scrape echo match: prefer plain match, separated form only on miss |
| CARD-1301 | `f17dfb7b030206a4ae168e6c260b1603934f1c9a` | CARD-1285 follow-up: tokenless specialist fresh-spawn token, save-failure-blocks-launch test |
| CARD-1303 | `6aaee1f83006e642841a97c0667c1ec251970d15` | RC 2026-10-15 follow-ups: interaction risks R3-R6, R10-R14 (one section each) |
| CARD-1307 | `1b62b6f67557d348f9b9c36f96b22e7419b0ed29` | CARD-0987 S1a advisory: cancellation mid tool-removal, exit-code mapping, file-only registry |
| CARD-1310 | `a625778ef9dbf532d1458afa8182e6119a6a1e30` | Checkpoint tool: Windows E2E output-ledger-mismatch on .msCoverageExtensionSourceRootsMapping makes  |
| CARD-1311 | `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0` | Release branch model: long-lived rc/<date>, fixes land there and merge back to master, -rc.K pre-rel |
| CARD-1312 | `fee6373a062f9cd7478aafe85319ff9546156fa7` | test-run-checkpoint.ps1: C585_FreshResultsDir and C589_SlotGranted share bin-c585h (inherited red) |
| CARD-1313 | `a60f9f058c88c33c26461c62e119dc4e55b544ef` | Change-aware test selection: path regexes decide which heavy suites (real TUI, browser, Docker) run |
| CARD-1315 | `a2eb78220f572db658c89e6adaf8f7fbfd8bb614` | Rollout script c590-remote.sh: fail-closed leftovers (F-3 hashes, F-8 marker, volume-inspect-as-free |
| CARD-1316 | `3999bbaca633559a63204b6d232accb23ff94acb` | redeploy-old recycle audit 3000s cap is below a clean volume time (5225s) |
| CARD-1317 | `e6ffa1bd6e300d3402f2c77383397485ddbccc47` | redeploy-old census races short-lived tasks: RecycleLandUnknown on a task that settles between list  |
| CARD-1318 | `79dae0ed8dfc0b6f8b6a148b28f722584ecc2f69` | CARD-1310 follow-ups: Windows seal docs drift and custody hardening |
| CARD-1319 | `da326d763964bc5063570eaa3ce77eeaa1ced33b` | redeploy census: tolerate a task that settles during pass 0 (Unstable abort) and update docker-stack |
| CARD-1321 | `81db5faf2addcd934e088352c4cd2c3191d9bb5c` | Closed-card binding: Merge/Commit helpers bypass card_implementation_closed; pin follow-up bind and  |
| CARD-1322 | `327978a9cc9b11e2d504bd01e0ef2d8403566575` | Codex turn_aborted follow-ups: usage stamping, stop-reason checks in runner adapters, idle-Working r |
| CARD-1324 | `d7b8bbd2504a0daac3a31e852c04ac33fb4492d0` | redeploy census: tolerate a no-land task that appears in pass 1 (Unstable abort) |
| CARD-1325 | `b2733fb160cd5c11e50ab25a0a64d87b71cd9537` | Nightly diagnostic follow-ups: structural explanation cut, real fixture shapes, quoted-state residua |
| CARD-1328 | `dab22827dc8be5d9b52717e16823eef03846a767` | C1192_Npm_seed_install_and_refill_use_work_volume red at base (refill-volume-source=0) |
| CARD-1329 | `b3c3f39ccbf2dfa74290139481311d515a30596c` | keep-six poll cost: AgentTasks (ProjectId, Status) index, bound the started-card read, add 409 and n |
| CARD-1330 | `96c4a720e935688914a43e5ee870a8366ecaf0f6` | InstructionBundleTests worst-case composition over budget on master (29,690 vs 29,500) |
| CARD-1331 | `efc5194ccc0639cb7f05b89955410c3c68ebef8b` | state-init own_tree forks chown once per path: 4.5 h on a populated server2 volume |
| CARD-1332 | `6ec71d427574f3a1b74bfbe2042e0ea9a819d191` | Orchestrator bundle is 7 chars under the argv reserve: move detail to owner docs and add an early wa |
| CARD-1335 | `e9e9ddccb82e58867b3921a9b983437adfa6a3c2` | gitdir-entry audit refuses antiphon-mirror-owner.json: blocks retire-temp and the next standing rede |
| CARD-1336 | `e6dced0b0636e1490cc4b46595f40f660bcaa0a3` | Release gate follow-ups: Windmill wrapper -Ref, nightly-run candidate id, exact refs/heads ls-remote |
| CARD-1339 | `42d48feab716a35c0b7153a8c51d1fe9cd3feabc` | Release tags: allocator must consult remote tags (hand-published finals), exact ref matching, promot |
| CARD-1350 | `15f742812b5348d6c790e13d28f3204da493633e` | rc/2026-10-18 release work: fixes, re-gates, merge to master (continues CARD-1311) |
| CARD-1353 | `792dd4bc5fd13fcca45aa74e30e75320c8eddb1a` | Allow lands throughout a server2 rolling rollout (drain-old/redeploy-old) instead of freezing landin |
| CARD-1355 | `3f3f6899f6838af82b4d4a16759376012a1d1a13` | C479_V07 mechanical-PC contract test passes on an unrelated 'eligible' word |
| CARD-1356 | `44b3147c2c7c2164447fcd24d63cf277d2a3d7d7` | Checkpoint tool kills builds at the 15-minute manifest default instead of the row timeout under host |
| CARD-1357 | `38b57ff2abd551e3c8bc0e8efdde13e50fa9abb2` | Release selection hop collapses child exit codes to 1; rc-pin fixture no longer reaches the hop |
| CARD-1358 | `01f95ef80e3f9a09d49c7e30cd98bb83288feb1e` | Checkpoint ledger 3-second per-file read deadline reports large unchanged files as tampered under lo |
| CARD-1359 | `ca856a8751bde71e986fcc910541687f50d5670d` | C540 child-host dispatch launch stalls ~60s before warning materialization (PreEnqueueCrash red) |
| CARD-1360 | `24fb53a932067ad3aa0af3d8ca0b8f7bca4ad177` | Land-delivery durable sweep needs a product seam to be testable (CARD-1305 F-1) |
| CARD-1361 | `5fb0d72e9ee81e97bd8a84865fe190cac28a00dd` | redeploy census: tolerate open-state status flips (Queued/Dispatched/Working/Blocked) between list/d |
| CARD-1362 | `dc5d0c4a381d0fd04c98696b526587d2a54a66d6` | AgentSessionRuntime sync-over-async runner reads without deadlines can pin the thread pool and stall |
| CARD-1363 | `7cf7537558fd6e62e315b7b26cd59d8611914935` | run-checkpoint.ps1 file reader has the same fixed 3s/8KiB deadline as CARD-1358 |
| CARD-1364 | `4ec446ab3996c2d12376768cbf6849c005004201` | CARD-1353 bound-land wait: assert waited= through each real proof path (mutation gap) |
| CARD-1365 | `b37ea0fde7e2d58a6ca7f7265a82a7cc8f80d6d3` | Runner herdr snapshot route blocks on ReadScreenAsync().GetResult() (sync-over-async) |
| CARD-1366 | `1d343ce8c03c9888d9d1baa4327574dbaa2b69db` | Orchestrator bundle lost the CARD-0479 tier routing words (explicit Frontier / eligible Low); README |
| CARD-1367 | `9fd8e6372a1e963279f01a1f55fe0239e6286922` | Linux isolated build of Antiphon.Tests fails MSB3021: FakeClaude apphost file collides with staged-h |
| CARD-1369 | `ae58707e80f189f06bf69ca16c4d228f693409a4` | CheckpointFileReader EOF-stall shortcut: re-check size after the stall (append race) and witness the |
| CARD-1370 | `706d99b8a6eb624ddb1bda535b015205b2c0d50e` | Checkpoint tool crashes with receipt-write-failed (access denied) writing state.json on Windows afte |
| CARD-1371 | `d1cf224baac6273e6ca006625e6e7371017b2eb3` | Recycle census reports cross-read task churn as Malformed exclusion count (stopped rollout after con |
| CARD-1372 | `bf9d96db2449bc35dab3c7d1c168c0ce9e78359c` | SessionUnowned wording still says 'standing owner' in attention visuals and DTO summary (CARD-0188 f |
| CARD-1373 | `fefb2791bc8ed390fc7e8dd5356a36621a3749dc` | Test fixture MarkWorkingAsync bypasses the loaded SessionStateStore, so ReadWorkingAsync sees a stal |
| CARD-1374 | `6f3a22ec7667fa6a6a935b18aa2374005bbb31e7` | Recycle census treats one transient HTTP 502 on a task read as fatal, discarding a ~4h resume after  |
| CARD-1375 | `29dfeaeb18bde6c5b54c5cb4d46aad9a38f0e30e` | Nightly diagnostics: capture real TUnit Timeout/Cancelled nodes and witness the probe supervisor kil |
| CARD-1377 | `53037d0caa8ff48b112952e35c3d7432e24d5f54` | run-checkpoint harness: isolate the remaining cases that still share bin-c585h (CARD-1312 follow-up) |
| CARD-1381 | `cff9b9bdadab0285d748be3d725bd76a406450d0` | CheckpointBoundedReaderTests: exact 4 s hard bound fails at 4033 ms under host load (changed_readers |
| CARD-1382 | `bca7e3f4fa0c9643e2088a4ddefab978aee58df1` | CARD-1322 F1: re-arm an interrupted AwaitingInput attempt without false stall-kills from slash-comma |
| CARD-1383 | `cfbc89f1d8f60599b65d6d532a6b4753178c3ab2` | Stop slow-tests-allowlist.txt being a merge hotspot: merge=union plus a registry guard (about 6 land |
| CARD-1385 | `fa8534122789887930f789ccdb61d80697db3bdd` | Land non-code skip: audited build-graph fingerprint goes stale on any unrelated script change |
| CARD-1386 | `87bdedfefdb543cf59069ed9fa36f7ee8def94ed` | Qualification harness retains a dead-owner run.lock after owner and parent exit |
| CARD-1389 | `7c585199d7f528ac41c08368d54f1b8faa1d5bbd` | Run-lock reclaim: Linux double-reclaim race, overwritten reclaim record, mixed boot-id sources (CARD |
| CARD-1391 | `e2e76df49b9e7ae59345aecca790d32c5955a680` | RC harness follow-ups: short-name class matching, lint gating, spliced diag records, odd TypeNames ( |
| CARD-1397 | `4626489274337d1d4d9994ba423f8070df46041e` | RC antiphon lane runs 1346 per-class processes (~22 h): batch classes into OR-filter groups |
| CARD-1399 | `8c8e08615ce2d40640f9ad0ea0809656ff71f3d3` | CARD-1371 follow-ups: phase lists disagree (volumesRemoved), per-marker witnesses |
| CARD-1403 | `a550bcbd2b7413093ed1cc978942b82696f4fd04` | Cut Postgres round trips: per-call-site budget, batching and caching so a remote DB is tolerable (CA |
| CARD-1409 | `6a2fe6ae390400aff141bf99633d427f78f47346` | CARD-1400 follow-ups: unknown-property Imports fail closed, missing import silent, doc wording |
| CARD-1412 | `e0080cfc609100be004c570ec1beab307c081973` | CARD-1411 follow-up: wait routing test pins names, not renewal behaviour (M2b, M5, M6) |
| CARD-1418 | `f3b2f0bbaea75a0729a9ae55933a72d31efe30c0` | Investigate timing-budget test Probe_redacts_many_same_line_assignments_near_the_output_limit_within |
| CARD-1425 | `3a8a23408f6b570608f1a136e004b9a47a5bd681` | Nine CARD-1281 producer-pack tests run nowhere: replace the untracked pack with a tracked fixture |
| CARD-1430 | `b59706530950ad5fb334c216a6f18a4e9bfd5103` | Worktree cut keeps the repository lease through the progress-baseline capture |
| CARD-1432 | `705026da81f9e00c6f9e6b901d9cc77fd1382873` | Desktop canonical repository: automatic gc and ref packing stopped around late September (23k loose  |
| CARD-1435 | `d872d06bdb7ed8556a19f7bf5a6e7e06a07ffdb0` | CARD-1422 follow-up: queue-exclusion and late-confirm-wait rules have no witness tests |
| CARD-1446 | `0541872b94e8de169e5c182d7439fd5feff592fe` | Stale active AgentTaskLandings rows and open sync debt on long-finished tasks block cleanup |
| CARD-1448 | `222ceeb0074427fb42b7a7cb8b439fa0e17ffa59` | Runner settlement sync refuses a fast-forwardable plan branch with runner_sync_identity_mismatch |
| CARD-1452 | `673a8c6d759b379294469985111e49ae5c8ede2b` | CardRelationshipSchemaTests pins the newest migration name; red on every later migration (and --base |
| CARD-1462 | `7b3955823570598bc1ab81126699520b755120ca` | Why lands run on the desktop, not server2: requirements, cost and options |
| CARD-1463 | `aa7a691ba961f8d4e7eea2562f6dfacfcf1b235e` | Land refuses a dirty or sequencer-busy task worktree after the verification window: 21 CARD-0688 D-2 |
| CARD-1464 | `e70a567107f56ffa35a9baa861f208d3ca5eea77` | Delivery fixtures mark Working/idle with raw transcript inserts while the queue reads the loaded Ses |
| CARD-1465 | `923b0acd1fff897b7e2a343e7f260a838c53179a` | BlockedNote.ContinueMessage assertion fails on CRLF checkouts: continue_replays_standing_authority_o |
| CARD-1469 | `8fce73036a83ac817e4ba118801ff936f690103c` | Carry the standing orchestration rules into the orchestrator prompts, bundles, docs and skills |
| CARD-1471 | `e7825b823c695945cb631f4248a41102dca5d686` | Remote dispatch commits an unbound pool agent, then fails IX_Agents_Slug on resume |
| CARD-1472 | `0cc45ebd931aa4f7f1643217d0f5d5987a345689` | Blocked Reply loses report contract; bare done erases a retained report and handoff |
| CARD-1474 | `b0c0f89b8e928a18d3a437194a13fde58a339602` | Closing-report nudge survives a valid report and is delivered after task completion |
| CARD-1475 | `1ba4ac9ffb569189a33f4447869b1f552e3f1b38` | First-prompt DeliveryUnverified copy claims verification is impossible after a bounded miss |
| CARD-1479 | `2fc03221aad59278be9a526893b91e3a88036ac5` | Runner settlement sync never registers a lease waiter, so land admission cannot yield to it |
| CARD-1480 | `cb7af8ccd54934b7a53dd88ca458dad0b643eae6` | Same-card worktree base selection ignores pending sync debt and can inherit a stale desktop tip |
| CARD-1481 | `3451bb3ab09f85df84f7cdd8f64471890060ce83` | Per-role Claude effort override: run non-Review roles on opus at xhigh |
| CARD-1483 | `bd215d7b4f559db69fbf4d7363417fce0f2c2467` | CARD-1409 follow-up: well-known item metadata read as empty in the land audit; evaluate with real MS |
| CARD-1485 | `6a84a33607c0ed3f6ad89c596d58e2cb9dd07e37` | Land replacement inherits stale Review evidence and refuses a newly reviewed source with resume_appr |
| CARD-1489 | `aa103e96b2bc4ff82c482d77a0b72ff9097a261a` | Server2 build-slot throughput: saturated 4/4 budget, long waits, duplicated test-project builds |
| CARD-1490 | `b0b7dc93d897f616ce9ff5f131d42b29bec00ff3` | Explicit -Level dispatch must refuse a held model (never launch on a blocked model) |
| CARD-1491 | `ed96820bde8e914fbe11e340820da54f39050268` | Record Claude launch effort on the pool agent row so warm reuse matches it (CARD-1481 follow-up) |
| CARD-1492 | `7950400ffc622988e1c19785109472fde566e1e2` | C544World/C1205 fixtures lack SessionRuntimeActionCoordinator (transcript persistence throws) |
| CARD-1493 | `b87492137c2b4ed30709d17b35913654cb0bdb42` | CARD-1489 remainder: same-group build reuse lint, Hosts page shared broker, nested build caps, artif |
| CARD-1494 | `713801a8f98c0403bb9981bcbe954e68110cbb19` | CheckNote Legacy crash-worker tests: load flake and output-dir writes block receipts |
| CARD-1495 | `ebec1ecfe8752ea2c255e5b46d60f8f8bff94606` | Warm-pool reuse has no witness for the provider-endpoint stamp wiring (CARD-0167 S3) |
| CARD-1503 | `93c81642327bbcd39e9ba7de01dcace3b3cde71c` | CARD-1486 follow-up: a question-shaped unmarked turn after a Reply still overwrites the retained mar |
| CARD-1512 | `98e96f029a8117034445d6c236db0cd0c045a0e6` | Land publication image test retains the pre-CARD-1446 protected-content expectation |
| CARD-1515 | `1f55bfc32df6aac32a48f9246748d5786cbe9e35` | Windows owned PTY fixture deletes OpenConsole before filesystem release is established |
| CARD-1519 | `19a0b6aa27c9cd925ddb045d5de1e6be364f42e2` | Runner capacity release waits behind long maintenance sweeps before queued admission is re-evaluated |
| CARD-1521 | `8d754fbbf69104e2f4f71154cd8cae494f781021` | Codex boot-wedge recovery mistakes a partial pointer for consumed input |
| CARD-1522 | `5a0cfd71324e2ff52c93e81b35004fc58e769f62` | Investigate seven dispatch statement-budget regressions (1-2 extra queries) |
| CARD-1523 | `ef121b23f3d78bbeb31e09886dcf8448e4a633e5` | Phone-home serial event consumer overflows during desktop load and flaps server2 eligibility |
| CARD-1531 | `7635f8de4d76434fb6ae1e8a758188d3b4da80d9` | Checkpoint total deadlines consume slot-only waits and abort verified-build rows before launch |
| CARD-1538 | `ffd8acccf4895bc46e2da9e949321c1c65892154` | Land graph audit fixtures still delete read-only Windows Git objects with raw Directory.Delete |
| CARD-1539 | `066b070e41add2edd7110af08c6bb1f74dabddbe` | Capture the lock-holding process whenever a file or directory in-use / access-denied failure occurs  |
| CARD-1540 | `5f7d5376f1cdbb06fa19f53686b126621031c839` | Land audit approves non-docs timestamp metadata and plain PATH property (CARD-1528 Review disclosure |
| CARD-1543 | `b693ad16d4ad3df92063f97ef859b70d43fae3ce` | Remove repeated session-kind SQL probes from guarded recovery delivery |
| CARD-1547 | `f5ae375b539795eb1e5ab51ef80088b307b9a1d1` | Use async DI disposal in pool and post-land worktree fixtures after CARD-1349 |
| CARD-1554 | `2f6c2e2ac62dd2dc9a3bb0e25d5cb45a01f3976a` | BootStall V15 fixture cannot resolve AgentSessionService on resumed-launch path |
| CARD-1562 | `c51023b99b3fcccd222ddbf8137de8c203e04760` | Share expensive test setup once per run: AppHost/web host/database container/migrations once, per-te |
| CARD-1563 | `6e11b03ce0f396c0203a1185c727b8d04dd2fb1f` | Timeout audit: derive per-test and per-row timeouts from measured p95 (about 2x), enforce with a gua |
| CARD-1564 | `3683c24c4a8d803a23cc003a0d791d2254411ae4` | Live-app startup and external-call observability with the same phase names as the test timing (spans |
| CARD-1565 | `2c9c578ceb2e8e95c3a2013835ecc347c24f5e58` | C519 converter receipt identity does not match final spilled queue pointer after crash recovery |
| CARD-1572 | `c68e8cabddb96b2e71171b66b99a722950d618ca` | Master Unit lane has dozens of unattributed reds and exceeds 30 minutes |
| CARD-1584 | `317fb964f8a518db3a3a05d3d40b23b9c1e29400` | E2E qualification underprices native landing tests and loses 77 unstarted cases |
| CARD-1590 | `3494aabcd7c8f64ebe9bd8c1ab107a4449a19056` | c1545 fixture leaves dirty nested seed repos in task worktrees, blocking server2 recycle |
| CARD-1627 | `ec45ed58deae36325cb1f6a8793639d805ff60f5` | Mutation loops hold one build-slot lease; looped builds escape the CPU cap |
| CARD-1637 | `e23a6a10b25adcf40c18d57a40def3ddd1462870` | Land: merge-branch fast-forward gap and resolver task ignores runner and kind |
| CARD-1675 | `ce89fa5d4c61d8fcfcae41f4d8299f74d5dab25a` | Grok store probe: a file at GROK_HOME or its parent still reads Missing |
| CARD-1697 | `9c1ab0823c1701068d89f7c7980923cb6092fd89` | Scope the main-runner recycle census to bound tasks like server2-temp |
| CARD-1706 | `d6fac5e7d59c7a20b178adbdff3a7929f7a93e73` | Reply-merge Conflicted event writes an uncapped conflict file list (22001) |
| CARD-1709 | `e5b680ff059d1fa6d87b22fe45231926c048eaff` | CompletionNote recovery pass enqueues without a lock-wait limit |
| CARD-1710 | `db19cbbfc661860c3fb747342638bb873548ac4f` | Event Detail cut at 4000 can split a surrogate pair; unbounded path joins |
| CARD-1720 | `98991a93c23c9c7f920bcfe9a176f78ff9f2ea67` | Whole-Unit checkpoint row is killed at 30 min with no TRX (under-budgeted) and ResilienceBudgetTests |
| CARD-1734 | `f8e5c9a41336f0814128317c40a45cf9915bd653` | Checkpoint cold build rewrites preparatory reference obj and loses terminal report |
| CARD-1741 | `1394d6e3db90d12ec38de564e7042c1d4603ee2c` | Record the CARD-1221 Linux/Docker production contract in docs/docker-stack.md |
| CARD-1742 | `f336c3ced190461de84adaab22a60642b09baeec` | Add a host-daemon staging Antiphon server compose on server2 |
| CARD-1754 | `3aff03d0fae761c406283637ff381bb7c00f9fc5` | Server CPU: CARD-0079 compaction discovery reloads and token-scans every Check seat's full transcrip |
| CARD-1756 | `abfb624d680cdeb913506e358f70e4accb312127` | SessionRunner CPU: bound TranscriptTailer opens the transcript for an identity check on every 300 ms |
| CARD-1761 | `933b5c016046fbcccbad04ed54da6b8b793b5a14` | CARD-1758 S1: server-only sequence and single-frame snapshot polls (no full buffer/transcript body) |

## Left in Review

| Card | Why |
|---|---|
| CARD-0038 | no land publication and no product commit on origin/master |
| CARD-0121 | latest Plan task handoff is Code; landed publication is not the remaining work |
| CARD-0416 | the investigations path in the description is context, not this card's landed change |
| CARD-0500 | latest Investigate task handoff is Code; landed publication is not the remaining work |
| CARD-0507 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-0511 | review next Land at none is not on origin/master |
| CARD-0512 | no land publication and no product commit on origin/master |
| CARD-0552 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-0588 | review Clean next Land at f7400e26563e is not on origin/master |
| CARD-0599 | latest review handed back to Code |
| CARD-0655 | latest review handed back to Code |
| CARD-0657 | latest review handed back to Code |
| CARD-0669 | no land publication and no product commit on origin/master |
| CARD-0692 | no land publication and no product commit on origin/master |
| CARD-0698 | latest review handoff is Mutation |
| CARD-0707 | no land publication and no product commit on origin/master |
| CARD-0712 | review next None at none is not on origin/master |
| CARD-0817 | latest review handoff is Decide |
| CARD-0824 | latest review asked for another review round |
| CARD-0851 | review Clean next Land at 28d19dd24a16 is not on origin/master |
| CARD-0856 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-0889 | the investigations path in the description is context, not this card's landed change |
| CARD-0905 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-0931 | no land publication and no product commit on origin/master |
| CARD-0972 | the card is the pending Windows native row and SourceLanding mutation PCs |
| CARD-0987 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1010 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1023 | no land publication and no product commit on origin/master |
| CARD-1039 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1040 | latest review handoff is Decide |
| CARD-1066 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1068 | latest Plan task handoff is Decide; landed publication is not the remaining work |
| CARD-1109 | latest Plan task handoff is Decide; landed publication is not the remaining work |
| CARD-1148 | latest Plan task handoff is Decide; landed publication is not the remaining work |
| CARD-1217 | latest review asked for another review round |
| CARD-1230 | review Clean next Land at 4bf2d2e6ed38 is not on origin/master |
| CARD-1246 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1281 | latest Plan task handoff is None; landed publication is not the remaining work |
| CARD-1287 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1288 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1305 | clean review 066f4a98916233c1020bd0dc3bcf03b416b46cae is not on origin/master and is newer than the last product commit a5c697cee89b |
| CARD-1314 | latest Plan task handoff is Code; landed publication is not the remaining work |
| CARD-1326 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1327 | no land publication and no product commit on origin/master |
| CARD-1347 | no land publication and no product commit on origin/master |
| CARD-1348 | the investigations path in the description is context, not this card's landed change |
| CARD-1349 | the investigations path in the description is context, not this card's landed change |
| CARD-1351 | review next Land at none is not on origin/master |
| CARD-1352 | no land publication and no product commit on origin/master |
| CARD-1354 | no land publication and no product commit on origin/master |
| CARD-1368 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1376 | review Clean next Land at 9b97b7aebae4 is not on origin/master |
| CARD-1378 | no land publication and no product commit on origin/master |
| CARD-1379 | clean review dc47eb4833519dd734ea44c67e3d6b8faa10e14c is not on origin/master and is newer than the last product commit d844fedf7d97 |
| CARD-1401 | latest Plan task handoff is Review; landed publication is not the remaining work |
| CARD-1406 | review Clean next Land at 800c968adbc2 is not on origin/master |
| CARD-1470 | latest review asked for another review round |
| CARD-1477 | latest Investigate task handoff is Decide; landed publication is not the remaining work |
| CARD-1482 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1488 | latest review asked for another review round |
| CARD-1527 | latest Code task handoff is Decide; landed publication is not the remaining work |
| CARD-1545 | latest review asked for another review round |
| CARD-1548 | the investigations path in the description is context, not this card's landed change |
| CARD-1570 | latest review asked for another review round |
| CARD-1573 | latest review handed back to Code |
| CARD-1581 | latest review handoff is Decide |
| CARD-1589 | no land publication and no product commit on origin/master |
| CARD-1591 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1593 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1594 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1596 | review Clean next Land at 3a0d8c9b006a is not on origin/master |
| CARD-1597 | review Clean next Land at 17cb417c3b3d is not on origin/master |
| CARD-1598 | latest review handed back to Code |
| CARD-1599 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1601 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1604 | latest review handed back to Code |
| CARD-1607 | review next Land at none is not on origin/master |
| CARD-1608 | latest review handed back to Code |
| CARD-1610 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1612 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1614 | latest review asked for another review round |
| CARD-1615 | latest review handoff is Plan |
| CARD-1616 | latest review handed back to Code |
| CARD-1617 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1618 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1620 | the investigations path in the description is context, not this card's landed change |
| CARD-1621 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1623 | no land publication and no product commit on origin/master |
| CARD-1624 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1625 | no land publication and no product commit on origin/master |
| CARD-1626 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1629 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1631 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1633 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1635 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1636 | latest review asked for another review round |
| CARD-1638 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1639 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1640 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1641 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1645 | latest review handoff is Decide |
| CARD-1647 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1648 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1649 | review Clean next Land at 09022f2fe0f1 is not on origin/master |
| CARD-1650 | review Clean next Land at 10dc40003b04 is not on origin/master |
| CARD-1652 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1653 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1654 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1655 | latest review asked for another review round |
| CARD-1656 | review next Land at none is not on origin/master |
| CARD-1657 | latest review asked for another review round |
| CARD-1658 | latest review asked for another review round |
| CARD-1659 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1660 | review next Land at none is not on origin/master |
| CARD-1662 | latest review asked for another review round |
| CARD-1663 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1664 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1665 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1666 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1667 | review Clean next Land at 639f78a890f3 is not on origin/master |
| CARD-1668 | latest review asked for another review round |
| CARD-1669 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1670 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1671 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1672 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1673 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1674 | review Clean next Land at 15f1bfffeedb is not on origin/master |
| CARD-1676 | review Clean next Land at 851bae3a19fb is not on origin/master |
| CARD-1677 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1678 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1679 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1680 | clean review is not on origin/master and is newer than the last product commit 6f886fe7e720 |
| CARD-1682 | review Clean next Land at cc7fc1061515 is not on origin/master |
| CARD-1683 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1684 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1685 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1686 | latest review handoff is Decide |
| CARD-1687 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1688 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1689 | review Clean next Land at c0666337845e is not on origin/master |
| CARD-1690 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1691 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1692 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1693 | latest review handed back to Code |
| CARD-1694 | review Clean next Land at aa33d8f639c9 is not on origin/master |
| CARD-1696 | review next Land at none is not on origin/master |
| CARD-1698 | review Clean next Land at 76e953e2b95a is not on origin/master |
| CARD-1699 | no land publication and no product commit on origin/master |
| CARD-1700 | review Clean next Land at 92a48622c5bd is not on origin/master |
| CARD-1701 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1702 | review next Land at none is not on origin/master |
| CARD-1703 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1704 | latest review handoff is Decide |
| CARD-1705 | review Clean next Land at 9d81df9b28a5 is not on origin/master |
| CARD-1707 | review next Land at none is not on origin/master |
| CARD-1708 | latest review handed back to Code |
| CARD-1711 | review Clean next Land at 66cbd06b11b6 is not on origin/master |
| CARD-1713 | review Clean next Land at b4ed0f582468 is not on origin/master |
| CARD-1714 | review Clean next Land at a670e82a9d0d is not on origin/master |
| CARD-1715 | review Clean next Land at e64c5cd801c7 is not on origin/master |
| CARD-1717 | latest review handoff is Decide |
| CARD-1718 | latest review handoff is Decide |
| CARD-1719 | latest review asked for another review round |
| CARD-1721 | review next Land at none is not on origin/master |
| CARD-1722 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1723 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1725 | no land publication and no product commit on origin/master |
| CARD-1726 | review Clean next Land at 13e9c1c8b019 is not on origin/master |
| CARD-1728 | review Clean next Land at 5acde1e2b7ad is not on origin/master |
| CARD-1732 | latest review asked for another review round |
| CARD-1733 | no land publication and no product commit on origin/master |
| CARD-1736 | origin/master mentions this card only in docs, tests, or commit bodies |
| CARD-1737 | latest review handoff is Decide |
| CARD-1739 | no land publication and no product commit on origin/master |
| CARD-1745 | operator hold: repair in review |
| CARD-1746 | the investigations path in the description is context, not this card's landed change |
| CARD-1755 | the investigations path in the description is context, not this card's landed change |
| CARD-1757 | the investigations path in the description is context, not this card's landed change |
| CARD-1758 | the investigations path in the description is context, not this card's landed change |
| CARD-1759 | the investigations path in the description is context, not this card's landed change |
| CARD-1760 | the investigations path in the description is context, not this card's landed change |
| CARD-1765 | no land publication and no product commit on origin/master |
| CARD-1768 | no land publication and no product commit on origin/master |

