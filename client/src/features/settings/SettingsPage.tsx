import { Container, Title, Paper, Tabs, Text } from '@mantine/core'
import { TemplateManager } from './TemplateManager'
import { ProviderConfig } from './ProviderConfig'

export function SettingsPage() {
  return (
    <Container size="lg" py="xl">
      <Title order={2} mb="lg">
        Settings
      </Title>
      <Paper p="md" radius="md" withBorder>
        <Tabs defaultValue="templates">
          <Tabs.List>
            <Tabs.Tab value="templates">Templates</Tabs.Tab>
            <Tabs.Tab value="llm-providers">LLM Providers</Tabs.Tab>
            <Tabs.Tab value="projects">Projects</Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="templates" pt="md">
            <TemplateManager />
          </Tabs.Panel>

          <Tabs.Panel value="llm-providers" pt="md">
            <ProviderConfig />
          </Tabs.Panel>

          <Tabs.Panel value="projects" pt="md">
            <Text c="dimmed">Project configuration coming soon.</Text>
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Container>
  )
}
