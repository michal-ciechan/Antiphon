import { Anchor, Badge, Button, Group, Stack, Text } from '@mantine/core'
import { Link } from 'react-router'
import {
  TbAlertTriangle,
  TbCircleCheck,
  TbCircleDashed,
  TbCircleX,
} from 'react-icons/tb'
import {
  readinessHeader,
  type ProjectReadinessDto,
  type ReadinessCheckDto,
  type ReadinessStatus,
} from '../../api/projectSetup'

function statusColor(status: ReadinessStatus, requiredMissing: boolean): string {
  if (requiredMissing) return 'red'
  if (status === 'Ok') return 'green'
  if (status === 'Warning') return 'yellow'
  if (status === 'Missing') return 'orange'
  return 'gray'
}

function StatusIcon({ status, requiredMissing }: { status: ReadinessStatus; requiredMissing: boolean }) {
  const color = statusColor(status, requiredMissing)
  if (status === 'Ok') return <TbCircleCheck color="var(--mantine-color-green-6)" size={16} />
  if (requiredMissing || status === 'Missing')
    return <TbCircleX color={`var(--mantine-color-${color}-6)`} size={16} />
  if (status === 'Warning') return <TbAlertTriangle color="var(--mantine-color-yellow-6)" size={16} />
  return <TbCircleDashed color="var(--mantine-color-gray-5)" size={16} />
}

function orderedChecks(checks: ReadinessCheckDto[]): ReadinessCheckDto[] {
  const missingRequired = checks.filter((c) => c.level === 'Required' && c.status === 'Missing')
  const rest = checks.filter((c) => !(c.level === 'Required' && c.status === 'Missing'))
  return [...missingRequired, ...rest]
}

export function ProjectReadinessPanel({
  readiness,
  onAction,
}: {
  readiness: ProjectReadinessDto
  onAction?: (action: string, check: ReadinessCheckDto) => void
}) {
  const rows = orderedChecks(readiness.checks)
  return (
    <Stack gap="sm" data-testid="project-readiness-panel">
      <Text fw={600} data-testid="project-readiness-header">
        {readinessHeader(readiness)}
      </Text>
      <Stack gap="xs">
        {rows.map((check) => {
          const requiredMissing = check.level === 'Required' && check.status === 'Missing'
          return (
            <Stack key={check.key} gap={2} data-testid={`readiness-row-${check.key}`}>
              <Group gap="xs" wrap="nowrap" align="flex-start">
                <StatusIcon status={check.status} requiredMissing={requiredMissing} />
                <Stack gap={0} style={{ flex: 1, minWidth: 0 }}>
                  <Group gap={6} wrap="nowrap">
                    <Text size="sm" c={requiredMissing ? 'red' : undefined}>
                      {check.summary}
                    </Text>
                    <Badge size="xs" variant="light" color={statusColor(check.status, requiredMissing)}>
                      {check.key}
                    </Badge>
                  </Group>
                  {check.status !== 'Ok' && check.detail && (
                    <Text size="xs" c="dimmed">
                      {check.detail}
                    </Text>
                  )}
                  {check.status !== 'Ok' && check.fix && (
                    <Group gap="xs" mt={4}>
                      {check.fix.route && (
                        <Button
                          component={Link}
                          to={check.fix.route}
                          size="compact-xs"
                          variant="light"
                        >
                          {check.fix.label}
                        </Button>
                      )}
                      {check.fix.action && onAction && (
                        <Button
                          size="compact-xs"
                          variant="light"
                          onClick={() => onAction(check.fix!.action!, check)}
                        >
                          {check.fix.label}
                        </Button>
                      )}
                      {!check.fix.route && !check.fix.action && (
                        <Text size="xs" c="dimmed">
                          {check.fix.label}
                        </Text>
                      )}
                    </Group>
                  )}
                </Stack>
              </Group>
            </Stack>
          )
        })}
      </Stack>
      {readiness.canDispatch && (
        <Text size="xs" c="dimmed">
          Cards can move into In Progress. The Delegate-a-task button stays off when{' '}
          <Anchor component="span" inherit>
            delegation-root
          </Anchor>{' '}
          is not Ok.
        </Text>
      )}
    </Stack>
  )
}
