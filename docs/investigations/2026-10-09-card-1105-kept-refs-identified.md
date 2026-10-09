# CARD-1105: the 13 kept refs, identified (2026-10-09)

Task a900731f (Debug). Read-only identification of the KEEP refs in
`docs/investigations/2026-10-09-card-1105-old-volume-cleanup.md`. Nothing was
written on volume `antiphon-runner_work`. No rescue ref was pushed.

## Outcome

All 13 KEEP refs, and both tips of `feat/card-task-3895bec1`, are SUPERSEDED.
They are finished CARD-0650 and CARD-0804/0805 work. CARD-0650, CARD-0804 and
CARD-0805 are Done. Current `origin/master` is
`c3e367cac532877761199865c2999f4af3546778` (fetched in this mirror on
2026-10-09). Deleting these refs loses no change that master lacks.

The only off-master commit that is not a patch is merge `52f5dd05`, which the
CARD-0650 S4 deploy already dropped while fast-forwarding master to `3700defd`.
The four commits whose patch-id differs from the same-subject commit on master
are the pre-rebase SHAs. Their replay is on master, and the land tree already
contains the one extra line (a JSON number guard in `BuildSlotClient`).

## Verdict

| Ref | tip8 | card | card status | classification | reason |
|---|---|---|---|---|---|
| `heads/feat/card-task-193a4a79`, `origin/feat/card-task-193a4a79` | `14dbe111` | CARD-0650 | Done | SUPERSEDED | S4 repair 5 tip. Every non-empty patch-id matches master. Equivalent replay is `3700defd` (same subject and author time). Card land is `826b2482`. |
| `heads/feat/card-task-1d94da9e`, `origin/feat/card-task-1d94da9e` | `14dbe111` | CARD-0650 | Done | SUPERSEDED | Same tip as `193a4a79`. Code task canceled on this empty verification commit. |
| `heads/feat/card-task-97ea55ef`, `origin/feat/card-task-97ea55ef` | `ce4cce1e` | CARD-0650 | Done | SUPERSEDED | Ancestor of `14dbe111`. Same dropped merge. Review of S4 repair 4. |
| `heads/feat/card-task-d24e1b4d`, `origin/feat/card-task-d24e1b4d` | `ce4cce1e` | CARD-0650 | Done | SUPERSEDED | Same tip as `97ea55ef`. Code task canceled. |
| `heads/feat/card-task-d9b74136`, `origin/feat/card-task-d9b74136` | `bac3ddeb` | CARD-0804 | Done | SUPERSEDED | Pre-rebase Linux review tip. Four patch-id mismatches are the pre-rebase SHAs whose twins are on master. Land is `6bf159cf`. |
| `origin/feat/card-task-5638a6b5` | `bac3ddeb` | CARD-0804 | Done | SUPERSEDED | Same tip. No local `heads/` ref. Code fix round. |
| `origin/feat/card-task-2db75657`, `origin/feat/card-task-ba64b974` | `5475ddee` | CARD-0804 | Done | SUPERSEDED | Later pre-rebase tip (Windows repair plus the inventory split). Contains `1dcc03c0`. Replayed onto land `6bf159cf`. |
| `heads/feat/card-task-3895bec1` | `1dcc03c0` | CARD-0804 | Done | SUPERSEDED | Local head. 27 commits off master, contained in `5475ddee`. Origin's branch of this name is `03603d9d`, not this tip. |
| `origin/feat/card-task-3895bec1` | `03603d9d` | CARD-0804 | Done | SUPERSEDED | Reviewed source that landed. 30 patch-id matches and 1 empty commit. Same subject and author time as land `6bf159cf`. |

CARD-0805 is Done on the same land (`6bf159cf`). It has no ref of its own in this set.

`heads/` is `refs/heads/`. `origin/` is `refs/remotes/origin/` on the old volume.
There is no `refs/heads` ref for `5638a6b5`, `2db75657` or `ba64b974`.

## Method

The host throwaway `docker run --rm` of `antiphon-server2/session-testing:4358939ecd85`
with the volume mounted `:ro` was not available from this session. This mirror
runs inside server2-temp. Nested dockerd cannot see host volumes or that image,
and there is no host docker socket and no SSH login to the host. The volume was
not mounted, so there is no mountinfo line and no write probe against a mount.

The substitute was `debugfs -n` (read-only, no `-w`) on the live root block
device `/dev/dm-0`, which already holds
`/var/lib/docker/volumes/antiphon-runner_work/_data`. The filesystem was not
mounted a second time. Packed-refs were read; no loose refs exist for these
names. No fetch, gc, checkout, reset, clean, stash, ref update, or delete ran
on the volume.

Comparison used this mirror after `git fetch origin master`. `git cherry` with
`GIT_NO_LAZY_FETCH=1` cannot read promisor blobs here, so each off-master
commit was compared by subject on `origin/master` and then by `git patch-id
--stable` of the two patches. This session rechecked all 73 unique pairs:
69 SAME still match, 4 DIFF still differ, 0 mismatches of the recorded class.
Empty commits have the same tree as their parent (9 checked).

Origin advertises `refs/heads/feat/card-task-3895bec1` at `03603d9d` and
`refs/heads/master` at `c3e367cac`. It does not advertise the other eight
branch names.

The cleanup note's replay line that the local `3895bec1` divergence was one
commit describes an earlier snapshot. Packed-refs now are local
`1dcc03c0a596dbd1fa857ee93293c6dfbf05828a` and origin
`03603d9d88808e798e2495d628f8a621deb33da1`. `git rev-list --count <tip> --not
origin/master` is 22, 19, 21, 29, 27 and 31 for `14dbe111`, `ce4cce1e`,
`bac3ddeb`, `5475ddee`, `1dcc03c0` and `03603d9d`. Commits reachable from
`03603d9d` and not from `1dcc03c0` number 389, because the rebase base contains
master history the old tip does not. The off-master set on `03603d9d` is still
31, and every one of those is patch-equivalent or empty.

## Owning tasks

