# CARD-0273 - Keep the local AppHost rooted in the main checkout

**Date:** 2026-08-31
**Status:** plan (Plan pass; nothing built)
**Card:** CARD-0273 - restart-apphost.ps1 run from a worktree silently steals the canonical stack's root

## Verdict

Guard both scripts/restart-apphost.ps1 and dev-aspire.ps1. By default, a script whose
own checkout is a linked Git worktree must emit a loud refusal and exit 3 before it
can kill or start anything. Add an explicit -AllowWorktree switch for the legitimate
case of deliberately testing this machinery from a worktree; that switch warns
prominently that the shared local ports and canonical stack will be affected.

restart-apphost.ps1 is the critical guard because it tears down machine-global
processes. dev-aspire.ps1 closes the equivalent direct-launch route. scripts/deploy-local.ps1
will run the same preflight and report its own deploy refusal, so the CARD-0258 S3 wrapper
does not bury the hazard in a child process.

This is a visible action precondition with an explicit override, not an invisible
policy gate. Main-checkout manual use and the existing scheduled-task callers remain
unchanged.

## Ground truth

The CARD-0273 incident follows directly from the current source:

1. restart-apphost.ps1 derives root from its own PSScriptRoot. It kills the PID in
   root/logs/apphost.pid, frees all AppHost-owned ports, and launches the absolute
   dev-aspire.ps1 path under that same root.
2. Its port and DCP/dashboard sweeps are intentionally machine-global. A worktree copy
   therefore kills the canonical AppHost even if its own PID file is stale or absent.
3. dev-aspire.ps1 also makes PSScriptRoot its root, so it writes the worktree's logs,
   builds/runs its server and client, and leaves live processes rooted in that worktree.
   The shared health ports still answer successfully, hiding the substitution.
4. Direct dev-aspire.ps1 execution from a worktree does not tear down the old stack, but
   can start a worktree-rooted stack when the shared ports are free. Restart-only
   protection leaves that unsafe path open.

autostart-apphost.ps1 and watchdog-apphost.ps1 construct absolute targets from their
canonical scripts directory. They need no change once their targets enforce their own roots.

## Decision record

| Question | Decision | Reason |
| --- | --- | --- |
| Canonical-root test | Run Git against the script root: git -C root rev-parse --show-toplevel, then compare that result to the first worktree record from git -C root worktree list --porcelain. | rev-parse alone returns the linked worktree in the failing case. PWD is irrelevant when a script is invoked by absolute path. |
| Shared implementation | Put a pure, read-only classifier in scripts/apphost-common.ps1. | The three entry points must not grow different parsing or Windows path-comparison rules. |
| Restart guard placement | Immediately after root is derived, before logs, locks, check-daemon-build, PID reads, or any kill. | A refusal must leave the stack and the worktree untouched. |
| Dev guard placement | Immediately after root is derived, before the launch lock, Docker, directory creation, restore, or detached process start. | It prevents partial worktree-local launch state. |
| Default action | Refuse with exit 3. | A warning that continues is easy for a delegate to miss and reproduces the incident. Exit 3 already expresses a non-destructive refusal and the watchdog does not stamp it as a restart. |
| Intentional exception | -AllowWorktree on restart and dev; restart forwards it to its detached dev child. | Worktree testing stays possible, but requires a present-tense acknowledgement of shared-stack impact. |
| Git cannot establish roots | Refuse with exit 3 and name the failed Git command/error. | An unknown root is unsafe for a destructive machine-wide command. |
| Deploy wrapper | Apply the same check and switch in deploy-local, with DEPLOY VERDICT: refused before restart is called. | It is the newest likely verification entry point and must identify the danger at its own boundary. |

Do not infer permission from an environment variable, branch name, task ID, or port state. Those
can be inherited accidentally; an explicit command-line switch cannot.

## S1 - Shared classifier and message

Add a small helper to scripts/apphost-common.ps1, preserving its ASCII-only constraint. It takes
the source/script root and returns an object such as Verified, IsMainWorktree,
ScriptWorktreeRoot, MainWorktreeRoot, and Failure. It must not print, exit, write files, or inspect
the current directory.

Algorithm:

1. Run git -C <script-root> rev-parse --show-toplevel; reject a non-zero/no-output result.
2. Run git -C <returned-root> worktree list --porcelain; reject a non-zero result or a missing first
   worktree <path> record.
3. Canonicalise both existing absolute paths with one Windows-aware routine (full path plus
   case-insensitive and separator-insensitive comparison). Do not use a literal C:\src\Antiphon
   path: supported clones must work too.
4. Set IsMainWorktree only when the script's Git worktree root equals the first porcelain root.

The shared caller formatter should give a stable, high-visibility refusal:

    REFUSED: this AppHost command is rooted in a linked Git worktree.
      Script worktree: <linked-root>
      Main worktree:   <main-root>
      This command controls the shared local ports and can replace the canonical AppHost stack.
      Re-run it from <main-root>.
      To intentionally exercise this worktree against the shared stack, re-run with -AllowWorktree.
      Nothing was killed or started.

