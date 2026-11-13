# CARD-1895 unbound Mutation candidate

Original Code/landing owner: 2ec9dc6b.
Every control and variant is pending. Mutation owns method-scoped deliberate
red/restore/green and missing-control discovery.

- PC-1: skip the allow-list and delete a directory named `src`. The exact-name guard must keep it.
- PC-2: treat a dirty tree as clean. The dirty guard must keep `bin` and report `dirty`.
- PC-3: follow a symlink named `bin` and delete the target. The no-follow guard must keep the target and the link.

Bind after ordinary Review and landing. No control execution is claimed.
Restart: none.