| Task | Role | Status | Completed | Card | Title |
|---|---|---|---|---|---|
| `193a4a79-0a1e-4f50-963d-7e2234ff44ac` | Deploy | Succeeded | 2026-09-25T00:37:36Z | CARD-0650 | S4 manual land (server2) |
| `1d94da9e-1291-43f1-b734-aa3fed7f4667` | Code | Canceled | 2026-09-25T00:13:58Z | CARD-0650 | S4 repair 5 (two-snapshot empty) |
| `97ea55ef-de3f-4a37-8c5a-fdea19a5a0bd` | Review | Succeeded | 2026-09-24T23:50:42Z | CARD-0650 | S4 repair 4 review (server2) |
| `d24e1b4d-d267-4043-80ee-551f351c7ea1` | Code | Canceled | 2026-09-24T23:28:59Z | CARD-0650 | S4 repair 4 (unreadable composer, route auth) |
| `d9b74136-89b8-49ed-bccf-20f3e155f754` | Review | Succeeded | 2026-09-29T21:08:42Z | CARD-0804 | final review Linux |
| `5638a6b5-f8b8-4fe7-8572-c69613e10de5` | Code | Succeeded | 2026-09-29T18:57:21Z | CARD-0804 | fix round |
| `2db75657-e638-410f-a271-ed0be026c4e8` | Code | Succeeded | 2026-09-30T05:45:19Z | CARD-0804 | Windows review repair |
| `ba64b974-dfe1-4c9a-b5c3-fdab89272312` | Review | Succeeded | 2026-09-30T06:04:14Z | CARD-0804 | Linux Final Review |
| `3895bec1-9fb2-414d-9a57-500002213e4f` | Code | Succeeded | 2026-09-29T23:04:26Z | CARD-0804 | fix Windows review defects |

Card verdicts:

- CARD-0650 Done 2026-09-29T11:14:44Z. Landed 2026-09-29T11:11:32Z at
  `826b2482f9fd882a0aa7cfdd4da3d852148e5ef0`. Final review clean. The close
  leaves post-land SourceLanding mutation and post-publication acceptance to
  the caller. `3700defd` is an ancestor of `826b2482`, which is an ancestor of
  current master. `3700defd` has the same author time and subject as tip
  `14dbe111`.
- CARD-0804 Done 2026-09-30T23:17:47Z. Landed at `6bf159cf` (owner `3895bec1`,
  source `03603d9d`). The conflict resolver rebased onto master `7ceaa5db` and
  added `067b5725` (TimeoutTests) and `03603d9d` (census 261 to 272). Linux
  Final Review 324cba42 clean. Windows Final Review 3bd63ed1 clean under
  operator option A. The close says null-tolerant lease handling kept master's
  version at the rebase.
- CARD-0805 Done 2026-09-30T23:17:49Z. Shipped with CARD-0804 on the same land.

Deploy `193a4a79` records the rebase of `14dbe111` onto then-master, dropping
merge `52f5dd05`, with the replayed tip fast-forwarded to `3700defd`.

## The merge and the four patch-id mismatches

Merge `52f5dd05a2909aac94f346d5988a6cd816997a8b` (2026-09-24T22:56:42Z),
parents `0c36c681` and `d5115157`, subject "merge origin/master into CARD-0650
S4 repair 4: take CARD-0658 OperatorCredential and the test factory's own
operator token path". First-parent diff is 105 files, +12977 -417. That is
then-master being joined in. The S4 rebase dropped it. It is on both
`14dbe111` and `ce4cce1e` and not on current master.

The four pre-rebase commits are reachable from `bac3ddeb`, `5475ddee` and
`1dcc03c0`. `range-diff` of each single commit against its same-subject twin:

- `f10c7293` (2026-09-29T09:13:01Z) "CARD-0804/0805 add checkpoint temp
  ownership and cleanup custody" versus `55214977`. Both sides are 34 files,
  +1678 -164, same per-file numstat (the new cleanup types under
  `tools/Antiphon.Checkpoints/Cleanup/`, `scripts/inspect-checkpoint-temp.ps1`
  +72, and the checkpoint tests). The range-diff is context in
  `RunSchedulerTests.cs`, `TimeoutTests.cs` and `WaitCommandTests.cs` after
  master moved those tests, including a `serialAll` parameter on the landed
  `Schedule` helper.
- `9352ebf8` (2026-09-29T10:15:46Z) "Add Windows native cases and measured usage
  acceptance drivers" versus `ca876f92`. Both sides are 11 files, +884 -3
  (`docs/testing-and-build.md` +33, `scripts/checkpoint-orphan-owner.ps1` +38,
  `scripts/verify-checkpoint-orphan-usage.ps1` +226,
  `scripts/verify-checkpoint-temp-usage.ps1` +254,
  `CheckpointRecoveryWindowsTests.cs` +303, and six smaller files). The
  range-diff is one context line in `ReportWriter.cs`.
- `1546dc44` (2026-09-29T13:18:58+01:00) "Tolerate null numeric fields in
  build-slot lease response; include inner exception in executor crash reason"
  versus `9729844d`. Kept numstat is `CheckpointApp.cs` +2 -1 and
  `Slots/BuildSlotClient.cs` +1 -1. The twin is only `CheckpointApp.cs` +2 -1.
  The kept patch adds `JsonValueKind.Number` before `TryGetInt32` in
  `ReadInt`. That guard is absent on the kept commit's parent and already
  present on the twin's parent, and it is present in land `6bf159cf`. The
  replay kept the base's copy of the guard, which is what the card close
  records.
- `d0630bde` (2026-09-29T18:45:52+01:00) "Fix Review 953ac836 D1-D4: fail-closed
  Windows sampler, stable boot id, real-code usage tests" versus `087e5860`.
  Both sides are 9 files, +589 -238 (`scripts/lib/checkpoint-usage.ps1` +217,
  `CheckpointTempUsageTests.cs` +194 -45, `ProcessIdentity.cs` +38 -2, and six
  other docs and tests). The range-diff is one context line in
  `BuildSlotClientTests.cs`.

On `03603d9d` those four subjects are SAME against the twins above
(`fee0fed6`, `037245fa`, `a8dd03c7`, `ca96727b`). Later pre-rebase commits on
`5475ddee` (`68950fe5` deny-ACL, `5475ddee` inventory split) match
`f1661edd` and `e5bbfb76`.

