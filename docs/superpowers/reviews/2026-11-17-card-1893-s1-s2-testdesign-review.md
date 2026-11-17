# CARD-1893 S1/S2 test design review, round 1

Subject: `docs/superpowers/plans/2026-11-17-card-1893-s1-s2-testdesign.md` at
`origin/feat/card-task-e7394f29` tip `c88fc182d965b65a8154545f6431d5e7c5289430` (Grok).
Base: merge-base `321f98ec990a575f77e442ba3b1554f0fde33521`; master `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.
Activation plan: `origin/feat/card-task-a1d414b6` `5c1a0d5023b9aada4b62c1ff85d4be42e5b72bbe`.
Reviewer: Claude Opus (Review f29c3d90). Scope: blockers and regressions only.

**Verdict: BLOCKERS (3).** The compose and doc red witnesses, the checkpoint table, the guard
roster and the slice order are sound. The hold-script fixtures and the upgrade-broker harness
are not.

## Blockers

### B-1: the NOT-AFFECTED witness requires the hold script to fail open

- **Where:** design `:225-236` ("When it is not, NOT-AFFECTED"; "The NOT-AFFECTED case is a
  `git init` repo whose only commit does not contain that ancestor object") and
  `C1893_S1_Hold_prints_NOT_AFFECTED_when_the_ancestor_is_absent` (`:261-262`). G-19/PC-19.
- **Failure:** in that fixture the ancestor object `9cf3e4917` does not exist, so
  `git merge-base --is-ancestor 9cf3e491705247bc03814e6afed77a6534805dfd HEAD` fails with
  `fatal: Not a valid commit name` and **exit 128**, not 1 (measured in a scratch `git init`
  repo). The witness only passes if the script treats a git error as NOT-AFFECTED. The same
  path prints NOT-AFFECTED for any rollout SHA the checkout cannot resolve. I ran the §7.0
  block read-only in this checkout: `870a93d21` prints `HOLD`, master `c3cc0bb9b` prints `HOLD`,
  and an unknown 40-hex SHA prints `NOT-AFFECTED`. The design's script also drops the plan's
  leading `git fetch origin` ("uses `git show` in the git directory it is run from"). An
  unfetched rc tip that contains `9cf3e4917` would pass the hold, and the test would require
  that outcome.
- **Why it blocks:** this is brief item (5). The design lets a `deploy-temp`/`redeploy-old`
  of a `9cf3e4917` descendant pass the hold check before S1's compose lines exist, and pins
  that bypass with a test. A fail-closed implementation would turn the design's own witness red.
- **Fix:** before the ancestor test, the script resolves both `X^{commit}` and
  `9cf3e4917^{commit}` (`git cat-file -e`). If either does not resolve, it exits non-zero and
  prints none of `HOLD`/`RELEASED`/`NOT-AFFECTED`. It prints `NOT-AFFECTED` only when
  `is-ancestor` exits exactly 1, and treats any other non-zero as that error. Rebuild the
  NOT-AFFECTED fixture so the ancestor object is present and X is an unrelated orphan commit.
  Add one method, mapped to a new guard and PC: an unresolvable X exits non-zero with none of
  the three words. Mutant: map exit 128 to NOT-AFFECTED. Say whether the script fetches first
  or the caller must. If the caller fetches, the docs sentence says so.

### B-2: the upgrade-broker harness is not the faithful host fake and cannot reach the case

- **Where:** design `:297-305` ("copy `scripts/c590-remote.sh` into a temp directory; prepend
  stubs for `docker`, `curl`, `jq`, and `sleep` ... Seed the stack env file ... Invoke
  `CASE=upgrade-broker` through the real case statement"); the eight `C1893_Upgrade_*`
  methods.
- **Failure:**
  1. `c590-remote.sh:13-21` hard-codes `EVIDENCE_ROOT=/work/test-evidence/...`,
     `ROOT=/home/mc/antiphon-c590` and `SERVER2_ROOT=/home/mc/antiphon-server2`. `:906` hard-codes
     `SERVER2_ENV=$SERVER2_ROOT/secrets/stack.env`. A prepended block runs before these
     assignments, so they overwrite it. The seeded env file and `CASE_DIR` are never the
     fixture's.
  2. `c590-remote.sh:10697-10701` runs `ensure_checkout` and then
     `exec bash "$CHECKOUT/scripts/c590-remote.sh"` for every case not on its exclusion list,
     unless `C590_REEXEC=1`. `upgrade-broker` is not on that list. `ensure_checkout`
     (`:143-153`) runs `git fetch`/`checkout --detach`/`reset --hard` on `CHECKOUT`, which
     defaults to `/work/repos/antiphon`, the runner's shared mirror. After the `exec`, the
     prepended function stubs are gone. As specified, the run never reaches
     `*) write_result false "RealCasePending $CASE"` (`:10744-10745`). The design's stated red
     reason is false, the red would be a setup failure that its own oracle (`:112-113`) rejects,
     and green is unreachable.
  3. The plan's idle gate (activation plan §D, line 83) reads occupancy with
     `docker exec <broker> curl -fsS http://127.0.0.1:8080/build-slots`, and the existing census
     read does the same (`c590-remote.sh:2771`). A `curl` stub never sees that call. The busy,
     unparsable, `unknown` and second-read bodies have to come from the `docker exec` fake.
