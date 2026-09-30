# CARD-0418 round 37: final ordinary evidence audit

This round continues [round 36](2026-09-29-card-0418-round-36-evidence.md)
from `913300828ac13abef680298d8b64a0d89d66a656` without merging master.
The card and plan were read before this audit. Round 36 closes the local V-15,
V-17, V-18, and V-23 cases and their dependent R-2–R-13 oracles, with the
inherited CP-8 `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` failures.
V-24's isolated owned-host fixture passed in [round 14](2026-09-28-card-0418-round-14-evidence.md);
it is a fake Slack receipt, not evidence for V-25.

## Open-item audit

| Item | Status at round start | Required next evidence |
|---|---|---|
| V-1–V-24; R-1–R-13 | Local ordinary cases claimed complete in round 36 | Run the plan's exact CP-1–CP-13 Final list on this committed branch; retain fresh native results and honest CP-8 verdict. |
| R-14 | Open | The existing validator catalogue and synthetic guard tests are not an actual-run index. Map every ordinary assertion key to a decisive method and native TRX result, invoke the validator on that index, and keep the live-receipt component separate. |
| V-25 | Pending | Separately authorized mav-ref/mikeysbot-slack migration, active thread, normal docs completion and native Slack upload/open receipt. No live destination is authorized by this task brief. |
| PC-1–PC-30 | Pending | Method-scoped SourceLanding Mutation is paused. No ordinary, isolated, or nightly green discharges a PC. |

## Round-37 receipts

The [ordinary assertion index](2026-09-30-card-0418-ordinary-index.tsv)
maps the validator's 104 required V/R assertion keys to a named native method,
client log, or the index audit itself. Its `EXTRA-PUMP` row is necessary because
`OutboundConversionManifestTestsPump` is a separate class that the plan's CP-4
filter does not select. Its `EXTRA-V24` row is the plan's isolated E2E test,
which CP-1–CP-13 intentionally omit. These supplemental runs are required to
account for V-12 and V-24 at the Final profile; they are not new checkpoint
table rows. The index is a claim map until the fresh run outcomes are checked.

## Final checkpoint run