A tree diff between a pre-rebase commit and its twin is the rebase-base
difference (hundreds of files) and is not unpublished work.

## Have we moved on

Yes. Both cards are Done, the S4 replay is `3700defd` on master, and the
CARD-0804/0805 reviewed source `03603d9d` is land `6bf159cf` on master.
Nothing in this ref set is an open card or a behavior master lacks.

## Rescue

A rescue is not required to preserve work. If the operator wants an archive
before deleting the refs, one bundle of the four covering tips measured
378,577 bytes in this mirror. The heads packed were temporary local names for
`14dbe111`, `bac3ddeb`, `5475ddee` and `03603d9d`. Prerequisites recorded by
the bundle were `822a0d16`, `d5115157`, `16da56da` and `7ceaa5db`. `ce4cce1e`
is an ancestor of `14dbe111`. `1dcc03c0` is an ancestor of `5475ddee`. The
parent of empty tip `bac3ddeb` is contained in `5475ddee`; the empty tip
itself is a separate commit with the same tree as `b01ef759`.

The same archive as origin branches, not created here:

```
git push origin 14dbe1110a590679e65eca40512e991e8dfbf37f:refs/heads/rescue/card-0650-14dbe111
git push origin 5475ddee667c1150a38186f6685d0e31820a54bb:refs/heads/rescue/card-0804-5475ddee
git push origin bac3ddeb988bb43ee26c4117aba2ef545bd8433c:refs/heads/rescue/card-0804-bac3ddeb
git push origin 03603d9d88808e798e2495d628f8a621deb33da1:refs/heads/rescue/card-0804-03603d9d
```

No off-master path matches `secret`, `credential`, `.env`, `token`, `id_rsa`,
`.pem`, `password`, or `api key`. The bundle file was deleted after measuring
it. It was not pushed.

## Commit index

Class SAME means the stable patch-id equals the twin. DIFF means it does not.
Per-file numstat of a SAME commit is the twin's `git show --numstat`. The four
DIFF commits are enumerated above; three of them have the same per-file
numstat as the twin, and `1546dc44` adds the one `BuildSlotClient.cs` line
already present at land. Shortstat below is the kept commit's own diff against
its first parent. `ce4cce1e` is the prefix of `14dbe111` through the empty
verification commit of that name. `1dcc03c0` is the prefix of `5475ddee`
through `1dcc03c0`. `bac3ddeb`'s non-tip commits are that same prefix through
`d0630bde`; its tip is a separate empty commit.

## Appendix

### `14dbe1110a590679e65eca40512e991e8dfbf37f`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| e952fd3a | 2026-09-24T17:52:04Z | SAME | bf9bfd59 | 6 files changed, 735 insertions(+) | test(CARD-0650): scaffold S4 direct delivery and receipt tests so the working-caller prompt is red (CP-7 pending) |
| fdc718c3 | 2026-09-24T17:57:32Z | SAME | bc065ae7 | 3 files changed, 391 insertions(+), 11 deletions(-) | feat(CARD-0650): S4 direct watchdog send, committed attempt and late receipt (WIP, CP-8/CP-9 pending) |
| e87f77e2 | 2026-09-24T17:58:17Z | SAME | 379d3b55 | 1 file changed, 2 insertions(+) | fix(CARD-0650): add the Dtos/Settings usings the S4 send partial needs (CP-8 build attempt 1 failed; rerun pending) |
| bc619b4a | 2026-09-24T18:01:44Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 at e87f77e2 on server2: CP-7 red 1/1, CP-8 green 12/12, CP-9 green 4/4 |
| 1d954efe | 2026-09-24T19:14:42Z | SAME | b8ff72dd | 3 files changed, 125 insertions(+), 5 deletions(-) | test(CARD-0650): S4 repair red-first tests for D1 submitted echo, D2 overlay refusal, D3 poll hold (CP-8 pending) |
| 816d8903 | 2026-09-24T19:15:42Z | SAME | f5e38d3c | 4 files changed, 67 insertions(+), 22 deletions(-) | fix(CARD-0650): S4 repair D1-D3: non-holding Submitted state, no-Escape overlay refusal, poll honours the hold (CP-8/CP-9 pending) |
| 2c85ee0a | 2026-09-24T19:20:20Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair at 816d8903 on server2: CP-8 green 14/14, CP-9 green 4/4 |
| f719f2e3 | 2026-09-24T20:58:20Z | SAME | 01076ec3 | 1 file changed, 64 insertions(+), 11 deletions(-) | test(CARD-0650): S4 repair 2 red-first tests: hold by verdict evidence (CP-8 pending) |
| 8e88bfd3 | 2026-09-24T20:59:39Z | SAME | 5a0829ee | 3 files changed, 48 insertions(+), 23 deletions(-) | fix(CARD-0650): S4 repair 2: the composer hold follows each verdict's evidence (CP-8/CP-9 pending) |
| 33c7f59c | 2026-09-24T21:01:01Z | SAME | ffdaa807 | 1 file changed, 7 insertions(+), 4 deletions(-) | fix(CARD-0650): make the S4 repair 2 hold compile: look up the standing nudge id, not a nullable tuple (CP-8 build attempt 1 failed with CS0037; rebuild pending) |
| dd76c862 | 2026-09-24T21:06:32Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 2 at 33c7f59c on server2: CP-8 green 15/15, CP-9 green 4/4 |
| e85ac851 | 2026-09-24T22:57:02+01:00 | SAME | 0706d47c | 3 files changed, 171 insertions(+), 3 deletions(-) | test(CARD-0650): S4 repair 3 red-first tests: composer-region hold and audited operator release (CP-8 pending) |
| d1bb2613 | 2026-09-24T23:00:47+01:00 | SAME | e8cc6762 | 9 files changed, 376 insertions(+), 22 deletions(-) | fix(CARD-0650): S4 repair 3: read Claude's composer region for the hold; audited operator release (CP-8/CP-9 pending) |
| e145fafd | 2026-09-24T23:07:28+01:00 | SAME | d6cd9355 | 1 file changed, 17 insertions(+), 10 deletions(-) | test(CARD-0650): S4 repair 3: drop only the watchdog prompt's record in the echo tests (CP-8 run 1: 17/18) |
| 0c36c681 | 2026-09-24T23:14:46+01:00 | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 3 at e145fafd on Windows desktop: CP-8 green 18/18, CP-9 green 4/4 |
| 52f5dd05 | 2026-09-24T22:56:42Z | MERGE | - | 105 files changed, 12977 insertions(+), 417 deletions(-) | merge origin/master into CARD-0650 S4 repair 4: take CARD-0658 OperatorCredential and the test factory's own operator token path |
| b04cd498 | 2026-09-24T23:05:45Z | SAME | 2fc1093f | 4 files changed, 442 insertions(+), 15 deletions(-) | test(CARD-0650): S4 repair 4 red-first tests: unreadable Claude composer holds; operator-token release route (CP-8 pending) |
| a43e48d1 | 2026-09-24T23:08:34Z | SAME | 58cf21c7 | 9 files changed, 172 insertions(+), 67 deletions(-) | fix(CARD-0650): S4 repair 4: an unreadable Claude composer holds; the hold release needs the operator token (CP-8/CP-9 pending) |
| ce4cce1e | 2026-09-24T23:21:30Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 4 at a43e48d1 on server2: CP-8 19/19, CP-9 4/4, route class 4/4 |
| 1d783074 | 2026-09-24T23:55:18Z | SAME | b684627e | 2 files changed, 90 insertions(+), 1 deletion(-) | test(CARD-0650): S4 repair 5 red-first test: a ghost empty composer on one snapshot holds while the next shows the body |
| df6a2547 | 2026-09-24T23:58:39Z | SAME | 1b160777 | 4 files changed, 47 insertions(+), 3 deletions(-) | fix(CARD-0650): S4 repair 5: an empty Claude composer releases the hold only when two snapshots read empty (CP-8/CP-9 pending) |
| 14dbe111 | 2026-09-25T00:07:58Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 5 at df6a2547 on server2: CP-8 20/20, CP-9 4/4, route class 4/4, ClaudeScreenTests 9/9 |