- **Why it blocks:** this is brief item (1): is the fake faithful? It is not. The repo already
  has the faithful whole-script harness, `RollingVolumeRecycleScriptTests.cs:3445-3503`. That
  harness uses `InsertSetupAtEntry`, redirects `SERVER2_ROOT`/`ROOT`/`EVIDENCE_ROOT`/`CASE_DIR`/
  `SERVER2_ENV`/`SERVER2_TEMP_ENV`, stubs `ensure_checkout`/`ensure_dirs`/`detect_lane`, puts a
  fake `docker` on `PATH`, and sets `C590_REEXEC=1` (`:3503`). The design names only the `Block`
  extractor and `LinuxShell`.
- **Fix:** bind the upgrade methods to that pattern: insert at entry, not prepend; redirect the
  roots and env files; `C590_REEXEC=1`; stub `ensure_checkout`. Put the occupancy and host-load
  responses on the `docker exec ... curl` path of the fake. Restate each method's red as
  `RealCasePending upgrade-broker` reached through that harness. Add one sanity assertion that
  the result file is under the fixture root.

### B-3: the hold-script descendant fixture fetch fails on the server2 runner mirror

- **Where:** design `:230-236` ("`git fetch --no-tags` of `9cf3e4917` from this repo") and
  `:573-575` (reused-row 2 min "covers the hold script's local `git fetch` of one commit").
- **Failure:** runner mirrors are partial clones (`/work/repos/antiphon`:
  `remote.origin.promisor true`, `partialclonefilter blob:none`). Measured here:
  - `git init` + `git fetch --no-tags <this worktree> 9cf3e4917` failed after 19 s with
    `fatal: git upload-pack: aborting due to possible repository corruption on the remote side`.
  - The same fetch from `/work/repos/antiphon` failed the same way after 47 s.
  - `--depth 1` worked in 3 s from the repo and 8 s from the worktree.
  - On a full clone, the design's fetch copies all 13,544 commits once per fixture method
    (5 methods).
- **Why it blocks:** brief items (1)/(2). CP-1 and CP-3 cannot go green on the server2 lane
  where S1 Code runs. The 2-minute reused-row estimate assumes a one-commit fetch, which the
  recipe does not do.
- **Fix:** specify `git fetch --no-tags --depth 1 <repo> 9cf3e491705247bc03814e6afed77a6534805dfd`
  (`is-ancestor` works on a child commit of the shallow root), or an alternates-based fixture.
  State the measured per-method cost.

## Verified (no blocker)

