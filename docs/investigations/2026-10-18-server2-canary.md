# Server2 post-rollout canary

Task a648697f. Purpose: prove the standing server2 runner can launch a task through worktree, git, a build-slot lease, and push. No product plan and no build or test beyond the slot echo.

| Fact | Value |
|---|---|
| hostname | a6457bc89508 |
| uname -a | Linux a6457bc89508 4.15.0-213-generic #224-Ubuntu SMP Mon Jun 19 13:30:12 UTC 2023 x86_64 GNU/Linux |
| pwd | /work/worktrees/task-a648697f |
| cwd under /work/worktrees | yes |
| git rev-parse HEAD | 69978ec1a246cd7a860f88a364eafc53a416aff7 |
| git status --short | empty |
| branch | feat/card-task-a648697f |
| codex --version | codex-cli 0.160.0 |
| pwsh --version | PowerShell 7.5.4 |
| dotnet --version | 10.0.402 |
| df -h /work | /dev/mapper/ubuntu--vg-ubuntu--lv 705G 493G 184G 73% /work |

Build slot (`scripts/build-slot.ps1 -Label server2-canary -- bash -c 'echo slot-ok'`):

```
BUILD SLOT granted lease=0828f91c-68d2-495f-b54a-a2e8e1f98b8d waited=0s maxcpucount=6
slot-ok
BUILD SLOT released lease=0828f91c-68d2-495f-b54a-a2e8e1f98b8d held=0s
```

canary: ok
