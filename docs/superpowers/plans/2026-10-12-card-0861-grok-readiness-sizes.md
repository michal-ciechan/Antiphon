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
tests/Antiphon.Tests/Agents/Fixtures/card0861/ (retained regression inputs);
docs/session-runtime-invariants.md (qualified-size contract).
Unbound Mutation recipe: docs/superpowers/mutations/card-0861/README.md.
Capture uses owned local PtyAgentRunner children, no model prompt, a fresh cwd,
current authenticated home for ready/trust and an empty home for welcome sign-in.
No raw output is exported. Standalone device codes and URL rows are redacted in
memory before persistence, preserving rendered width. Record actual observations, never resize fixed-width
ANSI from the 120x30 fixture and claim it is a native capture.

## Verification design

| ID | Case -> guarded defect/mutation -> expected red |
|---|---|
| V-1 | Real native ready fixtures at 80x24, 100x30, 160x50, 200x60 -> retain master dimensions -> IsReady true receives false/Unknown on pre-slice source. Each row also has one changed composer cell -> nonempty input must not become ready. |
| V-2 | 120x30 and 40x20 -> remove empty-input guard / admit narrow geometry -> changed composer becomes ready / narrow becomes ready. Handwritten Ready and Unknown oracles; existing 120x30 expectations unchanged. |
| V-3 | Native sign-in at all six sizes and explicitly synthetic replays of the historical real trust prompt at those sizes -> skip corresponding blocker -> receives Ready or Unknown instead of named blocker. One code-row flip retains SignIn; one affirmative-phrase flip changes Trust to Unknown. Native 1.0.41 trust was not observed and is not claimed qualified by capture. |
| V-4 | Runner adapter consumes each admitted nondefault ready capture without input -> retain master dimensions -> bounded waiter false instead of true. |
| R-1 | Full GrokStartupReadinessTests, GrokLinuxStartupReadinessTests and GrokLinuxBlockingPromptTests preserve default fixture semantics, precedence and CARD-0988 expired-read/fault behavior. |
| R-2 | Full RunnerGrokAdapterReadyTests preserve coherent snapshots, trust, cancellation, deadlines and launch blocks. |
| M-1 | Explicit native capture records actual size, CLI version, zero model input, screen and joined child exit. Inspect geometry and prompt wrapping manually before implementing. |

| PC | Variants | Mutation | Detecting filter | Expected red |
|---|---|---|---|---|
| PC-1 | 80x24, 100x30, 160x50, 200x60 | restore master dimension check | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | native-ready is false instead of true |
| PC-2 | five admitted sizes | remove empty-input check | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | changed marker becomes ready |
| PC-3 | 40x20 | return Ready rather than Unknown for the unsupported 20-row screen at the height guard | `/*/*/GrokStartupSizeTests/Captured_ready_size_is_qualified` | narrow becomes ready instead of Unknown |
| PC-4a | all six sizes | skip sign-in detector | `/*/*/GrokStartupSizeTests/Captured_sign_in_stays_blocked` | reason is not SignIn |
| PC-4b | all six sizes | skip trust detector | `/*/*/GrokStartupSizeTests/Historical_trust_prompt_replayed_at_size_stays_blocked` | reason is not Trust |
| PC-5 | four nondefault admitted sizes | restore master geometry | `/*/*/RunnerGrokAdapterReadyTests/Captured_nondefault_size_reaches_ready_without_input` | adapter is false instead of true |

All 26 PC variants remain pending for method-scoped SourceLanding Mutation.
The existing blocker/narrow behaviors are negative controls, expected green on
base; their detecting mutations belong to Mutation. The eight newly admitted
ready/adapter cases must fail against unchanged base production before the fix.

This slice's binding house rule explicitly prohibits a broad Unit lane. The
closed scope is the named full classes below, overriding the generic Final
profile's Unit default. There is no unbounded assembly impact: only the Grok
startup matcher changes; waiter, delivery, leases and persistence are unchanged.
CP-2/6 ran intentionally red against unchanged base production at 125869409c975f8b85ecb4663e8740aabd87ce6d
(historical manifest used bin-c861-red/). Rerun those same rows green after S3,
then run the remaining full affected classes from that one final build.
Native CP-1's final capture SHA is 2c8cdcbc3eef6b47a177875a420e8d1afcbc3b40;
no native trust was observed. One isolated build per committed group,
UseAppHost=false on Linux; reused rows use the same source/build certificate.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c861-capture/` | native-capture | `/*/*/GrokSizeCaptureTests/Capture_native_sizes` | M-1 | all 1 method, 0 failed/skipped | 1 | 5 | true | `C861_CAPTURE=1` |
| CP-2 | S2-S3 | `tests/Antiphon.Tests -> bin-c861-final/` | size-regressions | `/*/*/GrokStartupSizeTests/*` | V-1, V-2, V-3 | all 18 results, 0 failed/skipped after fix | 18 | 4 | true | n/a |
| CP-6 | S2-S3 | CP-2 | adapter-regressions | `/*/*/RunnerGrokAdapterReadyTests/Captured_nondefault_size_reaches_ready_without_input` | V-4 | all four results, 0 failed/skipped after fix | 4 | 1 | true | n/a |
| CP-3 | S2-S3 | CP-2 | existing-fixtures | `/*/*/(GrokStartupReadinessTests*)\|(GrokLinuxStartupReadinessTests*)/*` | R-1 | all 26 results, 0 failed/skipped | 26 | 1 | true | n/a |
| CP-4 | S2-S3 | CP-2 | adapters | `/*/*/RunnerGrokAdapterReadyTests/*` | V-4, R-2 | all 22 results, 0 failed/skipped | 22 | 2 | true | n/a |
| CP-5 | S2-S3 | CP-2 | blockers | `/*/*/GrokLinuxBlockingPromptTests/*` | R-1 | all listed, 0 failed/skipped | 11 | 1 | true | n/a |

### Cost

Ordinary floor: 14 minutes, plus about 25 minutes authoring/capture inspection.
No repeats required after green. Activation requires server restart after landing;
no restart is performed by this Code task. Review follows ordinary V/R;
caller lands this original Code task and commissions SourceLanding Mutation.
Final source review retains the original vertical composer search; qualification
changes width/height pairs, not the existing relative status/hint geometry.
