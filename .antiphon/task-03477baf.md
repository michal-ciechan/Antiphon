Published CARD-1729 Phase 0 wave 0a to master 3737148ee1e43e3695174b7a8e9befeb6d69fb41; final verification passed.

Base: 1038d07e66a54b630a1bc1d394923bc7049b73dd. Assigned branch: feat/card-task-03477baf. Integration loop count: 1; re-preparations: 0; push retries: 0. A fresh fetch immediately before publication still named the base. Plain fast-forward push succeeded. Merge-tree was conflict-free: de4ea6bffb4da5bb2563c498639d2fac597e7f59. No merge commits, squash, rebase, rc push, land-graph -Update, desktop execution, deployment or restart occurred.

Authority: continued under the caller's explicit waiver of intermediate guard-stack buildability and explicit master-only landing authority. The initial blocked report remains historical; the caller reopened the task and authorized preserving every red-first guard commit individually. All 29 source commits retain cherry-pick -x provenance. The task branch stayed local until the master push; this report is published afterward on the task branch.

Changed deliverables: the path-based guard, engine, map, warn-mode allowlist and project-context paragraph; the guard's repair design, mutation recipe and historical investigation documents; 40 byte-identical production moves; the verbatim RunnerRequestIntent/RunnerRequestSource split; and F2's relocated resilience pin. The only additional repair is allowlist reconciliation at 3737148ee1e43e3695174b7a8e9befeb6d69fb41. Engine/map/strict flag were not edited by integration. No csproj/props/targets/sln, selected build-definition record, target pin or policy input changed.

Verification

| Selection | Passed | Failed | Skipped | Evidence reuse |
|---|---:|---:|---:|---|
| CP-1 guard, including repaired L5/L6 variants | 9 | 0 | 0 | one execution at final source |
| CP-91 policy sweep | 50 | 0 | 0 | 39 classification policy + 1 classification guard + 5 registry merge + 2 tripwire + 3 native Linux witness |
| CP-92 mechanical instruction contract | 11 | 0 | 0 | same gated run as CP-1/91; total 70 |
| CP-2 A1 | 88 | 0 | 0 | 76 ModelAlias + 8 concurrency + 4 pipeline stages |
| CP-4 A2 | 56 | 0 | 0 | 37 provider contract + 7 lease waiters + 12 renderer |
| TaskPlatformGuidanceTests | 5 | 0 | 0 | same gated run as CP-2/4; total 149 |
| CP-22 E | 22 | 0 | 0 | 8 event pump + 10 host stats + 4 retire job |
| CP-5 / CP-23 / CP-24 shared guard | 9 each | 0 | 0 | reuse final CP-1, identical source and assembly; no repeat |
| CP-25 F2 | 15 | 0 | 0 | 6 resilience cases plus shared final CP-1's 9 guard cases |

Unique executed tests: 247; passed 247, failed 0, skipped 0, duplicate identities 0. The three gated invocations executed 70, 149 and 28 cases. Their exact filters selected only the intended classes. Every invocation used native Debug output, --no-build and UseAppHost=false; each acquired its own build-slot lease. No green row was repeated. CP-22 passed first attempt; CARD-1736 subscription-ready/live load sensitivity was not observed. No full assembly or full Unit lane was run. Historical missing gates in source reports and all pending post-land Mutation controls are preserved, not relabeled as completed by this Merge.

The real test's output is preserved verbatim:

```text
VIOLATIONS 0 LISTED 166 STALE 0 AMBIGUOUS 3
LAYERING files=1352 census=0.041s read=0.126s analysis=10.060s total=10.226s
```

The allowlist stays strict: false, as required. Three ambiguities are printed and retained by the guard: Stage in CascadeService and WorkflowEngine, and Context in AntiphonHub. None is an unlisted violation.

Parity

```text
PARITY base=1038d07e66a54b630a1bc1d394923bc7049b73dd tip=3737148ee1e43e3695174b7a8e9befeb6d69fb41 base=22133 tip=22142 added=9 removed=0 duplicates=0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Map_covers_every_server_file_exactly_once.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Layer_of_pinned_files_follows_first_match.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Allowlist_parses_with_grouped_rows_and_a_mode_header.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Repository_graph_satisfies_the_committed_allowlist.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Allowed_edges_listed_rows_comments_and_strings_do_not_fire.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Stale_row_and_unmapped_file_fail_in_strict_mode.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Ambiguous_simple_names_resolve_by_using_or_are_listed.1.1.0
ADDED Antiphon.Tests.Infrastructure.C1729LayeringGuardTests(Antiphon.Tests.Infrastructure.C1729LayeringRepository).1.1.Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors.1.1.0
```

Both complete --list-tests diagnostic rosters were adapted with ConvertFrom-NightlyDiagnosticLog and ConvertTo-NightlyDiscoveryDocument. The baseline contains 22,133 unique UIDs and final 22,142; exactly the nine new guard methods were added, none removed or duplicated. Raw diagnostic, discovery.json and uids.txt files remain under /work/worktrees/task-03477baf/.antiphon/c1729-parity/{base,final}/. Discovery was used only for parity, not execution credit.

Builds, gates and provenance

| Driver | Source | Build time | Errors | Purpose |
|---|---|---|---:|---|
| baseline Antiphon.sln, serialized | 1038d07e66a54b630a1bc1d394923bc7049b73dd | 12m47.67s | 0 | required master parity baseline and evaluator prerequisite |
| guard tests project, serialized | 0841539b099923d757b86428815f2f0316be6637 | 7m07.22s | 0 | caller-authorized guard-stack build; inventory prerequisite |
| final Antiphon.sln, serialized | 3737148ee1e43e3695174b7a8e9befeb6d69fb41 | 3m58.54s | 0 | final tree verification |

Baseline and final solution builds each reported 1,887 warnings and zero errors. No NETSDK1047 occurred. Each build held its own lease, used -maxcpucount:1, UseAppHost=false, TEMP=/tmp and the native default output. Explicit outer budgets were 45m, 35m and 40m with 12m slot admission caps. The two parity discoveries had their own leases, 22m outer caps and 12m admission caps. Test groups had separate leases and 22m outer caps, including 12m admission. No driver hit its cap or exceeded 30 minutes. Baseline host load was sampled at 81.16/64.57/46.84 at 06:20 UTC; exact guard/final build-start load was not captured.

baseline build admission: BUILD SLOT granted lease=ed3614e5-291a-443e-b170-8f3e59022d7a waited=120s maxcpucount=6 | BUILD SLOT released lease=ed3614e5-291a-443e-b170-8f3e59022d7a held=770s

baseline discovery admission: BUILD SLOT granted lease=67410161-debf-4f57-88a2-5d6f0715b7c2 waited=541s maxcpucount=6 | BUILD SLOT released lease=67410161-debf-4f57-88a2-5d6f0715b7c2 held=14s

