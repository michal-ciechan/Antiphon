import type { Meta, StoryObj } from '@storybook/react'
import { Button, Stack, Text } from '@mantine/core'

const meta: Meta = {
  title: 'Dashboard/NewWorkflowDialog/SubmitButton',
}
export default meta

type Story = StoryObj

/**
 * BUG: When both disabled and loading are set simultaneously, browsers suppress
 * CSS animations on disabled form elements — the spinner renders but does not spin.
 *
 * This is triggered by: canCreate = !!templateId && !!projectId && !isPending
 * When isPending becomes true, canCreate becomes false → disabled=true + loading=true.
 */
export const Broken: Story = {
  render: () => (
    <Stack gap="md" maw={400}>
      <Text size="sm" c="dimmed">
        Both <code>disabled</code> and <code>loading</code> are set — spinner is static (bug).
      </Text>
      <Button
        disabled={true}
        loading={true}
        fullWidth
      >
        Create
      </Button>
    </Stack>
  ),
}

/**
 * FIX: Only set disabled when the required fields are missing.
 * When loading, the button handles click prevention itself — no need for disabled.
 */
export const Fixed: Story = {
  render: () => (
    <Stack gap="md" maw={400}>
      <Text size="sm" c="dimmed">
        Only <code>loading</code> is set (not disabled) — spinner animates correctly (fixed).
      </Text>
      <Button
        disabled={false}
        loading={true}
        fullWidth
      >
        Create
      </Button>
    </Stack>
  ),
}
