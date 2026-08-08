import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../api/agents'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { renderWithProviders, screen } from '../../test/utils'
import { AgentActivityBadge } from './AgentActivityBadge'

const runningSession: AgentSessionSummaryDto = {
  id: 'session-1',
  definitionName: 'claude',
  agentKind: 'ClaudeCode',
  status: 'Running',
  cwd: 'D:/src/app',
  createdAt: '2026-05-18T09:00:00Z',
  startedAt: '2026-05-18T09:00:00Z',
  lastSeenAt: '2026-05-18T09:00:00Z',
  endedAt: null,
  exitCode: null,
  failureReason: null,
}

const base: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  workingDirectory: 'D:/src/app',
  details: '',
  defaultWorkflowTemplateId: null,
  defaultWorkflowTemplateName: null,
  assignmentPolicy: 'AutoPick',
  status: 'Idle',
  persistentSessionId: null,
  currentCardId: null,
  boardId: null,
  boardName: null,
  queueLength: 0,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  liveSession: null,
  alwaysOn: false,
  remoteControlEnabled: false,
  supervision: null,
  systemPromptAppend: null,
  modelLevel: 'High',
  working: false,
}

const agent = (over: Partial<AgentSummaryDto>): AgentSummaryDto => ({ ...base, ...over })

describe('AgentActivityBadge', () => {
  it('says Working only when the session is mid-turn, never merely because it started', () => {
    const { unmount } = renderWithProviders(
      <AgentActivityBadge agent={agent({ status: 'Running', liveSession: runningSession, working: true })} />,
    )
    expect(screen.getByTestId('agent-working-agent-1')).toHaveTextContent('Working')
    unmount()

    // status=Running is "was started", not "is working" — the whole point of the split.
    renderWithProviders(
      <AgentActivityBadge agent={agent({ status: 'Running', liveSession: runningSession, working: false })} />,
    )
    expect(screen.queryByTestId('agent-working-agent-1')).not.toBeInTheDocument()
  })

  it('renders no badge at all for quiet states by default (liveness is the terminal icon)', () => {
    renderWithProviders(<AgentActivityBadge agent={agent({ status: 'Running', liveSession: runningSession })} />)
    expect(screen.queryByTestId('agent-working-agent-1')).not.toBeInTheDocument()
    expect(screen.queryByTestId('agent-activity-agent-1')).not.toBeInTheDocument()
    expect(screen.queryByText('Running')).not.toBeInTheDocument()
  })

  it('surfaces the attention states regardless of showIdle', () => {
    const { unmount } = renderWithProviders(
      <AgentActivityBadge agent={agent({ status: 'WaitingForHumanReview' })} />,
    )
    expect(screen.getByText('Review')).toBeInTheDocument()
    unmount()

    renderWithProviders(<AgentActivityBadge agent={agent({ status: 'Failed' })} />)
    expect(screen.getByText('Failed')).toBeInTheDocument()
  })

  describe('showIdle — surfaces with no terminal icon (files page header)', () => {
    it('reads Idle for a live session that is not mid-turn', () => {
      renderWithProviders(
        <AgentActivityBadge agent={agent({ status: 'Running', liveSession: runningSession })} showIdle />,
      )
      expect(screen.getByTestId('agent-activity-agent-1')).toHaveTextContent('Idle')
    })

    it('reads the lifecycle status when there is no live session', () => {
      renderWithProviders(<AgentActivityBadge agent={agent({ status: 'Stopped' })} showIdle />)
      expect(screen.getByTestId('agent-activity-agent-1')).toHaveTextContent('Stopped')
    })

    it('still prefers Working over the idle fallback', () => {
      renderWithProviders(
        <AgentActivityBadge
          agent={agent({ status: 'Running', liveSession: runningSession, working: true })}
          showIdle
        />,
      )
      expect(screen.getByTestId('agent-working-agent-1')).toHaveTextContent('Working')
      expect(screen.queryByTestId('agent-activity-agent-1')).not.toBeInTheDocument()
    })
  })
})