guard build admission: BUILD SLOT granted lease=dc7ee0fe-9643-46d3-9546-583d4bb52400 waited=0s maxcpucount=6 | BUILD SLOT released lease=dc7ee0fe-9643-46d3-9546-583d4bb52400 held=428s

final build admission: BUILD SLOT granted lease=9554e853-d6fe-4894-a68f-978ca7504050 waited=0s maxcpucount=6 | BUILD SLOT released lease=9554e853-d6fe-4894-a68f-978ca7504050 held=240s

final discovery admission: BUILD SLOT granted lease=4f4bee47-65c9-4e85-8ffa-571621fa2811 waited=60s maxcpucount=6 | BUILD SLOT released lease=4f4bee47-65c9-4e85-8ffa-571621fa2811 held=38s

guard-policy-contract: source=3737148ee1e43e3695174b7a8e9befeb6d69fb41; DLL SHA-256=6b514f3fc63bb406532d1ba5565472594ba55544568b4a6838811fe32fc92d25; wall=249.968s. BUILD SLOT granted lease=9492a1f6-0c17-4657-b21c-74e7af5ce339 waited=105s maxcpucount=6 | BUILD SLOT released lease=9492a1f6-0c17-4657-b21c-74e7af5ce339 held=143s. Per-test durations are in its TRX; phase JSONL is under its timing/ directory. Missing fixture/external-call spans remain unknown, not zero.

core-guidance: source=3737148ee1e43e3695174b7a8e9befeb6d69fb41; DLL SHA-256=6b514f3fc63bb406532d1ba5565472594ba55544568b4a6838811fe32fc92d25; wall=74.593s. BUILD SLOT granted lease=ebaaa782-7766-461d-9447-71ecdd9e1ad1 waited=60s maxcpucount=6 | BUILD SLOT released lease=ebaaa782-7766-461d-9447-71ecdd9e1ad1 held=13s. Per-test durations are in its TRX; phase JSONL is under its timing/ directory. Missing fixture/external-call spans remain unknown, not zero.

drivers-resilience: source=3737148ee1e43e3695174b7a8e9befeb6d69fb41; DLL SHA-256=6b514f3fc63bb406532d1ba5565472594ba55544568b4a6838811fe32fc92d25; wall=106.795s. BUILD SLOT granted lease=d4075272-54e6-437b-93d0-491c5d036741 waited=45s maxcpucount=6 | BUILD SLOT released lease=d4075272-54e6-437b-93d0-491c5d036741 held=60s. Per-test durations are in its TRX; phase JSONL is under its timing/ directory. Missing fixture/external-call spans remain unknown, not zero.

Final assembly SHA-256: 6b514f3fc63bb406532d1ba5565472594ba55544568b4a6838811fe32fc92d25; server assembly SHA-256: 3f9c909ce2bfb276f54516037d54dfa1be928696eef2c606a4ac1f938fb6f754; source tree: de4ea6bffb4da5bb2563c498639d2fac597e7f59; build-log SHA-256: 6fd3931c8f3475d88abd0171a98dbb5e7bf5f997c5899b21602b35b25ee7480c. All three run manifests independently confirmed the same source, clean tracked tree and same test DLL.

Base and final land-graph -Check both completed with approved after pass1/pass2 and canary evaluation. Definition digest remained D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69. There is no inherited mismatch warning. The initial baseline probe only lacked its evaluator at the script's default output path; the full check was completed with the baseline solution's native evaluator at tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll. No -Update ran.

EVIDENCE range base=1038d07e66a54b630a1bc1d394923bc7049b73dd head=3737148ee1e43e3695174b7a8e9befeb6d69fb41
EVIDENCE result commits=30 entries=0 violations=0 base=1038d07e66a54b630a1bc1d394923bc7049b73dd head=3737148ee1e43e3695174b7a8e9befeb6d69fb41

-PrintInputDigest at base and final: 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939.
Get-NightlyPolicyHash at base and final: 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72.

The read-only inventory diagnostic invoked the unchanged compiled guard engine through an isolated AssemblyLoadContext and did not execute TUnit tests or claim a checkpoint. Four dependency/reflection harness failures preceded the successful inventory; their logs remain retained. The successful combined inventory before reconciliation was VIOLATIONS 1 LISTED 165 STALE 166 AMBIGUOUS 3. The final real TUnit test independently confirmed the reconciled inventory above.

Per-commit provenance

