# Review engine live check

This page belongs to the scratch PR that `tests/Antiphon.Tests/Review/ReviewLiveGitHubTests.cs`
reviews against real GitHub. The PR is labelled `antiphon:live-check`, which the check requires,
and `antiphon:skip`, which keeps a live instance's poller away from it. Never merge it.

The check signs in as the `antiphon-review` GitHub App and runs the real GitHub adapter. Its
decision model, reviewer and verifier are scripted. It posts:

- one scripted inline finding on the first added line of this PR;
- the commit statuses `antiphon/review` and `antiphon/approval`;
- a thread reply, after which it resolves the thread.

Run it with:

```powershell
pwsh -NoProfile -File scripts/review-live-github.ps1 -Pr <this PR's number>
```

The script reads the app key from Bitwarden into the test process's environment only.
