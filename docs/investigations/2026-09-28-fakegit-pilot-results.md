# FakeGit S2a Boundary trace: CP-42

Source commit `e0d450f947194c3f3129557a4ab9effd981ff032`; checkpoint run `20260928-221218-cad3`, row `CP-42`, Debian 12, .NET SDK 10.0.401, Git 2.47.3. The exact 19-case Boundary filter in the plan executed 19, passed 19, failed 0, skipped 0, rerun 0. Build passed. Slot wait for the row was 0 seconds; isolated build waited 45 seconds and took about 155 seconds. The row's outer no-build process interval was 76.3725563 seconds. The TRX and report are in the raw archive below.

The trace validator reports `Complete=true`, `Feasible=false`, `Reason=below-optimistic-bound`. Its command is:

```sh
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll fakegit-landing-trace .antiphon/checkpoints/20260928-221218-cad3 .antiphon/fakegit-receipts/CP-42-30870-20260928221542650
```

It found 3,632 distinct launched Git children, each with a launch ID, argv, cwd, role, PID, available OS start identity, monotonic start and awaited exit/drain timestamps, exit code, stdout and stderr. Sixteen very short children had no available OS start identity; all have a PID and a complete interval. All intervals lie inside their case and the row's outer process interval. Setup, service, independent observer, capture, database clone/drop, provider disposal and fixture disposal have balanced entered/exited evidence in all 19 receipts. The real file-I/O unregister vector is marked separately, never counted as a Git child. Every case has before/after service Git images and a committed fresh-context DB observation before schema drop. The shared PostgreSQL bootstrap has its own completed interval.

The union of child intervals clipped to the no-build process bounds is **10.920507814 seconds**. The optimistic threshold is `max(0.30 × 76.3725563, 1) = 22.91176689 seconds`; observed removable Git time is **14.30%** of outer wall. Shared DB bootstrap alone took 40.410809586 seconds; clone/drop across cases took about 1.697/0.619 seconds. This is an optimistic process-time bound, not a measured fake/real comparison or a G1 result. S2b must remain unauthored until a bounded TestDesign/manifest revision resolves the failed S2a feasibility gate. CP-42 was comfortably below its 45-minute row limit.

An earlier trace at `44ad924a7b58c46f713e40ba0f0b95536295da6c` ran CP-42 with 19 passed, 0 failed/skipped, but lacked explicit checkpoint-run identity and outer monotonic bounds. Those instrumentation gaps caused the second committed S2a run above. It is the qualified trace source; the earlier 9.96/67.67-second estimate is not used for the gate.

## Frozen B roster and launches

`B01`–`B15` are `C448_V10_EachAcknowledgedBoundaryRechecksSource`; `B16`–`B19` are `C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged`. The typed, ordered arguments are in each case receipt and in the frozen TestDesign table. Child counts by case:

| Cases | Launches |
|---|---|
| B01–B05 | 71, 66, 145, 226, 228 |
| B06–B10 | 228, 133, 214, 143, 148 |
| B11–B15 | 226, 228, 87, 226, 163 |
| B16–B19 | 275, 275, 274, 276 |

Role totals: setup 263, service 2,665, fault hook 48, capture 595, independent observer 61. The exact command sequence, full argv and exit/output bytes remain in the case receipts. `FixtureGit.Trace` was not used as a launch count because the test resets it at fault cuts.

## Observed command vocabulary

The following are all observed executable/option families. Counts are actual launched children, including setup, service, observation and capture. They bind to the planned S2b semantic contracts in the [TestDesign](../superpowers/plans/2026-09-28-fakegit-test-design.md#semantic-contracts-brought-forward); no new Git family was inferred from a synthetic unregister trace.

| Family and observed options | Count | Planned discriminator |
|---|---:|---|
| `init -b`, `init --bare`, `clone --no-hardlinks --branch --`, `remote add`, `remote get-url --push --all` | 38, 19, 19, 19, 360 | Bare clone/remote isolation; PC-18/19 |
| `worktree add -b`, `worktree add --detach --force --force`, `worktree list --porcelain -z`, `worktree lock --reason` | 19, 15, 93, 15 | PC-10/11/14/20; physical registration/admin controls |
| `rev-parse` plain, `--verify`, `--absolute-git-dir`, `--path-format=absolute --git-common-dir`, `--path-format=absolute --git-path` | 93, 413, 163, 61, 65 | Ref reads, admin/common paths, missing versus invalid |
| `show-ref` plain, `--exists`, `--verify --hash`; `symbolic-ref -q`; `check-ref-format` | 114, 83, 662; 317; 344 | Ref/HEAD/missing controls; PC-20/21 |
| `status --porcelain=v1 -z --untracked-files=all` with/without `--ignored`, `--ignore-submodules=none`; `--untracked-files=no` | 147, 1 | Index/worktree/ignored controls; PC-24 |
| `ls-files --others --ignored --exclude-standard -z`, `diff --cached`, `add`, `commit -m`, `commit --allow-empty -m`, `checkout -b` | 14, 3, 39, 36, 4, 3 | PC-1/24 and empty-commit/branch controls |
| `fetch --no-tags` and `--no-write-fetch-head`, `ls-remote --refs --exit-code`, `push` | 99, 145, 49 | PC-18/19/27 and push/remote proof |
| `merge-base --is-ancestor`, `update-ref` plain and `--no-deref -d` | 122, 47, 4 | Ancestry and expected-old CAS; PC-21 |
| `-c ... merge --ff-only`, `-c ... rebase` | 11, 15 | Captured target advance and detached preparation; PC-25 |

Observed nonzero diagnostic classes were `merge-base` exit 1 with empty stderr (60), `symbolic-ref` exit 1 with empty stderr (181), `show-ref` exit 2 with `reference does not exist` stderr (51), and `show-ref` exit 128 with a missing named ref fatal diagnostic (47). Actual ref bytes and command order are preserved in the archive. S2b contracts must distinguish these categories; a generic missing-ref or error normalization is insufficient.

## Raw evidence

[CP-42 raw archive](2026-09-28-fakegit-cp42-raw.tar.gz), SHA-256 `64abf37149592a1c15a1b5c07180e3aa549d061bcbadecb197987dd316973473`. It contains the 19 `Bnn.json` receipts, `bootstrap.json`, the checkpoint report, TRX, host identity, Git checkout identity and resolved manifest. The live copies are under `.antiphon/fakegit-receipts/CP-42-30870-20260928221542650/` and `.antiphon/checkpoints/20260928-221218-cad3/`; the committed archive is the durable copy.