| Land SHA | Source SHA | Source stable patch ID | Land stable patch ID | Result |
|---|---|---|---|---|
| 228c34013434d94b8b32133b7b308d34341b1646 | 4b66917d6294cc3d6c0b55d969a0fa74cc838e90 | 38470123ccd88bab2e0234cf1f99d337dbc50e5a | 38470123ccd88bab2e0234cf1f99d337dbc50e5a | exact |
| ae6a126590d3c998c060614f218a9135b604426f | feda287e2a6881a249015b269287c0891ad7d5c4 | 9c1c01b11c8aa725332ba3780eb9f543dee80b5b | 9c1c01b11c8aa725332ba3780eb9f543dee80b5b | exact |
| 0cbb280eb68364f9307736bb5819a2957977b8b3 | c8b641e8aec8dc2457cb7fe41fb12e022e230e79 | f102ae7440a58a02b92eb7774273cd99a1ecffbf | f102ae7440a58a02b92eb7774273cd99a1ecffbf | exact |
| d347ce411b866448b39d5b50d630517cf8cd14cb | 3621664c35100b14263cdc967ed05fd00a6e055a | 1cadd9c83f7afaee463762f34ab15844074997d2 | 1cadd9c83f7afaee463762f34ab15844074997d2 | exact |
| 0c523e19c9f8d33655ffb3569058e126ae22776a | e41e6e96ab6ed19d715cf9e94cb6b0acf37da357 | 04bafe5319eeff3482e604b84a696b382cf50905 | 04bafe5319eeff3482e604b84a696b382cf50905 | exact |
| a5cf1044967336508a46842632ae338b109ecf06 | 4b28cae3bf9137ab59496ee902dfc7f14203ad50 | 9232f4d2e9fa7e3e7cc20d206c3e684e5da5c1e5 | 9232f4d2e9fa7e3e7cc20d206c3e684e5da5c1e5 | exact |
| 419b7840bff649a7e79f0ba35f0a28a877874586 | 80a7df2fadc266bf307d08a5e5e53721e594929d | a809b9569389995675c9abf26e0e0374851852c8 | a809b9569389995675c9abf26e0e0374851852c8 | exact |
| 2890c82d0dd4075b2160225591d6eb5309617795 | 7a7d209b3044e4f5b5ef9e3e288ef716dee7ac63 | 1944fe88fb84d35d2f83caa25549c7b075026f5a | 1944fe88fb84d35d2f83caa25549c7b075026f5a | exact |
| 28bff5f248f4a9f8617be72575f5fed13cbe8c39 | 93efbc748def106ce2c2ca15d56589142c46986b | b1c81e3ac9919740fa400f57e124a97bde7bf717 | b1c81e3ac9919740fa400f57e124a97bde7bf717 | exact |
| d12a53cc7a3bdc4f357663f1bb400e99b7aaec06 | 320050108dcf1454d76bb99b8e1b2301e1519aeb | 5fdd1e16095904efb6b20df3b7634d1f4b4b4481 | 5fdd1e16095904efb6b20df3b7634d1f4b4b4481 | exact |
| d219b545da71c849b69ae6d473aec9f21e8b4295 | 8c8cd4c5a8e8e4a7d6b61f4475a57ce53bdbfce4 | 4743169c74201e394e2a636b8414862116c3545e | 4743169c74201e394e2a636b8414862116c3545e | exact |
| f3e05f4719eda7ee061945afa2af0e95ede32cf8 | 7e84fbab4239c584034f945c30921ce554a66d10 | c5f00e03aaf1f04642427338b550f9a8e4d586d3 | c5f00e03aaf1f04642427338b550f9a8e4d586d3 | exact |
| 06f334ab7e1669bcdf2c36f9684f6dfed57dc8dd | 035eee07fa2732ffe7a0cb9c0f0dff9550b63331 | d22357e8477dc9c825d9911513a36c683e34d12a | d22357e8477dc9c825d9911513a36c683e34d12a | exact |
| 69509fae7651e397616114b9e2cfb2c0942eb0ba | c945442892aa2363ee36ba1809176de2c7b255cd | 98577e0cf597f114f50a525e33ea39d4a853ed01 | 98577e0cf597f114f50a525e33ea39d4a853ed01 | exact |
| ca1d96e6e81987f6262ae966b28f6ca5a546339b | 89bbb05617900d963a99feeabc5177ccefba56bc | 59ddd033bf668c150a494df5088cd5e5aa12467e | 59ddd033bf668c150a494df5088cd5e5aa12467e | exact |
| e06d7369db3f50b3e62d0e9739155fb490797377 | 81af4919b72be356c9cfab0c7c9b5a507cd23e4b | 1b77de087f12aadccf98d9afe230870557ecab26 | 1b77de087f12aadccf98d9afe230870557ecab26 | exact |
| 5d3da11dc599d8f35387d2ace79732aa4bf52f70 | f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 | 69b6f128a224301135e9517b999b8a80d313f872 | 69b6f128a224301135e9517b999b8a80d313f872 | exact |
| 0841539b099923d757b86428815f2f0316be6637 | fb6394f01b1d9cbc531c8cb2b3cc5cfd274ee8c9 | ae32be2bb66c88ec57e3557c323f62ca1c7dbc71 | ae32be2bb66c88ec57e3557c323f62ca1c7dbc71 | exact |
| 25d739331c05e12d42c60450bff13e12ead2a25e | d2017e09f169cd1cade2af99ace7b94917fff813 | 6035f22e708e0a09563e2c2b03ffb08c285a79e8 | 6035f22e708e0a09563e2c2b03ffb08c285a79e8 | exact |
| 1b0b2556d581006d3f51e5ae969c551e53579d81 | b2313035eb5a332f27da983f4bec4a48cd13c971 | c8e2ebd57aa4ad758fb9d622f48e6475fdb23a2f | ebbbb9c76e2865e23aef244232bd258987442d82 | allowlist union only; all non-allowlist patches exact |
| e08e4109c0c54353b3773b718bdb28848854e861 | a1a9699d029b5396c633615d7f17454915854ed9 | 1abd384c2c9644ea15f284bf6f6b74490364394b | 1abd384c2c9644ea15f284bf6f6b74490364394b | exact |
| 17afeeb751f01c4f50398abb07ff44d99849ce52 | 83c1b54502425b56ddd4e84cc540e7cc79887bfc | 626b1badf30ed94ccbd0f7e1afad95d24b854f06 | 626b1badf30ed94ccbd0f7e1afad95d24b854f06 | exact |
| 21dfde2193c8f66a0b1bc04ea94533aa9add9f4f | cef4afac97ff1b2657b1f2ffcc91ddd0c0665eb8 | 02dacb70b13c3e421d9abce02b4d82249043d27a | 02dacb70b13c3e421d9abce02b4d82249043d27a | exact |
| 337c1a19630e6c582c2f2d594f7f1f17caccc800 | ba2b3d61339802dda026b91f675c31c762936c72 | 1b3bcb760f61217c960a6a1a7e804bf776f1eb19 | ee139f91bddbfda0620f8b3bd2c1df2a31cca784 | allowlist union only; all non-allowlist patches exact |
| df59545ae5095137404800d0453dcf962e06770a | 8a0af946cb48f46ce355f9e67faf9d6c9eba8b1d | b3369fb20a49d65ab9e8592150973163050244c5 | ac05a064586e995523f13c092ab3c01bb12ec82c | allowlist union only; all non-allowlist patches exact |
| a96abe60e48e50fb6d31af7c105131b58a0fd73a | e659bbb262867c03b1dc29be54786e0b5d36a23f | 062e9d273426013b1b371526950b79d58d4d19bb | 1b9dfd0936feef3c1cfc93516920ff334d415dcb | allowlist union only; all non-allowlist patches exact |
| 64315a6fbdff0a5f335fe8e5c9e9a277ddc785c6 | 230caf8ddceda4b703a9eb2864c7e7198b817a4b | 473ea9706531de64640c0ec06e03baf28ddfe088 | 473ea9706531de64640c0ec06e03baf28ddfe088 | exact |
| c1d5442264934e4d06402bc4861e51c868660432 | 4d69059679a98e514e892e2bf3fddc1a358f52e8 | 038dc823873bc9d6134852cc5edf563b399390e5 | 038dc823873bc9d6134852cc5edf563b399390e5 | exact |
| 0205d19770a8730c407c94c823617249244569a5 | fec8633bc128856938a9c1cac770fc6265792cb4 | fa972f533c044bedc0682cc54e5162f9ac4f05a2 | fa972f533c044bedc0682cc54e5162f9ac4f05a2 | exact |
| 3737148ee1e43e3695174b7a8e9befeb6d69fb41 | local inventory reconciliation | 4b29747abd4a45f1eca26155bb4e8dee397c64c5 | 4b29747abd4a45f1eca26155bb4e8dee397c64c5 | permitted stale-row deletion and real printed row |

25 picks have exact whole-commit stable patch IDs. The four conflict-resolved picks differ only in allowlist bytes; non-allowlist source and destination patch IDs are identical for every one of the 29 picks. No report-only .antiphon source commits were in the candidate lists. Deliverable docs/superpowers and investigation files were retained.

R100 proof for every moved file

