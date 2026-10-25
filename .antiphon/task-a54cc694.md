# RC integration top-up — a54cc694

Prepared and pushed 85 ordered cherry-picks (83 executable/test/build/configuration repairs and two prerequisite plan commits). The single gated solution build passed: 0 errors, 1250 warnings. No tests ran, and no rc ref, tag, release script, running qualification or host was changed.

## Identity and scope

- Assigned branch: feat/card-task-a54cc694, fast-forward-only from 665bf12b12f0ce2bb756f4779c134280146066de.
- Previous integration master boundary: c5ab17338632422905b4c3029b79abe6c0dd1100.
- Fetched master boundary, reconfirmed immediately before build: 101fb0a867426a400c0f09a8e5ebc51832bd0bdc.
- Built executable source: 381edc04e4a244bf86f44a2162d338f426d2e5f9.
- Post-build documentation normalization: 2b7383b243a37776a879a0cc0f35450055de4fad; one blank line deleted, with no executable difference from the built source. The subsequent report commit changes only this report. Final pushed tip/readback appears in the caller-facing completion.
- Protected rc readback: a97194ee7a4adf215454fd82c453ba46362596ee refs/heads/rc/2026-10-24, unchanged.
- Absolute deliverable: /work/worktrees/task-a54cc694/.antiphon/task-a54cc694.md.

## Selection and prerequisites

Git log identified 100 commits after the previous master boundary. Initial git cherry marked all 100 missing. Selected all 83 commits touching any non-Markdown file, plus 9eae50368e5e83cfc1a76ff6989735c915c1fbff and ea6799c2f6db82cb0c1f8241d26b9bb9d6c3e6d0 to retain the shared-build checkpoint syntax and slice-label history used by later mixed executable/plan commits. Omitted 15 documentation/investigation-only commits; dispositions follow. Every cherry-pick used -x and was pushed before the next pick. No executable pick was blocked or skipped.

The inherited integration had zero executable/build/configuration/test differences from c5ab1733. Before each selected executable pick, every changed non-Markdown path's current blob (or absence) was compared with that pick's master-parent blob (or absence): all 116 checks matched. Thus the picked code had its actual master preimages, rather than an assumed compatible prerequisite. Final executable/build/configuration/test comparison to master 101fb0a8 is empty. Post-pick git cherry finds 84 patch-equivalent picks; 583a36b0 is the sole selected non-equivalent whole-commit patch because its previously absent plan was restored as a full file. Its executable patch and final executable tree match master.

CARD-1574's named .antiphon/task-ad9a35c6.md does not exist on origin/feat/card-task-ad9a35c6. Reconstructed all 22 commits from that branch, matched their ordered subjects to the 22 landed master commits, and read the committed reader/runtime/queue checkpoint records instead. These records include the complete source inventory and preserve outstanding qualification. The source/master mapping follows below.

CARD-1574 S1/S2 prerequisites are already on this rc integration: existing standing-Claude resume and launch-note paths; RuntimeActionKey with its unique session/key index; original attempt generation/time/baseline fields; accepted generation carriage; lease-fenced transcript persistence and retained persist faults; and TerminalSeatReleaseObservation.IsCurrentFile. The two feat-named production commits implement the specifically commissioned repair to these existing paths. They require no unrelated master feature, migration, setting or new scheduler. S1 DTO/test scaffolding precedes the tailer/transport implementation and invalidation repair. S2 runtime scaffolding precedes persistence qualification and import repairs; the durable queue tests precede the queue fence, ownership and deferral repairs. Matching server and runner readiness support still needs caller-owned activation qualification; older peers yield Unknown rather than a false negative receipt verdict.

## Conflicts and resolution

1. Cherry-pick 583a36b0b1c4a6b4a414bce9e3d6d1c57e2563ed: modify/delete conflict on docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, because the previous integration omitted the plan-only master prerequisite fda635c6641fccc84c97dd299aeae205f14eec88. Restored the exact complete plan blob carried by 583a36b0, then continued. The test source applied unchanged; no executable conflict occurred.
2. The read-only master merge preview subsequently found add/add on the same plan, solely because omitted docs commit 49c700fa had deleted one blank line. After the build finished, normalized the plan to the exact final master blob in 2b7383b243a37776a879a0cc0f35450055de4fad. This incorporates that one whitespace hunk without importing 49c700fa's investigation report. No production/test source changed after the build. The repeated preview is clean: git merge-tree --write-tree origin/master HEAD returned tree aa522da10f86874efddb255f549c49ede29bbc59. No merge was performed.

