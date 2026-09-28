# CARD-0418 round 10: partial ordinary matrix and stale-owner fence

This continues the [round-9 record](2026-09-28-card-0418-round-9-evidence.md) and the [round-8 open-case ledger](2026-09-28-card-0418-round-8-evidence.md). Source tip before this record: `def4ad09ab6f3cedd0baef05d4a5a5d096f780c8`. This is not Final verification or a landing verdict.

## Changes and exact scope

- A legacy `Deliverables:Enabled=true` plus stale browser/timeout-key integration row keeps four Markdown sources, no PDF/render log, and zero fake-browser invocations. It narrows V-1/R-1; the full F-2 settlement through X/Y/Z remains open.
- A test-only evidence-accounting helper reads native TRX rows and refuses missing, zero-execution, skipped, or failed ordinary claims; positive controls require a named assertion-red result and distinct baseline/restored-green TRX files. A local PDF/native-looking marker cannot close V-25: independent actual-destination review stays required. These are R-14 guard fixtures, not a completed V/R/PC index.
- Renderer timeout and caller-cancellation process-tree tests now use a child-spawning fake browser on Windows and Linux rather than returning immediately on Windows. PID markers are atomically published, and fixture cleanup checks process identity/start time. CP-9 exercised the Linux path; the Windows path remains unexecuted on this host. This narrows V-19/R-12.
- A two-DbContext pump test holds the first owner immediately before producer invocation, proves a second pump cannot claim its live lease, advances the clock past expiration, and proves the second pump takes it to `PublishUncertain` while the resumed old owner sends nothing. The production pump re-reads owner, version, lease and state immediately before the broker call. This closes those individual V-9/R-6 lease assertions, not the full intent/task/recovery matrix. The pre-call read cannot create an atomic transaction with Kafka; ambiguous in-flight sends remain `PublishUncertain`.

## Checkpoints

Every row used the checkpoint tool against a committed slice; the tool reported no unlisted build or test command. All builds/test drivers held a host build slot.

| Run / source commit | Row | Executed / passed / failed / skipped | Verdict |
|---|---|---:|---|
| `20260928-131135-6bb1` / `afba3b65b02ebae3bdc309bfd64ad5b93e8db270` | CP-4 | 57 / 57 / 0 / 0 | Round-9 source-byte change green. |
| same | CP-5 | 5 / 5 / 0 / 0 | Round-9 intent change green. |
| `20260928-132255-2143` / `139259e369cbdd4ff438db97d9ccc25e5a118631` | CP-1 | 3467 / 3467 / 0 / 33 platform skips | Unit lane green on the evidence/legacy-config slice. |
| same | CP-2 | 340 / 340 / 0 / 0 | Settlement class selection green. |
| `20260928-133423-c778` / `8f0bc9db68bbb7f4dff9b6e73f9f2911a069c899` | CP-9 | 19 / 18 / 1 / 0 | Fixture race: PID file read before write; not a production assertion red. |
| `20260928-133540-05da` / `8645b4bd18e6455b9fcf91a572b334eed094dc16` | CP-9 | 19 / 19 / 0 / 0 | Atomic PID publication fixed the fixture. |
| `20260928-133756-2658` / `5d4e1b52d2923ede43f07acefa5d54bda6b44de4` | CP-5 | 6 / 5 / 1 / 0 | **Decisive assertion red:** stale owner sent one reply after lease takeover. |
| `20260928-134226-2332` / `1044af1703c0957d22d4f59a87842bbb08e8fe27` | CP-5 | 6 / 6 / 0 / 0 | Fence restored green. |
| `20260928-134517-89f3` / `def4ad09ab6f3cedd0baef05d4a5a5d096f780c8` | CP-5 | 6 / 6 / 0 / 0 | Added live-lease exclusion and attempt-count assertions, green. |
| `20260928-134740-293e` / same | CP-6 | 16 / 16 / 0 / 0 | Crash/transport classes green on the final source tip. |

Full reports, TRX, logs and failed-case diagnostics are under `.antiphon/checkpoints/<run-id>/` in this worktree. CP-9's first failure is deliberately classified as a fixture race, not as a positive control.

## Remaining gates

The [round-8 open-case table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) still applies except for the narrow assertions above and round 9's V-9 identity/V-16 source-byte checks. In particular, full P/Q four-source settlement through X/Y/Z, main/trailing/machine and control caller matrices, runtime release, worker-purpose/refusal/capacity/deadline companions, remaining file/ZIP fault and serialized fallback rows, complete T1/T2 ordering and multi-target stamps, full C-1–C-8 state/receipt oracles, policy race/attention/monitor controls, fresh DI/legacy warning, Windows browser execution, and an actual evidence index/validator invocation are open. R-14 is not closed by synthetic validator fixtures. The complete CP-1–CP-13 Final sweep was therefore not run.

F-5/V-24 remains with CARD-0784; `origin/master` fetched on 2026-09-28 had no CARD-0784 commit. V-25 remains the later authorized live receipt, and PC-1–PC-30 remain method-scoped SourceLanding Mutation. CP-8's two inherited `codex_desktop_unqualified` failures retain the round-7 classification. No land, shared-stack restart, or live destination send was performed.
