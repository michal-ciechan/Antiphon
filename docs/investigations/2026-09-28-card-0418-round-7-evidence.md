# CARD-0418 round 7: Final scope audit

Branch: `feat/card-task-38caa745`. This continues the [round 1–6 evidence](2026-09-27-card-0418-code-evidence.md). It is a coverage ledger, not a release verdict. F-5/V-24 belongs to CARD-0784; V-25 needs a later authorized live receipt; PC-1–PC-30 belong to post-land Mutation. The card must not land from this branch.

## Routing failure triage

The round-6 broad routing row was 648/650. On the untouched merge base `a0e73c976a91f8ebf02def495c69fe37df55289f`, the corresponding 11-class routing filter executed 644 cases: 643 passed and `ChannelConsumerIdentityEndpointTests.Returns_effective_overrides_and_only_allowlisted_fields` failed to boot against PostgreSQL with `53300: sorry, too many clients already`. The round-6 attention query method passed at that base. Baseline evidence: `.antiphon/c0418-r7-baseline/routing.trx` and `.log`. The disposable baseline worktree was removed.

The endpoint failure is inherited shared-host capacity. The attention method's old oracle counted unrelated conditional `AgentTaskEvents` queries during a fleet-wide projection and therefore depended on concurrently inserted rows. The production obligation query now carries the `CommitRecoveryObligations.LoadUnresolved` EF tag; the method counts only that exact query and asserts one batched query for one and four obligations. CP-7 subsequently passed all 320 routing and attention cases twice, including the endpoint and attention methods, on two committed slices.

## Round-7 changes

- `ChannelOutboundFileStore.MaterializeSourcesAsync` counts inline source bytes and ZIP-expanded member bytes against one 64 MiB budget. A new storage case proved the old implementation accepted 1 MiB inline plus 64 MiB ZIP (red 1/1 at the intended assertion); after the fix, 1 MiB plus 63 MiB passes and 1 MiB plus 64 MiB is refused.
- `SourceBundleManifestTests` exercises inline/ZIP byte hashes, distinct duplicate basenames, five/six source threshold, exactly 1 MiB, 64 selected paths, 64 MiB omission accounting, stale/unlisted artifacts, and legacy bundle fallback.
- New policy rows exercise disabled/unbound/mismatched/same-agent/pool/always-on/inbound-bound/nondelegatable converter and unusable prompt bindings as atomic refusals. `Raw` is enum zero and EF treats it as the default-value sentinel, so that fixture writes Raw directly and clears tracked values before validation. Positive and negative timeout and queue-limit edges are explicit.
- New deadline and ordinary-task tests cover deadline equality, conditional cancellation of a queued worker, preservation of a Working owner, late result suppression, pinned internal worker creation, and valid generic file additions. These are samples of the wider V-10/V-11 matrices, not their completion.
- A real-browser test invokes the standalone tool in a Docker Chromium fixture, independently parses four pages with Poppler, and retains page images. Its fixture Dockerfiles live beside the synthetic four-source specimen. The image needs `fonts-noto-color-emoji`; the prior local image rendered U+2728 as a missing-glyph box. The test checks that font explicitly. Current rerun status is recorded below.
- The plan now has a closed `### Checkpoints` manifest for CP-1–CP-13. Class-filter success is evidence for the executed cases only; the table does not imply that all V/R assertions exist.

## Checkpoint evidence

| Run | Result |
|---|---|
| `20260928-072408-d762` | CP-1 Unit 3451/3451, 33 platform skips; CP-2 source 332/332; CP-4 file boundary 44/44. |
| `20260928-073658-f689` | Full manifest: CP-2 332/332, CP-3 8/8, CP-4 45/45, CP-6 16/16, CP-7 320/320, CP-9/10 build failed from a test compile error later fixed, CP-11 messaging 122/122, CP-12 channel client 3/3 and CP-13 bundle exit 0. CP-1 had one unrelated `HttpResilienceRegistrationTests` wall-time miss (25 s against 15 s); CP-5's new oracle was premature; CP-8 had the same two `PinnedAgentKindTests` `codex_desktop_unqualified` refusals proved on the prior baseline. |
| `20260928-080659-307e` | CP-7 320/320 and CP-9 renderer 10/10. CP-1 had one unrelated shutdown wall-time miss (999.6344 ms against 1 s); CP-3 and CP-5 revealed fixture assertions subsequently corrected. CP-10 revealed the browser image's PostgreSQL entrypoint. |
| `20260928-083233-f8b7` | CP-1 Unit 3452/3452, 33 platform skips; CP-5 4/4. CP-3's Raw fixture still used EF's tracked ClaudeCode value; fixed by clearing tracking. CP-10 produced a four-page PDF and parsed all headings/body/end sentinels, but U+2728 was an empty box in the image; the font fixture was added. |
| `20260928-085437-f317` | CP-3 19/19. CP-10 produced a four-page PDF with all headings/body/end sentinels and a visible sparkle glyph; the exact-text assertion failed because Poppler places extra spacing before the emoji. A whitespace-tolerant assertion preserves the glyph requirement for the next rerun. |
| `20260928-090228-2dfa` | CP-10 real-browser 1/1; CP-12 channels/attention client 25/25. The PDF was independently parsed as four ordered pages and I visually inspected all four page PNGs: Unicode, headings, body endings, table, code and page starts are visible without clipping. Source hashes stayed unchanged. |

