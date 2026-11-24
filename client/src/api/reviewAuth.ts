import { apiGet } from './client'

/** `Review:Auth:Mode`: no sign-in (one default viewer), or sign-in with GitHub. */
export type ReviewAuthMode = 'None' | 'GitHub'

/** `GET /api/review-auth/status`: whether sign-in is on, and who is acting. */
export interface ReviewAuthStatusDto {
  mode: ReviewAuthMode
  signedIn: boolean
  login: string | null
  displayName: string | null
}

export const reviewAuthKeys = { status: ['reviewAuthStatus'] as const }

export function getReviewAuthStatus(): Promise<ReviewAuthStatusDto> {
  return apiGet<ReviewAuthStatusDto>('/review-auth/status')
}

/** Ends the session. A form post (not fetch): the server answers with a "signed out" page. */
export function signOut(): void {
  const form = document.createElement('form')
  form.method = 'post'
  form.action = '/api/review-auth/logout'
  document.body.appendChild(form)
  form.submit()
}
