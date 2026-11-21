# rc/2026-11-30 fast-forward 2: Codex 0.170 chip-row fix (2026-11-21)

Task 52075b7b (Custom). Authority: the operator's standing authority for this task (2026-11-20/21) allows one plain fast-forward of `origin/rc/2026-11-30` to the reviewed chip fix and nothing else.

## Subject

- Reviewed branch: `feat/card-task-589abe80`, tip `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`
- Review: dfeb678e (`docs/superpowers/reviews/2026-11-21-codex-chip-fix-review-r3.md` on `origin/feat/card-task-dfeb678e`). No blockers.
- Commits over the rc tip:
  - `94950a17c` test(pty): Codex 0.170 chip-only last row hides a typed pointer
  - `b963a3e8b` fix(pty): step off a Codex 0.170 warning-chip row before pointer evidence
  - `bd34a7ff6` docs(tests): name the 0.170 chip-fix checkpoint rows

## Pre-checks

- `git ls-remote origin refs/heads/rc/2026-11-30` before push: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44` (unchanged, as expected)
- `git merge-base --is-ancestor b02b4ea7a... bd34a7ff6...`: exit 0
- `git diff --stat b02b4ea7ae80ad8eff3445a82d5477b80107bf44..bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`:

```
 .../2026-11-21-codex-0170-chip-fix-checkpoints.md  | 87 ++++++++++++++++++++++
 .../CodexPointerComposerEvidence.cs                | 18 ++++-
 .../CodexAccountBootstrapTests.cs                  | 83 +++++++++++++++++++++
 3 files changed, 184 insertions(+), 4 deletions(-)
```

Changed paths (`--name-only`), exactly the three allowed:

- `docs/superpowers/plans/2026-11-21-codex-0170-chip-fix-checkpoints.md`
- `src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs`
- `tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs`

## Push

`git push origin bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0:refs/heads/rc/2026-11-30` (plain, no `--force`):

```
To github.com:michal-ciechan/Antiphon.git
   b02b4ea7a..bd34a7ff6  bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0 -> rc/2026-11-30
```

## Result

- Before: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`
- After (`git ls-remote origin refs/heads/rc/2026-11-30`): `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`

No other ref was changed. No tag, publish, promote, restart or deploy. This is a publication on rc, not activation: the product hosts are not restarted onto it.
