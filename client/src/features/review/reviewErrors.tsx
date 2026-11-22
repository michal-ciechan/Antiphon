import { notifications } from '@mantine/notifications'
import { ApiError, getApiErrorMessage } from '../../api/client'
import type { ReviewCodeHostErrorCode } from '../../api/reviewEngine'
import { ReviewProblemText } from './components/ReviewProblem'

/** A review API failure as the UI shows it: the Problem Details `detail`, its `code` and the status. */
export interface ReviewProblem {
  detail: string
  code: string | null
  status: number | null
  /** what to do next, for codes whose detail alone does not say (the 503 code-host codes) */
  hint: string | null
}

const CODE_HOST_HINTS: Record<ReviewCodeHostErrorCode, string> = {
  review_code_host_rejected: "The code host refused the request: check the repo name and the token's access.",
  review_code_host_rate_limited: 'The code host is rate-limiting Antiphon. Try again in a few minutes.',
  review_code_host_unavailable: 'The code host did not answer. Try again shortly.',
  review_code_host_too_large: 'The change is too large for the code host to return.',
}

export function reviewProblem(error: unknown, fallback: string): ReviewProblem {
  const body = error instanceof ApiError && error.body && typeof error.body === 'object' ? (error.body as Record<string, unknown>) : null
  const code = typeof body?.code === 'string' && body.code ? body.code : null
  return {
    detail: getApiErrorMessage(error, fallback),
    code,
    status: error instanceof ApiError ? error.status : null,
    hint: code && code in CODE_HOST_HINTS ? CODE_HOST_HINTS[code as ReviewCodeHostErrorCode] : null,
  }
}

/**
 * A failed review mutation as a notification: the server's `detail` and `code` (403 author gate,
 * 409 blocker and transition rules, 422 validation, 503 code host). The mutation hooks call it.
 */
export function notifyReviewError(title: string, error: unknown, fallback = 'The review API refused it.') {
  const problem = reviewProblem(error, fallback)
  notifications.show({ color: 'danger', title, message: <ReviewProblemText problem={problem} />, autoClose: problem.status === 503 ? 10_000 : 6_000 })
}
