import { Badge, type BadgeProps, Popover, Stack, Text, Tooltip } from '@mantine/core'
import { type ReactNode, useState } from 'react'
import { TbInfoCircle } from 'react-icons/tb'

/**
 * A badge that explains itself: hovering shows `hint` as a tooltip (desktop), and a click or a tap
 * opens `title` and `children` in a popover (phones have no hover). The tooltip is hidden while the
 * popover is open, so a tap on a phone does not show both.
 */
export function ExplainBadge({
  label,
  hint,
  title,
  children,
  testId,
  ...badge
}: {
  label: ReactNode
  hint: string
  title: string
  children: ReactNode
  testId?: string
} & Omit<BadgeProps, 'children'>) {
  const [opened, setOpened] = useState(false)
  return (
    // Below the badge, so what sits above it (a gate's reasons, which the details refer to) stays visible.
    <Popover opened={opened} onChange={setOpened} width={320} position="bottom-start" withArrow shadow="md">
      <Popover.Target>
        <Tooltip label={hint} multiline w={280} position="bottom-start" disabled={opened}>
          <Badge
            component="button"
            type="button"
            aria-label={`${title}: more about this`}
            aria-expanded={opened}
            onClick={() => setOpened((o) => !o)}
            rightSection={<TbInfoCircle size={11} />}
            style={{ cursor: 'pointer' }}
            data-testid={testId}
            {...badge}
          >
            {label}
          </Badge>
        </Tooltip>
      </Popover.Target>
      <Popover.Dropdown data-testid={testId ? `${testId}-details` : undefined}>
        <Stack gap={6}>
          <Text size="sm" fw={700}>
            {title}
          </Text>
          {children}
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}
