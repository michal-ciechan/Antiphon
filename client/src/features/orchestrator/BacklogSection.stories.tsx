import { Box } from '@mantine/core'
import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { boardKeys, type BoardSummaryDto, type CardListDto } from '../../api/boards'
import { BacklogSection } from './BacklogSection'
// CONTRACT fixtures — captured (and drift-guarded) by tests/Antiphon.E2E/ContractSnapshotTests
// against the REAL backend. Stories must seed from these files ONLY: hand-written mock shapes can
// silently diverge from what the server actually returns; these cannot.
import cardsBacklogFixture from '../../test/fixtures/contract/cards-backlog.json'
import boardsBacklogFixture from '../../test/fixtures/contract/boards-backlog.json'

const cardsBacklog = cardsBacklogFixture as CardListDto
const boards = boardsBacklogFixture as BoardSummaryDto[]

/**
 * The section mounts with a pre-seeded QueryClient (the repo's no-MSW Storybook convention): every
 * query resolves from cache, so rendering is deterministic and network-free — which is what the
 * Playwright screenshot suite needs.
 *
 * Four cells from the S3 fixture: Do first (High/Now), Schedule (High/Normal), Clear (Low with a
 * due date), Someday (the rest), on two boards so the chip shows. Columns are seeded empty so the
 * MoveMenu kebab stays off — that verb is S2's tests, not this screenshot.
 */
// The fixture's instants are fixed (they have to be — a snapshot cannot move), and age is measured
// against the clock, so without pinning "now" every screenshot would show months of drift. Story-
// local and never restored: the screenshot suite loads one story per page.
const NOW = Date.parse('2026-02-03T09:14:00Z')
Date.now = () => NOW

function seedClient(): QueryClient {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
    },
  })
  client.setQueryData(boardKeys.cardsFor({ status: 'Backlog' }), cardsBacklog)
  client.setQueryData(boardKeys.all, boards)
  for (const board of boards) {
    client.setQueryData(boardKeys.columns(board.id), [])
  }
  return client
}

function withContractData(Story: () => React.ReactElement) {
  return (
    <QueryClientProvider client={seedClient()}>
      <MemoryRouter>
        <Story />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const meta: Meta<typeof BacklogSection> = {
  title: 'Orchestrator/Backlog',
  component: BacklogSection,
  parameters: { layout: 'padded' },
  decorators: [withContractData],
}
export default meta

type Story = StoryObj<typeof BacklogSection>

/** Four boxes side by side on a desktop. */
export const Desktop: Story = {}

/** The same four boxes stacked on a phone. */
export const Mobile: Story = {
  globals: { viewport: { value: 'mobile1' } },
  render: () => (
    <Box maw={390} mx="auto">
      <BacklogSection />
    </Box>
  ),
}
