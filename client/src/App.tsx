import '@mantine/core/styles.css'
import '@mantine/notifications/styles.css'
import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Routes, Route } from 'react-router'
import { useSignalR } from './hooks/useSignalR'
import { useSignalRInvalidation } from './hooks/useSignalRInvalidation'
import { useStreamingEvents } from './hooks/useStreamingEvents'
import { theme } from './theme'
import { Layout } from './shared/Layout'
import { ErrorBoundary } from './shared/ErrorBoundary'
import { SuspenseBoundary } from './shared/SuspenseBoundary'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { WorkflowDetailPage } from './features/workflow/WorkflowDetailPage'
import { SettingsPage } from './features/settings/SettingsPage'

const queryClient = new QueryClient()

function SignalRProvider({ children }: { children: React.ReactNode }) {
  const connectionRef = useSignalR()
  useSignalRInvalidation(connectionRef)
  useStreamingEvents(connectionRef)
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
              <Route element={<Layout />}>
                <Route
                  index
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
