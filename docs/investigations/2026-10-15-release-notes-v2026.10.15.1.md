# v2026.10.15.1 — draft release notes

**DRAFT — NOT PUBLISHED; RELEASE GATE FAIL / INCOMPLETE.** No tag was created or
pushed, and publish-release.ps1 was not run. This proposed title reserves no
version. **First release, no earlier release.**

Candidate ref: release/rc-20261015T215926Z.
Candidate SHA: 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
Notes baseline: cc8798c1fa4f3c2faa834283aef080d8e5fd28f0.

The initial release/2026-10-15 branch was pushed under the original brief before
the naming correction arrived; it remains at the same SHA and was not deleted.
The corrected timestamped ref was created by the authorized plain create-only
push and verified remotely. This manual cut supplies no producer journal.

## Verification

| Piece | Result |
|---|---|
| Exact pinned-source Linux build | PASS; 0 errors, 1,057 warnings; 123.498s wall |
| Frozen Linux Unit roster | FAIL; 373 classes, 4,516 cases: 4,515 pass / 1 fail / 0 skip; 44m59.430s wall |
| Failed Linux class rerun | FAIL; 3 pass / 1 fail / 0 skip; same contract assertion |
| Stack smoke | FAIL/incomplete; absent named dev Postgres and Windows-only listener command on Linux |
| Fresh client dependencies/build/lint | PASS; lint has 0 errors/warnings |
| E2E build and Chromium download | PASS; browser launch lacks libglib-2.0.so.0 |
| E2E compiled census | 171 cases; 167 required, 4 explicitly excluded |
| Required local E2E run | FAIL; 54 pass / 110 fail / 3 required skips; 22m15.173s wall |
| Contract snapshot class rerun | FAIL; all five mismatches reproduced |
| Channel class rerun | FAIL; session-start timeout reproduced |

The Linux failure is CheckpointToolProjectContractTests.csproj_has_no_project_references
after the approved CARD-0987 tool-to-HostCleanup reference. E2E failures comprise
69 Windows fixture admissions, 35 missing-library browser setup failures, one
channel session-start timeout and five contract snapshot mismatches. Three F-5
cases skip under the automated safe environment. The official native diagnostic
adapter also drops the 110 Error terminal records; its UID ledger is incomplete.

Full defect details: [release candidate defects](2026-10-15-release-candidate-defects.md).
Build source identities, counts and limits: [release candidate evidence](2026-10-15-release-candidate.md).

## Profile, exclusions and limits

The required rc profile is the full automated Windows antiphon, session-runner,
pty-host, agents-pty, messaging, client, scripts and e2e profile. This Linux subset
and E2E diagnostic do not meet it. Full Windows/native, disposable-broker messaging,
Vitest, full script census and producer authority/ledger/accepted report evidence
remain missing. The local E2E assembly was built at evidence-only commit
8aabe6da92c258237619f66606d3bda3ea4baf7b with product/test inputs unchanged from
the candidate; it is not relabeled as a formal exact-SHA release build.

The frozen Linux selection has the CARD-1287 documented platform/harness exclusions
and is not all Unit coverage. The E2E policy excludes DelegationPipelineE2ETests,
DelegationSequencingE2ETests and OutputDistillationApplyCanaryTests (four cases),
under CARD-0599's separate live/headed/approved-canary obligations. No other OptIn
case was silently excluded. Required skips remain red/incomplete. The wider RC
policy also assigns live, headed, approved distiller, QEMU and Linux-only checks
to named separate owners. No policy exclusions or hashes changed in this pass.

The release gate refuses this manual red/incomplete subset. No complete-green,
Windmill qualification/scheduled receipts, CalVer tag, published release/manifest,
or Interim qualification was produced. Interim stays disabled; production
AppHost/server/runner were not restarted or stopped. RC evidence does not earn
master nightly readiness. GitHub Actions is not a release prerequisite, and no
Actions green is claimed here.

## Local evidence identities

Producer task: 1c14bf28-01e7-4cc4-a297-5a368057fc3a. These are actual local TRX
identities and digests, not trusted RC/Windmill receipt ids.