- **Compose/init/doc reds (item 1).** At base, the compose files carry no `HistoryPath`,
  `HistoryEnabled`, `PhysicalHostKey`, `BindingRevision`, `SourceEpoch` or `AuthorityId` (only
  `HostStats__Volumes__0/1` at `docker-compose.server2-runner.yml:77-78`). The temp file
  (`:1-14`) sets only `PhoneHome__RunnerId` and the broker URL. `init-state.sh:8-15` ends at
  `/runner-state/claude`. `/state/host-load` and `upgrade-broker` occur 0 times in
  `docs/docker-stack.md`, `docs/ops-http.md`, `docs/testing-and-build.md`,
  `deploy-server2.ps1`, `c590-real.ps1`, `verify-docker-stack.ps1` and `c590-remote.sh`.
  `deploy-server2.ps1:15` ValidateSet omits it. `c590-remote.sh:1020` has no `--no-recreate`.
  Each named decisive assertion is false today. The key-set "S1 does not change build-slots"
  half is correctly folded into the red `HistoryPath` method rather than standing alone
  (`:177-184` today are the eight keys). `DockerStackContractTests` has 117 `[Test]`, Unit, no
  `[Arguments]`. `DockerStackDocumentationTests` has 19. The existing top-level-key pin
  (`DockerStackContractTests.cs:269-279`) reads 2-space lines only, so the new 6-space
  environment keys do not disturb it.
- **Hold check on rc-24 (item 1).** `870a93d21` is an ancestor of master. The §7.0 block prints
  `HOLD` for it (and for master `c3cc0bb9b`). The rc witness can go red (script absent) and then
  green in any full or partial clone, because `git show` lazily fetches blobs.
- **Checkpoint table (item 2).** I ran
  `import --plan docs/superpowers/plans/2026-11-17-card-1893-s1-s2-testdesign.md --out .antiphon/f29c3d90-import.yaml`
  (ignored path): exit 0, **7 rows, 0 warnings**. There are two builds (`bin-c1893s1/`,
  `bin-c1893s2/`), and every row is one build plus one filter. Derived row timeouts are 42
  (CP-1, CP-4) and 15 (the rest); the ordinary floor is 38 min. The `(A*)|(B*)` method OR and
  `(X*)|(Y*)/*` class OR forms are the parseable ones. There is no whole
  `RemoteScriptContractTests` row. Budgets are defensible against the 607.7 s cold-build sample
  and the 11.56 s/13.55 s class samples, with the B-3 fetch cost to be restated. Min 147/149
  arithmetic is correct.
- **Guards (item 3).** G-1..G-20 each map to exactly one PC; PC-1..PC-20 are distinct. Spot
  checks of PC-1, 2, 3, 12, 16, 18 and 19 name a mutant that changes the named method's actual.
  B-1 adds one guard/PC.
- **Slices (item 4).** I re-read the in-flight tips:
  - `d336fcc3` `12105bfbc` edits only `docs/docker-stack.md`.
  - `83d30eec` `3d1fb98c8` descends from it and edits only `docs/docker-stack.md`.
  - CARD-1957 `c6c85b5b` edits `docs/docker-stack.md`, `c590-real.ps1`, `deploy-server2.ps1`,
    `c1609-host-proof.ps1`, `VolumeRetirementHostProofTests.cs` and
    `tests/derived-impact-index.json`.

  None touches the compose files, `init-state.sh` or `c590-remote.sh`. The land order
  `d336fcc3` → CARD-1957 → S1 → S2 holds. The design never names `c1008-retirement.sh`, the
  retirement constants or the audit.
- **Evidence diff / merge-tree.** `check-evidence-diff.ps1 -BaseRef 321f98ec9 -HeadRef c88fc182d`:
  commits=1, violations=0 (`-BaseRef c3cc0bb9b` refuses `base_not_ancestor`; master moved past
  the branch base). `git merge-tree --write-tree origin/master c88fc182d`: clean. No code or
  test changed, so no class or guard batch rerun is owed.

## Advisories (do not hold the land)

- A-1: the CARD-1957 tip moved from `1b59dd109` to `8714700dd`. It now also edits
  `tests/derived-impact-index.json`, so the derived-index rule applies to it as well.
- A-2: the hold greps are per-file, not per-service. A `HistoryPath` line on build-slots alone
  would release. The S1 key-set pin covers this at the S1 commit only.
- A-3: the docs-hold witness also asserts the token `all`, which is trivially present.
  `/state/host-load` is the decisive half.
- A-4: process note. My checkpoint-tool build lease started at load 16.43 (the read just before
  it was 14.55). It held for 37 s.
