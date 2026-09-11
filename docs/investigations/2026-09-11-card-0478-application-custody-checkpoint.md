# CARD-0478 application custody continuation

Code task `cd1d39f7`, based on `08d2d111`, branch `feat/card-task-cd1d39f7`.
**Incomplete Code checkpoint: do not land or deploy.** The full ordinary acceptance
matrix and guard-to-method inventory remain outstanding. All 230 PCs are pending.

The application now implements SourceLanding admission/provenance, exact-L managed
snapshots, immutable accepted execution binding, runner receipt import, task sealing,
no-publication fences and explicit guarded verification cleanup. The active contracts
now describe Code -> ordinary Review -> original Code land -> companion SourceLanding
Mutation. These changes must ship together only after complete ordinary acceptance.

Fresh custody evidence at `32998e92`: 54 passed, 0 failed, 0 skipped in 10m05s,
`.antiphon/c478-cd1d-custody-3/custody.trx`. This includes real modern native descendants
surviving root exit, refusal before removal, eventual original receipt import, server
service recreation and exact cleanup for Succeeded, Failed and Canceled tasks.
These are focused application/Git/native tests, not the full dispatch/delivery matrix.

This record will be completed with final scoped verification and remaining work before
the delegate settles. No live stack, card, landing or deployment action was performed.
