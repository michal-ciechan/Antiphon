# CARD-1603 code report

Outcome: implemented. Closed checkpoint `20261029-175154-c9bd` is green, 261/261, at `63ed1c99ca406102291b91265e49cd6d730f02ff` on `feat/card-task-1d5d34a3`. Source was clean and `buildSource=verified`. `git merge-tree --write-tree origin/master HEAD` at that commit wrote `3c69253ab588a45b4efed010cb6205f94f00c482` with exit 0.

## What landed

A canceled or parked selected Channel/Scheduled row is detached before the capacity grant can be reused. The detach takes the session gate, then the message row `FOR UPDATE NOWAIT`, then the provider `pg_try_advisory_xact_lock`, and it rolls the whole transaction back on a busy lock, a failed recheck, or a commit fault (`Deferred`). Demand that is proven absent releases the grant and returns the wait to Ready with `queue-demand-lost`, keeping the action key, ordinal, selection linkage, admission count, revalidation flag, due time, and provider clock. A positively canceled or parked selection that is still attached is not newly granted.

An unfinished StandingStart wait with no execution receipt is superseded when a later human start owns the agent (`standing-consumer-replaced`). The action key is not rotated. Missing, malformed, or ambiguous ownership is abandoned. The replacement session does not have to still be Running.

## Commits

Base `e65343132e877ce20bd26540d440eff3867d9798`.

| SHA | Subject |
|---|---|
| `8cef7010cb8f9370159f1de7a2281f798471bf0d` | docs: add the CARD-1603 capacity-grant cycling plan |
| `51a7b5e4568142aa1c988910e9be9481d3fdc637` | test: witness CARD-1603 obsolete capacity grants before cleanup |
| `4c37465de8b369f2af64f2d7c687ce49729b6481` | test: keep CARD-1603 queue witnesses off a frozen delivery clock |
| `320de58f12ed830da89ec04a306237a79beffaa0` | fix: detach an obsolete queue selection before the capacity grant recycles (S1) |
| `2b1aa929a2edc28c3eca7d90457ee71a05f1eea9` | fix: supersede a standing capacity wait when a human start replaces it (S2) |
| `b600e3e21d3773f13b8923f7002fa5e4c48f7b5e` | docs: re-anchor CARD-1603 citations and correct S3 Risk 8 |
| `1eea423b7dac87670f67c04a065c09950afd0aad` | fix: take the obsolete capacity provider lock through EF |
| `a0f1b5e6d5665f48cdd5f4ef0ef4e41b6d55d3c4` | test: seed held queue demand before capacity grants |
| `4c4e483db2efdd2171e72e5481407e620787497d` | fix: roll back a failed obsolete-selection commit |
| `63ed1c99ca406102291b91265e49cd6d730f02ff` | test: keep the schedule grant and the commit fault on one pass |

Verified product tip is `63ed1c99ca406102291b91265e49cd6d730f02ff`. This report commit is docs-only and sits after that tip.

## Checkpoints

One build `tests/Antiphon.Tests -> bin-c1603/` (`UseAppHost=false`), serial, `TEMP/TMP/TMPDIR=/tmp`. Tool `dotnet exec tools/Antiphon.Checkpoints/bin-c1603-tool/Antiphon.Checkpoints.dll run .antiphon/c1603-checkpoints.yaml --expected-source-sha 63ed1c99ca406102291b91265e49cd6d730f02ff --total-timeout 90m --hard-wall-timeout 100m`. Slot wait 0. No unlisted build or test. Green run deleted owned `bin-c1603/`. Evidence: `.antiphon/checkpoints/20261029-175154-c9bd/report.md`.