### `ce4cce1e8c03f6a769506eaa44a185d289a12b44`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| e952fd3a | 2026-09-24T17:52:04Z | SAME | bf9bfd59 | 6 files changed, 735 insertions(+) | test(CARD-0650): scaffold S4 direct delivery and receipt tests so the working-caller prompt is red (CP-7 pending) |
| fdc718c3 | 2026-09-24T17:57:32Z | SAME | bc065ae7 | 3 files changed, 391 insertions(+), 11 deletions(-) | feat(CARD-0650): S4 direct watchdog send, committed attempt and late receipt (WIP, CP-8/CP-9 pending) |
| e87f77e2 | 2026-09-24T17:58:17Z | SAME | 379d3b55 | 1 file changed, 2 insertions(+) | fix(CARD-0650): add the Dtos/Settings usings the S4 send partial needs (CP-8 build attempt 1 failed; rerun pending) |
| bc619b4a | 2026-09-24T18:01:44Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 at e87f77e2 on server2: CP-7 red 1/1, CP-8 green 12/12, CP-9 green 4/4 |
| 1d954efe | 2026-09-24T19:14:42Z | SAME | b8ff72dd | 3 files changed, 125 insertions(+), 5 deletions(-) | test(CARD-0650): S4 repair red-first tests for D1 submitted echo, D2 overlay refusal, D3 poll hold (CP-8 pending) |
| 816d8903 | 2026-09-24T19:15:42Z | SAME | f5e38d3c | 4 files changed, 67 insertions(+), 22 deletions(-) | fix(CARD-0650): S4 repair D1-D3: non-holding Submitted state, no-Escape overlay refusal, poll honours the hold (CP-8/CP-9 pending) |
| 2c85ee0a | 2026-09-24T19:20:20Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair at 816d8903 on server2: CP-8 green 14/14, CP-9 green 4/4 |
| f719f2e3 | 2026-09-24T20:58:20Z | SAME | 01076ec3 | 1 file changed, 64 insertions(+), 11 deletions(-) | test(CARD-0650): S4 repair 2 red-first tests: hold by verdict evidence (CP-8 pending) |
| 8e88bfd3 | 2026-09-24T20:59:39Z | SAME | 5a0829ee | 3 files changed, 48 insertions(+), 23 deletions(-) | fix(CARD-0650): S4 repair 2: the composer hold follows each verdict's evidence (CP-8/CP-9 pending) |
| 33c7f59c | 2026-09-24T21:01:01Z | SAME | ffdaa807 | 1 file changed, 7 insertions(+), 4 deletions(-) | fix(CARD-0650): make the S4 repair 2 hold compile: look up the standing nudge id, not a nullable tuple (CP-8 build attempt 1 failed with CS0037; rebuild pending) |
| dd76c862 | 2026-09-24T21:06:32Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 2 at 33c7f59c on server2: CP-8 green 15/15, CP-9 green 4/4 |
| e85ac851 | 2026-09-24T22:57:02+01:00 | SAME | 0706d47c | 3 files changed, 171 insertions(+), 3 deletions(-) | test(CARD-0650): S4 repair 3 red-first tests: composer-region hold and audited operator release (CP-8 pending) |
| d1bb2613 | 2026-09-24T23:00:47+01:00 | SAME | e8cc6762 | 9 files changed, 376 insertions(+), 22 deletions(-) | fix(CARD-0650): S4 repair 3: read Claude's composer region for the hold; audited operator release (CP-8/CP-9 pending) |
| e145fafd | 2026-09-24T23:07:28+01:00 | SAME | d6cd9355 | 1 file changed, 17 insertions(+), 10 deletions(-) | test(CARD-0650): S4 repair 3: drop only the watchdog prompt's record in the echo tests (CP-8 run 1: 17/18) |
| 0c36c681 | 2026-09-24T23:14:46+01:00 | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 3 at e145fafd on Windows desktop: CP-8 green 18/18, CP-9 green 4/4 |
| 52f5dd05 | 2026-09-24T22:56:42Z | MERGE | - | 105 files changed, 12977 insertions(+), 417 deletions(-) | merge origin/master into CARD-0650 S4 repair 4: take CARD-0658 OperatorCredential and the test factory's own operator token path |
| b04cd498 | 2026-09-24T23:05:45Z | SAME | 2fc1093f | 4 files changed, 442 insertions(+), 15 deletions(-) | test(CARD-0650): S4 repair 4 red-first tests: unreadable Claude composer holds; operator-token release route (CP-8 pending) |
| a43e48d1 | 2026-09-24T23:08:34Z | SAME | 58cf21c7 | 9 files changed, 172 insertions(+), 67 deletions(-) | fix(CARD-0650): S4 repair 4: an unreadable Claude composer holds; the hold release needs the operator token (CP-8/CP-9 pending) |
| ce4cce1e | 2026-09-24T23:21:30Z | EMPTY | - | empty tree | test(CARD-0650): verify S4 repair 4 at a43e48d1 on server2: CP-8 19/19, CP-9 4/4, route class 4/4 |

