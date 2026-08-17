import { Box, Group, Text, UnstyledButton } from '@mantine/core'
import { TbChevronRight } from 'react-icons/tb'
import { Link } from 'react-router'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { taskWorkLine, workLineTarget } from './workLineFormat'

/**
 * One live task as a tappable compact status line (spec §D3 band 2). The wording lives in
 * `workLineFormat.ts`; this is only the rendering — two truncating lines and a chevron, linking
 * to the surface that explains the line.
 */
export function WorkLine({
  task,
  formatTime,
}: {
  task: AgentTaskSummaryDto
  formatTime?: (iso: string) => string
}) {
  const parts = taskWorkLine(task, { formatTime })
  return (
    <UnstyledButton
      component={Link}
      to={workLineTarget(task)}
      w="100%"
      py={8}
      px={4}
      aria-label={`Open ${task.title}`}
    >
      <Group justify="space-between" wrap="nowrap" gap={4}>
        <Box style={{ minWidth: 0 }}>
          <Text size="sm" truncate>
            {parts.line}
          </Text>
          <Text size="xs" c="dimmed" truncate>
            {parts.sub}
          </Text>
        </Box>
        <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
      </Group>
    </UnstyledButton>
  )
}
