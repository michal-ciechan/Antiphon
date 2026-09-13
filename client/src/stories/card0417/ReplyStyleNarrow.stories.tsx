// CARD-0417 V-7 — narrow-width label-fit fixture for the six-option reply-style picker.
//
// JSDOM cannot answer "do all six labels still fit at 360/390 CSS px?" — it has no layout. These
// stories put the three real surfaces that host `ReplyStyleControl` (agent create, agent edit,
// project setup) into Storybook's isolated preview iframe with `fetch` stubbed, so a real browser
// lays them out with no server, no database and nothing saved. `client/scripts/v7-reply-style-widths.mjs`
// drives them at both widths and measures each label's overflow.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import type { Meta, StoryObj } from '@storybook/react'
import type { AgentSummaryDto } from '../../api/agents'
import type { ProjectSetupCatalogDto } from '../../api/projectSetup'
import { AgentCreateModal } from '../../features/agents/AgentCreateModal'
import { AgentSettingsModal } from '../../features/agents/AgentSettingsModal'
import { ProjectSetupModal } from '../../features/settings/ProjectSetupModal'

const directory = 'C:\\src\\starter'

const agent: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  workingDirectory: 'C:\\src\\app',
  details: 'UI work',
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

const catalog: ProjectSetupCatalogDto = {
  modelLevels: [],
  replyStyles: [],
  bundles: [{ key: 'delegate-basics', version: '1', stamp: 'stamp', summary: 'Standing rules', chars: 12 }],
  profiles: [],
  presets: [
    {
      key: 'orchestrator',
      label: 'Standing orchestrator',
      description: 'Watches the board',
      alwaysOn: true,
      modelLevel: 'High',
      replyStyle: 'Normal',
      bundleKeys: ['orchestrator'],
      systemPromptTemplate: 'Watch {project} on {board} at {directory}',
      namePattern: '{project} Orchestrator',
      remoteControlEnabled: true,
      defaultWorkflowTemplateId: null,
    },
  ],
  delegation: {
    allowedRoots: ['C:\\src'],
    allowedRootsIsEmpty: false,
    maxConcurrentTasks: 1,
    maxCostUsdPerRoot: 10,
    maxDepth: 2,
    defaultLevel: 'High',
  },
}

/**
 * Every request is answered from this table; anything unmatched resolves to `[]` and is logged so
 * an unstubbed call shows up in the capture script's console output instead of hitting a server.
 * Writes are refused outright — V-7 must not save anything.
 */
const routes: Array<[RegExp, unknown]> = [
  [/\/api\/agents\/bundles$/, []],
  [/\/api\/agents\/definitions$/, []],
  [/\/api\/agents\/agent-1$/, { ...agent, queue: [] }],
  [/\/api\/boards$/, []],
  [/\/api\/agent-tui\/profiles$/, []],
  [/\/api\/agent-tui\/runner-types$/, []],
  [/\/api\/projects\/setup-catalog$/, catalog],
  [/\/api\/projects$/, []],
  [/\/api\/filesystem\/browse/, { normalizedPath: directory, exists: true, isDrivesListing: false, suggestions: [] }],
  [/\/api\/filesystem\/workspaces/, [{ path: directory, isGitRepository: true, repoRoot: directory, branch: 'master', isWorktree: false }]],
]

function installFetchStub() {
  if ('__v7FetchStub' in globalThis) return
  Object.defineProperty(globalThis, '__v7FetchStub', { value: true })
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url
    const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase()
    if (method !== 'GET') {
      console.warn(`[v7] refused ${method} ${url} — the narrow-width fixture never saves`)
      return new Response('{}', { status: 405, headers: { 'content-type': 'application/json' } })
    }
    const match = routes.find(([pattern]) => pattern.test(url))
    if (!match) console.warn(`[v7] unstubbed GET ${url} — answered with []`)
    return new Response(JSON.stringify(match ? match[1] : []), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }
}

installFetchStub()

const meta: Meta = {
  title: 'CARD-0417/Reply style at narrow widths',
  parameters: { layout: 'fullscreen' },
  decorators: [
    (Story) => (
      <MemoryRouter>
        <QueryClientProvider
          client={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } })}
        >
          <Story />
        </QueryClientProvider>
      </MemoryRouter>
    ),
  ],
}

export default meta

export const AgentCreate: StoryObj = {
  render: () => <AgentCreateModal opened onClose={() => undefined} />,
}

export const AgentEdit: StoryObj = {
  render: () => (
    <AgentSettingsModal agent={agent} opened onClose={() => undefined} onDeleted={() => undefined} />
  ),
}

export const ProjectSetup: StoryObj = {
  render: () => <ProjectSetupModal opened onClose={() => undefined} />,
}
