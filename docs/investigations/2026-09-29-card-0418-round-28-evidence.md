# CARD-0418 round 28: competing admission and conversion leases

This continues the [round-27 evidence](2026-09-29-card-0418-round-27-evidence.md).
The full-sweep code commit is `142f879c02218536dfb11d1d9c50e15b56244ae7`.
The final test-oracle commit is `0826f131e007ddec3cd20aac63f94c8a7dca8517`.
No shared-stack restart, actual destination, land, or SourceLanding Mutation was used.

## V-9 ordinary acceptance

`ChannelOutboundDeliveryTests.Two_dispatchers_preserve_source_window_and_destination_identity_under_admission_race`
uses two independent EF connections and facade instances. The first dispatcher is
held after its insert but before commit; the second races the same source/window/
destination and cannot return before the first commits. A fresh connection sees
no partial intent while held. Both return Deferred, one intent survives, and
neither publishes at admission. The same source task id then produces distinct
keys for another destination and changes to prompt sequence, first or last text
sequence, and send kind. X and Y have separate input snapshots. A real pump
creates X's task, validates a PDF output, and publishes X with its original
Markdown plus sealed PDF. The same pump later creates Y's task; its failure
publishes Y's original Markdown with the fallback notice and no X PDF. Each
destination has one broker acceptance, one distinct conversion outcome, and no
cross-target attachment bytes.

`Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner`
executes two barriers with independent pump/EF instances. At both a pause
immediately before task creation and a pause after task commit, the second pump
cannot claim the unexpired lease. Six minutes of fake time permit takeover. At
the pre-creation barrier the old owner creates **zero** tasks, the new owner
creates one linked task, and no source reply is published before its result. At
the post-commit barrier the old owner created the one task; takeover reuses its
id, creates no replacement, and the stale owner cannot clear the new lease.
When the task fails, competing ticks publish the fallback exactly once and the
fresh database has one task and one Published intent. The explicit old-owner
creation counter is the oracle for the pre-creation fence; a total task count
alone would not distinguish which owner acted.

The production repair is a conditional version/lease/state update inside the
task creation transaction. It holds the PostgreSQL row lock until the linked
task and Converting state commit. A stale owner raises a concurrency exception
that the pump handles as a lost claim rather than converting into a fallback.
Direct runner fixtures now model claiming and releasing a lease. The prior
`Expired_lease_takeover_fences_the_old_owner_before_producer_invocation` still
checks the publish side: after another pump takes an expired Publishing lease,
the paused first owner invokes no producer and writes no success stamp.

Together these rows close **whole V-9** as ordinary isolated PostgreSQL and
fake-destination evidence. They do not claim an actual destination receipt.
R-6 gains the V-9 race oracles but remains open until V-15's complete crash
oracle matrix is named and checked.

## Checkpoint trail

The final full CP-1–CP-13 closed-list sweep ran at the code commit in
`.antiphon/checkpoints/20260929-144636-6ec5/`. It used granted host slots,
ran no unlisted row, and took 20m20s. Its only red row was CP-8's inherited
`codex_desktop_unqualified` T1/T2 pair. After the explicit stale-owner counter
was added, CP-5 passed 26/26 at the final test commit in
`.antiphon/checkpoints/20260929-150744-3205/`. The earlier CP-2/CP-5
fixture repair run passed 372/372 and 26/26 in
`.antiphon/checkpoints/20260929-143611-5b41/`. The separately leased
checkpoint-tool bootstrap build passed with its existing CS8602 warning.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 372 | 372 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 26 | 26 | 0 | 0 | Green; final-oracle rerun 26/26 |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 refusal only |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The full-sweep CP-8 failures are
`PinnedAgentKindTests.T1_a_task_pinned_to_a_stopped_standing_Codex_agent_with_no_kind_stores_Codex`
and `T2_an_explicit_kind_mismatch_is_refused_and_an_agreeing_kind_is_accepted`.
Both are the inherited `codex_desktop_unqualified` refusals from round 27;
this round did not change or chase them. All listed rows have fresh native
receipts in their checkpoint run directory.

## Remaining gates

Closed ordinary IDs are V-1–V-9, V-19–V-22, V-24, R-1 and R-12. The open
ordinary set is **V-10–V-18, V-23, R-2–R-11, R-13–R-14**. Use the
[round-17 assertion inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–28, for each missing matrix. Start next with V-10's
internal-purpose side-effect and privilege matrix, then V-11's refusal,
capacity and dispatch-deadline matrix. V-25 remains the later authorized
actual-destination gate. PC-1–PC-30 remain pending method-scoped SourceLanding
Mutation; no ordinary or nightly green discharges them. R-14 must index every
completed sub-assertion without treating one method as a whole broad ID.
