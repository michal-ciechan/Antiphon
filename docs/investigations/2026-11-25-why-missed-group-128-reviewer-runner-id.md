# Why reviewer runner isolation first failed in Windows Full group 128

Windows Full run `acca11ba` on `rc/2026-12-04` at
`6ed4cb58cd75fe386e94965165d96752452431a8` reported one failure in `antiphon-all-group-128` for
`ReviewDelegationRunnerApiTests.The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node`.
The earlier investigation recorded the desktop assertion `request.RunnerId should be null but was "local"`.
This is the same fixture leak already repaired by Debug `80ec0ddf`.
[Original investigation](2026-11-24-why-missed-review-delegation-runner-id-local.md)

## Cause and first affected candidate

`DelegationReviewAgentRunner` maps `Review:Agents:Runner` to the runner on the
reviewer task request. An unset option leaves the request unpinned. The
integration fixture boots the real server in Development. At the failed pin
it inherited the server's `antiphon-server` user-secrets source. The desktop
setup script wrote `Review:Agents:Runner=local` to that store. The dispatch
used the configured override as designed.

The reviewer assertion entered in `7e378e861fe26d607530f416969d39bd25c889b6`
and the setup script began writing the override in
`9f9c90824fb447bca0984a6abb16002be1b31cef`, in the direct-pushed review API
and engine series. The test source and setup script are both absent from the
previous Windows Full pin, `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91`
(run `557cdb63`), and both are present at the failed pin. The previous run
therefore supplies no pass for this method. Its published TRX analysis lists
neither the method nor this failure among its failed and uncovered cases.
[Previous Full analysis](https://github.com/michal-ciechan/Antiphon/blob/10587f33e98e5d3ae97b2a3a90e9b532448bd114/docs/investigations/2026-11-23-rc-run-557cdb63-final-analysis.md)

## Why Linux and short Merge missed it

The direct-pushed series had no approving Review or Merge selection. Short
Merge builds once and executes each subject's named narrow witnesses. The
reviewer case is in the Integration lane at
`tests/Antiphon.Tests/Review/ReviewDelegationRunnerApiTests.cs`. A build or a
run of that case with server2's ordinary configuration leaves the review
option unset. The desktop Full run exercised the same code with the local
store present. The Linux reproduction confirms that a Windows-specific lane
or skip roster would target the wrong condition.
[Short Merge scope](../orchestration-loop.md#merge-verification-is-short-2026-11-18)

## Landed fix and evidence

Master already contains guard commit `3d6e457b0a841286ebdf2c8b5b076ba81e930fb1`
and fixture fix `fb0538b7609f7a2ef7b49eec2642d26c2bc04cb1`. The factory
removes the machine's user-secrets JSON source. The regression guard checks
that the source is absent even on a machine that has no secrets file and
that the review option stays unset.

- Linux red, carried from Debug `80ec0ddf`: checkpoint
  `20261124-101744-f01e`, CP-2, at
  `cd54d190d4b79eb5c5b01253a2a08810f017562d`. One case ran and failed at
  the same `RunnerId` assertion with a synthetic store under `APPDATA`.
- Linux green, carried from the same Debug: checkpoint
  `20261124-103041-6f93`, CP-2, at
  `c96ec53248e6f843823c6e77f55da0ad82467793`. One case passed with the
  same synthetic input. The original task reported source and build validation.
- Fresh Linux replay from `f8243cfd60bdb6a0fb406de9223425bb72e588a8`, the
  assigned master-based checkout. The build and test each held a host build
  slot. The exact Integration method passed with the synthetic store.
  The TRX recorded 1 executed, 1 passed, 0 failed, 0 skipped.

## Cheapest pre-land check

`tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs` →
`/*/*/ProductionRunnerIsolationTests/*`. This is the existing proposal
from the original investigation. The configuration-source guard fails with
or without an operator's store.

## Native confirmation

Run the exact reviewer method on the next Windows Full candidate containing
the master fix and guard. The existing Windows run reports and the two
candidate pins provide the historical comparison. The failing desktop log
is `C:\Antiphon\releases\candidates\rc-2026-12-04-6ed4cb58cd75\logs\2026-11-24-0922-acca11ba\antiphon-all-group-128-tests.log`.
It was unavailable from this worktree.
