# C514 three-prompt PTY Debug, 2026-10-30

Task `dfd3f49c`, branch `feat/card-task-dfd3f49c`, inherited tip
`126b5e4e5124a1bbf69f7ef5dbac00cb73d1a5ac`.

Investigation in progress. No acceptance or regression classification yet.

The original Debug's pre-fix base is
`80b3a59c40cd18dea905829e428d35844cb89b8d` (parent of `b65503281`).
Its PTY helper skips Linux and resolves only fakeclaude.exe. For a meaningful
Linux comparison the detached scratch base receives exactly the inherited tip's
platform-enabling helper delta (Linux admission and extensionless child path),
without the delivery fix or any changed test assertion. This instrumentation
must be reported rather than calling the base source pristine.

Measurements: exact three-prompt method, 30 native TUnit repetitions per source
alone followed by 15 under an owned concurrent build. The repetition properties
use the existing CheckpointRepeat.targets mechanism. Shared process repetitions
are not independent fresh-host trials. The historical Review edc0f0b6 and round-2
report establish the flake and justify exceeding the normal repeat-proof budget.
Every driver takes a build slot; use TEMP=/tmp, UseAppHost=false, isolated bin
outputs. Raw console/TRX/load evidence stays ignored under
`.antiphon/c514-three-dfd3f49c/`. No native Windows work is authorized here.

The requested `.antiphon/task-5e08c4d4.md` is absent in this checkout, its original
worktree, and the remote branch. The committed investigation and full edc0f0b6
review were read instead; the round-2 report is present and was read in full.
