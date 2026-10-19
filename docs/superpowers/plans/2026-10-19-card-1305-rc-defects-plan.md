# CARD-1305 RC environment and lane-placement defects

Plan for D-2..D-6. Confirmation base is `7b3b9adbe98b18ae306b73cfb220331dc433f801`
plus `docs/investigations/2026-10-17-card-1305-investigation.md` at
`1cc9db9b6146a379d03b978822497afcde90b6cf`. The same defects were on
`rc/2026-10-15` `4ebdd47ded247558f72e2af8c740130f0b8f5513`. Fetched tips on
2026-10-19 are ahead of that confirmation:

| Tip | SHA | What it already contains |
|---|---|---|
| `origin/rc/2026-10-15` | `61117dd1663d2f47ce4708c18b609b8936491986` | D-2 listener fallback, D-6 exclusion, D-4 rows removed |
| `origin/master` | `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0` | Those same commits, via `6aa0be65d860c761c999ec627ce522eeb6ce073f` |
| `origin/rc/2026-10-18` | `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` | Those commits, plus CARD-1344 and CARD-1345. Under gate. Do not merge into it. |

`GET /api/runner-defaults` is revision 2 with an empty `kindDefaults`. `GET /api/session-runners`
has one eligible Linux lane, one eligible Windows lane, and one stale drained runner with no
platform. This plan names lanes only. It pins no runner and no `-Platform`.

No section adds a session that waits for input.

## Ground truth

| Assumption at confirmation | Published source | Correction |
|---|---|---|
| `verify-dev-stack.ps1:79` calls `Get-NetTCPConnection` and throws `CommandNotFoundException` before the HTTP checks | This branch still has that line. Master and both rc tips call `Get-DevStackListeningPorts` (`verify-dev-stack.ps1:28-51` on master): the cmdlet when `Get-Command` finds it, otherwise `ss -ltn`. `lsof` is not used. | D-2 is published. Do not replay it from this branch. |
| Aspire-mode smoke FAILs when Docker answers and container `antiphon-postgres` is absent | The check is unchanged (this branch lines 70-74; master lines 103-107). `docs/bootstrap.md` still describes that container as the Docker Desktop always-on database. | D-3 is still open. It is the CARD-1323 leftover, not a new patch on this branch. |
| 69 OptIn cases reach `LandDeliveryFixture.InitializeAsync` and fail at line 59 on non-Windows | `LandDeliveryFixture.cs`, `AgentTaskLandDeliveryE2ETests.cs`, `DispatchBaseWarningDeliveryE2ETests.cs`, and `WorktreeRetirementDeliveryE2ETests.cs` are identical on this branch and `origin/master`. Line 59 is still `OperatingSystem.IsWindows().ShouldBeTrue()`. None of the three classes is in `profiles.rc.exclusions`. | The published decision keeps the 69 on the Windows rc lane. |
| CARD-1344 fixed the 69 | `ef3b5ffee9570a75e0e98a49e1744db2eedf0592` adds `tests/Shared/ReadOnlyDirectory.cs` and uses it from `CompletedCardRunnerFixture`, `CodexAuthProbeTests`, `AgentTaskDispatchBaseGuardTests.CardBranchNames`, and `RunnerSeatReleaseFixture`. No land-delivery file references it. | Drop nothing from the 69. |
| The runner image lacks `libglib-2.0.so.0` | `docker/session-runner-grok/Dockerfile:98` is the same apt line on this branch, master, and both rc tips: `curl ca-certificates bash libcurl3-gnutls libexpat1 zlib1g procps`. This host still has no `libglib-2.0.so.0`. `PlaywrightFixture.InitializeAsync` still launches headless Chromium. | D-5 is operator-only and still open. |
| `ChannelOutboundIsolatedTests` is required and skips unless three opt-ins are set | The class is an `e2e` exclusion, owner `CARD-1305`, on master, rc/2026-10-15, and rc/2026-10-18. This branch's policy does not have the row. | D-6 is published. Do not add the row again. |

