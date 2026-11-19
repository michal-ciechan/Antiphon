# CARD-1972 Review round 1 (task b2ef9c4f)

Subject: origin/feat/card-task-26e01f09 tip 1ba17eb338033b35db28aa8c05307df97214b825 (Code owner 26e01f09).
Range: 81add8595d60295b4bf1d3bb76af2e927842421f..1ba17eb338033b35db28aa8c05307df97214b825 (4 commits).
Verdict: CLEAN. No blockers. Five advisories (A-1..A-5) for cards; none holds the land.

## Rerun evidence

Checkpoint run 20261119-052316-1158 at 1ba17eb33, one build (bin-c1972-a, deleted), `validate` -> `CHECKPOINT SOURCE VALID source=1ba17eb338033b35db28aa8c05307df97214b825 rows=8` (exit 0). Unlisted: none.

| CP | Result |
|---|---|
| CP-1 AgentTaskGetPollMetricsTests | executed=7 passed=7 failed=0 skipped=0 |
| CP-2 DbRateSampler + DbCallSiteMetrics | 11/11 |
| CP-3 TestClassificationPolicyTests | 42/42 |
| CP-4 DerivedImpactIndexTests | 27/27 |
| CP-5 guard union (7 classes) | 34/34 |
| CP-6 SlowTestRegistryMergeTests | 5/5 |
| CP-7 land graph -Check | exit 0, approved |
| CP-8 check-evidence-diff 81add8595..HEAD | exit 0, commits=4 entries=0 violations=0 |

The guard batch the brief names (TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness) is wholly inside CP-3/CP-5/CP-6, so no separate run was made.
merge-tree --write-tree a789c87770d3 1ba17eb33: clean, tree a7d679c4096cd6b80f2e1f0321145496faa2380c. Master's 9 commits since the base touch tests/derived-impact-index.json in other entries only; merged Antiphon.sln count is 333 (master 332, subject 333), consistent.

Red witness: not rerun (one more ~5 min build). Read at 446db1876: the stub's Record is a no-op, Read returns zeros/empty with StartedAt=MinValue, and AgentTaskEndpoints has no `polls.Record(`; each of the seven witnesses asserts a nonzero count, a label row, StartedAt near now, or the wiring string, so 7 failed / 0 passed is the expected outcome for the stated reason.

## Blocker checks (all pass)

1. Unbounded growth: none. Record takes `lock (_gate)`; the `_labels.Count >= MaxLabels` check and the insert are under the same lock, so the 64 cap holds under concurrency. Each label is at most 64 chars, so the store is at most 64 x 64 chars plus three longs.
2. Secret through the label path (Antiphon credentials): not reachable. Task and capability tokens are `Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()` (AgentTaskService.NewToken, 64 hex chars) and are refused by the 32+ hex rule. Session and task ids are guids, refused by `Guid.TryParseExact(text, "D")`, "N" form by the hex rule, "B"/"P" by the charset. The caller-derived labels hold only AgentKind, a capability name that passes the same shape rule, or the constant `session`/`capability`. See A-1 for secrets Antiphon does not mint.
3. Per-request Postgres query: none. Record calls the static Label and a dictionary under a lock. The route passes the `caller` from the existing `ResolvePollingCallerAsync`, and nothing new reaches the DbContext. The diagnostics route reads memory only.
4. Guards that cannot fail: the counting witnesses W-1..W-6 assert exact rows and counts. The guid and 32+ hex refusals would go red if removed: W-4 expects `Callers == [kind:Grok]` and `Unknown == 1`, and either removal stores the value as a label. See A-4 for the dead assertions.

Access control: `/api/diagnostics` is one `MapGroup` with no authorization, and the new route is in that group like `/session-state` and `/db-rate`. Same posture.

## Advisories (cards, not land holds)

