import { Stack, Text, Button, ThemeIcon } from '@mantine/core'
import { VscAdd, VscSearchStop } from 'react-icons/vsc'

interface EmptyStateProps {
  variant: 'no-workflows' | 'no-results'
  onCreateWorkflow?: () => void
  onClearFilters?: () => void
}

/**
 * Empty state for the dashboard with two variants:
 * - no-workflows: First-time or empty state with CTA to create first workflow
 * - no-results: Filter returned no matches with CTA to clear filters
 */
export function EmptyState({ variant, onCreateWorkflow, onClearFilters }: EmptyStateProps) {
  if (variant === 'no-workflows') {
    return (
      <Stack align="center" gap="md" py={60}>
        <ThemeIcon size={64} radius="xl" variant="light" color="blue">
          <VscAdd size={32} />
        </ThemeIcon>
        <Text size="lg" fw={500}>
          No workflows yet
        </Text>
        <Text size="sm" c="dimmed" ta="center" maw={360}>
          Create your first workflow to get started with AI-assisted development.
        </Text>
        {onCreateWorkflow && (
          <Button onClick={onCreateWorkflow} size="md" mt="sm">
            Create your first workflow
          </Button>
        )}
      </Stack>
    )
  }

  return (
    <Stack align="center" gap="md" py={60}>
      <ThemeIcon size={64} radius="xl" variant="light" color="gray">
        <VscSearchStop size={32} />
      </ThemeIcon>
      <Text size="lg" fw={500}>
        No matching workflows
      </Text>
      <Text size="sm" c="dimmed" ta="center" maw={360}>
        Try adjusting your filters or search terms.
      </Text>
      {onClearFilters && (
        <Button variant="subtle" onClick={onClearFilters} mt="sm">
          Clear filters
        </Button>
      )}
    </Stack>
  )
}
