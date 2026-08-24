import { ApiError } from './client'
import type { ConsoleRingEntry } from '../shared/consoleRing'

export const CLIENT_SHA_HEADER = 'X-Antiphon-Client-Sha'

export interface BugReportRequest {
  route?: string
  agentId?: string
  sessionId?: string
  screenshotPngBase64?: string
  console?: ConsoleRingEntry[]
  includePaths?: boolean
  note?: string
}

export async function downloadDiagnosticsBundle(
  body: BugReportRequest,
): Promise<{ blob: Blob; filename: string }> {
  const response = await fetch('/api/diagnostics/bundle', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      [CLIENT_SHA_HEADER]: __ANTIPHON_SHA__,
    },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    const text = await response.text()
    let parsed: unknown = text
    if (text) {
      try {
        parsed = JSON.parse(text)
      } catch {
        parsed = text
      }
    }
    throw new ApiError(response.status, response.statusText, parsed)
  }

  const disposition = response.headers.get('Content-Disposition') ?? ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(disposition)
  const filename = match?.[1]?.replace(/"/g, '') ?? 'antiphon-bug.zip'
  return { blob: await response.blob(), filename }
}
