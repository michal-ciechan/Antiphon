# server2 /work reclaim log (2026-11-08, task ed0f7942)

Manual removal of finished, clean, published task mirrors (brief step 2). Each row was re-verified
immediately before removal: task status Succeeded/Failed/Canceled (GET /api/agent-tasks/<id8>, the record
delegate.ps1 -Status reads), `git ls-remote origin refs/heads/<branch>` == mirror HEAD, `status --porcelain
--untracked-files=all` and `stash list` empty, any `.antiphon/task-<id8>.md` present in HEAD, not a live task,
no process cwd inside. Removed with `git -C /work/repos/antiphon worktree remove <exact path>` (no --force).

| Task | Status | HEAD (== origin) | MB | Method |
|---|---|---|---:|---|
| 36e2134b | Succeeded | ca497565e813392b47c1d159833c1eff91acc822 | 227 | worktree-remove |
| 27123eab | Succeeded | 88e9fc746c78503ef84c3f183bb93ea5ab713e75 | 246 | worktree-remove |
| 52890135 | Canceled | 388ff482046a62f813a251208bfeabe7c21808ea | 227 | worktree-remove |
| a8945a17 | Succeeded | b028d5e66228f65ec4944cfd75c39a72f7d84c9d | 227 | worktree-remove |
| a3a1bbee | Succeeded | 6aa60088f29119662b7d7aa971f8aeecf2f6bc57 | 228 | worktree-remove |
| 4aff2d15 | Succeeded | 388ff482046a62f813a251208bfeabe7c21808ea | 227 | worktree-remove |
| c993f042 | Succeeded | 88e9fc746c78503ef84c3f183bb93ea5ab713e75 | 226 | worktree-remove |
| 6532e78e | Succeeded | 212c916bbd97d94ef844442ec8422ee2e62562b6 | 227 | worktree-remove |
| 5ae98d31 | Succeeded | c318d8edfc97e046bb9a332ec34c76973f9a4f4a | 228 | worktree-remove |
| 8f14e2c9 | Succeeded | 7ff770297cdfb7ebb0743a6cc55fa41417baf13a | 232 | worktree-remove |
| cdf5415a | Succeeded | 66a7cbe5e8f21ee39d6baa03f3fd8dcbac237d32 | 226 | worktree-remove |
| 2b5414eb | Succeeded | e51d45b7c4678be10912b72b4f74b4bd13ac2b04 | 228 | worktree-remove |
<!-- batch 1 after: 4264M free -->
| 50f25440 | Succeeded | e0536f7a245affe2a2925cd9e442830bf0d61511 | 227 | worktree-remove |
| f822e84d | Succeeded | c9d9d7a631d923aa904a4daf1dde5ebd70ed73fb | 242 | worktree-remove |
| 38ec4219 | Succeeded | eb51c80ad6d070a592d16f017e2fc5f485ae8191 | 227 | worktree-remove |
| d8f70e4c | Succeeded | 1d15909de96d07fb10fa66b0381131226c56a40c | 229 | worktree-remove |
| 9222cb00 | Succeeded | c9d9d7a631d923aa904a4daf1dde5ebd70ed73fb | 226 | worktree-remove |
| 55b96312 | Succeeded | d7a8098a37f7516bf7dc2908c1d6e73aa1cc906e | 230 | worktree-remove |
| 1f10d17d | Succeeded | 2ce36496456b5853e043fd4c3f7fd9b24b4d9a17 | 229 | worktree-remove |
| 973b4d1b | Succeeded | 507d9508d292a5a5ecbf784dfd71959a51291c32 | 230 | worktree-remove |
| 65e6f8b0 | Succeeded | 8c54e76accc25928e503ac2081b2b5d558bd6663 | 229 | worktree-remove |
| b8770621 | Succeeded | 11cdf6ecbeb42ce7b08b4771943192395572507d | 229 | worktree-remove |
| 53da6f14 | Succeeded | a11b5e0d1548e6dd0429d6ffac9ed35b41eda413 | 229 | worktree-remove |
| 935ca9be | Succeeded | 3dcd5965b4ec15bf9773b066b197846bc3da42e6 | 252 | worktree-remove |
<!-- batch 2 after: 4565M free -->
<!-- from batch 3: largest first among finished mirrors completed >= 12 h ago (oldest-first freed ~230 MB/mirror, below the fill rate) -->
| cf0ad24d | Succeeded | 1e7e24e65dbc7260942b250e3e2c82137be40783 | 3599 | worktree-remove |
| 90ac18a9 | Succeeded | 74a8004f405bc54666fea33889839f0ff4d62ec7 | 3597 | worktree-remove |
| e6423203 | Succeeded | 44b02ee05b116967c6fbf764f6ce0d273ab54862 | 3596 | worktree-remove |
| ac04d0ad | Succeeded | c2c9a153c464dc84714bcefb30f1a540d99c5432 | 3483 | worktree-remove |
| 5a2fffb9 | Canceled | 6d5531762633dbb9077739cb451386c6c2131255 | 3334 | worktree-remove |
| df5a54fd | Succeeded | 8b75f315467f7b25c021ec1c0299c04e7398c0b8 | 3278 | worktree-remove |
| 73b64ea8 | Succeeded | f336c3ced190461de84adaab22a60642b09baeec | 3278 | worktree-remove |
| 51d7da79 | Succeeded | 99f3d328cfe1aaaaf211278f24e43b9d11b61438 | 3277 | worktree-remove |
| 7f9d46dc | Succeeded | 6d5531762633dbb9077739cb451386c6c2131255 | 3233 | worktree-remove |
| f2ba309e | Succeeded | edb2c20156afa4da503b1b29fb83f1f953987bd5 | 3232 | worktree-remove |
| 20805524 | Succeeded | c7216a06f55fba7dc255702c99217248e1b4a20e | 3232 | worktree-remove |
| 0738eb51 | Succeeded | 1394d6e3db90d12ec38de564e7042c1d4603ee2c | 3232 | worktree-remove |
| fd0ebdc3 | Succeeded | c34cd706a4bfcb5dc73d022ac2ebe80f7f038f97 | 2929 | worktree-remove |
| f1f7bd39 | Succeeded | 91c703532826ca742ee9932ab1850a1839468137 | 2312 | worktree-remove |
| aa1cb08e | Succeeded | 86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3 | 2099 | worktree-remove |
<!-- batch 3 after: 52062M free -->
| a9a521c5 | Succeeded | 68aca958a611f59c928aa9c7982004e732fac058 | 2055 | worktree-remove |
| 67112dd1 | Succeeded | b8fe734030a7a2d08f4a45d090326ccd576b9fe7 | 2054 | worktree-remove |
| 4cbff6f1 | Succeeded | 692ce256854afee6998ead87fde2407c4e406a57 | 2054 | worktree-remove |
| d59bc8e8 | Succeeded | 7d3a9763f2242aa23c420e2a5f7671ba763627d1 | 1997 | worktree-remove |
| f0960466 | Succeeded | ae3c8b88a7f56cafec3eab91f2e80995b7af7454 | 1996 | worktree-remove |
| 64cd7d3a | Succeeded | ae3c8b88a7f56cafec3eab91f2e80995b7af7454 | 1996 | worktree-remove |
| 63cae064 | Succeeded | 7bd3261cfefa50308cc2daabb64b454a00aeadeb | 1996 | worktree-remove |
| b1ceb641 | Succeeded | 5acde1e2b7ad10791cabea5732f6b9a22cc84506 | 1975 | worktree-remove |
| 3d2eb2ca | Succeeded | 4c70130c4f6bed1da146385e04f3921a9cbfdd7c | 1975 | worktree-remove |
| 8195f9ce | Succeeded | 457b9db2d1c758095296e5e520e35db7f5b23d13 | 1974 | worktree-remove |
| f668441c | Succeeded | 61a663b368262e05b4a936cfedcc6bb0d6a94881 | 1973 | worktree-remove |
| f31c70a6 | Succeeded | 68e8ccc4b27005102c27c4ec1905a17ee3924a3a | 1973 | worktree-remove |
| d823935e | Succeeded | da3c1fa8ae9c957e9f7d5bd2241831cac2892253 | 1973 | worktree-remove |
| 5c57130c | Succeeded | 5d5973be2cd867bf592cd0363240e728f64d3b28 | 1973 | worktree-remove |
| 2b6ad831 | Failed | 5d5973be2cd867bf592cd0363240e728f64d3b28 | 1973 | worktree-remove |
| 02644a04 | Succeeded | 13fc155b2c268ddb45acb2202235d8dda2cfdce2 | 1973 | worktree-remove |
<!-- batch 4 after: 84206M free -->

