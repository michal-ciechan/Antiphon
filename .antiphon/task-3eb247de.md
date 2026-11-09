# CARD-1799 Review a89708b6 fold — Code 3eb247de (Final)

Branch `feat/card-task-3eb247de`, runner mirror `/work/worktrees/task-3eb247de`, base `f0d53b51d345ed51983cf818aa41c503221d780a` (owner Code 27eba224, landing owner). Plan extract (ignored): `.antiphon/c1799-code-3eb247de-plan.md` (Review a89708b6 extract with Min raised to 78/58/52 and output ids bin-c1799cf1/2).

## Commits
- red `8df732e4a58e26ddc1f35ced1c2948c88be5ee3e` test: witnesses for A-1/A-2/A-4/A-5.
- fix `3b831a19eca2b397211a3f18e46e6359d2affa3e` adapter, AttachmentSize, bridge note, docs/telegram.md.
- test repair `6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3`: the bridge witness carried raw U+2028/U+202E/U+200B in C# literals (U+2028 is a line terminator, CS1010), so Antiphon.Tests did not compile at 8df732e4a and 3b831a19e; rewritten as `\u` escapes, same assertions. Tested SHA = 6ecfdcc14.

## Folds
- **A-1 (fixed)** `TelegramChannelAdapter.NoticeOverLimitAsync`: `message_thread_id` only when `is_topic_message == true`; a failed send whose error contains "thread not found" is resent exactly once without the thread (`lastAttempt: true` both times); 429 and every other failure stay single-attempt.
- **A-2 (fixed)** dedupe key `<chat>:album:<media_group_id>` when the message has one (else `<chat>:<message_id>`), same 512-entry FIFO; the notice replies to the first over-cap item.
- **A-4 (fixed)** `AttachmentSize`: GiB unit, `1 byte`, new public `FormatOver(bytes, limit)` (PublicAPI.Unshipped) keeps one decimal when the rounded size equals the limit's text (`14.0 MiB` vs `14 MiB`); used by the notice and the bridge note.
- **A-5 (fixed)** bridge note name: Unicode Format (Cf) chars dropped, separators (incl. U+2028) to space, capped at 80 chars ending `…` (surrogate-safe cut).
- A-3, A-6..A-9 stay advisories (A-8's 429 single-attempt witness now exists: `Rate_limited_notice_is_a_single_attempt`).

## Red proof
- Messaging, at 8df732e4a, `build-slot -Label c1799-fold-red-messaging -- dotnet run --project tests/Antiphon.Messaging.Tests -p:UseAppHost=false -- --treenode-filter "/*/*/TelegramChannelAdapterTests/*"`: total 37 failed 9 succeeded 28 (the 9 = non-forum x2, resend-once, twice-stops, album, 14.0 MiB, Format 1 byte/3 GiB/2.5 GiB; the 429 witness green by design). Slot waited=0s.
- Bridge, at 6ecfdcc14 with `ChannelBridgeService.cs` temporarily restored to f0d53b51d (uncommitted, restored with `git checkout HEAD --`, tree clean after): `build-slot -Label c1799-fold-bridge-red -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false -- --treenode-filter "/*/Antiphon.Tests.Application/ChannelBridgeTests/(An_over_limit*)"`: total 3 failed 1 (`An_over_limit_note_caps_the_name_and_strips_format_characters`: expected 80-char name + `(14.0 MiB)`), slot waited=0s.
- Green after fix (unlisted, leased): same Messaging filter at 3b831a19e, 37/37.

## Checkpoint run (tool, tip 6ecfdcc14) run `20261108-234758-3036`

```
source: 6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 state=clean buildSource=verified
CHECKPOINT CP-1 commit=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 build=ok filter=/*/*/(TelegramChannelAdapterTests*)|(TelegramResilienceTests*)|(TelegramMarkdownRendererTests*)|(ContractSchemaTests*)|(WireContractTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-3eb247de/.antiphon/checkpoints/20261108-234758-3036/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 build=ok filter=/*/Antiphon.Tests.Application/(ChannelBridgeTests*)|(ChannelContractsTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/task-3eb247de/.antiphon/checkpoints/20261108-234758-3036/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/task-3eb247de/.antiphon/checkpoints/20261108-234758-3036/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 13m15s  sequential-equivalent: 3m48s  builds: 2  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-c1799cf1/, bin-c1799cf2/
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=6ecfdcc148cf03f2a77aa817c9c1bd25093b35c3 rows=3
```

All 13 new adapter cases (8 methods) and the new bridge method are Passed by name in the fresh TRX. Earlier runs: `20261108-233832-a2ba` at 3b831a19e RED exit 2 (CP-1 78/78 green; CP-2/CP-3 build failed on the CS1010 above; kept outputs deleted by literal path), `20261108-234616-9e7c` RED exit 2 (refused: pre-existing outputs from the first run, 0 executed).

Unlisted builds/runs (all leased, waited=0s): checkpoint tool Debug build; the two red runs and one green run above; the deploy-set build below. Not run: the whole Unit lane (brief named this selection; 45-minute budget) — deferred to Final, not passed.

## Deploy set standalone
`git archive 3b831a19e` of exactly the Dockerfile COPY set (`src/Messaging.Pack.props`, `Antiphon.Messaging`, `.Gateway`, `.Telegram`, `.Slack`, `.Service`) into the scratchpad, `build-slot -Label c1799-fold-deploy-set-build -- dotnet build .../Antiphon.Messaging.Service.csproj -c Release -p:UseAppHost=false -nodeReuse:false`: 0 warnings 0 errors (6ecfdcc14 changes only a test file).

## Gates
- `check-evidence-diff.ps1 -Repository . -BaseRef f0d53b51d… -HeadRef 3b831a19e…`: commits=2 entries=0 violations=0 exit 0 (re-run over the final tip below in the report commit message context).
- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master = cad7766950).
- No test-execution-policy, migration or OS-branch change.

## Pending for Mutation
PCs (method-scoped): forum gate (`IsTopicMessage` removed → non-forum witness red), resend removed / looped (resend-once and twice-stops witnesses), 429 retried (rate-limit witness), album key reverted (album witness), FormatOver collision branch (14.0 MiB witnesses, adapter and bridge), GiB/1 byte arms, Cf filter and 80-char cap (bridge witness). Plus Review a89708b6 A-8 rows still open (downloaded-bytes branch, FIFO eviction, at-cap boundary).

## Not done
am-service deploy: not performed; the brief orders it after this fold's Review round 2 (next: review), and the stage rules forbid next: deploy.
