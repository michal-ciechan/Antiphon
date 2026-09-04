import { Box } from '@mantine/core'
import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { agentTaskKeys, type AgentTaskPipelineDto } from '../../api/agentTasks'
import { PipelineStagesPanel } from './PipelineStagesPanel'
import pipelineFixture from '../../test/fixtures/contract/pipeline.json'

const pipeline = pipelineFixture as AgentTaskPipelineDto

function calmPipeline(dto: AgentTaskPipelineDto): AgentTaskPipelineDto {
  return {
    ...dto,
    inFlightAgainstCap: 0,
    stages: dto.stages.map((stage) => ({
      ...stage,
      inFlightCount: 0,
      atOrAboveRecommendation: false,
      inFlight: [],
      queued: [],
      blocked: [],
      ready: [],
    })),
  }
}

// The fixture's instants are fixed, and elapsed / ago are measured against the clock, so without
// pinning "now" every screenshot would show months of drift. Story-local and never restored: the
// screenshot suite loads one story per page.
const NOW = Date.parse('2026-02-03T09:14:00Z')
Date.now = () => NOW

function withPipeline(data: AgentTaskPipelineDto) {
  return function Decorator(Story: () => React.ReactElement) {
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
      },
    })
    client.setQueryData(agentTaskKeys.pipeline(), data)
    return (
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <Story />
        </MemoryRouter>
      </QueryClientProvider>
    )
  }
}

const meta: Meta<typeof PipelineStagesPanel> = {
  title: 'Orchestrator/Pipeline stages',
  component: PipelineStagesPanel,
  parameters: { layout: 'padded' },
}
export default meta

type Story = StoryObj<typeof PipelineStagesPanel>

/** Docs in flight, Docs queued behind the lease, Deploy blocked, CARD-0031 ready. */
export const Live: Story = {
  decorators: [withPipeline(pipeline)],
  render: () => (
    <Box maw={390} mx="auto">
      <PipelineStagesPanel />
    </Box>
  ),
  globals: { viewport: { value: 'iphone12' } },
}

/** The same contract DTO with every collection emptied — calm is a designed state. */
export const Calm: Story = {
  decorators: [withPipeline(calmPipeline(pipeline))],
  render: () => (
    <Box maw={390} mx="auto">
      <PipelineStagesPanel />
    </Box>
  ),
  globals: { viewport: { value: 'iphone12' } },
}
