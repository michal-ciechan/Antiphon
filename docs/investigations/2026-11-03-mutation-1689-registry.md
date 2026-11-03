# CARD-1689 mutation sweep: retire-temp registry and exchange diagnoses (task 5a733e90)

**Outcome:** I ran all 10 brief PCs (13 named variants), and every one went red for its intended assertion.
I also ran 11 extra safety mutants that I found by higher-tier analysis. 6 were killed and 5 **survive**.

The headline survivor is a trust guard. Removing or weakening the registry **shape/schema check**
(`scripts/c1008-retirement.sh:222-228`) leaves every CARD-1689 witness green. No test feeds the exchange a
canonical, custody-clean registry that has the wrong schema.

The unmutated script does refuse such a registry. A no-build probe gave `reason=registry-invalid`, exit 2,
with no `C1609_PROOF_REQUEST`. So this is a coverage gap, not a live defect.

- Kind: waived ordinary-worktree sweep. This is **not CARD-0478 evidence**.
- Scope: method-scoped. Mutants were run one at a time.
- Host: server2/Linux, runner mirror `/work/worktrees/task-5a733e90`.
- Base and tested HEAD: `97e77f9f1fd6f71e2719bc661b48f8f89f129029` (master). It contains the CARD-1689 batch-39
  commits `788f1a97d`, `3d8f33b14`, `6bf418d8f`, `c10714db4`, `61cf6abd7` and `26006dcb5`.
- Subject Code task: bbe3899e (`origin/feat/card-task-bbe3899e`, report `.antiphon/task-bbe3899e.md`). The PC table
  comes from that report's "PCs pending" section. There is no CARD-1689 plan file.

## Method

