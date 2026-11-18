# CARD-1744 blocker board state (2026-11-18)

Investigate task `3472c9a2`. Read-only. No card was moved, no host was probed, and production Postgres was not contacted. Board reads are `scripts/card.ps1 get` and `history` on board Antiphon during this task (CARD-1744 history revision 10 is this dispatch at 11/18/2026 18:37:01). Master is `origin/master` `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.

A reviewed tip and its master commit are the same change when `git patch-id --stable` matches. Land rebase keeps the message and changes the SHA, so several reviewed tips are not ancestors of master.

Case-sensitive `git log origin/master --grep=CARD-NNNN` misses subjects spelled `card-nnnn`. The SHAs below are the card-scoped subjects from `--regexp-ignore-case --grep=card-NNNN`, checked against `git log -S 'CARD-NNNN'` (that pickaxe also hits later docs that only mention the id).

## Verdicts

| Card | State | Verdict | Evidence | Proposed move |
|---|---|---|---|---|
| CARD-1768 | Review | DONE | Clean review, patch on master | Done |
| CARD-1774 | Review | DONE | Round 2 clean, patch on master | Done |
| CARD-1746 | Review | DONE | Placement decision on master | Done |
| CARD-1776 | Review | DONE | Round 2 clean, credential slice on master | Done |
| CARD-1778 | Review | NOT DONE | Code and docs are on master; the window re-home has no witness | Stay Review. See the reason below |
| CARD-1406 | Review | NOT DONE | Compose, drill, and CPU commits are on master; V-4 and the backup flip are open | Stay Review |
| CARD-1751 | InProgress | NOT DONE | No implementing commit; the probe task was canceled | Stay InProgress |
| CARD-1752 | Review | NOT DONE | Gate code is on master and reviewed; the bridge is still off | Stay Review |
| CARD-1744 | InProgress | NOT DONE | Plan is on master; the control-plane window has no witness | Stay InProgress |

## Open acceptance items

1. CARD-1406 V-4 on the host. `docs/superpowers/reviews/2026-11-18-card-1406-cpu-layout-review.md` (master `462ef487ed209b7498f651f167450eb1b8bf3804`) leaves V-4 as an operator `docker inspect`. `docs/superpowers/plans/2026-11-18-card-1406-cpu-shared-checkpoints.md` V-4F expects Postgres `cpuset=0,12`, `cpu_shares=2048`, memory `8589934592`, shm `1073741824`, blkio weight 800, and both runner containers with an empty cpuset and shares 0, plus quiet and loaded p95 samples. The cutover-prep note records Postgres `cpuset=0,12 shares=2048 mem=8589934592 shm=1073741824 nanocpus=0` and tailnet port 5432 at 17:23Z (`70eaa025df`, `docs/investigations/2026-11-18-postgres-cutover-prep-result.md`). That note does not record blkio weight, either runner container, or the p95 samples, and it says the runners were not touched.
2. Backup retarget flip. The script and doc are on master: `a6fc0655772bed2932e203cc9cb61359af218806` and `docs/docker-stack.md` (the daily backup follows the database via `postgres-snapshot.ps1 -Plain -Target server2` and `snapshot-target`). CARD-1831 is still Review. The same prep note says both snapshot schedules are disabled and are re-enabled only after prerequisites P-2 and P-3 and the W-3 flip. No committed note shows that flip or a re-enabled schedule.
3. Postgres cutover execute. The brief times it at 2026-11-18 17:28–17:37Z. The cited notes stop earlier. Drill PASS is `717b869288` (`docs/investigations/2026-11-18-postgres-restore-drill-result.md`, task `49232218`): restore exit 0, 142 tables, copy 10 s, restore 151 s, production cluster untouched, `StartedAt` still `2026-11-18T16:49:09Z`. Empty-cluster prep is `63214e5a38` on `origin/feat/card-task-61c8b8b6`. Cutover prep is `70eaa025df` on `origin/feat/card-task-807203ad` at 17:26:51Z: WhatIf exit 0, and the note says the script was not run without `-WhatIf`. No `docs/investigations` commit after 17:27Z records an execute receipt. The execute is UNOBSERVED from this checkout.
4. CARD-1778 window re-home. Record each always-on `workingDirectory`, stop the desktop sessions, PATCH to `/work/agents/<name>`, clear Herdr labels, start, and confirm Running. No witness of that pass.
5. CARD-1744 control-plane window (runbook phases 0–4 in `docs/superpowers/plans/2026-11-08-card-1744-cutover-plan.md`). No witness. The plan also still owes the `Project.LocalRepositoryPath` before/after table and one land from `/work/repos/antiphon` (`docs/agent-credentials.md:437-444` states the procedure).
6. CARD-1751 metadata probe from the staging container. No witness. CARD-1742 is Done on the board, so the old blocker is resolved; task `e6a8b2c6` was Canceled at 11/09 12:17Z because that drill was still owed then. Whether staging is up now is UNOBSERVED.
7. CARD-1752 enable. `ChannelBridge` stays off until CARD-1744 has stopped the current `antiphon-server-bridge` member and CARD-1751 has seen cluster `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8`.
8. CARD-1774 residuals that review `52f47408` said do not hold the land: A-7 witness hand-over race, A-8 orphan holder between the fresh lock take and the pid read. They were not filed as new cards by this investigation.

## Per card

### CARD-1768 Review — DONE

Acceptance is the CARD-1743 review advisories A-1 through A-7 (lease release after drain, crash expiry, SQLSTATE reporting, the LGPLv3 / CREATE doc line, doc pins, the C459 co-schedule flake, stored job type names).

History: Debug `26b144e7` Succeeded, Code `3a6056e7` Succeeded, Review `5034186b` Succeeded. Review `5034186b` is Final/Full CLEAN at `a8290a5948dab53bf1cfce38d470bbeb4c75b9d4`, `reviewedSourceClean=true`, outcome Clean, next land. Checkpoint `.antiphon/checkpoints/20261108-043805-553e`. Its handoff sends the unreachable-tick and own-backend residuals to a follow-up, which is CARD-1774. Report: `.antiphon/task-5034186b.md` on `feat/card-task-5034186b` at `da898484b770d887d7f09ee3c821a2037c860744`.

Master: `51f90a0d58da803279858582c475658c77a06228`, `8415dec8fe80a47b9503d791b673716afa725dfa`, `d116a618f1ddc048e5b8501b659cd535fd96deef`. `d116a618` is patch-identical to `a8290a594`. The doc line is `docs/docker-stack.md:1395` (`Hangfire.PostgreSql` 1.21.1, release on `ApplicationStopped`, 60 s unreachable retry).

Proposed move: Done. Reason: Review 5034186b is clean at a8290a5948dab53bf1cfce38d470bbeb4c75b9d4 and that patch is master d116a618f1ddc048e5b8501b659cd535fd96deef. Residuals are CARD-1774.

### CARD-1774 Review — DONE

Acceptance is A-1 through A-7 from the CARD-1768 review (tick retry, own backend, successor-overlap doc, real-silence witness if cheap, Dispose timeout, bounded `pg_locks` poll, behavioural witnesses).

History: Code `cdf2c821` Succeeded, Review `0754711b` Succeeded with blocker D-1, Code `806e4892` Succeeded, Review `52f47408` Succeeded. Round 1 (`0754711b`) found D-1: `ReadGrantedHolder` read `pg_locks` with no database filter (`HangfireConfiguration.cs:703-720` at that tip). Round 2 (`52f47408`) is CLEAN at `62271791fb4cdf9c72e5aa748ccb91bc2f743298`, `reviewedSourceClean=true`, outcome Clean. CP-1 32/32, CP-2 75/75, run `.antiphon/checkpoints/20261108-125331-a968`. Report commit `84a92ce526a10d1fc27fef5c2bf2be968a4d5313` on `feat/card-task-52f47408`.

Master: `cbc08c44f6d43b8f0c9017878749d89cdd09e059`, `0774dfa3dd8f9e5392981418bed21de0e18937e9` (patch-identical to `36a7f9051a`), `e14fc009ffd3c1fb040c0bd5fe7fdaf8ef93a475`, `663faf96fa0853af2dcef70062e7acb71f3bdddf` (patch-identical to `62271791`). The database filter and the 5 s unlock bound are in `docs/docker-stack.md:1395`.

Proposed move: Done. Reason: Review 52f47408 round 2 is clean at 62271791fb4cdf9c72e5aa748ccb91bc2f743298 and that patch is master 663faf96fa0853af2dcef70062e7acb71f3bdddf. A-7 and A-8 do not hold the land.

### CARD-1746 Review — DONE

Acceptance on the card is measure both directions, place the broker, then enable ChannelBridge. History is only Investigate `e7d7d78b` Succeeded, next decide. There is no Code or Review task.

The decision is on master at `462e3e3b4568a4076d14c3eb9571fe94abb298ea`, `docs/investigations/2026-11-07-decide-card-1746-broker-placement.md`. It keeps cluster `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8` and assigns the staging probe to CARD-1751 and the enable to CARD-1752. Blocker CARD-1742 is resolved Done.

Proposed move: Done. Reason: The placement decision is master 462e3e3b4568a4076d14c3eb9571fe94abb298ea. Enabling ChannelBridge is CARD-1752.

### CARD-1776 Review — DONE

Acceptance: image credential helper and gitconfig, compose mount of the token directory and the push allow-list, no token env var, `ServerImagePushCredentialTests`, a `docs/agent-credentials.md` row, and the `/work/repos/<name>` layout including the cutover-day path table.

History: Code `19293e15`, Review `5b1ea772` (blocker B-1, missing git identity), Code `93dff2d9`, Review `cbf389dd`. Round 2 is CLEAN at `cef2481a855a317cfcb720a91e1f9a81513ffd19`, `reviewedSourceClean=true`, outcome Clean, guard batch 85/85. Advisories A-9 through A-12 were for a later slice. The identity allow-list (A-10) later landed as CARD-1787 (`1313960d2d54f86ca53d12e63a92129529f4d3ed`) and the refusal text is `docs/agent-credentials.md:425-435`. Report commit `23db36250615d8b1b75c81ea780db5674e3ff05d` on `feat/card-task-cbf389dd`.

Master credential slice, patch-identical to the reviewed commits: `841606834e214cc2fe948becdbcba6ba1a27b808`, `dff78584d636fa49eca37652f2c3f14a48002a92` (= `0f76e7b0`), `d6c63c922ee34106083f7fc7d7a0cd9954865b4a`, `4128a884a9ae5713a1708ae559b8f5ed5e9ec71e` (= `cef2481a`). Later rc repairs on master, after that review and with no new Review task on this card: `a8164de15908a343330575bb88a985bc5ad1b8da`, `6d3f025606637d81a3aa3143c4164df5caecd239`, `517fd96c258562f228206567f8f9be610bc58554`, `0b2d61750d96f183e8488c07fa270db3ad21e642`, `58890f0ab70f93867978757d9faf13f562988e4b`. Tests: `tests/Antiphon.Tests/Infrastructure/ServerImagePushCredentialTests.cs:22` refuses `GH_TOKEN` / `GITHUB_TOKEN` and pins the read-only mount (`:40-80`). The path procedure is `docs/agent-credentials.md:437-444`. The filled before/after table is a CARD-1744 window record; the review handed the slice to land with the live land still pending cutover.

Proposed move: Done. Reason: Review cbf389dd round 2 is clean at cef2481a855a317cfcb720a91e1f9a81513ffd19 and that patch is master 4128a884a9ae5713a1708ae559b8f5ed5e9ec71e. The path table is a CARD-1744 window record, so this card stops blocking that window.

### CARD-1778 Review — NOT DONE

Acceptance has two parts. The code part admits `/work/agents/<name>` if the sidecar needs it, and adds the topology B standing-agent paragraph. The operator part, in the window, records working directories, stops desktop sessions, PATCHes `/work/agents/<name>`, clears Herdr labels, and confirms each agent Running.

History: Plan `c748deca`, Code `c4eea940`, Review `fbed504a` (S1 clean, next land), Code `d68d1868`, Review `a2845b6c` CLEAN at `df1516b315860aec4cad872114ec4b6d4a0ef272`, `reviewedSourceClean=true`, then Review `304984ac` clean on the `c1778-trust` sweep prefix at `4f307f8c4d4853f2df95b444cfea23c20cbe33be` with `reviewedSourceClean=false` (build-slot runs, no checkpoint receipt). `fbed504a` was dispatched and settled Succeeded; its handoff is land S1.

Master: `31cbd977a11ca7ca791d63eeba5492ff5b4a3c61`, `619b29230aff343c927ac04f49920ddbf34d708d`, `1c83d2d97acd225da4cebed87217cc5a55e8a963`, `975403fe0d7679ddc080542c0baa99781647e939`, `2179d718cafd8abaa1900143094dc251d0df3fd8`, `de3c59991297c9d49d18bba0b7ad6d25ea270156` (patch-identical to `df1516b3`). The paragraph is `docs/docker-stack.md:1380`. The sweep-prefix fix subject on master is `2a8e5bc51adf89bfa3137888e8cc2adc29298738`; the matching why-missed note `d99e73afa00439fb08af79cb3a5b86f214bca0ff` is patch-identical to reviewed tip `4f307f8c`.

Missing witness: the operator re-home. That pass is plan step 3.5 and runs in the CARD-1744 window, which this card blocks.

Proposed move: no column change. Reason: Code and docs are on master (de3c59991297c9d49d18bba0b7ad6d25ea270156, docs/docker-stack.md:1380) and Review a2845b6c is clean, but the working-directory PATCH and Running check have no witness. Unblock CARD-1744 from this card, or the window step 3.5 cannot run while this card blocks it. Leave this card in Review until that pass is recorded.

### CARD-1406 Review — NOT DONE

Original acceptance: `docker-compose.server2-postgres.yml`, `scripts/deploy-server2-postgres.ps1` (`-WhatIf` default), `scripts/test-server2-postgres-compose.ps1`, the docker-stack sentence, and the operator scratch restore with timings. The card text says this card does not repoint AppHost and does not stop the desktop database. Later tasks on this card added the CPU pin.

History (newest first, all Succeeded): Review `ff5ff6a5`, Code `ae97c85e`, Code `27ed273a`, Code `92424f0e`, Investigate `61c8b8b6` dispatched, Review `afa5577c`, Code `9aeb262a`, Review `1c0cc933`, Plan `0540d663`, then the November 7 series Review `8195f9ce`, Code `d50dfc71`, Review `18fbae37` (blockers B-3/B-4), Code `edc17681`, Review `37d76938` (blockers B-1/B-2), Code `67727bbb`.

Reviews:

- `8195f9ce` round 3 clean at `800c968adbc23c72e1755495e22f78962b6f8d1f`, `reviewedSourceClean=false`. Handoff: operator drill, PCs, and the Unit lane still pending. That patch is master `2e43ac0f940b4d61ba8e91da130b536275d20b52`.
- `1c0cc933` plan review r1 CLEAN. File on master: `docs/superpowers/reviews/2026-11-18-card-1406-cpu-isolation-plan-review-r1.md` (`7605f590287606149f0f9b437c4dbbc91691186e`).
- `afa5577c` Final clean at `0f13b84917d2ef2317f6025814e3b46a3c6c2645`, `reviewedSourceClean=true`. V-4 stays operator-pending. File: `docs/superpowers/reviews/2026-11-18-card-1406-cpu-isolation-code-review-r1.md`. That patch is master `1cffbb8ad62ff1bb0359cc0767675549f8448c57` (the three-core pin, later superseded).
- `ff5ff6a5` behavior-clean at `18628faa77148d53ff6cd5ef7328d14d046f579d`, outcome Clean, `reviewedSourceClean=false` because the runs were build-slot leases without a checkpoint receipt. File: `docs/superpowers/reviews/2026-11-18-card-1406-cpu-layout-review.md`. The layout commit `31871abd` is patch-identical to master `317aa9e6088db4b6dd3a13b822cf9fd290736c6f`.

Drill PASS and the WhatIf GO are the notes in the open-items list. Original compose commits on master: `ecc08e75b0688062aae3b9d55050e65ec17bb0e4`, `0853f0266ba1ce65522ecdc4ff22f91eab5584cd`, `8ea3df2522b615895fbf1bfeb9fd41240009ce8f`, `2f188806cea4a45f2f7cc09dc782ac8d1f58820c`, `057d141287a4a962ebd0405ed52e1b8cdc1c0736`, `2e43ac0f`.

Missing: V-4 as specified (runner inspect, blkio 800, p95), and the live backup-schedule flip. The cluster deploy and the scratch drill are witnessed. The 17:28–17:37Z execute is UNOBSERVED here.

Proposed move: no column change. Reason: Drill PASS is 717b869288 and the one-core pin is master 317aa9e6088db4b6dd3a13b822cf9fd290736c6f, but V-4 still lacks the runner inspect, blkio weight, and p95 samples, and the snapshot schedules were left disabled pending the W-3 flip. Stay in Review until those two witnesses exist.

### CARD-1751 InProgress — NOT DONE

Acceptance: from the CARD-1742 staging container, metadata only, same cluster id and advertised host `100.93.77.126:19092`. No produce, no join, bridge stays false.

History: relationship to CARD-1742, then Code `e6a8b2c6` dispatched 11/09 12:11Z. That task is Canceled, completed 11/09 12:17Z, next decide. Its handoff says the runner only had the nested daemon, project `antiphon-server2-staging` was absent, and `127.0.0.1:17420` refused. `git log --grep=card-1751 -i` on master is empty. `-S CARD-1751` hits only later docs (`462e3e3b`, `b1e129b4`, `d8306aa8`, `c3cc0bb9`, `7d5dc1e0`).

Proposed move: no column change. Reason: The staging metadata probe has no witness and master has no CARD-1751 commit. CARD-1742 is Done, so re-dispatch the probe on the server2 host. Stay InProgress.

### CARD-1752 Review — NOT DONE

Acceptance: after CARD-1744 stops the current bridge member and CARD-1751 sees the same cluster id, set bootstrap from an untracked env file, refuse on a cluster-id mismatch, one member of `antiphon-server-bridge`, `am-service` `/health/inbound-unconsumed` Ready, Digest stays false. Blocked by CARD-1744 (InProgress) and CARD-1751 (InProgress).

History: Plan `7cf27fe8`, Code `1ebd3976`, Review `a816c556`. Review is clean at `d3f34af4772dad2785037ffacec06e28568536dc`, `reviewedSourceClean=true`, outcome Clean. CP-1 27/27, guards plus sidecar 83/83. Handoff: do not enable the bridge. An unchanged env file renders `ChannelBridge__Enabled=false` and an empty bootstrap.

Master: `d8306aa8dd6284ca957896698e00ffd84ed04202`, `1024990b6974d58d5e7cd6e84aeebc4146344cbc`, `c59ec92326311d36cc91e5ec5426e19369127f9e` (patch-identical to `d3f34af4`).

Missing: the enable itself, which the acceptance and the clean review both withhold until the two blockers pass.

Proposed move: no column change. Reason: Review a816c556 is clean at d3f34af4772dad2785037ffacec06e28568536dc (master c59ec92326311d36cc91e5ec5426e19369127f9e) and the bridge stays off. Stay in Review until CARD-1744 and CARD-1751 pass and the enable witness exists.

### CARD-1744 InProgress — NOT DONE

Acceptance is the control-plane switch: one server owns the production database, phone-home origin, public vhost, deploy-script API default, data-protection key ring, and a single retention worker. The card text still says Herdr and Windows-platform tasks stay on the desktop. Plan D-9 says Herdr ends at cutover. That conflict is operator default O-3 in the caller authority (follow D-9 and update the card text). This investigation did not edit the card.

History: blockers added, Plan `2fafa091` Succeeded, next decide, then this Investigate. Open blockers from `card.ps1 get`: CARD-1406 Review, CARD-1776 Review, CARD-1778 Review. Resolved: CARD-1742, CARD-1743, CARD-1775, CARD-1777. It blocks CARD-1752.

Plan on master: `b1e129b44855a4c6682a3b1fecc8fb445be7e11a`, patch-identical to `8791a9707295c4e3d69115535f1b835e2722f50a`, file `docs/superpowers/plans/2026-11-08-card-1744-cutover-plan.md`. No other card-1744 subject is an implementing commit. `-S CARD-1744` is mostly later docs.

The server window has no witness in this checkout.

Proposed move: no column change. Reason: The cutover plan is master b1e129b44855a4c6682a3b1fecc8fb445be7e11a. The control-plane window has no witness, and CARD-1406 and CARD-1778 still have open acceptance. Stay InProgress. Update the Herdr sentence to plan D-9 when the card is next edited.

## Not done, noted

No fix. Closing CARD-1776, CARD-1768, CARD-1774, and CARD-1746 is a board move for the orchestrator. CARD-1778 stays a blocker until someone unblocks CARD-1744 or records the re-home.
