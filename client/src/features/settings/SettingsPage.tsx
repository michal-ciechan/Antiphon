import { Container, Title, Paper, Tabs } from '@mantine/core'
import { useSearchParams } from 'react-router'
import { TemplateManager } from './TemplateManager'
import { ProviderConfig } from './ProviderConfig'
import { ProjectConfig } from './ProjectConfig'
import { StatusTab } from './StatusTab'
import { AgentTuiConfig } from './AgentTuiConfig'
import { ApiKeysSection } from './ApiKeysSection'
import { RoutingSettingsTab } from './RoutingSettingsTab'

const SETTINGS_TABS = [
  'templates',
  'llm-providers',
  'projects',
  'api-keys',
  'agent-tui',
  'routing',
  'status',
] as const

export function SettingsPage() {
  const [searchParams] = useSearchParams()
  const requested = searchParams.get('tab') ?? 'templates'
  const tab = (SETTINGS_TABS as readonly string[]).includes(requested) ? requested : 'templates'
  return (
    <Container size="lg" py="xl">
      <Title order={2} mb="lg">
        Settings
      </Title>
      <Paper p="md" radius="md" withBorder>
        <Tabs defaultValue={tab} keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="templates">Templates</Tabs.Tab>
            <Tabs.Tab value="llm-providers">LLM Providers</Tabs.Tab>
            <Tabs.Tab value="projects">Projects</Tabs.Tab>
            <Tabs.Tab value="api-keys">API Keys</Tabs.Tab>
            <Tabs.Tab value="agent-tui">AI Agent TUI</Tabs.Tab>
            <Tabs.Tab value="routing">Routing</Tabs.Tab>
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

          <Tabs.Panel value="api-keys" pt="md">
            <ApiKeysSection />
          </Tabs.Panel>

          <Tabs.Panel value="agent-tui" pt="md">
            <AgentTuiConfig />
          </Tabs.Panel>

          <Tabs.Panel value="routing" pt="md">
            <RoutingSettingsTab />
          </Tabs.Panel>

          <Tabs.Panel value="status" pt="md">
            <StatusTab />
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Container>
  )
}
