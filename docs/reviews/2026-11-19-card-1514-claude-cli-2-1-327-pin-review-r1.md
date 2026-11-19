# CARD-1514 Claude Code 2.1.327 runner-image pin: Final review round 1

Review task `8d80856b`. Subject: `feat/card-task-cf0dc207` (Code task `cf0dc207`, Grok author).
Range `709c41f16caf400b6fa91a80866a06d2c05fc318..fc3e3a5d97142f49643481548cb6795921c5f6ce`
(three commits: `a9a58049` red literals, `413461b7` pin, docs, harness and seed, `fc3e3a5d` derived index).
Master at review time: `b2547853c096260c4ca7319f763a7e5e25a5510e`.

**Verdict: CLEAN.** No blocker. Advisories A-1 to A-4 stand and do not hold the land.

## 1. Digest and platform key

- `GET https://downloads.claude.ai/claude-code-releases/2.1.327/manifest.json` (2,101 bytes, HTTP 200,
  fetched 2026-11-18). `version` is `2.1.327`. `platforms["linux-x64"].checksum` is
  `343c744baa14bfd38286a7cb90871051f04379956da6c5b803a9e66019a76ac9`, size 372,904,464. This
  matches the Dockerfile `ARG CLAUDE_CODE_SHA256` byte for byte.
- The other Linux keys have different digests: `linux-x64-musl` `d2b02e78…`, `linux-arm64` `acf0ef2f…`,
  and `linux-arm64-musl` `e44a59ef…`.
- The download step (`docker/session-runner-grok/Dockerfile:114`) fetches the literal
  `…/${CLAUDE_CODE_VERSION}/linux-x64/claude` into `runtime-base`, which is
  `mcr.microsoft.com/dotnet/aspnet:9.0` (Debian bookworm, glibc). server2 is `x86_64`. So
  `linux-x64` (glibc) is the right key. The musl and arm64 keys do not apply.
- Control: the 2.1.280 manifest's `linux-x64` checksum is `1e08503d…22925b`, the old pin.
- The binary was not downloaded or run.

## 2. Stale current-pin literals

`git grep` for `2.1.280`, `2.1.281` and `1e08503d` at the tip finds no literal that a test,
script, Dockerfile step or server code reads as the current pin. Every remaining hit is one of
the following:

- An investigation, an older plan, or `docs/cards`.
- A comment labelled as measured history: `dind-entrypoint.sh:116`,
  `seed-claude-onboarding.mjs:5-6`, `ModelLevelAliases.cs:12-15` and
  `RunnerStartupStaleObservationTests.cs:18`. The last one is a doc comment, not an assertion.
- A floor statement: `docs/agent-kinds.md:241`, "2.1.280+ is required", which still holds.

The current-pin readers are the `runtime-base` install `grep -F "${CLAUDE_CODE_VERSION}"`, the
`session-testing` probe, `verify-card0604-dind-runner.ps1:384`, the seed constant, and the three
docs. All of them say 2.1.327. `DindRunnerContractTests` derives the seed version from
`ARG CLAUDE_CODE_VERSION` (`:456-459`), so the seed cannot drift silently.

## 3. Onboarding seed (highest risk)

- **The production store is unaffected by the stamp.** `/state/claude` is the persistent
  `runner-state` volume. Read-only on the live server2 runner, only those keys, the store has
  `lastOnboardingVersion = "2.1.280"` and `hasCompletedOnboarding = true`. The seed writes the
  version only when the key is absent (`if (!config.lastOnboardingVersion)`). The old runner
  therefore keeps `2.1.280`, and 2.1.327 meets the same older-stamp state as any CLI upgrade. This
  store already went through one upgrade: the 2026-10-22 native self-update to 2.1.309, which the
  background note records.
- **A fresh store gets a stamp equal to the binary.** This applies to a new temp volume and to the
  CARD-0604 harness. The CLI writes its own version there when it completes onboarding, and the
  stamp is never newer than the binary. A stamp mismatch is therefore not a stall vector from this
  change. `hasCompletedOnboarding` is forced true in both cases.
- **Residual risk is unobserved, and the diff says so.** 2.1.327 may read a new first-run key or
  show a new dialog that the 2.1.280/2.1.281 key set does not pre-answer. If it does, the CARD-0680
  dialog guard (`docs/session-runtime-invariants.md` § Claude automated-input dialog guard) refuses
  to type into a visible dialog and retains the pending row. A session would then hold rather
  than corrupt input, but it would still stall. Only the canary can settle this.
- **The canary must check the following on `server2-temp` (rollout gate 2), before `drain-old`:**
  1. `claude --version` in the container prints `2.1.327`.
  2. The canary is a **Claude Code** task (not Grok) in a new `/work/worktrees/*` path.
  3. Its first prompt is transcript-confirmed as a `UserPrompt` and the turn completes, with no
     onboarding, trust, theme or bypass-permissions dialog on screen.
  4. `modelUsage` reports `claude-fable-5-2` for Frontier. `claude-sonnet-5-5` for Medium is
     optional.
  5. `/state/claude/.claude.json` still carries `hasCompletedOnboarding` and the trust entries
     after the turn.
