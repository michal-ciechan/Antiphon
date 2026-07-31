import { Select, Stack, Text } from '@mantine/core'
import type { AgentModelFamily } from '../../api/agents'
import { AGENT_MODEL_FAMILY_OPTIONS } from '../../api/agents'

/**
 * The model-family picker used by the agent create + settings modals. Each option shows its
 * capability/cost blurb; the selected agent launches with the family ALIAS (`--model opus`),
 * so it always gets the family's current model — never a pinned version.
 */
export function ModelFamilySelect({
  value,
  onChange,
}: {
  value: AgentModelFamily
  onChange: (value: AgentModelFamily) => void
}) {
  const selected = AGENT_MODEL_FAMILY_OPTIONS.find((o) => o.value === value)
  return (
    <Select
      label="Model"
      description={selected?.description ?? 'Model family for the agent’s sessions.'}
      data={AGENT_MODEL_FAMILY_OPTIONS.map(({ value: v, label }) => ({ value: v, label }))}
      value={value}
      onChange={(v) => onChange((v as AgentModelFamily | null) ?? 'Opus')}
      allowDeselect={false}
      renderOption={({ option }) => {
        const full = AGENT_MODEL_FAMILY_OPTIONS.find((o) => o.value === option.value)
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
