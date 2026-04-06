import { Container, Title, Paper, Tabs } from '@mantine/core'
import { TemplateManager } from './TemplateManager'
import { ProviderConfig } from './ProviderConfig'
import { ProjectConfig } from './ProjectConfig'
import { StatusTab } from './StatusTab'

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
            <Tabs.Tab value="status">Status</Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="templates" pt="md">
            <TemplateManager />
          </Tabs.Panel>

          <Tabs.Panel value="llm-providers" pt="md">
            <ProviderConfig />
          </Tabs.Panel>

          <Tabs.Panel value="projects" pt="md">
            <ProjectConfig />
          </Tabs.Panel>

          <Tabs.Panel value="status" pt="md">
            <StatusTab />
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Container>
  )
}
