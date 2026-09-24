# CARD-0647 durable runner spill follow-up

The queue row owns the remote spill bytes and runner-relative file name. The runner
checks the name against the row identity before writing. A new server process reads
the same row on every Input retry. The existing queue and receipt logic remain the
authority for terminal acceptance.

## Ordinary verification

- V-1: a queued brief survives courier replacement and reaches the runner file.
- V-2: two briefs for one busy session retain distinct bodies and file identities.
- V-3: a failed Input retries with the same identity and bytes.
- V-4: a complete matching UserPrompt is confirmed with `QueuedReceiptAssertions`.
- R-1: the existing server Grok credential and task projection classes.
- R-2: the runner command dispatcher and Grok auth probe classes.

The brief caps execution at one isolated build and these named selections, including
new tests. The ordinary Unit lane and other integration classes defer under this cap.

### Checkpoints

| ID | Slice committed before run | Build | Filter | Fresh TRX |
| --- | --- | --- | --- | --- |
| CP-1 | queue persistence, identity transport and runner write, migration, tests | one isolated solution build | named server selection | `.antiphon/d2e9bfc3-cp1/server.trx` |
| CP-2 | same committed slice | reuse CP-1 build | named runner selection | `.antiphon/d2e9bfc3-cp2/runner.trx` |

## Pending post-land controls

- PC-1: remove the persisted body assignment; V-1 must fail.
- PC-2: key the lookup by session only; V-2 must fail.
- PC-3: change the retry identity; V-3 must fail.
- PC-4: replace the complete UserPrompt matcher with a weaker receipt; V-4 must fail.

All PC variants remain for SourceLanding Mutation after ordinary Review and land.