| Original path | Final path | Identical Git blob | Similarity |
|---|---|---|---:|
| server/Application/Services/AgentTaskLandingState.cs | server/Application/Core/AgentTaskLandingState.cs | 58d929c5de84088e38c66cbe3e379b460c6dd2c9 | 100% |
| server/Application/Services/AntiphonDataPaths.cs | server/Application/Core/AntiphonDataPaths.cs | 82fd49aced057dfb196b626c448974ae351b7076 | 100% |
| server/Application/Services/CardFilePreCommitSweep.cs | server/Application/Core/CardFilePreCommitSweep.cs | 2bbf2b732f9e1a3452e61ab8821d6c9e80d99897 | 100% |
| server/Application/Services/CardTaskFileRenderer.cs | server/Application/Core/CardTaskFileRenderer.cs | 831c566de70761759a4d25d4b8815f9e518b608a | 100% |
| server/Application/Services/ChannelReplyPreparation.cs | server/Application/Core/ChannelReplyPreparation.cs | 98532855b30f0758168372d0946234e3343f6678 | 100% |
| server/Application/Services/ClaudeLaunchArgs.cs | server/Application/Core/ClaudeLaunchArgs.cs | cb1e65cfd86dbd0ac6d77c8ce47280f9a58d7d9b | 100% |
| server/Application/Services/CodexCliObservation.cs | server/Application/Core/CodexCliObservation.cs | 50c4fec140dab959a13ca928d064b8f7de744c27 | 100% |
| server/Application/Services/DelegationReportFormatter.cs | server/Application/Core/DelegationReportFormatter.cs | fba9b3e87c47ab5c40efbc1f3a6211ed14ad4536 | 100% |
| server/Application/Services/DispatchConcurrencyPolicy.cs | server/Application/Core/DispatchConcurrencyPolicy.cs | 022b3a167c0ea5f2ed32aac4fd68eb1bb670c9e0 | 100% |
| server/Application/Services/GrokRulesLaunchValidation.cs | server/Application/Core/GrokRulesLaunchValidation.cs | a7c8d57e45f31a1f2c451cdbabc77bc464b5eea4 | 100% |
| server/Application/Services/LandVerificationWaits.cs | server/Application/Core/LandVerificationWaits.cs | efdb4cb44379aa8af23a7f7c2d1bb9ae09807747 | 100% |
| server/Application/Services/ModelAlias.cs | server/Application/Core/ModelAlias.cs | a72c5f4e69f918ae2e68296c4a78d83979a97cb2 | 100% |
| server/Application/Services/OrchestratorInstructionsSignal.cs | server/Application/Core/OrchestratorInstructionsSignal.cs | 9249f896635020a5f08e746d31719529d2ac0062 | 100% |
| server/Application/Services/PipelineDefinitions.cs | server/Application/Core/PipelineDefinitions.cs | b63e5f60ddc84b0bb3beda52d873571ad2fed57c | 100% |
| server/Application/Services/PipelineStagesJson.cs | server/Application/Core/PipelineStagesJson.cs | 5db57915203add63defde42cae654e7330e912ef | 100% |
| server/Application/Services/ProviderContractCatalog.cs | server/Application/Core/ProviderContractCatalog.cs | 532a98c17c862e61f9d70216d94be4b46f9dcac9 | 100% |
| server/Application/Services/RepositoryLeaseWaiters.cs | server/Application/Core/RepositoryLeaseWaiters.cs | 50a87d04f19be449f874a50c8f1d3a6ff9daef2b | 100% |
| server/Application/Services/RunnerAbsenceEvidenceValidator.cs | server/Application/Core/RunnerAbsenceEvidenceValidator.cs | 4d56d29ad6f173009879f3c6a65475fbcb1178ff | 100% |
| server/Application/Services/RunnerPlatformProblems.cs | server/Application/Core/RunnerPlatformProblems.cs | 5a86bb4f314062a786128560e50c1cffaf0b3ccf | 100% |
| server/Application/Services/RunnerState.cs | server/Application/Core/RunnerState.cs | a4a99f5bb455670c7999776017d700020db48f2f | 100% |
| server/Application/Services/SessionStateSnapshot.cs | server/Application/Core/SessionStateSnapshot.cs | 87ae04c52ea0f5af3d2757fa32047395886d9303 | 100% |
| server/Application/Services/SubscriptionQuotaPolicy.cs | server/Application/Core/SubscriptionQuotaPolicy.cs | 11c8fda83a168b1636c1d3444fffcd2b4f89fd0e | 100% |
| server/Application/Services/VerificationReceiptPolicy.cs | server/Application/Core/VerificationReceiptPolicy.cs | 58e98a5a7fa329d4b30437333bcd3c9aef5e197a | 100% |
| server/Application/Services/WorkspaceReservationLiveness.cs | server/Application/Core/WorkspaceReservationLiveness.cs | 06499c70ca31a0de7087c132d07b6e7435cc1e1b | 100% |
| server/Application/Services/WorktreeCleanupPresentation.cs | server/Application/Core/WorktreeCleanupPresentation.cs | 1eec6713a8f26c19f8213b0ef58db8e38ab1610a | 100% |
| server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs | server/Application/Services/AgentTuiMetrics.cs | 156e561672b24d7fc4a0fe50ddc328fe6699b0ca | 100% |
| server/Infrastructure/Data/CardDoneArtifactRoots.cs | server/Application/Services/CardDoneArtifactRoots.cs | b2b5e7c2b0291678856fa9ac2a9e9cbd7634cdfa | 100% |
| server/Infrastructure/GitHub/GitHubMonitorService.cs | server/Application/Services/GitHubMonitorService.cs | 484c28518b856ceaf5bf44bf031998ac89b69988 | 100% |
| server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs | server/Application/Services/HostStatsPollService.cs | 26834fbd611098a6cd12f5228287fbbdefc88666 | 100% |
| server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs | server/Application/Services/PhoneHomeRecoveryPump.cs | 626d54b65b1503bf630077cb24090d3b634eefc1 | 100% |
| server/Infrastructure/Resilience/ResilienceReadClients.cs | server/Application/Services/ResilienceReadClients.cs | 79ebf4ca098804306f1b5c7c69e53f0d4a91a065 | 100% |
| server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs | server/Application/Services/RunnerAlarmState.cs | a770fb79318c62cfed8fb0e44951be65dce415e6 | 100% |
| server/Infrastructure/Agents/SessionRunner/RunnerRetireJob.cs | server/Application/Services/RunnerRetireJob.cs | d2d669af843632e4985ac419f13e78772606f2c4 | 100% |
| server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs | server/Application/Services/RunnerSlotReconcileJob.cs | 048937152a986222e106366e2df2837f97a55a95 | 100% |
| server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs | server/Application/Services/SeatOccupancyState.cs | 2507e6de6982e5d61d8ce1447a65800d19b6a004 | 100% |
| server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs | server/Application/Services/SessionRunnerEventPump.cs | 48f48e6b4e0522861be75f46ef47fb849fb5fc6c | 100% |
| server/Infrastructure/Agents/SessionRunner/SessionRuntimeActionHostedService.cs | server/Application/Services/SessionRuntimeActionHostedService.cs | a9a22a1dec5bd03cb23138429ca4172ce2706355 | 100% |
| server/Infrastructure/Data/WorktreeRemovalEvidence.cs | server/Application/Services/WorktreeRemovalEvidence.cs | 8f49cf8aed07d8567dd31b6811d735fcd88ecdd3 | 100% |
| server/Infrastructure/Agents/CodexSubmitConfirmation.cs | server/Infrastructure/Agents/SessionRunner/CodexSubmitConfirmation.cs | 8b452a6050c0122275bca0a1861e3ebe69d9b276 | 100% |
| server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs | server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs | 658c8b4d6fe19003ea6c09463dde23623d52c6b6 | 100% |

