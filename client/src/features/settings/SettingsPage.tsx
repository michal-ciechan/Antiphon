import { Container, Title, Paper, Tabs } from '@mantine/core'
import { useSearchParams } from 'react-router'
import { TemplateManager } from './TemplateManager'
import { ProviderConfig } from './ProviderConfig'
import { ProjectConfig } from './ProjectConfig'
import { StatusTab } from './StatusTab'
import { AgentTuiConfig } from './AgentTuiConfig'
import { ApiKeysSection } from './ApiKeysSection'
import { RoutingSettingsTab } from './RoutingSettingsTab'
import { ConcurrencySettingsTab } from './ConcurrencySettingsTab'

const SETTINGS_TABS = [
  'templates',
  'llm-providers',
  'projects',
  'api-keys',
  'agent-tui',
  'routing',
  'concurrency',
  'status',
] as const

export function SettingsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const requested = searchParams.get('tab') ?? 'templates'
  const tab = (SETTINGS_TABS as readonly string[]).includes(requested) ? requested : 'templates'
  return (
    <Container size="lg" py="xl">
      <Title order={2} mb="lg">
        Settings
      </Title>
      <Paper p="md" radius="md" withBorder>
        {/* Controlled by `?tab=` so later navigation switches the panel (`C506 V16 controlled-tab`); a click
            sets only `tab` (`C506 S3F1 active-tab-click`, `away-and-back`, `unknown-tab`). */}
        <Tabs
          value={tab}
          onChange={(value) => {
            if (value === null) return
            setSearchParams((previous) => {
              const next = new URLSearchParams(previous)
              next.set('tab', value)
              return next
            })
          }}
          keepMounted={false}
        >
          <Tabs.List>
            <Tabs.Tab value="templates">Templates</Tabs.Tab>
            <Tabs.Tab value="llm-providers">LLM Providers</Tabs.Tab>
            <Tabs.Tab value="projects">Projects</Tabs.Tab>
            <Tabs.Tab value="api-keys">API Keys</Tabs.Tab>
            <Tabs.Tab value="agent-tui">AI Agent TUI</Tabs.Tab>
            <Tabs.Tab value="routing">Routing</Tabs.Tab>
            <Tabs.Tab value="concurrency">Concurrency</Tabs.Tab>
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

          <Tabs.Panel value="concurrency" pt="md">
            <ConcurrencySettingsTab />
          </Tabs.Panel>

          <Tabs.Panel value="status" pt="md">
            <StatusTab />
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Container>
  )
}
