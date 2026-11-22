# Why the nightly path identity golden missed Linux directory size

The old `Test-NCEvidenceFilesMatchFallback` compared the compiled-enabled and
compiled-disabled calls to `Get-TestPhasePathIdentity`. On Linux both calls
took the same `lstat` path, so they agreed even when a directory reported its
filesystem size. Commit `2c1755dd3db176ec89ac83eb35db483001aae18d` switched to
an in-process call to `TestPhaseFiles.PathIdentity` and a Windows golden. The
golden's `unsupported` directory has size `0`. Running it on Windows exercises
the branch that already returns `0` for that case.

On Linux, the exact checkpoint on master `5411db8db92428e3db563485d3ee6c86ca3b8d64`
reproduced the mismatch. The `sub` directory was `unsupported` with size `4096`.
Both Linux entry points now report `0` for unsupported paths. Qualification
timing checks for `regular` before it uses size as a byte count or in a cache
key. The supervisor checks only the kind.

For changes to path identity, the cheapest pre-land witness is the existing
Linux checkpoint filter below with a temporary directory on a filesystem that
reports a nonzero directory size. The full class also checks bounded reads and
queued writes.

```text
/*/Antiphon.Tests.Scripts/NightlyCoreGoldenTests/Evidence_files_match_the_goldens
```