## Ordered pick mapping

| # | Master SHA | Picked SHA | Change |
|---:|---|---|---|
| 1 | 887b7f4bbdc479e7cc428e7dfff866a970f78249 | fa33dcc0155eb46592c6c44f4405fe59ffd22f43 | test(CARD-1577): expose group 71 script cost underpricing |
| 2 | 028f89fde5bd352fb0eb9c87a5952d5d841ff26b | fe9714418cd4f10afcfc23a80ade4c1702c07159 | fix(CARD-1577): price costly script classes from Windows observations |
| 3 | d35b37a01c4f1f3027ee53cc3f2b7d1106fcc322 | 15f87f5c23de831447e1e5856409ec2f82ee00ac | test(CARD-1577): pin recalibrated full-listing costs and launch census |
| 4 | 20d89e631840ca901fa59a781223bfe85c8b9b9a | aadbf7847182afcc43934b0cce929cf0afc44464 | test: execute durable actions in duplicate session-finish witnesses |
| 5 | 89933526f0ac7391c7a0b84bb3afb4585b73d7f2 | dc4dd89b8bd4f25b1bf858abad124c23cc4a6325 | test: reconcile standing boot prompt writes with queue state |
| 6 | 9f085e54a78643bf0392cfa9ea863c71eee6a8f0 | 0b221b81d1a5a7bb7a968611320c19de1d158da3 | test: bind Interim workspace matrix to explicit fixture defaults |
| 7 | e87e3d65b647d337fe69854a22b841694305f58d | e9a0a8eaa359f8659caf0c0599e0d74b46f0f26f | test: bind gap runner store and render current provider composers |
| 8 | 17ccf2511d5ef2e21d5fcc9daa03050f828d1ff8 | c2383ef8ccd604dec1525f5d11f3f2d3e7a714e6 | test: distinguish fresh and existing-agent Interim workspace defaults |
| 9 | 110693b1e917dc966aba55cb5efd01ebd6bbe54d | 77df2450705a65daaa57d0dcf4bc8431bb82c646 | test: model guarded Claude input and join gap recovery actions |
| 10 | c5501c01c40913120e350ab0f984cbdc5ae19ec7 | 7832cdccafc5438c2d94268c5b27b02f890f5ad7 | test: align gap recipient clock and guarded terminal sequences |
| 11 | 443c97a9bd9ac4bd3692c7904718d0a0c98ff5d2 | c947d1a81a27f476dcf5a0729f8b11458833ac97 | test: witness missing conditional-input spill transport and pre-input failure |
| 12 | 7110b93de928dd07156748a83b572bb5d4ede0ac | f0f79cba2611caafba1d2bc169b1896fae990f13 | fix: carry runner spills through guarded conditional input |
| 13 | 8123452bb50224dd75724055e8f3d29f6fb13c66 | ce0e723d8d3c40036f9941baa998bc2f80769a0e | test: bind gap receipt selection and postcommit cut to current contracts |
| 14 | 493cd6b44b2ac1cf17a5759befd5251116aa96e0 | 086d209361414217fe9136948edf58d793d72f67 | test: match release metadata fixture to diagnostic record delimiters |
| 15 | 501ebf1b3b48494104f54078542e006e2d1edcee | 764e5a8affa13f4074ea68d5c5910271deba9ddc | test: supply observability discovery in release publication fixtures |
| 16 | 0cd36ac0258ee2872522911b89360c1b5a13c484 | 057b33435ed40770aa46acb75bf88b74e82fa2ba | test: copy the complete script fixture project census for RC hops |
| 17 | 64b9de3edb03d2140bfc25a8a1e08dda770a9618 | ae9a92133494552ec9c9a38864b2f48509f28f7a | test: isolate repeat script outputs by fixture root and clean them |
| 18 | f11c735b52f0db18fbb7e7f5825e7a6a2f8812cc | 33b5be651e574b9d259efd77b0f6a7627e5b681d | test: assert cancellation cleanup for the captured invocation paths |
| 19 | 4f9eb9f3aa097ae8e78758d57ed69b591a019ff1 | f24fe8c2a136c0aec6f03850ed888a08757ea2b8 | test: bound promotion reservation matrices without widening timeouts |
| 20 | 3c94a31d4b3f44628d5d0d0122616d29c1de7ca5 | 553a2b13114ed27639cd6a56df1ed850a2942b63 | test: distinguish timing summaries and bind RC hop platform observation |
| 21 | a315d4d390afa8cef6fd69f5cd315ffe4a5e1cd7 | 0d2e58b8cc4377ba32da9c8522711fe05a9800c6 | test(CARD-1574): witness missing live transcript readiness |
| 22 | 7215321433e986dcbc87705852210e607eeb500e | b0925b1ab32627da11978e044f1ba8e831ca6e25 | feat(CARD-1574): publish current-file transcript ingestion readiness |
| 23 | 0de83a984a1f04f5012119a9858cb54e6ecae790 | eaabda4ccdf28f45e1afb775030215f788025c30 | test(CARD-1574): give fork fixture qualifying ownership evidence |
| 24 | 583a36b0b1c4a6b4a414bce9e3d6d1c57e2563ed | 125c720b97ac58b579f0456b71985727d8a99c8d | test(CARD-1574): expose regrowth and malformed-byte readiness holes |
| 25 | 3f2deea0e0da1527d849d484819eff4fe3fbd3a6 | 7ca66ce3bc21c4eaec6b7fad9a79ec2b2dc5aa03 | fix(CARD-1574): retain invalidation after truncation and malformed bytes |
| 26 | b467b2f7530a303fc93887d67108e42fc4a6e2bc | 8b9cbb59bcba895cabdad99f46eae3fc9c3d3966 | test(CARD-1574): witness committed same-launch receipt readiness |
| 27 | 49b6e9a09d43272950fb35ca61eb789f7d962a0b | 01e0a05c850dd523f22005cf86cc19b14c3d25be | feat(CARD-1574): qualify receipt readiness after same-launch persistence |
| 28 | 9eae50368e5e83cfc1a76ff6989735c915c1fbff | 26dd03fcfe5a230f83ec73c65729e78bef15f7d8 | docs(CARD-1574): use importer-supported shared checkpoint build |
| 29 | 3e1fe35791243127d455a15e4d5462e34e4804bc | 50dbb0aa3d1810e94222f7f4c672c0a9b6bd3efb | fix(CARD-1574): import generation comparison for readiness observer |
| 30 | 41794364f660043ce80250943c47c813b0a40742 | e4ba77105c8a4f47fb56b3103febc1767b518bb9 | fix(CARD-1574): resolve generation helper from runner contracts |
| 31 | f49fca92a2a4e1489a63a13e8775b09eceb45a67 | 4a06d62c5e49dc2f213b0b063a57d40c97a7de88 | test(CARD-1574): keep route-race fixture bindings structurally valid |
| 32 | ad50d1598bcf7f60cca46a78aa8ba7d3530a91b2 | e0525f8baf1f51bcfa3a131411be7cc61f290a84 | test(CARD-1574): preserve remote store during commit-race restoration |
| 33 | 2ef067dadcda10bcbb25b90c6f9c6f2e91fb164c | e9c566c8dbf30f6c30398f643e08a77c00547752 | test(CARD-1574): witness durable resume queue and readiness fences |
| 34 | d6356d0a02a8a593cb532886deacd12cef5927ce | b5d139c289d133806250790a72fcb626c42ccdaa | test(CARD-1574): import queue DTOs and exercise delayed replay |
| 35 | b0f7c20f73adb1516cb216ba8f83294b3e971eab | 3e4cbf5fb1636526c9f7f75fffd48cdb87324f41 | fix(CARD-1574): retain one resume note behind receipt readiness fences |
| 36 | 0680585365e761a1a00a6c879e6fb4b9e443457b | 0bd0df87cbe9079228b70acfe8091f5c1303287d | test(CARD-1574): expose note-text scope and old-claim recovery gaps |
| 37 | ea6799c2f6db82cb0c1f8241d26b9bb9d6c3e6d0 | f36bb12e541b366b0430a1e2f4f57042fb68e0d1 | docs(CARD-1574): align shared-build checkpoint slice labels |
| 38 | 4b73b87994566058a32b3ff253eabfbc74585438 | 52340a3c5f2a64570447b83a72b845f2f125d1ed | fix(CARD-1574): bind receipts to queue ownership and retain recovery debt |
| 39 | 50a4bfc84b8a6441fcdbed5e1b39104810a9a1a7 | ad34ee5141ed3ae6ac745d43e579edc85fd4d031 | fix(CARD-1574): propagate uncertain receipt attempts as queue deferrals |
| 40 | b3420e3f0d0dd9faa770bf21b87803ac977a59c0 | 9205b972623e668f896cac9aa737dc97dd1ff10d | test: preserve queue harness scoped context through service calls |
| 41 | df1a80b9e9023a08f39b92a6f889ef1621c7fc6d | b5ac692878b91a1153c8ea96011ac4789c143f76 | test: await asynchronous argv harness disposal |
| 42 | ee8d787178035cfdb69aa62464c0dc6ab406cf66 | 840364f320a505af8b957e883f536ff640ee8190 | test: bind replacement expectation terminal dialog generation |
| 43 | 747a6b4dd1ef515ee6a98b4e9b5144786d539ca3 | 72bc62602529c798b9140822110f61ad6e379b5c | test: assert review cleanliness in delegate status output |
| 44 | 0d1972a4bc1d8b76b2bda0522fa887dda1359402 | 6fb0e95b53b191211f7098242ed577e4a2626e11 | test: bind platform dispatch branch to task identity |
| 45 | 9f8ed85c7de53143e8977b4e7eec1cd73e54b96b | bd50329ad1648843f93a34156c4a9c14e8766a01 | test: use valid remote prep branches and await peer requests |
| 46 | 627addacccfd2f1eac49d58356d8c106add1005d | 01c6d6997ada4da388ec7c5db4d5c4d647f47694 | test: publish idle transition before flushing queued runner spill |
| 47 | 2a5df2f6581de6170d9b27e5a41bf86c0fb19c8e | 7d8afb11e2a27478aaf0f7eb6aed8261b20d9efe | test: distinguish named cleanup upgrade from latest schema |
| 48 | 52e87fd687b8237e16faf2746dfe16b12c1d65a6 | 3fbffd4f83342c25edbf1dda176776edb8293c15 | test: assert optional document conversion preamble contract |
| 49 | 1f6bfc54f7bb4f4a9a5a8d98f5364ad5fa9b6903 | 5a6ad977b8614c67e52bba7ed4d6acb43930ecf3 | test: attach guarded Claude terminal to matrix receipt fixtures |
| 50 | 813b4ec93455c96af4494174af494a44da0cd4de | 1e9d82374c6a43d2805d1b008368f467e20a05b1 | test: execute committed compaction actions before recovery assertions |
| 51 | 63b2600da60f6d2996947e355743fa4f7837ef4e | 444d2f5668a8ce4a460372442d427a4b26a39441 | test: settle snapshot runtime actions before asserting retention |
| 52 | 523bcd9ff5443690dafaf3a6b8f6029f35edef93 | 60fbabdd881162571854f57b1906e033979abdcc | test: delete tracker fixture cleanup obligations before revisions |
| 53 | 57e0041bdda2e5bd6bc035ab69235b16817e6338 | 90276a049b310bf5c268913fc136d2dc6ca84c13 | test: isolate retirement lock barrier from container removal work |
| 54 | b98e6a05ee45d08548f1ba5eab0ccffa1ea0493c | e0d051899b5d93268c35eab8253972d4a9b8e279 | test: give checkpoint source rebuilds fresh owned outputs |
| 55 | 343fad4f71a11658f6e6da2d1a4d71453ae2dbfd | 6f0b3347f71efbd16cae91d467df428d7b6dd4e9 | test: skip native Windows job ownership cases before setup on Linux |
| 56 | 739475934fa27140f25e632f1547a918c45f2670 | 3332e142bf0d740aa9f1016b072553501e808f1b | test: bound legacy rolling roster modes independently and cancel children |
| 57 | 78d3b62ce779d5d26fefa9159c3d1244ee9af7ac | b4df7ddf2bd9fa59f7b66d2177fec72936d8e66d | test: scope alarm queue assertions to owned sessions |
| 58 | be50cd80403a5db808d295abc09e55b671dd3f92 | ac3ef0bb5cab2b8de7bc362fd0cac3cfa8936d84 | test: assert composite eligibility observer wiring |
| 59 | bd5df3df1216d7d689b0eeca63966084e9195c1e | 914eb82d89e9f92057d9a712dd7a0d421158ddb1 | test: reuse the session-state persistence fixture |
| 60 | 963b955467daccd846434d8478d8670bde8d4e8b | 7589e7352f7945e8f646172022df2d3e4d01aedb | test: expect both advertised server capabilities |
| 61 | 7833196f81c39e33054bc3cc986cebf346038760 | 0e24d77df7e07cbd903c9de152f4ced6eca7543a | test: delete card cleanup rows before revisions |
| 62 | 8a26ed2c5241024432ae49b6dddefbca050b570e | 5aa3d34470f85dbd1ef3adba53314824507a35a2 | test: respect card cleanup foreign keys |
| 63 | 39e0ac7d6b5da9d018e41846baa671bec59b0e43 | daf0818e8f71153409ca85004270b8b7e7c8a4d7 | test: retain channel correlation in recovery expectations |
| 64 | eea738bc8060e390f256fc15d5e8a6b71b5e8c65 | 2d8883fa06c66735f47fb47aa8b99c614f7b4859 | test: stamp launch effort on the reusable pool control |
| 65 | c478d5c40a5afd48fefa693d140bdb07720f0e2e | b609e368b8f60ccfa1f8ace7ce677f43a87b2619 | test: gate queued exits on synchronous output publication |
| 66 | a12b48dc29ad9018870c2eaeb1624f937837479e | 82298cd02aa66ddd7db1d932d2add49ebba7506d | test: enable bound Claude observations for delivery |
| 67 | 83a67fdd3853b9b5a3360dc1fc1df4c3c0e5e228 | ec129afc6a45111d1ef923b219d085f9d3cb1217 | test: seed current entities before historical schema rewind |
| 68 | fe0993dd469aef992269ce701aa6ce8480232e43 | eee7b319c81db177a42814f68d2d11f31025c94d | test: read only historical columns during upgrade checks |
| 69 | def577fd1c74870dbda9b160f49e3ea180305731 | 0cefbd19ba4c1f286513dd937306e2b3061a47e6 | test: distinguish observation pins from owner ref mutations |
| 70 | 0eaa21329a0bf5a2723f93626d8ae5b91649ebb2 | fa300bb8a05c0063f1a3024b9c9d07858c08c90d | test: join deferred sibling inspection before the claim tick |
| 71 | dbbb724c84343d5df3c0e2aa12e4f397cd4a6aef | 4e4eaa5fef737cc9ff2b0b5bbc98aa55d1bf2594 | test: supply session state and drain admitted turn actions |
| 72 | 72afc8f9e7c46cbc93784760d74a611649f7c844 | c0b6136be80c3f7825e933eaa783f7e431d41641 | test: model rendered grids and explicit interactive input |
| 73 | 0bd03317c78db1b0ff957b0dc435c7f030b48be9 | 24fe7f7a2e3e6e64399219cfd0be4c0df1b677e0 | test: expose worktree dispatch refusal diagnostics |
| 74 | bae5f0a30abfb8538e0497ba6a37ef5d76ea9064 | 127208d5fc74c38abc4cbd1e6191d4ef06e76e10 | test: start the sibling inspection owner in mutation dispatch fixtures |
| 75 | 901dc09f199bcf9dc2de42210fcde4b044776cf6 | 33922b50beb2236a28db1b06aebc4c4178a439ed | test: preserve Grok identity and transcript DTO imports in persistence fixture |
| 76 | cacd5a7c8a62d8cfaed9d1e82f5ce92e63c4d346 | 5bb008c7f3f19f419288ff68eab3b92c751b309c | test: dispatch isolated scope tasks without existing-agent pins |
| 77 | cc8782b013ef328f8a864461c64fa08b88a11eaf | 9c775afe3220b4039091e2d6f729ce0679720e64 | test: bound checkpoint scheduler deadline completion witnesses |
| 78 | 569f3c387ded1b623deb90252baa3e1bc9775c18 | 066cde34edf8405deafb083bc5ef9ae3dd06484f | test: witness budget callback disposal self-join |
| 79 | 807e725c29a139698c8d1feeaaf42cd19f4f73a3 | 6cc929ee4bcdcd3cd908a6d6c4ac246d1b727dc7 | fix: await checkpoint budget timer joins during async cleanup |
| 80 | 1600f4bb7292db031eabbf6481691e0d644055e5 | 57de6d4961fb7fceebdbb521e19e27961d7f8fe0 | test: assert charged execution budget timeout diagnostics |
| 81 | 38d2b4d30c5be957beb0dbc491bf54ddfaaabb01 | 948d2a3bcef729a9db21df89530d05b5a489ac2c | test: use canonical OR operands in coverage census fixture |
| 82 | c494c9c6841041ccfc8792b1b739f52f83f40d22 | ca168a419ad9d94d5a0f1c6ff85dcd9392f215c6 | test: preserve source refusal beneath failed report precedence |
| 83 | 1ecfca9dceb42987788b75bf6fe66ccf66ab8647 | fb69c8791604097a880b9b9cedd818b62c94442e | test: force checkpoint budget timer to fire before its deadline |
| 84 | 3a646cf19b0c3c623a0e3d37fd86639599f8f63f | 7faa945d84f9214fd5092bd40386db9aff1ae71b | fix: rearm checkpoint budget timers after an early callback |
| 85 | 3edae481d5f0fb73dfb40c58d016090c9b6c7bbd | 381edc04e4a244bf86f44a2162d338f426d2e5f9 | test: isolate and reclaim repeat-build fixture outputs per invocation |

