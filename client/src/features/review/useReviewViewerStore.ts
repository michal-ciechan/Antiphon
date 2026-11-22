import { create } from 'zustand'

/** Who the review UI acts as. */
export interface ReviewViewer {
  login: string
  displayName: string
  teams: string[]
}

/**
 * TODO(identity): the review UI has no identity yet (design §11.1: JWT bearer or trusted proxy
 * headers mapped to a User, its code-host login and teams). Until it lands every page acts as
 * Alice, a payments-core member who exists in the fake data. Replace this with the authenticated
 * user and drop the "viewing as" switch in the fake-API banner.
 */
export const DEFAULT_REVIEW_VIEWER: ReviewViewer = { login: 'asmith', displayName: 'Alice Smith', teams: ['payments-core'] }

/** People the fake-API banner can switch to, so the author and reviewer views can both be tried. */
export const FAKE_VIEWERS: ReviewViewer[] = [
  DEFAULT_REVIEW_VIEWER,
  { login: 'hmartin', displayName: 'Hugo Martin', teams: ['payments-core', 'platform-sre'] },
  { login: 'ksato', displayName: 'Kenji Sato', teams: ['payments-core'] },
  { login: 'grao', displayName: 'Gita Rao', teams: ['platform-runtime'] },
  { login: 'druiz', displayName: 'Dana Ruiz', teams: ['web-experience'] },
]

interface ReviewViewerState {
  viewer: ReviewViewer
  setViewer: (viewer: ReviewViewer) => void
}

export const useReviewViewerStore = create<ReviewViewerState>((set) => ({
  viewer: DEFAULT_REVIEW_VIEWER,
  setViewer: (viewer) => set({ viewer }),
}))
