# CARD-1813 Review round 3 (Final, blockers only, diff 85815b633..ddb018db1)

Verdict: **CLEAN, no blockers.** The round-2 blocker D-1 is fixed. Both sibling witnesses now locate the cache through `PwshPrivateCache.ConfiguredCacheParent()`, and all three classes are green with only a private TMPDIR and also with an existing absolute `ANTIPHON_PWSH_CACHE_ROOT`. The A-2 and A-3 hardening adds no path that deletes a directory whose owner state is unknown.

Subject: origin/feat/card-task-0e3ac6a5 at tip ddb018db1dcd3f617cf94ad94798b7a169114eda (fix de82fddcd1c178476e8fd40389b8b1628565afec). This was a read-only review; no product or test source changed.

## Evidence (server2 Linux; every build and run leased through build-slot; UseAppHost=false, native output)

| Row | Result |
|---|---|
| build tests/Antiphon.Tests, tests/Antiphon.SessionRunner.Tests @ ddb018db1 | exit 0 / exit 0 |
| PwshPrivateCacheTests* + ScriptHarnessLinuxOwnershipTests*, TMPDIR=TMP=TEMP=/tmp/r04f1/t1, no ROOT | 43/43 passed, 0 skipped |
| Same, TMPDIR=/tmp/r04f1/t2 + ANTIPHON_PWSH_CACHE_ROOT=/tmp/r04f1/root (existing) | 43/43 passed, 0 skipped; supervisor caches were created under the root, so the override was in effect |
| C1793SessionPwshCacheTests*, TMPDIR=/tmp/r04f1/t3 only | 2/2 passed |
| C1793SessionPwshCacheTests*, TMPDIR=/tmp/r04f1/t4 + ROOT=/tmp/r04f1/root2 | 2/2 passed; the sweep stamp was written in root2 and the session cache was deleted at exit |
| Guards: TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests* | 52/52 passed |
| check-evidence-diff 85815b633..ddb018db1 / 364e27133 (merge-base)..ddb018db1 | violations=0 (commits 5 / 10) |
| merge-tree origin/master 0cb7bfa7f with HEAD | exit 0, clean, tree e1bc8424f |
| audit-land-verification-graph -Check on local scratch merge 28e603d03 (not pushed) | exit 0, `approved` |
| tests/test-execution-policy.json; csproj/props/targets | unchanged; none touched |

## Checked and safe
- **D-1 repair.** `ScriptHarnessLinuxOwnershipTests.cs:61` and `C1793SessionPwshCacheTests.cs:53,98` use `ConfiguredCacheParent()`. That helper returns the override, else the temp path, and it is independent of `ChooseRoot`. The new `Cache_parent_lookups_use_the_shared_helper` source guard pins the change. The docs paragraph ("existing ordinary absolute directory") now matches the code, and a Mutation host that sets ROOT to such a directory gets green witnesses.
- **A-2 (hidepid / missing /proc).** A stat read that gets ENOENT now asks `kill(pid,0)`. Only ESRCH gives Absent (sweep). Success, EPERM, or any other errno gives Unknown, and `IsLive` treats Unknown as live, so the cache is kept. The guards `pid <= 0` and non-Linux mean kill is never sent to a process group. Signal 0 delivers nothing. Both sides are witnessed: the EPERM test uses a real foreign pid, and the dead-pid test sees ESRCH and sweeps.
- **A-3.** A relative override returns (temp, sweep=false) before any filesystem call. A missing override is now unusable (`!Directory.Exists`), so the sweep logic no longer calls `CreateDirectory` on it, and the cache falls back to temp with the sweep skipped. Symlink, file, and reparse checks are unchanged.
- **A-1.** The `TryParseProcStartTicks` row with `)` in comm expects 888 and pins LastIndexOf.
- **Deletion paths.** The diff adds no `Delete` call. `SweepStale`, `Release`, and `DeleteOwned` are unchanged. The only change that affects deletion turns some former Absent results (delete) into Unknown (keep).

## Advisories (never hold the land)
- **A-R3-1:** When the override is set but unusable (relative or missing), `ConfiguredCacheParent()` still names the override, so the two sibling witnesses would go red on that host. That is consistent with the doc, which requires an existing absolute directory. Note it in Mutation briefs.
- **A-R3-2:** Supervisor probe runs leave dead-owner `antiphon-pwsh-cache-*` directories in the configured root (9 in /tmp/r04f1/root). The next sweep removes them. This is unchanged from round 2.
- **A-4 (carried):** Windows execution is still owed to rc qualification.

PCs: every PC stays pending for method-scoped SourceLanding Mutation.
Leftovers: /tmp/r04f1 (run logs, TRX, private temp and root dirs, landcheck.log); left in place under the no-rm -rf rule. The scratch merge worktree was removed with `git worktree remove`.