## Skipped picks

All rows below are documentation/investigation-only, not blocked executable repairs.

| Master SHA | Reason | Change |
|---|---|---|
| 1a5651eabe550d3461d2a7869e37ce266f21c53e | No executable prerequisite; narrative/investigation/evidence only | docs: investigate rc defect ownership and reconcile sibling content |
| 2d06d0a46cd72ea1b03e6cfacf035672f0122d7f | No executable prerequisite; narrative/investigation/evidence only | docs: record group 49 Linux reproduction and native rerun rows |
| c08244968149a4a00acc4d3bb013f49bd602a18e | No executable prerequisite; narrative/investigation/evidence only | docs: record group 36 fixture and conditional-spill verification |
| f0911378c59116609161a57141e6fa35bd7d388c | No executable prerequisite; narrative/investigation/evidence only | docs: retain final shared-fixture alignment and tested SHA limits |
| 49c700fa4267e6c5f777ad5afb54041dc3021409 | Investigation report omitted; one plan blank-line deletion incorporated separately in documentation normalization | docs(CARD-1574): preserve partial reader evidence and remaining Code scope |
| 6889e0cb922f64526bda149eb420b9d080609773 | No executable prerequisite; narrative/investigation/evidence only | docs(CARD-1574): preserve runtime evidence and remaining queue scope |
| f5456c2663d85ef3cc66c02f2d3ccaf393252857 | No executable prerequisite; narrative/investigation/evidence only | docs(CARD-1574): preserve queue-fence proof and remaining qualification |
| f903edacf82fba3dbc0a7057970265600ed46c83 | No executable prerequisite; narrative/investigation/evidence only | docs: retain reproduced failures for assigned rc groups |
| 4362e3ece2cfb6846882725d51f1927256e47e8a | No executable prerequisite; narrative/investigation/evidence only | docs: retain newly exposed tracker cleanup failures |
| e4da6e28d0e1b6c2fb6b1025e956a8f52114c26a | No executable prerequisite; narrative/investigation/evidence only | docs: publish rc group fixture repairs and verification handoff |
| f2b167a405f009d4dac303e79469b1e91b0416a1 | No executable prerequisite; narrative/investigation/evidence only | docs: record group 71 fixture fixes and incomplete qualification rows |
| e6211673d7e10617b0c1c8831b86fb28ca983b7e | No executable prerequisite; narrative/investigation/evidence only | docs: record group 71 continuation and inherited legacy timeout |
| e49be26ddd9a578277aaefc13bbeb7a555ce23a2 | No executable prerequisite; narrative/investigation/evidence only | docs: record completed group 71 Linux continuation and fixture evidence |
| bc8eedfdac8a288b84b7dae678485b504f8552d1 | No executable prerequisite; narrative/investigation/evidence only | docs: record verified Linux fixture repairs for CARD-1514 groups 42-58 |
| 101fb0a867426a400c0f09a8e5ebc51832bd0bdc | No executable prerequisite; narrative/investigation/evidence only | docs: record checkpoint cluster fixes, evidence and native rerun scope |

