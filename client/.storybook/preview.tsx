import type { Preview } from '@storybook/react'
import { MantineProvider } from '@mantine/core'
import '@mantine/core/styles.css'
import { theme } from '../src/theme'

const preview: Preview = {
  decorators: [
    (Story) => (
      <MantineProvider theme={theme} defaultColorScheme="dark">
        <div style={{ padding: '2rem', background: '#141517', minHeight: '100vh' }}>
          <Story />
        </div>
      </MantineProvider>
    ),
  ],
}

export default preview