All 40 blob pairs are identical to master at the base; move content additions/deletions are zero. The only non-rename production entries are Core/RunnerRequestIntent.cs and Services/DefaultRunnerRoutingPolicy.cs. The intent and enum body is a verbatim substring of the old file, and deleting it plus its separating blank line gives the final routing-policy bytes exactly.

Conflict union changes

Four conflicts were confined to tests/Antiphon.Tests/Layering/allowlist.txt. For each below, all ours rows were retained and new independent theirs rows were unioned once by exact row identity, under the existing owning header. This intentionally kept stale debt until one final reconciliation. No other textual conflict occurred. Each added line and its reason follows.

Source b2313035eb5a332f27da983f4bec4a48cd13c971:

- Added `server/Application/Core/ChannelReplyPreparation.cs -> ChannelContracts @ server/Application/Services/ChannelContracts.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> ChannelOutboundService @ server/Application/Services/ChannelOutboundService.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> ChannelPromptCorrelation @ server/Application/Services/ChannelPromptCorrelation.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> ChannelReplyDispatcher @ server/Application/Services/ChannelReplyDispatcher.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> ChatChannelService @ server/Application/Services/ChatChannelService.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> DeliverableBundleService @ server/Application/Services/DeliverableBundleService.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> SourceManifest @ server/Application/Services/DeliverableBundleService.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/DispatchConcurrencyPolicy.cs -> DelegationOpenGate @ server/Application/Services/DelegationOpenGate.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/PipelineStagesJson.cs -> PipelineHandoff @ server/Application/Services/PipelineHandoff.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/SubscriptionQuotaPolicy.cs -> SubscriptionUsageSnapshot @ server/Application/Services/SubscriptionUsageReader.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/ChannelReplyPreparation.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/VerificationReceiptPolicy.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentPinnedInstructionService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentRegistry.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentSupervisorService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTaskDispatcher.CeilingHold.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTaskReplyService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTuiLaunchResolver.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/BootReplyWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CapacityRecoveryService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardRevisionLog.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Generation @ server/Infrastructure/Git/LandVerificationArtifacts.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardWorktreeMirrorCleanupService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ChannelBridgeService.WithheldIncident.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ChannelBridgeService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ChannelOutboundFailureRecorder.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ChannelOutboundService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CheckCompactionContinuationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/DiagnoseService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/DispatchBaseNotificationPayload.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ExpectationWatchdogPolicy.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ExpectationWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/GrokRulesRefreshService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/HerdrSupervisionStateService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/HostBudgetService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/HostMaintenanceService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/LandNotificationPayload.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/LegacyCheckNotePublicationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ModelAvailability.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/OrchestratorInstructionsService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/OrchestratorWorkspaceWarningService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/OutboundConversionTaskRunner.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PipelineHandoffActionService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PlanCatalogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PoolDelegateRelease.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ProviderSignInIncident.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ProviderWaitWarningWriter.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/QueuedInputWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/RemoteControlRecoveryService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/RunnerRetireService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/RunnerSlotService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/ScheduleService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/SessionReconciliationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/SpecialistRequestService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/SpecialistTaskRunner.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/StandingBootWarningWriter.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/StandingContinuityState.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/StandingSpecialistHealthService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/StandingSpecialistSeatService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/TaskCompletionNotification.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/TranscriptUiProjection.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Generation @ server/Infrastructure/Git/LandVerificationArtifacts.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Files/ChannelOutboundFileStore.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Core/WorktreeCleanupPresentation.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTaskService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/CardWorktreeMirrorCleanupService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/LandOperationFactory.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/TaskWorktreeRetirementService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/LandingGit.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/WindowsWorktreeLockDiagnostics.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: union of the independently changed allowlist; final inventory decides retention.

Source ba2b3d61339802dda026b91f675c31c762936c72:

- Added `server/Application/Services/HostStatsPollService.cs -> PhoneHomeRunnerDirectory @ server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> PhoneHomeRunnerClient @ server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> PhoneHomeRunnerDirectory @ server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/RunnerSlotReconcileJob.cs -> PhoneHomeRunnerDirectory @ server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/LandingVerifier.cs -> LandVerificationWaits @ server/Application/Services/LandVerificationWaits.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/AgentTuiMetrics.cs -> AgentTuiKeyProtectionReadiness @ server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> PhoneHomeEventScheduler @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventScheduler.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> PhoneHomeLiveConnection @ server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> PhoneHomeQueuedEvent @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/PhoneHomeRecoveryPump.cs -> TranscriptIncidentReplayGuard @ server/Infrastructure/Agents/SessionRunner/TranscriptIncidentReplayGuard.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/RunnerAlarmState.cs -> JournalRecordState @ server/Infrastructure/Git/RepositoryChildJournalInspector.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `# slice P0-A2`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Exceptions/ConcurrencyLimitException.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Services/GitWorkspaceService.cs -> CardFilePreCommitSweep @ server/Application/Services/CardFilePreCommitSweep.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Application/Settings/DelegationSettings.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerAbsenceAuthentication @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerAbsenceEvidenceValidator @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> CodexCliObservation @ server/Application/Services/CodexCliObservation.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> OrchestratorInstructionsSignal @ server/Application/Services/OrchestratorInstructionsSignal.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs -> GrokRulesLaunchValidation @ server/Application/Services/GrokRulesLaunchValidation.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> GrokRulesLaunchValidation @ server/Application/Services/GrokRulesLaunchValidation.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> ProviderContractCatalog @ server/Application/Services/ProviderContractCatalog.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceAuthentication @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceEvidenceValidator @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceExpectation @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/CardFileRepository.cs -> CardTaskFileRenderer @ server/Application/Services/CardTaskFileRenderer.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/GitService.cs -> CardFilePreCommitSweep @ server/Application/Services/CardFilePreCommitSweep.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/RepositoryMutationLease.cs -> RepositoryLeaseWaiters @ server/Application/Services/RepositoryLeaseWaiters.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Git/RepositoryVerificationWindows.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: union of the independently changed allowlist; final inventory decides retention.

