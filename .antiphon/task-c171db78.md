# CARD-1514 final CP-8 verification

Verification is in progress; no all-green certification yet. Assigned base is
65beb323fdd6f88d0ea478d390be66ea2f8a67e7, incorporating final code
829e7b8c55dc1ac995e8ae5e78b64c681da20822. This task changes no product source.

The commissioned group is one fresh shared build and serial CP-1..CP-9, followed
by ten sequential CP-8 trials with host activity observation and five trials
under a concurrent real host build. Native CP-10/11 are excluded. Generated
receipts and raw logs remain ignored under .antiphon/c171db78-evidence/.

The imported plan remains unchanged. A diagnostic execution manifest will bind
CP-1..9 to one fresh output, retaining every exact filter/minimum/expectation and
serial execution. Historical provisional row ceilings (40/32/4/2/6/6/4/6/2
minutes) accommodate producing-build time on this loaded host; these are not
measured candidate p95s. Admission is separately bounded at 45 minutes. The
production 200 ms reader deadline remains unchanged. No -Update, desktop access,
live qualification manipulation, tag, publish, merge or rebase is authorized.

Keep tracked source frozen across this entire verification group so all receipts
and reused-build bindings identify one clean SHA. Append results only after all
owned commands have joined. A changed report SHA is not relabelled as test source.

Tool bootstrap passed: 0 errors / 5 inherited warnings, 6.47 s build, granted
lease e18aa086-0bf7-4107-9e13-f9aa3ca4f688 held 7 s. Its queue wait is retained
in tool-build.log. No unleased runs.
