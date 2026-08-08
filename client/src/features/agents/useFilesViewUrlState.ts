import { useSearchParams } from 'react-router'
import type { FilesViewSelection, FileViewMode } from './FilesReviewPanel'

const VIEW_MODES: FileViewMode[] = ['diff', 'raw', 'rendered']

/**
 * Backs the files view's "which file, which mode" by the query string, so a refresh (or a pasted
 * link, or the back button) reopens exactly what you were looking at:
 *
 *     /agents/:id/files?file=docs/README.md          — open, showing its default view
 *     /agents/:id/files?file=src/app.ts&view=raw     — open, showing a deliberately chosen view
 *
 * `view` is written ONLY when the mode is not the file's default, so ordinary browsing leaves a
 * clean URL and `?view=` always means "the human asked for this one".
 *
 * Selecting a file REPLACES the history entry rather than pushing: clicking through twenty files
 * should not bury the page you arrived from under twenty back-presses. Closing the file
 * (`select(null)`) drops both params.
 */
export function useFilesViewUrlState(): FilesViewSelection {
  const [searchParams, setSearchParams] = useSearchParams()

  const rawView = searchParams.get('view')
  const view = VIEW_MODES.includes(rawView as FileViewMode) ? (rawView as FileViewMode) : null

  const write = (path: string | null, nextView: FileViewMode | null) =>
    setSearchParams(
      (previous) => {
        const next = new URLSearchParams(previous)
        if (path) next.set('file', path)
        else next.delete('file')
        if (path && nextView) next.set('view', nextView)
        else next.delete('view')
        return next
      },
      { replace: true },
    )

  return {
    selectedPath: searchParams.get('file'),
    view,
    // A different file gets its own default — carrying `raw` onto the next file would silently
    // override a default the user never rejected for it.
    select: (path) => write(path, null),
    setView: (nextView) => write(searchParams.get('file'), nextView),
  }
}
