# CARD-0861: qualify Grok startup sizes

Base: eae23eeddae19ba5fe81d8a1bf81ea30c64589ff. Code/landing owner: 6d69efc4.

The card is not already satisfied. GrokStartupScreen.Classify requires exactly
30 rendered rows and composer borders at columns 2 and 117. This rejects healthy
dashboards at other sizes before checking their empty composer and enabled hint.
Trust/sign-in already compact whitespace, including wrapped prompt text. Banner
position and a model label in the lower border are not readiness witnesses.

Decision: capture the installed Grok 1.0.41 at 80x24, 100x30, 120x30, 160x50,
200x60 and 40x20. Admit only measured dimensions and their measured composer
geometry; retain both captured Windows/Linux markers at 120x30. Infer width from
the right border, with two cells of outer padding. A clipped hint, nonempty
composer, activity or unknown layout stays not-ready. Narrow 40x20 stays Unknown.
Keep the existing 120x29 Unknown expectation. CARD-0988's waiter is untouched;
no readiness timeout or assertion is widened.

Files: src/Antiphon.Agents.Pty/GrokStartupReadiness.cs;
tests/Antiphon.Tests/Agents/GrokStartupSizeTests.cs;
tests/Antiphon.Tests/Agents/GrokSizeCaptureTests.cs (explicit manual capture);
tests/Antiphon.Tests/Agents/Fixtures/card0861/ (retained regression inputs).
Capture uses owned local PtyAgentRunner children, no model prompt, a fresh cwd,
current authenticated home for ready/trust and an empty home for welcome sign-in.
No raw output is exported. Record actual observations, never resize fixed-width
ANSI from the 120x30 fixture and claim it is a native capture.

## Verification design

| ID | Case -> guarded defect/mutation -> expected red |
|---|---|
| V-1 | Real native ready fixtures at 80x24, 100x30, 160x50, 200x60 -> retain master dimensions -> IsReady true receives false/Unknown on pre-slice source. Each row also has one changed composer cell -> nonempty input must not become ready. |
| V-2 | 120x30 and 40x20 -> remove empty-input guard / admit narrow geometry -> changed composer becomes ready / narrow becomes ready. Handwritten Ready and Unknown oracles; existing 120x30 expectations unchanged. |
| V-3 | Captured trust/sign-in at each size if observable -> skip corresponding blocker -> receives Ready or Unknown instead of named blocker. No synthetic frame is described as real. Unobserved trust capture is reported as a split rather than fabricated. |
| V-4 | Runner adapter consumes each admitted nondefault ready capture without input -> retain master dimensions -> bounded waiter false instead of true. |
| R-1 | Full GrokStartupReadinessTests and GrokLinuxBlockingPromptTests preserve default fixture semantics, precedence and CARD-0988 expired-read/fault behavior. |
| R-2 | Full RunnerGrokAdapterReadyTests preserve coherent snapshots, trust, cancellation, deadlines and launch blocks. |
| M-1 | Explicit native capture records actual size, CLI version, zero model input, screen and joined child exit. Inspect geometry and prompt wrapping manually before implementing. |

PC-1 ready dimensions: each of the four nondefault V-1 rows, restore the master
size check, detecting method GrokStartupSizeTests.Captured_ready_size_is_qualified.
PC-2 each supported size's nonempty composer: remove the input-content guard,
same detecting method. PC-3 narrow size: admit 40x20, detecting method
GrokStartupSizeTests.Narrow_layout_stays_unknown. PC-4 each observed blocker
variant: skip detector, detecting method GrokStartupSizeTests.Captured_blocker_stays_blocked.
PC-5 each nondefault adapter row: restore master geometry, detecting method
RunnerGrokAdapterReadyTests.Captured_nondefault_size_reaches_ready_without_input.
All PCs/variants remain pending for method-scoped SourceLanding Mutation.

This slice's binding house rule explicitly prohibits a broad Unit lane. The
closed scope is the named full classes below, overriding the generic Final
profile's Unit default. There is no unbounded assembly impact: only the Grok
startup matcher changes; waiter, delivery, leases and persistence are unchanged.
CP-2 is intentionally red against unchanged base production, then CP-3/4 qualify
the implementation at committed HEAD. One isolated build per committed group,
UseAppHost=false on Linux; reused rows use the same source/build certificate.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c861-capture/` | native-capture | `/*/*/GrokSizeCaptureTests/Capture_native_sizes` | M-1 | all 1 method, 0 failed/skipped | 1 | 5 | true | `C861_CAPTURE=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c861-red/` | pre-fix-red | `/*/*/GrokStartupSizeTests/*` | V-1, V-2, V-3 | intended V-1 failures on base production; other rows inspected | 1 | 4 | true | n/a |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c861-final/` | sizes-and-fixtures | `/*/*/(GrokStartupSizeTests\|GrokStartupReadinessTests)/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed/skipped | 10 | 4 | true | n/a |
| CP-4 | S3 | CP-3 | adapters-and-blockers | `/*/*/(RunnerGrokAdapterReadyTests\|GrokLinuxBlockingPromptTests)/*` | V-4, R-1, R-2 | all listed, 0 failed/skipped | 30 | 3 | true | n/a |

### Cost

Ordinary floor: 16 minutes, plus about 25 minutes authoring/capture inspection.
No repeats required after green. Restart: none. Review follows ordinary V/R;
caller lands this original Code task and commissions SourceLanding Mutation.
