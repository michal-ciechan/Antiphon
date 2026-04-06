import {
  AppShell,
  Group,
  Text,
  Avatar,
  Anchor,
  UnstyledButton,
  Tooltip,
  ThemeIcon,
} from '@mantine/core'
import { TbWifi, TbWifiOff, TbLoader } from 'react-icons/tb'
import { Outlet, useNavigate, NavLink } from 'react-router'
import { useConnectionStore } from '../stores/connectionStore'

function RebelLogo({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 100 100"
      fill="currentColor"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path d="
        M50,8
        C48,14 42,20 36,24
        L14,17 L30,36
        C20,43 12,53 10,63
        L32,57
        C27,66 25,75 29,83
        L42,71 L43,91
        L50,79
        L57,91 L58,71
        L71,83
        C75,75 73,66 68,57
        L90,63
        C88,53 80,43 70,36
        L86,17 L64,24
        C58,20 52,14 50,8Z
      " />
    </svg>
  )
}

function ConnectionIndicator() {
  const status = useConnectionStore((s) => s.status)

  const config = {
    connected:    { icon: TbWifi,    color: 'green',  label: 'Connected' },
    connecting:   { icon: TbLoader,  color: 'yellow', label: 'Connecting...' },
    reconnecting: { icon: TbLoader,  color: 'orange', label: 'Reconnecting...' },
    disconnected: { icon: TbWifiOff, color: 'red',    label: 'Disconnected — retrying' },
  }[status]

  return (
    <Tooltip label={config.label} withArrow>
      <ThemeIcon variant="subtle" color={config.color} size="sm">
        <config.icon size={16} />
      </ThemeIcon>
    </Tooltip>
  )
}

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

          {/* Right — connection status + user avatar */}
          <Group gap="sm">
            <ConnectionIndicator />
            <Tooltip label="May the Force be with you, Admin" withArrow position="bottom-end">
              <Avatar radius="xl" size="sm" color="active" style={{ cursor: 'default' }}>
                <RebelLogo size={18} />
              </Avatar>
            </Tooltip>
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
