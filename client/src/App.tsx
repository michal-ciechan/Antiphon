import '@mantine/core/styles.css'
import '@mantine/notifications/styles.css'
import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { lazy } from 'react'
import { BrowserRouter, Routes, Route } from 'react-router'
import { useSignalR } from './hooks/useSignalR'
import { useSignalRInvalidation } from './hooks/useSignalRInvalidation'
import { useStreamingEvents } from './hooks/useStreamingEvents'
import { useSessionFinishedToasts } from './hooks/useSessionFinishedToasts'
import { useAlertToasts } from './hooks/useAlertToasts'
import { theme } from './theme'
import { Layout } from './shared/Layout'
import { ErrorBoundary } from './shared/ErrorBoundary'
import { SuspenseBoundary } from './shared/SuspenseBoundary'

const HomePage = lazy(() =>
  import('./features/home/HomePage').then((m) => ({ default: m.HomePage })),
)
const DashboardPage = lazy(() =>
  import('./features/dashboard/DashboardPage').then((m) => ({ default: m.DashboardPage })),
)
const WorkflowDetailPage = lazy(() =>
  import('./features/workflow/WorkflowDetailPage').then((m) => ({ default: m.WorkflowDetailPage })),
)
const SettingsPage = lazy(() =>
  import('./features/settings/SettingsPage').then((m) => ({ default: m.SettingsPage })),
)
const BoardPage = lazy(() =>
  import('./features/board/BoardPage').then((m) => ({ default: m.BoardPage })),
)
const OrchestratorPage = lazy(() =>
  import('./features/orchestrator/OrchestratorPage').then((m) => ({ default: m.OrchestratorPage })),
)
const AttentionPage = lazy(() =>
  import('./features/attention/AttentionPage').then((m) => ({ default: m.AttentionPage })),
)
const AgentsPage = lazy(() =>
  import('./features/agents/AgentsPage').then((m) => ({ default: m.AgentsPage })),
)
const AgentFilesPage = lazy(() =>
  import('./features/agents/AgentFilesPage').then((m) => ({ default: m.AgentFilesPage })),
)
const ChannelsPage = lazy(() =>
  import('./features/channels/ChannelsPage').then((m) => ({ default: m.ChannelsPage })),
)
const PlanReaderPage = lazy(() =>
  import('./features/plans/PlanReaderPage').then((m) => ({ default: m.PlanReaderPage })),
)
const CardThreadPage = lazy(() =>
  import('./features/thread/CardThreadPage').then((m) => ({ default: m.CardThreadPage })),
)

const queryClient = new QueryClient()

function SignalRProvider({ children }: { children: React.ReactNode }) {
  const connectionRef = useSignalR()
  useSignalRInvalidation(connectionRef)
  useStreamingEvents(connectionRef)
  useSessionFinishedToasts(connectionRef)
  useAlertToasts(connectionRef)
  return <>{children}</>
}

export default function App() {
  return (
    <MantineProvider theme={theme} defaultColorScheme="dark">
      <Notifications position="top-right" limit={3} />
      <QueryClientProvider client={queryClient}>
        <SignalRProvider>
          <BrowserRouter>
            <Routes>
              {/* Full-screen (no app chrome): the agent files review tab. */}
              <Route
                path="agents/:id/files"
                element={
                  <ErrorBoundary fallbackTitle="Files error">
                    <SuspenseBoundary variant="page">
                      <AgentFilesPage />
                    </SuspenseBoundary>
                  </ErrorBoundary>
                }
              />
              <Route element={<Layout />}>
                <Route
                  index
                  element={
                    <ErrorBoundary fallbackTitle="Home error">
                      <SuspenseBoundary variant="page">
                        <HomePage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="workflows"
                  element={
                    <ErrorBoundary fallbackTitle="Dashboard error">
                      <SuspenseBoundary variant="page">
                        <DashboardPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="workflow/:id"
                  element={
                    <ErrorBoundary fallbackTitle="Workflow error">
                      <SuspenseBoundary variant="page">
                        <WorkflowDetailPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="boards"
                  element={
                    <ErrorBoundary fallbackTitle="Board error">
                      <SuspenseBoundary variant="page">
                        <BoardPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="boards/:id"
                  element={
                    <ErrorBoundary fallbackTitle="Board error">
                      <SuspenseBoundary variant="page">
                        <BoardPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="agents"
                  element={
                    <ErrorBoundary fallbackTitle="Agents error">
                      <SuspenseBoundary variant="page">
                        <AgentsPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="channels"
                  element={
                    <ErrorBoundary fallbackTitle="Channels error">
                      <SuspenseBoundary variant="page">
                        <ChannelsPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="plans"
                  element={
                    <ErrorBoundary fallbackTitle="Plans error">
                      <SuspenseBoundary variant="page">
                        <PlanReaderPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="thread/:cardId"
                  element={
                    <ErrorBoundary fallbackTitle="Thread error">
                      <SuspenseBoundary variant="page">
                        <CardThreadPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="orchestrator"
                  element={
                    <ErrorBoundary fallbackTitle="Orchestrator error">
                      <SuspenseBoundary variant="page">
                        <OrchestratorPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="attention"
                  element={
                    <ErrorBoundary fallbackTitle="Attention error">
                      <SuspenseBoundary variant="page">
                        <AttentionPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
                <Route
                  path="settings"
                  element={
                    <ErrorBoundary fallbackTitle="Settings error">
                      <SuspenseBoundary variant="page">
                        <SettingsPage />
                      </SuspenseBoundary>
                    </ErrorBoundary>
                  }
                />
              </Route>
            </Routes>
          </BrowserRouter>
        </SignalRProvider>
      </QueryClientProvider>
    </MantineProvider>
  )
}
