import {
  AppShell,
  Group,
  Text,
  Avatar,
  Anchor,
  UnstyledButton,
} from '@mantine/core'
import { Outlet, useNavigate, NavLink } from 'react-router'

export function Layout() {
  const navigate = useNavigate()

  return (
    <AppShell header={{ height: 56 }} padding="md">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between">
          {/* Logo — left */}
          <UnstyledButton onClick={() => navigate('/')}>
            <Text
              size="xl"
              fw={700}
              variant="gradient"
              gradient={{ from: 'active', to: 'cyan', deg: 45 }}
            >
              Antiphon
            </Text>
          </UnstyledButton>

          {/* Nav links — center */}
          <Group gap="lg">
            <Anchor
              component={NavLink}
              to="/"
              underline="never"
              c="dimmed"
              fw={500}
              style={({ isActive }: { isActive: boolean }) =>
                isActive ? { color: 'var(--mantine-color-active-4)' } : undefined
              }
            >
              Workflows
            </Anchor>
            <Anchor
              component={NavLink}
              to="/settings"
              underline="never"
              c="dimmed"
              fw={500}
              style={({ isActive }: { isActive: boolean }) =>
                isActive ? { color: 'var(--mantine-color-active-4)' } : undefined
              }
            >
              Settings
            </Anchor>
          </Group>

          {/* User avatar — right */}
          <Avatar radius="xl" size="sm" color="active">
            U
          </Avatar>
        </Group>
      </AppShell.Header>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