The exact CP-1–CP-13 closed list ran on committed source
`1db4997661ddc421f5a239c839d70ed605132974`. CP-1, CP-9 and CP-11 each
made one isolated build; their dependent rows reused it. CP-1–CP-11 used
`scripts/run-checkpoint.ps1` and granted host build slots. CP-12 and CP-13 used
`scripts/build-slot.ps1`. Each filter is the plan table's literal filter
(the table's Markdown `\|` is a pipe in the argument). Native logs and TRX
files are under `.antiphon/checkpoints/r37-final/` in this worktree.

| Row | Executed / passed / failed / skipped | Verdict |
|---|---:|---|
| CP-1 Unit | 3491 / 3491 / 0 / 33 | Pass; `TUNIT_MAX_PARALLEL_TESTS=2` |
| CP-2 source settlement | 373 / 373 / 0 / 0 | Pass |
| CP-3 policy/schema | 34 / 34 / 0 / 0 | Pass |
| CP-4 file boundary | 64 / 64 / 0 / 0 | Pass |
| CP-5 purpose/deadline | 64 / 64 / 0 / 0 | Pass |
| CP-6 crash/transport | 30 / 30 / 0 / 0 | Pass; V-15 cut methods execute 5 + 1 + 6 cases |
| CP-7 routing/attention | 320 / 320 / 0 / 0 | Pass; `TUNIT_MAX_PARALLEL_TESTS=2` |
| CP-8 existing deadlines | 71 / 69 / 2 / 0 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified`; no other failure |
| CP-9 renderer | 19 / 19 / 0 / 0 | Pass |
| CP-10 real browser | 1 / 1 / 0 / 0 | Pass, with manual image inspection below |
| CP-11 gateway wire | 125 / 125 / 0 / 0 | Pass; isolated broker enabled |
| CP-12 channels client | 26 / 26 / 0 / 0 | Pass, two files |
| CP-13 client bundle | — | Pass, production Vite build |

CP-12 was selected by the exact command
`pwsh -NoProfile -File scripts/test-client.ps1 ChannelsPage.test.tsx attentionVisuals.test.ts`
inside the build-slot wrapper. Its quiet Vitest output reports two files and
26 passing cases plus `CLIENT TESTS EXIT CODE: 0`; it does not print file names.

The CP-10 artifact is
`.antiphon/test-output/card-0418/v20-r7/12d521aa23a545838ab02a88751076ca/combined.pdf`
(SHA-256 `75204222dd25207bb92e7d1ce249c372e8cc9690688627635b2a488835d69c23`).
I inspected `page-1.png` through `page-4.png` beside it. All four ordered
sources begin on their own readable pages; the tables, code, Polish text,
Unicode symbol, middle and final sentinels are visible, with no blank or
clipped section. The test separately checks parsed PDF text and source hashes.

## Supplemental ordinary coverage

`EXTRA-PUMP` is an unlisted exact class run required because the separately
declared `OutboundConversionManifestTestsPump` is absent from CP-4's class
filter. It reused CP-1's build under `scripts/run-checkpoint.ps1` and passed
1 / 1 / 0 / 0 at `.antiphon/checkpoints/r37-final/EXTRA-PUMP-20260930-040515-8c85/run.trx`.
`EXTRA-V24` is the plan's isolated E2E V-24 class, absent from the CP table.
After CP-13 rebuilt `client/dist`, its exact method filter built once and
passed 1 / 1 / 0 / 0 under a granted slot at
`.antiphon/checkpoints/r37-final/EXTRA-V24-20260930-040606-5fca/run.trx`.
The owned fixture root is
`.antiphon/test-output/card-0418/f5/d03ef7a51ebf4c41afa6a1246a5dc175/`.
Its `transported.pdf` and sealed `output/combined.pdf` both hash to
`c79dccfc3c5e7fc3f784a2637167869fcbb381fbe1778a29d3613deb38e1f127`.
This is the isolated fake Slack receipt described in the test and [round 14](2026-09-28-card-0418-round-14-evidence.md),
not native evidence from the actual incident destination.

## R-14 local evidence accounting

The committed [index](2026-09-30-card-0418-ordinary-index.tsv) names each of
`ChannelOutboundEvidenceAccounting.RequiredOrdinaryAssertions`'s 104 keys.
The [validator](../../scripts/validate-card0418-ordinary-evidence.ps1)
requires every key, rejects unrequired or duplicate assertion/method claims,
locates exactly one fresh TRX for each named native row, joins test ids to
executed class/method names, and requires all its results to pass. It pins
the C-1–C-8 split to 5 + 1 + 6 executed cases and checks the V-7 gate,
V-11 refusal, and V-16 publication data-row counts. For CP-12 it checks the
26-case green wrapper receipt; the exact selected files are recorded above.
It admits only the explicit R-14 self-audit key as `AUDIT`.

`pwsh -NoProfile -File scripts/validate-card0418-ordinary-evidence.ps1`
passed: **106 green native/log claims, 104 required keys present**. The
output is `.antiphon/checkpoints/r37-final/index-audit.log`. The first
incomplete invocation correctly reported absent V-24 TRX, then the final
invocation passed after `EXTRA-V24`. The script checks that named methods
ran; it does not itself decide whether a method's assertions are decisive.
The named methods and the earlier case ledgers provide that reviewable link.
Visual page inspection and the actual destination receipt remain independent
evidence, not interchangeable with a green TRX.

## Remaining gates and disposition

Local V-1–V-24 and R-1–R-13 have fresh passing ordinary evidence on this
committed source, apart from the inherited CP-8 pair. R-14's **local** index
and synthetic missing/false-evidence guards are complete; its actual-destination
component remains pending with V-25. The task did not authorize an actual
mav-ref/mikeysbot-slack deployment or send, so there is no active-thread,
native Slack upload/open, or live control receipt to claim. PC-1–PC-30 all
remain pending for paused method-scoped SourceLanding Mutation; no ordinary
or nightly green discharges one. No product source or live configuration was
changed, and no live message was sent. There is no further Code-stage row
available under this brief.
