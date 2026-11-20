# Agent steering log

Where the owner had to steer an agent away from what it did or recommended, why the agent chose that,
and the simpler direction the owner preferred. The purpose is to improve `AGENTS.md`, the bundled
prompts and agent briefs so the next agent starts where the owner already is. Each entry records:

- **Context**: the work in progress.
- **What the agent did or recommended.**
- **Why the agent picked it**: the reasoning at the time, stated honestly, including the instinct behind it.
- **What the owner preferred**: quoted where possible.
- **Principle**: the general rule behind the correction.
- **Candidate guidance**: wording that could go into `AGENTS.md`, a bundle or a brief.

Append new entries at the bottom, newest last. When a principle has been promoted into `AGENTS.md`, note it on
the entry rather than deleting it. A steer is recorded even when the owner picked a non-recommended option in a
multiple-choice question, since that choice is itself a steer.

## Summary: how the owner prefers things structured

Distilled from the entries below. These are the candidates for standing guidance.

1. **Build the end state; do not build bridges to the old one.** PowerShell is being retired in favour of
   compiled C#. A port is complete when the PowerShell is gone: no wrapper functions kept for call-site
   compatibility, no PowerShell loops orchestrating compiled pieces, no fallback paths for hosts without the
   compiled code, no second implementation kept in sync by a parity test.
2. **Prefer the simplest design that is correct, even if it changes behaviour or configuration.** A config
   change (switching a scheduled job from `powershell` to `pwsh`) beats a compatibility bridge (re-running
   under pwsh and marshalling results back). Standard toolchain defaults (`bin/` and `obj/`) beat a custom build
   layout plus the patches the custom layout then needs.
3. **When a fix compensates for something odd, remove the odd thing.** If a change needs an extra exclusion, a
   loader patch, a fixture that copies sources around or a bridge, ask what forced it and remove that cause
   instead of adding the compensation.
4. **Correct and fast beats byte-identical.** Outputs may change shape when the result is still correct. Prove
   correctness with tests, not with byte-for-byte parity against the code being replaced.
5. **Do not optimise for rare cases at the expense of simplicity.** A cost that only appears on a fresh clone,
   a cold machine or a legacy host is acceptable if the common path is simpler.
6. **Tests call the code in-process.** Tests do not launch `.ps1` scripts or child pwsh processes to reach logic
   that can be called directly. Entry points are invoked directly by their callers; the entry `.ps1` files go
   away rather than surviving as shims.
7. **Do routine maintenance without asking.** Re-recording digests, accepting output changes that follow from
   an agreed direction, and similar bookkeeping do not need sign-off. Ask only about genuine decisions.
8. **Do not argue against the stated direction on local cost/benefit grounds.** "Small remaining gain" or
   "adds risk" is not a reason to stop short of the direction the owner has set.

---

## 2026-11-05 - Re-record the land build-graph digest without asking

**Context:** A build-file change broke the land verification build-graph digest
(`C1385_RegistryMatchesNarrowGraph` / `unrecognized-build-graph`).

**What the agent did:** Treated the re-record as needing operator approval and asked first. Earlier commits had
labelled it "operator-approved", which made it look like a sign-off step.

**Why the agent picked it:** The digest guards what the land verification builds, so changing it looked like
weakening a safety check; the "operator-approved" wording reinforced that.

**What the owner preferred:** "Re record, always, no need to ask me".

**Principle:** Bookkeeping that follows mechanically from a change you were asked to make is part of that change.

**Candidate guidance:** "When a build-definition change (csproj, `Directory.Build.*`, `global.json`) breaks the land
build-graph digest, re-record it (`scripts/audit-land-verification-graph.ps1 -Update`, then `-Check`) and commit it
with the change. Do not ask."

---

## 2026-11-13 - No PowerShell 5.1 fallback for ported code

**Context:** Porting nightly PowerShell into the compiled core (`scripts/lib/Antiphon.Nightly`).

**What the agent did:** Kept the PowerShell bodies as a fallback for hosts where the core cannot load (Windows
PowerShell 5.1, a script copy without the project, no SDK).

