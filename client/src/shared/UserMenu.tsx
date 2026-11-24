import { Avatar, Menu, Text, UnstyledButton } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { TbBrandGithub, TbLogout } from 'react-icons/tb'
import { getReviewAuthStatus, reviewAuthKeys, signOut } from '../api/reviewAuth'

function initials(name: string): string {
  const parts = name.trim().split(/[\s._-]+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

/**
 * The header's user slot. With sign-in on (`Review:Auth:Mode` GitHub) it shows the signed-in GitHub
 * login and a Sign out item; otherwise it renders `fallback` (the single-operator avatar).
 */
export function UserMenu({ fallback }: { fallback: ReactNode }) {
  const status = useQuery({ queryKey: reviewAuthKeys.status, queryFn: getReviewAuthStatus, staleTime: 5 * 60_000, retry: false })
  const data = status.data
  if (!data || data.mode !== 'GitHub' || !data.signedIn || !data.login) return <>{fallback}</>
  const name = data.displayName ?? data.login
  return (
    <Menu position="bottom-end" withArrow>
      <Menu.Target>
        <UnstyledButton aria-label={`Signed in as ${data.login}`} data-testid="user-menu">
          <Avatar radius="xl" size="sm" color="active">
            {initials(name)}
          </Avatar>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Signed in with GitHub</Menu.Label>
        <Menu.Item leftSection={<TbBrandGithub size={14} />} disabled style={{ cursor: 'default' }}>
          <Text size="sm" fw={600}>
            {name}
          </Text>
          {data.displayName && (
            <Text size="xs" c="dimmed">
              @{data.login}
            </Text>
          )}
        </Menu.Item>
        <Menu.Divider />
        <Menu.Item leftSection={<TbLogout size={14} />} onClick={signOut} data-testid="user-menu-sign-out">
          Sign out
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
