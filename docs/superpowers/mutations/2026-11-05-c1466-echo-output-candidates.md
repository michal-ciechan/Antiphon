# C1466 unbound mutation candidates

Original Code/landing owner: `69ade330-9ef8-47da-8414-72d5790a61ea`.
These are unbound candidates, all pending for method-scoped SourceLanding Mutation.
Code ran the compiling S1 regression witness red before implementing S2; this
ordinary red/green evidence does not discharge any PC or deliberate mutant cycle.
No pack is bound and no deliberate mutation has been executed by Code.

| PC | Variant/site | Exact test method in AgentSessionCancelledTurnTests | Expected discrimination |
|---|---|---|---|
| PC-1 | EchoWorld.CreateAdapter: turn SynchronousPromptOutput off | C1466_EchoWorld_output_precedes_withheld_async_work | first-output must already be true while asynchronous worker is gated |
| PC-2 | synchronous branch: omit Emit(PromptOutput) | C1466_EchoWorld_output_precedes_withheld_async_work | actual emitted diagnostic text is absent |
| PC-3 | synchronous branch: omit first-output TrySetResult | C1466_EchoWorld_output_precedes_withheld_async_work | emitted text cannot replace the output signal |
| PC-4 | complete signal unconditionally for empty PromptOutput | C1466_Empty_output_and_noise_do_not_signal_first_output | noise does not count as first prompt output |
| PC-5 | change shared option default to true | C1466_Default_output_waits_for_async_gate | default path must still wait for gate before emitting/signalling |
| PC-6 | bypass callbacks or injected prompt failure on synchronous branch | C1466_Synchronous_output_preserves_callback_and_prompt_failure | exact exception identity, callback/output order and prompt history |

Each cycle must use `/*/*/AgentSessionCancelledTurnTests/ExactTestMethod`, a fresh
compiling build, expected assertion red, restore and fresh green. Mutation owns
binding, missing-control discovery, red/restore/green and any additional variants.
