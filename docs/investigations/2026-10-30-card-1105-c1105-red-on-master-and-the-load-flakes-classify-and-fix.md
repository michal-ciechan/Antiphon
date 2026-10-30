# C1105 red on master and the load flakes: classify and fix

Investigation in progress, 2026-10-30. Task `1ada4d4c`; assigned source `ade54222543381e2e1b31ad79d8ae33a4035f70b` on `feat/card-task-1ada4d4c`.

This Investigate stage records evidence and classification only. No repair, approval regeneration, deployment, or safety-deadline change is permitted by its stage contract.

The brief reports repeatable C1105 resume failure on master and load-sensitive C1364, C1008 and C1385 failures. The retained review reports are available through `origin/feat/card-task-b5b016a6:.antiphon/task-b5b016a6.md` and `origin/feat/card-task-8ef7bb9e:.antiphon/task-8ef7bb9e.md`; their original worktrees are absent on this runner. New generated evidence goes in gitignored `.antiphon/investigation-1ada4d4c/`.

Classification, exact witnesses, uncertainties and next stage will follow the local reproduction.

## Confirmed C1105 finding

**TEST DEFECT, inherited on both named branches.** The assigned rc compiled witness executes 1 test and fails 1 (test 806 ms, run 1.891 s; build 152 s, slot wait 0 s). Its outer assertion is missing `PASS resume`, at `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2852`. The underlying Node assertion at `scripts/fixtures/c1105-git-audit.cjs:789` expects `check=ls-remote status=128`, but receives `audit check=unclassified status= repo=`.

The fixture extracts three production functions at `scripts/fixtures/c1105-git-audit.cjs:785`. Production `c1008_audit` now calls `c1008_forward_host_proof` at `scripts/c590-remote.sh:8369`; its definition is at line 8300 and is absent from that extracted script. Bash returns 127 at the missing command, the explicit `|| return 2` converts it to audit refusal, and `c1008_audit_checked` at lines 8617–8626 stores the fallback detail and refuses. This is not a timeout or a failed preservation assertion: the preceding `saved.audit == initial.audit` assertion on fixture line 789 passes before the error-detail assertion fails. The successful second resume is never reached.

The production failure branch preserves an existing audit through `.auditFailure=$audit | .audit //= $audit` (`scripts/c590-remote.sh:8621`). The production file contains the missing definition; this observation does not certify every live rollout path, Docker behavior or host-proof mode. The broken witness remains a rollout-safety verification gap until repaired and reviewed.

### Reconstructed history

Only unedited historical script/fixture blobs were copied into ignored scratch; no branch switch, bisect checkout, source repair or live rollout occurred. A Node diagnostic preload records the existing child process result and copies its generated journal/script; it does not replace behavior.

| Source | Local fixture outcome | Evidence |
|---|---|---|
| `32f358b40dde0f7c413cc729c9f0f2ae498906e6` (parent) | PASS resume, exit 0, 1.123 s | `history-3/run.log`; fault branch reports ls-remote/128, restored origin resumes successfully |
| `29218e553d15854fca3540f29fd5cb3df2d2b122` | exit 1, 0.345 s, earlier assertion “the audit ran Git” | `history-2/run.log`; the same change added the missing dependency and also had a separately repaired parser error, so it does not expose the later assertion yet |
| `6a58c226242547f3eba5e1dcb438ed68718ca76c` (parser repair) | missing function and unclassified detail | `history-4/run.log:2` |
| master `0e7dd390520a43ab330396a72e9085a8691ac332` | exit 1, 0.556 s, same missing function | `history-1/run.log:1` |
| rc `ade54222543381e2e1b31ad79d8ae33a4035f70b` | exit 1, 0.442 s, same missing function | `history-0/run.log:1`; generated `resume.sh:60` |

Retained transcript excerpt (rc):

```text
DIAGNOSTIC resume status=2 signal=null stderr=/tmp/c1105-audit-jV0GTY/resume.sh: line 60: c1008_forward_host_proof: command not found
 stdout=RecycleGitAuditUnknown audit check=unclassified status= repo=
AssertionError [ERR_ASSERTION]: The input did not match the regular expression /check=ls-remote status=128/.
```

All scratch paths above are relative to `.antiphon/investigation-1ada4d4c/`. `git log -S 'c1008_forward_host_proof mounts' -- scripts/c590-remote.sh` identifies `29218e553d15854fca3540f29fd5cb3df2d2b122`; that commit did not update the extraction fixture. A previously similar omission of `c1008_resolve_audit_cap` was repaired in `2568f2368f2d0ce48894c2676a7e5faaa331cc5d`.

## Not done, noted

A separate Plan/Code task can restore the resume fixture's production-function dependency coverage and diagnostics; no repair is designed or implemented here.