An unverifiable-root refusal must identify the failed Git step and say no teardown/startup occurred.
When -AllowWorktree is present, print a yellow WARNING before any action, naming both roots and
saying that the shared ports are not isolated.

## S2 - Apply the preflight

### scripts/restart-apphost.ps1

- Add -AllowWorktree to help text, parameter block, examples, and the exit-code description.
- Run the classifier before the current Restarting Antiphon AppHost banner and before any log/lock
  work. Allow only a verified main root unless -AllowWorktree is explicit.
- On the explicit override, print the shared-stack warning once and append -AllowWorktree to devArgs
  so the detached child honours the same acknowledgement.
- Preserve lock ordering, session-runner preservation, port ownership, timeouts, and the exit-4 DCP
  diagnosis. This card changes no canonical restart behaviour.

### dev-aspire.ps1

- Add the -AllowWorktree switch, dot-source scripts/apphost-common.ps1, and preflight immediately
  after deriving root.
- Refuse linked or unknown roots without the switch; with the switch, warn and continue unchanged.
  NoBuild and NoBrowser behaviour stays intact.

### scripts/deploy-local.ps1

- Add -AllowWorktree and use the common classifier immediately after deriving repoRoot.
- A linked or unknown root without the switch prints DEPLOY VERDICT: refused <detail> and exits 3;
  it does not invoke restart.
- An explicit override prints the warning and forwards -AllowWorktree to restart-apphost.ps1.
- Update the synopsis/description to state plainly that this is a machine-global canonical-stack
  deploy, never an isolated worktree validation. Preserve ok and failed verdicts for actual deploy
  execution.

No automatic caller, including autostart or the watchdog, may pass -AllowWorktree.

## S3 - Regression coverage and documentation

Add a Pester-free smoke script, scripts/test-apphost-main-worktree-guard.ps1. It creates a
disposable linked worktree from the current checkout and removes it with git worktree remove --force
in finally. It must never pass a guard into Docker, process, port, or lock operations.

| Case | Assertion |
| --- | --- |
| Main classifier | Current checkout verifies as IsMainWorktree true. |
| Linked classifier | A disposable worktree verifies but reports IsMainWorktree false and the current checkout as MainWorktreeRoot. |
| Restart entry | The real linked-worktree restart script exits 3 before its restart banner/lock/teardown and prints REFUSED, both roots, and -AllowWorktree. |
| Direct dev entry | The real linked-worktree dev script with NoBuild exits 3 before Docker/launch activity with the same action message. |
| Deploy entry | The real linked-worktree deploy wrapper with NoBuild exits 3 and prints DEPLOY VERDICT: refused without invoking restart. |
| Explicit override | Assert the classifier/formatter warning path without starting a stack; it must name shared ports. |
| Non-Git directory | It returns a structured verification failure that callers would refuse, not an assumed main root. |

Run the smoke under pwsh and, where available, powershell.exe to preserve the Windows PowerShell
compatibility promise. Give it pass/fail counts and a non-zero result like test-apphost-lock-age.ps1.
Do not automate an AllowWorktree invocation against the live stack.

Update AGENTS.md at the canonical restart/AppHost gotcha: restarts and deploys normally originate
from the main checkout; linked worktrees refuse by default; -AllowWorktree intentionally controls
the shared stack and is only for an explicit test. Keep copied PowerShell-facing text ASCII-only.

## Acceptance and verification

1. A linked-worktree root cannot kill or start the local AppHost without explicit -AllowWorktree.
2. The refusal is exit 3, says what is wrong and how to continue, and leaves no worktree lock/state.
3. restart-apphost.ps1 forwards an explicit acknowledgement to its detached dev child.
4. deploy-local.ps1 reports the same condition at its own boundary and retains its one-line verdict contract.
5. Main-checkout restart, logon autostart, and watchdog recovery keep their current lock/no-flap semantics.
6. The smoke leaves no disposable worktree registered.

Expected implementation checks:

    pwsh -NoProfile -File scripts/test-apphost-main-worktree-guard.ps1
    pwsh -NoProfile -File scripts/test-apphost-lock-age.ps1
    git worktree list --porcelain
    git diff --check

Do not restart the live AppHost to test this card. The refusal paths prove the incident precondition
without risking the canonical stack. A separate intentional canonical deploy can use
pwsh -NoProfile -File scripts/deploy-local.ps1 after the implementation lands.

## Out of scope

- Separate per-worktree local stacks with different ports, databases, or Docker resources.
- Changing which ports/processes a canonical restart owns.
- Changing locks, watchdog policy, autostart registration, or DCP diagnosis.
- Cleaning up a stack intentionally launched under -AllowWorktree; the operator owns the subsequent
  canonical restart and cleanup sequence.