- **Build:** one `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, in native default output, under one build-slot lease
  (`92809c9f`, waited 15 s, held 207 s).
  - Every mutant edited only bytes that are read at runtime: `scripts/c1008-retirement.sh` is sourced by the witnesses
    through `DelegateScriptRunner.RepoRoot`, and the docs witness reads it as text. No C# changed, so I did not rebuild per mutant.
  - The same build served the baseline, every mutant and the restored-green run.
- **Each mutant:**
  1. Refuse to start unless HEAD is the base and the tracked tree is clean.
  2. Apply a `perl -pi` edit that touches only the stated line, and check that the diff is non-empty.
  3. Run one `dotnet run --no-build -p:UseAppHost=false` under its own build-slot lease. The filter is an exact method:
     `--treenode-filter "/*/*/<Class>*/<Method>"`, with `--report-trx`.
  4. Restore with `git checkout -- scripts/c1008-retirement.sh`, and check that the blob is `628de2f2c3d90ab2029859ea2d26784cd3f99953` again.
- **Red means** a TRX with total=1, executed=1 and failed=1, plus the expected Shouldly assertion message. Exit 2 alone does not count.
- **Baseline:** 4/4 passed (lease `79114f36`).
- **Restored green:** 4/4 passed after all mutants (lease `d45dd255`). The filter for both was
  `/*/*/(VolumeRetirementContentTests*)|(DockerStackDocumentationTests*)/(Exchange_*)|(Temp_retirement_trusted_origins_registry_is_documented*)`.

Witnesses, all Linux-only. The first three are in `tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs`:

| Short | Method |
|---|---|
| INV | `Exchange_inventory_refusal_names_its_check_and_detail_file` |
| REG | `Exchange_registry_refusals_name_the_registry_cause` |
| PRF | `Exchange_proof_failure_names_its_stage` |
| DOC | `DockerStackDocumentationTests.Temp_retirement_trusted_origins_registry_is_documented` |

## Brief PCs (bbe3899e table)

All line numbers refer to `scripts/c1008-retirement.sh` at the base. Every row ran 1 test with 1 failure.

| PC | Location | Mutation | Killing test | Assertion that failed |
|---|---|---|---|---|
| PC-1 | :414 | `*)` inventory branch becomes bare `c1008_refuse RecycleGitAuditUnknown` | INV | `Diagnosis(refused.Output)` expected `... reason=inventory detail=<.out>`, got `RecycleGitAuditUnknown` |
| PC-2a | :414 | drop ` detail=$out` | INV | expected `... reason=inventory detail=<.out>`, got `... reason=inventory` |
| PC-2b | :409 | drop the audit-line extraction (`detail=''`) | INV | expected `audit check=git-dir-shape ...`, got `audit check=unclassified status= repo=. ...` |
| PC-3a | :412 | the code 3 branch gains `$detail reason=inventory detail=$out` | INV | `Diagnosis(unpublished.Output)` expected `RecycleUnpublishedWork` |
| PC-3b | :413 | the code 4 branch gains the same suffix | INV | `Diagnosis(dirty.Output)` expected `RecycleWorktreeDirty` |
| PC-4 | :219 | delete the registry-missing pre-check | REG | vector `missing`: expected `reason=registry-missing`, got `reason=registry-invalid` |
| PC-5 | :229 | delete `C1609_ORIGINS_REASON=registry-project` (folded into invalid) | REG | vector `project`: expected `registry-project`, got `registry-invalid` |
| PC-6 | :421 | `unapproved-origin` detail points at the registry | REG | expected `detail=<inventory>/private-origins.txt`, got `detail=<server>/retirement-origins.json` |
| PC-7 | :424 | export-failure refusal becomes `return 1` | REG | `run.Exit` expected 2, got 1 (`missing; PREPARE_EXIT=1`) |
| PC-8 | :435 | exchange-failure refusal becomes `return 1` | PRF | `silent.Exit` expected 2, got 1 |
| PC-9 | :367 | delete `C1609_PROOF_STAGE=binding` | PRF | `Diagnosis(mismatched.Output)` expected `stage=binding`, got `stage=response` |
| PC-9 (doc) | :367 | same mutant | DOC | `script should contain "C1609_PROOF_STAGE=binding\n"` |
| PC-10a | :229, :420 | rename reason `registry-project` to `registry-entry`, in the script only | DOC | `c1689-docs: the script emits registry-project` |
| PC-10b | :376 | rename stage `freshness` to `fresh`, in the script only | DOC | `c1689-docs: the script sets stage freshness` |

## Safety-property mutants (higher-tier discovery)

These mutants target the properties named in the brief:
1. A missing, unreadable or wrong-schema registry is never trusted.
2. An unlisted origin is not trusted.
3. Registry content cannot forge a trusted line.
4. A diagnosis carries no URL, secret or git error text.

| ID | Location | Mutation | Result | Killing test and assertion, or missing test |
|---|---|---|---|---|
| X-1 | :219 | a missing registry returns 0, so it is treated as trusted | killed | REG `missing`: expected `registry-missing`, got `proof-exchange stage=response`. The run reached the exchange. |
| X-2 | :221 | ignore a `c1609_read_canonical` failure (non-canonical, unreadable or linked registry) | killed | REG `non-canonical`: expected `registry-invalid`, got `unapproved-origin` |
| X-9 | :144 | `c1609_custody` file branch drops the group/world-write check | killed | REG `writable`: expected `registry-invalid`, got `proof-exchange stage=response` |
| **X-3a** | :228 | ignore a failed registry **shape check** (`\|\| true`) | **SURVIVES** | Missing test: a REG vector for a canonical, mode-0600 registry that names the project and lists the origin but has the wrong schema. Examples are `schema:2`, an extra top-level key, duplicate origins, a non-GUID project key, or a non-string or empty origin. It must refuse `reason=registry-invalid` before `C1609_PROOF_REQUEST`. |
| **X-3b** | :223 | `.schema==1` becomes `true` | **SURVIVES** | Same missing test (the `schema:2` vector). |
| X-5 | :230 | ignore a failed project-entry check | killed | REG `project`: expected `registry-project`, got `unapproved-origin` |
| X-4 | :235 | origin membership `index($origin)!=null` becomes `length>=0`, so any origin is trusted | killed | REG `unapproved`: expected `unapproved-origin`, got `proof-exchange ...` |
| X-10 | :218 | export returns 0 without consulting the registry | killed | REG `missing`: got `proof-exchange stage=response` |
| **X-7** | :238 | exported `trusted-origins.json` takes origins from **every** project (`[.projects[].origins[]]`) | **SURVIVES** | Missing test: no test reads the bytes of `trusted-origins.json`. Add a registry with a second project, then assert the exact exported document `{"origins":[<this project only>],"projectId":<id>,"schema":1}`. See note 2. |
| **X-6** | :234 | request capture `origin=(?<origin>[^ ]+)` widened to `(.+)` | **SURVIVES** | Probably equivalent for trust: membership is still an exact-string match against this project's registry list. Optional witness: a request line with an embedded ` originHash=` must be refused. |
| **X-8** | :409 | the diagnosis detail is taken from the **first line** of the inventory output, not the first `audit check=` line | **SURVIVES** | Missing test: in INV, the inventory output that comes before the audit line includes a git error or URL line, such as `fatal: could not read Username for 'https://user@host/x'`. The test must assert that the diagnosis is the audit line and contains no `://`, `@` or `fatal:`. |

