import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import type { WorkflowDto } from '../../api/workflows'
import type { ProjectDto } from '../../api/projects'
import { DashboardPage } from './DashboardPage'

const ISO = '2026-06-11T09:00:00Z'

const MOCK_PROJECTS: ProjectDto[] = [
  {
    id: 'p1', name: 'Antiphon', gitRepositoryUrl: '', baseBranch: 'master', constitutionPath: 'AGENTS.md',
    gitHubIntegrationEnabled: false, notificationsEnabled: true, createdAt: ISO, updatedAt: ISO,
  },
  {
    id: 'p2', name: 'Horarium', gitRepositoryUrl: '', baseBranch: 'main', constitutionPath: 'README.md',
    gitHubIntegrationEnabled: false, notificationsEnabled: false, createdAt: ISO, updatedAt: ISO,
  },
]

const wf = (over: Partial<WorkflowDto> & Pick<WorkflowDto, 'id' | 'name' | 'status'>): WorkflowDto => ({
  description: '', currentStageName: null, templateId: 't1', templateName: 'One Shot',
  projectId: 'p1', projectName: 'Antiphon', featureName: null, stageCount: 4, completedStageCount: 0,
  availableTransitions: [], createdAt: ISO, updatedAt: ISO, ...over,
})

const MOCK_WORKFLOWS: WorkflowDto[] = [
  wf({ id: 'w1', name: 'Mobile terminal keypad', status: 'Running', currentStageName: 'Implement', completedStageCount: 2, featureName: 'mobile-keypad' }),
  wf({ id: 'w2', name: 'Cardless agent sessions', status: 'GateWaiting', currentStageName: 'Human review', completedStageCount: 3, featureName: 'cardless' }),
  wf({ id: 'w3', name: 'Storybook + Caddy wiring', status: 'Completed', currentStageName: null, completedStageCount: 4, projectId: 'p1', projectName: 'Antiphon' }),
  wf({ id: 'w4', name: 'Gantt sample import', status: 'Failed', currentStageName: 'Plan', completedStageCount: 1, projectId: 'p2', projectName: 'Horarium' }),
]

// Seeded so the page renders its populated grid with no network. retry/staleTime keep the
// closed New-Workflow dialog's background queries from churning.
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } },
})
queryClient.setQueryData(['workflows'], MOCK_WORKFLOWS)
queryClient.setQueryData(['projects'], MOCK_PROJECTS)

const meta: Meta<typeof DashboardPage> = {
  title: 'Pages/Home (Workflows)',
  component: DashboardPage,
  parameters: { layout: 'fullscreen' },
  decorators: [
    (Story) => (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <Story />
        </MemoryRouter>
      </QueryClientProvider>
    ),
  ],
}
export default meta

type Story = StoryObj<typeof DashboardPage>

/** Desktop layout — multi-column workflow card grid. */
export const Desktop: Story = {}

/** Mobile layout (iPhone 12 — 390×844). Cards stack to a single column. */
export const Mobile: Story = {
  globals: { viewport: { value: 'iphone12' } },
}
