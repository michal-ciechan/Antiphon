#requires -Version 5.1
# CARD-1514 S1. The Windows process-owner witness is Invoke-RcWindowsNativeProcessOwner
# in scripts/test-nightly-native.ps1. It calls NativeProcessOwner.Start. Host-stats
# sampling is not wired in this slice. Phase-root absent/empty/set is portable and
# is covered by RcFullTimingContractTests.PhaseRootAbsentEmptyAndSet.
