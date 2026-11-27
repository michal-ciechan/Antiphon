# GH-61 Workflow step output unbound mutation candidates

Original Code and landing owner `243d3d7d`. The candidate witnesses are in `WorkflowStepOutput.test.tsx`. Each name below selects one Vitest case. SourceLanding Mutation owns binding, execution and restoration.

| ID | Candidate mutation | Exact witness name | Required red | Status |
|---|---|---|---|---|
| PC-1 | Remove the pre-wrap style from the output block | Workflow execution output preserves line breaks and indentation in a wrapping monospace block | Preserved-whitespace style assertion | Pending |
| PC-2 | Render the unsplit summary in the output block | Workflow execution output separates commit-status warnings and keeps a routing failure in the output | Output contains warning lines | Pending |
| PC-3a | Remove horizontal overflow from the execution history region | Workflow execution output keeps wide execution rows in a keyboard-focusable horizontal scroll region | Region overflow assertion | Pending |
| PC-3b | Remove the execution history region from tab order | Workflow execution output keeps wide execution rows in a keyboard-focusable horizontal scroll region | Region focus assertion | Pending |
| PC-4a | Remove the Succeeded badge | Workflow graph outcome shows the recorded Succeeded outcome and its route | Succeeded missing | Pending |
| PC-4b | Render a recorded edge label for Failed | Workflow graph outcome shows the recorded Failed outcome and its route | Unfollowed route shown | Pending |
| PC-4c | Display a skipped execution as successful | Workflow graph outcome shows the recorded Skipped outcome and its route | Skipped missing | Pending |
| PC-4d | Display a continued failure as successful | Workflow graph outcome shows the recorded FailedContinued outcome and its route | Failed, continued missing | Pending |
| PC-5 | Remove the successful output preview when there is no recorded edge label | Workflow graph outcome shows the first output line of a successful step without a labelled route | Result preview missing | Pending |
