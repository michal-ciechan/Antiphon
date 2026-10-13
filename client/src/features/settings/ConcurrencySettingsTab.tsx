import { Select, Stack, Text, Title } from '@mantine/core'
import { useSearchParams } from 'react-router'
import { useProjects } from '../../api/projects'
import { DispatchConcurrencyEditor } from './DispatchConcurrencyEditor'

const GLOBAL = 'global'
const PROJECT_PREFIX = 'project:'

/**
 * Settings Concurrency tab (CARD-0506 D-1/D-2): a scope selector over the editor. The scope lives
 * only in the URL (`?project=<id>`); an id with no project row is shown as itself, not replaced by
 * the global scope (`C506 V15 missing-project`).
 */
export function ConcurrencySettingsTab() {
  const [searchParams, setSearchParams] = useSearchParams()
  const projectId = searchParams.get('project') || null
  const projects = useProjects()

  const options = [
    { value: GLOBAL, label: 'Global defaults' },
    ...(projects.data ?? []).map((project) => ({ value: PROJECT_PREFIX + project.id, label: project.name })),
  ]
  const selected = projectId === null ? GLOBAL : PROJECT_PREFIX + projectId
  if (!options.some((option) => option.value === selected)) {
    options.push({ value: selected, label: projects.data ? `Unknown project (${projectId})` : String(projectId) })
  }

  const choose = (value: string | null) => {
    if (value === null) return
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous)
      if (value.startsWith(PROJECT_PREFIX)) next.set('project', value.slice(PROJECT_PREFIX.length))
      else next.delete('project')
      return next
    })
  }

  return (
    <Stack gap="md" data-testid="concurrency-settings-tab">
      <div>
        <Title order={4}>Concurrency</Title>
        <Text size="sm" c="dimmed">
          Per-role parallel and queued task limits, globally or overridden for one project.
        </Text>
      </div>
      <Select
        label="Scope"
        data={options}
        value={selected}
        onChange={choose}
        allowDeselect={false}
        searchable
        w={{ base: '100%', sm: 360 }}
        data-testid="concurrency-scope"
      />
      <DispatchConcurrencyEditor projectId={projectId} />
    </Stack>
  )
}
