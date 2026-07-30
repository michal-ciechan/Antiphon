import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { SessionTranscriptDto } from '../../api/sessions'
import { SessionTranscriptPanel } from './SessionTranscriptPanel'
// CONTRACT fixture — captured (and drift-guarded) by tests/Antiphon.E2E/ContractSnapshotTests
// against the REAL backend: a two-turn conversation with per-API-call token usage and spaced
// timestamps, so the duration / idle / token metrics render real values.
import transcriptFixture from '../../test/fixtures/contract/session-transcript.json'

const transcript = transcriptFixture as SessionTranscriptDto

/**
 * The panel is seeded through its storybook hook (`initialEntries`) — it talks HTTP + SignalR,
 * not react-query, so cache seeding can't reach it. The QueryClientProvider is still needed for
 * the composer's slash-command hook (disabled until `/` is typed, so it never fetches).
 */
function withQueryClient(Story: () => React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
    },
  })
  return (
    <QueryClientProvider client={client}>
      <Story />
    </QueryClientProvider>
  )
}

const meta: Meta<typeof SessionTranscriptPanel> = {
  title: 'Agents/SessionTranscriptPanel',
  component: SessionTranscriptPanel,
  parameters: { layout: 'padded' },
  decorators: [withQueryClient],
}
export default meta

type Story = StoryObj<typeof SessionTranscriptPanel>

/**
 * Two finished turns with per-turn metrics: wall-clock duration, the idle gap before the second
 * prompt, API-call counts, and input/output/cache token totals (plus session totals in the header).
 */
export const Conversation: Story = {
  args: {
    sessionId: transcript.sessionId,
    initialEntries: transcript.entries,
  },
}

/** The same conversation with the message composer at the bottom (the full-screen files view dock). */
export const WithComposer: Story = {
  args: {
    sessionId: transcript.sessionId,
    initialEntries: transcript.entries,
    withComposer: true,
  },
}