## CARD-1574 complete source mapping and per-pick prerequisite checks

For each executable row below, Exact means every changed executable path matched its landed master-parent preimage immediately before picking; earlier rows supply the newly introduced readiness types and methods. Documentation rows retain their explicit disposition.

| Original task SHA | Landed master SHA | Disposition / prerequisite check |
|---|---|---|
| c7dc923efee268f06ea2b7dae779a7c237819bc5 | a315d4d390afa8cef6fd69f5cd315ffe4a5e1cd7 | Picked; Exact (3 paths) |
| 7ce076ab619dbd5f2f57af9af4822e3aa8e990d0 | 7215321433e986dcbc87705852210e607eeb500e | Picked; Exact (4 paths) |
| 8d2a4d5a7c3b31544db5ea1a30c0a5c6287190e7 | 0de83a984a1f04f5012119a9858cb54e6ecae790 | Picked; Exact (1 paths) |
| c489fc8e79ce7e0ea07b9f8c1512d65a741e9904 | 583a36b0b1c4a6b4a414bce9e3d6d1c57e2563ed | Picked; Exact (1 paths) |
| 420c32d40f92814f633afab5ef847f2647885c8c | 3f2deea0e0da1527d849d484819eff4fe3fbd3a6 | Picked; Exact (2 paths) |
| 1cacb12dc6cde604128a12871cb574c20dbc7460 | 49c700fa4267e6c5f777ad5afb54041dc3021409 | Skipped report; whitespace hunk incorporated separately |
| 3b60c1c1f5a5100d9f02b6a032d48628dc824737 | b467b2f7530a303fc93887d67108e42fc4a6e2bc | Picked; Exact (4 paths) |
| 29cb21404e58d9ad00fa82ef0b928ed183e462e7 | 49b6e9a09d43272950fb35ca61eb789f7d962a0b | Picked; Exact (3 paths) |
| ed0823d04ae978f26a93a2e5afe71f1d59ef4317 | 9eae50368e5e83cfc1a76ff6989735c915c1fbff | Picked; prerequisite checkpoint plan history |
| fac4d9851ae71ca682247876d7500008338d5f49 | 3e1fe35791243127d455a15e4d5462e34e4804bc | Picked; Exact (1 paths) |
| c091c1e8a781f5b215d9be43a25a3a63407ab878 | 41794364f660043ce80250943c47c813b0a40742 | Picked; Exact (1 paths) |
| f30accf2178662fb99bb8172d3540fe836b3614d | f49fca92a2a4e1489a63a13e8775b09eceb45a67 | Picked; Exact (1 paths) |
| 0166b5f488fcba7a73e98cd0cbc630ecba57f508 | ad50d1598bcf7f60cca46a78aa8ba7d3530a91b2 | Picked; Exact (1 paths) |
| af81145b8fdd157445562314955c4cbe976e580e | 6889e0cb922f64526bda149eb420b9d080609773 | Skipped; documentary checkpoint record |
| a9b65717793eaa0850eb8136d1fe553a9c357f2a | 2ef067dadcda10bcbb25b90c6f9c6f2e91fb164c | Picked; Exact (3 paths) |
| 8bdf2b1f7162dfc72f07de06e34c5c673f96d544 | d6356d0a02a8a593cb532886deacd12cef5927ce | Picked; Exact (1 paths) |
| 91b57789e978f9958fa20ca829e435bdae8acfef | b0f7c20f73adb1516cb216ba8f83294b3e971eab | Picked; Exact (4 paths) |
| 622b832e2aa4ac5e3bf36576634b1e587974e89a | 0680585365e761a1a00a6c879e6fb4b9e443457b | Picked; Exact (1 paths) |
| a8cfc84394b62eb2384c1a2bff7e29d2adc5dfc3 | ea6799c2f6db82cb0c1f8241d26b9bb9d6c3e6d0 | Picked; prerequisite checkpoint plan history |
| 3dc86c36458b46b39ca764dd0418a9c7ed6c5714 | 4b73b87994566058a32b3ff253eabfbc74585438 | Picked; Exact (6 paths) |
| 3165a54d9e4f1c01a8dc38a42ba941418c708780 | 50a4bfc84b8a6441fcdbed5e1b39104810a9a1a7 | Picked; Exact (3 paths) |
| f100a8c1216353b1353ee1649b4f178c7376e394 | f5456c2663d85ef3cc66c02f2d3ccaf393252857 | Skipped; documentary checkpoint record |

