import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'

/**
 * The read-only plan projection (mobile-thread spec §3, slice T1): the server lists markdown
 * plans straight out of the repo — dated specs under `docs/superpowers/specs/` and
 * `proposal.md` files under `docs/features/<name>/` — and serves one by name. There is no write
 * half and never will be: git is the store, this is a lens over it.
 *
 * Two error shapes matter to callers and must stay distinct (the server draws them deliberately):
 * a path that resolves outside the plan roots is a **422** refusal — never a 404, so a prober
 * learns nothing about the disk — while a well-formed name with no file behind it is a plain 404.
 */

/** Which convention the file follows. One list, one reader; the kind is a label, not a fork. */
export type PlanKind = 'Spec' | 'Proposal'

/** One plan file, best-effort parsed: only the path pair is guaranteed. */
export interface PlanSummaryDto {
  /** Root-relative, forward slashes — the `?file=` key the content route takes. */
  relativePath: string
  fileName: string
  kind: PlanKind
  /** First `#` heading, else the filename humanised. */
  title: string
  /** `"2026-08-17"` — null on proposals, which carry no date. */
  date: string | null
  /** The `Status:` header line verbatim; deliberately unclassified. */
  status: string | null
  /** Identifiers this plan is ABOUT (filename, title, `Card(s):` header). */
  cards: string[]
  /** Every other `CARD-nnnn` cited in the first 200 lines — neighbours, not subjects. */
  mentionedCards: string[]
  sizeBytes: number
  modifiedAt: string
}

export interface PlanCatalogDto {
  root: string | null
  /**
   * False means the list is ABSENT, not empty — "no plans" and "nobody could find the repo" are
   * different answers, and the page says so rather than showing a confident empty state.
   */
  rootResolved: boolean
  generatedAt: string
  plans: PlanSummaryDto[]
}

export interface PlanContentDto {
  plan: PlanSummaryDto
  content: string
}

export const planKeys = {
  all: ['plans'] as const,
  catalog: (path: string | null) => ['plans', 'catalog', path ?? ''] as const,
  content: (path: string | null, file: string) => ['plans', 'content', path ?? '', file] as const,
}

/**
 * `path` is the repo root to look under, resolved server-side by walking UP — a worktree
 * subdirectory works. Null means "the server's own checkout", the common case. The 30s staleTime
 * matches the server's own catalog cache window; no interval — plans change at git speed.
 */
export function usePlanCatalog(path: string | null) {
  return useQuery({
    queryKey: planKeys.catalog(path),
    queryFn: () => {
      const query = path === null ? '' : `?path=${encodeURIComponent(path)}`
      return apiGet<PlanCatalogDto>(`/plans${query}`)
    },
    staleTime: 30_000,
  })
}

export function usePlanContent(path: string | null, file: string | null) {
  return useQuery({
    queryKey: planKeys.content(path, file ?? ''),
    queryFn: () => {
      const params = new URLSearchParams()
      if (path !== null) params.set('path', path)
      params.set('file', file!)
      return apiGet<PlanContentDto>(`/plans/content?${params.toString()}`)
    },
    enabled: file !== null,
    staleTime: 30_000,
  })
}
