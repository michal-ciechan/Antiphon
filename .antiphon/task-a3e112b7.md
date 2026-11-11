# CARD-1224 S1 Final Review

Subject task `576f7781-c113-400a-8ee4-b4e0c1b89183` (Code, Succeeded). Reviewed `0c9b47baca178ebd8cd2eced8a70a6bdd82c0b7e` on `feat/card-task-576f7781`. Range `188ac942166884f060fdcc7cbf3b15f670a27b43..0c9b47baca178ebd8cd2eced8a70a6bdd82c0b7e` (five commits). Round 1. Verdict clean for this S1 slice.

## Checks

1. Fake agents only. `UnixPtyDeliveryEnvelopeTests` launches staged `fakeclaude` and `fakegrok` through `PtyAgentRunner`. No live Claude, Codex, or Grok process and no paid call.
2. The receipt integers match D-3 applied to the committed trial lines (all WHOLE, markers true): single 86400, brief 43200, reply 14400. CP-1 re-measured the live trials, markers, Grok newlines, and multiline submit count against that file. Code recorded that corrupting receipt fields failed 6 of 7 tests, with latency magnitudes still valid, and that removing the latency keys failed `Delivery_latency_is_recorded` 1 of 1. This review did not re-apply mutants. PC-1 through PC-10 stay pending.
3. The FakeClaude change catches only `DllNotFoundException` and `EntryPointNotFoundException` around the existing kernel32 mode probe when debug input is on, then writes `INMODE:unavailable` and continues. The Windows success path is unchanged. The new apphost target is in the pty test project, conditioned on a non-Windows host, and writes under that project's output. `server/Antiphon.Server.csproj` does not reference FakeClaude. The server image publishes that project only.
4. The class carries `[ParallelLimiter<ProcessSpawnLimit>]`. Each trial `KillAsync`s in `finally`. The Grok trial sets `GROK_HOME` to a fresh temp directory and deletes it. After CP-1, no `fakeclaude` or `fakegrok` process remained and no `c1224-grok-*` temp directory remained. FakeClaude writes a home only when `ANTIPHON_FAKE_NATIVE_HOME` or `ANTIPHON_FAKE_TRANSCRIPT_PATH` is set; both were unset. Codex home is inherited and unused by these fakes.
5. The receipt and `LoadReceipt` pin `codex: unmeasured`. The plan keeps Codex join-safe and unmeasured. This tip does not add a Codex ceiling.

## Fresh runs at the reviewed SHA

CP-1, checkpoint run `20261111-025438-02e7`, slot granted, source clean, `buildSource=verified`: executed 7, passed 7, failed 0, skipped 0. Methods: `Control_body_arrives_whole`, `Single_write_ladder_matches_the_receipt`, `Paced_ladder_matches_the_receipt`, `Bracketed_paste_markers_match_the_receipt`, `Multiline_lf_body_matches_the_receipt`, `Grok_newline_join_matches_the_receipt`, `Delivery_latency_is_recorded`. Row build `bin-c1224-pty` 45.7s. Test wall 24.8s. Run wall 1m47s. `bin-c1224-unit` was recorded unused. No unlisted checkpoint command.

Guard and impacted classes, one leased `--no-build` run of the already slotted `tests/Antiphon.Tests` build (`UseAppHost=false`, native output), lease `874169b6-d6bd-4cbe-b76f-e34579e7c168`: 88 passed, 0 failed, 0 skipped, 4m23s. Nine-class guard union 78 (policy 41, classification guard 1, slow registry 5, tripwire 2, NativeLinux 3, mechanical 11, TaskPlatformGuidance 5, C1729 9, lane category 1). `PlanCoverageBindingTests` 7. `Card1277PtyHostFixtureTests` 3 on Linux. The Windows filter `/*/*/(Card1277PtyHostFixtureTests*)/*` stays owed. `UnixPtyDeliveryEnvelopeTests` is the CP-1 row in the pty assembly.

`scripts/check-evidence-diff.ps1` over the five-commit range: commits=5, entries=0, violations=0, exit 0. `git merge-tree --write-tree origin/master HEAD` against `bc71a95e7710532a9a68416f1d7b9e158fe96a15` wrote `21c831e3caa467810a4e30e380d326a93c75d658`, exit 0. Not merged.

Impacted kinds: `pty-host-fixture` only. Downgrade selector: the migration row did not match, so selected downgrade callers = 0. No migration path in the diff.

`GET /api/runner-defaults` and `GET /api/session-runners` were read. This run omits `-Runner` and `-Platform`.

## Not in this slice

V-8 through V-13 and R-1 through R-7 are S3 and are not on this tip. PC-1 through PC-10 remain pending for method-scoped SourceLanding Mutation. No production queue or UserPrompt path changed. No input-wait was added, so the CARD-1083 release checklist does not apply. Restart impact is none.

## Advisory

V-7 does not pin latency magnitude. A non-negative edit of `latencyControlMs` or `latencyBriefMs` stays green while the keys remain. That is the recorded seventh mutant and it does not hold the land. A numeric edit of `singleWriteBytes`, `briefInlineMaxBytes`, or `replyInlineMaxChars` that leaves the trial lines intact is also not re-derived by CP-1. The committed numbers match D-3 of the trial lines CP-1 just re-measured. S3 is the slice that copies them.

`LaunchAsync` does not dispose the runner if the banner or backend assertion throws before the caller’s `await using`. The green path kills the child. No process remained after CP-1.
