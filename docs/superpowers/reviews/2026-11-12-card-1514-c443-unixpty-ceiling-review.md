# Review: C443 lock-outcome fixture expects the CARD-1224 UnixPty ceiling

Review task `e4d33bd2`, round 1 (Final, Full). Subject `feat/card-task-a259be5f` at
`8dcdab160bd49ee05eaeac86b9d70c9f6c3914c7`, one commit on master
`11eaf3c5106cbc2e71bbad7b82b6554780fbd60b`. Verdict: **CLEAN**, no blockers, no regressions.

## What changed

- `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs`: the
  `SingleWriteMaxBytes` expectation is now 86 400 off Windows. On Windows it is still
  86 400 for `modern` and 1 024 for `inbox`, the same values as before.
- `docs/investigations/2026-11-12-why-missed-c443-singlewritemaxbytes.md`: a new why-missed note.

## Product check

- `PtyBackendPolicy.Resolve` (`src/Antiphon.Agents.Pty/PtyBackend.cs:83`) returns `UnixPty`
  whenever `!isWindows`, before it looks at any selector.
- `DelegationSettings.CeilingsFor` (`server/Application/Settings/DelegationSettings.cs:318`)
  maps `UnixPty` to `UnixPtySingleWriteMaxBytes`, which is 86 400 (line 280). The
  `card-1224-unixpty` measurement id documents that value. `b35ed9863` is a cherry-pick of
  `bb4f2811c` and an ancestor of master. It is the only commit that adds the arm.
- `PtyDeliveryProfile` (`server/Application/Services/PtyDeliveryProfile.cs:70-71`) calls
  `Resolve(backendOverride)` and then `CeilingsFor`, so the fixture checks the real product path.
- `LandNotificationPayload.EnvelopeBytes` is a separate private constant: 1024, at
  `server/Application/Services/LandNotificationPayload.cs:10`. The fixture still checks
  `UTF8.GetByteCount(original.Body) <= 1024`. It also still requires the confirming
  `UserPrompt` transcript entry to match the body, plus exactly one queued message for each
  notification. Nothing was weakened or deleted, and the diff changes only the one expected value.
- The off-Windows expectation is a literal 86 400 with a comment. A Linux regression back to
  the inbox fall-through (1 024) would make this assertion fail, so it now pins the Unix arm
  at the integration level too.

## Evidence (server2, Linux)

- The checkpoint run `20261112-195411-8830` used a two-row review extract
  (`.antiphon/review-e4d33bd2-plan.md`, ignored). Source was `8dcdab160` with `state=clean` and
  `buildSource=verified`.
  - CP-1 `/*/*/AgentTaskWorktreeLockOutcomeTests/*`: executed=21 passed=21 failed=0 skipped=0 (232 s tests).
  - CP-2, the guard batch
    `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)`:
    executed=52 passed=52 failed=0 skipped=0 (TRX classes 41/1/5/2/3).
  - `validate`: `CHECKPOINT SOURCE VALID source=8dcdab160bd49ee05eaeac86b9d70c9f6c3914c7 rows=2`.
- Earlier build-slot runs at the same SHA gave the same counts: class 21/21 and guards 52/52.
- `check-evidence-diff` over `11eaf3c51..8dcdab160` found commits=1, entries=0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` exits 0 with tree `2df375b51`. origin/master is still `11eaf3c51`.

## Why-missed note accuracy

These claims were checked against git and the repository files:

- `de13507f7` introduced the old expression.
- `b35ed9863` is the only commit with `PtyBackend.UnixPty =>`. It is in master and in rc cut
  `56e96eabf`, and `bb4f2811c` is in neither.
- `72a552bdc` is the parent of `bb4f2811c` and has no Unix arm.
- `b35ed9863` deleted `Unix_backend_keeps_conservative_limits`.
- The `land-notification` row in `tests/impacted-classes.json` does not list this class.
- The derived index has `DelegationSettings.cs` as capped: 353 classes, 328 owed. Its first
  25 edges include `PtyDeliveryCeilingsTests` and `UnixDeliveryCeilingTests`.
- `scripts/lib/nightly-class-costs.json` gives 20 nodes and 948 s. The class now has 21 tests,
  because the harness-deadline test was added later. That staleness is in the cost roster and
  is not this change's concern.
- The orchestration-loop anchor `#why-missed-write-up-card-1781` exists.

## Windows

On Windows the expected values are byte-for-byte the same as before. Windows rc run
`0b292fa4` was not read, because nothing runs on the desktop from this lane. To check it, search
the antiphon chunk TRX under
`C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4`
for `AgentTaskWorktreeLockOutcomeTests`. Expect 21 passed, or 20 if that run predates the
harness test, with no `SingleWriteMaxBytes` mismatch. A Windows pass does not cover the Linux branch.

## Advisories (do not hold the land)

- A-1: `tests/linux-test-roster.json` still pins `sourceSha256` `88ac9bc8...` for this file,
  but neither base nor tip matches it (base `b5eecc23...`, tip `92e7f764...`). No code consumes
  that field. It was already stale at base.
- A-2: the note proposes a CARD-1801 impacted-classes row,
  `DelegationSettings.cs -> AgentTaskWorktreeLockOutcomeTests`. It is not applied, and it
  belongs to the CARD-1782/1801 owner.

PCs: none run. Any PCs stay pending for SourceLanding Mutation.
