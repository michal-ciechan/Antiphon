import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@mantine/core/styles.css'
import './global.css'
import App from './App.tsx'
import { installConsoleRing } from './shared/consoleRing'

installConsoleRing()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