| Row | Executed | Passed | Failed | Skipped | Row wall | Tests wall |
|---|---:|---:|---:|---:|---:|---:|
| CP-1 queue | 35 | 35 | 0 | 0 | 82.381s | 35.165s |
| CP-2 standing | 18 | 18 | 0 | 0 | 41.019s | 22.048s |
| CP-3 existing capacity | 47 | 47 | 0 | 0 | 63.039s | 42.608s |
| CP-4 supervision | 24 | 24 | 0 | 0 | 45.166s | 27.116s |
| CP-5 standing reservation | 52 | 52 | 0 | 0 | 68.090s | 46.197s |
| CP-6 acceptance/persistence/transcript | 28 | 28 | 0 | 0 | 49.574s | 29.203s |
| CP-7 admission lifecycle | 28 | 28 | 0 | 0 | 95.670s | 75.676s |
| CP-8 compatibility/locks | 29 | 29 | 0 | 0 | 89.576s | 67.421s |
| Total | 261 | 261 | 0 | 0 | | |

Build phase 255.235s. Run wall 14m52s. Sequential-equivalent 8m54s. Execution 891.930s. `CapacityAdmissionLifecycleTests` recorded 75s on CP-7 (existing slow class, 28 tests).

R1: the plan estimate (36–69 minutes of tests plus a 4–7 minute build) is over 30 minutes, so the timing above is the performance Debug record. The measured green wall is 14m52s. Earlier red runs do not count: `20261029-163014-e6a1` at `b600e3e21`, `20261029-170543-fb10` at `a0f1b5e6d` (CP-1 26/35, CP-2..CP-8 green), `20261029-173016-065f` at `4c4e483db` (259/261, CP-1 33/35).

Unit lane: not run. Result unknown. `tests/linux-test-roster.json` was not edited. Native and Windows tests were not run.

## Deviations

- Provider try-lock uses EF `SqlQueryRaw<bool>` (`1eea423b7`) so the lock stays on the cleanup transaction and the reader interceptor can see it. `GrantReady` still takes the blocking `pg_advisory_xact_lock`.
- LiveSession and PendingQueue waits with a session and no deliverable demand, receipt, confirmed prompt, redeemable task, or conservative selection are excluded before `Take`. Existing grant fixtures that had only a LiveSession owner were given a real Pending held Channel/Scheduled row (`a0f1b5e6d`). Counts and assertions stayed. The demand predicate was not bypassed.
- Round-3 A-3 stays the existing V-3 `message-busy` arm (7 results).
- Round-3 A-2 future-held selection is a seeded guard. Proven-absent demand on an admitted revalidation wait becomes Ready / `queue-demand-lost` and keeps `NeedsRevalidationGrant`.
- Round-3 A-4 parked V-11 arms call `ShowEmptyComposer()` before the flush that must select or stamp Held.
- A detach commit fault rolls back and returns Deferred (`4c4e483db`). The standing supersede commit stays on the outer lock-not-available path.
- Witness timing only: the competitor becomes due on the third 121s window; the schedule arm holds the live wait due 10 minutes so the cleanup assertion still sees Ready; the commit-fault boundary arms once (`4c4e483db`, `63ed1c99c`).
- The queue fixture clock is `OffsetClock` (real timers, offset utc). Citations were re-anchored to master `e65343132` and the S3 Risk 8 sentence was replaced (`b600e3e21`). The live card and generated `docs/cards/` were left unchanged.

## Release line and evidence

`rc/2026-10-27` is `8edda999623a473be2d1970727c172bfb5088d59`. That commit has no `CapacityRecoveryService.ObsoleteConsumers.cs`, and it has none of `ReconcileObsoleteConsumersAsync`, `queue-demand-lost`, `standing-consumer-replaced`, or `ConsumeAdmittedStartAsync`. S3-A is master-only. This fix stays on master. No rc rebase, tag, or publish.

`scripts/check-evidence-diff.ps1 -BaseRef e65343132e877ce20bd26540d440eff3867d9798 -HeadRef 63ed1c99ca406102291b91265e49cd6d730f02ff` exited 0: 10 commits, 0 violations. The same script against `origin/master` exited 2, `base_not_ancestor`, because master has moved past the fork.

## Next stage

```
--- next stage ---
next: review
handoff: Review feat/card-task-1d5d34a3. Product behavior verified at 63ed1c99ca406102291b91265e49cd6d730f02ff.
artifact: .antiphon/task-1d5d34a3.md
```
