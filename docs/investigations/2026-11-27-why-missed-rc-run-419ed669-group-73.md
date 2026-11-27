# Why Windows rc run 419ed669 missed the Grok spinner failure

Windows Full run `419ed669` on `rc/2026-12-07`, pinned to
`5c0883bf203d122fdc7d4556e79087af2a683669`, failed group 73 in 13.4 s.
`SessionMessageQueueGrokPtyIntegrationTests.Captured_spinner_reaches_ready_and_complete_user_prompt`
reported `specialist_start_intent_revoked` at the final standing-launch authorization read,
after readiness and the launch's queue flush. The prior cut record lists all 19 failed chunks
from `1fe763a9` and group 73 is absent. The complete desktop summary and the exact prior
method outcome were unavailable. [Prior cut record](2026-11-26-rc-cut-gates-pass3-bdb2790af.md)
and [coverage summary](2026-11-26-rc-1fe763a9-coverage-incomplete-reasons.md).

## Fixture failure

The spinner fixture inherits `BridgeQueueHarness`'s `AlwaysOn = true`. The harness
registers no production standing supervisor. Its Grok test agent has neither
the typed Check role nor the configured Check slug, so CARD-0079's Claude Check
compaction recovery cannot authorize or supersede its launch.

After readiness the launch sets the session Running and flushes the queued message.
If the submit stays unconfirmed and the session still appears idle, the AlwaysOn
queue recovery can call `KillGenerationAsync(SystemRequest)` for the same accepted
`StartedAt`. That closes the session before the last launch authorization read,
which requires Starting or Running. The generic conflict can therefore mask the
delivery failure even if the accepted generation never changes. CARD-0778 recorded
this same sequence in an earlier Windows run at
`5e3cacd6f7cc9d5d865fd2ffe81e305319674ad0`. Its then-proven cause was a mismatched
FakeGrok working directory, corrected by
`5efa69b3f0a5b74210aa46440d87736496eafee6`. The current fixture already passes the
row's working directory. The supplied 419ed669 stack omits the original
queue verdict, backend input trace or runner transcript. The current native
submit miss and its relationship to host load remain unverified.

CARD-0778 commit `b5f1dd7e9ee762568de9adc4d487ec89b0eb0e7d` introduced the capstone
with the inherited AlwaysOn configuration. The final authorization read and
generation comparison predate it and were introduced by CARD-0466 commit
`9a80aedb563179ed249be963bfd740c1d9a6ea11`. The launch service, queue, two fixture
files, DirectSessionRunnerClient and Check policy are unchanged between the failing
rc pin and fetched master `b5b349d968b5c5ae28a66c73201f1ab06a540aa5`. The same
fixture and launch/queue logic were already present at the previous pin
`0cfc4ac1ffb41f5af159a0c1db423fe119950267`.

The fixture runs as a manual standing session so a failed delivery remains directly
visible in the native assertion instead of triggering AlwaysOn recovery during launch.
The scripted control swallows Enter and checks the unchanged generation, the Pending
row with `NoSubmitOutput`, and successful finalization of the still-live session.
The native capstone retains the pre-ready queue hold, actual ModernConPty host log
and exactly one whole persisted UserPrompt.

## Why the cut let it through

Short Merge runs the batch's own narrow witnesses. The retained five-card Merge
selections for `27f8ede0c0e65c73b2174e6495e791cdf299a09c` did not select
`SessionMessageQueueGrokPtyIntegrationTests`. Its native PTY rows selected Linux
placement handshake methods only. The third cut gate's published rows also omit
the Grok capstone. Windows Full was the first retained execution at this rc cut
that could detect the failure. The evidence identifies no new product
change that introduced it. [Short Merge rule](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
and [cut gate rows](2026-11-26-rc-cut-gates-pass3-bdb2790af.md#linux-checkpoints-on-the-repair-branch).

## Native checkpoint still owed

Run the single native method through the checkpoint tool on the Windows desktop
with its staged FakeGrok and modern ConPTY assets, once the qualification releases
the desktop build slot. The caller's Windows Debug delegate should record the
exact source SHA, receipt and TRX, and retain the queue verdict and PTY diagnostics
if it fails. The Linux scripted witness covers the authorization symptom.

## Red witness

`Spinner_fixture_keeps_the_ready_generation_when_the_first_submit_is_swallowed`
ran on Linux at `b833874a1b9559f727cdd94c2a692e521d1bab5d` with 1 executed,
1 failed and 0 skipped. Its Pending status, `NoSubmitOutput` verdict and
accepted-generation equality assertions passed before the expected conflict from
`RequireCurrentCheckLaunchAsync` at the final check. The named mutant is
`M-InheritAlwaysOn`, which removes the explicit `AlwaysOn = false` from
`CreateSpinnerHarnessAsync` and restores the exact harness configuration used in
that red run. The TRX and paired leased build and test logs are retained under
`/work/evidence/task-14a6ef67/direct-red/`.

The initial checkpoint row reached its build deadline while scanning existing
ignored output in the runner mirror, with no test count or TRX. The red witness
used the leased native build output after that row settled.
