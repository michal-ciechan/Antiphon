import type { NodeExecutionDto } from '../../../../api/reviewEngine'

const GRAPH_PREVIEW_SIZE = 160
export const OUTPUT_PAGE_SIZE = 4096

export function summaryWithoutLegacyWarnings(execution: NodeExecutionDto): string {
  const summary = execution.summary ?? ''
  if (!execution.warnings?.length) return summary

  // The executor appends the structured warnings verbatim before any missing-edge failure.
  // Matching the whole block preserves warning-like lines inside filenames and other output.
  const block = '\n' + execution.warnings.join('\n')
  if (summary.endsWith(block)) return summary.slice(0, -block.length)

  if (execution.status === 'Failed') {
    const routeFailure = `\nNo edge leaves '${execution.nodeId}' for outcome '${execution.outcome ?? '(none)'}'.`
    const routeStart = summary.length - routeFailure.length
    if (summary.endsWith(routeFailure) && summary.endsWith(block, routeStart)) {
      return summary.slice(0, routeStart - block.length) + routeFailure
    }
  }
  return summary
}

function textBoundary(text: string, index: number): number {
  const char = text.charCodeAt(index)
  const previous = text.charCodeAt(index - 1)
  // Keep UTF-16 surrogate pairs and CRLF line endings together across pages.
  return (previous >= 0xd800 && previous <= 0xdbff && char >= 0xdc00 && char <= 0xdfff) || (previous === 13 && char === 10)
    ? index - 1
    : index
}

export function outputPage(text: string, page: number): string {
  return text.slice(textBoundary(text, page * OUTPUT_PAGE_SIZE), textBoundary(text, (page + 1) * OUTPUT_PAGE_SIZE))
}

export function summaryPreview(text: string): string {
  const prefix = text.slice(0, textBoundary(text, 1024))
  return prefix.match(/^(?:[^\n]*\n){16}/)?.[0] ?? prefix
}

export function graphSummaryPreview(summary: string | null): string {
  const first = summary?.slice(0, GRAPH_PREVIEW_SIZE + 1).trimStart().split(/\r?\n/, 1)[0].trimEnd() ?? ''
  return first.length > GRAPH_PREVIEW_SIZE ? first.slice(0, textBoundary(first, GRAPH_PREVIEW_SIZE - 1)) + '…' : first
}
