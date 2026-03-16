import '@mantine/core/styles.css'
import { MantineProvider, createTheme } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Routes, Route } from 'react-router'
import { useSignalR } from './hooks/useSignalR'
import { useSignalRInvalidation } from './hooks/useSignalRInvalidation'

const theme = createTheme({
  // Dark theme configured in Story 1.7
})

const queryClient = new QueryClient()

function SignalRProvider({ children }: { children: React.ReactNode }) {
  const connectionRef = useSignalR()
  useSignalRInvalidation(connectionRef)
  return <>{children}</>
}

export default function App() {
  return (
    <MantineProvider theme={theme} defaultColorScheme="dark">
      <QueryClientProvider client={queryClient}>
        <SignalRProvider>
          <BrowserRouter>
            <Routes>
              <Route path="/" element={<div>Antiphon — Dashboard placeholder</div>} />
            </Routes>
          </BrowserRouter>
        </SignalRProvider>
      </QueryClientProvider>
    </MantineProvider>
  )
}
