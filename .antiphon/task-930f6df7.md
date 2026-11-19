# Review 930f6df7: CARD-1514 core.longpaths guard (round 1, Final/Full)

Subject: origin/feat/card-task-016f2cc2 tip 71554a7f4dbba44a0351679a71ec1479d413c8f2 (Grok author)
Range: 8cca875d8c26b8590eeda6769c5db67fa8cf1a02..71554a7f4dbba44a0351679a71ec1479d413c8f2 (3 commits)
Verdict: CLEAN. No blockers. Advisories below; they go to cards and do not hold the land.

## Evidence

- Checkpoint run 20261119-120527-fa25, all 9 plan rows at 71554a7f4, verdict GREEN exit=0,
  `validate` -> `CHECKPOINT SOURCE VALID source=71554a7f4dbba44a0351679a71ec1479d413c8f2 rows=9`.
  - CP-1 harness `C487: 19 passed, 0 failed, 19 rows` (40 s)
  - CP-2 LongPathsCheckoutScriptTests executed=6 passed=6
  - CP-3 C1323_PlatformExecution `C487: 12 passed, 0 failed, 12 rows`
  - CP-4 C545_ResultLine `16 passed, 0 failed, 16 rows`; CP-5 C1698 `4 passed, 0 failed, 4 rows`
  - CP-6 nine-class guard union executed=81 passed=81 (floor 81)
  - CP-7 DerivedImpactIndexTests executed=27 passed=27
  - CP-8 land graph -Check exit 0; CP-9 -PrintInputDigest exit 0
- Red at cf2ce9458 (detached scratch worktree, build-slot lease): `C487: 7 passed, 12 failed, 19 rows`,
  exit 1. Each red row fails for the stated reason (no clone flag, guard without override/stderr,
  launch reaches build-slot, -NoSync exit 0 with empty refusal, preflight.ps1 absent).
- merge-tree origin/master 3021acaf531d64cdb04fcba51d48d2e68f342977 vs tip: clean.
- check-evidence-diff base..tip: commits=3 entries=0 violations=0.

## Judgments

1. Lockout / false refusal: no lockout. The check is `git config --local --get core.longpaths`
   compared `-cne 'true'`. Git booleans written as True/1/yes/on, and a global- or system-only
   `core.longpaths=true`, are refused; a non-git directory, missing git or unreadable config are
   refused. Every refusal prints the reason and `git -C <exact checkout> config --local core.longpaths true`,
   which the operator can run without a code change; that normalises every case above. A linked
   worktree reads the common config with `--local`. No override flag exists; the fix command is
   enough.
2. Ordering: launch-rc checks right after the HEAD check, before build-slot/nightly-run.
   nightly -NoSync checks after locks/Started state and before selection, build and the chunk
   loop (returns through the same path as candidate-moved). preflight runs before
   start-supervisor. launch-rc exit 2, nightly -NoSync exit 3 with Refusal/Phase
   `longpaths-not-enabled`, preflight exit 2; documented in release-gates.md and the rc skill.
3. Checkout creation: release-candidate.ps1 `clone -c core.longpaths=true` (persists in the new
   repo config before checkout); nightly sync clones with the flag and sets the local key on
   every existing clone before its -NoSync self-hop. Other `git clone` in scripts/ is
   server2-repository-mirrors (not an rc checkout). Seeded c487 checkouts are fixtures and now
   write `[core] longpaths = true`; their Git seam answers `true`. Operational note: the
   existing `C:\Antiphon\releases\checkout-fc9a0547` lacks the key, so the next launch on it
   refuses at once (intended); run the printed fix command first.
4. Guard: captures only git worktree add/remove output (no remote, no credentials), tail-capped
   at 4000 chars; remove still runs in finally when add succeeded and reports through Write-Fail.
5. PS 5.1: 0 bytes above 0x7F in all 8 changed scripts; no `??`, ternary, `&&`, `||` or PS7-only
   cmdlets added. launch-rc keeps `#requires -Version 7.0`; preflight and the guard are 5.1-clean
   (EAP Continue; the `$PSNativeCommandUseErrorActionPreference` assignment is inert in 5.1).
6. Red/green as above.

## Advisories (card, do not hold the land)

- A-1 `--get` returns the raw value: `True`, `1`, `yes`, `on` and a global-only setting are refused.
  Fix: `git config --local --type=bool --get core.longpaths` (or `--bool`) in all three checks.
- A-2 `LP_PathArithmetic` asserts exactly one tracked path exceeds 260 under the guard prefix:
  a repository-content tripwire that turns red when an unrelated path of 155+ chars is added
  (or the 163-char one removed). Prefer `-ge 1` or assert the guard override independently.
- A-3 The Windows `git.cmd` shim branch of `LP_GuardStderr` is unwitnessed ("unobserved on
  Windows"); its first run is the Windows rc qualification.
- A-4 Stated red was 6 passed/13 failed; measured 7 passed/12 failed/19 rows. Cosmetic.
- A-5 release-candidate.ps1 does not set local core.longpaths on a reused owned clone; the launch
  refuses before long work, but setting it there (as nightly sync does) would remove the manual step.
- A-6 launch-rc exit 2 is shared with HEAD and run-id refusals; the reason text is distinct.