### Notes

1. **Unmutated behaviour is correct for X-3.** I ran a scratch probe that sources the real script at the base bytes, with
   the same stubs as `PrepareExchange`. It refused all three of these with `RecycleGitAuditUnknown reason=registry-invalid`,
   exit 2 and no proof request:
   - `{schema:2,...}`
   - `{schema:1,extra:1,...}`
   - a duplicate origin list
2. **X-7 is pre-CARD-1609 code**, not a CARD-1689 change. Line 238 was untouched by `6bf418d8f`.
   - The host-side membership check at :232-236 still limits the *requested* origins to this project's list.
   - The desktop reader `scripts/c590-real.ps1:509-514` checks only `schema`, `projectId` and the key set. It does not
     check that `origins` came from this project.
   - So a widened export is accepted downstream without being noticed. I did not follow how the desktop uses `allow.origins` beyond that check.
3. **Forging with a newline or quote in a registry origin.** The export is `jq -cS`, so it is JSON-encoded by construction.
   Requests are split on `\n` and must match an origin exactly. I could not express a forging mutant without a rewrite.
   The nearest mutant is X-6. No witness puts a quote or newline in a registry origin. That is optional hardening.
4. **No-URL property, current code.** The diagnosis carries either:
   - a path under `$SERVER2_ROOT`, or
   - the first `audit check=` line that the helper produced. Its `fail`, `refuse_unknown` and `rel_repo` writers in
     `c590-remote.sh` print only check, status, a relative or redacted repo, and `reason=` tokens.

   X-8 shows that the anchor which keeps other lines out is unpinned.

   **Unverified observation:** the new `reason=inventory detail=/...` and `reason=registry-* detail=/...` forms do not match
   `Test-C1008SafeAuditDiagnosis` (`scripts/c1008-host-proof.ps1:71`). Its caller (`c590-real.ps1:493`) would collapse such
   a diagnosis to a bare one if it ever arrives through `c590-result.json`. In the exchange path, `c1008_refuse` fires before
   `c590-result.json` is written and the console shows `DIAGNOSIS=` directly, so I believe this does not apply. I have not proved it.
5. **Not run:**
   - `c590-remote.sh:10245-10246`, the `case_retire_temp_inventory` caller of the same export. It is unchanged CARD-1609 code.
   - The exchange stages `freshness`, `digest` and `record`. These have no behaviour test (the gap bbe3899e already named).
     Mutants there are killed only by the DOC pin (PC-10b).

## Restoration and evidence

- `scripts/c1008-retirement.sh` was restored after each mutant (blob `628de2f2c`). `docs/docker-stack.md` was never mutated (blob `a6a354785`).
- Final tracked source and index are clean at `97e77f9f1`, plus this report commit only.
- Evidence (TRX, console logs, mutant diffs, `restoration.json`) is in the session scratchpad
  `/tmp/claude-1654/-work-worktrees-task-5a733e90/228bd5dd-9923-4e0b-b90b-61c896cb7fcd/scratchpad/ev/`. It is not committed.
- My build outputs (`bin/`, `obj/`) were deleted after the restored-green run.
