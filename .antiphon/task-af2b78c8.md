Debug af2b78c8 evidence (in progress).

Base: 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9, fetched origin/master, Debian Linux/server2.

Pre-fix witness: scripts/fixtures/script-harness-linux-admission-witness.ps1 extracts the actual admission body as a string and compiles it with the skip exception mapped to PlatformNotSupportedException (no native test host or process ownership primitive). Missing admission has an empty body. Forced `windows` input fails with `forced-windows input was admitted without the Linux-only reason`; `linux` is the passing control. Gated driver exit 1; lease c03e6892-70ba-43c4-83c1-5f95e577f556. Log: .antiphon/evidence-af2b78c8/admission-red.log. This is decision/source evidence, not native skip receipt evidence.
