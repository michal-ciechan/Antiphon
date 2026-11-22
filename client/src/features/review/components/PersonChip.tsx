import { Avatar, Group, Text } from '@mantine/core'
import type { ReactNode } from 'react'
import { TbRobot } from 'react-icons/tb'

const isService = (login: string) => login.endsWith('[bot]') || login.includes('delegate') || login === 'release-train'

/** Avatar initials plus a name. Logins without a display name show as "@login". */
export function PersonChip({ login, displayName, size = 'sm', note }: { login: string | null; displayName?: string | null; size?: 'xs' | 'sm'; note?: ReactNode }) {
  if (!login)
    return (
      <Text size="xs" c="dimmed" fs="italic">
        Unassigned
      </Text>
    )
  const name = displayName ?? `@${login}`
  const service = isService(login)
  const initials = (displayName ?? login)
    .split(/[\s._-]+/)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')
    .slice(0, 2)
  return (
    <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
      <Avatar size={size === 'xs' ? 18 : 22} radius="xl" color={service ? 'gray' : 'active'}>
        {service ? (
          <TbRobot size={12} />
        ) : (
          <Text size="9px" fw={700}>
            {initials}
          </Text>
        )}
      </Avatar>
      <Text size={size === 'xs' ? 'xs' : 'sm'} truncate>
        {name}
      </Text>
      {note}
    </Group>
  )
}
