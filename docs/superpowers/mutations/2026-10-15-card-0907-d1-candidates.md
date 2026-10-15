# CARD-0907 D1 unbound mutation candidates

Code repair ec8f807a; original landing owner cd023562-f0f0-4238-84b4-063f9d03077b.
These are unbound candidates for caller-commissioned SourceLanding Mutation after
ordinary Review and publication. Code has run no deliberate mutants. Mutation owns
binding to the landed SHA, precise method-scoped selections, red/restore/green and
missing-control discovery. Do not execute a candidate against this moving branch.

Parent PC-1 through PC-12, all variants, remain pending. PC-6 now targets the opted-in
Codex cleaning branch in CodexResponseAnalyzer.ExtractResponse; the V0160 guard calls
that branch explicitly. Its oracle and assertion label remain unchanged.

| Candidate | Guard and first intended assertion | Status |
|---|---|---|
| D1-C1: enable cursor separation for shared default callers | RunnerGrokAdapterTurnCompleteTests.Cursor_moves_in_answer_keep_plain_stripping / C907-D1-grok-answer; OpenCodeAdapterTests.Cursor_moves_in_answer_keep_plain_stripping / C907-D1-opencode-answer | Pending discovery/binding |
| D1-C2: remove plain echo retry only | CodexResponseAnalyzerTests.Cursor_move_inside_echo_word_does_not_become_an_agent_question / C907-D1-codex-echo; existing-space method is a separate variant | Pending discovery/binding |
| D1-C3: remove RunnerCodexAdapter raw-fallback opt-in | CodexResponseAnalyzerTests.V0160_fallback_preserves_answer_word_boundaries / C907-fallback-answer-cup | Pending discovery/binding |
| D1-C4: remove CodexAdapter raw opt-in | CodexAdapterRawResponseTests.Raw_response_excludes_echo_and_preserves_codex_word_boundaries / C907-D1-inproc-codex-echo on the cursor-separated echo/answer argument | Pending discovery/binding |

Ordinary test-first assertion failures reproduce Review 2f237374 D1 at the inherited
production source. They do not discharge any positive control.
