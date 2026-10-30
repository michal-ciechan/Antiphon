Confirmed and repaired a PRE-EXISTING portable PTY fixture race; final scoped selection passed 285/285.

Branch feat/card-task-dfd3f49c, inherited tip 126b5e4e5124a1bbf69f7ef5dbac00cb73d1a5ac.
Final test code: c100106683ee98fa683d170675f351b7837b2a77. Product source unchanged.
| Condition | Total | Passed | Failed | Failure rate | 1-min load range |
|---|---:|---:|---:|---:|---|
| base-alone30 | 30 | 26 | 4 | 13.3% | 11.3–28.7 |
| tip-alone30 | 30 | 26 | 4 | 13.3% | 4.96–20.28 |
| base-loaded15 | 15 | 13 | 2 | 13.3% | 22.03–52.37 |
| tip-loaded15 | 15 | 14 | 1 | 6.7% | 26.69–65.59 |

Base product: 80b3a59c40cd18dea905829e428d35844cb89b8d, with only the identical Linux
PTY-helper admission/executable-path adaptation needed to avoid a platform skip.
Tip measurement source is inherited code plus the protocol-only commit. Native
TUnit repeats share setup but launch a fresh real PTY/fake-Claude child per case.
All 11 failures hit the same persisted-prompt deadline after alpha or bravo; no
submitted duplicate or reordered prompt appeared. All failure first lines and
SUBMITTED prefixes are preserved in the committed investigation.

Root: the fixture pump persists UserPrompt before TurnEnd and has no production
turn-end queue worker. A later enqueue/final single flush can see Working, then
passive waiting strands later rows. The deterministic held-TurnEnd witness failed
1/1 (36.597 s), proving alpha complete with a persisted TurnEnd while bravo/charlie
were Pending with zero attempts. Driving the production idle flush made it pass
1/1 (9.016 s). The original deadline, polling interval, modal/one-Esc setup, ordered
child/persisted equality and exactly-once assertions remain. No product Enter,
body, ambiguity/custody/generation guard or receipt rule changed.

First five-repeat selection: 284/285. All original/new three-prompt and RV514 rows
passed; a health row failed BEFORE arm with PostgreSQL 23505 on the transcript
sequence index. Base health rerun: 27/30, including the identical sequence collision,
a late-TurnEnd busy assertion and an original pre-fix arm assertion. A real persisted
TurnEnd/production-idle wait before the synthetic Working marker repairs that second
fixture race without retries or assertion relaxation.

Fresh final selection: 285/285, zero failures/skips. Maintenance 145, PTY integration
45, Recovery 85, Dialog 10. Each of 57 cases ran five times, including five each of
RV514, the original three-prompt method and the deterministic new witness.
| Control | Baseline passed/total | Mutated passed/failed/total | Restored passed/total |
|---|---:|---:|---:|
| a | 5/5 | 4/1/5 | 5/5 |
| b | 1/1 | 0/1/1 | 1/1 |
| c | 5/5 | 4/1/5 | 5/5 |

Controls at 1c0eb2f13b58dae675c38ba7732b2d107e38ff0c: (a) unknown repeated Enter;
(b) wrong ArmUnconfirmed; (c) redraw TimeoutException at 30.083 s, no hang. All
compile; source and DLL/PDB restoration byte-exact, restored 11/11. The later
health-only fixture change touches neither production nor the bound control pack.

Definition files: NONE changed; no -Update. Real -Check approved both evaluations
and canary, digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99,
68 inputs. All builds/tests granted slots; TEMP=/tmp, UseAppHost=false. Direct
scoped evidence, not validated checkpoint receipts or whole/native qualification.
Uninstrumented DB/IO subphases remain unknown; TRX/phase/run/load records retained.

Pushed slices:
- c20453c6bc4ce7d45f299ae8815e4336af3eeebd: measurement protocol.
- 4ff763e43b634e9f01d7ceda2ef4e21c918299dd: 90 measurements/classification.
- d4a29399264b4d20e39d086ece4efcd5e3341fe7: deterministic red witness.
- 1c0eb2f13b58dae675c38ba7732b2d107e38ff0c: persisted-idle queue driver.
- c100106683ee98fa683d170675f351b7837b2a77: health marker ordering.

Changed: RemoteControlPtyLane.cs, RemoteControlRecoveryPtyIntegrationTests.cs,
and the investigation/report. RC merge-tree clean; master's ten conflicts are
identical at inherited base. No merge, rebase, tags, publication or activation.
Native Windows original class/ALONE20/LOAD10 remains OWED after d47848cc's lock;
preserve every failure's MaintenanceEvidence. No native run or C:\Antiphon access.
Removed 224 owned bin outputs by literal path, none remains; all children awaited. Raw evidence: /work/worktrees/task-dfd3f49c/.antiphon/c514-three-dfd3f49c.

Full investigation: /work/worktrees/task-dfd3f49c/docs/investigations/2026-10-30-c514-three-prompt-flake-debug.md

--- next stage ---
next: short Opus diff check then merge to rc; native re-run owed
handoff: scoped portable gate is green; review the two fixture repairs and preserved earlier reds, then retain the original Windows class/ALONE20/LOAD10 obligation
artifact: /work/worktrees/task-dfd3f49c/docs/investigations/2026-10-30-c514-three-prompt-flake-debug.md