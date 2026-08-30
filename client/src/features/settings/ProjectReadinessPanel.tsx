import { Anchor, Badge, Button, Group, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { Link } from 'react-router'
import {
  TbAlertTriangle,
  TbCircleCheck,
  TbCircleDashed,
  TbCircleX,
} from 'react-icons/tb'
import { useEnsureAgentWorkingDirectory } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import {
  readinessHeader,
  type ProjectReadinessDto,
  type ReadinessCheckDto,
  type ReadinessFixDto,
  type ReadinessStatus,
} from '../../api/projectSetup'

/** The agent-directory fix encodes the target as `/agents?agent={id}`. */
function agentIdFromFixRoute(route: string | null | undefined): string | null {
  if (!route) return null
  try {
    return new URL(route, 'http://antiphon.local').searchParams.get('agent')
  } catch {
    return null
  }
}

function canRunCreateDirectory(fix: ReadinessFixDto): boolean {
  return fix.action === 'create-directory' && !!agentIdFromFixRoute(fix.route)
}

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
  const ensureDirectory = useEnsureAgentWorkingDirectory()
  const rows = orderedChecks(readiness.checks)

  const runAction = (action: string, check: ReadinessCheckDto) => {
    if (onAction) {
      onAction(action, check)
      return
    }
    if (action !== 'create-directory') return
    const agentId = agentIdFromFixRoute(check.fix?.route)
    if (!agentId) return
    ensureDirectory.mutate(agentId, {
      onSuccess: () => {
        notifications.show({ color: 'green', message: 'Working directory created.' })
      },
      onError: (error) => {
        notifications.show({
          color: 'red',
          message: getApiErrorMessage(error, 'Could not create the working directory.'),
        })
      },
    })
  }

  const canRunAction = (check: ReadinessCheckDto) => {
    if (!check.fix?.action) return false
    if (onAction) return true
    return canRunCreateDirectory(check.fix)
  }

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
                      {canRunAction(check) ? (
                        <Button
                          size="compact-xs"
                          variant="light"
                          loading={
                            !onAction
                            && check.fix.action === 'create-directory'
                            && ensureDirectory.isPending
                          }
                          onClick={() => runAction(check.fix!.action!, check)}
                        >
                          {check.fix.label}
                        </Button>
                      ) : check.fix.route ? (
                        <Button
                          component={Link}
                          to={check.fix.route}
                          size="compact-xs"
                          variant="light"
                        >
                          {check.fix.label}
                        </Button>
                      ) : (
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
