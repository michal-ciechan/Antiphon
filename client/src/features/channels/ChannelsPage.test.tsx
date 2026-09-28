import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { notifications } from '@mantine/notifications'
import type { ChatChannelDto } from '../../api/channels'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ChannelsPage } from './ChannelsPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

function channel(over: Partial<ChatChannelDto> = {}): ChatChannelDto {
  return {
    id: 'ch-1',
    provider: 'telegram',
    externalId: '-1001',
    kind: 'Direct',
    title: 'Family',
    agentId: null,
    agentName: null,
    enabled: true,
    lastMessageAt: '2026-09-03T18:00:00Z',
    lastMessagePreview: 'hello',
    lastAuthor: 'Mike Ciechan',
    lastReplyAt: '2026-09-03T18:05:00Z',
    lastReplyPreview: 'On it.',
    messageCount: 4,
    createdAt: '2026-09-01T00:00:00Z',
    alertMinSeverity: null,
    digestEnabled: false,
    digestLastSentAt: null,
    outboundAgentProfile: null,
    outboundProfile: null,
    ...over,
  }
}

describe('ChannelsPage', () => {
  it('shows the outbound reply stamp after the inbound last-message line', async () => {
    server.use(
      http.get('/api/channels', () => HttpResponse.json([channel()])),
      http.get('/api/agents', () => HttpResponse.json([])),
    )
    renderWithProviders(<ChannelsPage />)
    expect(await screen.findByText(/Mike Ciechan/)).toBeInTheDocument()
    expect(screen.getByText(/↩/)).toBeInTheDocument()
  })

  it('omits the reply stamp when the agent has never replied', async () => {
    server.use(
      http.get('/api/channels', () =>
        HttpResponse.json([channel({ lastReplyAt: null, lastReplyPreview: null })]),
      ),
      http.get('/api/agents', () => HttpResponse.json([])),
    )
    renderWithProviders(<ChannelsPage />)
    await screen.findByText(/Mike Ciechan/)
    expect(screen.queryByText(/↩/)).not.toBeInTheDocument()
  })

  it('saves and clears an explicit outbound profile with its metered preview', async () => {
    const profile = {
      name: 'pdf-project', projectId: 'project-1', agentId: 'converter-1', agentName: 'PDF converter',
      promptRevision: 'abcdef', trigger: 'MarkdownSources' as const, timeoutSeconds: 120,
      maxPending: 8, authorization: 'One metered worker invocation per matching agent reply',
    }
    let selected: string | null = null
    const requests: unknown[] = []
    server.use(
      http.get('/api/channels', () => HttpResponse.json([channel({
        agentId: 'agent-1', outboundAgentProfile: selected, outboundProfile: selected ? profile : null,
      })])),
      http.get('/api/channels/outbound-profiles', () => HttpResponse.json([profile])),
      http.get('/api/agents', () => HttpResponse.json([{ id: 'agent-1', name: 'Inbound agent' }])),
      http.patch('/api/channels/ch-1', async ({ request }) => {
        const body = await request.json() as { outboundAgentProfile?: string; clearOutboundAgentProfile?: boolean }
        requests.push(body)
        selected = body.clearOutboundAgentProfile ? null : body.outboundAgentProfile ?? selected
        return HttpResponse.json(channel({ agentId: 'agent-1', outboundAgentProfile: selected,
          outboundProfile: selected ? profile : null }))
      }),
    )
    renderWithProviders(<ChannelsPage />)
    const selector = await screen.findByRole('textbox', { name: 'Outbound profile for Family' })
    await userEvent.click(selector)
    await userEvent.click(await screen.findByText('pdf-project'))
    await waitFor(() => expect(requests).toEqual([{ outboundAgentProfile: 'pdf-project' }]))
    expect(await screen.findByText(/PDF converter · MarkdownSources/)).toBeInTheDocument()
    await waitFor(() => expect(selector).not.toBeDisabled())
    await userEvent.click(screen.getByRole('button', { name: 'Clear outbound profile for Family' }))
    await waitFor(() => expect(requests).toEqual([
      { outboundAgentProfile: 'pdf-project' }, { clearOutboundAgentProfile: true },
    ]))
  })

  it('shows an API validation refusal without displaying an unsaved profile', async () => {
    const profile = {
      name: 'pdf-project', projectId: 'project-1', agentId: 'converter-1', agentName: 'PDF converter',
      promptRevision: 'abcdef', trigger: 'MarkdownSources' as const, timeoutSeconds: 120,
      maxPending: 8, authorization: 'One metered worker invocation per matching agent reply',
    }
    let patchCount = 0
    server.use(
      http.get('/api/channels', () => HttpResponse.json([channel({ agentId: 'agent-1' })])),
      http.get('/api/channels/outbound-profiles', () => HttpResponse.json([profile])),
      http.get('/api/agents', () => HttpResponse.json([{ id: 'agent-1', name: 'Inbound agent' }])),
      http.patch('/api/channels/ch-1', () => {
        patchCount++
        return HttpResponse.json({ status: 400, title: 'Validation failed',
          detail: 'Converter is bound to an inbound channel.' }, { status: 400 })
      }),
    )
    renderWithProviders(<ChannelsPage />)
    const selector = await screen.findByRole('textbox', { name: 'Outbound profile for Family' })
    await userEvent.click(selector)
    await userEvent.click(await screen.findByText('pdf-project'))
    await waitFor(() => expect(patchCount).toBe(1))
    await waitFor(() => expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ color: 'red', message: 'Converter is bound to an inbound channel.' }),
    ))
    expect(selector).toHaveValue('')
    expect(screen.queryByText(/PDF converter · MarkdownSources/)).not.toBeInTheDocument()
  })
})
