# Why the TestDesign bundle exceeded its headroom

## Introducing commit and card

`88862285d0a54b2c831a7432917621fa81e2d0fb` added the R10 process-start rules to the performance
standard and copied them into the TestDesign launch bundle. The bundle grew from 2,473 to 3,868
LF-normalized, trimmed UTF-16 code units. The commit raised the TestDesign threshold to 10,000
in `InstructionBundleTests` and recorded operator approval for that change. The separate
`TaskPlatformGuidanceTests` headroom assertion still enforced 2,480 (2,500 less the 20-character
reserve). `ScopedVerificationInstructionTests.C487_G141` also retained its 2,500 threshold.
No introducing card was identified.

## Approving reviews

The runner mirror first observed the commit on master at 20:22:52 UTC on 2026-11-27 as a fast-forward from
`263a2880dde33eac23b542b133baba0c87008ac7`. The first-parent history and commit metadata point to
direct publication from the Claude session named in the commit. The Antiphon task history around
publication had no matching Review, Merge, or source approval for this commit. The search found no original
Full or Interim receipt or test selection.

Merge `97146b7c-f33b-4a71-a3e3-071b04bff855` later caught the failure in the wider CP-4 guard
selection for the phone-home deadlock fix. It reproduced the same assertion against an archive
of the starting master. The operator authorized excluding that inherited failure from the
phone-home rerun. That approval covered the phone-home repair.

## Outside the selection

These files are in the normal lane. A runtime bundle under `server/Bundles/` and a C# test are
outside the docs-only build skip. The Unit headroom test was absent from the six-class policy
subset and the fixed Merge high-radius rows. The wider Final guard recipe includes it.

Replaying `impacted-classes --merge-subset --range 263a2880d..88862285d --json` selected the fixed
rows plus the fixture-cleanup and lane-category guards. The ordinary impact selection added the
two indexed TestDesign readers, `AdditiveMigrationRuleWitnessTests` and
`CheckpointManifestDocumentationTests`, and the changed `InstructionBundleTests` class. Neither
selection included the headroom test. The changed embedded-bundle assertion already accepted a
3,868-character TestDesign bundle under its new threshold.

## Cheapest pre-land check

Map `server/Bundles/stage-*.md` to `/*/*/TaskPlatformGuidanceTests/*` in the impacted-classes
table (CARD-1782). The headroom method reads all six stage bundles from disk and asserts their
normalized lengths. The check needs the usual narrow test build and no service startup.

## Exemption

None.