**Why the agent picked it:** Some scheduled entry points ran under Windows PowerShell 5.1, and a fallback looked
like the safe way to keep them working without auditing every caller.

**What the owner preferred:** The core may replace the PowerShell bodies; the 5.1 fallback is dropped.

**Principle:** One implementation. A fallback is a second implementation that must be kept correct forever.

**Candidate guidance:** "Ported code keeps no PowerShell copy. Where something still runs where the core cannot
load, move that caller, don't keep a fallback."

---

## 2026-11-19 - Port to C#, don't tune PowerShell

**Context:** Speeding up nightly chunk timing; `New-NightlyDispatchSchedule` was slow.

**What the agent did:** Tuned `New-NightlyDispatchSchedule` in PowerShell and said that porting the rest "would add
risk for little gain".

**Why the agent picked it:** It weighed the local trade-off: the hot spot was fixed by tuning, the remaining port
looked like small marginal speed-up for a large diff and some regression risk, and a small diff felt like the
conservative, reviewable choice.

**What the owner preferred:** "Ultimately I want to move everything to c#".

**Principle:** The owner's direction (retire PowerShell) outranks local marginal-gain arguments. A PowerShell
speed-up that stops short of a port is a half step.

**Candidate guidance:** "When speeding up or changing script logic, port it to the compiled core instead of
optimising the PowerShell. Do not argue against a port on 'small remaining gain' or 'risk' grounds."

---

## 2026-11-19 - Correct and fast, not byte-identical; no cmdlet, no fallbacks, warm builds, don't ask

**Context:** `RcStoppedRunSeedTests` took about 90 s; investigating and optimising it.

**What the agent did:** Several things, all from the same instinct:
- kept ported outputs byte-identical to the PowerShell and `ConvertTo-Json` output, with differential checks;
- kept a `ConvertTo-Json` cmdlet path in the JSON writer;
- built the core cold for each source version (a new cache folder per source hash);
- kept PowerShell fallbacks for hosts without the core;
- asked before accepting changes that moved output.

**Why the agent picked it:** Byte parity made the port provably equivalent and easy to review. The cmdlet and the
fallbacks kept every legacy path working. A fresh folder per source hash was the simplest correct cache. Asking
before output changes avoided surprising downstream readers.

**What the owner preferred:** "Output doesn't need to be byte identical, just fast and correct. And drop the
cmdlet. Make sure we are always warm rebuilding. Don't have fall backs. Just accept any changes."

**Principle:** Correctness is proven by tests, not by parity with the old code. Keep one code path. Builds should be
incremental. Changes that follow from the agreed direction do not need approval.

**Candidate guidance:** "A port need not reproduce the old output byte for byte; it must be correct and tested.
Remove the old path (cmdlet, fallback) in the same change. Keep builds incremental. Accept output changes that
follow from the agreed direction without asking."

---

## 2026-11-19 - No PowerShell loops around compiled pieces

**Context:** Porting the stopped-run seed and dispatch code.

**What the agent did:** Ported the inner computations to C# but left the PowerShell loops that iterate over rows and
call the compiled pieces.

**Why the agent picked it:** Porting leaf functions first kept each step small and kept the PowerShell call sites
and tests unchanged; the loops looked like harmless glue.

**What the owner preferred:** "Make sure we are not using powershell for these loops. Just move it all into c# and
achieve the same result (even if output slightly different)." Then: "anywhere we have a script doing multiple thing
that should also be going into c#".

**Principle:** The unit of porting is the whole operation, including its loops and sequencing. PowerShell ends up
as a thin entry point at most.

**Candidate guidance:** "Port whole operations: loops, sequencing and multi-step script logic go into C#. Leaving
PowerShell to orchestrate compiled pieces is not a finished port."

---

## 2026-11-19 - One C# call, not a wrapper library

**Context:** `nightly-stopped-run-seed.ps1` after the first port pass.

**What the agent did:** Kept the PowerShell function names as thin wrappers over the C# methods (433 lines down to
80), so callers and tests kept calling the same functions.

**Why the agent picked it:** It avoided touching every call site and every test in the same change.