A-1 Non-hex secrets pass the label shape.
Where: server/Application/Services/AgentTaskGetPollMetrics.cs TryScript.
Failure: a client that puts a third-party or operator-configured secret in `X-Antiphon-Caller` publishes it, lowercased, on the unauthenticated `GET /api/diagnostics/agent-task-polls` for the process lifetime. Examples that pass: a GitHub `ghp_` + 36 alphanumerics (40 chars), an `sk-` + 48 key, a base64url or alphanumeric operator token or phone-home SharedSecret of 64 chars or fewer that starts with a letter. Only the hex rule catches random strings.
Why advisory: no client sends the header today, Antiphon's own tokens are refused, and the leak needs a caller to put a secret in a header named "caller".
Fix: accept only registered labels (an allowlist of script/client names), or refuse high-entropy shapes (for example a 20+ char run with no `.`/`-`/`_`, or known prefixes `ghp_`/`gho_`/`sk-`/`xox`). Then make docs/logs.md say "keeps no Antiphon token" rather than "does not keep tokens".

A-2 The first 64 labels win for the process lifetime, including over server-derived labels.
Where: Record. Server-derived `kind:*`, `session` and `capability:*` labels share the 64 slots with client header labels.
Failure: any client that reaches the detail route (`ResolvePollingCallerAsync` maps Forbidden to null, so an anonymous caller is counted) can send 64 junk labels. Every later real caller, `kind:Claude` included, then lands in `overflow` until restart. Memory stays bounded, so this poisons the attribution rather than exhausting memory.
Docs: ops-http.md says "further new labels increment `overflow` and are not stored", which is accurate, but it does not say that server-derived labels compete for those slots.
Fix: give the server-derived labels (a closed AgentKind set plus `session`/`capability`) their own slots outside the cap, and document first-come retention.

A-3 Small per-request allocations; global lock.
Where: Label/TryScript and Record. The code calls `raw.Trim().ToLowerInvariant()` before the length check, so a large header is copied up to Kestrel's header limit. It also builds `"kind:" + task.AgentKind.ToString()` on every task-token poll and allocates a boxed enumerator in `text.All(...)` for strings of 32+ chars. These costs are negligible beside GetAsync's own DB read.
The CARD-1987 DbCommandMetrics convention is lock-free (`ConcurrentDictionary<string, StrongBox<long>>` + `Interlocked`). This type follows the DbCallSiteMetrics `lock` style instead, which is acceptable.
Fix: check `raw.Length > 64` first, cache the kind labels, and optionally move to the DbCommandMetrics pattern.

A-4 Witness gaps.
- C1972_detail_get_and_diagnostics_route_are_wired runs `detail.Replace("polls.Record(", "polls.Skip(").ShouldNotContain("polls.Record(")` and the same pattern for `polls.Read()`. These compare the input with itself and cannot fail. The preceding ShouldContain lines carry the guard.
- Removing `text == UnknownLabel` from TryScript survives W-3. Header `unknown` with a null caller still reaches `_unknown` through Record's `label == UnknownLabel` branch. No witness sends `unknown` with a resolved task caller, which is the case that would show the difference: it should fall through to `kind:*`.
- No witness pins "the list route is not counted"; it holds only because the list route does not call `polls.Record`. W-7 checks for presence in the detail slice only.
Mutation execution is waived (operator 2026-11-12), so these remain pending and are not discharged.

A-5 Docs accuracy (otherwise accurate against the code).
- ops-http.md "an ASCII letter, then up to 63 letters, digits, `.`, `_` or `-`, stored lowercased" matches `char.IsAsciiLetter(text[0])`, `Length > 64` and the charset loop. "Up to 63" holds after Trim and ToLowerInvariant, so uppercase input is accepted and lowercased; the docs say "stored lowercased", which is fine.
- "At most 64 labels besides `unknown`" matches `MaxLabels = 64` with `unknown` counted separately.
- logs.md "it does not keep tokens" is true for Antiphon tokens and false for a non-hex secret header (A-1).
- logs.md caller notes check out: delegate.ps1 sends `X-Antiphon-Task-Token` (lines 451/454) and no script or client file sends `X-Antiphon-Caller`. With a capability token, delegate.ps1 counts as `capability:<name>`, which the ops-http rule covers.

## Not done / pending

- PCs: none in the plan. Mutation is waived and not discharged.
- No migration (none in the range).
