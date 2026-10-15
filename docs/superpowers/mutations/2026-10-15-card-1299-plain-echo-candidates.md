# CARD-1299 unbound mutation candidate

PC-1 remains pending for SourceLanding Mutation after ordinary Review and landing
of original Code task 095a7f1a-4260-43dc-b1dd-f0505eb03695.

Restore separated-first matching in CodexResponseAnalyzer.ExtractResponse; detecting
method: `/*/*/CodexResponseAnalyzerTests/Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match`.
Expected assertion: extracted response must equal Done.; the broken order retains
the later literal prompt and reports a question. Mutation owns deliberate red,
restore, green and any missing-control discovery. No variants have been executed.