### `bac3ddeb988bb43ee26c4117aba2ef545bd8433c`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| f4d86c88 | 2026-09-29T08:08:56Z | SAME | 901b1db2 | 1 file changed, 27 insertions(+) | docs: investigate CARD-0805 checkpoint temp root leak |
| 48dd480d | 2026-09-29T08:17:55Z | SAME | fd139357 | 1 file changed, 360 insertions(+) | docs: plan shared CARD-0804/0805 checkpoint temp lifecycle fix |
| a8e6a3be | 2026-09-29T08:45:22Z | SAME | e271cf1e | 1 file changed, 504 insertions(+) | docs: design CARD-0804/0805 checkpoint lifecycle verification |
| f10c7293 | 2026-09-29T09:13:01Z | DIFF | 55214977 | 34 files changed, 1678 insertions(+), 164 deletions(-) | CARD-0804/0805 add checkpoint temp ownership and cleanup custody |
| c733fa65 | 2026-09-29T09:16:04Z | SAME | 854e28a1 | 1 file changed, 1 insertion(+), 1 deletion(-) | CARD-0804/0805 fix ownership test predicate |
| 0ff1b694 | 2026-09-29T09:20:51Z | SAME | e43554b4 | 1 file changed, 3 insertions(+), 2 deletions(-) | CARD-0804/0805 wait for shared root index admission |
| bfa051c2 | 2026-09-29T09:33:02Z | SAME | 63393641 | 7 files changed, 648 insertions(+), 41 deletions(-) | CARD-0804/0805 cover checkpoint launch and tool-copy recovery |
| bb657d59 | 2026-09-29T09:42:18Z | SAME | ef8dbf62 | 7 files changed, 365 insertions(+), 4 deletions(-) | Add bounded sweep tests and usage event instrumentation |
| 056534cb | 2026-09-29T09:48:56Z | SAME | 170f07be | 7 files changed, 203 insertions(+), 19 deletions(-) | Stage lifecycle and filter hosts for real teardown tests |
| c643bf10 | 2026-09-29T09:54:25Z | SAME | d4dd3f45 | 3 files changed, 306 insertions(+) | Exercise Linux native checkpoint custody and sweep paths |
| 0ef8d7fc | 2026-09-29T10:02:01Z | SAME | 0d680b83 | 1 file changed, 10 insertions(+), 5 deletions(-) | Correct Linux native fixture ownership and link teardown |
| 9352ebf8 | 2026-09-29T10:15:46Z | DIFF | ca876f92 | 11 files changed, 884 insertions(+), 3 deletions(-) | Add Windows native cases and measured usage acceptance drivers |
| 9ce1fbc5 | 2026-09-29T10:35:32Z | SAME | b6c2ec3d | 7 files changed, 56 insertions(+), 7 deletions(-) | Use stable Linux process generation and seal measured rosters |
| 2bb71b99 | 2026-09-29T10:47:46Z | SAME | cc35274b | 1 file changed, 17 insertions(+), 3 deletions(-) | Serialize cross-process checkpoint usage events |
| 0227d7a2 | 2026-09-29T10:52:26Z | SAME | 376c8610 | 1 file changed, 6 insertions(+), 1 deletion(-) | Record explicit fixture disposal in usage evidence |
| d4e11530 | 2026-09-29T11:57:45Z | SAME | 86ef9e87 | 1 file changed, 25 insertions(+), 7 deletions(-) | Strengthen orphan acceptance custody and inventory checks |
| 1546dc44 | 2026-09-29T13:18:58+01:00 | DIFF | 9729844d | 2 files changed, 3 insertions(+), 2 deletions(-) | Tolerate null numeric fields in build-slot lease response; include inner exception in executor crash reason |
| 55ab8d4c | 2026-09-29T13:21:40+01:00 | SAME | 663e8c9f | 3 files changed, 43 insertions(+), 4 deletions(-) | Fall back to junctions when symlink privilege is missing in checkpoint link tests |
| f7fb8870 | 2026-09-29T13:25:57+01:00 | SAME | 1e062418 | 1 file changed, 48 insertions(+) | Add native Windows allocated-byte sampling to checkpoint usage acceptance |
| d0630bde | 2026-09-29T18:45:52+01:00 | DIFF | 087e5860 | 9 files changed, 589 insertions(+), 238 deletions(-) | Fix Review 953ac836 D1-D4: fail-closed Windows sampler, stable boot id, real-code usage tests |
| bac3ddeb | 2026-09-29T19:56:39+01:00 | EMPTY | - | empty tree | Record Windows checkpoint results for Review 953ac836 fix round (d0630bde) |

