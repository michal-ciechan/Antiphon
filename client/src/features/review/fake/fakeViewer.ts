import { create } from 'zustand'
import { FAKE_PEOPLE } from './seed'

/**
 * Fake mode only: who the in-memory API acts as. The fake's GET /api/review-me answers with this
 * person, every fake mutation records them as the actor, and the fake computes each gate's
 * `viewerCanDecide` against them. The fake-API banner's "Viewing as" switch sets it, so the author
 * and reviewer views can both be tried. The UI itself reads the viewer from `useReviewMe()`, never
 * from here; in HTTP mode the server resolves the viewer (contract: `GET /api/review-me`).
 */
export const FAKE_DEFAULT_VIEWER_LOGIN = 'asmith'

/** People the banner can switch to: an author (Alice on #612), reviewers in two teams, and an outsider. */
export const FAKE_VIEWERS: { login: string; displayName: string }[] = ['asmith', 'hmartin', 'ksato', 'grao', 'druiz'].map((login) => ({
  login,
  displayName: FAKE_PEOPLE.find((p) => p.login === login)?.name ?? login,
}))

interface FakeViewerState {
  login: string
  setLogin: (login: string) => void
}

export const useFakeViewerStore = create<FakeViewerState>((set) => ({
  login: FAKE_DEFAULT_VIEWER_LOGIN,
  setLogin: (login) => set({ login }),
}))
