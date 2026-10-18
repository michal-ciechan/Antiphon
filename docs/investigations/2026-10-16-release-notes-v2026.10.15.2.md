# v2026.10.15.2

Operator-approved manual release at rc/2026-10-15, commit
6ca859d006b0a3cac9a88dc6bd551c37e98b2217.

Previous release: [v2026.10.15.1](https://github.com/michal-ciechan/Antiphon/releases/tag/v2026.10.15.1)
at 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
The 72 commits below are the complete range
v2026.10.15.1..6ca859d006b0a3cac9a88dc6bd551c37e98b2217,
grouped by card where attribution is established.

## Verification

- Linux gate roster: 373 classes, 4,527 cases, 0 failed, 0 skipped, 36m21s on the pinned candidate b7265a05aa8773f08dbf457b93c7561e7874822d.
- Windows roster: 361 classes, 4,537 cases, 0 failed, 0 skipped; passed twice on the same SHA, b7265a05aa8773f08dbf457b93c7561e7874822d.
- The only change after those runs is the server2 rollout cache Fixture npm-ownership fix: rollout tooling, 7 narrow cases passed; the 3 native ownership cases were red before the fix and 3/3 green after; careful diff review was clean.
- The release candidate is running on the AppHost.
- The server2 runner rollout onto 6ca859d006b0a3cac9a88dc6bd551c37e98b2217 is pending behind a rollout-tooling Fixture gate that is being repaired.
- Aggregate evidence in docs/investigations: [2026-10-16-rc2-final-linux-roster.md](https://github.com/michal-ciechan/Antiphon/blob/fedcce723e78262f2dbfbff86fed01848e90943d/docs/investigations/2026-10-16-rc2-final-linux-roster.md) and [2026-10-16-rc2-final-windows-roster.md](https://github.com/michal-ciechan/Antiphon/blob/23d9cfccdd7fa9900b11222c633858e5f3f7cbb2/docs/investigations/2026-10-16-rc2-final-windows-roster.md). Earlier-candidate evidence in docs/investigations: [2026-10-16-rc2-linux-roster.md](https://github.com/michal-ciechan/Antiphon/blob/3304f2de425dab4589a56214b758c0dfee3cc36b/docs/investigations/2026-10-16-rc2-linux-roster.md), [2026-10-16-rc2-windows-roster.md](https://github.com/michal-ciechan/Antiphon/blob/d1551e80ca8781c0e4c108c85902c5021f69f7c9/docs/investigations/2026-10-16-rc2-windows-roster.md), [2026-10-15-release-candidate.md](https://github.com/michal-ciechan/Antiphon/blob/6e23e0b4c8d6b2ae12218c2c0058593dbf773530/docs/investigations/2026-10-15-release-candidate.md), [2026-10-15-release-candidate-windows.md](https://github.com/michal-ciechan/Antiphon/blob/6e23e0b4c8d6b2ae12218c2c0058593dbf773530/docs/investigations/2026-10-15-release-candidate-windows.md) and [2026-10-15-release-candidate-defects.md](https://github.com/michal-ciechan/Antiphon/blob/6e23e0b4c8d6b2ae12218c2c0058593dbf773530/docs/investigations/2026-10-15-release-candidate-defects.md).

## Known issues

- **CARD-1310:** checkpoint-tool Windows E2E receipt cleanup refusal remains unresolved; receipts for E2E rows are invalid.
- **CARD-1303:** remaining follow-up sections R4-R6 and R11-R14.
- **CARD-1305:** environment items D-2..D-6 remain.
- **CARD-1309:** native E2E failures remain; 16 reproduce at the morning baseline cc8798c1fa4f3c2faa834283aef080d8e5fd28f0.
- The automated release gate is not registered: there is no Windmill RC job or qualification receipt. **This release carries no gate credit.**

## Changes by card

### CARD-0987 — checkpoint custody contracts and portable fixtures

- [99ebb196d8](https://github.com/michal-ciechan/Antiphon/commit/99ebb196d8c7309fb4716699bfa91cbe8860dc2d) test: use host-native paths in virtual checkpoint custody fixtures
- [bb44d70cfc](https://github.com/michal-ciechan/Antiphon/commit/bb44d70cfc845e53e2ebb9725d3df8c0364414b3) fix(tests): align checkpoint reference contract with custody plan

### CARD-1299 — Codex plain prompt echo precedence

- [ce35694db9](https://github.com/michal-ciechan/Antiphon/commit/ce35694db96656470d1d627b7b34985748079a76) fix: prefer plain Codex prompt echo matches
- [2db5f46224](https://github.com/michal-ciechan/Antiphon/commit/2db5f462241283a307c6ea630c14202c0d10cd5e) test: reproduce Codex plain echo precedence defect

### CARD-1259 — durable transcript actions, queue progress and recovery

- [46fd97a187](https://github.com/michal-ciechan/Antiphon/commit/46fd97a187866e26f56a6f261038ada4d7d57870) fix: reconcile RC2 runtime action gate contracts
- [48d1edaabe](https://github.com/michal-ciechan/Antiphon/commit/48d1edaabe618087b478471693fb1f19e0954871) test: pin RC2 queue contract omissions before repair
- [f7900158ef](https://github.com/michal-ciechan/Antiphon/commit/f7900158ef3416894c720d892bb1c21cc8095057) fix: retain typed review handoff identity during wakeup replacement
- [12fe3cd76b](https://github.com/michal-ciechan/Antiphon/commit/12fe3cd76b34436ec6ed38048073655ef25bcc74) fix: reselect queue order after routing before completing new wakeup
- [2fea6a8b18](https://github.com/michal-ciechan/Antiphon/commit/2fea6a8b18559a4ef197e38f0e604dc997a5062d) fix: bound deferred queue work and replace stale wakeups (CARD-1259 D-1)
- [c8e128cfe4](https://github.com/michal-ciechan/Antiphon/commit/c8e128cfe47dae8134a90577d3aaabc6e29a7969) test: import transcript event DTO for D-1 reproductions
- [59544042d8](https://github.com/michal-ciechan/Antiphon/commit/59544042d8937d5e97b266098f494acee1ea5ab1) test: reproduce permanent queue action deferral and backlog (CARD-1259 D-1)
- [28f46f6384](https://github.com/michal-ciechan/Antiphon/commit/28f46f6384697adfa1fce5bf4917c3230e24cd6f) test: align inherited pump fixtures with deferred runtime work
- [bb1397f92d](https://github.com/michal-ciechan/Antiphon/commit/bb1397f92d4f42d6b23ffca66cb8105ff72b0774) test: complete recipient turns before idle-intent acknowledgement
- [6646e3c16e](https://github.com/michal-ciechan/Antiphon/commit/6646e3c16eaa130d357b9957c2299da627b1e346) test: complete manual and review handoff recovery cuts
- [7435d145c7](https://github.com/michal-ciechan/Antiphon/commit/7435d145c76611f796db322a7058ceefa489aeb7) test: exercise review recovery across durable handoff cuts
- [c1e6a5a2bb](https://github.com/michal-ciechan/Antiphon/commit/c1e6a5a2bba6aea724f1ca55191d0c372687f8f1) test: strengthen action persistence and page-yield verification
- [592e23b902](https://github.com/michal-ciechan/Antiphon/commit/592e23b90253d8e4e8d9f556fab204dc18aedbc3) test: capture routing receipt identity before EF query
- [ee4fff963b](https://github.com/michal-ciechan/Antiphon/commit/ee4fff963b33e09d128762287b62fb96406b07fa) test: implement remaining transcript action routing cases
- [818b65b976](https://github.com/michal-ciechan/Antiphon/commit/818b65b976924600dafa7eb729a4d4cb81e51c40) fix: recover direct tails beyond uncertain roots and terminal stops
- [fe97ca7fb4](https://github.com/michal-ciechan/Antiphon/commit/fe97ca7fb41ce34cc8e1e9fa264f227b1226e08b) test: reproduce direct-tail starvation and terminal suffix loss
- [afb07ab7a6](https://github.com/michal-ciechan/Antiphon/commit/afb07ab7a691a10152e17091a6b7d21fecba9c08) test: complete the second turn before checking idle debt acknowledgement
- [19025fd532](https://github.com/michal-ciechan/Antiphon/commit/19025fd532813fbfeff8c5f4c15728a1c4a373a1) test: complete deferred eligibility cases and await conversion routing
- [4a048e9d07](https://github.com/michal-ciechan/Antiphon/commit/4a048e9d073140233128f5001a19b3dbf3ccb26c) test: cover deferred eligibility and private recovery failures
- [72634f39cb](https://github.com/michal-ciechan/Antiphon/commit/72634f39cb13ff12f804e7e0115ab66a55995f08) test: await deferred channel actions after transcript ingestion
- [69c3259793](https://github.com/michal-ciechan/Antiphon/commit/69c32597935db75de9d7997f9e4ad0aff07d3fc5) test: route by the persisted boundary in alternate sessions
- [1e404fd307](https://github.com/michal-ciechan/Antiphon/commit/1e404fd307107f274db411139a7cd936a0d8f233) test: own the review producer database scope
- [77d1f65728](https://github.com/michal-ciechan/Antiphon/commit/77d1f657282517f88a10d2dab2ac4c0da1e20c46) test: add real review producer and pump routing coverage
- [3b24990616](https://github.com/michal-ciechan/Antiphon/commit/3b24990616375e6845aa3b4fd971fe2b87360df9) CARD-1259: generate attempt and direct-tail recovery migration
- [3b69c4f41c](https://github.com/michal-ciechan/Antiphon/commit/3b69c4f41c557819460c24d6b090875bd249e3d6) fix: correct direct-tail extraction and remove cache-only overload
- [6288f26fea](https://github.com/michal-ciechan/Antiphon/commit/6288f26fea0ded62206d74e4f5166d3482ce921a) WIP CARD-1259: fence deferred attempts and persist direct reply recovery
- [f6073f776e](https://github.com/michal-ciechan/Antiphon/commit/f6073f776eb5d6da0135ecd60dadf9bb67143f05) test: observe queue retention lease without database timing
- [b412fba529](https://github.com/michal-ciechan/Antiphon/commit/b412fba529a988d036d9dd1b8cf5fa146a4ac71c) test: import transcript DTO in recovery fixture
- [aaf12e3ee6](https://github.com/michal-ciechan/Antiphon/commit/aaf12e3ee6004f4239a6894582883a859edd66aa) test: wire recovery settlement fixture dependencies
- [da09571d01](https://github.com/michal-ciechan/Antiphon/commit/da09571d01a890e47e565b2c0a30a43ab408d9e8) test: reproduce CARD-1259 deferred recovery gaps
- [39abde2041](https://github.com/michal-ciechan/Antiphon/commit/39abde20417dfbbf1dce762bfa89b6575b359d74) CARD-1259: migrate manual intent and preserve review and shutdown boundaries
- [b7492f8a73](https://github.com/michal-ciechan/Antiphon/commit/b7492f8a731dc506dd7d216336f1d14622ea9bf7) CARD-1259: preserve manual intent and fit review action identity
- [6d1eb51029](https://github.com/michal-ciechan/Antiphon/commit/6d1eb510298abe25bc5e22c83cbb19c8241f1a3f) CARD-1259: guard manual compaction debt through safe-stub fallback
- [a7b18799b2](https://github.com/michal-ciechan/Antiphon/commit/a7b18799b249f14071ab329848305e1f48268d3b) CARD-1259: pin database clock precision and exercise retention lease race
- [f4770aa921](https://github.com/michal-ciechan/Antiphon/commit/f4770aa921e818cc94aef90c95ee6d5613c00e28) WIP CARD-1259 S2 contracts: exercise atomic debt and worker lifecycle
- [b783571324](https://github.com/michal-ciechan/Antiphon/commit/b78357132413edfa0e3eaee5299fda31f0830729) CARD-1259: commit review idle debt and late-confirm identities with effects
- [9fb8ecc872](https://github.com/michal-ciechan/Antiphon/commit/9fb8ecc8729efbc9985a1fa09a7a61435c40fda7) CARD-1259 R1: use committed state and action admission in error replay guard
- [256f607c68](https://github.com/michal-ciechan/Antiphon/commit/256f607c68f2b2cefb07e431278b5a2d4cc2866e) CARD-1259 S4 guards: cold UI restart and deferred regression fixtures
- [b354567d63](https://github.com/michal-ciechan/Antiphon/commit/b354567d63d60e0f476382195e681fae6504e09e) CARD-1259 S3: generate durable review and action handoff schema
- [04c38d0883](https://github.com/michal-ciechan/Antiphon/commit/04c38d08833efc7fad1533063357ac7e494eb895) CARD-1259: repair schema compile and preserve overflow discovery
- [b8893359f3](https://github.com/michal-ciechan/Antiphon/commit/b8893359f3c4b27e41c7934bfb229cd16e152191) WIP CARD-1259 S3: bind deferred turns and persist review and compaction authority
- [bccea18b73](https://github.com/michal-ciechan/Antiphon/commit/bccea18b73f43df211b2400bc941add92f2f1c5a) CARD-1259 S2: generate durable runtime action schema
- [09c4c9a5fe](https://github.com/michal-ciechan/Antiphon/commit/09c4c9a5fe311820963a9fadef3f596602cccd65) WIP CARD-1259 S2: persist deferred transcript actions and own bounded workers
- [f0b38259ca](https://github.com/michal-ciechan/Antiphon/commit/f0b38259ca22935985c1274ce637943b2d80716b) test: add red-first transcript pump progress regressions for CARD-1259

### CARD-1287 — RC E2E contract snapshots and runner determinism

- [a65a9ec17a](https://github.com/michal-ciechan/Antiphon/commit/a65a9ec17a3a84e8443e6a1fa809bfedfbdb13e9) docs: record D-10 red and runner projection scope blocker
- [7aabbd86f2](https://github.com/michal-ciechan/Antiphon/commit/7aabbd86f249e859f573cadb8b7787d0bb01c01f) test: bound D-10 runner determinism verification
- [510a5f7aef](https://github.com/michal-ciechan/Antiphon/commit/510a5f7aef84318fb66acd41e08954ba15f0b23b) docs: record CARD-1287 partial verification and outstanding delegated runner snapshot
- [9ebaab1aad](https://github.com/michal-ciechan/Antiphon/commit/9ebaab1aada62365c7d916e89c93720817c1243a) test: complete D-8 pipeline additive snapshot and final contract selection
- [e55f4bfcbb](https://github.com/michal-ciechan/Antiphon/commit/e55f4bfcbbdc78240a00f79081f632fb8c3ab4aa) test: reconcile D-9 through D-11 contracts; stabilize D-8 scenario (WIP)
- [3ca0ae90ab](https://github.com/michal-ciechan/Antiphon/commit/3ca0ae90ab093024dc1d79ad2b349007338581e3) test: bound D-8 through D-11 snapshot repair verification
- [7c580d7b52](https://github.com/michal-ciechan/Antiphon/commit/7c580d7b52a714a0c9812bcebee53ebf2e2e7a10) Fix RC E2E D-7 portable shell and D-12 deterministic snapshots

### CARD-0710 — runner display contract alignment

- [b227f814c0](https://github.com/michal-ciechan/Antiphon/commit/b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d) fix(CARD-0710): align D-10 snapshots with desktop runner display

### CARD-1303 — admission fence and R10 API contracts

- [b7265a05aa](https://github.com/michal-ciechan/Antiphon/commit/b7265a05aa8773f08dbf457b93c7561e7874822d) test(CARD-1303): distinguish admission fence from foreign-key locks
- [4b956c1c13](https://github.com/michal-ciechan/Antiphon/commit/4b956c1c13a534f3cbb21ab76313df9435cf6543) fix(CARD-1303): preserve admission fence without blocking card FK inserts
- [c55c33a077](https://github.com/michal-ciechan/Antiphon/commit/c55c33a0779e2191245de6d625e474cda2056b40) test(CARD-1303): implement red-first card-bound claim witness
- [09e768077c](https://github.com/michal-ciechan/Antiphon/commit/09e768077c986c5408be8d90b8ae8937f8545873) test(CARD-1303): require card claim session insert during admission
- [41d6f6adf4](https://github.com/michal-ciechan/Antiphon/commit/41d6f6adf431622b3982f9ac41417a8e555072ba) docs(api): document R10 handoff and Review correction contracts
- [5ac3388916](https://github.com/michal-ciechan/Antiphon/commit/5ac3388916c0b86c174397268352e29b87e6aec1) test(docs): fix route guard assertion diagnostics
- [0858375377](https://github.com/michal-ciechan/Antiphon/commit/08583753776fd81c8f3e510e118b7c165b8c094f) test(docs): guard R10 handoff and correction route contracts

### RC verification repairs — native diagnostic Error outcomes (no card attribution established)

- [1c72bb00d0](https://github.com/michal-ciechan/Antiphon/commit/1c72bb00d039b1efd6b9396f84ad4dfaeb652c94) fix: retain native Error outcomes and failure explanations (D-13)
- [60a41ef7fd](https://github.com/michal-ciechan/Antiphon/commit/60a41ef7fd4b9663f269464c188902e28bb1cac8) test: reproduce dropped native diagnostic Error outcomes (D-13)

### Rollout tooling — cache Fixture npm ownership (no card attribution established)

- [6ca859d006](https://github.com/michal-ciechan/Antiphon/commit/6ca859d006b0a3cac9a88dc6bd551c37e98b2217) test: validate mapped npm stage in fixture model
- [ed523659a6](https://github.com/michal-ciechan/Antiphon/commit/ed523659a6c4022b7810fec8ac9692f7cc8651ae) test: model writer npm verification in cache seed contract
- [19003ef50c](https://github.com/michal-ciechan/Antiphon/commit/19003ef50cc97793cf9c7391f924c822453ce46f) docs: correct TUnit method filters for adjacent cache checks
- [b7c64da7b9](https://github.com/michal-ciechan/Antiphon/commit/b7c64da7b9ce209ef93e5c8d078447d61f53268b) fix: verify fixture npm cache through its uid-1654 writer
- [6b8bf2086a](https://github.com/michal-ciechan/Antiphon/commit/6b8bf2086a089baa679da35d97edd297803e7941) docs: pin DLL launch for Windows ownership checkpoints
- [d758edc7d8](https://github.com/michal-ciechan/Antiphon/commit/d758edc7d8fa585073cf808e405aea34a53e307c) test: import script runner for native npm regression
- [e2977f41ed](https://github.com/michal-ciechan/Antiphon/commit/e2977f41ed3e07730187fef5f1c3f6f1268796f9) test: expose fixture npm seed mixed-uid cleanup defect