### `5475ddee667c1150a38186f6685d0e31820a54bb`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| f4d86c88 | 2026-09-29T08:08:56Z | SAME | 901b1db2 | 1 file changed, 27 insertions(+) | docs: investigate CARD-0805 checkpoint temp root leak |
| 48dd480d | 2026-09-29T08:17:55Z | SAME | fd139357 | 1 file changed, 360 insertions(+) | docs: plan shared CARD-0804/0805 checkpoint temp lifecycle fix |
| a8e6a3be | 2026-09-29T08:45:22Z | SAME | e271cf1e | 1 file changed, 504 insertions(+) | docs: design CARD-0804/0805 checkpoint lifecycle verification |
| f10c7293 | 2026-09-29T09:13:01Z | DIFF | 55214977 | 34 files changed, 1678 insertions(+), 164 deletions(-) | CARD-0804/0805 add checkpoint temp ownership and cleanup custody |
| c733fa65 | 2026-09-29T09:16:04Z | SAME | 854e28a1 | 1 file changed, 1 insertion(+), 1 deletion(-) | CARD-0804/0805 fix ownership test predicate |
| 0ff1b694 | 2026-09-29T09:20:51Z | SAME | e43554b4 | 1 file changed, 3 insertions(+), 2 deletions(-) | CARD-0804/0805 wait for shared root index admission |
| bfa051c2 | 2026-09-29T09:33:02Z | SAME | 63393641 | 7 files changed, 648 insertions(+), 41 deletions(-) | CARD-0804/0805 cover checkpoint launch and tool-copy recovery |
| bb657d59 | 2026-09-29T09:42:18Z | SAME | ef8dbf62 | 7 files changed, 365 insertions(+), 4 deletions(-) | Add bounded sweep tests and usage event instrumentation |
| 056534cb | 2026-09-29T09:48:56Z | SAME | 170f07be | 7 files changed, 203 insertions(+), 19 deletions(-) | Stage lifecycle and filter hosts for real teardown tests |
| c643bf10 | 2026-09-29T09:54:25Z | SAME | d4dd3f45 | 3 files changed, 306 insertions(+) | Exercise Linux native checkpoint custody and sweep paths |
| 0ef8d7fc | 2026-09-29T10:02:01Z | SAME | 0d680b83 | 1 file changed, 10 insertions(+), 5 deletions(-) | Correct Linux native fixture ownership and link teardown |
| 9352ebf8 | 2026-09-29T10:15:46Z | DIFF | ca876f92 | 11 files changed, 884 insertions(+), 3 deletions(-) | Add Windows native cases and measured usage acceptance drivers |
| 9ce1fbc5 | 2026-09-29T10:35:32Z | SAME | b6c2ec3d | 7 files changed, 56 insertions(+), 7 deletions(-) | Use stable Linux process generation and seal measured rosters |
| 2bb71b99 | 2026-09-29T10:47:46Z | SAME | cc35274b | 1 file changed, 17 insertions(+), 3 deletions(-) | Serialize cross-process checkpoint usage events |
| 0227d7a2 | 2026-09-29T10:52:26Z | SAME | 376c8610 | 1 file changed, 6 insertions(+), 1 deletion(-) | Record explicit fixture disposal in usage evidence |
| d4e11530 | 2026-09-29T11:57:45Z | SAME | 86ef9e87 | 1 file changed, 25 insertions(+), 7 deletions(-) | Strengthen orphan acceptance custody and inventory checks |
| 1546dc44 | 2026-09-29T13:18:58+01:00 | DIFF | 9729844d | 2 files changed, 3 insertions(+), 2 deletions(-) | Tolerate null numeric fields in build-slot lease response; include inner exception in executor crash reason |
| 55ab8d4c | 2026-09-29T13:21:40+01:00 | SAME | 663e8c9f | 3 files changed, 43 insertions(+), 4 deletions(-) | Fall back to junctions when symlink privilege is missing in checkpoint link tests |
| f7fb8870 | 2026-09-29T13:25:57+01:00 | SAME | 1e062418 | 1 file changed, 48 insertions(+) | Add native Windows allocated-byte sampling to checkpoint usage acceptance |
| d0630bde | 2026-09-29T18:45:52+01:00 | DIFF | 087e5860 | 9 files changed, 589 insertions(+), 238 deletions(-) | Fix Review 953ac836 D1-D4: fail-closed Windows sampler, stable boot id, real-code usage tests |
| b01ef759 | 2026-09-29T19:56:39+01:00 | EMPTY | - | empty tree | Record Windows checkpoint results for Review 953ac836 fix round (d0630bde) |
| aeef27a8 | 2026-09-29T21:30:00Z | SAME | dbf9f67e | 6 files changed, 170 insertions(+), 56 deletions(-) | Fix checkpoint temp registration and sweep index lifecycle |
| a40327dc | 2026-09-29T21:37:11Z | SAME | 0826b90d | 1 file changed, 1 insertion(+), 1 deletion(-) | Fix concurrent allocation assertion for Shouldly expression |
| 1b1b9d2e | 2026-09-29T21:39:26Z | SAME | 3eda7784 | 1 file changed, 7 insertions(+), 2 deletions(-) | Preserve invalid-marker receipts during legacy index migration |
| d6d6d053 | 2026-09-29T21:44:35Z | SAME | d291290c | 1 file changed, 18 insertions(+), 24 deletions(-) | Pin repair checkpoint rows to serial single-row execution |
| efb06340 | 2026-09-29T21:59:42Z | SAME | 75200fb4 | 2 files changed, 33 insertions(+), 5 deletions(-) | Retry transient vanished-child Linux usage samples |
| 1dcc03c0 | 2026-09-29T22:54:22Z | SAME | 05bbd702 | 5 files changed, 125 insertions(+), 26 deletions(-) | Fix checkpoint temp Windows review defects |
| 68950fe5 | 2026-09-30T06:38:23+01:00 | SAME | ec059b8b | 5 files changed, 212 insertions(+), 36 deletions(-) | Fix Review 272701b6 checkpoint defects: deny-ACL cleanup, derived census, explicit roster |
| 5475ddee | 2026-09-30T06:40:07+01:00 | SAME | e575cc79 | 2 files changed, 25 insertions(+), 16 deletions(-) | Split compiled checkpoint inventory out of the LifecycleHost-linked hook file |

