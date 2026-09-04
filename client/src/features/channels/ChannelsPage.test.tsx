import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ChatChannelDto } from '../../api/channels'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ChannelsPage } from './ChannelsPage'

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
})
