import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { render, screen } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { server } from './test/mocks/server'

vi.mock('./hooks/useSignalR', () => ({
  useSignalR: () => ({ current: null }),
}))
vi.mock('./hooks/useSignalRInvalidation', () => ({
  useSignalRInvalidation: () => undefined,
}))
vi.mock('./hooks/useStreamingEvents', () => ({
  useStreamingEvents: () => undefined,
}))
vi.mock('./hooks/useSessionFinishedToasts', () => ({
  useSessionFinishedToasts: () => undefined,
}))
vi.mock('./hooks/useAlertToasts', () => ({
  useAlertToasts: () => undefined,
}))

vi.mock('./features/home/HomePage', () => ({
  HomePage: () => <div>Home page ready</div>,
}))
vi.mock('./features/dashboard/DashboardPage', () => ({
  DashboardPage: () => <div>Workflows page ready</div>,
}))
vi.mock('./features/workflow/WorkflowDetailPage', () => ({
  WorkflowDetailPage: () => <div>Workflow detail ready</div>,
}))
vi.mock('./features/board/BoardPage', () => ({
  BoardPage: () => <div>Boards page ready</div>,
}))
vi.mock('./features/agents/AgentsPage', () => ({
  AgentsPage: () => <div>Agents page ready</div>,
}))
vi.mock('./features/agents/AgentFilesPage', () => ({
  AgentFilesPage: () => <div>Agent files ready</div>,
}))
vi.mock('./features/channels/ChannelsPage', () => ({
  ChannelsPage: () => <div>Channels page ready</div>,
}))
vi.mock('./features/plans/PlanReaderPage', () => ({
  PlanReaderPage: () => <div>Plans page ready</div>,
}))
vi.mock('./features/thread/CardThreadPage', () => ({
  CardThreadPage: () => <div>Thread page ready</div>,
}))
vi.mock('./features/orchestrator/OrchestratorPage', () => ({
  OrchestratorPage: () => <div>Orchestrator page ready</div>,
}))
vi.mock('./features/settings/SettingsPage', () => ({
  SettingsPage: () => <div>Settings page ready</div>,
}))

const ROUTES: Array<{ path: string; content: string }> = [
  { path: '/', content: 'Home page ready' },
  { path: '/workflows', content: 'Workflows page ready' },
  { path: '/workflow/wf-1', content: 'Workflow detail ready' },
  { path: '/boards', content: 'Boards page ready' },
  { path: '/boards/board-1', content: 'Boards page ready' },
  { path: '/agents', content: 'Agents page ready' },
  { path: '/agents/agent-1/files', content: 'Agent files ready' },
  { path: '/channels', content: 'Channels page ready' },
  { path: '/plans', content: 'Plans page ready' },
  { path: '/thread/card-1', content: 'Thread page ready' },
  { path: '/orchestrator', content: 'Orchestrator page ready' },
  { path: '/settings', content: 'Settings page ready' },
]

function renderAt(path: string) {
  window.history.pushState({}, '', path)
  return render(<App />)
}

describe('App routes', () => {
  beforeEach(() => {
    server.use(
      http.get('/api/attention/summary', () =>
        HttpResponse.json({ open: 0, decisions: 0, generatedAt: '2026-08-28T00:00:00Z' }),
      ),
    )
  })

  it.each(ROUTES)('resolves $path through Suspense ($content)', async ({ path, content }) => {
    renderAt(path)
    expect(await screen.findByText(content)).toBeInTheDocument()
  })
})

describe('entry bundle', () => {
  const htmlPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../dist/index.html')

  it.skipIf(!existsSync(htmlPath))('does not modulepreload SessionTerminal from the built index.html', () => {

    const html = readFileSync(htmlPath, 'utf8')
    const preloads = [...html.matchAll(/rel="modulepreload"[^>]*href="([^"]+)"/gi)].map(
      (match) => match[1],
    )
    const scripts = [...html.matchAll(/<script[^>]*src="([^"]+)"/gi)].map((match) => match[1])
    const entryRefs = [...preloads, ...scripts]

    expect(entryRefs.join('\n')).not.toMatch(/SessionTerminal/i)
    expect(entryRefs.join('\n')).not.toMatch(/xterm/i)

    for (const href of entryRefs) {
      const fileName = href.replace(/^.*\//, '')
      const assetPath = path.resolve(path.dirname(htmlPath), 'assets', fileName)
      if (!existsSync(assetPath) || !fileName.endsWith('.js')) continue
      const source = readFileSync(assetPath, 'utf8')
      expect(source, `${fileName} should not contain the xterm package`).not.toMatch(/@xterm\/xterm/)
    }
  })
})