**What the owner preferred:** "nightly-stopped-run-seed.ps1 is down from 433 lines to 80, all wrappers. Can we just
move the whole thing to 1 c# call?" Then, for `nightly-failure-history.ps1`, which had been partly ported:
"Move this to c# as well".

**Principle:** Rewrite call sites to call the compiled code directly and delete the wrapper library. Wrappers are
compatibility layers, and compatibility layers are not wanted.

**Candidate guidance:** "After porting, rewrite callers to call the compiled code directly and delete the
PowerShell file. Don't keep wrapper functions for call-site compatibility; an AST-based rewrite of call sites is
the expected tool."

---

## 2026-11-19 - Switch the scheduled jobs to pwsh rather than build a bridge

**Context:** Moving `nightly-policy.ps1` into C#. The Windmill job `antiphon-release-candidates` ran
`release-cut.ps1` under Windows PowerShell 5.1, and `release-cut` ran `publish-release.ps1` in the same process,
so publishing would no longer load once the policy code was compiled-only.

**What the agent recommended:** "Bridge in code": `publish-release.ps1` and `promote-release.ps1` re-run themselves
under pwsh when started from 5.1 and hand their `-PassThru` result back as Clixml. This followed the existing
release-selection bridge.

**Why the agent picked it:** It kept the deployed Windmill job definitions unchanged, avoided an outward-facing
redeploy, and copied a pattern that already existed in the codebase.

**What the owner preferred:** "Switch jobs to pwsh": change both Windmill job definitions in `scripts/windmill` from
`powershell` to `pwsh`.

**Principle:** A one-word configuration change is simpler than a bridge with result marshalling. "It touches
deployment" is a reason to confirm, not a reason to build machinery around it. An existing bridge is not a
template to copy; it is a sign of the same problem.

**Candidate guidance:** "Prefer changing configuration (the shell a job starts, a flag, a path) over adding a
compatibility layer to keep old configuration working. If the change is outward-facing, ask, but recommend the
simple change."

---

## 2026-11-19 - The release authority's source digest in C#, not new PowerShell

**Context:** The release authority pins digests of the scripts it runs. With the policy code compiled, it also
needed a digest of the core's sources in place of `nightly-policy.ps1`.

**What the agent did:** Wrote a new PowerShell function (`Get-ReleaseAuthoritySourceTreeDigest`) in
`release-authority.ps1`, modelled on the source hashing `nightly-core.ps1` does for its build cache.

**Why the agent picked it:** The caller was PowerShell, the existing source hashing was PowerShell, and a small
function next to its caller looked natural.

**What the owner preferred:** "Move both to c#" (the authority's digest and the build-cache hashing).

**Principle:** New logic goes into C# even when its caller is still PowerShell. Don't add PowerShell while the
direction is to remove it.

**Candidate guidance:** "Write new logic in the compiled core, even for a PowerShell caller. Adding a PowerShell
function is the wrong direction unless it is a bootstrap step that must run before the core exists."

---

## 2026-11-19 - Port the libraries, don't patch how they load

**Context:** After the policy port, several PowerShell libraries (`nightly-coverage`, `test-selection`,
`release-gate`, `release-authority`) called the compiled policy code and had to load the core first.

**What the agent did:** Added `. nightly-core.ps1; $null = Test-NightlyCoreAvailable` to each library and to about
30 loaders, then removed the ones that turned out not to be needed.

**Why the agent picked it:** It was the smallest change that made each library self-sufficient and kept every
caller working.

**What the owner preferred:** "We should just move those to c#".

**Principle:** When a PowerShell library needs compiled code, that library is next to port. Patching its loading
keeps PowerShell alive and adds churn that will be thrown away.

**Candidate guidance:** "If a PowerShell library starts depending on ported code, port the library rather than
adjusting its loading."

---

## 2026-11-19 - No test launches a .ps1; entry points are called directly

**Context:** Porting the qualification timing code; its tests were PowerShell scripts (`test-*.ps1`) run by C#
tests through `ScriptHarness`, which starts pwsh child processes. The agent was rewriting those PowerShell test
scripts to use the new C# seams.

**What the agent did:** Kept the existing test shape: C# test classes launching `test-*.ps1` in pwsh, with the
PowerShell harness edited to drive the new compiled code.

**Why the agent picked it:** It followed the established test infrastructure and changed as little test
plumbing as possible.

**What the owner preferred:** "No test should be launching ps1." Then: "Or at most 1 test should call the ps1
scripts to make sure parameters are ok ... But where are they invoked from? Because at that point they should just
be invoked directly." Given the choice, the owner chose to delete the entry `.ps1` files entirely: every caller
(Windmill, `launch-rc`, script-to-script hops, skills) calls one compiled CLI directly.

**Principle:** Test compiled code in-process. Production callers invoke the compiled entry point directly; a
script that only forwards to it should not exist.

**Candidate guidance:** "Tests call the code under test in-process: no pwsh children, no `ScriptHarness`, no
`test-*.ps1`. When a slice is ported, move its script test cases into C# tests and delete them from the script.
Entry points are one compiled CLI; callers start it directly; entry `.ps1` files are deleted, not kept as shims."

---

## 2026-11-20 - Standard bin/ and obj/, not an artifacts path

**Context:** The test project started referencing the core project directly. The on-demand core build in
`nightly-core.ps1` used `--artifacts-path` with a shared work folder under `%TEMP%`, so a test build's `obj/` (with
generated `.cs` files) beside the sources would have been compiled into the core.

