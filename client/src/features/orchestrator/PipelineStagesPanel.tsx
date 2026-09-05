import { Box, Divider, Group, Paper, Stack, Text, UnstyledButton } from '@mantine/core'
import { useInterval } from '@mantine/hooks'
import { Fragment, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { usePipeline } from '../../api/agentTasks'
import { InlineSkeleton } from '../../shared/SkeletonLayouts'
import { TaskDrawer } from '../delegations/TaskDrawer'
import {
  STAGE_LABEL,
  fleetStrip,
  idleLine,
  isPipelineEmpty,
  stageCountLine,
  stagePinLabel,
  stageRows,
  visibleStages,
  type PipelineRowView,
} from './pipelineStageModel'

/**
 * Fleet-wide stage glance: one compact line per card, grouped by role. The same component on
 * desktop and phone — a single column, 560 px max, centred. Tap a task row for the drawer; a
 * ready row on any stage goes to the plan reader when it has a deliverable path.
 */
export function PipelineStagesPanel() {
  const pipeline = usePipeline()
  const [now, setNow] = useState(() => Date.now())
  useInterval(() => setNow(Date.now()), 60_000, { autoInvoke: true })
  const [params, setParams] = useSearchParams()
  const taskId = params.get('task')

  const setTask = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set('task', id)
    else next.delete('task')
    setParams(next, { replace: true })
  }

  if (pipeline.isPending) {
    return (
      <Box maw={560} mx="auto" style={{ overflowX: 'hidden' }}>
        <Text size="xs" fw={700} tt="uppercase" mb={4} style={{ letterSpacing: 1 }}>
          Pipeline
        </Text>
        <Stack gap="xs">
          <InlineSkeleton />
          <InlineSkeleton />
          <InlineSkeleton />
        </Stack>
      </Box>
    )
  }

  if (pipeline.isError || !pipeline.data) {
    return (
      <Box maw={560} mx="auto" style={{ overflowX: 'hidden' }}>
        <Text size="sm" c="dimmed" px={4}>
          Couldn&apos;t load the pipeline — retrying.
        </Text>
      </Box>
    )
  }

  const data = pipeline.data
  const { shown, idleCount } = visibleStages(data)
  const empty = isPipelineEmpty(data)

  return (
    <Box maw={560} mx="auto" style={{ overflowX: 'hidden' }}>
      <Text size="sm" c="dimmed" px={4} data-testid="pipeline-strip">
        {fleetStrip(data)}
      </Text>
      {empty ? (
        <Text size="sm" c="dimmed" px={4} mt={4} data-testid="pipeline-empty">
          Nothing in the pipeline.
        </Text>
      ) : (
        <Stack gap="sm" mt={4}>
          {shown.map((stage) => {
            const rows = stageRows(stage, now, data)
            const pin = stagePinLabel(stage)
            const counts = stageCountLine(stage)
            return (
              <Box key={stage.role} data-testid={`pipeline-stage-${stage.role}`}>
                <Group justify="space-between" wrap="nowrap" px={4} mb={4} gap="xs">
                  <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
                    <Text size="xs" fw={700} tt="uppercase" style={{ letterSpacing: 1 }}>
                      {STAGE_LABEL[stage.role]}
                    </Text>
                    {counts ? (
                      <Text size="xs" c="dimmed" truncate>
                        {counts}
                      </Text>
                    ) : null}
                  </Group>
                  {pin ? (
                    <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
                      {pin}
                    </Text>
                  ) : null}
                </Group>
                <Paper withBorder radius="md" px="xs">
                  {rows.map((row, index) => (
                    <Fragment key={row.key}>
                      {index > 0 && <Divider />}
                      <PipelineRow row={row} onOpen={setTask} />
                    </Fragment>
                  ))}
                </Paper>
              </Box>
            )
          })}
          {idleCount > 0 ? (
            <Text size="xs" c="dimmed" px={4} data-testid="pipeline-idle">
              {idleLine(idleCount)}
            </Text>
          ) : null}
        </Stack>
      )}
      <TaskDrawer taskId={taskId} onClose={() => setTask(null)} />
    </Box>
  )
}

function PipelineRow({
  row,
  onOpen,
}: {
  row: PipelineRowView
  onOpen: (id: string) => void
}) {
  const body = (
    <Group wrap="nowrap" gap="xs">
      <Box
        w={8}
        h={8}
        bg={row.color}
        style={{ borderRadius: '50%', flexShrink: 0 }}
        aria-hidden
      />
      <Box style={{ minWidth: 0, flex: 1 }}>
        <Text size="sm" truncate>
          {row.identifier ? (
            <>
              <Text span ff="monospace" c="dimmed">
                {row.identifier}
              </Text>{' '}
              {row.title}
            </>
          ) : (
            row.title
          )}
        </Text>
      </Box>
      <Text size="xs" c="dimmed" style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}>
        {row.right}
      </Text>
    </Group>
  )

  // A const binding: TypeScript keeps the `in` narrowing inside the onClick closure below, which it
  // drops for `row.target` (a property of a parameter) — `tsc -b` failed on that (CARD-0378).
  const target = row.target
  if ('to' in target) {
    return (
      <UnstyledButton
        component={Link}
        to={target.to}
        w="100%"
        py={6}
        aria-label={row.ariaLabel}
        data-testid={`pipeline-row-${row.key}`}
      >
        {body}
      </UnstyledButton>
    )
  }

  return (
    <UnstyledButton
      w="100%"
      py={6}
      aria-label={row.ariaLabel}
      data-testid={`pipeline-row-${row.key}`}
      onClick={() => onOpen(target.drawer)}
    >
      {body}
    </UnstyledButton>
  )
}
