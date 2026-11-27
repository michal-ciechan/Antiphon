# GH-59 candidate positive controls

All controls are pending. Code and landing owner `57c44227-4884-42f1-8f50-afb726202c23`. Bind to the landed source before execution.

| ID | Variant | Exact method on `ReviewEndToEndApiTests` | Expected failure |
|---|---|---|---|
| PC-1 | Omit the summary link in `ReviewReviewSteps.SummaryBody`. | `Reviews_and_inline_comments_link_once_to_the_same_run_across_publications` | Summary heading and link assertion. |
| PC-2 | Append the inline footer twice in `ReviewReviewSteps.CommentBody`. | `Reviews_and_inline_comments_link_once_to_the_same_run_across_publications` | Comment destination occurs more than once. |
| PC-3 | Use the current iteration ID for the publish URL instead of the run ID. | `Reviews_and_inline_comments_link_once_to_the_same_run_across_publications` | Summary URL differs from the one built from the run ID. |
| PC-4 | Remove the user-info rejection and preserve the supplied credentials in the generated URL. | `Reviews_and_inline_comments_keep_the_original_bodies_without_a_safe_public_base` | Original summary mismatch for the synthetic user-info case. |
| PC-5 | Remove the query rejection while still constructing a link for query-bearing input. | `Reviews_and_inline_comments_keep_the_original_bodies_without_a_safe_public_base` | Original summary mismatch for the synthetic query case. |
| PC-6 | Replace the omission on missing input with a localhost fallback. | `Reviews_and_inline_comments_keep_the_original_bodies_without_a_safe_public_base` | Original summary mismatch for the absent setting. |
| PC-7 | Pass the footer only to the first inline comment of each review. | `Reviews_and_inline_comments_link_once_to_the_same_run_across_publications` | The second inline comment on the existing run has no footer. |