## Summary

- Free on /work: 1.8 GB at start (15:1x UTC, `df -h`), 84.0 GB after batch 4 (`df -BM` 84019M). The 80 GB stop was reached.
- Removed: 55 mirrors, 85,149 MB by `du -sm` taken just before each removal. All 55 `git worktree remove` calls succeeded without `--force`. No `rm -rf` was needed, and nothing outside `/work/worktrees/task-<id8>` was touched (/tmp, /state, Docker untouched; TempSweep not enabled).
- Ordering: batches 1-2 went oldest first. The oldest mirrors were about 230 MB each, so two batches freed about 5 GB while the volume kept filling. Batches 3-4 went largest first, but only among finished mirrors completed at least 12 h earlier, the retention job's own `MinTerminalHours`. Every row still passed all the per-mirror checks.
- Skipped (not deleted): 25 mirrors.

| Task | Reason | HEAD |
|---|---|---|
| 34125850 | origin!=HEAD(feat/card-task-34125850) | 72a8014a92 |
| 7626aee3 | report-not-in-branch | 7ef63d7ad9 |
| f8ac5aa5 | report-not-in-branch | ff85fc977a |
| 29e42131 | report-not-in-branch | 88ed031df1 |
| 013b9ff7 | report-not-in-branch | 253a86bc39 |
| 340f1d0a | report-not-in-branch | cc4ab8e313 |
| 82cf72c1 | report-not-in-branch | 9813bdb2c3 |
| 51b0c438 | report-not-in-branch | ba1b7371a3 |
| e4cb0636 | report-not-in-branch | 2fcbd3ab52 |
| 1c9ceb47 | report-not-in-branch | b4ed0f5824 |
| d69fe301 | report-not-in-branch | 5cb4c4abd0 |
| 420b5c90 | report-not-in-branch | e64c5cd801 |
| b8eac553 | report-not-in-branch | 73fe0aa291 |
| d8bdfd3a | report-not-in-branch | 5cb4c4abd0 |
| 07ab1e49 | report-not-in-branch | 13e9c1c8b0 |
| 543d8c5b | report-not-in-branch | 02932305f5 |
| 6552d9b1 | report-not-in-branch | 74a8004f40 |
| 4f5166ec | report-not-in-branch | b6052173f4 |
| dc68779f | report-not-in-branch | 602c37029f |
| 26d7bf3f | report-not-in-branch | 1516f551cb |
| 9edde7f9 | report-not-in-branch | 956736c959 |
| 47538c88 | report-not-in-branch | 8278a7205e |
| 3d0b1390 | report-not-in-branch | 825296b0ed |
| 2a819c07 | report-not-in-branch | f93d98e00d |
| edc17681 | report-not-in-branch | 1cdb72fb8c |

