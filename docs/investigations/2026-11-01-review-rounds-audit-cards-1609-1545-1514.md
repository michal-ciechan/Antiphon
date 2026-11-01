# Review-rounds audit: CARD-1609, CARD-1545 and the CARD-1514 failed-first / seed series

Task 60a7cb17 (Investigate), 2026-11-01, server2/Linux, read-only. Sources: `card.ps1 history`,
`GET /api/agent-tasks?projectId=…` (7,140 rows), `GET /api/agent-tasks/{id}` for 86 tasks (the
`goal` field is the dispatched brief, quoted below where a brief is blamed), the report files
`.antiphon/task-<id>.md` on each task's origin branch, and `git log -S` on the task branches to
date the commit that first carried each defect. I did not read the private-notes route or any
desktop file, and I ran no build or test. Every reviewer was `ClaudeCode` at level `High` (the
"Opus" reviewer). Costs are the recorded `costUsd` on each task.

## 1. Headline

**The "16" is not a review count. It is CARD-1609's fix-round counter.** Task `ae950679`'s title is
"CARD-1609 round 16". CARD-1609 had **4 review rounds**: 2 plan reviews (`3372044d`, `1f7eb8e4`;
`5c904345` failed to launch and wrote no report) and **2 code reviews** (`c1dc6681`, `52590db3`).
It had **17 implementation/fix rounds** (round 0 `958507f1` through round 16 `ae950679`, which is
still running), plus 2 Debug tasks and 2 Investigate tasks. Only **rounds 14 and 16** were driven by
a code review. Rounds 0–12 were the first implementation, spread across budget-capped continuations
($707 recorded). Their red rows were test, fixture and timing defects
(`docs/investigations/2026-10-31-card-1609-s5-triage.md`). Round 13 came from a triage, not a review.