| Run | TRX id | SHA-256 |
|---|---|---|
| roster | 78c763e2-17e3-407e-993f-6292ca5128f2 | aaeacbb7ce2a10258eb6a175683e11985051c44a874c0e58d03629db659bd4ef |
| rerun-project-contract | cce49f37-0895-4909-9c08-2306f9d4d182 | c26923116c8e669d4f85d3f4691c2936cb592ccc9561c7b4e15786193624ccde |
| e2e | 3f5d463a-f143-4aa3-bd77-11164543dd15 | 92842f6a98e66f6659e38c19095a72b385cb891f5a56b69c3dd9dc409d2bdf6a |
| e2e-rerun-contract | f4c0cc58-19fb-4cb4-9eaa-32d7aa5081d3 | 72812df6c17dcbeeee69aa6418760cd9a045c593c047d437a53e03e8c6a219ce |
| e2e-rerun-channel | d48fa74c-0c27-43b4-8e7a-c110c2cc8598 | 179c36a084cb5503d1bb50c76bc40b59555fcd2d75a91c86b44794ec47075dad |

## Changes since the morning baseline

125 commits grouped under 16 cards. Attribution uses explicit subjects, a sole
card artifact in changed paths, or the owner plan for the exact changed
checkpoint/caller-handoff file. This list is a changelog, not train membership,
acceptance or landing approval. Only commits through the pinned candidate are
included; later evidence commits are excluded.

### CARD-0907

