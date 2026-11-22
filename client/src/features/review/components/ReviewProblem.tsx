import { Alert, Button, Group, Stack, Text } from '@mantine/core'
import { TbRefresh } from 'react-icons/tb'
import { reviewProblem, type ReviewProblem } from '../reviewErrors'

/** The detail, the hint, and a dimmed `code · HTTP status` line, for an alert or a notification. */
export function ReviewProblemText({ problem }: { problem: ReviewProblem }) {
  const meta = [problem.code, problem.status ? `HTTP ${problem.status}` : null].filter(Boolean).join(' · ')
  return (
    <Stack gap={2}>
      <Text size="sm">{problem.detail}</Text>
      {problem.hint && <Text size="xs">{problem.hint}</Text>}
      {meta && (
        <Text size="xs" c="dimmed" ff="monospace">
          {meta}
        </Text>
      )}
    </Stack>
  )
}

/** A failed review query: the Problem Details `detail` (an `ApiError` body) and a Retry. */
export function QueryErrorAlert({
  title,
  error,
  onRetry,
  retrying = false,
  fallback = 'The review API did not answer.',
}: {
  title: string
  error: unknown
  onRetry?: () => void
  retrying?: boolean
  fallback?: string
}) {
  return (
    <Alert color="danger" variant="light" title={title}>
      <Group justify="space-between" align="flex-start" wrap="nowrap" gap="sm">
        <ReviewProblemText problem={reviewProblem(error, fallback)} />
        {onRetry && (
          <Button size="compact-xs" variant="light" color="danger" leftSection={<TbRefresh size={12} />} loading={retrying} onClick={onRetry}>
            Retry
          </Button>
        )}
      </Group>
    </Alert>
  )
}
