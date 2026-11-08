# Review c6ca6929: CARD-1788 round 1 (Final), subject b80e7169 @ a5aff46b35d551b798dac2bcc71738cabe926fdb

Verdict: CLEAN, no blockers. Base 57bbfc99d1810c99f2bf6e1a928427918c05d1fd (= origin/master).

## Checks
1. deploy-server2-server.ps1 -Execute: head-unknown / head-mismatch come right after revision-mismatch.
   They run before the lane check and before any docker call. The checked path is
   `git -C <compose dir> rev-parse --verify HEAD`, the compose dir is the `build.context: .` of all three
   services, the value is validated by Test-ServerSha, and the compare lower-cases both sides. Nothing
   secret is printed. The earlier refusals keep their order (the diff only inserts lines). The plan
   line no longer claims an image-label inspect, and -WhatIf still calls no docker.
2. server-state-init: server and sidecar both run as 1654:1654 and only init runs as root. The work
   volume is the project-owned `antiphon-server2-server_work`, and every earlier up ran `chown -R /work`.
   So nothing under /work/repos, /work/worktrees, /work/agents or /work/.antiphon can be root-owned
   except what init creates, and init now chowns those paths one level. chown -R on /state,
   /work/specialists and /runner-state keeps CARD-1775 B-1 (0755 + uid 1654). ForeignStateOwner is
   unchanged.
3. Script header and docs/docker-stack.md describe the change. No policy file changed (4 files total).
4. The 7 mutants M1-M7 were judged real by reading the code. Each one breaks a specific assertion
   (reason string, ShouldNotContain "image label", the recursive-under-/work/specialists check,
   created-path coverage, specialist env coverage, the uppercase expected-SHA case). The PCs were
   not run; they stay pending.

## Runs at a5aff46b3 (build-slot leases; native output; UseAppHost=false)
- Build of tests/Antiphon.Tests: lease 3b10295e, 0 errors.
- Contracts, lease af3c92b2: 139/139 passed, 0 skipped. Per the TRX: Server2ProductionServerContractTests 13,
  DockerStackContractTests 117, Server2StagingServerContractTests 9.
- Guards in their own process, lease 21a43643: 52/52 passed, 0 skipped. TestClassificationPolicyTests 41,
  TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3.
- check-evidence-diff base..a5aff46b3: commits=2 violations=0.
- merge-tree origin/master: clean, tree 95f5cb0e2443f909ab0c42facd611873db2f4095.

## Advisories (file as cards; do not hold the land)
- A-1: -Execute accepts a dirty build context, or untracked files, at the expected HEAD. up --build
  compiles the working tree, so the image can differ from the SHA. Code raised this as a decision.
- A-2: on the host, git can refuse the checkout as `dubious ownership` when the operator user does
  not own it. The deploy then fails closed as head-unknown with no hint about why.
- A-3: a relative -ComposeFile is resolved against the process cwd by GetFullPath and ReadAllText,
  but against the PS location by Test-Path. This pre-dates the card, and pwsh -File keeps the two the same.
- A-4: chown -R on /state and /runner-state also grows with session logs and agent homes (outside A-11).

## Not run
No checkpoint plan table and no receipts, so reviewedSourceClean=false. The whole Unit lane did not
run: the brief scoped Final/Full to the named classes plus the guards. PCs pending.
