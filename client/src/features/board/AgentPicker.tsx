import { Select } from '@mantine/core'
import { useEffect } from 'react'
import { useAgentDefinitions } from '../../api/agents'

interface AgentPickerProps {
  value: string | null
  onChange: (value: string | null) => void
  compact?: boolean
}

export function AgentPicker({ value, onChange, compact = false }: AgentPickerProps) {
  const { data, isLoading } = useAgentDefinitions()
  const options = (data?.definitions ?? []).map((agent) => ({
    value: agent.name,
    label: agent.isDefault ? `${agent.name} (${agent.kind}, default)` : `${agent.name} (${agent.kind})`,
  }))

  useEffect(() => {
    if (!value && data?.defaultDefinition) {
      onChange(data.defaultDefinition)
    }
  }, [data?.defaultDefinition, onChange, value])

  return (
    <Select
      label={compact ? undefined : 'Agent'}
      aria-label={compact ? 'Agent' : undefined}
      data={options}
      value={value}
      onChange={onChange}
      disabled={isLoading || options.length === 0}
      searchable
      allowDeselect={false}
      size={compact ? 'xs' : undefined}
      w={compact ? 240 : undefined}
    />
  )
}