| Card / series | Review tasks (with report) | Max rounds on one subject | Fix rounds | Code-review defects | A (original) | B (fix-introduced) | A+B (card's own) |
|---|---|---|---|---|---|---|---|
| CARD-1609 | 2 plan + 2 code (+1 failed launch) | 2 code | 17 (R0–R16) + 2 Debug | 4 | 2 | 0 | 2 (50%) |
| CARD-1545 | 3 plan + 7 code (+`185b71e2` running) | 4 (S1c chain: `95bc3d62`, `1e52322c`, `e38d6427`, `cffa0c4b`), 5th running | 7 | 29 | 12 | 7 | 19 (66%) |
| CARD-1514 failed-first + seed | 5 code (+`1634b300` running) | 4 (`b5c88cd9`/`1bf477d5`, `460ee5a4`, `1e04fe29`, `4ec701ad`), 5th running | 6 | 11 | 4 | 5 | 9 (82%) |
| **Three requested series** | **15 code reviews** | | | **44** | **18 (41%)** | **12 (27%)** | **30 (68%)** |
| Adjacent: tree-kill (`877889c5`, `ccbcfec4`) and master-port migration (`ef6babe1`, `6399a4f7`) | 4 | 2 | 3 | 7 | 4 | 1 | 5 |

Across the 44 code-review defects in the three requested series, the primary causes were:

| Cause | Count | Share |
|---|---:|---:|
| A: the card's original change | 18 | 41% |
| B: introduced by a previous fix round | 12 | 27% |
| D: interaction between slices or branches | 7 | 16% |
| E: orchestrator brief error | 5 | 11% |
| F: reviewer scope expansion | 2 | 5% |
| G: test or environment artifact | 0 primary | (1 secondary) |

Most defects were true defects in the card's own code: 30 of 44 (68%) are A or B. Only 18 (41%)
were present in the first implementation.

**The four costliest blockers were brief-prescribed.** In each case the brief dictated the
mechanism, and the worker built what it said:

- the gate placed at the delete loop (CARD-1609, strands server2 main);
- "a regrouped plan still falls back" (CARD-1514, failed-first silently lost);
- "RE-BASE the census seed on the combined tree" (CARD-1545, vacuous census);
- "the CARD-1619 migration needs a new timestamp" (port, 42701 startup failure).

A fifth costly blocker came from an orchestrator action rather than a brief line. Advisory
`b5c88cd9` A-2 was turned into a behaviour change on a parallel branch, and that change silently
dropped failed-first order (CARD-1514 `460ee5a4` D-1).

**A diff-only scope would have caught almost everything.** Of the 24 code-review defects found in
rounds ≥2, 22 sat inside the previous round's fix diff or the combination diff. One sat outside
it: the S1c Compile glob, which the Unit lane found, not the diff. One sat partly outside it: the
CARD-1609 sudo scans. Their code was older, but the round-15 fixture change that masked them was
in the diff.

## 2. Classification key and method

The classes are the ones the brief defined:

- **A:** the card's original change. I name the first commit that carried the defect.
- **B:** introduced by a previous fix round. I name the fix commit or task.
- **C:** pre-existing in master or rc.
- **D:** interaction between independently developed slices or branches.
- **E:** an orchestrator brief error. I quote the brief line.
- **F:** reviewer scope expansion. I mark whether the exposure is real today.
- **G:** a test or environment artifact.

Where two causes apply, the defect is listed as primary/secondary.

Each defect also has an **In diff?** judgement. It asks: would the defect lie inside the diff
from round N−1's reviewed SHA to round N's tip? A round-1 review covers the whole change, so it
reads "R1". A defect "found by a test" means a commissioned row turned red, which a diff-only
review that still runs its rows would also see.

Severity is the reviewer's own label. "n/s" means the reviewer did not state one.

**Blocker** in §6 means the operator's list: data loss, secret leak, a regression, a vacuous
gate, or a server that cannot start. I add a stranded rollout and a silently lost feature.

## 3. CARD-1609 (content-derived volume retirement)

### 3.1 Rounds

| Rd | Task | Subject (SHA) | Scope the reviewer used | Verdict | Cost |
|---|---|---|---|---|---:|
| P1 | 3372044d | plan v1 `ae1e87f87` (dcbf1d18) | whole plan; importer and base counts run | Not ready: 10 defects, 5 advisories | $5.2 |
| P2 | 1f7eb8e4 | plan v2 `50ebdf063` (02c3c411) | whole plan, centred on the round-1 fixes; importer run | Not ready: 4 defects, 1 advisory | $4.7 |
| — | 5c904345 | plan v2 | failed launch, no report | — | $0 |
| C1 | c1dc6681 | round 13 `33ab2cffb` (1fa8a516) | the new gate plus a whole-branch deletion census; rows CP-8/9/16/17 (Interim) | 2 defects (blocker, major), 6 advisories | $9.5 |
| C2 | 52590db3 | round 15 `7f801e8dc` (ad4999db) | rounds 13–15 plus a full Linux sweep (11 rows), a master merge and 14 mutants | 2 defects (1 blocker), 5 advisories | $16.0 |

### 3.2 Fix and implementation rounds: what actually drove them

| Round | Task | Driver | Outcome |
|---|---|---|---|
| R0 | 958507f1 | implement the plan | only S1 red witnesses; budget stop |
| R1–R4 | 404814e4, cba5ec74, 14947ff2, 19f9c7ca | continue S2–S4 | green slices; S2 sequencing stop in R1 |
| R5 | b2fc0aa5 | S5 | checkpoint red: 2 rows failed, 4 timeouts |
| Debug | a2935adb | the R5 reds | fixture defects, shared Compose setup, broken pipe (test/env) |
| R6–R10 | a869b1b6, 4c8e4fc2, cadd1086, cdf3cde4, 8b5c63b3 | the owed S5 list | budget stops; CP-10 hard budget; H7 unresolved |
| Debug | 7dd4e872 | H7 `origin-present` | the fixture omitted `/etc/alternatives`; test-only |
| R11–R12 | e9fd8902, 587775c4 | acceptance and roster | roster 344/335/9 failed |
| Triage | 4b1cc3e7 | the 9 failures | all test/fixture; **found that production deletes are not gated** |
| R13 | 1fa8a516 | the triage | 4 test repairs plus the new apply gate |
| R14 | a9634687 | review C1 D-1/D-2 | gate moved before the stop/journal |
| R15 | ad4999db | R14's own red N3, the M5 survivor and load flakes | custody vs root, fixture applies as owner (masks C2 D-1) |
| R16 | ae950679 (running) | review C2 D-1/D-2 plus the master slice | |

Rounds 0–12 cost $707.5. Rounds 13–15 cost $265.9. The two code reviews together cost $25.5.

### 3.3 Defects

| # | Rd | Id | Sev | Defect | Where | Class | Evidence | In diff? |
|---|---|---|---|---|---|---|---|---|
| 1 | P1 | D-1 | n/s | checkpoint table does not import (fractional EstimatedMinutes) | plan :641/:646 | A | plan's own table | R1 |
| 2 | P1 | D-2 | n/s | plan stale against master: CARD-1607 landed after its base | plan ground truth | D | CARD-1607 `4277f94cb`/`337c94aeb` landed after base `f8bddb6e` | R1 |
| 3 | P1 | D-3 | n/s | no list-all-offenders preview (card item 5) | plan D-5/D-8 | A | scanner exits on first finding, `c590-remote.sh:6454-6480` | R1 |
| 4 | P1 | D-4 | n/s | real refusal families undecided: stale FETCH_HEAD, nested fixture repos, gone-tracking | plan D-2/D-5/D-12 | A | named in the brief, absent from the plan | R1 |
| 5 | P1 | D-5 | n/s | transient census failures unhandled across 6+ census runs | plan D-3/D-8 | A / C | existing wrapper retries Drift only (`deploy-server2.ps1:868-879`, pre-existing) | R1 |
| 6 | P1 | D-6 | n/s | deploy-temp reuses retained temp volumes after a refusal | plan rollout | A | `case_deploy_temp_runner` checks containers only | R1 |
| 7 | P1 | D-7 | n/s | rc/master ordering and port absent | plan rollout | A / E | reviewer: "I found no board text for top-up 38, so the caller must supply it" | R1 |
| 8 | P1 | D-8 | n/s | Windows skip-roster obligations missing | plan slices | A | `WindowsUnitSkipRoster.cs:182-189` | R1 |
| 9 | P1 | D-9 | n/s | doc-pin tests missing from checkpoints | plan S5 | A | `DockerStackDocumentationTests` pins | R1 |
| 10 | P1 | D-10 | n/s | private-origin failure has no red witness | plan witness map | A | none of H1–H6/W1–W5 red at base | R1 |
| 11 | P2 | D-1 | n/s | D-20 deploy gate runs after the wrapper's `/drain/clear`, stranding the pending op | plan :536-545 | **B** (plan round 2's fix of #6) | `deploy-server2.ps1:1196` clear precedes host case | yes (new D-20 text) |
| 12 | P2 | D-2 | n/s | existing tests in changed paths have no disposition or row | plan slices | A | 15 RetiredTempContainerScriptTests and others | partly |
| 13 | P2 | D-3 | n/s | CARD-1615 landing order missing | plan :984-1030 | D | CARD-1615 plan `ddd9946b3` D-11 orders 1609 first | no (external) |
| 14 | P2 | D-4 | n/s | seed classifier admits extra commit headers (gpgsig/encoding payload) | plan D-17 :467 | **F** (hypothetical: needs a crafted Fixture-authored commit in the template path) / B (new D-17) | adversarial A5 only | yes |
| 15 | C1 | D-1 | **blocker** | apply gate runs after `docker stop`/`rm` and the journal, so a default `redeploy-old` removes server2 main's runner and cannot recreate it without deleting volumes | `c590-remote.sh:9462` vs 9416/9428; `deploy-server2.ps1:1383-1386` | **E** / B (`509f20891`, R13) | brief 1fa8a516: "a DEFAULT-OFF gate checked at the DELETE LOOP ITSELF (so EVERY entry point is covered…)" | R1 (whole) |
| 16 | C1 | D-2 | major | gate-off temp apply consumes the Ready preview, so the documented `-RetirementProof` apply can never use it | `c590-remote.sh:9456`, `c1008-retirement.sh:600` | **E** / B | same brief line; reviewer probe P6 | R1 |
| 17 | C2 | D-1 | **blocker** | host volume scans read Docker mountpoints without `sudo`; production `mc` apply always refuses, so the second rollout is stranded; the round-15 fixture masks it (`chmod o+x /var/lib/docker`, applies as the data owner) | `c1008-volume-metadata.sh:11,18,48`; `c590-remote.sh:6068-6069` | **A** (`e69af3177`, R2 cba5ec74, 2026-10-30) / B (mask: `3dbcaa600`, R15) | `git log -S c1609_volume_mount`; probe P1 | **partly**: scan code outside `33ab2cffb..7f801e8dc`; masking fixture inside |
| 18 | C2 | D-2 | n/s | C1210 frozen TRX totals not raised as the native roster grew; Unit test red at the tip; no row selects it | `WindowsUnitPlatformAdmissionTests.cs:331-333` | **A** (roster growth from `678655489`, R0; plan CP-12 selected 3 of 4 C1210 methods) | reviewer ran it: 105 vs 76 | yes (`f57e3c0c4` changed the roster) |

Outside the review rounds, the triage `4b1cc3e7` found the largest CARD-1609 defect. "Production
deletes DISABLED" was enforced nowhere in code: the runbook gate-9 command
(`docs/docker-stack.md:130`) runs Preview and then Apply, and the Apply deletes the four temp
volumes. I classify it A (the plan never required a code gate) with E secondary (the operator's
design rule never reached the plan as a requirement). Neither plan review flagged it. Also outside
the numbered defects: `52590db3` item 8 found master+1609 red on
`Policy_rows_require_pinned_attribute`, because master's `de1a52dd7` had deleted two baseline
tests (D).

## 4. CARD-1545 (rc scoped lanes, live-Claude lane)

### 4.1 Rounds

The three plan reviews (`a62610b7`, `917262f2`, `7973fc74`) are not bound to the card on the
board. The S1c brief names them as the plan's reviews.

| Rd | Task | Subject | Scope used | Verdict | Cost |
|---|---|---|---|---|---:|
| PR1 | a62610b7 | plan `a7a666c72` | whole plan | 7 defects | $4.0 |
| PR2 | 917262f2 | plan amendment `f11f05c02` | amendment vs PR1 | 4 new defects | $2.3 |
| PR3 | 7973fc74 | plan amendment `55036bf9f` | R2-D-1..4 only | 2 new defects | $3.0 |
| S1b-1 | 228e67c0 | S1b `45c24215e` | whole slice; 9 rows, 8 mutants | 3 defects | $6.5 |
| S1b-2 | 3ef5e873 | S1b fix `f41be02bd` | the fix plus 66 probes, 10 mutants | **Clean** | $5.7 |
| S1c-1 | 95bc3d62 | chain `c7a7b2c9e` (4 slices) | whole chain; companion rows; 17 mutants; no Unit lane | 5 defects | $23.0 |
| S1c-2 | 1e52322c | chain fix `2c550140e` | the fix plus **the first whole Unit lane** (6,341 results) | 6 defects | $21.2 |
| S1a/SAN-1 | 2a89d63d | S1a `9a05dbfcd`, SAN `5d719f5f0` | whole slices; rows; mutants; Interim (host saturated) | 6 defects | $22.0 |
| Combo-1 | e38d6427 | combined `0b903dd9d` (reviewer-built merge) | combination, prior defects, new attacks, whole Unit lane | 7 defects | $34.0 |
| Combo-2 | cffa0c4b | `330c6004` (f9d4a394) | fix diff, census attacks, sink attacks, Unit lane | 2 defects | $25.1 |
| Combo-3 | 185b71e2 | `ffeaa28f0` (6fb028cd) | running | — | — |

Fix rounds: `a848e56c` (S1b), `9f33b962` (S1c 1), `f6dfc56e` (SAN 1), `5dd200c3` (S1a 1),
`ee22148a` (S1c 2), `f9d4a394` (combined 1) and `6fb028cd` (combined 2). Together they cost
$414.0, against $314.7 for slice implementation and $137.5 for code reviews.

### 4.2 Plan-review defects (summary)

| # | Rd | Id | Defect | Class |
|---|---|---|---|---|
| p1–p7 | PR1 | D-1..D-7 | startup method vacuous without control; CP-5/7 screen-only; `--dangerously-skip-permissions` instructions outside fingerprint; CLI identity on cl-wrapper route; dedicated profile vs hard-coded OS profile; mid-run outage reads RED; CP-4..7 evidence bypasses sanitizer | A ×7 |
| p8 | PR2 | D-1 | portable rows never build Agents.Pty.Tests (new false-green path) | B (amendment) |
| p9 | PR2 | D-2 | FullCensusUnchanged self-compare cannot go red | B (amendment). **Same class as Combo-2 D-1, two days later** |
| p10 | PR2 | D-3 | sanitizer witness passes on absence | B |
| p11 | PR2 | D-4 | mandatory `--setting-sources ""` breaks CP-6 premise | B |
| p12 | PR3 | R3-D-1 | census is a permanent Unit test pinned to a pre-land seed; any later policy edit turns it red | B (amendment). **Same class as Combo-1 D-3** |
| p13 | PR3 | R3-D-2 | CP-29 requires markers in console.log, which the tool never writes | B |

### 4.3 Code-review defects

| # | Rd | Id | Sev | Defect | Where | Class | Evidence | In diff? |
|---|---|---|---|---|---|---|---|---|
| 1 | S1b-1 | D-1 | n/s | Failed row with absent/unknown Setup becomes INCONCLUSIVE | `live-lane-outcome.ps1:153-155,64` | A (b33a52c8/1989765a) | direct classifier calls | R1 |
| 2 | S1b-1 | D-2 | n/s | one post-failure probe relabels every failed row as outage | `live-lane-run.ps1:179-186` | A | two-row probe | R1 |
| 3 | S1b-1 | D-3 | n/s | raw-child-exit false-GREEN guard has no test (M7 survives) | `live-lane-outcome.ps1:185` | A | mutant M7 | R1 |
| 4 | S1c-1 | D-1 | High | CP-17 red at tip: S1c-startup broke S1c-executable's caller audit | `ClaudeRemoteControlAtStartupCanaryTests.cs:28-38` | **D** (slices of one card, each ran only its own rows) / A (`796579546`) | checkpoint run red | R1 |
| 5 | S1c-1 | D-2 | High | S1c refusals don't use S1b's exit-20; setup and outage become RED | `LiveClaudeLaunchContext.cs:30-35` | **D** (S1c vs master S1b contract) / A | no producer-to-recipient test | R1 |
| 6 | S1c-1 | D-3 | Medium | profile admission and startup canary require mutually exclusive hook commands | `LiveClaudeProfile.cs:136-146` vs canary :165-181 | **D** (S1c-profile vs S1c-startup) | each slice witnessed its own shape | R1 |
| 7 | S1c-1 | D-4 | Medium | C680 config-dir seam stops at the child; in-process tailer reads ambient `~/.claude` | `ClaudeDialogWatchdogCanaryTests.cs:49-60` | A (f72769c9) | | R1 |
| 8 | S1c-1 | D-5 | Low | three guards with no red test | `LiveClaudeProfile.cs:79,142`; `LiveClaudeStartupEvidence.cs:66` | A | mutants P2/P3/S2 | R1 |
| 9 | S1c-2 | D-1 | High | wildcard `../Shared/LiveClaude/*.cs` Compile breaks the checkpoint `coverage` verb; 2 Unit tests red | both test csproj | **A** (`d7702d1cb`, S1c-executable b0b6ec31) | same tool DLL clean on master, invalid on tip | **no**: outside `c7a7b2c9e..2c550140e`; S1c-1 had whole scope but ran no Unit lane |
| 10 | S1c-2 | D-2 | Medium | setup-skip mapping has no witness and fails open | `LiveClaudeOutcome.cs:8-15` | **B** (`699029a5a`, 9f33b962) | mutants S1/S4/S5 survive | yes |
| 11 | S1c-2 | D-3 | Medium | the mapping turns post-control assertions into INCONCLUSIVE | canary :78-101 | **B** (9f33b962) / A (plan R-D-6 vs test design V-18 disagree) | | yes |
| 12 | S1c-2 | D-4 | Medium | tailer-root wiring proven only by a source substring | `LiveLaneIsolationTests.cs:32-44` | **B** (hollow witness for S1c-1 D-4) | P6/P8 survive | yes |
| 13 | S1c-2 | D-5 | Low | delegation audit guards X2/X4/X6/P3 unwitnessed (X6 = PATH `claude` in helper) | `LiveClaudeExecutableAudit.cs:37,166` | **B** (9f33b962) | mutants survive | yes |
| 14 | S1c-2 | D-6 | Low | process-spawning witness in a Unit class without the limiter; CP-23 count stale | `LiveLaneExecutableTests.cs:56-137` | **B** (9f33b962) | | yes |
| 15 | SAN-1 | D-1 | high | V-6/V-7 audits miss bare raw sinks (`Console.WriteLine(screen)`) | `LiveClaudeEvidenceWiringTests.cs:242-267` | **A** (3a7c132f); no current leak | mutants SM-S1/S2 survive | R1 |
| 16 | SAN-1 | D-2 | medium | projection emits credential-shaped tokens verbatim | `LiveClaudeEvidenceSanitizer.cs:41-69` | **A**; latent (today's callers pass benign values) | Add-Type probe | R1 |
| 17 | SAN-1 | D-3 | medium | CP-29 scanner trusts mutable context sentinels and producers; ignores file names | `live-lane-evidence.ps1:176-183,448-450` | A | probes P1/P2/P8/P10 | R1 |
| 18 | S1a-1 | D-4 | medium | CP-30 credits an export whose producer row went red | `test-live-lane-manifest.ps1:81-97` | A (041adc14) | happened in this review | R1 |
| 19 | S1a-1 | D-5 | low | census writes outside the evidence dir, never cleans | `live-lane-manifest.ps1:2113,2416,2526` | A | 119 MB in `/tmp` | R1 |
| 20 | S1a-1 | D-6 | medium | ExactCoreRoster 150 s child budget kills discovery under load | `LiveLaneInventoryTests.cs:64` | A / G (fails only on saturated host; 150 s is 1.65× measured, under the 2× p95 rule) | | R1 |
| 21 | Combo-1 | D-1 | High | merged tree does not compile: chain's 15 literal items drop SAN's 2 new files | both csproj | **D** / B (literal items were the fix for #9, `eab4a6842`) | build CS0103 | yes (combination) |
| 22 | Combo-1 | D-2 | High | CP-20 red: SAN audit counts the chain's `"20 " + setup` skips as raw | `LiveClaudeSinkAudit.cs:70-72` | **D** / B (skips added by #10's fix) | run red | yes |
| 23 | Combo-1 | D-3 | High | CP-30 red on any merge: frozen seed vs master policy changes and other slices' UIDs | `live-lane-manifest.ps1:2372` | **D** / A (S1a design; predicted by PR3 R3-D-1 and by SAN-1 A-7) | run red | yes |
| 24 | Combo-1 | D-4 | Medium | sink audit misses thrown exceptions, `Skip.Test`, `Assert.Fail`, Trace, file-name sinks, helpers | `LiveClaudeSinkAudit.cs:68-76` | **F** (brief: "push each through EVERY sink including a thrown exception, a skip message, a truncated long value and a file name"); no canary has these shapes | mutants N1–N8 | yes (audit file) |
| 25 | Combo-1 | D-5 | Medium | base64 refusal skipped for any value containing `_` | `LiveClaudeEvidenceSanitizer.cs:91` | **B** (`255450b6c`, f6dfc56e fix of #16); latent secret-leak path | probe | yes |
| 26 | Combo-1 | D-6 | Low | parallel Unit test mutates process-wide `CLAUDE_CONFIG_DIR` | `LiveLaneIsolationTests.cs:65` | **B** (`8594a8a8a`, ee22148a witness for #12) | not observed | yes |
| 27 | Combo-1 | D-7 | Low | combined plan's CP-23 stale; CP-24 Min 5 unreachable | plan | **E** (brief e38d6427: "resolve the ONE inherited add/add on the plan doc once (take blob 542b9380…)", which dropped the chain's amendment `96132b74`) / A (CP-24 Min 5) | | yes |
| 28 | Combo-2 | D-1 | High | re-seeded CP-30 compares the combined tree with itself (pre-S1 = `2e5b78808`, the branch's own tip; 33 methods undeclared) | `live-lane-manifest.ps1:8`, fixture | **E** (brief f9d4a394: "RE-BASE the census seed on the combined tree through the S1a census's own documented re-seed procedure") / B (`ae885bfe9`; the documented procedure says master-based) | fixture equals candidate 20717/20717 | yes |
| 29 | Combo-2 | D-2 | Medium | sink audit misses alias, method-group, `?.`, StreamWriter, `Skip.When`, StringBuilder taint and similar | `LiveClaudeSinkAudit.cs` | **F** (brief cffa0c4b: "try NEW attacks: … via a local alias, via a method group, in a file name"); reviewer: "the canary methods contain none of these shapes today" | reflection probe | yes |

## 5. CARD-1514 failed-first and stopped-run seed series

### 5.1 Rounds

| Rd | Task | Subject | Scope used | Verdict | Cost |
|---|---|---|---|---|---:|
| R1a | b5c88cd9 | FF S2+S3+S3b `e805b7848` (9058bae5, 9294cb43, 8d80dbe9) | range `3fb3c113e..e805b784`; 13 rows; property probe | 1 defect | $10.1 |
| R1b | 1bf477d5 | seed builder `a00f1b467` (f6aabaef) | whole slice; probes; 23 rows (Interim, hard-wall) | 3 defects | $16.1 |
| R2 | 460ee5a4 | combined `4390c697e` = digest fix 79ae55d8 + seed fix d63efd0a | both fixes plus their combination | 4 defects (1 HIGH) | $12.4 |
| R3 | 1e04fe29 | `d92701adb` (91dd821a) | fix diff, packer census, property test | 3 defects (1 HIGH) | $10.9 |
| R4 | 4ec701ad | `3360b94d1` (f7273caf) | fix diff, real packer, 1,000-case property test | **Clean** | $8.2 |
| R5 | 1634b300 | method-level ordering (69381933) | running | — | — |

Fix rounds:

- `8d4a4d7b`: R1a D-1 witness.
- `79ae55d8`: R1a **advisory A-2** turned into a behaviour fix.
- `d63efd0a`: R1b defects.
- `91dd821a`: R2 defects.
- `f7273caf`: R3 defects.
- `69381933`: the first real-data run (`78641295`) showed class-level mapping puts 235 chunks in P0, not about 25. This is a requirement refinement that no review predicted.

The fix rounds cost $336.6 in total, implementation included. The reviews cost $57.7.

### 5.2 Defects

| # | Rd | Id | Sev | Defect | Where | Class | Evidence | In diff? |
|---|---|---|---|---|---|---|---|---|
| 1 | R1a | D-1 | n/s | no producer-to-recipient witness for previous-run order or regroup fallback | `nightly-dispatch.ps1:93,176` | A (S3, 9294cb43/8d80dbe9) | mutants M9/M13 survive | R1 |
| 2 | R1b | D-1 | High | builder looks for planned manifests only under the run dir; real layout puts them in sibling `timing-<runId>`; operator export exits 2 | `nightly-stopped-run-seed.ps1:89-98` | **A** (f6aabaef) | rearranged fixture refuses | R1 |
| 3 | R1b | D-2 | Medium | builder takes 20–25 min at real scale (per-item ancestor `Get-Item`) | `nightly-failure-history.ps1:8-20` | A | 741 files in 197 s | R1 |
| 4 | R1b | D-3 | Low | one unreadable/changing input aborts the whole export | `nightly-stopped-run-seed.ps1:149-151` | A | chmod-000 probe | R1 |
| 5 | R2 | D-1 | HIGH | on the combined tree the new digest guard replaces every operator seed with canonical order; top-up 52 would silently lose failed-first | `nightly-dispatch.ps1:105-116` | **D** (79ae55d8 `6a09884a` vs d63efd0a, built in parallel) / E (the orchestrator turned R1a's non-blocking A-2 into a behaviour change and told 79ae55d8 "Do NOT touch the stopped-run seed work") | CP-1 125/122/3 | yes (combination) |
| 6 | R2 | D-2 | Low | nothing witnesses the speed fix (SM2 survives) | `test-nightly-stopped-seed.ps1` | **B** (d63efd0a, hollow fix of #3) | mutant | yes |
| 7 | R2 | D-3 | Low | nothing witnesses the summary-prefix tamper recheck | `nightly-stopped-run-seed.ps1` | **B** (d63efd0a) | SM7 survives | yes |
| 8 | R2 | D-4 | Low | Windows link cache uses inode 0, so a swapped ancestor is accepted | `nightly-failure-history.ps1:98-111` | **B** (regression by d63efd0a's speed fix; "the base code before D-2 caught a junctioned ancestor") / F (needs a concurrent writer during a 10 s export: hypothetical) | forced-Windows probe | yes |
| 9 | R3 | D-1 | HIGH | `seed-plan-regrouped` throws away seed order when any chunk's class set changes (39 of 262 chunk ids at top-up 52) | `nightly-stopped-run-seed.ps1:469-474` | **E** (brief 91dd821a: "say what the seed verifies in place of the digest, and **that a regrouped plan still falls back**") / B (`d1e41d83e`) | real-packer census; seed-only branch kept order | yes |
| 10 | R3 | D-2 | Low | no witness for the normal-shape guard (M2 survives) | `nightly-dispatch.ps1:9,13` | B (91dd821a) | | yes |
| 11 | R3 | D-3 | Low | "original order on fallback" witnessed only without priority members | `nightly-stopped-run-seed.ps1:501` | B (91dd821a) | M6a/M6c survive | yes |

### 5.3 Adjacent series named in the brief (not counted in the headline 44)

| # | Rd | Id | Sev | Defect | Class |
|---|---|---|---|---|---|
| t1 | 877889c5 @`24415a6bb` | D-1 | High | new Windows tree walk drops the start-time ordering guard; a stale parent pid can make a `git push` timeout kill the AppHost | A (2fdc7fb5 `c70d88af9`); real regression vs .NET `Kill(entireProcessTree)` |
| t2 | 877889c5 | D-2 | Medium | pid-reuse guard and self-exclusion unwitnessed; no seam | A |
| t3 | 877889c5 | D-3 | Low-Medium | an exited root is still walked by pid on Linux | A |
| t4 | 877889c5 | D-4 | Low | failed snapshot silently becomes a root-only kill | A |
| t5 | ccbcfec4 @`426211ca2` | D-1 | Low | owned-root-only fallback cannot go red (Mf survives) | B (8562dc2a) |
| t6 | ccbcfec4 | D-2 | Low | Windows snapshot pairs a T0 parent edge with T1 ticks | F / B (millisecond window, newborn victims only) |
| m1 | ef6babe1 @`b1c6889fa` | D-1 | release-blocking | re-created CARD-1619 migration id makes every rc-migrated DB fail startup with `42701` | **E**: brief a584c99e: "the CARD-1619 migration needs a new timestamp/ordering after it and a regenerated model snapshot" / B (`00ee6f4c3`) |

## 6. Genuine blockers and what they would have cost if shipped

| Blocker | Found by | Class | Cost if shipped |
|---|---|---|---|
| Production deletes not gated in code | triage 4b1cc3e7 (not a review) | A/E | runbook gate 9 deletes the four temp volumes including unpublished work (data loss) |
| Gate after `docker stop` (C1 D-1) | c1dc6681 | E | first rollout step 6 removes server2 main's runner; recovery only by deleting host volumes |
| Gate-off apply consumes preview (C1 D-2) | c1dc6681 | E | second-rollout apply via the documented command impossible |
| Host scans without `sudo` (C2 D-1) | 52590db3 | A | retirement always refuses; next deploy-temp refuses `TempVolumesRetained`; **second rollout stranded** |
| Migration id re-created | ef6babe1 | E | new rc fails at startup with `42701`; CARD-1610 migrations rolled back; server cannot start |
| Digest guard drops seed order | 460ee5a4 | D/E | top-up 52 Windows Full accepted with canonical order; **failed-first silently lost**, a qualification day |
| `seed-plan-regrouped` | 1e04fe29 | E/B | same silent loss by another path |
| Census compares the tree with itself | cffa0c4b | E/B | CP-30 a **vacuous gate**: proves nothing about S1 vs master |
| Combined tree does not compile; CP-20/CP-30 red | e38d6427 | D | Merge blocked (caught by build and rows) |
| Wildcard Compile breaks the coverage verb | 1e52322c | A | plan-coverage lint unusable for two projects; 2 Unit tests red |
| Windows tree kill can kill the AppHost | 877889c5 | A | a land `git push` timeout on Windows could kill the AppHost tree |
| Sanitizer emits credential-shaped values (`_` bypass) | 2a89d63d D-2, e38d6427 D-5 | A, B | secret-leak path; no current caller passes such values (latent) |

None of the blockers is G. None is C. C appears only as a secondary cause (#5 of CARD-1609).

## 7. Diff-only and "blockers only after round 3": replayed on these cards

**In diff.** Of the 24 code-review defects found in rounds ≥2 across the three series:

| Series | Code defects in rounds ≥2 | Inside the previous round's fix diff or the combination diff |
|---|---:|---:|
| CARD-1609 (C2) | 2 | 1, plus 1 partly (C2 D-1) |
| CARD-1545 (S1c-2, Combo-1, Combo-2) | 15 | 14 |
| CARD-1514 (R2, R3) | 7 | 7 |
| **Total** | **24** | **22, plus 1 partly** |

The plan re-reviews (1609 P2, 1545 PR2/PR3) found 10 more. All but 1609 P2 D-3 (the external
CARD-1615 order) sat in the amended text.

The two that a strict diff-only reading misses:

- **1545 S1c-2 D-1 (glob).** Its code dates from the first slice. S1c-1 had whole-change scope
  but did not run the Unit lane. The defect was found only by the whole Unit lane, so a diff-only
  round that keeps one whole-lane sweep per subject still finds it.
- **1609 C2 D-1 (sudo).** The defective code dates from R2 (`e69af3177`). C1 had whole scope and
  missed it, because every fixture applied as root or as the data owner. C2 found it while
  checking round 15's custody and fixture change, which was in the diff. A diff-only reviewer
  would need to follow the masking `chmod o+x` back to the code it hides. That is plausible, but
  not guaranteed.

**Blockers-only from round 3**, and max 3 rounds per subject:

- **CARD-1609.** 2 code rounds, so nothing changes. The review cost was never the problem: $25.5
  of $1,034. The cost was in R0–R12, implementation continuations that this policy does not touch.
- **CARD-1514 seed.** R3 (`1e04fe29`) still reports its HIGH D-1. Its two Lows (#10, #11) become
  advisories. R4 (`4ec701ad`, Clean, $8.2) and the running R5 do not happen; a human decides
  after R3. **Nothing would have been lost:** R4 found nothing.
- **CARD-1545, counting per subject (the chain).** Rounds are S1c-1, S1c-2, Combo-1. In Combo-1,
  blockers-only keeps D-1, D-2 and D-3 (build and rows red) and drops D-4 (F), D-6 and D-7.
  - D-5 (the `_` base64 bypass) is kept only if a latent secret-leak path counts as a blocker.
  - **Combo-2 (cffa0c4b) does not happen. Its D-1 is lost:** the vacuous self-compare census,
    which was introduced after round 3 by the f9d4a394 fix. A human landing f9d4a394 unreviewed
    would have shipped a CP-30 that proves nothing. The cost is a vacuous diagnostic gate: no
    data loss, no outage.
  - Counting the combination as a new subject instead makes Combo-2 its round 2, and the defect is
    found.
- **Saved.** The F work in f9d4a394 (D-4) and 6fb028cd (D-2) would not have been commissioned.
  So would the even longer attack list in the 185b71e2 brief (reflection, dynamic, Task.Run
  closures).
- **Advisories.** The orchestrator turned one advisory, `b5c88cd9` A-2, into task 79ae55d8. That
  task produced the HIGH 460ee5a4 D-1. A "blockers only, advisories become cards" rule would have
  prevented that regression outright.

**Rounds spent on F/G/advisories:**

- No review round was spent only on F or G.
- Parts of three fix rounds were: f9d4a394 (D-4 sink shapes), 6fb028cd (D-2 sink shapes) and
  79ae55d8 (the advisory).
- CARD-1609 R15 spent its effort on load flakes and a fixture issue (G) plus advisory M5.
- CARD-1609 R0–R12 lost most of their budgets to build-slot waits and fixture and timing defects,
  per the triage. Those are G-like, but they are implementation continuations, not review rounds.

## 8. Recommendation for the operator's policy

**What the policy would save on these cards:**

- one Clean review: 1514 R4;
- one running review: 1545 Combo-3;
- the F expansions in two fix rounds;
- the advisory-driven fix (79ae55d8) and the HIGH regression it caused.

**What it would lose:** at most one vacuous-gate defect (1545 Combo-2 D-1), and only if the
combination counts as the same subject.

**Its blind spot.** Four of the most expensive blockers were E: the defect was written into a fix
or port brief. A max-3 rule does not remove E; it makes it more dangerous, because round 3's fix
is never reviewed.

The brief template should say:

1. **Round 1 is the whole change. Rounds 2 and 3 are the diff `<previous reviewed SHA>..<tip>`**,
   plus any file that diff touches. The combination of parallel branches is reviewed as the diff
   from each branch's reviewed tip to the merged tree (D was 7 of 44).
2. **One full ordinary sweep per subject**, including the whole Unit lane once, in round 1 or
   when the subject first lands as a combination. Without it, round 1 missed S1c-2 D-1.
3. **Report blockers only** from round 2 on (data loss, secret leak reachable by a current caller,
   regression, vacuous gate, server cannot start, stranded rollout, silently lost feature). Anything
   else is an advisory, and **an advisory becomes a card, never a fix task in flight**.
4. **No new attack classes beyond the plan's threat model** unless the reviewer shows a path
   reachable with today's callers. Orchestrator briefs must not add attack lists ("try NEW
   attacks…") after round 1.
5. **Fix briefs state the defect and the invariant, not the mechanism.**
   - When the orchestrator proposes a mechanism, it is marked "suggestion: verify against
     <owner doc/plan section>".
   - The next review checks the brief's prescription as well as the code.
   - Every E defect (five in the three series, plus the port's migration id) came from a
     prescriptive line: "at the DELETE LOOP ITSELF" (two defects), "a regrouped plan still falls
     back", "RE-BASE the census seed on the combined tree", "take blob 542b9380" and "needs a new
     timestamp".
6. **Every fix carries a red-first witness that fails on the old code.** Of the 12 B defects, 7
   were hollow fixes or missing witnesses for the fix itself.
7. **Fixtures model the production principal and permissions.** Name the production user, as in
   the CARD-1609 `mc` and `sudo -n` rule, and run a real-data probe (as `78641295` did) before the
   last review, not after.
8. After round 3 the reviewer writes a **decision page** (open blockers, what is unreviewed since
   the last reviewed SHA), and a human decides.

## 9. Uncertainties

- The plan reviews for CARD-1545 are unbound to the card on the board. I included them from the
  S1c brief's citation, so other unbound reviews may exist that I did not find.
- "In diff" judgements come from the commit that first carried each defect (`git log -S`) and from
  the review's stated range. I did not re-run any review.
- Severity labels are the reviewers'. Several reviewers stated none (n/s).
- Costs are the recorded `costUsd` (Grok rows are priced by the server's own table).
- `185b71e2`, `1634b300` and `ae950679` were still running. Their outcomes are not counted.

## Not done, noted

- Fix idea (one line): adopt §8 items 1–8 as the Review and fix-brief template text, and add a
  brief-lint check that flags mechanism-prescribing sentences in fix briefs.