**What the agent did:** Added an explicit `bin/**;obj/**` exclusion to the core's `.csproj`, then proposed moving
it into `Directory.Build.props` repo-wide. When asked why an artifacts path was used at all, it proposed dropping
`--artifacts-path` but keeping a custom output folder (`-o out-<hash>`).

**Why the agent picked it:** The artifacts path was the agent's own earlier design for "always warm rebuilding":
one warm intermediate folder shared across all checkouts and source versions, kept out of the source tree. When
that layout caused the stray-`obj/` problem, the agent patched the symptom rather than questioning the layout.

**What the owner preferred:** "Shouldn't these always be excluded? By default in a csproj. Why do we still build to
an artifacts path? The shared folder should just be standard bin and obj."

**Principle:** Use toolchain defaults. A custom layout that needs compensating patches is the problem; remove the
layout.

**Candidate guidance:** "Build projects the standard way (`bin/` and `obj/` beside the project). Don't introduce
artifacts paths, shared intermediate folders or custom output layouts. If a default seems to need overriding,
first ask what non-default choice created the need."

---

## 2026-11-20 - Don't optimise for fresh clones

**Context:** The cost of building the core in each checkout's own `bin/` and `obj/`: the first build in a fresh
clone or worktree is cold (about 10 to 20 s).

**What the agent did:** Raised the per-checkout cold build as a trade-off against the simpler design.

**Why the agent picked it:** The shared work folder had made even a new clone warm, so losing that looked like a
regression worth flagging.

**What the owner preferred:** "Fresh clones should be rare." (The nightly runner reuses one persistent checkout
and clones only when it is missing.)

**Principle:** Optimise the common path. A cost that only appears in a rare case does not justify extra machinery.

**Candidate guidance:** "Don't add machinery to speed up rare cases (fresh clones, cold machines, first runs).
Mention the cost if it is real, but recommend the simpler design."

---

## Where the agent's recommendation matched (for contrast)

These were offered as multiple-choice questions and the owner chose the agent's recommendation. They show the
direction the owner already holds:

- **2026-11-19, policy port:** "All to C#, pin core". The release authority pins the core's sources instead of
  keeping a PowerShell subset alive for Windows PowerShell 5.1.
- **2026-11-19, entry scripts:** "Delete them". One compiled CLI; no `.ps1` shims.
- **2026-11-20, core data model:** "Plain C# types first". The core moves off PowerShell's object types
  (`PSObject`) to plain C# records and System.Text.Json, rather than shipping the PowerShell SDK inside the test
  project and the CLI.

In each case, the recommended option was the one that removed a layer rather than kept one.
