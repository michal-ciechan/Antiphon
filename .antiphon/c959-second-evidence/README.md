# CARD-0959 second Code continuation evidence

Source worktree: /work/worktrees/task-818582a5; branch feat/card-task-818582a5; landing owner 818582a5-dcea-44e1-9884-e78807cd514a.

raw-checkpoints.tar.gz preserves both original task-owned run roots, including report.json/report.md, TRX, build and console logs, source snapshots, manifests, request/owner state and failure details. archive.sha256 records SHA256. Extract into an empty evidence directory to preserve the recorded relative paths; reports intentionally retain unedited original absolute TRX paths.

trx-roster.tsv joins every fresh result to its TestDefinitions class and method; trx-counts.txt summarizes each tested run. unit-windows-exclusions.tsv records all 33 allowed platform exclusions and original reasons. checkpoint-lines.txt copies every CP receipt without editing it. run-history.json retains source and slot/build details. source-validation.txt distinguishes green zero-skip receipt validation from the strict CP-8 validator refusal. jq-prerequisite.txt records official release/checksum provenance. output-cleanup.txt and final-output-inventory.txt record output removal. mutation-pending.md explicitly keeps every PC-1..224 variant pending.

See ../task-818582a5.md for the incomplete qualification matrix and Code handoff. None of this evidence claims Final qualification, PC proof, Review acceptance, landing or activation.
