# CARD-0418 round 8: ordinary matrix continuation

This record continues the [round-7 audit](2026-09-28-card-0418-round-7-evidence.md). It is a partial Code result, not a Final verification or landing verdict. The round-7 open-case table remains authoritative except for the individual assertions explicitly exercised below.

## Source and tests added

- `DeliverableBundleServiceTests.Default_settlement_preserves_sources_without_conversion` now expands across Plan, Docs and explicitly named Custom roles in two project identities. It compares each bundled file's bytes with the source and checks source-only note, null PDF/error and absent render log. Failed and canceled Docs tasks have their own no-bundle rows. The branch-only Git read now compares BOM, CRLF, Unicode and trailing-space bytes, rather than decoded text.
- The optional PDF command rejects staged source files reached through a linked path before reading or launching the browser. A real CLI test creates a fixture-owned directory link to a sentinel outside the staging directory. Other CLI rows check malformed JSON, unknown version, empty documents, absent source, traversal and unknown argument. Renderer rows check escaped cover/path, Unicode arguments and browser nonzero exit with a stale PDF.
- The output-manifest validator matrix now has fixture-owned file and directory links, UNC-shaped paths, empty name/MIME, extra manifest/file fields and an overlong replacement. The near-default wire-cap test uses actual `MessagingJson.Options` serialization with 13 MiB of random original bytes and Unicode metadata; exact 20 MiB stages and +1 byte refuses without a visible snapshot.
- A client test checks that a rejected outbound profile PATCH shows the API validation detail and leaves the selector/profile preview unbound.
- A ten-row trigger matrix probes direct Markdown, manifested ZIP, unmatched ZIP, missing bytes, missing manifest, wrong version and plain text against the production `MatchesMarkdownSources` decision. This does not substitute for the main/trailing/machine route integration in V-6.
- The file-store fault seam now has three rows for partial input write, complete temporary snapshot and final rename collision. Each refuses the staged reply and leaves no exposed partial snapshot. A narrow test-only barrier just before the admission commit supports an independent-connection check that `Deferred` and the correlation FK cannot escape before commit.

## Targeted checkpoint evidence

The checkpoint tool was provisioned under `scripts/build-slot.ps1` because its binary was absent; this was a tool build, not a product test. It reported one existing nullable-reference compiler warning. Each test selection below used the plan's exact checkpoint row and a committed source slice.

| Run | Row | Result |
|---|---|---|
| `20260928-114611-8c06`, `fcd4c90298748dba06596e67eaf1e0ffa568676d` | CP-2 | 339/339 passed. |
| same run | CP-9 | 17/18 passed; my expected HTML path used a literal `ó`, while `WebUtility.HtmlEncode` emitted `&#243;`. The red TRX is retained. |
| `20260928-115238-0054`, `2f4bb585545e29cd184fcbbce53841af3add1218` | CP-4 | 54/54 passed. |
| same run | CP-9 | 18/18 passed after correcting the oracle. |
| `20260928-115504-f591`, `e325654b8d04b0f923c62ed46606af3dbe92faf8` | CP-12 | 26/26 client cases passed across two files. |
| `20260928-115618-717f`, `fd8d521e87eba1195c88a01de74924202bd582ba` | CP-9 | 19/19 passed after the browser-nonzero row. |
| `20260928-115748-b176`, `4898519dbb2a88a732f12fad743505d732667f1f` | CP-3 | 29/29 passed, including ten trigger-shape rows. |
| `20260928-120328-fcc6`, `cbe943b420b0b880fc6086f382088e6c3337111b` | CP-4 | 57/57 passed, including three staging fault rows. |
| `20260928-120546-dbe4`, `8af6cdc58577318ab173a081098922621af365d6` | CP-5 | 5/5 passed, including the before-commit admission visibility barrier. |
| `20260928-121014-a06d`, `54aa71b0af5b46ca8c988fb6fc14a2f493b2025a` | CP-10 | 1/1 real-browser PDF test passed on the current source tip; the same tool and four-source specimen were visually inspected in round 7. |
| same run | CP-1 | 3464/3464 Unit cases passed, 33 platform skips. The build waited for a host slot; no test/build driver bypassed it. |

TRX, command, roster and logs are retained under `.antiphon/checkpoints/<run-id>/`. The client row has no TRX; its console log says `Tests 26 passed (26)` and `CLIENT TESTS EXIT CODE: 0`.

## Remaining ordinary scope

The full V-1–V-19, V-21–V-23 and R-1–R-14 assertions are **not closed**. The new rows narrow V-1/V-2, V-5/V-6, V-13, V-19 and V-23. The following major obligations still need named native evidence:

| IDs | Main remaining assertions |
|---|---|
| V-1–V-4, R-1/R-2 | Full P/Q four-source settlement through X/Y/Z and source provenance across all read paths, including complete ZIP input and actual completion-note routing. |
| V-5–V-9, R-3–R-6 | Endpoint/policy edge matrix, all three send shapes and withheld/control callers, runtime release after admission, concurrent trigger/lease takeover identity. The new before-commit probe covers only the admission visibility boundary. |
| V-10/V-11, R-7 | Internal-purpose side-effect and privilege matrix with ordinary companions; refusal, capacity, queue and dispatch race rows. |
| V-12/V-13, R-8/R-9 | Further I/O, partial-write and link-swap fault injection, serialized fallback, malicious prose and exact route/byte recovery. |
| V-14–V-18, R-10/R-11 | Frozen T1/T2 routing/order, C-1–C-8's full state/receipt oracles, multi-target acceptance/completeness, TTL ownership, revocation and final policy race. |
| V-19, V-21–V-23, R-12/R-13 | Windows browser child cleanup, fresh DI/legacy warning, all monitor controls, full broker fallback and complete near-default wire publication path. |
| R-14 | Evidence-accounting validator, including actual-red/restored-green and live-gate separation. |

F-5/V-24 remains pending **elsewhere** in CARD-0784. V-20's local Chromium/PDF evidence remains green from round 7. V-25 remains the authorized live acceptance gate. PC-1–PC-30 remain for method-scoped SourceLanding Mutation; none is discharged by these green rows. A full Final sweep is premature until the ordinary matrix is implemented, then it must run CP-1–CP-13 on one committed tip. CP-8's two `codex_desktop_unqualified` failures were reproduced as inherited in round 7; retain that classification, without treating them as new regressions or silently calling CP-8 green.

No land or shared-stack restart is authorized from this round.
