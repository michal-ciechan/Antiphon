import { Stack, Text, Title } from '@mantine/core'
import { useComplexityChains } from '../../api/complexityChains'
import { useModelAvailability } from '../../api/modelAvailability'
import { useRoutingPins } from '../../api/routingPins'
import { useSubscriptionUsage } from '../../api/subscriptionUsage'

/**
 * Settings Routing tab. Hooks run here so SettingsPage `keepMounted={false}` starts
 * routing queries only when this panel is open.
 */
export function RoutingSettingsTab() {
  useModelAvailability()
  useComplexityChains()
  useRoutingPins()
  useSubscriptionUsage()

  return (
    <Stack gap="sm" data-testid="routing-settings-tab">
      <Title order={4}>Routing</Title>
      <Text size="sm" c="dimmed">
        Model availability, subscription usage observations, routing pins, and the role ×
        complexity matrix.
      </Text>
    </Stack>
  )
}
