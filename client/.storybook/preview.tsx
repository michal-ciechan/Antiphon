import type { Preview } from '@storybook/react'
import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { INITIAL_VIEWPORTS } from 'storybook/viewport'
import '@mantine/core/styles.css'
import '@mantine/notifications/styles.css'
import { theme } from '../src/theme'

const preview: Preview = {
  parameters: {
    // Device presets in the viewport toolbar (pick one to see its dimensions / test mobile layouts).
    viewport: { options: INITIAL_VIEWPORTS },
  },
  decorators: [
    (Story) => (
      <MantineProvider theme={theme} defaultColorScheme="dark">
        <Notifications position="top-right" />
        <div style={{ padding: '2rem', background: '#141517', minHeight: '100vh' }}>
          <Story />
        </div>
      </MantineProvider>
    ),
  ],
}

export default preview