## Verification and cleanup

- One solution build at 381edc04e4a244bf86f44a2162d338f426d2e5f9: exit 0, 1250 warnings, 0 errors; compiler elapsed 00:04:11.25. The warning set includes ASP.NET analyzer AD0001 exceptions; the successful build does not resolve or suppress those diagnostics.
- Command: pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-topup-a54cc694 -- dotnet build Antiphon.sln --property:OutputPath=bin-a54cc694-rc/ --nologo
- BUILD SLOT granted lease=a7c7a731-116f-4dee-8b5d-35102e72c37b waited=0s maxcpucount=6; released held=253s. No second build, unleased retry or test driver.
- Tests: executed 0; no test sweep, native Windows probe or qualification. Qualification remains the gate, not this compile.
- Ignored build log: /work/worktrees/task-a54cc694/.antiphon/task-a54cc694-build.log. Generated manifest, cherry comparison, source mapping and cleanup inventory stay ignored beside it.
- Output baseline was empty; after joining the build, removed all 49 producer-owned bin-a54cc694-rc directories; zero remain. Tracked source stayed clean throughout build and cleanup.
- git diff --check 665bf12b..candidate passed. Executable source comparison to master is empty. Only the documented whitespace adjustment and this report follow the built commit.
- Evidence-history audit at built source: commits=85 entries=0 violations=0. The caller-facing completion carries the audit of the final pushed report tip.
- Every commit/push targets only feat/card-task-a54cc694. No rebase, amend, reset, target merge, force-push, rc publication, tag, release script, restart, deployment or qualification interference.