Policy hashes, so a later edit does not start from the confirmation file:

| Tip | Schema | `policyHash` | `watchdogs.e2e` | `ChannelOutboundIsolatedTests` |
|---|---|---|---|---|
| This branch | 2 | `922cce2a0064bebfc90e211fe33db10dc74cb61ecb5d9f050bd00ec669e10ada` | absent | absent |
| rc/2026-10-15 | 2 | `9d58816258ac606910dd5b5fb8dc09e3cb69c06cafffa498963b6e7531610a7a` | absent | present |
| master | 3 | `de492ab11aa3a325af46b5447e7f5e2c66d81bb1b84f626bb6f3fea3d3d4b279` | absent | present |
| rc/2026-10-18 | 3 | `37593bf0ff6e6da787079234d9c55aa37abd7a08b2737c30746e6b0a2e175744` | `7200000` (CARD-1345) | present |

Master's schema is 3 (`projectDispositions`, `selectionCatalog`, and the later exclusion rows). CARD-1323 already records that an rc policy edit has to be applied onto the master file and rehashed with the master copy of `scripts/lib/nightly-policy.ps1`.

## Decisions

Defect ids stay D-2..D-6. These are the publication decisions.

### PD-1. No Code slice on `feat/card-task-2c9302aa`

D-2 landed as `a63c2c0713053fec400d35d0e2242a216985fc3c` then
`23ce54b190c647732c8f761f20cc9e11e7bbdee5`, and D-6 as
`4e261b2faeafa50f25d04e0087777c7c6a7f2fa2`, merged at
`b61c1c550772e71ba419fd0f59fa26da7d003e2a`. Replaying them from `7b3b9adbe` fights
those commits. Rejected: a second `ss` fallback and a second exclusion row on this branch.

### PD-2. Do not merge into `rc/2026-10-18`

That tip already has D-2, the D-6 row, CARD-1344, and `watchdogs.e2e = 7200000`.
It is under gate. Rejected: cherry-pick, hash recompute, or any push to that ref.

### PD-3. Keep the 69 on the Windows rc lane

`profiles.rc.exclusions` has no platform field, so the three class rows added and then
removed by `a5c697cee89b70c3a29cff125e034eb60eed7b45` would also drop the cases from
Windows. `docs/release-gates.md` defines `rc` as the automated Windows profile.
Rejected: Linux `Skip` inside `LandDeliveryFixture`, and re-adding the three rows.
A Linux rc placement is CARD-1323. CARD-1344 does not change this decision.

### PD-4. D-3 stays the desktop container check

Do not create `antiphon-postgres` on the Linux runner. Do not delete the check on
Windows, where a missing container is a real failure. The behavior change, if any,
belongs to CARD-1323 (`2b58c0a6-5942-4d68-957c-0bbfb8082d1a`, Backlog, master-only).
Rejected: an isolated Postgres in the Linux smoke, and a Code task on this branch.

### PD-5. D-5 is operator-only

An agent does not edit `Dockerfile:98` and does not run the staged image rollout.
The operator adds the Chromium OS libraries (the `playwright install-deps` equivalent
for this bookworm image) through that rollout. CARD-1323 records the same leftover.
Rejected: a master test that stays red until the image exists.

### PD-6. This branch is not a land candidate

Its parent is the pre-fix confirmation. Master already contains the published fixes.
This commit is the plan. Rejected: landing `feat/card-task-2c9302aa` onto master to
carry the plan.

## D-2 — listener probe — published on rc and master

Class: already on rc/2026-10-15, rc/2026-10-18, and master. Effort: none.

`Get-DevStackListeningPorts` uses `Get-NetTCPConnection` when that command exists
and `ss -ltn` otherwise. `-ProbeListeningPorts` prints the ports and returns before
Docker or HTTP. The red-first witness already landed: `VerifyDevStackSmokeTests.Linux_listener_probe_lists_ports_without_GetNetTCPConnection`
in `a63c2c071`, before the fallback. Filter, Linux lane:

`/*/*/VerifyDevStackSmokeTests/Linux_listener_probe_lists_ports_without_GetNetTCPConnection`

The test opens a loopback listener and expects that port. It goes red on this branch
because the test file is absent and line 79 still throws `CommandNotFoundException`.
On the published tips that witness is the existing test, so a second test cannot go
red. Do not add one.

## D-3 — desktop Postgres — master-only leftover

Class: master-only, owned by CARD-1323. Not an rc/2026-10-18 change. Effort there
is about two hours. Effort on this card: none.

The Aspire-mode check still records FAIL and the script exits 1 when Docker answers
and `antiphon-postgres` is not Up. `-SimpleMode` skips the check. The Linux runner
is not that desktop stack.

Red-first witness idea, for CARD-1323, Linux lane: one test expects the Postgres
row to be not applicable when the host is not Windows, committed before the branch
that implements that. Windows still FAILs a down container. Rejected witness: running
the full `verify-dev-stack.ps1` here. After the Docker check it creates a network.

## D-4 — 69 land-delivery cases — Windows lane, already decided

Class: decision published on rc and master. Not master-only new work. Not operator
work. Effort: none. CARD-1344 overlap: none, so drop nothing.

The 69 are unchanged: 52 in `AgentTaskLandDeliveryE2ETests`, 12 in
`DispatchBaseWarningDeliveryE2ETests`, 5 in `WorktreeRetirementDeliveryE2ETests`.
All three are `[Category("OptIn")]`. OptIn is not an exclusion. They belong on the
Windows rc lane. A non-Windows run still fails at `LandDeliveryFixture.cs:59`.

Red-first witness: the stored `ShouldAssertException` (`IsWindows()` was false).
That assert is the contract, not a bug to weaken. Do not add an exclusion test.
The Linux gap stays on CARD-1323 as platform-scoped exclusions or an explicit
acceptance that `rc` runs on Windows only. Release gates already say the latter.

## D-5 — libglib — operator-only

Class: operator-only. The same Dockerfile line is on master and both rc lines.
Do not merge an image edit into rc/2026-10-18 while it is gated. Effort: an
operator rollout, not a Code slice.

`PlaywrightFixture.cs:38-41` launches headless Chromium. The stored failure is
`libglib-2.0.so.0: cannot open shared object file`. This host still lacks that
library. The apt line at `Dockerfile:98` does not install a Playwright OS stack.

Red-first witness: that missing library. Do not commit a test that fails the
suite until the operator image is published. The 35 historical browser cases were
not re-run.

## D-6 — F-5 canaries — published exclusion

Class: already on rc/2026-10-15, rc/2026-10-18, and master. Effort: none.

`ChannelOutboundIsolatedTests` stays excluded with owner CARD-1305 and the reason
that the canary skips unless `ANTIPHON_HEADED_TESTS`, `ANTIPHON_BROKER_TESTS`, and
`ANTIPHON_CHANNEL_OUTBOUND_ISOLATED` are all `1`. The automated lane does not set
those variables. The three land-delivery classes are not part of this row.

Red-first witness already happened: the class skipped with
`SkipTestException("F-5 requires all three isolated opt-ins.")`, and a skipped
required row fails the rc census. A new test of that skip cannot go red on the
published tips.

Any future policy edit starts from the target tip's file. rc/2026-10-18 already
has `watchdogs.e2e = 7200000`; preserve it. Do not recompute a hash from this
branch's `922cce2a…` payload. Hash with `Get-NightlyPolicyHash` in
`scripts/lib/nightly-policy.ps1` (it excludes the `policyHash` field). This plan
does not edit the policy.

## Slices

No product, test, policy, or image slice. The only commit on
`feat/card-task-2c9302aa` is this plan.

| Slice | Files | Tests | Lane |
|---|---|---|---|
| None | — | — | — |

CARD-1323 is the owner for a later D-3 behavior change, for platform-scoped
placement of the 69, and for the D-5 operator note. That card is master-only.
