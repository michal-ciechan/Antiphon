import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Notifications } from '@mantine/notifications'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { pushConsoleEntry, resetConsoleRing } from '../../shared/consoleRing'
import { ReportBugButton } from './ReportBugButton'

const downloadDiagnosticsBundle = vi.fn()

vi.mock('html-to-image', () => ({
  toPng: vi.fn(async () => 'data:image/png;base64,aaa'),
}))

vi.mock('../../api/diagnostics', () => ({
  downloadDiagnosticsBundle: (...args: unknown[]) => downloadDiagnosticsBundle(...args),
}))

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>()
  return { ...actual, notifications: { show: vi.fn() } }
})

function renderButton() {
  return renderWithProviders(
    <>
      <Notifications />
      <ReportBugButton agentId="agent-1" sessionId="session-1" />
    </>,
  )
}

describe('ReportBugButton', () => {
  beforeEach(() => {
    resetConsoleRing()
    downloadDiagnosticsBundle.mockReset()
    downloadDiagnosticsBundle.mockResolvedValue({
      blob: new Blob(['PK']),
      filename: 'antiphon-bug-test.zip',
    })
    vi.stubGlobal(
      'URL',
      Object.assign(window.URL, {
        createObjectURL: vi.fn(() => 'blob:test'),
        revokeObjectURL: vi.fn(),
      }),
    )
  })

  it('opens a modal with Include local paths defaulting off', async () => {
    renderButton()
    await userEvent.click(screen.getByTestId('report-bug-button'))
    expect(screen.getByTestId('report-bug-modal')).toBeInTheDocument()
    const toggle = screen.getByTestId('include-paths-switch')
    expect(toggle).not.toBeChecked()
  })

  it('posts the ring buffer on download', async () => {
    pushConsoleEntry({ level: 'error', message: 'ring-entry' })
    renderButton()
    await userEvent.click(screen.getByTestId('report-bug-button'))
    await userEvent.click(screen.getByTestId('report-bug-download'))

    await waitFor(() => expect(downloadDiagnosticsBundle).toHaveBeenCalled())
    const body = downloadDiagnosticsBundle.mock.calls[0][0] as {
      console: Array<{ message: string }>
      includePaths: boolean
      agentId: string
      sessionId: string
      screenshotPngBase64: string
    }
    expect(body.includePaths).toBe(false)
    expect(body.agentId).toBe('agent-1')
    expect(body.sessionId).toBe('session-1')
    expect(body.screenshotPngBase64).toContain('base64')
    expect(body.console.some((e) => e.message === 'ring-entry')).toBe(true)
  })
})
