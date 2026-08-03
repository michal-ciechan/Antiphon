import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { AgentFileContentDto, AgentFilesDto, ReviewThreadDto } from '../../api/review'
import { reviewKeys } from '../../api/review'
import { FilesReviewPanel } from './FilesReviewPanel'
// CONTRACT fixtures — captured (and drift-guarded) by tests/Antiphon.E2E/ContractSnapshotTests
// against the REAL backend. Stories must seed from these files ONLY: hand-written mock shapes can
// silently diverge from what the server actually returns; these cannot.
import agentFilesFixture from '../../test/fixtures/contract/agent-files.json'
import contentHeadFixture from '../../test/fixtures/contract/file-content-head.json'
import contentWorkFixture from '../../test/fixtures/contract/file-content-work.json'
import reviewThreadsFixture from '../../test/fixtures/contract/review-threads.json'

const agentFiles = agentFilesFixture as AgentFilesDto
const threads = reviewThreadsFixture as ReviewThreadDto[]
const contentWork = contentWorkFixture as AgentFileContentDto
const contentHead = contentHeadFixture as AgentFileContentDto
const agentId = agentFiles.agentId

/**
 * The stories mount the panel with a pre-seeded QueryClient (the repo's no-MSW Storybook
 * convention): every query the panel makes resolves from cache, so rendering is deterministic and
 * network-free — exactly what the Playwright screenshot suite needs. The panel itself contains no
 * app chrome (navbar, live status badges), so screenshots stay comparable across runs.
 */
function withContractData(threadData: ReviewThreadDto[]) {
  return function Decorator(Story: () => React.ReactElement) {
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
      },
    })
    // 'checkpoint' is the panel's default baseline selection — seed under that key.
    client.setQueryData(reviewKeys.files(agentId, 'checkpoint'), agentFiles)
    client.setQueryData(reviewKeys.commits(agentId), { commits: [] })
    client.setQueryData(reviewKeys.threads(agentId), threadData)
    client.setQueryData(reviewKeys.content(agentId, 'README.md', 'work', 'checkpoint'), contentWork)
    client.setQueryData(reviewKeys.content(agentId, 'README.md', 'head', 'checkpoint'), contentHead)
    client.setQueryData(reviewKeys.content(agentId, 'notes/report.md', 'work', 'checkpoint'), {
      path: 'notes/report.md',
      rev: 'work',
      text: '# Report\n\n- finding one\n- finding two\n',
      truncated: false,
      missing: false,
      isBinary: false,
    } satisfies AgentFileContentDto)
    client.setQueryData(reviewKeys.content(agentId, 'notes/report.md', 'head', 'checkpoint'), {
      path: 'notes/report.md',
      rev: 'head',
      text: null,
      truncated: false,
      missing: true,
      isBinary: false,
    } satisfies AgentFileContentDto)
    return (
      <QueryClientProvider client={client}>
        <Story />
      </QueryClientProvider>
    )
  }
}

const meta: Meta<typeof FilesReviewPanel> = {
  title: 'Agents/FilesReviewPanel',
  component: FilesReviewPanel,
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj<typeof FilesReviewPanel>

/** The file tree with git statuses, review marks, and an unviewed indicator — nothing selected. */
export const Tree: Story = {
  args: { agentId },
  decorators: [withContractData(threads)],
}

/** Markdown file open in the git diff view (HEAD vs working tree) with its answered thread below. */
export const DiffWithAnsweredThread: Story = {
  args: { agentId, initialSelectedPath: 'README.md' },
  decorators: [withContractData(threads)],
}

/** The same panel with no threads — the empty commenting state. */
export const DiffNoThreads: Story = {
  args: { agentId, initialSelectedPath: 'README.md' },
  decorators: [withContractData([])],
}