### `1dcc03c0a596dbd1fa857ee93293c6dfbf05828a`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| f4d86c88 | 2026-09-29T08:08:56Z | SAME | 901b1db2 | 1 file changed, 27 insertions(+) | docs: investigate CARD-0805 checkpoint temp root leak |
| 48dd480d | 2026-09-29T08:17:55Z | SAME | fd139357 | 1 file changed, 360 insertions(+) | docs: plan shared CARD-0804/0805 checkpoint temp lifecycle fix |
| a8e6a3be | 2026-09-29T08:45:22Z | SAME | e271cf1e | 1 file changed, 504 insertions(+) | docs: design CARD-0804/0805 checkpoint lifecycle verification |
| f10c7293 | 2026-09-29T09:13:01Z | DIFF | 55214977 | 34 files changed, 1678 insertions(+), 164 deletions(-) | CARD-0804/0805 add checkpoint temp ownership and cleanup custody |
| c733fa65 | 2026-09-29T09:16:04Z | SAME | 854e28a1 | 1 file changed, 1 insertion(+), 1 deletion(-) | CARD-0804/0805 fix ownership test predicate |
| 0ff1b694 | 2026-09-29T09:20:51Z | SAME | e43554b4 | 1 file changed, 3 insertions(+), 2 deletions(-) | CARD-0804/0805 wait for shared root index admission |
| bfa051c2 | 2026-09-29T09:33:02Z | SAME | 63393641 | 7 files changed, 648 insertions(+), 41 deletions(-) | CARD-0804/0805 cover checkpoint launch and tool-copy recovery |
| bb657d59 | 2026-09-29T09:42:18Z | SAME | ef8dbf62 | 7 files changed, 365 insertions(+), 4 deletions(-) | Add bounded sweep tests and usage event instrumentation |
| 056534cb | 2026-09-29T09:48:56Z | SAME | 170f07be | 7 files changed, 203 insertions(+), 19 deletions(-) | Stage lifecycle and filter hosts for real teardown tests |
| c643bf10 | 2026-09-29T09:54:25Z | SAME | d4dd3f45 | 3 files changed, 306 insertions(+) | Exercise Linux native checkpoint custody and sweep paths |
| 0ef8d7fc | 2026-09-29T10:02:01Z | SAME | 0d680b83 | 1 file changed, 10 insertions(+), 5 deletions(-) | Correct Linux native fixture ownership and link teardown |
| 9352ebf8 | 2026-09-29T10:15:46Z | DIFF | ca876f92 | 11 files changed, 884 insertions(+), 3 deletions(-) | Add Windows native cases and measured usage acceptance drivers |
| 9ce1fbc5 | 2026-09-29T10:35:32Z | SAME | b6c2ec3d | 7 files changed, 56 insertions(+), 7 deletions(-) | Use stable Linux process generation and seal measured rosters |
| 2bb71b99 | 2026-09-29T10:47:46Z | SAME | cc35274b | 1 file changed, 17 insertions(+), 3 deletions(-) | Serialize cross-process checkpoint usage events |
| 0227d7a2 | 2026-09-29T10:52:26Z | SAME | 376c8610 | 1 file changed, 6 insertions(+), 1 deletion(-) | Record explicit fixture disposal in usage evidence |
| d4e11530 | 2026-09-29T11:57:45Z | SAME | 86ef9e87 | 1 file changed, 25 insertions(+), 7 deletions(-) | Strengthen orphan acceptance custody and inventory checks |
| 1546dc44 | 2026-09-29T13:18:58+01:00 | DIFF | 9729844d | 2 files changed, 3 insertions(+), 2 deletions(-) | Tolerate null numeric fields in build-slot lease response; include inner exception in executor crash reason |
| 55ab8d4c | 2026-09-29T13:21:40+01:00 | SAME | 663e8c9f | 3 files changed, 43 insertions(+), 4 deletions(-) | Fall back to junctions when symlink privilege is missing in checkpoint link tests |
| f7fb8870 | 2026-09-29T13:25:57+01:00 | SAME | 1e062418 | 1 file changed, 48 insertions(+) | Add native Windows allocated-byte sampling to checkpoint usage acceptance |
| d0630bde | 2026-09-29T18:45:52+01:00 | DIFF | 087e5860 | 9 files changed, 589 insertions(+), 238 deletions(-) | Fix Review 953ac836 D1-D4: fail-closed Windows sampler, stable boot id, real-code usage tests |
| b01ef759 | 2026-09-29T19:56:39+01:00 | EMPTY | - | empty tree | Record Windows checkpoint results for Review 953ac836 fix round (d0630bde) |
| aeef27a8 | 2026-09-29T21:30:00Z | SAME | dbf9f67e | 6 files changed, 170 insertions(+), 56 deletions(-) | Fix checkpoint temp registration and sweep index lifecycle |
| a40327dc | 2026-09-29T21:37:11Z | SAME | 0826b90d | 1 file changed, 1 insertion(+), 1 deletion(-) | Fix concurrent allocation assertion for Shouldly expression |
| 1b1b9d2e | 2026-09-29T21:39:26Z | SAME | 3eda7784 | 1 file changed, 7 insertions(+), 2 deletions(-) | Preserve invalid-marker receipts during legacy index migration |
| d6d6d053 | 2026-09-29T21:44:35Z | SAME | d291290c | 1 file changed, 18 insertions(+), 24 deletions(-) | Pin repair checkpoint rows to serial single-row execution |
| efb06340 | 2026-09-29T21:59:42Z | SAME | 75200fb4 | 2 files changed, 33 insertions(+), 5 deletions(-) | Retry transient vanished-child Linux usage samples |
| 1dcc03c0 | 2026-09-29T22:54:22Z | SAME | 05bbd702 | 5 files changed, 125 insertions(+), 26 deletions(-) | Fix checkpoint temp Windows review defects |

### `03603d9d88808e798e2495d628f8a621deb33da1`