Reports and TRX for these runs are under `.antiphon/checkpoints/<run-id>/`; red evidence is retained. The extra `docker build` under `scripts/build-slot.ps1` was fixture provisioning for CP-10, not an unlisted product test. Its first all-package attempt exited 143 before completion; the split base/font build completed, and `fc-match` resolves Noto Color Emoji. Each round-7 commit was pushed and its full SHA matched `git ls-remote` immediately after the push.

To recreate the browser fixture, build `Dockerfile.browser-base` as `antiphon-card0418-browser:base`, then `Dockerfile.browser` as `antiphon-card0418-browser:latest` with `docker build -f <file> -t <tag> .` under a host build slot. CP-10 runs with `ANTIPHON_HEADED_TESTS=1`; its artifact path is printed in the test output.

The successful V-20 local artifact is `.antiphon/test-output/card-0418/v20-r7/ffca2acc28414852b9176a90e5e495c6/combined.pdf`, SHA-256 `d8c069878eb56a362071223619791f678a6c80c1bab320fa3958864a6f96c32c`; `extracted.txt` and `page-1.png` through `page-4.png` are beside it. This is synthetic local PDF evidence, not a live destination receipt.

## Open ordinary matrix

Passing class filters cannot substitute for the exact plan cases. The following remain open beyond F-5/V-24 and V-25:

| IDs | Unclosed assertions |
|---|---|
| V-1–V-4; R-1/R-2 | Full role/project source eligibility, old bundle history and actual four-source F-2 completion through X/Y/Z with no copied markers. V-2/V-3 have substantial direct byte/manifest checks. |
| V-5–V-9; R-3–R-6 | Complete profile/client error matrix; main/trailing/machine trigger rows; withheld/control caller matrix; admission/commit/runtime-release barriers; concurrent trigger and lease takeover identity. |
| V-10/V-11; R-7 | Full internal-purpose suppression/privilege and ordinary-task companions; refusal categories, converter/global capacity, queue/dispatch race and owner retention matrix. |
| V-12/V-13; R-8/R-9 | PDF/PNG/TXT and unchanged result matrix; file-store I/O denial/partial write, symlink/reparse/swap and remaining ZIP attack rows; complete serialized fallback limits. Existing tests cover many malformed output fields, one combined source budget and exact small wire cap. |
| V-14–V-18; R-10/R-11 | Complete T1/T2 ordering, all C-1–C-8 attention/stamp invariants, multi-target acceptance/completeness, TTL ownership, revocation/prompt revision and final policy race. C-1–C-8 process-death cuts and a real private broker already passed, but these additional assertions are not all present. |
| V-19, V-21–V-23; R-12/R-13 | Windows browser child cleanup, fresh DI/legacy warning, any remaining monitor controls, near-default wire cap and full broker fallback. V-20 is locally green on the real-browser specimen above. |
| R-14 | Evidence-accounting validator cases, separate from the pending actual V-25 receipt. |

No PC is discharged by a local guard-red check. A green Unit lane and green existing integration classes do not close these plan assertions. The next Code slice should add named/data-row evidence for these open entries, then rerun the affected checkpoints; the Final scope cannot be called complete while they remain.

## Round-7 verdict

Incomplete. CP-1, CP-2, CP-3, CP-4, CP-5, CP-6, CP-7, CP-9, CP-10, CP-11, CP-12 and CP-13 have a green run on a production-compatible slice; CP-8 retains two inherited environment refusals. These runs are spread across commits, and the ordinary V/R matrix above still lacks required cases. Do not present the work as "only F-5 remains," and do not land. The next stage is Code for those cases; CARD-0784 can separately provide F-5/V-24 before a later complete Final sweep and Review. V-25 and every PC remain separate later gates.
