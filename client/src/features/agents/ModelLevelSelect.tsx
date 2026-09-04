import { Select, Stack, Text } from '@mantine/core'
import type { ReactNode } from 'react'
import type { AgentModelLevel } from '../../api/agents'
import { AGENT_MODEL_LEVEL_OPTIONS } from '../../api/agents'

/**
 * The generic model-level picker used by the agent create + settings modals. Levels map to each
 * provider's ladder at launch (Claude today: Frontier→Fable, High→Opus, Medium→Sonnet,
 * Low→Haiku), always via the family alias — never a pinned model version. Each option shows its
 * capability/cost blurb plus the current Claude mapping.
 */
export function ModelLevelSelect({
  value,
  onChange,
  label = 'Model level',
  withDescription = true,
  size,
  w,
  error,
  disabled,
  'aria-label': ariaLabel,
}: {
  value: AgentModelLevel
  onChange: (value: AgentModelLevel) => void
  label?: string
  withDescription?: boolean
  size?: 'xs' | 'sm' | 'md'
  w?: number | string
  error?: ReactNode
  disabled?: boolean
  'aria-label'?: string
}) {
  const selected = AGENT_MODEL_LEVEL_OPTIONS.find((o) => o.value === value)
  return (
    <Select
      label={label || undefined}
      aria-label={ariaLabel}
      description={
        withDescription
          ? (selected?.description ?? 'Capability level for the agent’s sessions.')
          : undefined
      }
      data={AGENT_MODEL_LEVEL_OPTIONS.map(({ value: v, label: optionLabel }) => ({
        value: v,
        label: optionLabel,
      }))}
      value={value}
      onChange={(v) => onChange((v as AgentModelLevel | null) ?? 'High')}
      allowDeselect={false}
      size={size}
      w={w}
      error={error}
      disabled={disabled}
      renderOption={({ option }) => {
        const full = AGENT_MODEL_LEVEL_OPTIONS.find((o) => o.value === option.value)
        return (
          <Stack gap={0}>
            <Text size="sm" fw={600}>
              {option.label}
            </Text>
            {full && (
              <Text size="xs" c="dimmed">
                {full.description}
              </Text>
            )}
          </Stack>
        )
      }}
    />
  )
}