| sha8 | author date | class | twin | shortstat | subject |
|---|---|---|---|---|---|
| 3ea478ea | 2026-09-29T08:08:56Z | SAME | 901b1db2 | 1 file changed, 27 insertions(+) | docs: investigate CARD-0805 checkpoint temp root leak |
| d47214e0 | 2026-09-29T08:17:55Z | SAME | fd139357 | 1 file changed, 360 insertions(+) | docs: plan shared CARD-0804/0805 checkpoint temp lifecycle fix |
| 73b1c41e | 2026-09-29T08:45:22Z | SAME | e271cf1e | 1 file changed, 504 insertions(+) | docs: design CARD-0804/0805 checkpoint lifecycle verification |
| fee0fed6 | 2026-09-29T09:13:01Z | SAME | 55214977 | 34 files changed, 1678 insertions(+), 164 deletions(-) | CARD-0804/0805 add checkpoint temp ownership and cleanup custody |
| 3b394c1a | 2026-09-29T09:16:04Z | SAME | 854e28a1 | 1 file changed, 1 insertion(+), 1 deletion(-) | CARD-0804/0805 fix ownership test predicate |
| 44c16c37 | 2026-09-29T09:20:51Z | SAME | e43554b4 | 1 file changed, 3 insertions(+), 2 deletions(-) | CARD-0804/0805 wait for shared root index admission |
| 85dcdd33 | 2026-09-29T09:33:02Z | SAME | 63393641 | 7 files changed, 648 insertions(+), 41 deletions(-) | CARD-0804/0805 cover checkpoint launch and tool-copy recovery |
| bb3164ab | 2026-09-29T09:42:18Z | SAME | ef8dbf62 | 7 files changed, 365 insertions(+), 4 deletions(-) | Add bounded sweep tests and usage event instrumentation |
| ca84ed99 | 2026-09-29T09:48:56Z | SAME | 170f07be | 7 files changed, 203 insertions(+), 19 deletions(-) | Stage lifecycle and filter hosts for real teardown tests |
| b8e19845 | 2026-09-29T09:54:25Z | SAME | d4dd3f45 | 3 files changed, 306 insertions(+) | Exercise Linux native checkpoint custody and sweep paths |
| ff100b1b | 2026-09-29T10:02:01Z | SAME | 0d680b83 | 1 file changed, 10 insertions(+), 5 deletions(-) | Correct Linux native fixture ownership and link teardown |
| 037245fa | 2026-09-29T10:15:46Z | SAME | ca876f92 | 11 files changed, 884 insertions(+), 3 deletions(-) | Add Windows native cases and measured usage acceptance drivers |
| 8079b91c | 2026-09-29T10:35:32Z | SAME | b6c2ec3d | 7 files changed, 56 insertions(+), 7 deletions(-) | Use stable Linux process generation and seal measured rosters |
| 1a728ea5 | 2026-09-29T10:47:46Z | SAME | cc35274b | 1 file changed, 17 insertions(+), 3 deletions(-) | Serialize cross-process checkpoint usage events |
| 081f01be | 2026-09-29T10:52:26Z | SAME | 376c8610 | 1 file changed, 6 insertions(+), 1 deletion(-) | Record explicit fixture disposal in usage evidence |
| d7eff790 | 2026-09-29T11:57:45Z | SAME | 86ef9e87 | 1 file changed, 25 insertions(+), 7 deletions(-) | Strengthen orphan acceptance custody and inventory checks |
| a8dd03c7 | 2026-09-29T13:18:58+01:00 | SAME | 9729844d | 1 file changed, 2 insertions(+), 1 deletion(-) | Tolerate null numeric fields in build-slot lease response; include inner exception in executor crash reason |
| a88c3117 | 2026-09-29T13:21:40+01:00 | SAME | 663e8c9f | 3 files changed, 43 insertions(+), 4 deletions(-) | Fall back to junctions when symlink privilege is missing in checkpoint link tests |
| 71232f4b | 2026-09-29T13:25:57+01:00 | SAME | 1e062418 | 1 file changed, 48 insertions(+) | Add native Windows allocated-byte sampling to checkpoint usage acceptance |
| ca96727b | 2026-09-29T18:45:52+01:00 | SAME | 087e5860 | 9 files changed, 589 insertions(+), 238 deletions(-) | Fix Review 953ac836 D1-D4: fail-closed Windows sampler, stable boot id, real-code usage tests |
| ed5f9b63 | 2026-09-29T19:56:39+01:00 | EMPTY | - | empty tree | Record Windows checkpoint results for Review 953ac836 fix round (d0630bde) |
| 0e075083 | 2026-09-29T21:30:00Z | SAME | dbf9f67e | 6 files changed, 170 insertions(+), 56 deletions(-) | Fix checkpoint temp registration and sweep index lifecycle |
| 77befaaa | 2026-09-29T21:37:11Z | SAME | 0826b90d | 1 file changed, 1 insertion(+), 1 deletion(-) | Fix concurrent allocation assertion for Shouldly expression |
| 2fe2cef8 | 2026-09-29T21:39:26Z | SAME | 3eda7784 | 1 file changed, 7 insertions(+), 2 deletions(-) | Preserve invalid-marker receipts during legacy index migration |
| 16e25f7d | 2026-09-29T21:44:35Z | SAME | d291290c | 1 file changed, 18 insertions(+), 24 deletions(-) | Pin repair checkpoint rows to serial single-row execution |
| 5b678568 | 2026-09-29T21:59:42Z | SAME | 75200fb4 | 2 files changed, 33 insertions(+), 5 deletions(-) | Retry transient vanished-child Linux usage samples |
| 77097771 | 2026-09-29T22:54:22Z | SAME | 05bbd702 | 5 files changed, 125 insertions(+), 26 deletions(-) | Fix checkpoint temp Windows review defects |
| f1661edd | 2026-09-30T06:38:23+01:00 | SAME | ec059b8b | 5 files changed, 212 insertions(+), 36 deletions(-) | Fix Review 272701b6 checkpoint defects: deny-ACL cleanup, derived census, explicit roster |
| e5bbfb76 | 2026-09-30T06:40:07+01:00 | SAME | e575cc79 | 2 files changed, 25 insertions(+), 16 deletions(-) | Split compiled checkpoint inventory out of the LifecycleHost-linked hook file |
| 067b5725 | 2026-09-30T18:37:38+01:00 | SAME | ee7111ad | 1 file changed, 2 insertions(+), 2 deletions(-) | Use owned TempDir in master's new TimeoutTests total-deadline case after rebase |
| 03603d9d | 2026-09-30T18:40:37+01:00 | SAME | 6bf159cf | 2 files changed, 8 insertions(+), 5 deletions(-) | Move checkpoint namespace census to 272 after rebase onto master 7ceaa5db |