- **Rollback:**
  - At gate 2: keep the old `server2` runner accepting work, do not run `drain-old`, and retire
    temp.
  - After `redeploy-old`: redeploy the previous `session-testing` image from the rollback tag
    (`docs/docker-stack.md:1924`, keep previous runner and state-init image IDs under explicit
    rollback tags before the rollout).
  - A stuck session is held, not killed. Recovery is the standard drain under the four-hour cap.

## 4. Red first

`a9a58049` touches only the four test files. At that commit the Dockerfile has three `2.1.280`
literals, and the docs, script and seed are still at the old pin. Seven of the eight changed
assertions fail by literal mismatch, which is the stated reason: version and digest `ShouldContain`,
the probe, the doc `Claude Code 2.1.327`/`343c744b`, the harness grade, and the seed
`ShouldBe("2.1.327")`. This was verified statically and the red commit was not executed. The eighth,
`install.sh | bash -s 2.1.327` `ShouldBeFalse`, is a pre-existing negative guard that cannot go red
from a literal change (A-3).

## 5. Reruns at `fc3e3a5d97142f49643481548cb6795921c5f6ce`

Run `20261118-233842-0f50`, the plan's `### Checkpoints` table, all 8 rows, one cold build
(263 s). Verdict GREEN.

| CP | Class / command | executed | passed | failed | skipped |
|---|---|---:|---:|---:|---:|
| CP-1 | DockerStackContractTests | 132 | 132 | 0 | 0 |
| CP-2 | DockerStackDocumentationTests | 20 | 20 | 0 | 0 |
| CP-3 | CodexRunnerImageContractTests | 21 | 21 | 0 | 0 |
| CP-4 | DindRunnerContractTests | 25 | 25 | 0 | 0 |
| CP-5 | VerifyDindRunnerScriptTests | 9 | 9 | 0 | 0 |
| CP-6 | guard batch (10 classes) | 87 | 87 | 0 | 1 |
| CP-7 | DerivedImpactIndexTests | 27 | 27 | 0 | 0 |
| CP-8 | `audit-land-verification-graph.ps1 -Check` | n/a | exit 0 (`approved`) | | |

- `validate --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-7,CP-8`: `CHECKPOINT SOURCE VALID rows=7`.
- The whole run validates `row_failed` only because of CP-6's single skip. That skip is
  `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`, a Windows-only
  case that skips on Linux by construction and is untouched by the subject.
- Review extract `.antiphon/review-8d80856b-guards-plan.md` (ignored), run
  `20261118-235532-c7f7`: CP-6 without that class, executed 81, passed 81, failed 0, skipped 0.
  `CHECKPOINT SOURCE VALID rows=1`. Unlisted-run reason: a validated receipt for the guard batch.
- A first start of that extract (`20261118-235212-e066`) began at load 24, above the brief's
  limit of 16. The reviewer stopped it during its build, before any test ran, and restarted it at
  load 4.8.
- `scripts/check-evidence-diff.ps1` over the range: `commits=3 entries=0 violations=0`, exit 0.

## 6. Merge-tree

- Against `709c41f1` (the subject base): clean.
- Against current master `b2547853`: **conflict in `tests/derived-impact-index.json` only.** On the
  `Antiphon.sln` allowlist entry, master has 331 classes and the subject has 330. Master also
  changed `PhoneHomeRunnerSettings.cs`. No other file conflicts; `docs/docker-stack.md`
  auto-merges.

## Advisories

- **A-1:** The land must take master's `tests/derived-impact-index.json` and regenerate it with the
  builder on the rebased tree. It must not hand-merge the count, and must then re-run
  `DerivedImpactIndexTests` (`committed_index_matches_the_builder`). The subject's pin literals do
  not move the count.
- **A-2:** The rollout canary, step 3 above, is the only evidence for 2.1.327 onboarding and trust
  behaviour on Linux. It must be a Claude Code task on `server2-temp`, not the generic gate-2 Plan
  canary on whichever kind dispatch picks.
- **A-3:** The negative `install.sh | bash -s <version>` assertion can never go red from a version
  bump. A version-independent `install.sh` refusal would be stronger. This is pre-existing.
- **A-4:** Plan CP-6 includes `WindowsUnitPlatformAdmissionTests`. On Linux its platform skip
  keeps that row from ever validating. Future plans for Linux reviews should list it separately.

--- review evidence ---
subjectTaskId: cf0dc207-0577-4268-8e08-d8e12b6e544d
reviewedSourceSha: fc3e3a5d97142f49643481548cb6795921c5f6ce
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 709c41f16caf400b6fa91a80866a06d2c05fc318..fc3e3a5d97142f49643481548cb6795921c5f6ce
