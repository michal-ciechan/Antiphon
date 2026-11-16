# Retire-temp: provisional 11-second host freshness fence

Code task `235c38c6`, 2026-11-16. CARD-1955 tracks qualification of the value.
Diagnosis and measurements: Debug `2b159393`,
`docs/investigations/2026-11-16-retire-temp-control-window-5s.md` on
`feat/card-task-2b159393` (commit `66eb023acdbf549bc153cce30a4d493eb364300f`).

## Decision

> OPERATOR DECISION 2026-11-16 (answered by the operator through the question
> dialog): "Widen to 11 s, provisional (Recommended)": widen the retire-temp host
> freshness fence from 5,000 ms to a provisional 11,000 ms, host-side only, keeping
> the desktop 5-second control and git limits, the 10-second response timeout, the
> 900-second proof lifetime and every validation check; track the qualification of
> the value under CARD-1955.

`scripts/c1008-retirement.sh` now has one named constant,
`C1609_EFFECT_WINDOW_MS=11000`. It replaces the four `5000` literals in the
exchange-required refusal, the publication diagnostic `limitMs`, and the
`c1609_effect_publication_fresh` comparison and message. The comparison also
refuses when the constant is missing or malformed.

These are unchanged: the host clock binding (`C1609_BINDING_CLOCK`, boot,
namespace and call bracket), the desktop 5,000 ms control and Git budget
(`NetworkBudgetMs` 1..5000 in `scripts/c1609-host-proof.ps1` and the
`control-deadline` in `scripts/c590-real.ps1`), the host `read -t 10`, the
900-second proof lifetime and admission, custody/header/nonce checks, dirty-work
guards, fsync refusal, live-origin withdrawal, the fail-closed lifecycle and the
staged rendering.

## Why the 5-second fence was missed

The native volume has one repository, `repos/antiphon`. One final `ls-remote`
takes 1.3 to 2.0 s. With ssh transit, scheduling of the existing desktop
PowerShell process and host persistence, the whole fence measured 3.9 to 5.1 s on
a busy desktop. Two live gates were refused `proof-stale` with 5,070 ms (preview)
and 5,050 ms (runner-state control) against the 5,000 ms limit. The fixture
witnesses ran each exchange on an idle local origin well under 5 s, so no test
modeled a slow single query. The 5-second value was a design bound, not a
measured budget.

Measurements (UTC, from host evidence, four attempts):

| Gate | File delta s | Guard elapsed |
| --- | ---: | --- |
| first preview | 4.55 | 5,070 ms, refused |
| second preview | 3.88 | passed 5 s, not recorded |
| general Apply | 4.24 | passed 5 s, not recorded |
| runner-state control | 4.41 | 5,050 ms, refused |

The 11,000 ms value is twice the worst sample (5,070 ms), rounded up. Four
attempts are not a population p95, so the value is provisional.

## What the extra six seconds exposes

The fence bounds the age of the final host binding observation and the origin
query through the publication check and each deletion effect. Between that query
and the effect, origin could withdraw the last ref that retains a local commit
(branch delete or force-push), and the host would delete the temp volume without
seeing it. The 5-second fence already allowed this race; 11 seconds widens it by
at most six seconds. For a TEMP volume the commits were published and re-verified
moments earlier, and each Apply effect re-snapshots fingerprints and re-verifies
publication through the full gate again. Ordinary additive pushes keep old
ancestry and do not cause the race. Stronger protection needs an origin-side
retained ref or another durable copy, which is outside this change.

## Witnesses

`VolumeRetirementReceiptTests` (native Linux):

- `One_repository_measured_control_fits_the_host_fence`: a coordinator spends
  1.5 s on its one origin query and the host guard sample is held to 5,170 ms
  after the binding observation. At the 5,000 ms fence it was refused
  `proof-stale` at 5,180 ms with a `control` row (`limitMs` 5000) in
  `publication-diagnostics.jsonl`. With the 11,000 ms fence the same preview is
  Ready.
- `Control_observation_older_than_the_host_fence_refuses`: the same exchange aged
  to 15,820 ms after the reply is still refused `proof-stale`, with a `control`
  row of `limitMs` 11000 and no Ready receipt.
- The bound-sensitive witnesses (`Large_ready_rendering_precedes_the_unchanged_final_fence`,
  `Late_ready_fence_refusal_invalidates_apply_admission`,
  `Failed_refused_persist_refuses_active_lifecycle_apply` and
  `VolumeRetirementPublicationTests.Final_origin_binding_refresh_is_inside_effect_window`)
  now age past the new fence: 12 s instead of 6 s, `elapsedMs > 11000`,
  `limitMs == 11000`.

`VolumeRetirementHostProofTests.Final_live_check_keeps_the_five_second_desktop_budget`:
the desktop live check admits `NetworkBudgetMs` 5000 and refuses 5001 and 11000
before Git starts, and the coordinator's 5,000 ms control deadline is unchanged.

## Revisit trigger

Re-measure and tighten the fence if any measured exchange exceeds 8 seconds, or
after the first five native applies, whichever comes first. Record control,
Ready and effect tails with host load for each approved native preview and apply
under CARD-1955. Do not run extra destructive operations to collect timings.
Treat the value as a measured p95 only after 30 comparable natural gate samples.
