import type { Meta, StoryObj } from '@storybook/react'
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Divider,
  Group,
  Paper,
  Stack,
  Text,
  Textarea,
  UnstyledButton,
} from '@mantine/core'
import { TbChevronRight, TbCoin, TbLayoutGrid, TbSend } from 'react-icons/tb'

/**
 * PROPOSAL MOCK — not production code. Visual drawing for the spec
 * `docs/superpowers/specs/2026-08-17-mobile-thread-and-plan-surfacing.md` §D3 (the phone home:
 * three bands). Delete this folder and the proposal is cleanly gone; nothing imports it.
 */

function WorkLine({ line, sub }: { line: string; sub?: string }) {
  return (
    <UnstyledButton w="100%" py={8} px={4}>
      <Group justify="space-between" wrap="nowrap" gap={4}>
        <Box style={{ minWidth: 0 }}>
          <Text size="sm" truncate>
            {line}
          </Text>
          {sub && (
            <Text size="xs" c="dimmed" truncate>
              {sub}
            </Text>
          )}
        </Box>
        <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
      </Group>
    </UnstyledButton>
  )
}

function BandTitle({ children, color }: { children: React.ReactNode; color?: string }) {
  return (
    <Text size="xs" fw={700} c={color ?? 'dimmed'} tt="uppercase" mt="md" mb={4} style={{ letterSpacing: 1 }}>
      {children}
    </Text>
  )
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <Box maw={390} mx="auto">
      <Group justify="space-between" mb={4}>
        <Text fw={700}>Antiphon</Text>
        <Group gap="xs">
          <Badge variant="light" color="gray" leftSection={<TbCoin size={12} />}>
            $4.18 today
          </Badge>
          <ActionIcon variant="subtle" color="gray" aria-label="board">
            <TbLayoutGrid size={18} />
          </ActionIcon>
        </Group>
      </Group>
      {children}
    </Box>
  )
}

function InMotion() {
  return (
    <>
      <BandTitle>In motion · 3</BandTitle>
      <Paper withBorder radius="md" px="xs">
        <WorkLine line="#35 stuck-work view · slices 4+6 · opus" sub="check 13:02 · $1.12" />
        <Divider />
        <WorkLine line="#58 instruction bundles · slice 6 · sonnet" sub="check 13:25 · $0.31" />
        <Divider />
        <WorkLine line="#67 reply durability · verify sweep · fable" sub="settling · $2.75" />
      </Paper>
    </>
  )
}

function Away() {
  return (
    <>
      <BandTitle>While you were away · since 09:12</BandTitle>
      <Paper withBorder radius="md" px="xs">
        <WorkLine
          line="#67 closed — reply route now as durable as the route in"
          sub="c4df66b · 6 tests · Critical incident on any lost reply"
        />
        <Divider />
        <WorkLine line="#35 slices 1–3 shipped — GET /api/attention live" sub="nine conditions · diagnostic tab" />
        <Divider />
        <WorkLine line="New plan — mobile thread & plan surfacing" sub="docs/superpowers/specs · awaiting review" />
      </Paper>
    </>
  )
}

function NeedsYouHome() {
  return (
    <Shell>
      <BandTitle color="var(--mantine-color-red-5)">Needs you · 2</BandTitle>
      <Stack gap="xs">
        <Paper withBorder radius="md" p="sm" style={{ borderColor: 'var(--mantine-color-red-8)' }}>
          <Group gap={6} mb={4}>
            <Badge size="xs" color="red" variant="filled">
              blocked
            </Badge>
            <Text size="xs" c="dimmed">
              #56 launch leak · reconcile pass · 22m
            </Text>
          </Group>
          <Text size="sm" mb={6}>
            Your call: cap re-adoptions at 3 per uptime, or unlimited with Critical on the 4th flap?
          </Text>
          <Textarea placeholder="Answer — the delegate resumes on send" autosize minRows={1} mb={6} />
          <Group justify="flex-end" gap="xs">
            <Button size="compact-xs" leftSection={<TbSend size={12} />}>
              Answer
            </Button>
          </Group>
        </Paper>
        <Paper withBorder radius="md" p="sm" style={{ borderColor: 'var(--mantine-color-orange-8)' }}>
          <Group gap={6} mb={4}>
            <Badge size="xs" color="orange" variant="filled">
              parked
            </Badge>
            <Text size="xs" c="dimmed">
              Family chat reply · 3 delivery attempts spent
            </Text>
          </Group>
          <Text size="sm" lineClamp={2} mb={6}>
            “Guest list drafted — 42 names, grouped by side. Shall I send the long version…”
          </Text>
          <Group justify="flex-end" gap="xs">
            <Button size="compact-xs" variant="default">
              Cancel message
            </Button>
            <Button size="compact-xs" color="orange">
              Send now
            </Button>
          </Group>
        </Paper>
      </Stack>
      <InMotion />
      <Away />
    </Shell>
  )
}

function CalmHome() {
  return (
    <Shell>
      <Paper radius="md" p="md" mt="xs" bg="var(--mantine-color-dark-6)">
        <Text size="sm" c="dimmed">
          Nothing needs you.
        </Text>
        <Text size="xs" c="dimmed">
          Next check-in 13:02 — you’ll see its reading here.
        </Text>
      </Paper>
      <InMotion />
      <Away />
    </Shell>
  )
}

const meta: Meta = {
  title: 'Proposals/Mobile home',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** Band 1 present: a blocked question answerable in place, a parked channel reply with its verbs. */
export const NeedsYou: Story = {
  render: () => <NeedsYouHome />,
  globals: { viewport: { value: 'iphone12' } },
}

/** The common case. Band 1 absent entirely; the screen says when it will next have news. */
export const Calm: Story = {
  render: () => <CalmHome />,
  globals: { viewport: { value: 'iphone12' } },
}
