import {
  AppShell,
  Group,
  Text,
  Avatar,
  Anchor,
  UnstyledButton,
  Tooltip,
  ThemeIcon,
  Burger,
  Drawer,
  Stack,
  Badge,
} from '@mantine/core'
import { useDisclosure, useViewportSize } from '@mantine/hooks'
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

const NAV_ITEMS = [
  { to: '/', label: 'Workflows', end: true },
  { to: '/boards', label: 'Boards' },
  { to: '/agents', label: 'Agents' },
  { to: '/orchestrator', label: 'Orchestrator' },
  { to: '/settings', label: 'Settings' },
]

// Storybook is served at storybook.<app-host> behind Caddy on codeperf, else :17283 locally.
function storybookUrl(): string {
  const { protocol, host, hostname } = window.location
  return host.endsWith('.codeperf.net') ? `${protocol}//storybook.${host}` : `${protocol}//${hostname}:17283`
}

// Shared by the desktop header and the mobile drawer. onNavigate closes the drawer on tap.
function NavLinks({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <>
      {NAV_ITEMS.map((item) => (
        <Anchor
          key={item.to}
          component={NavLink}
          to={item.to}
          end={item.end}
          onClick={onNavigate}
          underline="never"
          c="dimmed"
          fw={500}
          style={({ isActive }: { isActive: boolean }) =>
            isActive ? { color: 'var(--mantine-color-active-4)' } : undefined
          }
        >
          {item.label}
        </Anchor>
      ))}
      {import.meta.env.DEV && (
        <Anchor
          href={storybookUrl()}
          target="_blank"
          rel="noreferrer"
          onClick={onNavigate}
          underline="never"
          c="dimmed"
          fw={500}
          title="Open Storybook (dev)"
        >
          Storybook ↗
        </Anchor>
      )}
    </>
  )
}

// Dev-only live viewport readout so you can read your phone's real CSS viewport size.
function ViewportBadge() {
  const { width, height } = useViewportSize()
  if (!import.meta.env.DEV || width === 0) return null
  return (
    <Badge
      variant="filled"
      color="dark"
      radius="sm"
      style={{
        position: 'fixed',
        bottom: 8,
        right: 8,
        zIndex: 1000,
        fontFamily: 'var(--mantine-font-family-monospace)',
        opacity: 0.65,
        pointerEvents: 'none',
      }}
    >
      {width} × {height}
    </Badge>
  )
}

export function Layout() {
  const navigate = useNavigate()
  const [drawerOpened, { toggle: toggleDrawer, close: closeDrawer }] = useDisclosure(false)

  return (
    <AppShell header={{ height: 56 }} padding="md">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
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

          {/* Nav links — center (desktop only) */}
          <Group gap="lg" visibleFrom="sm">
            <NavLinks />
          </Group>

          {/* Right — connection status + user avatar + mobile burger */}
          <Group gap="sm" wrap="nowrap">
            <ConnectionIndicator />
            <Tooltip label="May the Force be with you, Admin" withArrow position="bottom-end">
              <Avatar radius="xl" size="sm" color="active" style={{ cursor: 'default' }}>
                <RebelLogo size={18} />
              </Avatar>
            </Tooltip>
            <Burger
              opened={drawerOpened}
              onClick={toggleDrawer}
              hiddenFrom="sm"
              size="sm"
              aria-label="Toggle navigation menu"
            />
          </Group>
        </Group>
      </AppShell.Header>

      {/* Mobile navigation drawer */}
      <Drawer
        opened={drawerOpened}
        onClose={closeDrawer}
        title="Menu"
        position="right"
        size="70%"
        hiddenFrom="sm"
      >
        <Stack gap="lg">
          <NavLinks onNavigate={closeDrawer} />
        </Stack>
      </Drawer>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>

      <ViewportBadge />
    </AppShell>
  )
}
