# CARD-1932: the populated-tree custody witness judged transient PIDs

Date: 2026-11-25. Task bf619976 (Debug), branch `feat/card-task-bf619976`, base `bdca854ff`.
Lane: server2 runner mirror, Linux (Debian 12, cgroup v1 `pids` hierarchy), serial through
`scripts/build-slot.ps1`.

## Defect

`LinuxCgroupCustodyTests.C1614_Populated_tree_refuses_entry_and_drain_success` sampled the
execution's `cgroup.procs` once (`WaitForTreeAsync(atLeast: 1)`), ran the enter helper and a
one-second `custody_drain`, and then required
`members.All(pid => Directory.Exists("/proc/" + pid))`.

The payload's `HoldLine()` is a `sleep 0.1` loop. The shell spends nearly all its time waiting
on a `sleep` child, so the sample usually contains that child as well as the tracked root. The
child exits on its own within 100 ms, well inside the drain window, so the assertion fails
while the tree is still held by the same tracked root. This is a test defect: no product or
helper behaviour is involved. The installed helpers on this lane are byte-identical to
`docker/session-runner-grok/` (SHA-256 `aec68b78…` common, `e0aa13fd…` enter, `68d965da…` kill),
which agrees with the card's post-run inspection.

## Red witness (commit `513ec2e5956d7b3c64fcaee06b99255e7ceba6e1`)

The sample now waits for at least two members and requires the root among them. Two distinct
members are the root and a `sleep` child, so the sample always contains a transient. The old
assertion is otherwise unchanged.

Run: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1932-red -- dotnet run --project
tests/Antiphon.PtyHost.Tests --no-build -p:UseAppHost=false -- --treenode-filter
"/*/*/LinuxCgroupCustodyTests*/C1614_Populated_tree_refuses_entry_and_drain_success*" --report-trx`

Result: total 1, failed 1: `ShouldAssertException: alive should be True but was False`, the
card's exact failure. Load average at the end was 17.7.

## Fix (commit `d17286794f87d60aca7cafa22211d6666e26ea35`)

The fix stays inside the test method. The `CustodyWorld` fixture and every hunk the CARD-2060
branch edits are untouched.

- Each sampled member's identity is captured at sample time as its pid plus its kernel start
  ticks (`/proc/<pid>/stat` field 22). The tracked root must be live in the sample.
- After the entry and drain checks, the tracked root must have the same start ticks, so it is
  the same live process, not a reused pid. A process cannot come back, so equal identity
  before and after means the root was alive throughout the drain.
- A fresh `cgroup.procs` read after the drain must still contain the tracked root, so the
  owned tree is still populated by its root.
- `custody_drain` must return exactly `1` ("still populated after its bound"). Previously any
  non-zero code passed, including `2`, which means missing or unreadable. This tightens the
  check.
- On failure, the message lists every sampled member, its start ticks at sampling, and whether
  it is gone now. That makes a future red self-attributing.

Nothing is weakened. The old check showed that the tree stayed populated by showing that every
member survived, which is a stronger claim than custody needs and false by construction. The new
checks prove the populated state directly: the same root throughout, the root still in the
owned tree, and drain reporting "populated". The entry-refusal checks are unchanged:
non-zero exit, `CustodyTreeNotEmpty`, and no second-payload marker. The forced transient sample
stays in place, so this method now proves it tolerates a transient's exit instead of hitting
the case only by chance.

Green run, same command at the fix commit: total 1, succeeded 1, failed 0, skipped 0, duration
2.4 s. Load average 26.7.

## Not addressed (decision for the caller)

The method's comment says "Candidate image only ... This standing runner must not execute it",
but `RequireLinuxCustody()` checks only Linux, helper presence and the custody root. The card
asks that qualification admission express that lane requirement. That change is to admission or
lane policy, not to the witness. Making it in the fixture would turn the method into a skip,
which this class defines as never a pass. It is left for a separate card or decision.

This lane ran the method on the explicit brief ("Reproduce red on Linux ... one narrow green").
Its helpers equal the repository bytes, so the runs exercise the same helper code a candidate
image would ship. The green run reproduces the fix; it is not a qualification receipt for a
candidate image.
