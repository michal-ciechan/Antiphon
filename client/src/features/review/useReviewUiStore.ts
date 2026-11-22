import { create } from 'zustand'
import type { QueueScope, ReviewRunStatus } from '../../api/reviewEngine'

export type RunsView = 'table' | 'board'

export interface RunsFilters {
  statuses: ReviewRunStatus[]
  /** waitingOn === 'Humans' */
  humans: boolean
  /** round >= 3 */
  deepRounds: boolean
  models: string[]
  repo: string | null
  q: string
}

export const EMPTY_RUNS_FILTERS: RunsFilters = { statuses: [], humans: false, deepRounds: false, models: [], repo: null, q: '' }

/**
 * Review-page UI state that should survive navigating into a run and back: the runs filters and
 * view, the queue tab, each viewer's expanded/collapsed workflow sections (design §3.3: "each
 * viewer's expand and collapse choices are kept"), and whether the server has demo seeding off.
 */
interface ReviewUiState {
  runsView: RunsView
  runsFilters: RunsFilters
  queueScope: QueueScope
  /** workflow name → section id → collapsed */
  sectionCollapse: Record<string, Record<string, boolean>>
  /** `POST /api/review-runs/demo` answered 404 (seeding off), so "Load demo data" stays hidden */
  demoSeedUnavailable: boolean
  setRunsView: (view: RunsView) => void
  patchRunsFilters: (patch: Partial<RunsFilters>) => void
  clearRunsFilters: () => void
  setQueueScope: (scope: QueueScope) => void
  setSectionCollapsed: (workflow: string, sectionId: string, collapsed: boolean) => void
  setAllSectionsCollapsed: (workflow: string, sectionIds: string[], collapsed: boolean) => void
  setDemoSeedUnavailable: (unavailable: boolean) => void
}

export const useReviewUiStore = create<ReviewUiState>((set) => ({
  runsView: 'table',
  runsFilters: EMPTY_RUNS_FILTERS,
  queueScope: 'Mine',
  sectionCollapse: {},
  demoSeedUnavailable: false,
  setRunsView: (runsView) => set({ runsView }),
  patchRunsFilters: (patch) => set((s) => ({ runsFilters: { ...s.runsFilters, ...patch } })),
  clearRunsFilters: () => set((s) => ({ runsFilters: { ...EMPTY_RUNS_FILTERS, q: s.runsFilters.q, repo: s.runsFilters.repo } })),
  setQueueScope: (queueScope) => set({ queueScope }),
  setSectionCollapsed: (workflow, sectionId, collapsed) =>
    set((s) => ({ sectionCollapse: { ...s.sectionCollapse, [workflow]: { ...s.sectionCollapse[workflow], [sectionId]: collapsed } } })),
  setAllSectionsCollapsed: (workflow, sectionIds, collapsed) =>
    set((s) => ({ sectionCollapse: { ...s.sectionCollapse, [workflow]: Object.fromEntries(sectionIds.map((id) => [id, collapsed])) } })),
  setDemoSeedUnavailable: (demoSeedUnavailable) => set({ demoSeedUnavailable }),
}))
