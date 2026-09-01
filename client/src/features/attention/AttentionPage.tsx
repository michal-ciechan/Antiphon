import { Stack, Title } from '@mantine/core'
import { AttentionPanel } from './AttentionPanel'

/**
 * Dedicated attention surface (CARD-0300). Home is a glance; this page is the list.
 * The panel itself is unchanged — Orchestrator `?tab=attention` still embeds the same component.
 */
export function AttentionPage() {
  return (
    <Stack gap="md">
      <Title order={2}>Needs attention</Title>
      <AttentionPanel />
    </Stack>
  )
}