Source 8a0af946cb48f46ce355f9e67faf9d6c9eba8b1d:

- Added `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: union of the independently changed allowlist; final inventory decides retention.

Source e659bbb262867c03b1dc29be54786e0b5d36a23f:

- Added `server/Application/Services/CardDoneArtifactRoots.cs -> WorktreeSetAside @ server/Infrastructure/Git/WorktreeSetAside.cs`: union of the independently changed allowlist; final inventory decides retention.
- Added `server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs -> AgentTuiKeyProtectionReadiness @ server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs`: union of the independently changed allowlist; final inventory decides retention.

Final reconciliation: every changed allowlist row

166 stale rows were deleted and one real printed row was added. Each exact line and reason follows; the map, engine, strict flag and unchanged rows were retained.

- Deleted `server/Application/Dtos/LandingEvidenceDto.cs -> AgentTaskLandingState @ server/Application/Services/AgentTaskLandingState.cs`: target moved to server/Application/Core/AgentTaskLandingState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Exceptions/ConcurrencyLimitException.cs -> DispatchConcurrencyPolicy @ server/Application/Services/DispatchConcurrencyPolicy.cs`: target moved to server/Application/Core/DispatchConcurrencyPolicy.cs; no violation remains for the remapped row.
- Deleted `server/Application/Exceptions/ConcurrencyLimitException.cs -> DispatchConstraint @ server/Application/Services/DispatchConcurrencyPolicy.cs`: target moved to server/Application/Core/DispatchConcurrencyPolicy.cs; no violation remains for the remapped row.
- Deleted `server/Application/Exceptions/ModelDisabledException.cs -> ModelAlias @ server/Application/Services/ModelAlias.cs`: target moved to server/Application/Core/ModelAlias.cs; no violation remains for the remapped row.
- Deleted `server/Application/Exceptions/SubscriptionQuotaLowException.cs -> SubscriptionQuotaPolicy @ server/Application/Services/SubscriptionQuotaPolicy.cs`: target moved to server/Application/Core/SubscriptionQuotaPolicy.cs; no violation remains for the remapped row.
- Deleted `server/Application/Exceptions/SubscriptionQuotaLowException.cs -> SubscriptionQuotaVerdict @ server/Application/Services/SubscriptionQuotaPolicy.cs`: target moved to server/Application/Core/SubscriptionQuotaPolicy.cs; no violation remains for the remapped row.
- Deleted `server/Application/Interfaces/IChannelOutboundFileStore.cs -> ChannelReplyBundleSnapshot @ server/Application/Services/ChannelReplyPreparation.cs`: target moved to server/Application/Core/ChannelReplyPreparation.cs; no violation remains for the remapped row.
- Deleted `server/Application/Interfaces/IChannelOutboundFileStore.cs -> ChannelReplyPrepared @ server/Application/Services/ChannelReplyPreparation.cs`: target moved to server/Application/Core/ChannelReplyPreparation.cs; no violation remains for the remapped row.
- Deleted `server/Application/Interfaces/ISessionRunnerDirectory.cs -> RunnerState @ server/Application/Services/RunnerState.cs`: target moved to server/Application/Core/RunnerState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Interfaces/ISessionStateLoader.cs -> SessionStateSnapshot @ server/Application/Services/SessionStateSnapshot.cs`: target moved to server/Application/Core/SessionStateSnapshot.cs; no violation remains for the remapped row.
- Deleted `server/Application/Settings/AgentTuiSettings.cs -> AntiphonDataPaths @ server/Application/Services/AntiphonDataPaths.cs`: target moved to server/Application/Core/AntiphonDataPaths.cs; no violation remains for the remapped row.
- Deleted `server/Application/Settings/DelegationSettings.cs -> ClaudeLaunchArgs @ server/Application/Services/ClaudeLaunchArgs.cs`: target moved to server/Application/Core/ClaudeLaunchArgs.cs; no violation remains for the remapped row.
- Deleted `server/Application/Core/ChannelReplyPreparation.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Core/VerificationReceiptPolicy.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentPinnedInstructionService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentRegistry.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentSupervisorService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTaskDispatcher.CeilingHold.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTaskReplyService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTuiLaunchResolver.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/BootReplyWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CapacityRecoveryService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardRevisionLog.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Generation @ server/Infrastructure/Git/LandVerificationArtifacts.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardWorktreeMirrorCleanupService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ChannelBridgeService.WithheldIncident.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ChannelBridgeService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ChannelOutboundFailureRecorder.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ChannelOutboundService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CheckCompactionContinuationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/DiagnoseService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/DispatchBaseNotificationPayload.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ExpectationWatchdogPolicy.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ExpectationWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/GrokRulesRefreshService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/HerdrSupervisionStateService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/HostBudgetService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/HostMaintenanceService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/LandNotificationPayload.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/LegacyCheckNotePublicationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ModelAvailability.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/OrchestratorInstructionsService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/OrchestratorWorkspaceWarningService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/OutboundConversionTaskRunner.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/PipelineHandoffActionService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/PlanCatalogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/PoolDelegateRelease.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ProviderSignInIncident.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ProviderWaitWarningWriter.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/QueuedInputWatchdogService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/RemoteControlRecoveryService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/RunnerRetireService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/RunnerSlotService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/ScheduleService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/SessionReconciliationService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/SpecialistRequestService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/SpecialistTaskRunner.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/StandingBootWarningWriter.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/StandingContinuityState.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/StandingSpecialistHealthService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/StandingSpecialistSeatService.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/TaskCompletionNotification.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/TranscriptUiProjection.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Generation @ server/Infrastructure/Git/LandVerificationArtifacts.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Files/ChannelOutboundFileStore.cs -> Kind @ server/Application/Services/GitIndexLock.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Git/LandingVerifier.cs -> LandVerificationWaits @ server/Application/Services/LandVerificationWaits.cs`: target moved to server/Application/Core/LandVerificationWaits.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> HostStatsCache @ server/Application/Services/HostStatsCache.cs`: source moved to server/Application/Services/HostStatsPollService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> HostStatsFailureKind @ server/Application/Services/HostStatsCache.cs`: source moved to server/Application/Services/HostStatsPollService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> IHostStatsAntiphonCounters @ server/Application/Services/HostStatsAntiphonCounters.cs`: source moved to server/Application/Services/HostStatsPollService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> SessionRunnerCatalogue @ server/Application/Services/SessionRunnerCatalogue.cs`: source moved to server/Application/Services/HostStatsPollService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs -> AgentSessionRuntime @ server/Application/Services/AgentSessionRuntime.cs`: source moved to server/Application/Services/PhoneHomeRecoveryPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs -> TranscriptBindingIncidentService @ server/Application/Services/TranscriptBindingIncidentService.cs`: source moved to server/Application/Services/PhoneHomeRecoveryPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs -> JournalRecordState @ server/Infrastructure/Git/RepositoryChildJournalInspector.cs`: source moved to server/Application/Services/RunnerAlarmState.cs; current-path row already retained.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerRetireJob.cs -> AppDbContext @ server/Infrastructure/Data/AppDbContext.cs`: source moved to server/Application/Services/RunnerRetireJob.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerRetireJob.cs -> RunnerRetireService @ server/Application/Services/RunnerRetireService.cs`: source moved to server/Application/Services/RunnerRetireJob.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs -> AppDbContext @ server/Infrastructure/Data/AppDbContext.cs`: source moved to server/Application/Services/RunnerSlotReconcileJob.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs -> RunnerSlotService @ server/Application/Services/RunnerSlotService.cs`: source moved to server/Application/Services/RunnerSlotReconcileJob.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs -> TerminalRunnerSeatReleaseService @ server/Application/Services/TerminalRunnerSeatReleaseService.cs`: source moved to server/Application/Services/RunnerSlotReconcileJob.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs -> SeatOccupancySnapshot @ server/Application/Services/SeatOccupancySnapshot.cs`: source moved to server/Application/Services/SeatOccupancyState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs -> AgentSessionGroups @ server/Application/Services/AgentSessionRuntime.cs`: source moved to server/Application/Services/SessionRunnerEventPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs -> AgentSessionRuntime @ server/Application/Services/AgentSessionRuntime.cs`: source moved to server/Application/Services/SessionRunnerEventPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs -> RuntimePhase @ server/Application/Services/RuntimePhase.cs`: source moved to server/Application/Services/SessionRunnerEventPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs -> TranscriptBindingIncidentService @ server/Application/Services/TranscriptBindingIncidentService.cs`: source moved to server/Application/Services/SessionRunnerEventPump.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRuntimeActionHostedService.cs -> SessionRuntimeActionCoordinator @ server/Application/Services/SessionRuntimeActionCoordinator.cs`: source moved to server/Application/Services/SessionRuntimeActionHostedService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs -> AppDbContext @ server/Infrastructure/Data/AppDbContext.cs`: source moved to server/Application/Services/AgentTuiMetrics.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: source moved to server/Application/Services/HostStatsPollService.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs -> DataProtectionApiKeyProtector @ server/Infrastructure/Security/DataProtectionApiKeyProtector.cs`: source moved to server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs -> DataProtectionPayload @ server/Infrastructure/Security/DataProtectionPayload.cs`: source moved to server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/CardDoneArtifactRoots.cs -> WorktreeSetAside @ server/Infrastructure/Git/WorktreeSetAside.cs`: source moved to server/Application/Services/CardDoneArtifactRoots.cs; current-path row already retained.
- Deleted `server/Infrastructure/GitHub/GitHubMonitorService.cs -> AppDbContext @ server/Infrastructure/Data/AppDbContext.cs`: source moved to server/Application/Services/GitHubMonitorService.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AgentTaskService.cs -> SeatOccupancyState @ server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs`: target moved to server/Application/Services/SeatOccupancyState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AgentTuiLaunchResolver.cs -> AgentTuiLaunchActivityMode @ server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`: target moved to server/Application/Services/AgentTuiMetrics.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AgentTuiLaunchResolver.cs -> AgentTuiLaunchModelMode @ server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`: target moved to server/Application/Services/AgentTuiMetrics.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AgentTuiLaunchResolver.cs -> AgentTuiMetricOutcome @ server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`: target moved to server/Application/Services/AgentTuiMetrics.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AgentTuiLaunchResolver.cs -> AgentTuiMetrics @ server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`: target moved to server/Application/Services/AgentTuiMetrics.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AttentionService.Alarms.cs -> RunnerAlarmSnapshot @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AttentionService.cs -> RunnerAlarmState @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/AttentionService.cs -> SeatOccupancyState @ server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs`: target moved to server/Application/Services/SeatOccupancyState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> JournalAlarmRecord @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> JournalFinding @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> NotedAlarmRow @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> PendingRecoveryNote @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> RunnerAlarmSnapshot @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> RunnerAlarmState @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/RunnerAlarmCoordinator.cs -> RunnerOutageEpisode @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/SeatOccupancySampler.cs -> SeatOccupancyState @ server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs`: target moved to server/Application/Services/SeatOccupancyState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> RunnerState @ server/Application/Services/RunnerState.cs`: target moved to server/Application/Core/RunnerState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/Seeding/DatabaseSeeder.cs -> PipelineDefinitions @ server/Application/Services/PipelineDefinitions.cs`: target moved to server/Application/Core/PipelineDefinitions.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/Seeding/DatabaseSeeder.cs -> PipelineStagesJson @ server/Application/Services/PipelineStagesJson.cs`: target moved to server/Application/Core/PipelineStagesJson.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/SessionStateLoader.cs -> SessionStateReadiness @ server/Application/Services/SessionStateSnapshot.cs`: target moved to server/Application/Core/SessionStateSnapshot.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/SessionStateLoader.cs -> SessionStateSnapshot @ server/Application/Services/SessionStateSnapshot.cs`: target moved to server/Application/Core/SessionStateSnapshot.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> OwnerFacts @ server/Application/Services/WorkspaceReservationLiveness.cs`: target moved to server/Application/Core/WorkspaceReservationLiveness.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> WorkspaceReservationLiveness @ server/Application/Services/WorkspaceReservationLiveness.cs`: target moved to server/Application/Core/WorkspaceReservationLiveness.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeCleanupJournal.cs -> AgentTaskLandingState @ server/Application/Services/AgentTaskLandingState.cs`: target moved to server/Application/Core/AgentTaskLandingState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeCleanupJournal.cs -> WorktreeCleanupPresentation @ server/Application/Services/WorktreeCleanupPresentation.cs`: target moved to server/Application/Core/WorktreeCleanupPresentation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> AgentTaskLandingState @ server/Application/Services/AgentTaskLandingState.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; target moved to server/Application/Core/AgentTaskLandingState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> CardDoneArtifactPreservation @ server/Infrastructure/Files/CardDoneArtifactPreservation.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> CardWorktreeCleanupService @ server/Application/Services/CardWorktreeCleanupService.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> DelegationWorkspaceResolver @ server/Application/Services/DelegationWorkspaceResolver.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> LandApproval @ server/Application/Services/LandApproval.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> VerificationReceiptPolicy @ server/Application/Services/VerificationReceiptPolicy.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; target moved to server/Application/Core/VerificationReceiptPolicy.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/GuardedWorktreeRemoval.cs -> AgentTaskLandingState @ server/Application/Services/AgentTaskLandingState.cs`: target moved to server/Application/Core/AgentTaskLandingState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/GuardedWorktreeRemoval.cs -> WorktreeCleanupPresentation @ server/Application/Services/WorktreeCleanupPresentation.cs`: target moved to server/Application/Core/WorktreeCleanupPresentation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/WorktreeGuardedCleanup.cs -> WorktreeCleanupPresentation @ server/Application/Services/WorktreeCleanupPresentation.cs`: target moved to server/Application/Core/WorktreeCleanupPresentation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/WorktreeIgnoredContentGate.cs -> WorktreeCleanupPresentation @ server/Application/Services/WorktreeCleanupPresentation.cs`: target moved to server/Application/Core/WorktreeCleanupPresentation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs -> RunnerAlarmSnapshot @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs -> RunnerAlarmState @ server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`: target moved to server/Application/Services/RunnerAlarmState.cs; no violation remains for the remapped row.
- Deleted `server/Application/Core/WorktreeCleanupPresentation.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTaskService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardWorktreeCleanupExecutor.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/CardWorktreeMirrorCleanupService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/LandOperationFactory.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/TaskWorktreeRetirementService.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Data/WorkspaceReservationJournal.cs -> Active @ server/Infrastructure/Agents/SessionRunner/PhoneHomeEventDiagnostics.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Git/LandingGit.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Git/WindowsWorktreeLockDiagnostics.cs -> Status @ server/Application/Services/BootReplyWatch.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Application/Services/AgentTuiMetrics.cs -> AgentTuiKeyProtectionReadiness @ server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs`: target moved to server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs; current-path row already retained.
- Deleted `server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs -> AgentTuiKeyProtectionReadiness @ server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs`: source moved to server/Application/Services/AgentTuiMetrics.cs; current-path row already retained.
- Deleted `server/Application/Exceptions/ConcurrencyLimitException.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: target moved to server/Application/Core/DelegationReportFormatter.cs; no violation remains for the remapped row.
- Deleted `server/Application/Services/GitWorkspaceService.cs -> CardFilePreCommitSweep @ server/Application/Services/CardFilePreCommitSweep.cs`: target moved to server/Application/Core/CardFilePreCommitSweep.cs; no violation remains for the remapped row.
- Deleted `server/Application/Settings/DelegationSettings.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerAbsenceAuthentication @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: target moved to server/Application/Core/RunnerAbsenceEvidenceValidator.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerAbsenceEvidenceValidator @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: target moved to server/Application/Core/RunnerAbsenceEvidenceValidator.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: target moved to server/Application/Core/RunnerPlatformProblems.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> CodexCliObservation @ server/Application/Services/CodexCliObservation.cs`: target moved to server/Application/Core/CodexCliObservation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> OrchestratorInstructionsSignal @ server/Application/Services/OrchestratorInstructionsSignal.cs`: target moved to server/Application/Core/OrchestratorInstructionsSignal.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: target moved to server/Application/Core/RunnerPlatformProblems.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs -> RunnerRequestIntent @ server/Application/Services/DefaultRunnerRoutingPolicy.cs`: repaired guard reports no violation for this exact triple.
- Deleted `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs -> GrokRulesLaunchValidation @ server/Application/Services/GrokRulesLaunchValidation.cs`: target moved to server/Application/Core/GrokRulesLaunchValidation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> GrokRulesLaunchValidation @ server/Application/Services/GrokRulesLaunchValidation.cs`: target moved to server/Application/Core/GrokRulesLaunchValidation.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> ProviderContractCatalog @ server/Application/Services/ProviderContractCatalog.cs`: target moved to server/Application/Core/ProviderContractCatalog.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceAuthentication @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: target moved to server/Application/Core/RunnerAbsenceEvidenceValidator.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceEvidenceValidator @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: target moved to server/Application/Core/RunnerAbsenceEvidenceValidator.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerAbsenceExpectation @ server/Application/Services/RunnerAbsenceEvidenceValidator.cs`: target moved to server/Application/Core/RunnerAbsenceEvidenceValidator.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs -> RunnerPlatformProblems @ server/Application/Services/RunnerPlatformProblems.cs`: target moved to server/Application/Core/RunnerPlatformProblems.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Data/WorktreeRemovalEvidence.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: source moved to server/Application/Services/WorktreeRemovalEvidence.cs; target moved to server/Application/Core/DelegationReportFormatter.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/CardFileRepository.cs -> CardTaskFileRenderer @ server/Application/Services/CardTaskFileRenderer.cs`: target moved to server/Application/Core/CardTaskFileRenderer.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/GitService.cs -> CardFilePreCommitSweep @ server/Application/Services/CardFilePreCommitSweep.cs`: target moved to server/Application/Core/CardFilePreCommitSweep.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/RepositoryMutationLease.cs -> RepositoryLeaseWaiters @ server/Application/Services/RepositoryLeaseWaiters.cs`: target moved to server/Application/Core/RepositoryLeaseWaiters.cs; no violation remains for the remapped row.
- Deleted `server/Infrastructure/Git/RepositoryVerificationWindows.cs -> DelegationReportFormatter @ server/Application/Services/DelegationReportFormatter.cs`: target moved to server/Application/Core/DelegationReportFormatter.cs; no violation remains for the remapped row.
- Added `server/Application/Services/AgentTuiMetrics.cs -> AgentTuiKeyProtectionReadiness @ server/Infrastructure/Security/DataProtectionAgentTuiSecretProtector.cs`: real printed violation: combine P0-E source move of AgentTuiMetrics with P0-F1 target move of AgentTuiKeyProtectionReadiness.