`report-not-in-branch` means `.antiphon/task-<id8>.md` exists in the mirror but is not in the branch HEAD (the brief's skip rule). These 24 mirrors are otherwise clean, and their branches equal origin. 34125850: HEAD 72a8014a92 is not on its task branch (origin is at 1470340559, and the two have diverged). It is contained in `origin/salvage/stale-merge-34125850-reports` and is clean. It was left untouched.
- Dirty or unpublished terminal mirrors found: none, apart from the diverged 34125850 above. ea539697 returned HTTP 502 in the census status read, so it was never considered.

## Retention job observation

A DryRun preview at 15:19 UTC (`POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview`, run `8f1f4338-4d7d-4317-a72b-98fad51fe47e`) gave examined 2664 and eligible 2241, with `sent` 25. Its 449 rows break down as follows:
Removed/already_absent 25, all for mirrors no longer on disk, so 0 bytes. Held settling 153 present and 15 absent (the 15 are this pass's removals). Held sync_debt 6 present and 209 absent. Held mutation_excluded 27 absent. Held outside_scope 5 absent. Held live_owner 8 present. Inventory identity_unknown 1.
No present, settled mirror older than 24 h got a row at all. The whole `MaxRemovalsPerTick=25` budget again went to historical tasks whose mirrors are already gone. This is the same defect the c04ce26d note measured. The scheduled tick reclaims nothing until `already_absent` stops consuming the send budget, or until `sync_debt`/`settling` held rows for absent mirrors stop dominating the population.
There is no execute or run route: ops-http and the API map list only `/preview` and `GET runs/{runId}`, and no per-task release exists for runner mirrors.