## Handoff and retained qualification limits

Batch C (groups 17/3/73/66/74, task 2ef05012) is still in flight per the commissioned handoff and is not in the fetched master boundary. It will need one more small top-up after landing. Do not publish this intermediate integration as the complete final batch.

Next is an exact-tip Review. Preserve the previous integration's startup/native publication gates: the running Windows qualification of a97194ee must settle before rc publication; obtain the required exact-candidate Windows native probe and later fresh qualification under docs/release-gates.md. This task grants no rc publication or activation.

CARD-1574 inherited checkpoint reports explicitly retain incomplete whole-plan V-6/V-7 coverage: independent acknowledgement-only and native-acceptance-before-persistence reconstruction, launch producer response/event failure injection, and keyed-note NoComposerEvidence/Truncated/clipped/QueuedUserPrompt/screen-surrogate cases. All Mutation PCs remain Pending. This Merge task did not execute those tests and does not relabel the source as Final/Full or deployed. Review/caller must retain these limits when deciding readiness; the brief's qualification gate remains due.

--- next stage ---
next: review
handoff: Review the exact pushed feat/card-task-a54cc694 tip, ordered coverage, documentation conflict resolution and retained qualification limits; await batch C top-up and the running Windows qualification before any publication decision.
artifact: .antiphon/task-a54cc694.md