Evidence and cleanup

Full evidence: /work/worktrees/task-03477baf/.antiphon/c1729-merge/. Exact raw execution commands are in each <group>-1/manifest.json; fresh TRX/summary.json and timing/ are retained beside them. Per-commit source/land patch IDs, byte-identity move proofs, all conflict preimages/unions, and per-line allowlist reasons are in the named JSON/text files in that directory. Generated payloads remain ignored.

All owned native bin/obj directories were removed with exact literal absolute paths after every child completed. A final census found no bin, obj, TestResults or TestOutput directory. No scratch worktree was created, no foreign process was killed and no shared service or volume was touched. Required ignored parity and verification evidence is retained for the caller.

Rerun after rebuilding native outputs:

```sh
env TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1729-rebuild -- dotnet build Antiphon.sln -p:UseAppHost=false -maxcpucount:1
node .antiphon/c1729-merge/run-group.cjs guard-policy-contract 2
node .antiphon/c1729-merge/run-group.cjs core-guidance 2
node .antiphon/c1729-merge/run-group.cjs drivers-resilience 2
```

These are direct gated runs, not fabricated checkpoint-tool receipts. Repeated guard CPs share the single passing final guard execution; no required checkpoint was omitted.

--- next stage ---
next: done
handoff: Wave 0a is published on master. Later Phase 0 work and post-land Mutation remain with their existing owners.
artifact: /work/worktrees/task-03477baf/.antiphon/task-03477baf.md