- [157321d8bb](https://github.com/michal-ciechan/Antiphon/commit/157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f) docs(card-0907): record D1 red and qualified green evidence
- [7d290cca00](https://github.com/michal-ciechan/Antiphon/commit/7d290cca00c9d5c53cbf508cfcee7ea8c947eb93) fix(card-0907): confine cursor separation to Codex raw callers
- [1c76c16efe](https://github.com/michal-ciechan/Antiphon/commit/1c76c16efebeb66c84beae3d60e2c7da5a1acc6d) test(card-0907): reproduce shared cursor analyzer regression D1
- [32ad5efe0d](https://github.com/michal-ciechan/Antiphon/commit/32ad5efe0d9531f5eaf6b5a77572bab74af5f6b6) docs(card-0907): record verified raw-parser slice and pending capture
- [1eeac03eec](https://github.com/michal-ciechan/Antiphon/commit/1eeac03eece1692919ba1ef16969010997b51348) fix(card-0907): preserve Codex cursor words and parse status bars
- [363cf17a1d](https://github.com/michal-ciechan/Antiphon/commit/363cf17a1d167e91de333d58f5e7fbc8c5d34552) test(card-0907): correct labeled excerpt assertion overload
- [42b7627e54](https://github.com/michal-ciechan/Antiphon/commit/42b7627e54138c884673551eb020148bc8960332) test(card-0907): use supported Shouldly aggregate assertions
- [6e322c0b4a](https://github.com/michal-ciechan/Antiphon/commit/6e322c0b4a5c09345b5785d268d67b46e58d0991) test(card-0907): expose Codex raw cursor and status bar regressions

### CARD-0987

- [067b47e2dc](https://github.com/michal-ciechan/Antiphon/commit/067b47e2dc903247f1a7c8aa539032cc4266e4ad) docs(checkpoints): record S1a1 final qualification and pending mutation
- [ca9e7074e8](https://github.com/michal-ciechan/Antiphon/commit/ca9e7074e89490d8c8eacf5de223537a6583b5dd) test(checkpoints): complete receipt positive pairs and preserve qualified evidence
- [dd65b25c8f](https://github.com/michal-ciechan/Antiphon/commit/dd65b25c8f1eedf4ce2bcd0c67fc31d9e2d262fd) fix(checkpoints): preserve missing-source receipt refusal before shape validation
- [2071ad345c](https://github.com/michal-ciechan/Antiphon/commit/2071ad345c389fffb9240340d62619b3c7ea57f4) feat(checkpoints): thread receipt context and bind S1a1 guards
- [d0c612cbef](https://github.com/michal-ciechan/Antiphon/commit/d0c612cbef3c4a5d7628494f007de1ae620892e0) fix(checkpoints): align context containment with output cleanup
- [cdb18eef50](https://github.com/michal-ciechan/Antiphon/commit/cdb18eef5013bfa45336dfd304a022b489686bbc) fix(checkpoints): preserve standalone native directory facade
- [6effc7a7ee](https://github.com/michal-ciechan/Antiphon/commit/6effc7a7ee4d244e2b38c6536ee2f2a59330294f) fix(checkpoints): isolate creation custody per explicit context
- [09e585149e](https://github.com/michal-ciechan/Antiphon/commit/09e585149e2959cab503e168003108523336662d) test(checkpoints): correct assertion overloads and bind variant labels
- [e6a4cbaa45](https://github.com/michal-ciechan/Antiphon/commit/e6a4cbaa4509f0eac79a415f1f7793fdfa880727) test(checkpoints): disambiguate custody test category import
- [367c8ee4cd](https://github.com/michal-ciechan/Antiphon/commit/367c8ee4cdcc877aa71f1a46d30c410d628edc3b) test(checkpoints): bind S1a0 custody primitives and isolation regressions
- [52180f3898](https://github.com/michal-ciechan/Antiphon/commit/52180f3898a8ce6b05e5ce888eb8f896d300221c) docs: freeze CARD-0987 custody verification ledger
- [0923bcd1b3](https://github.com/michal-ciechan/Antiphon/commit/0923bcd1b317a1f1388dc72cb51ab2faa37fbf18) docs(card-0987): refresh remaining plan for guarded custody
- [9d4481d1e1](https://github.com/michal-ciechan/Antiphon/commit/9d4481d1e1f3a2a48ad65f994b6eb749c5d54b92) docs(card-0987): record S1a custody and acceptance blockers

### CARD-1031

- [a30b7f4297](https://github.com/michal-ciechan/Antiphon/commit/a30b7f4297400f010ce70188ac99ebe55be51931) docs(card-1031): reconcile completed implementation and Windows proof

### CARD-1136

- [23ad878aaa](https://github.com/michal-ciechan/Antiphon/commit/23ad878aaa6b45e1f12cdb2becd6fb3349eb22dc) docs(card-1136): explain chained drivers escape the build slot lease
- [9eb4954df2](https://github.com/michal-ciechan/Antiphon/commit/9eb4954df275fa0025a825e430ddafaeba98b9d5) test(card-1136): correct combined checkpoint class selectors
- [f80779d7f7](https://github.com/michal-ciechan/Antiphon/commit/f80779d7f7b44cc4c63edcdec40f9dcd2eb19119) test(card-1136): pin CLI sync debt output and missing slot warning

### CARD-1246

- [d154f7264d](https://github.com/michal-ciechan/Antiphon/commit/d154f7264d45af29926e6f1fdd78cc661cadd98d) docs(card-1246): correct production scope and receipt timeout evidence
- [ca41664bcc](https://github.com/michal-ciechan/Antiphon/commit/ca41664bccbc558fc0ffd743b5ad807180bc9327) docs(CARD-1246): correct the warm getComputedStyle figure for gcs7 (1870 - 1106 = 764 ms)
- [d0bc8e5001](https://github.com/michal-ciechan/Antiphon/commit/d0bc8e5001823494ec4697c0073cb38466408676) docs(CARD-1246): evidence for the SessionTabs act fix, the modal render-cost probe and checkpoint run 20261015-064750-1f66
- [d970fde2d2](https://github.com/michal-ciechan/Antiphon/commit/d970fde2d263a6cfff8f4a3126f34ccbcb4e73c4) docs(CARD-1246): checkpoint plan, one serial row over the six named client files
- [54fb60f5e6](https://github.com/michal-ciechan/Antiphon/commit/54fb60f5e6ffaaa064754af8f2eb8c704244b1e7) test(CARD-1246): SessionTabs terminal opens inside act through a shared lazy loader (CARD-1256 pattern)
- [2a628cfbad](https://github.com/michal-ciechan/Antiphon/commit/2a628cfbad23441315fe00e20815324bf7e03885) test(CARD-1246): Phone assertion typechecks after the settle; Code evidence (partial: stressed re-measure not green)
- [ce1c626ead](https://github.com/michal-ciechan/Antiphon/commit/ce1c626ead2417e5ec418138db6866ce3b46c9c0) test(CARD-1246): settle React Query inside act and paste instead of typing in the load-sensitive agents tests; receipt limit starts at the hub connection

### CARD-1274

- [526dd8abd8](https://github.com/michal-ciechan/Antiphon/commit/526dd8abd8fdebe8fc03a9638886338a911a1d62) docs(settings): record C1274 repair verification and remaining disclosures
- [96665668ef](https://github.com/michal-ciechan/Antiphon/commit/96665668ef757d68f8c69d1a67c48ab56aa50af2) fix(settings): keep unread selectors and setup state explicit
- [c1dc81b0be](https://github.com/michal-ciechan/Antiphon/commit/c1dc81b0be3ca20145854bf6f017478a4940986c) test(settings): locate described remote-control switch in repair rows
- [3db6bcf800](https://github.com/michal-ciechan/Antiphon/commit/3db6bcf8001fa710832a5ad9190837faf0fc715a) test(settings): reproduce unread wizard and selector findings C1274
- [689e4b8d2c](https://github.com/michal-ciechan/Antiphon/commit/689e4b8d2c64b15720040dac5c401a8bc729b471) docs(CARD-1274): evidence wording (shrink method)
- [1ea6d7aa43](https://github.com/michal-ciechan/Antiphon/commit/1ea6d7aa4303a6f38ff6ad768d50884306e45513) docs(CARD-1274): Code evidence, checkpoint lines and disclosures
- [78185afe72](https://github.com/michal-ciechan/Antiphon/commit/78185afe72233201a51d562674616a20eec93d51) docs(CARD-1274): SettingsPage.test.tsx moves to CP-4 as inherited red (expected 15 to be 1 at base 78e400178)
- [316e1a2501](https://github.com/michal-ciechan/Antiphon/commit/316e1a2501bd88d1e7969f5428060aa2bea7f353) fix(CARD-1274): Settings tabs show pending, paused and failed reads as loading or unknown; Delete and Save key stay disabled on an unread impact or key list
- [317be628f6](https://github.com/michal-ciechan/Antiphon/commit/317be628f61452f4acf4a79f9f09adc52101f17a) test(CARD-1274): Settings reads table for loading, paused, failed and success states; plan and PC candidates

### CARD-1277

- [e1383249fa](https://github.com/michal-ciechan/Antiphon/commit/e1383249fab9025f5b8f82424d4ece37030529ee) docs(CARD-1277): record the S3 PtyHost checkpoint receipt
- [b5d46722a1](https://github.com/michal-ciechan/Antiphon/commit/b5d46722a18c963acac6ebc47ac12a50487ed87c) fix(CARD-1277): deliver the Linux CR as its own fake burst
- [2b4e9b4fb5](https://github.com/michal-ciechan/Antiphon/commit/2b4e9b4fb5b473ae84cf624cb6bcf1e2c1063c6b) fix(CARD-1277): keep the channel nonce in the PtyHost transcript
- [5e21b61a2d](https://github.com/michal-ciechan/Antiphon/commit/5e21b61a2d3b05d919edee81d71e43dd65f34751) test(CARD-1277): pin the PtyHost transcript nonce and the Claude two-write submit

### CARD-1278

- [d7b7bdc5a2](https://github.com/michal-ciechan/Antiphon/commit/d7b7bdc5a22375152d539fd0b249b88ddcfd9d07) docs(CARD-1278): Code evidence (S1 red 13a4, S2 green a221)
- [8b88737144](https://github.com/michal-ciechan/Antiphon/commit/8b88737144dc1dcf5ef4548b2ac239541b27fa6c) fix(CARD-1278): the expiry sweep forgets the rows it cleared on each lost race, the last included, so a default-tier create that catches the error saves its task with the warning; API doc sweep table, matrix test row, CARD-1275 plan sentence
- [708bd6b5ea](https://github.com/michal-ciechan/Antiphon/commit/708bd6b5ead396a4f0c48af9dcfbff57e912a3da) docs(CARD-1278): checkpoint rows share the S1-S2 After group (one build)
- [8fd6333909](https://github.com/michal-ciechan/Antiphon/commit/8fd6333909fb6536d51f07a5f08ef33c6940f50c) test(CARD-1278): a default-tier create survives a sweep that loses every race; the sweep leaves no stale chain row tracked (red: V-1, V-3, V-4)

### CARD-1281

- [b6fdedc13f](https://github.com/michal-ciechan/Antiphon/commit/b6fdedc13fc46baddc22206c5c34003a93d3cd5b) docs(card-1281): record S6a acceptance and confirmed baseline gate failures
- [e54d54f91e](https://github.com/michal-ciechan/Antiphon/commit/e54d54f91e3662f8305c61ddc052bcd99ebdfbb0) test(card-1281): bind refusal codes and correct Program wire fixtures
- [635e286627](https://github.com/michal-ciechan/Antiphon/commit/635e286627ade38635d1dff858a5216e02d07b1b) feat(card-1281): register guarded correction and caller handoff routes
- [0633527c62](https://github.com/michal-ciechan/Antiphon/commit/0633527c62be63a8acedc35cb7bb6a967d06afdf) fix(card-1281): enforce correction card scope and guarded child admission
- [86d917a79b](https://github.com/michal-ciechan/Antiphon/commit/86d917a79b8e91e7ea7e4a2ebbc87e832be0fe6b) test(card-1281): repair adversarial fixtures and guard host drain admission
- [99cefb4f74](https://github.com/michal-ciechan/Antiphon/commit/99cefb4f7453ae3ea56251694dca5b9a1f5f8fed) test(card-1281): fix assertion overload and extend receipt adversarial cases
- [f3f28d964e](https://github.com/michal-ciechan/Antiphon/commit/f3f28d964ef57d809d26ea218c64277cf09de29e) test(card-1281): bind caller child and Program wiring acceptance
- [1839210555](https://github.com/michal-ciechan/Antiphon/commit/1839210555a5e6cf84e11295e5e226b0749e7960) fix(card-1281): enforce recipient and handoff admission refusals
- [1b4bc3dab7](https://github.com/michal-ciechan/Antiphon/commit/1b4bc3dab78c1233fbb5428bf602a4e806a6dbab) test(card-1281): extend correction authorization and transaction coverage
- [4e7f3229e6](https://github.com/michal-ciechan/Antiphon/commit/4e7f3229e69ce9f4056af0875cd8aff184951f19) docs: record S6a checkpoint ownership blocker and pending verification
- [ce6dd79df0](https://github.com/michal-ciechan/Antiphon/commit/ce6dd79df05e5813758d2545ccb8fe9901fb66a0) test: expose S6a recipient admission and receipt number defects
- [8ffe363335](https://github.com/michal-ciechan/Antiphon/commit/8ffe3633357e6383b7e408bd078465acf9f5fd79) fix: restore stage bundle caps while preserving D-2 policy
- [b2a612807e](https://github.com/michal-ciechan/Antiphon/commit/b2a612807e9541258cc4f396123541958884fe03) docs(card-1281): retain B1 proof and inherited Final gate failures
- [7e95cd20ae](https://github.com/michal-ciechan/Antiphon/commit/7e95cd20ae1e2b853b86bf0e59fff81aae4935b1) fix(card-1281): return exact incomplete rows and harden correction approval
- [a345758956](https://github.com/michal-ciechan/Antiphon/commit/a345758956eac104664c0e4313c1639ffb8b260e) feat(card-1281): retain and validate receipt corrections before approval
- [42f101ad45](https://github.com/michal-ciechan/Antiphon/commit/42f101ad4515813a708b2806081c9e4b06e361af) test(card-1281): use existing repository root helper
- [1f47b9aa7a](https://github.com/michal-ciechan/Antiphon/commit/1f47b9aa7acee52dabd934cb23ecff2c5f761a2f) test(card-1281): specify receipt correction HTTP and approval acceptance
- [8906ea7c5f](https://github.com/michal-ciechan/Antiphon/commit/8906ea7c5f48a2cb74ebd97ecb097ef02dd1e6f3) fix(card-1281): bind completion receipts to the generated move revision
- [e4d444c911](https://github.com/michal-ciechan/Antiphon/commit/e4d444c9116d68635aa20cbfe33201afa8172e3a) test(card-1281): assert queued assignment rollback and real cleanup residue
- [4d0bc2b4b1](https://github.com/michal-ciechan/Antiphon/commit/4d0bc2b4b1db21467cc840736fb1c477a0118218) fix(card-1281): compare the required board project identifier
- [5a857cab1d](https://github.com/michal-ciechan/Antiphon/commit/5a857cab1dc8b19cf44fa991466a97986bb4b006) feat(card-1281): atomically close published cards against immutable acceptance
- [0e2ef3c304](https://github.com/michal-ciechan/Antiphon/commit/0e2ef3c30456d37950e3bebf77bf70eda5e4d2f1) test(card-1281): cover publication close predicates and recovery cuts
- [cdde745455](https://github.com/michal-ciechan/Antiphon/commit/cdde745455d817a9ffe617a9dde27980d799c4d1) test(card-1281): pin durable completion readiness before implementation
- [2020c1d708](https://github.com/michal-ciechan/Antiphon/commit/2020c1d7081513fbb6f717c20d5fe76993dce52a) docs: retain B-2 writer verification and deferred admission boundary
- [ea41e9ec0e](https://github.com/michal-ciechan/Antiphon/commit/ea41e9ec0e6728b399607ae92cc35f3117621787) test: isolate capability names in caller authorization matrix
- [2298ca664a](https://github.com/michal-ciechan/Antiphon/commit/2298ca664a3aabcfdc6dc68acccebf97b8042b42) test: fix writer assertion overloads and record refined scope
- [895b79c87d](https://github.com/michal-ciechan/Antiphon/commit/895b79c87d55f8e4d7d764b2e579bffe831b94a7) feat: persist scoped caller consumption and delay observations
- [4b963cab68](https://github.com/michal-ciechan/Antiphon/commit/4b963cab68f145414698c8da6e0bee817b29f80c) test: expose missing durable B-2 caller action route
- [3931a31149](https://github.com/michal-ciechan/Antiphon/commit/3931a31149acbc63837fcdda13cad60fdd1117cb) fix(review): restore pinned instructions alongside D-2 policy
- [9b4414b305](https://github.com/michal-ciechan/Antiphon/commit/9b4414b30582e7c3e4af9de82ea2a13e8e305566) docs(card-1281): apply acceptance-only verification refinement
- [ebad5df0d4](https://github.com/michal-ciechan/Antiphon/commit/ebad5df0d4bca7ad92245208e41fe62287f0b123) docs(card-1281): record policy proof and incomplete final unit gate
- [b707e7387e](https://github.com/michal-ciechan/Antiphon/commit/b707e7387e93737ba2ccd289e2358ef1f98d3367) feat(card-1281): separate advisory follow-ups from verification gates
- [902bedc490](https://github.com/michal-ciechan/Antiphon/commit/902bedc4902bd823bad859c027d935c33eb541d0) test(card-1281): correct Shouldly message argument binding
- [90141c05a5](https://github.com/michal-ciechan/Antiphon/commit/90141c05a5c853a839bc514758bd822b063a981c) test(card-1281): specify advisory policy against blanket review behavior
- [5dd289dbc3](https://github.com/michal-ciechan/Antiphon/commit/5dd289dbc37a3eeb82583899c4cb3fbd647f02b2) docs(card-1281): record CP-only refinement and Unit disclosure
- [6553b820db](https://github.com/michal-ciechan/Antiphon/commit/6553b820db819e4a56d919fb366831b2ac942858) docs(card-1281): retain S0 evidence and incomplete Final verification
- [879677aedf](https://github.com/michal-ciechan/Antiphon/commit/879677aedf319138f1207dc6e78e3749ff2eeb53) feat(card-1281): serialize card admission and atomically bind handoff children
- [f60c0bbb41](https://github.com/michal-ciechan/Antiphon/commit/f60c0bbb4164ba8d96cbe4ff27386cb69fb95aa2) test(card-1281): distinguish missing card lock from FK insert contention
- [03f6994007](https://github.com/michal-ciechan/Antiphon/commit/03f69940076c4eda39ef98416125a21037fbd7da) feat(card-1281): generate additive pipeline foundation migration
- [95c5b999f6](https://github.com/michal-ciechan/Antiphon/commit/95c5b999f61914b83935788d470644bb16753801) feat(card-1281): map foundation records and specify admission red witnesses
- [a48c04299c](https://github.com/michal-ciechan/Antiphon/commit/a48c04299c19457b89f1081a02a9533a0d6fb720) test(card-1281): use EF interpolated scalar query API
- [d34cbed484](https://github.com/michal-ciechan/Antiphon/commit/d34cbed4845218812e8bd64ab5c5742e628f8ef4) test(card-1281): specify foundation schema acceptance before mappings
- [1dcf2b1663](https://github.com/michal-ciechan/Antiphon/commit/1dcf2b1663b40a576934f00722f26de5a6af61a6) docs(card-1281): bind shared integration verification
- [b3d1bb6fc5](https://github.com/michal-ciechan/Antiphon/commit/b3d1bb6fc5f89f6627fe5fc82ff0e29f39f339bd) docs(card-1281): record qualified D-5 rows and deferred Final checks
- [19b25d2217](https://github.com/michal-ciechan/Antiphon/commit/19b25d22171fc2f1126a1e661fefab3678d34173) docs(card-1281): record D-5 preflight boundaries and pending qualification
- [179044a46d](https://github.com/michal-ciechan/Antiphon/commit/179044a46d1f20625a91a91af84615b45f81289e) docs(card-1281): carry verification plan onto master history
- [7012a447c0](https://github.com/michal-ciechan/Antiphon/commit/7012a447c0919e944dc8e95b912d8699682cf3db) docs: call CARD-1281 pre-publication state ready for release
- [d402e9579c](https://github.com/michal-ciechan/Antiphon/commit/d402e9579ccc7502fbc8460b1279ad80dd02de50) docs(card-1281): plan measured pipeline savings and release train

### CARD-1285

- [6a2fc6a3cf](https://github.com/michal-ciechan/Antiphon/commit/6a2fc6a3cfac85de811027021937b35f2f2e30d3) test(card-1285): repair inherited remote branch fixture and record attribution
- [38903d73a3](https://github.com/michal-ciechan/Antiphon/commit/38903d73a30ff28f5c71fb3a4899a143046298e4) test(card-1285): correct class selectors and record base attribution row
- [b602eac16a](https://github.com/michal-ciechan/Antiphon/commit/b602eac16a150d2acfe05d472aff500524168195) fix(card-1285): recover and persist task token before fresh launch
- [d6509499c0](https://github.com/michal-ciechan/Antiphon/commit/d6509499c074eae3090abdf50aceba4ffdf7805a) test(card-1285): seed prepared dispatch source before token regression
- [550b4f715f](https://github.com/michal-ciechan/Antiphon/commit/550b4f715f707c585bed3ad45c9768223c0addf4) test(card-1285): reproduce lost task launch credentials after restart

### CARD-1286

- [4dd7fb9f1f](https://github.com/michal-ciechan/Antiphon/commit/4dd7fb9f1fca0205f1f585fe31c7c041aeaee691) fix(card-1286): restore routing owner pointer after attributed red
- [c3199d53a8](https://github.com/michal-ciechan/Antiphon/commit/c3199d53a8ac2b9e519c8336a05ff6f6ca3dbd4a) docs(card-1286): bound pointer attribution and verification

### CARD-1287

- [fface5e533](https://github.com/michal-ciechan/Antiphon/commit/fface5e533c65164ed1ffa11730880432d319049) docs(CARD-1287): clarify exact-SHA replay filter inputs
- [71f10a7076](https://github.com/michal-ciechan/Antiphon/commit/71f10a707676683aca4596a97c63df678072bfc6) test(CARD-1287): qualify Linux roster twice with measured variance
- [3a076c39ee](https://github.com/michal-ciechan/Antiphon/commit/3a076c39ee0c2d39682683128a7576b6dc146c2d) test(CARD-1287): quarantine reader supervisor deadline failure
- [14acece536](https://github.com/michal-ciechan/Antiphon/commit/14acece536bc45665f9899902cac444f50e76b8b) test(CARD-1287): retain failed Linux candidate counts and wall time
- [ba182f4449](https://github.com/michal-ciechan/Antiphon/commit/ba182f4449e32e497ecffc5592ae70a430218882) test(CARD-1287): exclude selectors that admit Integration suffixes
- [bcbb07c4be](https://github.com/michal-ciechan/Antiphon/commit/bcbb07c4be8183703f2eca248c7115167940dd32) test(CARD-1287): pin candidate Linux Unit gate roster
- [f6310a452a](https://github.com/michal-ciechan/Antiphon/commit/f6310a452a911b45b0a0946feb9ac49dcc71bbfd) docs(CARD-1287): record Windows Unit failures and timing

### CARD-1289

- [f7c2ebfc9a](https://github.com/michal-ciechan/Antiphon/commit/f7c2ebfc9a48fda2df3abfa18b0046b19d6a3dc6) docs(card-1289): disclose receipt validation refusal and output cleanup
- [8f60dc93a1](https://github.com/michal-ciechan/Antiphon/commit/8f60dc93a1c06dfcf59d58ba688b10af8d618832) docs(card-1289): record red-first proof and incomplete Final qualification
- [fda428d923](https://github.com/michal-ciechan/Antiphon/commit/fda428d92384ff536fe776a83e1310c262f22559) fix(card-1289): preserve capacity refusal before Git preparation
- [9dcf778028](https://github.com/michal-ciechan/Antiphon/commit/9dcf778028668d332d3ff08cf02b3e14b864358e) fix(card-1289): fence admission state and prepare ordinary Git before card lock
- [c78b6afe29](https://github.com/michal-ciechan/Antiphon/commit/c78b6afe29157ac05d0ea3d3526e9e0b21159edb) test(card-1289): observe resolver Git subprocess seam
- [4abdc27217](https://github.com/michal-ciechan/Antiphon/commit/4abdc27217bbf64a681332a6c265ad0830063c31) test(card-1289): assert exact ordered lock sequence as text
- [aa3ce05881](https://github.com/michal-ciechan/Antiphon/commit/aa3ce05881425d981b4265610edb150a03be2655) test(card-1289): name collection assertion diagnostics
- [49a69fce96](https://github.com/michal-ciechan/Antiphon/commit/49a69fce96752d35f3d66e85ce79214c4e9c8ff6) test(card-1289): expose unrelated card edit refusal and Git lock hold

### CARD-1296

- [e03508908b](https://github.com/michal-ciechan/Antiphon/commit/e03508908b510dc43b60b3a3aa2a54f0d008e1c5) docs: record D-2 guard verification and pending mutation controls
- [41f4942073](https://github.com/michal-ciechan/Antiphon/commit/41f4942073f41295e38a4e52866e41d5751581c1) test: correct TUnit class alternatives in D-2 regression selection
- [c785999268](https://github.com/michal-ciechan/Antiphon/commit/c78599926845853177365a75670d60850421b064) docs: name D-2 checkpoint obligations and record delivery options
- [f4a923d8ec](https://github.com/michal-ciechan/Antiphon/commit/f4a923d8ec335a2dbad8bab004922ef524eef00c) test: guard D-2 owner section and delegate pointer (red first)

### CARD-1298

- [5ea87cdd91](https://github.com/michal-ciechan/Antiphon/commit/5ea87cdd91349ca6fe972088b260761c97fd2a37) test: repair reply helper and embedded fixture census failures

### CARD-1302

- [e1bbf5cb64](https://github.com/michal-ciechan/Antiphon/commit/e1bbf5cb649977d376b992f52f2b6afbed66f127) test(checkpoints): expose containment normalization defects (CARD-1302)
