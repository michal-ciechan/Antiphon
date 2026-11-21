# rc/2026-11-30 fast-forward: Codex 0.170 fixes (task 5990132e, 2026-11-21)

Outcome: origin/rc/2026-11-30 fast-forwarded 493bee0ee3af2052575bc7a26e9a313669b2fc2d -> b02b4ea7ae80ad8eff3445a82d5477b80107bf44 with a plain push (no force).

## Pre-push checks

- `git ls-remote origin refs/heads/rc/2026-11-30` before push: `493bee0ee3af2052575bc7a26e9a313669b2fc2d` (unchanged pin).
- `git ls-remote origin refs/heads/feat/card-task-0a49db09`: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`.
- `git merge-base --is-ancestor 493bee0ee b02b4ea7a`: exit 0.
- Reviews present: `docs/superpowers/reviews/2026-11-21-codex-ready-fix-review-r1.md` on origin/feat/card-task-851cb98f (round 1 CLEAN, 2f5e1721), `docs/superpowers/reviews/2026-11-21-codex-pointer-fix-review-r2.md` on origin/feat/card-task-9bef45fb (round 2 CLEAN, diff 2f5e1721..b02b4ea7).

## Commits (493bee0ee..b02b4ea7a, 6)

```
b02b4ea7a docs(tests): name the 0.170 pointer-fix checkpoint rows
9adbf3ba1 fix(pty): step off the Codex 0.170 shortcuts row before pointer evidence
41415c404 test(pty): Codex 0.170 shortcuts row hides a typed pointer
2f5e17219 docs(tests): name the 0.170 ready-fix checkpoint rows
fd16cd4ca fix(pty): step off the Codex 0.170 shortcuts row before the footer
f459e06e5 test(pty): Codex 0.170 shortcuts row is not classified Ready
```

## Diff stat (only the six allowed paths)

```
 docs/superpowers/plans/2026-11-21-codex-0170-pointer-fix-checkpoints.md | 93 ++++
 docs/superpowers/plans/2026-11-21-codex-0170-ready-fix-checkpoints.md   | 62 +++
 src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs                 | 10 +
 src/Antiphon.Agents.Pty/CodexStartupReadiness.cs                        | 23 +
 tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs           | 61 +++
 tests/Antiphon.Agents.Pty.Tests/CodexStartupReadinessTests.cs           | 93 ++++
 6 files changed, 342 insertions(+)
```

## Push

```
$ git push origin b02b4ea7ae80ad8eff3445a82d5477b80107bf44:refs/heads/rc/2026-11-30
To github.com:<owner>/Antiphon.git
   493bee0ee..b02b4ea7a  b02b4ea7ae80ad8eff3445a82d5477b80107bf44 -> rc/2026-11-30
```

## Post-push

- `git ls-remote origin refs/heads/rc/2026-11-30`: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`.
- No other ref, tag, publish, restart or deploy was touched. No build or test was run (pure ref move of reviewed commits).
